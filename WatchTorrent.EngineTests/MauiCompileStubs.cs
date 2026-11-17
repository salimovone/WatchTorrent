// Compile the Windows download coordinator without requiring a MAUI workload.
// These stand-ins are used only by this console test project.
global using WatchTorrent.EngineTests;

namespace WatchTorrent.EngineTests
{
    internal sealed class Preferences
    {
        public static Preferences Default { get; } = new();
        readonly Dictionary<string, object> _values = [];
        public T Get<T>(string key, T fallback) => _values.TryGetValue(key, out var value) ? (T)value : fallback;
        public void Set<T>(string key, T value) where T : notnull => _values[key] = value;
        public void Remove(string key) => _values.Remove(key);
    }
    internal static class FileSystem { public static string AppDataDirectory => Path.GetTempPath(); }
    internal enum DevicePlatform { WinUI, Android, iOS }
    internal sealed class DeviceInfo
    {
        public static DeviceInfo Current { get; } = new();
        public DevicePlatform Platform => DevicePlatform.WinUI;
    }
    internal sealed class SecureStorage
    {
        public static SecureStorage Default { get; } = new();
        public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value) => Task.CompletedTask;
    }
    internal interface IDispatcherTimer
    {
        TimeSpan Interval { get; set; }
        event EventHandler Tick;
        void Start();
        void Stop();
    }
    internal class Dispatcher
    {
        public static Dispatcher? GetForCurrentThread() => null;
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
    internal class Application
    {
        public static Application? Current => null;
        public Dispatcher Dispatcher => new();
    }
}
namespace WatchTorrent.ViewModels { public sealed class PartnerDownload { } }
namespace WatchTorrent.Models
{
    public sealed record PageDetail(List<string> Files, string? ImageUrl, List<string> Tags);
    public sealed record TorrentPageData(object? Source, PageDetail Detail, string Size, string? PosterUrl, int Seeders, int Leechers);
}
namespace WatchTorrent.Services
{
    public sealed class TorrentPageCache
    {
        public Task<Models.TorrentPageData?> LoadAsync(string hash) => Task.FromResult<Models.TorrentPageData?>(null);
        public Task RemoveAsync(string hash) => Task.CompletedTask;
    }
    internal static class DownloadKeepAlive { public static void Update(IEnumerable<DownloadItem> items) { } }
}
