// Tests of the selection flow (with fake OS ports), the skill runner, settings storage and the legacy import.

using Microsoft.Extensions.Logging.Abstractions;
using TinyFix.Core.Engine;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;
using TinyFix.Core.Selection;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.Core.Tests;

/// <summary>An in-memory clipboard that a fake "app" fills when copy is sent.</summary>
internal sealed class FakeClipboard : IClipboardAccess, IKeySender, IForegroundWindow
{
    private sealed class Snap(string? text) : IClipboardSnapshot
    {
        public string? Text { get; } = text;

        public void Dispose()
        {
        }
    }

    public string? Text { get; set; } = "original clipboard";
    public string? Selection { get; set; }
    public string? PastedOver { get; private set; }
    public bool UseChangeCounter { get; init; } = true;
    public bool Elevated { get; set; }
    public List<string> Log { get; } = [];
    private long _count;

    public long ChangeCount => UseChangeCounter ? _count : -1;

    public IClipboardSnapshot? Snapshot() => new Snap(Text);

    public void Restore(IClipboardSnapshot? snapshot)
    {
        Text = ((Snap?)snapshot)?.Text;
        _count++;
        Log.Add("restore");
    }

    public string? GetText() => Text;

    public void SetText(string text)
    {
        Text = text;
        _count++;
    }

    public void Clear() => Text = null;

    public void SendCopy()
    {
        Log.Add("copy");
        if (Selection is not null)
            SetText(Selection);
    }

    public void SendPaste()
    {
        Log.Add("paste");
        PastedOver = Text;
    }

    public void SendSelectAll() => Log.Add("selectall");

    public string? Save() => "w1";

    public bool Restore(string? token)
    {
        Log.Add("focus:" + token);
        return true;
    }

    public bool IsTargetElevated() => Elevated;

    public Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken ct = default) => Task.FromResult(true);
}

internal sealed class FakeSecrets : ISecretStore
{
    public Dictionary<string, string> Keys { get; } = [];

    public string Description => "fake";
    public string? Warning => null;
    public string Get(string provider) => Keys.GetValueOrDefault(provider, "");

    public void Set(string provider, string apiKey) => Keys[provider] = apiKey;

    public IReadOnlyDictionary<string, string> GetAll() => Keys;
}

internal sealed class RecordingStatus : IStatusSink
{
    public List<string> Keys { get; } = [];

    public void Show(StatusKind kind, string key, params object[] args) => Keys.Add(key);

    public void SetBusy(bool busy, char letter = '\0')
    {
    }
}

