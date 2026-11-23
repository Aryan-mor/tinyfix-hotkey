// Where the caret is: GetGUIThreadInfo of the focused window's thread, falling back to the mouse pointer for apps that
// draw their own caret (browsers, Electron). Ported from skill_menu.py (caret_or_cursor_position).

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Windows;

/// <summary><see cref="ICaretLocator"/> over user32.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCaret : ICaretLocator
{
    /// <inheritdoc />
    public ScreenPoint GetCaretOrCursor()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd != 0)
        {
            var thread = NativeMethods.GetWindowThreadProcessId(hwnd, 0);
            var info = new GuiThreadInfo { CbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (thread != 0 && NativeMethods.GetGUIThreadInfo(thread, ref info) && info.HwndCaret != 0)
            {
                var point = new NativePoint { X = info.RcCaret.Left, Y = info.RcCaret.Bottom };
                if (NativeMethods.ClientToScreen(info.HwndCaret, ref point))
                    return new ScreenPoint(point.X, point.Y);
            }
        }
        return GetCursor();
    }

    /// <inheritdoc />
    public ScreenPoint GetCursor() =>
        NativeMethods.GetCursorPos(out var cursor) ? new ScreenPoint(cursor.X, cursor.Y) : new ScreenPoint(200, 200);
}
