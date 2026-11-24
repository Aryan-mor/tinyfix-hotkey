# TinyFix (.NET) architecture

This document describes the C# / Avalonia rewrite of TinyFix Hotkey that lives in `src/` and `tests/` (solution
`TinyFix.slnx`). The original Python app in the repository root is unchanged and keeps its own CI and installer.
The project layout, the localization mechanism and several patterns are adapted from Filee (MIT, KnifeLemon); see
`THIRD-PARTY-NOTICES.md`.

## What it does

A tray app that fixes and rewrites the text you are typing in any other app. Select text (or let TinyFix select the whole
field), press a shortcut, and the result replaces the selection.

* Skills: **Fix**, **Fix all**, **Translate**, **Improve prompt** (and the optional **Rewrite** and **Rewrite to target
  language**), plus any number of custom skills (name, enabled, input mode, shortcut, prompt, model choice).
* Every skill has a global shortcut. The **skill selector** (default `Ctrl+Alt+M`) is a donut (radial) menu that opens at the
  caret or, when Settings says so, the mouse pointer: press `1`-`9`, click a slice, `Tab` or the wheel turns the page when there are more than nine
  skills, `Esc` or a click outside cancels.
* **Every AI skill goes through an AI model** (Google Gemini, OpenRouter, OpenAI, Anthropic, Ollama, LM Studio, or a custom
  server, with a fallback model), in any language. With no usable model such a skill shows the localized notice
  "Set up an AI model in Settings to use this skill."
* **Layout skills need no model and no network.** A layout conversion (`Skill.Kind` `layout` or `swap`) re-types text typed on the
  wrong keyboard layout: each character goes back to its physical key and forward to the character that key gives on the other
  layout (`TinyFix.Core.Layouts`). The layouts are read from the OS (`IKeyboardLayouts`; Windows asks `ToUnicodeEx` per key, so no
  table is typed by hand; macOS and Linux have no adapter yet and say so). A rule is "from a language or any, to a language"; the
  swap rule turns text of one of two languages into the other, in both directions. They use the same input modes, donut and shortcuts
  as AI skills, and are off by default.
* Settings: General, Donut, Skills, AI models, Cost, Updates, About.

## Projects

```
src/
  TinyFix.Core                 no UI, no OS code
  TinyFix.Platform.Windows     user32 clipboard / focus / caret, DPAPI, Run key
  TinyFix.Platform.Linux       xclip / wl-clipboard, xdotool, secret-tool, XDG autostart
  TinyFix.Platform.MacOS       pbcopy / pbpaste, osascript, CoreGraphics, `security`, LaunchAgent
  TinyFix.App                  Avalonia: tray, donut, Settings, toasts, SharpHook hook
tests/
  TinyFix.Core.Tests           ports of the Python tests + new ones
  TinyFix.Platform.Tests       adapters (Windows tests skip on other OSes)
  TinyFix.App.Tests            headless rendering, view models, localization
```

`TinyFix.Core` defines the **ports** (interfaces in `Platform/PlatformInterfaces.cs`) and everything that is pure logic.
The three platform projects implement the ports; the app picks the set of the running OS in `AppHost`. There is no
dependency-injection container: `AppHost.Build` creates the object graph with plain constructors (small graph, fast
start-up, nothing created by reflection).

### Core

The Python app these types replace was removed from the tree; the files named in the last column are in the git history
(branch `main`, release v1.5.14).

| Area | Types | Python original |
| --- | --- | --- |
| Hotkeys | `Hotkey`, `HotkeyParser`, `HotkeyDefaults` | `hotkey_util.py` |
| Skills | `Skill`, `SkillStore`, `SkillCatalog`, `SkillStoreFile`, `SkillRunner` | `skills.py`, `app.py` (`run_skill`) |
| Prompts | `DefaultPrompts` (generated verbatim), `PromptRenderer` | `prompts.py` |
| LLM | `LlmClient`, `LlmConfig`, `LlmConfigResolver` | `llm_client.py`, `language_guess.py` |
| Fix seam | `IFixEngine`, `FixOutcome`, `OnlineFixEngine` | `llm_client.py` |
| Selection | `SelectionService` | `app.py` (`copy_selection`) |
| Settings | `UserDataStore`, `AppSettings`, `DataDirectory`, `AtomicFile` | `config.ini`, `skills.json` |
| Donut | `DonutGeometry`, `DonutPaging`, `DonutPlacement` | new |
| Updates | `UpdateChecker` | `update_check.py` (check only) |

