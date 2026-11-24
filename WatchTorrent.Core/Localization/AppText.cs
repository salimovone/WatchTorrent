using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace WatchTorrent.Core.Localization;

public interface ILocalized
{
    void LanguageChanged();
}

public sealed class AppText : INotifyPropertyChanged
{
    public static AppText Instance { get; } = new();
    private static readonly ResourceManager resources = new("WatchTorrent.Core.Localization.Strings", typeof(AppText).Assembly);
    private static readonly Dictionary<string, string> lookup = BuildLookup();
    private static readonly Lazy<Template[]> templates = new(BuildTemplates);
    private static readonly List<WeakReference<ILocalized>> listeners = [];
    private static readonly object sync = new();
    private static string language = "uz";
    public static string Language => language;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => resources.GetString(key, CultureInfo.GetCultureInfo(language)) ?? key;

    public static string ResolveLanguage(string? preference, string? deviceLanguage) => preference switch
    {
        "uz" => "uz",
        "en" => "en",
        _ => string.Equals(deviceLanguage?.Split('-', '_')[0], "uz", StringComparison.OrdinalIgnoreCase) ? "uz" : "en"
    };

    public static void Register(ILocalized target)
    {
        lock (sync)
        {
            listeners.RemoveAll(entry => !entry.TryGetTarget(out _));
            listeners.Add(new(target));
        }
    }

    public static void SetLanguage(string value)
    {
        if (value is not ("uz" or "en")) throw new ArgumentException("Unsupported app language.", nameof(value));
        var culture = CultureInfo.GetCultureInfo(value);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = culture;
        if (language == value) return;
        language = value;
        Instance.PropertyChanged?.Invoke(Instance, new(string.Empty));
        ILocalized[] alive;
        lock (sync)
        {
            listeners.RemoveAll(entry => !entry.TryGetTarget(out _));
            alive = listeners.Select(entry => { entry.TryGetTarget(out var target); return target!; }).ToArray();
        }
        foreach (var target in alive) target.LanguageChanged();
    }

    [return: NotNullIfNotNull(nameof(text))]
    public static string? Get(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (lookup.TryGetValue(text, out var key)) return Instance[key];
        // Re-localize cached status strings without resetting downloads, playback or a live call.
        foreach (var template in templates.Value)
        {
            if (template.SourceLanguage == language) continue;
            if (template.Prefix && text.StartsWith(template.Source, StringComparison.Ordinal))
                return Instance[template.Key] + text[template.Source.Length..];
            if (template.Pattern is not { } regex) continue;
            var match = regex.Match(text);
            if (match.Success) return F(template.Key, Enumerable.Range(0, template.Arguments)
                .Select(index => (object?)match.Groups["v" + index].Value).ToArray());
        }
        return text;
    }

    public static string F(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Instance[key], values);
    public static string V(object? value, string format, int alignment) => string.Format(CultureInfo.CurrentCulture,
        "{0" + (alignment == 0 ? "" : "," + alignment.ToString(CultureInfo.InvariantCulture)) + (format.Length == 0 ? "" : ":" + format) + "}", value);

    private static Dictionary<string, string> BuildLookup()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en") })
        {
            var set = resources.GetResourceSet(culture, true, true)!;
            foreach (System.Collections.DictionaryEntry entry in set)
                if (entry.Key is string key && entry.Value is string value) result.TryAdd(value, key);
        }
        return result;
    }

    private sealed record Template(string Key, string Source, string SourceLanguage, bool Prefix, Regex? Pattern, int Arguments);
    private static Template[] BuildTemplates()
    {
        var result = new List<Template>();
        foreach (string sourceLanguage in new[] { "uz", "en" })
        {
            var set = resources.GetResourceSet(sourceLanguage == "uz" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("en"), true, true)!;
            foreach (System.Collections.DictionaryEntry entry in set)
            {
                var key = (string)entry.Key;
                var text = (string)entry.Value!;
                var holes = Regex.Matches(text, @"\{(\d+)\}");
                if (holes.Count == 0)
                {
                    if (text.EndsWith(' ')) result.Add(new(key, text, sourceLanguage, true, null, 0));
                    continue;
                }
                string pattern = Regex.Escape(text);
                foreach (Match hole in holes)
                    pattern = pattern.Replace(Regex.Escape(hole.Value), "(?<v" + hole.Groups[1].Value + ">.*?)", StringComparison.Ordinal);
                var regex = new Regex("\\A" + pattern + "\\z", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
                result.Add(new(key, text, sourceLanguage, false, regex, holes.Max(hole => int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture)) + 1));
            }
        }
        return result.ToArray();
    }
}
