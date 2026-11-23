// The UI languages TinyFix ships (Assets/i18n/<code>.json) and the OS-language rules that pick one.
// Adapted from the reference project's language registry (MIT, credited in THIRD-PARTY-NOTICES.md): the Portuguese region and Chinese script
// helpers are kept so adding pt-BR, pt-PT, zh-CN or zh-TW later (docs/DOTNET-ADDING-A-LANGUAGE.md) needs one line.

namespace TinyFix.App.Services;

/// <summary>Registry of the shipped languages.</summary>
public static class AppLanguages
{
    /// <summary>Code to native name (shown in the language picker, each in its own language).</summary>
    public static readonly IReadOnlyList<(string Code, string NativeName)> All =
    [
        ("en", "English"),
        ("de", "Deutsch"),
        ("fa", "فارسی"),
        ("ja", "日本語"),
    ];

    /// <summary>Languages written right to left: windows mirror their layout for these.</summary>
    public static readonly IReadOnlySet<string> RightToLeft = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ar", "fa", "he", "ur" };

    /// <summary>True when the language code reads right to left.</summary>
    public static bool IsRightToLeft(string code) => RightToLeft.Contains(code.Split('-')[0]);

    /// <summary>
    /// Maps an OS language name ("de-AT", "fr", "pt_BR", "zh-Hant-TW") to a shipped code, or null when none fits.
    /// The match is on the whole language subtag, so "fr" never catches North Frisian "frr" and "id" never catches
    /// Idoma "idu"; Portuguese and Chinese are told apart by region and script.
    /// </summary>
    public static string? Match(string osLanguage)
    {
        var name = osLanguage.Trim().Replace('_', '-');
        if (name.Length == 0)
            return null;
        // Mongolian in Cyrillic only: the traditional script (Inner Mongolia) reads differently.
        if (name.StartsWith("mn-", StringComparison.OrdinalIgnoreCase) && name.Contains("Mong", StringComparison.OrdinalIgnoreCase))
            return null;
        if (PortugueseVariant(name) is { } portuguese && Has(portuguese))
            return portuguese;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && name.Split('-')[0].Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            var chinese = IsTraditionalChinese(name) ? "zh-TW" : "zh-CN";
            return Has(chinese) ? chinese : null;
        }
        var exact = All.FirstOrDefault(l => l.Code.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact.Code is not null)
            return exact.Code;
        var language = name.Split('-')[0];
        var byLanguage = All.FirstOrDefault(l => l.Code.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase));
        return byLanguage.Code;
    }

    private static bool Has(string code) => All.Any(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Portuguese region policy: Brazil gets pt-BR, every other region (and none) pt-PT.</summary>
    public static string? PortugueseVariant(string name)
    {
        var normalized = name.Trim().Replace('_', '-');
        var parts = normalized.Split('-');
        if (!parts[0].Equals("pt", StringComparison.OrdinalIgnoreCase))
            return null;
        // The region follows the language and an optional script (pt-Latn-BR). An extension or private-use subtag is
        // not a region: pt-PT-x-br still means Portuguese as used in Portugal.
        var regionIndex = parts.Length > 1 && parts[1].Length == 4 && parts[1].All(char.IsAsciiLetter) ? 2 : 1;
        return parts.ElementAtOrDefault(regionIndex)?.Equals("BR", StringComparison.OrdinalIgnoreCase) == true ? "pt-BR" : "pt-PT";
    }

    /// <summary>
    /// True for a Chinese culture name written in Traditional characters: "zh-TW", "zh-HK", "zh-Hant-TW", "zh-CHT".
    /// An explicit script wins over the region ("zh-Hans-TW" is Simplified in Taiwan); a bare "zh" is Simplified.
    /// </summary>
    public static bool IsTraditionalChinese(string culture)
    {
        var parts = culture.Trim().Replace('_', '-').Split('-');
        if (!parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = parts.Skip(1).ToArray();
        if (rest.Contains("Hans", StringComparer.OrdinalIgnoreCase))
            return false;
        return rest.Intersect(["Hant", "TW", "HK", "MO", "CHT"], StringComparer.OrdinalIgnoreCase).Any();
    }
}
