// Code-behind of the Settings window: gives it its view model and cleans up when it closes. The window is created
// lazily by WindowService and shown again when the tray icon or a second launch asks for it. Motion and the window
// zoom follow the MIT settings window credited in THIRD-PARTY-NOTICES.md.

using Avalonia.Controls;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;

namespace TinyFix.App.Views;

/// <summary>The settings window.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>Creates the window over <paramref name="viewModel"/>.</summary>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Motion.Track(this);
        UiZoom.Track(this);
    }

    /// <summary>For the XAML designer and loader.</summary>
    public SettingsWindow()
        : this(new SettingsViewModel(TinyFix.App.Services.AppHost.Services))
    {
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // Pending edits (a prompt typed a moment ago) are written before the window goes away.
        (DataContext as SettingsViewModel)?.Dispose();
        DataContext = null;
        base.OnClosed(e);
    }
}
