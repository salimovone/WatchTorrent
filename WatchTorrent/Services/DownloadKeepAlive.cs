#if ANDROID
using System.Diagnostics;
#elif WINDOWS
using System.Runtime.InteropServices;
#endif

namespace WatchTorrent.Services
{
    /// <summary>
    /// Yuklash ketayotganda qurilma uni to'xtatib qo'ymasin: Android'da fon xizmati (bildirishnoma, wake lock) —
    /// ekran o'chsa yoki ilova fonga o'tsa ham yuklash davom etadi; Windows'da kompyuter uyquga ketmaydi.
    /// Yuklamalar sahifasi ochiq bo'lsa (sozlamada yoqilgan bo'lsa) ekran o'chmaydi. Har soniyada
    /// <see cref="DownloadService"/> taymeridan, UI oqimida chaqiriladi.
    /// </summary>
    public static class DownloadKeepAlive
    {
        private const string ScreenReason = "downloads";

        private static bool _isPageVisible;
        private static bool _hasActive;
        private static bool _isRunning;

        /// <summary>Yuklamalar sahifasi ko'rinib turibdi.</summary>
        public static void SetPageVisible(bool visible)
        {
            _isPageVisible = visible;
            ApplyScreen();
        }

        public static void Update(IReadOnlyCollection<DownloadItem> items)
        {
            var active = items.Where(item => item.IsRunning && !item.IsComplete).ToList();

            _hasActive = active.Count > 0;
            ApplyScreen();

            var keepRunning = _hasActive && AppSettings.BackgroundDownloads;

            if (keepRunning)
            {
                Start(active);
            }
            else if (_isRunning)
            {
                Stop();
            }
        }

        private static void ApplyScreen()
        {
            ScreenAwake.Set(ScreenReason, _isPageVisible && _hasActive && AppSettings.KeepScreenOnInDownloads);
        }

        private static void Start(IReadOnlyList<DownloadItem> active)
        {
            var total = active.Sum(item => item.TotalBytes);
            var downloaded = active.Sum(item => item.DownloadedBytes);
            var rate = active.Sum(item => item.Manager.Monitor.DownloadRate);
            var title = active.Count == 1 ? active[0].Name : $"{active.Count} ta torrent yuklanmoqda";
            var text = total > 0
                ? $"{100.0 * downloaded / total:0.0}% · {Formatting.Bytes(downloaded)} / {Formatting.Bytes(total)} · ↓ {Formatting.Speed(rate)}"
                : $"↓ {Formatting.Speed(rate)}";
            var progress = total > 0 ? (int)(1000 * downloaded / total) : -1;

#if ANDROID
            try
            {
                if (!_isRunning)
                {
                    DownloadForegroundService.Start(Platform.AppContext, title, text, progress);
                    _ = RequestNotificationPermissionAsync();
                }
                else
                {
                    DownloadForegroundService.UpdateNotification(Platform.AppContext, title, text, progress);
                }

                _isRunning = true;
            }
            catch (Exception ex)
            {
                // Android 12+: ilova fonda bo'lsa fon xizmatini boshlashga ruxsat yo'q — keyingi soniyada qayta uriniladi.
                Debug.WriteLine($"[KeepAlive] Fon xizmati boshlanmadi: {ex.Message}");
            }
#elif WINDOWS
            if (!_isRunning)
            {
                // Ekran o'chishi mumkin, lekin kompyuter uyquga ketmaydi (yuklash to'xtamasin).
                SetThreadExecutionState(EsContinuous | EsSystemRequired);
                _isRunning = true;
            }
#endif
        }

        private static void Stop()
        {
            _isRunning = false;

#if ANDROID
            DownloadForegroundService.Stop(Platform.AppContext);
#elif WINDOWS
            SetThreadExecutionState(EsContinuous);
#endif
        }

#if ANDROID
        private const string NotificationPermissionAskedKey = "keepalive.notification_permission_asked";

        /// <summary>Android 13+: bildirishnomaga ruxsatsiz ham xizmat ishlaydi, lekin yuklash holati ko'rinmaydi — bir marta so'raladi.</summary>
        private static async Task RequestNotificationPermissionAsync()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33) || Preferences.Default.Get(NotificationPermissionAskedKey, false))
            {
                return;
            }

            try
            {
                if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                {
                    Preferences.Default.Set(NotificationPermissionAskedKey, true);
                    await Permissions.RequestAsync<Permissions.PostNotifications>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KeepAlive] Bildirishnoma ruxsati: {ex.Message}");
            }
        }
#elif WINDOWS
        private const uint EsContinuous = 0x80000000;
        private const uint EsSystemRequired = 0x00000001;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);
#endif
    }
}
