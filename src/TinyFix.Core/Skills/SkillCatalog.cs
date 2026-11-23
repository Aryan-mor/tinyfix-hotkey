// Defaults and operations on skills: built-ins, conflicts, ordering, rendering. Ported from skills.py.

using TinyFix.Core.Hotkeys;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;

namespace TinyFix.Core.Skills;

/// <summary>Two enabled skills (or a skill and the menu) that share a shortcut.</summary>
public sealed record HotkeyConflict(string Left, string Right, string Hotkey)
{
    /// <summary>English sentence for logs and tests; the UI builds its own localized text.</summary>
    public string Describe() => $"{Left} and {Right} both use {Hotkey}";
}

/// <summary>Settings of the pre-skills Python app that seed the built-ins on first start.</summary>
public sealed record LegacySkillOptions
{
    public string FixHotkey { get; init; } = HotkeyDefaults.Fix;
    public string FixAllHotkey { get; init; } = HotkeyDefaults.FixAll;
    public string TranslateHotkey { get; init; } = HotkeyDefaults.Translate;
    public bool TranslateSelectAll { get; init; }
    public string FixPrompt { get; init; } = DefaultPrompts.Fix;
    public string TranslatePrompt { get; init; } = DefaultPrompts.Translate;
}

/// <summary>Skill defaults and the rules for changing a store.</summary>
public static class SkillCatalog
{
    /// <summary>Example skills offered as a starting point (name, prompt).</summary>
    public static IReadOnlyList<(string Name, string Prompt)> ExampleSpecs { get; } =
    [
        ("Make formal email",
            "Rewrite the text below as a clear, polite formal email. Keep the meaning, tighten the wording, and use a professional tone. Return only the email body.\n\n{{content}}\n"),
        ("Summarize",
            "Summarize the text below in a few short sentences. Keep the key facts and the original language. Return only the summary.\n\n{{content}}\n"),
        ("Translate to German",
            "Translate the text below into German. Keep the meaning, tone, and line breaks. Return only the translation.\n\n{{content}}\n"),
    ];

    /// <summary>The six built-in skills in their default order.</summary>
    public static List<Skill> DefaultSkills(LegacySkillOptions? legacy = null)
    {
        legacy ??= new LegacySkillOptions();
        return
        [
            Make(SkillIds.Fix, "Fix", InputModes.Selection, Or(legacy.FixPrompt, DefaultPrompts.Fix), legacy.FixHotkey, HotkeyDefaults.Fix, true),
            Make(SkillIds.FixAll, "Fix all", InputModes.SelectAll, Or(legacy.FixPrompt, DefaultPrompts.Fix), legacy.FixAllHotkey, HotkeyDefaults.FixAll, true),
            Make(SkillIds.Translate, "Translate", legacy.TranslateSelectAll ? InputModes.SelectAll : InputModes.Selection,
                Or(legacy.TranslatePrompt, DefaultPrompts.Translate), legacy.TranslateHotkey, HotkeyDefaults.Translate, true),
            Make(SkillIds.Rewrite, "Rewrite", InputModes.Selection, DefaultPrompts.Rewrite, "none", "none", false),
            Make(SkillIds.RewriteLanguage, "Rewrite to target language", InputModes.Selection, DefaultPrompts.RewriteLanguage, "none", "none", false),
            Make(SkillIds.ImprovePrompt, "Improve prompt", InputModes.Selection, DefaultPrompts.ImprovePrompt, "none", "none", true, ModelModes.Better),
        ];
    }

    private static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    private static Skill Make(string id, string name, string input, string prompt, string hotkey, string defaultHotkey, bool enabled,
        string mode = ModelModes.Default) =>
        new Skill
        {
            Id = id,
            Name = name,
            InputMode = input,
            Prompt = prompt,
            Hotkey = HotkeyParser.Normalize(hotkey, defaultHotkey),
            Enabled = enabled,
            Builtin = true,
            ModelMode = mode,
        }.Normalize();

    /// <summary>A store with the default skills.</summary>
    public static SkillStore DefaultStore(LegacySkillOptions? legacy = null) =>
        new() { Skills = DefaultSkills(legacy), SkillMenuHotkey = HotkeyDefaults.Menu };

    /// <summary>
    /// Adds missing built-ins (core ones first, optional ones last unless dismissed), repairs their flags and upgrades
    /// unedited copies of older default prompts. Customised prompts are left alone.
    /// </summary>
    public static SkillStore EnsureBuiltins(SkillStore store, LegacySkillOptions? legacy = null)
    {
        var have = store.Skills.Select(s => s.Id).ToHashSet();
        var defaults = DefaultSkills(legacy);
        var missingCore = defaults.Where(s => SkillIds.CoreBuiltin.Contains(s.Id) && !have.Contains(s.Id)).ToList();
        var gone = store.Dismissed.ToHashSet();
        var missingOptional = defaults.Where(s => SkillIds.OptionalBuiltin.Contains(s.Id) && !have.Contains(s.Id) && !gone.Contains(s.Id)).ToList();
        if (missingCore.Count > 0)
            store.Skills = [.. missingCore, .. store.Skills];
        if (missingOptional.Count > 0)
            store.Skills = [.. store.Skills, .. missingOptional];

        foreach (var skill in store.Skills)
        {
            if (skill.Id is SkillIds.Fix or SkillIds.FixAll)
            {
                skill.Builtin = true;
                skill.OfflineFallback = true;
            }
            else if (skill.Id is SkillIds.Translate or SkillIds.Rewrite or SkillIds.RewriteLanguage or SkillIds.ImprovePrompt)
            {
                skill.Builtin = true;
                skill.OfflineFallback = false;
            }
            if (skill.Id == SkillIds.ImprovePrompt && IsLegacy(skill.Prompt, DefaultPrompts.LegacyImprove))
                skill.Prompt = DefaultPrompts.ImprovePrompt;
            if (skill.Id is SkillIds.Fix or SkillIds.FixAll && IsLegacy(skill.Prompt, DefaultPrompts.LegacyFix))
                skill.Prompt = DefaultPrompts.Fix;
            if (skill.Id == SkillIds.Translate && IsLegacy(skill.Prompt, DefaultPrompts.LegacyTranslate))
                skill.Prompt = DefaultPrompts.Translate;
        }
        return store;
    }

