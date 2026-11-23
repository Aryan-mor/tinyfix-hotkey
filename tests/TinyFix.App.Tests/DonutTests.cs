// Tests of the donut: its view model (paging, number keys, hover, availability) and the pointer and keyboard handling
// of its window, run headless.

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.Tests;

public class DonutTests
{
    private static List<Skill> Skills(int count) =>
        Enumerable.Range(0, count).Select(i => SkillCatalog.NewCustomSkill("Skill " + (i + 1))).ToList();

    private static List<Skill> DefaultEnabled() => SkillCatalog.VisibleMenuSkills(SkillCatalog.DefaultStore());

    [AvaloniaFact]
    public void Slices_are_numbered_from_one_and_carry_names_and_shortcuts()
    {
        var s = TestServices.EnsureInitialized("en");
        var vm = new DonutViewModel(DefaultEnabled(), s.Loc, aiReady: true);
        Assert.Equal(["Fix", "Fix all", "Translate", "Improve prompt"], vm.Slices.Select(x => x.Label));
        Assert.Equal("Ctrl+Alt+Space", vm.Slices[0].Caption);
        Assert.Null(vm.Slices[3].Caption);
        Assert.Equal(1, vm.PageCount);
        Assert.Equal("", vm.AiHint);
    }

    [AvaloniaFact]
    public void Builtin_names_follow_the_ui_language_but_user_names_do_not()
    {
        var s = TestServices.EnsureInitialized("de");
        var skills = DefaultEnabled();
        skills.Add(SkillCatalog.NewCustomSkill("Fix"));
        var vm = new DonutViewModel(skills, s.Loc, true);
        Assert.Equal("Alles korrigieren", vm.Slices[1].Label);
        Assert.Equal("Fix", vm.Slices[4].Label);
        TestServices.EnsureInitialized("en");
    }

    [AvaloniaFact]
    public void Ai_skills_are_not_usable_without_an_online_model_and_say_why()
    {
        var s = TestServices.EnsureInitialized("en");
        var vm = new DonutViewModel(DefaultEnabled(), s.Loc, aiReady: false);
        Assert.All(vm.Slices, slice => Assert.False(slice.IsEnabled));
        Assert.NotEqual("", vm.AiHint);
        int? result = -2;
        vm.Completed += r => result = r;
        Assert.False(vm.Choose(2));
        Assert.Equal(-2, result);
        var ready = new DonutViewModel(DefaultEnabled(), s.Loc, aiReady: true);
        ready.Completed += r => result = r;
        Assert.True(ready.Choose(0));
        Assert.Equal(0, result);
        vm.Hover = 2;
        Assert.Equal("Translate", vm.CenterTitle);
        Assert.Equal(s.Loc["donut.needs_ai"], vm.CenterSubtitle);
    }

    [AvaloniaFact]
    public void More_than_nine_skills_make_pages_and_digits_follow_the_page()
    {
        var s = TestServices.EnsureInitialized("en");
        var vm = new DonutViewModel(Skills(12), s.Loc, true);
        Assert.Equal(2, vm.PageCount);
        Assert.Equal(8, vm.Slices.Count);
        int? result = null;
        vm.Completed += r => result = r;
        Assert.True(vm.ChooseDigit(3));
        Assert.Equal(2, result);
        vm.Turn(1);
        Assert.Equal(4, vm.Slices.Count);
        Assert.Equal("Skill 9", vm.Slices[0].Label);
        Assert.Contains("2", vm.PageText);
        Assert.True(vm.ChooseDigit(4));
        Assert.Equal(11, result);
        Assert.False(vm.ChooseDigit(5));
        vm.Turn(1);
        Assert.Equal(0, vm.Page);
    }

    [AvaloniaFact]
    public void Cancel_and_an_empty_donut_report_cleanly()
    {
        var s = TestServices.EnsureInitialized("en");
        var vm = new DonutViewModel([], s.Loc, true);
        Assert.Equal(s.Loc["donut.none"], vm.CenterTitle);
        var cancelled = false;
        vm.Completed += r => cancelled = r is null;
        vm.Cancel();
        Assert.True(cancelled);
    }

