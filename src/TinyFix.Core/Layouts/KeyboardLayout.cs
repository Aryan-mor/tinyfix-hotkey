// A keyboard layout as data: which character each physical key gives. The OS adapters build one per installed layout
// (Windows asks ToUnicodeEx for every key), so no table is typed by hand and every layout the OS has works.

namespace TinyFix.Core.Layouts;

/// <summary>
/// A physical key plus the modifier it is pressed with, as one integer: the scan code in the low byte,
/// <see cref="Shift"/> and <see cref="AltGr"/> as flags. The same slot on two layouts is the same finger position.
/// </summary>
public static class KeySlots
{
    /// <summary>The key is pressed with Shift.</summary>
    public const int Shift = 0x100;

    /// <summary>The key is pressed with AltGr (Ctrl+Alt).</summary>
    public const int AltGr = 0x200;
}

/// <summary>One installed keyboard layout.</summary>
public sealed class KeyboardLayout
{
    private readonly Dictionary<char, int> _slotOf = [];

    /// <summary>Creates a layout from the character of every slot.</summary>
    /// <param name="language">Short language code of the layout ("fa", "en"), lower case. Also its id in settings.</param>
    /// <param name="name">Name for people, e.g. "Persian (Iran)".</param>
    /// <param name="keys">Slot to character (see <see cref="KeySlots"/>).</param>
    public KeyboardLayout(string language, string name, IReadOnlyDictionary<int, char> keys)
    {
        Language = language.Trim().ToLowerInvariant();
        Name = name;
        Keys = keys;
        // A character that sits on several slots is reached the plainest way: no modifier before Shift before AltGr.
        foreach (var (slot, c) in keys.OrderBy(pair => pair.Key))
            _slotOf.TryAdd(c, slot);
    }

    /// <summary>The language code and id, e.g. "fa".</summary>
    public string Language { get; }

    /// <summary>Name for people.</summary>
    public string Name { get; }

    /// <summary>The character of every slot.</summary>
    public IReadOnlyDictionary<int, char> Keys { get; }

    /// <summary>The slot that types <paramref name="c"/>, when this layout can type it.</summary>
    public bool TryGetSlot(char c, out int slot) => _slotOf.TryGetValue(c, out slot);

    /// <summary>The character a slot gives on this layout.</summary>
    public bool TryGetChar(int slot, out char c) => Keys.TryGetValue(slot, out c);

    /// <summary>How many letters of <paramref name="text"/> this layout can type.</summary>
    public int CountLetters(string text)
    {
        var count = 0;
        foreach (var c in text)
            if (char.IsLetter(c) && _slotOf.ContainsKey(c))
                count++;
        return count;
    }
}

/// <summary>The keyboard layouts installed on the machine. Null on a platform that cannot list them.</summary>
public interface IKeyboardLayouts
{
    /// <summary>One layout per language, in the order the OS lists them (the user's first choice first).</summary>
    IReadOnlyList<KeyboardLayout> Installed();
}
