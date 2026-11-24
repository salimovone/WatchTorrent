using L = WatchTorrent.Core.Localization.AppText;
using System.Security.Cryptography;
using System.Text.Json;
using VoiceConnection;

namespace WatchTorrent.Services;

// All methods except native pump callbacks run on the UI thread, like DirectPeer's control messages.
internal sealed class NativeVoiceAudio : IDisposable
{
    public const string Engine = "voiceconnection-1";
    private readonly Action<string> send;
    private readonly Action<Action> dispatch;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private VoiceSession? session;
    private bool outgoing, started, disposed;
    public event Action? Changed;
    public event Action<string>? Failed;
    public bool Recording => session?.Microphone == true;
    public CallState State { get; private set; } = CallState.WaitInit;
    public bool Connected => !disposed && State == CallState.Established;

    public NativeVoiceAudio(Action<string> send, Action<Action> dispatch)
    {
        this.send = send;
        this.dispatch = dispatch;
    }

    public void Start(bool isOutgoing)
    {
        if (started || disposed) return;
        outgoing = isOutgoing;
        started = true;
        if (!outgoing) return;
        var key = RandomNumberGenerator.GetBytes(256);
        try
        {
            Create(key);
            // The key travels only over the established DTLS data channel, never in the shared invite URL.
            send(JsonSerializer.Serialize(new { type = "voice-hello", engine = Engine, key = Convert.ToBase64String(key) }));
            session!.Start();
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public void Receive(JsonElement root)
    {
        if (disposed || !started) return;
        var type = root.GetProperty("type").GetString();
        if (type == "voice-hello")
        {
            if (outgoing || session != null || !root.TryGetProperty("engine", out var engine) || engine.GetString() != Engine)
                throw new InvalidDataException(L.Get("VoiceConnection ovoz protokoli mos kelmadi."));
            if (!root.TryGetProperty("key", out var value) || value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException(L.Get("VoiceConnection ovoz kaliti yo‘q."));
            var key = new byte[256];
            try
            {
                if (!value.TryGetBytesFromBase64(out var decoded))
                    throw new InvalidDataException(L.Get("VoiceConnection ovoz kaliti noto‘g‘ri."));
                try
                {
                    if (decoded.Length != key.Length) throw new InvalidDataException(L.Get("VoiceConnection ovoz kaliti noto‘g‘ri."));
                    decoded.CopyTo(key, 0);
                    Create(key);
                }
                finally { CryptographicOperations.ZeroMemory(decoded); }
                session!.Start();
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        else if (type == "voice-signal")
        {
            if (session == null || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String ||
                !data.TryGetBytesFromBase64(out var bytes) || bytes.Length is 0 or > 45000)
                throw new InvalidDataException(L.Get("VoiceConnection signaling noto‘g‘ri."));
            session.ReceiveSignaling(bytes);
        }
    }

    private void Create(byte[] key)
    {
        session = new VoiceSession(key, outgoing);
        session.SignalingEmitted += data => dispatch(() =>
        {
            if (disposed) return;
            try
            {
                if (data.Length > 45000) throw new IOException(L.Get("VoiceConnection signaling paketi juda katta."));
                send(JsonSerializer.Serialize(new { type = "voice-signal", data = Convert.ToBase64String(data) }));
            }
            catch (Exception ex) { Fail(ex); }
        });
        session.StateChanged += state => dispatch(() =>
        {
            if (disposed) return;
            State = state;
            if (state == CallState.Established) ready.TrySetResult();
            Changed?.Invoke();
        });
        session.Faulted += ex => dispatch(() => Fail(ex));
    }

    private void Fail(Exception ex)
    {
        if (disposed) return;
        State = CallState.Failed;
        ready.TrySetException(ex);
        Failed?.Invoke(L.Get("VoiceConnection ovoz ulanishi ishlamadi. ") + ex.Message);
    }

    public Task WaitAsync(CancellationToken token) => ready.Task.WaitAsync(TimeSpan.FromSeconds(45), token);
    public (long CaptureSeconds, long PlaybackSeconds) MediaTime() => session?.MediaTime() ?? (0, 0);
    public Task SetMicrophoneAsync(bool enabled)
    {
        var current = session;
        return current == null ? Task.CompletedTask : Task.Run(() => current.SetMicrophone(enabled));
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ready.TrySetCanceled();
        session?.Dispose();
        session = null;
    }
}
