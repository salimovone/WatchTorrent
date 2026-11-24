using System.Runtime.InteropServices;

namespace VoiceConnection;

public enum CallState { WaitInit, WaitInitAck, Established, Failed, Reconnecting }

/// <summary>Owns one native VoiceConnection voice session. Signaling must use an authenticated, encrypted channel.</summary>
public sealed partial class VoiceSession : IDisposable, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenSource life = new();
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr handle;
    private bool started;
    private Task cleanup = Task.CompletedTask;
    public event Action<byte[]>? SignalingEmitted;
    public event Action<CallState>? StateChanged;
    public event Action<Exception>? Faulted;
    public bool Microphone { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;

    public VoiceSession(byte[] key, bool outgoing, bool relayOnly = false)
    {
        if (key.Length != 256) throw new ArgumentException("VoiceConnection call key must contain 256 bytes.", nameof(key));
#if !ANDROID
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("VoiceConnection supports Android and Windows.");
#endif
        if (Native.AbiVersion() != 2) throw new InvalidOperationException("VoiceConnection native ABI mismatch.");
        var relay = VoiceRelay.Default();
        // NTgCalls' native TURN port factory expects a numeric IPv4 endpoint.
        if (relayOnly && relay == null) throw new InvalidOperationException("A TURN server must be configured for a relay-only connection.");
        string relayAddress = relay == null ? "" : System.Net.IPAddress.TryParse(relay.Host, out var address) ? address.ToString()
            : System.Net.Dns.GetHostAddresses(relay.Host).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString();
        handle = Native.Create(key, key.Length, outgoing ? 1 : 0, relayAddress, relay?.Port ?? 0, relay?.Username ?? "", relay?.Password ?? "", relayOnly ? 1 : 0);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("VoiceConnection voice session could not be created.");
    }

    // Subscribe before starting: the native engine can emit its first ICE candidates immediately.
    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
            if (started) throw new InvalidOperationException("Session already started.");
            started = true;
            Completion = Task.Run(PumpAsync);
        }
    }

    public Task WaitAsync(CancellationToken token) => connected.Task.WaitAsync(TimeSpan.FromSeconds(45), token);

    public void ReceiveSignaling(byte[] data)
    {
        if (data.Length is 0 or > 65536) throw new InvalidDataException("Invalid VoiceConnection signaling length.");
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
            if (Native.Receive(handle, data, data.Length) != 0) throw new IOException("VoiceConnection signaling was rejected.");
        }
    }

    public void SetMicrophone(bool enabled)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
            if (Native.Mute(handle, enabled ? 0 : 1) != 0) throw new IOException("VoiceConnection microphone could not be changed.");
            Microphone = enabled;
        }
    }

    public (long CaptureSeconds, long PlaybackSeconds) MediaTime()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
            return (Native.Time(handle, 0), Native.Time(handle, 1));
        }
    }

    private async Task PumpAsync()
    {
        var token = life.Token;
        var buffer = new byte[65536];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int kind, length;
                lock (sync)
                {
                    if (handle == IntPtr.Zero) return;
                    length = Native.Poll(handle, out kind, buffer, buffer.Length);
                }
                if (length < 0) throw new IOException("VoiceConnection voice event queue failed.");
                if (kind == 1) SignalingEmitted?.Invoke(buffer.AsSpan(0, length).ToArray());
                else if (kind == 2)
                {
                    if (length != 1 || buffer[0] > (byte)CallState.Reconnecting) throw new IOException("Invalid VoiceConnection call state.");
                    var state = (CallState)buffer[0];
                    if (state == CallState.Established) connected.TrySetResult();
                    if (state == CallState.Failed) throw new IOException("VoiceConnection voice connection failed.");
                    StateChanged?.Invoke(state);
                }
                else await Task.Delay(10, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            connected.TrySetException(ex);
            Faulted?.Invoke(ex);
        }
        finally { connected.TrySetCanceled(); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (handle == IntPtr.Zero) return;
            life.Cancel();
            var retiring = handle;
            handle = IntPtr.Zero;
            Microphone = false;
            connected.TrySetCanceled();
            cleanup = Task.Run(() => Native.Destroy(retiring));
        }
        // Native audio shutdown never blocks MAUI's UI thread.
        _ = Task.WhenAll(Completion, cleanup).ContinueWith(_ => life.Dispose(), TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync() { Dispose(); await cleanup.ConfigureAwait(false); }

    private static partial class Native
    {
#if !ANDROID
        private const string Library = "voiceconnection_native";
        [LibraryImport(Library, EntryPoint = "vc_abi_version")]
        internal static partial int AbiVersion();
        [LibraryImport(Library, EntryPoint = "vc_create", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial IntPtr Create(byte[] key, int length, int outgoing, string relayHost, int relayPort, string relayUser, string relayPassword, int relayOnly);
        [LibraryImport(Library, EntryPoint = "vc_receive")]
        internal static partial int Receive(IntPtr session, byte[] data, int length);
        [LibraryImport(Library, EntryPoint = "vc_mute")]
        internal static partial int Mute(IntPtr session, int muted);
        [LibraryImport(Library, EntryPoint = "vc_poll")]
        internal static partial int Poll(IntPtr session, out int kind, [Out] byte[] data, int capacity);
        [LibraryImport(Library, EntryPoint = "vc_destroy")]
        internal static partial void Destroy(IntPtr session);
        [LibraryImport(Library, EntryPoint = "vc_time")]
        internal static partial long Time(IntPtr session, int mode);
#endif
    }
}
