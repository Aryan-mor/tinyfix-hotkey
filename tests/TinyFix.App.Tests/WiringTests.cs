// Tests of the glue: shortcut registration, the shortcut recorder, key mapping, the single-instance lock and the
// settings view models (with fake OS ports).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SharpHook.Data;
using TinyFix.App.Controls;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;
using TinyFix.Core.Hotkeys;
using TinyFix.Core.Platform;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.Tests;

public class WiringTests
{
    [AvaloniaFact]
    public void Enabled_skills_and_the_menu_are_registered_as_shortcuts()
    {
        var s = TestServices.EnsureInitialized("en");
        AppHost.ApplyHotkeys();
        var ids = TestServices.Hotkeys.Bindings.ToDictionary(b => b.Id, b => b.Key.Display);
        Assert.Equal("Ctrl+Alt+Space", ids[SkillIds.Fix]);
        Assert.Equal("Ctrl+F3", ids[SkillIds.FixAll]);
        Assert.Equal("Ctrl+Alt+T", ids[SkillIds.Translate]);
        Assert.Equal("Ctrl+Alt+M", ids[HotkeyIds.Menu]);
        // Skills without a shortcut and disabled skills are not registered.
        Assert.DoesNotContain(SkillIds.ImprovePrompt, ids.Keys);
        Assert.DoesNotContain(SkillIds.Rewrite, ids.Keys);

        s.Store.Skills.Get(SkillIds.Translate)!.Enabled = false;
        AppHost.ApplyHotkeys();
        Assert.DoesNotContain(TestServices.Hotkeys.Bindings, b => b.Id == SkillIds.Translate);
        s.Store.Skills.Get(SkillIds.Translate)!.Enabled = true;
    }

    [AvaloniaFact]
    public void Pausing_suspends_every_shortcut()
    {
        var s = TestServices.EnsureInitialized("en");
        s.Tray.TogglePause();
        Assert.True(s.Store.Settings.Paused);
        Assert.True(TestServices.Hotkeys.Suspended);
        s.Tray.TogglePause();
        Assert.False(TestServices.Hotkeys.Suspended);
    }

    [AvaloniaFact]
    public void The_recorder_pauses_the_hook_while_open_and_records_a_combination()
    {
        TestServices.EnsureInitialized("en");
        var recorder = new ShortcutRecorder { Value = "none" };
        var window = new Window { Content = recorder };
        window.Show();
        recorder.Begin();
        Assert.True(recorder.IsRecording);
        Assert.True(TestServices.Hotkeys.Suspended);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.Equal("Ctrl+Alt+G", recorder.Value);
        Assert.False(recorder.IsRecording);
        Assert.False(TestServices.Hotkeys.Suspended);
        window.Close();
    }

    [AvaloniaFact]
    public void The_recorder_rejects_plain_letters_clears_on_backspace_and_cancels_on_escape()
    {
        TestServices.EnsureInitialized("en");
        var recorder = new ShortcutRecorder { Value = "Ctrl+Alt+T" };
        var window = new Window { Content = recorder };
        window.Show();
        var rejected = 0;
        recorder.Rejected += (_, _) => rejected++;

        recorder.Begin();
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.Equal(1, rejected);
        Assert.True(recorder.IsRecording);
        window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.None);
        Assert.Equal("F5", recorder.Value);

        recorder.Begin();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal("F5", recorder.Value);
        Assert.False(recorder.IsRecording);

