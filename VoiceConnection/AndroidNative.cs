#if ANDROID
using System.Runtime.InteropServices;
using JavaBridge = IO.Watchtorrent.Voice.VoiceBridge;

namespace VoiceConnection;

public sealed partial class VoiceSession
{
    private static partial class Native
    {
        internal static int AbiVersion() => 2;
        internal static IntPtr Create(byte[] key, int length, int outgoing, string relayHost, int relayPort, string relayUser, string relayPassword, int relayOnly) =>
            GCHandle.ToIntPtr(GCHandle.Alloc(new JavaBridge(Android.App.Application.Context, key, outgoing != 0, relayHost, relayPort, relayUser, relayPassword, relayOnly != 0)));
        private static JavaBridge Bridge(IntPtr handle) => (JavaBridge)GCHandle.FromIntPtr(handle).Target!;
        internal static int Receive(IntPtr handle, byte[] data, int length) { Bridge(handle).Receive(data); return 0; }
        internal static int Mute(IntPtr handle, int muted) { Bridge(handle).SetMuted(muted != 0); return 0; }
        internal static long Time(IntPtr handle, int mode) => mode == 0 ? Bridge(handle).CaptureTime() : Bridge(handle).PlaybackTime();
        internal static int Poll(IntPtr handle, out int kind, byte[] data, int capacity)
        {
            var entry = Bridge(handle).ReadEvent();
            kind = entry is { Length: > 0 } ? entry[0] : 0;
            if (kind == 0) return 0;
            if (entry!.Length - 1 > capacity) return -1;
            entry.AsSpan(1).CopyTo(data);
            return entry.Length - 1;
        }
        internal static void Destroy(IntPtr handle)
        {
            var owner = GCHandle.FromIntPtr(handle);
            using var bridge = (JavaBridge)owner.Target!;
            try { bridge.Close(); }
            finally { owner.Free(); }
        }
    }
}
#endif
