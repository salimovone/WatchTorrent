namespace WatchTorrent.Core;

/// <summary>BEP 9/52 magnet identity without a dependency on a torrent engine.</summary>
public sealed record Magnet(string Uri, string Hash)
{
    public static Magnet Parse(string uri) => TryParse(uri, out var magnet) ? magnet! : throw new FormatException("Invalid BitTorrent magnet URI.");
    public static bool TryParse(string? uri, out Magnet? magnet)
    {
        magnet = null;
        if (uri == null || !uri.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return false;
        string? v1 = null, v2 = null;
        try
        {
            foreach (var field in uri[8..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = field.Split('=', 2);
                if (pair.Length != 2 || !pair[0].Equals("xt", StringComparison.OrdinalIgnoreCase)) continue;
                var value = System.Uri.UnescapeDataString(pair[1]);
                if (value.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
                {
                    var hash = value[9..];
                    if (hash.Length == 40 && hash.All(System.Uri.IsHexDigit)) v1 = hash.ToLowerInvariant();
                    else if (hash.Length == 32) v1 = DecodeBase32(hash);
                }
                if (value.StartsWith("urn:btmh:1220", StringComparison.OrdinalIgnoreCase))
                {
                    var hash = value[13..];
                    if (hash.Length == 64 && hash.All(System.Uri.IsHexDigit)) v2 = hash.ToLowerInvariant();
                }
            }
        }
        catch (UriFormatException) { return false; }
        if ((v1 ?? v2) is not { } identity) return false;
        magnet = new(uri, identity);
        return true;
    }
    private static string? DecodeBase32(string encoded)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        Span<byte> bytes = stackalloc byte[20];
        int bits = 0, accumulator = 0, index = 0;
        foreach (var c in encoded)
        {
            var value = alphabet.IndexOf(char.ToUpperInvariant(c));
            if (value < 0) return null;
            accumulator = (accumulator << 5) | value;
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes[index++] = (byte)(accumulator >> bits); }
        }
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
