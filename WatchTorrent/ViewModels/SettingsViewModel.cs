using L = WatchTorrent.Core.Localization.AppText;
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
        public string LanguageFlag => L.Language == "uz" ? "flag_uz.png" : "flag_en.png";
        public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
            [new("system"), new("uz"), new("en")];
        public int LanguageIndex
        {
            get => WatchTorrent.Localization.AppLanguage.Preference switch { "uz" => 1, "en" => 2, _ => 0 };
            set
            {
                if (value is < 0 or > 2 || value == LanguageIndex) return;
                WatchTorrent.Localization.AppLanguage.Select(LanguageOptions[value].Id);
                OnPropertyChanged();
            }
        }

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

        public string HdrDescription => L.Get(HdrDisplay.IsSupported
            ? L.Instance["SettingsHdrHelp"]
            : HdrDisplay.CanSwitch ? L.Get("HDR videolar ekraningizga mos rang va yorqinlikda ko'rsatiladi.")
                : L.Get("HDR videolarning rang va yorqinligi ekranga avtomatik moslanadi."));

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

        public string BackgroundDownloadsDescription => L.Get(DeviceInfo.Current.Platform == DevicePlatform.Android
            ? L.Instance["SettingsBackgroundHelp"]
            : L.Get("Yuklash davom etayotganda kompyuter uyqu rejimiga o'tmaydi."));

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

        public string BatteryStatus => L.Get(IsBatteryUnrestricted
            ? L.Get("Cheklanmagan — tizim fondagi yuklashni to'xtatmaydi.")
            : L.Get("Cheklangan — tizim uzoq vaqt fonda qolgan yuklashni to'xtatib qo'yishi mumkin."));

        public Command RequestBatteryExemptionCommand { get; }

        public string DownloadDirectory => _downloads.DownloadDirectory;
        public bool UsesCustomDirectory => _downloads.UsesCustomDirectory;

        public void SetDownloadDirectory(string path)
        {
            _downloads.SetDownloadDirectory(path);
            OnPropertyChanged(nameof(DownloadDirectory));
            OnPropertyChanged(nameof(UsesCustomDirectory));
        }

        public void RestoreDefaultDirectory()
        {
            _downloads.RestoreDefaultDirectory();
            OnPropertyChanged(nameof(DownloadDirectory));
            OnPropertyChanged(nameof(UsesCustomDirectory));
        }

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

    public sealed class LanguageOption(string id) : ObservableObject
    {
        public string Id { get; } = id;
        public string Title => Id switch
        {
            "uz" => "O‘zbekcha",
            "en" => "English",
            _ => WatchTorrent.Core.Localization.AppText.Instance["LanguageSystem"]
        };
    }
}
