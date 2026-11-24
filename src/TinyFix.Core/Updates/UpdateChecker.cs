// Asks GitHub whether a newer release exists, and (Windows) downloads the setup program so the app can update itself.

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
/// <param name="Url">The release page (fallback when there is no installer to run).</param>
/// <param name="InstallerUrl">Direct download of the Windows setup program, null when the release has none.</param>
public sealed record LatestRelease(ReleaseVersion Version, string Url, string? InstallerUrl = null);

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

    /// <summary>Asks GitHub for the latest release, retrying a few times on a failed request. Null when the answer holds no usable version.</summary>
    /// <exception cref="HttpRequestException">The request kept failing (offline, rate limited).</exception>
    public static async Task<LatestRelease?> FetchLatestAsync(HttpClient http, CancellationToken ct = default, TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? TimeSpan.FromSeconds(1.5);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await FetchOnceAsync(http, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested &&
                                       ex is HttpRequestException or TaskCanceledException or IOException)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task<LatestRelease?> FetchOnceAsync(HttpClient http, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TinyFix-Hotkey-UpdateCheck", "2"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        string? installer = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is not null && name.EndsWith("-win-x64-Setup.exe", StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var d) && d.GetString() is { Length: > 0 } link &&
                    link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    installer = link;
                    break;
                }
            }
        }
        try
        {
            return new LatestRelease(Parse(tag), string.IsNullOrEmpty(url) ? LatestPage : url, installer);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Downloads the setup program to the temp folder and returns its path.</summary>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    public static async Task<string> DownloadInstallerAsync(HttpClient http, string installerUrl, CancellationToken ct = default,
        IProgress<int>? percent = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tinyfix-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(new Uri(installerUrl).LocalPath));
        using var request = new HttpRequestMessage(HttpMethod.Get, installerUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TinyFix-Hotkey-UpdateCheck", "2"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var file = File.Create(path))
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var buffer = new byte[81920];
            long read = 0;
            var last = -1;
            int n;
            while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0 && percent is not null)
                {
                    var p = (int)Math.Min(100, read * 100 / total);
                    if (p != last)
                    {
                        last = p;
                        percent.Report(p);
                    }
                }
            }
        }
        return path;
    }
}
