// LLM client tests with a fake HTTP handler, ported from tests/test_llm_client.py and tests/test_llm_fallback.py.

using System.Net;
using System.Text;
using TinyFix.Core.Llm;

namespace TinyFix.Core.Tests;

/// <summary>An HTTP handler that answers from a delegate and records the requests.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(string Url, string Body, string? Auth, string? GoogleKey)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("x-goog-api-key", out var v) ? v.First() : null));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static string GeminiAnswer(string text) => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"" + text + "\"}]}}]}";

    public static string ChatAnswer(string text) => "{\"choices\":[{\"message\":{\"content\":\"" + text + "\"}}]}";
}

/// <summary>A clock tests move by hand.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public class LlmTests
{
    private static LlmConfig Config() => new() { ApiKey = "k", Model = "main", FallbackModel = "backup", Enabled = true };

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Timeout_uses_fallback_then_sticks_for_the_window()
    {
        var calls = new List<string>();
        var handler = new FakeHandler((req, _) =>
        {
            var model = req.RequestUri!.AbsolutePath.Split('/').Last().Split(':')[0];
            calls.Add(model);
            if (model == "main")
                throw new TaskCanceledException("simulated timeout", new TimeoutException());
            return FakeHandler.Json(FakeHandler.GeminiAnswer("ok"));
        });
        var client = new LlmClient(new HttpClient(handler), new ManualTime());
        Assert.Equal("ok", await client.CompleteAsync(Config(), "p", ct: Ct));
        Assert.Equal(["main", "backup"], calls);
        Assert.Equal("ok", await client.CompleteAsync(Config(), "p", ct: Ct));
        Assert.Equal(["main", "backup", "backup"], calls);
    }

    [Fact]
    public async Task Main_model_returns_after_the_window()
    {
        var time = new ManualTime();
        var calls = new List<string>();
        var failMain = true;
        var handler = new FakeHandler((req, _) =>
        {
            var model = req.RequestUri!.AbsolutePath.Split('/').Last().Split(':')[0];
            calls.Add(model);
            return model == "main" && failMain
                ? FakeHandler.Json("{}", HttpStatusCode.TooManyRequests)
                : FakeHandler.Json(FakeHandler.GeminiAnswer("ok"));
        });
        var client = new LlmClient(new HttpClient(handler), time);
        await client.CompleteAsync(Config(), "p", ct: Ct);
        Assert.Equal(["main", "backup"], calls);
        failMain = false;
        time.Advance(LlmDefaults.FallbackWindow + TimeSpan.FromSeconds(1));
        calls.Clear();
        await client.CompleteAsync(Config(), "p", ct: Ct);
        Assert.Equal(["main"], calls);
    }

    [Fact]
    public async Task Bad_key_does_not_fall_back()
    {
        var calls = new List<string>();
        var handler = new FakeHandler((req, _) =>
        {
            calls.Add(req.RequestUri!.AbsolutePath);
            return FakeHandler.Json("{\"error\":\"key invalid\"}", HttpStatusCode.Forbidden);
        });
        var client = new LlmClient(new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Config(), "p", ct: Ct));
        Assert.Equal(403, ex.StatusCode);
        Assert.Single(calls);
    }

    [Fact]
    public void Missing_model_counts_as_fallback_worthy()
    {
        Assert.True(new LlmException(LlmErrorKind.Http, "HTTP 404: model no longer available", 404).FallbackWorthy);
        Assert.False(new LlmException(LlmErrorKind.Http, "HTTP 400: bad request", 400).FallbackWorthy);
        Assert.True(new LlmException(LlmErrorKind.Http, "HTTP 503", 503).FallbackWorthy);
        Assert.True(new LlmException(LlmErrorKind.EmptyResponse, "empty").FallbackWorthy);
    }

    [Fact]
    public async Task Fallback_can_be_another_provider_with_its_own_key()
    {
        var handler = new FakeHandler((req, _) =>
            req.RequestUri!.Host.Contains("googleapis", StringComparison.Ordinal)
                ? throw new TaskCanceledException("t", new TimeoutException())
                : FakeHandler.Json(FakeHandler.ChatAnswer("fixed")));
        var config = new LlmConfig
        {
            Provider = LlmDefaults.ProviderGoogle,
            ApiKey = "g-key",
            Model = "main",
            FallbackModel = "openai/gpt-x",
            FallbackProvider = LlmDefaults.ProviderOpenRouter,
            FallbackApiKey = "or-key",
            Enabled = true,
        };
        var client = new LlmClient(new HttpClient(handler));
        var result = await client.CompleteWithSourceAsync(config, "p", ct: Ct);
        Assert.Equal("fixed", result.Text);
        Assert.Equal('F', result.Source);
        Assert.Equal("g-key", handler.Requests[0].GoogleKey);
        Assert.Equal("Bearer or-key", handler.Requests[1].Auth);
        Assert.StartsWith(LlmDefaults.OpenRouterBase, handler.Requests[1].Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Fallback_on_another_provider_is_skipped_without_its_key()
    {
        var config = new LlmConfig
        {
            Provider = LlmDefaults.ProviderGoogle,
            ApiKey = "g",
            Model = "m",
            FallbackModel = "x",
            FallbackProvider = LlmDefaults.ProviderOpenRouter,
            FallbackApiKey = "",
        };
        Assert.Null(LlmConfigResolver.Fallback(config));
        Assert.Equal(LlmDefaults.ProviderOpenRouter, LlmConfigResolver.Fallback(config with { FallbackApiKey = "k" })!.Provider);
        var same = new LlmConfig { Provider = LlmDefaults.ProviderOpenRouter, ApiKey = "k", Model = "a", FallbackModel = "b" };
        Assert.Equal("b", LlmConfigResolver.Fallback(same)!.Model);
        Assert.Equal("k", LlmConfigResolver.Fallback(same)!.ApiKey);
        Assert.Null(LlmConfigResolver.Fallback(same with { FallbackModel = "a" }));
    }

    [Fact]
    public async Task OpenRouter_uses_its_own_endpoint()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.ChatAnswer("ok")));
        var config = new LlmConfig
        {
            Provider = LlmDefaults.ProviderOpenRouter,
            ApiKey = "sk-or",
            Model = "deepseek/deepseek-v4.1-flash",
            FallbackModel = "",
            Enabled = true,
        };
        Assert.Equal("ok", await new LlmClient(new HttpClient(handler)).CompleteAsync(config, "p", ct: Ct));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.Url);
        Assert.Equal("Bearer sk-or", request.Auth);
        Assert.Contains("\"model\":\"deepseek/deepseek-v4.1-flash\"", request.Body);
    }

    [Fact]
    public async Task OpenAI_compatible_completion_and_test_button()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.ChatAnswer("```\\nHi friends\\n```")));
        var config = new LlmConfig
        {
            Provider = LlmDefaults.ProviderOpenAi,
            ApiKey = "sk-test-key",
            Model = "local-model",
            BaseUrl = "http://127.0.0.1:1234",
            FallbackModel = "",
            Enabled = true,
        };
        var client = new LlmClient(new HttpClient(handler));
        var result = await client.CompleteWithSourceAsync(config, "p", ct: Ct);
        Assert.Equal("Hi friends", result.Text);
        Assert.Equal('L', result.Source);
        Assert.Equal("Hi friends", await client.ProbeConnectionAsync(config, Ct));
        Assert.StartsWith("http://127.0.0.1:1234/v1/chat/completions", handler.Requests[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_error_redacts_key_and_bearer()
    {
        const string key = "sk-secret-12345";
        var text = LlmClient.SanitizeError($"Could not reach https://example.invalid/v1?key={key} Bearer {key}", key);
        Assert.DoesNotContain(key, text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void Remote_http_base_url_is_rejected()
    {
        var ex = Assert.Throws<LlmException>(() => LlmClient.NormalizeOpenAiBase("http://example.invalid/v1"));
        Assert.Contains("localhost", ex.Message);
        Assert.Equal("http://127.0.0.1:1234/v1", LlmClient.NormalizeOpenAiBase("http://127.0.0.1:1234/v1"));
        Assert.Equal("https://openrouter.ai/api/v1", LlmClient.NormalizeOpenAiBase("https://openrouter.ai/api/v1"));
        Assert.Equal("https://api.openai.com/v1", LlmClient.NormalizeOpenAiBase(""));
        Assert.Equal("http://localhost:11434/v1", LlmClient.NormalizeOpenAiBase("http://localhost:11434/chat/completions"));
    }

    [Fact]
    public void Resolve_google_model_keeps_chosen_and_fills_empty()
    {
        Assert.Equal("gemini-3.5-flash-lite", LlmDefaults.GoogleModel);
        Assert.Equal(LlmDefaults.GoogleModel, LlmDefaults.ResolveGoogleModel(""));
        Assert.Equal(LlmDefaults.GoogleModel, LlmDefaults.ResolveGoogleModel("   "));
        Assert.Equal(LlmDefaults.GoogleModel, LlmDefaults.ResolveGoogleModel(null));
        Assert.Equal("gemini-2.0-flash", LlmDefaults.ResolveGoogleModel("gemini-2.0-flash"));
    }

    [Fact]
    public async Task Run_skill_returns_the_cleaned_answer_and_the_model_letter()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.GeminiAnswer("ok")));
        var client = new LlmClient(new HttpClient(handler));
        var run = await client.RunSkillAsync(Config() with { TimeoutSeconds = 2, Enabled = true }, "T: {{content}}", "hi", ct: Ct);
        Assert.Equal(("ok", 'M'), (run.Text, run.Source));
        Assert.Contains("T: hi", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Run_skill_refuses_when_ai_is_off()
    {
        var client = new LlmClient(new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}"))));
        await Assert.ThrowsAsync<LlmException>(() => client.RunSkillAsync(Config() with { Enabled = false }, "t", "x", ct: Ct));
        await Assert.ThrowsAsync<LlmException>(() => client.RunSkillAsync(null, "t", "x", ct: Ct));
    }

    [Fact]
    public void Skill_models_default_better_and_custom()
    {
        var baseConfig = new LlmConfig
        {
            Provider = LlmDefaults.ProviderGoogle,
            ApiKey = "g",
            Model = "gemini-3.5-flash-lite",
            FallbackModel = "gemini-3.6-flash",
            Enabled = true,
        };
        Assert.Same(baseConfig, LlmConfigResolver.ForSkill(baseConfig, ModelModes.Default));
        var better = LlmConfigResolver.ForSkill(baseConfig, ModelModes.Better);
        Assert.Equal(LlmConfigResolver.BetterModels[LlmDefaults.ProviderGoogle], (better.Model, better.FallbackModel));
        Assert.Equal("g", better.ApiKey);
        Assert.Equal("", better.FallbackProvider);
        var plain = new LlmConfig { Provider = LlmDefaults.ProviderOpenAi, ApiKey = "k", Model = "m", Enabled = true };
        Assert.Same(plain, LlmConfigResolver.ForSkill(plain, ModelModes.Better));

        var custom = new SkillModelChoice(LlmDefaults.ProviderOpenRouter, "anthropic/claude-sonnet-5.5", LlmDefaults.ProviderGoogle, "gemini-3.5-flash-lite");
        var keys = new Dictionary<string, string> { ["openrouter"] = "or", ["google"] = "g" };
        var cfg = LlmConfigResolver.ForSkill(baseConfig, ModelModes.Custom, custom, keys);
        Assert.Equal((LlmDefaults.ProviderOpenRouter, "or", "anthropic/claude-sonnet-5.5"), (cfg.Provider, cfg.ApiKey, cfg.Model));
        Assert.Equal((LlmDefaults.ProviderGoogle, "g", "gemini-3.5-flash-lite"), (cfg.FallbackProvider, cfg.FallbackApiKey, cfg.FallbackModel));
        // No key for the chosen provider: use the Online AI settings instead of failing.
        Assert.Same(baseConfig, LlmConfigResolver.ForSkill(baseConfig, ModelModes.Custom, custom, new Dictionary<string, string>()));
        // Empty custom fields mean "use the global ones".
        Assert.Equal("gemini-3.5-flash-lite", LlmConfigResolver.ForSkill(baseConfig, ModelModes.Custom, new SkillModelChoice()).Model);
    }
}
