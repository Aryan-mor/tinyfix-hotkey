// One LLM endpoint (protocol, model, base URL, key, timeout) and the pair a request tries: main, then fallback.
// Presets only choose a protocol and a default URL; the user can edit both. Secrets never live in these settings types.

using TinyFix.Core.Prompts;

namespace TinyFix.Core.Llm;

/// <summary>Provider ids, default model names and request limits. The Google and OpenRouter model names match the previous app.</summary>
public static class LlmDefaults
{
    /// <summary>Legacy id of the OpenAI-compatible provider, still used as a secret-store account.</summary>
    public const string ProviderOpenAi = "openai_compatible";

    public const string ProviderGoogle = "google";
    public const string ProviderOpenRouter = "openrouter";

    /// <summary>The provider ids the previous settings page offered. New protocols live on <see cref="LlmProtocols"/>.</summary>
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

    /// <summary>The sample the "test connection" button sends. It is not the user's text.</summary>
    public const string ProbePrompt = "Reply with OK";

    /// <summary>The model to use when a Google setting is empty.</summary>
    public static string ResolveGoogleModel(string? value) => string.IsNullOrWhiteSpace(value) ? GoogleModel : value.Trim();

    /// <summary>The default main model of a legacy provider id.</summary>
    public static string DefaultModelFor(string provider) => provider switch
    {
        ProviderGoogle => GoogleModel,
        ProviderOpenRouter => OpenRouterModel,
        _ => OpenAiModel,
    };
}

/// <summary>Protocol presets. Each one only fills a default base URL; the base URL and the model id stay editable.</summary>
public static class LlmProtocols
{
    public const string Google = "google";
    public const string OpenRouter = "openrouter";
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";
    public const string Ollama = "ollama";
    public const string LmStudio = "lmstudio";
    public const string Custom = "custom";

    public const string AnthropicBase = "https://api.anthropic.com";
    public const string OllamaBase = "http://localhost:11434/v1";
    public const string LmStudioBase = "http://localhost:1234/v1";

    /// <summary>Required by the Anthropic Messages API.</summary>
    public const string AnthropicVersion = "2023-06-01";

    public const string AnthropicModel = "claude-sonnet-4-5";
    public const string OllamaModel = "llama3.2";
    public const string LmStudioModel = "local-model";

    /// <summary>Presets in the order the endpoint editor lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Google, OpenRouter, OpenAi, Anthropic, Ollama, LmStudio, Custom];

    /// <summary>A known preset, or <see cref="Google"/> when the text is not one.</summary>
    public static string Normalize(string? protocol)
    {
        var value = (protocol ?? "").Trim();
        if (All.Contains(value))
            return value;
        // The previous app stored every OpenAI-compatible server under one id. URL-aware mapping is MapLegacy.
        return value == LlmDefaults.ProviderOpenAi ? OpenAi : Google;
    }

    /// <summary>True when this preset talks to a server on this computer and needs no key.</summary>
    public static bool IsLocalPreset(string protocol) => protocol is Ollama or LmStudio;

    /// <summary>The base URL a preset starts with. Custom starts empty.</summary>
    public static string DefaultBaseUrl(string protocol) => Normalize(protocol) switch
    {
        Google => LlmDefaults.GeminiBase,
        OpenRouter => LlmDefaults.OpenRouterBase,
        OpenAi => LlmDefaults.OpenAiBase,
        Anthropic => AnthropicBase,
        Ollama => OllamaBase,
        LmStudio => LmStudioBase,
        _ => "",
    };

    /// <summary>The model id a preset starts with. Custom starts empty.</summary>
    public static string DefaultModel(string protocol) => Normalize(protocol) switch
    {
        Google => LlmDefaults.GoogleModel,
        OpenRouter => LlmDefaults.OpenRouterModel,
        OpenAi => LlmDefaults.OpenAiModel,
        Anthropic => AnthropicModel,
        Ollama => OllamaModel,
        LmStudio => LmStudioModel,
        _ => "",
    };

    /// <summary>The fallback model a preset starts with, or empty when the preset has no paired default.</summary>
    public static string DefaultFallbackModel(string protocol) => Normalize(protocol) switch
    {
        Google => LlmDefaults.GoogleFallbackModel,
        OpenRouter => LlmDefaults.OpenRouterFallbackModel,
        _ => "",
    };

