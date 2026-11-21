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

- Select English text in Chrome, Notepad, Word, or any other app, then press **Ctrl+Alt+Space** (or the Fix selection shortcut you chose).
- To correct the whole field, press **Ctrl+F3** (or your Fix all shortcut). TinyFix selects all, copies, corrects, and pastes back.
- Left-click the tray icon to correct whatever text is currently on the clipboard. The corrected text stays on the clipboard; a short notification tells you how many changes were made, or that nothing needed fixing.
- Right-click the tray icon for **Settings…**, pause, sensitivity, updates, or exit.
- If nothing is selected (or the copy fails), TinyFix tells you. If the text was already fine, it says **No typos found**.
- Non-English text (for example Persian) is left unchanged by the built-in engine.

## Settings

Open **Settings…** from the tray for a single window with Shortcuts, Sensitivity, Online AI, Prompts, Updates, and About.

### Shortcuts

Each of the three shortcuts can be recorded, changed, or set to **None/Disabled**:

| Action | Default | Notes |
| --- | --- | --- |
| Fix selection | **Ctrl+Alt+Space** | Corrects the highlighted text. |
| Fix all | **Ctrl+F3** | Selects the whole field first. |
| Translate | **Ctrl+Alt+T** | Only active when an online model is enabled. **T** is for translate; Ctrl+T and Ctrl+Shift+T are already used by browsers, but Ctrl+Alt+T is rarely bound on Windows. |

### Sensitivity

A **Sensitivity** submenu in the tray (and the Settings tab) changes how eagerly the built-in engine corrects, live, without restarting:

- **Cautious** — the default. Uses the original, conservative thresholds and prefers to leave a word alone.
- **Balanced** — a little more willing to fix spelling and real-word mix-ups.
- **Aggressive** — lower confidence floors and real-word swap margins, so it can fix cases such as `creat` → `create` and `fro` → `for` in a “scope for now” context. It still aims to leave clean English (and phrases such as “to and fro”) alone.

### Optional online AI

In **Settings → Online AI** you can add a provider:

- **Google AI Studio (Gemini)** — paste an API key from [Google AI Studio](https://aistudio.google.com/apikey). TinyFix calls the native Gemini `generateContent` REST API.
- **OpenAI-compatible** — base URL + API key. This covers OpenAI, OpenRouter, Groq, Ollama, LM Studio, and any other `/v1/chat/completions` server.

Refresh the model list from the provider when possible, or type a model name. **Test** sends a tiny typo-correction request; only after a successful test can you tick **Use online model**. The default timeout is **2 seconds**. If the request times out or errors, TinyFix silently falls back to the built-in offline engine and logs the reason (never the key, never your text). With no key, everything works as it does today.

API keys are stored with Windows DPAPI (`CryptProtectData`) in `secrets.dpapi` next to the app. They are never written to `config.ini` and never logged.

### Prompts

Settings includes an editable **correction prompt** and **translate prompt**. `{{content}}` is replaced with the selected text; if you omit it, the text is appended. The translate prompt also has `{{language}}` (default target language: English). **Reset to default** restores the built-in wording. TinyFix strips wrapping code fences or quotes a model might add.

**Translate** (Ctrl+Alt+T) is selection-based, with an optional “select the whole field first” checkbox in Settings. It only runs when an online model is configured and enabled. If translation fails, the original text is left unchanged.

## Configuration

Shortcuts, sensitivity, and non-secret online settings are stored in `config.ini` next to the app (typically `%LOCALAPPDATA%\Programs\TinyFix Hotkey\config.ini`):

```ini
[TinyFix]
hotkey=Ctrl+Alt+Space
hotkey_all=Ctrl+F3
hotkey_translate=Ctrl+Alt+T
sensitivity=cautious
```

Set a shortcut to `none` to disable it. Both fix shortcuts can be disabled at once; left-clicking the tray icon still fixes the clipboard.

The setup wizard remembers your existing shortcuts on upgrade. Silent installs keep those choices, or you can pass them explicitly (use `none` to disable):

```text
TinyFix-Setup.exe /VERYSILENT /HOTKEY=Ctrl+Shift+F12 /HOTKEYALL=Ctrl+F3 /HOTKEYTRANSLATE=Ctrl+Alt+T
TinyFix-Setup.exe /VERYSILENT /HOTKEY=none /HOTKEYALL=none
```

## Updates

TinyFix checks GitHub for a new release at most once a day when it starts, and whenever you choose **Check for updates** in the tray or in Settings. If a newer version is available it asks: “TinyFix vX.Y.Z is available. Update now?” Choosing Yes downloads `TinyFix-Setup.exe` and runs a silent upgrade that keeps your install folder, startup choice, and shortcuts, then relaunches the app. If the computer is offline, or GitHub rate-limits the check (HTTP 403/429), the attempt is only logged and is retried later — it does not count as a successful “checked today”.

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
