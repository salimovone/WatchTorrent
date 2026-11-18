using WatchTorrent.Views;

namespace WatchTorrent.EngineTests
{
    internal static class GestureChecks
    {
        public static async Task RunAsync(Action<bool, string> check)
        {
            var page = new PlayerPage();
            page.Press();
            page.Release();
            await Task.Delay(600);
            check(page.Taps == 1 && page.Holds == 0, "short tap never enters delayed 2x");

            page.Press();
            await WaitForHold(page);
            page.Release();
            check(!page.Holding && !page.BadgeVisible && page.Taps == 1 && page.LastTapAt == 0,
                "releasing a hold restores speed and cannot produce a tap");

            page.Press();
            await WaitForHold(page);
            page.Cancel();
            check(!page.Holding && !page.BadgeVisible && page.Taps == 1,
                "pointer cancellation restores speed without a phantom tap");

            int holds = page.Holds;
            page.Press();
            page.Swipe();
            await Task.Delay(600);
            page.Release();
            check(page.Holds == holds && page.Taps == 1, "level swipe cancels pending hold and tap");

            page.Press();
            page.Close();
            await Task.Delay(600);
            check(page.Holds == holds && !page.Holding, "page shutdown cancels delayed hold recognition");
        }

        private static async Task WaitForHold(PlayerPage page)
        {
            var until = Environment.TickCount64 + 3000;
            while (!page.Holding && Environment.TickCount64 < until) await Task.Delay(20);
            if (!page.Holding) throw new Exception("Hold was not recognized");
        }
    }
}

namespace WatchTorrent.Services
{
    internal static class PlayerWindow
    {
        public static bool CanAdjustLevels => true;
        public static double Brightness { get; set; } = 0.5;
        public static double Volume { get; set; } = 0.5;
    }
}

namespace WatchTorrent.Views
{
    // Drive the real shared gesture state machine with lightweight UI stand-ins.
    public partial class PlayerPage
    {
        private readonly ViewModels.PlayerViewModel _viewModel = new();
        private bool _isShutDown;
        private CancellationTokenSource? _singleTap = null;
        private long _lastTapAt = 1;
        private readonly TestView PlaylistOverlay = new(), HoldSpeedBadge = new(), LevelIcon = new(),
            LevelBar = new(), LevelText = new(), LevelIndicator = new();
        public int Taps { get; private set; }
        public int Holds => _viewModel.HoldCount;
        public bool Holding => _viewModel.Holding;
        public bool BadgeVisible => HoldSpeedBadge.Opacity > 0;
        public long LastTapAt => _lastTapAt;
        public void Press() => BeginTouch(200, 200);
        public void Release() => EndTouch(200);
        public void Cancel() => EndTouch(null);
        public void Swipe() => OnTouchMove(200, 250, 1000, 600);
        public void Close() { _isShutDown = true; EndTouch(null); }
        private void HandleTap(double x) => Taps++;
        private void HideControls() { }
    }
    internal sealed class TestView
    {
        public bool IsVisible { get; set; }
        public string? Source { get; set; }
        public string? Text { get; set; }
        public double Progress { get; set; }
        public double Opacity { get; set; }
    }
}
