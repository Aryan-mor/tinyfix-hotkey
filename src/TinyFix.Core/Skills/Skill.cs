// One skill: a named prompt with an input mode, an optional shortcut and a model choice. Ported from skills.py.
// The stored form stays compatible with the Python app's skills.json (snake_case keys, same value spellings).

using TinyFix.Core.Hotkeys;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;

namespace TinyFix.Core.Skills;

/// <summary>Ids of the built-in skills.</summary>
public static class SkillIds
{
    public const string Fix = "fix";
    public const string FixAll = "fix-all";
    public const string Translate = "translate";
    public const string Rewrite = "rewrite";
    public const string RewriteLanguage = "rewrite-language";
    public const string ImprovePrompt = "improve-prompt";
    public const string LayoutAnyFa = "layout-any-fa";
    public const string LayoutSwap = "layout-swap";

    /// <summary>Every built-in id.</summary>
    public static IReadOnlyList<string> Builtin { get; } = [Fix, FixAll, Translate, Rewrite, RewriteLanguage, ImprovePrompt, LayoutAnyFa, LayoutSwap];

    /// <summary>Built-ins that can only be turned off: the app's fallback and old settings rely on them.</summary>
    public static IReadOnlyList<string> CoreBuiltin { get; } = [Fix, Translate];

    /// <summary>Built-ins a user may delete; a deleted one is not added back on the next start.</summary>
    public static IReadOnlyList<string> OptionalBuiltin { get; } = [Rewrite, RewriteLanguage, ImprovePrompt, LayoutAnyFa, LayoutSwap];
}

/// <summary>What a skill does with its text.</summary>
public static class SkillKinds
{
    /// <summary>Sends the text through an AI model with the skill's prompt.</summary>
    public const string Ai = "ai";

    /// <summary>Re-types text typed on one keyboard layout as another layout (no AI, no internet).</summary>
    public const string Layout = "layout";

    /// <summary>Swaps two layouts: text typed on A becomes B's and the other way round.</summary>
    public const string Swap = "swap";

    /// <summary>Maps loose spellings to a valid kind.</summary>
    public static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        Layout => Layout,
        Swap => Swap,
        _ => Ai,
    };
}

/// <summary>How a skill gets its text.</summary>
public static class InputModes
{
    /// <summary>The current selection.</summary>
    public const string Selection = "selection";

    /// <summary>Select the whole field first.</summary>
    public const string SelectAll = "select_all";

    /// <summary>The selection when there is one, else the whole field.</summary>
    public const string Auto = "auto";

    /// <summary>Maps loose spellings ("Select all", "all") to a valid mode.</summary>
    public static string Normalize(string? value)
    {
        var raw = (value ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        if (raw == "auto")
            return Auto;
        return raw is "select_all" or "selectall" or "all" ? SelectAll : Selection;
    }
}

/// <summary>A skill. Call <see cref="Normalize"/> after changing fields by hand.</summary>
public sealed class Skill
{
    /// <summary>Stable id (a GUID for custom skills).</summary>
    public string Id { get; set; } = "";

    /// <summary>Name shown in menus and settings.</summary>
    public string Name { get; set; } = "New skill";

    /// <summary>See <see cref="SkillKinds"/>.</summary>
    public string Kind { get; set; } = SkillKinds.Ai;

    /// <summary>Layout kinds: the layout the text was typed on (a language code such as "en", or "any"). For a swap: the first language.</summary>
    public string LayoutFrom { get; set; } = "any";

    /// <summary>Layout kinds: the layout the text should have been typed on. For a swap: the second language.</summary>
    public string LayoutTo { get; set; } = "fa";

    /// <summary>See <see cref="InputModes"/>.</summary>
    public string InputMode { get; set; } = InputModes.Auto;

    /// <summary>Prompt template with <c>{{content}}</c> and <c>{{language}}</c>.</summary>
    public string Prompt { get; set; } = PromptRenderer.DefaultCustomPrompt;

    /// <summary>Shortcut text, or <c>none</c>.</summary>
    public string Hotkey { get; set; } = "none";

    public bool Enabled { get; set; } = true;
    public bool Builtin { get; set; }

