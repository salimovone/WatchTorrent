#if ANDROID
using Android.OS;
using Debug = System.Diagnostics.Debug;
#endif

namespace WatchTorrent.Services
{
    /// <summary>
    /// Samsung'ning mobil HDR rejimi (Galaxy S8/S9/Note8 va boshqalar). Bu telefonlar HDR'ni Android'ning standart
    /// API'si orqali e'lon qilmaydi: Samsung Video pleyeri HDR video ochilganda tizimning CodecSolution xizmatiga
    /// startMhdr("UHDA") yuboradi va displey HDR rejimiga (mDNIe HDR egri chizig'i, yuqori yorqinlik) o'tadi.
    /// Player HDR videoni HDR'da ko'rsatganda xuddi shunday qiladi. Xizmat ruxsat talab qilmaydi; ilova yopilsa
    /// (binder o'lsa) tizim rejimni o'zi o'chiradi.
    /// </summary>
    public static class SamsungHdrMode
    {
#if ANDROID
        private const string ServiceName = "CodecSolution";
        private const string Descriptor = "com.samsung.android.codecsolution.ICodecSolutionManagerService";
        private const int TransactionStartMhdr = 1;
        private const int TransactionStopMhdr = 2;

        // HDR10 videolar uchun Samsung Video ishlatadigan rejim.
        private const string HdrModeUhda = "UHDA";

        private static readonly Lazy<IBinder?> Service = new(GetService);
        private static readonly Lazy<bool> Supported = new(() => Service.Value != null && HasFloatingFeature("SEC_FLOATING_FEATURE_MMFW_SUPPORT_MHDRSERVICE"));

        // Tizim rejimni shu binder "tirik" ekan ushlab turadi (ilova o'lsa o'chadi).
        private static Binder? _client;
        private static int? _sessionId;
#endif

        /// <summary>Telefonda Samsung HDR xizmati bor (displey HDR'ni qo'llaydi).</summary>
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

        /// <summary>HDR video ekranda ko'rinib turganda true. Har tick'da chaqirish mumkin — faqat o'zgarganda yuboriladi.</summary>
        public static void SetActive(bool active)
        {
#if ANDROID
            if (!IsSupported || active == (_sessionId != null) || Service.Value is not { } service)
            {
                return;
            }

            var data = Parcel.Obtain();
            var reply = Parcel.Obtain();

            try
            {
                data.WriteInterfaceToken(Descriptor);

                if (active)
                {
                    _client ??= new Binder();

                    // startMhdr(ICodecClient client, int pid, String mode, boolean isLocalVideo)
                    data.WriteStrongBinder(_client);
                    data.WriteInt(Android.OS.Process.MyPid());
                    data.WriteString(HdrModeUhda);
                    data.WriteInt(0);
                    service.Transact(TransactionStartMhdr, data, reply, 0);
                    reply.ReadException();
                    _sessionId = reply.ReadInt();
                }
                else
                {
                    data.WriteInt(_sessionId!.Value);
                    _sessionId = null;
                    service.Transact(TransactionStopMhdr, data, reply, 0);
                    reply.ReadException();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SamsungHdr] {ex.Message}");
            }
            finally
            {
                data.Recycle();
                reply.Recycle();
            }
#endif
        }

#if ANDROID
        private static IBinder? GetService()
        {
            if (!string.Equals(Build.Manufacturer, "samsung", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                // android.os.ServiceManager.getService(name) — Android 9 da ruxsat etilgan yashirin API.
                var serviceManager = Java.Lang.Class.ForName("android.os.ServiceManager");
                var getService = serviceManager.GetMethod("getService", Java.Lang.Class.FromType(typeof(Java.Lang.String)));
                var binder = getService.Invoke(null, new Java.Lang.String(ServiceName));

                return binder == null ? null : Android.Runtime.Extensions.JavaCast<IBinder>(binder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SamsungHdr] Xizmat yo'q: {ex.Message}");

                return null;
            }
        }

        /// <summary>Samsung qurilma imkoniyatlari ro'yxati (/system/etc/floating_feature.xml).</summary>
        internal static bool HasFloatingFeature(string name)
        {
            try
            {
                var xml = File.ReadAllText("/system/etc/floating_feature.xml");

                return xml.Contains($"<{name}>TRUE</{name}>", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
#endif
    }
}
