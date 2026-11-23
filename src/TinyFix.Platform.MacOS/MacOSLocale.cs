// The language list of macOS. An app started from Finder gets no LANG, so .NET reports the invariant culture; the
// system's own preferred-language list is the reliable source.

using System.Text.RegularExpressions;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.MacOS;

/// <summary>Reads the preferred UI language on macOS.</summary>
public static partial class MacOSLocale
{
    [GeneratedRegex("\"([A-Za-z]{2,3}(?:[-_][A-Za-z0-9]+)*)\"")]
    private static partial Regex Quoted();

    /// <summary>The first preferred language ("de-DE", "zh-Hans-CN"), or null off macOS or when it cannot be read.</summary>
    public static string? PreferredLanguage()
    {
        if (!OperatingSystem.IsMacOS())
            return null;
        var result = ExternalProcess.Run("defaults", ["read", "-g", "AppleLanguages"]);
        return result.Success ? First(result.Output) : null;
    }

    /// <summary>The first language of the plist-style list that <c>defaults read</c> prints.</summary>
    public static string? First(string output)
    {
        var match = Quoted().Match(output);
        if (match.Success)
            return match.Groups[1].Value;
        // Unquoted entries: "(\n    en,\n    de\n)".
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim().Trim(',', '(', ')').Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }
        return null;
    }
}
