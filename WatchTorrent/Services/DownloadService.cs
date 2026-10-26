using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Dht;
using MonoTorrent.PiecePicking;

namespace WatchTorrent.Services
{
    /// <summary>
    /// MonoTorrent dvigateli ustidagi yuklamalar menejeri. Ro'yxat va yuklanish holati ilova qayta
    /// ochilganda tiklanadi. Public metodlar UI oqimidan chaqiriladi.
    /// </summary>
    public class DownloadService
    {
        private const string PausedKey = "downloads.paused";
        private const string WebSeedsKeyPrefix = "downloads.webseeds.";

        // Stream paytida o'qilayotgan joydan oldinda ustuvor yuklanadigan hajm (4K remux ~10 MB/s uchun ~6 soniya+).
        private const int StreamBufferBytes = 64 * 1024 * 1024;

        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        // Rejim almashtirishda trackerlarga "stopped" xabari uchun uzoq kutilmaydi.
        private static readonly TimeSpan QuickStopTimeout = TimeSpan.FromSeconds(1);

        private readonly string _cacheDirectory = Path.Combine(FileSystem.AppDataDirectory, "monotorrent");
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private readonly HashSet<string> _paused;
        private readonly HashSet<TorrentManager> _streaming = new();
        private readonly Factories _factories;
        private IReadOnlyList<ReadOnlyMemory<byte>> _dhtBootstrapNodes = Array.Empty<ReadOnlyMemory<byte>>();
        private DhtEngine? _dht;
        private ClientEngine? _engine;
        private Task? _initializeTask;
        private IDispatcherTimer? _refreshTimer;

