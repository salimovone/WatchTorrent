using L = WatchTorrent.Core.Localization.AppText;
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
            service.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(service.ConnectionStatus)) OnPropertyChanged(nameof(EngineStatus)); };
            _service.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
            service.PartnerDownloads.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));

            ToggleCommand = new Command<DownloadItem>(async item => await ToggleAsync(item));
            RemoveCommand = new Command<DownloadItem>(async item => await RemoveAsync(item));
            OpenCommand = new Command<DownloadItem>(async item => await OpenAsync(item));
            WatchCommand = new Command<DownloadItem>(async item => await Routes.WatchAsync(item));
        }

        public ObservableCollection<DownloadItem> Items => _service.Items;

        public ObservableCollection<PartnerDownload> PartnerDownloads => _service.PartnerDownloads;
        public bool IsEmpty => Items.Count == 0 && PartnerDownloads.Count == 0;

        public string DownloadDirectory => _service.DownloadDirectory;
        public string EngineStatus =>
            L.Get(_service.ConnectionStatus);

        public Command<DownloadItem> ToggleCommand { get; }

        public Command<DownloadItem> RemoveCommand { get; }

        public Command<DownloadItem> OpenCommand { get; }

        public Command<DownloadItem> WatchCommand { get; }

        private async Task ToggleAsync(DownloadItem item)
        {
            if (item == null || item.IsBusy || item.IsComplete) return;
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
                await Shell.Current.DisplayAlertAsync(L.Get("Xatolik"), ex.Message, "OK");
            }
        }

        private async Task RemoveAsync(DownloadItem item)
        {
            if (item == null || item.IsBusy) return;
            var keepFiles = L.Get(RemoveKeepFiles);
            var deleteFiles = L.Get(RemoveWithFiles);
            var choice = await Shell.Current.DisplayActionSheetAsync(
                L.F("S001", item.Name),
                L.Get("Bekor qilish"),
                deleteFiles,
                keepFiles);

            if (choice != keepFiles && choice != deleteFiles)
            {
                return;
            }

            try
            {
                await _service.RemoveAsync(item, deleteFiles: choice == deleteFiles);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlertAsync(L.Get("O'chirib bo'lmadi"), ex.Message, "OK");
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
                await Shell.Current.DisplayAlertAsync(L.Get("Ochib bo'lmadi"), ex.Message, "OK");
            }
        }
    }
}
