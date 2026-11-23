// The setup wizard's state machine, its headless render (English and Persian, 100% and 125%), and the keyboard path.
// Keys in these tests are fake. The HTTP handler never leaves the process, and the data folder is the temp one.

using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;
using TinyFix.Core.Hotkeys;
using TinyFix.Core.Llm;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.Tests;

public class SetupWizardTests
{
    [Fact]
    public void Setup_is_offered_once_unless_the_skip_variable_is_set()
    {
        var settings = new AppSettings();
        Assert.False(settings.SetupCompleted);
        Assert.True(SetupWizardViewModel.ShouldOffer(settings, null));
        Assert.True(SetupWizardViewModel.ShouldOffer(settings, "0"));
        Assert.False(SetupWizardViewModel.ShouldOffer(settings, "1"));
        settings.SetupCompleted = true;
        Assert.False(SetupWizardViewModel.ShouldOffer(settings, null));
        Assert.Equal(20, SetupWizardViewModel.ProbeTimeoutSeconds);
    }

    [Fact]
    public void Key_pages_are_only_the_known_provider_urls()
    {
        Assert.Equal("https://aistudio.google.com/apikey", SetupWizardViewModel.KeyPage(LlmProtocols.Google));
        Assert.Equal("https://openrouter.ai/keys", SetupWizardViewModel.KeyPage(LlmProtocols.OpenRouter));
        Assert.Equal("https://platform.openai.com/api-keys", SetupWizardViewModel.KeyPage(LlmProtocols.OpenAi));
        Assert.Equal("https://console.anthropic.com/settings/keys", SetupWizardViewModel.KeyPage(LlmProtocols.Anthropic));
        Assert.Null(SetupWizardViewModel.KeyPage(LlmProtocols.Ollama));
        Assert.Null(SetupWizardViewModel.KeyPage(LlmProtocols.LmStudio));
        Assert.Null(SetupWizardViewModel.KeyPage(LlmProtocols.Custom));
    }

    [AvaloniaFact]
    public void Plain_reasons_keep_the_sanitized_detail_separate()
    {
        var s = TestServices.EnsureInitialized();
        Assert.Equal(s.Loc["setup.test_timeout"], SetupWizardViewModel.PlainReason(s.Loc, new LlmTimeoutException("Timed out after 20s")));
        Assert.Equal(s.Loc["setup.test_network"], SetupWizardViewModel.PlainReason(s.Loc, new LlmException(LlmErrorKind.Network, "Network error: HttpRequestException")));
        Assert.Equal(s.Loc["setup.test_wrong_key"], SetupWizardViewModel.PlainReason(s.Loc, new LlmException(LlmErrorKind.Http, "HTTP 401", 401)));
        Assert.Equal(s.Loc["setup.test_wrong_key"], SetupWizardViewModel.PlainReason(s.Loc, new LlmException(LlmErrorKind.Http, "HTTP 403", 403)));
        Assert.Equal(s.Loc["setup.test_other"], SetupWizardViewModel.PlainReason(s.Loc, new LlmException(LlmErrorKind.Http, "HTTP 500", 500)));
    }

