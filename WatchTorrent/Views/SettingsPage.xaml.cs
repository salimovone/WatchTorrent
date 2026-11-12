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

        private async void OpenDirectConnectionTest(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(DirectConnectionTestPage));
        }

        private async void ChooseFolder(object? sender, EventArgs e)
        {
            if (sender is not Button button) return;
            button.IsEnabled = false;
            try
            {
#if ANDROID
                if (Platform.CurrentActivity is not MainActivity activity) return;
                var path = await activity.ChooseMovieFolderAsync();
#elif WINDOWS
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.FileTypeFilter.Add("*");
                var window = (Microsoft.UI.Xaml.Window)Window!.Handler!.PlatformView!;
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
                var path = (await picker.PickSingleFolderAsync())?.Path;
#else
                string? path = null;
#endif
                if (path == null) return;
#if ANDROID
                try { _viewModel.SetDownloadDirectory(path); }
                catch (UnauthorizedAccessException) when (!OperatingSystem.IsAndroidVersionAtLeast(30)
                    || !Android.OS.Environment.IsExternalStorageManager)
                {
                    if (!await DisplayAlertAsync("Papkaga yozish uchun ruxsat kerak",
                        "Kinoni shu papkaga yuklash uchun Android sozlamalarida fayllarga kirishga ruxsat bering.", "Davom etish", "Bekor qilish")) return;
                    if (!await activity.RequestFolderAccessAsync()) return;
                    _viewModel.SetDownloadDirectory(path);
                }
#else
                _viewModel.SetDownloadDirectory(path);
#endif
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or PermissionException)
            {
                await DisplayAlertAsync("Papka tanlanmadi", "Telefon yoki kompyuterdagi yozish mumkin bo‘lgan boshqa papkani tanlang.", "Yopish");
            }
            finally { button.IsEnabled = true; }
        }

        private void RestoreFolder(object? sender, EventArgs e) => _viewModel.RestoreDefaultDirectory();

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Batareya cheklovi tizim oynasida o'zgartirilgan bo'lishi mumkin.
            _viewModel.Refresh();
        }
    }
}
