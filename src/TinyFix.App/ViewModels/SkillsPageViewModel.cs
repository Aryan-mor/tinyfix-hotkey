// Skills page: the list of skills with add, duplicate, delete and reorder, and an editor for the selected one.
// Every edit is saved (debounced, atomically) and re-registers the shortcuts.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Layouts;
using TinyFix.Core.Llm;
using TinyFix.Core.Prompts;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.ViewModels;

/// <summary>One entry of a layout drop-down: a language code (or "any") and the text shown for it.</summary>
public sealed record LayoutChoice(string Id, string Label);

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
            parts.Add(services.Loc[Skill.NeedsAi ? "skills.needs_ai" : "skills.kind_layout"]);
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

    private readonly AppServices _s;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loading;

    /// <summary>Creates the page.</summary>
    public SkillsPageViewModel(AppServices services)
        : base(services.Loc, "nav.skills")
    {
        _s = services;
        MainOverride = new EndpointEditorViewModel(services, LlmSecrets.MainSlot, "skills.override_main", "skills.override_use", allowSkip: true);
        FallbackOverride = new EndpointEditorViewModel(services, LlmSecrets.FallbackSlot, "skills.override_fallback", "skills.override_use", allowSkip: true);
        MainOverride.Changed += (_, _) => ApplyEndpoints();
        FallbackOverride.Changed += (_, _) => ApplyEndpoints();
        foreach (var skill in _s.Store.Skills.Skills)
            Rows.Add(new SkillRowViewModel(skill, _s));
        _save.Tick += (_, _) => Flush();
        _s.Store.Changed += OnStoreChanged;
        Selected = Rows.FirstOrDefault();
        UpdateConflicts();
        Track();
    }

    /// <summary>The skill's own main endpoint, used when the model mode is custom.</summary>
    public EndpointEditorViewModel MainOverride { get; }

    /// <summary>The skill's own fallback endpoint, used when the model mode is custom.</summary>
    public EndpointEditorViewModel FallbackOverride { get; }

    /// <summary>The variables a prompt can insert, including <c>{{content}}</c>.</summary>
    public string VariableHint
    {
        get
        {
            var names = new List<string> { "{{" + VariableNames.Content + "}}" };
            foreach (var key in _s.Store.Settings.Variables.Keys
                         .OrderBy(name => name == VariableNames.Language ? 0 : 1)
                         .ThenBy(name => name, StringComparer.Ordinal))
                names.Add("{{" + key + "}}");
            return _s.Loc.Format("skills.prompt_vars", string.Join(", ", names));
        }
    }

    /// <summary>The skills in menu order.</summary>
    public ObservableCollection<SkillRowViewModel> Rows { get; } = [];

    /// <summary>The selected row, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanDelete), nameof(IsCustomModel), nameof(IsAiSkill), nameof(IsLayoutSkill),
        nameof(IsSwapSkill), nameof(FromLabel), nameof(ToLabel), nameof(LayoutHelp))]
    private SkillRowViewModel? _selected;

    /// <summary>The layout the text was typed on (first language of a swap).</summary>
    [ObservableProperty]
    private LayoutChoice? _layoutFromChoice;

    /// <summary>The layout to convert to (second language of a swap).</summary>
    [ObservableProperty]
    private LayoutChoice? _layoutToChoice;

    /// <summary>The "typed on" choices: every installed layout, plus "any language" for a conversion.</summary>
    public ObservableCollection<LayoutChoice> FromChoices { get; } = [];

    /// <summary>The "convert to" choices: every installed layout.</summary>
    public ObservableCollection<LayoutChoice> ToChoices { get; } = [];

    /// <summary>True when the selected skill goes through an AI model (shows the model and prompt fields).</summary>
    public bool IsAiSkill => Selected is { } row && row.Skill.NeedsAi;

    /// <summary>True when the selected skill converts between keyboard layouts.</summary>
    public bool IsLayoutSkill => Selected is { } row && row.Skill.IsLayout;

    /// <summary>True when the selected skill swaps two languages.</summary>
    public bool IsSwapSkill => Selected is { } row && row.Skill.Kind == SkillKinds.Swap;

    /// <summary>Label of the first layout drop-down.</summary>
    public string FromLabel => _s.Loc[IsSwapSkill ? "skills.layout_a" : "skills.layout_from"];

    /// <summary>Label of the second layout drop-down.</summary>
    public string ToLabel => _s.Loc[IsSwapSkill ? "skills.layout_b" : "skills.layout_to"];

    /// <summary>One sentence about what a layout skill does.</summary>
    public string LayoutHelp => _s.Loc[IsSwapSkill ? "skills.layout_swap_help" : "skills.layout_help"];

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

    /// <summary>0 the models from Settings, 1 a stronger model, 2 this skill's own endpoints.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomModel))]
    private int _modelModeIndex;

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
            InputIndex = skill.Auto ? 0 : skill.SelectAll ? 2 : 1;
            Shortcut = skill.Hotkey;
            ModelModeIndex = Math.Max(0, Array.IndexOf(ModelModeIds, skill.ModelMode));
            var mainSlot = LlmSecrets.SkillMain(skill.Id);
            var fallbackSlot = LlmSecrets.SkillFallback(skill.Id);
            MainOverride.Slot = mainSlot;
            FallbackOverride.Slot = fallbackSlot;
            MainOverride.Load(new LlmEndpointSettings
            {
                Protocol = string.IsNullOrEmpty(skill.ModelProvider) ? _s.Store.Settings.Llm.Main.Protocol : skill.ModelProvider,
                Model = skill.Model,
                BaseUrl = skill.ModelBaseUrl,
                Timeout = skill.ModelTimeout,
            }, use: HasEndpoint(skill.ModelProvider, skill.Model, skill.ModelBaseUrl));
            FallbackOverride.Load(new LlmEndpointSettings
            {
                Protocol = string.IsNullOrEmpty(skill.ModelFallbackProvider) ? _s.Store.Settings.Llm.Main.Protocol : skill.ModelFallbackProvider,
                Model = skill.ModelFallback,
                BaseUrl = skill.ModelFallbackBaseUrl,
                Timeout = skill.ModelFallbackTimeout,
            }, use: HasEndpoint(skill.ModelFallbackProvider, skill.ModelFallback, skill.ModelFallbackBaseUrl));
            Prompt = skill.Prompt;
            LoadLayoutChoices(skill);
        }
        _loading = false;
    }

    private IReadOnlyList<KeyboardLayout> InstalledLayouts() => _s.Platform.Layouts?.Installed() ?? [];

    private string ShortLayout(string id) => id == LayoutConverter.Any ? _s.Loc["skills.layout_any"] : id.ToUpperInvariant();

    private LayoutChoice Describe(string id, IReadOnlyList<KeyboardLayout> installed)
    {
        if (id == LayoutConverter.Any)
            return new LayoutChoice(id, _s.Loc["skills.layout_any"]);
        return LayoutConverter.Find(installed, id) is { } layout
            ? new LayoutChoice(id, $"{layout.Name} ({layout.Language.ToUpperInvariant()})")
            : new LayoutChoice(id, _s.Loc.Format("skills.layout_missing", id.ToUpperInvariant()));
    }

    private void LoadLayoutChoices(Skill skill)
    {
        var installed = InstalledLayouts();
        FromChoices.Clear();
        ToChoices.Clear();
        if (skill.Kind == SkillKinds.Layout)
            FromChoices.Add(Describe(LayoutConverter.Any, installed));
        foreach (var layout in installed)
        {
            FromChoices.Add(Describe(layout.Language, installed));
            ToChoices.Add(Describe(layout.Language, installed));
        }
        // A layout the rule names but this machine does not have stays visible (marked "not installed") instead of vanishing.
        if (FromChoices.All(c => c.Id != skill.LayoutFrom))
            FromChoices.Add(Describe(skill.LayoutFrom, installed));
        if (ToChoices.All(c => c.Id != skill.LayoutTo))
            ToChoices.Add(Describe(skill.LayoutTo, installed));
        var from = skill.LayoutFrom;
        var to = skill.LayoutTo;
        // A swap whose pair is not installed, on a machine with exactly two layouts, shows (and runs) those two.
        if (skill.Kind == SkillKinds.Swap && LayoutConverter.ResolvePair(from, to, installed) is var (a, b))
            (from, to) = (a.Language, b.Language);
        LayoutFromChoice = FromChoices.First(c => c.Id == from);
        LayoutToChoice = ToChoices.First(c => c.Id == to);
    }

    /// <summary>The name a layout skill gets from its two choices.</summary>
    private string AutoName(Skill skill) => skill.Kind == SkillKinds.Swap
        ? _s.Loc.Format("skills.layout_swap_name", ShortLayout(skill.LayoutFrom), ShortLayout(skill.LayoutTo))
        : _s.Loc.Format("skills.layout_name", ShortLayout(skill.LayoutFrom), ShortLayout(skill.LayoutTo));

    private void ChangeLayout(Action<Skill> change)
    {
        if (_loading || Selected is not { } row)
            return;
        var skill = row.Skill;
        // A name nobody typed follows the choice ("Any to FA" becomes "EN to FA"); a name the user wrote stays.
        var follows = skill.Name == AutoName(skill) || SkillNames.IsDefaultName(skill);
        change(skill);
        if (follows)
        {
            skill.Name = AutoName(skill);
            _loading = true;
            Name = skill.Name;
            _loading = false;
        }
        Edit(_ => { });
    }

    partial void OnLayoutFromChoiceChanged(LayoutChoice? value)
    {
        if (value is not null)
            ChangeLayout(skill => skill.LayoutFrom = value.Id);
    }

    partial void OnLayoutToChoiceChanged(LayoutChoice? value)
    {
        if (value is not null)
            ChangeLayout(skill => skill.LayoutTo = value.Id);
    }

    partial void OnNameChanged(string value) => Edit(skill =>
    {
        // Leaving a built-in's name as shown keeps the stored English name, so it stays translatable.
        if (value != SkillNames.Display(skill, _s.Loc))
            skill.Name = string.IsNullOrWhiteSpace(value) ? _s.Loc["skills.new_name"] : value.Trim();
    });

    partial void OnEnabledChanged(bool value) => Edit(skill => skill.Enabled = value);

    partial void OnInputIndexChanged(int value) => Edit(skill => skill.InputMode = value switch
    {
        0 => InputModes.Auto,
        2 => InputModes.SelectAll,
        _ => InputModes.Selection,
    });

    partial void OnShortcutChanged(string value) => Edit(skill => skill.Hotkey = value);

    partial void OnModelModeIndexChanged(int value) => Edit(skill => skill.ModelMode = ModelModeIds[Math.Clamp(value, 0, 2)]);

    partial void OnPromptChanged(string value) => Edit(skill => skill.Prompt = value);

    private static bool HasEndpoint(string provider, string model, string baseUrl) =>
        !string.IsNullOrWhiteSpace(provider) || !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(baseUrl);

    private void ApplyEndpoints()
    {
        Edit(skill =>
        {
            if (MainOverride.UseThis)
            {
                var main = MainOverride.ToSettings();
                skill.ModelProvider = main.Protocol;
                skill.Model = main.Model;
                skill.ModelBaseUrl = main.BaseUrl;
                skill.ModelTimeout = main.Timeout;
            }
            else
            {
                skill.ModelProvider = "";
                skill.Model = "";
                skill.ModelBaseUrl = "";
                skill.ModelTimeout = 0;
            }
            if (FallbackOverride.UseThis)
            {
                var fallback = FallbackOverride.ToSettings();
                skill.ModelFallbackProvider = fallback.Protocol;
                skill.ModelFallback = fallback.Model;
                skill.ModelFallbackBaseUrl = fallback.BaseUrl;
                skill.ModelFallbackTimeout = fallback.Timeout;
            }
            else
            {
                skill.ModelFallbackProvider = "";
                skill.ModelFallback = "";
                skill.ModelFallbackBaseUrl = "";
                skill.ModelFallbackTimeout = 0;
            }
        });
    }

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
        // The key waits until the user pauses, then goes to the skill's own slot (never into skills.json).
        if (MainOverride.UseThis)
            MainOverride.RememberNewKey();
        if (FallbackOverride.UseThis)
            FallbackOverride.RememberNewKey();
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

    /// <summary>Adds a layout conversion (any language to Persian when that layout is installed) and selects it.</summary>
    [RelayCommand]
    private void AddLayout()
    {
        var installed = InstalledLayouts();
        var to = LayoutConverter.Find(installed, "fa")?.Language ?? installed.FirstOrDefault()?.Language ?? "fa";
        var skill = SkillCatalog.NewLayoutSkill("", LayoutConverter.Any, to);
        skill.Name = AutoName(skill);
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

    /// <summary>Picks up skill edits the setup wizard made on the same store.</summary>
    public void RefreshAfterExternalEdit()
    {
        foreach (var row in Rows)
            row.Refresh();
        if (Selected is not { } selected)
        {
            UpdateConflicts();
            return;
        }
        _loading = true;
        Enabled = selected.Skill.Enabled;
        Shortcut = selected.Skill.Hotkey;
        _loading = false;
        UpdateConflicts();
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
        OnPropertyChanged(nameof(VariableHint));
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            OnPropertyChanged(nameof(VariableHint));
        else
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(VariableHint)));
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _s.Store.Changed -= OnStoreChanged;
        if (_save.IsEnabled)
            Flush();
        MainOverride.Dispose();
        FallbackOverride.Dispose();
        base.Dispose();
    }
}
