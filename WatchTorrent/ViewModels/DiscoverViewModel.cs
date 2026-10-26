using TorrentReader;
using TorrentReader.Providers;
using TorrentReader.Search.Models;
using WatchTorrent.Services;

namespace WatchTorrent.ViewModels
{
    public class DiscoverViewModel : ObservableObject
    {
        private const string EmptyListMessage = "Sayt hozircha ro'yxatni qaytarmadi. Birozdan so'ng qayta urinib ko'ring.";

        private readonly TorrentClient _client;
        private readonly Dictionary<string, IReadOnlyList<SearchResultItem>> _cache = new();
        private DiscoverCategory _selectedCategory;
        private RangeOption _selectedRange;
        private IReadOnlyList<SearchResultItem> _items = Array.Empty<SearchResultItem>();
        private bool _isBusy;
        private bool _isRefreshing;
        private string? _errorMessage;
        private int _loadVersion;

        public DiscoverViewModel(TorrentClient client)
        {
            _client = client;

            Categories = new[]
            {
                new DiscoverCategory("Trendda", true, (c, range) => c.GetTrendingAsync(range)),
                new DiscoverCategory("Mashhur kinolar", true, (c, range) => c.GetPopularMoviesAsync(range)),
                new DiscoverCategory("Trend kinolar", true, (c, range) => c.GetTrendingMoviesAsync(range)),
                new DiscoverCategory("Xorijiy kinolar", true, (c, range) => c.GetPopularForeignMoviesAsync(range)),
                new DiscoverCategory("Top 100 kinolar", false, (c, _) => c.GetTop100MoviesAsync()),
                new DiscoverCategory("Mashhur o'yinlar", true, (c, range) => c.GetPopularGamesAsync(range)),
                new DiscoverCategory("Trend o'yinlar", true, (c, range) => c.GetTrendingGamesAsync(range)),
                new DiscoverCategory("Top 100 o'yinlar", false, (c, _) => c.GetTop100GamesAsync())
            };

            Ranges = new[]
            {
                new RangeOption("Bugun", TorrentPopularityRange.Today),
                new RangeOption("Shu hafta", TorrentPopularityRange.Week)
            };

            _selectedCategory = Categories[0];
            _selectedRange = Ranges[0];

            RefreshCommand = new Command(async () => await LoadAsync(force: true));
        }

        public IReadOnlyList<DiscoverCategory> Categories { get; }

        public IReadOnlyList<RangeOption> Ranges { get; }

        public DiscoverCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (value != null && SetProperty(ref _selectedCategory, value))
                {
                    OnPropertyChanged(nameof(ShowRanges));
                    _ = LoadAsync();
                }
            }
        }

        public RangeOption SelectedRange
        {
            get => _selectedRange;
            set
            {
                if (value != null && SetProperty(ref _selectedRange, value))
                {
                    _ = LoadAsync();
                }
            }
        }

        public bool ShowRanges => SelectedCategory.HasRange;

        public IReadOnlyList<SearchResultItem> Items
        {
            get => _items;
            private set => SetProperty(ref _items, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>RefreshView bilan ikki tomonlama bog'langan (pastga tortib yangilash).</summary>
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => ErrorMessage != null;

        public Command RefreshCommand { get; }

        public Task InitializeAsync()
        {
            return Items.Count == 0 && !IsBusy ? LoadAsync() : Task.CompletedTask;
        }

        private async Task LoadAsync(bool force = false)
        {
            var version = ++_loadVersion;
            var category = SelectedCategory;
            var range = SelectedRange.Range;
            var cacheKey = category.HasRange ? $"{category.Title}|{range}" : category.Title;

            ErrorMessage = null;

            if (!force && _cache.TryGetValue(cacheKey, out var cached))
            {
                Items = cached;
                IsBusy = false;
                IsRefreshing = false;

                return;
            }

            if (!force)
            {
                Items = Array.Empty<SearchResultItem>();
            }

            // Pastga tortib yangilashda RefreshView o'z indikatorini ko'rsatadi.
            IsBusy = !IsRefreshing;

            try
            {
                var items = await category.Load(_client, range);

                // Sayt vaqti-vaqti bilan 200 status bilan "Error something went wrong" sahifasini qaytaradi,
                // kutubxona esa uni bo'sh ro'yxat qilib beradi. Bu ro'yxatlar aslida hech qachon bo'sh bo'lmaydi,
                // shuning uchun bo'sh natija keshlanmaydi va xato sifatida ko'rsatiladi.
                if (items.Count > 0)
                {
                    _cache[cacheKey] = items;
                }

                if (version == _loadVersion)
                {
                    Items = items;
                    ErrorMessage = items.Count > 0 ? null : EmptyListMessage;
                }
            }
            catch (Exception ex)
            {
                if (version == _loadVersion)
                {
                    Items = Array.Empty<SearchResultItem>();
                    ErrorMessage = Formatting.Error(ex);
                }
            }
            finally
            {
                if (version == _loadVersion)
                {
                    IsBusy = false;
                    IsRefreshing = false;
                }
            }
        }
    }
}
