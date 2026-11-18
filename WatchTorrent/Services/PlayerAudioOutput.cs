using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace WatchTorrent.Services;

/// <summary>
/// Keep one PCM device alive across rate changes. VLC's native outputs can
/// insert silence or flush on clock corrections; amem leaves device pacing to
/// this bounded, continuous queue. Native callbacks never call Android JNI.
/// </summary>
internal sealed class PlayerAudioOutput : IDisposable
{
    private const int Rate = 48000;
    private const int Channels = 2;
    private readonly ConcurrentQueue<Packet> _packets = new();
    private readonly SemaphoreSlim _slots = new(4, 4);
    private readonly AutoResetEvent _wake = new(false);
    private readonly Task _worker;
    private readonly Func<IPcmDevice> _createDevice;
    private readonly LibVLC _libVlc;
    private volatile bool _closed;
    private volatile bool _paused;
    private volatile bool _writing;
    private int _epoch;
    private int _reset;
    public bool IsClosed => _closed;
    public Exception? Error { get; private set; }
    private sealed record Packet(short[] Samples, long Pts, int Epoch);

    internal interface IPcmDevice : IDisposable
    {
        void Write(short[] samples, Func<bool> current);
        void Pause();
        void Resume();
        void Reset();
    }

    public PlayerAudioOutput(VlcPlayer player, LibVLC libVlc, Func<IPcmDevice>? createDevice = null)
    {
        _libVlc = libVlc;
        _createDevice = createDevice ?? (() => new PcmDevice());
        player.SetAudioFormat("S16N", Rate, Channels);
        player.SetAudioCallbacks(Play, Pause, Resume, Flush, Drain);
        _worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void Play(IntPtr opaque, IntPtr samples, uint count, long pts)
    {
        int epoch = Volatile.Read(ref _epoch);
        while (!_closed && !_paused && epoch == Volatile.Read(ref _epoch))
        {
            if (!_slots.Wait(10)) continue;
            if (_closed || _paused || epoch != Volatile.Read(ref _epoch)) { _slots.Release(); return; }
            var copy = new short[checked((int)count * Channels)];
            Marshal.Copy(samples, copy, 0, copy.Length);
            _packets.Enqueue(new(copy, pts, epoch));
            _wake.Set();
            return;
        }
    }

    private void Pause(IntPtr opaque, long pts) { _paused = true; _wake.Set(); }
    private void Resume(IntPtr opaque, long pts) { _paused = false; _wake.Set(); }
    private void Flush(IntPtr opaque, long pts)
    {
        Interlocked.Increment(ref _epoch);
        while (_packets.TryDequeue(out _)) _slots.Release();
        Interlocked.Exchange(ref _reset, 1);
        _wake.Set();
    }

    private void Drain(IntPtr opaque)
    {
        while (!_closed && !_paused && (!_packets.IsEmpty || _writing)) Thread.Sleep(5);
        // Let the device play its final bounded buffer before VLC reports EOF.
        for (int i = 0; i < 40 && !_closed && !_paused; i++) Thread.Sleep(5);
    }

    private void Run()
    {
        try
        {
            using var device = _createDevice();
            bool starting = true;
            bool wasPaused = false;
            while (!_closed)
            {
                if (Interlocked.Exchange(ref _reset, 0) != 0)
                {
                    device.Reset();
                    starting = true;
                }
                if (_paused != wasPaused)
                {
                    if (_paused) device.Pause();
                    else if (!starting) device.Resume();
                    wasPaused = _paused;
                }
                if (_paused || !_packets.TryDequeue(out var packet)) { _wake.WaitOne(10); continue; }
                _slots.Release();
                if (packet.Epoch != Volatile.Read(ref _epoch)) continue;
                _writing = true;
                bool Current() => !_closed && !_paused && packet.Epoch == Volatile.Read(ref _epoch);
                try
                {
                    if (starting)
                    {
                        // The first PTS aligns sound with video. Subsequent PCM is
                        // contiguous; rate changes do not restart or empty the device.
                        while (Current() && packet.Pts - _libVlc.Clock > 3000) _wake.WaitOne(2);
                        if (!Current()) continue;
                        device.Resume();
                        starting = false;
                    }
                    device.Write(packet.Samples, Current);
                }
                finally { _writing = false; }
            }
        }
        catch (Exception ex)
        {
            Error = ex;
            _closed = true;
            System.Diagnostics.Debug.WriteLine($"Player audio output: {ex}");
        }
    }

    public void Cancel() { _closed = true; _wake.Set(); }
    public void Dispose()
    {
        Cancel();
        _worker.GetAwaiter().GetResult();
        // Callbacks are detached by disposing/replacing the stopped VLC player.
    }

#if ANDROID
    private sealed class PcmDevice : IPcmDevice
    {
        private readonly Android.Media.AudioTrack _track;
        public PcmDevice()
        {
            using var attributes = new Android.Media.AudioAttributes.Builder()
                .SetUsage(Android.Media.AudioUsageKind.Media)!
                .SetContentType(Android.Media.AudioContentType.Movie)!.Build()!;
            using var format = new Android.Media.AudioFormat.Builder()
                .SetSampleRate(Rate)!.SetChannelMask(Android.Media.ChannelOut.Stereo)!
                .SetEncoding(Android.Media.Encoding.Pcm16bit)!.Build()!;
            int size = Math.Max(Rate * Channels * sizeof(short) / 10,
                Android.Media.AudioTrack.GetMinBufferSize(Rate, Android.Media.ChannelOut.Stereo, Android.Media.Encoding.Pcm16bit));
            _track = new Android.Media.AudioTrack.Builder().SetAudioAttributes(attributes)!
                .SetAudioFormat(format)!.SetBufferSizeInBytes(size)!
                .SetTransferMode(Android.Media.AudioTrackMode.Stream)!.Build()!;
            if (_track.State != Android.Media.AudioTrackState.Initialized) throw new IOException("AudioTrack could not initialize.");
        }
        public void Write(short[] samples, Func<bool> current)
        {
            int offset = 0;
            while (offset < samples.Length && current())
            {
                int written = _track.Write(samples, offset, samples.Length - offset, Android.Media.WriteMode.NonBlocking);
                if (written < 0) throw new IOException($"AudioTrack write failed: {written}");
                offset += written;
                if (written == 0) Thread.Sleep(2);
            }
        }
        public void Pause() => _track.Pause();
        public void Resume() => _track.Play();
        public void Reset() { _track.Pause(); _track.Flush(); }
        public void Dispose() { _track.Stop(); _track.Release(); _track.Dispose(); }
    }
#elif WINDOWS && !PLAYER_AUDIO_TESTS
    private sealed class PcmDevice : IPcmDevice
    {
        private readonly NAudio.Wave.BufferedWaveProvider _buffer = new(new NAudio.Wave.WaveFormat(Rate, 16, Channels));
        private readonly NAudio.Wave.WaveOutEvent _output = new() { DesiredLatency = 80 };
        public PcmDevice() => _output.Init(_buffer);
        public void Write(short[] samples, Func<bool> current)
        {
            while (_buffer.BufferedDuration.TotalMilliseconds > 80 && current()) Thread.Sleep(2);
            if (!current()) return;
            var bytes = new byte[samples.Length * sizeof(short)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            _buffer.AddSamples(bytes, 0, bytes.Length);
        }
        public void Pause() => _output.Pause();
        public void Resume() => _output.Play();
        public void Reset() { _output.Stop(); _buffer.ClearBuffer(); }
        public void Dispose() { _output.Stop(); _output.Dispose(); }
    }
#elif IOS
    private sealed class PcmDevice : IPcmDevice
    {
        private const int SamplesPerBuffer = Rate * Channels / 50;
        private readonly AudioToolbox.OutputAudioQueue _queue;
        private readonly ConcurrentQueue<IntPtr> _available = new();
        private readonly List<IntPtr> _buffers = [];
        private bool _primed;
        public PcmDevice()
        {
            var session = AVFoundation.AVAudioSession.SharedInstance();
            var error = session.Category.ToString() == AVFoundation.AVAudioSessionCategory.PlayAndRecord.ToString()
                ? null : session.SetCategory(AVFoundation.AVAudioSessionCategory.Playback);
            error ??= session.SetActive(true);
            if (error != null) throw new IOException(error.LocalizedDescription);
            _queue = new(AudioToolbox.AudioStreamBasicDescription.CreateLinearPCM(Rate, Channels, 16, false));
            _queue.BufferCompleted += (_, e) => _available.Enqueue(e.IntPtrBuffer);
            for (int i = 0; i < 5; i++)
            {
                Check(_queue.AllocateBuffer(SamplesPerBuffer * sizeof(short), out IntPtr buffer));
                _buffers.Add(buffer);
                _available.Enqueue(buffer);
            }
        }
        public void Write(short[] samples, Func<bool> current)
        {
            int offset = 0;
            while (offset < samples.Length && current())
            {
                if (!_available.TryDequeue(out var buffer)) { Thread.Sleep(2); continue; }
                int count = Math.Min(SamplesPerBuffer, samples.Length - offset);
                var data = Marshal.PtrToStructure<AudioToolbox.AudioQueueBuffer>(buffer);
                Marshal.Copy(samples, offset, data.AudioData, count);
                Check(_queue.EnqueueBuffer(buffer, count * sizeof(short), null!));
                _primed = true;
                if (!_queue.IsRunning) Check(_queue.Start());
                offset += count;
            }
        }
        private static void Check(AudioToolbox.AudioQueueStatus status)
        {
            if (status != AudioToolbox.AudioQueueStatus.Ok) throw new IOException($"AudioQueue: {status}");
        }
        public void Pause() => Check(_queue.Pause());
        public void Resume() { if (_primed && !_queue.IsRunning) Check(_queue.Start()); }
        public void Reset()
        {
            Check(_queue.Stop(true));
            _primed = false;
            while (_available.TryDequeue(out _)) { }
            foreach (var buffer in _buffers) _available.Enqueue(buffer);
        }
        public void Dispose() { _queue.Stop(true); _queue.Dispose(); }
    }
#else
    private sealed class PcmDevice : IPcmDevice
    {
        public void Write(short[] samples, Func<bool> current) => throw new PlatformNotSupportedException();
        public void Pause() { }
        public void Resume() { }
        public void Reset() { }
        public void Dispose() { }
    }
#endif
}
