using LibVLCSharp.Shared;
using WatchTorrent.Services;

namespace WatchTorrent.EngineTests;

internal static class PlaybackNativeChecks
{
    // Optional real-decoder regression: --vlc-path <directory containing libvlc>.
    // The source deliberately never supplies the middle of a small generated WAV.
    public static async Task RunAsync(string nativePath, Action<bool, string> check)
    {
        LibVLCSharp.Shared.Core.Initialize(nativePath);
        using var source = new PartialWave { BlockSeekRangesOnly = true };
        using var server = new TorrentMediaServer(source);
        using var vlc = new LibVLC("--aout=dummy", "--vout=dummy");
        using var player = new MediaPlayer(vlc);
        using var media = new Media(vlc, server.Url, FromType.FromLocation);
        media.AddOption(":network-caching=300");
        try
        {
            player.Play(media);
            await UntilAsync(() => player.IsPlaying && player.Time >= 700, "VLC did not start");
            player.Time = 30_000;
            await UntilAsync(() => server.IsWaiting && source.MissingAt > 2_000_000,
                "VLC did not wait on the missing seek target");

            var stoppingInput = server.StopAsync();
            player.Stop();
            await stoppingInput.WaitAsync(TimeSpan.FromSeconds(2));
            using var cachedServer = new TorrentMediaServer(source);
            using var cachedMedia = new Media(vlc, cachedServer.Url, FromType.FromLocation);
            cachedMedia.AddOption(":network-caching=300");
            cachedMedia.AddOption(":start-time=1");
            try
            {
                player.Play(cachedMedia);
                await UntilAsync(() => player.IsPlaying && player.Time is >= 1400 and < 4500,
                    "VLC remained blocked after reopening at cached bytes");
                check(!source.AllReady, "real VLC resumes cached playback while the abandoned target is still missing");
            }
            finally { cachedServer.Cancel(); player.Stop(); }
        }
        finally { server.Cancel(); player.Stop(); }

        using var forwardSource = new PartialWave { BlockSeekRangesOnly = true };
        using var forwardServer = new TorrentMediaServer(forwardSource);
        using var forwardPlayer = new MediaPlayer(vlc);
        using var forwardMedia = new Media(vlc, forwardServer.Url, FromType.FromLocation);
        forwardMedia.AddOption(":network-caching=300");
        try
        {
            forwardPlayer.Play(forwardMedia);
            await UntilAsync(() => forwardPlayer.IsPlaying && forwardPlayer.Time >= 700,
                "VLC did not start the forward-seek fixture");
            var forward = forwardPlayer.Time + 10_000;
            forwardPlayer.Time = forward;
            await UntilAsync(() => forwardServer.IsWaiting && forwardSource.MissingAt > 900_000,
                "VLC did not buffer the forward seek");
            check(forwardPlayer.State is not (VLCState.Ended or VLCState.Error),
                "a missing +10s target buffers without ending playback");
            forwardSource.MakeAllReady();
            await UntilAsync(() => forwardPlayer.IsPlaying && forwardPlayer.Time >= forward + 400,
                "VLC did not continue at the forward target when its bytes arrived");
            check(true, "real VLC advances from the +10s target after missing bytes arrive");
        }
        finally { forwardServer.Cancel(); forwardPlayer.Stop(); }

        using var fullSource = new PartialWave { AllReady = true };
        using var audioServer = new TorrentMediaServer(fullSource);
        using var audioPlayer = new MediaPlayer(vlc);
        var probe = new AudioProbe();
        using var continuousAudio = new PlayerAudioOutput(audioPlayer, vlc, () => probe);
        using var audioMedia = new Media(vlc, audioServer.Url, FromType.FromLocation);
        audioMedia.AddOption(":network-caching=300");
        var transition = new PlaybackRateTransition(rate => audioPlayer.SetRate(rate) == 0);
        try
        {
            audioPlayer.Play(audioMedia);
            await UntilAsync(() => Volatile.Read(ref probe.Packets) > 8, "PCM output did not start");
            int resetsBefore = probe.Resets;
            probe.Measure = true;
            for (int i = 0; i < 3; i++)
            {
                transition.Set(2);
                await Task.Delay(500);
                transition.Set(1);
                await Task.Delay(500);
            }
            check(continuousAudio.Error == null && probe.Packets > 40 && probe.LongestSilence < 96,
                "real VLC continuous PCM output preserves tone through repeated hold/release");
            check(probe.Resets == resetsBefore, "rate transitions do not flush the PCM device");
            int resetsAtSeek = probe.Resets;
            audioPlayer.Time = 2000;
            await UntilAsync(() => probe.Resets > resetsAtSeek && audioPlayer.Time is >= 2400 and < 5000,
                "PCM output did not recover after seek flush");
            check(continuousAudio.Error == null, "PCM device resets for seek and resumes at the new clock");
            audioPlayer.SetPause(true);
            await UntilAsync(() => !audioPlayer.IsPlaying, "Audio did not pause");
            await Task.Delay(100);
            int pausedPackets = probe.Packets;
            audioPlayer.SetPause(false);
            await UntilAsync(() => probe.Packets > pausedPackets + 5, "PCM device did not resume after pause");
            check(continuousAudio.Error == null, "continuous PCM output resumes after an explicit pause");
        }
        finally { transition.Cancel(); continuousAudio.Cancel(); audioServer.Cancel(); audioPlayer.Stop(); }
    }

