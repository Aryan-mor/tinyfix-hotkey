// Tests that "start with the system" turns itself on once, on the first run, and then leaves the user's choice alone.

using TinyFix.Core.Platform;
using TinyFix.Core.Settings;

namespace TinyFix.Core.Tests;

public sealed class AutostartDefaultTests
{
    private sealed class FakeAutostart : IAutostart
    {
        public bool IsEnabled { get; private set; }

        public string? Path { get; private set; }

        public void Enable(string executablePath)
        {
            IsEnabled = true;
            Path = executablePath;
        }

        public void Disable() => IsEnabled = false;
    }

    [Fact]
    public void The_first_run_turns_it_on_and_asks_for_a_save()
    {
        var settings = new AppSettings();
        var autostart = new FakeAutostart();

        var changed = AutostartDefault.ApplyOnce(settings, autostart, @"C:\Apps\TinyFix.exe");

        Assert.True(changed);
        Assert.True(autostart.IsEnabled);
        Assert.Equal(@"C:\Apps\TinyFix.exe", autostart.Path);
        Assert.True(settings.AutostartDefaultApplied);
    }

    [Fact]
    public void A_user_who_switched_it_off_stays_off()
    {
        var settings = new AppSettings();
        var autostart = new FakeAutostart();
        AutostartDefault.ApplyOnce(settings, autostart, @"C:\Apps\TinyFix.exe");
        autostart.Disable(); // the Settings switch

        var changed = AutostartDefault.ApplyOnce(settings, autostart, @"C:\Apps\TinyFix.exe");

        Assert.False(changed);
        Assert.False(autostart.IsEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData("/usr/share/dotnet/dotnet")]
    public void Running_from_the_dotnet_host_registers_nothing(string? path)
    {
        var settings = new AppSettings();
        var autostart = new FakeAutostart();

        var changed = AutostartDefault.ApplyOnce(settings, autostart, path);

        Assert.False(changed);
        Assert.False(autostart.IsEnabled);
        Assert.False(settings.AutostartDefaultApplied); // a real run later still gets the default
    }

    [Fact]
    public void A_stale_registration_of_an_old_install_is_replaced_by_the_running_program()
    {
        var settings = new AppSettings();
        var autostart = new FakeAutostart();
        autostart.Enable(@"D:\Old\TinyFix.exe");

        var changed = AutostartDefault.ApplyOnce(settings, autostart, @"C:\Apps\TinyFix.exe");

        Assert.True(changed);
        Assert.Equal(@"C:\Apps\TinyFix.exe", autostart.Path);
        Assert.True(settings.AutostartDefaultApplied);
    }
}
