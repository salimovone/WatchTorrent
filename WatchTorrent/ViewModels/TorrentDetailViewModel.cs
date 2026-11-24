using L = WatchTorrent.Core.Localization.AppText;
using WatchTorrent.Models;
using System.Net;
using System.Text.RegularExpressions;
using WatchTorrent.Core;
using TorrentReader;
using TorrentReader.Search.Models;
using WatchTorrent.Services;
using WatchTorrent.Views;
using TorrentModel = TorrentReader.Torrent.Models.Torrent;

namespace WatchTorrent.ViewModels
{
    public class TorrentDetailViewModel : ObservableObject, IQueryAttributable
    {
        private const int FilesPreviewCount = 10;
        private const string CopyMagnetDefaultText = "Magnetni nusxalash";

        private static readonly Regex LineBreakRegex = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

        private readonly TorrentClient _client;
        private readonly DownloadService _downloads;
        private readonly PosterService _posters;
        private readonly TorrentPageCache _pages;
        private DownloadItem? _download;
        private bool _showTechnicalDetails;
        private SearchResultItem? _item;
        private string? _posterUrl;
        private string? _posterFullUrl;
        private string _title = string.Empty;
        private string _size = string.Empty;
        private int _seeders;
        private int _leechers;
        private TorrentInfo? _detail;
        private bool _showAllFiles;
        private bool _isLoading;
        private string? _errorMessage;
        private bool _isInDownloads;
        private bool _isAddingDownload;
        private string _copyMagnetText = CopyMagnetDefaultText;

        public TorrentDetailViewModel(TorrentClient client, DownloadService downloads, PosterService posters, TorrentPageCache pages)
        {
            _client = client;
            _downloads = downloads;
            _posters = posters;
            _pages = pages;
            ToggleTechnicalDetailsCommand = new Command(() => ShowTechnicalDetails = !ShowTechnicalDetails);

            WatchCommand = new Command(async () => await WatchAsync());
            DownloadCommand = new Command(async () => await DownloadAsync());
            CopyMagnetCommand = new Command(async () => await CopyMagnetAsync());
            OpenExternalCommand = new Command(async () => await OpenExternalAsync());
            ShowDescriptionCommand = new Command(async () => await ShowDescriptionAsync());
            ShowAllFilesCommand = new Command(() => ShowAllFiles = true);
            RetryCommand = new Command(async () => await LoadAsync());
            OpenPosterCommand = new Command(async () => await OpenPosterAsync());
        }

        public string? PosterUrl
        {
            get => _posterUrl;
            private set
            {
                if (SetProperty(ref _posterUrl, value))
                {
                    OnPropertyChanged(nameof(HasPoster));
                }
            }
        }

        public bool HasPoster => PosterUrl != null;

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public string Size
        {
            get => _size;
            private set => SetProperty(ref _size, value);
        }

        public int Seeders
        {
            get => _seeders;
            private set => SetProperty(ref _seeders, value);
        }

        public int Leechers
        {
            get => _leechers;
            private set => SetProperty(ref _leechers, value);
        }

        public TorrentInfo? Detail
        {
            get => _detail;
            private set
            {
                if (SetProperty(ref _detail, value))
                {
                    OnPropertyChanged(nameof(IsLoaded));
                    OnPropertyChanged(nameof(DisplayDetail));
                    OnFilesChanged();
                }
            }
        }

        public Command ToggleTechnicalDetailsCommand { get; }
        public bool ShowTechnicalDetails
        {
            get => _showTechnicalDetails;
            private set { if (SetProperty(ref _showTechnicalDetails, value)) OnPropertyChanged(nameof(TechnicalDetailsText)); }
        }
        public string TechnicalDetailsText => L.Get(ShowTechnicalDetails ? "Texnik ma'lumotni yopish −" : "Texnik ma'lumot +");

        public bool IsLoaded => Detail != null;

        // Compiled XAML needs non-null value types while metadata is still loading.
        private static readonly TorrentInfo EmptyDetail = new(null, [], "", "", "", 0, "", "", "", [], []);
        public TorrentInfo DisplayDetail => Detail ?? EmptyDetail;

        public bool ShowAllFiles
        {
            get => _showAllFiles;
            private set
            {
                if (SetProperty(ref _showAllFiles, value))
                {
                    OnFilesChanged();
                }
            }
        }

        public IReadOnlyList<string> VisibleFiles
        {
            get
            {
                var files = Detail?.Files ?? Array.Empty<string>();

                return ShowAllFiles || files.Count <= FilesPreviewCount ? files : files.Take(FilesPreviewCount).ToList();
            }
        }

        public string FilesHeader => L.Get(L.F("S047", Detail?.Files.Count ?? 0));

