namespace WatchTorrent.Services
{
    /// <summary>
    /// WebView ichidagi cookie'lar bilan ishlash. JS'dagi document.cookie HttpOnly cookie'larni (cf_clearance) ko'rmaydi,
    /// shuning uchun har platformaning o'z API'si ishlatiladi.
    /// </summary>
    public static class WebViewCookies
    {
        /// <summary>Domen cookie'lari "name=value; ..." ko'rinishida (HttpOnly cf_clearance ham).</summary>
        public static Task<string> GetAsync(WebView webView, Uri url)
        {
#if ANDROID
            var cookies = Android.Webkit.CookieManager.Instance?.GetCookie(url.GetLeftPart(UriPartial.Authority));

            return Task.FromResult(cookies ?? string.Empty);
#elif WINDOWS
            return GetWindowsAsync(webView, url);
#else
            return Task.FromResult(string.Empty);
#endif
        }

        /// <summary>
        /// Cloudflare tekshiruvi (Turnstile) challenges.cloudflare.com iframe'ida ishlaydi. Android WebView esa
        /// third-party cookie'larni sukut bo'yicha bloklaydi (MAUI ham yoqmaydi) — ba'zi qurilmalarda tekshiruv shu
        /// sababli cheksiz takrorlanadi. Cloudflare'ning Android namunasidagidek ruxsat beriladi.
        /// Sahifa yuklanishidan oldin chaqirilishi kerak.
        /// </summary>
        public static void AllowThirdPartyCookies(WebView webView)
        {
#if ANDROID
            if (webView.Handler?.PlatformView is Android.Webkit.WebView platformView
                && Android.Webkit.CookieManager.Instance is { } cookieManager)
            {
                cookieManager.SetAcceptCookie(true);
                cookieManager.SetAcceptThirdPartyCookies(platformView, true);
            }
#endif
        }

        /// <summary>
        /// WebView'ning barcha cookie, kesh va saqlangan ma'lumotlarini o'chiradi — eskirgan yoki boshqa tarmoqda
        /// olingan Cloudflare cookie'lari tekshiruvni aylantirib qo'yganda yordam beradi.
        /// </summary>
        public static Task ClearAsync(WebView webView)
        {
#if ANDROID
            if (Android.Webkit.CookieManager.Instance is { } cookieManager)
            {
                cookieManager.RemoveAllCookies(null);
                cookieManager.Flush();
            }

            Android.Webkit.WebStorage.Instance?.DeleteAllData();
            (webView.Handler?.PlatformView as Android.Webkit.WebView)?.ClearCache(true);

            return Task.CompletedTask;
#elif WINDOWS
            return ClearWindowsAsync(webView);
#else
            return Task.CompletedTask;
#endif
        }

        /// <summary>Qurilmadagi WebView dvigateli (masalan "com.google.android.webview 133.0.6943.137").</summary>
        public static string? GetEngineInfo()
        {
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && Android.Webkit.WebView.CurrentWebViewPackage is { } package)
            {
                return $"{package.PackageName} {package.VersionName}";
            }
#endif
            return null;
        }

        /// <summary>Qurilmadagi WebView'ning Play Market sahifasini ochadi (yangilash uchun); Play Market bo'lmasa — brauzerda.</summary>
        public static Task OpenStorePageAsync()
        {
#if ANDROID
            var package = StorePackageName;

            try
            {
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse($"market://details?id={package}"));
                intent.SetPackage("com.android.vending");
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Platform.AppContext.StartActivity(intent);

                return Task.CompletedTask;
            }
            catch (Android.Content.ActivityNotFoundException)
            {
                // Play Market o'rnatilmagan.
            }

            return Launcher.Default.OpenAsync($"https://play.google.com/store/apps/details?id={package}");
#else
            return Task.CompletedTask;
#endif
        }

#if ANDROID
        /// <summary>
        /// Android 7–9 da WebView'ni ko'pincha Chrome ta'minlaydi — o'shaning sahifasi ochiladi. Play Market'da bo'lmagan
        /// dvigatellarda (emulyator, Huawei) Google'ning "Android System WebView"i.
        /// </summary>
        private static string StorePackageName
        {
            get
            {
                var current = OperatingSystem.IsAndroidVersionAtLeast(26) ? Android.Webkit.WebView.CurrentWebViewPackage?.PackageName : null;

                return current != null && (current.StartsWith("com.google.android.webview") || current.StartsWith("com.android.chrome") || current.StartsWith("com.chrome."))
                    ? current
                    : "com.google.android.webview";
            }
        }
#endif

#if WINDOWS
        private static async Task<string> GetWindowsAsync(WebView webView, Uri url)
        {
            if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } core })
            {
                return string.Empty;
            }

            var cookies = await core.CookieManager.GetCookiesAsync(url.GetLeftPart(UriPartial.Authority));

            return string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
        }

        private static async Task ClearWindowsAsync(WebView webView)
        {
            if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } core })
            {
                return;
            }

            core.CookieManager.DeleteAllCookies();
            await core.Profile.ClearBrowsingDataAsync();
        }
#endif
    }
}
