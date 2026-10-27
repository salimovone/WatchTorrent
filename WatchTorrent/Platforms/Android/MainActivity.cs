using Android.App;
using Android.Content.PM;
using Android.OS;
using WatchTorrent.Services;

namespace WatchTorrent
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            // Player ochiq bo'lsa, dialog yopilgach qaytib chiqqan tizim panellari yana yashiriladi.
            PlayerWindow.OnWindowFocusChanged(hasFocus);
        }
    }
}
