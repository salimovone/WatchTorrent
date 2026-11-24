using L = WatchTorrent.Core.Localization.AppText;
using System.Security.Cryptography;

namespace WatchTorrent.Services;

internal sealed class VoiceCall
{
    public const string InviteMime = "application/vnd.watchtorrent.voice";
    private const string Format = "watchtorrent-voice-1";
    private SignalFile? invitation;
    private DirectPeer? peer;
    public event Action? Changed;
    public event Action<MovieInvite, CancellationToken>? MovieReceived;
    public event Action<WatchMessage>? ControlReceived;
    public CancellationToken Token => peer?.Token ?? new CancellationToken(true);
    public void Send(WatchMessage message) => peer?.Send(message);
    public void Invite(MovieInvite movie) => peer?.Invite(movie);
    public bool Pending => invitation != null && peer == null;
    public bool Active => peer != null;
    public bool Connected => peer?.Connected == true;
    public bool Microphone => peer?.Microphone == true;
    private string status = "Taklif havolasini yuboring yoki kelgan havolani oching.";
    public string Status { get => L.Get(status); private set => status = value; }

    private void SetStatus(string text) { Status = text; Changed?.Invoke(); }

    public async Task ImportAsync(Stream stream)
    {
        var bytes = new byte[65537];
        int length = await stream.ReadAtLeastAsync(bytes, bytes.Length, false);
        if (length == bytes.Length) throw new InvalidDataException(L.Get("Fayl juda katta."));
        ImportOffer(System.Text.Encoding.UTF8.GetString(bytes, 0, length));
    }

    public async Task ImportLinkAsync(string link) => ImportOffer(await InviteLink.ReadAsync(link));

    private void ImportOffer(string text)
    {
        var file = SignalFile.Parse(text, "offer");
        if (file.Format != Format || !ValidId(file.CallId) || file.ReplyKey == null ||
            !Convert.TryFromBase64String(file.ReplyKey, new byte[32], out var size) || size != 32 ||
            (file.AudioEngine == null && !file.Sdp.Contains("m=audio ")) || !file.Sdp.Contains("m=application "))
            throw new InvalidDataException(L.Get("Taklif eskirgan yoki buzilgan. Yangi taklif so‘rang."));
        if (file.AudioEngine != DirectPeer.VoiceEngine)
            throw new InvalidDataException(L.Get("Ovoz dvigatellari mos kelmadi. Ikkala qurilmaga bir xil build o‘rnating."));
        if (Active) { SetStatus(L.Get("Avval joriy suhbatni tugating.")); return; }
        invitation = file;
        SetStatus(L.Get("Ovozli suhbatga taklif keldi."));
    }

    private static bool ValidId(string? id) => id?.Length == 32 && id.All(Uri.IsHexDigit);

    public async Task StartAsync(bool accept)
    {
        if (Active || (accept && !Pending)) return;
        var invite = invitation;
        invitation = null;
        DirectPeer? current = null;
        try
        {
            current = peer = new DirectPeer(true);
            current.ControlReceived += control => { if (peer == current) ControlReceived?.Invoke(control); };
            current.MovieReceived += movie =>
            {
                if (peer == current) MovieReceived?.Invoke(movie, current.Token);
            };
            current.Changed += () => { if (peer == current) Changed?.Invoke(); };
            current.Ended += reason => { if (peer == current) End(reason); };
            SetStatus(L.Get("Ulanish tayyorlanmoqda…"));
            var local = await current.DescribeAsync(accept ? invite : null);
            current.Token.ThrowIfCancellationRequested();
            if (accept)
            {
                var answer = local with { Format = Format, CallId = invite!.CallId };
                SetStatus(L.Get("Suhbatdoshingizga ulanmoqda…"));
                await VoiceAnswerExchange.SendAsync(Convert.FromBase64String(invite.ReplyKey!), invite.CallId!, answer.Encode(), current.Token);
            }
            else
            {
                var key = RandomNumberGenerator.GetBytes(32);
                var secret = RandomNumberGenerator.GetBytes(16);
                string id = InviteLink.CallId(secret);
                var file = local with { Format = Format, CallId = id, ReplyKey = Convert.ToBase64String(key) };
                var link = await InviteLink.CreateAsync(secret, file.Encode(), current.Token);
                using var expiration = CancellationTokenSource.CreateLinkedTokenSource(current.Token);
                expiration.CancelAfter(TimeSpan.FromMinutes(10));
                SetStatus(L.Get("Taklif qabul qilinishi kutilmoqda."));
                var waiting = VoiceAnswerExchange.WaitAsync(key, id, expiration.Token);
                try
                {
                    await Share.Default.RequestAsync(new ShareTextRequest { Title = L.Get("Taklif havolasini yuborish"), Text = link });
                    var answer = SignalFile.Parse(await waiting, "answer");
                    current.Token.ThrowIfCancellationRequested();
                    if (answer.CallId != id) throw new InvalidDataException(L.Get("Taklif mos kelmadi."));
                    current.Apply(answer);
                }
                finally
                {
                    expiration.Cancel();
                    try { await waiting; } catch (OperationCanceledException) { }
                }
            }
            await current.WaitAsync();
            if (peer == current) SetStatus(L.Get("Gapirish uchun mikrofoningizni yoqing."));
        }
        catch (Exception ex)
        {
            if (peer == current) End(ex is TimeoutException or OperationCanceledException
                ? L.Get("Ulanish vaqti tugadi. Yangi taklif bilan qayta urinib ko‘ring.")
                : L.Get("Ulanib bo‘lmadi. ") + ex.Message);
        }
    }

    public async Task ToggleMicrophoneAsync()
    {
        var current = peer;
        if (current?.Connected != true) return;
        bool enable = !current.Microphone;
        try
        {
            if (enable && await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            {
                if (peer == current) SetStatus(L.Get("Mikrofonga ruxsat berilmadi."));
                return;
            }
            if (peer != current || !current.Connected) return;
            await current.SetMicrophoneAsync(enable);
            SetStatus(enable ? L.Get("Mikrofon yoqilgan.") : L.Get("Mikrofon o‘chiq."));
        }
        catch (Exception ex) { if (peer == current) SetStatus(L.Get("Mikrofon ochilmadi. ") + ex.Message); }
    }

    public void End(string message = "Suhbat tugadi.")
    {
        var old = peer;
        peer = null;
        invitation = null;
        old?.Dispose();
        SetStatus(message);
    }
}
