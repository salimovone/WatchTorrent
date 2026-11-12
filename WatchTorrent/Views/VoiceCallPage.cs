using System.Text.Json;
using WatchTorrent.Services;
#if ANDROID
using Microsoft.Maui.Platform;
#endif

namespace WatchTorrent.Views;

#if ANDROID
internal sealed class CallSurface : View { }

internal sealed class CallSurfaceHandler() : Microsoft.Maui.Handlers.ViewHandler<CallSurface, Android.Widget.FrameLayout>(ViewMapper)
{
    protected override Android.Widget.FrameLayout CreatePlatformView() => new(Context);
}
#endif

public sealed class VoiceCallPage : ContentPage
{
    private const string Origin = "https://voice.watchtorrent.invalid/";
    internal const string InviteMime = "application/vnd.watchtorrent.voice";
    private readonly WebView browser = new();
    private bool choosing;
    private int navigation;
    private CancellationTokenSource? exchange;
    private string? exchangeId;

    public VoiceCallPage()
    {
        Title = "Ulanish";
        Shell.SetNavBarIsVisible(this, false);
        browser.Navigating += OnBrowserNavigating;
#if ANDROID
        // Shell may recycle a tab's native layout. Keep the call WebView alive across those layouts.
        var host = new CallSurface();
        Content = host;
        host.HandlerChanged += (_, _) =>
        {
            if (host.Handler?.PlatformView is not Android.Views.ViewGroup container) return;
            var native = browser.ToPlatform(host.Handler.MauiContext!);
            if (native.Parent == container) return;
            (native.Parent as Android.Views.ViewGroup)?.RemoveView(native);
            container.AddView(native, new Android.Views.ViewGroup.LayoutParams(-1, -1));
        };
        browser.HandlerChanged += (_, _) =>
        {
            if (browser.Handler?.PlatformView is Android.Webkit.WebView view)
            {
                view.Settings.MediaPlaybackRequiresUserGesture = false;
                view.SetWebChromeClient(new MicrophoneClient());
            }
        };
#else
        Content = browser;
#endif
    }