    /// <summary>
    /// Maps a provider id from an older settings file. <paramref name="baseUrl"/> tells an OpenAI-compatible server
    /// apart from Ollama, LM Studio and the official OpenAI API.
    /// </summary>
    public static string MapLegacy(string? provider, string? baseUrl)
    {
        var value = (provider ?? "").Trim();
        if (value.Length == 0)
            return Google;
        if (All.Contains(value))
            return value;
        if (value != LlmDefaults.ProviderOpenAi)
            return Custom;
        if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri))
            return OpenAi;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" or "::1")
        {
            return uri.Port switch
            {
                11434 => Ollama,
                1234 => LmStudio,
                _ => Custom,
            };
        }
        if (host == "api.openai.com")
            return OpenAi;
        if (host is "openrouter.ai" or "www.openrouter.ai")
            return OpenRouter;
        if (host == "api.anthropic.com")
            return Anthropic;
        return Custom;
    }

    /// <summary>True when a request to this endpoint cannot succeed without a key. Local presets and Custom never require one.</summary>
    public static bool RequiresKey(string protocol, string? baseUrl)
    {
        if (IsLocalPreset(protocol) || protocol == Custom || IsLoopback(baseUrl))
            return false;
        return protocol is Google or OpenRouter or OpenAi or Anthropic || protocol == LlmDefaults.ProviderOpenAi;
    }

    /// <summary>True when <paramref name="url"/> points at this computer.</summary>
    public static bool IsLoopback(string? url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri))
            return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return host is "localhost" or "127.0.0.1" or "::1";
    }
}

/// <summary>How a skill chooses its model: the global settings, a stronger preset, or its own endpoints.</summary>
public static class ModelModes
{
    public const string Default = "default";
    public const string Better = "better";
    public const string Custom = "custom";

    /// <summary>The valid values.</summary>
    public static IReadOnlyList<string> All { get; } = [Default, Better, Custom];
}

/// <summary>What one call needs. The API key is filled in from the secret store and is never written to settings.json.</summary>
public sealed record LlmEndpoint
{
    public string Protocol { get; init; } = LlmProtocols.Google;
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = LlmDefaults.GoogleModel;
    public string BaseUrl { get; init; } = "";

    /// <summary>Seconds to wait. Zero means <see cref="LlmDefaults.DefaultTimeoutSeconds"/>.</summary>
    public double TimeoutSeconds { get; init; } = LlmDefaults.DefaultTimeoutSeconds;

    /// <summary>
    /// True when a request can be made: a model id, a base URL when the protocol has none of its own, and an API key
    /// when <see cref="LlmProtocols.RequiresKey"/> says so. The key is never logged from here.
    /// </summary>
    public bool IsUsable()
    {
        if (string.IsNullOrWhiteSpace(Model))
            return false;
        if (!LlmProtocols.All.Contains(Protocol) && Protocol != LlmDefaults.ProviderOpenAi)
            return false;
        var protocol = LlmProtocols.Normalize(Protocol);
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? LlmProtocols.DefaultBaseUrl(protocol) : BaseUrl.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;
        return !LlmProtocols.RequiresKey(protocol, baseUrl) || !string.IsNullOrWhiteSpace(ApiKey);
    }
}

/// <summary>The main endpoint and an optional fallback. The main one is tried first.</summary>
public sealed record LlmConfig
{
    public LlmEndpoint Main { get; init; } = new();
    public LlmEndpoint? Fallback { get; init; }
    public string FixPrompt { get; init; } = DefaultPrompts.Fix;
    public string TranslatePrompt { get; init; } = DefaultPrompts.Translate;

    /// <summary>True when the main endpoint can answer. A configured model is used; there is no separate on/off switch.</summary>
    public bool Ready => Main.IsUsable();
}

/// <summary>Accounts the secret store is asked for. Provider ids keep already saved keys working; slot ids hold a second key.</summary>
public static class LlmSecrets
{
    public const string MainSlot = "main";
    public const string FallbackSlot = "fallback";

    /// <summary>Secret-store account of a skill's main endpoint.</summary>
    public static string SkillMain(string skillId) => "skill:" + skillId + ":main";

    /// <summary>Secret-store account of a skill's fallback endpoint.</summary>
    public static string SkillFallback(string skillId) => "skill:" + skillId + ":fallback";

    /// <summary>Accounts <see cref="Platform.ISecretStore.GetAll"/> should look up (Linux and macOS have no "list" call).</summary>
    public static IReadOnlyList<string> Accounts { get; } =
    [
        .. LlmProtocols.All,
        .. LlmDefaults.Providers.Where(provider => !LlmProtocols.All.Contains(provider)),
        MainSlot,
        FallbackSlot,
    ];

    /// <summary>
    /// The key for an endpoint: the slot's own key, else the provider's key, else the legacy <c>openai_compatible</c> key.
    /// An empty string when nothing is saved. The value is never logged.
    /// </summary>
    public static string Resolve(IReadOnlyDictionary<string, string> keys, string? slot, string protocol)
    {
        if (!string.IsNullOrEmpty(slot) && Try(keys, slot, out var slotKey))
            return slotKey;
        if (Try(keys, protocol, out var providerKey))
            return providerKey;
        if (protocol is LlmProtocols.OpenAi or LlmProtocols.Custom && Try(keys, LlmDefaults.ProviderOpenAi, out var legacy))
            return legacy;
        return "";
    }

    private static bool Try(IReadOnlyDictionary<string, string> keys, string name, out string value)
    {
        if (keys.TryGetValue(name, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found.Trim();
            return true;
        }
        value = "";
        return false;
    }
}
