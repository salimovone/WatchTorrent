using WatchTorrent.ViewModels;

namespace WatchTorrent.Services;

internal sealed record WatchMessage(Guid Session, string Action, long Position = 0, long Clock = 0,
    bool Playing = false, float Speed = 1, int Stage = 0)
{
    public string Type => "watch";
}

// The inviter orders commands from both players. All deadlines are monotonic clock values.
internal sealed class WatchSession
{
    private readonly VoiceCall call;
    public Guid Id { get; }
    public bool Host { get; }
    private PlayerViewModel? player;
    private bool localReady, remoteReady, clockReady, running, closed;
    private bool wantsPlay = true;
    private float rate = 1;
    private long offset, rtt, probe, lastProbe, lastSnapshot, due;
    private int revision, rateRevision, stage;
    private long rateSettlesAt;
    private long rateRequest;
    private float? requestedRate;
    public bool RemoteLoading => !remoteReady && !closed;
    public bool WantsPlay => wantsPlay;
    public bool Closed => closed;
    public float PlaybackRate => requestedRate ?? rate;

    public WatchSession(VoiceCall call, Guid id, bool host)
    {
        this.call = call;
        Id = id;
        Host = host;
        call.ControlReceived += Receive;
        call.Changed += CheckConnection;
    }
    public void Attach(PlayerViewModel vm) => player = vm;
    private static long Now => Environment.TickCount64;
    private void Send(string action, long pos = 0, long time = 0, bool play = false) =>
        call.Send(new(Id, action, pos, time, play, rate, stage));
    private long Position => Math.Max(0, player?.Player?.Time ?? 0);

    public void Tick(bool ready)
    {
        if (closed) return;
        if (localReady != ready)
        {
            localReady = ready;
            if (!ready) player?.SetSharedPause(true);
            if (Host) Reconcile(); else Send("ready", play: ready);
        }
        if (!Host) return;
        if (Now - lastProbe > (clockReady ? 10000 : 1000) || lastProbe == 0)
        {
            lastProbe = probe = Now;
            Send("clock", time: probe);
        }
        if (running && Now >= due && Now - lastSnapshot > 1500 && localReady && remoteReady)
        {
            lastSnapshot = Now;
            Send("position", Position, Now + offset, true);
        }
    }

    public void Command(string action, long value = 0)
    {
        if (closed) return;
        if (Host) HandleCommand(action, value);
        else
        {
            // Remember the requested value even while the host's scheduled reply is
            // in flight, so a quick second hold restores the selected speed.
            if (action == "rate" && value is >= 100 and <= 4000)
            {
                requestedRate = value / 1000f;
                Send(action, value, time: ++rateRequest);
            }
            else Send(action, value);
        }
    }

    private void HandleCommand(string action, long value, long request = 0)
    {
        switch (action)
        {
            case "play": wantsPlay = true; break;
            case "pause": wantsPlay = false; break;
            case "seek": if (value < 0) return; break;
            case "rate":
                if (value is < 100 or > 4000) return;
                var nextRate = value / 1000f;
                if (Math.Abs(rate - nextRate) < 0.01)
                {
                    // Acknowledge even a duplicate guest request, without resetting
                    // the host decoder or leaving the guest's desired rate pending.
                    if (request > 0) Send("rateApply", request, Now + offset);
                    return;
                }
                rate = nextRate;
                // A speed change keeps each decoder at its current position. Sending a
                // full "apply" here used to seek and pause at both ends of every hold.
                var rateDeadline = Now + Math.Clamp(rtt + 100, 120, 1500);
                Send("rateApply", request, rateDeadline + offset);
                ScheduleRate(rateDeadline, rate);
                return;
            case "file":
                if (player == null || value < 0 || value >= player.Playlist.Count) return;
                stage++;
                Send("switch", value);
                Switch((int)value);
                return;
            default: return;
        }
        if (clockReady) Broadcast(action == "seek" ? value : Position);
    }