public sealed class SelectionAndSettingsTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-core-tests", Guid.NewGuid().ToString("N")[..8]);

    public SelectionAndSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Capture_reads_the_selection_and_paste_restores_the_clipboard(bool changeCounter)
    {
        var fake = new FakeClipboard { Selection = "Hi frinds", UseChangeCounter = changeCounter };
        var service = new SelectionService(fake, fake, fake) { PasteSettleDelay = TimeSpan.Zero };
        var capture = await service.CaptureAsync(selectAll: false, Ct);
        Assert.Equal(SelectionFailure.None, capture.Failure);
        Assert.Equal("Hi frinds", capture.Text);
        Assert.True(await service.PasteAsync(capture, "Hi friends", Ct));
        Assert.Equal("Hi friends", fake.PastedOver);
        Assert.Equal("original clipboard", fake.Text);
    }

    [Fact]
    public async Task Select_all_sends_ctrl_a_before_copy()
    {
        var fake = new FakeClipboard { Selection = "all text" };
        var service = new SelectionService(fake, fake, fake);
        await service.CaptureAsync(selectAll: true, Ct);
        Assert.True(fake.Log.IndexOf("selectall") < fake.Log.IndexOf("copy"));
    }

    [Fact]
    public async Task Copy_is_retried_once_and_then_reported_and_the_clipboard_is_restored()
    {
        var fake = new FakeClipboard { Selection = null };
        var service = new SelectionService(fake, fake, fake);
        var capture = await service.CaptureAsync(selectAll: false, Ct);
        Assert.Equal(SelectionFailure.CopyFailed, capture.Failure);
        Assert.Equal(2, fake.Log.Count(l => l == "copy"));
        Assert.Equal("original clipboard", fake.Text);
    }

    /// <summary>A runner whose online model answers <paramref name="answer"/> (AI switched on with a saved key).</summary>
    private SkillRunner OnlineRunner(FakeClipboard fake, RecordingStatus status, UserDataStore store, string answer)
    {
        store.Settings.LlmEnabled = true;
        var secrets = new FakeSecrets();
        secrets.Set(LlmDefaults.ProviderGoogle, "key");
        return Runner(fake, secrets, status, store, new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.GeminiAnswer(answer))));
    }

    private SkillRunner Runner(FakeClipboard fake, FakeSecrets secrets, RecordingStatus status, UserDataStore store, HttpMessageHandler? http = null)
    {
        var platform = new PlatformServices(fake, fake, new FixedCaret(), secrets, new NoAutostart(), new PlatformCapabilities(true, true, true, false));
        return new SkillRunner(store, new LlmClient(new HttpClient(http ?? new FakeHandler((_, _) => FakeHandler.Json("{}")))), secrets,
            new SelectionService(fake, fake, fake) { PasteSettleDelay = TimeSpan.Zero }, platform, status, NullLogger.Instance);
    }

    private UserDataStore NewStore()
    {
        var store = new UserDataStore(_dir) { LegacySource = () => null };
        store.Load();
        return store;
    }

    [Fact]
    public async Task Fix_goes_through_the_online_model_and_pastes_the_result()
    {
        var fake = new FakeClipboard { Selection = "Hi frinds, i hope" };
        var status = new RecordingStatus();
        var store = NewStore();
        var runner = OnlineRunner(fake, status, store, "Hi friends, I hope");
        await runner.RunAsync(store.Skills.Get(SkillIds.Fix)!, "w1", Ct);
        Assert.Equal("Hi friends, I hope", fake.PastedOver);
        Assert.Equal("original clipboard", fake.Text);
        Assert.Contains("status.running", status.Keys);
    }

    [Fact]
    public async Task Every_skill_says_so_when_ai_is_off()
    {
        var store = NewStore();
        foreach (var id in new[] { SkillIds.Fix, SkillIds.FixAll, SkillIds.Translate })
        {
            var fake = new FakeClipboard { Selection = "Hello" };
            var status = new RecordingStatus();
            await Runner(fake, new FakeSecrets(), status, store).RunAsync(store.Skills.Get(id)!, null, Ct);
            Assert.Equal(["status.ai_off"], status.Keys);
            Assert.Empty(fake.Log);
        }
    }

    [Fact]
    public async Task An_online_skill_runs_and_a_failure_leaves_the_text_alone()
    {
        var store = NewStore();
        store.Settings.LlmEnabled = true;
        var secrets = new FakeSecrets();
        secrets.Set(LlmDefaults.ProviderGoogle, "key");
        var fake = new FakeClipboard { Selection = "Hello" };
        var status = new RecordingStatus();
        var ok = Runner(fake, secrets, status, store, new FakeHandler((_, _) => FakeHandler.Json(FakeHandler.GeminiAnswer("Hallo"))));
        await ok.RunAsync(store.Skills.Get(SkillIds.Translate)!, null, Ct);
        Assert.Equal("Hallo", fake.PastedOver);

        var failing = new FakeClipboard { Selection = "Hello" };
        var failStatus = new RecordingStatus();
        var bad = Runner(failing, secrets, failStatus, store, new FakeHandler((_, _) => FakeHandler.Json("{}", System.Net.HttpStatusCode.Forbidden)));
        await bad.RunAsync(store.Skills.Get(SkillIds.Translate)!, null, Ct);
        Assert.Null(failing.PastedOver);
        Assert.Equal("original clipboard", failing.Text);
        Assert.Contains("status.failed", failStatus.Keys);
    }

    [Fact]
    public async Task Elevated_windows_and_pause_are_respected()
    {
        var store = NewStore();
        var elevated = new FakeClipboard { Selection = "Hi frinds", Elevated = true };
        var status = new RecordingStatus();
        await OnlineRunner(elevated, status, store, "x").RunAsync(store.Skills.Get(SkillIds.Fix)!, null, Ct);
        Assert.Contains("status.elevated", status.Keys);
        Assert.Null(elevated.PastedOver);

        store.Settings.Paused = true;
        var paused = new FakeClipboard { Selection = "Hi frinds" };
        await OnlineRunner(paused, new RecordingStatus(), store, "x").RunAsync(store.Skills.Get(SkillIds.Fix)!, null, Ct);
        Assert.Empty(paused.Log);
    }

    [Fact]
    public async Task Fix_clipboard_changes_the_clipboard_text_in_place()
    {
        var fake = new FakeClipboard { Text = "Hi frinds" };
        var status = new RecordingStatus();
        await OnlineRunner(fake, status, NewStore(), "Hi friends").FixClipboardAsync(Ct);
        Assert.Equal("Hi friends", fake.Text);
        Assert.Contains("clipboard.fixed", status.Keys);

        var off = new FakeClipboard { Text = "Hi frinds" };
        var offStatus = new RecordingStatus();
        await Runner(off, new FakeSecrets(), offStatus, NewStore()).FixClipboardAsync(Ct);
        Assert.Equal("Hi frinds", off.Text);
        Assert.Equal(["status.ai_off"], offStatus.Keys);
    }

    // ---- settings

    [Fact]
    public void Settings_round_trip_and_a_missing_file_gives_defaults()
    {
        var store = NewStore();
        Assert.True(File.Exists(store.SettingsPath));
        Assert.True(File.Exists(store.SkillsPath));
        Assert.Equal("English", store.Settings.LlmTargetLanguage);
        store.Settings.LlmTargetLanguage = "German";
        store.Settings.LlmModel = "custom-model";
        store.SaveSettings();
        var again = new UserDataStore(_dir) { LegacySource = () => null };
        again.Load();
        Assert.Equal("German", again.Settings.LlmTargetLanguage);
        Assert.Equal("custom-model", again.Settings.LlmModel);
        Assert.Contains("\"llm_model\"", File.ReadAllText(again.SettingsPath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Donut_settings_keep_todays_defaults_and_round_trip()
    {
        var store = NewStore();
        Assert.Equal(DonutSettings.DefaultRadius, store.Settings.Donut.OuterRadius);
        Assert.Equal(DonutSettings.DefaultOpacity, store.Settings.Donut.Opacity);
        Assert.Equal(DonutOpenAt.Caret, store.Settings.Donut.OpenAt);
        Assert.True(store.Settings.Donut.ShowShortcutHints);
        var caret = new FixedCaret();
        Assert.Equal(new ScreenPoint(10, 10), DonutOpenAt.Point(caret, store.Settings.Donut.OpenAt));
        Assert.Equal(new ScreenPoint(10, 10), DonutOpenAt.Point(caret, "nope"));
        Assert.Equal(new ScreenPoint(20, 20), DonutOpenAt.Point(caret, DonutOpenAt.Cursor));

        store.SaveSettings();
        var defaults = new UserDataStore(_dir) { LegacySource = () => null };
        defaults.Load();
        Assert.Equal(DonutSettings.DefaultRadius, defaults.Settings.Donut.OuterRadius);
        Assert.Equal(DonutSettings.DefaultOpacity, defaults.Settings.Donut.Opacity);
        Assert.True(defaults.Settings.Donut.ShowShortcutHints);

        store.Settings.Donut.OuterRadius = 200;
        store.Settings.Donut.Opacity = 0.4;
        store.Settings.Donut.OpenAt = DonutOpenAt.Cursor;
        store.Settings.Donut.ShowShortcutHints = false;
        store.SaveSettings();
        var again = new UserDataStore(_dir) { LegacySource = () => null };
        again.Load();
        Assert.Equal(200, again.Settings.Donut.OuterRadius);
        Assert.Equal(0.4, again.Settings.Donut.Opacity);
        Assert.Equal(DonutOpenAt.Cursor, again.Settings.Donut.OpenAt);
        Assert.False(again.Settings.Donut.ShowShortcutHints);
        Assert.Equal(new ScreenPoint(20, 20), DonutOpenAt.Point(caret, again.Settings.Donut.OpenAt));

        File.WriteAllText(store.SettingsPath, """
            { "language": "de" }
            """);
        var old = new UserDataStore(_dir) { LegacySource = () => null };
        old.Load();
        Assert.Equal("de", old.Settings.Language);
        Assert.Equal(DonutSettings.DefaultRadius, old.Settings.Donut.OuterRadius);
        Assert.Equal(DonutOpenAt.Caret, old.Settings.Donut.OpenAt);
        Assert.True(old.Settings.Donut.ShowShortcutHints);

        File.WriteAllText(store.SettingsPath, """
            { "donut": { "outer_radius": 9999, "opacity": 0, "open_at": "nope" } }
            """);
        var bad = new UserDataStore(_dir) { LegacySource = () => null };
        bad.Load();
        Assert.Equal(DonutSettings.MaxRadius, bad.Settings.Donut.OuterRadius);
        Assert.Equal(DonutSettings.MinOpacity, bad.Settings.Donut.Opacity);
        Assert.Equal(DonutOpenAt.Caret, bad.Settings.Donut.OpenAt);
        Assert.True(bad.Settings.Donut.ShowShortcutHints);
    }

    [Fact]
    public void A_damaged_settings_file_means_defaults_not_a_crash()
    {
        File.WriteAllText(Path.Combine(_dir, UserDataStore.SettingsFileName), "{ nope");
        var store = new UserDataStore(_dir) { LegacySource = () => null };
        store.Load();
        Assert.Equal("auto", store.Settings.Language);
    }

    [Fact]
    public void The_python_apps_files_are_imported_once()
    {
        var legacy = Path.Combine(_dir, "legacy");
        Directory.CreateDirectory(legacy);
        var python = SkillCatalog.DefaultStore();
        python.Get(SkillIds.Fix)!.Hotkey = "F7";
        SkillStoreFile.Save(Path.Combine(legacy, "skills.json"), python);
        File.WriteAllBytes(Path.Combine(legacy, "secrets.dpapi"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(legacy, "config.ini"), "[TinyFix]\nllm_enabled = true\nllm_provider = openrouter\nllm_model = x/y\nllm_timeout = 7.5\nsensitivity = cautious\n");

        var data = Path.Combine(_dir, "new");
        Directory.CreateDirectory(data);
        var store = new UserDataStore(data) { LegacySource = () => legacy };
        store.Load();
        Assert.Equal("F7", store.Skills.Get(SkillIds.Fix)!.Hotkey);
        Assert.True(store.Settings.LlmEnabled);
        Assert.Equal("openrouter", store.Settings.LlmProvider);
        Assert.Equal(7.5, store.Settings.LlmTimeout);
        Assert.True(File.Exists(Path.Combine(data, "secrets.dpapi")));
        Assert.True(store.Settings.LegacyImported);

        // Changed after the import: a second start must not overwrite it.
        store.Skills.Get(SkillIds.Fix)!.Hotkey = "F9";
        store.SaveSkills();
        var again = new UserDataStore(data) { LegacySource = () => legacy };
        again.Load();
        Assert.Equal("F9", again.Skills.Get(SkillIds.Fix)!.Hotkey);
    }

    [Fact]
    public void The_data_directory_can_be_overridden()
    {
        Environment.SetEnvironmentVariable(DataDirectory.OverrideVariable, _dir);
        try
        {
            Assert.Equal(Path.GetFullPath(_dir), DataDirectory.Resolve());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideVariable, null);
        }
        Assert.EndsWith(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? "TinyFix" : "tinyfix", DataDirectory.ForCurrentOs());
    }

    private sealed class FixedCaret(int caretX = 10, int caretY = 10, int cursorX = 20, int cursorY = 20) : ICaretLocator
    {
        public ScreenPoint GetCaretOrCursor() => new(caretX, caretY);

        public ScreenPoint GetCursor() => new(cursorX, cursorY);
    }

    private sealed class NoAutostart : IAutostart
    {
        public bool IsEnabled => false;

        public void Enable(string executablePath)
        {
        }

        public void Disable()
        {
        }
    }
}
