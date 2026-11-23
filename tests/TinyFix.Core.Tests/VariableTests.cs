// Prompt variables: rendering, reserved names, and migration of the old target-language and LLM keys.

using System.Text.Json;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;
using TinyFix.Core.Settings;

namespace TinyFix.Core.Tests;

public sealed class VariableTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-core-tests", Guid.NewGuid().ToString("N")[..8]);

    public VariableTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Names_reject_content_and_ill_formed_or_duplicate_keys()
    {
        Assert.Equal("reserved", VariableNames.Check("content", []));
        Assert.Null(VariableNames.Check("Content", []));
        Assert.Equal("invalid", VariableNames.Check("1name", []));
        Assert.Equal("invalid", VariableNames.Check("has-dash", []));
        Assert.Equal("invalid", VariableNames.Check("", []));
        Assert.Equal("invalid", VariableNames.Check("two words", []));
        Assert.Equal("duplicate", VariableNames.Check("tone", ["language", "tone"]));
        Assert.Null(VariableNames.Check("Tone", ["tone"]));
        Assert.Null(VariableNames.Check("tone_2", ["language"]));
    }

    [Fact]
    public void Rendering_inserts_values_once_and_leaves_unknown_tokens()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VariableNames.Language] = "German",
            ["tone"] = "see {{content}} and {{tone}}",
            ["content"] = "must not win",
        };
        var rendered = PromptRenderer.Render("{{tone}} / {{language}} / {{missing}} / {{content}}", "Hello", variables);
        Assert.Equal("see {{content}} and {{tone}} / German / {{missing}} / Hello", rendered);
        // A value is not scanned again, so {{content}} inside a value stays literal.
        Assert.Equal(1, rendered.Split("Hello").Length - 1);
    }

    [Fact]
    public void A_template_without_the_content_token_still_appends_the_selection()
    {
        var rendered = PromptRenderer.Render("Write in {{language}}.", "Hi", new Dictionary<string, string> { ["language"] = "English" });
        Assert.EndsWith("Hi", rendered);
        Assert.Contains("Write in English.", rendered);
    }

    [Fact]
    public void Old_settings_move_the_target_language_and_the_flat_llm_keys()
    {
        File.WriteAllText(Path.Combine(_dir, UserDataStore.SettingsFileName), """
            {
              "language": "de",
              "llm_enabled": true,
              "llm_provider": "openai_compatible",
              "llm_model": "llama3",
              "llm_base_url": "http://localhost:11434/v1",
              "llm_fallback_provider": "google",
              "llm_fallback_model": "gemini-3.6-flash",
              "llm_timeout": 9,
              "llm_target_language": "German",
              "legacy_imported": true
            }
            """);
        var store = new UserDataStore(_dir) { LegacySource = () => null };
        store.Load();
        Assert.Equal("German", store.Settings.Variables["language"]);
        Assert.Equal("de", store.Settings.Language);
        Assert.Equal(LlmProtocols.Ollama, store.Settings.Llm.Main.Protocol);
        Assert.Equal("llama3", store.Settings.Llm.Main.Model);
        Assert.Equal("http://localhost:11434/v1", store.Settings.Llm.Main.BaseUrl);
        Assert.Equal(9, store.Settings.Llm.Main.Timeout);
        Assert.Equal(LlmProtocols.Google, store.Settings.Llm.Fallback!.Protocol);
        Assert.Equal("gemini-3.6-flash", store.Settings.Llm.Fallback.Model);

        store.SaveSettings();
        var saved = File.ReadAllText(store.SettingsPath);
        Assert.DoesNotContain("llm_target_language", saved);
        Assert.DoesNotContain("llm_enabled", saved);
        Assert.DoesNotContain("llm_provider", saved);
        Assert.DoesNotContain("llm_model", saved);
        Assert.Contains("\"variables\"", saved);
        Assert.Contains("\"language\": \"German\"", saved);

        var again = new UserDataStore(_dir) { LegacySource = () => null };
        again.Load();
        Assert.Equal("German", again.Settings.Variables["language"]);
        Assert.Equal(LlmProtocols.Ollama, again.Settings.Llm.Main.Protocol);
        Assert.Equal("llama3", again.Settings.Llm.Main.Model);
    }

    [Fact]
    public void A_missing_fallback_model_keeps_the_old_default_and_an_empty_one_means_none()
    {
        File.WriteAllText(Path.Combine(_dir, UserDataStore.SettingsFileName), """
            { "llm_provider": "google", "llm_model": "gemini-2.0-flash", "legacy_imported": true }
            """);
        var kept = new UserDataStore(_dir) { LegacySource = () => null };
        kept.Load();
        Assert.Equal("gemini-2.0-flash", kept.Settings.Llm.Main.Model);
        Assert.Equal(LlmDefaults.GoogleFallbackModel, kept.Settings.Llm.Fallback!.Model);

        File.WriteAllText(Path.Combine(_dir, UserDataStore.SettingsFileName), """
            { "llm_provider": "google", "llm_fallback_model": "", "legacy_imported": true }
            """);
        var none = new UserDataStore(_dir) { LegacySource = () => null };
        none.Load();
        Assert.Null(none.Settings.Llm.Fallback);
    }

    [Fact]
    public void An_already_migrated_file_is_not_overwritten_by_leftover_flat_keys()
    {
        File.WriteAllText(Path.Combine(_dir, UserDataStore.SettingsFileName), """
            {
              "llm_provider": "google",
              "llm_target_language": "German",
              "variables": { "language": "French", "tone": "see {{content}}" },
              "llm": { "main": { "protocol": "anthropic", "model": "claude-sonnet-4-5" }, "fallback": null },
              "legacy_imported": true
            }
            """);
        var store = new UserDataStore(_dir) { LegacySource = () => null };
        store.Load();
        Assert.Equal("French", store.Settings.Variables["language"]);
        Assert.Equal("see {{content}}", store.Settings.Variables["tone"]);
        Assert.Equal(LlmProtocols.Anthropic, store.Settings.Llm.Main.Protocol);
        Assert.Equal("claude-sonnet-4-5", store.Settings.Llm.Main.Model);
        Assert.Null(store.Settings.Llm.Fallback);
        Assert.False(store.Settings.Variables.ContainsKey(VariableNames.Content));
    }

    [Fact]
    public void Saved_variables_round_trip_including_a_literal_placeholder()
    {
        var store = new UserDataStore(_dir) { LegacySource = () => null };
        store.Load();
        store.Settings.Variables["language"] = "Japanese";
        store.Settings.Variables["note"] = "keep {{content}} literal";
        store.SaveSettings();
        var again = new UserDataStore(_dir) { LegacySource = () => null };
        again.Load();
        Assert.Equal("Japanese", again.Settings.Variables["language"]);
        Assert.Equal("keep {{content}} literal", again.Settings.Variables["note"]);
        var rendered = PromptRenderer.Render("{{note}} {{content}}", "TEXT", again.Settings.Variables);
        Assert.Equal("keep {{content}} literal TEXT", rendered);
        using var doc = JsonDocument.Parse(File.ReadAllText(again.SettingsPath));
        Assert.False(doc.RootElement.TryGetProperty("llm_target_language", out _));
    }
}
