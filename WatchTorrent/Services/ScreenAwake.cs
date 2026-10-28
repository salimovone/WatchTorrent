namespace WatchTorrent.Services
{
    /// <summary>
    /// Ekranni yoqiq ushlab turish bir nechta joydan so'raladi (player, yuklamalar sahifasi) — biri qo'yib
    /// yuborganda boshqasining so'rovi bekor bo'lib qolmasligi uchun sabablar alohida hisoblanadi. UI oqimidan chaqiriladi.
    /// </summary>
    public static class ScreenAwake
    {
        private static readonly HashSet<string> Reasons = new();

        public static void Set(string reason, bool keepOn)
        {
            var changed = keepOn ? Reasons.Add(reason) : Reasons.Remove(reason);

            if (changed)
            {
                DeviceDisplay.Current.KeepScreenOn = Reasons.Count > 0;
            }
        }
    }
}
