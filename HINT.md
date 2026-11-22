# TinyFix Hotkey: quick hints

TinyFix Hotkey fixes typos in any Windows app. Select text, press a shortcut, and the corrected text replaces it. This file is a short guide to the installed folder. The full guide is in `README.md`.

## Default shortcuts

| Skill | Shortcut | Works offline |
| --- | --- | --- |
| Fix (selection) | `Ctrl+Alt+Space` | Yes, English only |
| Fix all (whole field) | `Ctrl+F3` | Yes, English only |
| Translate | `Ctrl+Alt+T` | No, needs an online model |
| Skill menu | `Ctrl+Alt+M` | n/a |
| Improve prompt | none (pick one in Settings) | No, needs an online model |
| Rewrite, Rewrite to target language | none, and switched off | No, needs an online model |

Any shortcut can be changed or set to `none` in **Settings → Skills**. The values you actually use are in `config.ini` and `skills.json`.

## Settings files

Open **Settings** from the tray icon. You can also edit the files below and restart TinyFix.

`config.ini`:

| Key | What it does |
| --- | --- |
| `hotkey` | Fix selection. |
| `hotkey_all` | Fix whole field. |
| `hotkey_translate` | Translate. |
| `sensitivity` | Offline engine: `cautious`, `balanced` (default) or `aggressive`. |
| `translate_select_all` | When `true`, Translate takes the whole field. |
| `llm_enabled` | Turns the online model on or off. Off by default. |
| `llm_provider` | `google`, `openrouter` or an OpenAI-compatible provider. |
| `llm_model`, `llm_base_url` | Main online model and, for compatible servers, its URL. |
| `llm_fallback_provider`, `llm_fallback_model` | Backup model used if the main one fails. |
| `llm_timeout` | Seconds to wait for the online model. Default `5`. |
| `llm_target_language` | Target language for Translate and Rewrite to target language. Default `English`. |
| `last_update_check` | Date of the last GitHub update check. Checked at most once a day. |
| `pack_offer_shown` | Whether the optional offline pack prompt was already shown. |

`skills.json` is the skill list. Each skill has a name, shortcut, `input_mode` (`selection` or `select_all`), prompt, on/off switch and `offline_fallback`. In a prompt, `{{content}}` is the text and `{{language}}` is the target language. The skill menu shortcut is `skill_menu_hotkey`.

`prompt_fix.txt` and `prompt_translate.txt` are copies of the Fix and Translate prompts. TinyFix runs the prompts in `skills.json`, so change them there or on the Skills page.

## Folders and files

- `TinyFix.exe`: the app.
- `_internal`: bundled Python runtime and the offline typo model. Leave it alone.
- `pack`: the optional offline pack (context-aware fixes such as their and they're). Manage it in **Settings → Offline engine**.
- `secrets.dpapi`: your API keys, encrypted with Windows DPAPI. Do not edit or share it.
- `tinyfix.log`: shows which shortcuts registered and which skills ran. It never contains keys or your text.
- `cache`, `tinyfix.pid`: runtime files.
- `stop-tinyfix-in-dir.ps1`: stops only the `TinyFix.exe` that runs from the folder you pass as `-AppDir`.
- `unins000.exe`: the uninstaller.

## Good to know

- The built-in engine only knows English. With an online model on, Fix also works in other languages.
- If a shortcut such as `F3` is enabled, other programs no longer receive that key. Pick another shortcut if you need it back.
- TinyFix cannot send keys to a window running as Administrator unless TinyFix is elevated too.
