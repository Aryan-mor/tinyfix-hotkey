// The setup wizard's state machine, its headless render (English and Persian, 100% and 125%), and the keyboard path.
// Keys in these tests are fake. The HTTP handler never leaves the process, and the data folder is the temp one.

using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;
using TinyFix.App.Views.Controls;
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
        var menu = vm.SkillRows[0];
        Assert.True(menu.IsMenu);
        Assert.False(menu.ShowToggle);
        Assert.Equal(HotkeyDefaults.Menu, menu.Shortcut);

        var fix = vm.SkillRows.First(row => row.SkillId == SkillIds.Fix);
        var translate = vm.SkillRows.First(row => row.SkillId == SkillIds.Translate);
        var rewrite = vm.SkillRows.First(row => row.SkillId == SkillIds.Rewrite);
        Assert.True(fix.Enabled);
        Assert.False(rewrite.Enabled);
        Assert.False(fix.ShortcutChanged);
        Assert.Contains(s.Loc["setup.skill.fix.desc"], fix.Description);
        Assert.Contains(s.Loc["setup.skills.menu_desc"], menu.Description);

        rewrite.Enabled = true;
        Assert.True(s.Store.Skills.Get(SkillIds.Rewrite)!.Enabled);

        fix.Shortcut = HotkeyDefaults.Translate;
        Assert.True(fix.ShortcutChanged);
        Assert.Equal(HotkeyDefaults.Translate, s.Store.Skills.Get(SkillIds.Fix)!.Hotkey);
        Assert.Contains("Fix", fix.Conflict);
        Assert.Contains("Translate", translate.Conflict);

        fix.ResetShortcutCommand.Execute(null);
        Assert.False(fix.ShortcutChanged);
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
                foreach (var theme in new[] { "light", "dark" })
                    foreach (var scale in new[] { 1.0, 1.25 })
                        yield return [step, language, theme, scale];
    }

    public static IEnumerable<object[]> ModelLayouts()
    {
        foreach (var protocol in new[] { LlmProtocols.Google, LlmProtocols.OpenRouter, LlmProtocols.OpenAi, LlmProtocols.Anthropic })
            foreach (var scale in new[] { 1.0, 1.25 })
                yield return [protocol, scale, false];
        yield return [LlmProtocols.Google, 1.0, true];
        yield return [LlmProtocols.Google, 1.25, true];
    }

    [AvaloniaTheory]
    [MemberData(nameof(StepShots))]
    public void Setup_steps_render_without_clipped_text(SetupStep step, string language, string theme, double scale)
    {
        var s = TestServices.EnsureInitialized(language);
        s.Store.Settings.Theme = theme;
        s.Theme.Apply(language);
        Motion.Set(false);
        UiZoom.Set(scale, remember: false);
        var vm = new SetupWizardViewModel(s, step);
        var window = new SetupWindow(vm) { Width = 776 * scale, Height = 639 * scale };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(language == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
            Assert.Equal(s.Loc["setup.window_title"], window.Title);
            AssertNoClippedText(window);
            AssertNoNestedScroll(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), box => (box.Text ?? "").Contains("sk-", StringComparison.Ordinal));
            var suffix = scale > 1 ? "-125" : "";
            RenderTests.Save(window, $"setup-{step.ToString().ToLowerInvariant()}-{language}-{theme}{suffix}.png");
        }
        finally
        {
            window.Close();
            UiZoom.Set(1, remember: false);
            Motion.Set(true);
            s.Store.Settings.Theme = "system";
            s.Theme.Apply("en");
            if (language != "en")
                TestServices.EnsureInitialized("en");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ModelLayouts))]
    public void The_model_step_keeps_the_key_and_the_test_button_in_view(string protocol, double scale, bool savedKey)
    {
        var s = TestServices.EnsureInitialized();
        if (savedKey)
            TestServices.Ports.Set(LlmSecrets.MainSlot, "sk-saved-value");
        Motion.Set(false);
        UiZoom.Set(scale, remember: false);
        var vm = new SetupWizardViewModel(s, SetupStep.Model);
        if (protocol != LlmProtocols.Google)
            vm.SelectProvider(protocol);
        var window = new SetupWindow(vm) { Width = 776, Height = 639 };
        try
        {
            window.Show();
            Settle(window);
            Assert.Equal(0, window.FindControl<ScrollViewer>("BodyScroll")!.Offset.Y, 1);
            AssertNoNestedScroll(window);
            var chip = window.GetVisualDescendants().OfType<RadioButton>().First(button => button.IsChecked == true);
            AssertInWindow(chip, window);
            AssertInWindow(window.FindControl<TextBox>("ModelBox")!, window);
            AssertInWindow(window.FindControl<Button>("TestButton")!, window);
            if (savedKey)
            {
                var mask = window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == EndpointEditorViewModel.SavedKeyMask);
                AssertInWindow(mask, window);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), block => (block.Text ?? "").Contains("sk-saved-value", StringComparison.Ordinal));
            }
            else
            {
                var box = window.GetVisualDescendants().OfType<TextBox>().First(item => item.FindAncestorOfType<ApiKeyField>() is not null && item.IsEffectivelyVisible);
                AssertInWindow(box, window);
            }
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), box => (box.Text ?? "").Contains("sk-", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            UiZoom.Set(1, remember: false);
            Motion.Set(true);
            TestServices.Ports.Keys.Clear();
        }
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    public void The_skills_step_shows_at_least_five_rows(double scale)
    {
        var s = TestServices.EnsureInitialized();
        Motion.Set(false);
        UiZoom.Set(scale, remember: false);
        var vm = new SetupWizardViewModel(s, SetupStep.Skills);
        var window = new SetupWindow(vm) { Width = 776, Height = 639 };
        try
        {
            window.Show();
            Settle(window);
            AssertNoNestedScroll(window);
            var rows = window.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("skill-row")).ToList();
            Assert.True(((SetupSkillRowViewModel)rows[0].DataContext!).IsMenu);
            var visible = rows.Count(row => FullyInside(row, window));
            Assert.True(visible >= 5, $"visible {visible} of {rows.Count} at {scale}");
        }
        finally
        {
            window.Close();
            UiZoom.Set(1, remember: false);
            Motion.Set(true);
        }
    }

    [AvaloniaFact]
    public async Task The_model_step_focuses_the_key_or_the_model_and_arrows_move_the_provider()
    {
        var s = TestServices.EnsureInitialized();
        Motion.Set(false);
        var vm = new SetupWizardViewModel(s);
        var window = new SetupWindow(vm) { Width = 776, Height = 639 };
        try
        {
            window.Show();
            Settle(window);
            vm.ContinueCommand.Execute(null);
            await Flush();
            var key = FocusedKeyBox(window);
            Assert.NotNull(key);

            window.Focus();
            var chips = window.GetVisualDescendants().OfType<RadioButton>().ToList();
            chips[0].Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await Flush();
            Assert.Equal(LlmProtocols.OpenRouter, vm.Endpoint.Protocol);
            Assert.True(chips[1].IsChecked);

            vm.SelectProvider(LlmProtocols.Ollama);
            window.Close();
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
            Motion.Set(true);
        }

        using var local = new SetupWizardViewModel(s, SetupStep.Model);
        local.SelectProvider(LlmProtocols.Ollama);
        var again = new SetupWindow(local) { Width = 776, Height = 639 };
        try
        {
            again.Show();
            Settle(again);
            await Flush();
            Assert.Same(again.FindControl<TextBox>("ModelBox"), again.FocusManager?.GetFocusedElement());
        }
        finally
        {
            again.Close();
        }

        TestServices.Ports.Set(LlmSecrets.MainSlot, "sk-saved-value");
        using var saved = new SetupWizardViewModel(s, SetupStep.Model);
        saved.SelectProvider(LlmProtocols.Google);
        var savedWindow = new SetupWindow(saved) { Width = 776, Height = 639 };
        try
        {
            savedWindow.Show();
            Settle(savedWindow);
            await Flush();
            Assert.Same(savedWindow.FindControl<TextBox>("ModelBox"), savedWindow.FocusManager?.GetFocusedElement());
            Assert.DoesNotContain("sk-saved-value", savedWindow.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text ?? ""));
        }
        finally
        {
            savedWindow.Close();
            TestServices.Ports.Keys.Clear();
        }
    }

    [AvaloniaFact]
    public async Task Enter_in_the_key_box_runs_the_test_and_then_continue()
    {
        var s = TestServices.EnsureInitialized();
        Motion.Set(false);
        ScriptedHttp.Status = HttpStatusCode.OK;
        ScriptedHttp.Body = """{"candidates":[{"content":{"parts":[{"text":"OK"}]}}]}""";
        var vm = new SetupWizardViewModel(s, SetupStep.Model);
        var window = new SetupWindow(vm) { Width = 776, Height = 639 };
        try
        {
            window.Show();
            Settle(window);
            await Flush();
            var key = FocusedKeyBox(window);
            Assert.NotNull(key);
            vm.Endpoint.ApiKey = "sk-fake-wizard";
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await WaitUntil(() => vm.TestSucceeded);
            Assert.False(vm.Testing);
            Assert.Contains("ms", vm.TestHeadline);
            Assert.DoesNotContain("sk-fake-wizard", vm.TestHeadline + vm.TestDetail);
            var success = window.FindControl<StackPanel>("TestSuccess");
            Assert.NotNull(success);
            Assert.True(success.IsEffectivelyVisible);
            Assert.Contains(success.GetVisualDescendants().OfType<PathIcon>(), icon => icon.IsEffectivelyVisible);
            var continueButton = window.FindControl<Button>("ContinueButton")!;
            Assert.True(continueButton.IsDefault);
            Assert.Same(continueButton, window.FocusManager?.GetFocusedElement());

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Flush();
            Assert.Equal(SetupStep.Skills, vm.Step);
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
            ScriptedHttp.Reset();
            Motion.Set(true);
        }
    }

    [AvaloniaFact]
    public async Task A_failed_test_keeps_the_plain_reason_and_the_detail_in_an_expander()
    {
        var s = TestServices.EnsureInitialized();
        Motion.Set(false);
        ScriptedHttp.Status = HttpStatusCode.Unauthorized;
        ScriptedHttp.Body = "rejected";
        var vm = new SetupWizardViewModel(s, SetupStep.Model);
        vm.Endpoint.ApiKey = "sk-fake-wizard";
        var window = new SetupWindow(vm) { Width = 776, Height = 639 };
        try
        {
            window.Show();
            Settle(window);
            await vm.TestCommand.ExecuteAsync(null);
            await Flush();
            Assert.Equal(s.Loc["setup.test_wrong_key"], vm.TestHeadline);
            var expander = window.GetVisualDescendants().OfType<Expander>().Single();
            Assert.False(expander.IsExpanded);
            Assert.Equal(s.Loc["setup.test_details"], expander.Header as string);
            var detail = Assert.IsType<TextBlock>(expander.Content);
            Assert.Contains("401", detail.Text);
            Assert.DoesNotContain("sk-fake-wizard", detail.Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == s.Loc["setup.test_wrong_key"]);
        }
        finally
        {
            window.Close();
            ScriptedHttp.Reset();
            Motion.Set(true);
        }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Continue_stays_accent_until_it_is_disabled(string theme)
    {
        var s = TestServices.EnsureInitialized();
        s.Store.Settings.Theme = theme;
        s.Theme.Apply("en");
        Motion.Set(false);
        var vm = new SetupWizardViewModel(s);
        var window = new SetupWindow(vm) { Width = 776, Height = 639 };
        try
        {
            window.Show();
            Settle(window);
            var button = window.FindControl<Button>("ContinueButton")!;
            var enabled = ButtonColors(button);
            Assert.True(button.Opacity > 0.99);
            Assert.True(Contrast(enabled.Fg, enabled.Bg) >= 4.5, $"enabled {Contrast(enabled.Fg, enabled.Bg):0.00}");
            Assert.Equal(127, enabled.Bg.R);
            Assert.Equal(119, enabled.Bg.G);
            Assert.Equal(221, enabled.Bg.B);

            vm.ContinueCommand.Execute(null);
            Settle(window);
            Assert.False(button.IsEffectivelyEnabled);
            var disabled = ButtonColors(button);
            Assert.True(Contrast(disabled.Fg, disabled.Bg) >= 4.5, $"disabled {Contrast(disabled.Fg, disabled.Bg):0.00}");
            Assert.True(Contrast(enabled.Bg, disabled.Bg) >= 1.4, $"fills {Contrast(enabled.Bg, disabled.Bg):0.00}");
        }
        finally
        {
            window.Close();
            Motion.Set(true);
            s.Store.Settings.Theme = "system";
            s.Theme.Apply("en");
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

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Flush() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();

    private static async Task WaitUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until)
        {
            await Flush();
            await Task.Delay(20);
        }
        Assert.True(done());
    }

    private static TextBox? FocusedKeyBox(Window window) =>
        window.FocusManager?.GetFocusedElement() as TextBox is { } box && box.FindAncestorOfType<ApiKeyField>() is not null ? box : null;

    private static void AssertNoNestedScroll(Window window)
    {
        foreach (var viewer in window.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (viewer.TemplatedParent is not null)
                continue;
            var nested = viewer.GetVisualAncestors().OfType<ScrollViewer>().Any(parent => parent.TemplatedParent is null);
            Assert.False(nested, viewer.Name);
        }
    }

    private static void AssertInWindow(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible, control.GetType().Name);
        Assert.True(FullyInside(control, window), $"{control.GetType().Name} {control.Bounds} in {window.ClientSize}");
    }

    private static bool FullyInside(Control control, Window window)
    {
        if (!control.IsEffectivelyVisible || control.Bounds.Width < 1 || control.Bounds.Height < 1)
            return false;
        var origin = control.TranslatePoint(default, window);
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window);
        if (origin is null || end is null)
            return false;
        return origin.Value.X >= -1 && origin.Value.Y >= -1
            && end.Value.X <= window.ClientSize.Width + 1
            && end.Value.Y <= window.ClientSize.Height + 1;
    }

    private static (Color Bg, Color Fg) ButtonColors(Button button)
    {
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First(item => item.Name == "PART_ContentPresenter");
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background);
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground);
        return (background.Color, foreground.Color);
    }

    private static double Contrast(Color a, Color b)
    {
        var lighter = Math.Max(Luminance(a), Luminance(b));
        var darker = Math.Min(Luminance(a), Luminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
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
