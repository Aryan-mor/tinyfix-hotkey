// The seam between the skill runner and whatever fixes text. TinyFix sends every skill (Fix, Fix all, Translate, Improve
// prompt, custom skills) to the online LLM; there is no local engine in the C# app. Keeping the interface lets a test or a
// future engine plug in without touching the runner.

using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;

namespace TinyFix.Core.Engine;

/// <summary>A fixed text and which model answered (M main, F fallback, L local server).</summary>
public readonly record struct FixOutcome(string Text, char ModelLetter = '\0');

/// <summary>Transforms a text (fixes it, translates it, rewrites it).</summary>
public interface IFixEngine
{
    /// <summary>Returns the transformed text.</summary>
    /// <exception cref="LlmException">The online request failed.</exception>
    Task<FixOutcome> FixAsync(string text, CancellationToken ct = default);
}

/// <summary>Runs a prompt template on the text with the online model.</summary>
/// <param name="client">The LLM client.</param>
/// <param name="config">The request settings (with the API key) at the moment of the call.</param>
/// <param name="template">The prompt template with <c>{{content}}</c> and optionally <c>{{language}}</c>.</param>
/// <param name="language">The target language for <c>{{language}}</c>, or null.</param>
public sealed class OnlineFixEngine(LlmClient client, Func<LlmConfig> config, string template, string? language = null) : IFixEngine
{
    /// <inheritdoc />
    public async Task<FixOutcome> FixAsync(string text, CancellationToken ct = default)
    {
        var result = await client.RunSkillAsync(config(), template, text, language, ct).ConfigureAwait(false);
        return new FixOutcome(result.Text, result.Source);
    }
}
