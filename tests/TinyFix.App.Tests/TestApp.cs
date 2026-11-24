// Headless Avalonia with real Skia rendering, so windows can be captured as images, plus the fake OS ports and the
// service container the tests build against a throw-away data folder (the real %APPDATA% is never touched).

using System.Net;
using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.Headless;
using Avalonia.Media;
using TinyFix.App;
using TinyFix.App.Services;
using TinyFix.Core.Hotkeys;
using TinyFix.Core.Platform;
using TinyFix.Core.Settings;

[assembly: AvaloniaTestApplication(typeof(TinyFix.App.Tests.TestAppBuilder))]
// One application for all tests: Avalonia sets its font manager up per application, which is slow and fragile.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
// AppHost, the fake ports and the data folder are process-wide. Parallel classes would race on them (a models-page
// test can save another protocol while the key-field test reads it).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TinyFix.App.Tests;

/// <summary>Headless Avalonia with Skia drawing.</summary>
public static class TestAppBuilder
{
    /// <summary>Builds the test application.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        // The real startup must not open the wizard over the throw-away folder (or, worse, the user's data).
        Environment.SetEnvironmentVariable(TinyFix.App.ViewModels.SetupWizardViewModel.SkipVariable, "1");
        return AppBuilder.Configure<App>()
            .UseSkia()
            // Slice-label tests measure with this face, not whatever UI font the runner has installed.
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

/// <summary>Records shortcut registrations instead of installing a global hook.</summary>
internal sealed class FakeHotkeys : IHotkeyService
{
    public IReadOnlyList<HotkeyBinding> Bindings { get; private set; } = [];

    public bool Suspended { get; set; }

    public event Action<HotkeyPressedEventArgs>? Pressed;

    public void SetBindings(IReadOnlyList<HotkeyBinding> bindings) => Bindings = bindings;

    public bool Start() => true;

    public void Press(string id) => Pressed?.Invoke(new HotkeyPressedEventArgs(id));

    public void Dispose()
    {
    }
}

/// <summary>An in-memory stand-in for every OS port.</summary>
internal sealed class FakePorts : IClipboardAccess, IKeySender, IForegroundWindow, ICaretLocator, ISecretStore, IAutostart
{
    private sealed class Snap(string? text) : IClipboardSnapshot
    {
        public string? Text { get; } = text;

        public void Dispose()
        {
        }
    }

    private long _count;

    public string? ClipboardText { get; set; } = "clipboard";
    public string? Selection { get; set; }
    public List<string> Focused { get; } = [];
    public Dictionary<string, string> Keys { get; } = [];
    public bool AutostartOn { get; private set; }
    public ScreenPoint Caret { get; set; } = new(400, 300);
    public ScreenPoint Cursor { get; set; } = new(500, 400);

    public long ChangeCount => _count;
    public IClipboardSnapshot? Snapshot() => new Snap(ClipboardText);

    public void Restore(IClipboardSnapshot? snapshot)
    {
        ClipboardText = (snapshot as Snap)?.Text;
        _count++;
    }

    public string? GetText() => ClipboardText;

    public void SetText(string text)
    {
        ClipboardText = text;
        _count++;
    }

    public void Clear() => ClipboardText = null;

    public void SendCopy()
    {
        if (Selection is not null)
            SetText(Selection);
    }

    public void SendPaste()
    {
    }

    public void SendSelectAll()
    {
    }

    public void SendUndo()
    {
    }

    public string? Save() => "window-1";

    public bool Restore(string? token)
    {
        Focused.Add(token ?? "");
        return true;
    }

    public bool IsTargetElevated() => false;

    public Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken ct = default) => Task.FromResult(true);

    public ScreenPoint GetCaretOrCursor() => Caret;

    public ScreenPoint GetCursor() => Cursor;

    public string Description => "fake store";
    public string? Warning => null;
    public int SecretReads { get; private set; }

    public void ResetSecretReads() => SecretReads = 0;

    /// <summary>Back to the values a fresh test should see. The change counter is left, so a test can still count its own edits.</summary>
    public void Reset()
    {
        ClipboardText = "clipboard";
        Selection = null;
        Focused.Clear();
        Keys.Clear();
        AutostartOn = false;
        Caret = new ScreenPoint(400, 300);
        Cursor = new ScreenPoint(500, 400);
        SecretReads = 0;
    }

