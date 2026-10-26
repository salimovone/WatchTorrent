using System.Collections.ObjectModel;
using TorrentReader;
using TorrentReader.Search;
using TorrentReader.Search.Models;
using WatchTorrent.Services;

namespace WatchTorrent.ViewModels
{
    public class SearchViewModel : ObservableObject
    {
        private readonly TorrentClient _client;
        private string _query = string.Empty;
        private string? _activeQuery;
        private SortOption _selectedSort;
        private bool _isBusy;
        private bool _isLoadingMore;
        private string? _errorMessage;
        private int _currentPage;
        private int _totalPages;
        private int _searchVersion;

        public SearchViewModel(TorrentClient client)
        {
            _client = client;

            SortOptions = new[]
            {
                new SortOption("Eng yangi", SearchSortByType.Time, SortOrderType.Descending),
                new SortOption("Ko'p seed", SearchSortByType.Seeders, SortOrderType.Descending),
                new SortOption("Ko'p leech", SearchSortByType.Leechers, SortOrderType.Descending),
                new SortOption("Eng katta", SearchSortByType.Size, SortOrderType.Descending),
                new SortOption("Eng kichik", SearchSortByType.Size, SortOrderType.Ascending)
            };

            _selectedSort = SortOptions[0];

            SearchCommand = new Command(async () => await SearchAsync());
            LoadMoreCommand = new Command(async () => await LoadMoreAsync());
            RetryCommand = new Command(async () =>
            {
                if (Items.Count == 0)
                {
                    await SearchAsync();
                }
                else
                {
                    await LoadMoreAsync();
                }
            });
        }

        public string Query
        {
            get => _query;
            set => SetProperty(ref _query, value);
        }

        public IReadOnlyList<SortOption> SortOptions { get; }

        public SortOption SelectedSort
        {
            get => _selectedSort;
            set
            {
                if (value != null && SetProperty(ref _selectedSort, value) && _activeQuery != null)
                {
                    _ = SearchAsync(_activeQuery);
                }
            }
        }

        public ObservableCollection<SearchResultItem> Items { get; } = new();

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnStateChanged();
                }
            }
        }

        public bool IsLoadingMore
        {
            get => _isLoadingMore;
            private set => SetProperty(ref _isLoadingMore, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnStateChanged();
                }
            }
        }

        public bool HasError => ErrorMessage != null;

        /// <summary>Hali hech narsa qidirilmagan.</summary>
        public bool ShowHint => _activeQuery == null && !IsBusy;

        public bool IsEmpty => _activeQuery != null && !IsBusy && !HasError && Items.Count == 0;

        public string Summary => _totalPages > 0 ? $"{Items.Count} ta natija · {_currentPage}/{_totalPages} sahifa" : string.Empty;

        public Command SearchCommand { get; }

        public Command LoadMoreCommand { get; }

        public Command RetryCommand { get; }

        private Task SearchAsync()
        {
            var query = Query.Trim();

            return query.Length == 0 ? Task.CompletedTask : SearchAsync(query);
        }

        private async Task SearchAsync(string query)
        {
            _searchVersion++;
            _activeQuery = query;
            _currentPage = 0;
            _totalPages = 0;
            Items.Clear();

            await LoadPageAsync(1);
        }

        private async Task LoadMoreAsync()
        {
            if (_activeQuery == null || IsBusy || IsLoadingMore || _currentPage >= _totalPages)
            {
                return;
            }

            await LoadPageAsync(_currentPage + 1);
        }

        private async Task LoadPageAsync(int page)
        {
            var version = _searchVersion;
            var query = _activeQuery!;
            var sort = SelectedSort;

            ErrorMessage = null;

            if (page == 1)
            {
                IsBusy = true;
            }
            else
            {
                IsLoadingMore = true;
            }

            try
            {
                var result = await _client.SearchAsync(query, page, sort.SortBy, sort.Order);

                if (version != _searchVersion)
                {
                    return;
                }

                foreach (var item in result.Items)
                {
                    Items.Add(item);
                }

                _currentPage = page;
                _totalPages = result.AmountPages;
            }
            catch (Exception ex)
            {
                if (version == _searchVersion)
                {
                    ErrorMessage = Formatting.Error(ex);
                }
            }
            finally
            {
                if (version == _searchVersion)
                {
                    IsBusy = false;
                    IsLoadingMore = false;
                    OnStateChanged();
                }
            }
        }

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ShowHint));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Summary));
        }
    }
}
