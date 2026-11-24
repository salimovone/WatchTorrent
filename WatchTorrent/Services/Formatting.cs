using L = WatchTorrent.Core.Localization.AppText;
using System.Net.Http;
using TorrentReader.Http;

namespace WatchTorrent.Services
{
    public static class Formatting
    {
        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

        public static string Bytes(long bytes)
        {
            double value = Math.Max(bytes, 0);
            var unit = 0;

            while (value >= 1024 && unit < SizeUnits.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{value:0} {SizeUnits[unit]}" : $"{value:0.0} {SizeUnits[unit]}";
        }

        public static string Speed(long bytesPerSecond)
        {
            return $"{Bytes(bytesPerSecond)}/s";
        }

        public static string Duration(TimeSpan duration)
        {
            if (duration.TotalMinutes < 1)
            {
                return L.Get("< 1 daq");
            }

            if (duration.TotalHours < 1)
            {
                return L.F("Minutes", (int)duration.TotalMinutes);
            }

            if (duration.TotalDays < 1)
            {
                return L.F("HoursMinutes", (int)duration.TotalHours, duration.Minutes);
            }

            return L.F("DaysHours", (int)duration.TotalDays, duration.Hours);
        }

        /// <summary>Foydalanuvchiga ko'rsatiladigan xato matni.</summary>
        public static string Error(Exception exception)
        {
            return exception switch
            {
                CloudflareChallengeException => L.Get("Cloudflare tekshiruvidan o'tib bo'lmadi. Qayta urinib ko'ring."),
                HttpRequestException => L.Get("Saytga ulanib bo'lmadi. Internet aloqasini tekshiring."),
                TaskCanceledException => L.Get("So'rov vaqti tugadi. Qayta urinib ko'ring."),
                _ => L.F("S232", exception.Message)
            };
        }
    }
}
