// Calls Google Gemini or an OpenAI-compatible chat API with a short timeout and a model fallback.
// Ported from llm_client.py. Never logs or puts into an exception message the API key or the user's text.

using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using TinyFix.Core.Prompts;

namespace TinyFix.Core.Llm;

/// <summary>The answer of a request and which model gave it.</summary>
/// <param name="Source">M main model, F fallback model, L local server.</param>
public readonly record struct LlmResult(string Text, char Source);

/// <summary>Sends prompts to the configured provider.</summary>
public sealed partial class LlmClient
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, DateTimeOffset> _fallbackUntil = [];
    private readonly Lock _gate = new();

    /// <summary>Creates a client over <paramref name="http"/> (tests pass a fake handler).</summary>
    public LlmClient(HttpClient http, TimeProvider? time = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
    }

    [GeneratedRegex("(key=|api_key=|Bearer )[^ &\"'<>\\n]*", RegexOptions.IgnoreCase)]
    private static partial Regex KeyLike();

    /// <summary>Removes the key and key-like query parameters from an error text.</summary>
    public static string SanitizeError(string message, string secret = "")
    {
        var text = message;
        if (secret.Length > 0)
            text = text.Replace(secret, "***", StringComparison.Ordinal);
        // Belt and braces for key-like parameters if a URL leaked into the error.
        return KeyLike().Replace(text, "$1***");
    }

    /// <summary>Normalizes an OpenAI-compatible base URL and refuses plain HTTP for remote hosts.</summary>
    /// <exception cref="LlmException">The URL is unsafe or malformed.</exception>
    public static string NormalizeOpenAiBase(string? url)
    {
        var baseUrl = (url ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            return LlmDefaults.OpenAiBase;
        AssertSafeBaseUrl(baseUrl);
        const string chat = "/chat/completions";
        if (baseUrl.EndsWith(chat, StringComparison.Ordinal))
            baseUrl = baseUrl[..^chat.Length];
        // Ollama and LM Studio sometimes expose /v1 already; add it when the user left it out.
        if (!baseUrl.EndsWith("/v1", StringComparison.Ordinal) && !(baseUrl + "/").Contains("/v1/", StringComparison.Ordinal))
            baseUrl += "/v1";
        return baseUrl;
    }

    private static void AssertSafeBaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new LlmException(LlmErrorKind.Config, "The base URL must start with https:// (or http:// for localhost).");
        var host = uri.Host.ToLowerInvariant();
        if (uri.Scheme == "http" && host is not ("127.0.0.1" or "localhost" or "::1" or "[::1]"))
            throw new LlmException(LlmErrorKind.Config, "HTTP base URLs are only allowed for localhost. Use HTTPS for a remote provider.");
    }

    /// <summary>Sends <paramref name="prompt"/> and returns the cleaned answer, using the fallback model when needed.</summary>
    /// <exception cref="LlmException">The request failed.</exception>
    public async Task<string> CompleteAsync(LlmConfig config, string prompt, TimeSpan? timeout = null, CancellationToken ct = default) =>
        (await CompleteWithSourceAsync(config, prompt, timeout, ct).ConfigureAwait(false)).Text;

    /// <summary>Like <see cref="CompleteAsync"/>, also telling which model answered.</summary>
    public async Task<LlmResult> CompleteWithSourceAsync(LlmConfig config, string prompt, TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(config.TimeoutSeconds);
        if (wait <= TimeSpan.Zero)
            wait = TimeSpan.FromSeconds(LlmDefaults.DefaultTimeoutSeconds);
        if (string.IsNullOrWhiteSpace(config.ApiKey) && config.Provider is LlmDefaults.ProviderGoogle or LlmDefaults.ProviderOpenRouter)
            throw new LlmException(LlmErrorKind.Config, "An API key is required");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new LlmException(LlmErrorKind.Config, "A model name is required");
        if (!LlmDefaults.Providers.Contains(config.Provider))
            throw new LlmException(LlmErrorKind.Config, $"Unknown provider: {config.Provider}");
        return await CompleteWithFallbackAsync(config, prompt, wait, ct).ConfigureAwait(false);
    }

    private async Task<LlmResult> CompleteWithFallbackAsync(LlmConfig config, string prompt, TimeSpan timeout, CancellationToken ct)
    {
        var backupConfig = LlmConfigResolver.Fallback(config);
        if (backupConfig is null)
            return await AttemptAsync(config, prompt, timeout, 'M', ct).ConfigureAwait(false);

        var main = (string.IsNullOrWhiteSpace(config.Model) ? LlmDefaults.DefaultModelFor(config.Provider) : config.Model).Trim();
        var key = $"{config.Provider}:{main}";
        bool inWindow;
        lock (_gate)
            inWindow = _fallbackUntil.TryGetValue(key, out var until) && until > _time.GetUtcNow();
        if (inWindow)
        {
            try
            {
                return await AttemptAsync(backupConfig, prompt, timeout, 'F', ct).ConfigureAwait(false);
            }
            catch (LlmException)
            {
                return await AttemptAsync(config, prompt, timeout, 'M', ct).ConfigureAwait(false);
            }
        }
        try
        {
            return await AttemptAsync(config, prompt, timeout, 'M', ct).ConfigureAwait(false);
        }
        catch (LlmException ex) when (ex.FallbackWorthy)
        {
            lock (_gate)
                _fallbackUntil[key] = _time.GetUtcNow() + LlmDefaults.FallbackWindow;
            try
            {
                return await AttemptAsync(backupConfig, prompt, timeout, 'F', ct).ConfigureAwait(false);
            }
            catch (LlmException)
            {
                lock (_gate)
                    _fallbackUntil.Remove(key);
                // The caller sees why the main model failed, not the fallback's error.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }
        }
    }

    private async Task<LlmResult> AttemptAsync(LlmConfig config, string prompt, TimeSpan timeout, char letter, CancellationToken ct)
    {
        var text = config.Provider == LlmDefaults.ProviderGoogle
            ? await CompleteGeminiAsync(config, prompt, timeout, ct).ConfigureAwait(false)
            : await CompleteOpenAiAsync(config, prompt, timeout, ct).ConfigureAwait(false);
        return new LlmResult(text, IsLocal(config) ? 'L' : letter);
    }

    private static bool IsLocal(LlmConfig config) =>
        config.Provider == LlmDefaults.ProviderOpenAi && Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var uri) &&
        uri.Host.ToLowerInvariant() is "127.0.0.1" or "localhost" or "::1" or "[::1]";

    private async Task<string> CompleteGeminiAsync(LlmConfig config, string prompt, TimeSpan timeout, CancellationToken ct)
    {
        var model = (string.IsNullOrWhiteSpace(config.Model) ? LlmDefaults.GoogleModel : config.Model).Trim();
        if (model.StartsWith("models/", StringComparison.Ordinal))
            model = model["models/".Length..];
        var url = $"{LlmDefaults.GeminiBase}/models/{model}:generateContent";
        var body = BuildJson(w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("contents");
            w.WriteStartObject();
            w.WriteStartArray("parts");
            w.WriteStartObject();
            w.WriteString("text", prompt);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteStartObject("generationConfig");
            w.WriteNumber("temperature", 0.1);
            w.WriteEndObject();
            w.WriteEndObject();
        });
        using var doc = await SendAsync(HttpMethod.Post, url, body, GeminiHeaders(config.ApiKey), timeout, config.ApiKey, ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
            }
        }
        else
        {
            throw new LlmException(LlmErrorKind.EmptyResponse, "Gemini returned no candidates");
        }
        if (string.IsNullOrWhiteSpace(sb.ToString()))
            throw new LlmException(LlmErrorKind.EmptyResponse, "Gemini returned empty text");
        return PromptRenderer.StripModelOutput(sb.ToString());
    }

    private async Task<string> CompleteOpenAiAsync(LlmConfig config, string prompt, TimeSpan timeout, CancellationToken ct)
    {
        var baseUrl = config.Provider == LlmDefaults.ProviderOpenRouter ? LlmDefaults.OpenRouterBase : NormalizeOpenAiBase(config.BaseUrl);
        var url = $"{baseUrl}/chat/completions";
        var model = string.IsNullOrWhiteSpace(config.Model) ? LlmDefaults.OpenAiModel : config.Model;
        var body = BuildJson(w =>
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("temperature", 0.1);
            w.WriteStartArray("messages");
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", prompt);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        });
        using var doc = await SendAsync(HttpMethod.Post, url, body, BearerHeaders(config.ApiKey), timeout, config.ApiKey, ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new LlmException(LlmErrorKind.EmptyResponse, "The model returned no choices");
        string text = "";
        if (choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                text = content.GetString() ?? "";
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                    sb.Append(part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var t) ? t.GetString() : part.ToString());
                text = sb.ToString();
            }
        }
        if (string.IsNullOrWhiteSpace(text))
            throw new LlmException(LlmErrorKind.EmptyResponse, "The model returned empty text");
        return PromptRenderer.StripModelOutput(text);
    }

    /// <summary>Lists the model ids the provider offers for the configured key.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmConfig config, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(Math.Max(config.TimeoutSeconds, 5));
        var names = new List<string>();
        switch (config.Provider)
        {
            case LlmDefaults.ProviderGoogle:
                {
                    using var doc = await SendAsync(HttpMethod.Get, $"{LlmDefaults.GeminiBase}/models", null, GeminiHeaders(config.ApiKey), wait, config.ApiKey, ct).ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in models.EnumerateArray())
                        {
                            if (item.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array &&
                                methods.GetArrayLength() > 0 && !methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
                                continue;
                            var name = item.TryGetProperty("name", out var n) ? n.GetString()?.Trim() ?? "" : "";
                            if (name.StartsWith("models/", StringComparison.Ordinal))
                                name = name["models/".Length..];
                            if (name.Length > 0)
                                names.Add(name);
                        }
                    }
                    return names;
                }
            case LlmDefaults.ProviderOpenRouter:
            case LlmDefaults.ProviderOpenAi:
                {
                    var baseUrl = config.Provider == LlmDefaults.ProviderOpenRouter ? LlmDefaults.OpenRouterBase : NormalizeOpenAiBase(config.BaseUrl);
                    var headers = config.Provider == LlmDefaults.ProviderOpenRouter
                        ? new Dictionary<string, string> { ["User-Agent"] = LlmDefaults.UserAgent }
                        : BearerHeaders(config.ApiKey);
                    using var doc = await SendAsync(HttpMethod.Get, $"{baseUrl}/models", null, headers, wait, config.ApiKey, ct).ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in data.EnumerateArray())
                        {
                            var name = item.TryGetProperty("id", out var id) ? id.GetString()?.Trim() ?? "" : "";
                            if (name.Length > 0)
                                names.Add(name);
                        }
                    }
                    return names;
                }
            default:
                throw new LlmException(LlmErrorKind.Config, $"Unknown provider: {config.Provider}");
        }
    }

    /// <summary>Sends a tiny typo-correction request and returns the model text (the "test connection" button).</summary>
    public Task<string> ProbeConnectionAsync(LlmConfig config, CancellationToken ct = default)
    {
        var prompt = PromptRenderer.Render(DefaultPrompts.Fix, LlmDefaults.TestSample);
        // A little more room than the tight runtime default, unless the user set more.
        return CompleteAsync(config, prompt, TimeSpan.FromSeconds(Math.Max(config.TimeoutSeconds, 8)), ct);
    }

    /// <summary>Runs a skill prompt online. There is no offline fallback for skills.</summary>
    /// <exception cref="LlmException">AI is off or the request failed.</exception>
    public async Task<LlmResult> RunSkillAsync(LlmConfig? config, string template, string text, string? language = null,
        CancellationToken ct = default)
    {
        if (config is null || !config.Ready)
            throw new LlmException(LlmErrorKind.Config, "Turn on an online model in Settings to use this skill");
        var prompt = PromptRenderer.Render(template, text, language);
        var wait = TimeSpan.FromSeconds(Math.Max(config.TimeoutSeconds, LlmDefaults.SkillMinTimeoutSeconds));
        var result = await CompleteWithSourceAsync(config, prompt, wait, ct).ConfigureAwait(false);
        var cleaned = PromptRenderer.StripModelOutput(result.Text);
        if (cleaned.Length == 0)
            throw new LlmException(LlmErrorKind.EmptyResponse, "The model returned empty text");
        return result with { Text = cleaned };
    }

    private static Dictionary<string, string> GeminiHeaders(string apiKey) => new()
    {
        // The header rather than the query string, so the key is less likely to appear in exception URLs.
        ["x-goog-api-key"] = apiKey,
        ["User-Agent"] = LlmDefaults.UserAgent,
    };

    private static Dictionary<string, string> BearerHeaders(string apiKey)
    {
        var headers = new Dictionary<string, string> { ["User-Agent"] = LlmDefaults.UserAgent };
        // Some local servers need no key.
        if (apiKey.Length > 0)
            headers["Authorization"] = $"Bearer {apiKey}";
        return headers;
    }

    private static byte[] BuildJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            write(writer);
        return stream.ToArray();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, byte[]? body, Dictionary<string, string> headers,
        TimeSpan timeout, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            using var response = await _http.SendAsync(request, limit.Token).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = raw.Length > 300 ? raw[..300] : raw;
                if (detail.Length == 0)
                    detail = response.ReasonPhrase ?? "";
                throw new LlmException(LlmErrorKind.Http, SanitizeError($"HTTP {(int)response.StatusCode}: {detail}", apiKey), (int)response.StatusCode);
            }
            try
            {
                var doc = JsonDocument.Parse(raw.Length == 0 ? "{}" : raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    doc.Dispose();
                    throw new LlmException(LlmErrorKind.BadPayload, "Provider returned an unexpected payload");
                }
                return doc;
            }
            catch (JsonException ex)
            {
                throw new LlmException(LlmErrorKind.BadPayload, "Provider returned a non-JSON response", inner: ex);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmTimeoutException(SanitizeError($"Timed out after {timeout.TotalSeconds:0.#}s", apiKey));
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.StatusCode is HttpStatusCode code ? code.ToString() : ex.InnerException?.Message ?? ex.GetType().Name;
            throw new LlmException(LlmErrorKind.Network, SanitizeError($"Network error: {reason}", apiKey), inner: ex);
        }
    }
}
