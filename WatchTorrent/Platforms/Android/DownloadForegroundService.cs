using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace WatchTorrent
{
    /// <summary>
    /// Yuklash davom etayotganda ishlaydigan fon xizmati: bildirishnoma ko'rsatadi va tizimga ilova jarayonini
    /// to'xtatmaslikni bildiradi; wake lock — ekran o'chganda protsessor uxlamaydi, Wi-Fi lock — Wi-Fi sekinlashmaydi.
    /// Torrent dvigatelining o'zi ilovada (DownloadService) ishlaydi — xizmat faqat jarayonni tirik ushlaydi.
    /// </summary>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
    public class DownloadForegroundService : Service
    {
        private const int NotificationId = 1001;
        private const string ChannelId = "downloads";
        private const string TitleExtra = "title";
        private const string TextExtra = "text";
        private const string ProgressExtra = "progress";

        private PowerManager.WakeLock? _wakeLock;
        private WifiManager.WifiLock? _wifiLock;

        public static void Start(Context context, string title, string text, int progress)
        {
            var intent = new Intent(context, typeof(DownloadForegroundService));
            intent.PutExtra(TitleExtra, title);
            intent.PutExtra(TextExtra, text);
            intent.PutExtra(ProgressExtra, progress);

            ContextCompat.StartForegroundService(context, intent);
        }

        public static void UpdateNotification(Context context, string title, string text, int progress)
        {
            NotificationManagerCompat.From(context)!.Notify(NotificationId, BuildNotification(context, title, text, progress));
        }

        public static void Stop(Context context)
        {
            // Xizmat oldinroq to'xtagan bo'lsa (Android 15 muddati) bildirishnoma qolib ketmasin — OnDestroy ham o'chiradi.
            if (!context.StopService(new Intent(context, typeof(DownloadForegroundService))))
            {
                NotificationManagerCompat.From(context)!.Cancel(NotificationId);
            }
        }

        public override IBinder? OnBind(Intent? intent)
        {
            return null;
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            var notification = BuildNotification(
                this,
                intent?.GetStringExtra(TitleExtra) ?? "Yuklanmoqda",
                intent?.GetStringExtra(TextExtra) ?? string.Empty,
                intent?.GetIntExtra(ProgressExtra, -1) ?? -1);

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }

            AcquireLocks();

            // Tizim jarayonni o'ldirsa xizmat o'zi qayta tug'ilmaydi — yuklamalar ilova ochilganda davom etadi.
            return StartCommandResult.NotSticky;
        }

        /// <summary>Android 15+: "dataSync" xizmatlari sutkasiga 6 soat ishlaydi — muddat tugasa xizmat to'xtaydi.</summary>
        [SupportedOSPlatform("android35.0")]
        public override void OnTimeout(int startId, ForegroundService fgsType)
        {
            StopSelf();
        }

        public override void OnDestroy()
        {
            ReleaseLocks();

            // Xizmat to'xtaganda bildirishnoma ham yo'qolsin (UpdateNotification'dan keyin u o'zi o'chmaydi).
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                StopForeground(StopForegroundFlags.Remove);
            }
            else
            {
                StopForeground(true);
            }

            NotificationManagerCompat.From(this)!.Cancel(NotificationId);
            base.OnDestroy();
        }

        private void AcquireLocks()
        {
            if (_wakeLock == null && GetSystemService(PowerService) is PowerManager power)
            {
                _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "WatchTorrent:downloads");
                _wakeLock?.SetReferenceCounted(false);
                _wakeLock?.Acquire();
            }

            if (_wifiLock == null && ApplicationContext?.GetSystemService(WifiService) is WifiManager wifi)
            {
#pragma warning disable CA1422 // Android 10+ da eskirgan, lekin fon yuklashda Wi-Fi'ni tejamkor rejimga o'tkazmaydi
                _wifiLock = wifi.CreateWifiLock(Android.Net.WifiMode.FullHighPerf, "WatchTorrent:downloads");
#pragma warning restore CA1422
                _wifiLock?.SetReferenceCounted(false);
                _wifiLock?.Acquire();
            }
        }

        private void ReleaseLocks()
        {
            if (_wakeLock?.IsHeld == true)
            {
                _wakeLock.Release();
            }

            if (_wifiLock?.IsHeld == true)
            {
                _wifiLock.Release();
            }

            _wakeLock = null;
            _wifiLock = null;
        }

        private static Notification BuildNotification(Context context, string title, string text, int progress)
        {
            CreateChannel(context);

            // Bildirishnoma bosilsa ilova ochiladi (yangi oyna emas — mavjudi oldinga chiqadi).
            var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
            launch?.SetFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
            var contentIntent = launch == null ? null : PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

            var builder = new NotificationCompat.Builder(context, ChannelId)
                .SetSmallIcon(Resource.Drawable.tab_downloads)!
                .SetContentTitle(title)!
                .SetContentText(text)!
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(text))!
                .SetOngoing(true)!
                .SetOnlyAlertOnce(true)!
                .SetSilent(true)!
                .SetCategory(NotificationCompat.CategoryProgress)!
                .SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate)!
                .SetContentIntent(contentIntent)!;

            builder.SetProgress(1000, Math.Max(0, progress), progress < 0);

            return builder.Build()!;
        }

        private static void CreateChannel(Context context)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26) || context.GetSystemService(NotificationService) is not NotificationManager manager)
            {
                return;
            }

            if (manager.GetNotificationChannel(ChannelId) != null)
            {
                return;
            }

            // Past ahamiyat: ovoz va tebranishsiz, faqat bildirishnomalar panelida.
            var channel = new NotificationChannel(ChannelId, "Yuklamalar", NotificationImportance.Low)
            {
                Description = "Torrent yuklanayotganini ko'rsatadi (ekran o'chganda ham yuklash davom etadi)"
            };

            manager.CreateNotificationChannel(channel);
        }
    }
}
