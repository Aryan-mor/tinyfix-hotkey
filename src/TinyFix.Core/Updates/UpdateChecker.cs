// Asks GitHub whether a newer release exists. Ported from update_check.py (the check only: the app never downloads or
// installs anything by itself in this version, it opens the release page).

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TinyFix.Core.Updates;

/// <summary>A release number (major.minor.patch).</summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    /// <inheritdoc />
    public int CompareTo(ReleaseVersion other) =>
        (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>The newest release.</summary>
/// <param name="Version">Its version.</param>
/// <param name="Url">The page to open to download it.</param>
public sealed record LatestRelease(ReleaseVersion Version, string Url);

/// <summary>Version parsing and the GitHub call.</summary>
public static partial class UpdateChecker
{
    /// <summary>The GitHub API endpoint of the latest release.</summary>
    public const string LatestApi = "https://api.github.com/repos/Aryan-mor/tinyfix-hotkey/releases/latest";

    /// <summary>The page of the latest release.</summary>
    public const string LatestPage = "https://github.com/Aryan-mor/tinyfix-hotkey/releases/latest";

    [GeneratedRegex("^v?(\\d+)\\.(\\d+)\\.(\\d+)")]
    private static partial Regex VersionPattern();

    /// <summary>Parses "1.5.14", "v2.0.0" or "2.0.0-dev".</summary>
    /// <exception cref="FormatException">The text starts with no version.</exception>
    public static ReleaseVersion Parse(string? text)
    {
        var match = VersionPattern().Match((text ?? "").Trim());
        if (!match.Success)
            throw new FormatException($"Not a version: {text}");
        return new ReleaseVersion(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>True when <paramref name="latest"/> is a higher version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string latest, string current) => Parse(current).CompareTo(Parse(latest)) < 0;

    /// <summary>True when the automatic check has not run today. Dates are yyyy-MM-dd texts.</summary>
    public static bool ShouldAutoCheck(string lastCheck, string today) => lastCheck.Trim() != today;

    /// <summary>Asks GitHub for the latest release. Null when the answer holds no usable version.</summary>
    /// <exception cref="HttpRequestException">The request failed (offline, rate limited).</exception>
    public static async Task<LatestRelease?> FetchLatestAsync(HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TinyFix-Hotkey-UpdateCheck", "2"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        try
        {
            return new LatestRelease(Parse(tag), string.IsNullOrEmpty(url) ? LatestPage : url);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
