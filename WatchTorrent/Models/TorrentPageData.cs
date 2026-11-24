using L = WatchTorrent.Core.Localization.AppText;
using TorrentReader.Search.Models;

namespace WatchTorrent.Models
{
    public record CommentItem(string Username, string Text, string Posted)
    {
        public string Initial => Username.Length > 0 ? Username[..1].ToUpperInvariant() : "?";
    }

    /// <summary>Torrent sahifasidan olingan, ko'rsatishga tayyorlangan (HTML belgilaridan tozalangan) ma'lumot.</summary>
    public record TorrentInfo(
        string? ImageUrl,
        IReadOnlyList<string> Tags,
        string UploadedBy,
        string DateUploaded,
        string DateLastChecked,
        int Downloads,
        string InfoHash,
        string Magnet,
        string DescriptionHtml,
        IReadOnlyList<string> Files,
        IReadOnlyList<CommentItem> Comments)
    {
        public bool HasImage => ImageUrl != null;

        public bool HasDescription => !string.IsNullOrWhiteSpace(DescriptionHtml);

        public bool HasComments => Comments.Count > 0;
        public string DownloadCountText => L.F("DownloadCount", Downloads);

        public string CommentsHeader => L.Get(L.F("S072", Comments.Count));
    }

    public record TorrentPageData(SearchResultItem? Source, TorrentInfo Detail, string Title, string Size,
        int Seeders, int Leechers, string? PosterUrl);
}
