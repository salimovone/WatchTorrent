using System.Runtime.Versioning;
#if ANDROID
using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Util;
#endif

namespace WatchTorrent.Services
{
    /// <summary>Kichik oynadagi tugmalardan keladigan buyruqlar.</summary>
    public enum PictureInPictureCommand
    {
        Rewind,
        PlayPause,
        Forward
    }

    /// <summary>
    /// Kichik oyna (Picture-in-Picture, faqat Android 8+): film ekran burchagida, boshqa ilovalar ustida o'ynayveradi.
    /// Player'dagi tugma bilan yoki film o'ynab turganda "Home" bosilganda ochiladi; oynada 10 s orqaga,
    /// pauza/davom va 10 s oldinga tugmalari bor.
    /// </summary>
    public static class PictureInPicture
    {
        // Windows'da kichik oyna yo'q — hodisalar faqat Android'da chaqiriladi.
#pragma warning disable CS0067
        /// <summary>Kichik oynaga o'tildi yoki undan chiqildi.</summary>
        public static event EventHandler? Changed;

        public static event EventHandler<PictureInPictureCommand>? CommandReceived;
#pragma warning restore CS0067

        /// <summary>Film hozir kichik oynada.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Qurilma kichik oynani qo'llaydimi (Android 8+; ba'zi arzon qurilmalarda o'chirilgan bo'ladi).</summary>
        [SupportedOSPlatformGuard("android26.0")]
        public static bool IsSupported
        {
            get
            {
#if ANDROID
                return Supported.Value;
#else
                return false;
#endif
            }
        }

        /// <summary>Player ochildi: oynadagi tugmalar tinglanadi, "Home" bosilganda kichik oynaga o'tiladi.</summary>
        public static void Enable()
        {
#if ANDROID
            if (!IsSupported || _isEnabled)
            {
                return;
            }

            _isEnabled = true;
            _isPlaying = false;
            _applied = null;
            _receiver = new CommandReceiver();

            // Faqat o'zimizning PendingIntent'larimiz yetib keladi — boshqa ilovalar filmni boshqara olmaydi.
            AndroidX.Core.Content.ContextCompat.RegisterReceiver(Platform.AppContext, _receiver, new IntentFilter(CommandAction), AndroidX.Core.Content.ContextCompat.ReceiverNotExported);
#endif
        }

        /// <summary>Player yopildi: ilovaning boshqa sahifalari "Home" bosilganda kichik oynaga o'tib ketmasin.</summary>
        public static void Disable()
        {
#if ANDROID
            if (!IsSupported || !_isEnabled)
            {
                return;
            }

            _isEnabled = false;
            _isPlaying = false;
            Apply();

            if (_receiver != null)
            {
                Platform.AppContext.UnregisterReceiver(_receiver);
                _receiver = null;
            }
#endif
        }

        /// <summary>
        /// Har ~0.5 soniyada: oynadagi pauza/davom tugmasi va oyna shakli (video nisbati) film holatiga moslanadi.
        /// Faqat nimadir o'zgarganda tizimga yuboriladi.
        /// </summary>
        public static void Update(bool isPlaying, double aspectRatio)
        {
#if ANDROID
            if (!IsSupported || !_isEnabled)
            {
                return;
            }

            _isPlaying = isPlaying;
            _aspectRatio = aspectRatio;
            Apply();
#endif
        }

