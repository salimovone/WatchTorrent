using L = WatchTorrent.Core.Localization.AppText;
using System.Collections.ObjectModel;
using System.Diagnostics;
using WatchTorrent.Core;
using WatchTorrent.Libtorrent;

namespace WatchTorrent.Services;

public sealed class DownloadService : ViewModels.ObservableObject, IDisposable
{
    private readonly TorrentPageCache _pages;
    private LibtorrentEngine? _engine;
    private Task? _initialization;
    private IDispatcherTimer? _timer;
    private bool _refreshing;
    private long _lastSave;
    private readonly SemaphoreSlim _addLock = new(1, 1);
    private readonly HashSet<string> _streaming = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<DownloadItem> Items { get; } = [];
    public ObservableCollection<ViewModels.PartnerDownload> PartnerDownloads { get; } = [];
    public string DownloadDirectory { get; private set; }
    private string _connectionStatus = string.Empty;
    public string ConnectionStatus { get => L.Get(_connectionStatus); private set => SetProperty(ref _connectionStatus, value); }

    public DownloadService(TorrentPageCache pages)
    {
        _pages = pages;
        DownloadDirectory = Preferences.Default.Get("downloads.directory", GetDefaultDownloadDirectory());
#if IOS
        DownloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.GetFileName(DownloadDirectory));
#endif
    }

    internal async Task<MovieInvite> DescribeMovieAsync(MovieInvite movie, Models.TorrentPageData? page)
        {
            var item = FindByMagnet(movie.Magnet);
            if (page == null && item != null) page = await _pages.LoadAsync(item.InfoHash);
            var manager = item?.Manager;
            var selected = movie.SelectFile(item);
            bool catalog = page?.Source != null;
            var size = manager?.Torrent?.Size ?? manager?.MagnetLink.Size;
            int? count = manager?.HasMetadata == true ? manager.Files.Count : page?.Detail.Files.Count;
            return movie with { Details = new MovieDetails(
                size > 0 ? Formatting.Bytes(size.Value) : page?.Size,
                page?.PosterUrl ?? page?.Detail.ImageUrl,
                page == null ? null : string.Join(" · ", page.Detail.Tags.Take(3)),
                catalog ? page!.Seeders : manager?.Peers.Seeds,
                catalog ? page!.Leechers : manager?.Peers.Leechs,
                catalog, count, selected == null ? null : Path.GetFileName(selected.Path),
                selected == null ? null : Formatting.Bytes(selected.Length)) };
        }


    public bool UsesCustomDirectory => DownloadDirectory != GetDefaultDownloadDirectory();

        public void RestoreDefaultDirectory()
        {
            Preferences.Default.Remove("downloads.directory");
            DownloadDirectory = GetDefaultDownloadDirectory();
        }

        public void SetDownloadDirectory(string directory)
        {
            directory = Path.GetFullPath(directory);
            using (new FileStream(Path.Combine(directory, $".watchtorrent-{Guid.NewGuid():N}"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            Preferences.Default.Set("downloads.directory", directory);
            DownloadDirectory = directory;
        }


    private static string GetDefaultDownloadDirectory()
        {
#if ANDROID
            // Android/data/<paket>/files/Download — ruxsat so'ramasdan yozish mumkin bo'lgan tashqi xotira.
            var external = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;

            return string.IsNullOrEmpty(external) ? Path.Combine(FileSystem.AppDataDirectory, "Downloads") : external;
#elif WINDOWS
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "WatchTorrent");
#elif IOS
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Downloads");
#else
            return Path.Combine(FileSystem.AppDataDirectory, "Downloads");
#endif
        }


    public Task InitializeAsync()
    {
        if (_initialization == null || _initialization.IsFaulted || _initialization.IsCanceled)
            _initialization = InitializeCoreAsync();
        return _initialization;
    }
    private async Task InitializeCoreAsync()
    {
        try
        {
        ConnectionStatus = string.Empty;
        _engine?.Dispose();
        _engine = await LibtorrentEngine.CreateAsync(Path.Combine(FileSystem.AppDataDirectory, "libtorrent"), DeviceInfo.Current.Platform != DevicePlatform.WinUI);
        var torrents = await _engine.ListAsync();
        Items.Clear();
        foreach (var torrent in torrents)
        {
            await _engine.RefreshAsync(torrent);
            Items.Add(new DownloadItem(torrent, null));
        }
        if (_timer == null)
        {
            _timer = (Dispatcher.GetForCurrentThread() ?? Application.Current!.Dispatcher).CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += async (_, _) => await RefreshAsync();
            _timer.Start();
        }
        }
        catch (Exception ex)
        {
            ConnectionStatus = L.Get("libtorrent ishga tushmadi. ") + ex.Message;
            throw;
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _engine == null) return;
        _refreshing = true;
        try
        {
            foreach (var item in Items.ToArray())
            {
                if (item.IsBusy) continue;
                try
                {
                    await _engine.RefreshAsync(item.Manager);
                    item.Refresh();
                    if(item.IsComplete && item.IsRunning && !_streaming.Contains(item.InfoHash))
                        await PauseAsync(item);
                }
                catch (Exception ex) { Debug.WriteLine(ex); item.SetConnectionError(ex.Message); }
            }
            DownloadKeepAlive.Update(Items);
            if (Environment.TickCount64 - _lastSave > 30_000)
            {
                try { await SaveAsync(); _lastSave = Environment.TickCount64; }
                catch (Exception ex) { ConnectionStatus = L.Get("Yuklama holati saqlanmadi: ") + ex.Message; }
            }
        }
        finally { _refreshing = false; }
    }

    public DownloadItem? Find(string infoHash) => Items.FirstOrDefault(i => string.Equals(i.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
    public bool Contains(string infoHash) => Find(infoHash) != null;
    public DownloadItem? FindByMagnet(string magnet) => Magnet.TryParse(magnet, out var link) ? Find(link!.Hash) : null;
    internal static string ShareMagnet(DownloadItem item) => item.Manager.MagnetLink.ToV1String();

    public async Task<DownloadItem> AddAsync(string magnetUri, string? name = null)
    {
        await InitializeAsync();
        await _addLock.WaitAsync();
        try
        {
            var hash = Magnet.Parse(magnetUri).Hash;
            if (Find(hash) is { } existing) { await ResumeAsync(existing); return existing; }
            var manager = await _engine!.AddAsync(magnetUri, hash, Directory.CreateDirectory(DownloadDirectory).FullName);
            var item = new DownloadItem(manager, name);
            Items.Insert(0, item);
            return item;
        }
        finally { _addLock.Release(); }
    }

    public Task PauseAsync(DownloadItem item) => SetPausedAsync(item, true);
    public Task ResumeAsync(DownloadItem item) => SetPausedAsync(item, false);
    private async Task SetPausedAsync(DownloadItem item, bool paused)
    {
        if (!item.TryBeginOperation(paused ? L.Get("Pauza qilinmoqda…") : L.Get("Davom ettirilmoqda…"))) return;
        try { await _engine!.SetPausedAsync(item.Manager, paused); }
        finally { item.EndOperation(); }
    }

    public async Task RemoveAsync(DownloadItem item, bool deleteFiles)
    {
        if (!item.TryBeginOperation(L.Get("O'chirilmoqda…"))) return;
        try
        {
            await _engine!.RemoveAsync(item.Manager, deleteFiles);
            Items.Remove(item);
            await _pages.RemoveAsync(item.InfoHash);
        }
        finally { item.EndOperation(); }
    }

    public async Task WaitForMetadataAsync(DownloadItem item, CancellationToken token)
    {
        await ResumeAsync(item);
        while (!item.Manager.HasMetadata)
        {
            token.ThrowIfCancellationRequested();
            await _engine!.RefreshAsync(item.Manager, token);
            if (item.Manager.State == TorrentState.Error) throw new IOException(item.Manager.Error?.Reason);
            await Task.Delay(400, token);
        }
    }

    public async Task<Stream> OpenStreamAsync(DownloadItem item, ITorrentManagerFile file, bool onlyThisFile, CancellationToken token)
    {
        foreach (var other in item.Manager.Files)
        {
            token.ThrowIfCancellationRequested();
            await item.Manager.SetFilePriorityAsync(other, other == file ? Priority.Highest : MediaFiles.IsSubtitle(other) ? Priority.High : onlyThisFile ? Priority.DoNotDownload : Priority.Normal);
        }
        await ResumeAsync(item);
        var stream = await _engine!.OpenStreamAsync(item.Manager, file, token);
        _streaming.Add(item.InfoHash);
        return stream;
    }
    public async Task CloseStreamAsync(DownloadItem item, Stream? stream)
    {
        stream?.Dispose();
        _streaming.Remove(item.InfoHash);
        try { await _engine!.EndStreamingAsync(item.Manager); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }
    // Resume data is saved without pausing active peer connections.
    public async Task SaveAsync()
    {
        var engine=_engine;
        if(engine==null)return;
        try { await engine.SaveAsync(); }
        catch(ObjectDisposedException) when(_engine==null) { }
        catch(Exception ex)
        {
            ConnectionStatus=L.Get("Yuklama holati saqlanmadi: ")+ex.Message;
            Debug.WriteLine(ex);
        }
    }
    public void Dispose()
    {
        _timer?.Stop();
        var engine=_engine;
        _engine=null;
        try { engine?.Dispose(); }
        catch(Exception ex) { Debug.WriteLine("[Downloads] Final save failed: "+ex); }
    }
}
