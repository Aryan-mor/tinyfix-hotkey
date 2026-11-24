# Security policy

TinyFix handles API keys and the text you select, so security reports are taken seriously.

## Reporting a problem

Please **do not open a public issue**. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/Aryan-mor/tinyfix-hotkey/security/advisories/new).
Include what you found, how to reproduce it, and the version and OS you used.

You can expect a first reply within a week. Once a fix is released, the advisory is published and you are credited
unless you ask not to be.

## Supported versions

Only the [latest release](https://github.com/Aryan-mor/tinyfix-hotkey/releases/latest) gets security fixes. TinyFix
updates itself in place, so please update before reporting.

## Worth knowing

- API keys are stored encrypted with Windows DPAPI. The macOS Keychain and Linux libsecret adapters are untested.
- Only the selected text and your prompt are sent, and only to the AI provider you configure. HTTP is refused for any
  endpoint other than localhost.
- TinyFix never logs your text or your keys. A log line that contains either is a security bug.
- The packages are not code-signed. Check downloads against `SHA256SUMS.txt` from the release.
