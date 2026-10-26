#if WINDOWS
using System.Diagnostics;
#endif

namespace WatchTorrent.Services
{
    /// <summary>Yuklab bo'lingan torrentni ochadi: kompyuterda papkani, telefonda eng katta faylni.</summary>
    public static class DownloadLauncher
    {
        public static async Task OpenAsync(DownloadItem item)
        {
            var mainFile = item.Manager.Files.OrderByDescending(file => file.Length).FirstOrDefault()?.FullPath;

#if WINDOWS
            var arguments = mainFile != null && File.Exists(mainFile)
                ? $"/select,\"{mainFile}\""
                : $"\"{item.Manager.ContainingDirectory}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });

            await Task.CompletedTask;
#else
            if (mainFile == null || !File.Exists(mainFile))
            {
                throw new FileNotFoundException("Fayl topilmadi", mainFile);
            }

            await Launcher.Default.OpenAsync(new OpenFileRequest(item.Name, new ReadOnlyFile(mainFile)));
#endif
        }
    }
}
