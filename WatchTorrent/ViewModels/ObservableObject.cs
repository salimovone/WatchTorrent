using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WatchTorrent.ViewModels
{
    public abstract class ObservableObject : INotifyPropertyChanged, WatchTorrent.Core.Localization.ILocalized
    {
        protected ObservableObject() => WatchTorrent.Core.Localization.AppText.Register(this);
        void WatchTorrent.Core.Localization.ILocalized.LanguageChanged() => OnPropertyChanged(string.Empty);
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);

            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
