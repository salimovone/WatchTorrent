using L = WatchTorrent.Core.Localization.AppText;
using WatchTorrent.Services;

namespace WatchTorrent.Views;

public partial class DirectConnectionTestPage : ContentPage
{
    private DirectPeer? peer;
    private enum Phase { Start, Busy, Waiting, Confirm, Connecting, Open, Failed }
    private Phase phase;
    public DirectConnectionTestPage() { InitializeComponent(); Clear(); }

    private void Clear(string message = "Bir telefonda offer yarating, ikkinchisida uni qabul qiling.")
    {
        var old = peer;
        peer = null;
        old?.Dispose();
        Input.Text = Output.Text = Diagnostic.Text = "";
        StatusLabel.Text = L.Get(message);
        phase = Phase.Start;
        Refresh();
    }

    private void Refresh()
    {
        Create.IsVisible = phase == Phase.Start;
        Input.IsVisible = Apply.IsVisible = phase is Phase.Start or Phase.Waiting;
        Input.Placeholder = phase == Phase.Waiting ? L.Get("Answer matnini joylang") : L.Get("Offer matnini joylang");
        Apply.Text = phase == Phase.Waiting ? L.Get("Answer qo‘llash") : L.Get("Offer qabul qilish");
        Copy.IsVisible = Output.IsVisible = !string.IsNullOrEmpty(Output.Text);
        Confirm.IsVisible = phase == Phase.Confirm;
        Ping.IsEnabled = peer?.Connected == true;
        if (peer != null) Diagnostic.Text = peer.Diagnostics();
    }

    private DirectPeer Begin()
    {
        var current = peer = new DirectPeer(false);
        current.Changed += () =>
        {
            if (peer != current) return;
            if (current.Connected && phase != Phase.Open) { phase = Phase.Open; StatusLabel.Text = L.Get("Kanal ochildi. Ping yuboring."); }
            Refresh();
        };
        current.Ended += reason => { if (peer == current) Fail(reason); };
        return current;
    }

    private void Fail(string reason)
    {
        if (peer != null) Diagnostic.Text = peer.Diagnostics();
        var old = peer;
        peer = null;
        old?.Dispose();
        phase = Phase.Failed;
        StatusLabel.Text = reason;
        Refresh();
    }

    private async Task Describe(SignalFile? remote)
    {
        var current = Begin();
        phase = Phase.Busy;
        StatusLabel.Text = L.Get("ICE manzillari yig‘ilmoqda…");
        Refresh();
        try
        {
            var description = await current.DescribeAsync(remote);
            if (peer != current) return;
            Output.Text = description.Encode();
            Input.Text = "";
            phase = remote == null ? Phase.Waiting : Phase.Confirm;
            StatusLabel.Text = remote == null ? L.Get("Offer’ni yuboring, kelgan answer’ni joylang.") : L.Get("Answer’ni yuboring. U qo‘llangach, tasdiqlang.");
            Refresh();
        }
        catch (Exception ex) { if (peer == current) Fail(ex.Message); }
    }

    private async void CreateOffer(object? sender, EventArgs e)
    {
        try { await Describe(null); } catch (Exception ex) { Fail(ex.Message); }
    }
    private async void ApplyRemote(object? sender, EventArgs e)
    {
        try
        {
            var file = SignalFile.Parse(Input.Text ?? "", phase == Phase.Waiting ? "answer" : "offer");
            if (phase == Phase.Start) await Describe(file);
            else { peer!.Apply(file); await WaitForConnection(); }
        }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    private async Task WaitForConnection()
    {
        var current = peer!;
        phase = Phase.Connecting;
        Refresh();
        try { await current.WaitAsync(); if (peer == current) { phase = Phase.Open; Refresh(); } }
        catch (Exception) { if (peer == current) Fail(L.Get("Ulanish vaqti tugadi. Bir Wi-Fi tarmog‘ida qayta urinib ko‘ring.")); }
    }
    private async void ConfirmAnswer(object? sender, EventArgs e) => await WaitForConnection();
    private async void SendPing(object? sender, EventArgs e)
    {
        var current = peer;
        if (current == null) return;
        try { var text = await current.PingAsync(); if (peer == current) StatusLabel.Text = text; }
        catch (Exception ex) { if (peer == current) StatusLabel.Text = ex.Message; }
    }
    private async void CopySignal(object? sender, EventArgs e) => await Clipboard.Default.SetTextAsync(Output.Text);
    private void Reset(object? sender, EventArgs e) => Clear();
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args) { Clear(); base.OnNavigatedFrom(args); }
}
