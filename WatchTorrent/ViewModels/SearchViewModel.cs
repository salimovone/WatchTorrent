using L = WatchTorrent.Core.Localization.AppText;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using TorrentReader;
using TorrentReader.Search;
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
                new SortOption(L.Get("Eng yangi"), SearchSortByType.Time, SortOrderType.Descending),
                new SortOption(L.Get("Ko'p manbali"), SearchSortByType.Seeders, SortOrderType.Descending),
                new SortOption(L.Get("Ko'p yuklanayotgan"), SearchSortByType.Leechers, SortOrderType.Descending),
                new SortOption(L.Get("Eng katta"), SearchSortByType.Size, SortOrderType.Descending),
                new SortOption(L.Get("Eng kichik"), SearchSortByType.Size, SortOrderType.Ascending)
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
            set { if (SetProperty(ref _query, value)) OnPropertyChanged(nameof(HasQuery)); }
        }

        public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

        public event EventHandler? ResultsReplaced;

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

        private ResultCollection _items = new();
        public IReadOnlyList<TorrentRow> Items => _items;

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
            get => L.Get(_errorMessage);
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

        public bool HasResults => Items.Count > 0;
        public bool HasInitialError => HasError && !HasResults;
        public bool HasMoreError => HasError && HasResults;
        public string Summary => HasResults ? L.F("ResultsCount", Items.Count) : string.Empty;

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
            SetProperty(ref _items, new(), nameof(Items));

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
                var rows = await Task.Run(() => result.Items.Select(item => new TorrentRow(item)).ToArray());

                if (version != _searchVersion)
                {
                    return;
                }

                _currentPage = page;
                _totalPages = result.AmountPages;
                _items.AppendPage(rows);
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
                    if (page == 1 && HasResults) ResultsReplaced?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasInitialError));
            OnPropertyChanged(nameof(HasMoreError));
            OnPropertyChanged(nameof(ShowHint));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Summary));
        }

        private sealed class ResultCollection : ObservableCollection<TorrentRow>
        {
            public void AppendPage(TorrentRow[] rows)
            {
                if (rows.Length == 0) return;

                CheckReentrancy();
                int start = Count;
                foreach (var row in rows)
                {
                    Items.Add(row);
                }

                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, rows, start));
            }
        }
    }
}
