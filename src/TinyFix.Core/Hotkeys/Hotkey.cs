// A global shortcut: modifier bits plus a Windows virtual-key code, as plain integers.
//
// Why Win32 numbers on every OS: the skills.json of the Python app stores shortcuts as text ("Ctrl+Alt+Space"), and
// the platform layer maps the virtual-key code to whatever the OS hook speaks. Keeping the numbers here means the
// parser and the conflict rules behave identically everywhere and stay free of any OS or UI type.

namespace TinyFix.Core.Hotkeys;

/// <summary>A parsed shortcut. <see cref="Vk"/> 0 means "disabled".</summary>
/// <param name="Modifiers">Combination of <see cref="HotkeyModifiers"/> bits.</param>
/// <param name="Vk">Windows virtual-key code of the main key.</param>
/// <param name="Display">Canonical text such as <c>Ctrl+Alt+Space</c> (or <c>None</c>).</param>
public readonly record struct Hotkey(int Modifiers, int Vk, string Display)
{
    /// <summary>The modifier bits that count (the no-repeat flag is not one of them).</summary>
    public int ModifierBits => Modifiers & 0x0F;

    /// <summary>True for F1 to F12.</summary>
    public bool IsFunctionKey => Vk is >= 0x70 and <= 0x7B;

    /// <summary>True when the shortcut is switched off.</summary>
    public bool IsDisabled => Vk == 0;

    /// <summary>Plain letters without a modifier would break typing, so they are unsafe; function keys are fine alone.</summary>
    public bool IsSafe => IsDisabled || IsFunctionKey || ModifierBits != 0;

    /// <summary>True when both shortcuts are the same key with the same modifiers.</summary>
    public bool Conflicts(Hotkey other) => !IsDisabled && !other.IsDisabled && Vk == other.Vk && ModifierBits == other.ModifierBits;

    /// <summary>The disabled shortcut.</summary>
    public static Hotkey Disabled { get; } = new(0, 0, "None");
}

/// <summary>Modifier bits (the values Windows' RegisterHotKey uses).</summary>
public static class HotkeyModifiers
{
    public const int Alt = 0x0001;
    public const int Control = 0x0002;
    public const int Shift = 0x0004;
    public const int Win = 0x0008;
    public const int NoRepeat = 0x4000;
}

/// <summary>The default shortcuts of the built-in skills and of the skill menu.</summary>
public static class HotkeyDefaults
{
    public const string Fix = "Ctrl+Alt+Space";
    public const string FixAll = "Ctrl+F3";
    public const string Translate = "Ctrl+Alt+T";
    public const string Menu = "Ctrl+Alt+M";
    public const string Swap = "Ctrl+Alt+L";
}
