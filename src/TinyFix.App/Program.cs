// Entry point. Order matters:
//  1. A log and handlers for unhandled exceptions, so even a start that fails leaves a line.
//  2. Single instance: a second launch tells the running TinyFix to open Settings and exits.
//  3. Start Avalonia. The app lives in the tray; it only exits through the tray "Quit" command.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using TinyFix.App.Services;
using TinyFix.Core.Settings;

namespace TinyFix.App;

internal static class Program
{
    /// <summary>The single-instance lock, owned by the first instance for its whole life.</summary>
    public static SingleInstance? Instance { get; private set; }

    /// <summary>How long a later launch waits for a TinyFix that is still starting.</summary>
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(15);

    [STAThread]
    public static int Main(string[] args)
    {
        var dataDirectory = DataDirectory.Resolve();
        var log = new FileLoggerProvider(Path.Combine(dataDirectory, "logs")).CreateLogger("TinyFix");
        Log = log;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.LogCritical("Unhandled exception ({Type})", e.ExceptionObject.GetType().Name);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.LogError("Unobserved task exception ({Type})", e.Exception.GetBaseException().GetType().Name);
            e.SetObserved();
        };

        SingleInstance instance;
        try
        {
            instance = SingleInstance.Acquire();
        }
        catch (UnauthorizedAccessException)
        {
            log.LogWarning("Another TinyFix runs with other rights; this launch cannot reach it");
            return 1;
        }
        if (!instance.IsFirst)
        {
            var delivered = instance.ForwardToFirst(args, ForwardTimeout);
            log.LogInformation("A running TinyFix was asked to open Settings (delivered: {Delivered})", delivered);
            instance.Dispose();
            return delivered ? 0 : 1;
        }

        Instance = instance;
        instance.StartListening();
        log.LogInformation("TinyFix {Version} starting on {OS}", ViewModels.AboutPageViewModel.CurrentVersion, Environment.OSVersion.Platform);
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            log.LogCritical("TinyFix stopped ({Type})", ex.GetType().Name);
            throw;
        }
        finally
        {
            instance.Dispose();
            Instance = null;
        }
    }

    /// <summary>Configures Avalonia (also used by the headless tests' counterpart).</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (OperatingSystem.IsWindows())
        {
            // Which way Avalonia draws the transparent donut and toast windows (see WindowsRendering).
            builder = builder.With(WindowsRendering.Options(
                WindowsRendering.Choose(Environment.GetEnvironmentVariable(WindowsRendering.Variable), RuntimeInformation.ProcessArchitecture),
                adapters => Log.LogInformation("Graphics adapters: {Adapters}", adapters)));
        }
        return builder;
    }

    private static ILogger Log { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
