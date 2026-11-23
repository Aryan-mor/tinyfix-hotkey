# TinyFix

Fix the text you are typing in any app: select it, press a shortcut, and the corrected text replaces it. TinyFix lives in the system tray and works with an AI model of your choice, in the language of your text.

> TinyFix 2 is a rewrite in C# with [Avalonia](https://avaloniaui.net/) for Windows, macOS and Linux. Its layout and code quality follow [Filee](https://github.com/KnifeLemon/Filee) (MIT). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Features

- **Fix in place.** Select text, press a shortcut, and the corrected text replaces it. No copy and paste, no browser tab.
- **Fix the whole field.** One shortcut selects everything in the current text box, fixes it, and pastes it back.
- **Any language.** The model fixes text in the language it is written in.
- **Translate, Improve prompt, your own skills.** Every skill has a name, an input mode, a prompt, and an optional shortcut of its own.
- **The donut menu.** One shortcut opens a ring of your skills next to the caret or the mouse. Pick with the mouse or press 1 to 9. Tab or the mouse wheel flips pages. Size, opacity and where it opens are set in Settings, with a live preview.
- **Your choice of model.** Google, OpenRouter, or any OpenAI-compatible endpoint, with a fallback model. API keys are stored encrypted with Windows DPAPI (macOS Keychain and Linux libsecret adapters exist but are untested).
- **Quiet.** System tray icon, Settings window, light and dark themes.

## Default shortcuts

| Action | Shortcut |
| --- | --- |
| Fix | Ctrl+Alt+Space |
| Fix all | Ctrl+F3 |
| Translate | Ctrl+Alt+T |
| Donut menu | Ctrl+Alt+M |

Change any of them in **Settings**. A skill does nothing until an AI model is set up under **AI models** (and an API key is saved, when that protocol asks for one).

## Run it

Packaged releases are not published yet. Build and run from source with the .NET 10 SDK:

```sh
dotnet run --project src/TinyFix.App
```

Data (settings, skills, encrypted keys, logs) lives in `%APPDATA%\TinyFix` on Windows, `~/Library/Application Support/TinyFix` on macOS and `~/.config/tinyfix` on Linux. Set `TINYFIX_DATA_DIR` to use another folder.

## Platform status

| OS | Status |
| --- | --- |
| Windows 10/11 | Developed and tried here. |
| macOS | Adapters written, built and unit-tested in CI only. Needs the Accessibility permission. Not tried on a real Mac. |
| Linux | X11 only, same status as macOS. Wayland blocks global shortcuts and simulated paste. |

## Develop

```sh
dotnet build TinyFix.slnx -c Release
dotnet format TinyFix.slnx --verify-no-changes
dotnet test TinyFix.slnx -c Release
```

Read [CONTRIBUTING-DOTNET.md](CONTRIBUTING-DOTNET.md) and [docs/DOTNET-ARCHITECTURE.md](docs/DOTNET-ARCHITECTURE.md) first. UI text lives in `src/TinyFix.App/Assets/i18n/*.json` (English, German, Persian, Japanese; the translations are machine-made and welcome review).

## License

MIT, see [LICENSE](LICENSE). Prompt style rules are adapted from the No-Slop skill by Soheil Momeni (MIT), see [third_party/no-slop](third_party/no-slop).