        recorder.Begin();
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Assert.Equal("none", recorder.Value);
        window.Close();
    }

    [Fact]
    public void Avalonia_keys_map_to_the_virtual_key_codes_of_the_hotkey_model()
    {
        Assert.Equal(0x41, AvaloniaKeys.ToVk(Key.A));
        Assert.Equal(0x5A, AvaloniaKeys.ToVk(Key.Z));
        Assert.Equal(0x30, AvaloniaKeys.ToVk(Key.D0));
        Assert.Equal(0x72, AvaloniaKeys.ToVk(Key.F3));
        Assert.Equal(0x20, AvaloniaKeys.ToVk(Key.Space));
        Assert.Equal(0, AvaloniaKeys.ToVk(Key.LeftCtrl));
        Assert.True(AvaloniaKeys.IsModifier(Key.RightAlt));
        Assert.False(AvaloniaKeys.IsModifier(Key.A));
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, AvaloniaKeys.ToBits(KeyModifiers.Control | KeyModifiers.Shift));
        // Every key the recorder can produce parses back to the same shortcut.
        foreach (var key in new[] { Key.A, Key.F12, Key.Space, Key.Tab, Key.OemComma })
        {
            var vk = AvaloniaKeys.ToVk(key);
            Assert.Equal(vk, HotkeyParser.Parse("Ctrl+" + HotkeyParser.DisplayKey(vk)).Vk);
        }
    }

    [Fact]
    public void Every_hotkey_key_choice_has_a_sharphook_key()
    {
        foreach (var name in HotkeyParser.KeyChoices)
        {
            var vk = HotkeyParser.VkFromName(name);
            Assert.True(KeyMap.TryGetKeyCode(vk, out var code), name);
            Assert.NotEqual(KeyCode.VcUndefined, code);
            Assert.Equal(vk, KeyMap.ToVk(code));
        }
    }

    [Fact]
    public async Task The_single_instance_lock_is_exclusive_and_forwards_arguments()
    {
        var name = "TinyFix-test-" + Guid.NewGuid().ToString("N")[..10];
        using var first = SingleInstance.Acquire(name);
        Assert.True(first.IsFirst);
        var received = new TaskCompletionSource<string[]>();
        first.ArgumentsReceived += args => received.TrySetResult(args);
        first.StartListening();

        using var second = SingleInstance.Acquire(name);
        Assert.False(second.IsFirst);
        Assert.True(second.ForwardToFirst(["--open", "x"], TimeSpan.FromSeconds(10)));
        var args = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["--open", "x"], args);
    }

    [Fact]
    public void A_data_dir_override_gets_its_own_instance_name()
    {
        var plain = SingleInstance.NameFor("anna", null);
        var overridden = SingleInstance.NameFor("anna", @"C:\Temp\TinyFix-Test");
        Assert.StartsWith("TinyFix-", plain, StringComparison.Ordinal);
        Assert.NotEqual(plain, overridden);
        Assert.Equal(plain, SingleInstance.NameFor("anna", null));
        Assert.NotEqual(plain, SingleInstance.NameFor("bob", null));
    }

    [AvaloniaFact]
    public void A_layout_skill_shows_its_layouts_instead_of_a_prompt_and_follows_its_name()
    {
        var s = TestServices.EnsureInitialized("en");
        using var page = new SkillsPageViewModel(s);

        page.AddLayoutCommand.Execute(null);
        var skill = page.Selected!.Skill;
        Assert.True(page.IsLayoutSkill);
        Assert.False(page.IsAiSkill);
        Assert.False(skill.NeedsAi);
        Assert.Equal(("any", "fa"), (skill.LayoutFrom, skill.LayoutTo));
        Assert.Equal("Any language to FA", skill.Name);
        Assert.Equal(["any", "en", "fa"], page.FromChoices.Select(c => c.Id));
        Assert.Equal(["en", "fa"], page.ToChoices.Select(c => c.Id));

        // A name nobody typed follows the choice.
        page.LayoutFromChoice = page.FromChoices.First(c => c.Id == "en");
        page.LayoutToChoice = page.ToChoices.First(c => c.Id == "en");
        Assert.Equal(("en", "en"), (skill.LayoutFrom, skill.LayoutTo));
        Assert.Equal("EN to EN", skill.Name);
        // A name the user typed stays.
        page.Name = "My fix";
        page.LayoutToChoice = page.ToChoices.First(c => c.Id == "fa");
        Assert.Equal("My fix", skill.Name);

        // The built-in swap has two languages and no "any".
        page.Selected = page.Rows.First(r => r.Skill.Id == SkillIds.LayoutSwap);
        Assert.True(page.IsSwapSkill);
        Assert.Equal(["en", "fa"], page.FromChoices.Select(c => c.Id));
        Assert.Equal("First language", page.FromLabel);

        // An AI skill is unchanged.
        page.Selected = page.Rows.First(r => r.Skill.Id == SkillIds.Fix);
        Assert.True(page.IsAiSkill);
        Assert.False(page.IsLayoutSkill);

        // The layout skill is saved with its kind.
        page.Flush();
        var reloaded = SkillStoreFile.Load(s.Store.SkillsPath).Store.Get(skill.Id)!;
        Assert.Equal((SkillKinds.Layout, "en", "fa"), (reloaded.Kind, reloaded.LayoutFrom, reloaded.LayoutTo));
    }

    [AvaloniaFact]
    public void The_skills_page_adds_edits_duplicates_moves_and_deletes_with_saving()
    {
        var s = TestServices.EnsureInitialized("en");
        using var page = new SkillsPageViewModel(s);
        var before = page.Rows.Count;

        page.AddCommand.Execute(null);
        Assert.Equal(before + 1, page.Rows.Count);
        var added = page.Selected!;
        page.Name = "Make formal";
        page.Shortcut = "Ctrl+Alt+Space";
        Assert.Equal("Make formal", added.Skill.Name);
        // The same shortcut as Fix: a conflict is reported.
        Assert.Contains("Ctrl+Alt+Space", page.Conflicts);
        page.Shortcut = "Ctrl+Alt+J";
        Assert.Equal("", page.Conflicts);

        page.DuplicateCommand.Execute(null);
        Assert.Equal(before + 2, page.Rows.Count);
        Assert.Equal("Make formal copy", page.Selected!.Skill.Name);
        Assert.True(page.Selected.Skill.HotkeyValue().IsDisabled);

        page.MoveUpCommand.Execute(null);
        Assert.True(page.Rows.IndexOf(page.Selected) < page.Rows.Count - 1);

        page.Flush();
        var reloaded = SkillStoreFile.Load(s.Store.SkillsPath).Store;
        Assert.Contains(reloaded.Skills, k => k.Name == "Make formal" && k.Hotkey == "Ctrl+Alt+J");

        page.DeleteCommand.Execute(null);
        page.Selected = page.Rows.First(r => r.Skill.Name == "Make formal");
        page.DeleteCommand.Execute(null);
        Assert.Equal(before, page.Rows.Count);
        // Core skills cannot be deleted.
        page.Selected = page.Rows.First(r => r.Skill.Id == SkillIds.Fix);
        Assert.False(page.CanDelete);
        page.DeleteCommand.Execute(null);
        Assert.Equal(before, page.Rows.Count);
    }

    [AvaloniaFact]
    public void Editing_a_builtin_name_keeps_the_english_default_translatable()
    {
        var s = TestServices.EnsureInitialized("de");
        using var page = new SkillsPageViewModel(s);
        page.Selected = page.Rows.First(r => r.Skill.Id == SkillIds.Translate);
        // Shown in German, but the stored name is still the English default.
        Assert.Equal("Translate", page.Selected.Skill.Name);
        Assert.Equal(s.Loc["skill.translate"], page.Name);
        TestServices.EnsureInitialized("en");
    }

    [AvaloniaFact]
    public void The_online_page_saves_settings_and_keeps_the_key_in_the_secret_store()
    {
        var s = TestServices.EnsureInitialized("en");
        using var page = new OnlinePageViewModel(s);
        page.Main.ProtocolIndex = 1;
        page.Main.ApiKey = "sk-or-123";
        page.Main.Timeout = "7.5";
        page.SaveCommand.Execute(null);
        Assert.Equal("openrouter", s.Store.Settings.Llm.Main.Protocol);
        Assert.Equal(7.5, s.Store.Settings.Llm.Main.Timeout);
        Assert.Equal("sk-or-123", TestServices.Ports.Get("openrouter"));
        Assert.Equal("sk-or-123", TestServices.Ports.Get("main"));
        Assert.Equal("", page.Main.ApiKey);
        Assert.Equal(KeyFieldState.Saved, page.Main.State);
        Assert.True(page.Main.HasSavedKey);
        // The key is in no settings file.
        Assert.DoesNotContain("sk-or-123", File.ReadAllText(s.Store.SettingsPath));
        Assert.DoesNotContain("sk-or-123", File.ReadAllText(s.Store.SkillsPath));
        TestServices.Ports.Keys.Clear();
    }

    [AvaloniaFact]
    public async Task The_online_test_button_reports_a_failure_without_the_key()
    {
        var s = TestServices.EnsureInitialized("en");
        using var page = new OnlinePageViewModel(s);
        page.Main.ApiKey = "sk-secret-999";
        await page.Main.TestCommand.ExecuteAsync(null);
        Assert.Contains("Network error", page.Main.Status);
        Assert.DoesNotContain("sk-secret-999", page.Main.Status);
    }

    [AvaloniaFact]
    public void The_general_page_switches_language_and_theme_and_autostart_only_on_request()
    {
        var s = TestServices.EnsureInitialized("en");
        using var page = new GeneralPageViewModel(s);
        Assert.False(TestServices.Ports.AutostartOn);
        page.SelectedLanguage = page.Languages.First(l => l.Code == "ja");
        Assert.Equal("ja", s.Loc.Language);
        Assert.Equal("ja", s.Store.Settings.Language);
        page.ThemeIndex = 2;
        Assert.Equal("dark", s.Store.Settings.Theme);
        page.ThemeIndex = 0;
        page.Autostart = true;
        Assert.True(TestServices.Ports.AutostartOn);
        page.Autostart = false;
        Assert.False(TestServices.Ports.AutostartOn);
        page.SelectedLanguage = page.Languages.First(l => l.Code == "en");
    }

    [AvaloniaFact]
    public void The_donut_page_saves_the_shortcut_the_look_and_where_it_opens()
    {
        var s = TestServices.EnsureInitialized("en");
        using var page = new DonutPageViewModel(s);
        try
        {
            Assert.Equal(DonutSettings.DefaultRadius, page.OuterRadius);
            Assert.Equal(Math.Round(DonutSettings.DefaultOpacity * 100), page.OpacityPercent);
            Assert.True(page.OpenAtCaret);
            Assert.False(page.OpenAtCursor);
            Assert.True(page.ShowShortcutHints);
            Assert.Equal(HotkeyDefaults.Menu, page.MenuShortcut);
            Assert.Equal("", page.Conflicts);

            var before = File.ReadAllText(s.Store.SettingsPath);
            page.OuterRadius = 180;
            page.OpacityPercent = 40;
            Assert.Equal(180, s.Store.Settings.Donut.OuterRadius);
            Assert.Equal(0.4, s.Store.Settings.Donut.Opacity);
            Assert.Equal(before, File.ReadAllText(s.Store.SettingsPath));
            Assert.True(DonutLookSave.IsPending);
            DonutLookSave.Flush();
            Assert.False(DonutLookSave.IsPending);

            page.OpenAtCursor = true;
            Assert.False(page.OpenAtCaret);
            Assert.Equal(DonutOpenAt.Cursor, s.Store.Settings.Donut.OpenAt);
            page.ShowShortcutHints = false;
            page.MenuShortcut = HotkeyDefaults.Fix;
            Assert.Contains("Ctrl+Alt+Space", page.Conflicts);
            Assert.Equal(HotkeyDefaults.Fix, s.Store.Skills.SkillMenuHotkey);

            var again = new UserDataStore(s.DataDirectory) { LegacySource = () => null };
            again.Load();
            Assert.Equal(180, again.Settings.Donut.OuterRadius);
            Assert.Equal(0.4, again.Settings.Donut.Opacity);
            Assert.Equal(DonutOpenAt.Cursor, again.Settings.Donut.OpenAt);
            Assert.False(again.Settings.Donut.ShowShortcutHints);

            var window = new SettingsWindow(new SettingsViewModel(s)) { Width = 800, Height = 600 };
            window.Show();
            window.Position = new PixelPoint(40, 60);
            var center = DonutPageViewModel.CenterOf(window);
            var scale = OperatingSystem.IsMacOS() ? 1 : window.RenderScaling;
            Assert.Equal(40 + (int)Math.Round(window.Bounds.Width * scale / 2.0), center.X);
            Assert.Equal(60 + (int)Math.Round(window.Bounds.Height * scale / 2.0), center.Y);
            window.Close();
        }
        finally
        {
            page.Dispose();
            s.Store.Settings.Donut = new DonutSettings();
            s.Store.Skills.SkillMenuHotkey = HotkeyDefaults.Menu;
            s.Store.SaveSettings();
            s.Store.SaveSkills();
            AppHost.ApplyHotkeys();
        }
    }

    [AvaloniaFact]
    public void A_wayland_session_gets_a_notice_on_the_general_page()
    {
        var s = TestServices.EnsureInitialized("en");
        using var normal = new GeneralPageViewModel(s);
        Assert.Equal("", normal.PlatformNotice);
        var wayland = new PlatformServices(TestServices.Ports, TestServices.Ports, TestServices.Ports, TestServices.Ports, TestServices.Ports,
            new PlatformCapabilities(false, false, false, true));
        Assert.True(wayland.Capabilities.ClipboardFixAdvised);
        Assert.False(s.Platform.Capabilities.ClipboardFixAdvised);
    }

    [AvaloniaFact]
    public void The_about_page_knows_its_version_and_repository()
    {
        var s = TestServices.EnsureInitialized("en");
        using var page = new AboutPageViewModel(s);
        Assert.Matches(@"^\d+\.\d+\.\d+", AboutPageViewModel.CurrentVersion);
        Assert.StartsWith("https://github.com/Aryan-mor/", page.RepositoryUrl, StringComparison.Ordinal);
        Assert.Contains("Version", page.VersionText);
        Assert.Contains("MIT", page.Notices);
        Assert.False(UrlOpener.Open("http://insecure.example"));
        Assert.False(UrlOpener.Open("file:///c:/windows"));
    }
}
