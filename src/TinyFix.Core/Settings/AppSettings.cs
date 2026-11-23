// The user's settings (settings.json). Keys are snake_case like the Python app's files. Secrets never live here.

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

    public bool LlmEnabled { get; set; }
    public string LlmProvider { get; set; } = LlmDefaults.ProviderGoogle;
    public string LlmModel { get; set; } = LlmDefaults.GoogleModel;
    public string LlmFallbackProvider { get; set; } = "";
    public string LlmFallbackModel { get; set; } = LlmDefaults.GoogleFallbackModel;
    public string LlmBaseUrl { get; set; } = "";
    public double LlmTimeout { get; set; } = LlmDefaults.DefaultTimeoutSeconds;
    public string LlmTargetLanguage { get; set; } = PromptRenderer.DefaultTargetLanguage;

    /// <summary>Ask GitHub once a day whether a newer release exists.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Date (yyyy-MM-dd) of the last update check.</summary>
    public string LastUpdateCheck { get; set; } = "";

    /// <summary>Set once the Python app's files were looked for, so the import happens only once.</summary>
    public bool LegacyImported { get; set; }

    /// <summary>Set once "start with the system" got its on-by-default registration (<c>AutostartDefault</c>).</summary>
    public bool AutostartDefaultApplied { get; set; }

    /// <summary>How the skill donut looks and where it opens. Missing values keep today's behaviour.</summary>
    public DonutSettings Donut { get; set; } = new();

    /// <summary>The request settings; keys are added by the caller from the secret store.</summary>
    public LlmConfig ToLlmConfig(string apiKey = "", string fallbackApiKey = "") => new()
    {
        Enabled = LlmEnabled,
        Provider = LlmProvider,
        ApiKey = apiKey,
        Model = LlmModel,
        FallbackProvider = LlmFallbackProvider,
        FallbackApiKey = fallbackApiKey,
        FallbackModel = LlmFallbackModel,
        BaseUrl = LlmBaseUrl,
        TimeoutSeconds = LlmTimeout,
        TargetLanguage = LlmTargetLanguage,
    };
}

/// <summary>Source-generated JSON metadata: no reflection at start-up, and safe for trimming.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
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
