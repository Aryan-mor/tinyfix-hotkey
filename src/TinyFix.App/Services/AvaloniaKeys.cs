// Maps Avalonia keys (what the shortcut recorder sees in a window) to the Windows virtual-key codes Core uses.

using Avalonia.Input;
using TinyFix.Core.Hotkeys;

namespace TinyFix.App.Services;

/// <summary>Key and modifier conversion for the shortcut recorder.</summary>
public static class AvaloniaKeys
{
    /// <summary>The virtual-key code of <paramref name="key"/>, or 0 when it cannot be a shortcut key (modifiers, media keys).</summary>
    public static int ToVk(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return 'A' + (key - Key.A);
        if (key is >= Key.D0 and <= Key.D9)
            return '0' + (key - Key.D0);
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return '0' + (key - Key.NumPad0);
        if (key is >= Key.F1 and <= Key.F24)
            return 0x70 + (key - Key.F1);
        return key switch
        {
            Key.Space => 0x20,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Back => 0x08,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            Key.OemSemicolon => 0xBA,
            _ => 0,
        };
    }

    /// <summary>True for Ctrl, Alt, Shift and Win/Cmd themselves.</summary>
    public static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System;

    /// <summary>The <see cref="HotkeyModifiers"/> bits of <paramref name="modifiers"/>.</summary>
    public static int ToBits(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? HotkeyModifiers.Control : 0) |
        (modifiers.HasFlag(KeyModifiers.Alt) ? HotkeyModifiers.Alt : 0) |
        (modifiers.HasFlag(KeyModifiers.Shift) ? HotkeyModifiers.Shift : 0) |
        (modifiers.HasFlag(KeyModifiers.Meta) ? HotkeyModifiers.Win : 0);
}
