// Start at login on Linux desktops through an XDG autostart .desktop file, plus the factory of the Linux ports.

using TinyFix.Core.Platform;

namespace TinyFix.Platform.Linux;

/// <summary><see cref="IAutostart"/> over ~/.config/autostart.</summary>
public sealed class LinuxAutostart(string? configHome = null) : IAutostart
{
    private string FilePath => Path.Combine(AutostartDirectory(configHome), "tinyfix.desktop");

    /// <summary>The autostart folder for a config home (XDG_CONFIG_HOME or ~/.config).</summary>
    public static string AutostartDirectory(string? configHome = null)
    {
        var home = configHome ?? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(home, "autostart");
    }

    /// <inheritdoc />
    public bool IsEnabled => File.Exists(FilePath);

    /// <summary>The text of the .desktop file for an executable.</summary>
    public static string DesktopEntry(string executablePath) =>
        "[Desktop Entry]\nType=Application\nName=TinyFix\nComment=Fix typos with a shortcut\n" +
        $"Exec={QuoteExec(executablePath)}\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";

    // The Exec key needs a double-quoted path with backslash, quote, backtick and dollar escaped.
    private static string QuoteExec(string path) =>
        "\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal) + "\"";

    /// <inheritdoc />
    public void Enable(string executablePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, DesktopEntry(executablePath));
    }

    /// <inheritdoc />
    public void Disable()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}

/// <summary>Factory for the Linux <see cref="PlatformServices"/>.</summary>
public static class LinuxPlatform
{
    /// <summary>Creates the services; the encrypted-file fallback lives under <paramref name="dataDirectory"/>.</summary>
    public static PlatformServices Create(string dataDirectory)
    {
        var wayland = LinuxDisplay.IsWayland();
        return new PlatformServices(
            new LinuxClipboard(wayland),
            new LinuxForeground(wayland),
            new LinuxCaret(wayland),
            LinuxSecrets.Create(dataDirectory),
            new LinuxAutostart(),
            new PlatformCapabilities(GlobalHotkeys: !wayland, CaretPosition: false, RestoreForeground: !wayland, IsWayland: wayland));
    }
}
