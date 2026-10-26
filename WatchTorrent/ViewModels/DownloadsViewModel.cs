using System.Collections.ObjectModel;
using WatchTorrent.Services;
using WatchTorrent.Views;

namespace WatchTorrent.ViewModels
{
    public class DownloadsViewModel : ObservableObject
    {
        private const string RemoveKeepFiles = "Faqat ro'yxatdan";
        private const string RemoveWithFiles = "Fayllari bilan birga";

        private readonly DownloadService _service;

        public DownloadsViewModel(DownloadService service)
        {
            _service = service;
            _service.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));

            ToggleCommand = new Command<DownloadItem>(async item => await ToggleAsync(item));
            RemoveCommand = new Command<DownloadItem>(async item => await RemoveAsync(item));
            OpenCommand = new Command<DownloadItem>(async item => await OpenAsync(item));
            WatchCommand = new Command<DownloadItem>(async item => await Routes.WatchAsync(item));
        }

        public ObservableCollection<DownloadItem> Items => _service.Items;

        public bool IsEmpty => Items.Count == 0;

        public string DownloadDirectory => _service.DownloadDirectory;

        public Command<DownloadItem> ToggleCommand { get; }

        public Command<DownloadItem> RemoveCommand { get; }

        public Command<DownloadItem> OpenCommand { get; }

        public Command<DownloadItem> WatchCommand { get; }

        private async Task ToggleAsync(DownloadItem item)
        {
            try
            {
                if (item.IsRunning)
                {
                    await _service.PauseAsync(item);
                }
                else
                {
                    await _service.ResumeAsync(item);
                }
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("Xatolik", ex.Message, "OK");
            }
        }

        private async Task RemoveAsync(DownloadItem item)
        {
            var choice = await Shell.Current.DisplayActionSheetAsync(
                $"\"{item.Name}\" o'chirilsinmi?",
                "Bekor qilish",
                RemoveWithFiles,
                RemoveKeepFiles);

            if (choice != RemoveKeepFiles && choice != RemoveWithFiles)
            {
                return;
            }

            try
            {
                await _service.RemoveAsync(item, deleteFiles: choice == RemoveWithFiles);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("O'chirib bo'lmadi", ex.Message, "OK");
            }
        }

        private static async Task OpenAsync(DownloadItem item)
        {
            try
            {
                await DownloadLauncher.OpenAsync(item);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync("Ochib bo'lmadi", ex.Message, "OK");
            }
        }
    }
}
