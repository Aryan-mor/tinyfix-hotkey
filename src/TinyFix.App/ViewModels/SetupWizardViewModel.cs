// First-run setup: welcome, model, skills, done. The state machine lives here so tests drive it without a window.
// A saved API key is never read back; the field only asks the secret store whether one exists.

using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Hotkeys;
using TinyFix.Core.Llm;
using TinyFix.Core.Localization;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.ViewModels;

/// <summary>The four setup steps, in the order the window shows them.</summary>
public enum SetupStep
{
    /// <summary>The promise, the language, and a small donut.</summary>
    Welcome = 0,

    /// <summary>Provider, model, and API key.</summary>
    Model = 1,

    /// <summary>Skills, shortcuts, and the donut shortcut.</summary>
    Skills = 2,

    /// <summary>A short summary, autostart, and a donut preview.</summary>
    Done = 3,
}

/// <summary>One segment of the step indicator.</summary>
public sealed partial class SetupStepIndicator : ObservableObject
{
    private readonly string _key;

    /// <summary>Creates a segment for <paramref name="index"/>.</summary>
    public SetupStepIndicator(int index, string key) => (Index, _key) = (index, key);

    /// <summary>Zero-based step index.</summary>
    public int Index { get; }

    /// <summary>The short label under the bar.</summary>
    [ObservableProperty]
    private string _label = "";

    /// <summary>True when this is the step on screen.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>True when the user has already passed this step.</summary>
    [ObservableProperty]
    private bool _isDone;

    /// <summary>Refreshes the label and which segment is current.</summary>
    public void Refresh(ILocalizer loc, SetupStep step)
    {
        Label = loc[_key];
        IsCurrent = (int)step == Index;
        IsDone = (int)step > Index;
    }
}

/// <summary>One provider chip.</summary>
public sealed partial class SetupProviderCard : ObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;
    private readonly Action<string> _select;
    private bool _applying;

    /// <summary>Creates a chip for <paramref name="id"/>.</summary>
    public SetupProviderCard(string id, string titleKey, string descriptionKey, Action<string> select)
    {
        Id = id;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        _select = select;
        IsLocal = LlmProtocols.IsLocalPreset(id);
        SelectCommand = new RelayCommand(() => _select(id));
    }

    /// <summary>The protocol id.</summary>
    public string Id { get; }

    /// <summary>True for Ollama and LM Studio.</summary>
    public bool IsLocal { get; }

    /// <summary>The provider name.</summary>
    [ObservableProperty]
    private string _title = "";

    /// <summary>One neutral line under the name.</summary>
    [ObservableProperty]
    private string _description = "";

    /// <summary>True when this chip is the one in the form below.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Selects this provider.</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>Reloads the name and description, and whether this chip is selected, without selecting it again.</summary>
    public void Refresh(ILocalizer loc, bool selected)
    {
        Title = loc[_titleKey];
        Description = loc[_descriptionKey];
        if (IsSelected == selected)
            return;
        _applying = true;
        IsSelected = selected;
        _applying = false;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !_applying)
            _select(Id);
    }
}

