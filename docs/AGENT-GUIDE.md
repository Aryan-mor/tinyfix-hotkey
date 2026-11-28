# TinyFix guide for AI agents

This file is for an AI coding agent (Claude Code, Codex, Cursor and similar) that is asked to **install TinyFix**, **get it a free API key** or **configure it** on a user's computer. It only states what the code does today. When this file and the code disagree, the code wins: the file formats are in `src/TinyFix.Core/Skills/` and `src/TinyFix.Core/Settings/`.

What TinyFix is: a tray app. The user selects text in any app, presses a shortcut, and an AI model fixes, translates or rewrites it in place.

## Rules for the agent

- **Never ask the user to paste an API key into the chat, and never write one to a file.** Keys are entered in the TinyFix window, which stores them encrypted. There is no file or environment variable for a key.
- **Stop TinyFix before editing `settings.json` or `skills.json`.** The running app does not watch these files, and it rewrites them when Settings is saved, so an edit made while it runs can be lost. Quit it from the tray icon (Quit), edit, then start it again.
- Ask before changing a file the user has already customised. Read it first and change only the fields you were asked to change.
- Verify downloads against `SHA256SUMS.txt` (see below). None of the packages are code-signed.

## 1. Install

Get the file for the user's system from the [latest release](https://github.com/Aryan-mor/tinyfix-hotkey/releases/latest). Replace `<version>` with the version in the file name.

### Windows 10/11 (x64)

1. Download `TinyFix-<version>-win-x64-Setup.exe` and `SHA256SUMS.txt`.
2. Check it, in PowerShell:

   ```powershell
   (Get-FileHash .\TinyFix-<version>-win-x64-Setup.exe -Algorithm SHA256).Hash
   Select-String "Setup.exe" .\SHA256SUMS.txt
   ```

   The two hashes must match (case does not matter).
3. Run the installer. It installs for the current user and needs no administrator rights. SmartScreen may warn because the file is not signed: **More info**, then **Run anyway**. Tell the user before doing this.
4. A portable `TinyFix-<version>-win-x64.exe` needs no install.
5. Remove it in Windows **Settings → Apps**.

### macOS (Apple silicon or Intel)

1. Download `TinyFix-<version>-osx-arm64.dmg` (Apple silicon) or `TinyFix-<version>-osx-x64.dmg` (Intel). `uname -m` prints `arm64` or `x86_64`.
2. Check it: `shasum -a 256 TinyFix-<version>-osx-arm64.dmg`, and compare with `SHA256SUMS.txt`.
3. Open the image and drag TinyFix to Applications.
4. The app is not notarized. The first time, the user must **right-click the app and choose Open**.
5. Grant the **Accessibility** permission when asked (System Settings → Privacy & Security → Accessibility). Without it, shortcuts and paste do nothing.

macOS support is built and unit-tested in CI but has not been tried on a real Mac. Say so to the user if something does not work.

### Linux (x64 or arm64)

1. Download `TinyFix-<version>-linux-x64.tar.gz` or `-linux-arm64.tar.gz` (`uname -m`: `x86_64` or `aarch64`).
2. Check it: `sha256sum -c --ignore-missing SHA256SUMS.txt`.
3. Extract it, then from the extracted folder run `./install.sh`. It copies TinyFix to `~/.local/share/tinyfix` and adds `~/.local/bin/tinyfix`. `./uninstall.sh` removes that copy and keeps the settings in `~/.config/tinyfix`.
4. **X11 is required.** Wayland blocks global shortcuts and simulated paste. Check with `echo $XDG_SESSION_TYPE`; if it prints `wayland`, tell the user to log in with an X11 session (XWayland may work).
5. GNOME hides tray icons unless the AppIndicator extension (AppIndicator and KStatusNotifierItem Support) is installed.
6. The Linux adapters use `xclip` (or `wl-clipboard`), `xdotool` and `secret-tool`. Install them with the system package manager if they are missing.

### From source (any system, needs the .NET 10 SDK)

```sh
git clone https://github.com/Aryan-mor/tinyfix-hotkey.git
cd tinyfix-hotkey
dotnet run --project src/TinyFix.App
```

### Check the install

TinyFix has no window until opened: look for its icon in the system tray. Starting it a second time opens Settings in the running copy. The first start opens a short setup window (language, AI model, skills, shortcuts).

## 2. Get a free Google (Gemini) API key

Google is TinyFix's default provider, and Google AI Studio gives a key without a payment card. The user must do these steps themselves in their own browser, because they involve their Google account:

1. Open <https://aistudio.google.com/apikey> and sign in with a Google account.
2. Choose **Create API key** (pick or create a Google Cloud project when asked, the default is fine) and copy the key.
3. In TinyFix, paste it in the setup window's AI model step, or in **Settings → AI models** (protocol **Google**, key field). Use **Test connection** to confirm it works.

Notes for the agent:

