// "Start with the system" is on by default: the first run registers TinyFix once, and Settings can switch it off later.

using TinyFix.Core.Settings;

namespace TinyFix.Core.Platform;

/// <summary>Applies the default of "start with the system" a single time, so a later choice of the user always wins.</summary>
public static class AutostartDefault
{
    /// <summary>
    /// Registers <paramref name="executablePath"/> for start at login on the first run. Returns true when settings
    /// changed and must be saved. Later runs do nothing, so a user who turned it off in Settings stays off.
    /// </summary>
    /// <param name="settings">The app settings; <see cref="AppSettings.AutostartDefaultApplied"/> records the first run.</param>
    /// <param name="autostart">The OS registration.</param>
    /// <param name="executablePath">The running program, or null when unknown.</param>
    public static bool ApplyOnce(AppSettings settings, IAutostart autostart, string? executablePath)
    {
        if (settings.AutostartDefaultApplied)
            return false;
        // "dotnet run" and "dotnet TinyFix.dll" report the dotnet host: registering that would start nothing useful.
        if (!IsUsable(executablePath))
            return false;
        // Always written, even when an entry exists: an old install may have left one that points at a folder that is gone.
        autostart.Enable(executablePath!);
        settings.AutostartDefaultApplied = true;
        return true;
    }

    // Path.GetFileName splits only on this OS's separator, so "C:\...\dotnet.exe" stays one name on Linux and macOS
    // and would be registered. Both separators are directory separators here.
    private static bool IsUsable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;
        var normalized = executablePath.Replace('\\', '/');
        return !Path.GetFileNameWithoutExtension(normalized).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    }
}
