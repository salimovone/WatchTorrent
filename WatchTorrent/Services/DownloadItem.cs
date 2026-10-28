using MonoTorrent;
using MonoTorrent.Client;
using WatchTorrent.ViewModels;

namespace WatchTorrent.Services
{
    /// <summary>Bitta yuklamaning (TorrentManager) UI uchun ko'rinishi. <see cref="Refresh"/> har soniyada chaqiriladi.</summary>
    public class DownloadItem : ObservableObject
    {
        private readonly string? _fallbackName;
        private string _name = string.Empty;
        private double _progress;
        private string _progressText = string.Empty;
        private string _statusText = string.Empty;
        private string _sizeText = string.Empty;
        private string _transferText = string.Empty;
        private bool _isRunning;
        private bool _isComplete;

        public DownloadItem(TorrentManager manager, string? fallbackName)
        {
            Manager = manager;
            InfoHash = manager.InfoHashes.V1OrV2.ToHex();
            _fallbackName = fallbackName;

            Refresh();
        }

        public TorrentManager Manager { get; }

        public string InfoHash { get; }

        public string Name
        {
            get => _name;
            private set => SetProperty(ref _name, value);
        }

        /// <summary>0..1 oralig'ida (ProgressBar uchun).</summary>
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string SizeText
        {
            get => _sizeText;
            private set => SetProperty(ref _sizeText, value);
        }

        public string TransferText
        {
            get => _transferText;
            private set => SetProperty(ref _transferText, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(ToggleText));
                }
            }
        }

        public bool IsComplete
        {
            get => _isComplete;
            private set
            {
                if (SetProperty(ref _isComplete, value))
                {
                    OnPropertyChanged(nameof(CanToggle));
                }
            }
        }

        public bool CanToggle => !IsComplete;

        /// <summary>Tanlangan fayllar hajmi (bayt) — fon xizmati bildirishnomasidagi umumiy foiz uchun.</summary>
        public long TotalBytes { get; private set; }

        public long DownloadedBytes { get; private set; }

        public string ToggleText => IsRunning ? "Pauza" : "Davom ettirish";

        public void Refresh()
        {
            var manager = Manager;

            // Faqat bitta film tanlangan bo'lsa (qolganlari DoNotDownload), progress va hajm shu tanlov bo'yicha.
            var size = manager.HasMetadata
                ? manager.Files.Where(file => file.Priority != Priority.DoNotDownload).Sum(file => file.Length)
                : manager.MagnetLink?.Size ?? 0;
            var progress = manager.PartialProgress;
            var downloaded = (long)(size * progress / 100);

            TotalBytes = size;
            DownloadedBytes = downloaded;
            Name = string.IsNullOrEmpty(manager.Name) ? _fallbackName ?? InfoHash : manager.Name;
            Progress = progress / 100;
            ProgressText = $"{progress:0.0}%";
            IsComplete = manager.HasMetadata && progress >= 100;
            IsRunning = manager.State is not (TorrentState.Stopped or TorrentState.Paused or TorrentState.Error);
            SizeText = size > 0 ? $"{Formatting.Bytes(downloaded)} / {Formatting.Bytes(size)}" : string.Empty;
            StatusText = GetStatusText(manager, IsComplete, size - downloaded);
            TransferText = IsRunning && !IsComplete
                ? $"↓ {Formatting.Speed(manager.Monitor.DownloadRate)}   ↑ {Formatting.Speed(manager.Monitor.UploadRate)}   ·   {manager.Peers.Seeds} seed, {manager.Peers.Leechs} peer"
                : string.Empty;
        }

        private static string GetStatusText(TorrentManager manager, bool isComplete, long remainingBytes)
        {
            if (isComplete && manager.State is TorrentState.Stopped or TorrentState.Stopping or TorrentState.Seeding)
            {
                return "Yuklab bo'lindi";
            }

            return manager.State switch
            {
                TorrentState.Metadata => "Torrent ma'lumoti olinmoqda…",
                TorrentState.Starting or TorrentState.FetchingHashes => "Boshlanmoqda…",
                TorrentState.Hashing or TorrentState.HashingPaused => "Fayllar tekshirilmoqda…",
                TorrentState.Downloading => GetDownloadingText(manager.Monitor.DownloadRate, remainingBytes),
                TorrentState.Stopping => "To'xtatilmoqda…",
                TorrentState.Stopped or TorrentState.Paused => "Pauzada",
                TorrentState.Error => $"Xato: {manager.Error?.Exception?.Message ?? manager.Error?.Reason.ToString()}",
                _ => manager.State.ToString()
            };
        }

        private static string GetDownloadingText(long rate, long remainingBytes)
        {
            if (rate <= 0)
            {
                return "Peerlar qidirilmoqda…";
            }

            return $"Yuklanmoqda · {Formatting.Duration(TimeSpan.FromSeconds(remainingBytes / rate))} qoldi";
        }
    }
}
