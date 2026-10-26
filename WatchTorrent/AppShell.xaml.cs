using WatchTorrent.Views;

namespace WatchTorrent
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(TorrentDetailPage), typeof(TorrentDetailPage));
            Routing.RegisterRoute(nameof(DescriptionPage), typeof(DescriptionPage));
            Routing.RegisterRoute(nameof(PlayerPage), typeof(PlayerPage));
            Routing.RegisterRoute(nameof(ImageViewerPage), typeof(ImageViewerPage));
        }
    }
}
