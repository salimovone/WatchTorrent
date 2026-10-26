using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public partial class DownloadsPage : ContentPage
    {
        public DownloadsPage(DownloadsViewModel viewModel)
        {
            InitializeComponent();

            BindingContext = viewModel;
        }
    }
}
