// Chooses the endpoints one skill runs with, and whether a fallback endpoint is worth calling.

namespace TinyFix.Core.Llm;

/// <summary>A skill's own endpoint choice (model mode "custom"). Empty strings mean "use the global endpoint".</summary>
public sealed record SkillModelChoice(
    string Provider = "",
    string Model = "",
    string FallbackProvider = "",
    string FallbackModel = "",
    string BaseUrl = "",
    string FallbackBaseUrl = "",
    double Timeout = 0,
    double FallbackTimeout = 0);

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
            [LlmProtocols.Google] = ("gemini-3.6-flash", "gemini-3.5-flash-lite"),
            [LlmProtocols.OpenRouter] = ("anthropic/claude-sonnet-5.5", "google/gemini-3.6-flash"),
        };

    /// <summary>The settings a skill runs with. A custom endpoint wins over the global one; an empty custom slot keeps the global one.</summary>
    /// <param name="keys">Saved API keys by secret-store account.</param>
    /// <param name="skillId">When set, <c>skill:{id}:main</c> and <c>skill:{id}:fallback</c> are looked up before the provider key.</param>
    public static LlmConfig ForSkill(LlmConfig baseConfig, string mode, SkillModelChoice? custom = null,
        IReadOnlyDictionary<string, string>? keys = null, string? skillId = null)
    {
        keys ??= new Dictionary<string, string>();
        if (mode == ModelModes.Better)
        {
            if (!BetterModels.TryGetValue(baseConfig.Main.Protocol, out var pair))
                return baseConfig;
            // The stronger pair stays on the main endpoint's protocol and key. The global fallback endpoint is not used.
            return baseConfig with
            {
                Main = baseConfig.Main with { Model = pair.Main },
                Fallback = baseConfig.Main with { Model = pair.Fallback },
            };
        }
        if (mode != ModelModes.Custom)
            return baseConfig;

        custom ??= new SkillModelChoice();
        var main = Override(baseConfig.Main, custom.Provider, custom.Model, custom.BaseUrl, custom.Timeout, keys,
            skillId is null ? null : LlmSecrets.SkillMain(skillId));
        var fallback = HasOverride(custom.FallbackProvider, custom.FallbackModel, custom.FallbackBaseUrl)
            ? Override(baseConfig.Fallback ?? baseConfig.Main, custom.FallbackProvider, custom.FallbackModel, custom.FallbackBaseUrl,
                custom.FallbackTimeout, keys, skillId is null ? null : LlmSecrets.SkillFallback(skillId))
            : baseConfig.Fallback;
        return baseConfig with { Main = main, Fallback = fallback };
    }

    /// <summary>The endpoint that answers when the main one fails, or null when there is nothing different to try.</summary>
    public static LlmEndpoint? UsableFallback(LlmConfig config)
    {
        var backup = config.Fallback;
        if (backup is null || string.IsNullOrWhiteSpace(backup.Model))
            return null;
        var protocol = LlmProtocols.Normalize(backup.Protocol);
        var model = backup.Model.Trim();
        var mainModel = (string.IsNullOrWhiteSpace(config.Main.Model) ? LlmProtocols.DefaultModel(config.Main.Protocol) : config.Main.Model).Trim();
        if (protocol == config.Main.Protocol && model == mainModel && SameBase(backup.BaseUrl, config.Main.BaseUrl))
            return null;
        var candidate = backup with { Protocol = protocol, Model = model };
        // A remote preset without a key cannot answer; a preset with no default URL needs one typed in.
        if (!candidate.IsUsable())
            return null;
        return candidate;
    }

    private static bool HasOverride(string provider, string model, string baseUrl) =>
        !string.IsNullOrWhiteSpace(provider) || !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(baseUrl);

    private static LlmEndpoint Override(LlmEndpoint global, string provider, string model, string baseUrl, double timeout,
        IReadOnlyDictionary<string, string> keys, string? slot)
    {
        if (!HasOverride(provider, model, baseUrl))
            return global;
        var protocol = string.IsNullOrWhiteSpace(provider) ? global.Protocol : provider.Trim();
        if (!LlmProtocols.All.Contains(protocol) && protocol != LlmDefaults.ProviderOpenAi)
            return global;
        protocol = LlmProtocols.Normalize(protocol);
        var same = protocol == global.Protocol;
        var resolvedModel = string.IsNullOrWhiteSpace(model)
            ? (same ? global.Model : LlmProtocols.DefaultModel(protocol))
            : model.Trim();
        var resolvedBase = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.Trim() : same ? global.BaseUrl : "";
        var key = LlmSecrets.Resolve(keys, slot, protocol);
        // Same protocol and no separate key: keep the key the global endpoint already resolved (often the "main" slot).
        if (key.Length == 0 && same)
            key = global.ApiKey;
        return global with
        {
            Protocol = protocol,
            Model = resolvedModel,
            BaseUrl = resolvedBase,
            ApiKey = key,
            TimeoutSeconds = timeout > 0 ? timeout : global.TimeoutSeconds,
        };
    }

    private static bool SameBase(string left, string right) =>
        string.Equals(left.Trim().TrimEnd('/'), right.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
