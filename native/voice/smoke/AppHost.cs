using System.Collections.Concurrent;

// A serial UI dispatcher for the console host. The linked app files are the real production peer/audio code.
internal static class MainThread
{
    private static readonly BlockingCollection<Action> queue = new();
    static MainThread() => Task.Run(() => { foreach (var action in queue.GetConsumingEnumerable()) action(); });
    public static void BeginInvokeOnMainThread(Action action) => queue.Add(action);
}

namespace WatchTorrent.Services
{
    // Movie playback is outside this voice probe; these DTOs only satisfy linked peer signatures.
    internal sealed record MovieInvite(string Magnet, string Title, int FileIndex, Guid Session, string? Details = null)
    {
        public const int MaxBytes = 65536;
        public bool Valid => false;
    }
    internal sealed record WatchMessage(string Type, Guid Session);
}
