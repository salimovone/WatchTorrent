using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public partial class DiscoverPage : ContentPage
    {
        private readonly DiscoverViewModel _viewModel;

        public DiscoverPage(DiscoverViewModel viewModel)
        {
            InitializeComponent();

            BindingContext = _viewModel = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _viewModel.InitializeAsync();
        }

        private async void OnTorrentSelected(object? sender, SelectionChangedEventArgs e)
        {
            await Routes.OpenTorrentAsync(sender, e);
        }
    }
}
