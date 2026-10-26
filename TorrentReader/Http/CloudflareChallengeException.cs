using System;

namespace TorrentReader.Http
{
    /// <summary>
    /// Sayt Cloudflare tekshiruvini qaytardi va uni hal qilib bo'lmadi
    /// (solver berilmagan yoki u bergan cookie ishlamadi).
    /// </summary>
    public class CloudflareChallengeException : Exception
    {
        public Uri Url { get; }

        public CloudflareChallengeException(Uri url)
            : base($"Cloudflare challenge: {url}")
        {
            Url = url;
        }
    }
}
