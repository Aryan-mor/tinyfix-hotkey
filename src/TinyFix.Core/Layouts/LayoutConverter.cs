// Turns text typed on the wrong keyboard layout into what the right layout would have typed: every character goes back
// to the physical key that made it and forward to the character the same key gives on the other layout.

using System.Text;

namespace TinyFix.Core.Layouts;

/// <summary>Why a layout conversion gave no text.</summary>
public enum LayoutFailure
{
    /// <summary>It worked.</summary>
    None,

    /// <summary>A layout the rule names is not installed.</summary>
    Missing,

    /// <summary>The text does not look like it was typed on any of the layouts (or is already right).</summary>
    Unknown,
}

/// <summary>The result of a conversion.</summary>
public readonly record struct LayoutResult(string? Text, LayoutFailure Failure)
{
    /// <summary>True when <see cref="Text"/> holds the converted text.</summary>
    public bool Ok => Failure == LayoutFailure.None;
}

/// <summary>Conversion between keyboard layouts.</summary>
public static class LayoutConverter
{
    /// <summary>The id a rule uses for "any layout".</summary>
    public const string Any = "any";

    /// <summary>The layout with language code <paramref name="id"/> (the first one when there are several), or null.</summary>
    public static KeyboardLayout? Find(IEnumerable<KeyboardLayout> layouts, string id) =>
        layouts.FirstOrDefault(l => l.Language == id.Trim().ToLowerInvariant());

    /// <summary>Re-types <paramref name="text"/>: what <paramref name="to"/> would have typed on the keys that made it on <paramref name="from"/>. Other characters stay.</summary>
    public static string Convert(string text, KeyboardLayout from, KeyboardLayout to)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(from.TryGetSlot(c, out var slot) && to.TryGetChar(slot, out var typed) ? typed : c);
        return sb.ToString();
    }

    /// <summary>
    /// The layout among <paramref name="candidates"/> that can type the most letters of <paramref name="text"/>, or null
    /// when none can or <paramref name="target"/> can type at least as many (the text is already right). A tie goes to
    /// the layout listed first, which is the user's first layout.
    /// </summary>
    public static KeyboardLayout? DetectSource(string text, IEnumerable<KeyboardLayout> candidates, KeyboardLayout target)
    {
        KeyboardLayout? best = null;
        var bestScore = 0;
        foreach (var layout in candidates)
        {
            if (layout.Language == target.Language)
                continue;
            var score = layout.CountLetters(text);
            if (score > bestScore)
                (best, bestScore) = (layout, score);
        }
        return best is not null && target.CountLetters(text) < bestScore ? best : null;
    }

    /// <summary>Converts to <paramref name="toId"/> from <paramref name="fromId"/>, or from whichever installed layout the text was typed on when <paramref name="fromId"/> is <see cref="Any"/>.</summary>
    public static LayoutResult ConvertTo(string text, string fromId, string toId, IReadOnlyList<KeyboardLayout> installed)
    {
        var to = Find(installed, toId);
        if (to is null)
            return new LayoutResult(null, LayoutFailure.Missing);
        KeyboardLayout? from;
        if (string.Equals(fromId, Any, StringComparison.OrdinalIgnoreCase))
            from = DetectSource(text, installed, to);
        else
        {
            from = Find(installed, fromId);
            if (from is null)
                return new LayoutResult(null, LayoutFailure.Missing);
        }
        return Finish(text, from, to);
    }

    /// <summary>
    /// The two layouts of a swap: the ones named, or, when they are not both installed and the machine has exactly two
    /// layouts, those two (so a user with English and Arabic needs no setup). Null when there is no clear pair.
    /// </summary>
    public static (KeyboardLayout A, KeyboardLayout B)? ResolvePair(string idA, string idB, IReadOnlyList<KeyboardLayout> installed)
    {
        var a = Find(installed, idA);
        var b = Find(installed, idB);
        if (a is not null && b is not null && a != b)
            return (a, b);
        return installed.Count == 2 ? (installed[0], installed[1]) : null;
    }

    /// <summary>True when the layouts a rule needs are installed, so it can run.</summary>
    public static bool CanRun(bool swap, string fromId, string toId, IReadOnlyList<KeyboardLayout> installed) =>
        swap
            ? ResolvePair(fromId, toId, installed) is not null
            : Find(installed, toId) is not null && (string.Equals(fromId, Any, StringComparison.OrdinalIgnoreCase) || Find(installed, fromId) is not null);

    /// <summary>Swaps two layouts: text typed on A becomes what B would have typed, and the other way round, by whichever layout types more of the letters.</summary>
    public static LayoutResult Swap(string text, string idA, string idB, IReadOnlyList<KeyboardLayout> installed)
    {
        if (ResolvePair(idA, idB, installed) is not var (a, b))
            return new LayoutResult(null, LayoutFailure.Missing);
        var scoreA = a.CountLetters(text);
        var scoreB = b.CountLetters(text);
        if (scoreA == scoreB)
            return new LayoutResult(null, LayoutFailure.Unknown);
        return scoreA > scoreB ? Finish(text, a, b) : Finish(text, b, a);
    }

    private static LayoutResult Finish(string text, KeyboardLayout? from, KeyboardLayout to)
    {
        if (from is null)
            return new LayoutResult(null, LayoutFailure.Unknown);
        var converted = Convert(text, from, to);
        return converted == text ? new LayoutResult(null, LayoutFailure.Unknown) : new LayoutResult(converted, LayoutFailure.None);
    }
}
