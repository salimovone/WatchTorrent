using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using WatchTorrent.Core.Localization;

internal static class LocalizationChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(AppText.ResolveLanguage("system", "uz-Latn-UZ") == "uz" && AppText.ResolveLanguage(null, "uz-Cyrl-UZ") == "uz",
            "system language recognizes Uzbek regional and script variants");
        check(AppText.ResolveLanguage("system", "en-GB") == "en" && AppText.ResolveLanguage("system", "ru-RU") == "en",
            "system language selects English and falls back for unsupported languages");
        check(AppText.ResolveLanguage("en", "uz-UZ") == "en" && AppText.ResolveLanguage("uz", "en-US") == "uz",
            "explicit app language overrides device language");
        var resources = new ResourceManager("WatchTorrent.Core.Localization.Strings", typeof(AppText).Assembly);
        var uz = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        var en = resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        check(uz.Count > 100 && uz.Keys.ToHashSet().SetEquals(en.Keys), "English and Uzbek resources cover the same UI strings");
        check(uz.All(e => !string.IsNullOrEmpty(en[e.Key]) && CompositeFormat.Parse(e.Value).MinimumArgumentCount == CompositeFormat.Parse(en[e.Key]).MinimumArgumentCount),
            "translated messages preserve all formatting arguments");
        var observer = new Observer();
        AppText.Register(observer);
        var previousLanguage = AppText.Language;
        try
        {
            AppText.SetLanguage("en");
            check(AppText.Get("Sozlamalar") == "Settings" && AppText.F("S047", 7) == "Files (7)", "English resources load and format values");
            check(AppText.Get("film · 15% yuklangan") == "film · 15% downloaded" && AppText.Get("Mikrofon ochilmadi. device") == "Could not open the microphone. device",
                "cached status messages and error prefixes switch language without losing their values");
            AppText.SetLanguage("uz");
            check(AppText.Get("Settings") == "Sozlamalar" && AppText.Get("movie · 15% downloaded") == "movie · 15% yuklangan",
                "existing English UI state can switch back to Uzbek");
            check(observer.Updates >= 2, "live bindings are notified for each language change");
            check(AppText.Get(null) == null && AppText.Get("A custom movie title") == "A custom movie title", "localization preserves missing values and user content");
        }
        finally { AppText.SetLanguage(previousLanguage); }
    }
    private sealed class Observer : ILocalized
    {
        public int Updates;
        public void LanguageChanged() => Updates++;
    }
}
