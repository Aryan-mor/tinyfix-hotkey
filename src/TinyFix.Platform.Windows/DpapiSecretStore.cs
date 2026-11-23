// API keys encrypted with Windows DPAPI (CryptProtectData), in the same file format as the Python app's secrets.dpapi:
// the encrypted bytes of {"keys": {"<provider>": "<key>"}} (an older file holds one bare key for Google).
// DPAPI ties the file to the Windows user, so it is useless when copied to another account or computer.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using TinyFix.Core.IO;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Windows;

/// <summary><see cref="ISecretStore"/> over DPAPI.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    /// <summary>File name inside the data directory (the Python app's name, so its keys carry over).</summary>
    public const string FileName = "secrets.dpapi";

    private const string Description_ = "TinyFix Hotkey";
    private readonly string _path = Path.Combine(directory, FileName);

    /// <inheritdoc />
    public string Description => "Windows DPAPI";

    /// <inheritdoc />
    public string? Warning => null;

    /// <inheritdoc />
    public string Get(string provider) => GetAll().GetValueOrDefault(provider, "");

    /// <inheritdoc />
    public bool Contains(string account) =>
        GetAll().TryGetValue(account, out var value) && !string.IsNullOrWhiteSpace(value);

    /// <inheritdoc />
    public void Set(string provider, string apiKey)
    {
        var keys = GetAll().ToDictionary(p => p.Key, p => p.Value);
        if (string.IsNullOrEmpty(apiKey))
            keys.Remove(provider);
        else
            keys[provider] = apiKey;
        Save(keys);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, string>();
        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(_path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new Dictionary<string, string>();
        }
        if (raw.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Object)
                {
                    return keys.EnumerateObject()
                        .Where(p => p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p.Value.GetString()))
                        .ToDictionary(p => p.Name, p => p.Value.GetString()!);
                }
            }
            catch (JsonException)
            {
                // Fall through to the bare-key reading.
            }
        }
        // A file from an older version holds one bare Google key.
        return raw.Length == 0 ? new Dictionary<string, string>() : new Dictionary<string, string> { [LlmDefaults.ProviderGoogle] = raw };
    }

    private void Save(Dictionary<string, string> keys)
    {
        if (keys.Count == 0)
        {
            if (File.Exists(_path))
                File.Delete(_path);
            return;
        }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("keys");
            foreach (var (name, value) in keys)
                writer.WriteString(name, value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        AtomicFile.WriteAllBytes(_path, Protect(stream.ToArray()));
    }

    internal static byte[] Protect(byte[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new DataBlob { CbData = (uint)data.Length, PbData = handle.AddrOfPinnedObject() };
            if (!NativeMethods.CryptProtectData(ref input, Description_, 0, 0, 0, NativeMethods.CryptProtectUiForbidden, out var output))
                throw new InvalidOperationException($"CryptProtectData failed (error {Marshal.GetLastWin32Error()})");
            return Take(output);
        }
        finally
        {
            handle.Free();
        }
    }

    internal static byte[] Unprotect(byte[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new DataBlob { CbData = (uint)data.Length, PbData = handle.AddrOfPinnedObject() };
            if (!NativeMethods.CryptUnprotectData(ref input, 0, 0, 0, 0, NativeMethods.CryptProtectUiForbidden, out var output))
                throw new InvalidOperationException($"CryptUnprotectData failed (error {Marshal.GetLastWin32Error()})");
            return Take(output);
        }
        finally
        {
            handle.Free();
        }
    }

    // Copies the system-allocated output and frees it.
    private static byte[] Take(DataBlob blob)
    {
        try
        {
            var bytes = new byte[blob.CbData];
            Marshal.Copy(blob.PbData, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (blob.PbData != 0)
                NativeMethods.LocalFree(blob.PbData);
        }
    }
}
