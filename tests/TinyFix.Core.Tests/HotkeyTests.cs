// Hotkey parsing tests, ported case by case from tests/test_hotkey.py of the Python app.

using TinyFix.Core.Hotkeys;

namespace TinyFix.Core.Tests;

public class HotkeyTests
{
    [Fact]
    public void Default_hotkey_is_ctrl_alt_space()
    {
        var hotkey = HotkeyParser.Parse(HotkeyDefaults.Fix);
        Assert.Equal("Ctrl+Alt+Space", hotkey.Display);
        Assert.Equal(0x20, hotkey.Vk);
        Assert.NotEqual(0, hotkey.Modifiers & HotkeyModifiers.Control);
        Assert.NotEqual(0, hotkey.Modifiers & HotkeyModifiers.Alt);
        Assert.True(hotkey.IsSafe);
        Assert.False(hotkey.IsDisabled);
    }

    [Fact]
    public void Default_fix_all_hotkey_is_ctrl_f3()
    {
        var hotkey = HotkeyParser.Parse(HotkeyDefaults.FixAll);
        Assert.Equal("Ctrl+F3", hotkey.Display);
        Assert.Equal(0x72, hotkey.Vk);
        Assert.NotEqual(0, hotkey.Modifiers & HotkeyModifiers.Control);
        Assert.True(hotkey.IsSafe);
    }

    [Fact]
    public void Parses_function_key_without_modifier()
    {
        var hotkey = HotkeyParser.Parse("F8");
        Assert.Equal("F8", hotkey.Display);
        Assert.Equal(0x77, hotkey.Vk);
        Assert.True(hotkey.IsFunctionKey);
        Assert.True(hotkey.IsSafe);
    }

    [Fact]
    public void Letter_without_modifier_is_unsafe() => Assert.False(HotkeyParser.Parse("A").IsSafe);

    [Fact]
    public void Canonicalizes_separators_and_case() => Assert.Equal("Ctrl+Alt+Space", HotkeyParser.Parse("ctrl-alt-space").Display);

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    public void Rejects_empty_and_double_keys(string spec) => Assert.Throws<FormatException>(() => HotkeyParser.Parse(spec));

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("disabled")]
    [InlineData("Disabled")]
    [InlineData("off")]
    [InlineData("no")]
    [InlineData("None/Disabled")]
    [InlineData("none / disabled")]
    public void Disabled_hotkey_specs(string spec)
    {
        Assert.True(HotkeyParser.IsDisabledSpec(spec));
        var hotkey = HotkeyParser.Parse(spec);
        Assert.True(hotkey.IsDisabled);
        Assert.Equal("None", hotkey.Display);
        Assert.True(hotkey.IsSafe);
        Assert.Equal(Hotkey.Disabled, hotkey);
    }

    [Fact]
    public void Default_translate_hotkey_is_ctrl_alt_t()
    {
        var hotkey = HotkeyParser.Parse(HotkeyDefaults.Translate);
        Assert.Equal("Ctrl+Alt+T", hotkey.Display);
        Assert.Equal('T', hotkey.Vk);
        Assert.True(hotkey.IsSafe);
    }

    [Fact]
    public void Default_skill_menu_hotkey_is_ctrl_alt_m()
    {
        var hotkey = HotkeyParser.Parse(HotkeyDefaults.Menu);
        Assert.Equal("Ctrl+Alt+M", hotkey.Display);
        Assert.False(hotkey.IsDisabled);
    }

    [Fact]
    public void Unknown_text_parses_as_disabled_when_lenient()
    {
        Assert.True(HotkeyParser.ParseOrDisabled("not-a-hotkey").IsDisabled);
        Assert.True(HotkeyParser.ParseOrDisabled(null).IsDisabled);
    }

    [Fact]
    public void Every_key_choice_parses()
    {
        Assert.All(HotkeyParser.KeyChoices, key => Assert.False(HotkeyParser.Parse("Ctrl+" + key).IsDisabled));
    }
}
