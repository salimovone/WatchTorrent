using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WatchTorrent.Services;

internal static class VoiceAnswerExchange
{
    private const int Port = 47863;
    private const int Limit = 65536;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static string Topic(string id) => "https://ntfy.sh/wt-voice-" + id;

    private static byte[] Encrypt(byte[] key, string id, string json)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, true))
            gzip.Write(Encoding.UTF8.GetBytes(json));
        var plain = compressed.ToArray();
        var packet = new byte[28 + plain.Length];
        RandomNumberGenerator.Fill(packet.AsSpan(0, 12));
        using var cipher = new AesGcm(key, 16);
        cipher.Encrypt(packet.AsSpan(0, 12), plain, packet.AsSpan(28), packet.AsSpan(12, 16), Encoding.ASCII.GetBytes(id));
        return packet;
    }

    private static string? ReadAnswer(byte[] key, string id, byte[] packet)
    {
        if (packet.Length is < 29 or > Limit) return null;
        try
        {
            var plain = new byte[packet.Length - 28];
            using var cipher = new AesGcm(key, 16);
            cipher.Decrypt(packet.AsSpan(0, 12), packet.AsSpan(28), packet.AsSpan(12, 16), plain, Encoding.ASCII.GetBytes(id));
            using var gzip = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress);
            var bytes = new byte[Limit + 1];
            var size = gzip.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (size > Limit) return null;
            using var doc = JsonDocument.Parse(bytes.AsMemory(0, size));
            var root = doc.RootElement;
            if (root.GetProperty("callId").GetString() != id || root.GetProperty("type").GetString() != "answer") return null;
            return JsonSerializer.Serialize(root);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    public static async Task<string> WaitAsync(byte[] key, string id, CancellationToken token)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(token);
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lan = ListenLan();
        var internet = ListenInternet();
        try { return await result.Task.WaitAsync(token); }
        finally
        {
            race.Cancel();
            await Task.WhenAll(lan, internet);
        }

        async Task ListenLan()
        {
            try
            {
                using var socket = new UdpClient(Port);
                while (true)
                {
                    var received = await socket.ReceiveAsync(race.Token);
                    var answer = ReadAnswer(key, id, received.Buffer);
                    if (answer == null) continue;
                    var ack = HMACSHA256.HashData(key, received.Buffer);
                    await socket.SendAsync(ack, received.RemoteEndPoint, race.Token);
                    result.TrySetResult(answer);
                    return;
                }
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException) { }
        }

        async Task ListenInternet()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(5000, race.Token);
                    try
                    {
                        var body = await Http.GetStringAsync(Topic(id) + "/json?poll=1&since=latest", race.Token);
                        if (string.IsNullOrWhiteSpace(body) || body.Length > Limit) continue;
                        using var message = JsonDocument.Parse(body.Trim());
                        var encoded = message.RootElement.GetProperty("message").GetString();
                        if (message.RootElement.TryGetProperty("attachment", out var attachment))
                        {
                            if (!Uri.TryCreate(attachment.GetProperty("url").GetString(), UriKind.Absolute, out var url)
                                || url.Scheme != "https" || url.Host != "ntfy.sh" || !url.AbsolutePath.StartsWith("/file/")
                                || attachment.GetProperty("size").GetInt64() > Limit) continue;
                            encoded = await Http.GetStringAsync(url, race.Token);
                        }
                        var answer = ReadAnswer(key, id, Convert.FromBase64String(encoded ?? ""));
                        if (answer != null) { result.TrySetResult(answer); return; }
                    }
                    catch (Exception ex) when (!race.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException) { }
                }
            }
            catch (OperationCanceledException) { }
        }
    }

    public static async Task SendAsync(byte[] key, string id, string answer, CancellationToken token)
    {
        var packet = Encrypt(key, id, answer);
        using var local = CancellationTokenSource.CreateLinkedTokenSource(token);
        local.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var socket = new UdpClient(0) { EnableBroadcast = true };
            await socket.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, Port), local.Token);
            var ack = await socket.ReceiveAsync(local.Token);
            if (CryptographicOperations.FixedTimeEquals(ack.Buffer, HMACSHA256.HashData(key, packet))) return;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { }
        using var body = new StringContent(Convert.ToBase64String(packet));
        using var response = await Http.PostAsync(Topic(id), body, token);
        response.EnsureSuccessStatusCode();
    }
}
