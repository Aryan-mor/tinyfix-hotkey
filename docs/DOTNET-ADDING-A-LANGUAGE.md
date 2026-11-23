# Adding a language to TinyFix (.NET)

The mechanism and these steps follow Filee's `docs/ADDING-A-LANGUAGE.md` (MIT, KnifeLemon, see `THIRD-PARTY-NOTICES.md`),
reduced to TinyFix. Four languages ship today: `en`, `de`, `fa`, `ja`.

> **About the existing translations:** the German, Persian and Japanese texts were machine-generated. They are meant to be
> reviewed, and corrections from native speakers are welcome as pull requests.

## Steps

1. **Copy `src/TinyFix.App/Assets/i18n/en.json`** to `<code>.json` in the same folder. Use the language code of the culture
   (`es`, `fr`, `pt-BR`, `zh-CN`, ...). The folder is embedded automatically (`AvaloniaResource Include="Assets\**"`).
2. **Translate the values, never the keys.** Keep every `{0}`, `{1}`, `{2}` placeholder (count and meaning; the order may
   change in a sentence). Keep `{{content}}` and `{{language}}` in `skills.prompt_hint` exactly: they are prompt tokens, not
   UI text. Keep the brand name `TinyFix`. Do not translate LLM prompt templates: they live in code
   (`TinyFix.Core/Prompts`) and are model input.
3. **Register it** in `src/TinyFix.App/Services/AppLanguages.cs`: one line in `All` with the language's own name,
   for example `("es", "Español")`. For a right-to-left language also add it to `RightToLeft` (Arabic and Persian are there).
4. **Fonts:** if the script needs fonts that the default list lacks, add a case to `ThemeService.FontFamilies`. Only system
   fonts are used; nothing is bundled.
5. **Run the tests:** `dotnet test`. `LocalizationTests` fails when a key is missing or extra, a `{0}` placeholder differs from
   English, a value is empty, or the registry and the files disagree. The render tests in `RenderTests` list the languages
   they screenshot; add yours to `SettingsPages` if you want its pages captured.
6. **Look at it:** the screenshots land in `tests/TinyFix.App.Tests/bin/<config>/<tfm>/screenshots`. Check long words,
   wrapping and (for right to left) mirroring.

## How a language is chosen

`LocalizationService.Resolve(setting)`: an explicit choice in Settings always wins. With "Automatic" the OS language is
mapped by `AppLanguages.Match`, which compares whole language subtags (so `fr` never catches `frr`, `id` never catches `idu`),
tells `pt-BR` from `pt-PT` by region and `zh-TW`/`zh-Hant` from `zh-CN` by script, and falls back to English. On macOS the
list comes from `defaults read -g AppleLanguages`, because an app started from Finder has no `LANG`.

## Rules for keys

* Keep the set small and meaningful: every key costs a translation per language.
* Keys of built-in skill names are `skill.<id>`; the name is shown translated only while the stored name still equals the
  English default, so a skill the user renamed keeps its name.
* A missing translation falls back to English at run time, but the test above requires full coverage so it never happens
  silently.
