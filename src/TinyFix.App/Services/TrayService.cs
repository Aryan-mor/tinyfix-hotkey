// The system tray icon: open settings, open the skill menu, fix the clipboard, pause shortcuts, quit. While a skill
// runs the icon animates (the working frames of the Python app), then shows which model answered (M, F or L).
// Pattern adapted from the reference project's tray service (MIT, credited in THIRD-PARTY-NOTICES.md).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using TinyFix.Core.Localization;
using TinyFix.Core.Settings;

namespace TinyFix.App.Services;

/// <summary>Owns the tray icon and its menu.</summary>
public sealed class TrayService(UserDataStore store, ILocalizer loc)
{
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromSeconds(1.6) };
    private TrayIcon? _tray;
    private NativeMenuItem? _settings, _setup, _menu, _clipboard, _pause, _quit;
    private int _frame;

    /// <summary>Creates the icon and attaches it to <paramref name="app"/>.</summary>
    public void Create(Application app)
    {
        _settings = new NativeMenuItem();
        _settings.Click += (_, _) => AppHost.Services.Windows.ShowSettings();
        _setup = new NativeMenuItem();
        _setup.Click += (_, _) => AppHost.Services.Windows.ShowSetup();
        _menu = new NativeMenuItem();
        _menu.Click += (_, _) => _ = AppHost.Services.Donut.OpenAsync();
        _clipboard = new NativeMenuItem();
        _clipboard.Click += (_, _) => _ = AppHost.Services.Runner.FixClipboardAsync();
        _pause = new NativeMenuItem();
        _pause.Click += (_, _) => TogglePause();
        _quit = new NativeMenuItem();
        _quit.Click += (_, _) => Quit(app);

        _tray = new TrayIcon
        {
            ToolTipText = "TinyFix",
            Menu = new NativeMenu { Items = { _settings, _setup, _menu, _clipboard, new NativeMenuItemSeparator(), _pause, new NativeMenuItemSeparator(), _quit } },
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => AppHost.Services.Windows.ShowSettings();
        SetIcon("tinyfix.ico");
        TrayIcon.SetIcons(app, [_tray]);

        _frames.Tick += (_, _) =>
        {
            _frame = (_frame + 1) % 3;
            SetIcon($"tinyfix-working-{_frame + 1}.ico");
        };
        _resultTimer.Tick += (_, _) =>
        {
            _resultTimer.Stop();
            SetIcon("tinyfix.ico");
        };
        Refresh();
        loc.LanguageChanged += (_, _) => Refresh();
    }

    /// <summary>Updates the menu texts and the tooltip.</summary>
    public void Refresh()
    {
        if (_tray is null)
            return;
        _settings!.Header = loc["tray.settings"];
        _setup!.Header = loc["tray.setup"];
        _menu!.Header = loc["tray.skill_menu"];
        _clipboard!.Header = loc["tray.fix_clipboard"];
        _pause!.Header = store.Settings.Paused ? loc["tray.resume"] : loc["tray.pause"];
        _quit!.Header = loc["tray.quit"];
        _tray.ToolTipText = store.Settings.Paused ? loc["tray.tooltip_paused"] : loc["tray.tooltip"];
    }

    /// <summary>Pauses or resumes every shortcut.</summary>
    public void TogglePause()
    {
        store.Settings.Paused = !store.Settings.Paused;
        store.SaveSettings();
        AppHost.Services.Hotkeys.Suspended = store.Settings.Paused;
        Refresh();
    }

    /// <summary>Animates the icon while a skill runs; when it ends shows the answering model's letter for a moment.</summary>
    public void SetBusy(bool busy, char letter)
    {
        if (_tray is null)
            return;
        _resultTimer.Stop();
        if (busy)
        {
            _frame = 0;
            SetIcon("tinyfix-working-1.ico");
            _frames.Start();
            return;
        }
        _frames.Stop();
        if (letter is 'M' or 'F' or 'L')
        {
            SetIcon($"tinyfix-result-{letter}.ico");
            _resultTimer.Start();
        }
        else
        {
            SetIcon("tinyfix.ico");
        }
    }

    private void SetIcon(string file)
    {
        if (_tray is null)
            return;
        using var stream = AssetLoader.Open(new Uri($"avares://TinyFix/Assets/Icons/{file}"));
        _tray.Icon = new WindowIcon(stream);
    }

    private static void Quit(Application app)
    {
        AppHost.Services.Hotkeys.Dispose();
        (app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Hides the icon (at exit).</summary>
    public void Dispose() => _tray?.Dispose();
}
