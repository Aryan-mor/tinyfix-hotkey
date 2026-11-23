// Maps the Windows virtual-key codes of Core's hotkey model to SharpHook key codes.

using SharpHook.Data;

namespace TinyFix.App.Services;

/// <summary>Virtual-key code to <see cref="KeyCode"/>.</summary>
public static class KeyMap
{
    private static readonly Dictionary<int, KeyCode> Map = Build();

    private static Dictionary<int, KeyCode> Build()
    {
        var map = new Dictionary<int, KeyCode>
        {
            [0x08] = KeyCode.VcBackspace,
            [0x09] = KeyCode.VcTab,
            [0x0D] = KeyCode.VcEnter,
            [0x1B] = KeyCode.VcEscape,
            [0x20] = KeyCode.VcSpace,
            [0x21] = KeyCode.VcPageUp,
            [0x22] = KeyCode.VcPageDown,
            [0x23] = KeyCode.VcEnd,
            [0x24] = KeyCode.VcHome,
            [0x25] = KeyCode.VcLeft,
            [0x26] = KeyCode.VcUp,
            [0x27] = KeyCode.VcRight,
            [0x28] = KeyCode.VcDown,
            [0x2D] = KeyCode.VcInsert,
            [0x2E] = KeyCode.VcDelete,
            [0xBA] = KeyCode.VcSemicolon,
            [0xBB] = KeyCode.VcEquals,
            [0xBC] = KeyCode.VcComma,
            [0xBD] = KeyCode.VcMinus,
            [0xBE] = KeyCode.VcPeriod,
            [0xBF] = KeyCode.VcSlash,
            [0xC0] = KeyCode.VcBackQuote,
            [0xDB] = KeyCode.VcOpenBracket,
            [0xDC] = KeyCode.VcBackslash,
            [0xDD] = KeyCode.VcCloseBracket,
            [0xDE] = KeyCode.VcQuote,
        };
        KeyCode[] letters =
        [
            KeyCode.VcA, KeyCode.VcB, KeyCode.VcC, KeyCode.VcD, KeyCode.VcE, KeyCode.VcF, KeyCode.VcG, KeyCode.VcH, KeyCode.VcI,
            KeyCode.VcJ, KeyCode.VcK, KeyCode.VcL, KeyCode.VcM, KeyCode.VcN, KeyCode.VcO, KeyCode.VcP, KeyCode.VcQ, KeyCode.VcR,
            KeyCode.VcS, KeyCode.VcT, KeyCode.VcU, KeyCode.VcV, KeyCode.VcW, KeyCode.VcX, KeyCode.VcY, KeyCode.VcZ,
        ];
        for (var i = 0; i < letters.Length; i++)
            map['A' + i] = letters[i];
        KeyCode[] digits =
        [
            KeyCode.Vc0, KeyCode.Vc1, KeyCode.Vc2, KeyCode.Vc3, KeyCode.Vc4, KeyCode.Vc5, KeyCode.Vc6, KeyCode.Vc7, KeyCode.Vc8, KeyCode.Vc9,
        ];
        for (var i = 0; i < digits.Length; i++)
            map['0' + i] = digits[i];
        KeyCode[] functions =
        [
            KeyCode.VcF1, KeyCode.VcF2, KeyCode.VcF3, KeyCode.VcF4, KeyCode.VcF5, KeyCode.VcF6, KeyCode.VcF7, KeyCode.VcF8,
            KeyCode.VcF9, KeyCode.VcF10, KeyCode.VcF11, KeyCode.VcF12, KeyCode.VcF13, KeyCode.VcF14, KeyCode.VcF15, KeyCode.VcF16,
            KeyCode.VcF17, KeyCode.VcF18, KeyCode.VcF19, KeyCode.VcF20, KeyCode.VcF21, KeyCode.VcF22, KeyCode.VcF23, KeyCode.VcF24,
        ];
        for (var i = 0; i < functions.Length; i++)
            map[0x70 + i] = functions[i];
        return map;
    }

    /// <summary>Looks up the SharpHook key for a virtual-key code.</summary>
    public static bool TryGetKeyCode(int vk, out KeyCode code) => Map.TryGetValue(vk, out code);

    /// <summary>The virtual-key code of a SharpHook key (for the shortcut recorder), or 0.</summary>
    public static int ToVk(KeyCode code)
    {
        foreach (var (vk, mapped) in Map)
            if (mapped == code)
                return vk;
        return 0;
    }
}
