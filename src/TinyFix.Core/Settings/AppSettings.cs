// The user's settings (settings.json). Keys are snake_case like the Python app's files. Secrets never live here.

using System.Text.Json;
using System.Text.Json.Serialization;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;
using TinyFix.Core.Prompts;

namespace TinyFix.Core.Settings;

/// <summary>Settings stored as JSON in the data directory.</summary>
public sealed class AppSettings
{
    /// <summary>A language code from the shipped list, or "auto" for the OS language.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>How large Settings is drawn: 1 as designed, changed with Ctrl and the wheel (see UiZoom).</summary>
    public double UiScale { get; set; } = 1;

    /// <summary>All shortcuts are off while paused.</summary>
    public bool Paused { get; set; }

    /// <summary>Prompt variables, keyed by name. <c>language</c> is always present. <c>content</c> is not stored here.</summary>
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.Ordinal)
    {
        [VariableNames.Language] = PromptRenderer.DefaultTargetLanguage,
    };

    /// <summary>The main endpoint and the optional fallback.</summary>
    public LlmSettings Llm { get; set; } = new();

    /// <summary>Ask GitHub once a day whether a newer release exists.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Show a short "changed" toast with an Undo button after a skill replaced text.</summary>
    public bool ShowChangeToast { get; set; } = true;

    /// <summary>Date (yyyy-MM-dd) of the last update check.</summary>
    public string LastUpdateCheck { get; set; } = "";

    /// <summary>Set once the Python app's files were looked for, so the import happens only once.</summary>
    public bool LegacyImported { get; set; }

    /// <summary>Set once "start with the system" got its on-by-default registration (<c>AutostartDefault</c>).</summary>
    public bool AutostartDefaultApplied { get; set; }

    /// <summary>
    /// False until the first-run setup is finished, skipped, or closed. The window opens once; running it again does not
    /// clear this or the saved model and skills.
    /// </summary>
    public bool SetupCompleted { get; set; }

    /// <summary>How the skill donut looks and where it opens. Missing values keep today's behaviour.</summary>
    public DonutSettings Donut { get; set; } = new();

    /// <summary>Brings variables and endpoints into the shape the rest of the app expects.</summary>
    public void Normalize()
    {
        Donut ??= new DonutSettings();
        Donut.Normalize();
        Variables ??= new Dictionary<string, string>(StringComparer.Ordinal);
        Variables.Remove(VariableNames.Content);
        if (!Variables.TryGetValue(VariableNames.Language, out var language) || string.IsNullOrWhiteSpace(language))
            Variables[VariableNames.Language] = PromptRenderer.DefaultTargetLanguage;
        else
            Variables[VariableNames.Language] = language.Trim();
        Llm ??= new LlmSettings();
        Llm.Normalize();
    }

    /// <summary>The request settings. Keys come from <paramref name="keys"/> (slot, then provider); they are not stored here.</summary>
    public LlmConfig ToLlmConfig(IReadOnlyDictionary<string, string> keys)
    {
        Llm ??= new LlmSettings();
        return new LlmConfig
        {
            Main = ToEndpoint(Llm.Main, LlmSecrets.MainSlot, keys),
            Fallback = Llm.Fallback is { } fallback && !string.IsNullOrWhiteSpace(fallback.Model)
                ? ToEndpoint(fallback, LlmSecrets.FallbackSlot, keys)
                : null,
        };
    }

    private static LlmEndpoint ToEndpoint(LlmEndpointSettings settings, string slot, IReadOnlyDictionary<string, string> keys)
    {
        var protocol = LlmProtocols.Normalize(settings.Protocol);
        return new LlmEndpoint
        {
            Protocol = protocol,
            Model = string.IsNullOrWhiteSpace(settings.Model) ? LlmProtocols.DefaultModel(protocol) : settings.Model.Trim(),
            BaseUrl = (settings.BaseUrl ?? "").Trim(),
            TimeoutSeconds = settings.Timeout > 0 ? settings.Timeout : LlmDefaults.DefaultTimeoutSeconds,
            ApiKey = LlmSecrets.Resolve(keys, slot, protocol),
        };
    }
}

/// <summary>The two endpoints stored in settings.json. API keys are not fields of this type.</summary>
public sealed class LlmSettings
{
    public LlmEndpointSettings Main { get; set; } = new();

    /// <summary>Absent when the user turned the fallback off.</summary>
    public LlmEndpointSettings? Fallback { get; set; } = new()
    {
        Model = LlmDefaults.GoogleFallbackModel,
    };

    /// <summary>Fills empty models and drops a fallback that has no model.</summary>
    public void Normalize()
    {
        Main ??= new LlmEndpointSettings();
        NormalizeOne(Main, fallback: false);
        if (Fallback is null)
            return;
        if (string.IsNullOrWhiteSpace(Fallback.Model))
        {
            Fallback = null;
            return;
        }
        NormalizeOne(Fallback, fallback: true);
    }

