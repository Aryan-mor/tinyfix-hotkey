// Where TinyFix keeps its files: %APPDATA%\TinyFix, ~/.config/tinyfix or ~/Library/Application Support/TinyFix.

namespace TinyFix.Core.Settings;

/// <summary>Resolves the per-user data folder.</summary>
public static class DataDirectory
{
    /// <summary>Environment variable that moves everything to another folder (a test copy, a portable setup).</summary>
    public const string OverrideVariable = "TINYFIX_DATA_DIR";

    /// <summary>The override folder, or null when the variable is not set.</summary>
    public static string? Override
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(OverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
    }

    /// <summary>The folder for settings, skills, logs and the secret fallback file.</summary>
    public static string Resolve() => Override ?? ForCurrentOs();

    /// <summary>The default folder of the running OS.</summary>
    public static string ForCurrentOs()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TinyFix");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "TinyFix");
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(config) ? Path.Combine(home, ".config") : config, "tinyfix");
    }
}
