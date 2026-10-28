using System.Globalization;

namespace WatchTorrent.Converters
{
    /// <summary>bool qiymatni teskarisiga aylantiradi (IsVisible="{Binding X, Converter=InvertBool}").</summary>
    public class InvertBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is not true;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is not true;
        }
    }
}
