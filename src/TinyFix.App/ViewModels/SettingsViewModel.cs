// The Settings window's view model: the navigation list and the selected page. The pane (width, icons, collapse)
// follows the MIT settings window credited in THIRD-PARTY-NOTICES.md. Each page has its own view model.

using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Localization;

namespace TinyFix.App.ViewModels;

/// <summary>Base of a settings page: a title that follows the UI language, and the icon in the navigation.</summary>
public abstract partial class PageViewModel(ILocalizer loc, string titleKey) : ObservableObject, IDisposable
{
    private readonly ILocalizer _loc = loc;
    private bool _subscribed;

    /// <summary>The page title (also the label in the navigation list).</summary>
    public string Title => _loc[titleKey];

    /// <summary>The navigation icon, or null when the resource is missing.</summary>
    public Geometry? Icon { get; private set; }

    /// <summary>The navigation is collapsed to icons: the title is hidden and shown as a tooltip instead.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip), nameof(ShowTitle))]
    private bool _collapsed;

    /// <summary>True when the title is drawn beside the icon.</summary>
    public bool ShowTitle => !Collapsed;

    /// <summary>The tooltip: the title while collapsed (no text is shown then), else none.</summary>
    public string? Tip => Collapsed ? Title : null;

    /// <summary>Sets the icon from an application resource (<c>Icon.Settings</c> and the like).</summary>
    public void UseIcon(string resourceKey)
    {
        var app = Application.Current;
        if (app?.TryGetResource(resourceKey, app.ActualThemeVariant, out var value) == true)
            Icon = value as Geometry;
    }

    /// <summary>Starts following language changes.</summary>
    protected void Track()
    {
        if (_subscribed)
            return;
        _subscribed = true;
        _loc.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Called after the UI language changed.</summary>
    protected virtual void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Tip));
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (_subscribed)
            _loc.LanguageChanged -= OnLanguageChanged;
        _subscribed = false;
        GC.SuppressFinalize(this);
    }
}

/// <summary>The window's state.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizer _loc;

    /// <summary>Width of the navigation with its titles, and collapsed to icons.</summary>
    private const double NavExpandedWidth = 230;

    private const double NavCollapsedWidth = 64;

    /// <summary>Creates the pages.</summary>
    public SettingsViewModel(AppServices services)
    {
        _loc = services.Loc;
        Pages =
        [
            new GeneralPageViewModel(services),
            new DonutPageViewModel(services),
            new SkillsPageViewModel(services),
            new OnlinePageViewModel(services),
            new UpdatesPageViewModel(services),
            new AboutPageViewModel(services),
        ];
        Pages[0].UseIcon("Icon.Settings");
        Pages[1].UseIcon("Icon.Donut");
        Pages[2].UseIcon("Icon.Tune");
        Pages[3].UseIcon("Icon.Link");
        Pages[4].UseIcon("Icon.Refresh");
        Pages[5].UseIcon("Icon.Info");
        _selected = Pages[2];
        Title = _loc["settings.title"];
        VersionText = _loc.Format("about.version", AboutPageViewModel.CurrentVersion);
        _loc.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>All pages in navigation order.</summary>
    public IReadOnlyList<PageViewModel> Pages { get; }

    /// <summary>The window title.</summary>
    [ObservableProperty]
    private string _title;

    /// <summary>The page on screen.</summary>
    [ObservableProperty]
    private PageViewModel _selected;

    /// <summary>The version line at the bottom of the navigation.</summary>
    [ObservableProperty]
    private string _versionText;

    /// <summary>The navigation shows only icons.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavWidth), nameof(NavPadding), nameof(NavToggleTip))]
    private bool _isNavCollapsed;

    /// <summary>The navigation pane's width, animated by the style on <c>Border.navpane</c>.</summary>
    public double NavWidth => IsNavCollapsed ? NavCollapsedWidth : NavExpandedWidth;

    /// <summary>Room around the navigation's content; less when collapsed, so an icon fits the rail.</summary>
    public Thickness NavPadding => new(IsNavCollapsed ? 10 : 14, 18);

    /// <summary>Tooltip and screen-reader name of the button that collapses or expands the navigation.</summary>
    public string NavToggleTip => _loc[IsNavCollapsed ? "nav.expand" : "nav.collapse"];

    /// <summary>Collapses or expands the navigation.</summary>
    [RelayCommand]
    private void ToggleNav() => IsNavCollapsed = !IsNavCollapsed;

    partial void OnIsNavCollapsedChanged(bool value)
    {
        foreach (var page in Pages)
            page.Collapsed = value;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = _loc["settings.title"];
        VersionText = _loc.Format("about.version", AboutPageViewModel.CurrentVersion);
        OnPropertyChanged(nameof(NavToggleTip));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _loc.LanguageChanged -= OnLanguageChanged;
        foreach (var page in Pages)
            page.Dispose();
    }
}
