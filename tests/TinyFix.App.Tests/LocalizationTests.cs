// Keeps the shipped languages in sync so none silently falls back to English: same keys, same {0} placeholders, no
// empty values, every key the code or XAML uses exists, and the OS-language rules pick the right translation.
// Adapted from the reference project's localization tests (MIT, credited in THIRD-PARTY-NOTICES.md).

using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using TinyFix.App.Services;

namespace TinyFix.App.Tests;

public partial class LocalizationTests
{
    private static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TinyFix.slnx")))
                dir = dir.Parent;
            return dir!.FullName;
        }
    }

    private static string I18nDirectory => Path.Combine(RepositoryRoot, "src", "TinyFix.App", "Assets", "i18n");

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(I18nDirectory, code + ".json")))!;

    [GeneratedRegex("\\{\\d+\\}")]
    private static partial Regex Placeholder();

    public static TheoryData<string> Languages => new(AppLanguages.All.Where(l => l.Code != "en").Select(l => l.Code));

    [Fact]
    public void The_registry_covers_every_shipped_translation_once()
    {
        var codes = AppLanguages.All.Select(l => l.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Directory.GetFiles(I18nDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).Order(), codes.Order());
        Assert.Contains(("fa", "فارسی"), AppLanguages.All);
        Assert.Contains(("ja", "日本語"), AppLanguages.All);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_exactly_the_english_keys(string code)
    {
        var english = Load("en").Keys.ToHashSet();
        var other = Load(code).Keys.ToHashSet();
        Assert.Empty(english.Except(other));
        Assert.Empty(other.Except(english));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Placeholders_match_the_english_text(string code)
    {
        var english = Load("en");
        var translated = Load(code);
        foreach (var (key, text) in english)
        {
            var expected = Placeholder().Matches(text).Select(m => m.Value).Order().ToList();
            var actual = Placeholder().Matches(translated[key]).Select(m => m.Value).Order().ToList();
            Assert.True(expected.SequenceEqual(actual), $"{code}:{key} has placeholders [{string.Join(",", actual)}], English has [{string.Join(",", expected)}]");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fa")]
    [InlineData("ja")]
    public void Catalogs_have_no_duplicate_keys_or_empty_translations(string code)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(I18nDirectory, code + ".json")));
        var entries = json.RootElement.EnumerateObject().ToList();
        Assert.Equal(entries.Count, entries.Select(e => e.Name).Distinct().Count());
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Value.GetString()), $"{code}:{e.Name}"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Prompt_placeholders_are_never_translated(string code)
    {
        var english = Load("en");
        var translated = Load(code);
        foreach (var key in english.Keys.Where(k => english[k].Contains("{{content}}", StringComparison.Ordinal)))
        {
            Assert.Contains("{{content}}", translated[key]);
            Assert.Contains("{{language}}", translated[key]);
        }
    }

    [GeneratedRegex("\"((?:app|tray|donut|nav|settings|skills|shortcut|skill|online|updates|about|status|clipboard|common)\\.[a-z0-9_-]+)\"")]
    private static partial Regex KeyLiteral();

    [GeneratedRegex("^(?:app|tray|donut|nav|settings|skills|shortcut|skill|online|updates|about|status|clipboard|common)\\.")]
    private static partial Regex KeyPrefix();

    [GeneratedRegex("\\{DynamicResource ([A-Za-z0-9_.-]+)\\}")]
    private static partial Regex XamlKey();

    [Fact]
    public void Every_key_used_by_code_or_xaml_exists_in_english()
    {
        var english = Load("en");
        var used = new HashSet<string>();
        foreach (var project in new[] { "TinyFix.App", "TinyFix.Core" })
        {
            var root = Path.Combine(RepositoryRoot, "src", project);
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                var text = File.ReadAllText(file);
                foreach (Match m in KeyLiteral().Matches(text))
                    if (!m.Groups[1].Value.EndsWith(".json", StringComparison.Ordinal))
                        used.Add(m.Groups[1].Value);
                foreach (Match m in XamlKey().Matches(text))
                    if (KeyPrefix().IsMatch(m.Groups[1].Value))
                        used.Add(m.Groups[1].Value);
            }
        }
        // Keys built from an id ("skill." + id) are checked separately below.
        var missing = used.Where(k => !english.ContainsKey(k) && !k.EndsWith('.')).Order().ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_builtin_skill_has_a_translated_name_key()
    {
        var english = Load("en");
        foreach (var id in TinyFix.Core.Skills.SkillIds.Builtin)
            Assert.True(english.ContainsKey("skill." + id), $"skill.{id}");
    }

    [Fact]
    public void No_key_is_unused()
    {
        var english = Load("en");
        var corpus = new System.Text.StringBuilder();
        foreach (var project in new[] { "TinyFix.App", "TinyFix.Core" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", project), "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
                corpus.Append(File.ReadAllText(file));
        var text = corpus.ToString();
        // "skill.<id>" keys are built at run time; everything else must appear literally.
        var unused = english.Keys.Where(k => !k.StartsWith("skill.", StringComparison.Ordinal) && !text.Contains(k, StringComparison.Ordinal)).Order().ToList();
        Assert.Empty(unused);
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de", "de")]
    [InlineData("fa-IR", "fa")]
    [InlineData("fa_AF", "fa")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ja", "ja")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("frr", "en")]
    [InlineData("jam", "en")]
    [InlineData("pt-BR", "en")]
    [InlineData("zh-Hant-TW", "en")]
    [InlineData("", "en")]
    public void The_os_language_picks_a_shipped_translation(string os, string expected) =>
        Assert.Equal(expected, LocalizationService.Resolve("auto", () => os));

    [Fact]
    public void An_explicit_choice_wins_over_the_os_language()
    {
        Assert.Equal("ja", LocalizationService.Resolve("ja", () => "de-DE"));
        Assert.Equal("fa", LocalizationService.Resolve("FA", () => "en-US"));
        Assert.Equal("de", LocalizationService.Resolve("unknown", () => "de-AT"));
    }

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("pt-Latn-BR", "pt-BR")]
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("pt", "pt-PT")]
    [InlineData("pt-PT-x-br", "pt-PT")]
    public void Portuguese_regions_are_told_apart(string os, string expected) => Assert.Equal(expected, AppLanguages.PortugueseVariant(os));

    [Theory]
    [InlineData("zh-TW", true)]
    [InlineData("zh-HK", true)]
    [InlineData("zh-Hant-CN", true)]
    [InlineData("zh-Hans-TW", false)]
    [InlineData("zh-CN", false)]
    [InlineData("zh", false)]
    [InlineData("ja", false)]
    public void Chinese_script_decides_traditional_or_simplified(string culture, bool traditional) =>
        Assert.Equal(traditional, AppLanguages.IsTraditionalChinese(culture));

    [Fact]
    public void Persian_and_arabic_mirror_the_layout_and_get_font_lists()
    {
        Assert.True(AppLanguages.IsRightToLeft("fa"));
        Assert.True(AppLanguages.IsRightToLeft("ar-SA"));
        Assert.All(AppLanguages.All.Where(l => l.Code != "fa"), l => Assert.False(AppLanguages.IsRightToLeft(l.Code)));
        Assert.Equal(Avalonia.Media.FlowDirection.RightToLeft, (SoftPalette.IsRightToLeft("fa") ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight));
        Assert.Equal(Avalonia.Media.FlowDirection.LeftToRight, (SoftPalette.IsRightToLeft("ja") ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight));
        Assert.Contains("Noto Sans Arabic", SoftPalette.FontFamilies("fa"));
        Assert.Contains("Yu Gothic UI", SoftPalette.FontFamilies("ja"));
        Assert.DoesNotContain("Yu Gothic UI", SoftPalette.FontFamilies("de"));
    }

    [AvaloniaFact]
    public void Missing_keys_fall_back_to_english_then_to_the_key()
    {
        var s = TestServices.EnsureInitialized("de");
        Assert.Equal("de", s.Loc.Language);
        Assert.Equal("no.such.key", s.Loc["no.such.key"]);
        Assert.NotEqual(Load("en")["tray.quit"], s.Loc["tray.quit"]);
        TestServices.EnsureInitialized("en");
        Assert.Equal("Quit", s.Loc["tray.quit"]);
    }

    [AvaloniaFact]
    public void Format_fills_placeholders_and_survives_a_bad_format()
    {
        var s = TestServices.EnsureInitialized("en");
        Assert.Equal("Running Fix...", s.Loc.Format("status.running", "Fix"));
        Assert.Equal("Fix and Translate both use Ctrl+X", s.Loc.Format("skills.conflict", "Fix", "Translate", "Ctrl+X"));
        // Fewer arguments than placeholders must not throw.
        Assert.Contains("{0}", s.Loc.Format("status.running"));
    }
}
