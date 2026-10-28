using WatchTorrent.Services;
#if ANDROID
using Android.Content;
using Android.OS;
#endif

namespace WatchTorrent.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly DownloadService _downloads;
        private bool _isBatteryUnrestricted;

        public SettingsViewModel(DownloadService downloads)
        {
            _downloads = downloads;

            RequestBatteryExemptionCommand = new Command(RequestBatteryExemption);
        }

        public bool HdrOutput
        {
            get => AppSettings.HdrOutput;
            set
            {
                AppSettings.HdrOutput = value;
                OnPropertyChanged();
            }
        }

        public string HdrDescription => HdrDisplay.IsSupported
            ? "Ekraningiz HDR'ni qo'llaydi. O'chirilsa HDR videolar SDR'ga moslab (tone mapping) ko'rsatiladi. Player'dagi HDR/SDR tugmasi bilan har bir video uchun almashtirish mumkin."
            : HdrDisplay.CanSwitch
                ? "Ekraningiz HDR'ni qo'llamaydi — HDR videolar SDR'ga moslab ko'rsatiladi. Player'dagi HDR/SDR tugmasi bilan almashtirish mumkin."
                : "HDR videolar ekranga moslab (tone mapping) ko'rsatiladi.";

        public bool CanSwitchHdr => HdrDisplay.CanSwitch;

        public bool KeepScreenOnInDownloads
        {
            get => AppSettings.KeepScreenOnInDownloads;
            set
            {
                AppSettings.KeepScreenOnInDownloads = value;
                OnPropertyChanged();
            }
        }

        public bool BackgroundDownloads
        {
            get => AppSettings.BackgroundDownloads;
            set
            {
                AppSettings.BackgroundDownloads = value;
                OnPropertyChanged();
            }
        }

        public string BackgroundDownloadsDescription => DeviceInfo.Current.Platform == DevicePlatform.Android
            ? "Ekran o'chganda yoki ilova yopilganda ham yuklash davom etadi — bildirishnomada holati ko'rinadi."
            : "Yuklash davom etayotganda kompyuter uyqu rejimiga o'tmaydi.";

        /// <summary>Batareya cheklovi faqat Android'da (Samsung fon ilovalarini tez to'xtatadi).</summary>
        public bool HasBatterySetting => DeviceInfo.Current.Platform == DevicePlatform.Android;

        public bool IsBatteryUnrestricted
        {
            get => _isBatteryUnrestricted;
            private set
            {
                if (SetProperty(ref _isBatteryUnrestricted, value))
                {
                    OnPropertyChanged(nameof(BatteryStatus));
                }
            }
        }

        public string BatteryStatus => IsBatteryUnrestricted
            ? "Cheklanmagan — tizim fondagi yuklashni to'xtatmaydi."
            : "Cheklangan — tizim uzoq vaqt fonda qolgan yuklashni to'xtatib qo'yishi mumkin.";

        public Command RequestBatteryExemptionCommand { get; }

        public string DownloadDirectory => _downloads.DownloadDirectory;

        public string AppVersion => $"WatchTorrent {AppInfo.Current.VersionString}";

        /// <summary>Sahifa ochilganda: foydalanuvchi tizim sozlamalarida o'zgartirgan bo'lishi mumkin.</summary>
        public void Refresh()
        {
#if ANDROID
            var power = Platform.AppContext.GetSystemService(Context.PowerService) as PowerManager;
            IsBatteryUnrestricted = power?.IsIgnoringBatteryOptimizations(Platform.AppContext.PackageName) == true;
#endif
        }

        private void RequestBatteryExemption()
        {
#if ANDROID
            var context = Platform.CurrentActivity ?? Platform.AppContext;

            try
            {
                var intent = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations, Android.Net.Uri.Parse($"package:{context.PackageName}"));
                context.StartActivity(intent);
            }
            catch (ActivityNotFoundException)
            {
                // Ba'zi qurilmalarda bu oyna yo'q — umumiy ro'yxat ochiladi.
                context.StartActivity(new Intent(Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings));
            }
#endif
        }
    }
}
