// Creates windows only when they are first needed (start-up stays light) and brings an open one to the front.

using Avalonia.Controls;
using Avalonia.Threading;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;

namespace TinyFix.App.Services;

/// <summary>Opens the Settings window.</summary>
public sealed class WindowService
{
    private SettingsWindow? _settings;

    /// <summary>Shows Settings (on the UI thread, whichever thread asks), creating it on first use.</summary>
    public void ShowSettings()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowSettings);
            return;
        }
        if (_settings is null)
        {
            _settings = new SettingsWindow(new SettingsViewModel(AppHost.Services));
            _settings.Closed += (_, _) => _settings = null;
        }
        if (_settings.WindowState == WindowState.Minimized)
            _settings.WindowState = WindowState.Normal;
        _settings.Show();
        _settings.Activate();
    }

    /// <summary>The open Settings window, or null.</summary>
    public SettingsWindow? Settings => _settings;
}
