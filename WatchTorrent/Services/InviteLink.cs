using L = WatchTorrent.Core.Localization.AppText;
using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;

namespace WatchTorrent.Services;

internal static class InviteLink
{
    public const string Scheme = "watchtorrent";
    public const string Host = "join";
    public const string WebHost = "salimovone.github.io";
    public const string WebPath = "/watchtorrent/join/";
    private const string WebPrefix = "https://" + WebHost + WebPath + "#";
    private const string ShortPrefix = Scheme + "://" + Host + "/v2/";
    private const string LegacyWebPrefix = "https://watchtorrent-join.convivialbongo1.chatgpt.site/#";
    private const string Prefix = Scheme + "://" + Host + "/v1/";

    internal static string CallId(byte[] secret) =>
        Convert.ToHexString(SHA256.HashData(secret).AsSpan(0, 16)).ToLowerInvariant();

    public static async Task<string> CreateAsync(byte[] secret, string offer, CancellationToken token)
    {
        await VoiceAnswerExchange.PublishOfferAsync(secret, offer, token);
        return WebPrefix + Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static async Task<string> ReadAsync(string input)
    {
        string link = input.Trim();
        foreach (var prefix in new[] { WebPrefix, ShortPrefix, LegacyWebPrefix })
        {
            if (!link.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string code = link[prefix.Length..];
            if (code.Length != 22 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidDataException(L.Get("Taklif havolasi buzilgan."));
            var secret = Convert.FromBase64String(code.Replace('-', '+').Replace('_', '/') + "==");
            return await VoiceAnswerExchange.FetchOfferAsync(secret);
        }
        if (link.Length > 32768 || !link.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.Get("WatchTorrent taklif havolasini to‘liq kiriting."));
        var token = link[Prefix.Length..];
        if (token.Length == 0 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException(L.Get("Taklif havolasi buzilgan."));
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
        throw new InvalidDataException(L.Get("Taklif havolasi buzilgan. Yangi havola so‘rang."));
    }
}
