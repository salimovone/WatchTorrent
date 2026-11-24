using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TorrentReader.Search.Models;
using WatchTorrent.Services;

namespace WatchTorrent.ViewModels;

public sealed class TorrentRow
{
    private static readonly Regex ReleaseParts = new(
        @"\b(?:S\d{1,2}(?:E\d{1,3})?|2160p|1080p|720p|576p|480p|4K|WEB-?DL|WEBRip|BluRay|BDRip|HDTV|HDCAM|TELESYNC|HEVC|x265|x264)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TorrentRow(SearchResultItem item)
    {
        Item = item;
        var name = WebUtility.HtmlDecode(item.Name ?? string.Empty);
        FullName = name.Trim();
        PosterTitle = TorrentTitle.Parse(name);
        Title = PosterTitle?.Title ?? name;

        var tags = ReleaseParts.Matches(name.Replace('.', ' ').Replace('_', ' '))
            .Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(4);
        if (PosterTitle?.Year is int year)
        {
            tags = tags.Prepend(year.ToString(CultureInfo.InvariantCulture));
        }
        Release = string.Join(" · ", tags);
    }

    public SearchResultItem Item { get; }
    public string FullName { get; }
    public string Title { get; }
    public string Release { get; }
    public TorrentTitle? PosterTitle { get; }
}
