using AudioEncoder = SIPSorcery.Media.AudioEncoder;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
#if ANDROID
using Android.Media;
using Android.Media.Audiofx;
#elif WINDOWS
using NAudio.Wave;
#elif IOS
using AudioToolbox;
using AVFoundation;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
#elif IOS
    private InputAudioQueue? input;
    private OutputAudioQueue? output;
    private readonly ConcurrentQueue<IntPtr> available = new();
    private static AudioStreamBasicDescription Format => AudioStreamBasicDescription.CreateLinearPCM(Rate, 1, 16, false);
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
#elif IOS
            if (output == null)
            {
                if (input == null) ConfigureSession(false);
                output = new OutputAudioQueue(Format);
                output.BufferCompleted += (_, e) => available.Enqueue(e.IntPtrBuffer);
                for (int i = 0; i < 8; i++)
                {
                    Check(output.AllocateBuffer(Rate * 120 / 1000 * sizeof(short), out IntPtr slot));
                    available.Enqueue(slot);
                }
            }
            if (samples.Length <= Rate * 120 / 1000 && available.TryDequeue(out var buffer))
            {
                var data = Marshal.PtrToStructure<AudioQueueBuffer>(buffer);
                Marshal.Copy(samples, 0, data.AudioData, samples.Length);
                Check(output.EnqueueBuffer(buffer, samples.Length * sizeof(short), null!));
                if (!output.IsRunning) Check(output.Start());
            }
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
#elif IOS
            ConfigureSession(true);
            var recorder = input = new InputAudioQueue(Format);
            var frame = new short[Frame];
            int offset = 0;
            recorder.InputCompleted += (_, e) =>
            {
                lock (sync)
                {
                    if (disposed || input != recorder) return;
                    var buffer = Marshal.PtrToStructure<AudioQueueBuffer>(e.IntPtrBuffer);
                    int count = (int)buffer.AudioDataByteSize / sizeof(short);
                    for (int index = 0; index < count;)
                    {
                        int take = Math.Min(count - index, Frame - offset);
                        Marshal.Copy(buffer.AudioData + index * sizeof(short), frame, offset, take);
                        offset += take;
                        index += take;
                        if (offset == Frame) { Send(frame, recorder); offset = 0; }
                    }
                    recorder.EnqueueBuffer(e.IntPtrBuffer, Frame * sizeof(short), null!);
                }
            };
            for (int i = 0; i < 3; i++)
            {
                Check(recorder.AllocateBuffer(Frame * sizeof(short), out IntPtr slot));
                Check(recorder.EnqueueBuffer(slot, Frame * sizeof(short), null!));
            }
            Check(recorder.Start());
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

#if IOS
    private static void Check(AudioQueueStatus status)
    {
        if (status != AudioQueueStatus.Ok) throw new IOException($"AudioQueue: {status}");
    }
    private static void ConfigureSession(bool record)
    {
        var session = AVAudioSession.SharedInstance();
        var error = record
            ? session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker)
            : session.SetCategory(AVAudioSessionCategory.Playback);
        error ??= session.SetActive(true);
        if (error != null) throw new IOException(error.LocalizedDescription);
    }
#endif

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
#elif IOS
        recorder.Stop(true);
        recorder.Dispose();
        AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Playback);
#endif
    }

    public void Dispose()
    {
        pc.OnAudioFrameReceived -= Play;
        StopInput();
        lock (sync)
        {
            disposed = true;
#if IOS
            output?.Stop(true);
#else
            output?.Stop();
#endif
#if ANDROID
            output?.Release();
#endif
            output?.Dispose();
            codec.Dispose();
        }
    }
}