    private void NormalizeOne(LlmEndpointSettings endpoint, bool fallback)
    {
        endpoint.Protocol = LlmProtocols.Normalize(string.IsNullOrWhiteSpace(endpoint.Protocol) ? Main.Protocol : endpoint.Protocol);
        if (string.IsNullOrWhiteSpace(endpoint.Model))
            endpoint.Model = fallback ? LlmProtocols.DefaultFallbackModel(endpoint.Protocol) : LlmProtocols.DefaultModel(endpoint.Protocol);
        else
            endpoint.Model = endpoint.Model.Trim();
        endpoint.BaseUrl = (endpoint.BaseUrl ?? "").Trim();
        if (endpoint.Timeout <= 0 || !double.IsFinite(endpoint.Timeout))
            endpoint.Timeout = Main.Timeout > 0 ? Main.Timeout : LlmDefaults.DefaultTimeoutSeconds;
        endpoint.Timeout = Math.Min(endpoint.Timeout, 120);
    }

    /// <summary>Builds the new shape from the flat keys an older settings.json or config.ini used.</summary>
    public static LlmSettings FromLegacy(string? provider, string? model, string? baseUrl, string? fallbackProvider,
        string? fallbackModel, double timeout)
    {
        var seconds = timeout > 0 && double.IsFinite(timeout) ? Math.Min(timeout, 120) : LlmDefaults.DefaultTimeoutSeconds;
        var protocol = LlmProtocols.MapLegacy(provider, baseUrl);
        var main = new LlmEndpointSettings
        {
            Protocol = protocol,
            Model = string.IsNullOrWhiteSpace(model) ? LlmProtocols.DefaultModel(protocol) : model.Trim(),
            BaseUrl = (baseUrl ?? "").Trim(),
            Timeout = seconds,
        };
        LlmEndpointSettings? fallback = null;
        if (!string.IsNullOrWhiteSpace(fallbackModel))
        {
            var fallbackProtocol = string.IsNullOrWhiteSpace(fallbackProvider) ? protocol : LlmProtocols.MapLegacy(fallbackProvider, "");
            fallback = new LlmEndpointSettings
            {
                Protocol = fallbackProtocol,
                Model = fallbackModel.Trim(),
                BaseUrl = fallbackProtocol == protocol ? main.BaseUrl : "",
                Timeout = seconds,
            };
        }
        return new LlmSettings { Main = main, Fallback = fallback };
    }
}

/// <summary>One endpoint as stored in settings.json.</summary>
public sealed class LlmEndpointSettings
{
    public string Protocol { get; set; } = LlmProtocols.Google;
    public string Model { get; set; } = LlmDefaults.GoogleModel;
    public string BaseUrl { get; set; } = "";

    /// <summary>Seconds. Zero means the default timeout.</summary>
    public double Timeout { get; set; } = LlmDefaults.DefaultTimeoutSeconds;
}

/// <summary>Reads the flat LLM keys of an older settings file into <see cref="AppSettings"/>.</summary>
public static class SettingsMigration
{
    /// <summary>
    /// Copies <c>llm_target_language</c> into <c>variables.language</c> and the flat <c>llm_*</c> keys into
    /// <see cref="AppSettings.Llm"/> when the new objects are absent. Already migrated files are left alone.
    /// </summary>
    public static void Apply(AppSettings settings, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return;
        settings.Variables ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var hasVariables = root.TryGetProperty("variables", out var variables) && variables.ValueKind == JsonValueKind.Object;
        var hasLanguage = hasVariables && variables.TryGetProperty(VariableNames.Language, out var languageElement) &&
                          languageElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(languageElement.GetString());
        if (!hasLanguage && root.TryGetProperty("llm_target_language", out var legacyLanguage) && legacyLanguage.ValueKind == JsonValueKind.String)
        {
            var value = legacyLanguage.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                settings.Variables[VariableNames.Language] = value.Trim();
        }

        var hasLlm = root.TryGetProperty("llm", out var llm) && llm.ValueKind == JsonValueKind.Object;
        if (hasLlm || !HasLegacyLlm(root))
            return;
        // A missing llm_fallback_model kept the property default (Google's fallback). An explicit empty string means none.
        var fallbackSpecified = root.TryGetProperty("llm_fallback_model", out var fallbackElement) && fallbackElement.ValueKind == JsonValueKind.String;
        settings.Llm = LlmSettings.FromLegacy(
            String(root, "llm_provider"),
            String(root, "llm_model"),
            String(root, "llm_base_url"),
            String(root, "llm_fallback_provider"),
            fallbackSpecified ? fallbackElement.GetString() : LlmDefaults.GoogleFallbackModel,
            Number(root, "llm_timeout"));
    }