    [AvaloniaFact]
    public void The_steps_run_in_order_and_the_model_step_gates_continue()
    {
        var s = TestServices.EnsureInitialized();
        using var vm = new SetupWizardViewModel(s);

        Assert.Equal(SetupWizardViewModel.Order, new[] { SetupStep.Welcome, SetupStep.Model, SetupStep.Skills, SetupStep.Done });
        Assert.Equal(SetupStep.Welcome, vm.Step);
        Assert.False(vm.CanGoBack);
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.True(vm.CanContinue);
        vm.BackCommand.Execute(null);
        Assert.Equal(SetupStep.Welcome, vm.Step);

        vm.ContinueCommand.Execute(null);
        Assert.Equal(SetupStep.Model, vm.Step);
        Assert.True(vm.CanGoBack);
        Assert.False(vm.CanContinue);
        Assert.Equal(s.Loc["setup.model.need_key"], vm.Validation);
        Assert.False(vm.ContinueCommand.CanExecute(null));

        vm.Endpoint.Model = "  ";
        Assert.Equal(s.Loc["setup.model.need_model"], vm.Validation);

        vm.Endpoint.Model = LlmProtocols.DefaultModel(LlmProtocols.Google);
        vm.Endpoint.ApiKey = "sk-fake";
        Assert.True(vm.ModelUsable);
        Assert.True(vm.CanContinue);
        Assert.Equal("", vm.Validation);

        vm.SetupLater = true;
        vm.Endpoint.ApiKey = "";
        Assert.True(vm.CanContinue);
        Assert.Equal("", vm.Validation);
        vm.SelectProvider(LlmProtocols.OpenAi);
        Assert.False(vm.SetupLater);
        Assert.Equal(LlmProtocols.OpenAi, vm.Endpoint.Protocol);
        Assert.Equal(LlmProtocols.DefaultModel(LlmProtocols.OpenAi), vm.Endpoint.Model);
        Assert.False(vm.ShowBaseUrl);
        Assert.True(vm.ShowGetKey);

        vm.SelectProvider(LlmProtocols.Ollama);
        Assert.Equal(LlmProtocols.OllamaModel, vm.Endpoint.Model);
        Assert.False(vm.ShowKey);
        Assert.True(vm.ShowBaseUrl);
        Assert.Null(vm.KeyPageUrl);
        Assert.True(vm.ModelUsable);

        vm.SelectProvider(LlmProtocols.LmStudio);
        Assert.Equal(LlmProtocols.LmStudioModel, vm.Endpoint.Model);
        Assert.True(vm.ModelUsable);

        vm.SelectProvider(LlmProtocols.Custom);
        Assert.Equal("", vm.Endpoint.Model);
        Assert.Equal("", vm.Endpoint.BaseUrl);
        Assert.Equal(s.Loc["setup.model.need_model"], vm.Validation);
        vm.Endpoint.Model = "local";
        Assert.Equal(s.Loc["setup.model.need_url"], vm.Validation);
        vm.Endpoint.BaseUrl = "http://127.0.0.1:9/v1";
        Assert.True(vm.ModelUsable);
        Assert.False(vm.ShowGetKey);

        vm.SetupLater = true;
        vm.ContinueCommand.Execute(null);
        Assert.Equal(SetupStep.Skills, vm.Step);
        Assert.Equal(s.Loc["setup.continue"], vm.ContinueLabel);
        vm.ContinueCommand.Execute(null);
        Assert.Equal(SetupStep.Done, vm.Step);
        Assert.Equal(s.Loc["setup.finish"], vm.ContinueLabel);
        vm.BackCommand.Execute(null);
        Assert.Equal(SetupStep.Skills, vm.Step);
        Assert.Equal(-1, vm.StepDelta);
    }

    [AvaloniaFact]
    public void Skip_and_finish_persist_the_model_and_a_new_key_without_writing_it_to_settings()
    {
        var s = TestServices.EnsureInitialized();
        using (var vm = new SetupWizardViewModel(s))
        {
            vm.ContinueCommand.Execute(null);
            vm.SelectProvider(LlmProtocols.Ollama);
            vm.Endpoint.Model = "llama-custom";
            vm.SkipCommand.Execute(null);
        }

        Assert.True(s.Store.Settings.SetupCompleted);
        Assert.Equal(LlmProtocols.Ollama, s.Store.Settings.Llm.Main.Protocol);
        Assert.Equal("llama-custom", s.Store.Settings.Llm.Main.Model);
        Assert.False(TestServices.Ports.Contains(LlmSecrets.MainSlot));
        var json = File.ReadAllText(s.Store.SettingsPath);
        Assert.Contains("\"setup_completed\": true", json);
        Assert.DoesNotContain("sk-", json);

        using (var again = new SetupWizardViewModel(s, SetupStep.Model))
        {
            Assert.Equal(LlmProtocols.Ollama, again.Endpoint.Protocol);
            Assert.Equal("llama-custom", again.Endpoint.Model);
            again.SelectProvider(LlmProtocols.Google);
            again.Endpoint.ApiKey = "  sk-typed  ";
            again.SkipCommand.Execute(null);
        }

        Assert.Equal("sk-typed", TestServices.Ports.Keys[LlmSecrets.MainSlot]);
        Assert.DoesNotContain("sk-typed", File.ReadAllText(s.Store.SettingsPath));
        Assert.True(s.Store.Settings.SetupCompleted);

        TestServices.Ports.ResetSecretReads();
        using (var third = new SetupWizardViewModel(s, SetupStep.Model))
        {
            Assert.True(third.Endpoint.HasSavedKey);
            Assert.Equal("", third.Endpoint.ApiKey);
            Assert.True(third.CanContinue);
            Assert.Equal(0, TestServices.Ports.SecretReads);
            third.ContinueCommand.Execute(null);
            third.ContinueCommand.Execute(null);
            third.ContinueCommand.Execute(null);
        }

        Assert.Equal("sk-typed", TestServices.Ports.Keys[LlmSecrets.MainSlot]);
        Assert.True(s.Store.Settings.SetupCompleted);
    }