The fix seam is deliberately tiny (`OnlineFixEngine` renders a skill's prompt and calls `LlmClient`):

```csharp
public interface IFixEngine { Task<FixOutcome> FixAsync(string text, CancellationToken ct = default); }
public readonly record struct FixOutcome(string Text, char ModelLetter = '\0');   // M main, F fallback, L local server
```

`skills.json` stays compatible with the Python app: the `offline_fallback` flag is read and written but has no effect.

Hotkeys keep **Windows virtual-key codes and modifier bits as plain integers**, so the parser and the conflict rules are
identical on every OS and the ported tests assert the same numbers (`0x20`, `0x72`). The platform layer maps them to
what the OS hook speaks (`KeyMap` to SharpHook key codes; "Win" is Cmd on macOS).

### The path of one skill run

```
global hook (SharpHook thread)          SharpHookHotkeyService  -- match modifiers + key, swallow it, hand off
  -> SkillRunner.RunAsync                one job at a time (a SemaphoreSlim), paused / AI-off checks
     -> SelectionService.CaptureAsync    snapshot clipboard, wait for modifiers up, (Ctrl+A), Ctrl+C, wait for change
     -> OnlineFixEngine / LlmClient      main model, fallback model on timeout / 429 / 5xx; text never logged
     -> restore the window, SelectionService.PasteAsync   set clipboard, Ctrl+V, restore the user's clipboard
  -> IStatusSink                         toast + animated tray icon (M main / F fallback / L local model)
```

A failure at any step restores the user's clipboard and leaves the text unchanged.

### The donut

`TinyFix.Core.Donut` holds the testable maths: slice angles (clockwise from 12 o'clock, slice `i` centred on `i * span`),
hit testing, wedge outlines, paging (up to 9 skills on one ring, otherwise 8 per page), number-key mapping and window
placement (`DonutPlacement.CenterClamped` keeps the window inside the monitor's work area; `PickWorkArea` handles several
monitors). `DonutMenu` (Filee's control, adapted so a slice is a skill) only draws; `DonutViewModel` has no UI types;
`RadialController` runs the sequence that matters:

1. remember the focused window (`IForegroundWindow.Save`),
2. wait until the shortcut's modifier keys are up (so they cannot dismiss the donut),
3. show the transparent topmost window where Settings says (the caret, else the pointer, or always the pointer) and take the focus,
4. when it closes (choice, `Esc`, click elsewhere = deactivation, or a 25 s safety timeout), **restore the remembered window**,
5. only then does the skill runner send keys.

The window is hidden, not closed, so the second opening is instant. It never keeps the focus.

### Localization

UI text lives only in `src/TinyFix.App/Assets/i18n/<code>.json` (flat key/value). XAML uses `{DynamicResource key}`, code uses
`ILocalizer["key"]` / `ILocalizer.Format`. Missing keys fall back to English at run time. The tests in `LocalizationTests` check key parity, `{0}` placeholder parity, that every key used in code or XAML exists and that
no key is unused. Prompt templates are **not** UI text and are never translated. See `DOTNET-ADDING-A-LANGUAGE.md`.

Shipped now: English, German, Persian (right to left: windows mirror through `FlowDirection`) and Japanese. **The German,
Persian and Japanese texts are machine-generated and welcome a review.** Fonts are system font lists per language
(`ThemeService.FontFamilies`); no font files are bundled or downloaded.

### Storage

`%APPDATA%\TinyFix` (Windows), `~/Library/Application Support/TinyFix` (macOS), `~/.config/tinyfix` (Linux), or the folder in
`TINYFIX_DATA_DIR`. Files are written atomically (temp file + rename, `AtomicFile`).

* `settings.json`: language, theme, donut size, opacity, where it opens and whether slices show shortcuts, model settings (never keys). An old `llm_enabled` value is ignored.
* `skills.json`: the **same schema as the Python app** (`version`, `skill_menu_hotkey`, `dismissed`, `skills[]` with
  `id, name, input_mode, prompt, hotkey, enabled, builtin, offline_fallback, model_mode, model_provider, model,
  model_fallback_provider, model_fallback`), parsed by hand so one wrong field does not lose the rest.
* Secrets: Windows DPAPI file `secrets.dpapi` in the Python app's format (`{"keys": {...}}`), macOS Keychain, Linux
  `secret-tool` with an AES-GCM encrypted file fallback that shows a warning.
* **Import, once:** on the first start the app looks for the Python app's `skills.json`, `secrets.dpapi` and `config.ini`
  (model settings) in `%LOCALAPPDATA%\Programs\TinyFix Hotkey` (or `TINYFIX_LEGACY_DIR`) and copies what the new data folder
  lacks, then records `legacy_imported`. With `TINYFIX_DATA_DIR` set the real installation is never read.

### Single instance

A named mutex plus a named pipe whose name includes a hash of the user and of `TINYFIX_DATA_DIR`. Starting TinyFix again
sends the launch to the running one, which opens Settings, or focuses the setup window when that is already open.
(Adapted from Filee's `SingleInstance`.)

### First-run setup

`setup_completed` in `settings.json` starts false. A file that never had the field is read the same way, so an existing
install sees the window once. After the tray icon is up and the background work starts, TinyFix opens `SetupWindow`
when the flag is false. Skip, the window's close button, and Finish all set it, and the window does not open on the
next start. `TINYFIX_SKIP_SETUP=1` (the smoke tests) suppresses that open. Settings, on the About page, and the tray
item Setup... open the same window again and leave the saved model, skills, and keys in place.

`SetupWizardViewModel` is the state machine the window binds to: welcome (the language picker writes the same setting
as General), model (the shared endpoint editor; a saved key is only detected with `Contains`, never read), skills (the
same skill store and shortcut conflicts as Settings), and a short summary. The autostart switch reads `IAutostart`
(the first start has already registered TinyFix). "Try the donut now" calls the real preview, and a pick there does
not run a skill. The connection test waits up to 20 seconds and does not change the timeout stored for skills.

### Security and privacy

* API keys only in the platform secret store; never in `settings.json`, logs or exception messages
  (`LlmClient.SanitizeError`, tested). The log never contains the user's text (only types and counts).
* Plain `http://` base URLs are accepted only for localhost.
* Window tokens passed to `xdotool` / `osascript` are validated (digits / bundle-id characters) before use.
* The app never runs elevated and says so when the focused window does (Windows UAC).
* Autostart is written only when the user turns it on in Settings.

## Platform support

| Capability | Windows | macOS | Linux X11 | Linux Wayland |
| --- | --- | --- | --- | --- |
| Global shortcuts (SharpHook) | hook starts (run); firing against other apps not exercised | untested (needs Accessibility permission) | untested | not supported by the compositor; use "Fix clipboard" |
| Copy / paste simulation | implemented, not exercised against other apps | untested | untested | best effort |
| Clipboard snapshot | all memory formats | text only | text only | text only |
| Caret position | `GetGUIThreadInfo`, pointer fallback | pointer | pointer (`xdotool`) | fixed point |
| Restore previous window | `AttachThreadInput` + `SetForegroundWindow` | `osascript` | `xdotool` | no |
| Secrets | DPAPI | Keychain (`security`) | `secret-tool`, else encrypted file | same |
| Autostart | HKCU Run | LaunchAgent | XDG `.desktop` | same |

**What was run:** the whole test suite on Windows 11, and the app started on Windows (tray and hook came up).
**What was not:** any real shortcut press, copy or paste against another program, anything on macOS or Linux. The Linux and macOS adapters are written for real but have
only been exercised through their pure parts (command-line parsing, file contents, environment detection). CI builds and
tests on all three systems; the tests that need a real desktop skip themselves.

A Wayland session is detected (`LinuxDisplay`); the app then shows a localized notice and points to the tray's
**Fix clipboard** action, which needs no hook.

## Performance notes

Compiled Avalonia bindings are on by default; JSON uses `System.Text.Json` source generation or hand-written readers; the
Settings and donut windows are created lazily; start-up only builds the tray icon and posts the
rest at background priority. `scripts/publish.ps1` publishes self-contained, single-file, ReadyToRun and removes native
`.pdb` files. No timings are claimed here: nothing was measured.

## Open items

* An optional local engine, if ever wanted; installer; in-app update; macOS and Linux packaging and real-desktop testing;
  a Keychain API binding; more languages and a native-speaker review of the translations.
