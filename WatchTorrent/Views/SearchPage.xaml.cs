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
        }

        private async void OnSearchPressed(object? sender, EventArgs e)
        {
            _viewModel.SearchCommand.Execute(null);

            await SearchInput.HideSoftInputAsync(CancellationToken.None);
        }

        private async void OnTorrentSelected(object? sender, SelectionChangedEventArgs e)
        {
            await Routes.OpenTorrentAsync(sender, e);
        }
    }
}