    [AvaloniaFact]
    public void Escape_keeps_the_window_only_while_a_key_is_typed()
    {
        var s = TestServices.EnsureInitialized();
        using var vm = new SetupWizardViewModel(s, SetupStep.Model);
        vm.Endpoint.ApiKey = "draft";
        Assert.True(vm.HasUnsavedTypedKey);
        Assert.False(vm.TryDismissFromEscape());
        Assert.False(s.Store.Settings.SetupCompleted);

        vm.Endpoint.ApiKey = "   ";
        Assert.False(vm.HasUnsavedTypedKey);
        var closed = false;
        vm.RequestClose += (_, _) => closed = true;
        Assert.True(vm.TryDismissFromEscape());
        Assert.True(closed);
        Assert.True(s.Store.Settings.SetupCompleted);
    }

    [AvaloniaFact]
    public void A_saved_key_lets_the_model_step_continue_and_is_never_read()
    {
        var s = TestServices.EnsureInitialized();
        TestServices.Ports.Set(LlmSecrets.MainSlot, "sk-saved-value");
        TestServices.Ports.ResetSecretReads();
        using var vm = new SetupWizardViewModel(s, SetupStep.Model);
        Assert.True(vm.Endpoint.HasSavedKey);
        Assert.Equal("", vm.Endpoint.ApiKey);
        Assert.True(vm.CanContinue);
        Assert.Equal(0, TestServices.Ports.SecretReads);
        Assert.Contains(LlmProtocols.DefaultModel(LlmProtocols.Google), vm.ModelSummary);
    }

    [AvaloniaFact]
    public void Skill_rows_write_the_same_store_and_show_conflicts()
    {
        var s = TestServices.EnsureInitialized();
        using var vm = new SetupWizardViewModel(s, SetupStep.Skills);
        Assert.Equal(s.Store.Skills.Skills.Count + 1, vm.SkillRows.Count);
        var menu = vm.SkillRows[^1];
        Assert.True(menu.IsMenu);
        Assert.False(menu.ShowToggle);
        Assert.Equal(HotkeyDefaults.Menu, menu.Shortcut);

        var fix = vm.SkillRows.First(row => row.SkillId == SkillIds.Fix);
        var translate = vm.SkillRows.First(row => row.SkillId == SkillIds.Translate);
        var rewrite = vm.SkillRows.First(row => row.SkillId == SkillIds.Rewrite);
        Assert.True(fix.Enabled);
        Assert.False(rewrite.Enabled);
        Assert.Contains(s.Loc["setup.skill.fix.desc"], fix.Description);
        Assert.Contains(s.Loc["setup.skills.menu_desc"], menu.Description);

        rewrite.Enabled = true;
        Assert.True(s.Store.Skills.Get(SkillIds.Rewrite)!.Enabled);

        fix.Shortcut = HotkeyDefaults.Translate;
        Assert.Equal(HotkeyDefaults.Translate, s.Store.Skills.Get(SkillIds.Fix)!.Hotkey);
        Assert.Contains("Fix", fix.Conflict);
        Assert.Contains("Translate", translate.Conflict);

        fix.ResetShortcutCommand.Execute(null);
        Assert.Equal(HotkeyDefaults.Fix, fix.Shortcut);
        Assert.Equal(HotkeyDefaults.Fix, s.Store.Skills.Get(SkillIds.Fix)!.Hotkey);
        Assert.Equal("", fix.Conflict);

        menu.Shortcut = "Ctrl+Alt+9";
        Assert.Equal("Ctrl+Alt+9", s.Store.Skills.SkillMenuHotkey);
        menu.ResetShortcutCommand.Execute(null);
        Assert.Equal(HotkeyDefaults.Menu, s.Store.Skills.SkillMenuHotkey);

        vm.ContinueCommand.Execute(null);
        Assert.Contains("Fix", vm.SkillsSummary);
        Assert.Contains("Rewrite", vm.SkillsSummary);
        Assert.Equal(HotkeyParser.ParseOrDisabled(HotkeyDefaults.Menu).Display, vm.DonutSummary);
    }

