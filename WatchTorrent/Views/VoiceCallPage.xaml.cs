using WatchTorrent.Services;

namespace WatchTorrent.Views;

public partial class VoiceCallPage : ContentPage
{
    private VoiceCall call = null!;
    public VoiceCallPage() => InitializeComponent();

    internal void Bind(VoiceCall service)
    {
        call = service;
        call.Changed += Render;
        Render();
    }

    private void Render()
    {
        ConnectionState.Text = call.Connected ? "Ulangan" : call.Active ? "Ulanmoqda" : "Ulanmagan";
        Indicator.Fill = new SolidColorBrush(call.Connected ? Color.FromArgb("#b6ff2e") : call.Active ? Colors.Orange : Colors.Gray);
        StatusLabel.Text = call.Status;
        Offer.IsVisible = Pick.IsVisible = !call.Active && !call.Pending;
        Accept.IsVisible = call.Pending;
        Hangup.IsVisible = call.Active || call.Pending;
        Hangup.Text = call.Connected ? "Suhbatni tugatish" : "Bekor qilish";
        Mic.IsVisible = call.Connected;
        Mic.Text = call.Microphone ? "Mikrofon · Yoqilgan" : "Mikrofon · O‘chiq";
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
            var link = await DisplayPromptAsync("Taklif havolasi", "Suhbatdoshingiz yuborgan havolani joylang.",
                "Ochish", "Bekor qilish", "watchtorrent://join/…", maxLength: 32768);
            if (!string.IsNullOrWhiteSpace(link)) call.ImportLink(link);
        }
        catch (Exception ex) { await DisplayAlertAsync("Ulanish", ex.Message, "Yopish"); }
    }
}