        public bool CanShowAllFiles => !ShowAllFiles && (Detail?.Files.Count ?? 0) > FilesPreviewCount;

        public string ShowAllFilesText => L.Get(L.F("S010", Detail?.Files.Count ?? 0));

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public string? ErrorMessage
        {
            get => L.Get(_errorMessage);
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => ErrorMessage != null;

        public bool IsInDownloads
        {
            get => _isInDownloads;
            private set
            {
                if (SetProperty(ref _isInDownloads, value))
                {
                    OnPropertyChanged(nameof(DownloadButtonText));
                }
            }
        }

        public bool IsAddingDownload
        {
            get => _isAddingDownload;
            private set
            {
                if (SetProperty(ref _isAddingDownload, value))
                {
                    OnPropertyChanged(nameof(DownloadButtonText));
                }
            }
        }

        public string DownloadButtonText => L.Get(IsAddingDownload
            ? "Qo'shilmoqda…"
            : IsInDownloads ? L.Get("Yuklamalarda ko'rish") : L.Get("Yuklab olish"));

        public string CopyMagnetText
        {
            get => L.Get(_copyMagnetText);
            private set => SetProperty(ref _copyMagnetText, value);
        }

        public Command WatchCommand { get; }

        public Command DownloadCommand { get; }

        public Command CopyMagnetCommand { get; }

        public Command OpenExternalCommand { get; }

        public Command ShowDescriptionCommand { get; }

        public Command ShowAllFilesCommand { get; }

        public Command RetryCommand { get; }

        public Command OpenPosterCommand { get; }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("Download", out var selected) && selected is DownloadItem download)
            {
                _download = download;
                _ = LoadDownloadAsync(download);
                return;
            }
            if (!query.TryGetValue("Item", out var value) || value is not SearchResultItem item)
            {
                return;
            }

            _item = item;
            Title = Decode(item.Name);
            Size = item.Size;
            Seeders = item.AmountSeeders;
            Leechers = item.AmountLeechers;

            _ = LoadPosterAsync(item.Name);
            _ = LoadAsync();
        }

        /// <summary>Sahifaga qaytilganda (yuklama o'chirilgan bo'lishi mumkin) tugma holatini yangilaydi.</summary>
        public void UpdateDownloadState()
        {
            IsInDownloads = Detail != null && _downloads.Contains(Detail.InfoHash);
        }

        private Task SavePageAsync() => Detail == null || _item == null ? Task.CompletedTask :
            _pages.SaveAsync(new TorrentPageData(_item, Detail, Title, Size, Seeders, Leechers, _posterFullUrl ?? PosterUrl ?? Detail.ImageUrl));

        private async Task LoadDownloadAsync(DownloadItem download)
        {
            var manager = download.Manager;
            Title = Decode(download.Name);
            Size = download.TotalBytes > 0 ? Formatting.Bytes(download.TotalBytes) : "—";
            Seeders = manager.Peers.Seeds;
            Leechers = manager.Peers.Leechs;
            Detail = new TorrentInfo(null, [], "—", "—", "—", 0, download.InfoHash, manager.MagnetLink.ToV1String(),
                string.Empty, manager.Files.Select(file => file.Path).ToArray(), []);
            UpdateDownloadState();
            var page = await _pages.LoadAsync(download.InfoHash);
            if (page != null)
            {
                _item = page.Source;
                Title = page.Title;
                Size = page.Size;
                Seeders = page.Seeders;
                Leechers = page.Leechers;
                Detail = page.Detail;
                PosterUrl = _posterFullUrl = page.PosterUrl ?? page.Detail.ImageUrl;
            }
            if (PosterUrl == null) await LoadPosterAsync(download.Name);
        }

