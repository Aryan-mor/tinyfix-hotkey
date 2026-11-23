// The composition root: builds every service once and hands them out. Plain constructors, no container: the object
// graph is small, start-up stays fast, and nothing is created by reflection.

using Microsoft.Extensions.Logging;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;
using TinyFix.Core.Selection;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.Services;

/// <summary>All long-lived services.</summary>
public sealed class AppServices
{
    /// <summary>The data folder in use.</summary>
    public required string DataDirectory { get; init; }

    public required UserDataStore Store { get; init; }
    public required LocalizationService Loc { get; init; }
    public required ThemeService Theme { get; init; }
    public required ILogger Log { get; init; }
    public required PlatformServices Platform { get; init; }
    public required IHotkeyService Hotkeys { get; init; }
    public required LlmClient Llm { get; init; }
    public required SkillRunner Runner { get; init; }
    public required SelectionService Selection { get; init; }
    public required ToastService Toasts { get; init; }
    public required RadialController Donut { get; init; }
    public required WindowService Windows { get; init; }
    public required TrayService Tray { get; init; }
}

/// <summary>Holds the one <see cref="AppServices"/> of the process.</summary>
public static class AppHost
{
    /// <summary>The services. Set by <see cref="Build"/>.</summary>
    public static AppServices Services { get; private set; } = null!;

    /// <summary>True once <see cref="Build"/> ran.</summary>
    public static bool IsBuilt => Services is not null;

    /// <summary>Builds the services over <paramref name="dataDirectory"/>.</summary>
    /// <param name="platform">The OS ports; null picks the ones of the running OS.</param>
    /// <param name="hotkeys">The shortcut service; null uses the real global hook (tests pass a fake).</param>
    /// <param name="keys">The key sender; null uses the real simulator (tests pass a fake).</param>
    /// <param name="http">The HTTP handler of the LLM client (tests pass a fake).</param>
    public static AppServices Build(string dataDirectory, PlatformServices? platform = null, IHotkeyService? hotkeys = null,
        IKeySender? keys = null, HttpMessageHandler? http = null, ILoggerProvider? logProvider = null)
    {
        var store = new UserDataStore(dataDirectory);
        store.Load();
        // A single provider is all the app needs; the logging container (Microsoft.Extensions.Logging) is not pulled in.
        var log = (logProvider ?? new FileLoggerProvider(Path.Combine(dataDirectory, "logs"))).CreateLogger("TinyFix");
        var loc = new LocalizationService();
        platform ??= CreatePlatform(dataDirectory);
        var hook = hotkeys ?? new SharpHookHotkeyService(log);
        var llm = new LlmClient(http is null ? new HttpClient() : new HttpClient(http));
        var selection = new SelectionService(platform.Clipboard, keys ?? new SharpHookKeySender(), platform.Foreground);
        var theme = new ThemeService(store);
        loc.LanguageChanged += (_, _) => theme.Apply(loc.Language);
        var toasts = new ToastService(loc);
        var runner = new SkillRunner(store, llm, platform.Secrets, selection, platform, toasts, log);
        var donut = new RadialController(store, loc, runner, platform, log);
        var tray = new TrayService(store, loc);
        var windows = new WindowService();
        Services = new AppServices
        {
            DataDirectory = dataDirectory,
            Store = store,
            Loc = loc,
            Theme = theme,
            Log = log,
            Platform = platform,
            Hotkeys = hook,
            Llm = llm,
            Runner = runner,
            Selection = selection,
            Toasts = toasts,
            Donut = donut,
            Windows = windows,
            Tray = tray,
        };
        toasts.Tray = tray;
        return Services;
    }

    private static PlatformServices CreatePlatform(string dataDirectory)
    {
        if (OperatingSystem.IsWindows())
            return TinyFix.Platform.Windows.WindowsPlatform.Create(dataDirectory);
        if (OperatingSystem.IsMacOS())
            return TinyFix.Platform.MacOS.MacOSPlatform.Create();
        return TinyFix.Platform.Linux.LinuxPlatform.Create(dataDirectory);
    }

    /// <summary>Re-registers every shortcut after skills or the menu shortcut changed.</summary>
    public static void ApplyHotkeys()
    {
        var s = Services;
        var bindings = new List<HotkeyBinding>();
        foreach (var skill in s.Store.Skills.Skills.Where(k => k.Enabled))
        {
            var key = skill.HotkeyValue();
            if (!key.IsDisabled)
                bindings.Add(new HotkeyBinding(skill.Id, key));
        }
        var menu = s.Store.Skills.MenuHotkey();
        if (!menu.IsDisabled)
            bindings.Add(new HotkeyBinding(HotkeyIds.Menu, menu));
        s.Hotkeys.SetBindings(bindings);
        s.Hotkeys.Suspended = s.Store.Settings.Paused;
    }
}
