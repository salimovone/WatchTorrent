using System.Net;
using System.Net.Http.Headers;
using WatchTorrent.Services;

namespace WatchTorrent.EngineTests;

internal static class StreamPlaybackChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var values = new List<float>();
        var rates = new PlaybackRateTransition(value => { values.Add(value); return true; });
        rates.Set(2);
        check(values[0] > 1 && values[0] < 1.2f, "hold enters acceleration without an instantaneous 100% clock jump");
        await Task.Delay(300);
        check(values[^1] == 2 && values.Count > 2, "hold reaches exactly 2x through intermediate audio rates");
        rates.Set(1.25f);
        await Task.Delay(45);
        rates.Set(2);
        rates.Set(1.25f);
        await Task.Delay(300);
        check(values[^1] == 1.25f && rates.Target == 1.25f,
            "rapid hold/release supersedes old ramps and restores selected speed");
        rates.Set(2);
        rates.Cancel();
        int applied = values.Count;
        await Task.Delay(250);
        check(values.Count == applied, "stopping playback cancels all queued audio rate updates");

        using var torrent = new WaitingTorrent();
        using var server = new TorrentMediaServer(torrent);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var missingRequest = new HttpRequestMessage(HttpMethod.Get, server.Url);
        missingRequest.Headers.Range = new RangeHeaderValue(4, 7);
        using var missingResponse = await client.SendAsync(missingRequest, HttpCompletionOption.ResponseHeadersRead);
        await torrent.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using var cachedRequest = new HttpRequestMessage(HttpMethod.Get, server.Url);
        cachedRequest.Headers.Range = new RangeHeaderValue(0, 3);
        using var cachedResponse = await client.SendAsync(cachedRequest);
        check(cachedResponse.StatusCode == HttpStatusCode.PartialContent
            && (await cachedResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "seek back to downloaded bytes completes without waiting for missing future data");
        check(torrent.CancelledReads == 1 && torrent.Position == 4,
            "new range cancels stale torrent read before changing the stream cursor");

        using var stalled = new HttpRequestMessage(HttpMethod.Get, server.Url);
        stalled.Headers.Range = new RangeHeaderValue(4, 7);
        using var stalledResponse = await client.SendAsync(stalled, HttpCompletionOption.ResponseHeadersRead);
        var until = Environment.TickCount64 + 2000;
        while (!server.IsWaiting && Environment.TickCount64 < until) await Task.Delay(20);
        if (!server.IsWaiting) throw new Exception("Missing range did not enter read wait");
        server.InterruptRead();
        using var retry = new HttpRequestMessage(HttpMethod.Get, server.Url);
        retry.Headers.Range = new RangeHeaderValue(4, 7);
        using var retryResponse = await client.SendAsync(retry);
        check(retryResponse.StatusCode == HttpStatusCode.ServiceUnavailable,
            "interrupted HTTP recovery cannot block VLC on the abandoned range again");

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, server.Url));
        check(head.Content.Headers.ContentLength == 8 && torrent.Position == 4, "HEAD reports file length without moving playback");
        using var invalid = new HttpRequestMessage(HttpMethod.Get, server.Url);
        invalid.Headers.Range = new RangeHeaderValue(20, 25);
        using var invalidResponse = await client.SendAsync(invalid);
        check(invalidResponse.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable, "invalid seek range is rejected");

        using var suffix = new HttpRequestMessage(HttpMethod.Get, server.Url);
        suffix.Headers.Range = new RangeHeaderValue(null, 2);
        torrent.AllReady = true;
        using var suffixResponse = await client.SendAsync(suffix);
        check((await suffixResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 7, 8 }), "suffix ranges read verified file tail");

        using var wrongPath = await client.GetAsync(server.Url + "-invalid");
        check(wrongPath.StatusCode == HttpStatusCode.NotFound, "loopback media endpoint requires its private path");
    }

    private sealed class WaitingTorrent : Stream
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool AllReady { get; set; }
        public int CancelledReads { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 8;
        public override long Position { get; set; }
        public override long Seek(long offset, SeekOrigin origin) => Position = offset;
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!AllReady && Position >= 4)
            {
                Waiting.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { CancelledReads++; throw; }
            }
            int length = (int)Math.Min(count, Length - Position);
            for (int i = 0; i < length; i++) buffer[offset + i] = (byte)(Position + i + 1);
            Position += length;
            return length;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
