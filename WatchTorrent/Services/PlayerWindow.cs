#if ANDROID
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Views;
using AndroidX.Core.View;
#elif WINDOWS
using Microsoft.UI.Windowing;
#endif

namespace WatchTorrent.Services
{
    /// <summary>
    /// Film ko'rish rejimi: ekran o'chmaydi; Android'da landshaft + tizim panellari yashiriladi,
    /// kompyuterda oyna to'liq ekranga o'tadi.
    /// </summary>
    public static class PlayerWindow
    {
        /// <summary>To'liq ekranni qo'lda yoqib-o'chirish faqat kompyuterda ma'noli.</summary>
        public static bool CanToggleFullScreen => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop;

        /// <summary>Yorug'lik va ovozni ekranda barmoq bilan surib boshqarish (VLC'dagi kabi) — faqat Android'da.</summary>
        public static bool CanAdjustLevels => DeviceInfo.Current.Platform == DevicePlatform.Android;

        private static bool IsActive { get; set; }

        public static void Enter()
        {
            DeviceDisplay.Current.KeepScreenOn = true;
            IsActive = true;

#if ANDROID
            if (Platform.CurrentActivity is not { Window: { } window } activity)
            {
                return;
            }

            activity.RequestedOrientation = ScreenOrientation.SensorLandscape;

            WindowCompat.SetDecorFitsSystemWindows(window, false);
            HideSystemBars(window);

            // Kamera "kesik"i bo'lgan ekranlarda video butun ekranni egallasin.
            if (OperatingSystem.IsAndroidVersionAtLeast(28) && window.Attributes is { } attributes)
            {
                attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
                window.Attributes = attributes;
            }
#elif WINDOWS
            SetFullScreen(true);
#endif
        }

        public static void Exit()
        {
            DeviceDisplay.Current.KeepScreenOn = false;
            IsActive = false;

#if ANDROID
            if (Platform.CurrentActivity is not { Window: { } window } activity)
            {
                return;
            }

            activity.RequestedOrientation = ScreenOrientation.Unspecified;

            WindowCompat.SetDecorFitsSystemWindows(window, true);
            WindowCompat.GetInsetsController(window, window.DecorView)?.Show(WindowInsetsCompat.Type.SystemBars());

            if (window.Attributes is { } attributes)
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(28))
                {
                    attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Default;
                }

                // Player'da o'zgartirilgan yorug'lik faqat film uchun — ilovaning qolgan qismida tizimniki.
                attributes.ScreenBrightness = WindowManagerLayoutParams.BrightnessOverrideNone;
                window.Attributes = attributes;
            }

            _brightnessBeforePictureInPicture = null;
#elif WINDOWS
            SetFullScreen(false);
#endif
        }

        /// <summary>Ekran yorug'ligi (0..1) — faqat player oynasi uchun, <see cref="Exit"/> da tizimnikiga qaytadi.</summary>
        public static double Brightness
        {
            get
            {
#if ANDROID
                if (Platform.CurrentActivity?.Window?.Attributes is { ScreenBrightness: >= 0 } attributes)
                {
                    return attributes.ScreenBrightness;
                }

                // Oyna uchun hali o'rnatilmagan — tizim sozlamasidan (0..255).
                var resolver = Platform.AppContext.ContentResolver;
                var system = Android.Provider.Settings.System.GetInt(resolver, Android.Provider.Settings.System.ScreenBrightness, 128);

                return Math.Clamp(system / 255.0, 0, 1);
#else
                return 1;
#endif
            }
            set
            {
#if ANDROID
                if (Platform.CurrentActivity?.Window is { Attributes: { } attributes } window)
                {
                    // 0 ba'zi qurilmalarda ekranni butunlay o'chiradi.
                    attributes.ScreenBrightness = (float)Math.Clamp(value, 0.01, 1);
                    window.Attributes = attributes;
                }
#endif
            }
        }

        /// <summary>Tizimning media ovozi (0..1) — telefon tugmalari boshqaradigan o'sha ovoz.</summary>
        public static double Volume
        {
            get
            {
#if ANDROID
                if (Audio is { } audio && audio.GetStreamMaxVolume(Android.Media.Stream.Music) is > 0 and var max)
                {
                    return audio.GetStreamVolume(Android.Media.Stream.Music) / (double)max;
                }
#endif
                return 1;
            }
            set
            {
#if ANDROID
                var audio = Audio;
                var max = audio?.GetStreamMaxVolume(Android.Media.Stream.Music) ?? 0;

                if (audio == null || max <= 0)
                {
                    return;
                }

                var index = (int)Math.Round(Math.Clamp(value, 0, 1) * max);

                if (index == audio.GetStreamVolume(Android.Media.Stream.Music))
                {
                    return;
                }

                try
                {
                    // Tizimning ovoz paneli chiqmaydi — player o'z ko'rsatkichini chizadi.
                    audio.SetStreamVolume(Android.Media.Stream.Music, index, 0);
                }
                catch (Java.Lang.SecurityException)
                {
                    // "Bezovta qilmang" rejimi ovozni o'zgartirishga ruxsat bermasligi mumkin.
                }
#endif
            }
        }

#if ANDROID
        private static float? _brightnessBeforePictureInPicture;

        private static AudioManager? Audio => Platform.AppContext.GetSystemService(Context.AudioService) as AudioManager;

        /// <summary>
        /// Dialog (audio, subtitr, tezlik tanlash...) ochilganda Android tizim panellarini ko'rsatadi va yopilgach
        /// pastdagi navigatsiya paneli qaytib yashirinmay qoladi. Oyna fokusni qaytarib olganda (MainActivity) chaqiriladi.
        /// </summary>
        public static void OnWindowFocusChanged(bool hasFocus)
        {
            if (hasFocus && IsActive && Platform.CurrentActivity?.Window is { } window)
            {
                HideSystemBars(window);
            }
        }

        /// <summary>
        /// Kichik oyna (PiP) boshqa ilovalar ustida turadi va player'da o'zgartirilgan yorug'lik butun ekranga ta'sir
        /// qilib qoladi — kichik oynada tizimniki qaytariladi, katta ekranga qaytganda yana qo'yiladi.
        /// </summary>
        public static void OnPictureInPictureChanged(bool isActive)
        {
            if (!IsActive || Platform.CurrentActivity?.Window is not { Attributes: { } attributes } window)
            {
                return;
            }

            if (isActive)
            {
                _brightnessBeforePictureInPicture = attributes.ScreenBrightness;
                attributes.ScreenBrightness = WindowManagerLayoutParams.BrightnessOverrideNone;
            }
            else
            {
                attributes.ScreenBrightness = _brightnessBeforePictureInPicture ?? WindowManagerLayoutParams.BrightnessOverrideNone;
                _brightnessBeforePictureInPicture = null;
                HideSystemBars(window);
            }

            window.Attributes = attributes;
        }

        private static void HideSystemBars(Android.Views.Window window)
        {
            if (WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
            {
                controller.Hide(WindowInsetsCompat.Type.SystemBars());
                controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            }
        }
#endif

        public static bool IsFullScreen
        {
            get
            {
#if WINDOWS
                return AppWindow?.Presenter.Kind == AppWindowPresenterKind.FullScreen;
#else
                return false;
#endif
            }
        }

        public static void ToggleFullScreen()
        {
#if WINDOWS
            SetFullScreen(!IsFullScreen);
#endif
        }

#if WINDOWS
        private static AppWindow? AppWindow =>
            (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow;

        private static void SetFullScreen(bool fullScreen)
        {
            AppWindow?.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        }
#endif
    }
}
