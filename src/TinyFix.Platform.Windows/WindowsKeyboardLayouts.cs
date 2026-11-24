// Reads the keyboard layouts installed in Windows. For every physical key, with and without Shift and AltGr, the OS is
// asked which character the layout gives (ToUnicodeEx), so nothing is typed by hand and every installed layout works.

using System.Globalization;
using System.Runtime.Versioning;
using TinyFix.Core.Layouts;

namespace TinyFix.Platform.Windows;

/// <summary>The installed layouts of the current user.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsKeyboardLayouts : IKeyboardLayouts
{
    private const uint MapVscToVkEx = 3;

    // ToUnicodeEx flag: leave the keyboard state alone, so a dead key does not swallow the next call.
    private const uint KeepState = 0x4;

    // The keys that type characters: the number row, the letter rows, the keys next to them, and the extra ISO key.
    private static readonly int[] ScanCodes = [.. Enumerable.Range(0x02, 0x39 - 0x02), 0x56];

    /// <inheritdoc />
    public IReadOnlyList<KeyboardLayout> Installed()
    {
        var result = new List<KeyboardLayout>();
        foreach (var handle in Handles())
        {
            var langId = (int)(handle & 0xFFFF);
            string language, name;
            try
            {
                var culture = CultureInfo.GetCultureInfo(langId);
                language = culture.TwoLetterISOLanguageName;
                name = culture.EnglishName;
            }
            catch (CultureNotFoundException)
            {
                continue;
            }
            // One layout per language: a second English layout would only make the choice unclear.
            if (result.Any(l => l.Language == language.ToLowerInvariant()))
                continue;
            var keys = Read(handle);
            if (keys.Count > 0)
                result.Add(new KeyboardLayout(language, name, keys));
        }
        return result;
    }

    private static unsafe nint[] Handles()
    {
        var count = NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0)
            return [];
        var list = new nint[count];
        fixed (nint* p = list)
            count = NativeMethods.GetKeyboardLayoutList(count, p);
        return count == list.Length ? list : list[..Math.Max(count, 0)];
    }

    private static unsafe Dictionary<int, char> Read(nint layout)
    {
        var keys = new Dictionary<int, char>();
        byte* state = stackalloc byte[256];
        char* buffer = stackalloc char[8];
        foreach (var scan in ScanCodes)
        {
            var vk = NativeMethods.MapVirtualKeyExW((uint)scan, MapVscToVkEx, layout);
            if (vk == 0)
                continue;
            foreach (var (modifier, slotFlag) in new[] { (0, 0), (1, KeySlots.Shift), (2, KeySlots.AltGr) })
            {
                new Span<byte>(state, 256).Clear();
                if (modifier == 1)
                    state[NativeMethods.VkShift] = 0x80;
                else if (modifier == 2)
                {
                    state[NativeMethods.VkControl] = 0x80;
                    state[NativeMethods.VkMenu] = 0x80;
                }
                var length = NativeMethods.ToUnicodeEx(vk, (uint)scan, state, buffer, 8, KeepState, layout);
                // One plain character only: a dead key (negative), a ligature (two) or a control code is not a typed letter.
                if (length != 1 || char.IsControl(buffer[0]))
                    continue;
                keys[scan | slotFlag] = buffer[0];
            }
        }
        return keys;
    }
}
