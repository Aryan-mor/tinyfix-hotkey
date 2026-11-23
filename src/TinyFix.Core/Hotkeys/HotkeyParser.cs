// Parses and formats shortcut text ("Ctrl+Alt+Space", "ctrl-alt-space", "none"). Ported from hotkey_util.py.

namespace TinyFix.Core.Hotkeys;

/// <summary>Text to <see cref="Hotkey"/> and back.</summary>
public static class HotkeyParser
{
    private const int VkBack = 0x08, VkTab = 0x09, VkReturn = 0x0D, VkEscape = 0x1B, VkSpace = 0x20;
    private const int VkPrior = 0x21, VkNext = 0x22, VkEnd = 0x23, VkHome = 0x24;
    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;
    private const int VkInsert = 0x2D, VkDelete = 0x2E;
    private const int VkOemPlus = 0xBB, VkOemComma = 0xBC, VkOemMinus = 0xBD, VkOemPeriod = 0xBE;
    private const int VkOem1 = 0xBA, VkOem2 = 0xBF, VkOem3 = 0xC0, VkOem4 = 0xDB, VkOem5 = 0xDC, VkOem6 = 0xDD, VkOem7 = 0xDE;

    private static readonly string[] DisabledNames = ["none", "disabled", "off", "no", "none/disabled"];

    private static readonly Dictionary<string, int> ModifierNames = new()
    {
        ["ctrl"] = HotkeyModifiers.Control,
        ["control"] = HotkeyModifiers.Control,
        ["alt"] = HotkeyModifiers.Alt,
        ["shift"] = HotkeyModifiers.Shift,
        ["win"] = HotkeyModifiers.Win,
        ["windows"] = HotkeyModifiers.Win,
        ["meta"] = HotkeyModifiers.Win,
    };

    private static readonly Dictionary<string, int> NamedKeys = new()
    {
        ["space"] = VkSpace,
        ["tab"] = VkTab,
        ["enter"] = VkReturn,
        ["return"] = VkReturn,
        ["escape"] = VkEscape,
        ["esc"] = VkEscape,
        ["backspace"] = VkBack,
        ["insert"] = VkInsert,
        ["ins"] = VkInsert,
        ["delete"] = VkDelete,
        ["del"] = VkDelete,
        ["home"] = VkHome,
        ["end"] = VkEnd,
        ["pageup"] = VkPrior,
        ["pgup"] = VkPrior,
        ["pagedown"] = VkNext,
        ["pgdn"] = VkNext,
        ["left"] = VkLeft,
        ["up"] = VkUp,
        ["right"] = VkRight,
        ["down"] = VkDown,
        ["plus"] = VkOemPlus,
        ["minus"] = VkOemMinus,
        ["comma"] = VkOemComma,
        ["period"] = VkOemPeriod,
        ["dot"] = VkOemPeriod,
        ["slash"] = VkOem2,
        ["backslash"] = VkOem5,
        ["semicolon"] = VkOem1,
        ["quote"] = 0xDE,
        ["apostrophe"] = VkOem7,
        ["leftbracket"] = VkOem4,
        ["rightbracket"] = VkOem6,
        ["backtick"] = VkOem3,
    };

    private static readonly Dictionary<int, string> VkDisplay = new()
    {
        [VkSpace] = "Space",
        [VkTab] = "Tab",
        [VkReturn] = "Enter",
        [VkEscape] = "Esc",
        [VkBack] = "Backspace",
        [VkInsert] = "Insert",
        [VkDelete] = "Delete",
        [VkHome] = "Home",
        [VkEnd] = "End",
        [VkPrior] = "PageUp",
        [VkNext] = "PageDown",
        [VkLeft] = "Left",
        [VkUp] = "Up",
        [VkRight] = "Right",
        [VkDown] = "Down",
        [VkOemPlus] = "Plus",
        [VkOemMinus] = "Minus",
        [VkOemComma] = "Comma",
        [VkOemPeriod] = "Period",
        [VkOem2] = "Slash",
        [VkOem5] = "Backslash",
        [VkOem1] = "Semicolon",
        [VkOem7] = "Quote",
        [VkOem4] = "LeftBracket",
        [VkOem6] = "RightBracket",
        [VkOem3] = "Backtick",
    };

