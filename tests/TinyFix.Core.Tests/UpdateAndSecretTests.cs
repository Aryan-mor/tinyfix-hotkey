// Update check parsing (ported from tests/test_update_check.py) and the encrypted-file secret store.

using System.Net;
using TinyFix.Core.Platform;
using TinyFix.Core.Updates;

namespace TinyFix.Core.Tests;

public sealed class UpdateAndSecretTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tinyfix-core-tests", Guid.NewGuid().ToString("N")[..8]);

    public UpdateAndSecretTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Parse_version_accepts_a_v_prefix_and_a_suffix()
    {
        Assert.Equal("1.2.3", UpdateChecker.Parse("v1.2.3").ToString());
        Assert.Equal("1.2.3", UpdateChecker.Parse("1.2.3").ToString());
        Assert.Equal(UpdateChecker.Parse("v2.0.0"), UpdateChecker.Parse("2.0.0"));
        Assert.Equal("2.0.0", UpdateChecker.Parse("2.0.0-dev").ToString());
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("1.2")]
    public void Parse_version_rejects_garbage(string text) => Assert.Throws<FormatException>(() => UpdateChecker.Parse(text));

    [Fact]
    public void Is_newer_compares_the_numbers_not_the_text()
    {
        Assert.True(UpdateChecker.IsNewer("1.1.0", "1.0.0"));
        Assert.True(UpdateChecker.IsNewer("v1.0.1", "1.0.0"));
        Assert.True(UpdateChecker.IsNewer("2.0.0", "1.9.9"));
        Assert.False(UpdateChecker.IsNewer("1.0.0", "1.0.0"));
        Assert.False(UpdateChecker.IsNewer("1.0.0", "1.1.0"));
        Assert.True(UpdateChecker.IsNewer("1.0.10", "1.0.9"));
        Assert.False(UpdateChecker.IsNewer("1.0.2", "1.0.10"));
    }

    [Fact]
    public void The_automatic_check_runs_once_a_day()
    {
        Assert.True(UpdateChecker.ShouldAutoCheck("", "2026-11-21"));
        Assert.False(UpdateChecker.ShouldAutoCheck("2026-11-21", "2026-11-21"));
        Assert.True(UpdateChecker.ShouldAutoCheck("2026-11-21", "2026-11-22"));
    }

    [Fact]
    public async Task The_latest_release_is_read_from_the_github_answer()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{\"tag_name\":\"v2.1.0\",\"html_url\":\"https://github.com/x/y/releases/tag/v2.1.0\"}"));
        using var http = new HttpClient(handler);
        var latest = await UpdateChecker.FetchLatestAsync(http, TestContext.Current.CancellationToken);
        Assert.Equal("2.1.0", latest!.Version.ToString());
        Assert.Equal("https://github.com/x/y/releases/tag/v2.1.0", latest.Url);
        Assert.StartsWith("https://api.github.com/", handler.Requests[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_release_without_a_usable_tag_gives_null_and_a_rate_limit_throws()
    {
        using var odd = new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{\"tag_name\":\"nightly\"}")));
        Assert.Null(await UpdateChecker.FetchLatestAsync(odd, TestContext.Current.CancellationToken));
        using var limited = new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}", HttpStatusCode.Forbidden)));
        await Assert.ThrowsAsync<HttpRequestException>(() => UpdateChecker.FetchLatestAsync(limited, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_encrypted_store_round_trips_and_refuses_other_machines()
    {
        var store = new EncryptedFileSecretStore(_dir, "machine-1", "alice");
        store.Set("google", "g-key");
        Assert.Equal("g-key", new EncryptedFileSecretStore(_dir, "machine-1", "alice").Get("google"));
        Assert.Empty(new EncryptedFileSecretStore(_dir, "machine-2", "alice").GetAll());
        store.Set("google", "");
        Assert.False(File.Exists(Path.Combine(_dir, "secrets.enc")));
    }
}