    private async void OnBrowserNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url == Origin || e.Url == "about:blank" || e.Url.StartsWith("data:text/html", StringComparison.Ordinal)) return;
        e.Cancel = true;
        if (e.Url == Origin + "stop-reply/" + exchangeId)
        {
            exchange?.Cancel();
            return;
        }
        if (e.Url == Origin + "auto-reply")
        {
            await ExchangeAnswerAsync(true);
            return;
        }
        if (choosing || (e.Url != Origin + "share" && e.Url != Origin + "pick")) return;
        choosing = true;
        var visit = navigation;
        try
        {
            if (e.Url == Origin + "pick")
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Ovozli aloqa faylini ochish" });
                if (file == null || visit != navigation) return;
                using var stream = await file.OpenReadAsync();
                await ImportAsync(stream);
            }
            else
            {
                var encoded = await browser.EvaluateJavaScriptAsync(
                    "encodeURIComponent(JSON.stringify(outgoingInvite()))");
                var json = Uri.UnescapeDataString(encoded);
                using var payload = JsonDocument.Parse(json);
                if (payload.RootElement.GetProperty("type").GetString() != "offer") return;
                var id = payload.RootElement.GetProperty("callId").GetString();
                if (exchangeId != id || exchange == null)
                    _ = ExchangeAnswerAsync(false);
                var directory = Directory.CreateDirectory(Path.Combine(FileSystem.CacheDirectory, "sharing-root"));
                var path = Path.Combine(directory.FullName, $"Taklif-{id}.wtcall");
                await File.WriteAllTextAsync(path, json);
                if (visit == navigation)
                    await Share.Default.RequestAsync(new ShareFileRequest("Taklif yuborish", new ShareFile(path, InviteMime)));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (visit == navigation) await DisplayAlertAsync("Ovozli aloqa", "Faylni ochish yoki ulashib bo‘lmadi. Qaytadan urinib ko‘ring.", "Yopish");
        }
        finally { choosing = false; }
    }

    private async Task ExchangeAnswerAsync(bool send)
    {
        var visit = navigation;
        CancellationTokenSource? operation = null;
        string? id = null;
        try
        {
            var encoded = await browser.EvaluateJavaScriptAsync(
                "encodeURIComponent(JSON.stringify({key:active.replyKey,file:outgoingInvite()}))");
            using var envelope = JsonDocument.Parse(Uri.UnescapeDataString(encoded));
            var file = envelope.RootElement.GetProperty("file");
            id = file.GetProperty("callId").GetString();
            var key = Convert.FromBase64String(envelope.RootElement.GetProperty("key").GetString()!);
            if (visit != navigation || id?.Length != 32 || !id.All(Uri.IsHexDigit) || key.Length != 32) return;
            exchange?.Cancel();
            operation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            exchange = operation;
            exchangeId = id;
            if (send)
            {
                await VoiceAnswerExchange.SendAsync(key, id, file.GetRawText(), operation.Token);
                if (!operation.IsCancellationRequested) await browser.EvaluateJavaScriptAsync($"autoReplyStatus('{id}',true)");
            }
            else
            {
                var answer = await VoiceAnswerExchange.WaitAsync(key, id, operation.Token);
                if (!operation.IsCancellationRequested)
                    await browser.EvaluateJavaScriptAsync($"if(active?.callId==='{id}' && phase==='answer') importInvite({answer},true)");
            }
        }
        catch (Exception)
        {
            if (visit == navigation && exchange == operation && id?.Length == 32 && id.All(Uri.IsHexDigit))
            {
                if (send) await browser.EvaluateJavaScriptAsync($"autoReplyStatus('{id}',false)");
                else await browser.EvaluateJavaScriptAsync($"if(active?.callId==='{id}' && phase==='answer') fail(active,'Taklif muddati tugadi. Yangi taklif yuboring.')");
            }
        }
        finally
        {
            if (exchange == operation) { exchange = null; exchangeId = null; }
            operation?.Dispose();
        }
    }

    internal async Task ImportAsync(Stream stream)
    {
        var bytes = new byte[65537];
        var count = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count == bytes.Length) throw new InvalidDataException();
        using var payload = JsonDocument.Parse(bytes.AsMemory(0, count));
        var json = JsonSerializer.Serialize(payload.RootElement);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            if (browser.Handler != null && await browser.EvaluateJavaScriptAsync(
                "document.readyState === 'complete' && typeof importInvite === 'function' ? 'ready' : 'loading'") == "ready")
            {
                await browser.EvaluateJavaScriptAsync("importInvite(" + json + ")");
                return;
            }
            await Task.Delay(100, timeout.Token);
        }
        throw new TimeoutException();
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (browser.Source is HtmlWebViewSource)
        {
            if (browser.Handler != null)
            {
                var connected = await browser.EvaluateJavaScriptAsync("typeof controls === 'function' && (controls(), document.title === 'Ulangan') ? 'on' : 'off'");
                (Shell.Current as AppShell)?.ShowConnection(connected == "on");
            }
            return;
        }
        var visit = ++navigation;
        try
        {
            var html = await ReadAsset("voice-call.html");
            var script = await ReadAsset("direct-connection.js");
            if (navigation == visit)
                browser.Source = new HtmlWebViewSource { BaseUrl = Origin, Html = html.Replace("/* signaling */", script) };
        }
        catch (IOException)
        {
            if (navigation == visit) Content = new Label { Text = "Ovozli aloqa sahifasini yuklab bo‘lmadi." };
        }
    }

    private static async Task<string> ReadAsset(string name)
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync(name);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

#if ANDROID
    private sealed class MicrophoneClient : Android.Webkit.WebChromeClient
    {
        private Android.Webkit.PermissionRequest? pending;

        public override void OnReceivedTitle(Android.Webkit.WebView? view, string? title)
        {
            base.OnReceivedTitle(view, title);
            if (title is "Ovozli aloqa" or "Ulangan")
                (Shell.Current as AppShell)?.ShowConnection(title == "Ulangan");
        }

        public override async void OnPermissionRequest(Android.Webkit.PermissionRequest? request)
        {
            if (request == null) return;
            var audio = Android.Webkit.PermissionRequest.ResourceAudioCapture!;
            if (request.Origin?.ToString()?.TrimEnd('/') != Origin.TrimEnd('/') ||
                request.GetResources()?.Contains(audio) != true || pending != null)
            {
                request.Deny();
                return;
            }

            pending = request;
            var granted = false;
            try { granted = await Permissions.RequestAsync<Permissions.Microphone>() == PermissionStatus.Granted; }
            catch (PermissionException) { }
            if (pending != request) return;
            pending = null;
            if (granted) request.Grant([audio]);
            else request.Deny();
        }

        public override void OnPermissionRequestCanceled(Android.Webkit.PermissionRequest? request)
        {
            if (pending == request) pending = null;
        }
    }
#endif
}