    private void Switch(int index)
    {
        revision++;
        rateRevision++;
        localReady = remoteReady = running = false;
        wantsPlay = true;
        if (player != null) _ = player.SwitchSharedFileAsync(index);
    }

    private void Reconcile()
    {
        if (clockReady && player != null) Broadcast(Position);
    }
    private void Broadcast(long position)
    {
        bool play = wantsPlay && localReady && remoteReady;
        long wait = Math.Clamp(rtt + 100, 120, 1500);
        long deadline = Now + wait;
        if (running && play && Now >= due && Math.Abs(position - Position) < 100)
            position += (long)(wait * rate);
        Send("intent", play: wantsPlay);
        Send("remoteReady", play: localReady);
        Send("apply", position, deadline + offset, play);
        Schedule(position, deadline, play);
    }

    private async void Schedule(long position, long deadline, bool play)
    {
        int version = ++revision;
        int speedVersion = rateRevision;
        float scheduledRate = rate;
        due = deadline;
        var delay = (int)Math.Clamp(deadline - Now, 0, 3000);
        if (delay > 0) await Task.Delay(delay);
        if (closed || revision != version || player == null) return;
        running = play && localReady;
        player.ApplyShared(position + (running ? (long)(Math.Max(0, Now - deadline) * scheduledRate) : 0), running);
        if (speedVersion == rateRevision) player.SetSharedRate(scheduledRate);
    }

    private async void ScheduleRate(long deadline, float nextRate)
    {
        int version = ++rateRevision;
        rateSettlesAt = deadline + 2000;
        var delay = (int)Math.Clamp(deadline - Now, 0, 3000);
        if (delay > 0) await Task.Delay(delay);
        if (closed || version != rateRevision || player == null) return;
        player.SetSharedRate(nextRate);
    }

    private void Receive(WatchMessage m)
    {
        if (closed || m.Session != Id) return;
        if (m.Action == "leave") { Close(false); return; }
        if (!Host && m.Action == "switch" && m.Stage == stage + 1 && player != null &&
            m.Position >= 0 && m.Position < player.Playlist.Count)
        {
            stage = m.Stage;
            Switch((int)m.Position);
            return;
        }
        if (m.Stage != stage) return;
        if (Host)
        {
            if (m.Action == "clockReply" && m.Clock == probe)
            {
                rtt = Math.Clamp(Now - probe, 0, 3000);
                offset = m.Position - (probe + rtt / 2);
                bool first = !clockReady;
                clockReady = true;
                if (first) Reconcile();
            }
            else if (m.Action == "ready" && remoteReady != m.Playing) { remoteReady = m.Playing; Reconcile(); }
            else HandleCommand(m.Action, m.Position, m.Clock);
        }
        else
        {
            switch (m.Action)
            {
                case "clock": Send("clockReply", Now, m.Clock); Send("ready", play: localReady); break;
                case "intent": wantsPlay = m.Playing; break;
                case "remoteReady": remoteReady = m.Playing; break;
                case "apply" when m.Speed is >= 0.1f and <= 4:
                    rate = m.Speed;
                    Schedule(m.Position, m.Clock, m.Playing);
                    break;
                case "rateApply" when m.Speed is >= 0.1f and <= 4:
                    rate = m.Speed;
                    if (m.Position == rateRequest) requestedRate = null;
                    ScheduleRate(m.Clock, rate);
                    break;
                case "position" when running && localReady && Now >= due && Now >= rateSettlesAt:
                    var target = m.Position + (long)(Math.Clamp(Now - m.Clock, 0, 3000) * rate);
                    if (Math.Abs(target - Position) > 650) player?.SetSharedTime(target);
                    break;
            }
        }
    }

    private void CheckConnection() { if (!call.Connected) Close(false); }
    public void Close(bool notify = true)
    {
        if (closed) return;
        closed = true;
        if (notify) Send("leave");
        call.ControlReceived -= Receive;
        call.Changed -= CheckConnection;
        player?.SetSharedPause(true);
        if (player != null) player.Session = null;
        player = null;
    }
}
