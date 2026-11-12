using AudioEncoder = SIPSorcery.Media.AudioEncoder;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
#if ANDROID
using Android.Media;
using Android.Media.Audiofx;
#elif WINDOWS
using NAudio.Wave;
#endif

namespace WatchTorrent.Services;

internal sealed class VoiceAudio : IDisposable
{
    private const int Rate = 48000, Frame = 960;
    private readonly RTCPeerConnection pc;
    private readonly AudioEncoder codec = new(includeOpus: true);
    private readonly object sync = new();
    private bool disposed;
#if ANDROID
    private AudioRecord? input;
    private AudioTrack? output;
    private AcousticEchoCanceler? echo;
#elif WINDOWS
    private WaveInEvent? input;
    private WaveOutEvent? output;
    private BufferedWaveProvider? buffer;
#endif
    public bool Recording => input != null;

    public VoiceAudio(RTCPeerConnection pc)
    {
        this.pc = pc;
        pc.OnAudioFrameReceived += Play;
    }

    private void Play(EncodedAudioFrame frame)
    {
        lock (sync)
        {
            if (disposed) return;
            var samples = codec.DecodeAudio(frame.EncodedAudio, frame.AudioFormat);
            if (samples.Length == 0) return;
#if ANDROID
            if (output == null)
            {
                var size = Math.Max(Frame * 8, AudioTrack.GetMinBufferSize(Rate, ChannelOut.Mono, Android.Media.Encoding.Pcm16bit));
                output = new AudioTrack(Android.Media.Stream.Music, Rate, ChannelOut.Mono, Android.Media.Encoding.Pcm16bit, size, AudioTrackMode.Stream);
                output.Play();
            }
            output.Write(samples, 0, samples.Length);
#elif WINDOWS
            if (output == null)
            {
                buffer = new BufferedWaveProvider(new WaveFormat(Rate, 16, 1)) { DiscardOnBufferOverflow = true };
                output = new WaveOutEvent { DesiredLatency = 100 };
                output.Init(buffer);
                output.Play();
            }
            var bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            buffer!.AddSamples(bytes, 0, bytes.Length);
#endif
        }
    }

    public void SetMicrophone(bool enabled)
    {
        if (enabled == Recording || disposed) return;
        if (!enabled) { StopInput(); return; }
        try
        {
#if ANDROID
            int size = Math.Max(Frame * 8, AudioRecord.GetMinBufferSize(Rate, ChannelIn.Mono, Android.Media.Encoding.Pcm16bit));
            var recorder = input = new AudioRecord(AudioSource.VoiceCommunication, Rate, ChannelIn.Mono, Android.Media.Encoding.Pcm16bit, size);
            if (recorder.State != Android.Media.State.Initialized) throw new IOException("Mikrofon ochilmadi.");
            if (AcousticEchoCanceler.IsAvailable) { echo = AcousticEchoCanceler.Create(recorder.AudioSessionId); echo?.SetEnabled(true); }
            recorder.StartRecording();
            _ = Task.Run(() => Capture(recorder));
#elif WINDOWS
            input = new WaveInEvent { WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = 20 };
            input.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded != Frame * 2) return;
                var samples = new short[Frame];
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
                Send(samples, _);
            };
            input.StartRecording();
#endif
        }
        catch { StopInput(); throw; }
    }

#if ANDROID
    private void Capture(AudioRecord recorder)
    {
        var samples = new short[Frame];
        int offset = 0;
        try
        {
            while (ReferenceEquals(input, recorder))
            {
                int count = recorder.Read(samples, offset, Frame - offset);
                if (count <= 0) break;
                offset += count;
                if (offset == Frame) { Send(samples, recorder); offset = 0; }
            }
        }
        finally { recorder.Release(); recorder.Dispose(); }
    }
#endif

    private void Send(short[] samples, object? source)
    {
        lock (sync)
        {
            if (disposed || !ReferenceEquals(input, source)) return;
            pc.SendAudio(Frame, codec.EncodeAudio(samples, AudioCommonlyUsedFormats.OpusWebRTC));
        }
    }

    private void StopInput()
    {
        var recorder = input;
        input = null;
        if (recorder == null) return;
#if ANDROID
        if (recorder.RecordingState == RecordState.Recording) recorder.Stop();
        else { recorder.Release(); recorder.Dispose(); }
        echo?.Release(); echo?.Dispose(); echo = null;
#elif WINDOWS
        recorder.StopRecording();
        recorder.Dispose();
#endif
    }

    public void Dispose()
    {
        pc.OnAudioFrameReceived -= Play;
        StopInput();
        lock (sync)
        {
            disposed = true;
            output?.Stop();
#if ANDROID
            output?.Release();
#endif
            output?.Dispose();
            codec.Dispose();
        }
    }
}
