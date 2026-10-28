#if ANDROID
using Android.Content;
using Android.Hardware.Display;
#endif

namespace WatchTorrent.Services
{
    /// <summary>
    /// Ekran HDR'ni ko'rsata oladimi va HDR video qanday chiqariladi. Android'da HDR video MediaCodec orqali
    /// to'g'ridan-to'g'ri ekranga beriladi (HDR'ni displey ko'rsatadi; Samsung'da HDR rejimi ham yoqiladi), SDR'da esa
    /// VLC OpenGL orqali chizib, libplacebo bilan SDR'ga moslaydi (tone mapping). Windows'da VLC har doim o'zi moslaydi.
    /// </summary>
    public static class HdrDisplay
    {
        /// <summary>HDR/SDR ni player'da almashtirish mumkin (faqat Android'da ikkala yo'l bor).</summary>
        public static bool CanSwitch => DeviceInfo.Current.Platform == DevicePlatform.Android;

        /// <summary>Ekran HDR10/HLG/Dolby Vision'dan birini qo'llaydi.</summary>
        public static bool IsSupported => Supported.Value;

        private static readonly Lazy<bool> Supported = new(() =>
        {
#if ANDROID
            // Samsung (S8/S9/Note8...) HDR'ni standart API'da e'lon qilmaydi — o'z HDR xizmati bor.
            if (SamsungHdrMode.IsSupported)
            {
                return true;
            }

            try
            {
                var display = (Platform.AppContext.GetSystemService(Context.DisplayService) as DisplayManager)?.GetDisplay(0);
#pragma warning disable CA1422 // Android 14+ da Display.Mode orqali, lekin eski API ham ishlaydi
                return display?.GetHdrCapabilities()?.GetSupportedHdrTypes() is { Length: > 0 };
#pragma warning restore CA1422
            }
            catch (Exception)
            {
                return false;
            }
#else
            return false;
#endif
        });

        /// <summary>HDR video uchun boshlang'ich rejim: sozlamada yoqilgan va ekran qo'llasa — HDR.</summary>
        public static bool PreferHdrOutput => CanSwitch && IsSupported && AppSettings.HdrOutput;
    }
}
