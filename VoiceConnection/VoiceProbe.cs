using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace VoiceConnection;

/// <summary>Device-to-device diagnostic. Audio uses real native ICE; the local TCP channel carries signaling only.</summary>
public static class VoiceProbe
{
    public static async Task RunAsync(bool host, int port, Action<string> report, CancellationToken token, bool relayOnly = false)
    {
        using var socket = new TcpClient();
        TcpClient peer;
        if (host)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            report("Waiting for Android signaling on USB port " + port);
            try { peer = await listener.AcceptTcpClientAsync(token); }
            finally { listener.Stop(); }
        }
        else
        {
            await socket.ConnectAsync(IPAddress.Loopback, port, token);
            peer = socket;
        }
        using (peer)
        using (var stream = peer.GetStream())
        using (var reader = new StreamReader(stream, leaveOpen: true))
        using (var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true })
        using (var life = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            byte[] key;
            if (host)
            {
                key = RandomNumberGenerator.GetBytes(256);
                await writer.WriteLineAsync(Convert.ToBase64String(key).AsMemory(), token);
            }
            else key = Convert.FromBase64String(await reader.ReadLineAsync(token) ?? throw new IOException("No session key."));
            await using var session = new VoiceSession(key, host, relayOnly);
            CryptographicOperations.ZeroMemory(key);
            var outgoing = Channel.CreateBounded<string>(256);
            session.SignalingEmitted += data =>
            {
                if (!outgoing.Writer.TryWrite("signal:" + Convert.ToBase64String(data))) throw new IOException("Probe signaling queue overflow.");
            };
            session.StateChanged += state => report("Native: " + state);
            session.Faulted += error => { report("Native error: " + error.Message); life.Cancel(); };
            var receive = ReceiveAsync();
            var send = SendAsync();
            try
            {
                session.Start();
                await session.WaitAsync(life.Token);
                session.SetMicrophone(true);
                report("Microphone started; testing bidirectional native audio for 30 seconds.");
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(5000, life.Token);
                    var time = session.MediaTime();
                    report(JsonSerializer.Serialize(new { captureSeconds = time.CaptureSeconds, playbackSeconds = time.PlaybackSeconds }));
                    if (i is 1 or 3) { session.SetMicrophone(false); await Task.Delay(200, life.Token); session.SetMicrophone(true); }
                }
                var final = session.MediaTime();
                if (final.CaptureSeconds < 15 || final.PlaybackSeconds < 15)
                    throw new IOException($"Audio stalled: capture={final.CaptureSeconds}, playback={final.PlaybackSeconds}");
                session.SetMicrophone(false);
                report("PASS VoiceConnection native audio capture/playback and repeated mute/unmute.");
                await outgoing.Writer.WriteAsync("result:PASS", life.Token);
                await Task.Delay(700, token);
            }
            finally
            {
                life.Cancel();
                outgoing.Writer.TryComplete();
                try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { }
            }

            async Task ReceiveAsync()
            {
                while (await reader.ReadLineAsync(life.Token) is { } line)
                {
                    if (line.StartsWith("signal:")) session.ReceiveSignaling(Convert.FromBase64String(line[7..]));
                    else if (line.StartsWith("result:")) report("Remote " + line);
                }
            }
            async Task SendAsync()
            {
                await foreach (var line in outgoing.Reader.ReadAllAsync(life.Token))
                    await writer.WriteLineAsync(line.AsMemory(), life.Token);
            }
        }
    }
}
