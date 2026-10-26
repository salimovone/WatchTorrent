using System.Net;
using System.Text.RegularExpressions;

namespace WatchTorrent.Services
{
    public enum TitleKind
    {
        Unknown,
        Series,
        Game
    }

    /// <summary>
    /// Torrent nomidan film/serial/o'yin sarlavhasi va yilini ajratadi:
    /// "The.Weight.2026.1080p.WEBRip.x265-NeoNoir" → "The Weight" (2026),
    /// "Sheriff.Country.S02E03.1080p" → "Sheriff Country" (serial),
    /// "Cyberpunk 2077: Ultimate Edition (v2.5 + DLCs) [FitGirl Repack]" → "Cyberpunk 2077" (o'yin).
    /// </summary>
    public sealed record TorrentTitle(string Title, int? Year, TitleKind Kind)
    {
        private static readonly Regex LeadingGroupRegex = new(@"^\s*\[[^\]]*\]\s*", RegexOptions.Compiled);
        private static readonly Regex YearRegex = new(@"(?<!\d)(19\d{2}|20\d{2})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex SeasonRegex = new(@"\bS\d{1,2}(?:E\d{1,3})?\b|\bSeason\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ReleaseTagRegex = new(
            @"\b(2160p|1080p|720p|576p|480p|4K|UHD|WEB-?DL|WEB-?Rip|BluRay|Blu-Ray|BDRip|BRRip|HDRip|DVDRip|DVDScr|HDTV|HDCAM|TELESYNC|HDTS|x264|x265|HEVC|H\s?264|H\s?265|10bit|REMASTERED|EXTENDED|UNRATED|PROPER|REPACK|COMPLETE|MULTi\d*|DUBBED|IMAX)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameRegex = new(
            @"FitGirl|DODI|ElAmigos|KaOs|\bGOG\b|Repack|\bv\d+(?:\.\d+)+|\bDLCs?\b|\bBuild\s*\d+|CODEX|\bRUNE\b|TENOKE|EMPRESS|PLAZA|SKIDROW|Razor1911|Deluxe Edition|Ultimate Edition|Game of the Year",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameNoiseRegex = new(
            @"\b(Update|Build|v\d+|Deluxe|Digital|Complete|Definitive|GOTY|Ultimate|Gold|Premium|Anniversary|Edition|Bundle|Repack|MULTi\d*)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SceneGroupSuffixRegex = new(@"-[A-Za-z0-9]{2,}\s*$", RegexOptions.Compiled);
        private static readonly Regex SpacesRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex NonAlphanumericRegex = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

        public string Key => $"{Normalize(Title)}|{Year}|{Kind}";

        public string Query => Year is { } year ? $"{Title} {year}" : Title;

        public static TorrentTitle? Parse(string torrentName)
        {
            var name = LeadingGroupRegex.Replace(WebUtility.HtmlDecode(torrentName ?? string.Empty), string.Empty);
            var kind = GameRegex.IsMatch(name) ? TitleKind.Game : SeasonRegex.IsMatch(name) ? TitleKind.Series : TitleKind.Unknown;

            // O'yin scene relizlari: "Forza Horizon 6 Update v461.691-RUNE" — oxiridagi guruh nomi olib tashlanadi.
            // Filmlarda qilinmaydi: "X-Men" kabi nomlar buzilardi (u yerda guruh baribir sifat teglaridan keyin keladi).
            if (kind == TitleKind.Game)
            {
                name = SceneGroupSuffixRegex.Replace(name, string.Empty);
            }

            // Nuqta/pastki chiziq — so'z ajratuvchi ("The.Weight.2026"); "v2.5" kabilar baribir kesib tashlanadi.
            var text = name.Replace('.', ' ').Replace('_', ' ');
            var cut = text.Length;
            int? year = null;

            foreach (Match match in YearRegex.Matches(text))
            {
                var value = int.Parse(match.Value);

                // Nom boshidagi raqam ("2001 A Space Odyssey 1968") yoki kelajakdagi "yil" ("Cyberpunk 2077") yil emas.
                if (match.Index > 0 && value <= DateTime.Now.Year + 1)
                {
                    year = value;
                    cut = match.Index;
                    break;
                }
            }

            cut = Earliest(cut, SeasonRegex.Match(text));
            cut = Earliest(cut, ReleaseTagRegex.Match(text));

            if (kind == TitleKind.Game)
            {
                cut = Earliest(cut, GameNoiseRegex.Match(text));
            }

            var separators = new[] { "(", "[", " - ", " – ", "+", "|" };

            foreach (var separator in separators)
            {
                var index = text.IndexOf(separator, StringComparison.Ordinal);

                if (index > 0)
                {
                    cut = Math.Min(cut, index);
                }
            }

            var title = SpacesRegex.Replace(text[..cut], " ").Trim(' ', '-', ':', '(', '[', ',', '+', '|');

            return title.Length < 2 ? null : new TorrentTitle(title, year, kind);
        }

        /// <summary>Taqqoslash uchun: kichik harf, faqat harf/raqam, boshidagi "the" olib tashlanadi.</summary>
        public static string Normalize(string text)
        {
            // Apostrof so'zni bo'lmasin: "Logan's Run" == "Logans Run".
            var normalized = NonAlphanumericRegex.Replace(text.ToLowerInvariant().Replace("'", string.Empty).Replace("’", string.Empty), " ").Trim();

            return normalized.StartsWith("the ", StringComparison.Ordinal) ? normalized[4..] : normalized;
        }

        private static int Earliest(int cut, Match match)
        {
            return match.Success && match.Index > 0 ? Math.Min(cut, match.Index) : cut;
        }
    }
}
