// Tests of the fix seam: the online engine sends the skill's prompt through the LLM client and reports the model letter.

using TinyFix.Core.Engine;
using TinyFix.Core.Llm;

namespace TinyFix.Core.Tests;

public class EngineTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static LlmConfig Config() => new() { ApiKey = "k", Model = "m", FallbackModel = "", Enabled = true };

    [Fact]
    public async Task The_online_engine_renders_the_template_and_reports_the_model_letter()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.GeminiAnswer("Hallo Freunde")));
        var engine = new OnlineFixEngine(new LlmClient(new HttpClient(handler)), Config, "Translate into {{language}}: {{content}}", "German");
        var result = await engine.FixAsync("Hello friends", Ct);
        Assert.Equal(("Hallo Freunde", 'M'), (result.Text, result.ModelLetter));
        Assert.Contains("Translate into German: Hello friends", handler.Requests[0].Body);
    }

    [Fact]
    public async Task The_online_engine_refuses_when_ai_is_off_and_never_sends_anything()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        var engine = new OnlineFixEngine(new LlmClient(new HttpClient(handler)), () => Config() with { Enabled = false }, "{{content}}");
        await Assert.ThrowsAsync<LlmException>(() => engine.FixAsync("text", Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_failing_main_model_falls_back_to_the_second_model_and_says_so()
    {
        var handler = new FakeHandler((req, _) => req.RequestUri!.AbsolutePath.Contains("main", StringComparison.Ordinal)
            ? FakeHandler.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable)
            : FakeHandler.Json(FakeHandler.GeminiAnswer("fixed")));
        var config = Config() with { Model = "main", FallbackModel = "backup" };
        var result = await new OnlineFixEngine(new LlmClient(new HttpClient(handler)), () => config, "{{content}}").FixAsync("x", Ct);
        Assert.Equal(("fixed", 'F'), (result.Text, result.ModelLetter));
    }
}
