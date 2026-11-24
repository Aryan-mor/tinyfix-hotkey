// Layout conversion: the pure logic over small hand-made layouts (the Windows adapter is tested on the real OS).

using Microsoft.Extensions.Logging.Abstractions;
using TinyFix.Core.Hotkeys;
using TinyFix.Core.Layouts;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;
using TinyFix.Core.Selection;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.Core.Tests;

public sealed class LayoutTests
{
    // Three keys of the home row and a shifted one, enough to tell layouts apart. Scan codes: a=0x1E s=0x1F d=0x20.
    private static KeyboardLayout Layout(string language, string a, string s, string d, string shiftedA) =>
        new(language, language, new Dictionary<int, char>
        {
            [0x1E] = a[0],
            [0x1F] = s[0],
            [0x20] = d[0],
            [0x1E | KeySlots.Shift] = shiftedA[0],
        });

    private static readonly KeyboardLayout En = Layout("en", "a", "s", "d", "A");
    private static readonly KeyboardLayout Fa = Layout("fa", "ش", "س", "ی", "ؤ");
    private static readonly KeyboardLayout Ru = Layout("ru", "ф", "ы", "в", "Ф");
    private static readonly KeyboardLayout De = Layout("de", "a", "s", "d", "A");

    [Fact]
    public void Convert_goes_through_the_physical_key()
    {
        Assert.Equal("شسی", LayoutConverter.Convert("asd", En, Fa));
        Assert.Equal("asd", LayoutConverter.Convert("شسی", Fa, En));
        Assert.Equal("ؤ", LayoutConverter.Convert("A", En, Fa));
    }

    [Fact]
    public void Characters_the_layout_cannot_type_stay_as_they_are()
    {
        Assert.Equal("شس 1!ی", LayoutConverter.Convert("as 1!d", En, Fa));
    }

    [Fact]
    public void Any_finds_the_layout_the_text_was_typed_on()
    {
        var installed = new[] { En, Ru, Fa };
        Assert.Equal("фыв", LayoutConverter.ConvertTo("asd", "en", "ru", installed).Text);
        Assert.Equal("شسی", LayoutConverter.ConvertTo("asd", "any", "fa", installed).Text);
        Assert.Equal("asd", LayoutConverter.ConvertTo("фыв", "any", "en", installed).Text);
    }

    [Fact]
    public void Text_that_is_already_in_the_target_layout_is_left_alone()
    {
        var result = LayoutConverter.ConvertTo("شسی", "any", "fa", [En, Ru, Fa]);
        Assert.Equal(LayoutFailure.Unknown, result.Failure);
        Assert.Null(result.Text);
    }

    [Fact]
    public void A_tie_between_layouts_goes_to_the_one_listed_first()
    {
        Assert.Equal(En, LayoutConverter.DetectSource("asd", [En, De, Fa], Fa));
        Assert.Equal(De, LayoutConverter.DetectSource("asd", [De, En, Fa], Fa));
    }

    [Fact]
    public void A_missing_layout_is_reported()
    {
        Assert.Equal(LayoutFailure.Missing, LayoutConverter.ConvertTo("asd", "any", "ar", [En, Fa]).Failure);
        Assert.Equal(LayoutFailure.Missing, LayoutConverter.ConvertTo("asd", "ru", "fa", [En, Fa]).Failure);
    }

    [Fact]
    public void Swap_works_both_ways_and_picks_the_direction_from_the_text()
    {
        Assert.Equal("شسی", LayoutConverter.Swap("asd", "fa", "en", [En, Fa]).Text);
        Assert.Equal("asd", LayoutConverter.Swap("شسی", "fa", "en", [En, Fa]).Text);
    }

    [Fact]
    public void Swap_needs_both_layouts_and_a_clear_direction()
    {
        // Three layouts and a pair that is not among them: no clear pair.
        Assert.Equal(LayoutFailure.Missing, LayoutConverter.Swap("asd", "fa", "ru", [En, Fa, De]).Failure);
        Assert.Equal(LayoutFailure.Missing, LayoutConverter.Swap("asd", "en", "en", [En, Fa, De]).Failure);
        // Exactly two layouts: they are the pair, whatever the rule names.
        Assert.Equal("شسی", LayoutConverter.Swap("asd", "fa", "ru", [En, Fa]).Text);
        // Two layouts that type the same letters cannot tell which way the text should go.
        Assert.Equal(LayoutFailure.Unknown, LayoutConverter.Swap("asd", "en", "de", [En, De]).Failure);
        Assert.Equal(LayoutFailure.Unknown, LayoutConverter.Swap("123", "fa", "en", [En, Fa]).Failure);
    }

