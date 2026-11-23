// Reads and writes skills.json, compatible with the Python app's file. JSON is handled by hand (no reflection) so
// start-up stays light and a hand-edited file with a wrong type in one field does not lose the rest.

using System.Text.Encodings.Web;
using System.Text.Json;
using TinyFix.Core.Hotkeys;
using TinyFix.Core.IO;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;

namespace TinyFix.Core.Skills;

/// <summary>Persistence of <see cref="SkillStore"/>.</summary>
public static class SkillStoreFile
{
    /// <summary>File name inside the data directory.</summary>
    public const string FileName = "skills.json";

    /// <summary>Loads <paramref name="path"/>, or builds the defaults when it is missing or unreadable.</summary>
    /// <returns>The store and whether it was created from defaults (so the caller may save it).</returns>
    public static (SkillStore Store, bool Created) Load(string path, LegacySkillOptions? legacy = null)
    {
        if (File.Exists(path))
        {
            try
            {
                var store = Parse(File.ReadAllText(path));
                return (SkillCatalog.EnsureBuiltins(store, legacy), false);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Same as the Python app: an unreadable file means the defaults, not a crash.
            }
        }
        return (SkillCatalog.EnsureBuiltins(SkillCatalog.DefaultStore(legacy), legacy), true);
    }

    /// <summary>Parses the JSON text of a skills file.</summary>
    /// <exception cref="InvalidDataException">The root is not an object.</exception>
    public static SkillStore Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("skills.json is not an object");

        var store = new SkillStore
        {
            Version = GetInt(root, "version", SkillStore.CurrentVersion),
            SkillMenuHotkey = HotkeyParser.Normalize(GetString(root, "skill_menu_hotkey", null), HotkeyDefaults.Menu),
        };
        if (root.TryGetProperty("dismissed", out var dismissed) && dismissed.ValueKind == JsonValueKind.Array)
            store.Dismissed = dismissed.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
        if (root.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in skills.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
                store.Skills.Add(ParseSkill(item));
        }
        return store;
    }

    private static Skill ParseSkill(JsonElement item)
    {
        var id = GetString(item, "id", "") ?? "";
        return new Skill
        {
            Id = id,
            Name = Or(GetString(item, "name", null), "New skill"),
            InputMode = Or(GetString(item, "input_mode", null), InputModes.Selection),
            Prompt = GetString(item, "prompt", null) ?? PromptRenderer.DefaultCustomPrompt,
            Hotkey = GetString(item, "hotkey", null) ?? "none",
            Enabled = GetBool(item, "enabled", true),
            Builtin = GetBool(item, "builtin", false),
            OfflineFallback = GetBool(item, "offline_fallback", false),
            // A file from before per-skill models: Improve prompt gets the stronger model, the rest keep the default.
            ModelMode = Or(GetString(item, "model_mode", null), id == SkillIds.ImprovePrompt ? ModelModes.Better : ModelModes.Default),
            ModelProvider = GetString(item, "model_provider", "") ?? "",
            Model = GetString(item, "model", "") ?? "",
            ModelFallbackProvider = GetString(item, "model_fallback_provider", "") ?? "",
            ModelFallback = GetString(item, "model_fallback", "") ?? "",
            ModelBaseUrl = GetString(item, "model_base_url", "") ?? "",
            ModelFallbackBaseUrl = GetString(item, "model_fallback_base_url", "") ?? "",
            ModelTimeout = GetDouble(item, "model_timeout"),
            ModelFallbackTimeout = GetDouble(item, "model_fallback_timeout"),
        }.Normalize();
    }

    private static string Or(string? value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    private static string? GetString(JsonElement e, string name, string? fallback) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : fallback;

    private static bool GetBool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;

    private static int GetInt(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) && v > 0 ? v : fallback;

    private static double GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v) && double.IsFinite(v) && v > 0 ? v : 0;

    /// <summary>The JSON text of a store, two-space indented like the Python app writes it.</summary>
    public static string Serialize(SkillStore store)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", store.Version > 0 ? store.Version : SkillStore.CurrentVersion);
            w.WriteString("skill_menu_hotkey", HotkeyParser.Normalize(store.SkillMenuHotkey, HotkeyDefaults.Menu));
            w.WriteStartArray("dismissed");
            foreach (var id in store.Dismissed)
                w.WriteStringValue(id);
            w.WriteEndArray();
            w.WriteStartArray("skills");
            foreach (var s in store.Skills)
            {
                w.WriteStartObject();
                w.WriteString("id", s.Id);
                w.WriteString("name", s.Name);
                w.WriteString("input_mode", s.InputMode);
                w.WriteString("prompt", s.Prompt);
                w.WriteString("hotkey", s.Hotkey);
                w.WriteBoolean("enabled", s.Enabled);
                w.WriteBoolean("builtin", s.Builtin);
                w.WriteBoolean("offline_fallback", s.OfflineFallback);
                w.WriteString("model_mode", s.ModelMode);
                w.WriteString("model_provider", s.ModelProvider);
                w.WriteString("model", s.Model);
                w.WriteString("model_base_url", s.ModelBaseUrl);
                w.WriteNumber("model_timeout", s.ModelTimeout);
                w.WriteString("model_fallback_provider", s.ModelFallbackProvider);
                w.WriteString("model_fallback", s.ModelFallback);
                w.WriteString("model_fallback_base_url", s.ModelFallbackBaseUrl);
                w.WriteNumber("model_fallback_timeout", s.ModelFallbackTimeout);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Saves the store atomically.</summary>
    public static void Save(string path, SkillStore store) => AtomicFile.WriteAllText(path, Serialize(store));
}