        /// <summary>Kichik oynaga o'tadi. Foydalanuvchi sozlamalarda ilovaga bu ruxsatni o'chirib qo'ygan bo'lsa — false.</summary>
        public static bool Enter()
        {
#if ANDROID
            if (!IsSupported || !_isEnabled || Platform.CurrentActivity is not { } activity)
            {
                return false;
            }

            if (IsActive)
            {
                return true;
            }

            try
            {
                return activity.EnterPictureInPictureMode(BuildParams(activity));
            }
            catch (Java.Lang.RuntimeException ex)
            {
                Debug.WriteLine($"[PiP] Kichik oynaga o'tib bo'lmadi: {ex}");

                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>Kichik oynani yopadi (masalan film tugadi) — ilova fonga o'tadi, "X" bosilgandagidek.</summary>
        public static void Dismiss()
        {
#if ANDROID
            if (IsActive)
            {
                Platform.CurrentActivity?.MoveTaskToBack(true);
            }
#endif
        }

#if ANDROID
        // Tizim 2.39:1 dan cho'zinchoqroq oynaga ruxsat bermaydi (istisno beradi).
        private const double MaxAspectRatio = 2.38;

        private const string CommandExtra = "command";

        private static readonly Lazy<bool> Supported = new(() =>
            OperatingSystem.IsAndroidVersionAtLeast(26)
            && Platform.AppContext.PackageManager?.HasSystemFeature(PackageManager.FeaturePictureInPicture) == true);

        private static CommandReceiver? _receiver;
        private static bool _isEnabled;
        private static bool _isPlaying;
        private static double _aspectRatio = 16 / 9.0;
        private static (bool IsEnabled, bool IsPlaying, int Aspect)? _applied;

        private static string CommandAction => $"{Platform.AppContext.PackageName}.PICTURE_IN_PICTURE";

        // Nisbat 1000 ga ko'paytirilgan butun son: Rational uchun va kichik tebranishlarda tizimga qayta yubormaslik uchun.
        private static int AspectKey => (int)Math.Round(Math.Clamp(_aspectRatio, 1 / MaxAspectRatio, MaxAspectRatio) * 1000);

        /// <summary>"Home" yoki "Recents" bosildi (MainActivity). Android 12+ da buni tizim o'zi qiladi (SetAutoEnterEnabled).</summary>
        public static void OnUserLeaveHint()
        {
            if (_isEnabled && _isPlaying && !OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                Enter();
            }
        }

        /// <summary>MainActivity.OnPictureInPictureModeChanged dan chaqiriladi.</summary>
        public static void OnModeChanged(bool isActive)
        {
            if (IsActive == isActive)
            {
                return;
            }

            IsActive = isActive;
            PlayerWindow.OnPictureInPictureChanged(isActive);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Oyna parametrlari oldindan beriladi: Android 12+ da "Home" bosilganda shular bilan silliq o'tadi.</summary>
        private static void Apply()
        {
            if (!IsSupported || Platform.CurrentActivity is not { } activity)
            {
                return;
            }

            var state = (_isEnabled, _isPlaying, AspectKey);

            if (state == _applied)
            {
                return;
            }

            try
            {
                activity.SetPictureInPictureParams(BuildParams(activity));
                _applied = state;
            }
            catch (Java.Lang.RuntimeException ex)
            {
                Debug.WriteLine($"[PiP] Parametrlar o'rnatilmadi: {ex}");
            }
        }

        [SupportedOSPlatform("android26.0")]
        private static PictureInPictureParams BuildParams(Android.App.Activity activity)
        {
            var actions = new List<RemoteAction>
            {
                CreateAction(activity, PictureInPictureCommand.Rewind, Resource.Drawable.ic_rewind, "10 s orqaga"),
                _isPlaying
                    ? CreateAction(activity, PictureInPictureCommand.PlayPause, Resource.Drawable.ic_pause, "Pauza")
                    : CreateAction(activity, PictureInPictureCommand.PlayPause, Resource.Drawable.ic_play, "Davom ettirish"),
                CreateAction(activity, PictureInPictureCommand.Forward, Resource.Drawable.ic_forward, "10 s oldinga")
            };

            var builder = new PictureInPictureParams.Builder();
            builder.SetAspectRatio(new Rational(AspectKey, 1000));
            builder.SetActions(actions);

            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                // Film o'ynab turganda "Home" (imo-ishora bilan ham) bosilsa — tizim o'zi silliq animatsiya bilan o'tkazadi.
                builder.SetAutoEnterEnabled(_isEnabled && _isPlaying);

                // Video uchun tavsiya etilgan: oyna o'lchami o'zgarganda eski va yangi kadr ustma-ust chiqmaydi.
                builder.SetSeamlessResizeEnabled(false);
            }

            return builder.Build()!;
        }

        [SupportedOSPlatform("android26.0")]
        private static RemoteAction CreateAction(Context context, PictureInPictureCommand command, int icon, string title)
        {
            var intent = new Intent(CommandAction);
            intent.SetPackage(context.PackageName);
            intent.PutExtra(CommandExtra, (int)command);

            // Har buyruqning o'z requestCode'i — aks holda tizim PendingIntent'larni bitta deb hisoblaydi.
            var pendingIntent = PendingIntent.GetBroadcast(context, (int)command, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

            return new RemoteAction(Icon.CreateWithResource(context, icon), title, title, pendingIntent);
        }

        private sealed class CommandReceiver : BroadcastReceiver
        {
            public override void OnReceive(Context? context, Intent? intent)
            {
                var command = (PictureInPictureCommand)(intent?.GetIntExtra(CommandExtra, -1) ?? -1);

                if (Enum.IsDefined(command))
                {
                    CommandReceived?.Invoke(null, command);
                }
            }
        }
#endif
    }
}
