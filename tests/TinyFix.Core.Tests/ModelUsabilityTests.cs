// A model is usable when it has a model id, a base URL where the protocol has none, and a key when the protocol
// asks for one. An old settings file that still contains llm_enabled loads; the key is not written back.

using TinyFix.Core.Llm;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.Core.Tests;

public sealed class ModelUsabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-core-tests", Guid.NewGuid().ToString("N")[..8]);

    public ModelUsabilityTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(LlmProtocols.Google, true)]
    [InlineData(LlmProtocols.OpenRouter, true)]
    [InlineData(LlmProtocols.OpenAi, true)]
    [InlineData(LlmProtocols.Anthropic, true)]
    [InlineData(LlmProtocols.Ollama, false)]
    [InlineData(LlmProtocols.LmStudio, false)]
    [InlineData(LlmProtocols.Custom, false)]
    public void Each_protocol_needs_a_key_only_when_its_preset_does(string protocol, bool needsKey)
    {
        var endpoint = new LlmEndpoint
        {
            Protocol = protocol,
            Model = "m",
            BaseUrl = protocol == LlmProtocols.Custom ? "http://127.0.0.1:9/v1" : "",
            ApiKey = "",
        };
        Assert.Equal(!needsKey, endpoint.IsUsable());
        if (needsKey)
            Assert.True((endpoint with { ApiKey = "k" }).IsUsable());
    }

    [Fact]
    public void An_empty_model_or_a_custom_url_left_blank_is_not_usable()
    {
        Assert.False(new LlmEndpoint { Protocol = LlmProtocols.Ollama, Model = "" }.IsUsable());
        Assert.False(new LlmEndpoint { Protocol = LlmProtocols.Custom, Model = "m", BaseUrl = "  " }.IsUsable());
        Assert.True(new LlmEndpoint { Protocol = LlmProtocols.Custom, Model = "m", BaseUrl = "http://127.0.0.1:9/v1" }.IsUsable());
        Assert.False(new LlmConfig().Ready);
    }

    [Fact]
    public void A_skill_override_is_judged_on_its_own()
    {
        var global = new LlmConfig { Main = new LlmEndpoint { Protocol = LlmProtocols.Google, Model = "gemini", ApiKey = "" } };
        Assert.False(global.Ready);
        var ollama = LlmConfigResolver.ForSkill(global, ModelModes.Custom,
            new SkillModelChoice(LlmProtocols.Ollama, "llama3.2"), new Dictionary<string, string>(), SkillIds.Fix);
        Assert.True(ollama.Ready);
        var openRouter = LlmConfigResolver.ForSkill(global, ModelModes.Custom,
            new SkillModelChoice(LlmProtocols.OpenRouter, "vendor/model"), new Dictionary<string, string>(), SkillIds.Fix);
        Assert.False(openRouter.Ready);
        var withKey = LlmConfigResolver.ForSkill(global, ModelModes.Custom,
            new SkillModelChoice(LlmProtocols.OpenRouter, "vendor/model"),
            new Dictionary<string, string> { [LlmSecrets.SkillMain(SkillIds.Fix)] = "k" }, SkillIds.Fix);
        Assert.True(withKey.Ready);
        Assert.Equal("k", withKey.Main.ApiKey);
    }

    [Fact]
    public void Fallback_usability_follows_the_protocol()
    {
        var main = new LlmEndpoint { Protocol = LlmProtocols.Google, Model = "main", ApiKey = "k" };
        LlmConfig Config(LlmEndpoint fallback) => new() { Main = main, Fallback = fallback };
        Assert.Null(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.OpenAi, Model = "m" })));
        Assert.NotNull(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.OpenAi, Model = "m", ApiKey = "k" })));
        Assert.NotNull(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.Ollama, Model = "llama" })));
        Assert.NotNull(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.LmStudio, Model = "local" })));
        Assert.Null(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.Custom, Model = "m" })));
        Assert.NotNull(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint
        {
            Protocol = LlmProtocols.Custom,
            Model = "m",
            BaseUrl = "http://localhost:9/v1",
        })));
        Assert.Null(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.Anthropic, Model = "c" })));
        Assert.NotNull(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.Anthropic, Model = "c", ApiKey = "k" })));
        Assert.Null(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.OpenRouter, Model = "x" })));
        Assert.NotNull(LlmConfigResolver.UsableFallback(Config(new LlmEndpoint { Protocol = LlmProtocols.Google, Model = "other", ApiKey = "k" })));
    }

    [Fact]
    public void Old_settings_with_llm_enabled_still_load_and_are_not_rewritten()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """
            {
              "llm_enabled": false,
              "language": "en",
              "llm": { "main": { "protocol": "ollama", "model": "llama3", "base_url": "http://127.0.0.1:11434/v1" } }
            }
            """);
        var store = new UserDataStore(_dir) { LegacySource = () => null };
        store.Load();
        Assert.Equal(LlmProtocols.Ollama, store.Settings.Llm.Main.Protocol);
        Assert.Equal("llama3", store.Settings.Llm.Main.Model);
        Assert.True(store.Settings.ToLlmConfig(new Dictionary<string, string>()).Ready);
        store.SaveSettings();
        var saved = File.ReadAllText(store.SettingsPath);
        Assert.DoesNotContain("llm_enabled", saved);
        Assert.DoesNotContain("test-key", saved);
    }
}