        public DownloadService()
        {
            DownloadDirectory = GetDefaultDownloadDirectory();
            _factories = Factories.Default.WithDhtCreator(CreateDht);

            // Foydalanuvchi o'zi to'xtatgan torrentlar qayta ishga tushganda avtomatik boshlanmaydi.
            _paused = Preferences.Default.Get(PausedKey, string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public ObservableCollection<DownloadItem> Items { get; } = new();

        /// <summary>Yuklangan fayllar saqlanadigan papka.</summary>
        public string DownloadDirectory { get; }

        private static bool IsMobile => DeviceInfo.Current.Platform == DevicePlatform.Android;

        private string StatePath => Path.Combine(_cacheDirectory, "engine.state");

        private string DhtCachePath => Path.Combine(_cacheDirectory, "dht-nodes.bencode");

        public Task InitializeAsync()
        {
            if (_initializeTask == null || _initializeTask.IsFaulted)
            {
                _initializeTask = InitializeCoreAsync();
            }

            return _initializeTask;
        }

        public bool Contains(string infoHash)
        {
            return Find(infoHash) != null;
        }

        public DownloadItem? Find(string infoHash)
        {
            return Items.FirstOrDefault(item => string.Equals(item.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        }

        public DownloadItem? FindByMagnet(string magnetUri)
        {
            return MagnetLink.TryParse(magnetUri, out var link) ? Find(link.InfoHashes.V1OrV2.ToHex()) : null;
        }

        /// <param name="forStreaming">Torrent darhol ko'rish uchun qo'shiladi (stream rejimida, keyin qayta ishga tushirmaslik uchun).</param>
        public async Task<DownloadItem> AddAsync(string magnetUri, string? name = null, bool forStreaming = false)
        {
            await InitializeAsync();

            var magnet = MagnetLink.Parse(magnetUri);
            var webSeeds = TorrentNetwork.GetWebSeeds(magnetUri);
            var existing = Find(magnet.InfoHashes.V1OrV2.ToHex());

            if (existing != null)
            {
                RestoreWebSeeds(existing.Manager, webSeeds);
                await ResumeAsync(existing);

                return existing;
            }

            var manager = forStreaming
                ? await _engine!.AddStreamingAsync(magnet, DownloadDirectory, CreateTorrentSettings())
                : await _engine!.AddAsync(magnet, DownloadDirectory, CreateTorrentSettings());

            RestoreWebSeeds(manager, webSeeds);

            var item = await PrepareAsync(manager, name);

            Items.Insert(0, item);

            await manager.StartAsync();
            await SaveAsync();

            return item;
        }

        public async Task PauseAsync(DownloadItem item)
        {
            _paused.Add(item.InfoHash);
            SavePaused();

            await item.Manager.StopAsync(StopTimeout);
            item.Refresh();
        }

        public async Task ResumeAsync(DownloadItem item)
        {
            _paused.Remove(item.InfoHash);
            SavePaused();

            var manager = item.Manager;

            if (manager.State == TorrentState.Error)
            {
                await manager.StopAsync(StopTimeout);
            }

            if (manager.State is TorrentState.Stopped or TorrentState.Paused)
            {
                await manager.StartAsync();
            }

            item.Refresh();
        }

        public async Task RemoveAsync(DownloadItem item, bool deleteFiles)
        {
            var manager = item.Manager;

            if (manager.State != TorrentState.Stopped)
            {
                await manager.StopAsync(StopTimeout);
            }

            await _engine!.RemoveAsync(manager, deleteFiles ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);

            if (deleteFiles)
            {
                DeleteEmptyDirectory(manager.ContainingDirectory);
            }

            Items.Remove(item);
            _paused.Remove(item.InfoHash);
            SavePaused();
            Preferences.Default.Remove(WebSeedsKeyPrefix + item.InfoHash);

            await SaveAsync();
        }

        /// <summary>Torrent ishga tushiriladi va fayllar ro'yxati (metadata) kelguncha kutiladi.</summary>
        public async Task WaitForMetadataAsync(DownloadItem item, CancellationToken token)
        {
            await ResumeAsync(item);
            await item.Manager.WaitForMetadataAsync(token);
            RestoreWebSeeds(item.Manager);
        }

        /// <summary>
        /// Faylni yuklanish davomida o'qish uchun stream ochadi. Torrent stream rejimiga o'tadi: o'qilayotgan
        /// joydan oldingi bo'laklar birinchi navbatda yuklanadi. <paramref name="onlyThisFile"/> bo'lsa
        /// torrentdagi boshqa videolar yuklanmaydi — butun tezlik shu filmga ketadi.
        /// </summary>
        public async Task<Stream> OpenStreamAsync(DownloadItem item, ITorrentManagerFile file, bool onlyThisFile, CancellationToken token)
        {
            var manager = item.Manager;

            lock (_streaming)
            {
                _streaming.Add(manager);
            }

            foreach (var other in manager.Files)
            {
                // Boshqa faylga o'tib ketilgan bo'lsa, eski so'rov ustuvorliklarni buzmasin.
                token.ThrowIfCancellationRequested();

                // Oldin ko'rilgan qism (Highest) oddiy navbatga qaytadi — tezlik faqat hozirgi faylga ketsin.
                var priority = other == file ? Priority.Highest
                    : MediaFiles.IsSubtitle(other) ? Priority.High
                    : onlyThisFile ? Priority.DoNotDownload
                    : other.Priority == Priority.Highest ? Priority.Normal
                    : other.Priority;

                await manager.SetFilePriorityAsync(other, priority);
            }

            if (manager.StreamProvider == null)
            {
                await ChangePickerAsync(manager, CreateStreamingRequester(manager));
            }
            else
            {
                TuneStreamingWindow(manager, GetStreamingRequester(manager));
            }

            await ResumeAsync(item);

            // Bekor qilingan prebuffer'dan (masalan buferlanayotganda boshqa qismga o'tilsa) qolgan stream MonoTorrent'da
            // "faol" bo'lib qoladi va yangisini ochishga yo'l bermaydi. Bir vaqtda faqat bitta player bor — u yopiladi.
            GetActiveStream(manager)?.Dispose();

            return await manager.StreamProvider!.CreateStreamAsync(file, prebuffer: true, token);
        }

        public async Task CloseStreamAsync(DownloadItem item, Stream? stream)
        {
            var manager = item.Manager;

            stream?.Dispose();

            lock (_streaming)
            {
                _streaming.Remove(manager);
            }

            try
            {
                if (manager.State == TorrentState.Seeding)
                {
                    await manager.StopAsync(StopTimeout);
                }
                else if (manager.StreamProvider != null)
                {
                    // Qolgan qismni yuklab olishda standart (rarest-first) rejim ancha tez:
                    // stream rejimi oynadan tashqaridagi bo'laklarni har peerdan atigi 2 tadan so'raydi.
                    var wasRunning = manager.State is not (TorrentState.Stopped or TorrentState.Stopping or TorrentState.Error);

                    await ChangePickerAsync(manager, _factories.CreatePieceRequester());

                    if (wasRunning)
                    {
                        await manager.StartAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloads] Stream yopilmadi: {ex}");
            }

            await SaveAsync();
        }

        /// <summary>Torrentlar ro'yxati va DHT tugunlarini diskka yozadi (fast-resume'ni MonoTorrent o'zi saqlaydi).</summary>
        public async Task SaveAsync()
        {
            if (_engine == null)
            {
                return;
            }

            await _saveLock.WaitAsync();

            try
            {
                await _engine.SaveStateAsync(StatePath);

                if (_dht?.State == DhtState.Ready)
                {
                    await TorrentNetwork.SaveNodesAsync(_dht, DhtCachePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloads] Saqlab bo'lmadi: {ex}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private async Task InitializeCoreAsync()
        {
            Directory.CreateDirectory(_cacheDirectory);
            Directory.CreateDirectory(DownloadDirectory);

            _dhtBootstrapNodes = await TorrentNetwork.GetBootstrapNodesAsync(DhtCachePath);

            var engine = await RestoreEngineAsync();

            Items.Clear();

            foreach (var manager in engine.Torrents)
            {
                // Player yopilmay ilova o'chgan bo'lsa torrent stream rejimida qolgan — tez yuklash rejimiga qaytariladi.
                if (manager.StreamProvider != null)
                {
                    await manager.ChangePickerAsync(_factories.CreatePieceRequester());
                }

                Items.Insert(0, await PrepareAsync(manager, null));
            }

            _engine = engine;

            foreach (var item in Items.Where(item => !item.IsComplete && !_paused.Contains(item.InfoHash)))
            {
                try
                {
                    await item.Manager.StartAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Downloads] {item.Name} ishga tushmadi: {ex}");
                }
            }

            if (_refreshTimer == null)
            {
                _refreshTimer = (Dispatcher.GetForCurrentThread() ?? Application.Current!.Dispatcher).CreateTimer();
                _refreshTimer.Interval = TimeSpan.FromSeconds(1);
                _refreshTimer.Tick += (_, _) =>
                {
                    foreach (var item in Items)
                    {
                        item.Refresh();
                    }
                };
                _refreshTimer.Start();
            }
        }

        private async Task<ClientEngine> RestoreEngineAsync()
        {
            if (File.Exists(StatePath))
            {
                try
                {
                    var engine = await ClientEngine.RestoreStateAsync(StatePath, _factories);
                    await engine.UpdateSettingsAsync(CreateSettings());

                    return engine;
                }
                catch (Exception ex)
                {
                    // Buzilgan holat fayli ilovani ishdan chiqarmasin — bo'sh ro'yxat bilan boshlanadi.
                    Debug.WriteLine($"[Downloads] Holatni tiklab bo'lmadi: {ex}");
                }
            }

            return new ClientEngine(CreateSettings(), _factories);
        }

        private EngineSettings CreateSettings()
        {
            return new EngineSettingsBuilder
            {
                CacheDirectory = _cacheDirectory,
                AutoSaveLoadFastResume = true,
                AutoSaveLoadMagnetLinkMetadata = true,
                // 3.0.2 da bu kesh buzuq formatda o'qiladi — DHT tugunlarini TorrentNetwork saqlaydi.
                AutoSaveLoadDhtCache = false,
                AllowPortForwarding = true,
                // Standart qiymatlar (150 ulanish, bir vaqtda 8 ta ulanish urinishi) peerlarni juda sekin yig'adi.
                MaximumConnections = IsMobile ? 200 : 400,
                MaximumHalfOpenConnections = IsMobile ? 24 : 48,
                DiskCacheBytes = 32 * 1024 * 1024,
                // Web-seed (HTTP manba) standart holatda 60 soniyadan keyin ulanadi — faqat web-seed'li torrentlarda
                // (masalan archive.org) bu bir daqiqalik qora ekran. Peerlar sekin bo'lsa ham yordamga keladi.
                WebSeedDelay = TimeSpan.FromSeconds(5),
                WebSeedSpeedTrigger = 1024 * 1024
            }.ToSettings();
        }

        private static TorrentSettings CreateTorrentSettings()
        {
            return new TorrentSettingsBuilder
            {
                // Standart 60 ta — mashhur filmlarda yuzlab peer bor, ulardan ko'prog'idan yuklash tezroq.
                MaximumConnections = IsMobile ? 120 : 200
            }.ToSettings();
        }

        private IDhtEngine CreateDht()
        {
            var dht = new DhtEngine();

            if (_dhtBootstrapNodes.Count > 0)
            {
                dht.Add(_dhtBootstrapNodes);
            }

            _dht = dht;

            return dht;
        }

        private static IPieceRequester CreateStreamingRequester(TorrentManager manager)
        {
            var requester = new StreamingPieceRequester();

            TuneStreamingWindow(manager, requester);

            return requester;
        }

        /// <summary>
        /// MonoTorrent oldinga faqat 15 bo'lakni ustuvor yuklaydi (internal sozlama) — yuqori bitreytli video uchun
        /// bu bir necha soniyalik bufer xolos, shuning uchun bo'lak hajmiga qarab ~64 MB ga kengaytiriladi.
        /// </summary>
        private static void TuneStreamingWindow(TorrentManager manager, IStreamingPieceRequester? requester)
        {
            var pieceLength = manager.Torrent?.PieceLength ?? 0;

            if (requester is not StreamingPieceRequester streaming || pieceLength <= 0)
            {
                return;
            }

            var count = Math.Clamp((int)Math.Ceiling((double)StreamBufferBytes / pieceLength), 15, 128);

            typeof(StreamingPieceRequester)
                .GetProperty("HighPriorityCount", BindingFlags.Instance | BindingFlags.NonPublic)?
                .SetValue(streaming, count);
        }

        private static IStreamingPieceRequester? GetStreamingRequester(TorrentManager manager)
        {
            return typeof(MonoTorrent.Streaming.StreamProvider)
                .GetProperty("PieceRequester", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(manager.StreamProvider) as IStreamingPieceRequester;
        }

        private static Stream? GetActiveStream(TorrentManager manager)
        {
            return typeof(MonoTorrent.Streaming.StreamProvider)
                .GetProperty("ActiveStream", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(manager.StreamProvider) as Stream;
        }

        /// <summary>MonoTorrent piece tanlash rejimini faqat to'xtatilgan torrentda almashtiradi.</summary>
        private static async Task ChangePickerAsync(TorrentManager manager, IPieceRequester requester)
        {
            if (manager.State != TorrentState.Stopped)
            {
                await manager.StopAsync(QuickStopTimeout);
            }

            await manager.ChangePickerAsync(requester);
        }

        private async Task<DownloadItem> PrepareAsync(TorrentManager manager, string? name)
        {
            manager.TorrentStateChanged += OnTorrentStateChanged;

            try
            {
                await manager.UpdateSettingsAsync(CreateTorrentSettings());
                await TorrentNetwork.SpreadTrackersAsync(manager);
                RestoreWebSeeds(manager);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloads] Torrent sozlanmadi: {ex}");
            }

            return new DownloadItem(manager, name);
        }

        /// <summary>
        /// MonoTorrent 3.0.2 magnet'dagi web-seed (ws=) manzillarini o'qimaydi, metadata kelganda va ilova qayta
        /// ochilganda (torrent metadata keshidan tiklanadi) ham ularni bilmaydi — shuning uchun manzillar alohida
        /// saqlanib, har safar qayta qo'shiladi. Ro'yxatni MonoTorrent o'z oqimida (ClientEngine.MainLoop) aylanib
        /// chiqadi, qo'shish ham o'sha oqimga yuboriladi — torrentni to'xtatish shart emas.
        /// </summary>
        /// <param name="webSeeds">Magnet'dan olingan yangi manzillar (bo'lmasa saqlanganlari ishlatiladi).</param>
        private static void RestoreWebSeeds(TorrentManager manager, IReadOnlyList<string>? webSeeds = null)
        {
            var key = WebSeedsKeyPrefix + manager.InfoHashes.V1OrV2.ToHex();

            if (webSeeds is { Count: > 0 })
            {
                Preferences.Default.Set(key, string.Join(' ', webSeeds));
            }

            if (manager.Torrent is not { } torrent)
            {
                return;
            }

            var seeds = Preferences.Default.Get(key, string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null)
                .OfType<Uri>()
                .ToList();

            void AddMissing()
            {
                foreach (var uri in seeds.Where(uri => !torrent.HttpSeeds.Contains(uri)))
                {
                    torrent.HttpSeeds.Add(uri);
                }
            }

            if (EngineLoop is { } loop)
            {
                loop.Post(_ => AddMissing(), null);
            }
            else if (manager.State == TorrentState.Stopped)
            {
                AddMissing();
            }
        }

        private static readonly SynchronizationContext? EngineLoop =
            typeof(ClientEngine).GetField("MainLoop", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as SynchronizationContext;

        private async void OnTorrentStateChanged(object? sender, TorrentStateChangedEventArgs e)
        {
            if (sender is not TorrentManager manager)
            {
                return;
            }

            if (e.OldState == TorrentState.Metadata && manager.HasMetadata)
            {
                RestoreWebSeeds(manager);

                return;
            }

            if (e.NewState != TorrentState.Seeding)
            {
                return;
            }

            // Yuklab bo'lingach tarqatish (seeding) to'xtatiladi — mobil trafik va batareyani tejash uchun.
            // Film ko'rilayotgan paytda emas: stream torrent ishlab turishini talab qiladi.
            lock (_streaming)
            {
                if (_streaming.Contains(manager))
                {
                    return;
                }
            }

            try
            {
                await manager.StopAsync(StopTimeout);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloads] Seeding to'xtatilmadi: {ex}");
            }
        }

        /// <summary>Ko'p faylli torrent o'chirilgach qolgan bo'sh papkani tozalaydi (MonoTorrent faqat fayllarni o'chiradi).</summary>
        private void DeleteEmptyDirectory(string directory)
        {
            try
            {
                var isDownloadRoot = string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(DownloadDirectory)),
                    StringComparison.OrdinalIgnoreCase);

                if (isDownloadRoot || !Directory.Exists(directory) || Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any())
                {
                    return;
                }

                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloads] Papka o'chirilmadi: {ex}");
            }
        }

        private void SavePaused()
        {
            Preferences.Default.Set(PausedKey, string.Join(',', _paused));
        }

        private static string GetDefaultDownloadDirectory()
        {
#if ANDROID
            // Android/data/<paket>/files/Download — ruxsat so'ramasdan yozish mumkin bo'lgan tashqi xotira.
            var external = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;

            return string.IsNullOrEmpty(external) ? Path.Combine(FileSystem.AppDataDirectory, "Downloads") : external;
#elif WINDOWS
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "WatchTorrent");
#else
            return Path.Combine(FileSystem.AppDataDirectory, "Downloads");
#endif
        }
    }
}
