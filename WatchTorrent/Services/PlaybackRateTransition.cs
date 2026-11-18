namespace WatchTorrent.Services;

/// <summary>Retargetable rate changes keep the audio clock from jumping by 100% in one block.</summary>
internal sealed class PlaybackRateTransition(Func<float, bool> apply, float initialRate = 1)
{
    private CancellationTokenSource? _pending;
    private float _applied = initialRate;
    public float Target { get; private set; } = initialRate;

    public bool Set(float target)
    {
        if (target <= 0 || !float.IsFinite(target)) return false;
        if (Math.Abs(Target - target) < 0.001f) return true;
        Cancel();
        Target = target;
        float start = _applied;
        var cancellation = _pending = new CancellationTokenSource();
        // First small step is synchronous so unsupported rates can be rejected.
        if (!Apply(start + (target - start) / 10)) { Cancel(); Target = _applied; return false; }
        _ = CompleteAsync(start, target, cancellation.Token);
        return true;
    }

    private bool Apply(float rate)
    {
        if (!apply(rate)) return false;
        _applied = rate;
        return true;
    }

    private async Task CompleteAsync(float start, float target, CancellationToken token)
    {
        try
        {
            for (int step = 2; step <= 10; step++)
            {
                await Task.Delay(20, token);
                token.ThrowIfCancellationRequested();
                if (!Apply(start + (target - start) * step / 10)) { Target = _applied; return; }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Cancel()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }
}
