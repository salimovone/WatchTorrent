using MonoTorrent;

namespace WatchTorrent.Services
{
    /// <summary>Torrent ichidagi video va subtitr fayllarini aniqlash.</summary>
    public static class MediaFiles
    {
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".mts", ".webm",
            ".mpg", ".mpeg", ".flv", ".vob", ".3gp", ".ogv", ".divx", ".rmvb"
        };

        private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".smi"
        };

        public static bool IsVideo(ITorrentFile file)
        {
            return VideoExtensions.Contains(Path.GetExtension(file.Path));
        }

        public static bool IsSubtitle(ITorrentFile file)
        {
            return SubtitleExtensions.Contains(Path.GetExtension(file.Path));
        }

        /// <summary>
        /// Ko'rish mumkin bo'lgan fayllar: "sample" lar tashlab yuboriladi (agar boshqa video bo'lsa),
        /// serial qismlari to'g'ri tartibda bo'lishi uchun yo'l bo'yicha saralanadi.
        /// </summary>
        public static IReadOnlyList<ITorrentManagerFile> GetPlayable(IEnumerable<ITorrentManagerFile> files)
        {
            var videos = files.Where(IsVideo).ToList();
            var withoutSamples = videos
                .Where(file => !Path.GetFileName(file.Path).Contains("sample", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return (withoutSamples.Count > 0 ? withoutSamples : videos)
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
