#if DEBUG
using Android.App;
using Android.OS;
using Android.Widget;
using VoiceConnection;

namespace WatchTorrent;

[Activity(Name = "com.companyname.watchtorrent.VoiceProbeActivity", Exported = true)]
public sealed class VoiceProbeActivity : Activity
{
    private readonly CancellationTokenSource life = new(TimeSpan.FromSeconds(100));
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var display = new TextView(this) { Text = "VoiceConnection: native audio sinovi…", TextSize = 18 };
        SetContentView(display);
        _ = Task.Run(async () =>
        {
            var log = new List<string>();
            void Report(string message)
            {
                log.Add(message);
                Android.Util.Log.Info("VoiceConnectionProbe", message);
                var text = string.Join("\n", log);
                RunOnUiThread(() => display.Text = text);
            }
            try
            {
                if (Intent?.GetBooleanExtra("full_peer", false) == true)
                    await WatchTorrent.Services.DirectPeerProbe.RunAsync(false, 47900, Report, life.Token);
                else await VoiceProbe.RunAsync(false, 47900, Report, life.Token, Intent?.GetBooleanExtra("relay_only", false) == true);
            }
            catch (Exception error) { Report("FAIL " + error); }
            finally { File.WriteAllLines(Path.Combine(FilesDir!.AbsolutePath, "voice-probe.txt"), log); }
        });
    }
    protected override void OnDestroy() { life.Cancel(); base.OnDestroy(); }
}
#endif
