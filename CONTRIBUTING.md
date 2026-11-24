# Contributing to TinyFix

Thanks for helping. Bug reports, translations, docs fixes and code are all welcome. This page explains how a change gets from
your computer into `main`. The coding rules for the C# app are in [CONTRIBUTING-DOTNET.md](CONTRIBUTING-DOTNET.md).

`main` is protected: nobody pushes to it directly, the maintainer included. Every change, however small, arrives as a pull
request (PR) and is merged on GitHub.

## Before you start

- **Small fix** (typo, wording, an obvious bug): just open a PR.
- **Anything bigger** (a new skill, a new setting, a change in behaviour): open an
  [issue](https://github.com/Aryan-mor/tinyfix-hotkey/issues) or a
  [discussion](https://github.com/Aryan-mor/tinyfix-hotkey/discussions) first and describe what you want to change. This
  saves you from writing code that cannot be merged.
- **Translations:** the files in `src/TinyFix.App/Assets/i18n/` are machine-made. Corrections from native speakers are
  very welcome. To add a language, read [docs/DOTNET-ADDING-A-LANGUAGE.md](docs/DOTNET-ADDING-A-LANGUAGE.md).

## Open a pull request

You need a GitHub account and Git. The steps use the command line; GitHub Desktop and the GitHub website work too.

1. **Fork the repository.** Click **Fork** at the top of the
   [repository page](https://github.com/Aryan-mor/tinyfix-hotkey). This makes your own copy under your account.

2. **Clone your fork** and add this repository as `upstream`:

   ```sh
   git clone https://github.com/<your-name>/tinyfix-hotkey.git
   cd tinyfix-hotkey
   git remote add upstream https://github.com/Aryan-mor/tinyfix-hotkey.git
   ```

3. **Create a branch** from an up-to-date `main`. Name it after the change:

   ```sh
   git fetch upstream
   git switch -c fix/donut-label-overlap upstream/main
   ```

4. **Make the change**, then build, format and test (needs the .NET 10 SDK):

   ```sh
   dotnet format TinyFix.slnx
   dotnet build TinyFix.slnx -c Release
   dotnet test TinyFix.slnx -c Release
   ```

   Docs-only changes can skip this.

5. **Commit** with a message that says what changed and why, in plain words:

   ```sh
   git add -A
   git commit -m "Keep long skill names inside their donut slice"
   ```

6. **Push the branch to your fork:**

   ```sh
   git push -u origin fix/donut-label-overlap
   ```

7. **Open the pull request.** GitHub shows a **Compare & pull request** button on your fork, or run
   `gh pr create --repo Aryan-mor/tinyfix-hotkey`. Use `Aryan-mor/tinyfix-hotkey`, branch `main`, as the base. Fill in
   the template: what changed, why, and which OS you ran it on. Add a screenshot or GIF for anything visible.

8. **Wait for CI and review.** Code changes run format, build and tests on Windows, Linux and macOS. If something fails,
   or the review asks for a change, commit to the same branch and push again: the PR updates by itself. You do not need to
   open a new one.

9. **Keep the branch current** if `main` moves on and GitHub reports a conflict:

   ```sh
   git fetch upstream
   git rebase upstream/main
   git push --force-with-lease
   ```

When the PR is merged you can delete your branch. Your commits are in `main` and your name is in the history.

## What makes a PR easy to merge

- One change per PR. A bug fix and a refactor are two PRs.
- A test for new behaviour or a fixed bug, where one is possible.
- No new warnings: CI builds with warnings as errors.
- UI text goes in `Assets/i18n/*.json`, never in code.
- Say which OS you tried it on. Linux and macOS are untested on real desktops, so a report from either is useful by itself.
- Never include API keys, personal text or logs that contain them.

## For the maintainer: review and merge

1. Read the diff and the PR description. For a code change, wait for the `format` and `build and test` checks to pass. The
   `dotnet` workflow runs only when code, tests or build files change, so a docs-only PR shows no checks.
2. Try it locally when the change is visible or risky:

   ```sh
   gh pr checkout <number>
   dotnet run --project src/TinyFix.App
   ```

3. Ask for changes in review comments, or approve.
4. Merge on GitHub, or run `gh pr merge <number> --squash --delete-branch`. Squash keeps one commit per PR in `main`;
   use **Create a merge commit** (`--merge`) for a branch whose commits should stay separate, such as a release branch.
5. Releases are cut from `main` by pushing a `v*` tag, which runs the `release` workflow.

Your own changes take the same path: branch, push, open a PR, merge.