    private static async Task UntilAsync(Func<bool> condition, string error)
    {
        var until = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < until) await Task.Delay(50);
        if (!condition()) throw new Exception(error);
    }

    private sealed class PartialWave : Stream
    {
        private readonly byte[] _bytes;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _missingAt;
        private long _rangeStart;
        public long MissingAt => Interlocked.Read(ref _missingAt);
        public bool BlockSeekRangesOnly { get; init; }
        public void MakeAllReady() => _ready.TrySetResult();
        public bool AllReady { get => _ready.Task.IsCompleted; init { if (value) MakeAllReady(); } }
        public PartialWave()
        {
            const int size = 48000 * 2 * 60;
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write("RIFF"u8); writer.Write(size + 36); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(size);
            for (int i = 0; i < size / 2; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 48000) * 8000));
            _bytes = output.ToArray();
        }
        public override long Length => _bytes.Length;
        public override long Position { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Seek(long offset, SeekOrigin origin)
        {
            _rangeStart = origin switch
            { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, _ => Length + offset };
            return Position = _rangeStart;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            const int cachedEnd = 44 + 96000 * 5;
            var blockRange = !BlockSeekRangesOnly || _rangeStart >= cachedEnd;
            if (!AllReady && blockRange && Position >= cachedEnd && Position < 44 + 96000 * 55)
            {
                Interlocked.Exchange(ref _missingAt, Position);
                await _ready.Task.WaitAsync(token);
            }
            if (BlockSeekRangesOnly && !blockRange && !AllReady)
                await Task.Delay(160, token);
            int length = (int)Math.Min(count, Length - Position);
            if (!AllReady && blockRange && Position < cachedEnd) length = (int)Math.Min(length, cachedEnd - Position);
            Array.Copy(_bytes, Position, buffer, offset, length);
            Position += length;
            return length;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class AudioProbe : PlayerAudioOutput.IPcmDevice
    {
        public int Packets;
        public int Resets;
        public volatile bool Measure;
        public int LongestSilence;
        private int _silence;
        public void Write(short[] samples, Func<bool> current)
        {
            if (!current()) return;
            Interlocked.Increment(ref Packets);
            if (Measure)
                foreach (short sample in samples)
                {
                    _silence = sample == 0 ? _silence + 1 : 0;
                    LongestSilence = Math.Max(LongestSilence, _silence);
                }
            Thread.Sleep(Math.Max(1, samples.Length * 1000 / (48000 * 2)));
        }
        public void Pause() { }
        public void Resume() { }
        public void Reset() { Resets++; }
        public void Dispose() { }
    }
}
