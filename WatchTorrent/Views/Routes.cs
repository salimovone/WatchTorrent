using TorrentReader.Search.Models;
using WatchTorrent.Services;

namespace WatchTorrent.Views
{
    public static class Routes
    {
        public const string Downloads = "//downloads";

        /// <summary>Ro'yxatda tanlangan torrentni ochadi va tanlovni bekor qiladi (shu elementni qayta bosish mumkin bo'lsin).</summary>
        public static async Task OpenTorrentAsync(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not CollectionView list || e.CurrentSelection.FirstOrDefault() is not SearchResultItem item)
            {
                return;
            }

            list.SelectedItem = null;

            await Shell.Current.GoToAsync(nameof(TorrentDetailPage), new ShellNavigationQueryParameters
            {
                ["Item"] = item
            });
        }

        /// <summary>Torrent sahifasidan ko'rish: kerak bo'lsa torrent yuklamalarga qo'shiladi.</summary>
        public static Task WatchAsync(string magnet, string title)
        {
            return Shell.Current.GoToAsync(nameof(PlayerPage), new ShellNavigationQueryParameters
            {
                ["Magnet"] = magnet,
                ["Title"] = title
            });
        }

        /// <summary>Yuklamalar ro'yxatidagi torrentni ko'rish.</summary>
        public static Task WatchAsync(DownloadItem item)
        {
            return Shell.Current.GoToAsync(nameof(PlayerPage), new ShellNavigationQueryParameters
            {
                ["InfoHash"] = item.InfoHash,
                ["Title"] = item.Name
            });
        }
    }
}
