using System.Runtime.InteropServices;
using System.Text.Json;

namespace WatchTorrent.Libtorrent;

public interface ILibtorrentApi : IDisposable
{
    string Request(string command);
    int Read(string hash, int file, long position, byte[] buffer, int offset, int count);
}

public sealed class NativeApi : ILibtorrentApi
{
    private IntPtr _context;
    public NativeApi(string cacheDirectory, bool mobile)
    {
        _context = OperatingSystem.IsIOS() ? IOS.Create(cacheDirectory, mobile ? 1 : 0) : Methods.Create(cacheDirectory, mobile ? 1 : 0);
        if (_context == IntPtr.Zero) throw Error();
    }
    private static IOException Error() => new(Marshal.PtrToStringUTF8(OperatingSystem.IsIOS() ? IOS.Error() : Methods.Error()) ?? "libtorrent failed.");
    public string Request(string command)
    {
        ObjectDisposedException.ThrowIf(_context == IntPtr.Zero, this);
        var result = OperatingSystem.IsIOS() ? IOS.Request(_context, command) : Methods.Request(_context, command);
        if (result == IntPtr.Zero) throw Error();
        try { return Marshal.PtrToStringUTF8(result)!; }
        finally { if (OperatingSystem.IsIOS()) IOS.Free(result); else Methods.Free(result); }
    }
    public int Read(string hash, int file, long position, byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_context == IntPtr.Zero, this);
        var result = OperatingSystem.IsIOS() ? IOS.Read(_context, hash, file, position, buffer, offset, count) : Methods.Read(_context, hash, file, position, buffer, offset, count);
        if (result == -2) throw Error();
        return result;
    }
    public void Dispose()
    {
        if (_context == IntPtr.Zero) return;
        if (OperatingSystem.IsIOS()) IOS.Destroy(_context); else Methods.Destroy(_context);
        _context = IntPtr.Zero;
    }
    private static class Methods
    {
        const string Library = "watchtorrent_libtorrent";
        [DllImport(Library, EntryPoint="wt_create", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string directory, int mobile);
        [DllImport(Library, EntryPoint="wt_destroy", CallingConvention=CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr context);
        [DllImport(Library, EntryPoint="wt_last_error", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Error();
        [DllImport(Library, EntryPoint="wt_request", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Request(IntPtr context,[MarshalAs(UnmanagedType.LPUTF8Str)] string command);
        [DllImport(Library, EntryPoint="wt_string_free", CallingConvention=CallingConvention.Cdecl)] internal static extern void Free(IntPtr value);
        [DllImport(Library, EntryPoint="wt_read", CallingConvention=CallingConvention.Cdecl)] internal static extern int Read(IntPtr context,[MarshalAs(UnmanagedType.LPUTF8Str)] string hash,int file,long position,[Out] byte[] buffer,int offset,int count);
    }
    private static class IOS
    {
        const string Library = "__Internal";
        [DllImport(Library, EntryPoint="wt_create", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string directory, int mobile);
        [DllImport(Library, EntryPoint="wt_destroy", CallingConvention=CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr context);
        [DllImport(Library, EntryPoint="wt_last_error", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Error();
        [DllImport(Library, EntryPoint="wt_request", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Request(IntPtr context,[MarshalAs(UnmanagedType.LPUTF8Str)] string command);
        [DllImport(Library, EntryPoint="wt_string_free", CallingConvention=CallingConvention.Cdecl)] internal static extern void Free(IntPtr value);
        [DllImport(Library, EntryPoint="wt_read", CallingConvention=CallingConvention.Cdecl)] internal static extern int Read(IntPtr context,[MarshalAs(UnmanagedType.LPUTF8Str)] string hash,int file,long position,[Out] byte[] buffer,int offset,int count);
    }
}
