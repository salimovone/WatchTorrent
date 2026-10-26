using TorrentReader.Http;
using WatchTorrent.Views;

namespace WatchTorrent.Services
{
    /// <summary>
    /// TorrentClient uchun Cloudflare yechuvchi: WebView sahifasini modal ochadi, tekshiruvdan o'tilgach
    /// cookie + User-Agent'ni qaytaradi va keyingi ishga tushishlar uchun saqlab qo'yadi.
    /// </summary>
    public class CloudflareSolver
    {
        private const string UserAgentKey = "cloudflare.user_agent";
        private const string CookiesKey = "cloudflare.cookies";

        public CloudflareClearance? LoadSaved()
        {
            var userAgent = Preferences.Default.Get(UserAgentKey, string.Empty);
            var cookies = Preferences.Default.Get(CookiesKey, string.Empty);

            if (string.IsNullOrEmpty(userAgent) || string.IsNullOrEmpty(cookies))
            {
                return null;
            }

            return new CloudflareClearance(userAgent, cookies);
        }

        public Task<CloudflareClearance?> SolveAsync(Uri url)
        {
            // TorrentClient bu metodni fon oqimidan chaqiradi.
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

                if (navigation == null)
                {
                    return null;
                }

                var page = new CloudflarePage(url);

                await navigation.PushModalAsync(page);

                var clearance = await page.Result;

                // Foydalanuvchi "orqaga" bilan yopgan bo'lsa sahifa stekda allaqachon yo'q.
                if (navigation.ModalStack.Contains(page))
                {
                    await navigation.PopModalAsync();
                }

                if (clearance != null)
                {
                    Preferences.Default.Set(UserAgentKey, clearance.UserAgent);
                    Preferences.Default.Set(CookiesKey, clearance.Cookies);
                }

                return clearance;
            });
        }
    }
}
