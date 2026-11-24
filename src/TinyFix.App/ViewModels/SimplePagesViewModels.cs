// The smaller settings pages: Updates and About.

using System.Reflection;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Updates;

namespace TinyFix.App.ViewModels;

/// <summary>Update check.</summary>
public sealed partial class UpdatesPageViewModel : PageViewModel
{
    private readonly AppServices _s;
    private LatestRelease? _latest;

    /// <summary>Creates the page.</summary>
    public UpdatesPageViewModel(AppServices services)
        : base(services.Loc, "nav.updates")
    {
        _s = services;
        _auto = _s.Store.Settings.CheckForUpdates;
        Track();
    }

    /// <summary>Check once a day automatically.</summary>
    [ObservableProperty]
    private bool _auto;

    /// <summary>Result of the last check.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>True while a check runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    private bool _busy;

    /// <summary>True when a newer release was found.</summary>
    [ObservableProperty]
    private bool _hasUpdate;

    partial void OnAutoChanged(bool value)
    {
        _s.Store.Settings.CheckForUpdates = value;
        _s.Store.SaveSettings();
    }

    /// <summary>Asks GitHub whether a newer release exists.</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckNowAsync()
    {
        Busy = true;
        Status = _s.Loc["updates.checking"];
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _latest = await UpdateChecker.FetchLatestAsync(http).ConfigureAwait(true);
            var current = AboutPageViewModel.CurrentVersion;
            HasUpdate = _latest is not null && UpdateChecker.IsNewer(_latest.Version.ToString(), current);
            Status = _latest is null ? _s.Loc["updates.failed"]
                : HasUpdate ? _s.Loc.Format("updates.available", _latest.Version)
                : _s.Loc.Format("updates.up_to_date", current);
            _s.Store.Settings.LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            _s.Store.SaveSettings();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException)
        {
            HasUpdate = false;
            Status = _s.Loc["updates.failed"];
        }
        finally
        {
            Busy = false;
        }
    }

    private bool CanCheck() => !Busy;

    /// <summary>Downloads the new setup program, runs it silently and exits so it can replace the app.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_latest?.InstallerUrl is not { } installerUrl || !OperatingSystem.IsWindows())
        {
            UrlOpener.Open(_latest?.Url ?? UpdateChecker.LatestPage);
            return;
        }
        Busy = true;
        HasUpdate = false;
        Status = _s.Loc["updates.downloading"];
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var downloading = true;
            var progress = new Progress<int>(p =>
            {
                if (downloading)
                    Status = $"{_s.Loc["updates.downloading"]} {p}%";
            });
            var path = await UpdateChecker.DownloadInstallerAsync(http, installerUrl, default, progress).ConfigureAwait(true);
            downloading = false;
            Status = _s.Loc["updates.installing"];
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path,
                "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
            { UseShellExecute = true });
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            HasUpdate = true;
            Status = _s.Loc["updates.install_failed"];
        }
        finally
        {
            Busy = false;
        }
    }
}

/// <summary>Version, links and notices.</summary>
public sealed partial class AboutPageViewModel : PageViewModel
{
    private readonly AppServices _s;

    /// <summary>Creates the page.</summary>
    public AboutPageViewModel(AppServices services)
        : base(services.Loc, "nav.about")
    {
        _s = services;
        Notices = ReadNotices();
        Track();
    }

    /// <summary>The running version (from the assembly, set by Directory.Build.props).</summary>
    public static string CurrentVersion =>
        typeof(AboutPageViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "2.0.0";

    /// <summary>"Version 2.0.0".</summary>
    public string VersionText => _s.Loc.Format("about.version", CurrentVersion);

    /// <summary>The repository URL (from the assembly metadata).</summary>
    public string RepositoryUrl { get; } =
        typeof(AboutPageViewModel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value
        ?? "https://github.com/Aryan-mor/tinyfix-hotkey";

    /// <summary>Where the API keys are kept.</summary>
    public string SecretBackend => _s.Loc.Format("models.secret_backend", _s.Platform.Secrets.Description);

    /// <summary>The third-party notices text.</summary>
    public string Notices { get; }

    /// <summary>True on Linux and macOS, where support is experimental.</summary>
    public bool ShowExperimental => !OperatingSystem.IsWindows();

    private static string ReadNotices()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://TinyFix/Assets/THIRD-PARTY-NOTICES.md"));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException)
        {
            return "";
        }
    }

    /// <summary>Opens the project page.</summary>
    public void OpenRepository() => UrlOpener.Open(RepositoryUrl);

    /// <summary>Opens setup again. The saved model, skills, and keys stay.</summary>
    [RelayCommand]
    private void RunSetupAgain() => _s.Windows.ShowSetup();

    /// <inheritdoc />
    protected override void OnLanguageChanged(object? sender, EventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(SecretBackend));
    }
}
