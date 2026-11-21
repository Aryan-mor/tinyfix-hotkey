# TinyFix Hotkey

![TinyFix Hotkey demo](docs/demo.gif)

Fix English typos in any Windows app: select text, press your shortcut, done.

## Install

1. Download **TinyFix-Setup.exe** from the [latest release](https://github.com/Aryan-mor/tinyfix-hotkey/releases/latest).
2. Run the setup and click **Next** a few times.
3. Pick shortcuts for **Fix selection** (default **Ctrl+Alt+Space**) and **Fix all** (default **Ctrl+F3**). Either shortcut can be set to **None/Disabled**. You can also start TinyFix with Windows.
4. Click **Finish**. TinyFix sits in the system tray and is ready to use.

No administrator account is required. The setup is per-user and includes the Python runtime and the correction model — nothing else is downloaded at install time.

## Use

- Select English text in Chrome, Notepad, Word, or any other app, then press **Ctrl+Alt+Space** (or the Fix selection shortcut you chose).
- To correct the whole field, press **Ctrl+F3** (or your Fix all shortcut). TinyFix selects all, copies, corrects, and pastes back.
- Left-click the tray icon to correct whatever text is currently on the clipboard. The corrected text stays on the clipboard; a short notification tells you how many changes were made, or that nothing needed fixing.
- Right-click the tray icon for the menu: pause, change shortcuts, set correction sensitivity, check for updates, or exit.
- If nothing is selected (or the copy fails), TinyFix tells you. If the text was already fine, it says **No typos found**.
- Non-English text (for example Persian) is left unchanged.

## Sensitivity

A **Sensitivity** submenu in the tray changes how eagerly TinyFix corrects, live, without restarting:

- **Cautious** — the default. Uses the original, conservative thresholds and prefers to leave a word alone.
- **Balanced** — a little more willing to fix spelling and real-word mix-ups.
- **Aggressive** — lower confidence floors and real-word swap margins, so it can fix cases such as `creat` → `create` and `fro` → `for` in a “scope for now” context. It still aims to leave clean English (and phrases such as “to and fro”) alone.

The setting is stored in `config.ini` as `sensitivity=cautious`, `balanced`, or `aggressive`.

## Configuration

Shortcuts, sensitivity, and update-check state are stored in `config.ini` next to the app (typically `%LOCALAPPDATA%\Programs\TinyFix Hotkey\config.ini`):

```ini
[TinyFix]
hotkey=Ctrl+Alt+Space
hotkey_all=Ctrl+F3
sensitivity=cautious
```

Set `hotkey=none` or `hotkey_all=none` to disable that shortcut. Both can be disabled at once; left-clicking the tray icon still fixes the clipboard.

You can also change shortcuts from the tray menu (**Change shortcut…**), including a **None/Disabled** option for each one. If a shortcut is already taken by another program, TinyFix will tell you and let you pick another.

The setup wizard remembers your existing shortcuts on upgrade. Silent installs keep those choices, or you can pass them explicitly (use `none` to disable):

```text
TinyFix-Setup.exe /VERYSILENT /HOTKEY=Ctrl+Shift+F12 /HOTKEYALL=Ctrl+F3
TinyFix-Setup.exe /VERYSILENT /HOTKEY=none /HOTKEYALL=none
```

## Updates

TinyFix checks GitHub for a new release at most once a day when it starts, and whenever you choose **Check for updates** in the tray menu. If a newer version is available it asks: “TinyFix vX.Y.Z is available. Update now?” Choosing Yes downloads `TinyFix-Setup.exe` and runs a silent upgrade that keeps your install folder, startup choice, and shortcuts, then relaunches the app. If the computer is offline, or GitHub rate-limits the check (HTTP 403/429), the attempt is only logged and is retried later — it does not count as a successful “checked today”.

## Uninstall

Open **Settings → Apps → Installed apps**, find **TinyFix Hotkey**, and uninstall. The uninstaller stops the app first. You can also use the Start Menu shortcut or `unins000.exe` in the install folder.

## Limitations

- English only.
- While TinyFix is running, it owns the shortcuts you enabled. If you keep a key such as F3, that key will no longer mean “Find Next” in other programs. Disable a shortcut, or pick a different combo, if you need the key back.
- The shortcuts cannot reach windows that are running as Administrator unless TinyFix itself is also elevated. In that case you will see a short notification.
- The model still misses some errors; Cautious would rather leave a word alone than change one you meant. Aggressive fixes more, and may occasionally over-correct.

## Privacy

After installation, TinyFix works entirely offline on your computer. It does not send your text or clipboard to a server. The optional update check only asks GitHub whether a newer release exists.

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
