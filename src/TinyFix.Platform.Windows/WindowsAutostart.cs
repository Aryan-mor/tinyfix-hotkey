// Start at login through the current user's Run key (HKCU, no administrator rights needed).

using System.Runtime.Versioning;
using Microsoft.Win32;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Windows;

/// <summary><see cref="IAutostart"/> over the registry.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart(string valueName = "TinyFix", string runKeyPath = WindowsAutostart.RunKey) : IAutostart
{
    /// <summary>The registry key Windows reads at login.</summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
            return key?.GetValue(valueName) is string;
        }
    }

    /// <inheritdoc />
    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        // Quoted: a path with spaces ("C:\Program Files\...") would otherwise be cut at the first space.
        key.SetValue(valueName, $"\"{executablePath}\"");
    }

    /// <inheritdoc />
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
