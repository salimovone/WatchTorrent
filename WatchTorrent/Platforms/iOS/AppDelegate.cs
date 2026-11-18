using Foundation;

namespace WatchTorrent;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    [Export("application:supportedInterfaceOrientationsForWindow:")]
    public UIKit.UIInterfaceOrientationMask GetSupportedInterfaceOrientations(
        UIKit.UIApplication application, UIKit.UIWindow? forWindow) => WatchTorrent.Services.PlayerWindow.SupportedOrientations;
}
