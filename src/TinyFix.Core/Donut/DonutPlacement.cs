// Where the donut window goes: centred on the caret or the mouse pointer, kept inside the monitor it opens on.

namespace TinyFix.Core.Donut;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>True when the point lies inside the rectangle.</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>Window placement maths.</summary>
public static class DonutPlacement
{
    /// <summary>
    /// Top-left corner of a square window of <paramref name="size"/> pixels centred on (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) and moved just enough to lie inside <paramref name="workArea"/>. A window larger than
    /// the work area is pinned to its top-left corner.
    /// </summary>
    public static (int X, int Y) CenterClamped(int centerX, int centerY, int size, PixelRect workArea)
    {
        var x = centerX - size / 2;
        var y = centerY - size / 2;
        x = Math.Max(workArea.X, Math.Min(x, workArea.Right - size));
        y = Math.Max(workArea.Y, Math.Min(y, workArea.Bottom - size));
        return (x, y);
    }

    /// <summary>The work area that holds the point, else the first one (the primary screen).</summary>
    public static PixelRect PickWorkArea(int x, int y, IReadOnlyList<PixelRect> workAreas)
    {
        if (workAreas.Count == 0)
            return new PixelRect(0, 0, 1920, 1080);
        foreach (var area in workAreas)
            if (area.Contains(x, y))
                return area;
        // Not inside any: the nearest one by distance to its centre.
        return workAreas.OrderBy(a => Math.Pow(a.X + a.Width / 2.0 - x, 2) + Math.Pow(a.Y + a.Height / 2.0 - y, 2)).First();
    }
}
