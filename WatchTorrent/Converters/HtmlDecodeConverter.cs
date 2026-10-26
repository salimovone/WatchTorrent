using System.Globalization;
using System.Net;

namespace WatchTorrent.Converters
{
    /// <summary>Saytdan kelgan matndagi HTML belgilarini (&amp;amp; va h.k.) oddiy matnga aylantiradi.</summary>
    public class HtmlDecodeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is string text ? WebUtility.HtmlDecode(text).Trim() : value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
