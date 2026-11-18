using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WatchTorrent.Services;

/// <summary>
/// Loopback HTTP ranges give VLC an interruptible input. A blocking MediaInput
/// callback prevents VLC from processing seek until the missing old piece arrives.
/// Each new range cancels the previous read before moving the torrent cursor.
/// </summary>
public sealed class TorrentMediaServer : IDisposable
{
    private readonly Stream _stream;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _reader = new(1, 1);
    private readonly object _sync = new();
    private readonly string _path = "/" + Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _range;
    private long _readStartedAt;
    private long? _interruptedPosition;
    private long _interruptedUntil;
    private int _disposed;

    public string Url { get; }
    public bool IsWaiting => Interlocked.Read(ref _readStartedAt) is > 0 and var started
        && Environment.TickCount64 - started > 400;

    public TorrentMediaServer(Stream stream)
    {
        _stream = stream;
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{_path}";
        _ = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_closed.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_closed.Token).ConfigureAwait(false);
                _ = ServeAsync(client);
            }
        }
        catch (Exception) when (_closed.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token))
        {
            lifetime.CancelAfter(TimeSpan.FromSeconds(10));
            CancellationTokenSource? range = null;
            bool ownsReader = false;
            try
            {
                client.NoDelay = true;
                var network = client.GetStream();
                using var reader = new StreamReader(network, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = (await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false))?.Split(' ');
                if (request is not { Length: 3 } || request[1] != _path
                    || request[0] is not ("GET" or "HEAD"))
                {
                    await HeadersAsync(network, "404 Not Found", 0, null, lifetime.Token).ConfigureAwait(false);
                    return;
                }
                string? rangeHeader = null;
                int headerBytes = 0;
                while (await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { Length: > 0 } header)
                {
                    if ((headerBytes += header.Length) > 8192) return;
                    if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) rangeHeader = header[6..].Trim();
                }
                if (!TryRange(rangeHeader, _stream.Length, out long start, out long end))
                {
                    await HeadersAsync(network, "416 Range Not Satisfiable", 0, $"bytes */{_stream.Length}", lifetime.Token).ConfigureAwait(false);
                    return;
                }
                bool interruptedRetry;
                lock (_sync)
                {
                    interruptedRetry = request[0] == "GET" && _interruptedPosition == start
                        && Environment.TickCount64 <= _interruptedUntil;
                    if (request[0] == "GET") _interruptedPosition = null;
                }
                if (interruptedRetry)
                {
                    // VLC retries a truncated HTTP body once at the old cursor.
                    // Do not let that recovery read block the queued seek again.
                    await HeadersAsync(network, "503 Service Unavailable", 0, null, lifetime.Token).ConfigureAwait(false);
                    return;
                }
                long length = end - start + 1;
                await HeadersAsync(network, rangeHeader == null ? "200 OK" : "206 Partial Content", length,
                    rangeHeader == null ? null : $"bytes {start}-{end}/{_stream.Length}", lifetime.Token).ConfigureAwait(false);
                if (request[0] == "HEAD") return;
                lifetime.CancelAfter(Timeout.InfiniteTimeSpan);
                range = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                lock (_sync)
                {
                    _range?.Cancel();
                    _range = range;
                }
                using var abort = range.Token.Register(client.Dispose);
                await _reader.WaitAsync(range.Token).ConfigureAwait(false);
                ownsReader = true;
                _stream.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[64 * 1024];
                while (length > 0)
                {
                    range.Token.ThrowIfCancellationRequested();
                    Interlocked.Exchange(ref _readStartedAt, Environment.TickCount64);
                    int count;
                    try
                    {
                        count = await _stream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, length), range.Token).ConfigureAwait(false);
                    }
                    finally { Interlocked.Exchange(ref _readStartedAt, 0); }
                    if (count == 0) break;
                    await network.WriteAsync(buffer.AsMemory(0, count), range.Token).ConfigureAwait(false);
                    length -= count;
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
            finally
            {
                if (ownsReader) _reader.Release();
                lock (_sync)
                {
                    if (ReferenceEquals(_range, range)) _range = null;
                    range?.Dispose();
                }
            }
        }
    }

    private static Task HeadersAsync(NetworkStream network, string status, long length, string? range, CancellationToken token)
    {
        var headers = $"HTTP/1.1 {status}\r\nContent-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\nContent-Length: {length}\r\nConnection: close\r\n";
        if (range != null) headers += $"Content-Range: {range}\r\n";
        return network.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"), token).AsTask();
    }

    private static bool TryRange(string? header, long size, out long start, out long end)
    {
        start = 0;
        end = size - 1;
        if (header == null) return true;
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = header[6..].Split('-');
        if (parts.Length != 2) return false;
        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long suffix) || suffix <= 0) return false;
            start = Math.Max(0, size - suffix);
        }
        else
        {
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)) return false;
            if (parts[1].Length > 0 && !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)) return false;
            end = Math.Min(end, size - 1);
        }
        return start >= 0 && start < size && end >= start;
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (!_closed.IsCancellationRequested) _closed.Cancel();
            _range?.Cancel();
        }
        _listener.Stop();
    }

    // Queue the VLC seek first, then close a stalled old range so the demux
    // thread can consume that command instead of waiting for remote peers.
    public void InterruptRead()
    {
        lock (_sync)
        {
            _interruptedPosition = Interlocked.Read(ref _readStartedAt) != 0 ? _stream.Position : null;
            _interruptedUntil = Environment.TickCount64 + 2000;
            _range?.Cancel();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Cancel();
        // Active reads finish asynchronously before the caller releases the stream.
        // Keep their cancellation sources alive until their finally blocks run.
    }
}