    [Fact]
    public void Layout_skills_need_no_model_and_survive_the_file()
    {
        var skill = SkillCatalog.NewLayoutSkill("Any to FA");
        Assert.False(skill.NeedsAi);
        Assert.True(skill.IsLayout);
        Assert.Equal(InputModes.Auto, skill.InputMode);

        var store = SkillCatalog.DefaultStore();
        store.Skills.Add(skill);
        var again = SkillStoreFile.Parse(SkillStoreFile.Serialize(store));
        var copy = again.Get(skill.Id)!;
        Assert.Equal((SkillKinds.Layout, "any", "fa"), (copy.Kind, copy.LayoutFrom, copy.LayoutTo));
        Assert.True(copy.IsLayout);
    }

    [Fact]
    public void The_built_in_layout_skills_start_off_and_are_optional()
    {
        var any = SkillCatalog.DefaultSkills().Single(s => s.Id == SkillIds.LayoutAnyFa);
        var swap = SkillCatalog.DefaultSkills().Single(s => s.Id == SkillIds.LayoutSwap);
        Assert.False(any.Enabled || swap.Enabled);
        Assert.Equal(("any", "fa"), (any.LayoutFrom, any.LayoutTo));
        Assert.Equal(SkillKinds.Swap, swap.Kind);
        Assert.Equal("Ctrl+Alt+L", swap.Hotkey);
        Assert.True(swap.HotkeyValue().IsDisabled == false && any.HotkeyValue().IsDisabled);
        Assert.Equal(InputModes.Auto, swap.InputMode);
        Assert.Equal(InputModes.Auto, any.InputMode);
        Assert.Empty(SkillCatalog.FindConflicts(SkillCatalog.DefaultSkills().Select(s => { s.Enabled = true; return s; }), HotkeyDefaults.Menu));
        Assert.Equal(("en", "fa"), (swap.LayoutFrom, swap.LayoutTo));
        Assert.True(SkillCatalog.CanDelete(any) && SkillCatalog.CanDelete(swap));
    }

    [Fact]
    public void An_old_skills_file_without_a_kind_is_an_ai_skill()
    {
        var store = SkillStoreFile.Parse("""{"skills":[{"id":"x","name":"X","prompt":"{{content}}"}]}""");
        Assert.Equal(SkillKinds.Ai, store.Get("x")!.Kind);
        Assert.True(store.Get("x")!.NeedsAi);
    }
}

