// Skills page: the list of skills with add, duplicate, delete and reorder, and an editor for the selected one.
// Every edit is saved (debounced, atomically) and re-registers the shortcuts.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;
using TinyFix.Core.Skills;

namespace TinyFix.App.ViewModels;

/// <summary>One row of the skill list.</summary>
public sealed partial class SkillRowViewModel(Skill skill, AppServices services) : ObservableObject
{
    /// <summary>The skill behind the row.</summary>
    public Skill Skill { get; } = skill;

    /// <summary>The name shown in the list.</summary>
    public string Title => SkillNames.Display(Skill, services.Loc);

    /// <summary>Shortcut and flags under the name.</summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            parts.Add(Skill.HotkeyValue().IsDisabled ? services.Loc["shortcut.none"] : Skill.DisplayHotkey());
            if (Skill.Builtin)
                parts.Add(services.Loc["skills.builtin"]);
            if (Skill.NeedsAi)
                parts.Add(services.Loc["skills.needs_ai"]);
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Opacity of the row: disabled skills are dimmed.</summary>
    public double Dim => Skill.Enabled ? 1.0 : 0.5;

    /// <summary>Raises change notifications after the skill was edited.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Dim));
    }
}

/// <summary>View model of the Skills page.</summary>
public sealed partial class SkillsPageViewModel : PageViewModel
{
    private static readonly string[] ModelModeIds = [ModelModes.Default, ModelModes.Better, ModelModes.Custom];
    private static readonly string[] ProviderIds = [.. LlmDefaults.Providers];

    private readonly AppServices _s;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loading;

    /// <summary>Creates the page.</summary>
    public SkillsPageViewModel(AppServices services)
        : base(services.Loc, "nav.skills")
    {
        _s = services;
        foreach (var skill in _s.Store.Skills.Skills)
            Rows.Add(new SkillRowViewModel(skill, _s));
        _save.Tick += (_, _) => Flush();
        Selected = Rows.FirstOrDefault();
        UpdateConflicts();
        Track();
    }

    /// <summary>The skills in menu order.</summary>
    public ObservableCollection<SkillRowViewModel> Rows { get; } = [];

    /// <summary>The selected row, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanDelete), nameof(IsCustomModel))]
    private SkillRowViewModel? _selected;

    /// <summary>Text about shortcut conflicts, or empty.</summary>
    [ObservableProperty]
    private string _conflicts = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _enabled;

    /// <summary>0 current selection, 1 select the whole field first.</summary>
    [ObservableProperty]
    private int _inputIndex;

    [ObservableProperty]
    private string _shortcut = "none";

    /// <summary>0 online AI settings, 1 stronger model, 2 custom.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomModel))]
    private int _modelModeIndex;

    [ObservableProperty]
    private int _providerIndex;

    [ObservableProperty]
    private string _modelName = "";

    [ObservableProperty]
    private string _fallbackModel = "";

    [ObservableProperty]
    private string _prompt = "";

    /// <summary>True when a skill is selected.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>True when the selected skill may be deleted (not Fix, Fix all or Translate).</summary>
    public bool CanDelete => Selected is { } row && SkillCatalog.CanDelete(row.Skill);

    /// <summary>True when the custom model fields apply.</summary>
    public bool IsCustomModel => ModelModeIndex == 2 && Selected is not null;

    partial void OnSelectedChanged(SkillRowViewModel? value)
    {
        _loading = true;
        if (value is { } row)
        {
            var skill = row.Skill;
            Name = SkillNames.Display(skill, _s.Loc);
            Enabled = skill.Enabled;
            InputIndex = skill.SelectAll ? 1 : 0;
            Shortcut = skill.Hotkey;
            ModelModeIndex = Math.Max(0, Array.IndexOf(ModelModeIds, skill.ModelMode));
            ProviderIndex = Math.Max(0, Array.IndexOf(ProviderIds, string.IsNullOrEmpty(skill.ModelProvider) ? _s.Store.Settings.LlmProvider : skill.ModelProvider));
            ModelName = skill.Model;
            FallbackModel = skill.ModelFallback;
            Prompt = skill.Prompt;
        }
        _loading = false;
    }

