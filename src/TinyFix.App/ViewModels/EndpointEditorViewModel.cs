// One endpoint (main or fallback): protocol preset, model, base URL, optional key and timeout, plus a test button.
// The key is kept in the secret store. This control is shared by the AI models page and the per-skill override.
// The view model never reads a saved key: it only asks the store whether one exists.

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Llm;
using TinyFix.Core.Settings;

namespace TinyFix.App.ViewModels;

/// <summary>What the API-key field is showing.</summary>
public enum KeyFieldState
{
    /// <summary>No key is stored. The password box is the only control.</summary>
    None,

    /// <summary>A key is stored. The field shows a fixed mask, not the key.</summary>
    Saved,

    /// <summary>The user is typing a replacement. The stored key is kept until Save succeeds.</summary>
    Replacing,

    /// <summary>The user is confirming that the stored key should be deleted.</summary>
    ConfirmingRemove,
}

/// <summary>Edits one LLM endpoint. <see cref="Changed"/> fires after a user edit, not while <see cref="Load"/> runs.</summary>
public sealed partial class EndpointEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>Twelve dots. The mask never depends on the saved key or its length.</summary>
    public const string SavedKeyMask = "••••••••••••";

    private readonly AppServices _s;
    private readonly string _titleKey;
    private readonly string _useKey;
    private bool _loading = true;
    private bool _mirrorToProvider;

    /// <summary>Creates an editor. <paramref name="slot"/> is the secret-store account for this endpoint's own key.</summary>
    public EndpointEditorViewModel(AppServices services, string slot, string titleKey, string useKey, bool allowSkip)
    {
        _s = services;
        Slot = slot;
        _titleKey = titleKey;
        _useKey = useKey;
        AllowSkip = allowSkip;
        _s.Loc.LanguageChanged += OnLanguageChanged;
        _loading = false;
    }

    /// <summary>Secret-store account of this endpoint (<c>main</c>, <c>fallback</c>, or a skill slot).</summary>
    public string Slot { get; set; }

    /// <summary>True when the endpoint can be turned off (the fallback, and a skill override).</summary>
    public bool AllowSkip { get; }

    /// <summary>
    /// When true, a saved key is also written to (and removed from) the protocol account, so a key stored under the
    /// provider id still counts. Skill overrides leave this false and only touch their own slot.
    /// </summary>
    public bool MirrorToProvider
    {
        get => _mirrorToProvider;
        set
        {
            if (_mirrorToProvider == value)
                return;
            _mirrorToProvider = value;
            if (!_loading)
                RefreshKeyState();
        }
    }

    /// <summary>The section title.</summary>
    public string Title => _s.Loc[_titleKey];

    /// <summary>The checkbox caption, when <see cref="AllowSkip"/> is set.</summary>
    public string UseLabel => _s.Loc[_useKey];

    /// <summary>The preset's default base URL, shown as the empty field's placeholder.</summary>
    public string BaseUrlHint => LlmProtocols.DefaultBaseUrl(Protocol);

    /// <summary>The fixed mask drawn when a key is saved. It is not the key.</summary>
    public string MaskedKey => SavedKeyMask;

    /// <summary>Accessible name of the show/hide button.</summary>
    public string RevealName => _s.Loc[Reveal ? "models.hide_key" : "models.show_key"];

    /// <summary>Mask character while the draft is hidden. <c>\0</c> shows what the user is typing.</summary>
    public char PasswordChar => Reveal ? '\0' : '*';

    /// <summary>True when the eye icon (show the draft) is drawn.</summary>
    public bool ShowEye => !Reveal;

    /// <summary>True when the crossed-out eye (hide the draft) is drawn.</summary>
    public bool ShowEyeOff => Reveal;

    /// <summary>True when the password box is on screen.</summary>
    public bool ShowInput => State is KeyFieldState.None or KeyFieldState.Replacing;

    /// <summary>True when the saved-key row is on screen.</summary>
    public bool IsSaved => State == KeyFieldState.Saved;

    /// <summary>True when Save and Cancel for a replacement are on screen.</summary>
    public bool IsReplacing => State == KeyFieldState.Replacing;

    /// <summary>True when the remove confirmation is on screen.</summary>
    public bool IsConfirmingRemove => State == KeyFieldState.ConfirmingRemove;

    /// <summary>Raised after the user changes a field. Key save and remove do not raise it.</summary>
    public event EventHandler? Changed;

    /// <summary>False hides the fields: this endpoint is not used.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsVisible))]
    private bool _useThis = true;

    /// <summary>Index into <see cref="LlmProtocols.All"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Protocol), nameof(BaseUrlHint))]
    private int _protocolIndex;

    [ObservableProperty]
    private string _model = "";

    [ObservableProperty]
    private string _baseUrl = "";

    /// <summary>Text the user is typing. Never a key read back from the store.</summary>
    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _timeout = "5";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _busy;

    [ObservableProperty]
    private bool _hasSavedKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordChar), nameof(RevealName), nameof(ShowEye), nameof(ShowEyeOff))]
    private bool _reveal;

    /// <summary>Which key controls are visible.</summary>
    public KeyFieldState State { get; private set; } = KeyFieldState.None;

    /// <summary>The preset id.</summary>
    public string Protocol => LlmProtocols.All[Math.Clamp(ProtocolIndex, 0, LlmProtocols.All.Count - 1)];

    /// <summary>True when the fields are on screen.</summary>
    public bool FieldsVisible => !AllowSkip || UseThis;

    /// <summary>Copies <paramref name="settings"/> in without raising <see cref="Changed"/>.</summary>
    public void Load(LlmEndpointSettings? settings, bool use)
    {
        _loading = true;
        UseThis = !AllowSkip || use;
        var protocol = LlmProtocols.Normalize(settings?.Protocol);
        ProtocolIndex = Math.Max(0, IndexOf(protocol));
        Model = settings?.Model ?? LlmProtocols.DefaultModel(protocol);
        BaseUrl = settings?.BaseUrl ?? "";
        var seconds = settings is { Timeout: > 0 } ? settings.Timeout : LlmDefaults.DefaultTimeoutSeconds;
        Timeout = seconds.ToString("0.#", CultureInfo.InvariantCulture);
        ApiKey = "";
        Reveal = false;
        Status = "";
        SetState(KeyFieldState.None);
        _loading = false;
        RefreshKeyState();
    }

    /// <summary>The fields, ready to store. The key is not included.</summary>
    public LlmEndpointSettings ToSettings() => new()
    {
        Protocol = Protocol,
        Model = Model.Trim(),
        BaseUrl = BaseUrl.Trim(),
        Timeout = ParseTimeout(),
    };

    /// <summary>
    /// Stores a newly typed key (the empty-field state). A replacement uses <see cref="SaveReplacement"/> instead,
    /// so a half-typed replacement is not written by the page Save. An empty draft is ignored.
    /// </summary>
    public void RememberNewKey()
    {
        if (State != KeyFieldState.None)
            return;
        var key = ApiKey.Trim();
        if (key.Length == 0)
            return;
        if (!TryWrite(key))
            return;
        ClearDraft();
        SetState(KeyFieldState.None);
        RefreshKeyState();
    }

    /// <summary>Shows the password box. The stored key stays until <see cref="SaveReplacement"/> succeeds.</summary>
    [RelayCommand]
    private void BeginReplace()
    {
        if (State != KeyFieldState.Saved)
            return;
        ClearDraft();
        SetState(KeyFieldState.Replacing);
    }

    /// <summary>Writes the trimmed draft immediately. Whitespace is ignored and the stored key is left as it is.</summary>
    [RelayCommand]
    private void SaveReplacement()
    {
        if (State != KeyFieldState.Replacing)
            return;
        var key = ApiKey.Trim();
        if (key.Length == 0)
            return;
        if (!TryWrite(key))
            return;
        ClearDraft();
        SetState(KeyFieldState.None);
        RefreshKeyState();
    }

    /// <summary>Asks, in the field, whether the stored key should be deleted.</summary>
    [RelayCommand]
    private void BeginRemove()
    {
        if (State != KeyFieldState.Saved)
            return;
        SetState(KeyFieldState.ConfirmingRemove);
    }

    /// <summary>Deletes the stored key now and returns to the empty password box.</summary>
    [RelayCommand]
    private void ConfirmRemove()
    {
        if (State != KeyFieldState.ConfirmingRemove)
            return;
        if (!TryDelete())
            return;
        ClearDraft();
        SetState(KeyFieldState.None);
        RefreshKeyState();
    }

    /// <summary>Leaves replace or remove confirmation. The stored key is not changed.</summary>
    [RelayCommand]
    private void CancelKeyEdit()
    {
        if (State is not (KeyFieldState.Replacing or KeyFieldState.ConfirmingRemove))
            return;
        ClearDraft();
        SetState(KeyFieldState.None);
        RefreshKeyState();
    }

    /// <summary>Shows or hides the characters the user is typing. It does not reveal a stored key.</summary>
    [RelayCommand]
    private void ToggleReveal() => Reveal = !Reveal;

    /// <summary>Sends "Reply with OK" to this endpoint only.</summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        Busy = true;
        Status = _s.Loc["models.testing"];
        try
        {
            var endpoint = new LlmEndpoint
            {
                Protocol = Protocol,
                Model = Model.Trim(),
                BaseUrl = BaseUrl.Trim(),
                TimeoutSeconds = ParseTimeout(),
            };
            var typed = ShowInput ? ApiKey : "";
            var answer = await EndpointProbe.RunAsync(_s, endpoint, Slot, typed).ConfigureAwait(true);
            Status = _s.Loc.Format("models.test_ok", answer);
        }
        catch (LlmException ex)
        {
            // The message never holds the key (LlmClient sanitizes it) or the user's text.
            Status = _s.Loc.Format("models.test_failed", ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private bool CanTest() => !Busy;

    /// <inheritdoc />
    public void Dispose() => _s.Loc.LanguageChanged -= OnLanguageChanged;

    partial void OnUseThisChanged(bool value)
    {
        if (_loading)
            return;
        if (value && string.IsNullOrWhiteSpace(Model))
            Model = LlmProtocols.DefaultModel(Protocol);
        Notify();
    }

    partial void OnProtocolIndexChanged(int value)
    {
        if (_loading)
            return;
        // A preset only fills the defaults. The boxes stay editable.
        Model = LlmProtocols.DefaultModel(Protocol);
        BaseUrl = LlmProtocols.DefaultBaseUrl(Protocol);
        if (State is KeyFieldState.Replacing or KeyFieldState.ConfirmingRemove)
        {
            ClearDraft();
            SetState(KeyFieldState.None);
        }
        RefreshKeyState();
        Notify();
    }

    partial void OnModelChanged(string value) => Notify();

    partial void OnBaseUrlChanged(string value) => Notify();

    partial void OnTimeoutChanged(string value) => Notify();

    partial void OnApiKeyChanged(string value) => Notify();

    private void Notify()
    {
        if (!_loading)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(UseLabel));
        OnPropertyChanged(nameof(RevealName));
    }

    private double ParseTimeout() =>
        double.TryParse(Timeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? Math.Min(seconds, 120)
            : LlmDefaults.DefaultTimeoutSeconds;

    private void RefreshKeyState()
    {
        var saved = QueryHasKey();
        HasSavedKey = saved;
        if (State is KeyFieldState.Replacing or KeyFieldState.ConfirmingRemove)
            return;
        SetState(saved ? KeyFieldState.Saved : KeyFieldState.None);
        if (saved)
            ClearDraft();
    }

    private bool QueryHasKey()
    {
        var secrets = _s.Platform.Secrets;
        if (secrets.Contains(Slot))
            return true;
        if (!MirrorToProvider)
            return false;
        if (secrets.Contains(Protocol))
            return true;
        return Protocol is LlmProtocols.OpenAi or LlmProtocols.Custom && secrets.Contains(LlmDefaults.ProviderOpenAi);
    }

    private bool TryWrite(string key)
    {
        try
        {
            _s.Platform.Secrets.Set(Slot, key);
            if (MirrorToProvider)
                _s.Platform.Secrets.Set(Protocol, key);
            return true;
        }
        catch (IOException)
        {
            Status = _s.Loc.Format("models.test_failed", "secret store");
            return false;
        }
    }

    private bool TryDelete()
    {
        try
        {
            _s.Platform.Secrets.Set(Slot, "");
            if (MirrorToProvider)
            {
                _s.Platform.Secrets.Set(Protocol, "");
                if (Protocol is LlmProtocols.OpenAi or LlmProtocols.Custom)
                    _s.Platform.Secrets.Set(LlmDefaults.ProviderOpenAi, "");
            }
            return true;
        }
        catch (IOException)
        {
            Status = _s.Loc.Format("models.test_failed", "secret store");
            return false;
        }
    }

    private void ClearDraft()
    {
        _loading = true;
        ApiKey = "";
        Reveal = false;
        _loading = false;
    }

    private void SetState(KeyFieldState value)
    {
        if (State == value)
            return;
        State = value;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(ShowInput));
        OnPropertyChanged(nameof(IsSaved));
        OnPropertyChanged(nameof(IsReplacing));
        OnPropertyChanged(nameof(IsConfirmingRemove));
    }

    private static int IndexOf(string protocol)
    {
        for (var i = 0; i < LlmProtocols.All.Count; i++)
            if (LlmProtocols.All[i] == protocol)
                return i;
        return 0;
    }
}
