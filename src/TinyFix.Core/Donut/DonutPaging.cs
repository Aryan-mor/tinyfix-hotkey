// Paging of the donut: more than nine skills do not fit a readable ring, so the ring shows eight at a time and Tab or
// the mouse wheel turns to the next page (the idea of the reference project's two-faced donut, as plain integer math).

namespace TinyFix.Core.Donut;

/// <summary>Which skills are on which page, and what a number key picks.</summary>
public static class DonutPaging
{
    /// <summary>Up to this many skills share one page, so 1 to 9 all work.</summary>
    public const int SinglePageLimit = 9;

    /// <summary>Slices per page once there are more than <see cref="SinglePageLimit"/> skills.</summary>
    public const int MultiPageSize = 8;

    /// <summary>Slices on a page for <paramref name="total"/> skills.</summary>
    public static int PageSize(int total) => total <= SinglePageLimit ? Math.Max(total, 1) : MultiPageSize;

    /// <summary>Number of pages (at least 1).</summary>
    public static int PageCount(int total) => total <= SinglePageLimit ? 1 : (total + MultiPageSize - 1) / MultiPageSize;

    /// <summary>The first item index and how many items page <paramref name="page"/> holds.</summary>
    public static (int Start, int Count) PageRange(int total, int page)
    {
        var size = PageSize(total);
        var start = Math.Clamp(page, 0, PageCount(total) - 1) * size;
        return (start, Math.Max(0, Math.Min(size, total - start)));
    }

    /// <summary>The page after <paramref name="page"/> moved by <paramref name="delta"/>, wrapping around.</summary>
    public static int Turn(int page, int total, int delta)
    {
        var pages = PageCount(total);
        return ((page + delta) % pages + pages) % pages;
    }

    /// <summary>The overall item index a number key (1 to 9) selects on <paramref name="page"/>, or -1 when that slice does not exist.</summary>
    public static int ItemForDigit(int digit, int total, int page)
    {
        if (digit is < 1 or > 9)
            return -1;
        var (start, count) = PageRange(total, page);
        return digit <= count ? start + digit - 1 : -1;
    }
}
