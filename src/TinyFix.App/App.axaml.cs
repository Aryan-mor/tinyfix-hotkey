// The application: wires the services to the UI when Avalonia is ready. TinyFix has no main window; the tray icon is
// its face, and the donut and Settings windows open on demand.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.Core.Platform;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;
using TinyFix.Core.Updates;

namespace TinyFix.App;

/// <summary>The Avalonia application.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // The headless test host builds its own services first (a throw-away data folder, fake ports).
        var prebuilt = AppHost.IsBuilt;
        var services = prebuilt ? AppHost.Services : AppHost.Build(DataDirectory.Resolve());
        // First run only, and never for tests or a copy with its own data folder: they must not touch the user's login items.
        if (!prebuilt && DataDirectory.Override is null
            && AutostartDefault.ApplyOnce(services.Store.Settings, services.Platform.Autostart, Environment.ProcessPath))
        {
            services.Store.SaveSettings();
        }
        services.Loc.Attach(this);
        services.Loc.SetLanguage(services.Store.Settings.Language);
        services.Theme.Apply(services.Loc.Language);
        services.Tray.Create(this);
        Motion.Configure(null, () => false);
        UiZoom.Configure(services.Store.Settings.UiScale, scale =>
        {
            services.Store.Settings.UiScale = scale;
            services.Store.SaveSettings();
        });
        if (OperatingSystem.IsWindows())
        {
            services.Log.LogInformation("{Rendering}", WindowsRendering.Describe(Environment.GetEnvironmentVariable(WindowsRendering.Variable)));
            TransparentWindows.Watch(services.Log);
        }

        if (Program.Instance is { } instance)
        {
            // A second launch means "show me": open Settings.
            instance.ArgumentsReceived += _ => services.Windows.ShowSettings();
        }
        desktop.Exit += (_, _) =>
        {
            UiZoom.Flush();
            DonutLookSave.Flush();
            services.Hotkeys.Dispose();
            services.Tray.Dispose();
        };

        // Everything that is not needed to show the tray icon waits until the UI is idle: start-up stays light.
        Dispatcher.UIThread.Post(() => StartBackgroundWork(services), DispatcherPriority.Background);
    }

    private static void StartBackgroundWork(AppServices services)
    {
        services.Hotkeys.Pressed += e => OnHotkey(services, e);
        AppHost.ApplyHotkeys();
        if (!services.Hotkeys.Start())
            services.Toasts.Show(StatusKind.Failure, "status.hook_failed");
        else if (services.Platform.Capabilities.IsWayland)
            services.Toasts.Show(StatusKind.Info, "status.wayland");

        var conflicts = ConflictText.Build(services);
        if (conflicts.Length > 0)
            services.Toasts.Show(StatusKind.Failure, "status.conflict", conflicts.Split('\n')[0]);

        // A copy started with TINYFIX_DATA_DIR (tests, portable use) never phones home on its own.
        if (DataDirectory.Override is null && services.Store.Settings.CheckForUpdates &&
            UpdateChecker.ShouldAutoCheck(services.Store.Settings.LastUpdateCheck, DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))
            _ = CheckForUpdateAsync(services);
    }

    private static void OnHotkey(AppServices services, HotkeyPressedEventArgs e)
    {
        if (e.Id == HotkeyIds.Menu)
        {
            _ = services.Donut.OpenAsync();
            return;
        }
        if (services.Store.Skills.Get(e.Id) is { } skill)
            _ = services.Runner.RunAsync(skill, null);
    }

    private static async Task CheckForUpdateAsync(AppServices services)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var latest = await UpdateChecker.FetchLatestAsync(http).ConfigureAwait(false);
            services.Store.Settings.LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            services.Store.SaveSettings();
            if (latest is not null && UpdateChecker.IsNewer(latest.Version.ToString(), ViewModels.AboutPageViewModel.CurrentVersion))
                services.Toasts.Show(StatusKind.Info, "updates.available", latest.Version);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            services.Log.LogInformation("Update check failed");
        }
    }
}
