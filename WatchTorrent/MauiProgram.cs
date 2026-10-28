using LibVLCSharp.MAUI;
using Microsoft.Extensions.Logging;
using TorrentReader;
using WatchTorrent.Services;
using WatchTorrent.ViewModels;
using WatchTorrent.Views;

namespace WatchTorrent
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseLibVLCSharp()
#if ANDROID
                .ConfigureMauiHandlers(handlers => handlers.AddHandler<Controls.SubtitleSurface, Controls.SubtitleSurfaceHandler>())
#endif
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<CloudflareSolver>();
            builder.Services.AddSingleton(services =>
            {
                var solver = services.GetRequiredService<CloudflareSolver>();

                // Saqlangan cf_clearance bo'lsa WebView har safar ochilmaydi.
                return new TorrentClient(solver.SolveAsync) { Clearance = solver.LoadSaved() };
            });
            builder.Services.AddSingleton<DownloadService>();
            builder.Services.AddSingleton<PosterService>();

            builder.Services.AddTransient<DiscoverViewModel>();
            builder.Services.AddTransient<SearchViewModel>();
            builder.Services.AddTransient<TorrentDetailViewModel>();
            builder.Services.AddTransient<DownloadsViewModel>();
            builder.Services.AddTransient<PlayerViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();

            builder.Services.AddTransient<DiscoverPage>();
            builder.Services.AddTransient<SearchPage>();
            builder.Services.AddTransient<TorrentDetailPage>();
            builder.Services.AddTransient<DescriptionPage>();
            builder.Services.AddTransient<DownloadsPage>();
            builder.Services.AddTransient<PlayerPage>();
            builder.Services.AddTransient<ImageViewerPage>();
            builder.Services.AddTransient<SettingsPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
