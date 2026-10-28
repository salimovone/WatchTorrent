using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public partial class SettingsPage : ContentPage
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsPage(SettingsViewModel viewModel)
        {
            InitializeComponent();

            BindingContext = _viewModel = viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Batareya cheklovi tizim oynasida o'zgartirilgan bo'lishi mumkin.
            _viewModel.Refresh();
        }
    }
}