    /// <summary>
    /// True for Fix and Fix all in the Python app, where they could fall back to an offline engine. Kept so skills.json
    /// stays compatible; the C# app has no offline engine and the flag has no effect.
    /// </summary>
    public bool OfflineFallback { get; set; }

    /// <summary>See <see cref="ModelModes"/>.</summary>
    public string ModelMode { get; set; } = ModelModes.Default;
    public string ModelProvider { get; set; } = "";
    public string Model { get; set; } = "";
    public string ModelFallbackProvider { get; set; } = "";
    public string ModelFallback { get; set; } = "";

    /// <summary>Base URL of a custom main endpoint. Empty keeps the preset or the global URL. Older files omit it.</summary>
    public string ModelBaseUrl { get; set; } = "";

    /// <summary>Base URL of a custom fallback endpoint. Empty keeps the preset or the global URL.</summary>
    public string ModelFallbackBaseUrl { get; set; } = "";

    /// <summary>Timeout of a custom main endpoint, in seconds. Zero keeps the global timeout.</summary>
    public double ModelTimeout { get; set; }

    /// <summary>Timeout of a custom fallback endpoint, in seconds. Zero keeps the global timeout.</summary>
    public double ModelFallbackTimeout { get; set; }

    /// <summary>True when the skill cannot run without a configured AI model: every skill but a layout conversion.</summary>
    public bool NeedsAi => Kind == SkillKinds.Ai;

    /// <summary>True for the layout kinds (convert, swap).</summary>
    public bool IsLayout => Kind != SkillKinds.Ai;

    /// <summary>True when the whole field is selected first.</summary>
    public bool SelectAll => InputMode == InputModes.SelectAll;

    /// <summary>True when the selection is used if there is one, and the whole field otherwise.</summary>
    public bool Auto => InputMode == InputModes.Auto;

    /// <summary>The parsed shortcut (disabled when the text is empty or invalid).</summary>
    public Hotkey HotkeyValue() => HotkeyParser.ParseOrDisabled(Hotkey);

    /// <summary>Shortcut text for display, "None" when off.</summary>
    public string DisplayHotkey()
    {
        var key = HotkeyValue();
        return key.IsDisabled ? "None" : key.Display;
    }

    /// <summary>The skill's own model choice. Empty fields mean the global endpoint.</summary>
    public SkillModelChoice ModelChoice() =>
        new(ModelProvider, Model, ModelFallbackProvider, ModelFallback, ModelBaseUrl, ModelFallbackBaseUrl, ModelTimeout, ModelFallbackTimeout);

    /// <summary>Repairs every field the way the Python constructor does.</summary>
    public Skill Normalize()
    {
        ModelMode = ModelModes.All.Contains(ModelMode) ? ModelMode : ModelModes.Default;
        ModelProvider = (ModelProvider ?? "").Trim();
        Model = (Model ?? "").Trim();
        ModelFallbackProvider = (ModelFallbackProvider ?? "").Trim();
        ModelFallback = (ModelFallback ?? "").Trim();
        ModelBaseUrl = (ModelBaseUrl ?? "").Trim();
        ModelFallbackBaseUrl = (ModelFallbackBaseUrl ?? "").Trim();
        if (!double.IsFinite(ModelTimeout) || ModelTimeout < 0)
            ModelTimeout = 0;
        if (!double.IsFinite(ModelFallbackTimeout) || ModelFallbackTimeout < 0)
            ModelFallbackTimeout = 0;
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString() : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "New skill" : Name.Trim();
        InputMode = InputModes.Normalize(InputMode);
        Kind = SkillKinds.Normalize(Kind);
        LayoutFrom = string.IsNullOrWhiteSpace(LayoutFrom) ? "any" : LayoutFrom.Trim().ToLowerInvariant();
        LayoutTo = string.IsNullOrWhiteSpace(LayoutTo) ? "fa" : LayoutTo.Trim().ToLowerInvariant();
        if (Kind == SkillKinds.Swap && LayoutFrom == "any")
            LayoutFrom = "en";
        Prompt ??= "";
        Hotkey = HotkeyParser.Normalize(Hotkey, "none");
        OfflineFallback = Id is SkillIds.Fix or SkillIds.FixAll;
        return this;
    }

    /// <summary>A deep copy.</summary>
    public Skill Clone() => (Skill)MemberwiseClone();
}