- The free tier has rate limits and its terms can change. If requests fail with HTTP 429 or 403, the quota or the key is the cause. Do not promise a quota. Point the user to Google's current pricing page.
- Free-tier Google requests may be used by Google to improve its products. Tell the user not to run TinyFix on private text if that matters to them.
- Alternatives: **OpenRouter** (<https://openrouter.ai/keys>, some models are free), **OpenAI**, **Anthropic**, or a server on the user's own machine (**Ollama**, **LM Studio**) that needs no key and no internet.
- Default models: Google main `gemini-3.5-flash-lite`, fallback `gemini-3.6-flash`. The model id is editable.

## 3. Configure TinyFix

### Where the files are

| System | Data folder |
| --- | --- |
| Windows | `%APPDATA%\TinyFix` |
| macOS | `~/Library/Application Support/TinyFix` |
| Linux | `~/.config/tinyfix` (or `$XDG_CONFIG_HOME/tinyfix`) |

`TINYFIX_DATA_DIR` moves it to another folder, which is the safe way to try a change on a copy. `TINYFIX_SKIP_SETUP=1` starts without the first-run window.

| File | Holds |
| --- | --- |
| `settings.json` | language, theme, model endpoints (no keys), prompt variables, donut look, update checks |
| `skills.json` | the skill list and the skill menu shortcut |
| `secrets.dpapi` (Windows), Keychain (macOS), libsecret or an encrypted file (Linux) | API keys. Never edit. |
| logs | the selected text is never logged |

Both JSON files use `snake_case` keys. Files are written atomically. A wrong field is repaired or ignored, it does not stop the app from starting.

### `settings.json`

```json
{
  "language": "auto",
  "theme": "system",
  "variables": { "language": "English" },
  "llm": {
    "main":     { "protocol": "google", "model": "gemini-3.5-flash-lite", "base_url": "", "timeout": 5 },
    "fallback": { "protocol": "google", "model": "gemini-3.6-flash", "base_url": "", "timeout": 5 }
  },
  "check_for_updates": true,
  "show_change_toast": true,
  "setup_completed": true,
  "donut": { "outer_radius": 150, "opacity": 0.96, "open_at": "caret", "show_shortcut_hints": true }
}
```

- `llm.main.protocol` is one of `google`, `openrouter`, `openai`, `anthropic`, `ollama`, `lmstudio`, `custom`. `base_url` empty means the preset's URL (a `custom` protocol needs one, and it must be OpenAI-compatible).
- `llm.fallback` is tried when the main model times out or answers 429 or 5xx. Remove it (or give it an empty `model`) to turn the fallback off.
- `variables.language` is the target language of Translate (`{{language}}` in prompts). Other variables added here can be used in prompts as `{{name}}`. A name is letters, digits and `_`, not starting with a digit, and `content` is reserved.
- `donut.open_at` is `caret` or `cursor`. `donut.outer_radius` is 110 to 220, `donut.opacity` 0.4 to 1.
- Keep `setup_completed` `true` so the setup window does not open again.

### `skills.json`: add or change a skill

A skill is a name, an input mode, a prompt and an optional shortcut. Minimal custom AI skill, added to the `skills` array:

```json
{
  "id": "my-formal-mail",
  "name": "Formal mail",
  "kind": "ai",
  "input_mode": "auto",
  "prompt": "Rewrite the text below as a polite, formal email in {{language}}. Return only the email.\n\n{{content}}",
  "hotkey": "Ctrl+Alt+F",
  "enabled": true,
  "model_mode": "default"
}
```

| Field | Values |
| --- | --- |
| `id` | unique string. Leave it empty to get a GUID. Built-in ids: `fix`, `fix-all`, `translate`, `rewrite`, `rewrite-language`, `improve-prompt`, `layout-any-fa`, `layout-swap` |
| `name` | text shown in the donut and in Settings |
| `kind` | `ai` (default), `layout` (retype text typed on the wrong keyboard layout, no AI) or `swap` (two layouts, both directions) |
| `input_mode` | `selection` (the selected text), `select_all` (select the whole field first) or `auto` (the selection if there is one, else the whole field) |
| `prompt` | must contain `{{content}}` (the text). `{{language}}` and your own variables are filled in. Prompts are not translated |
| `hotkey` | for example `Ctrl+Alt+F`, or `none`. Modifiers: `Ctrl`, `Alt`, `Shift`, `Win` (Cmd on macOS). Keys: letters, digits, F1 to F12, `Space`, `Tab`, `Enter`, `Esc`, arrows, and so on. Pick a combination that is free, because TinyFix swallows it system-wide |
| `enabled` | `true` or `false`. Built-in `fix` and `translate` can only be turned off, not deleted |
| `keep_original_on_clipboard` | AI skills only. `true` leaves the replaced text on the clipboard |
| `model_mode` | `default` (global model), `better` (a stronger preset) or `custom` (use the skill's own `model_provider`, `model`, `model_base_url`, `model_timeout` and the `model_fallback_*` fields) |
| `layout_from`, `layout_to` | layout skills: a language code such as `en` or `fa`, or `any` for `layout_from` |

The top-level `skill_menu_hotkey` (default `Ctrl+Alt+M`) opens the donut menu. Default shortcuts: Fix `Ctrl+Alt+Space`, Translate `Ctrl+Alt+T`.

A skill with a custom model needs its own key in the TinyFix window (**Settings → Skills**), or it uses the key saved for its protocol.

### Apply and check

1. Edit the files while TinyFix is stopped, and keep a copy of the originals.
2. Start TinyFix. Open **Settings → Skills** and check that the skill is listed with the right shortcut. A shortcut that clashes with another one is shown as a conflict there.
3. Try it: type a sentence with a mistake in any text box, select it, press the shortcut.
4. If a skill does nothing: is TinyFix paused (tray menu), is the skill enabled, is an AI model set with a key saved (**Settings → AI models → Test connection**), and is another app using the same shortcut?

### Prefer the window when the user is present

Everything above can also be done in the TinyFix window: **Settings → AI models** (endpoint and keys), **Skills** (add, edit, shortcut, model), **Variables**, **Donut**. **Settings → About → Run setup again**, or **Setup...** on the tray icon, reopens the first-run window without clearing what is saved. Editing the files is the way when the agent has to set up many skills at once or work without a user at the screen. The key still has to be typed into the window.
