// Settings of one LLM request: provider, models, key and timeout. Ported from llm_client.py (LLMConfig).

using TinyFix.Core.Prompts;

namespace TinyFix.Core.Llm;

/// <summary>Provider ids and model constants. The model names are copied from the Python app on purpose.</summary>
public static class LlmDefaults
{
    public const string ProviderGoogle = "google";
    public const string ProviderOpenAi = "openai_compatible";
    public const string ProviderOpenRouter = "openrouter";

    /// <summary>All providers, in the order the settings page lists them.</summary>
    public static IReadOnlyList<string> Providers { get; } = [ProviderGoogle, ProviderOpenRouter, ProviderOpenAi];

    public const string OpenRouterBase = "https://openrouter.ai/api/v1";
    public const string GeminiBase = "https://generativelanguage.googleapis.com/v1beta";
    public const string OpenAiBase = "https://api.openai.com/v1";

    // Cheap, fast, good at text: a Gemini Flash-Lite main and a different vendor (DeepSeek) as the fallback.
    public const string OpenRouterModel = "google/gemini-3.5-flash-lite";
    public const string OpenRouterFallbackModel = "deepseek/deepseek-v4.1-flash";
    public const string GoogleModel = "gemini-3.5-flash-lite";
    public const string GoogleFallbackModel = "gemini-3.6-flash";
    public const string OpenAiModel = "gpt-4o-mini";

    /// <summary>Older Google defaults that Google has since retired (HTTP 404); a saved copy is replaced by the default.</summary>
    public static IReadOnlyList<string> RetiredFallbackModels { get; } = ["gemini-2.5-flash-lite", "gemini-flash-lite-latest"];

    public const double DefaultTimeoutSeconds = 5.0;

    /// <summary>After the main model fails, the fallback answers first for this long before the main one is tried again.</summary>
    public static readonly TimeSpan FallbackWindow = TimeSpan.FromSeconds(120);

    /// <summary>Skills generate longer text and the user already waits on a status message, so they get more time than Fix.</summary>
    public const double SkillMinTimeoutSeconds = 10.0;

    public const string UserAgent = "TinyFix-Hotkey-LLM";

    /// <summary>The sample the "test connection" button sends.</summary>
    public const string TestSample = "Hi frinds";

    /// <summary>The model to use when the setting is empty.</summary>
    public static string ResolveGoogleModel(string? value) => string.IsNullOrWhiteSpace(value) ? GoogleModel : value.Trim();

    /// <summary>The default main model of a provider.</summary>
    public static string DefaultModelFor(string provider) => provider switch
    {
        ProviderGoogle => GoogleModel,
        ProviderOpenRouter => OpenRouterModel,
        _ => OpenAiModel,
    };
}

/// <summary>How a skill chooses its model: the global settings, a stronger preset, or its own provider and models.</summary>
public static class ModelModes
{
    public const string Default = "default";
    public const string Better = "better";
    public const string Custom = "custom";

    /// <summary>The valid values.</summary>
    public static IReadOnlyList<string> All { get; } = [Default, Better, Custom];
}

/// <summary>Everything one request needs.</summary>
public sealed record LlmConfig
{
    public string Provider { get; init; } = LlmDefaults.ProviderGoogle;
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = LlmDefaults.GoogleModel;
    public string FallbackModel { get; init; } = LlmDefaults.GoogleFallbackModel;

    /// <summary>The fallback can live on another provider; empty means the main provider.</summary>
    public string FallbackProvider { get; init; } = "";
    public string FallbackApiKey { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public double TimeoutSeconds { get; init; } = LlmDefaults.DefaultTimeoutSeconds;
    public bool Enabled { get; init; }
    public string FixPrompt { get; init; } = DefaultPrompts.Fix;
    public string TranslatePrompt { get; init; } = DefaultPrompts.Translate;
    public string TargetLanguage { get; init; } = PromptRenderer.DefaultTargetLanguage;

    /// <summary>True when an online request can be made.</summary>
    public bool Ready =>
        Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model) && LlmDefaults.Providers.Contains(Provider);
}