    public string Get(string provider)
    {
        SecretReads++;
        return Keys.GetValueOrDefault(provider, "");
    }

    public bool Contains(string account) => Keys.TryGetValue(account, out var value) && !string.IsNullOrWhiteSpace(value);

    public void Set(string provider, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            Keys.Remove(provider);
        else
            Keys[provider] = apiKey;
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        SecretReads++;
        return Keys;
    }

    public bool IsEnabled => AutostartOn;

    public void Enable(string executablePath) => AutostartOn = true;

    public void Disable() => AutostartOn = false;
}

/// <summary>Builds the service container once per application against a temporary data folder.</summary>
internal static class TestServices
{
    private static Application? _initializedFor;

    public static string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "tinyfix-app-tests", Guid.NewGuid().ToString("N")[..8]);

    public static FakePorts Ports { get; } = new();

    public static FakeHotkeys Hotkeys { get; } = new();

    /// <summary>
    /// The headless runner may create a new Application per run, so services are rebuilt when it changes.
    /// Every call also puts settings, skills and the fake ports back to defaults: one data folder and one
    /// <see cref="AppHost"/> serve the whole assembly, and a test must not see what an earlier one saved.
    /// </summary>
    public static AppServices EnsureInitialized(string language = "en", bool wayland = false)
    {
        var app = Application.Current!;
        if (!ReferenceEquals(_initializedFor, app))
        {
            var platform = new PlatformServices(Ports, Ports, Ports, Ports, Ports, new PlatformCapabilities(true, true, true, wayland));
            Environment.SetEnvironmentVariable(UserDataStore.LegacyVariable, Path.Combine(DataDirectory, "no-legacy"));
            AppHost.Build(DataDirectory, platform, Hotkeys, Ports, new HttpClientHandlerStub(), new NullLogProvider());
            AppHost.Services.Loc.Attach(app);
            UiZoom.Configure(AppHost.Services.Store.Settings.UiScale, scale =>
            {
                AppHost.Services.Store.Settings.UiScale = scale;
                AppHost.Services.Store.SaveSettings();
            });
            _initializedFor = app;
        }
        return ResetSharedState(language);
    }

    private static AppServices ResetSharedState(string language)
    {
        // A slider's delayed write must not land on top of the defaults this call just loaded.
        if (DonutLookSave.IsPending)
            DonutLookSave.Flush();
        Ports.Reset();
        Hotkeys.Suspended = false;
        Motion.Set(true);
        ScriptedHttp.Reset();
        var store = AppHost.Services.Store;
        if (File.Exists(store.SettingsPath))
            File.Delete(store.SettingsPath);
        if (File.Exists(store.SkillsPath))
            File.Delete(store.SkillsPath);
        store.Load();
        var s = AppHost.Services;
        s.Store.Settings.Language = language;
        s.Store.SaveSettings();
        s.Loc.SetLanguage(language);
        s.Theme.Apply(s.Loc.Language);
        AppHost.ApplyHotkeys();
        return s;
    }

    /// <summary>
    /// The embedded Inter face (<c>fonts:Inter</c> is the collection key), so slice-label measurements do not follow
    /// the runner's UI font. A plain "Inter" name is the system font and falls back when it is not installed.
    /// </summary>
    public static FontFamily SliceLabelFont => new("fonts:Inter#Inter");

    /// <summary>Where rendered screenshots go: tests/TinyFix.App.Tests/bin/.../screenshots.</summary>
    public static string ScreenshotDirectory
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

/// <summary>A scripted reply for one test. The default is still "no network", so nothing is sent for real.</summary>
internal static class ScriptedHttp
{
    public static HttpStatusCode? Status { get; set; }

    public static string Body { get; set; } = "";

    /// <summary>Wait until the request is cancelled (the wizard's Cancel button).</summary>
    public static bool Hang { get; set; }

    public static void Reset()
    {
        Status = null;
        Body = "";
        Hang = false;
    }
}

/// <summary>A handler that refuses every request unless a test scripted a reply. Tests must never reach the network.</summary>
internal sealed class HttpClientHandlerStub : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ScriptedHttp.Hang)
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        if (ScriptedHttp.Status is { } status)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(ScriptedHttp.Body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
        throw new HttpRequestException("The tests have no network.");
    }
}

/// <summary>Discards log lines.</summary>
internal sealed class NullLogProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }
}
