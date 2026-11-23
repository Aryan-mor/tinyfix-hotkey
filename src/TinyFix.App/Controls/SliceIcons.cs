// The icons a donut slice can show: a set chosen from Material Design Icons, each by its own name as key, and the built-in skills' own icons. The path data
// is in SliceIcons.Data.cs.

using Avalonia.Media;

namespace TinyFix.App.Controls;

/// <summary>The icon picker's groups.</summary>
public enum SliceIconCategory
{
    TextAndData,
    General,
}

/// <summary>The icons a slice can show , drawn from 24 × 24 path data.</summary>
internal static partial class SliceIcons
{
    /// <summary>The side of the square every icon's path data is drawn in.</summary>
    internal const double Box = 24;

    // Built on first use: the data is in another file of this class, and static fields of a class split over files
    // are not set up in a known order.
    private static Dictionary<string, int>? _index;

    private static Dictionary<string, int> Index =>
        _index ??= Data.Select((icon, i) => (icon.Key, i)).ToDictionary(e => e.Key, e => e.i, StringComparer.Ordinal);

    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.Ordinal);

    /// <summary>Every icon's key, in the picker's order.</summary>
    internal static IEnumerable<string> Keys => Data.Select(icon => icon.Key);

    /// <summary>The picker's groups and their icons, in order.</summary>
    internal static IEnumerable<(SliceIconCategory Category, IReadOnlyList<string> Keys)> Categories =>
        CategoryData.Select(c => (c.Category, (IReadOnlyList<string>)c.Keys));

    internal static bool Exists(string? key) => key is not null && Index.ContainsKey(key);

    /// <summary>The built-in skills that have an icon of their own (tests: each is one).</summary>
    internal static IEnumerable<string> DefaultIds => DefaultData.Keys;

    /// <summary>The icon a skill's slice shows: the one of its id for a built-in skill, else none (the slice shows its name).</summary>
    internal static string? For(string skillId) => DefaultData.TryGetValue(skillId, out var key) ? key : null;

    /// <summary>
    /// The shape of icon <paramref name="key"/> in a <see cref="Box"/> × <see cref="Box"/> square, or null.
    /// </summary>
    internal static Geometry? Geometry(string? key)
    {
        if (key is null || !Index.TryGetValue(key, out var i))
            return null;
        if (!Geometries.TryGetValue(key, out var geometry))
            Geometries[key] = geometry = StreamGeometry.Parse(Data[i].Path);
        return geometry;
    }

    /// <summary>
    /// The icons matching <paramref name="text"/>: every word of it starts a word of the icon's name or search words.
    /// Blank: every icon.
    /// </summary>
    internal static IEnumerable<string> Search(string? text)
    {
        var wanted = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Data.Where(icon =>
        {
            var words = icon.Words.Split(' ');
            return wanted.All(w => words.Any(word => word.StartsWith(w, StringComparison.OrdinalIgnoreCase)));
        }).Select(icon => icon.Key);
    }
}
