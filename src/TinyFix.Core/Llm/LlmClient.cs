// Calls a main endpoint, then an optional fallback, over Gemini, OpenAI-compatible chat, or Anthropic Messages.
// Never logs or puts into an exception message the API key or the user's text.

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

/// <summary>Sends prompts to the configured endpoints.</summary>
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
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.Scheme == "http" && host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new LlmException(LlmErrorKind.Config, "HTTP base URLs are only allowed for localhost. Use HTTPS for a remote provider.");
    }

    /// <summary>Sends <paramref name="prompt"/> and returns the cleaned answer, using the fallback endpoint when needed.</summary>
    /// <exception cref="LlmException">The request failed.</exception>
    public async Task<string> CompleteAsync(LlmConfig config, string prompt, TimeSpan? timeout = null, CancellationToken ct = default) =>
        (await CompleteWithSourceAsync(config, prompt, timeout, ct).ConfigureAwait(false)).Text;

    /// <summary>Like <see cref="CompleteAsync"/>, also telling which model answered.</summary>
    public async Task<LlmResult> CompleteWithSourceAsync(LlmConfig config, string prompt, TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.Main.Model))
            throw new LlmException(LlmErrorKind.Config, "A model name is required");
        if (!LlmProtocols.All.Contains(config.Main.Protocol) && config.Main.Protocol != LlmDefaults.ProviderOpenAi)
            throw new LlmException(LlmErrorKind.Config, $"Unknown provider: {config.Main.Protocol}");
        var mainBase = EffectiveBase(config.Main);
        if (LlmProtocols.RequiresKey(config.Main.Protocol, mainBase) && string.IsNullOrWhiteSpace(config.Main.ApiKey))
            throw new LlmException(LlmErrorKind.Config, "An API key is required");
        return await CompleteWithFallbackAsync(config, prompt, timeout, ct).ConfigureAwait(false);
    }

    private async Task<LlmResult> CompleteWithFallbackAsync(LlmConfig config, string prompt, TimeSpan? timeout, CancellationToken ct)
    {
        var backup = LlmConfigResolver.UsableFallback(config);
        var mainWait = timeout ?? Span(config.Main.TimeoutSeconds);
        if (backup is null)
            return await AttemptAsync(config.Main, prompt, mainWait, 'M', ct).ConfigureAwait(false);

        var main = config.Main.Model.Trim();
        var key = $"{config.Main.Protocol}:{main}";
        bool inWindow;
        lock (_gate)
            inWindow = _fallbackUntil.TryGetValue(key, out var until) && until > _time.GetUtcNow();
        var backupWait = timeout ?? Span(backup.TimeoutSeconds);
        if (inWindow)
        {
            try
            {
                return await AttemptAsync(backup, prompt, backupWait, 'F', ct).ConfigureAwait(false);
            }
            catch (LlmException)
            {
                return await AttemptAsync(config.Main, prompt, mainWait, 'M', ct).ConfigureAwait(false);
            }
        }
        try
        {
            return await AttemptAsync(config.Main, prompt, mainWait, 'M', ct).ConfigureAwait(false);
        }
        catch (LlmException ex) when (ex.FallbackWorthy)
        {
            lock (_gate)
                _fallbackUntil[key] = _time.GetUtcNow() + LlmDefaults.FallbackWindow;
            try
            {
                return await AttemptAsync(backup, prompt, backupWait, 'F', ct).ConfigureAwait(false);
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

    private async Task<LlmResult> AttemptAsync(LlmEndpoint endpoint, string prompt, TimeSpan timeout, char letter, CancellationToken ct)
    {
        var text = endpoint.Protocol switch
        {
            LlmProtocols.Google => await CompleteGeminiAsync(endpoint, prompt, timeout, ct).ConfigureAwait(false),
            LlmProtocols.Anthropic => await CompleteAnthropicAsync(endpoint, prompt, timeout, ct).ConfigureAwait(false),
            _ => await CompleteOpenAiAsync(endpoint, prompt, timeout, ct).ConfigureAwait(false),
        };
        return new LlmResult(text, IsLocal(endpoint) ? 'L' : letter);
    }

    private static bool IsLocal(LlmEndpoint endpoint) =>
        LlmProtocols.IsLocalPreset(endpoint.Protocol) || LlmProtocols.IsLoopback(EffectiveBase(endpoint));

    private static string EffectiveBase(LlmEndpoint endpoint) =>
        string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? LlmProtocols.DefaultBaseUrl(endpoint.Protocol) : endpoint.BaseUrl.Trim();

    private static TimeSpan Span(double seconds) =>
        TimeSpan.FromSeconds(seconds > 0 ? seconds : LlmDefaults.DefaultTimeoutSeconds);

    private async Task<string> CompleteGeminiAsync(LlmEndpoint endpoint, string prompt, TimeSpan timeout, CancellationToken ct)
    {
        var model = (string.IsNullOrWhiteSpace(endpoint.Model) ? LlmDefaults.GoogleModel : endpoint.Model).Trim();
        if (model.StartsWith("models/", StringComparison.Ordinal))
            model = model["models/".Length..];
        var baseUrl = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? LlmDefaults.GeminiBase : endpoint.BaseUrl.Trim().TrimEnd('/');
        AssertSafeBaseUrl(baseUrl);
        var url = $"{baseUrl}/models/{Uri.EscapeDataString(model)}:generateContent";
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
        using var doc = await SendAsync(HttpMethod.Post, url, body, GeminiHeaders(endpoint.ApiKey), timeout, endpoint.ApiKey, ct).ConfigureAwait(false);
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

    private async Task<string> CompleteOpenAiAsync(LlmEndpoint endpoint, string prompt, TimeSpan timeout, CancellationToken ct)
    {
        var protocol = endpoint.Protocol == LlmDefaults.ProviderOpenAi ? LlmProtocols.Custom : endpoint.Protocol;
        var rawBase = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? LlmProtocols.DefaultBaseUrl(protocol) : endpoint.BaseUrl;
        if (protocol == LlmProtocols.Custom && string.IsNullOrWhiteSpace(rawBase))
            throw new LlmException(LlmErrorKind.Config, "A base URL is required");
        var baseUrl = protocol == LlmProtocols.OpenRouter && string.IsNullOrWhiteSpace(endpoint.BaseUrl)
            ? LlmDefaults.OpenRouterBase
            : NormalizeOpenAiBase(rawBase);
        var url = $"{baseUrl}/chat/completions";
        var model = string.IsNullOrWhiteSpace(endpoint.Model) ? LlmProtocols.DefaultModel(protocol) : endpoint.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new LlmException(LlmErrorKind.Config, "A model name is required");
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
        using var doc = await SendAsync(HttpMethod.Post, url, body, BearerHeaders(endpoint.ApiKey), timeout, endpoint.ApiKey, ct).ConfigureAwait(false);
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

    private async Task<string> CompleteAnthropicAsync(LlmEndpoint endpoint, string prompt, TimeSpan timeout, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? LlmProtocols.AnthropicBase : endpoint.BaseUrl.Trim().TrimEnd('/');
        AssertSafeBaseUrl(baseUrl);
        string url;
        if (baseUrl.EndsWith("/messages", StringComparison.Ordinal))
            url = baseUrl;
        else if (baseUrl.EndsWith("/v1", StringComparison.Ordinal))
            url = baseUrl + "/messages";
        else
            url = baseUrl + "/v1/messages";
        var model = string.IsNullOrWhiteSpace(endpoint.Model) ? LlmProtocols.AnthropicModel : endpoint.Model.Trim();
        var body = BuildJson(w =>
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("max_tokens", 4096);
            w.WriteNumber("temperature", 0.1);
            w.WriteStartArray("messages");
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", prompt);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        });
        using var doc = await SendAsync(HttpMethod.Post, url, body, AnthropicHeaders(endpoint.ApiKey), timeout, endpoint.ApiKey, ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                    continue;
                if (block.TryGetProperty("type", out var type) && type.GetString() != "text")
                    continue;
                if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    sb.Append(text.GetString());
            }
        }
        if (string.IsNullOrWhiteSpace(sb.ToString()))
            throw new LlmException(LlmErrorKind.EmptyResponse, "Anthropic returned empty text");
        return PromptRenderer.StripModelOutput(sb.ToString());
    }

    /// <summary>Lists the model ids the endpoint offers.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmEndpoint endpoint, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var wait = timeout ?? Span(Math.Max(endpoint.TimeoutSeconds, 5));
        var names = new List<string>();
        switch (endpoint.Protocol)
        {
            case LlmProtocols.Google:
                {
                    var baseUrl = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? LlmDefaults.GeminiBase : endpoint.BaseUrl.Trim().TrimEnd('/');
                    using var doc = await SendAsync(HttpMethod.Get, $"{baseUrl}/models", null, GeminiHeaders(endpoint.ApiKey), wait, endpoint.ApiKey, ct).ConfigureAwait(false);
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
            case LlmProtocols.Anthropic:
                {
                    var baseUrl = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? LlmProtocols.AnthropicBase : endpoint.BaseUrl.Trim().TrimEnd('/');
                    var url = baseUrl.EndsWith("/v1", StringComparison.Ordinal) ? baseUrl + "/models" : baseUrl + "/v1/models";
                    using var doc = await SendAsync(HttpMethod.Get, url, null, AnthropicHeaders(endpoint.ApiKey), wait, endpoint.ApiKey, ct).ConfigureAwait(false);
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
                {
                    var protocol = endpoint.Protocol == LlmDefaults.ProviderOpenAi ? LlmProtocols.Custom : endpoint.Protocol;
                    var rawBase = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? LlmProtocols.DefaultBaseUrl(protocol) : endpoint.BaseUrl;
                    var baseUrl = NormalizeOpenAiBase(rawBase);
                    using var doc = await SendAsync(HttpMethod.Get, $"{baseUrl}/models", null, BearerHeaders(endpoint.ApiKey), wait, endpoint.ApiKey, ct).ConfigureAwait(false);
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
        }
    }

    /// <summary>Sends "Reply with OK" to this one endpoint (no fallback) and returns the model text.</summary>
    public Task<string> ProbeAsync(LlmEndpoint endpoint, CancellationToken ct = default)
    {
        var config = new LlmConfig { Main = endpoint, Fallback = null };
        return CompleteAsync(config, LlmDefaults.ProbePrompt, Span(Math.Max(endpoint.TimeoutSeconds, 8)), ct);
    }

    /// <summary>Sends a tiny request to the main endpoint and returns the model text (the "test connection" button).</summary>
    public Task<string> ProbeConnectionAsync(LlmConfig config, CancellationToken ct = default) =>
        ProbeAsync(config.Main, ct);

    /// <summary>Runs a skill prompt. There is no other engine.</summary>
    /// <exception cref="LlmException">No usable model is configured, or the request failed. The message never contains an API key.</exception>
    public async Task<LlmResult> RunSkillAsync(LlmConfig? config, string template, string text,
        IReadOnlyDictionary<string, string>? variables = null, CancellationToken ct = default)
    {
        if (config is null || !config.Ready)
            throw new LlmException(LlmErrorKind.Config, "No AI model is configured");
        var prompt = PromptRenderer.Render(template, text, variables);
        var seconds = Math.Max(config.Main.TimeoutSeconds, config.Fallback?.TimeoutSeconds ?? 0);
        var wait = TimeSpan.FromSeconds(Math.Max(seconds, LlmDefaults.SkillMinTimeoutSeconds));
        var result = await CompleteWithSourceAsync(config, prompt, wait, ct).ConfigureAwait(false);
        var cleaned = PromptRenderer.StripModelOutput(result.Text);
        if (cleaned.Length == 0)
            throw new LlmException(LlmErrorKind.EmptyResponse, "The model returned empty text");
        return result with { Text = cleaned };
    }

    private static Dictionary<string, string> GeminiHeaders(string apiKey)
    {
        var headers = new Dictionary<string, string> { ["User-Agent"] = LlmDefaults.UserAgent };
        // The header rather than the query string, so the key is less likely to appear in exception URLs.
        if (apiKey.Length > 0)
            headers["x-goog-api-key"] = apiKey;
        return headers;
    }

    private static Dictionary<string, string> BearerHeaders(string apiKey)
    {
        var headers = new Dictionary<string, string> { ["User-Agent"] = LlmDefaults.UserAgent };
        // Local and custom servers often need no key.
        if (apiKey.Length > 0)
            headers["Authorization"] = $"Bearer {apiKey}";
        return headers;
    }

    private static Dictionary<string, string> AnthropicHeaders(string apiKey)
    {
        var headers = new Dictionary<string, string>
        {
            ["anthropic-version"] = LlmProtocols.AnthropicVersion,
            ["User-Agent"] = LlmDefaults.UserAgent,
        };
        if (apiKey.Length > 0)
            headers["x-api-key"] = apiKey;
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
