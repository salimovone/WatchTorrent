using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VoiceConnection;

if (args.Length < 1 || !Path.IsPathFullyQualified(args[0]) || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: VoiceConnection.Smoke <absolute native DLL path> [--device-host] [--relay-only]");
    return 2;
}
NativeLibrary.SetDllImportResolver(typeof(VoiceSession).Assembly, (name, assembly, path) =>
    name == "voiceconnection_native" ? NativeLibrary.Load(args[0], assembly, DllImportSearchPath.SafeDirectories | DllImportSearchPath.UseDllDirectoryForDependencies) : IntPtr.Zero);
if (args.Contains("--device-host"))
{
    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    try
    {
        if (args.Contains("--full-peer")) await WatchTorrent.Services.DirectPeerProbe.RunAsync(true, 47900, Console.WriteLine, probeTimeout.Token);
        else await VoiceProbe.RunAsync(true, 47900, Console.WriteLine, probeTimeout.Token, args.Contains("--relay-only"));
        return 0;
    }
    catch (Exception error) { Console.Error.WriteLine("FAIL " + error); return 1; }
}
var key = RandomNumberGenerator.GetBytes(256);
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
try
{
    await using var caller = new VoiceSession(key, true, args.Contains("--relay-only"));
    await using var receiver = new VoiceSession(key, false, args.Contains("--relay-only"));
    caller.SignalingEmitted += receiver.ReceiveSignaling;
    receiver.SignalingEmitted += caller.ReceiveSignaling;
    caller.StateChanged += state => Console.WriteLine($"Caller: {state}");
    receiver.StateChanged += state => Console.WriteLine($"Receiver: {state}");
    caller.Start();
    receiver.Start();
    await Task.WhenAll(caller.WaitAsync(lifetime.Token), receiver.WaitAsync(lifetime.Token));
    if (caller.Microphone || receiver.Microphone) throw new Exception("Microphone must start muted.");
    // Establishment exercises both real ICE transports and the native device setup. It does not measure audible quality.
    Console.WriteLine("PASS: two real VoiceConnection native sessions established; both microphones start muted.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    return 1;
}
finally { CryptographicOperations.ZeroMemory(key); }
