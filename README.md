# TinyFix Hotkey

![TinyFix Hotkey demo](docs/demo.gif)

Fix English typos in any Windows app: select text, press your shortcut, done.

## Install

1. Download **TinyFix-Setup.exe** from the [latest release](https://github.com/Aryan-mor/tinyfix-hotkey/releases/latest).
2. Run the setup and click **Next** a few times.
3. Pick shortcuts for **Fix selection** (default **Ctrl+Alt+Space**) and **Fix all** (default **Ctrl+F3**). Either shortcut can be set to **None/Disabled**. You can also start TinyFix with Windows.
4. Click **Finish**. TinyFix sits in the system tray and is ready to use.

No administrator account is required. The setup is per-user and includes the Python runtime and the correction model — nothing else is downloaded at install time. Online AI is off until you choose it in Settings.

## Use

- Select English text in Chrome, Notepad, Word, or any other app, then press **Ctrl+Alt+Space** (or the Fix shortcut you chose).
- To correct the whole field, press **Ctrl+F3** (or your Fix all shortcut). TinyFix selects all, copies, corrects, and pastes back.
- Press **Ctrl+Alt+M** (or your Skill menu shortcut) to open a small menu of enabled skills next to the caret or mouse cursor. Press 1–9 to run a skill, or Esc to cancel. TinyFix restores the original window before it sends keys.
- Left-click the tray icon to correct whatever text is currently on the clipboard. The corrected text stays on the clipboard; a short notification tells you how many changes were made, or that nothing needed fixing.
- Right-click the tray icon for **Settings…**, pause, sensitivity, updates, or exit.
- If nothing is selected (or the copy fails), TinyFix tells you. If the text was already fine, it says **No typos found**.
- Non-English text (for example Persian) is left unchanged by the built-in engine.

## Settings

Open **Settings…** from the tray for a single window with Shortcuts, Skills, Sensitivity, Online AI, Prompts, Updates, and About.

### Shortcuts

Each built-in shortcut can be recorded, changed, or set to **None/Disabled**:

| Action | Default | Notes |
| --- | --- | --- |
| Fix | **Ctrl+Alt+Space** | Corrects the highlighted text. Falls back to the built-in offline engine if an online model is off, times out, or fails. |
| Fix all | **Ctrl+F3** | Same as Fix, but selects the whole field first. |
| Translate | **Ctrl+Alt+T** | Only active when an online model is enabled. **T** is for translate; Ctrl+T and Ctrl+Shift+T are already used by browsers, but Ctrl+Alt+T is rarely bound on Windows. |
| Skill menu | **Ctrl+Alt+M** | Pops up a list of enabled skills near the caret or cursor. **M** is for menu; the combo is rarely used on Windows. Can be disabled. |

### Skills

**Settings → Skills** lets you add as many skills as you want. Each skill has a name, an input mode (current selection, or select the whole field first), a prompt template, an optional shortcut, and an on/off switch. You can add, edit, duplicate, delete, and reorder them. Built-in **Fix**, **Fix all**, and **Translate** can be edited or turned off, but not deleted.

Skill prompts use `{{content}}` for the selected text (if you omit it, the text is appended). They may also use `{{language}}`, which comes from the target language on the Prompts tab. A **Test on sample text** button runs the skill on a sample without changing the current window.

Skills that need an online model (everything except Fix and Fix all) are only active when an online model is configured and enabled. The skill menu still lists them and shows a hint if AI is off. Custom skills do not fall back to the offline engine: if the request times out or fails, TinyFix shows a short notification and leaves the text unchanged. While it waits, a tray toast says **Running {skill}…**.

If two skills (or a skill and the skill menu) share the same shortcut, Settings asks you to change one of them.

Examples you can add yourself:

| Skill | What it does |
| --- | --- |
| **Make formal email** | Rewrites the selection as a clear, polite formal email. Prompt idea: `Rewrite the text below as a clear, polite formal email. Keep the meaning and return only the email body.` plus `{{content}}`. |
| **Summarize** | Replaces the selection with a short summary. Prompt idea: `Summarize the text below in a few short sentences. Return only the summary.` plus `{{content}}`. |
| **Translate to German** | Translates the selection into German (or duplicate the built-in Translate skill and set `{{language}}` / the wording to German). |

Two more built-in skills ship with TinyFix but stay **off** until you enable them on **Settings → Skills** and assign a shortcut (like Translate, they need an online model):

| Skill | What it does |
| --- | --- |
| **Rewrite** | Polishes the selection: grammar, clarity, and natural wording, with embedded [No-Slop](https://github.com/soheilmomeniii/no-slop) style rules to avoid common AI phrasing. The prompt is editable; **Reset prompt to default** restores the bundled wording. |
| **Rewrite to target language** | Same polish and style rules, but the result is written in your chosen target language (`{{language}}` comes from the target-language picker, default **English**). |

Neither Rewrite skill has a shortcut until you pick one. They do not use the offline engine.

### Sensitivity

A **Sensitivity** submenu in the tray (and the Settings tab) changes how eagerly the built-in engine corrects, live, without restarting:

- **Cautious** — uses the original, conservative thresholds and prefers to leave a word alone.
- **Balanced** — the default. A little more willing to fix spelling and real-word mix-ups.
- **Aggressive** — lower confidence floors and real-word swap margins, so it can fix cases such as `creat` → `create` and `fro` → `for` in a “scope for now” context. It still aims to leave clean English (and phrases such as “to and fro”) alone.

### Optional online AI

In **Settings → Online AI** you can add a provider:

- **Google AI Studio (Gemini)** — paste an API key from [Google AI Studio](https://aistudio.google.com/apikey). TinyFix calls the native Gemini `generateContent` REST API.
- **OpenAI-compatible** — base URL + API key. This covers OpenAI, OpenRouter, Groq, Ollama, LM Studio, and any other `/v1/chat/completions` server.

The default Gemini model is `gemini-3.5-flash-lite`. Refresh the model list from the provider when possible, or type a model name. An existing `llm_model` in `config.ini` is left unchanged. **Test** sends a tiny typo-correction request; only after a successful test can you tick **Use online model**. The default timeout is **2 seconds**. If the request times out or errors, TinyFix silently falls back to the built-in offline engine and logs the reason (never the key, never your text). With no key, everything works as it does today.

API keys are stored with Windows DPAPI (`CryptProtectData`) in `secrets.dpapi` next to the app. They are never written to `config.ini` and never logged.

### Prompts

Each skill has its own editable prompt on the Skills tab (`{{content}}`, and optionally `{{language}}`). The Prompts tab holds the default target language (English). **Reset prompt to default** on a built-in skill restores its original wording. TinyFix strips wrapping code fences or quotes a model might add.

**Translate** (Ctrl+Alt+T) is selection-based, with an optional “select the whole field first” checkbox in Settings. It only runs when an online model is configured and enabled. If translation fails, the original text is left unchanged.

On **Settings → Skills**, the **Built-in skills** section lists Fix, Fix all, Translate, Rewrite, and Rewrite to target language, each with an enable switch and shortcut recorder. The target-language control (default **English**) is shared with the Prompts tab and feeds `{{language}}` in prompts that use it.

## Configuration

Shortcuts, sensitivity, and non-secret online settings are stored in `config.ini` next to the app (typically `%LOCALAPPDATA%\Programs\TinyFix Hotkey\config.ini`):

```ini
[TinyFix]
hotkey=Ctrl+Alt+Space
hotkey_all=Ctrl+F3
hotkey_translate=Ctrl+Alt+T
sensitivity=balanced
```

User-defined skills (and the Skill menu shortcut) are stored in `skills.json` in the same folder. This file is not encrypted; do not put API keys in a prompt. Upgrades from 1.2.x create `skills.json` from your existing shortcuts and prompts on first launch.

Set a shortcut to `none` to disable it. Both fix shortcuts can be disabled at once; left-clicking the tray icon still fixes the clipboard.

The setup wizard remembers your existing shortcuts on upgrade. Silent installs keep those choices, or you can pass them explicitly (use `none` to disable):

```text
TinyFix-Setup.exe /VERYSILENT /HOTKEY=Ctrl+Shift+F12 /HOTKEYALL=Ctrl+F3 /HOTKEYTRANSLATE=Ctrl+Alt+T
TinyFix-Setup.exe /VERYSILENT /HOTKEY=none /HOTKEYALL=none
```

## Updates

TinyFix checks GitHub for a new release at most once a day when it starts, and whenever you choose **Check for updates** in the tray, in Settings, or in **About TinyFix**. A manual check always runs (it ignores the once-a-day limit) and always shows visible feedback: first **Checking…**, then either the update prompt, **You're on the latest version (vX)**, or **Couldn't check for updates** with a short reason. Every step is written to `tinyfix.log`. If a newer version is available it asks: “TinyFix vX.Y.Z is available. Update now?” Choosing Yes downloads `TinyFix-Setup.exe` and runs a silent upgrade that closes only the TinyFix running from this install folder, keeps your folder, startup choice, shortcuts, and skills, then relaunches the app. If the computer is offline, or GitHub rate-limits the check (HTTP 403/429), a manual check still tells you; an automatic check is only logged and is retried later — it does not count as a successful “checked today”.

**About TinyFix** in the tray menu shows the version, the bundled model revision, the install path, and a link to the GitHub project. The tray tooltip also includes the version, for example `TinyFix v1.4.0`.

v1.4.0 adds built-in **Rewrite** and **Rewrite to target language** skills. They ship disabled with no shortcut; enable them and assign a hotkey in Settings when you use an online model. Their default prompts include No-Slop style rules (see Credits).

v1.3.2 changes the default sensitivity to Balanced and the default Gemini model to `gemini-3.5-flash-lite` (existing config values are kept). It also stops TinyFix from leaving other windows always-on-top or holding focus after Fix, the Skill menu, or Settings (foreign windows are never made topmost; thread-input attaches are always detached; the hidden UI window is not raised), speeds up Settings OK by moving ACL work off the UI thread, and tightens Settings/update UI threading and silent upgrades when the app cannot be closed.

## Uninstall

Open **Settings → Apps → Installed apps**, find **TinyFix Hotkey**, and uninstall. The uninstaller stops the app first and removes the encrypted key file and custom prompts. You can also use the Start Menu shortcut or `unins000.exe` in the install folder.

## Limitations

- The built-in engine is English only.
- While TinyFix is running, it owns the shortcuts you enabled. If you keep a key such as F3, that key will no longer mean “Find Next” in other programs. Disable a shortcut, or pick a different combo, if you need the key back.
- The shortcuts cannot reach windows that are running as Administrator unless TinyFix itself is also elevated. In that case you will see a short notification.
- The built-in model still misses some errors; Cautious would rather leave a word alone than change one you meant. Aggressive fixes more, and may occasionally over-correct.
- Online correction depends on your provider and the 2-second default timeout; a failure falls back to the offline engine.

## Privacy

After installation, TinyFix works entirely offline on your computer unless you enable an online model. It does not send your text or clipboard to a server by default. The optional update check only asks GitHub whether a newer release exists. If you enable online AI, only the text you ask it to correct or translate is sent to the provider you configured. API keys stay on this PC, encrypted with Windows DPAPI.

## Credits

The correction model is [paperweight/tinyfix](https://huggingface.co/paperweight/tinyfix) by Chris Ahlgren. TinyFix Hotkey bundles that model and a Python port of its engine; it is not affiliated with the model author. The model files are fetched at build time from the revision pinned in `model-revision.txt` and shipped inside the installer. A daily GitHub Action checks Hugging Face for a newer model and, if tests pass, publishes a new patch release.

The default **Rewrite** and **Rewrite to target language** prompts include style rules adapted from [No-Slop](https://github.com/soheilmomeniii/no-slop) by Soheil Momeni (MIT). A copy of the license and third-party notices is in `third_party/no-slop/` in this repository.

## Development

```powershell
git clone git@github.com:Aryan-mor/tinyfix-hotkey.git
cd tinyfix-hotkey
powershell -File scripts\fetch-model.ps1
python -m pip install -r requirements-dev.txt
python -m pytest -q
powershell -File scripts\build.ps1
```

The build script produces `dist\TinyFix-Setup.exe`. It uses a local Python 3.10+ if available, otherwise the machine Python, and packages a self-contained app with PyInstaller plus an Inno Setup wizard.
