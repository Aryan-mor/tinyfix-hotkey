// A last-resort secret store: API keys in an AES-GCM encrypted file under the data directory. It exists for Linux
// desktops without a Secret Service (secret-tool). The key is derived from the machine id and the user name, so the
// file is useless when copied to another computer or account, but it is NOT protection against another program
// running as the same user, which is why the store carries a warning the UI shows.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TinyFix.Core.IO;

namespace TinyFix.Core.Platform;

/// <summary><see cref="ISecretStore"/> over an encrypted file.</summary>
public sealed class EncryptedFileSecretStore : ISecretStore
{
    private const string Magic = "TFS1";
    private readonly string _path;
    private readonly byte[] _key;

    /// <summary>Creates the store. <paramref name="machineSecret"/> is a per-computer value (the machine id).</summary>
    public EncryptedFileSecretStore(string directory, string machineSecret, string userName)
    {
        _path = Path.Combine(directory, "secrets.enc");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes($"tinyfix-secrets|{machineSecret}|{userName}"));
    }

    /// <inheritdoc />
    public string Description => "Encrypted file";

    /// <inheritdoc />
    public string? Warning => "No system keyring was found: API keys are kept in an encrypted file that other programs of your account can read.";

    /// <inheritdoc />
    public string Get(string provider) => GetAll().GetValueOrDefault(provider, "");

    /// <inheritdoc />
    public void Set(string provider, string apiKey)
    {
        var keys = GetAll().ToDictionary(p => p.Key, p => p.Value);
        if (string.IsNullOrEmpty(apiKey))
            keys.Remove(provider);
        else
            keys[provider] = apiKey;
        if (keys.Count == 0)
        {
            if (File.Exists(_path))
                File.Delete(_path);
            return;
        }
        var plain = JsonSerializer.SerializeToUtf8Bytes(keys, SecretJsonContext.Default.DictionaryStringString);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, 16))
            aes.Encrypt(nonce, plain, cipher, tag);
        AtomicFile.WriteAllBytes(_path, [.. Encoding.ASCII.GetBytes(Magic), .. nonce, .. tag, .. cipher]);
        TryRestrictPermissions(_path);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, string>();
        try
        {
            var data = File.ReadAllBytes(_path);
            if (data.Length < 4 + 12 + 16 || Encoding.ASCII.GetString(data, 0, 4) != Magic)
                return new Dictionary<string, string>();
            var nonce = data.AsSpan(4, 12);
            var tag = data.AsSpan(16, 16);
            var cipher = data.AsSpan(32);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(_key, 16))
                aes.Decrypt(nonce, cipher, tag, plain);
            return JsonSerializer.Deserialize(plain, SecretJsonContext.Default.DictionaryStringString) ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort.
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SecretJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
