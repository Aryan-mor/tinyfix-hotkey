// Donut paging, number-key and window placement tests (the slice geometry is covered by the donut control's own tests in
// TinyFix.App.Tests).

using TinyFix.Core.Donut;

namespace TinyFix.Core.Tests;

public class DonutTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(9, 9, 1)]
    [InlineData(10, 8, 2)]
    [InlineData(16, 8, 2)]
    [InlineData(17, 8, 3)]
    public void Pages_hold_up_to_eight_skills_once_there_are_more_than_nine(int total, int pageSize, int pages)
    {
        Assert.Equal(Math.Max(pageSize, 1), DonutPaging.PageSize(total));
        Assert.Equal(pages, DonutPaging.PageCount(total));
    }

    [Fact]
    public void Page_ranges_cover_every_skill_exactly_once()
    {
        const int total = 19;
        var seen = new List<int>();
        for (var page = 0; page < DonutPaging.PageCount(total); page++)
        {
            var (start, count) = DonutPaging.PageRange(total, page);
            seen.AddRange(Enumerable.Range(start, count));
        }
        Assert.Equal(Enumerable.Range(0, total), seen);
    }

    [Fact]
    public void Turning_pages_wraps()
    {
        Assert.Equal(1, DonutPaging.Turn(0, 20, 1));
        Assert.Equal(0, DonutPaging.Turn(2, 20, 1));
        Assert.Equal(2, DonutPaging.Turn(0, 20, -1));
        Assert.Equal(0, DonutPaging.Turn(0, 5, 1));
    }

    [Fact]
    public void Number_keys_pick_the_slice_of_the_current_page()
    {
        Assert.Equal(0, DonutPaging.ItemForDigit(1, 4, 0));
        Assert.Equal(3, DonutPaging.ItemForDigit(4, 4, 0));
        Assert.Equal(-1, DonutPaging.ItemForDigit(5, 4, 0));
        Assert.Equal(-1, DonutPaging.ItemForDigit(0, 4, 0));
        Assert.Equal(8, DonutPaging.ItemForDigit(9, 9, 0));
        // Second page of 12 skills: 8 on the first, 4 on the second.
        Assert.Equal(8, DonutPaging.ItemForDigit(1, 12, 1));
        Assert.Equal(11, DonutPaging.ItemForDigit(4, 12, 1));
        Assert.Equal(-1, DonutPaging.ItemForDigit(5, 12, 1));
    }

    [Fact]
    public void Window_is_centred_on_the_point_and_clamped_to_the_work_area()
    {
        var area = new PixelRect(0, 0, 1920, 1040);
        Assert.Equal((860, 420), DonutPlacement.CenterClamped(960, 520, 200, area));
        Assert.Equal((0, 0), DonutPlacement.CenterClamped(5, 5, 200, area));
        Assert.Equal((1720, 840), DonutPlacement.CenterClamped(1919, 1039, 200, area));
        var second = new PixelRect(1920, 0, 1280, 1000);
        Assert.Equal((1920, 800), DonutPlacement.CenterClamped(1930, 990, 200, second));
        Assert.Equal((0, 0), DonutPlacement.CenterClamped(50, 50, 4000, area));
    }

    [Fact]
    public void The_work_area_is_picked_by_point_then_by_nearest_centre()
    {
        var left = new PixelRect(0, 0, 1920, 1080);
        var right = new PixelRect(1920, 0, 1280, 1024);
        Assert.Equal(right, DonutPlacement.PickWorkArea(2000, 500, [left, right]));
        Assert.Equal(left, DonutPlacement.PickWorkArea(10, 10, [left, right]));
        Assert.Equal(right, DonutPlacement.PickWorkArea(5000, 500, [left, right]));
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), DonutPlacement.PickWorkArea(1, 1, []));
    }
}
