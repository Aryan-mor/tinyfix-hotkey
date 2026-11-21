"""Check GitHub releases for a newer TinyFix and optionally start a silent update."""

from __future__ import annotations

import json
import logging
import os
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import date
from pathlib import Path

GITHUB_LATEST = "https://api.github.com/repos/Aryan-mor/tinyfix-hotkey/releases/latest"
SETUP_ASSET = "TinyFix-Setup.exe"
CONFIG_SECTION = "TinyFix"
LAST_CHECK_KEY = "last_update_check"
USER_AGENT = "TinyFix-Hotkey-UpdateCheck"

_VERSION_RE = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)")


@dataclass(frozen=True)
class Version:
    major: int
    minor: int
    patch: int

    def __str__(self) -> str:
        return f"{self.major}.{self.minor}.{self.patch}"

    def __lt__(self, other: "Version") -> bool:
        return (self.major, self.minor, self.patch) < (other.major, other.minor, other.patch)


def parse_version(text: str) -> Version:
    if text is None:
        raise ValueError("Version is empty")
    raw = str(text).strip()
    match = _VERSION_RE.match(raw)
    if not match:
        raise ValueError(f"Not a version: {text}")
    return Version(int(match.group(1)), int(match.group(2)), int(match.group(3)))


def is_newer(latest: str, current: str) -> bool:
    """Return True if latest is a higher semver than current (v prefix allowed)."""
    return parse_version(current) < parse_version(latest)


def bump_patch(current: str) -> str:
    version = parse_version(current)
    return str(Version(version.major, version.minor, version.patch + 1))


@dataclass(frozen=True)
class LatestRelease:
    tag: str
    version: str
    setup_url: str
    name: str


def fetch_latest_release(
    url: str = GITHUB_LATEST,
    timeout: float = 15,
    opener: object | None = None,
) -> LatestRelease:
    request = urllib.request.Request(
        url,
        headers={
            "User-Agent": USER_AGENT,
            "Accept": "application/vnd.github+json",
        },
    )
    open_url = opener or urllib.request.urlopen
    try:
        with open_url(request, timeout=timeout) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        if exc.code in (403, 429):
            raise ConnectionError(f"GitHub rate limited ({exc.code}); will retry later") from exc
        raise ConnectionError(f"Could not reach GitHub: {exc}") from exc
    except urllib.error.URLError as exc:
        raise ConnectionError(f"Could not reach GitHub: {exc}") from exc
    tag = str(payload.get("tag_name") or "").strip()
    if not tag:
        raise RuntimeError("Latest release is missing a tag")
    version = str(parse_version(tag))
    setup_url = ""
    for asset in payload.get("assets") or []:
        if asset.get("name") == SETUP_ASSET:
            setup_url = str(asset.get("browser_download_url") or "")
            break
    if not setup_url:
        raise RuntimeError(f"{SETUP_ASSET} was not found on the latest release")
    return LatestRelease(tag=tag, version=version, setup_url=setup_url, name=str(payload.get("name") or tag))


def should_auto_check(config_path: Path, today: str | None = None) -> bool:
    today = today or date.today().isoformat()
    if not config_path.exists():
        return True
    parser = __import__("configparser").ConfigParser()
    try:
        parser.read(config_path, encoding="utf-8")
        last = parser.get(CONFIG_SECTION, LAST_CHECK_KEY, fallback="").strip()
    except Exception:
        logging.exception("Could not read last update check from %s", config_path)
        return True
    return last != today


def mark_checked(config_path: Path, today: str | None = None) -> None:
    today = today or date.today().isoformat()
    parser = __import__("configparser").ConfigParser()
    if config_path.exists():
        try:
            parser.read(config_path, encoding="utf-8")
        except Exception:
            logging.exception("Could not read %s before saving last update check", config_path)
    if CONFIG_SECTION not in parser:
        parser[CONFIG_SECTION] = {}
    parser[CONFIG_SECTION][LAST_CHECK_KEY] = today
    config_path.parent.mkdir(parents=True, exist_ok=True)
    with config_path.open("w", encoding="utf-8") as handle:
        parser.write(handle)


def default_setup_download_path() -> Path:
    temp = os.environ.get("TEMP") or os.environ.get("TMP") or tempfile.gettempdir()
    return Path(temp) / SETUP_ASSET


def download_file(url: str, dest: Path, timeout: float = 180, opener: object | None = None) -> Path:
    dest.parent.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    open_url = opener or urllib.request.urlopen
    tmp = dest.with_suffix(dest.suffix + ".partial")
    try:
        with open_url(request, timeout=timeout) as response, tmp.open("wb") as handle:
            while True:
                chunk = response.read(256 * 1024)
                if not chunk:
                    break
                handle.write(chunk)
        tmp.replace(dest)
    except urllib.error.URLError as exc:
        if tmp.exists():
            tmp.unlink(missing_ok=True)
        raise ConnectionError(f"Could not download update: {exc}") from exc
    return dest


def start_silent_update(setup_path: Path) -> None:
    if not setup_path.exists():
        raise FileNotFoundError(setup_path)
    args = [str(setup_path), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LAUNCH"]
    kwargs: dict = {}
    if sys.platform == "win32":
        kwargs["creationflags"] = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
        kwargs["close_fds"] = True
    subprocess.Popen(args, cwd=str(setup_path.parent), **kwargs)


def check_for_update(current_version: str, fetcher=fetch_latest_release) -> tuple[bool, LatestRelease | None, str | None]:
    """Return (available, release, error). error is set when the network or API fails."""
    try:
        latest = fetcher()
    except Exception as exc:
        return False, None, str(exc)
    return is_newer(latest.version, current_version), latest, None


def short_update_error(error: str | None) -> str:
    """A short, user-facing reason for a failed update check."""
    text = " ".join(str(error or "unknown error").split())
    lowered = text.lower()
    if "rate limit" in lowered or " 403" in text or " 429" in text or "(403)" in text or "(429)" in text:
        return "GitHub rate limited, try again later"
    if "timed out" in lowered or "timeout" in lowered:
        return "the request timed out"
    if "reach github" in lowered or "offline" in lowered or "urlopen" in lowered or "name or service" in lowered:
        return "could not reach GitHub"
    if len(text) > 80:
        return text[:77] + "..."
    return text


def format_update_feedback(
    current_version: str,
    available: bool,
    latest: LatestRelease | None,
    error: str | None,
) -> tuple[str, str]:
    """Return (kind, message). kind is 'available', 'latest', or 'error'."""
    if error:
        return "error", f"Couldn't check for updates: {short_update_error(error)}"
    if available and latest is not None:
        return "available", f"TinyFix v{latest.version} is available. Update now?"
    return "latest", f"You're on the latest version (v{current_version})."
