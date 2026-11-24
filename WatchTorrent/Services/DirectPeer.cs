using L = WatchTorrent.Core.Localization.AppText;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using VoiceConnection;

namespace WatchTorrent.Services;

internal sealed record SignalFile(string Type, string Sdp, string? Format = null, string? CallId = null, string? ReplyKey = null, string? AudioEngine = null)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    public string Encode() => JsonSerializer.Serialize(this, Json);

    public static SignalFile Parse(string text, string type)
    {
        if (text.Length > 65536) throw new InvalidDataException(L.Get("Fayl juda katta."));
        SignalFile? signal;
        try { signal = JsonSerializer.Deserialize<SignalFile>(text, Json); }
        catch (JsonException) { throw new InvalidDataException(L.Get("JSON buzilgan. To‘liq matnni kiriting.")); }
        if (signal?.Type != type) throw new InvalidDataException(L.F("ExpectedStage", type));
        if (string.IsNullOrWhiteSpace(signal.Sdp) || !signal.Sdp.StartsWith("v=0"))
            throw new InvalidDataException(L.Get("Ulanish ma’lumoti noto‘g‘ri."));
        return signal;
    }
}

// The peer owns ICE, DTLS, SCTP and the optional audio endpoint for one session.
internal sealed class DirectPeer : IDisposable
{
    private readonly RTCPeerConnection pc;
    private readonly bool voice;
    private readonly NativeVoiceAudio? voiceAudio;
    private bool outgoing;
    private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource life = new();
    private readonly Queue<string> log = new();
    private readonly Dictionary<int, TaskCompletionSource> pings = new();
    private RTCDataChannel? channel;
    private int nextPing;
    private bool closed;
    public event Action? Changed;
    public event Action<MovieInvite>? MovieReceived;
    public event Action<WatchMessage>? ControlReceived;
    public event Action<string>? Ended;
    private bool ControlConnected => !closed && channel?.readyState == RTCDataChannelState.open && pc.connectionState == RTCPeerConnectionState.connected;
    public bool Connected => ControlConnected && (voiceAudio == null || voiceAudio.Connected);
    public bool Microphone => voiceAudio?.Recording == true;
    public CancellationToken Token { get; }
    internal (long CaptureSeconds, long PlaybackSeconds) VoiceMediaTime() => voiceAudio?.MediaTime() ?? (0, 0);

    public DirectPeer(bool voice)
    {
        this.voice = voice;
        Token = life.Token;
        var relay = VoiceRelay.Default();
        var iceServers = new List<RTCIceServer> { new() { urls = "stun:stun.l.google.com:19302" } };
        if (relay != null) iceServers.Add(new RTCIceServer { urls = $"turn:{relay.Host}:{relay.Port}", username = relay.Username, credential = relay.Password });
        pc = new RTCPeerConnection(new RTCConfiguration
        {
            iceServers = iceServers,
            X_GatherTimeoutMs = 30000
        });
        if (voice)
        {
            voiceAudio = new NativeVoiceAudio(message => channel!.send(message), Ui);
            voiceAudio.Changed += () => Changed?.Invoke();
            voiceAudio.Failed += reason => Ended?.Invoke(reason);
        }
        pc.ondatachannel += Attach;
        pc.onicegatheringstatechange += state => Note(L.Get("ICE yig‘ish: ") + state);
        pc.oniceconnectionstatechange += state => Note(L.Get("ICE: ") + state);
        pc.onconnectionstatechange += state => Ui(() =>
        {
            Note(L.Get("Ulanish: ") + state);
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
                Ended?.Invoke(L.Get("Ulanish uzildi. Internetni tekshirib, qayta urinib ko‘ring."));
            if (state == RTCPeerConnectionState.disconnected) _ = CheckRecoveryAsync();
        });
    }

    private void Ui(Action act) => MainThread.BeginInvokeOnMainThread(() => { if (!closed) act(); });

    private async Task CheckRecoveryAsync()
    {
        try { await Task.Delay(45000, Token); }
        catch (OperationCanceledException) { return; }
        if (pc.connectionState == RTCPeerConnectionState.disconnected) Ended?.Invoke(L.Get("Ulanish tiklanmadi."));
    }

    private void Note(string text) => Ui(() =>
    {
        if (log.Count >= 24) log.Dequeue();
        log.Enqueue($"{DateTime.Now:T}  {text}");
        Changed?.Invoke();
    });

    public string Diagnostics()
    {
        var ice = pc.GetRtpChannel();
        var counts = string.Join(", ", ice.Candidates.GroupBy(c => c.type).Select(g => $"{g.Key} × {g.Count()}"));
        var pair = ice.NominatedEntry;
        var route = pair == null ? "aniqlanmadi" : $"{pair.LocalCandidate.type}/{pair.LocalCandidate.protocol} - {pair.RemoteCandidate.type}/{pair.RemoteCandidate.protocol}";
        return L.F("S161", pc.signalingState, pc.iceGatheringState, pc.iceConnectionState, pc.connectionState, channel?.readyState.ToString() ?? "—", counts, route)
            + L.F("S225", voiceAudio?.State.ToString() ?? "—")
            + "\n" + string.Join("\n", log);
    }

