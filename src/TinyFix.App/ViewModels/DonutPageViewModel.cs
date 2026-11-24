// Donut page: the skill-menu shortcut, the donut's size and opacity, where it opens, and a preview that does not run
// a skill. Size and opacity are written once the slider rests (DonutLookSave); the donut reads them the next time it
// opens. The shortcut stays in skills.json, where it already lived.

using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Platform;
using TinyFix.Core.Settings;

namespace TinyFix.App.ViewModels;

/// <summary>View model of the Donut page.</summary>
public sealed partial class DonutPageViewModel : PageViewModel
{
    private readonly AppServices _s;
    private bool _loading = true;

    /// <summary>Creates the page from the current settings.</summary>
    public DonutPageViewModel(AppServices services)
        : base(services.Loc, "nav.donut")
    {
        _s = services;
        var donut = services.Store.Settings.Donut;
        _outerRadius = DonutSettings.ClampRadius(donut.OuterRadius);
        _opacityPercent = Math.Round(DonutSettings.ClampOpacity(donut.Opacity) * 100);
        _openAtIndex = DonutOpenAt.IsCursor(donut.OpenAt) ? 1 : 0;
        _showShortcutHints = donut.ShowShortcutHints;
        _menuShortcut = services.Store.Skills.SkillMenuHotkey;
        RefreshConflicts();
        _loading = false;
        Track();
    }

    /// <summary>Outer radius in device-independent pixels (110–220).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OuterRadiusText))]
    private double _outerRadius;

    /// <summary>Slice opacity in percent (40–100).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    private double _opacityPercent;

    /// <summary>0 caret, 1 mouse pointer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenAtCaret), nameof(OpenAtCursor))]
    private int _openAtIndex;

    /// <summary>Shortcuts are drawn under the skill names.</summary>
    [ObservableProperty]
    private bool _showShortcutHints;

    /// <summary>The skill-menu shortcut text.</summary>
    [ObservableProperty]
    private string _menuShortcut;

    /// <summary>Text about a shortcut conflict, or empty.</summary>
    [ObservableProperty]
    private string _conflicts = "";

    /// <summary>The radius, for the label beside the slider.</summary>
    public string OuterRadiusText =>
        $"{Math.Round(OuterRadius).ToString(CultureInfo.CurrentCulture)} px";

    /// <summary>The opacity, for the label beside the slider.</summary>
    public string OpacityText =>
        $"{Math.Round(OpacityPercent).ToString(CultureInfo.CurrentCulture)}%";

    /// <summary>The donut opens at the caret.</summary>
    public bool OpenAtCaret
    {
        get => OpenAtIndex == 0;
        set
        {
            if (value)
                OpenAtIndex = 0;
        }
    }

    /// <summary>The donut opens at the mouse pointer.</summary>
    public bool OpenAtCursor
    {
        get => OpenAtIndex == 1;
        set
        {
            if (value)
                OpenAtIndex = 1;
        }
    }

    /// <summary>The middle of <paramref name="window"/> in screen pixels.</summary>
    internal static ScreenPoint CenterOf(Window window)
    {
        var scale = OperatingSystem.IsMacOS() ? 1 : window.RenderScaling;
        if (scale <= 0 || !double.IsFinite(scale))
            scale = 1;
        return new ScreenPoint(
            window.Position.X + (int)Math.Round(window.Bounds.Width * scale / 2.0),
            window.Position.Y + (int)Math.Round(window.Bounds.Height * scale / 2.0));
    }

    /// <summary>Picks up a menu shortcut the setup wizard saved.</summary>
    public void RefreshMenuShortcut()
    {
        _loading = true;
        MenuShortcut = _s.Store.Skills.SkillMenuHotkey;
        _loading = false;
        RefreshConflicts();
    }

    /// <summary>Opens the donut over Settings (or at the pointer) and closes it without running a skill.</summary>
    [RelayCommand]
    private Task Preview() => _s.Donut.PreviewAsync(PreviewAt());

    /// <inheritdoc />
    protected override void OnLanguageChanged(object? sender, EventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        OnPropertyChanged(nameof(OuterRadiusText));
        OnPropertyChanged(nameof(OpacityText));
        RefreshConflicts();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        DonutLookSave.Flush();
        base.Dispose();
    }

    partial void OnOuterRadiusChanged(double value)
    {
        if (_loading)
            return;
        _s.Store.Settings.Donut.OuterRadius = DonutSettings.ClampRadius(value);
        DonutLookSave.Schedule(_s.Store);
    }

    partial void OnOpacityPercentChanged(double value)
    {
        if (_loading)
            return;
        _s.Store.Settings.Donut.Opacity = DonutSettings.ClampOpacity(Math.Round(value) / 100);
        DonutLookSave.Schedule(_s.Store);
    }

    partial void OnOpenAtIndexChanged(int value)
    {
        if (_loading)
            return;
        _s.Store.Settings.Donut.OpenAt = value == 1 ? DonutOpenAt.Cursor : DonutOpenAt.Caret;
        _s.Store.SaveSettings();
    }

    partial void OnShowShortcutHintsChanged(bool value)
    {
        if (_loading)
            return;
        _s.Store.Settings.Donut.ShowShortcutHints = value;
        _s.Store.SaveSettings();
    }

    partial void OnMenuShortcutChanged(string value)
    {
        if (_loading)
            return;
        _s.Store.Skills.SkillMenuHotkey = value;
        _s.Store.SaveSkills();
        AppHost.ApplyHotkeys();
        RefreshConflicts();
    }

    private ScreenPoint PreviewAt()
    {
        var window = _s.Windows.Settings;
        if (window is { IsVisible: true, Bounds.Width: > 1, Bounds.Height: > 1 })
            return CenterOf(window);
        return _s.Platform.Caret.GetCursor();
    }

    private void RefreshConflicts() => Conflicts = ConflictText.Build(_s);
}
