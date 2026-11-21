# TinyFix Hotkey

![TinyFix Hotkey demo](docs/demo.gif)

Fix English typos in any Windows app: select text, press your shortcut, done.

## Install

1. Download **TinyFix-Setup.exe** from the [latest release](https://github.com/Aryan-mor/tinyfix-hotkey/releases/latest).
2. Run the setup and click **Next** a few times.
3. Pick a shortcut (default is **Ctrl+Alt+Space**) and optionally start TinyFix with Windows.
4. Click **Finish**. TinyFix sits in the system tray and is ready to use.

No administrator account is required. The setup is per-user and includes the Python runtime and the correction model — nothing else is downloaded at install time.

## Use

- Select English text in Chrome, Notepad, Word, or any other app.
- Press **Ctrl+Alt+Space** (or the shortcut you chose).
- If nothing is selected, nothing happens.
- Non-English text (for example Persian) is left unchanged.

Right-click the tray icon to pause, change the shortcut, or exit.

## Configuration

The shortcut is stored in `config.ini` next to the app (typically `%LOCALAPPDATA%\Programs\TinyFix Hotkey\config.ini`):

```ini
[TinyFix]
hotkey=Ctrl+Alt+Space
```

You can also change it from the tray menu (**Change shortcut…**). If the shortcut is already taken by another program, TinyFix will tell you and let you pick another one.

The setup wizard remembers your existing shortcut on upgrade. Silent installs keep that choice, or you can pass one explicitly:

```text
TinyFix-Setup.exe /VERYSILENT /HOTKEY=Ctrl+Shift+F12
```


## Updates

TinyFix checks GitHub for a new release at most once a day when it starts, and whenever you choose **Check for updates** in the tray menu. If a newer version is available it asks: “TinyFix vX.Y.Z is available. Update now?” Choosing Yes downloads `TinyFix-Setup.exe` and runs a silent upgrade that keeps your install folder, startup choice, and shortcut, then relaunches the app. If the computer is offline, the check is only logged.

## Uninstall

Open **Settings → Apps → Installed apps**, find **TinyFix Hotkey**, and uninstall. The uninstaller stops the app first. You can also use the Start Menu shortcut or `unins000.exe` in the install folder.

## Limitations

- English only.
- While TinyFix is running, it owns the shortcut you chose. If you keep a key such as F3, that key will no longer mean “Find Next” in other programs.
- The shortcut cannot reach windows that are running as Administrator unless TinyFix itself is also elevated.
- The model still misses some errors; it would rather leave a word alone than change one you meant.

## Privacy

After installation, TinyFix works entirely offline on your computer. It does not send your text or clipboard to a server.

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
