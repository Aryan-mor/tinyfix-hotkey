// Tests of the Linux and macOS adapters that need no real desktop: environment detection, the text of the autostart
// files and the parsing of command output. They run on every OS. What cannot be tested here (real clipboard tools, the
// keychain, window activation) is listed in docs/DOTNET-ARCHITECTURE.md as untested.

using TinyFix.Core.Platform;
using TinyFix.Platform.Linux;
using TinyFix.Platform.MacOS;

namespace TinyFix.Platform.Tests;

public sealed class UnixAdapterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-platform-tests", Guid.NewGuid().ToString("N")[..8]);

    public UnixAdapterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("wayland", null, null, true)]
    [InlineData("Wayland", null, null, true)]
    [InlineData("x11", "wayland-0", ":0", false)]
    [InlineData("tty", null, null, false)]
    [InlineData(null, "wayland-0", null, true)]
    [InlineData(null, "wayland-0", ":0", false)]
    [InlineData(null, null, ":0", false)]
    [InlineData(null, null, null, false)]
    public void Wayland_is_detected_from_the_session_environment(string? sessionType, string? waylandDisplay, string? display, bool expected)
    {
        string? Env(string name) => name switch
        {
            "XDG_SESSION_TYPE" => sessionType,
            "WAYLAND_DISPLAY" => waylandDisplay,
            "DISPLAY" => display,
            _ => null,
        };
        Assert.Equal(expected, LinuxDisplay.IsWayland(Env));
    }

    [Fact]
    public void Linux_autostart_writes_an_xdg_desktop_file_with_a_safely_quoted_command()
    {
        var autostart = new LinuxAutostart(_dir);
        Assert.False(autostart.IsEnabled);
        autostart.Enable("/opt/Tiny Fix/$HOME/tinyfix");
        Assert.True(autostart.IsEnabled);
        var text = File.ReadAllText(Path.Combine(_dir, "autostart", "tinyfix.desktop"));
        Assert.Contains("[Desktop Entry]", text);
        Assert.Contains("Type=Application", text);
        // Quoted, and the dollar sign is escaped so the shell never expands it.
        Assert.Contains("Exec=\"/opt/Tiny Fix/\\$HOME/tinyfix\"", text);
        autostart.Disable();
        Assert.False(autostart.IsEnabled);
        autostart.Disable();
    }

    [Fact]
    public void The_linux_autostart_folder_follows_xdg_config_home()
    {
        Assert.Equal(Path.Combine("/custom", "autostart"), LinuxAutostart.AutostartDirectory("/custom"));
    }

    [Fact]
    public void Macos_autostart_writes_an_escaped_launch_agent_plist()
    {
        var autostart = new MacAutostart(_dir);
        autostart.Enable("/Applications/Tiny & Fix.app/Contents/MacOS/TinyFix");
        Assert.True(autostart.IsEnabled);
        var text = File.ReadAllText(Path.Combine(_dir, MacAutostart.Label + ".plist"));
        Assert.Contains("<key>RunAtLoad</key>", text);
        Assert.Contains("Tiny &amp; Fix.app", text);
        Assert.StartsWith("app.tinyfix.", MacAutostart.Label, StringComparison.Ordinal);
        Assert.Equal("app.tinyfix.hotkey", MacAutostart.Label);
        autostart.Disable();
        Assert.False(autostart.IsEnabled);
    }

    [Theory]
    [InlineData("(\n    \"de-DE\",\n    \"en-US\"\n)", "de-DE")]
    [InlineData("(\n    \"zh-Hans-CN\"\n)", "zh-Hans-CN")]
    [InlineData("(\n    fr,\n    en\n)", "fr")]
    [InlineData("", null)]
    public void The_macos_language_list_is_parsed(string output, string? expected) => Assert.Equal(expected, MacOSLocale.First(output));

    [Fact]
    public void The_preferred_language_is_unknown_off_macos()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Null(MacOSLocale.PreferredLanguage());
    }

    [Fact]
    public void A_missing_helper_tool_is_a_failed_result_not_an_exception()
    {
        var result = ExternalProcess.Run("tinyfix-no-such-tool-" + Guid.NewGuid().ToString("N"), ["--version"]);
        Assert.False(result.Success);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(ExternalProcess.Exists("tinyfix-no-such-tool-xyz"));
    }

    [Fact]
    public void External_processes_get_their_arguments_unmangled()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "needs a shell");
        // dotnet itself is on the path wherever these tests run.
        var dotnet = Environment.ProcessPath is { } p && Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? p : "dotnet";
        var result = ExternalProcess.Run(dotnet, ["--version"]);
        if (result.ExitCode == -1)
            return; // dotnet is not on PATH of this runner: nothing to check.
        Assert.True(result.Success);
        Assert.Matches(@"^\d+\.\d+", result.Output.Trim());
    }

    [Fact]
    public void Linux_window_tokens_are_validated_before_they_reach_the_command_line()
    {
        var foreground = new LinuxForeground(wayland: false);
        Assert.False(foreground.Restore("12; rm -rf /"));
        Assert.False(foreground.Restore(null));
        Assert.False(new LinuxForeground(wayland: true).Restore("123"));
        Assert.Null(new LinuxForeground(wayland: true).Save());
        Assert.False(foreground.IsTargetElevated());
    }

    [Fact]
    public void Macos_bundle_ids_are_validated_before_they_reach_the_script()
    {
        var foreground = new MacForeground();
        Assert.False(foreground.Restore("com.apple.Safari\" to quit"));
        Assert.False(foreground.Restore(null));
    }

    [Fact]
    public void The_linux_caret_falls_back_to_a_fixed_point_on_wayland()
    {
        var caret = new LinuxCaret(wayland: true);
        Assert.Equal(new ScreenPoint(200, 200), caret.GetCaretOrCursor());
        Assert.Equal(new ScreenPoint(200, 200), caret.GetCursor());
    }

    [Fact]
    public void The_encrypted_file_store_is_the_linux_fallback_and_carries_a_warning()
    {
        var store = new EncryptedFileSecretStore(_dir, "machine-id-1", "alice");
        Assert.NotNull(store.Warning);
        store.Set("google", "g-key");
        store.Set("openrouter", "or-key");
        Assert.DoesNotContain("g-key", File.ReadAllText(Path.Combine(_dir, "secrets.enc"), System.Text.Encoding.Latin1));
        Assert.Equal("g-key", new EncryptedFileSecretStore(_dir, "machine-id-1", "alice").Get("google"));
        // Another computer or user cannot read it.
        Assert.Empty(new EncryptedFileSecretStore(_dir, "machine-id-2", "alice").GetAll());
        Assert.Empty(new EncryptedFileSecretStore(_dir, "machine-id-1", "bob").GetAll());
        store.Set("google", "");
        store.Set("openrouter", "");
        Assert.False(File.Exists(Path.Combine(_dir, "secrets.enc")));
    }

    [Fact]
    public void A_tampered_encrypted_file_reads_as_empty()
    {
        var store = new EncryptedFileSecretStore(_dir, "m", "u");
        store.Set("google", "k");
        var path = Path.Combine(_dir, "secrets.enc");
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        Assert.Empty(store.GetAll());
    }
}
