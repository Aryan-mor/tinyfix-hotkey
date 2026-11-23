// Source-generated JSON metadata for the types the app serializes itself (no reflection at start-up).

using System.Text.Json.Serialization;

namespace TinyFix.App.Services;

/// <summary>JSON contexts of the app.</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