    private static bool IsLegacy(string prompt, IReadOnlyList<string> legacy) => legacy.Any(p => p.Trim() == prompt.Trim());

    /// <summary>A new user skill.</summary>
    public static Skill NewCustomSkill(string name = "New skill", string? prompt = null) =>
        new Skill
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Prompt = prompt ?? PromptRenderer.DefaultCustomPrompt,
        }.Normalize();

    /// <summary>A copy with a new id, no shortcut and no built-in flags.</summary>
    public static Skill Duplicate(Skill skill)
    {
        var copy = skill.Clone();
        copy.Id = Guid.NewGuid().ToString();
        copy.Name = $"{skill.Name} copy";
        copy.Hotkey = "none";
        copy.Builtin = false;
        copy.OfflineFallback = false;
        copy.Enabled = true;
        return copy.Normalize();
    }

    /// <summary>Fix, Fix all and Translate can only be turned off.</summary>
    public static bool CanDelete(Skill skill) => !SkillIds.CoreBuiltin.Contains(skill.Id);

    /// <summary>Removes a skill (remembering a deleted optional built-in). False when it may not be deleted.</summary>
    public static bool Delete(SkillStore store, string skillId)
    {
        var skill = store.Get(skillId);
        if (skill is null || !CanDelete(skill))
            return false;
        store.Skills = store.Skills.Where(s => s.Id != skillId).ToList();
        if (SkillIds.OptionalBuiltin.Contains(skillId) && !store.Dismissed.Contains(skillId))
            store.Dismissed.Add(skillId);
        return true;
    }

    /// <summary>Moves a skill up (<paramref name="delta"/> -1) or down (+1). False when it cannot move.</summary>
    public static bool Move(SkillStore store, string skillId, int delta)
    {
        var index = store.Skills.FindIndex(s => s.Id == skillId);
        if (index < 0 || delta == 0)
            return false;
        var target = index + delta;
        if (target < 0 || target >= store.Skills.Count)
            return false;
        (store.Skills[index], store.Skills[target]) = (store.Skills[target], store.Skills[index]);
        return true;
    }

    /// <summary>The skill's prompt with its placeholders filled.</summary>
    public static string RenderPrompt(Skill skill, string content, string? language = null) =>
        PromptRenderer.Render(skill.Prompt, content, language);

    /// <summary>
    /// Pairs of enabled skills (and the skill menu) with the same key and modifiers. Disabled skills and shortcuts that
    /// are off or invalid never conflict. <paramref name="menuName"/> is how the menu is named in the result.
    /// </summary>
    public static List<HotkeyConflict> FindConflicts(IEnumerable<Skill> skills, string? menuHotkey = null, string menuName = "Skill menu")
    {
        var entries = new List<(string Name, Hotkey Key)>();
        foreach (var skill in skills)
        {
            if (!skill.Enabled)
                continue;
            var key = skill.HotkeyValue();
            if (!key.IsDisabled)
                entries.Add((skill.Name, key));
        }
        var menu = HotkeyParser.ParseOrDisabled(menuHotkey);
        if (!menu.IsDisabled)
            entries.Add((menuName, menu));

        var conflicts = new List<HotkeyConflict>();
        for (var i = 0; i < entries.Count; i++)
            for (var j = i + 1; j < entries.Count; j++)
                if (entries[i].Key.Conflicts(entries[j].Key))
                    conflicts.Add(new HotkeyConflict(entries[i].Name, entries[j].Name, entries[i].Key.Display));
        return conflicts;
    }

    /// <summary>Enabled skills whose model <paramref name="isUsable"/> accepts. A skill that needs a model is left out when its own model is not usable.</summary>
    public static List<Skill> EnabledSkills(SkillStore store, Func<Skill, bool> isUsable) =>
        store.Skills.Where(s => s.Enabled && (!s.NeedsAi || isUsable(s))).ToList();

    /// <summary>The skills the selector lists: every enabled one (AI ones are shown greyed when AI is off).</summary>
    public static List<Skill> VisibleMenuSkills(SkillStore store) => store.Skills.Where(s => s.Enabled).ToList();

    /// <summary>Maps the built-ins back to the older single-purpose settings.</summary>
    public static LegacySkillOptions LegacyFromStore(SkillStore store)
    {
        var defaults = DefaultSkills();
        var fix = store.Get(SkillIds.Fix) ?? defaults[0];
        var fixAll = store.Get(SkillIds.FixAll) ?? defaults[1];
        var translate = store.Get(SkillIds.Translate) ?? defaults[2];
        return new LegacySkillOptions
        {
            FixHotkey = fix.Hotkey,
            FixAllHotkey = fixAll.Hotkey,
            TranslateHotkey = translate.Hotkey,
            TranslateSelectAll = translate.SelectAll,
            FixPrompt = fix.Prompt,
            TranslatePrompt = translate.Prompt,
        };
    }
}
