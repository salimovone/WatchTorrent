using System.Collections.ObjectModel;
using System.Diagnostics;
using LibVLCSharp.Shared;
using MonoTorrent;
using WatchTorrent.Services;

namespace WatchTorrent.ViewModels
{
    /// <summary>
    /// Film o'ynatish: torrentni tayyorlaydi (metadata, fayl tanlash), faylni diskdan yoki yuklanish davomida
    /// stream qilib VLC'ga beradi, to'xtagan joyni eslab qoladi va audio/subtitr/format sozlamalarini boshqaradi.
    /// </summary>
    public class PlayerViewModel : ObservableObject, IQueryAttributable
    {
        private static readonly float[] Speeds = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };

        // Format tugmasi shu tartibda aylantiradi; tanlov keyingi filmlar uchun ham eslab qolinadi.
        private static readonly string[] AspectModes = { "Asl o'lcham", "Ekranni to'ldirish", "Cho'zish", "16:9", "4:3" };

        private const string AspectModeKey = "player.aspect_mode";

        // Subtitrlar torrentdan o'zimiz qo'shamiz (Subs/ papkasidagilari ham) — VLC'ning avtomatik qidiruvi takrorlaydi.
        private static readonly string[] BaseOptions = { "--no-sub-autodetect-file" };

        // Android: LibVLC'ni har safar yaratib-yo'q qilish beqaror, shuning uchun bitta nusxa qayta ishlatiladi.
        private static LibVLC? _sharedLibVLC;

        private readonly DownloadService _downloads;
        private readonly CancellationTokenSource _cancellation = new();
        private string? _magnet;
        private string? _infoHash;
        private DownloadItem? _item;
        private ITorrentManagerFile? _file;
        private int _fileIndex;
        private string _torrentTitle = string.Empty;
        private bool _watchOnly;
        private bool _isSwitching;
        private CancellationTokenSource? _fileCancellation;
        private Stream? _stream;
        private bool _streamRequested;
        private TorrentMediaInput? _input;
        private LibVLC? _libVLC;
        private bool _ownsLibVLC;
        private Media? _media;
        private bool _hasStartedPlaying;
        private bool _isSeeking;
        private bool _isClosed;
        private long _lastResumeSaveAt;
        private bool _bufferPaused;
        private long _resumeMs;
        private int _aspectMode = Math.Clamp(Preferences.Default.Get(AspectModeKey, 0), 0, AspectModes.Length - 1);
        private double _screenWidth;
        private double _screenHeight;

        private string _title = string.Empty;
        private string _statusText = "Tayyorlanmoqda…";
        private bool _isLoading = true;
        private bool _isBuffering;
        private string? _errorMessage;
        private bool _isPlaying;
        private double _positionSeconds;
        private double _durationSeconds = 1;
        private string _positionText = "0:00";
        private string _durationText = "0:00";
        private string _streamInfo = string.Empty;
        private double _bufferedFraction;
        private string? _resumeNotice;

        public PlayerViewModel(DownloadService downloads)
        {
            _downloads = downloads;
        }

        /// <summary>Film tugadi yoki foydalanuvchi faylni tanlamadi — sahifa yopilishi kerak.</summary>
        public event EventHandler? CloseRequested;