    [AvaloniaFact]
    public void The_language_and_autostart_switches_write_the_live_settings()
    {
        var s = TestServices.EnsureInitialized();
        TestServices.Ports.Enable("C:\\TinyFix\\TinyFix.exe");
        using var vm = new SetupWizardViewModel(s, SetupStep.Done);
        Assert.True(vm.Autostart);
        var english = vm.Headline;

        vm.SelectedLanguage = vm.Languages.First(option => option.Code == "fa");
        Assert.Equal("fa", s.Store.Settings.Language);
        Assert.Equal("fa", s.Loc.Language);
        Assert.NotEqual(english, vm.Headline);
        Assert.Equal(s.Loc["setup.done.title"], vm.Headline);
        Assert.Equal(s.Loc["setup.step.done"], vm.Indicators[^1].Label);

        vm.Autostart = false;
        Assert.False(TestServices.Ports.IsEnabled);
        vm.Autostart = true;
        Assert.True(TestServices.Ports.IsEnabled);
        TestServices.EnsureInitialized("en");
    }

    [AvaloniaFact]
    public async Task The_connection_test_reports_the_time_or_a_plain_reason_and_can_be_cancelled()
    {
        var s = TestServices.EnsureInitialized();
        ScriptedHttp.Status = HttpStatusCode.OK;
        ScriptedHttp.Body = """{"candidates":[{"content":{"parts":[{"text":"OK"}]}}]}""";
        using (var vm = new SetupWizardViewModel(s, SetupStep.Model))
        {
            vm.Endpoint.ApiKey = "sk-fake-wizard";
            await vm.TestCommand.ExecuteAsync(null);
            Assert.False(vm.Testing);
            Assert.True(vm.TestSucceeded);
            Assert.Contains("ms", vm.TestHeadline);
            Assert.Matches(new Regex(@"\d+"), vm.TestHeadline);
            Assert.StartsWith(s.Loc["setup.test_ok"].Split("{0}")[0], vm.TestHeadline);
            Assert.DoesNotContain("sk-fake-wizard", vm.TestHeadline + vm.TestDetail);
        }

        ScriptedHttp.Status = HttpStatusCode.Unauthorized;
        ScriptedHttp.Body = "rejected";
        using (var vm = new SetupWizardViewModel(s, SetupStep.Model))
        {
            vm.Endpoint.ApiKey = "sk-fake-wizard";
            await vm.TestCommand.ExecuteAsync(null);
            Assert.Equal(s.Loc["setup.test_wrong_key"], vm.TestHeadline);
            Assert.Contains("401", vm.TestDetail);
            Assert.DoesNotContain("sk-fake-wizard", vm.TestDetail);
        }

        ScriptedHttp.Reset();
        using (var vm = new SetupWizardViewModel(s, SetupStep.Model))
        {
            vm.Endpoint.ApiKey = "sk-fake-wizard";
            await vm.TestCommand.ExecuteAsync(null);
            Assert.Equal(s.Loc["setup.test_network"], vm.TestHeadline);
            Assert.Contains("Network error", vm.TestDetail);
            Assert.DoesNotContain("sk-fake-wizard", vm.TestDetail);
        }

        ScriptedHttp.Hang = true;
        using (var vm = new SetupWizardViewModel(s, SetupStep.Model))
        {
            vm.Endpoint.ApiKey = "sk-fake-wizard";
            var running = vm.TestCommand.ExecuteAsync(null);
            await Task.Delay(50);
            Assert.True(vm.Testing);
            vm.CancelTestCommand.Execute(null);
            await running;
            Assert.False(vm.Testing);
            Assert.Equal("", vm.TestHeadline);
            Assert.DoesNotContain("sk-fake-wizard", vm.TestDetail);
        }
    }

