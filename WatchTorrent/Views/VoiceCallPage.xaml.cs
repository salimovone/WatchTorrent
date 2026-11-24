using L = WatchTorrent.Core.Localization.AppText;
using WatchTorrent.Services;

namespace WatchTorrent.Views;

public partial class VoiceCallPage : ContentPage, WatchTorrent.Core.Localization.ILocalized
{
    private VoiceCall call = null!;
    public VoiceCallPage()
    {
        InitializeComponent();
        WatchTorrent.Core.Localization.AppText.Register(this);
    }
    void WatchTorrent.Core.Localization.ILocalized.LanguageChanged() { if (call != null) Render(); }

    internal void Bind(VoiceCall service)
    {
        call = service;
        call.Changed += Render;
        Render();
    }

    private void Render()
    {
        ConnectionState.Text = call.Connected ? L.Get("Ulangan") : call.Active ? L.Get("Ulanmoqda") : L.Get("Ulanmagan");
        Indicator.Fill = new SolidColorBrush(call.Connected ? Color.FromArgb("#b6ff2e") : call.Active ? Colors.Orange : Colors.Gray);
        StatusLabel.Text = call.Status;
        Offer.IsVisible = Pick.IsVisible = !call.Active && !call.Pending;
        Accept.IsVisible = call.Pending;
        Hangup.IsVisible = call.Active || call.Pending;
        Hangup.Text = call.Connected ? L.Get("Suhbatni tugatish") : L.Get("Bekor qilish");
        Mic.IsVisible = call.Connected;
        Mic.Text = call.Microphone ? L.Get("Mikrofon · Yoqilgan") : L.Get("Mikrofon · O‘chiq");
    }

    private async void CreateOffer(object? sender, EventArgs e) => await call.StartAsync(false);
    private async void AcceptOffer(object? sender, EventArgs e) => await call.StartAsync(true);
    private void EndCall(object? sender, EventArgs e) => call.End();
    private async void ToggleMic(object? sender, EventArgs e)
    {
        Mic.IsEnabled = false;
        try { await call.ToggleMicrophoneAsync(); }
        finally { Mic.IsEnabled = true; }
    }

    private async void OpenLink(object? sender, EventArgs e)
    {
        try
        {
            var link = await DisplayPromptAsync(L.Get("Taklif havolasi"), L.Get("Suhbatdoshingiz yuborgan havolani joylang."),
                L.Get("Ochish"), L.Get("Bekor qilish"), "https://…", maxLength: 32768);
            if (!string.IsNullOrWhiteSpace(link)) await call.ImportLinkAsync(link);
        }
        catch (Exception ex) { await DisplayAlertAsync(L.Get("Ulanish"), ex.Message, L.Get("Yopish")); }
    }
}
