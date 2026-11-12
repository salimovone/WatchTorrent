using WatchTorrent.Views;

namespace WatchTorrent
{
    public partial class AppShell : Shell
    {
        internal async Task OpenInvitationAsync(Stream stream)
        {
            if (CurrentPage is not VoiceCallPage) await GoToAsync("//connection/call");
            var page = (VoiceCallPage)ConnectionTab.Content;
            await page.ImportAsync(stream);
        }

        internal void ShowConnection(bool connected)
        {
            VoiceTab.Title = connected ? "Ulangan" : "Ulanish";
            ConnectionTab.Title = VoiceTab.Title;
        }

        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(TorrentDetailPage), typeof(TorrentDetailPage));
            Routing.RegisterRoute(nameof(DescriptionPage), typeof(DescriptionPage));
            Routing.RegisterRoute(nameof(PlayerPage), typeof(PlayerPage));
            Routing.RegisterRoute(nameof(ImageViewerPage), typeof(ImageViewerPage));
            Routing.RegisterRoute(nameof(DirectConnectionTestPage), typeof(DirectConnectionTestPage));
            Routing.RegisterRoute(nameof(SearchPage), typeof(SearchPage));
        }
    }
}
