// Usage tracking: what the client reads from each provider's answer, what it reports for a failure, and what the ledger,
// the price list and the summary make of it.

using System.Net;
using TinyFix.Core.Llm;
using TinyFix.Core.Usage;

namespace TinyFix.Core.Tests;

public class UsageTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static LlmEndpoint Endpoint(string protocol, string model) => new() { Protocol = protocol, ApiKey = "k", Model = model };

    private static async Task<List<LlmAttempt>> Run(LlmConfig config, FakeHandler handler)
    {
        var client = new LlmClient(new HttpClient(handler));
        var seen = new List<LlmAttempt>();
        client.Attempted += seen.Add;
        try
        {
            await client.CompleteAsync(config, "hi", ct: Ct);
        }
        catch (LlmException)
        {
            // The attempts are what the test looks at.
        }
        return seen;
    }

    [Fact]
    public async Task Gemini_usage_counts_thinking_tokens_as_output()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]}}],\"usageMetadata\":{\"promptTokenCount\":457,\"candidatesTokenCount\":10,\"thoughtsTokenCount\":5}}"));
        var attempt = Assert.Single(await Run(new LlmConfig { Main = Endpoint(LlmProtocols.Google, "models/gemini-x") }, handler));
        Assert.True(attempt.Success);
        Assert.Equal('M', attempt.Role);
        Assert.Equal("gemini-x", attempt.Model);
        Assert.Equal(457, attempt.InputTokens);
        Assert.Equal(15, attempt.OutputTokens);
        Assert.Null(attempt.Cost);
    }

    [Fact]
    public async Task OpenRouter_reports_the_exact_cost()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(
            "{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":456,\"completion_tokens\":3,\"cost\":0.0001443}}"));
        var attempt = Assert.Single(await Run(new LlmConfig { Main = Endpoint(LlmProtocols.OpenRouter, "google/gemini-3.5-flash-lite") }, handler));
        Assert.Equal(456, attempt.InputTokens);
        Assert.Equal(3, attempt.OutputTokens);
        Assert.Equal(0.0001443m, attempt.Cost);
    }

    [Fact]
    public async Task Anthropic_usage_includes_cached_input()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(
            "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"usage\":{\"input_tokens\":10,\"cache_read_input_tokens\":90,\"output_tokens\":7}}"));
        var attempt = Assert.Single(await Run(new LlmConfig { Main = Endpoint(LlmProtocols.Anthropic, "claude-sonnet-5.5") }, handler));
        Assert.Equal(100, attempt.InputTokens);
        Assert.Equal(7, attempt.OutputTokens);
    }

    [Fact]
    public async Task An_answer_without_usage_counts_zero_tokens()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.ChatAnswer("ok")));
        var attempt = Assert.Single(await Run(new LlmConfig { Main = Endpoint(LlmProtocols.OpenRouter, "m") }, handler));
        Assert.True(attempt.Success);
        Assert.Equal(0, attempt.InputTokens);
    }

    [Fact]
    public async Task A_failed_main_and_the_fallback_that_answered_are_both_reported_with_the_reason()
    {
        var handler = new FakeHandler((req, _) => req.RequestUri!.Host.Contains("google", StringComparison.Ordinal)
            ? FakeHandler.Json("{}", HttpStatusCode.ServiceUnavailable)
            : FakeHandler.Json(FakeHandler.ChatAnswer("ok")));
        var config = new LlmConfig { Main = Endpoint(LlmProtocols.Google, "main"), Fallback = Endpoint(LlmProtocols.OpenRouter, "backup") };
        var attempts = await Run(config, handler);
        Assert.Collection(attempts,
            main =>
            {
                Assert.Equal('M', main.Role);
                Assert.False(main.Success);
                Assert.Equal("HTTP 503", main.Error);
            },
            fallback =>
            {
                Assert.Equal('F', fallback.Role);
                Assert.True(fallback.Success);
            });
    }

    [Fact]
    public async Task A_configuration_error_makes_no_request_and_reports_nothing()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        var config = new LlmConfig { Main = Endpoint(LlmProtocols.Google, "") };
        Assert.Empty(await Run(config, handler));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_listener_that_throws_cannot_break_a_request()
    {
        var client = new LlmClient(new HttpClient(new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.GeminiAnswer("ok")))));
        client.Attempted += _ => throw new InvalidOperationException("boom");
        Assert.Equal("ok", await client.CompleteAsync(new LlmConfig { Main = Endpoint(LlmProtocols.Google, "m") }, "hi", ct: Ct));
    }

    [Theory]
    [InlineData("google/gemini-3.5-flash-lite", "gemini-3-5-flash-lite")]
    [InlineData("models/Gemini-3.5-Flash-Lite", "gemini-3-5-flash-lite")]
    [InlineData("claude-sonnet-4-5-20250929", "claude-sonnet-4-5")]
    [InlineData("anthropic/claude-sonnet-4.5", "claude-sonnet-4-5")]
    [InlineData("google/gemini-3.5-flash-lite:batch", "gemini-3-5-flash-lite")]
    public void Model_ids_from_every_provider_find_the_same_price(string model, string key) =>
        Assert.Equal(key, UsagePricing.Key(model));

    private static LlmAttempt Attempt(string protocol, string model, char role = 'M', bool ok = true, string error = "",
        long input = 0, long output = 0, decimal? cost = null, DateTimeOffset? at = null) =>
        new(at ?? DateTimeOffset.UtcNow, protocol, model, role, ok, error, 100, input, output, cost);

    [Fact]
    public void The_reported_cost_wins_over_the_price_list()
    {
        var pricing = new UsagePricing();
        Assert.Equal(0.5m, pricing.CostOf(Attempt("openrouter", "google/gemini-3.5-flash-lite", input: 1_000_000, cost: 0.5m)));
    }

    [Fact]
    public void Tokens_are_priced_per_million_and_unknown_models_are_not_guessed()
    {
        var pricing = new UsagePricing();
        // 1M in at 0.30 plus 1M out at 2.50.
        Assert.Equal(2.8m, pricing.CostOf(Attempt("google", "gemini-3.5-flash-lite", input: 1_000_000, output: 1_000_000)));
        Assert.Null(pricing.CostOf(Attempt("google", "some-new-model", input: 10, output: 10)));
        Assert.Equal(0m, pricing.CostOf(Attempt("google", "some-new-model", ok: false, error: "Timeout")));
    }

    [Fact]
    public void Local_servers_cost_nothing()
    {
        Assert.Equal(0m, new UsagePricing().CostOf(Attempt("ollama", "llama3.2", role: 'L', input: 500, output: 500)));
    }

    [Fact]
    public void The_users_price_file_adds_and_replaces_prices()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, UsagePricing.FileName);
            File.WriteAllText(path, "{ \"my-model\": { \"input\": 1, \"output\": 2 }, \"gemini-3.5-flash-lite\": { \"input\": 10, \"output\": 20 }, \"broken\": 5 }");
            var pricing = UsagePricing.Load(path);
            Assert.Equal(3m, pricing.CostOf(Attempt("custom", "vendor/my-model", input: 1_000_000, output: 1_000_000)));
            Assert.Equal(30m, pricing.CostOf(Attempt("google", "gemini-3.5-flash-lite", input: 1_000_000, output: 1_000_000)));
            File.WriteAllText(path, "not json");
            Assert.Null(UsagePricing.Load(path).CostOf(Attempt("custom", "my-model", input: 1, output: 1)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_price_file_template_loads_back_to_the_built_in_prices()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, UsagePricing.FileName);
            File.WriteAllText(path, UsagePricing.Template());
            Assert.Equal(2.8m, UsagePricing.Load(path).CostOf(Attempt("google", "gemini-3.5-flash-lite", input: 1_000_000, output: 1_000_000)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_ledger_keeps_attempts_across_restarts_skips_damage_and_can_be_cleared()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ledger = new UsageLedger(dir);
            var raised = 0;
            ledger.Recorded += (_, _) => raised++;
            var at = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
            ledger.Record(Attempt("google", "gemini-3.5-flash-lite", input: 457, output: 3, at: at));
            ledger.Record(Attempt("openrouter", "google/gemini-3.5-flash-lite", 'F', cost: 0.0001443m, input: 456, output: 3, at: at));
            ledger.Record(Attempt("google", "gemini-3.5-flash-lite", ok: false, error: "HTTP 503", at: at));
            File.AppendAllText(ledger.Path, "{ half a line\n");

            var read = new UsageLedger(dir).ReadAll();
            Assert.Equal(3, read.Count);
            Assert.Equal(at, read[0].At);
            Assert.Equal('F', read[1].Role);
            Assert.Equal(0.0001443m, read[1].Cost);
            Assert.Equal("HTTP 503", read[2].Error);
            Assert.False(read[2].Success);

            ledger.Clear();
            Assert.Empty(ledger.ReadAll());
            Assert.Equal(4, raised);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_ledger_never_stores_text()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var ledger = new UsageLedger(dir);
            ledger.Record(Attempt("google", "m", input: 1, output: 1));
            Assert.DoesNotContain("prompt", File.ReadAllText(ledger.Path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_summary_adds_up_models_costs_and_why_the_main_model_failed()
    {
        var now = DateTimeOffset.UtcNow;
        var attempts = new[]
        {
            Attempt("google", "gemini-3.5-flash-lite", input: 1_000_000, output: 0),
            Attempt("google", "gemini-3.5-flash-lite", ok: false, error: "HTTP 503"),
            Attempt("google", "gemini-3.5-flash-lite", ok: false, error: "HTTP 503"),
            Attempt("google", "gemini-3.5-flash-lite", ok: false, error: "Timeout"),
            Attempt("openrouter", "google/gemini-3.5-flash-lite", 'F', cost: 0.25m, input: 100, output: 10),
            Attempt("openrouter", "google/gemini-3.5-flash-lite", 'F', cost: 0.25m, input: 100, output: 10),
            Attempt("custom", "mystery", input: 5, output: 5),
            Attempt("google", "gemini-3.5-flash-lite", input: 0, output: 0, at: now.AddDays(-90)),
        };
        var summary = UsageSummary.Build(attempts, new UsagePricing(), now.AddDays(-30));

        Assert.Equal(7, summary.Requests);
        Assert.Equal(3, summary.Failures);
        Assert.Equal(2, summary.AnsweredByFallback);
        Assert.Equal(2, summary.AnsweredByMain);
        Assert.Equal(0.5, summary.FallbackShare);
        Assert.Equal([new FailureCount("HTTP 503", 2), new FailureCount("Timeout", 1)], summary.MainFailures);
        // 0.30 for 1M Google tokens + 0.50 reported by OpenRouter; "mystery" has no price, so the total is partial.
        Assert.Equal(0.8m, summary.Cost);
        Assert.True(summary.CostPartial);
        Assert.Equal(1_000_205, summary.InputTokens);
        Assert.Equal(3, summary.Models.Count);
        Assert.Equal("google/gemini-3.5-flash-lite", summary.Models[0].Model);
        Assert.Equal(0.5m, summary.Models[0].Cost);
    }

    [Fact]
    public void An_empty_ledger_gives_a_zero_summary()
    {
        var summary = UsageSummary.Build([], new UsagePricing());
        Assert.Equal(0, summary.Requests);
        Assert.Equal(0, summary.FallbackShare);
        Assert.False(summary.CostPartial);
        Assert.Empty(summary.Models);
    }
}
