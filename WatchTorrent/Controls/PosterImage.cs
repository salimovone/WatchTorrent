using System.Diagnostics;
using WatchTorrent.Services;

namespace WatchTorrent.Controls
{
    /// <summary>
    /// Torrent nomi bo'yicha posterni o'zi topib ko'rsatadigan rasm. CollectionView qatorlari qayta ishlatilganda
    /// (virtualizatsiya) eski so'rov natijasi yangi qatorga tushib qolmasligi tekshiriladi.
    /// </summary>
    public class PosterImage : Image
    {
        public static readonly BindableProperty TorrentNameProperty = BindableProperty.Create(
            nameof(TorrentName),
            typeof(string),
            typeof(PosterImage),
            propertyChanged: (bindable, _, newValue) => ((PosterImage)bindable).OnTorrentNameChanged(newValue as string));

        public string? TorrentName
        {
            get => (string?)GetValue(TorrentNameProperty);
            set => SetValue(TorrentNameProperty, value);
        }

        /// <summary>Yuklanadigan rasm kengligi (px) — ro'yxatda kichik, katta joylarda kattaroq.</summary>
        public int PosterWidth { get; set; } = 240;

        private async void OnTorrentNameChanged(string? torrentName)
        {
            Source = null;

            if (string.IsNullOrWhiteSpace(torrentName)
                || IPlatformApplication.Current?.Services.GetService<PosterService>() is not { } posters)
            {
                return;
            }

            try
            {
                var poster = await posters.FindAsync(torrentName);

                if (poster != null && TorrentName == torrentName)
                {
                    Source = ImageSource.FromUri(new Uri(poster.Sized(PosterWidth)));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Posters] {torrentName}: {ex.Message}");
            }
        }
    }
}