    public static IEnumerable<object[]> StepShots()
    {
        foreach (var step in SetupWizardViewModel.Order)
            foreach (var language in new[] { "en", "fa" })
                foreach (var scale in new[] { 1.0, 1.25 })
                    yield return [step, language, scale];
    }

    [AvaloniaTheory]
    [MemberData(nameof(StepShots))]
    public void Setup_steps_render_without_clipped_text(SetupStep step, string language, double scale)
    {
        var s = TestServices.EnsureInitialized(language);
        Motion.Set(false);
        if (scale > 1)
            UiZoom.Set(scale, remember: false);
        var vm = new SetupWizardViewModel(s, step);
        var window = new SetupWindow(vm) { Width = 760 * scale, Height = 600 * scale };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(language == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
            Assert.Equal(s.Loc["setup.window_title"], window.Title);
            AssertNoClippedText(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), box => (box.Text ?? "").Contains("sk-", StringComparison.Ordinal));
            var suffix = scale > 1 ? "-125" : "";
            RenderTests.Save(window, $"setup-{step.ToString().ToLowerInvariant()}-{language}{suffix}.png");
        }
        finally
        {
            window.Close();
            UiZoom.Set(1, remember: false);
            Motion.Set(true);
            if (language != "en")
                TestServices.EnsureInitialized("en");
        }
    }

    [AvaloniaFact]
    public void The_keyboard_moves_through_the_window_and_escape_respects_a_typed_key()
    {
        var s = TestServices.EnsureInitialized();
        Motion.Set(false);
        var vm = new SetupWizardViewModel(s);
        var window = new SetupWindow(vm);
        try
        {
            window.Show();
            window.Focus();
            Dispatcher.UIThread.RunJobs();

            var names = window.GetVisualDescendants().OfType<Control>()
                .Select(AutomationProperties.GetName)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
            Assert.Contains(s.Loc["setup.back"], names);
            Assert.Contains(s.Loc["setup.skip"], names);
            Assert.Contains(s.Loc["setup.continue"], names);

            var before = window.FocusManager?.GetFocusedElement();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var after = window.FocusManager?.GetFocusedElement();
            Assert.NotNull(after);
            Assert.NotSame(before, after);
            Assert.Contains(window.GetVisualDescendants(), visual => ReferenceEquals(visual, after));

            var title = window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == s.Loc["setup.welcome.title"]);
            title.Focusable = true;
            title.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SetupStep.Model, vm.Step);

            vm.Endpoint.ApiKey = "draft-key";
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.False(s.Store.Settings.SetupCompleted);

            vm.Endpoint.ApiKey = "";
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            Assert.True(s.Store.Settings.SetupCompleted);
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
            Motion.Set(true);
        }
    }

    [AvaloniaFact]
    public void Closing_or_opening_again_keeps_one_window_and_marks_setup_done()
    {
        var s = TestServices.EnsureInitialized();
        var window = new SetupWindow(new SetupWizardViewModel(s));
        window.Show();
        window.Close();
        Assert.True(s.Store.Settings.SetupCompleted);

        s.Store.Settings.SetupCompleted = false;
        s.Windows.ShowSetup();
        var first = s.Windows.Setup;
        Assert.NotNull(first);
        s.Windows.ShowSetup();
        Assert.Same(first, s.Windows.Setup);
        first.Close();
        Assert.Null(s.Windows.Setup);
        Assert.True(s.Store.Settings.SetupCompleted);
    }

    private static void AssertNoClippedText(Window window)
    {
        foreach (var block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(block.Text) || block.Bounds.Width <= 1 || block.Bounds.Height <= 1)
                continue;
            var layout = block.TextLayout;
            Assert.True(layout.Width <= block.Bounds.Width + 1, block.Text);
            Assert.True(layout.Height <= block.Bounds.Height + 1, block.Text);
        }
    }
}
