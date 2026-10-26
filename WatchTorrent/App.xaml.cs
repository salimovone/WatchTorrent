using System.Diagnostics;
using WatchTorrent.Services;

namespace WatchTorrent
{
    public partial class App : Application
    {
        private readonly DownloadService _downloads;

        public App(DownloadService downloads)
        {
            InitializeComponent();

            UserAppTheme = AppTheme.Dark;
            _downloads = downloads;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell()) { Title = "WatchTorrent" };

            // Oldingi sessiyadagi yuklamalar ilova ochilishi bilan davom etadi.
            _ = InitializeDownloadsAsync();

            window.Stopped += async (_, _) => await _downloads.SaveAsync();

            return window;
        }

        private async Task InitializeDownloadsAsync()
        {
            try
            {
                await _downloads.InitializeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloads] Ishga tushmadi: {ex}");
            }
        }
    }
}
