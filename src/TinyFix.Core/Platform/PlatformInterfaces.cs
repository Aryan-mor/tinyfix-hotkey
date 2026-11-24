// The OS-facing ports. Core defines them; src/TinyFix.Platform.Windows|Linux|MacOS implement them and the app picks
// the set that matches the running OS. Everything here is small on purpose so a fake is easy to write in a test.

using TinyFix.Core.Hotkeys;
using TinyFix.Core.Layouts;

namespace TinyFix.Core.Platform;

/// <summary>A point in screen pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A shortcut that fired.</summary>
/// <param name="Id">The id the shortcut was registered under (a skill id, or <see cref="HotkeyIds.Menu"/>).</param>
public sealed record HotkeyPressedEventArgs(string Id);

/// <summary>One shortcut to listen for.</summary>
public sealed record HotkeyBinding(string Id, Hotkey Key);

/// <summary>Ids of non-skill shortcuts.</summary>
public static class HotkeyIds
{
    /// <summary>The shortcut that opens the skill selector.</summary>
    public const string Menu = "__menu__";
}

/// <summary>Global shortcuts.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised on a background thread when a registered shortcut is pressed.</summary>
    event Action<HotkeyPressedEventArgs>? Pressed;

    /// <summary>Replaces the set of shortcuts to listen for.</summary>
    void SetBindings(IReadOnlyList<HotkeyBinding> bindings);

    /// <summary>While true no shortcut fires (the shortcut recorder is open, or the user paused TinyFix).</summary>
    bool Suspended { get; set; }

    /// <summary>Starts listening. Returns false (with a reason in the log) when the OS refuses a global hook.</summary>
    bool Start();
}

/// <summary>Synthesises the key presses a copy or paste needs.</summary>
public interface IKeySender
{
    /// <summary>Ctrl+C (Cmd+C on macOS).</summary>
    void SendCopy();

    /// <summary>Ctrl+V (Cmd+V on macOS).</summary>
    void SendPaste();

    /// <summary>Ctrl+A (Cmd+A on macOS).</summary>
    void SendSelectAll();

    /// <summary>Ctrl+Z (Cmd+Z on macOS).</summary>
    void SendUndo();
}

/// <summary>An opaque copy of everything on the clipboard, restored after TinyFix is done with it.</summary>
public interface IClipboardSnapshot : IDisposable;

/// <summary>Plain access to the system clipboard.</summary>
public interface IClipboardAccess
{
    /// <summary>A number that changes whenever the clipboard changes, or -1 when the OS has none (then text is compared).</summary>
    long ChangeCount { get; }

    /// <summary>Copies every format on the clipboard so it can be put back.</summary>
    IClipboardSnapshot? Snapshot();

    /// <summary>Puts a snapshot back (an empty clipboard when <paramref name="snapshot"/> is null).</summary>
    void Restore(IClipboardSnapshot? snapshot);

    /// <summary>The clipboard text, or null when there is none.</summary>
    string? GetText();

    /// <summary>Replaces the clipboard with <paramref name="text"/>.</summary>
    void SetText(string text);

    /// <summary>Empties the clipboard.</summary>
    void Clear();
}

/// <summary>A remembered spot in another app (see <see cref="IForegroundWindow.SaveFocus"/>).</summary>
public interface IFocusAnchor
{
    /// <summary>Brings the window and control back and, when the control allows it, selects the remembered range again.</summary>
    /// <returns>True when the exact range was selected again; false when only the window could be brought back.</returns>
    bool Restore();

    /// <summary>The spot a paste left behind: the same start, up to the caret. Null when the control cannot tell.</summary>
    IFocusAnchor? AfterPaste();
}

/// <summary>Remembers the window the user was typing in and brings it back before keys are sent.</summary>
public interface IForegroundWindow
{
    /// <summary>A token for the currently focused window of another app, or null when the OS cannot say.</summary>
    string? Save();

    /// <summary>Brings the saved window forward. False when it is gone or the OS refused.</summary>
    bool Restore(string? token);

    /// <summary>True when the focused window runs with more rights than TinyFix, so synthetic keys cannot reach it (Windows UAC).</summary>
    bool IsTargetElevated();

    /// <summary>
    /// Remembers exactly where the user is typing (window, control and, where the OS allows it, the selected range) so
    /// the result can go back to that spot even if the user clicks somewhere else meanwhile. Null when the OS cannot say.
    /// </summary>
    IFocusAnchor? SaveFocus() => null;

    /// <summary>Waits until Ctrl, Alt, Shift and Win are up so a held shortcut cannot turn a copy into something else.</summary>
    Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Where the text caret (or, failing that, the mouse pointer) is on screen.</summary>
public interface ICaretLocator
{
    /// <summary>The caret's bottom-left corner in screen pixels, else the pointer position.</summary>
    ScreenPoint GetCaretOrCursor();

    /// <summary>The mouse pointer in screen pixels.</summary>
    ScreenPoint GetCursor();
}

/// <summary>Where API keys are kept, by provider id.</summary>
public interface ISecretStore
{
    /// <summary>A short description of the backend for the About page ("Windows DPAPI", "macOS Keychain", ...).</summary>
    string Description { get; }

    /// <summary>Non-null when keys are kept less safely than usual (an encrypted file on Linux without a keyring).</summary>
    string? Warning { get; }

    /// <summary>The saved key of a provider, or an empty string. Callers that only need to know whether a key exists use <see cref="Contains"/>.</summary>
    string Get(string provider);

    /// <summary>True when a non-empty key is stored for <paramref name="account"/>. The key itself is not returned.</summary>
    bool Contains(string account);

    /// <summary>Saves a key (an empty value deletes it).</summary>
    void Set(string provider, string apiKey);

    /// <summary>Every saved key by provider id.</summary>
    IReadOnlyDictionary<string, string> GetAll();
}

/// <summary>Starting TinyFix when the user logs in.</summary>
public interface IAutostart
{
    /// <summary>True when the OS will start TinyFix at login.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers <paramref name="executablePath"/> to start at login.</summary>
    void Enable(string executablePath);

    /// <summary>Removes the registration.</summary>
    void Disable();
}

/// <summary>What the running desktop can and cannot do, for notices in the UI.</summary>
public sealed record PlatformCapabilities(bool GlobalHotkeys, bool CaretPosition, bool RestoreForeground, bool IsWayland)
{
    /// <summary>The platform needs the "Fix clipboard" tray action because global shortcuts cannot be relied on.</summary>
    public bool ClipboardFixAdvised => IsWayland || !GlobalHotkeys;
}

/// <summary>The set of port implementations for the running OS.</summary>
public sealed record PlatformServices(
    IClipboardAccess Clipboard,
    IForegroundWindow Foreground,
    ICaretLocator Caret,
    ISecretStore Secrets,
    IAutostart Autostart,
    PlatformCapabilities Capabilities,
    IKeyboardLayouts? Layouts = null);
