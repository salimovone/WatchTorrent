using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public partial class SearchPage : ContentPage
    {
        private readonly SearchViewModel _viewModel;

        public SearchPage(SearchViewModel viewModel)
        {
            InitializeComponent();

            BindingContext = _viewModel = viewModel;
            _viewModel.ResultsReplaced += OnResultsReplaced;
        }

        private async void OnSearchPressed(object? sender, EventArgs e)
        {
            await SearchInput.HideSoftInputAsync(CancellationToken.None);
            SearchInput.Unfocus();
            _viewModel.SearchCommand.Execute(null);
        }

        private void OnResultsReplaced(object? sender, EventArgs e)
        {
            var first = _viewModel.Items.FirstOrDefault();
            Dispatcher.Dispatch(() =>
            {
                if (first != null && first == _viewModel.Items.FirstOrDefault())
                    ResultsList.ScrollTo(first, position: ScrollToPosition.Start, animate: false);
            });
        }

        private async void OnTorrentSelected(object? sender, SelectionChangedEventArgs e)
        {
            await Routes.OpenTorrentAsync(sender, e);
        }
    }
}
