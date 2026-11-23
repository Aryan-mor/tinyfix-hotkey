// Chooses the LLM settings one skill runs with, and the fallback of a request. Ported from llm_client.py.

namespace TinyFix.Core.Llm;

/// <summary>A skill's own provider and model choice (model mode "custom").</summary>
public sealed record SkillModelChoice(string Provider = "", string Model = "", string FallbackProvider = "", string FallbackModel = "");

/// <summary>Pure functions that derive request settings.</summary>
public static class LlmConfigResolver
{
    /// <summary>
    /// The "better" preset of each provider: (main, fallback). Google: a stronger Flash model, then the free Flash-Lite
    /// model with the largest free allowance, so a user on the free tier still gets an answer when the quota runs out.
    /// </summary>
    public static IReadOnlyDictionary<string, (string Main, string Fallback)> BetterModels { get; } =
        new Dictionary<string, (string, string)>
        {
            [LlmDefaults.ProviderGoogle] = ("gemini-3.6-flash", "gemini-3.5-flash-lite"),
            [LlmDefaults.ProviderOpenRouter] = ("anthropic/claude-sonnet-5.5", "google/gemini-3.6-flash"),
        };

    /// <summary>The settings a skill runs with: the global ones, a stronger preset, or the skill's own choice.</summary>
    /// <param name="keys">Saved API keys by provider id.</param>
    public static LlmConfig ForSkill(LlmConfig baseConfig, string mode, SkillModelChoice? custom = null,
        IReadOnlyDictionary<string, string>? keys = null)
    {
        keys ??= new Dictionary<string, string>();
        if (mode == ModelModes.Better)
        {
            return BetterModels.TryGetValue(baseConfig.Provider, out var pair)
                ? baseConfig with { Model = pair.Main, FallbackModel = pair.Fallback, FallbackProvider = "", FallbackApiKey = "" }
                : baseConfig;
        }
        if (mode != ModelModes.Custom)
            return baseConfig;

        custom ??= new SkillModelChoice();
        var provider = string.IsNullOrWhiteSpace(custom.Provider) ? baseConfig.Provider : custom.Provider.Trim();
        if (!LlmDefaults.Providers.Contains(provider))
            return baseConfig;
        var same = provider == baseConfig.Provider;
        var key = same ? baseConfig.ApiKey : Key(keys, provider);
        // A skill that names a provider without a saved key falls back to the Online AI settings instead of failing.
        if (key.Length == 0 && provider is LlmDefaults.ProviderGoogle or LlmDefaults.ProviderOpenRouter)
            return baseConfig;
        var main = string.IsNullOrWhiteSpace(custom.Model)
            ? (same ? baseConfig.Model : LlmDefaults.DefaultModelFor(provider))
            : custom.Model.Trim();
        var fbProvider = string.IsNullOrWhiteSpace(custom.FallbackProvider) ? provider : custom.FallbackProvider.Trim();
        if (!LlmDefaults.Providers.Contains(fbProvider))
            fbProvider = provider;
        var fbModel = custom.FallbackModel.Trim();
        if (fbModel.Length == 0 && fbProvider == provider && same)
            fbModel = baseConfig.FallbackModel;
        return baseConfig with
        {
            Provider = provider,
            ApiKey = key,
            BaseUrl = same ? baseConfig.BaseUrl : "",
            Model = main,
            FallbackModel = fbModel,
            FallbackProvider = fbProvider == provider ? "" : fbProvider,
            FallbackApiKey = fbProvider == provider ? "" : Key(keys, fbProvider),
        };
    }

    /// <summary>The config that answers when the main model fails, or null when there is nothing different to try.</summary>
    public static LlmConfig? Fallback(LlmConfig config)
    {
        var backup = config.FallbackModel.Trim();
        if (backup.Length == 0)
            return null;
        var provider = string.IsNullOrWhiteSpace(config.FallbackProvider) ? config.Provider : config.FallbackProvider.Trim();
        if (provider == config.Provider)
        {
            var main = (string.IsNullOrWhiteSpace(config.Model) ? LlmDefaults.DefaultModelFor(provider) : config.Model).Trim();
            return backup == main ? null : config with { Model = backup };
        }
        var key = config.FallbackApiKey.Trim();
        // Another provider without its key cannot answer.
        if (key.Length == 0 && provider is LlmDefaults.ProviderGoogle or LlmDefaults.ProviderOpenRouter)
            return null;
        return config with { Provider = provider, ApiKey = key, Model = backup };
    }

    private static string Key(IReadOnlyDictionary<string, string> keys, string provider) =>
        keys.TryGetValue(provider, out var value) ? value.Trim() : "";
}
