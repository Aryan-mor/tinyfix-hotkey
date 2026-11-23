// AI models page: a main endpoint and an optional fallback. Keys go to the secret store and are never shown again
// or logged; the other fields live in settings.json. Translate-into lives on the Variables page.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Llm;

namespace TinyFix.App.ViewModels;

/// <summary>View model of the AI models page.</summary>
public sealed partial class OnlinePageViewModel : PageViewModel
{
    private readonly AppServices _s;

    /// <summary>Creates the page.</summary>
    public OnlinePageViewModel(AppServices services)
        : base(services.Loc, "nav.models")
    {
        _s = services;
        var settings = _s.Store.Settings;
        Main = new EndpointEditorViewModel(services, LlmSecrets.MainSlot, "models.main", "models.fallback_enable", allowSkip: false);
        Fallback = new EndpointEditorViewModel(services, LlmSecrets.FallbackSlot, "models.fallback", "models.fallback_enable", allowSkip: true);
        Main.MirrorToProvider = true;
        Main.Load(settings.Llm.Main, use: true);
        Fallback.Load(settings.Llm.Fallback, use: settings.Llm.Fallback is not null);
        SyncFallbackMirror();
        Main.Changed += (_, _) => SyncFallbackMirror();
        Fallback.Changed += (_, _) => SyncFallbackMirror();
        SecretWarning = _s.Platform.Secrets.Warning is null ? "" : _s.Loc["models.secret_warning"];
        Track();
    }

    /// <summary>The endpoint tried first.</summary>
    public EndpointEditorViewModel Main { get; }

    /// <summary>The endpoint tried when the main one fails. Optional.</summary>
    public EndpointEditorViewModel Fallback { get; }

    /// <summary>Result of the last save.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>Warning about the secret store, or empty.</summary>
    public string SecretWarning { get; }

    /// <summary>Saves both endpoints. A newly typed key is stored here; replacing or removing a key already wrote it.</summary>
    [RelayCommand]
    private void Save()
    {
        var settings = _s.Store.Settings;
        settings.Llm.Main = Main.ToSettings();
        settings.Llm.Fallback = Fallback.UseThis ? Fallback.ToSettings() : null;
        SyncFallbackMirror();
        try
        {
            Main.RememberNewKey();
            if (Fallback.UseThis)
                Fallback.RememberNewKey();
        }
        catch (IOException)
        {
            Status = _s.Loc.Format("models.test_failed", "secret store");
            return;
        }
        _s.Store.SaveSettings();
        Status = _s.Loc["models.saved"];
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Main.Dispose();
        Fallback.Dispose();
        base.Dispose();
    }

    private void SyncFallbackMirror() => Fallback.MirrorToProvider = Fallback.Protocol != Main.Protocol;
}
