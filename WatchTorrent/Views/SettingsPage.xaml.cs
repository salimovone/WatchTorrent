using L = WatchTorrent.Core.Localization.AppText;
using WatchTorrent.ViewModels;

namespace WatchTorrent.Views
{
    public partial class SettingsPage : ContentPage
    {
        protected override bool OnBackButtonPressed() => Controls.DropdownMenu.Dismiss(this) || base.OnBackButtonPressed();

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
#elif IOS
                var root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folders = Directory.GetDirectories(root).Select(Path.GetFileName).OfType<string>().Order().ToArray();
                var create = L.Get("Yangi papka…");
                var name = await DisplayActionSheetAsync(L.Get("WatchTorrent papkalari"), L.Get("Bekor qilish"), null, folders.Append(create).ToArray());
                if (name == create) name = (await DisplayPromptAsync(L.Get("Yangi papka"), L.Get("Papka nomini kiriting.")))?.Trim();
                if (string.IsNullOrEmpty(name) || name is "." or ".." || name == L.Get("Bekor qilish") || Path.GetFileName(name) != name) return;
                var path = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
#else
                string? path = null;
#endif
                if (path == null) return;
#if ANDROID
                try { _viewModel.SetDownloadDirectory(path); }
                catch (UnauthorizedAccessException) when (!OperatingSystem.IsAndroidVersionAtLeast(30)
                    || !Android.OS.Environment.IsExternalStorageManager)
                {
                    if (!await DisplayAlertAsync(L.Get("Papkaga yozish uchun ruxsat kerak"),
                        L.Get("Kinoni shu papkaga yuklash uchun Android sozlamalarida fayllarga kirishga ruxsat bering."), L.Get("Davom etish"), L.Get("Bekor qilish"))) return;
                    if (!await activity.RequestFolderAccessAsync()) return;
                    _viewModel.SetDownloadDirectory(path);
                }
#else
                _viewModel.SetDownloadDirectory(path);
#endif
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or PermissionException)
            {
                await DisplayAlertAsync(L.Get("Papka tanlanmadi"), L.Get("Telefon yoki kompyuterdagi yozish mumkin bo‘lgan boshqa papkani tanlang."), L.Get("Yopish"));
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
