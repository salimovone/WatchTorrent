using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace WatchTorrent.Services
{
    /// <summary>
    /// VLC uchun torrent fayl manbasi. VLC o'z oqimida sinxron o'qiydi; ma'lumot hali yuklanmagan bo'lsa
    /// o'qish kutadi (MonoTorrent shu joyni birinchi navbatga qo'yadi). Player yopilganda <see cref="Cancel"/>
    /// kutayotgan o'qishni to'xtatadi — aks holda VLC'ning Stop() chaqiruvi osilib qoladi.
    /// </summary>
    public class TorrentMediaInput : MediaInput
    {
        // Kichik bo'laklab o'qiladi: tayyor ma'lumot VLC'ga darhol beriladi, katta so'rov to'liq yuklanishini kutmaydi.
        private const int MaxReadSize = 256 * 1024;

        // Shundan uzoq kutilsa foydalanuvchiga "buferlanmoqda" ko'rsatiladi.
        private const long WaitingThresholdMs = 400;

        private readonly Stream _stream;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly byte[] _buffer = new byte[MaxReadSize];
        private long _readStartedAt;

        public TorrentMediaInput(Stream stream)
        {
            _stream = stream;
            CanSeek = true;
        }

        /// <summary>VLC hozir yuklanmagan ma'lumotni kutyaptimi.</summary>
        public bool IsWaiting
        {
            get
            {
                var startedAt = Interlocked.Read(ref _readStartedAt);

                return startedAt != 0 && Environment.TickCount64 - startedAt > WaitingThresholdMs;
            }
        }

        public override bool Open(out ulong size)
        {
            size = (ulong)_stream.Length;

            return true;
        }

        public override int Read(IntPtr buf, uint len)
        {
            Interlocked.Exchange(ref _readStartedAt, Environment.TickCount64);

            try
            {
                var count = (int)Math.Min(len, MaxReadSize);
                var read = _stream.ReadAsync(_buffer, 0, count, _cancellation.Token).GetAwaiter().GetResult();

                Marshal.Copy(_buffer, 0, buf, read);

                return read;
            }
            catch (Exception)
            {
                // Bekor qilingan yoki stream yopilgan — VLC uchun o'qish xatosi.
                return -1;
            }
            finally
            {
                Interlocked.Exchange(ref _readStartedAt, 0);
            }
        }

        public override bool Seek(ulong offset)
        {
            try
            {
                _stream.Seek((long)offset, SeekOrigin.Begin);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override void Close()
        {
        }

        public void Cancel()
        {
            _cancellation.Cancel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
