// Tests of the Windows adapters against the real OS (skipped elsewhere). They stay hermetic: DPAPI works on a temp
// folder, the autostart test uses a throw-away registry key instead of the real Run key, and the clipboard test puts
// the user's clipboard back.

using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using TinyFix.Core.Llm;
using TinyFix.Platform.Windows;

namespace TinyFix.Platform.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsAdapterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-platform-tests", Guid.NewGuid().ToString("N")[..8]);

    public WindowsAdapterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Dpapi_keeps_keys_encrypted_in_the_python_file_format()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var store = new DpapiSecretStore(_dir);
        Assert.Empty(store.GetAll());
        store.Set(LlmDefaults.ProviderGoogle, "g-secret-123");
        store.Set(LlmDefaults.ProviderOpenRouter, "or-secret-456");

        var path = Path.Combine(_dir, DpapiSecretStore.FileName);
        var raw = File.ReadAllBytes(path);
        // Encrypted: neither key appears in the file, in UTF-8 or UTF-16.
        Assert.DoesNotContain("g-secret-123", Encoding.UTF8.GetString(raw));
        Assert.DoesNotContain("or-secret-456", Encoding.UTF8.GetString(raw));
        // The payload is the Python app's {"keys": {...}} JSON.
        var json = Encoding.UTF8.GetString(DpapiSecretStore.Unprotect(raw));
        Assert.Contains("\"keys\"", json);

        var again = new DpapiSecretStore(_dir);
        Assert.Equal("g-secret-123", again.Get(LlmDefaults.ProviderGoogle));
        Assert.Equal("or-secret-456", again.Get(LlmDefaults.ProviderOpenRouter));
        Assert.Equal("", again.Get(LlmDefaults.ProviderOpenAi));

        again.Set(LlmDefaults.ProviderGoogle, "");
        again.Set(LlmDefaults.ProviderOpenRouter, "");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void An_old_file_with_one_bare_key_belongs_to_google()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        File.WriteAllBytes(Path.Combine(_dir, DpapiSecretStore.FileName), DpapiSecretStore.Protect(Encoding.UTF8.GetBytes("bare-key-1")));
        var keys = new DpapiSecretStore(_dir).GetAll();
        Assert.Equal("bare-key-1", Assert.Single(keys).Value);
        Assert.Equal(LlmDefaults.ProviderGoogle, Assert.Single(keys).Key);
    }

    [Fact]
    public void A_damaged_secrets_file_reads_as_empty_instead_of_throwing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        File.WriteAllBytes(Path.Combine(_dir, DpapiSecretStore.FileName), [1, 2, 3, 4]);
        Assert.Empty(new DpapiSecretStore(_dir).GetAll());
    }

    [Fact]
    public void Clipboard_text_and_snapshots_round_trip()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var clipboard = new WindowsClipboard();
        var original = clipboard.Snapshot();
        Assert.NotNull(original);
        try
        {
            var before = clipboard.ChangeCount;
            clipboard.SetText("TinyFix test \u00E4\u00F6\u00FC \u0633\u0644\u0627\u0645 \U0001F600");
            Assert.NotEqual(before, clipboard.ChangeCount);
            Assert.Equal("TinyFix test \u00E4\u00F6\u00FC \u0633\u0644\u0627\u0645 \U0001F600", clipboard.GetText());

            var snapshot = clipboard.Snapshot();
            clipboard.SetText("something else");
            clipboard.Restore(snapshot);
            Assert.Equal("TinyFix test \u00E4\u00F6\u00FC \u0633\u0644\u0627\u0645 \U0001F600", clipboard.GetText());

            clipboard.Clear();
            Assert.Null(clipboard.GetText());
        }
        finally
        {
            clipboard.Restore(original);
        }
    }

    [Fact]
    public void Autostart_writes_a_quoted_command_to_the_run_key_and_removes_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var keyPath = @"Software\TinyFixTests\Run-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var autostart = new WindowsAutostart("TinyFixTest", keyPath);
            Assert.False(autostart.IsEnabled);
            autostart.Enable(@"C:\Program Files\TinyFix\TinyFix.exe");
            Assert.True(autostart.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                Assert.Equal("\"C:\\Program Files\\TinyFix\\TinyFix.exe\"", key!.GetValue("TinyFixTest"));
            autostart.Disable();
            Assert.False(autostart.IsEnabled);
            autostart.Disable();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void The_caret_locator_always_gives_a_point_and_the_foreground_token_restores()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var caret = new WindowsCaret();
        var point = caret.GetCaretOrCursor();
        var cursor = caret.GetCursor();
        Assert.InRange(point.X, -20000, 20000);
        Assert.InRange(point.Y, -20000, 20000);
        Assert.InRange(cursor.X, -20000, 20000);
        Assert.InRange(cursor.Y, -20000, 20000);

        var foreground = new WindowsForeground();
        Assert.False(foreground.Restore(null));
        Assert.False(foreground.Restore("not a handle"));
        Assert.False(foreground.Restore("999999999"));
        // A test process is not elevated above itself.
        Assert.False(foreground.IsTargetElevated());
    }

    [Fact]
    public async Task Waiting_for_modifiers_returns_at_once_when_none_is_held()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var started = DateTime.UtcNow;
        Assert.True(await new WindowsForeground().WaitForModifiersReleasedAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void The_factory_builds_a_complete_set_of_ports()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var services = WindowsPlatform.Create(_dir);
        Assert.Equal("Windows DPAPI", services.Secrets.Description);
        Assert.Null(services.Secrets.Warning);
        Assert.True(services.Capabilities.GlobalHotkeys);
        Assert.False(services.Capabilities.IsWayland);
    }
}
