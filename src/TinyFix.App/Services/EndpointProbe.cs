// Sends the "test connection" prompt. The saved key is read here, never stored on a view model, and never logged.

using TinyFix.Core.Llm;
using TinyFix.Core.Platform;

namespace TinyFix.App.Services;

/// <summary>Builds one probe request. The API key stays inside this type for the duration of the call.</summary>
internal static class EndpointProbe
{
    /// <summary>
    /// Sends "Reply with OK". <paramref name="typedKey"/> is text the user just typed; when it is empty the saved key
    /// for <paramref name="slot"/> is used. The key is not returned.
    /// </summary>
    public static Task<string> RunAsync(AppServices services, LlmEndpoint endpoint, string slot, string? typedKey, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(typedKey) ? Resolve(services.Platform.Secrets, slot, endpoint.Protocol) : typedKey.Trim();
        return services.Llm.ProbeAsync(endpoint with { ApiKey = key }, ct);
    }

    private static string Resolve(ISecretStore secrets, string slot, string protocol)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Take(string account)
        {
            if (map.ContainsKey(account))
                return;
            var value = secrets.Get(account);
            if (!string.IsNullOrWhiteSpace(value))
                map[account] = value.Trim();
        }

        foreach (var pair in secrets.GetAll())
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
                map[pair.Key] = pair.Value.Trim();
        }
        Take(slot);
        Take(protocol);
        if (protocol is LlmProtocols.OpenAi or LlmProtocols.Custom)
            Take(LlmDefaults.ProviderOpenAi);
        return LlmSecrets.Resolve(map, slot, protocol);
    }
}
