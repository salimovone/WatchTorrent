using System.Runtime.Versioning;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using WatchTorrent.Services;

namespace WatchTorrent
{
    // SupportsPictureInPicture: player kichik oynada (PiP) o'ynay oladi. ConfigurationChanges'dagi ScreenSize/ScreenLayout
    // tufayli kichik oynaga o'tishda activity qayta yaratilmaydi.
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, SupportsPictureInPicture = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            // Player ochiq bo'lsa, dialog yopilgach qaytib chiqqan tizim panellari yana yashiriladi.
            PlayerWindow.OnWindowFocusChanged(hasFocus);
        }

        protected override void OnUserLeaveHint()
        {
            base.OnUserLeaveHint();

            // Film o'ynab turganda "Home" bosildi — kichik oynaga o'tiladi.
            PictureInPicture.OnUserLeaveHint();
        }

        [SupportedOSPlatform("android26.0")]
        public override void OnPictureInPictureModeChanged(bool isInPictureInPictureMode, Configuration? newConfig)
        {
            base.OnPictureInPictureModeChanged(isInPictureInPictureMode, newConfig);

            PictureInPicture.OnModeChanged(isInPictureInPictureMode);
        }
    }
}
