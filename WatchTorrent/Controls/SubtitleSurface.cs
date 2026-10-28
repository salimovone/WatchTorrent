#if ANDROID
using Android.Graphics;
using Android.Views;
using Microsoft.Maui.Handlers;
#endif

namespace WatchTorrent.Controls
{
    /// <summary>
    /// HDR rejimida (VLC android_display) subtitrlar chiziladigan shaffof sirt: video MediaCodec'dan to'g'ridan-to'g'ri
    /// ekranga chiqadi va VLC subtitrni kadr ustiga chiza olmaydi — u shu alohida sirtga chiziladi. Faqat Android.
    /// </summary>
    public class SubtitleSurface : Microsoft.Maui.Controls.View
    {
    }

#if ANDROID
    public class SubtitleSurfaceHandler : ViewHandler<SubtitleSurface, SurfaceView>
    {
        public SubtitleSurfaceHandler() : base(ViewMapper)
        {
        }

        protected override SurfaceView CreatePlatformView()
        {
            var view = new SurfaceView(Context);

            // Video sirti ustida, lekin ilova oynasi (boshqaruv tugmalari) ostida.
            view.SetZOrderMediaOverlay(true);
            view.Holder?.SetFormat(Format.Translucent);

            return view;
        }
    }
#endif
}
