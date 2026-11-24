using System.Diagnostics;
using WatchTorrent.Services;

namespace WatchTorrent.Controls
{
    /// <summary>
    /// Loads the poster for the current row and drops results from rows that have been recycled.
    /// </summary>
    public class PosterImage : Image
    {
        public static readonly BindableProperty TitleProperty = BindableProperty.Create(
            nameof(Title),
            typeof(TorrentTitle),
            typeof(PosterImage),
            propertyChanged: (bindable, _, newValue) => ((PosterImage)bindable).OnTitleChanged(newValue as TorrentTitle));

        public TorrentTitle? Title
        {
            get => (TorrentTitle?)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Yuklanadigan rasm kengligi (px) — ro'yxatda kichik, katta joylarda kattaroq.</summary>
        public int PosterWidth { get; set; } = 240;

        private async void OnTitleChanged(TorrentTitle? title)
        {
            if (title is null
                || IPlatformApplication.Current?.Services.GetService<PosterService>() is not { } posters)
            {
                Source = null;
                return;
            }

            try
            {
                var lookup = posters.FindAsync(title);
                if (!lookup.IsCompletedSuccessfully)
                {
                    Source = null;
                }

                var poster = await lookup;
                if (Title == title)
                {
                    Source = poster is null ? null : ImageSource.FromUri(new Uri(poster.Sized(PosterWidth)));
                }
            }
            catch (Exception ex)
            {
                if (Title == title) Source = null;
                Debug.WriteLine($"[Posters] {title.Title}: {ex.Message}");
            }
        }
    }
}
