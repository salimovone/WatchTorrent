using Foundation;
using UIKit;
using WatchTorrent.Services;
using WatchTorrent.Views;

namespace WatchTorrent;

[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options)
    {
        base.WillConnect(scene, session, options);
        OpenInvitation(options.UrlContexts);
    }

    public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> urls) =>
        OpenInvitation(urls) || base.OpenUrl(scene, urls);

    private static bool OpenInvitation(NSSet<UIOpenUrlContext> urls)
    {
        var link = urls.ToArray().Select(context => context.Url)
            .FirstOrDefault(url => url.Scheme == InviteLink.Scheme && url.Host == InviteLink.Host);
        if (link?.AbsoluteString is not string text || Shell.Current is not AppShell shell) return false;
        if (shell.IsLoaded) _ = ImportAsync();
        else shell.Loaded += OnLoaded;
        return true;

        void OnLoaded(object? sender, EventArgs args)
        {
            shell.Loaded -= OnLoaded;
            _ = ImportAsync();
        }
        async Task ImportAsync()
        {
            try
            {
                if (shell.CurrentPage is not VoiceCallPage) await shell.GoToAsync("//connection/call");
                await shell.Call.ImportLinkAsync(text);
            }
            catch (Exception ex) { await shell.DisplayAlertAsync("Ovozli aloqa", ex.Message, "Yopish"); }
        }
    }
}
