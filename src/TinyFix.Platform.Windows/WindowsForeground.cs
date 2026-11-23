// Remembers the focused window and brings it back, as skill_menu.py / app.py do: AttachThreadInput lets
// SetForegroundWindow succeed from a background process. Also: wait for modifier keys, and the UAC elevation check.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Windows;

/// <summary><see cref="IForegroundWindow"/> over user32.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsForeground : IForegroundWindow
{
    private static readonly int[] ModifierKeys =
        [NativeMethods.VkShift, NativeMethods.VkControl, NativeMethods.VkMenu, NativeMethods.VkLWin, NativeMethods.VkRWin];

    /// <inheritdoc />
    public string? Save()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        return hwnd == 0 ? null : hwnd.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public bool Restore(string? token)
    {
        if (!long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value == 0)
            return false;
        var hwnd = (nint)value;
        if (!NativeMethods.IsWindow(hwnd))
            return false;
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
            return true;
        var ourThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, 0);
        var attached = false;
        try
        {
            // Windows only lets the process the user is working with take the foreground: borrow its input queue.
            if (foregroundThread != 0 && foregroundThread != ourThread)
                attached = NativeMethods.AttachThreadInput(ourThread, foregroundThread, true);
            NativeMethods.BringWindowToTop(hwnd);
            return NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            // Always detached: a leaked attachment would tie our input state to another app's for good.
            if (attached)
                NativeMethods.AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <inheritdoc />
    public async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!ModifierKeys.Any(IsDown))
                return true;
            await Task.Delay(10, ct).ConfigureAwait(false);
        }
        var held = ModifierKeys.Where(IsDown).ToArray();
        if (held.Length > 0)
        {
            // Still held after the wait: release them synthetically so the copy is not read as Ctrl+Alt+C.
            var events = held.Select(vk => KeyEvent(vk, up: true)).ToArray();
            NativeMethods.SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
            await Task.Delay(30, ct).ConfigureAwait(false);
        }
        return !ModifierKeys.Any(IsDown);
    }

    private static Input KeyEvent(int vk, bool up) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { Vk = (ushort)vk, Flags = up ? NativeMethods.KeyEventKeyUp : 0 } },
    };

    /// <inheritdoc />
    public bool IsTargetElevated()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == NativeMethods.GetCurrentProcessId())
            return false;
        var target = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (target == 0)
            return false;
        try
        {
            return IsElevated(target) == true && IsElevated(NativeMethods.GetCurrentProcess()) != true;
        }
        finally
        {
            NativeMethods.CloseHandle(target);
        }
    }

    private static bool? IsElevated(nint process)
    {
        if (!NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out var token))
            return null;
        try
        {
            return NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, out var elevated, sizeof(uint), out _)
                ? elevated != 0
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }
}