/// <summary>The runner with a layout skill: no model, no key, no network, the whole selection flow still applies.</summary>
public sealed class LayoutRunnerTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-core-tests", Guid.NewGuid().ToString("N")[..8]);

    public LayoutRunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Layouts(params KeyboardLayout[] installed) : IKeyboardLayouts
    {
        public IReadOnlyList<KeyboardLayout> Installed() => installed;
    }

    private sealed class Caret : ICaretLocator
    {
        public ScreenPoint GetCaretOrCursor() => new(0, 0);

        public ScreenPoint GetCursor() => new(0, 0);
    }

    private sealed class NoAutostart : IAutostart
    {
        public bool IsEnabled => false;

        public void Enable(string executablePath)
        {
        }

        public void Disable()
        {
        }
    }

    private static KeyboardLayout Layout(string language, string a, string s, string d) =>
        new(language, language, new Dictionary<int, char> { [0x1E] = a[0], [0x1F] = s[0], [0x20] = d[0] });

    private (SkillRunner Runner, UserDataStore Store) Build(FakeClipboard fake, RecordingStatus status, IKeyboardLayouts? layouts)
    {
        var store = new UserDataStore(_dir) { LegacySource = () => null };
        store.Load();
        var secrets = new FakeSecrets();
        var platform = new PlatformServices(fake, fake, new Caret(), secrets, new NoAutostart(), new PlatformCapabilities(true, true, true, false), layouts);
        var runner = new SkillRunner(store, new LlmClient(new HttpClient(new FakeHandler((_, _) => throw new InvalidOperationException("no network")))), secrets,
            new SelectionService(fake, fake, fake) { PasteSettleDelay = TimeSpan.Zero }, platform, status, NullLogger.Instance);
        return (runner, store);
    }

    [Fact]
    public async Task A_layout_skill_runs_with_no_model_and_pastes_the_converted_text()
    {
        var fake = new FakeClipboard { Selection = "asd" };
        var status = new RecordingStatus();
        var (runner, store) = Build(fake, status, new Layouts(Layout("en", "a", "s", "d"), Layout("fa", "ش", "س", "ی")));
        var skill = SkillCatalog.NewLayoutSkill("Any to FA");
        store.Skills.Skills.Add(skill);

        Assert.True(runner.IsUsable(skill));
        await runner.RunAsync(skill, "w1", Ct);

        Assert.Equal("شسی", fake.PastedOver);
        Assert.DoesNotContain("status.ai_off", status.Keys);
        Assert.Contains("status.changed", status.Keys);
    }

    [Fact]
    public async Task Swap_goes_the_other_way_on_the_next_press()
    {
        var fake = new FakeClipboard { Selection = "شسی" };
        var status = new RecordingStatus();
        var (runner, store) = Build(fake, status, new Layouts(Layout("en", "a", "s", "d"), Layout("fa", "ش", "س", "ی")));
        await runner.RunAsync(store.Skills.Get(SkillIds.LayoutSwap)!, "w1", Ct);
        Assert.Equal("asd", fake.PastedOver);
    }

    [Fact]
    public async Task A_layout_that_is_not_installed_leaves_the_text_and_the_clipboard_alone()
    {
        var fake = new FakeClipboard { Selection = "asd" };
        var status = new RecordingStatus();
        var (runner, store) = Build(fake, status, new Layouts(Layout("en", "a", "s", "d")));
        await runner.RunAsync(store.Skills.Get(SkillIds.LayoutAnyFa)!, "w1", Ct);
        Assert.Null(fake.PastedOver);
        Assert.Contains("status.layout_missing", status.Keys);
        Assert.Equal("original clipboard", fake.Text);
    }

    [Fact]
    public async Task Swap_uses_the_two_installed_layouts_when_its_own_pair_is_not_there()
    {
        // The skill is stored as en/fa, but this machine has English and Arabic only.
        var fake = new FakeClipboard { Selection = "asd" };
        var status = new RecordingStatus();
        var layouts = new Layouts(Layout("en", "a", "s", "d"), Layout("ar", "ش", "س", "ي"));
        var (runner, store) = Build(fake, status, layouts);
        var swap = store.Skills.Get(SkillIds.LayoutSwap)!;
        Assert.True(runner.IsUsable(swap));
        await runner.RunAsync(swap, "w1", Ct);
        Assert.Equal("شسي", fake.PastedOver);
    }

    [Fact]
    public void A_layout_skill_is_usable_only_when_its_layouts_are_installed()
    {
        var fake = new FakeClipboard();
        var en = Layout("en", "a", "s", "d");
        var fa = Layout("fa", "ش", "س", "ی");
        var ru = Layout("ru", "ф", "ы", "в");
        var (both, store) = Build(fake, new RecordingStatus(), new Layouts(en, fa));
        Assert.True(both.IsUsable(store.Skills.Get(SkillIds.LayoutAnyFa)!));
        Assert.True(both.IsUsable(store.Skills.Get(SkillIds.LayoutSwap)!));

        var (noFa, store2) = Build(fake, new RecordingStatus(), new Layouts(en, ru, Layout("de", "a", "s", "d")));
        Assert.False(noFa.IsUsable(store2.Skills.Get(SkillIds.LayoutAnyFa)!));
        // Three layouts and no en/fa pair: there is no clear pair, so the swap waits for the user to pick two.
        Assert.False(noFa.IsUsable(store2.Skills.Get(SkillIds.LayoutSwap)!));

        var (none, store3) = Build(fake, new RecordingStatus(), layouts: null);
        Assert.False(none.IsUsable(store3.Skills.Get(SkillIds.LayoutAnyFa)!));
    }

    [Fact]
    public async Task A_platform_without_layouts_says_so()
    {
        var fake = new FakeClipboard { Selection = "asd" };
        var status = new RecordingStatus();
        var (runner, store) = Build(fake, status, layouts: null);
        await runner.RunAsync(store.Skills.Get(SkillIds.LayoutAnyFa)!, "w1", Ct);
        Assert.Null(fake.PastedOver);
        Assert.Contains("status.layout_unsupported", status.Keys);
    }
}
