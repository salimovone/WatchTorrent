namespace TorrentReader.Http
{
    /// <summary>
    /// Cloudflare tekshiruvidan o'tgan brauzerdan (WebView) olingan ma'lumot.
    /// cf_clearance cookie aynan shu User-Agent va IP ga bog'langan.
    /// </summary>
    public class CloudflareClearance
    {
        /// <summary>WebView ning User-Agent qatori (aynan bir xil bo'lishi shart).</summary>
        public string UserAgent { get; }

        /// <summary>Domen cookie'lari "name=value; name2=value2" ko'rinishida (cf_clearance shu yerda).</summary>
        public string Cookies { get; }

        public CloudflareClearance(string userAgent, string cookies)
        {
            UserAgent = userAgent;
            Cookies = cookies;
        }
    }
}