        public LibVLCSharp.Shared.MediaPlayer? Player { get; private set; }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnOverlayChanged();
                }
            }
        }

        public bool IsBuffering
        {
            get => _isBuffering;
            private set
            {
                if (SetProperty(ref _isBuffering, value))
                {
                    OnOverlayChanged();
                }
            }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnOverlayChanged();
                }
            }
        }

        public bool HasError => ErrorMessage != null;

        public bool ShowSpinner => (IsLoading || IsBuffering) && !HasError;

        public bool ShowTransport => !IsLoading && !HasError;

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseIcon));
                }
            }
        }

        public string PlayPauseIcon => IsPlaying ? "ic_pause.png" : "ic_play.png";

        public double PositionSeconds
        {
            get => _positionSeconds;
            private set => SetProperty(ref _positionSeconds, value);
        }

        /// <summary>Slider uchun: Maximum Minimum'dan katta bo'lishi shart, shuning uchun kamida 1.</summary>
        public double DurationSeconds
        {
            get => _durationSeconds;
            private set => SetProperty(ref _durationSeconds, Math.Max(1, value));
        }

        public string PositionText
        {
            get => _positionText;
            private set => SetProperty(ref _positionText, value);
        }

        public string DurationText
        {
            get => _durationText;
            private set => SetProperty(ref _durationText, value);
        }

        /// <summary>Stream holati: tezlik, peerlar, faylning yuklangan qismi.</summary>
        public string StreamInfo
        {
            get => _streamInfo;
            private set
            {
                if (SetProperty(ref _streamInfo, value))
                {
                    OnPropertyChanged(nameof(HasStreamInfo));
                }
            }
        }

        public bool HasStreamInfo => StreamInfo.Length > 0;

        /// <summary>Hozirgi joydan keyin uzluksiz yuklangan qism (0..1) — slider ostidagi bufer chizig'i.</summary>
        public double BufferedFraction
        {
            get => _bufferedFraction;
            private set => SetProperty(ref _bufferedFraction, value);
        }

        public string? ResumeNotice
        {
            get => _resumeNotice;
            private set
            {
                if (SetProperty(ref _resumeNotice, value))
                {
                    OnPropertyChanged(nameof(HasResumeNotice));
                }
            }
        }

        public bool HasResumeNotice => ResumeNotice != null;

        /// <summary>Torrentdagi ko'rish mumkin bo'lgan fayllar (serial qismlari va h.k.).</summary>
        public ObservableCollection<PlaylistItem> Playlist { get; } = new();

        public bool HasPlaylist => Playlist.Count > 1;

        public bool HasNext => CurrentIndex >= 0 && CurrentIndex < Playlist.Count - 1;

        public string PlaylistHeader => $"Fayllar ({Playlist.Count})";

        private int CurrentIndex => Playlist.IndexOf(Playlist.FirstOrDefault(item => item.IsCurrent)!);

        private string LastFileKey => $"lastfile.{_item?.InfoHash}";

        private string ResumeKey => $"resume.{_item?.InfoHash}.{_fileIndex}";

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            _magnet = query.TryGetValue("Magnet", out var magnet) ? magnet as string : null;
            _infoHash = query.TryGetValue("InfoHash", out var infoHash) ? infoHash as string : null;
            Title = _torrentTitle = query.TryGetValue("Title", out var title) ? title as string ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// VLC dvigateli va player yaratiladi. <paramref name="platformOptions"/> — Windows'da VideoView swapchain
        /// parametrlari (ularsiz video ko'rinmaydi).
        /// </summary>
        public void CreatePlayer(string[] platformOptions)
        {
            Core.Initialize();

            if (platformOptions.Length == 0)
            {
                _libVLC = _sharedLibVLC ??= new LibVLC(enableDebugLogs: false, BaseOptions);
            }
            else
            {
                // Windows: LibVLC aynan shu VideoView swapchain'i bilan yaratilishi shart.
                _libVLC = new LibVLC(enableDebugLogs: false, BaseOptions.Concat(platformOptions).ToArray());
                _ownsLibVLC = true;
            }

            Player = new LibVLCSharp.Shared.MediaPlayer(_libVLC) { EnableHardwareDecoding = true };

            // MediaPlayer hodisalariga (Playing, EndReached...) ataylab obuna bo'linmaydi: ular VLC'ning native
            // oqimlarida chaqiriladi va Android'da shu oqimlar tugayotganda libvlc JNI xatosi bilan ilovani yiqitadi
            // (LibVLCSharp #567). Holat o'rniga Tick() da Player.State orqali kuzatiladi.
        }

        public async Task StartAsync()
        {
            var token = _cancellation.Token;

            try
            {
                await _downloads.InitializeAsync();

                var created = false;
                _item = _infoHash != null ? _downloads.Find(_infoHash) : null;

                if (_item == null)
                {
                    if (_magnet == null)
                    {
                        throw new InvalidOperationException("Torrent yuklamalar ro'yxatida topilmadi.");
                    }

                    // Ro'yxatda bor torrent ham shu yo'ldan o'tadi — magnet'dagi web-seed manzillari tiklanadi.
                    created = _downloads.FindByMagnet(_magnet) == null;
                    StatusText = "Torrent qo'shilmoqda…";
                    _item = await _downloads.AddAsync(_magnet, Title, forStreaming: true);
                }

                StatusText = "Torrent ma'lumoti olinmoqda…";
                await _downloads.WaitForMetadataAsync(_item, token);

                var playable = MediaFiles.GetPlayable(_item.Manager.Files);

                if (playable.Count == 0)
                {
                    throw new InvalidOperationException("Bu torrentda video fayl yo'q.");
                }

                _watchOnly = created;

                var duplicateNames = playable
                    .GroupBy(file => Path.GetFileNameWithoutExtension(file.Path), StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < playable.Count; i++)
                {
                    var showFormat = duplicateNames.Contains(Path.GetFileNameWithoutExtension(playable[i].Path));
                    Playlist.Add(new PlaylistItem(i + 1, playable[i], showFormat));
                }

                OnPlaylistChanged();

                // Serialda oxirgi ko'rilgan qismdan, aks holda birinchisidan boshlanadi; boshqasini ro'yxatdan tanlash mumkin.
                var lastFile = Preferences.Default.Get(LastFileKey, -1);
                var first = Playlist.FirstOrDefault(item => _item.Manager.Files.IndexOf(item.File) == lastFile) ?? Playlist[0];

                await PlayFileAsync(first);
            }
            catch (OperationCanceledException) when (_isClosed)
            {
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    ErrorMessage = ex is InvalidOperationException ? ex.Message : Formatting.Error(ex);
                }
            }
        }

        /// <summary>Har ~0.5 soniyada sahifa taymeridan chaqiriladi.</summary>
        public void Tick()
        {
            if (_isClosed)
            {
                return;
            }

            UpdateStreamInfo();

            var player = Player;

            if (player == null)
            {
                return;
            }

            if (!_isSwitching)
            {
                HandlePlayerState(player.State);
            }

            if (!_hasStartedPlaying)
            {
                return;
            }

            UpdateStreamBuffer(player);

            // Bufer uchun avtomatik pauza foydalanuvchi uchun "o'ynamoqda" holati hisoblanadi.
            IsPlaying = player.IsPlaying || _bufferPaused;
            IsBuffering = _bufferPaused || _input?.IsWaiting == true;

            var length = player.Length;

            if (length > 0)
            {
                DurationSeconds = length / 1000.0;
                DurationText = FormatTime(length);
            }

            if (!_isSeeking)
            {
                var time = Math.Max(0, player.Time);

                PositionSeconds = time / 1000.0;
                PositionText = FormatTime(time);
            }

            if (Environment.TickCount64 - _lastResumeSaveAt > 10_000)
            {
                SaveResumePosition();
            }
        }

        public void ShowError(Exception exception)
        {
            ErrorMessage = $"Playerni ishga tushirib bo'lmadi: {exception.Message}";
        }

        public void TogglePlay()
        {
            var player = Player;

            if (player == null || !_hasStartedPlaying)
            {
                return;
            }

            if (_bufferPaused)
            {
                // Buferlanish paytida foydalanuvchi pauza bosdi — bufer to'lganda o'zi davom etmaydi.
                _bufferPaused = false;
            }
            else if (player.IsPlaying)
            {
                player.SetPause(true);
            }
            else if (player.State == VLCState.Ended)
            {
                player.Stop();
                player.Play(_media!);
            }
            else
            {
                player.SetPause(false);
            }

            IsPlaying = !IsPlaying;
        }

        public void Pause()
        {
            _bufferPaused = false;
            Player?.SetPause(true);
        }

        public void BeginSeek()
        {
            _isSeeking = true;
        }

        public void PreviewSeek(double seconds)
        {
            PositionText = FormatTime((long)(seconds * 1000));
        }

        public void EndSeek(double seconds)
        {
            SeekTo(seconds);
            _isSeeking = false;
        }

        public void SeekBy(double seconds)
        {
            SeekTo(PositionSeconds + seconds);
        }

        public void SeekTo(double seconds)
        {
            if (Player == null || !_hasStartedPlaying)
            {
                return;
            }

            seconds = Math.Clamp(seconds, 0, DurationSeconds);

            Player.Time = (long)(seconds * 1000);
            PositionSeconds = seconds;
            PositionText = FormatTime((long)(seconds * 1000));
        }

        public void RestartFromBeginning()
        {
            ResumeNotice = null;
            SeekTo(0);
        }

        public async Task ChooseAudioAsync()
        {
            if (Player == null)
            {
                return;
            }

            // -1 — "o'chirish"; filmda ovozsiz qolishning foydasi yo'q.
            var tracks = Player.AudioTrackDescription.Where(track => track.Id != -1).ToArray();

            if (tracks.Length == 0)
            {
                return;
            }

            var current = Player.AudioTrack;
            var index = await ChooseAsync("Audio yo'lak", tracks.Select(track => MarkCurrent(track.Name, track.Id == current)));

            if (index >= 0)
            {
                Player.SetAudioTrack(tracks[index].Id);
            }
        }

        public async Task ChooseSubtitleAsync()
        {
            if (Player == null)
            {
                return;
            }

            var tracks = Player.SpuDescription;

            if (tracks.Length == 0)
            {
                await Shell.Current.DisplayAlertAsync("Subtitr", "Bu filmda subtitr topilmadi.", "OK");

                return;
            }

            var current = Player.Spu;
            var names = tracks.Select(track => MarkCurrent(track.Id == -1 ? "O'chirish" : track.Name, track.Id == current));
            var index = await ChooseAsync("Subtitr", names);

            if (index >= 0)
            {
                Player.SetSpu(tracks[index].Id);
            }
        }

        /// <summary>Ekran formatini ro'yxatdagi keyingisiga almashtiradi va uning nomini qaytaradi.</summary>
        public string CycleAspectMode()
        {
            _aspectMode = (_aspectMode + 1) % AspectModes.Length;
            Preferences.Default.Set(AspectModeKey, _aspectMode);

            ApplyAspectMode();

            return AspectModes[_aspectMode];
        }

        /// <summary>"To'ldirish" va "Cho'zish" ekran nisbatiga bog'liq — oyna o'lchami o'zgarganda qayta qo'llanadi.</summary>
        public void SetScreenSize(double width, double height)
        {
            _screenWidth = width;
            _screenHeight = height;

            ApplyAspectMode();
        }

        private void ApplyAspectMode()
        {
            if (Player == null || _screenWidth < 1 || _screenHeight < 1)
            {
                return;
            }

            var screen = $"{(int)_screenWidth}:{(int)_screenHeight}";

            (Player.CropGeometry, Player.AspectRatio) = _aspectMode switch
            {
                1 => (screen, null),
                2 => (null, screen),
                3 => (null, "16:9"),
                4 => (null, "4:3"),
                _ => ((string?)null, (string?)null)
            };
        }

        public async Task ChooseSpeedAsync()
        {
            if (Player == null)
            {
                return;
            }

            var current = Player.Rate;
            var index = await ChooseAsync("Tezlik", Speeds.Select(speed => MarkCurrent($"{speed:0.##}x", Math.Abs(speed - current) < 0.01)));

            if (index >= 0)
            {
                Player.SetRate(Speeds[index]);
            }
        }

        /// <summary>Player'dan chiqmasdan boshqa faylga (masalan serialning keyingi qismiga) o'tadi.</summary>
        public async Task SwitchToAsync(PlaylistItem entry)
        {
            if (_isClosed || _isSwitching || entry.IsCurrent)
            {
                return;
            }

            _isSwitching = true;

            try
            {
                SaveResumePosition();
                _fileCancellation?.Cancel();
                _input?.Cancel();

                if (Player is { } player)
                {
                    await Task.Run(player.Stop);
                }

                _media?.Dispose();
                _media = null;
                _input?.Dispose();
                _input = null;

                // MonoTorrent bitta torrentda faqat bitta ochiq stream'ga ruxsat beradi — yangisidan oldin eskisi yopiladi.
                _stream?.Dispose();
                _stream = null;

                ResetPlaybackState();
                await PlayFileAsync(entry);
            }
            catch (OperationCanceledException) when (_isClosed)
            {
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    ErrorMessage = ex is InvalidOperationException ? ex.Message : Formatting.Error(ex);
                }
            }
            finally
            {
                _isSwitching = false;
            }
        }

        public Task PlayNextAsync()
        {
            return HasNext ? SwitchToAsync(Playlist[CurrentIndex + 1]) : Task.CompletedTask;
        }

        /// <summary>Ro'yxat ochiq turganda yuklanish foizlarini yangilaydi.</summary>
        public void RefreshPlaylist()
        {
            foreach (var item in Playlist)
            {
                item.Refresh();
            }
        }

        /// <summary>VLC to'xtatiladi (video sirtidan ajratishdan oldin chaqiriladi).</summary>
        public async Task StopPlaybackAsync()
        {
            if (_isClosed)
            {
                return;
            }

            _isClosed = true;

            SaveResumePosition();
            _fileCancellation?.Cancel();
            _cancellation.Cancel();
            _input?.Cancel();

            var player = Player;

            if (player != null)
            {
                // Stop() VLC oqimlari tugashini kutadi — UI oqimini bloklamaslik uchun fonda.
                await Task.Run(player.Stop);
            }

            _media?.Dispose();
            _media = null;
        }

        /// <summary>VideoView'dan ajratilgandan keyin chaqiriladi.</summary>
        public void DisposePlayer()
        {
            Player?.Dispose();
            Player = null;

            if (_ownsLibVLC)
            {
                _libVLC?.Dispose();
            }

            _libVLC = null;
        }

        /// <summary>Stream yopiladi va torrent tez yuklab olish rejimiga qaytariladi.</summary>
        public async Task ReleaseTorrentAsync()
        {
            try
            {
                if (_item != null && _streamRequested)
                {
                    _streamRequested = false;
                    await _downloads.CloseStreamAsync(_item, _stream);
                }

                _input?.Dispose();
                _input = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Player] Torrent bo'shatilmadi: {ex}");
            }
        }

        /// <summary>Faylni o'ynatadi: to'liq yuklangan bo'lsa diskdan, aks holda yuklanish davomida stream qilib.</summary>
        private async Task PlayFileAsync(PlaylistItem entry)
        {
            var file = entry.File;

            _fileCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);

            // Buferlanayotganda boshqa faylga o'tilsa yoki player yopilsa, shu fayl bo'yicha kutish bekor bo'ladi.
            var token = _fileCancellation.Token;

            _file = file;
            _fileIndex = _item!.Manager.Files.IndexOf(file);

            foreach (var item in Playlist)
            {
                item.IsCurrent = item == entry;
            }

            OnPropertyChanged(nameof(HasNext));

            Title = HasPlaylist ? entry.Title : _torrentTitle;
            Preferences.Default.Set(LastFileKey, _fileIndex);

            // To'liq yuklangan fayl to'g'ridan-to'g'ri diskdan o'ynatiladi — eng tez va silliq yo'l.
            if (file.BitField.AllTrue && File.Exists(file.FullPath))
            {
                _media = new Media(_libVLC!, file.FullPath, FromType.FromPath);
            }
            else
            {
                StatusText = "Buferlanmoqda…";
                _streamRequested = true;

                Stream stream;

                try
                {
                    stream = await _downloads.OpenStreamAsync(_item, file, onlyThisFile: _watchOnly, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    stream.Dispose();

                    return;
                }

                _stream = stream;
                _input = new TorrentMediaInput(stream);
                _media = new Media(_libVLC!, _input);
            }

            // To'xtagan joydan davom: VLC media'ni o'sha joydan ochadi (0 dan o'ynab, keyin seek qilmaydi —
            // stream'da bu yuklanmagan joyga ikki marta sakrash va ba'zan seek'ning yo'qolishiga olib kelardi).
            _resumeMs = Preferences.Default.Get(ResumeKey, 0L);

            if (_resumeMs > 0)
            {
                _media.AddOption($":start-time={(_resumeMs / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            StatusText = "Ochilmoqda…";
            Player!.Play(_media);
        }

        private void ResetPlaybackState()
        {
            _hasStartedPlaying = false;
            _bufferPaused = false;
            _resumeMs = 0;

            ResumeNotice = null;
            ErrorMessage = null;
            IsBuffering = false;
            IsPlaying = false;
            IsLoading = true;
            StatusText = "Ochilmoqda…";
            PositionSeconds = 0;
            DurationSeconds = 1;
            PositionText = "0:00";
            DurationText = "0:00";
            BufferedFraction = 0;
        }

        private void OnPlaylistChanged()
        {
            OnPropertyChanged(nameof(HasPlaylist));
            OnPropertyChanged(nameof(HasNext));
            OnPropertyChanged(nameof(PlaylistHeader));
        }

        private void HandlePlayerState(VLCState state)
        {
            switch (state)
            {
                case VLCState.Playing when !_hasStartedPlaying:
                    OnPlaying();
                    break;
                case VLCState.Error when !HasError:
                    ErrorMessage = "Videoni o'ynatib bo'lmadi: fayl buzilgan yoki format qo'llab-quvvatlanmaydi.";
                    break;
                case VLCState.Ended when _hasStartedPlaying:
                    OnEndReached();
                    break;
            }
        }

        private void OnPlaying()
        {
            if (_hasStartedPlaying || _isClosed)
            {
                return;
            }

            _hasStartedPlaying = true;
            IsLoading = false;

            ApplyAspectMode();

            if (_resumeMs > 0)
            {
                ResumeNotice = $"{FormatTime(_resumeMs)} dan davom etilmoqda";
                _ = HideResumeNoticeAsync();
            }

            _ = LoadExternalSubtitlesAsync(_fileCancellation?.Token ?? _cancellation.Token);
        }

        private void OnEndReached()
        {
            if (_isClosed || !_hasStartedPlaying)
            {
                return;
            }

            Preferences.Default.Remove(ResumeKey);

            // Serial: qism tugagach keyingisi o'zi boshlanadi.
            if (HasNext)
            {
                _ = PlayNextAsync();

                return;
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private async Task HideResumeNoticeAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(7));
            ResumeNotice = null;
        }

        /// <summary>Torrentdagi .srt/.ass fayllari yuklanib bo'lishi bilan subtitr ro'yxatiga qo'shiladi.</summary>
        private async Task LoadExternalSubtitlesAsync(CancellationToken token)
        {
            // Serialda har qismga faqat o'zining subtitrlari qo'shiladi (fayl yoki papka nomida qism nomi bor).
            var videoName = Path.GetFileNameWithoutExtension(_file?.Path ?? string.Empty);
            var subtitles = _item?.Manager.Files
                .Where(MediaFiles.IsSubtitle)
                .Where(file => !HasPlaylist || file.Path.Contains(videoName, StringComparison.OrdinalIgnoreCase))
                .ToList() ?? new List<ITorrentManagerFile>();
            var added = new HashSet<ITorrentManagerFile>();

            try
            {
                while (!token.IsCancellationRequested && added.Count < subtitles.Count)
                {
                    foreach (var subtitle in subtitles.Where(file => !added.Contains(file) && file.BitField.AllTrue && File.Exists(file.FullPath)))
                    {
                        Player?.AddSlave(MediaSlaveType.Subtitle, new Uri(subtitle.FullPath).AbsoluteUri, select: false);
                        added.Add(subtitle);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// Stream'da VLC hech qachon ma'lumotsiz qolmasligi kerak: aks holda (ayniqsa Android MediaCodec'da)
        /// dekoder tiqilib, ovoz davom etsa ham video qotib qoladi. Shuning uchun oldinda yuklangan qism
        /// ~3 soniyadan kamaysa player pauza qilinadi va ~12 soniyalik bufer to'planganda davom ettiriladi.
        /// </summary>
        private void UpdateStreamBuffer(LibVLCSharp.Shared.MediaPlayer player)
        {
            if (_stream == null || _file == null || _item?.Manager.Torrent is not { } torrent)
            {
                return;
            }

            var lengthSeconds = player.Length / 1000.0;
            var bytesPerSecond = lengthSeconds > 0 ? _file.Length / lengthSeconds : 1024 * 1024;
            var low = Math.Clamp((long)(bytesPerSecond * 3), 2L * 1024 * 1024, 32L * 1024 * 1024);
            var high = Math.Clamp((long)(bytesPerSecond * 12), 8L * 1024 * 1024, 48L * 1024 * 1024);

            // VLC oxirgi o'qigan joydan keyin uzluksiz yuklangan baytlar.
            var readPosition = _file.OffsetInTorrent + _stream.Position;
            var firstMissing = _item.Manager.Bitfield.FirstFalse((int)(readPosition / torrent.PieceLength), _file.EndPieceIndex);
            var ahead = firstMissing < 0 ? long.MaxValue : (long)firstMissing * torrent.PieceLength - readPosition;

            if (!_bufferPaused && player.IsPlaying && ahead < low)
            {
                player.SetPause(true);
                _bufferPaused = true;
            }
            else if (_bufferPaused && ahead >= high)
            {
                player.SetPause(false);
                _bufferPaused = false;
            }
        }

        private void UpdateStreamInfo()
        {
            var manager = _item?.Manager;

            if (manager == null)
            {
                return;
            }

            var transfer = $"↓ {Formatting.Speed(manager.Monitor.DownloadRate)} · {manager.Peers.Seeds + manager.Peers.Leechs} peer";

            if (_file == null)
            {
                // Metadata kutilmoqda.
                StreamInfo = transfer;

                return;
            }

            if (_stream == null)
            {
                StreamInfo = _streamRequested ? transfer : string.Empty;

                return;
            }

            var bitfield = _file.BitField;
            StreamInfo = $"{transfer} · {100.0 * bitfield.TrueCount / Math.Max(1, bitfield.Length):0}% yuklangan";

            if (Player is { } player && bitfield.Length > 0)
            {
                var current = Math.Clamp((int)(player.Position * bitfield.Length), 0, bitfield.Length - 1);
                var firstMissing = bitfield.FirstFalse(current, bitfield.Length - 1);

                BufferedFraction = (firstMissing < 0 ? bitfield.Length : firstMissing) / (double)bitfield.Length;
            }
        }

        private void SaveResumePosition()
        {
            _lastResumeSaveAt = Environment.TickCount64;

            var player = Player;

            if (player == null || !_hasStartedPlaying || _item == null)
            {
                return;
            }

            var time = player.Time;
            var length = player.Length;

            // Boshida yoki oxirida (titrlar) to'xtatilgan bo'lsa eslab qolinmaydi.
            if (time > 30_000 && length > 0 && time < length - 120_000)
            {
                Preferences.Default.Set(ResumeKey, time);
            }
            else if (length > 0 && time >= length - 120_000)
            {
                Preferences.Default.Remove(ResumeKey);
            }
        }

        private static async Task<int> ChooseAsync(string title, IEnumerable<string> options)
        {
            var names = options.ToArray();
            var choice = await Shell.Current.DisplayActionSheetAsync(title, "Bekor qilish", null, names);

            return choice == null ? -1 : Array.IndexOf(names, choice);
        }

        private static string MarkCurrent(string name, bool isCurrent)
        {
            return isCurrent ? $"✓  {name}" : name;
        }

        private static string FormatTime(long milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));

            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }

        private void OnOverlayChanged()
        {
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ShowSpinner));
            OnPropertyChanged(nameof(ShowTransport));
        }
    }
}