    [AvaloniaFact]
    public void Keyboard_keys_pick_move_page_and_cancel()
    {
        var s = TestServices.EnsureInitialized("en");
        var vm = new DonutViewModel(Skills(12), s.Loc, true);
        var window = new RadialWindow { DataContext = vm };
        window.Show();
        var results = new List<int?>();
        vm.Completed += results.Add;
        // The flip animation queues a second Tab until the first turn ends. With motion off the page changes at once.
        var motion = Motion.Enabled;
        Motion.Set(false);
        try
        {
            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.Equal(1, vm.Page);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.Equal(0, vm.Page);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Assert.Equal(0, vm.Hover);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal([1, 1, null], results);
        }
        finally
        {
            Motion.Set(motion);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_mouse_picks_the_slice_under_it_and_cancels_in_the_hole_or_outside()
    {
        var s = TestServices.EnsureInitialized("en");
        var vm = new DonutViewModel(Skills(4), s.Loc, true);
        var window = new RadialWindow { DataContext = vm };
        window.Show();
        var results = new List<int?>();
        vm.Completed += results.Add;
        var side = Controls.DonutMenu.SideFor(Controls.DonutMenu.DefaultOuterRadius, subRing: false);
        var center = new Point(side / 2, side / 2);

        // Right of the middle is slice 1 (second of four, clockwise from the top).
        var right = new Point(center.X + 100, center.Y);
        window.MouseMove(right);
        Assert.Equal(1, vm.Hover);
        window.MouseDown(right, MouseButton.Left);
        window.MouseUp(right, MouseButton.Left);
        // The hole cancels, and so does the corner outside the ring.
        window.MouseDown(center, MouseButton.Left);
        window.MouseDown(new Point(2, 2), MouseButton.Left);

        Assert.Equal([1, null, null], results);
        window.Close();
    }

    [AvaloniaFact]
    public void The_mouse_wheel_turns_the_page()
    {
        var s = TestServices.EnsureInitialized("en");
        var vm = new DonutViewModel(Skills(12), s.Loc, true);
        var window = new RadialWindow { DataContext = vm };
        window.Show();
        var motion = Motion.Enabled;
        Motion.Set(false);
        try
        {
            var side = Controls.DonutMenu.SideFor(Controls.DonutMenu.DefaultOuterRadius, subRing: false);
            window.MouseWheel(new Point(side / 2, side / 2), new Vector(0, -1));
            Assert.Equal(1, vm.Page);
            window.MouseWheel(new Point(side / 2, side / 2), new Vector(0, 1));
            Assert.Equal(0, vm.Page);
        }
        finally
        {
            Motion.Set(motion);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task The_controller_shows_the_donut_and_returns_the_choice()
    {
        var s = TestServices.EnsureInitialized("en");
        TestServices.Ports.Focused.Clear();
        var shown = s.Donut.ShowAsync(DefaultEnabled(), new ScreenPoint(300, 300), aiReady: true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var window = s.Donut.Window!;
        Assert.True(window.IsVisible);
        // A number key while the donut is open picks the third skill.
        window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
        Assert.Equal(2, await shown);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_donut_without_a_choice()
    {
        var s = TestServices.EnsureInitialized("en");
        var shown = s.Donut.ShowAsync(DefaultEnabled(), new ScreenPoint(300, 300), aiReady: true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        s.Donut.Window!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Null(await shown);
    }

    [AvaloniaFact]
    public async Task A_preview_uses_the_current_look_and_a_pick_does_not_run_a_skill()
    {
        var s = TestServices.EnsureInitialized("en");
        s.Store.Settings.LlmEnabled = true;
        s.Store.Settings.Donut.OuterRadius = 200;
        s.Store.Settings.Donut.Opacity = 0.5;
        s.Store.Settings.Donut.ShowShortcutHints = false;
        TestServices.Ports.Set(LlmDefaults.ProviderGoogle, "preview-key");
        TestServices.Ports.Selection = "Hello there";
        TestServices.Ports.Focused.Clear();
        var clipboard = TestServices.Ports.ClipboardText;
        var changes = TestServices.Ports.ChangeCount;
        Task? preview = null;
        try
        {
            preview = s.Donut.PreviewAsync(new ScreenPoint(420, 280));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var window = s.Donut.Window!;
            Assert.True(window.IsVisible);
            Assert.Equal(200, window.DonutControl.OuterRadius, 0.01);
            Assert.Equal(0.5, window.DonutControl.SliceOpacity, 0.01);
            Assert.False(window.DonutControl.ShowCaptions);
            window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
            await preview;
            preview = null;
            Assert.False(window.IsVisible);
            Assert.Empty(TestServices.Ports.Focused);
            Assert.Equal(clipboard, TestServices.Ports.ClipboardText);
            Assert.Equal(changes, TestServices.Ports.ChangeCount);
            Assert.Equal("Hello there", TestServices.Ports.Selection);
        }
        finally
        {
            if (s.Donut.IsVisible)
                s.Donut.Window!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            if (preview is not null)
                await preview;
            s.Store.Settings.LlmEnabled = false;
            s.Store.Settings.Donut = new DonutSettings();
            TestServices.Ports.Keys.Remove(LlmDefaults.ProviderGoogle);
            TestServices.Ports.Selection = null;
            TestServices.Ports.Focused.Clear();
        }
    }
}
