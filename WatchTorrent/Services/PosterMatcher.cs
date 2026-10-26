using System.Text.Json;

namespace WatchTorrent.Services
{
    /// <summary>IMDb qidiruv natijalaridan torrentga eng mos posterni tanlaydi.</summary>
    public static class PosterMatcher
    {
        public static string? PickBest(JsonElement root, TorrentTitle title)
        {
            if (!root.TryGetProperty("d", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? best = null;
            var bestScore = double.MinValue;

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("i", out var image) || !image.TryGetProperty("imageUrl", out var imageUrl))
                {
                    continue;
                }

                var name = item.TryGetProperty("l", out var label) ? label.GetString() ?? string.Empty : string.Empty;
                var year = item.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : (int?)null;
                var kind = item.TryGetProperty("qid", out var qid) ? qid.GetString() ?? string.Empty : string.Empty;

                var score = Score(title, name, year, kind);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = imageUrl.GetString();
                }
            }

            // Nomi umuman mos kelmasa noto'g'ri poster ko'rsatgandan ko'ra ko'rsatmagan yaxshi.
            return bestScore >= 2 ? best : null;
        }

        private static double Score(TorrentTitle title, string name, int? year, string kind)
        {
            var wanted = TorrentTitle.Normalize(title.Title);
            var found = TorrentTitle.Normalize(name);
            double score;

            if (found == wanted)
            {
                score = 4;
            }
            else if (found.StartsWith(wanted, StringComparison.Ordinal) || wanted.StartsWith(found, StringComparison.Ordinal))
            {
                score = 2;
            }
            else
            {
                return double.MinValue;
            }

            if (title.Year is { } wantedYear && year is { } foundYear)
            {
                var difference = Math.Abs(wantedYear - foundYear);
                score += difference == 0 ? 3 : difference == 1 ? 1.5 : -2;
            }

            score += (title.Kind, kind) switch
            {
                (_, "musicVideo" or "podcastSeries" or "podcastEpisode" or "tvEpisode") => -10,
                (TitleKind.Game, "videoGame") => 3,
                (TitleKind.Game, _) => -1,
                (TitleKind.Series, "tvSeries" or "tvMiniSeries") => 2,
                (TitleKind.Series, _) => -1,
                (_, "movie") => 1,
                (_, "tvSeries" or "tvMiniSeries" or "tvMovie") => 0.5,
                (_, "videoGame") => -0.5,
                (_, "short") => -2,
                _ => 0
            };

            return score;
        }
    }
}
