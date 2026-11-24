// Renders the real windows headlessly (Skia) in English, Persian (right to left) and Japanese. It catches XAML and
// binding errors and writes screenshots to bin/<config>/<tfm>/screenshots for visual review. Glyphs are not asserted:
// a CI machine may lack the Persian or Japanese fonts, so these tests check that rendering works and has content.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using TinyFix.App.Controls;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;
using TinyFix.Core.Llm;
using TinyFix.Core.Skills;

namespace TinyFix.App.Tests;

public class RenderTests
{
    public static TheoryData<string, string> SettingsPages
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var language in new[] { "en", "fa", "ja", "de" })
                foreach (var page in new[] { "General", "Donut", "Skills", "Variables", "Online", "Cost", "Updates", "About" })
                    data.Add(page, language);
            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(SettingsPages))]
    public void Settings_pages_render(string page, string language)
    {
        var s = TestServices.EnsureInitialized(language);
        using var vm = new SettingsViewModel(s);
        vm.Selected = vm.Pages.First(p => p.GetType().Name.StartsWith(page, StringComparison.Ordinal));
        var window = new SettingsWindow(vm) { Width = 1040, Height = 720 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(language == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
        Assert.Equal(s.Loc["settings.title"], window.Title);
        var content = window.GetVisualDescendants().OfType<ContentControl>().First(c => c.Content == vm.Selected);
        Assert.NotNull(content.Presenter?.Child);
        Save(window, $"settings-{page.ToLowerInvariant()}-{language}.png");
        window.Close();
        vm.Selected = vm.Pages[0];
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("fa")]
    [InlineData("ja")]
    public void The_skill_selector_donut_renders(string language)
    {
        var s = TestServices.EnsureInitialized(language);
        var vm = new DonutViewModel(SkillCatalog.VisibleMenuSkills(SkillCatalog.DefaultStore()), s.Loc, _ => true) { Hover = 0 };
        var window = new RadialWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(DonutMenu.SideFor(DonutMenu.DefaultOuterRadius, subRing: false), window.Menu.Bounds.Width, 1);
        Assert.Equal(DonutMenu.SideFor(DonutMenu.DefaultOuterRadius, subRing: false), window.Menu.Bounds.Height, 1);
        Save(window, $"donut-{language}.png");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(12)]
    public void The_donut_renders_any_number_of_skills(int count)
    {
        var s = TestServices.EnsureInitialized("en");
        var skills = Enumerable.Range(0, count).Select(i => SkillCatalog.NewCustomSkill("Skill " + (i + 1))).ToList();
        var vm = new DonutViewModel(skills, s.Loc, _ => count != 2) { Hover = count > 1 ? 1 : 0 };
        var window = new RadialWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, $"donut-{count}-skills.png");
        window.Close();
    }

    [AvaloniaFact]
    public void The_donut_renders_in_the_dark_theme()
    {
        var s = TestServices.EnsureInitialized("en");
        s.Store.Settings.Theme = "dark";
        s.Theme.Apply("en");
        var vm = new DonutViewModel(SkillCatalog.VisibleMenuSkills(SkillCatalog.DefaultStore()), s.Loc, _ => false) { Hover = 1 };
        var window = new RadialWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "donut-dark.png");
        window.Close();
        s.Store.Settings.Theme = "system";
        s.Theme.Apply("en");
    }

    [AvaloniaFact]
    public void The_toast_shows_localized_text()
    {
        var s = TestServices.EnsureInitialized("en");
        var toast = new ToastWindow();
        toast.Display(s.Loc.Format("status.running", "Fix"), failure: false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("Running Fix...", toast.Text);
        Save(toast, "toast-en.png");
        toast.Close();
    }

    [AvaloniaFact]
    public void The_settings_window_follows_a_language_change_live()
    {
        var s = TestServices.EnsureInitialized("en");
        using var vm = new SettingsViewModel(s);
        var window = new SettingsWindow(vm);
        window.Show();
        Assert.Equal("TinyFix Settings", window.Title);
        s.Loc.SetLanguage("fa");
        Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
        Assert.Equal(s.Loc["settings.title"], window.Title);
        Assert.NotEqual("TinyFix Settings", window.Title);
        window.Close();
        TestServices.EnsureInitialized("en");
    }

    [AvaloniaTheory]
    [InlineData("en", "none")]
    [InlineData("en", "saved")]
    [InlineData("en", "replacing")]
    [InlineData("en", "confirm")]
    [InlineData("fa", "none")]
    [InlineData("fa", "saved")]
    [InlineData("fa", "replacing")]
    [InlineData("fa", "confirm")]
    public void The_ai_models_page_renders_each_key_state(string language, string state)
    {
        const string secret = "test-key-123";
        var s = TestServices.EnsureInitialized(language);
        TestServices.Ports.Keys.Clear();
        if (state != "none")
            TestServices.Ports.Set(LlmSecrets.MainSlot, secret);
        using var vm = new SettingsViewModel(s);
        var page = vm.Pages.OfType<OnlinePageViewModel>().Single();
        if (state == "replacing")
            page.Main.BeginReplaceCommand.Execute(null);
        if (state == "confirm")
            page.Main.BeginRemoveCommand.Execute(null);
        vm.Selected = page;
        var window = new SettingsWindow(vm) { Width = 1040, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(language == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == s.Loc["nav.models"]);
        foreach (var block in window.GetVisualDescendants().OfType<TextBlock>())
            Assert.DoesNotContain(secret, block.Text ?? "");
        foreach (var box in window.GetVisualDescendants().OfType<TextBox>())
            Assert.DoesNotContain(secret, box.Text ?? "");
        var names = window.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).ToList();
        if (state is "none" or "replacing")
        {
            var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PasswordChar == '*');
            Assert.True(box.Focusable);
            Assert.True(box.IsTabStop);
            Assert.Contains(s.Loc["models.show_key"], names);
        }
        if (state == "saved")
        {
            var mask = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == EndpointEditorViewModel.SavedKeyMask);
            Assert.False(mask.IsHitTestVisible);
            Assert.False(mask.Focusable);
            Assert.Contains(s.Loc["models.replace"], names);
            Assert.Contains(s.Loc["models.remove"], names);
            Assert.Contains(s.Loc["models.key_saved"], window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        if (state == "replacing")
            Assert.Contains(s.Loc["models.cancel"], names);
        if (state == "confirm")
        {
            Assert.Contains(s.Loc["models.remove_confirm"], window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains(s.Loc["models.remove"], names);
            Assert.Contains(s.Loc["models.cancel"], names);
        }
        Save(window, $"models-key-{state}-{language}.png");
        window.Close();
        TestServices.Ports.Keys.Clear();
        if (language != "en")
            TestServices.EnsureInitialized("en");
    }

    internal static void Save(TopLevel window, string name)
    {
        // Disposed at the end, so the frame stays alive while Skia encodes it.
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 50 && frame.PixelSize.Height > 50);
        using var stream = File.Create(Path.Combine(TestServices.ScreenshotDirectory, name));
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
