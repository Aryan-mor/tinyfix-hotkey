// macOS adapters. Command-line helpers do the clipboard (pbcopy/pbpaste), the frontmost app (osascript) and the
// keychain (`security`); CoreGraphics gives the pointer position. UNTESTED on a real Mac by the author.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.MacOS;

/// <summary><see cref="IClipboardAccess"/> over pbcopy and pbpaste (text only).</summary>
public sealed class MacClipboard : IClipboardAccess
{
    // pbcopy reads its input in the locale's encoding: pin UTF-8 so non-ASCII text survives.
    private static readonly Dictionary<string, string> Utf8 = new() { ["LANG"] = "en_US.UTF-8", ["LC_ALL"] = "en_US.UTF-8" };

    private sealed class TextSnapshot(string? text) : IClipboardSnapshot
    {
        public string? Text { get; } = text;

        public void Dispose()
        {
        }
    }

    /// <inheritdoc />
    public long ChangeCount => -1;

    /// <inheritdoc />
    public IClipboardSnapshot? Snapshot() => new TextSnapshot(GetText());

    /// <inheritdoc />
    public void Restore(IClipboardSnapshot? snapshot)
    {
        if (snapshot is not TextSnapshot saved)
            return;
        if (saved.Text is null)
            Clear();
        else
            SetText(saved.Text);
    }

    /// <inheritdoc />
    public string? GetText()
    {
        var result = ExternalProcess.Run("pbpaste", [], environment: Utf8);
        return result.Success ? result.Output : null;
    }

    /// <inheritdoc />
    /// <exception cref="IOException">pbcopy failed.</exception>
    public void SetText(string text)
    {
        if (!ExternalProcess.Run("pbcopy", [], text, environment: Utf8).Success)
            throw new IOException("Could not write the clipboard.");
    }

    /// <inheritdoc />
    public void Clear() => ExternalProcess.Run("pbcopy", [], "", environment: Utf8);
}

/// <summary><see cref="IForegroundWindow"/> over AppleScript (System Events).</summary>
public sealed partial class MacForeground : IForegroundWindow
{
    [GeneratedRegex("^[A-Za-z0-9.\\-]+$")]
    private static partial Regex BundleId();

    /// <inheritdoc />
    public string? Save()
    {
        var result = ExternalProcess.Run("osascript",
            ["-e", "tell application \"System Events\" to get bundle identifier of first application process whose frontmost is true"]);
        var id = result.Output.Trim();
        return result.Success && BundleId().IsMatch(id) ? id : null;
    }

    /// <inheritdoc />
    public bool Restore(string? token)
    {
        // A bundle id is letters, digits, dots and dashes: validated so nothing else is ever put into the script.
        if (token is null || !BundleId().IsMatch(token))
            return false;
        return ExternalProcess.Run("osascript", ["-e", $"tell application id \"{token}\" to activate"]).Success;
    }

    /// <inheritdoc />
    public bool IsTargetElevated() => false;

    /// <inheritdoc />
    public async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        await Task.Delay(Math.Min(150, (int)timeout.TotalMilliseconds), ct).ConfigureAwait(false);
        return true;
    }
}

/// <summary><see cref="ICaretLocator"/> using the pointer position from CoreGraphics.</summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacCaret : ICaretLocator
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [StructLayout(LayoutKind.Sequential)]
    private struct CgPoint
    {
        public double X;
        public double Y;
    }

    [LibraryImport(CoreGraphics)]
    private static partial nint CGEventCreate(nint source);

    [LibraryImport(CoreGraphics)]
    private static partial CgPoint CGEventGetLocation(nint ev);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint obj);

    /// <inheritdoc />
    /// <remarks>There is no caret query here, so the caret and the pointer are the same point.</remarks>
    public ScreenPoint GetCaretOrCursor() => GetCursor();

    /// <inheritdoc />
    public ScreenPoint GetCursor()
    {
        try
        {
            var ev = CGEventCreate(0);
            if (ev == 0)
                return new ScreenPoint(200, 200);
            try
            {
                var p = CGEventGetLocation(ev);
                return new ScreenPoint((int)p.X, (int)p.Y);
            }
            finally
            {
                CFRelease(ev);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new ScreenPoint(200, 200);
        }
    }
}

/// <summary><see cref="ISecretStore"/> over the login keychain (the `security` tool).</summary>
public sealed class KeychainSecretStore : ISecretStore
{
    private const string Service = "TinyFix";

    /// <inheritdoc />
    public string Description => "macOS Keychain";

    /// <inheritdoc />
    public string? Warning => null;

    /// <inheritdoc />
    public bool Contains(string account) => Get(account).Length > 0;

    /// <inheritdoc />
    public string Get(string provider)
    {
        var result = ExternalProcess.Run("security", ["find-generic-password", "-a", provider, "-s", Service, "-w"]);
        return result.Success ? result.Output.Trim() : "";
    }

    /// <inheritdoc />
    /// <remarks>
    /// `security` takes the secret as an argument (-w), so it is visible in the process list for the few milliseconds the
    /// command runs: the usual limitation of this tool. A Keychain API binding would avoid it (open item).
    /// </remarks>
    public void Set(string provider, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            ExternalProcess.Run("security", ["delete-generic-password", "-a", provider, "-s", Service]);
            return;
        }
        if (!ExternalProcess.Run("security", ["add-generic-password", "-a", provider, "-s", Service, "-w", apiKey, "-U"]).Success)
            throw new IOException("The keychain refused the key.");
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll()
    {
        var result = new Dictionary<string, string>();
        foreach (var account in LlmSecrets.Accounts)
        {
            var key = Get(account);
            if (key.Length > 0)
                result[account] = key;
        }
        return result;
    }
}

/// <summary><see cref="IAutostart"/> over a LaunchAgent plist.</summary>
public sealed class MacAutostart(string? launchAgentsDirectory = null) : IAutostart
{
    /// <summary>The launchd label (and file name) of the agent.</summary>
    public const string Label = "app.tinyfix.hotkey";

    private string FilePath => Path.Combine(
        launchAgentsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents"),
        Label + ".plist");

    /// <inheritdoc />
    public bool IsEnabled => File.Exists(FilePath);

    /// <summary>The text of the plist for an executable.</summary>
    public static string Plist(string executablePath) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
        "<plist version=\"1.0\">\n<dict>\n" +
        $"  <key>Label</key>\n  <string>{Label}</string>\n" +
        $"  <key>ProgramArguments</key>\n  <array>\n    <string>{SecurityElement.Escape(executablePath)}</string>\n  </array>\n" +
        "  <key>RunAtLoad</key>\n  <true/>\n</dict>\n</plist>\n";

    /// <inheritdoc />
    public void Enable(string executablePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, Plist(executablePath));
    }

    /// <inheritdoc />
    public void Disable()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}

/// <summary>Factory for the macOS <see cref="PlatformServices"/>.</summary>
public static class MacOSPlatform
{
    /// <summary>Creates the services.</summary>
    [SupportedOSPlatform("macos")]
    public static PlatformServices Create() => new(
        new MacClipboard(),
        new MacForeground(),
        new MacCaret(),
        new KeychainSecretStore(),
        new MacAutostart(),
        new PlatformCapabilities(GlobalHotkeys: true, CaretPosition: false, RestoreForeground: true, IsWayland: false));
}
