// The slice icons: every one draws, every built-in skill has its own icon, and the search finds icons by name.

using Avalonia.Headless.XUnit;
using TinyFix.App.Controls;

namespace TinyFix.App.Tests;

public class SliceIconsTests
{
    [AvaloniaFact]
    public void Every_icon_draws_inside_its_square()
    {
        foreach (var key in SliceIcons.Keys)
        {
            var bounds = SliceIcons.Geometry(key)!.Bounds;
            Assert.True(bounds.Width > 0 && bounds.Height > 0, key);
            Assert.InRange(bounds.Left, -0.01, SliceIcons.Box);
            Assert.InRange(bounds.Right, 0, SliceIcons.Box + 0.01);
            Assert.InRange(bounds.Top, -0.01, SliceIcons.Box);
            Assert.InRange(bounds.Bottom, 0, SliceIcons.Box + 0.01);
        }
    }

    [Fact]
    public void Every_icon_is_in_the_picker_and_no_key_is_listed_twice()
    {
        var keys = SliceIcons.Keys.ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(keys.ToHashSet(), SliceIcons.Categories.SelectMany(c => c.Keys).ToHashSet());
        Assert.Equal(Enum.GetValues<SliceIconCategory>(), SliceIcons.Categories.Select(c => c.Category));
    }

    [Fact]
    public void Every_built_in_skill_has_an_icon_and_a_custom_skill_has_none()
    {
        foreach (var id in TinyFix.Core.Skills.SkillIds.Builtin)
            Assert.True(SliceIcons.Exists(SliceIcons.For(id)), id);
        Assert.Equal(TinyFix.Core.Skills.SkillIds.Builtin.Order(), SliceIcons.DefaultIds.Order());
        Assert.Null(SliceIcons.For("3f2c1a9e-0b52-4a0e-9d5e-1a2b3c4d5e6f"));
    }

    [Fact]
    public void The_picker_finds_icons_by_name_and_search_words()
    {
        Assert.Contains("translate", SliceIcons.Search("translate"));
        Assert.Contains("pencil-outline", SliceIcons.Search("edit"));
        Assert.Contains("check-circle-outline", SliceIcons.Search("check"));
        Assert.Empty(SliceIcons.Search("zzzz"));
        Assert.Equal(SliceIcons.Keys, SliceIcons.Search(" "));
    }
}