/// <summary>One skill row, or the donut-menu shortcut row.</summary>
public sealed partial class SetupSkillRowViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly SetupWizardViewModel _wizard;
    private readonly Skill? _skill;
    private readonly string _descriptionKey;
    private bool _loading = true;

    private SetupSkillRowViewModel(AppServices services, SetupWizardViewModel wizard, Skill? skill, string id, string descriptionKey)
    {
        _services = services;
        _wizard = wizard;
        _skill = skill;
        SkillId = id;
        _descriptionKey = descriptionKey;
        IsMenu = skill is null;
        if (skill is not null)
        {
            _enabled = skill.Enabled;
            _shortcut = skill.Hotkey;
        }
        else
        {
            _enabled = true;
            _shortcut = services.Store.Skills.SkillMenuHotkey;
        }
        RefreshText();
        _loading = false;
    }

    /// <summary>A row for <paramref name="skill"/>.</summary>
    public static SetupSkillRowViewModel ForSkill(AppServices services, SetupWizardViewModel wizard, Skill skill)
    {
        var key = DescriptionKey(skill.Id);
        return new SetupSkillRowViewModel(services, wizard, skill, skill.Id, key);
    }

    /// <summary>The donut-menu shortcut, which has no on/off switch of its own.</summary>
    public static SetupSkillRowViewModel ForMenu(AppServices services, SetupWizardViewModel wizard) =>
        new(services, wizard, null, "menu", "setup.skills.menu_desc");

    /// <summary>The skill id, or <c>menu</c>.</summary>
    public string SkillId { get; }

    /// <summary>True for the donut-menu row.</summary>
    public bool IsMenu { get; }

    /// <summary>False for the menu row: a shortcut of <c>none</c> turns the menu off.</summary>
    public bool ShowToggle => !IsMenu;

    /// <summary>The name in the current language.</summary>
    [ObservableProperty]
    private string _name = "";

    /// <summary>What the skill does, in one line.</summary>
    [ObservableProperty]
    private string _description = "";

    /// <summary>The shortcut text (<c>none</c> when off).</summary>
    [ObservableProperty]
    private string _shortcut = "none";

    /// <summary>Whether the skill runs. The menu row does not use this.</summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>A conflict that mentions this row, or empty.</summary>
    [ObservableProperty]
    private string _conflict = "";

    /// <summary>True when the shortcut is not the one the row started with. The reset icon shows only then.</summary>
    public bool ShortcutChanged => !string.Equals(Shortcut.Trim(), DefaultShortcut(), StringComparison.Ordinal);

    /// <summary>Accessible name of the reset button.</summary>
    public string ResetName => _services.Loc.Format("setup.skills.reset_named", Name);

    /// <summary>Accessible name of the shortcut chip.</summary>
    public string ShortcutName => _services.Loc.Format("setup.skills.shortcut_named", Name);

    /// <summary>Reloads the translated name and description.</summary>
    public void RefreshText()
    {
        Name = IsMenu ? _services.Loc["setup.skills.menu"] : SkillNames.Display(_skill!, _services.Loc);
        Description = _services.Loc[_descriptionKey];
        OnPropertyChanged(nameof(ResetName));
        OnPropertyChanged(nameof(ShortcutName));
    }

    /// <summary>Shows the conflict lines that name this row.</summary>
    public void ApplyConflicts(IReadOnlyList<HotkeyConflict> conflicts, string menuName)
    {
        var raw = IsMenu ? menuName : _skill!.Name;
        var lines = new List<string>();
        foreach (var conflict in conflicts)
        {
            if (conflict.Left != raw && conflict.Right != raw)
                continue;
            lines.Add(_services.Loc.Format("skills.conflict", Display(conflict.Left, menuName), Display(conflict.Right, menuName), conflict.Hotkey));
        }
        Conflict = string.Join("\n", lines);
    }

    /// <summary>Puts the row's default shortcut back.</summary>
    [RelayCommand]
    private void ResetShortcut() => Shortcut = DefaultShortcut();

    partial void OnEnabledChanged(bool value)
    {
        if (_loading || IsMenu || _skill!.Enabled == value)
            return;
        _skill.Enabled = value;
        _wizard.CommitSkills();
    }

    partial void OnShortcutChanged(string value)
    {
        OnPropertyChanged(nameof(ShortcutChanged));
        if (_loading)
            return;
        var next = string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
        if (IsMenu)
        {
            if (_services.Store.Skills.SkillMenuHotkey == next)
            {
                _wizard.RefreshConflicts();
                return;
            }
            _services.Store.Skills.SkillMenuHotkey = next;
        }
        else if (_skill!.Hotkey == next)
        {
            _wizard.RefreshConflicts();
            return;
        }
        else
        {
            _skill.Hotkey = next;
            _skill.Normalize();
        }
        _wizard.CommitSkills();
    }

    private string DefaultShortcut()
    {
        if (IsMenu)
            return HotkeyDefaults.Menu;
        foreach (var skill in SkillCatalog.DefaultSkills())
            if (skill.Id == _skill!.Id)
                return skill.Hotkey;
        return "none";
    }

    private string Display(string raw, string menuName)
    {
        if (raw == menuName)
            return menuName;
        var skill = _services.Store.Skills.Skills.FirstOrDefault(item => item.Name == raw);
        return skill is null ? raw : SkillNames.Display(skill, _services.Loc);
    }

    private static string DescriptionKey(string id) => id switch
    {
        SkillIds.Fix => "setup.skill.fix.desc",
        SkillIds.FixAll => "setup.skill.fix-all.desc",
        SkillIds.Translate => "setup.skill.translate.desc",
        SkillIds.Rewrite => "setup.skill.rewrite.desc",
        SkillIds.RewriteLanguage => "setup.skill.rewrite-language.desc",
        SkillIds.ImprovePrompt => "setup.skill.improve-prompt.desc",
        SkillIds.LayoutAnyFa => "setup.skill.layout-any-fa.desc",
        SkillIds.LayoutSwap => "setup.skill.layout-swap.desc",
        _ => "setup.skill.custom.desc",
    };
}

