using System.Diagnostics;
using System.Reflection;
using Android.Views;
using Org.Videolan.Libvlc;

namespace WatchTorrent
{
    /// <summary>
    /// HDR rejimi uchun VLC "android_display" chiqishi: MediaCodec kadrlari (BT.2020 PQ) to'g'ridan-to'g'ri video
    /// sirtiga beriladi va HDR'ni tizim/displey o'zi ko'rsatadi. VLC bu rejimda rasmni kattalashtirmaydi va faqat
    /// ilova video o'lchamini qabul qilsa (OnNewVideoLayoutListener) ishlaydi — LibVLCSharp VideoView buni qilmaydi,
    /// shuning uchun uning ichidagi AWindow'ga tinglovchi va subtitr sirti shu yerda ulanadi.
    /// </summary>
    public sealed class DirectVideoOutput : Java.Lang.Object, IVLCVout.IOnNewVideoLayoutListener
    {
        private readonly Action<double> _onAspectChanged;

        private DirectVideoOutput(Action<double> onAspectChanged)
        {
            _onAspectChanged = onAspectChanged;
        }

        /// <summary>
        /// VideoView'ga MediaPlayer berilgandan keyin (video ochilishidan oldin) chaqiriladi.
        /// <paramref name="onAspectChanged"/> — videoning ekrandagi nisbati (eni / bo'yi), asosiy oqimda.
        /// Natija video va subtitr sirtlari tayyor bo'lganda tugaydi (false — ulab bo'lmadi): VLC sirt tayyor
        /// bo'lmasdan ochilsa MediaCodec'ning to'g'ridan-to'g'ri chiqishini rad etadi va dasturiy dekodlashga o'tadi.
        /// </summary>
        public static Task<bool> AttachAsync(Android.Views.View videoView, SurfaceView subtitlesView, Action<double> onAspectChanged)
        {
            try
            {
                if (videoView is not SurfaceView surface || FindWindowField(videoView.GetType())?.GetValue(videoView) is not AWindow window)
                {
                    return Task.FromResult(false);
                }

                var ready = new SurfacesReady(window);

                window.DetachViews();
                window.SetVideoView(surface);
                window.SetSubtitlesView(subtitlesView);
                window.AddCallback(ready);
                window.AttachViews(new DirectVideoOutput(onAspectChanged));

                return ready.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DirectVideo] Ulab bo'lmadi: {ex}");

                return Task.FromResult(false);
            }
        }

        public void OnNewVideoLayout(IVLCVout? vlcVout, int width, int height, int visibleWidth, int visibleHeight, int sarNum, int sarDen)
        {
            if (visibleWidth <= 0 || visibleHeight <= 0)
            {
                return;
            }

            // Ekran formati (to'ldirish, 16:9...) ham shu yerga keladi: VLC ko'rinadigan qism va piksel nisbatini o'zgartiradi.
            var sampleAspect = sarNum > 0 && sarDen > 0 ? (double)sarNum / sarDen : 1;

            _onAspectChanged(visibleWidth * sampleAspect / visibleHeight);
        }

        /// <summary>AWindow sirtlari (video + subtitr) tayyor bo'lganini bildiradi.</summary>
        private sealed class SurfacesReady : Java.Lang.Object, IVLCVout.ICallback
        {
            private readonly AWindow _window;
            private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public SurfacesReady(AWindow window)
            {
                _window = window;
            }

            public Task<bool> Task => _completion.Task;

            public void OnSurfacesCreated(IVLCVout? vlcVout)
            {
                _window.RemoveCallback(this);
                _completion.TrySetResult(true);
            }

            public void OnSurfacesDestroyed(IVLCVout? vlcVout)
            {
            }
        }

        private static FieldInfo? FindWindowField(Type? type)
        {
            for (; type != null; type = type.BaseType)
            {
                if (type.GetField("_awindow", BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
                {
                    return field;
                }
            }

            return null;
        }
    }
}
