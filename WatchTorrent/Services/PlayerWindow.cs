#if ANDROID
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Views;
using AndroidX.Core.View;
using Google.Android.Material.BottomNavigation;
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

        private const string ScreenReason = "player";

        public static void Enter()
        {
            ScreenAwake.Set(ScreenReason, true);
            IsActive = true;

#if ANDROID
            if (Platform.CurrentActivity is not { Window: { } window } activity)
            {
                return;
            }

            _previousOrientation = activity.RequestedOrientation;
            _previousConfiguration = activity.Resources?.Configuration?.Orientation;
            if (!OperatingSystem.IsAndroidVersionAtLeast(30))
                _previousSystemUi = window.DecorView.SystemUiFlags;
            var controller = WindowCompat.GetInsetsController(window, window.DecorView);
            _previousLightStatusBars = controller?.AppearanceLightStatusBars ?? false;
            _previousLightNavigationBars = controller?.AppearanceLightNavigationBars ?? false;
            _navigationPadding.Clear();
            CaptureNavigationPadding(window.DecorView);
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
            ScreenAwake.Set(ScreenReason, false);
            IsActive = false;

#if ANDROID
            if (Platform.CurrentActivity is not { Window: { } window } activity)
            {
                return;
            }

            WindowCompat.SetDecorFitsSystemWindows(window, !OperatingSystem.IsAndroidVersionAtLeast(35));
            WindowCompat.GetInsetsController(window, window.DecorView)?.Show(WindowInsetsCompat.Type.SystemBars());
            if (!OperatingSystem.IsAndroidVersionAtLeast(30))
                window.DecorView.SystemUiFlags = _previousSystemUi;
            if (WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
            {
                controller.AppearanceLightStatusBars = _previousLightStatusBars;
                controller.AppearanceLightNavigationBars = _previousLightNavigationBars;
            }
            var rotate = Android.Provider.Settings.System.GetInt(activity.ContentResolver, "accelerometer_rotation", 0) != 0;
            activity.RequestedOrientation = !rotate && _previousOrientation is ScreenOrientation.Unspecified or ScreenOrientation.User
                ? (_previousConfiguration == Android.Content.Res.Orientation.Portrait ? ScreenOrientation.Portrait : ScreenOrientation.Landscape)
                : _previousOrientation;

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
            RefreshAppLayout();
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
            else if (hasFocus)
            {
                RefreshAppLayout();
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

        private static ScreenOrientation _previousOrientation;
        private static Android.Content.Res.Orientation? _previousConfiguration;
        private static SystemUiFlags _previousSystemUi;
        private static bool _previousLightStatusBars, _previousLightNavigationBars;
        private static readonly List<(WeakReference<BottomNavigationView> View, int Left, int Top, int Right, int Bottom)> _navigationPadding = [];

        private static void CaptureNavigationPadding(Android.Views.View view)
        {
            if (view is BottomNavigationView nav)
                _navigationPadding.Add((new(nav), nav.PaddingLeft, nav.PaddingTop, nav.PaddingRight, nav.PaddingBottom));
            if (view is not ViewGroup group) return;
            for (int i = 0; i < group.ChildCount; i++)
                if (group.GetChildAt(i) is { } child) CaptureNavigationPadding(child);
        }

        public static void RefreshAppLayout()
        {
            if (IsActive || Platform.CurrentActivity is not { Window: { } window } activity) return;
            window.DecorView.Post(() =>
            {
                if (IsActive) return;
                // Android 15+ enforces edge-to-edge. Older versions need their
                // normal fitted window restored after startup/fullscreen playback.
                WindowCompat.SetDecorFitsSystemWindows(window, !OperatingSystem.IsAndroidVersionAtLeast(35));
                ViewCompat.RequestApplyInsets(window.DecorView);
                if (activity.Resources?.Configuration?.Orientation == _previousConfiguration)
                    foreach (var padding in _navigationPadding)
                        if (padding.View.TryGetTarget(out var nav) && nav.IsAttachedToWindow)
                        {
                            nav.SetPadding(padding.Left, padding.Top, padding.Right, padding.Bottom);
                            nav.RequestLayout();
                        }
                window.DecorView.RequestLayout();
            });
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