/// <summary>
/// The setup window's state: which step is showing, whether Continue is allowed, and writing through to the same
/// settings and skills the Settings window uses. Closing, Skip, and Finish all mark setup complete.
/// </summary>
public sealed partial class SetupWizardViewModel : ObservableObject, IDisposable
{
    /// <summary>Environment variable set to <c>1</c> by smoke tests so the window does not open on start.</summary>
    public const string SkipVariable = "TINYFIX_SKIP_SETUP";

    /// <summary>How long the wizard waits for a connection test. The stored timeout is left as the user set it.</summary>
    public const double ProbeTimeoutSeconds = 20;

    /// <summary>The steps in order.</summary>
    public static IReadOnlyList<SetupStep> Order { get; } = [SetupStep.Welcome, SetupStep.Model, SetupStep.Skills, SetupStep.Done];

    private readonly AppServices _services;
    private readonly bool _loading;
    private bool _dismissed;
    private bool _disposed;
    private CancellationTokenSource? _probe;

    /// <summary>Creates the wizard from the current settings and skills. <paramref name="start"/> is for screenshots.</summary>
    public SetupWizardViewModel(AppServices services, SetupStep start = SetupStep.Welcome)
    {
        _services = services;
        _loading = true;
        Endpoint = new EndpointEditorViewModel(services, LlmSecrets.MainSlot, "models.main", "models.fallback_enable", allowSkip: false)
        {
            MirrorToProvider = true,
        };
        Endpoint.Load(services.Store.Settings.Llm.Main, use: true);
        Endpoint.PropertyChanged += OnEndpointPropertyChanged;

        Languages.Add(new LanguageOption("auto", services.Loc["settings.language_auto"]));
        foreach (var (code, name) in AppLanguages.All)
            Languages.Add(new LanguageOption(code, name));
        _selectedLanguage = Languages.FirstOrDefault(option => option.Code == services.Store.Settings.Language) ?? Languages[0];
        _autostart = services.Platform.Autostart.IsEnabled;
        _keepPrevious = services.Store.Settings.KeepPreviousOnClipboard;
        _changeToast = services.Store.Settings.ShowChangeToast;

        foreach (var protocol in LlmProtocols.All)
            Providers.Add(new SetupProviderCard(protocol, TitleKey(protocol), DescriptionKey(protocol), SelectProvider));

        for (var step = 0; step < Order.Count; step++)
            Indicators.Add(new SetupStepIndicator(step, StepKey(Order[step])));

        SkillRows.Add(SetupSkillRowViewModel.ForMenu(services, this));
        foreach (var skill in services.Store.Skills.Skills)
            SkillRows.Add(SetupSkillRowViewModel.ForSkill(services, this, skill));

        _step = start;
        _loading = false;
        RefreshCards();
        RefreshConflicts();
        RefreshSteps();
        services.Loc.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Raised when Skip, Finish, or Escape (with no unsaved key) asks the window to close.</summary>
    public event EventHandler? RequestClose;

    /// <summary>The main endpoint. The key box is the shared editor; a saved key is never copied into <see cref="EndpointEditorViewModel.ApiKey"/>.</summary>
    public EndpointEditorViewModel Endpoint { get; }

    /// <summary>Provider chips, in the same order as <see cref="LlmProtocols.All"/>.</summary>
    public ObservableCollection<SetupProviderCard> Providers { get; } = [];

    /// <summary>The four step labels.</summary>
    public ObservableCollection<SetupStepIndicator> Indicators { get; } = [];

    /// <summary>"Automatic" and every shipped language.</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = [];

    /// <summary>The donut-menu shortcut, then every skill.</summary>
    public ObservableCollection<SetupSkillRowViewModel> SkillRows { get; } = [];

    /// <summary>Which way the last step change moved: 1 forward, -1 back. The window uses it for the slide.</summary>
    public int StepDelta { get; private set; } = 1;

    /// <summary>The step on screen.</summary>
    [ObservableProperty]
    private SetupStep _step = SetupStep.Welcome;

    /// <summary>The chosen language. The same objects stay in <see cref="Languages"/> when the language changes.</summary>
    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    /// <summary>Continue without a usable model. Skills still need one before they can run.</summary>
    [ObservableProperty]
    private bool _setupLater;

    /// <summary>Start with the system, read from the autostart port.</summary>
    [ObservableProperty]
    private bool _autostart;

    /// <summary>Leave the text a skill replaced on the clipboard. On unless the user turns it off.</summary>
    [ObservableProperty]
    private bool _keepPrevious;

    /// <summary>Show the "changed" toast with Undo.</summary>
    [ObservableProperty]
    private bool _changeToast;

    partial void OnKeepPreviousChanged(bool value)
    {
        if (_loading)
            return;
        _services.Store.Settings.KeepPreviousOnClipboard = value;
        _services.Store.SaveSettings();
    }

    partial void OnChangeToastChanged(bool value)
    {
        if (_loading)
            return;
        _services.Store.Settings.ShowChangeToast = value;
        _services.Store.SaveSettings();
    }

    /// <summary>True while the connection test is waiting.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _testing;

    /// <summary>The plain result of the test: success with the time, or a short reason.</summary>
    [ObservableProperty]
    private string _testHeadline = "";

    /// <summary>The sanitized provider error, under the plain reason. Empty after a success.</summary>
    [ObservableProperty]
    private string _testDetail = "";

    /// <summary>True after a test that got an answer.</summary>
    [ObservableProperty]
    private bool _testSucceeded;

    /// <summary>The window title.</summary>
    public string WindowTitle => _services.Loc["setup.window_title"];

    /// <summary>The large title of the current step.</summary>
    public string Headline => _services.Loc[Step switch
    {
        SetupStep.Model => "setup.model.title",
        SetupStep.Skills => "setup.skills.title",
        SetupStep.Done => "setup.done.title",
        _ => "setup.welcome.title",
    }];

    /// <summary>The one-line subtitle of the current step.</summary>
    public string Subtitle => _services.Loc[Step switch
    {
        SetupStep.Model => "setup.model.subtitle",
        SetupStep.Skills => "setup.skills.subtitle",
        SetupStep.Done => "setup.done.subtitle",
        _ => "setup.welcome.subtitle",
    }];

    /// <summary>Continue, or Finish on the last step.</summary>
    public string ContinueLabel => _services.Loc[Step == SetupStep.Done ? "setup.finish" : "setup.continue"];

    /// <summary>True after a connection test succeeded on the model step, so Enter activates Continue.</summary>
    public bool ContinueIsDefault => IsModel && TestSucceeded;

    /// <summary>True on the welcome step.</summary>
    public bool IsWelcome => Step == SetupStep.Welcome;

    /// <summary>True on the model step.</summary>
    public bool IsModel => Step == SetupStep.Model;

    /// <summary>True on the skills step.</summary>
    public bool IsSkills => Step == SetupStep.Skills;

    /// <summary>True on the last step.</summary>
    public bool IsDone => Step == SetupStep.Done;

    /// <summary>True when Back has a previous step.</summary>
    public bool CanGoBack => Step != SetupStep.Welcome;

    /// <summary>True when Continue or Finish may run.</summary>
    public bool CanContinue => Step switch
    {
        SetupStep.Model => SetupLater || ModelUsable,
        _ => true,
    };

    /// <summary>True when this endpoint can answer: a model id, a URL when the preset has none, and a key when it needs one.</summary>
    public bool ModelUsable
    {
        get
        {
            var settings = Endpoint.ToSettings();
            var protocol = LlmProtocols.Normalize(settings.Protocol);
            var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? LlmProtocols.DefaultBaseUrl(protocol) : settings.BaseUrl.Trim();
            var needsKey = LlmProtocols.RequiresKey(protocol, baseUrl);
            var hasKey = Endpoint.HasSavedKey || !string.IsNullOrWhiteSpace(Endpoint.ApiKey);
            return new LlmEndpoint
            {
                Protocol = protocol,
                Model = settings.Model,
                BaseUrl = settings.BaseUrl,
                ApiKey = !needsKey || hasKey ? "set" : "",
            }.IsUsable();
        }
    }

    /// <summary>True when the user has typed a key that is not stored yet. Escape keeps the window in that case.</summary>
    public bool HasUnsavedTypedKey => Endpoint.ShowInput && !string.IsNullOrWhiteSpace(Endpoint.ApiKey);

    /// <summary>Why Continue is disabled, or empty when it is enabled or this is not the model step.</summary>
    public string Validation
    {
        get
        {
            if (Step != SetupStep.Model || SetupLater || ModelUsable)
                return "";
            var settings = Endpoint.ToSettings();
            if (string.IsNullOrWhiteSpace(settings.Model))
                return _services.Loc["setup.model.need_model"];
            var protocol = LlmProtocols.Normalize(settings.Protocol);
            var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? LlmProtocols.DefaultBaseUrl(protocol) : settings.BaseUrl.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                return _services.Loc["setup.model.need_url"];
            if (LlmProtocols.RequiresKey(protocol, baseUrl))
                return _services.Loc["setup.model.need_key"];
            return _services.Loc["setup.model.need_model"];
        }
    }

    /// <summary>True when the base URL box is on screen (a local preset or Custom).</summary>
    public bool ShowBaseUrl => Endpoint.Protocol is LlmProtocols.Ollama or LlmProtocols.LmStudio or LlmProtocols.Custom;

    /// <summary>True when the key field is on screen. Local presets need no key.</summary>
    public bool ShowKey => !LlmProtocols.IsLocalPreset(Endpoint.Protocol);

    /// <summary>True when the model step should focus the key box: this provider needs a key and none is saved.</summary>
    public bool FocusApiKey
    {
        get
        {
            if (!ShowKey || !Endpoint.ShowInput || Endpoint.HasSavedKey)
                return false;
            var settings = Endpoint.ToSettings();
            var protocol = LlmProtocols.Normalize(settings.Protocol);
            var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? LlmProtocols.DefaultBaseUrl(protocol) : settings.BaseUrl.Trim();
            return LlmProtocols.RequiresKey(protocol, baseUrl);
        }
    }

    /// <summary>The one-line description of the selected provider.</summary>
    public string ProviderDescription
    {
        get
        {
            foreach (var card in Providers)
                if (card.IsSelected)
                    return card.Description;
            return "";
        }
    }

    /// <summary>The provider's key page, or null when there is no known page.</summary>
    public string? KeyPageUrl => KeyPage(Endpoint.Protocol);

    /// <summary>True when <see cref="KeyPageUrl"/> is set.</summary>
    public bool ShowGetKey => KeyPageUrl is not null;

    /// <summary>The model line on the last step.</summary>
    public string ModelSummary
    {
        get
        {
            if (!ModelUsable)
                return _services.Loc["setup.done.model_later"];
            var name = _services.Loc[TitleKey(Endpoint.Protocol)];
            var model = Endpoint.Model.Trim();
            return model.Length == 0 ? name : name + " · " + model;
        }
    }

    /// <summary>The enabled skills, in menu order.</summary>
    public string SkillsSummary
    {
        get
        {
            var names = _services.Store.Skills.Skills.Where(skill => skill.Enabled).Select(skill => SkillNames.Display(skill, _services.Loc)).ToList();
            return names.Count == 0 ? _services.Loc["setup.done.skills_none"] : string.Join(", ", names);
        }
    }

    /// <summary>The donut shortcut as the user would read it.</summary>
    public string DonutSummary
    {
        get
        {
            var key = HotkeyParser.ParseOrDisabled(_services.Store.Skills.SkillMenuHotkey);
            return key.IsDisabled ? _services.Loc["shortcut.none"] : key.Display;
        }
    }

    /// <summary>True when the test badge should show.</summary>
    public bool HasTestResult => TestSucceeded || TestFailed;

    /// <summary>True when the plain reason is a failure.</summary>
    public bool TestFailed => !Testing && !TestSucceeded && TestHeadline.Length > 0;

    /// <summary>True when the sanitized detail should show.</summary>
    public bool HasTestDetail => TestDetail.Length > 0;

    /// <summary>
    /// True when setup should open on start. <paramref name="skipVariable"/> is <see cref="SkipVariable"/>;
    /// <c>1</c> (smoke tests) suppresses the window.
    /// </summary>
    public static bool ShouldOffer(AppSettings settings, string? skipVariable) =>
        !settings.SetupCompleted && skipVariable != "1";

    /// <summary>The https page where this provider issues keys, or null for local and custom presets.</summary>
    public static string? KeyPage(string protocol) => protocol switch
    {
        LlmProtocols.Google => "https://aistudio.google.com/apikey",
        LlmProtocols.OpenRouter => "https://openrouter.ai/keys",
        LlmProtocols.OpenAi => "https://platform.openai.com/api-keys",
        LlmProtocols.Anthropic => "https://console.anthropic.com/settings/keys",
        _ => null,
    };

    /// <summary>A short reason for a failed test. <paramref name="error"/>'s message stays as the detail and is already sanitized.</summary>
    public static string PlainReason(ILocalizer loc, LlmException error) => error.Kind switch
    {
        LlmErrorKind.Timeout => loc["setup.test_timeout"],
        LlmErrorKind.Network => loc["setup.test_network"],
        LlmErrorKind.Http when error.StatusCode is 401 or 403 => loc["setup.test_wrong_key"],
        _ => loc["setup.test_other"],
    };

    /// <summary>Selects a provider chip and fills its default model. Choosing one leaves "set up later".</summary>
    public void SelectProvider(string protocol)
    {
        var index = 0;
        for (var i = 0; i < LlmProtocols.All.Count; i++)
            if (LlmProtocols.All[i] == protocol)
                index = i;
        if (Endpoint.ProtocolIndex != index)
            Endpoint.ProtocolIndex = index;
        if (SetupLater)
            SetupLater = false;
        RefreshCards();
    }

    /// <summary>Saves skill edits and re-registers shortcuts. Rows call this; tests call it only through the rows.</summary>
    public void CommitSkills()
    {
        if (_loading)
            return;
        _services.Store.SaveSkills();
        AppHost.ApplyHotkeys();
        RefreshConflicts();
        NotifySummary();
        SyncSkillPages();
    }

    /// <summary>Rebuilds the inline conflict lines.</summary>
    public void RefreshConflicts()
    {
        var menu = _services.Loc["tray.skill_menu"];
        var conflicts = SkillCatalog.FindConflicts(_services.Store.Skills.Skills, _services.Store.Skills.SkillMenuHotkey, menu);
        foreach (var row in SkillRows)
            row.ApplyConflicts(conflicts, menu);
    }

    /// <summary>
    /// Marks setup complete and writes the model fields. A key typed into the empty box is stored; a half-finished
    /// replacement is not. Safe to call again when the window then closes.
    /// </summary>
    public void Dismiss()
    {
        if (_dismissed)
            return;
        _dismissed = true;
        CancelProbe();
        Endpoint.RememberNewKey();
        _services.Store.Settings.Llm.Main = Endpoint.ToSettings();
        _services.Store.Settings.SetupCompleted = true;
        _services.Store.SaveSettings();
        SyncModelPage();
    }

    /// <summary>Escape: with an unsaved key, the window stays. Otherwise this is Skip.</summary>
    public bool TryDismissFromEscape()
    {
        if (HasUnsavedTypedKey)
            return false;
        FinishSetup();
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _probe?.Cancel();
        _probe?.Dispose();
        _services.Loc.LanguageChanged -= OnLanguageChanged;
        Endpoint.PropertyChanged -= OnEndpointPropertyChanged;
        Endpoint.Dispose();
    }

    /// <summary>Goes to the next step, or closes on the last one.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveNext))]
    private void Continue()
    {
        if (!CanContinue)
            return;
        if (Step == SetupStep.Done)
        {
            FinishSetup();
            return;
        }
        Move(1);
    }

    /// <summary>Goes back one step.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveBack))]
    private void Back() => Move(-1);

    /// <summary>Closes the window and does not ask again. Fields already chosen are kept.</summary>
    [RelayCommand]
    private void Skip() => FinishSetup();

    /// <summary>Turns "set up later" on or off. Choosing a provider turns it off.</summary>
    [RelayCommand]
    private void ToggleLater() => SetupLater = !SetupLater;

    /// <summary>Opens the provider's key page.</summary>
    [RelayCommand]
    private void OpenKeyPage()
    {
        if (KeyPageUrl is { } url)
            UrlOpener.Open(url);
    }

    /// <summary>Sends "Reply with OK" and reports the time, or a plain reason plus the sanitized error.</summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        _probe?.Cancel();
        _probe?.Dispose();
        _probe = new CancellationTokenSource();
        var token = _probe.Token;
        Testing = true;
        TestSucceeded = false;
        TestHeadline = _services.Loc["setup.model.testing"];
        TestDetail = "";
        var watch = Stopwatch.StartNew();
        try
        {
            var settings = Endpoint.ToSettings();
            var endpoint = new LlmEndpoint
            {
                Protocol = settings.Protocol,
                Model = settings.Model,
                BaseUrl = settings.BaseUrl,
                TimeoutSeconds = ProbeTimeoutSeconds,
            };
            var typed = Endpoint.ShowInput ? Endpoint.ApiKey : "";
            await EndpointProbe.RunAsync(_services, endpoint, Endpoint.Slot, typed, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed)
                return;
            watch.Stop();
            TestSucceeded = true;
            TestHeadline = _services.Loc.Format("setup.test_ok", Math.Max(0, (int)watch.ElapsedMilliseconds));
            TestDetail = "";
        }
        catch (OperationCanceledException)
        {
            if (!_disposed)
            {
                TestSucceeded = false;
                TestHeadline = "";
                TestDetail = "";
            }
        }
        catch (LlmException ex)
        {
            if (_disposed)
                return;
            TestSucceeded = false;
            TestHeadline = PlainReason(_services.Loc, ex);
            TestDetail = ex.Message;
        }
        finally
        {
            if (!_disposed)
                Testing = false;
        }
    }

