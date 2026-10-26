using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public partial class TorrentDetailPage : ContentPage
    {
        private readonly TorrentDetailViewModel _viewModel;

        public TorrentDetailPage(TorrentDetailViewModel viewModel)
        {
            InitializeComponent();

            BindingContext = _viewModel = viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            _viewModel.UpdateDownloadState();
        }
    }
}