    private void Attach(RTCDataChannel data)
    {
        channel = data;
        void Open() => Ui(() =>
        {
            try
            {
                voiceAudio?.Start(outgoing);
                opened.TrySetResult();
                Note(L.Get("Kanal ochildi"));
            }
            catch (Exception ex) { opened.TrySetException(ex); Ended?.Invoke(L.Get("Ovoz dvigateli ochilmadi. ") + ex.Message); }
        });
        data.onopen += Open;
        data.onclose += () => Ui(() => Ended?.Invoke(L.Get("Suhbatdosh aloqani tugatdi.")));
        data.onerror += _ => Ui(() => Ended?.Invoke(L.Get("Ma’lumot kanalida xato.")));
        data.onmessage += (_, _, bytes) =>
        {
            if (bytes.Length > MovieInvite.MaxBytes) return;
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return;
                if (t.GetString() == "hangup") { Ui(() => Ended?.Invoke(L.Get("Suhbatdosh aloqani tugatdi."))); return; }
                if (voiceAudio != null && t.GetString() is "voice-hello" or "voice-signal")
                {
                    var message = root.Clone();
                    Ui(() =>
                    {
                        try { voiceAudio.Receive(message); }
                        catch (Exception ex) { Ended?.Invoke(L.Get("VoiceConnection signaling ishlamadi. ") + ex.Message); }
                    });
                    return;
                }
                if (voice && t.GetString() == "watch" && bytes.Length <= 2048)
                {
                    var control = root.Deserialize<WatchMessage>(SignalFile.Json);
                    if (control is { Session: var session } && session != Guid.Empty)
                        Ui(() => ControlReceived?.Invoke(control));
                    return;
                }
                if (voice && t.GetString() == "movie")
                {
                    var movie = root.Deserialize<MovieInvite>(SignalFile.Json);
                    if (movie?.Valid == true) Ui(() => MovieReceived?.Invoke(movie));
                    return;
                }
                if (!root.TryGetProperty("id", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out int id)) return;
                if (t.GetString() == "ping") Ui(() => { data.send(JsonSerializer.Serialize(new { type = "pong", id })); Note(L.F("S136", id)); });
                else if (t.GetString() == "pong") Ui(() => { if (pings.TryGetValue(id, out var pong)) pong.TrySetResult(); });
            }
            catch (JsonException) { }
        };
        if (data.IsOpened) Open();
    }

    public async Task<SignalFile> DescribeAsync(SignalFile? remote = null)
    {
        outgoing = remote == null;
        if (remote == null) Attach(await pc.createDataChannel(voice ? "voice-control" : "direct-test"));
        else Apply(remote);
        Token.ThrowIfCancellationRequested();
        var description = await Task.Run(() => remote == null
            ? pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true })
            : pc.createAnswer(new RTCAnswerOptions { X_WaitForIceGatheringToComplete = true }), Token);
        Token.ThrowIfCancellationRequested();
        if (!description.sdp.Contains("a=candidate:") || (!voice && pc.iceGatheringState != RTCIceGatheringState.complete))
            throw new TimeoutException(L.Get("ICE manzillarini yig‘ish vaqti tugadi."));
        await pc.setLocalDescription(description);
        Note(L.Get("Mahalliy ulanish ma’lumoti tayyor."));
        return new(description.type.ToString(), description.sdp, AudioEngine: voice ? VoiceEngine : null);
    }

    public static string VoiceEngine => NativeVoiceAudio.Engine;

    public void Apply(SignalFile signal)
    {
        if (voice && signal.AudioEngine != VoiceEngine)
            throw new InvalidDataException(L.Get("Ovoz dvigatellari mos kelmadi. Ikkala qurilmaga bir xil build o‘rnating."));
        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit
        { type = signal.Type == "offer" ? RTCSdpType.offer : RTCSdpType.answer, sdp = signal.Sdp });
        if (result != SetDescriptionResultEnum.OK) throw new InvalidDataException(L.Get("SDP qabul qilinmadi: ") + result);
        Note(L.Get("Masofaviy ulanish ma’lumoti qabul qilindi."));
    }

    public async Task WaitAsync()
    {
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(45), Token);
        if (voiceAudio != null) await voiceAudio.WaitAsync(Token);
    }
    public void Invite(MovieInvite movie)
    {
        if (!Connected) return;
        var message = JsonSerializer.Serialize(new { type = "movie", movie.Magnet, movie.Title, movie.FileIndex, movie.Session, movie.Details }, SignalFile.Json);
        if (Encoding.UTF8.GetByteCount(message) > MovieInvite.MaxBytes)
            throw new InvalidOperationException(L.Get("Magnet havolasi juda uzun."));
        channel!.send(message);
    }
    public Task SetMicrophoneAsync(bool enabled) => voiceAudio?.SetMicrophoneAsync(enabled) ?? Task.CompletedTask;
    public void Send(WatchMessage message)
    {
        if (Connected) channel!.send(JsonSerializer.Serialize(message, SignalFile.Json));
    }
    public async Task<string> PingAsync()
    {
        if (!Connected) throw new InvalidOperationException(L.Get("Avval ulaning."));
        var id = ++nextPing;
        var pong = pings[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = Stopwatch.GetTimestamp();
        try
        {
            channel!.send(JsonSerializer.Serialize(new { type = "ping", id }));
            await pong.Task.WaitAsync(TimeSpan.FromSeconds(8), Token);
            return $"Pong #{id} · RTT {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms";
        }
        catch (TimeoutException) { return $"Pong timeout · #{id}"; }
        finally { pings.Remove(id); }
    }

    public void Dispose()
    {
        if (closed) return;
        closed = true;
        life.Cancel();
        try { if (channel?.IsOpened == true) channel.send("{\"type\":\"hangup\"}"); }
        finally
        {
            voiceAudio?.Dispose();
            pc.Close("ended"); pc.Dispose(); life.Dispose();
        }
    }
}