    /// <summary>Keys offered by the shortcut pickers: F1 to F12, Space, A to Z, 0 to 9 and the navigation keys.</summary>
    public static IReadOnlyList<string> KeyChoices { get; } =
    [
        .. Enumerable.Range(1, 12).Select(i => $"F{i}"),
        "Space",
        .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString()),
        .. Enumerable.Range(0, 10).Select(d => d.ToString()),
        "Tab", "Enter", "Esc", "Backspace", "Insert", "Delete", "Home", "End", "PageUp", "PageDown", "Left", "Right", "Up", "Down",
    ];

    /// <summary>True for "none", "off", "None/Disabled" and friends.</summary>
    public static bool IsDisabledSpec(string? spec)
    {
        if (spec is null)
            return false;
        var text = spec.Trim().ToLowerInvariant().Replace(" ", "").Replace('-', '/');
        return Array.IndexOf(DisabledNames, text) >= 0;
    }

    /// <summary>Virtual-key code for a key name ("Space", "F5", "a").</summary>
    /// <exception cref="FormatException">The name is empty or not a supported key.</exception>
    public static int VkFromName(string name)
    {
        var raw = name.Trim();
        if (raw.Length == 0)
            throw new FormatException("Missing key name");
        var lower = raw.ToLowerInvariant().Replace(" ", "");
        if (NamedKeys.TryGetValue(lower, out var named))
            return named;
        if (lower.Length >= 2 && lower[0] == 'f' && lower[1..].All(char.IsAsciiDigit))
        {
            var number = int.Parse(lower[1..], System.Globalization.CultureInfo.InvariantCulture);
            if (number is >= 1 and <= 24)
                return 0x70 + number - 1;
            throw new FormatException($"Unsupported function key: {raw}");
        }
        if (raw.Length == 1)
        {
            var c = char.ToUpperInvariant(raw[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return c;
        }
        throw new FormatException($"Unsupported key: {raw}");
    }

    /// <summary>Display text of a virtual-key code.</summary>
    public static string DisplayKey(int vk)
    {
        if (VkDisplay.TryGetValue(vk, out var text))
            return text;
        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x70 + 1}";
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39)
            return ((char)vk).ToString();
        return $"VK_{vk:X2}";
    }

    /// <summary>"Ctrl+Alt+Shift+Win+Key" in that fixed order.</summary>
    public static string Format(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & HotkeyModifiers.Control) != 0)
            parts.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0)
            parts.Add("Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0)
            parts.Add("Shift");
        if ((modifiers & HotkeyModifiers.Win) != 0)
            parts.Add("Win");
        parts.Add(DisplayKey(vk));
        return string.Join('+', parts);
    }

    /// <summary>Parses shortcut text. Disabled names give <see cref="Hotkey.Disabled"/>.</summary>
    /// <exception cref="FormatException">Empty text, no key, or more than one key.</exception>
    public static Hotkey Parse(string? spec)
    {
        if (IsDisabledSpec(spec))
            return Hotkey.Disabled;
        if (string.IsNullOrWhiteSpace(spec))
            throw new FormatException("Hotkey is empty");
        var tokens = spec.Trim().Replace('-', '+').Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            throw new FormatException("Hotkey is empty");
        var modifiers = 0;
        string? key = null;
        foreach (var token in tokens)
        {
            if (ModifierNames.TryGetValue(token.ToLowerInvariant(), out var mapped))
            {
                modifiers |= mapped;
                continue;
            }
            if (key is not null)
                throw new FormatException($"More than one key in hotkey: {spec}");
            key = token;
        }
        if (key is null)
            throw new FormatException($"Hotkey is missing a key: {spec}");
        var vk = VkFromName(key);
        return new Hotkey(modifiers, vk, Format(modifiers, vk));
    }

    /// <summary>Parses shortcut text, treating anything unusable as disabled (what a hand-edited skills.json needs).</summary>
    public static Hotkey ParseOrDisabled(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec) || IsDisabledSpec(spec))
            return Hotkey.Disabled;
        try
        {
            return Parse(spec);
        }
        catch (FormatException)
        {
            return Hotkey.Disabled;
        }
    }

    /// <summary>Canonical stored text of <paramref name="value"/>, or <paramref name="fallback"/> when it is empty or invalid.</summary>
    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrEmpty(value))
            return fallback;
        var text = value.Trim();
        if (text.Length == 0 || IsDisabledSpec(text))
            return "none";
        try
        {
            var parsed = Parse(text);
            return parsed.IsDisabled ? "none" : parsed.Display;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
