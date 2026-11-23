// Variables page: named values a prompt can insert with {{name}}. language is built in and cannot be removed.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Prompts;

namespace TinyFix.App.ViewModels;

/// <summary>One variable row.</summary>
public sealed partial class VariableRowViewModel : ObservableObject
{
    private readonly VariablesPageViewModel _page;

    /// <summary>Creates a row. <paramref name="builtIn"/> rows keep their name.</summary>
    public VariableRowViewModel(VariablesPageViewModel page, string name, string value, bool builtIn)
    {
        _page = page;
        IsBuiltIn = builtIn;
        _name = name;
        _value = value;
    }

    /// <summary>True for <c>language</c>: the name stays, the value can change, and the row cannot be removed.</summary>
    public bool IsBuiltIn { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _value;

    /// <summary>Why this row cannot be saved, or empty.</summary>
    [ObservableProperty]
    private string _error = "";

    partial void OnNameChanged(string value) => _page.OnRowEdited();

    partial void OnValueChanged(string value) => _page.OnRowEdited();

    /// <summary>Removes this row.</summary>
    [RelayCommand]
    private void Remove() => _page.Remove(this);
}

/// <summary>View model of the Variables page.</summary>
public sealed partial class VariablesPageViewModel : PageViewModel
{
    private readonly AppServices _s;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _loading = true;

    /// <summary>Creates the page from the saved variables.</summary>
    public VariablesPageViewModel(AppServices services)
        : base(services.Loc, "nav.variables")
    {
        _s = services;
        Reload();
        _save.Tick += (_, _) => Commit();
        _loading = false;
        Track();
    }

    /// <summary>The rows, with <c>language</c> first.</summary>
    public ObservableCollection<VariableRowViewModel> Rows { get; } = [];

    /// <summary>Adds an empty row.</summary>
    [RelayCommand]
    private void Add()
    {
        Rows.Add(new VariableRowViewModel(this, "", "", builtIn: false));
        OnRowEdited();
    }

    /// <summary>Removes a row the user added.</summary>
    public void Remove(VariableRowViewModel row)
    {
        if (row.IsBuiltIn)
            return;
        Rows.Remove(row);
        OnRowEdited();
    }

    /// <summary>Validates and schedules a save.</summary>
    public void OnRowEdited()
    {
        if (_loading)
            return;
        Validate();
        _save.Stop();
        _save.Start();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_save.IsEnabled)
            Commit();
        base.Dispose();
    }

    private void Reload()
    {
        Rows.Clear();
        var variables = _s.Store.Settings.Variables;
        var language = variables.TryGetValue(VariableNames.Language, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : PromptRenderer.DefaultTargetLanguage;
        Rows.Add(new VariableRowViewModel(this, VariableNames.Language, language, builtIn: true));
        foreach (var (name, text) in variables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (name == VariableNames.Language || name == VariableNames.Content)
                continue;
            Rows.Add(new VariableRowViewModel(this, name, text ?? "", builtIn: false));
        }
    }

    private bool Validate()
    {
        var ok = true;
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (row.IsBuiltIn)
            {
                row.Error = "";
                continue;
            }
            var others = Rows.Where((other, index) => index != i && !other.IsBuiltIn).Select(other => other.Name.Trim());
            // language is taken, so a second "language" is a duplicate too.
            others = others.Append(VariableNames.Language);
            var code = VariableNames.Check(row.Name.Trim(), others);
            row.Error = code switch
            {
                "reserved" => _s.Loc["variables.reserved"],
                "invalid" => _s.Loc["variables.invalid"],
                "duplicate" => _s.Loc["variables.duplicate"],
                _ => "",
            };
            if (code is not null)
                ok = false;
        }
        return ok;
    }

    private void Commit()
    {
        _save.Stop();
        if (_loading || !Validate())
            return;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Rows)
            map[row.Name.Trim()] = row.Value ?? "";
        _s.Store.Settings.Variables = map;
        _s.Store.SaveSettings();
    }
}
