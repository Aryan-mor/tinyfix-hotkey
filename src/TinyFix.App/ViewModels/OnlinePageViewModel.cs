// Online AI page: provider, key, models, timeout. The key goes to the platform secret store and is never shown again
// or logged; the other fields live in settings.json.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Llm;

namespace TinyFix.App.ViewModels;

/// <summary>View model of the Online AI page.</summary>
public sealed partial class OnlinePageViewModel : PageViewModel
{
    private static readonly string[] ProviderIds = [.. LlmDefaults.Providers];
    private readonly AppServices _s;

    /// <summary>Creates the page.</summary>
    public OnlinePageViewModel(AppServices services)
        : base(services.Loc, "nav.online")
    {
        _s = services;
        var settings = _s.Store.Settings;
        _enabled = settings.LlmEnabled;
        _providerIndex = Math.Max(0, Array.IndexOf(ProviderIds, settings.LlmProvider));
        _model = settings.LlmModel;
        _fallbackModel = settings.LlmFallbackModel;
        _baseUrl = settings.LlmBaseUrl;
        _timeout = settings.LlmTimeout.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        _targetLanguage = settings.LlmTargetLanguage;
        HasSavedKey = _s.Platform.Secrets.Get(ProviderIds[_providerIndex]).Length > 0;
        SecretWarning = _s.Platform.Secrets.Warning is null ? "" : _s.Loc["online.secret_warning"];
        Track();
    }

    [ObservableProperty]
    private bool _enabled;

    /// <summary>0 Google, 1 OpenRouter, 2 OpenAI-compatible.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBaseUrl))]
    private int _providerIndex;

    /// <summary>A new key typed by the user; empty keeps the saved one.</summary>
    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _model = "";

    [ObservableProperty]
    private string _fallbackModel = "";

    [ObservableProperty]
    private string _baseUrl = "";

    [ObservableProperty]
    private string _timeout = "5";

    [ObservableProperty]
    private string _targetLanguage = "";

    /// <summary>Result of the last test or save.</summary>
    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _busy;

    /// <summary>True when a key is already saved for the chosen provider.</summary>
    [ObservableProperty]
    private bool _hasSavedKey;

    /// <summary>Warning about the secret store, or empty.</summary>
    public string SecretWarning { get; }

    /// <summary>The base URL only matters for an OpenAI-compatible server.</summary>
    public bool ShowBaseUrl => ProviderIndex == 2;

    private string Provider => ProviderIds[Math.Clamp(ProviderIndex, 0, ProviderIds.Length - 1)];

    partial void OnProviderIndexChanged(int value)
    {
        HasSavedKey = _s.Platform.Secrets.Get(Provider).Length > 0;
        // A model that belongs to another provider is useless here: offer this provider's defaults.
        Model = LlmDefaults.DefaultModelFor(Provider);
        FallbackModel = Provider switch
        {
            LlmDefaults.ProviderGoogle => LlmDefaults.GoogleFallbackModel,
            LlmDefaults.ProviderOpenRouter => LlmDefaults.OpenRouterFallbackModel,
            _ => "",
        };
    }

    /// <summary>Saves the fields (and the key, when one was typed).</summary>
    [RelayCommand]
    private void Save()
    {
        var settings = _s.Store.Settings;
        settings.LlmEnabled = Enabled;
        settings.LlmProvider = Provider;
        settings.LlmModel = Model.Trim();
        settings.LlmFallbackModel = FallbackModel.Trim();
        settings.LlmBaseUrl = BaseUrl.Trim();
        settings.LlmTargetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? "English" : TargetLanguage.Trim();
        if (double.TryParse(Timeout, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            settings.LlmTimeout = Math.Min(seconds, 120);
        _s.Store.SaveSettings();
        if (ApiKey.Length > 0)
        {
            try
            {
                _s.Platform.Secrets.Set(Provider, ApiKey.Trim());
                ApiKey = "";
                HasSavedKey = true;
            }
            catch (IOException)
            {
                Status = _s.Loc.Format("online.test_failed", "secret store");
                return;
            }
        }
        Status = _s.Loc["online.saved"];
    }

    /// <summary>Sends a tiny request to check the key and the model.</summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        Busy = true;
        Status = _s.Loc["online.testing"];
        try
        {
            var key = ApiKey.Length > 0 ? ApiKey.Trim() : _s.Platform.Secrets.Get(Provider);
            var config = new LlmConfig
            {
                Provider = Provider,
                ApiKey = key,
                Model = Model.Trim(),
                FallbackModel = "",
                BaseUrl = BaseUrl.Trim(),
                TimeoutSeconds = double.TryParse(Timeout, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) && t > 0 ? t : 5,
                Enabled = true,
            };
            var answer = await _s.Llm.ProbeConnectionAsync(config).ConfigureAwait(true);
            Status = _s.Loc.Format("online.test_ok", answer);
        }
        catch (LlmException ex)
        {
            // The message never holds the key (LlmClient sanitizes it).
            Status = _s.Loc.Format("online.test_failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private bool CanTest() => !Busy;
}
