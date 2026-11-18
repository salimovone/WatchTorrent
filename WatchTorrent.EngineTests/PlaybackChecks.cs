using WatchTorrent.Services;
using WatchTorrent.ViewModels;

namespace WatchTorrent.EngineTests
{
    internal static class PlaybackChecks
    {
        public static async Task RunAsync(Action<bool, string> check)
        {
            await GestureChecks.RunAsync(check);
            var id = Guid.NewGuid();
            var call = new VoiceCall();
            var player = new PlayerViewModel();
            var host = new WatchSession(call, id, true);
            host.Attach(player);
            player.Session = host;
            host.Command("rate", 2000);
            check(call.Sent.Count == 1 && call.Sent[0].Action == "rateApply",
                "hold speed broadcasts a rate change without seek/play/pause");
            host.Command("rate", 1000);
            await Task.Delay(250);
            check(player.Rates.SequenceEqual(new[] { 1f }) && player.ApplyCount == 0 && player.PauseCount == 0,
                "release supersedes queued 2x without flushing playback");
            host.Command("rate", 1000);
            check(call.Sent.Count == 2, "duplicate rate commands are ignored");
            host.Command("rate", 5000);
            check(call.Sent.Count == 2, "invalid playback rate is rejected");
            host.Close(false);

            call = new VoiceCall();
            player = new PlayerViewModel();
            var guest = new WatchSession(call, id, false);
            guest.Attach(player);
            player.Session = guest;
            guest.Tick(true);
            call.Deliver(new(id, "apply", Clock: Environment.TickCount64 - 1, Playing: true));
            int applied = player.ApplyCount;
            int paused = player.PauseCount;
            call.Deliver(new(id, "rateApply", Clock: Environment.TickCount64 - 1, Speed: 2));
            call.Deliver(new(id, "position", Position: 10000, Clock: Environment.TickCount64));
            check(player.Rates.Last() == 2 && player.ApplyCount == applied && player.PauseCount == paused
                && player.SeekCount == 0, "guest speed changes and settling snapshots do not seek or pause");

            // An already queued play/seek must not overwrite a newer hold transition.
            call.Deliver(new(id, "apply", Clock: Environment.TickCount64 + 100, Speed: 1, Playing: true));
            call.Deliver(new(id, "rateApply", Clock: Environment.TickCount64 - 1, Speed: 2));
            await Task.Delay(200);
            check(player.Rates.Last() == 2, "older queued playback command cannot restore a stale speed");

            guest.Command("rate", 1250);
            check(guest.PlaybackRate == 1.25f, "guest remembers selected speed before host reply");
            guest.Command("rate", 2000);
            long holdRequest = call.Sent.Last().Clock;
            guest.Command("rate", 1250);
            long releaseRequest = call.Sent.Last().Clock;
            call.Deliver(new(id, "rateApply", Position: holdRequest, Clock: Environment.TickCount64 - 1, Speed: 2));
            check(guest.PlaybackRate == 1.25f, "late hold reply cannot replace the guest's released speed");
            call.Deliver(new(id, "rateApply", Position: releaseRequest, Clock: Environment.TickCount64 - 1, Speed: 1.25f));
            check(guest.PlaybackRate == 1.25f, "latest release acknowledgement preserves selected speed");
            call.Deliver(new(id, "rateApply", Clock: Environment.TickCount64 + 100, Speed: 2));
            int rateCount = player.Rates.Count;
            guest.Close(false);
            await Task.Delay(200);
            check(player.Rates.Count == rateCount, "closing a player cancels pending speed changes");
        }
    }
}

// Exercise the real session protocol without loading MAUI or a native VLC decoder.
namespace WatchTorrent.Services
{
    internal sealed class VoiceCall
    {
        public event Action<WatchMessage>? ControlReceived;
        public event Action? Changed;
        public bool Connected { get; private set; } = true;
        public List<WatchMessage> Sent { get; } = [];
        public void Send(WatchMessage message) => Sent.Add(message);
        public void Deliver(WatchMessage message) => ControlReceived?.Invoke(message);
        public void Disconnect() { Connected = false; Changed?.Invoke(); }
    }
}

namespace WatchTorrent.ViewModels
{
    internal sealed class PlayerViewModel
    {
        public sealed class Playback { public long Time { get; set; } }
        public Playback Player { get; } = new();
        public WatchSession? Session { get; set; }
        public List<object> Playlist { get; } = [new(), new()];
        public List<float> Rates { get; } = [];
        public int ApplyCount { get; private set; }
        public int PauseCount { get; private set; }
        public int SeekCount { get; private set; }
        public bool CanHold { get; set; } = true;
        public bool Holding { get; private set; }
        public int HoldCount { get; private set; }
        public bool BeginHoldSpeed()
        {
            if (!CanHold) return false;
            HoldCount++;
            return Holding = true;
        }
        public void EndHoldSpeed() => Holding = false;
        public void SetSharedPause(bool pause) => PauseCount++;
        public void SetSharedTime(long time) { SeekCount++; Player.Time = time; }
        public void SetSharedRate(float rate) => Rates.Add(rate);
        public void ApplyShared(long position, bool play) => ApplyCount++;
        public Task SwitchSharedFileAsync(int index) => Task.CompletedTask;
    }
}