        private async Task LoadAsync()
        {
            if (_item == null || IsLoading)
            {
                return;
            }

            IsLoading = true;
            ErrorMessage = null;

            try
            {
                var torrent = await _client.GetTorrentAsync(_item);

                Title = Decode(torrent.Detail.Title);
                Size = torrent.Detail.TotalSize;
                Seeders = torrent.Detail.AmountSeeders;
                Leechers = torrent.Detail.AmountLeechers;
                Detail = CreateInfo(torrent);

                // IMDb posteri topilmagan bo'lsa, 1337x sahifasidagi rasm ishlatiladi.
                if (PosterUrl == null && Detail.ImageUrl != null)
                {
                    PosterUrl = _posterFullUrl = Detail.ImageUrl;
                }

                UpdateDownloadState();
                if (IsInDownloads) await SavePageAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = Formatting.Error(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>IMDb posteri (yuqori sifatli) — ro'yxatdagi bilan bir xil bo'lishi uchun birinchi navbatda.</summary>
        private async Task LoadPosterAsync(string torrentName)
        {
            var poster = await _posters.FindAsync(torrentName);

            if (poster != null)
            {
                _posterFullUrl = poster.Sized(1600);
                PosterUrl = poster.Sized(600);
            }
        }

        private async Task OpenPosterAsync()
        {
            if (_posterFullUrl == null)
            {
                return;
            }

            await Shell.Current.GoToAsync(nameof(ImageViewerPage), new ShellNavigationQueryParameters
            {
                ["Url"] = _posterFullUrl,
                ["Title"] = Title
            });
        }

        private async Task WatchAsync()
        {
            if (Detail != null)
            {
                await SavePageAsync();
                var page = new TorrentPageData(_item, Detail, Title, Size, Seeders, Leechers, PosterUrl ?? Detail.ImageUrl);
                if (_download != null) await Routes.WatchAsync(_download, page);
                else await Routes.WatchAsync(Detail.Magnet, Title, page);
            }
        }

        private async Task DownloadAsync()
        {
            if (Detail == null || IsAddingDownload)
            {
                return;
            }

            if (IsInDownloads)
            {
                await Shell.Current.GoToAsync(Routes.Downloads);

                return;
            }

            IsAddingDownload = true;

            try
            {
                await _downloads.AddAsync(Detail.Magnet, Title);
                await SavePageAsync();

                UpdateDownloadState();
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync(L.Get("Yuklab bo'lmadi"), ex.Message, "OK");
            }
            finally
            {
                IsAddingDownload = false;
            }
        }

        private async Task CopyMagnetAsync()
        {
            if (Detail == null)
            {
                return;
            }

            await Clipboard.Default.SetTextAsync(Detail.Magnet);

            CopyMagnetText = L.Get("Nusxalandi");
            await Task.Delay(2000);
            CopyMagnetText = CopyMagnetDefaultText;
        }

        private async Task OpenExternalAsync()
        {
            if (Detail == null)
            {
                return;
            }

            bool opened;

            try
            {
                opened = await Launcher.Default.TryOpenAsync(new Uri(Detail.Magnet));
            }
            catch (Exception)
            {
                opened = false;
            }

            if (!opened)
            {
                await Shell.Current.DisplayAlertAsync(L.Get("Ilova topilmadi"), L.Get("Magnet havolani ochadigan torrent ilovasi o'rnatilmagan."), "OK");
            }
        }

        private async Task ShowDescriptionAsync()
        {
            if (Detail == null)
            {
                return;
            }

            await Shell.Current.GoToAsync(nameof(DescriptionPage), new ShellNavigationQueryParameters
            {
                ["Title"] = Title,
                ["Html"] = Detail.DescriptionHtml
            });
        }

        private void OnFilesChanged()
        {
            OnPropertyChanged(nameof(VisibleFiles));
            OnPropertyChanged(nameof(FilesHeader));
            OnPropertyChanged(nameof(CanShowAllFiles));
            OnPropertyChanged(nameof(ShowAllFilesText));
        }

        private static TorrentInfo CreateInfo(TorrentModel torrent)
        {
            var detail = torrent.Detail;
            var magnet = WebUtility.HtmlDecode(detail.MagnetDownload);

            // DownloadService torrentlarni magnet'dagi hash bo'yicha taniydi, sahifadagi matn bo'yicha emas.
            var infoHash = Magnet.TryParse(magnet, out var link) ? link!.Hash : detail.InfoHash.Trim();

            var tags = new[] { detail.Category, detail.Type, detail.Language }
                .Select(Decode)
                .Where(tag => tag.Length > 0 && tag != "--")
                .Distinct()
                .ToList();

            var comments = (torrent.Comments ?? Array.Empty<TorrentReader.Torrent.Models.TorrentComment>())
                .Select(comment => new CommentItem(Decode(comment.Username), StripHtml(comment.Comment), Decode(comment.Posted)))
                .Where(comment => comment.Text.Length > 0)
                .ToList();

            return new TorrentInfo(
                Uri.TryCreate(detail.ImageUrl, UriKind.Absolute, out _) ? detail.ImageUrl : null,
                tags,
                Decode(detail.UploadedBy),
                Decode(detail.DateUploaded),
                Decode(detail.DateLastChecked),
                detail.AmountDownloads,
                infoHash,
                magnet,
                detail.HtmlDescription ?? string.Empty,
                (detail.Files ?? Array.Empty<string>()).Select(Decode).ToList(),
                comments);
        }

        private static string Decode(string? text)
        {
            return WebUtility.HtmlDecode(text ?? string.Empty).Trim();
        }

        private static string StripHtml(string? html)
        {
            var text = LineBreakRegex.Replace(html ?? string.Empty, "\n");

            return Decode(TagRegex.Replace(text, string.Empty));
        }
    }
}
