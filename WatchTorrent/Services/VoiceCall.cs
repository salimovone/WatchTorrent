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
    public string Status { get; private set; } = "Taklif havolasini yuboring yoki kelgan havolani oching.";

    private void SetStatus(string text) { Status = text; Changed?.Invoke(); }

    public async Task ImportAsync(Stream stream)
    {
        var bytes = new byte[65537];
        int length = await stream.ReadAtLeastAsync(bytes, bytes.Length, false);
        if (length == bytes.Length) throw new InvalidDataException("Fayl juda katta.");
        ImportOffer(System.Text.Encoding.UTF8.GetString(bytes, 0, length));
    }

    public void ImportLink(string link) => ImportOffer(InviteLink.Read(link));

    private void ImportOffer(string text)
    {
        var file = SignalFile.Parse(text, "offer");
        if (file.Format != Format || !ValidId(file.CallId) || file.ReplyKey == null ||
            !Convert.TryFromBase64String(file.ReplyKey, new byte[32], out var size) || size != 32 ||
            !file.Sdp.Contains("m=audio ") || !file.Sdp.Contains("m=application "))
            throw new InvalidDataException("Taklif eskirgan yoki buzilgan. Yangi taklif so‘rang.");
        if (Active) { SetStatus("Avval joriy suhbatni tugating."); return; }
        invitation = file;
        SetStatus("Ovozli suhbatga taklif keldi.");
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
            SetStatus("Ulanish tayyorlanmoqda…");
            var local = await current.DescribeAsync(accept ? invite : null);
            current.Token.ThrowIfCancellationRequested();
            if (accept)
            {
                var answer = local with { Format = Format, CallId = invite!.CallId };
                SetStatus("Suhbatdoshingizga ulanmoqda…");
                await VoiceAnswerExchange.SendAsync(Convert.FromBase64String(invite.ReplyKey!), invite.CallId!, answer.Encode(), current.Token);
            }
            else
            {
                var key = RandomNumberGenerator.GetBytes(32);
                string id = Guid.NewGuid().ToString("N");
                var file = local with { Format = Format, CallId = id, ReplyKey = Convert.ToBase64String(key) };
                var link = InviteLink.Create(file.Encode());
                using var expiration = CancellationTokenSource.CreateLinkedTokenSource(current.Token);
                expiration.CancelAfter(TimeSpan.FromMinutes(10));
                SetStatus("Taklif qabul qilinishi kutilmoqda.");
                var waiting = VoiceAnswerExchange.WaitAsync(key, id, expiration.Token);
                try
                {
                    await Share.Default.RequestAsync(new ShareTextRequest { Title = "Taklif havolasini yuborish", Text = link });
                    var answer = SignalFile.Parse(await waiting, "answer");
                    current.Token.ThrowIfCancellationRequested();
                    if (answer.CallId != id) throw new InvalidDataException("Taklif mos kelmadi.");
                    current.Apply(answer);
                }
                finally
                {
                    expiration.Cancel();
                    try { await waiting; } catch (OperationCanceledException) { }
                }
            }
            await current.WaitAsync();
            if (peer == current) SetStatus("Gapirish uchun mikrofoningizni yoqing.");
        }
        catch (Exception ex)
        {
            if (peer == current) End(ex is TimeoutException or OperationCanceledException
                ? "Ulanish vaqti tugadi. Yangi taklif bilan qayta urinib ko‘ring."
                : "Ulanib bo‘lmadi. " + ex.Message);
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
                if (peer == current) SetStatus("Mikrofonga ruxsat berilmadi.");
                return;
            }
            if (peer != current || !current.Connected) return;
            current.SetMicrophone(enable);
            SetStatus(enable ? "Mikrofon yoqilgan." : "Mikrofon o‘chiq.");
        }
        catch (Exception ex) { if (peer == current) SetStatus("Mikrofon ochilmadi. " + ex.Message); }
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