    /// <summary>Stops a test that is still waiting.</summary>
    [RelayCommand]
    private void CancelTest() => CancelProbe();

    /// <summary>Opens the real donut preview. A pick closes it and does not run a skill.</summary>
    [RelayCommand]
    private Task TryDonutAsync() => _services.Donut.PreviewAsync(_services.Platform.Caret.GetCursor());

    private bool CanMoveNext() => CanContinue;

    private bool CanMoveBack() => CanGoBack;

    private bool CanTest() => !Testing;

    partial void OnStepChanged(SetupStep value)
    {
        RefreshSteps();
        NotifyStepChrome();
    }

    partial void OnSetupLaterChanged(bool value) => NotifyCanContinue();

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_loading || value is null)
            return;
        _services.Store.Settings.Language = value.Code;
        _services.Store.SaveSettings();
        _services.Loc.SetLanguage(value.Code);
        _services.Theme.Apply(_services.Loc.Language);
        _services.Tray.Refresh();
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_loading)
            return;
        if (value)
            _services.Platform.Autostart.Enable(Environment.ProcessPath ?? "");
        else
            _services.Platform.Autostart.Disable();
    }

    partial void OnTestingChanged(bool value)
    {
        OnPropertyChanged(nameof(TestFailed));
        OnPropertyChanged(nameof(HasTestResult));
    }

    partial void OnTestHeadlineChanged(string value)
    {
        OnPropertyChanged(nameof(TestFailed));
        OnPropertyChanged(nameof(HasTestResult));
    }

    partial void OnTestDetailChanged(string value) => OnPropertyChanged(nameof(HasTestDetail));

    partial void OnTestSucceededChanged(bool value)
    {
        OnPropertyChanged(nameof(TestFailed));
        OnPropertyChanged(nameof(HasTestResult));
        OnPropertyChanged(nameof(ContinueIsDefault));
    }

    private void Move(int delta)
    {
        var next = (int)Step + delta;
        if (next < 0 || next >= Order.Count)
            return;
        if (delta > 0 && !CanContinue)
            return;
        if (Step == SetupStep.Model || (SetupStep)next == SetupStep.Model)
            PersistModel();
        StepDelta = delta;
        Step = (SetupStep)next;
    }

    private void FinishSetup()
    {
        Dismiss();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void PersistModel()
    {
        if (_loading)
            return;
        Endpoint.RememberNewKey();
        _services.Store.Settings.Llm.Main = Endpoint.ToSettings();
        _services.Store.SaveSettings();
        SyncModelPage();
    }

    private void CancelProbe() => _probe?.Cancel();

    private void OnEndpointPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading)
            return;
        if (e.PropertyName is nameof(EndpointEditorViewModel.ProtocolIndex)
            or nameof(EndpointEditorViewModel.Model)
            or nameof(EndpointEditorViewModel.BaseUrl)
            or nameof(EndpointEditorViewModel.ApiKey))
            ClearTestResult();
        if (e.PropertyName is nameof(EndpointEditorViewModel.ProtocolIndex)
            or nameof(EndpointEditorViewModel.Model)
            or nameof(EndpointEditorViewModel.BaseUrl)
            or nameof(EndpointEditorViewModel.ApiKey)
            or nameof(EndpointEditorViewModel.HasSavedKey)
            or nameof(EndpointEditorViewModel.State))
            RefreshCards();
    }

    private void ClearTestResult()
    {
        if (Testing || _loading)
            return;
        if (!TestSucceeded && TestHeadline.Length == 0 && TestDetail.Length == 0)
            return;
        TestSucceeded = false;
        TestHeadline = "";
        TestDetail = "";
    }

    private void RefreshCards()
    {
        foreach (var card in Providers)
            card.Refresh(_services.Loc, card.Id == Endpoint.Protocol);
        OnPropertyChanged(nameof(ShowBaseUrl));
        OnPropertyChanged(nameof(ShowKey));
        OnPropertyChanged(nameof(FocusApiKey));
        OnPropertyChanged(nameof(ProviderDescription));
        OnPropertyChanged(nameof(ShowGetKey));
        OnPropertyChanged(nameof(KeyPageUrl));
        NotifyCanContinue();
        NotifySummary();
    }

    private void RefreshSteps()
    {
        foreach (var indicator in Indicators)
            indicator.Refresh(_services.Loc, Step);
    }

    private void NotifyStepChrome()
    {
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(ContinueIsDefault));
        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsModel));
        OnPropertyChanged(nameof(IsSkills));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(CanGoBack));
        NotifyCanContinue();
        NotifySummary();
        BackCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCanContinue()
    {
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ModelUsable));
        OnPropertyChanged(nameof(Validation));
        OnPropertyChanged(nameof(HasUnsavedTypedKey));
        ContinueCommand.NotifyCanExecuteChanged();
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(ModelSummary));
        OnPropertyChanged(nameof(SkillsSummary));
        OnPropertyChanged(nameof(DonutSummary));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Languages.Count > 0)
            Languages[0].Name = _services.Loc["settings.language_auto"];
        OnPropertyChanged(nameof(WindowTitle));
        RefreshCards();
        RefreshSteps();
        NotifyStepChrome();
        foreach (var row in SkillRows)
            row.RefreshText();
        RefreshConflicts();
    }

    private void SyncSkillPages()
    {
        if (_services.Windows.Settings?.DataContext is not SettingsViewModel settings)
            return;
        foreach (var page in settings.Pages)
        {
            if (page is SkillsPageViewModel skills)
                skills.RefreshAfterExternalEdit();
            else if (page is DonutPageViewModel donut)
                donut.RefreshMenuShortcut();
        }
    }

    private void SyncModelPage()
    {
        if (_services.Windows.Settings?.DataContext is not SettingsViewModel settings)
            return;
        foreach (var page in settings.Pages)
            if (page is OnlinePageViewModel online)
                online.ReloadFromStore();
    }

    private static string StepKey(SetupStep step) => step switch
    {
        SetupStep.Model => "setup.step.model",
        SetupStep.Skills => "setup.step.skills",
        SetupStep.Done => "setup.step.done",
        _ => "setup.step.welcome",
    };

    private static string TitleKey(string protocol) => protocol switch
    {
        LlmProtocols.Google => "models.provider_google",
        LlmProtocols.OpenRouter => "models.provider_openrouter",
        LlmProtocols.OpenAi => "models.provider_openai",
        LlmProtocols.Anthropic => "models.provider_anthropic",
        LlmProtocols.Ollama => "models.provider_ollama",
        LlmProtocols.LmStudio => "models.provider_lmstudio",
        _ => "models.provider_custom",
    };

    private static string DescriptionKey(string protocol) => protocol switch
    {
        LlmProtocols.Google => "setup.provider.google",
        LlmProtocols.OpenRouter => "setup.provider.openrouter",
        LlmProtocols.OpenAi => "setup.provider.openai",
        LlmProtocols.Anthropic => "setup.provider.anthropic",
        LlmProtocols.Ollama => "setup.provider.ollama",
        LlmProtocols.LmStudio => "setup.provider.lmstudio",
        _ => "setup.provider.custom",
    };
}
