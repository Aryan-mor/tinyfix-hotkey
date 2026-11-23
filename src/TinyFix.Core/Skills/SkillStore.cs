// The list of skills plus the skill-menu shortcut. Ported from skills.py (SkillStore).

using TinyFix.Core.Hotkeys;

namespace TinyFix.Core.Skills;

/// <summary>What skills.json holds.</summary>
public sealed class SkillStore
{
    /// <summary>Schema version of skills.json.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>The skills in menu order.</summary>
    public List<Skill> Skills { get; set; } = [];

    /// <summary>Shortcut that opens the skill selector.</summary>
    public string SkillMenuHotkey { get; set; } = HotkeyDefaults.Menu;

    /// <summary>Ids of optional built-in skills the user deleted; they are not added back on the next start.</summary>
    public List<string> Dismissed { get; set; } = [];

    /// <summary>The skill with this id, or null.</summary>
    public Skill? Get(string id) => Skills.FirstOrDefault(s => s.Id == id);

    /// <summary>The parsed skill-menu shortcut.</summary>
    public Hotkey MenuHotkey() => HotkeyParser.ParseOrDisabled(SkillMenuHotkey);
}
