// Remembers the focused control and, for Edit and RichEdit controls (Notepad, most dialogs, many Win32 apps), the
// selected character range, so a result can be pasted exactly there even after the user clicked somewhere else.
// Apps that draw their own text (browsers, Electron) give no range: Restore then reports false and the caller falls back
// to checking the selection by copying it.

using System.Runtime.Versioning;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Windows;

/// <summary><see cref="IFocusAnchor"/> over user32.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsFocusAnchor : IFocusAnchor
{
    private readonly WindowsForeground _foreground;
    private readonly string _window;
    private readonly nint _focus;
    private readonly (int Start, int End)? _range;

    private WindowsFocusAnchor(WindowsForeground foreground, string window, nint focus, (int Start, int End)? range)
    {
        _foreground = foreground;
        _window = window;
        _focus = focus;
        _range = range;
    }

    /// <summary>Reads the focused control of the foreground window, or null when there is none.</summary>
    public static WindowsFocusAnchor? Capture(WindowsForeground foreground, string? window)
    {
        if (window is null)
            return null;
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
            return null;
        var thread = NativeMethods.GetWindowThreadProcessId(hwnd, 0);
        var info = new GuiThreadInfo { CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GuiThreadInfo>() };
        var focus = thread != 0 && NativeMethods.GetGUIThreadInfo(thread, ref info) ? info.HwndFocus : 0;
        (int, int)? range = null;
        if (focus != 0 && IsTextControl(focus) && TryGetSelection(focus, out var start, out var end))
            range = (start, end);
        return new WindowsFocusAnchor(foreground, window, focus, range);
    }

    /// <inheritdoc />
    public bool Restore()
    {
        _foreground.Restore(_window);
        if (_focus == 0 || !NativeMethods.IsWindow(_focus))
            return false;
        FocusControl();
        if (_range is not { } range)
            return false;
        NativeMethods.SendMessageTimeout(_focus, NativeMethods.EmSetSel, range.Start, range.End, NativeMethods.SmtoAbortIfHung, 300, out _);
        return TryGetSelection(_focus, out var start, out var end) && start == range.Start && end == range.End;
    }

    /// <inheritdoc />
    public IFocusAnchor? AfterPaste()
    {
        if (_range is not { } range || _focus == 0 || !NativeMethods.IsWindow(_focus))
            return null;
        // The caret ends up after the pasted text; the replaced range began at the old start.
        return TryGetSelection(_focus, out _, out var end) && end >= range.Start
            ? new WindowsFocusAnchor(_foreground, _window, _focus, (range.Start, end))
            : null;
    }

    private void FocusControl()
    {
        var ourThread = NativeMethods.GetCurrentThreadId();
        var targetThread = NativeMethods.GetWindowThreadProcessId(_focus, 0);
        var attached = false;
        try
        {
            // SetFocus only works on a control of the calling thread's own queue: borrow the target's.
            if (targetThread != 0 && targetThread != ourThread)
                attached = NativeMethods.AttachThreadInput(ourThread, targetThread, true);
            NativeMethods.SetFocus(_focus);
        }
        finally
        {
            if (attached)
                NativeMethods.AttachThreadInput(ourThread, targetThread, false);
        }
    }

    private static unsafe bool IsTextControl(nint hwnd)
    {
        var buffer = stackalloc char[64];
        var length = NativeMethods.GetClassName(hwnd, buffer, 64);
        if (length <= 0)
            return false;
        var name = new string(buffer, 0, length);
        return name.Equals("Edit", StringComparison.OrdinalIgnoreCase) || name.Contains("RichEdit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetSelection(nint hwnd, out int start, out int end)
    {
        start = 0;
        end = 0;
        return NativeMethods.SendMessageTimeout(hwnd, NativeMethods.EmGetSel, ref start, ref end, NativeMethods.SmtoAbortIfHung, 300, out _) != 0
               && start >= 0 && end >= start;
    }
}