    partial void OnNameChanged(string value) => Edit(skill =>
    {
        // Leaving a built-in's name as shown keeps the stored English name, so it stays translatable.
        if (value != SkillNames.Display(skill, _s.Loc))
            skill.Name = string.IsNullOrWhiteSpace(value) ? _s.Loc["skills.new_name"] : value.Trim();
    });

    partial void OnEnabledChanged(bool value) => Edit(skill => skill.Enabled = value);

    partial void OnInputIndexChanged(int value) => Edit(skill => skill.InputMode = value == 1 ? InputModes.SelectAll : InputModes.Selection);

    partial void OnShortcutChanged(string value) => Edit(skill => skill.Hotkey = value);

    partial void OnModelModeIndexChanged(int value) => Edit(skill => skill.ModelMode = ModelModeIds[Math.Clamp(value, 0, 2)]);

    partial void OnProviderIndexChanged(int value) => Edit(skill => skill.ModelProvider = ProviderIds[Math.Clamp(value, 0, ProviderIds.Length - 1)]);

    partial void OnModelNameChanged(string value) => Edit(skill => skill.Model = value);

    partial void OnFallbackModelChanged(string value) => Edit(skill => skill.ModelFallback = value);

    partial void OnPromptChanged(string value) => Edit(skill => skill.Prompt = value);

    private void Edit(Action<Skill> change)
    {
        if (_loading || Selected is not { } row)
            return;
        change(row.Skill);
        row.Skill.Normalize();
        row.Refresh();
        UpdateConflicts();
        // Typing a prompt must not write the file on every key: wait until the user pauses.
        _save.Stop();
        _save.Start();
    }

    /// <summary>Writes pending edits now and re-registers the shortcuts.</summary>
    public void Flush()
    {
        _save.Stop();
        _s.Store.SaveSkills();
        AppHost.ApplyHotkeys();
    }

    private void UpdateConflicts() => Conflicts = ConflictText.Build(_s);

    /// <summary>Adds a new custom skill and selects it.</summary>
    [RelayCommand]
    private void Add()
    {
        var skill = SkillCatalog.NewCustomSkill(_s.Loc["skills.new_name"]);
        _s.Store.Skills.Skills.Add(skill);
        var row = new SkillRowViewModel(skill, _s);
        Rows.Add(row);
        Selected = row;
        Flush();
    }

    /// <summary>Duplicates the selected skill.</summary>
    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is not { } row)
            return;
        var copy = SkillCatalog.Duplicate(row.Skill);
        var index = _s.Store.Skills.Skills.IndexOf(row.Skill) + 1;
        _s.Store.Skills.Skills.Insert(index, copy);
        var copyRow = new SkillRowViewModel(copy, _s);
        Rows.Insert(index, copyRow);
        Selected = copyRow;
        Flush();
    }

    /// <summary>Deletes the selected skill (not the core built-ins).</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { } row || !SkillCatalog.Delete(_s.Store.Skills, row.Skill.Id))
            return;
        var index = Rows.IndexOf(row);
        Rows.Remove(row);
        Selected = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        UpdateConflicts();
        Flush();
    }

    /// <summary>Moves the selected skill up.</summary>
    [RelayCommand]
    private void MoveUp() => Move(-1);

    /// <summary>Moves the selected skill down.</summary>
    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (Selected is not { } row || !SkillCatalog.Move(_s.Store.Skills, row.Skill.Id, delta))
            return;
        var index = Rows.IndexOf(row);
        Rows.Move(index, index + delta);
        Flush();
    }

    /// <summary>Puts the default prompt of the selected skill back.</summary>
    [RelayCommand]
    private void ResetPrompt()
    {
        if (Selected is { } row)
            Prompt = PromptRenderer.DefaultFor(row.Skill.Id);
    }

    /// <inheritdoc />
    protected override void OnLanguageChanged(object? sender, EventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        foreach (var row in Rows)
            row.Refresh();
        UpdateConflicts();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_save.IsEnabled)
            Flush();
        base.Dispose();
    }
}
