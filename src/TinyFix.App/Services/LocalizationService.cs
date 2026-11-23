// Loads Assets/i18n/<lang>.json and exposes the strings to XAML as dynamic resources.
// Adapted from the reference project's localization service (MIT, credited in THIRD-PARTY-NOTICES.md).
//
// XAML:  Text="{DynamicResource nav.skills}"   (updates live when the language changes)
// Code:  _loc["nav.skills"]  or  _loc.Format("status.running", name)

using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using TinyFix.Core.Localization;

namespace TinyFix.App.Services;

/// <summary>JSON based localization with live language switching.</summary>
public sealed class LocalizationService : ILocalizer
{
    /// <summary>Languages shipped with TinyFix: code to native name.</summary>
    public static readonly IReadOnlyList<(string Code, string NativeName)> Languages = AppLanguages.All;

    /// <summary>
    /// The language the user reads the OS in, taken before <see cref="SetLanguage"/> makes TinyFix's own the current one.
    /// Not InstalledUICulture: that is the language Windows was installed in, which stays the same when the user
    /// switches the display language later.
    /// </summary>
    private static readonly string UserLanguage = CultureInfo.CurrentUICulture.Name;

    private readonly ResourceDictionary _resources = new();
    private readonly Dictionary<string, string> _english;
    private Dictionary<string, string> _current;
    private bool _initialized;

    /// <summary>Loads English (the fallback) from the assets.</summary>
    public LocalizationService()
    {
        _english = LoadStrings("en");
        _current = _english;
    }

    /// <inheritdoc />
    public string Language { get; private set; } = "en";

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <summary>True if <see cref="SetLanguage"/> would change anything.</summary>
    public bool NeedsUpdate(string setting) => !_initialized || Resolve(setting) != Language;

    /// <inheritdoc />
    public string this[string key] =>
        _current.TryGetValue(key, out var value) ? value
        : _english.TryGetValue(key, out var fallback) ? fallback
        : key;

    /// <inheritdoc />
    public string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, this[key], args);
        }
        catch (FormatException)
        {
            return this[key];
        }
    }

    /// <summary>Registers the string dictionary with the application so XAML can use DynamicResource.</summary>
    public void Attach(Application app) => app.Resources.MergedDictionaries.Add(_resources);

    /// <summary>Switches the UI language. <paramref name="setting"/> is a language code or "auto" (the OS language, else English).</summary>
    public void SetLanguage(string setting)
    {
        var code = Resolve(setting);
        _current = code == "en" ? _english : LoadStrings(code);
        Language = code;

        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // Every key is published (English where a translation is missing) so DynamicResource never breaks. All in one
        // SetItems: setting keys one by one raises a resource change per key, and each reaches every live control.
        _resources.SetItems(_english.Keys.Concat(_current.Keys).Distinct()
            .Select(key => new KeyValuePair<object, object?>(key, this[key])));

        _initialized = true;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Maps "auto" / a language code to one of the shipped languages.</summary>
    public static string Resolve(string setting) =>
        Resolve(setting, () => TinyFix.Platform.MacOS.MacOSLocale.PreferredLanguage() ?? UserLanguage);

    /// <summary>Maps "auto" (the OS language <paramref name="osLanguage"/> gives) or a language code to a shipped language.</summary>
    internal static string Resolve(string setting, Func<string> osLanguage)
    {
        // An explicit choice wins over the OS language.
        if (!string.Equals(setting, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var explicitChoice = Languages.FirstOrDefault(l => l.Code.Equals(setting, StringComparison.OrdinalIgnoreCase));
            if (explicitChoice.Code is not null)
                return explicitChoice.Code;
        }
        return AppLanguages.Match(osLanguage()) ?? "en";
    }

    /// <summary>
    /// Left-to-right <paramref name="text"/> inside a sentence of the UI language: in a right-to-left language a list
    /// like ".txt, .md" keeps its dots in front (left-to-right marks around it). Unchanged otherwise.
    /// </summary>
    public static string LeftToRight(string text) =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? "‎" + text + "‎" : text;

    /// <summary>Reads a translation file. A missing file yields an empty dictionary (English is used instead).</summary>
    internal static Dictionary<string, string> LoadStrings(string code, IAssetLoader? loader = null)
    {
        var uri = new Uri($"avares://TinyFix/Assets/i18n/{code}.json");
        if (!(loader?.Exists(uri) ?? AssetLoader.Exists(uri)))
            return [];
        using var stream = loader?.Open(uri) ?? AssetLoader.Open(uri);
        return JsonSerializer.Deserialize(stream, AppJsonContext.Default.DictionaryStringString) ?? [];
    }

    /// <summary>
    /// One string in the system's language before Avalonia and the settings are loaded: for the message a second
    /// launch shows when it cannot reach the running TinyFix. Falls back to English, then to the key.
    /// </summary>
    internal static string EarlyText(string key)
    {
        try
        {
            var loader = new StandardAssetLoader(typeof(LocalizationService).Assembly);
            var code = Resolve("auto");
            if (LoadStrings(code, loader).TryGetValue(key, out var text) || LoadStrings("en", loader).TryGetValue(key, out text))
                return text;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException)
        {
            // Fall through to the key.
        }
        return key;
    }
}
