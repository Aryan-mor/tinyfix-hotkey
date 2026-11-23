# Contributing to the .NET rewrite

The C# code in `src/` and `tests/` follows the rules of Filee's `CONTRIBUTING.md` (MIT, KnifeLemon; see
`THIRD-PARTY-NOTICES.md`). The Python app in the repository root has its own, unchanged workflow.

## Setup

1. Install the .NET 10 SDK (the band is pinned in `global.json`, newer patches roll forward).
2. `dotnet build TinyFix.slnx -c Release` then `dotnet test TinyFix.slnx -c Release`.
3. `dotnet run --project src/TinyFix.App`.

To try a build next to the TinyFix you use every day, set `TINYFIX_DATA_DIR` to an empty folder first. Settings, logs and the
secret store then live there, that copy is its own single instance, it never reads the Python app's files, never checks for
updates by itself, and registers no autostart entry.

## Rules

* **Style is enforced:** `dotnet format TinyFix.slnx --verify-no-changes` must pass (file-scoped namespaces, `_camelCase`
  private fields, braces when multiline, see `.editorconfig`). Run `dotnet format TinyFix.slnx` before you commit.
* **`dotnet build -c Release` has zero warnings.** CI builds with warnings as errors.
* **Every file starts with a short comment saying what it is for.** Public types and members get `///` documentation. Comments
  are in English and explain *why* when something is not obvious.
* **Projects:** `TinyFix.Core` has no UI and no OS code (it defines the ports); OS code goes into
  `TinyFix.Platform.Windows|Linux|MacOS`; Avalonia code goes into `TinyFix.App`. Packages and versions are only in
  `Directory.Packages.props` (central package management). Add no package without a reason written in that file.
* **UI strings never in code:** keys in every `Assets/i18n/*.json`; XAML uses `{DynamicResource key}`, code uses
  `ILocalizer`. Prompt templates are model input and are never translated. See `docs/DOTNET-ADDING-A-LANGUAGE.md`.
  Translations are machine-generated today and welcome review.
* **MVVM:** view models (CommunityToolkit.Mvvm) have no UI types; views are thin; bindings are compiled (`x:DataType` on
  every view and template).
* **Privacy:** never log API keys or user text; never put a key in an exception message. Tests use a fake `HttpMessageHandler`
  and never reach the network.
* **Tests are hermetic:** temp data folders, fake ports, no real `%APPDATA%`, no Run key, no global hook. Tests that need a
  real OS skip themselves elsewhere (`Assert.SkipUnless`). Port a Python test case by case when you port Python behaviour.
* **Platform honesty:** say in the docs which OS a change was run on. Linux and macOS adapters are untested on real desktops.
* **Perf:** startup does minimal work, nothing blocks the UI thread, windows are created lazily, JSON is source generated.

## Pull requests

Small, focused, with a test. Mention the OS you ran it on.
