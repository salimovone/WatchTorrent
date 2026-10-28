namespace WatchTorrent.Services
{
    /// <summary>Sozlamalar sahifasidagi tanlovlar (Preferences'da saqlanadi).</summary>
    public static class AppSettings
    {
        private const string HdrOutputKey = "settings.hdr_output";
        private const string KeepScreenOnInDownloadsKey = "settings.downloads_keep_screen_on";
        private const string BackgroundDownloadsKey = "settings.background_downloads";

        /// <summary>HDR videolar HDR'da ko'rsatiladi (ekran qo'llasa); o'chiq bo'lsa SDR'ga moslab ko'rsatiladi.</summary>
        public static bool HdrOutput
        {
            get => Preferences.Default.Get(HdrOutputKey, true);
            set => Set(HdrOutputKey, value);
        }

        /// <summary>Yuklamalar sahifasi ochiq va yuklash ketayotganda ekran o'chmaydi.</summary>
        public static bool KeepScreenOnInDownloads
        {
            get => Preferences.Default.Get(KeepScreenOnInDownloadsKey, true);
            set => Set(KeepScreenOnInDownloadsKey, value);
        }

        /// <summary>Ekran o'chganda yoki ilova fonga o'tganda ham yuklash davom etadi.</summary>
        public static bool BackgroundDownloads
        {
            get => Preferences.Default.Get(BackgroundDownloadsKey, true);
            set => Set(BackgroundDownloadsKey, value);
        }

        private static void Set(string key, bool value)
        {
            Preferences.Default.Set(key, value);
        }
    }
}
