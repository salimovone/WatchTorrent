using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;
using WatchTorrent.Services;

namespace WatchTorrent.Converters;

public sealed class TorrentLabelConverter : IValueConverter
{
    private static readonly Regex ReleaseParts = new Regex("\\b(?:S\\d{1,2}(?:E\\d{1,3})?|2160p|1080p|720p|576p|480p|4K|WEB-?DL|WEBRip|BluRay|BDRip|HDTV|HDCAM|TELESYNC|HEVC|x265|x264)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string text = WebUtility.HtmlDecode((value as string) ?? string.Empty);
        var torrentTitle = TorrentTitle.Parse(text);
        if (parameter as string != "Release")
        {
            return torrentTitle?.Title ?? text;
        }
        List<string> list = new List<string>();
        if (torrentTitle?.Year is int year) list.Add(year.ToString(CultureInfo.InvariantCulture));
        list.AddRange((from match in ReleaseParts.Matches(text.Replace('.', ' ').Replace('_', ' '))
            select match.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
        return string.Join(" · ", list);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
