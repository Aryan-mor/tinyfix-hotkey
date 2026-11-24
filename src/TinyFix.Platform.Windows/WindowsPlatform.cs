// Builds the Windows set of ports for the app.

using System.Runtime.Versioning;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Windows;

/// <summary>Factory for the Windows <see cref="PlatformServices"/>.</summary>
public static class WindowsPlatform
{
    /// <summary>Creates the services; keys live under <paramref name="dataDirectory"/>.</summary>
    [SupportedOSPlatform("windows")]
    public static PlatformServices Create(string dataDirectory) => new(
        new WindowsClipboard(),
        new WindowsForeground(),
        new WindowsCaret(),
        new DpapiSecretStore(dataDirectory),
        // Registered only when the user turns "start with Windows" on in Settings, never by the app on its own.
        new WindowsAutostart(),
        new PlatformCapabilities(GlobalHotkeys: true, CaretPosition: true, RestoreForeground: true, IsWayland: false),
        new WindowsKeyboardLayouts());
}
