using WatchTorrent.Services;
using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public static class Routes
    {
        public const string Downloads = "//downloads";

        public static Task OpenDownloadAsync(DownloadItem item) =>
            Shell.Current.GoToAsync(nameof(TorrentDetailPage), new ShellNavigationQueryParameters { ["Download"] = item });

        /// <summary>Ro'yxatda tanlangan torrentni ochadi va tanlovni bekor qiladi (shu elementni qayta bosish mumkin bo'lsin).</summary>
        public static async Task OpenTorrentAsync(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not CollectionView list || e.CurrentSelection.FirstOrDefault() is not TorrentRow row)
            {
                return;
            }

            list.SelectedItem = null;

            await Shell.Current.GoToAsync(nameof(TorrentDetailPage), new ShellNavigationQueryParameters
            {
                ["Item"] = row.Item
            });
        }

        /// <summary>Torrent sahifasidan ko'rish: kerak bo'lsa torrent yuklamalarga qo'shiladi.</summary>
        public static async Task WatchAsync(string magnet, string title, Models.TorrentPageData? page = null)
        {
            var shell = (AppShell)Shell.Current;
            var parameters = new ShellNavigationQueryParameters
            {
                ["Magnet"] = magnet,
                ["Title"] = title
            };
            if (shell.Call.Connected)
            {
                var session = await shell.InviteMovieAsync(magnet, title, page);
                if (session == null) return;
                parameters["Session"] = session;
            }
            await shell.GoToAsync(nameof(PlayerPage), parameters);
        }

        /// <summary>Yuklamalar ro'yxatidagi torrentni ko'rish.</summary>
        public static async Task WatchAsync(DownloadItem item, Models.TorrentPageData? page = null)
        {
            var shell = (AppShell)Shell.Current;
            var parameters = new ShellNavigationQueryParameters
            {
                ["InfoHash"] = item.InfoHash,
                ["Title"] = item.Name
            };
            if (shell.Call.Connected)
            {
                var session = await shell.InviteMovieAsync(DownloadService.ShareMagnet(item), item.Name, page);
                if (session == null) return;
                parameters["Session"] = session;
            }
            await shell.GoToAsync(nameof(PlayerPage), parameters);
        }
    }
}
