namespace WatchTorrent.Services;

// UI-only formatting is irrelevant to the control-state checks.
internal static class Formatting
{
    public static string Bytes(long value) => value.ToString();
    public static string Speed(long value) => value.ToString();
    public static string Duration(TimeSpan value) => value.ToString();
}
