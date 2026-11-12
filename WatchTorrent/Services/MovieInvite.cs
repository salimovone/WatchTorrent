using MonoTorrent;
using System.Text.Json.Serialization;

namespace WatchTorrent.Services;

internal sealed record MovieDetails(string? Size, string? Poster, string? Tags, int? Seeds, int? Leeches,
    bool CatalogCounts, int? FileCount, string? SelectedVideo, string? VideoSize);

internal sealed record MovieInvite(string Magnet, string Title, int FileIndex, Guid Session, MovieDetails? Details = null)
{
    internal const int MaxBytes = 64 * 1024;
    [JsonIgnore]
    public bool Valid => Title is { Length: > 0 and <= 512 } && FileIndex >= -1 &&
        Magnet?.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) == true &&
        MagnetLink.TryParse(Magnet, out _);

    public static MovieInvite Create(string magnet, string title)
    {
        var hash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
        return new(magnet, string.IsNullOrWhiteSpace(title) ? hash : title[..Math.Min(title.Length, 512)],
            Preferences.Default.Get($"lastfile.{hash}", -1), Guid.NewGuid());
    }

    public ITorrentManagerFile? SelectFile(DownloadItem? item)
    {
        if (item?.Manager.HasMetadata != true) return null;
        var files = item.Manager.Files;
        var playable = MediaFiles.GetPlayable(files);
        return playable.FirstOrDefault(f => files.IndexOf(f) == FileIndex) ?? playable.FirstOrDefault();
    }
}
