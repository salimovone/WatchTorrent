using System.IO.Compression;
using System.Text;

namespace WatchTorrent.Services;

internal static class InviteLink
{
    public const string Scheme = "watchtorrent";
    public const string Host = "join";
    private const string Prefix = Scheme + "://" + Host + "/v1/";

    public static string Create(string offer)
    {
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(Encoding.UTF8.GetBytes(offer));
        var encoded = Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Prefix + encoded;
    }

    public static string Read(string input)
    {
        string link = input.Trim();
        if (link.Length > 32768 || !link.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WatchTorrent taklif havolasini to‘liq kiriting.");
        var token = link[Prefix.Length..];
        if (token.Length == 0 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Taklif havolasi buzilgan.");
        try
        {
            string base64 = token.Replace('-', '+').Replace('_', '/').PadRight((token.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(base64);
            var plain = new byte[65536];
            using var decoder = new BrotliDecoder();
            if (decoder.Decompress(bytes, plain, out int consumed, out int written) == System.Buffers.OperationStatus.Done &&
                consumed == bytes.Length && written > 0)
                return Encoding.UTF8.GetString(plain, 0, written);
        }
        catch (FormatException) { }
        throw new InvalidDataException("Taklif havolasi buzilgan. Yangi havola so‘rang.");
    }
}
