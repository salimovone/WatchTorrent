using MonoTorrent;
using WatchTorrent.Services;

namespace WatchTorrent.ViewModels
{
    /// <summary>Player ichidagi fayllar ro'yxatining bitta elementi (serial qismi va h.k.).</summary>
    public class PlaylistItem : ObservableObject
    {
        private bool _isCurrent;
        private string _details = string.Empty;

        /// <param name="showFormat">Torrentda shu nomli boshqa fayl ham bor — sarlavhaga format qo'shiladi.</param>
        public PlaylistItem(int number, ITorrentManagerFile file, bool showFormat = false)
        {
            Number = number;
            File = file;
            Title = showFormat
                ? $"{Path.GetFileNameWithoutExtension(file.Path)} ({Format})"
                : Path.GetFileNameWithoutExtension(file.Path);

            Refresh();
        }

        public int Number { get; }

        public ITorrentManagerFile File { get; }

        public string Title { get; }

        private string Format => Path.GetExtension(File.Path).TrimStart('.').ToUpperInvariant();

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }

        /// <summary>Hajmi va qancha yuklangani.</summary>
        public string Details
        {
            get => _details;
            private set => SetProperty(ref _details, value);
        }

        public void Refresh()
        {
            var bitfield = File.BitField;

            // Format ham ko'rsatiladi: bir xil nomli fayllar (masalan .mkv va .mp4) shu bilan farqlanadi.
            var info = $"{Format} · {Formatting.Bytes(File.Length)}";

            Details = bitfield.AllTrue
                ? $"{info} · yuklab bo'lingan"
                : $"{info} · {100.0 * bitfield.TrueCount / Math.Max(1, bitfield.Length):0}% yuklangan";
        }
    }
}
