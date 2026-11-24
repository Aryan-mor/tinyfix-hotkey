// Skill store, templating and conflict tests, ported from tests/test_skills.py and tests/test_prompts.py.

using TinyFix.Core.Hotkeys;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;
using TinyFix.Core.Skills;

namespace TinyFix.Core.Tests;

public sealed class SkillTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-core-tests", Guid.NewGuid().ToString("N")[..8]);

    public SkillTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Default_skills_include_fix_translate_and_rewrite()
    {
        var skills = SkillCatalog.DefaultSkills();
        Assert.Equal(["fix", "translate", "rewrite", "rewrite-language", "improve-prompt", "layout-any-fa", "layout-swap"], skills.Select(s => s.Id));
        Assert.Equal("Ctrl+Alt+Space", skills[0].Hotkey);
        Assert.True(skills[0].Auto);
        Assert.Equal("Ctrl+Alt+T", skills[1].Hotkey);
        Assert.True(skills[1].Auto);
        Assert.True(skills[0].OfflineFallback);
        Assert.False(skills[1].OfflineFallback);
        Assert.False(skills[2].Enabled);
        Assert.Equal(ModelModes.Better, skills[4].ModelMode);
    }

    [Fact]
    public void Legacy_hotkeys_and_prompts_are_migrated()
    {
        var path = Path.Combine(_dir, "skills.json");
        var (store, created) = SkillStoreFile.Load(path, new LegacySkillOptions
        {
            FixHotkey = "F3",
            TranslateHotkey = "Ctrl+Alt+T",
            TranslateSelectAll = true,
            FixPrompt = "Fix this: {{content}}",
            TranslatePrompt = "To {{language}}: {{content}}",
        });
        Assert.True(created);
        Assert.Equal("F3", store.Get(SkillIds.Fix)!.HotkeyValue().Display);
        Assert.Null(store.Get(SkillIds.FixAll));
        Assert.True(store.Get(SkillIds.Translate)!.Auto);
        Assert.Equal("Fix this: {{content}}", store.Get(SkillIds.Fix)!.Prompt);
        Assert.Contains("To {{language}}", store.Get(SkillIds.Translate)!.Prompt);
        Assert.False(store.Get(SkillIds.Rewrite)!.Enabled);

        SkillStoreFile.Save(path, store);
        Assert.Contains("\"skill_menu_hotkey\": \"Ctrl+Alt+M\"", File.ReadAllText(path));
        var (loaded, createdAgain) = SkillStoreFile.Load(path, new LegacySkillOptions { FixHotkey = "Ctrl+Alt+Space" });
        Assert.False(createdAgain);
        Assert.Equal("F3", loaded.Get(SkillIds.Fix)!.HotkeyValue().Display);
    }

    [Fact]
    public void Round_trip_custom_skill()
    {
        var store = SkillCatalog.DefaultStore();
        var skill = SkillCatalog.NewCustomSkill("Make formal email", SkillCatalog.ExampleSpecs[0].Prompt);
        skill.Hotkey = "Ctrl+Alt+E";
        store.Skills.Add(skill);
        var path = Path.Combine(_dir, "skills.json");
        SkillStoreFile.Save(path, store);
        var (loaded, created) = SkillStoreFile.Load(path);
        Assert.False(created);
        var found = loaded.Get(skill.Id)!;
        Assert.Equal("Make formal email", found.Name);
        Assert.False(found.Builtin);
        Assert.False(found.OfflineFallback);
        Assert.True(found.NeedsAi);
        Assert.Equal("Ctrl+Alt+E", found.HotkeyValue().Display);
        Assert.Contains("{{content}}", found.Prompt);
    }

    [Fact]
    public void Saved_file_uses_the_python_schema()
    {
        var json = SkillStoreFile.Serialize(SkillCatalog.DefaultStore());
        Assert.EndsWith("\n", json);
        Assert.DoesNotContain("\r", json);
        foreach (var key in new[] { "\"version\"", "\"skill_menu_hotkey\"", "\"dismissed\"", "\"skills\"", "\"input_mode\"", "\"offline_fallback\"",
                     "\"model_mode\"", "\"model_provider\"", "\"model_fallback_provider\"", "\"model_fallback\"" })
            Assert.Contains(key, json);
        Assert.Contains("\"input_mode\": \"auto\"", json);
    }

    [Fact]
    public void A_python_written_file_loads()
    {
        const string pythonFile = """
            {
              "version": 1,
              "skill_menu_hotkey": "Ctrl+Alt+M",
              "dismissed": ["rewrite"],
              "skills": [
                {"id": "fix", "name": "Fix", "input_mode": "selection", "prompt": "p {{content}}", "hotkey": "Ctrl+Alt+Space",
                 "enabled": true, "builtin": true, "offline_fallback": true},
                {"id": "improve-prompt", "name": "Improve prompt", "input_mode": "selection", "prompt": "x", "hotkey": "none", "enabled": true}
              ]
            }
            """;
        var store = SkillStoreFile.Parse(pythonFile);
        Assert.Equal(["rewrite"], store.Dismissed);
        // A file from before per-skill models: Improve prompt gets the stronger model.
        Assert.Equal(ModelModes.Better, store.Get(SkillIds.ImprovePrompt)!.ModelMode);
        SkillCatalog.EnsureBuiltins(store);
        // A dismissed optional built-in is not added back, but missing core ones are.
        Assert.Null(store.Get(SkillIds.Rewrite));
        Assert.NotNull(store.Get(SkillIds.Translate));
        Assert.Equal(SkillIds.Translate, store.Skills[0].Id);
    }

    [Fact]
    public void Damaged_file_gives_the_defaults()
    {
        var path = Path.Combine(_dir, "skills.json");
        File.WriteAllText(path, "[1,2,3]");
        var (store, created) = SkillStoreFile.Load(path);
        Assert.True(created);
        Assert.NotNull(store.Get(SkillIds.Fix));
    }

    [Fact]
    public void Render_skill_prompt_replaces_tokens()
    {
        var skill = SkillCatalog.NewCustomSkill("Translate to German", "Translate into {{language}}:\n{{content}}");
        var rendered = SkillCatalog.RenderPrompt(skill, "Hello friends", "German");
        Assert.Contains("Hello friends", rendered);
        Assert.Contains("German", rendered);
        Assert.DoesNotContain("{{content}}", rendered);
        Assert.Contains("Hi", PromptRenderer.Render("Fix this.", "Hi"));
    }

    [Fact]
    public void Conflict_detection_across_skills_and_menu()
    {
        var skills = SkillCatalog.DefaultSkills();
        Assert.Empty(SkillCatalog.FindConflicts(skills, HotkeyDefaults.Menu));

        skills[0].Hotkey = "Ctrl+Alt+M";
        var conflicts = SkillCatalog.FindConflicts(skills, HotkeyDefaults.Menu);
        var single = Assert.Single(conflicts);
        Assert.Contains("Fix", single.Describe());
        Assert.Contains("Skill menu", single.Describe());
        Assert.Equal("Ctrl+Alt+M", single.Hotkey);

        skills[0].Hotkey = HotkeyDefaults.Fix;
        skills[1].Hotkey = HotkeyDefaults.Fix;
        conflicts = SkillCatalog.FindConflicts(skills, HotkeyDefaults.Menu);
        Assert.Contains(conflicts, c => c.Hotkey == HotkeyParser.Parse(HotkeyDefaults.Fix).Display);
        Assert.Contains(conflicts, c => c.Left == "Fix" && c.Right == "Translate");

        skills[1].Enabled = false;
        Assert.Empty(SkillCatalog.FindConflicts(skills, HotkeyDefaults.Menu));

        skills[2].Enabled = true;
        skills[2].Hotkey = "none";
        Assert.Empty(SkillCatalog.FindConflicts(skills, "none"));
    }

    [Fact]
    public void Disabled_and_invalid_hotkeys_are_ignored()
    {
        var skill = SkillCatalog.NewCustomSkill("Broken");
        skill.Hotkey = "not-a-hotkey";
        Assert.True(skill.HotkeyValue().IsDisabled);
        Assert.Empty(SkillCatalog.FindConflicts([skill], "none"));
    }

    [Fact]
    public void Cannot_delete_core_skills_but_can_duplicate_and_reorder()
    {
        var store = SkillCatalog.DefaultStore();
        Assert.False(SkillCatalog.Delete(store, SkillIds.Fix));
        Assert.NotNull(store.Get(SkillIds.Fix));
        var custom = SkillCatalog.NewCustomSkill("Summarize");
        store.Skills.Add(custom);
        Assert.True(SkillCatalog.Delete(store, custom.Id));
        Assert.Null(store.Get(custom.Id));

        store.Skills.Add(SkillCatalog.NewCustomSkill("A"));
        store.Skills.Add(SkillCatalog.NewCustomSkill("B"));
        var firstCustom = store.Skills[^2];
        Assert.True(SkillCatalog.Move(store, firstCustom.Id, 1));
        Assert.Equal(firstCustom.Id, store.Skills[^1].Id);
        var copy = SkillCatalog.Duplicate(store.Get(SkillIds.Translate)!);
        Assert.NotEqual(SkillIds.Translate, copy.Id);
        Assert.False(copy.Builtin);
        Assert.False(copy.OfflineFallback);
        Assert.True(copy.HotkeyValue().IsDisabled);
        Assert.Equal("Translate copy", copy.Name);
    }

    [Fact]
    public void Deleting_an_optional_builtin_remembers_it()
    {
        var store = SkillCatalog.DefaultStore();
        Assert.True(SkillCatalog.Delete(store, SkillIds.Rewrite));
        Assert.Contains(SkillIds.Rewrite, store.Dismissed);
        SkillCatalog.EnsureBuiltins(store);
        Assert.Null(store.Get(SkillIds.Rewrite));
    }

    [Fact]
    public void Enabled_skills_hide_ai_skills_when_offline()
    {
        var store = SkillCatalog.DefaultStore();
        store.Skills.Add(SkillCatalog.NewCustomSkill("Summarize"));
        // Every skill needs a usable model, so none is available when the caller says none is.
        Assert.Empty(SkillCatalog.EnabledSkills(store, _ => false));
        var on = SkillCatalog.EnabledSkills(store, _ => true).Select(s => s.Name).ToList();
        Assert.Contains("Translate", on);
        Assert.Contains("Summarize", on);
        Assert.DoesNotContain("Rewrite", on);
    }

    [Fact]
    public void Legacy_from_store_maps_builtins()
    {
        var store = SkillCatalog.DefaultStore();
        store.Get(SkillIds.Fix)!.Hotkey = "F4";
        store.Get(SkillIds.Translate)!.InputMode = InputModes.SelectAll;
        var legacy = SkillCatalog.LegacyFromStore(store);
        Assert.Equal("F4", legacy.FixHotkey);
        Assert.True(legacy.TranslateSelectAll);
    }

    [Fact]
    public void Ensure_builtins_restores_missing_fix()
    {
        var store = SkillCatalog.DefaultStore();
        store.Skills.RemoveAll(s => s.Id == SkillIds.Fix);
        SkillCatalog.EnsureBuiltins(store);
        Assert.Equal(SkillIds.Fix, store.Skills[0].Id);
    }

    [Fact]
    public void Rewrite_and_translate_never_use_offline_fallback_even_from_json()
    {
        foreach (var id in new[] { SkillIds.Translate, SkillIds.Rewrite, SkillIds.RewriteLanguage })
        {
            var skill = new Skill { Id = id, Name = "X", OfflineFallback = true, Builtin = true }.Normalize();
            Assert.False(skill.OfflineFallback);
            Assert.True(skill.NeedsAi);
        }
        var store = SkillCatalog.DefaultStore();
        store.Get(SkillIds.Rewrite)!.OfflineFallback = true;
        store.Get(SkillIds.Translate)!.OfflineFallback = true;
        SkillCatalog.EnsureBuiltins(store);
        Assert.False(store.Get(SkillIds.Rewrite)!.OfflineFallback);
        Assert.False(store.Get(SkillIds.Translate)!.OfflineFallback);
    }

    [Fact]
    public void Unedited_old_prompts_are_upgraded_but_custom_ones_are_kept()
    {
        var store = SkillCatalog.DefaultStore();
        store.Get(SkillIds.ImprovePrompt)!.Prompt = DefaultPrompts.LegacyImprove[0];
        store.Get(SkillIds.Fix)!.Prompt = DefaultPrompts.LegacyFix[2];
        store.Get(SkillIds.Translate)!.Prompt = "my own prompt {{content}}";
        SkillCatalog.EnsureBuiltins(store);
        Assert.Equal(DefaultPrompts.ImprovePrompt, store.Get(SkillIds.ImprovePrompt)!.Prompt);
        Assert.Equal(DefaultPrompts.Fix, store.Get(SkillIds.Fix)!.Prompt);
        Assert.Equal("my own prompt {{content}}", store.Get(SkillIds.Translate)!.Prompt);
    }

    [Fact]
    public void Example_specs_are_documented() =>
        Assert.Equal(["Make formal email", "Summarize", "Translate to German"], SkillCatalog.ExampleSpecs.Select(s => s.Name));

    // ---- prompts (tests/test_prompts.py)

    [Fact]
    public void Render_replaces_content_and_language()
    {
        var text = PromptRenderer.Render("Fix this:\n{{content}}\nLang={{language}}", "Hi frinds", "French");
        Assert.Contains("Hi frinds", text);
        Assert.DoesNotContain("{{content}}", text);
        Assert.Contains("Lang=French", text);
    }

    [Fact]
    public void Render_appends_content_when_token_missing()
    {
        var text = PromptRenderer.Render("You are a copy editor.", "Hi frinds");
        Assert.StartsWith("You are a copy editor.", text);
        Assert.EndsWith("Hi frinds", text);
    }

    [Fact]
    public void Default_prompts_include_required_tokens()
    {
        foreach (var prompt in new[] { DefaultPrompts.Fix, DefaultPrompts.Translate, DefaultPrompts.Rewrite, DefaultPrompts.RewriteLanguage })
            Assert.Contains("{{content}}", prompt);
        Assert.Contains("{{language}}", DefaultPrompts.Translate);
        Assert.Contains("{{language}}", DefaultPrompts.RewriteLanguage);
        var rendered = PromptRenderer.Render(DefaultPrompts.Fix, "abc");
        Assert.Contains("abc", rendered);
        Assert.DoesNotContain("{{content}}", rendered);
    }

    [Fact]
    public void Default_prompt_for_skill_mapping()
    {
        Assert.Equal(DefaultPrompts.Fix, PromptRenderer.DefaultFor(SkillIds.Fix));
        Assert.Equal(DefaultPrompts.Translate, PromptRenderer.DefaultFor(SkillIds.Translate));
        Assert.Equal(DefaultPrompts.Rewrite, PromptRenderer.DefaultFor(SkillIds.Rewrite));
        Assert.Equal(DefaultPrompts.RewriteLanguage, PromptRenderer.DefaultFor(SkillIds.RewriteLanguage));
        Assert.Equal(PromptRenderer.DefaultCustomPrompt, PromptRenderer.DefaultFor("custom-id"));
    }

    [Fact]
    public void Strip_code_fences_and_quotes()
    {
        Assert.Equal("Hello friends", PromptRenderer.StripModelOutput("```\nHello friends\n```"));
        Assert.Equal("Hello friends", PromptRenderer.StripModelOutput("```text\nHello friends\n```"));
        Assert.Equal("Hello friends", PromptRenderer.StripModelOutput("\"Hello friends\""));
        Assert.Equal("Hello friends", PromptRenderer.StripModelOutput("'Hello friends'"));
        Assert.Equal("He said \"hello\" to friends", PromptRenderer.StripModelOutput("He said \"hello\" to friends"));
        Assert.Equal("already clean", PromptRenderer.StripModelOutput("  already clean  "));
        Assert.Equal("", PromptRenderer.StripModelOutput(null));
        Assert.Equal("", PromptRenderer.StripModelOutput(""));
    }

    [Fact]
    public void Improve_prompt_renders_the_draft_inside_tags_in_any_language()
    {
        var rendered = PromptRenderer.Render(DefaultPrompts.ImprovePrompt, "سلام دنیا");
        Assert.Contains("<draft>\nسلام دنیا\n</draft>", rendered);
        Assert.Equal(1, DefaultPrompts.ImprovePrompt.Split("{{content}}").Length - 1);
    }

    [Fact]
    public void Fix_prompt_requires_capitalization_and_checks_every_word()
    {
        Assert.Contains("Capitalize the first letter of every sentence", DefaultPrompts.Fix);
        Assert.Contains("Check every word", DefaultPrompts.Fix);
        Assert.EndsWith("Don't use em dashes.\n\n{{content}}\n", DefaultPrompts.Fix);
    }

    [Fact]
    public void A_skill_file_without_the_new_endpoint_fields_still_loads()
    {
        var store = SkillStoreFile.Parse("""
            {
              "version": 1,
              "skill_menu_hotkey": "Ctrl+Alt+Space",
              "skills": [
                {
                  "id": "fix",
                  "name": "Fix",
                  "prompt": "Keep {{content}}",
                  "hotkey": "F3",
                  "model_mode": "default",
                  "extra_field": { "nested": true }
                }
              ]
            }
            """);
        var skill = Assert.Single(store.Skills);
        Assert.Equal("Keep {{content}}", skill.Prompt);
        Assert.Equal("", skill.ModelBaseUrl);
        Assert.Equal("", skill.ModelFallbackBaseUrl);
        Assert.Equal(0, skill.ModelTimeout);
        Assert.Equal(0, skill.ModelFallbackTimeout);
        Assert.Equal(ModelModes.Default, skill.ModelMode);
    }

    [Fact]
    public void Custom_endpoint_fields_round_trip_and_unknown_fields_do_not_drop_known_ones()
    {
        var skill = SkillCatalog.NewCustomSkill("Custom");
        skill.ModelMode = ModelModes.Custom;
        skill.ModelProvider = "ollama";
        skill.Model = "llama3.2";
        skill.ModelBaseUrl = "http://127.0.0.1:11434/v1";
        skill.ModelTimeout = 12;
        skill.ModelFallbackProvider = "anthropic";
        skill.ModelFallback = "claude-sonnet-4-5";
        skill.ModelFallbackBaseUrl = "https://api.anthropic.com";
        skill.ModelFallbackTimeout = 20;
        var store = new SkillStore { Skills = [skill] };
        var loaded = SkillStoreFile.Parse(SkillStoreFile.Serialize(store)).Skills[0];
        Assert.Equal(ModelModes.Custom, loaded.ModelMode);
        Assert.Equal("ollama", loaded.ModelProvider);
        Assert.Equal("llama3.2", loaded.Model);
        Assert.Equal("http://127.0.0.1:11434/v1", loaded.ModelBaseUrl);
        Assert.Equal(12, loaded.ModelTimeout);
        Assert.Equal("anthropic", loaded.ModelFallbackProvider);
        Assert.Equal("claude-sonnet-4-5", loaded.ModelFallback);
        Assert.Equal("https://api.anthropic.com", loaded.ModelFallbackBaseUrl);
        Assert.Equal(20, loaded.ModelFallbackTimeout);

        var withUnknown = SkillStoreFile.Serialize(store).Replace("\"model\": \"llama3.2\"", "\"model\": \"llama3.2\", \"future\": 1", StringComparison.Ordinal);
        var kept = SkillStoreFile.Parse(withUnknown).Skills[0];
        Assert.Equal("llama3.2", kept.Model);
        Assert.Equal("http://127.0.0.1:11434/v1", kept.ModelBaseUrl);
    }
}