    private static bool HasLegacyLlm(JsonElement root) =>
        root.TryGetProperty("llm_provider", out _) || root.TryGetProperty("llm_model", out _) ||
        root.TryGetProperty("llm_base_url", out _) || root.TryGetProperty("llm_fallback_provider", out _) ||
        root.TryGetProperty("llm_fallback_model", out _) || root.TryGetProperty("llm_timeout", out _);

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;
}

/// <summary>Source-generated JSON metadata: no reflection at start-up, and safe for trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LlmSettings))]
[JsonSerializable(typeof(LlmEndpointSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Size, opacity and placement of the skill donut. A settings file from before this object existed reads as the defaults.</summary>
public sealed class DonutSettings
{
    /// <summary>Smallest and largest outer radius the Donut page offers, in device-independent pixels.</summary>
    public const double MinRadius = 110, MaxRadius = 220;

    /// <summary>The radius the donut has always used.</summary>
    public const double DefaultRadius = 150;

    /// <summary>The lowest slice opacity the Donut page offers. Labels and outlines stay opaque.</summary>
    public const double MinOpacity = 0.4;

    /// <summary>The slice opacity the donut has always used.</summary>
    public const double DefaultOpacity = 0.96;

    /// <summary>Outer radius in device-independent pixels. The donut reads it the next time it opens.</summary>
    public double OuterRadius { get; set; } = DefaultRadius;

    /// <summary>Background opacity of the slices (<see cref="MinOpacity"/>–1).</summary>
    public double Opacity { get; set; } = DefaultOpacity;

    /// <summary><see cref="DonutOpenAt.Caret"/> (then the pointer if the app has no caret) or <see cref="DonutOpenAt.Cursor"/>.</summary>
    public string OpenAt { get; set; } = DonutOpenAt.Caret;

    /// <summary>Draw each skill's shortcut under its name. On, as before this setting existed.</summary>
    public bool ShowShortcutHints { get; set; } = true;

    /// <summary><paramref name="value"/> inside <see cref="MinRadius"/>–<see cref="MaxRadius"/>, or <see cref="DefaultRadius"/> when it is not a number.</summary>
    public static double ClampRadius(double value) =>
        double.IsFinite(value) ? Math.Clamp(Math.Round(value), MinRadius, MaxRadius) : DefaultRadius;

    /// <summary><paramref name="value"/> inside <see cref="MinOpacity"/>–1, or <see cref="DefaultOpacity"/> when it is not a number.</summary>
    public static double ClampOpacity(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinOpacity, 1) : DefaultOpacity;

    /// <summary>Brings a value read from disk back into the range the page offers.</summary>
    public void Normalize()
    {
        OuterRadius = ClampRadius(OuterRadius);
        Opacity = ClampOpacity(Opacity);
        OpenAt = DonutOpenAt.Normalize(OpenAt);
    }
}

/// <summary>Where the skill donut opens.</summary>
public static class DonutOpenAt
{
    /// <summary>At the text caret, or the mouse pointer when the focused app has no caret.</summary>
    public const string Caret = "caret";

    /// <summary>At the mouse pointer.</summary>
    public const string Cursor = "cursor";

    /// <summary>True when <paramref name="value"/> asks for the pointer.</summary>
    public static bool IsCursor(string? value) => string.Equals(value, Cursor, StringComparison.OrdinalIgnoreCase);

    /// <summary><see cref="Cursor"/> or <see cref="Caret"/>. Anything else, including empty, is the caret.</summary>
    public static string Normalize(string? value) => IsCursor(value) ? Cursor : Caret;

    /// <summary>The screen point the donut opens on for <paramref name="openAt"/>.</summary>
    public static ScreenPoint Point(ICaretLocator caret, string? openAt) =>
        IsCursor(openAt) ? caret.GetCursor() : caret.GetCaretOrCursor();
}

/// <summary>The look the donut copies when it opens: size, slice opacity and whether shortcuts are drawn on the slices.</summary>
/// <param name="OuterRadius">Outer radius in device-independent pixels.</param>
/// <param name="Opacity">Slice background opacity, 0.1–1.</param>
/// <param name="ShowShortcutHints">True when each slice shows its shortcut.</param>
public readonly record struct DonutLook(double OuterRadius, double Opacity, bool ShowShortcutHints)
{
    /// <summary>The look the donut had before these settings existed.</summary>
    public static DonutLook Default { get; } = new(DonutSettings.DefaultRadius, DonutSettings.DefaultOpacity, true);

    /// <summary>The look stored in <paramref name="settings"/>, clamped to the range the page offers.</summary>
    public static DonutLook From(DonutSettings settings) => new(
        DonutSettings.ClampRadius(settings.OuterRadius),
        DonutSettings.ClampOpacity(settings.Opacity),
        settings.ShowShortcutHints);
}
