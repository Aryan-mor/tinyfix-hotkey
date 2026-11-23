// Errors of the LLM client. Messages never include API keys or the user's text.

namespace TinyFix.Core.Llm;

/// <summary>What went wrong, so the fallback logic does not have to parse messages.</summary>
public enum LlmErrorKind
{
    /// <summary>The server answered with an error status.</summary>
    Http,

    /// <summary>The request ran out of time.</summary>
    Timeout,

    /// <summary>The server could not be reached.</summary>
    Network,

    /// <summary>The answer held no text (no candidates, empty text).</summary>
    EmptyResponse,

    /// <summary>The answer was not what the provider promises.</summary>
    BadPayload,

    /// <summary>The settings are unusable (no key, no model, unsafe URL).</summary>
    Config,
}

/// <summary>A provider or network failure.</summary>
public class LlmException : Exception
{
    public LlmException(LlmErrorKind kind, string message, int statusCode = 0, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    /// <summary>The category of the failure.</summary>
    public LlmErrorKind Kind { get; }

    /// <summary>HTTP status for <see cref="LlmErrorKind.Http"/>, else 0.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// True for failures another model could fix: timeouts, rate limits, retired models, server errors, network
    /// failures and empty answers. Not for bad keys or bad requests, which every model would refuse the same way.
    /// </summary>
    public bool FallbackWorthy => Kind switch
    {
        LlmErrorKind.Timeout or LlmErrorKind.Network or LlmErrorKind.EmptyResponse => true,
        LlmErrorKind.Http => StatusCode is 402 or 404 or 429 || StatusCode >= 500,
        _ => false,
    };
}

/// <summary>The request exceeded its timeout.</summary>
public sealed class LlmTimeoutException(string message) : LlmException(LlmErrorKind.Timeout, message);
