using System.Globalization;
using WatchTorrent.Core.Localization;

namespace WatchTorrent.Localization;

public static class AppLanguage
{
    private const string PreferenceKey = "settings.language";
    private static readonly string originalDeviceLanguage = CultureInfo.CurrentUICulture.Name;
    public static string Preference
    {
        get
        {
            var selected = Preferences.Default.Get(PreferenceKey, "system");
            return selected is "uz" or "en" ? selected : "system";
        }
    }

    public static void Initialize() => RefreshSystemLanguage();

    public static void Select(string preference)
    {
        if (preference is not ("system" or "uz" or "en")) throw new ArgumentException("Unsupported language preference.", nameof(preference));
        if (preference == "system") Preferences.Default.Remove(PreferenceKey);
        else Preferences.Default.Set(PreferenceKey, preference);
        RefreshSystemLanguage();
    }

    public static void RefreshSystemLanguage()
    {
        string deviceLanguage = originalDeviceLanguage;
#if ANDROID
        deviceLanguage = Android.App.Application.Context.Resources?.Configuration?.Locales?.Get(0)?.ToLanguageTag() ?? deviceLanguage;
#elif WINDOWS
        deviceLanguage = global::Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? deviceLanguage;
#elif IOS
        deviceLanguage = Foundation.NSLocale.PreferredLanguages.FirstOrDefault() ?? deviceLanguage;
#endif
        AppText.SetLanguage(AppText.ResolveLanguage(Preference, deviceLanguage));
    }
}
