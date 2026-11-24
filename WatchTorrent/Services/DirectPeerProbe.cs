#if DEBUG
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace WatchTorrent.Services;

internal static class DirectPeerProbe
{
    internal static async Task RunAsync(bool host, int port, Action<string> report, CancellationToken token)
    {
        using var socket = new TcpClient();
        TcpClient remote;
        if (host)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            report("Waiting for app peer signaling on USB port " + port);
            try { remote = await listener.AcceptTcpClientAsync(token); }
            finally { listener.Stop(); }
        }
        else { await socket.ConnectAsync(IPAddress.Loopback, port, token); remote = socket; }
        using (remote)
        using (var network = remote.GetStream())
        using (var reader = new StreamReader(network, leaveOpen: true))
        using (var writer = new StreamWriter(network, leaveOpen: true) { AutoFlush = true })
        using (var peer = new DirectPeer(true))
        {
            peer.Ended += reason => report("Peer ended: " + reason);
            if (host)
            {
                await writer.WriteLineAsync((await peer.DescribeAsync()).Encode().AsMemory(), token);
                peer.Apply(SignalFile.Parse(await reader.ReadLineAsync(token) ?? throw new IOException("No app answer."), "answer"));
            }
            else
            {
                var offer = SignalFile.Parse(await reader.ReadLineAsync(token) ?? throw new IOException("No app offer."), "offer");
                await writer.WriteLineAsync((await peer.DescribeAsync(offer)).Encode().AsMemory(), token);
            }
            await peer.WaitAsync();
            report("App data channel + encrypted native voice handshake established.");
            await peer.SetMicrophoneAsync(true);
            for (int i = 0; i < 6; i++)
            {
                await Task.Delay(5000, token);
                if (!peer.Connected) throw new IOException("App connection was lost.");
                report(await peer.PingAsync());
                var time = peer.VoiceMediaTime();
                report(JsonSerializer.Serialize(new { captureSeconds = time.CaptureSeconds, playbackSeconds = time.PlaybackSeconds }));
                if (i is 1 or 3)
                {
                    await peer.SetMicrophoneAsync(false);
                    await Task.Delay(200, token);
                    await peer.SetMicrophoneAsync(true);
                }
            }
            var final = peer.VoiceMediaTime();
            if (final.CaptureSeconds < 15 || final.PlaybackSeconds < 15) throw new IOException("App audio frame counters stopped.");
            await peer.SetMicrophoneAsync(false);
            report("PASS full app peer, DTLS signaling, native bidirectional audio, data-channel ping, mute/unmute.");
            await Task.Delay(1000, token);
        }
    }
}
#endif
