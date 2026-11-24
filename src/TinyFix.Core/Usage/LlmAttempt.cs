// One request to a model, as the cost page and the log remember it. It holds no text of the user and no key.

namespace TinyFix.Core.Usage;

/// <summary>One request to one model.</summary>
/// <param name="At">When it finished (UTC).</param>
/// <param name="Protocol">The provider preset (google, openrouter, ...).</param>
/// <param name="Model">The model id the request used.</param>
/// <param name="Role">M main model, F fallback model, L local server.</param>
/// <param name="Success">The model answered with text.</param>
/// <param name="Error">Why it failed ("Timeout", "HTTP 503", ...), empty on success.</param>
/// <param name="LatencyMs">How long the request took.</param>
/// <param name="InputTokens">Prompt tokens the provider reported, 0 when it did not say.</param>
/// <param name="OutputTokens">Answer tokens (including thinking) the provider reported.</param>
/// <param name="Cost">Dollars the provider itself reported (OpenRouter does), else null.</param>
public sealed record LlmAttempt(DateTimeOffset At, string Protocol, string Model, char Role, bool Success, string Error,
    int LatencyMs, long InputTokens, long OutputTokens, decimal? Cost);
