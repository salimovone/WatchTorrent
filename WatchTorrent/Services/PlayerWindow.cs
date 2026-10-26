#if ANDROID
using Android.Content.PM;
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

        public static void Enter()
        {
            DeviceDisplay.Current.KeepScreenOn = true;

#if ANDROID
            if (Platform.CurrentActivity is not { Window: { } window } activity)
            {
                return;
            }

            activity.RequestedOrientation = ScreenOrientation.SensorLandscape;

            WindowCompat.SetDecorFitsSystemWindows(window, false);

            if (WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
            {
                controller.Hide(WindowInsetsCompat.Type.SystemBars());
                controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            }

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

#if ANDROID
            if (Platform.CurrentActivity is not { Window: { } window } activity)
            {
                return;
            }

            activity.RequestedOrientation = ScreenOrientation.Unspecified;

            WindowCompat.SetDecorFitsSystemWindows(window, true);
            WindowCompat.GetInsetsController(window, window.DecorView)?.Show(WindowInsetsCompat.Type.SystemBars());

            if (OperatingSystem.IsAndroidVersionAtLeast(28) && window.Attributes is { } attributes)
            {
                attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Default;
                window.Attributes = attributes;
            }
#elif WINDOWS
            SetFullScreen(false);
#endif
        }

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
