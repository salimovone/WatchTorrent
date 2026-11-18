using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace WatchTorrent.Services;

internal sealed record SignalFile(string Type, string Sdp, string? Format = null, string? CallId = null, string? ReplyKey = null)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    public string Encode() => JsonSerializer.Serialize(this, Json);

    public static SignalFile Parse(string text, string type)
    {
        if (text.Length > 65536) throw new InvalidDataException("Fayl juda katta.");
        SignalFile? signal;
        try { signal = JsonSerializer.Deserialize<SignalFile>(text, Json); }
        catch (JsonException) { throw new InvalidDataException("JSON buzilgan. To‘liq matnni kiriting."); }
        if (signal?.Type != type) throw new InvalidDataException($"Bu bosqichda {type} kerak.");
        if (string.IsNullOrWhiteSpace(signal.Sdp) || !signal.Sdp.StartsWith("v=0"))
            throw new InvalidDataException("Ulanish ma’lumoti noto‘g‘ri.");
        return signal;
    }
}

// The peer owns ICE, DTLS, SCTP and the optional audio endpoint for one session.
internal sealed class DirectPeer : IDisposable
{
    private readonly RTCPeerConnection pc;
    private readonly VoiceAudio? audio;
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
    public bool Connected => !closed && channel?.readyState == RTCDataChannelState.open && pc.connectionState == RTCPeerConnectionState.connected;
    public bool Microphone => audio?.Recording == true;
    public CancellationToken Token { get; }

    public DirectPeer(bool voice)
    {
        Token = life.Token;
        pc = new RTCPeerConnection(new RTCConfiguration
        {
            iceServers = [new RTCIceServer { urls = "stun:stun.l.google.com:19302" }],
            X_GatherTimeoutMs = voice ? 6000 : 30000
        });
        if (voice)
        {
            pc.addTrack(new MediaStreamTrack(AudioCommonlyUsedFormats.OpusWebRTC, MediaStreamStatusEnum.SendRecv));
            audio = new VoiceAudio(pc);
        }
        pc.ondatachannel += Attach;
        pc.onicegatheringstatechange += state => Note("ICE yig‘ish: " + state);
        pc.oniceconnectionstatechange += state => Note("ICE: " + state);
        pc.onconnectionstatechange += state => Ui(() =>
        {
            Note("Ulanish: " + state);
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
                Ended?.Invoke("Ulanish uzildi. Bir Wi-Fi tarmog‘ida qayta urinib ko‘ring.");
            if (state == RTCPeerConnectionState.disconnected) _ = CheckRecoveryAsync();
        });
    }

    private void Ui(Action act) => MainThread.BeginInvokeOnMainThread(() => { if (!closed) act(); });

    private async Task CheckRecoveryAsync()
    {
        try { await Task.Delay(45000, Token); }
        catch (OperationCanceledException) { return; }
        if (pc.connectionState == RTCPeerConnectionState.disconnected) Ended?.Invoke("Ulanish tiklanmadi.");
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
        return $"Signaling: {pc.signalingState}\nICE yig‘ish: {pc.iceGatheringState}\nICE: {pc.iceConnectionState}\nUlanish: {pc.connectionState}\nDataChannel: {channel?.readyState.ToString() ?? "—"}\nManzillar: {counts}\nTanlangan juftlik: {route}\n\n" + string.Join("\n", log);
    }

    private void Attach(RTCDataChannel data)
    {
        channel = data;
        void Open() { opened.TrySetResult(); Note("Kanal ochildi"); }
        data.onopen += Open;
        data.onclose += () => Ui(() => Ended?.Invoke("Suhbatdosh aloqani tugatdi."));
        data.onerror += _ => Ui(() => Ended?.Invoke("Ma’lumot kanalida xato."));
        data.onmessage += (_, _, bytes) =>
        {
            if (bytes.Length > MovieInvite.MaxBytes) return;
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return;
                if (t.GetString() == "hangup") { Ui(() => Ended?.Invoke("Suhbatdosh aloqani tugatdi.")); return; }
                if (audio != null && t.GetString() == "watch" && bytes.Length <= 2048)
                {
                    var control = root.Deserialize<WatchMessage>(SignalFile.Json);
                    if (control is { Session: var session } && session != Guid.Empty)
                        Ui(() => ControlReceived?.Invoke(control));
                    return;
                }
                if (audio != null && t.GetString() == "movie")
                {
                    var movie = root.Deserialize<MovieInvite>(SignalFile.Json);
                    if (movie?.Valid == true) Ui(() => MovieReceived?.Invoke(movie));
                    return;
                }
                if (!root.TryGetProperty("id", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out int id)) return;
                if (t.GetString() == "ping") Ui(() => { data.send(JsonSerializer.Serialize(new { type = "pong", id })); Note($"Ping #{id} olindi, pong yuborildi."); });
                else if (t.GetString() == "pong") Ui(() => { if (pings.TryGetValue(id, out var pong)) pong.TrySetResult(); });
            }
            catch (JsonException) { }
        };
        if (data.IsOpened) Open();
    }

    public async Task<SignalFile> DescribeAsync(SignalFile? remote = null)
    {
        if (remote == null) Attach(await pc.createDataChannel(audio == null ? "direct-test" : "voice-control"));
        else Apply(remote);
        Token.ThrowIfCancellationRequested();
        var description = await Task.Run(() => remote == null
            ? pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true })
            : pc.createAnswer(new RTCAnswerOptions { X_WaitForIceGatheringToComplete = true }), Token);
        Token.ThrowIfCancellationRequested();
        if (!description.sdp.Contains("a=candidate:") || (audio == null && pc.iceGatheringState != RTCIceGatheringState.complete))
            throw new TimeoutException("ICE manzillarini yig‘ish vaqti tugadi.");
        await pc.setLocalDescription(description);
        Note("Mahalliy ulanish ma’lumoti tayyor.");
        return new(description.type.ToString(), description.sdp);
    }

    public void Apply(SignalFile signal)
    {
        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit
        { type = signal.Type == "offer" ? RTCSdpType.offer : RTCSdpType.answer, sdp = signal.Sdp });
        if (result != SetDescriptionResultEnum.OK) throw new InvalidDataException("SDP qabul qilinmadi: " + result);
        Note("Masofaviy ulanish ma’lumoti qabul qilindi.");
    }

    public Task WaitAsync() => opened.Task.WaitAsync(TimeSpan.FromSeconds(45), Token);
    public void Invite(MovieInvite movie)
    {
        if (!Connected) return;
        var message = JsonSerializer.Serialize(new { type = "movie", movie.Magnet, movie.Title, movie.FileIndex, movie.Session, movie.Details }, SignalFile.Json);
        if (Encoding.UTF8.GetByteCount(message) > MovieInvite.MaxBytes)
            throw new InvalidOperationException("Magnet havolasi juda uzun.");
        channel!.send(message);
    }
    public void SetMicrophone(bool enabled) => audio?.SetMicrophone(enabled);
    public void Send(WatchMessage message)
    {
        if (Connected) channel!.send(JsonSerializer.Serialize(message, SignalFile.Json));
    }
    public async Task<string> PingAsync()
    {
        if (!Connected) throw new InvalidOperationException("Avval ulaning.");
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
        finally { audio?.Dispose(); pc.Close("ended"); pc.Dispose(); life.Dispose(); }
    }
}
