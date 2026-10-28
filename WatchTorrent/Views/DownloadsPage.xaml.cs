using WatchTorrent.Services;
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

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Sahifa ochiq va yuklash ketayotganda ekran o'chmaydi (Sozlamalarda o'chirish mumkin).
            DownloadKeepAlive.SetPageVisible(true);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            DownloadKeepAlive.SetPageVisible(false);
        }
    }
}
