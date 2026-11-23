// API keys on Linux: the Secret Service through `secret-tool` when it works, else an encrypted file with a warning.
// UNTESTED against a real keyring by the author; the fallback path is covered by tests in TinyFix.Core.Tests.

using TinyFix.Core.Llm;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Linux;

/// <summary><see cref="ISecretStore"/> over `secret-tool`.</summary>
public sealed class SecretToolStore : ISecretStore
{
    private const string Service = "tinyfix";

    /// <inheritdoc />
    public string Description => "Secret Service (secret-tool)";

    /// <inheritdoc />
    public string? Warning => null;

    /// <summary>True when secret-tool exists and a keyring answers (a lookup that finds nothing exits with 1, not -1).</summary>
    public static bool IsUsable() =>
        ExternalProcess.Exists("secret-tool") &&
        ExternalProcess.Run("secret-tool", ["lookup", "service", Service, "account", "__probe__"]).ExitCode is 0 or 1;

    /// <inheritdoc />
    public bool Contains(string account) => Get(account).Length > 0;

    /// <inheritdoc />
    public string Get(string provider)
    {
        var result = ExternalProcess.Run("secret-tool", ["lookup", "service", Service, "account", provider]);
        return result.Success ? result.Output.Trim() : "";
    }

    /// <inheritdoc />
    public void Set(string provider, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            ExternalProcess.Run("secret-tool", ["clear", "service", Service, "account", provider]);
            return;
        }
        // The key goes through stdin, never the argument list, so `ps` cannot show it.
        var result = ExternalProcess.Run("secret-tool", ["store", "--label=TinyFix " + provider, "service", Service, "account", provider], apiKey);
        if (!result.Success)
            throw new IOException("secret-tool could not store the key.");
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll()
    {
        var result = new Dictionary<string, string>();
        foreach (var account in LlmSecrets.Accounts)
        {
            var key = Get(account);
            if (key.Length > 0)
                result[account] = key;
        }
        return result;
    }
}

/// <summary>Chooses the secret backend of a Linux session.</summary>
public static class LinuxSecrets
{
    /// <summary>Secret Service when usable, else the encrypted file.</summary>
    public static ISecretStore Create(string dataDirectory) => SecretToolStore.IsUsable()
        ? new SecretToolStore()
        : new EncryptedFileSecretStore(dataDirectory, MachineId(), Environment.UserName);

    private static string MachineId()
    {
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next location.
            }
        }
        return Environment.MachineName;
    }
}
