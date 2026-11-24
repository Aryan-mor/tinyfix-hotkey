// General page: UI language, theme, start with the system and platform notices. The skill-menu shortcut is on the
// Donut page; the conflict text it shares with the Skills page is built here.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TinyFix.App.Services;
using TinyFix.Core.Skills;

namespace TinyFix.App.ViewModels;

/// <summary>A language choice in the picker. The instance stays put when the UI language changes, so the combo keeps its selection.</summary>
public sealed partial class LanguageOption : ObservableObject
{
    /// <summary>Creates a choice.</summary>
    public LanguageOption(string code, string name)
    {
        Code = code;
        _name = name;
    }

    /// <summary>The code stored in settings (<c>auto</c>, <c>en</c>, ...).</summary>
    public string Code { get; }

    /// <summary>The name drawn in the combo. Only <c>auto</c> changes with the UI language; the others are native names.</summary>
    [ObservableProperty]
    private string _name;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>View model of the General page.</summary>
public sealed partial class GeneralPageViewModel : PageViewModel
{
    private readonly AppServices _s;
    private bool _loading = true;

    /// <summary>Creates the page.</summary>
    public GeneralPageViewModel(AppServices services)
        : base(services.Loc, "nav.general")
    {
        _s = services;
        // Built once. Replacing these instances (or clearing the list) makes the combo's selection box go blank:
        // the selected object is no longer in the list, and a value-equal replacement does not notify the binding.
        Languages.Add(new LanguageOption("auto", _s.Loc["settings.language_auto"]));
        foreach (var (code, name) in AppLanguages.All)
            Languages.Add(new LanguageOption(code, name));
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == _s.Store.Settings.Language) ?? Languages[0];
        _themeIndex = _s.Store.Settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        _autostart = _s.Platform.Autostart.IsEnabled;
        _keepPrevious = _s.Store.Settings.KeepPreviousOnClipboard;
        _changeToast = _s.Store.Settings.ShowChangeToast;
        PlatformNotice = _s.Platform.Capabilities.IsWayland ? _s.Loc["status.wayland"] : "";
        _loading = false;
        Track();
    }

    /// <summary>"Automatic" followed by every shipped language, each in its own name.</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = [];

    /// <summary>The chosen language.</summary>
    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    /// <summary>0 system, 1 light, 2 dark.</summary>
    [ObservableProperty]
    private int _themeIndex;

    /// <summary>Whether TinyFix starts when the user logs in.</summary>
    [ObservableProperty]
    private bool _autostart;

    /// <summary>Leave the replaced text on the clipboard.</summary>
    [ObservableProperty]
    private bool _keepPrevious;

    /// <summary>Show the "changed" toast with Undo.</summary>
    [ObservableProperty]
    private bool _changeToast;

    partial void OnKeepPreviousChanged(bool value)
    {
        if (_loading)
            return;
        _s.Store.Settings.KeepPreviousOnClipboard = value;
        _s.Store.SaveSettings();
    }

    partial void OnChangeToastChanged(bool value)
    {
        if (_loading)
            return;
        _s.Store.Settings.ShowChangeToast = value;
        _s.Store.SaveSettings();
    }

    /// <summary>A note about the desktop (Wayland), or empty.</summary>
    public string PlatformNotice { get; }

    /// <inheritdoc />
    protected override void OnLanguageChanged(object? sender, EventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        // Update the "Automatic" caption in place. The selected item stays the same object.
        if (Languages.Count > 0)
            Languages[0].Name = _s.Loc["settings.language_auto"];
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_loading || value is null)
            return;
        _s.Store.Settings.Language = value.Code;
        _s.Store.SaveSettings();
        _s.Loc.SetLanguage(value.Code);
        _s.Theme.Apply(_s.Loc.Language);
        _s.Tray.Refresh();
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading)
            return;
        _s.Store.Settings.Theme = value switch { 1 => "light", 2 => "dark", _ => "system" };
        _s.Store.SaveSettings();
        _s.Theme.Apply(_s.Loc.Language);
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_loading)
            return;
        // Only on the user's explicit action; never started by the app on its own.
        if (value)
            _s.Platform.Autostart.Enable(Environment.ProcessPath ?? "");
        else
            _s.Platform.Autostart.Disable();
    }
}

/// <summary>Builds the localized conflict message.</summary>
public static class ConflictText
{
    /// <summary>One line per conflicting pair, or an empty string.</summary>
    public static string Build(AppServices s)
    {
        var skills = s.Store.Skills;
        var menuName = s.Loc["tray.skill_menu"];
        var conflicts = SkillCatalog.FindConflicts(skills.Skills, skills.SkillMenuHotkey, menuName);
        string Name(string raw) =>
            skills.Skills.FirstOrDefault(k => k.Name == raw) is { } skill ? SkillNames.Display(skill, s.Loc) : raw;
        return string.Join('\n', conflicts.Select(c => s.Loc.Format("skills.conflict", Name(c.Left), Name(c.Right), c.Hotkey)));
    }
}
