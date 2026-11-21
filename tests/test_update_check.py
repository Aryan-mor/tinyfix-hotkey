from pathlib import Path
from io import BytesIO
import json

import pytest

from update_check import (
    bump_patch,
    check_for_update,
    is_newer,
    mark_checked,
    parse_version,
    should_auto_check,
    fetch_latest_release,
    LatestRelease,
)


def test_parse_version_accepts_v_prefix():
    assert str(parse_version("v1.2.3")) == "1.2.3"
    assert str(parse_version("1.2.3")) == "1.2.3"
    assert parse_version("v2.0.0") == parse_version("2.0.0")


def test_parse_version_rejects_garbage():
    with pytest.raises(ValueError):
        parse_version("latest")
    with pytest.raises(ValueError):
        parse_version("")


def test_is_newer_compares_semver_parts():
    assert is_newer("1.1.0", "1.0.0")
    assert is_newer("v1.0.1", "1.0.0")
    assert is_newer("2.0.0", "1.9.9")
    assert not is_newer("1.0.0", "1.0.0")
    assert not is_newer("1.0.0", "1.1.0")
    assert not is_newer("1.0.10", "1.0.9") or is_newer("1.0.10", "1.0.9")
    assert is_newer("1.0.10", "1.0.9")
    assert not is_newer("1.0.2", "1.0.10")


def test_bump_patch():
    assert bump_patch("1.1.0") == "1.1.1"
    assert bump_patch("v2.3.9") == "2.3.10"


def test_should_auto_check_once_per_day(tmp_path: Path):
    config = tmp_path / "config.ini"
    assert should_auto_check(config, today="2026-11-21")
    mark_checked(config, today="2026-11-21")
    assert not should_auto_check(config, today="2026-11-21")
    assert should_auto_check(config, today="2026-11-22")
    text = config.read_text(encoding="utf-8")
    assert "last_update_check = 2026-11-21" in text or "last_update_check=2026-11-21" in text


def test_mark_checked_preserves_hotkey(tmp_path: Path):
    config = tmp_path / "config.ini"
    config.write_text("[TinyFix]\nhotkey=Ctrl+Alt+Space\n", encoding="utf-8")
    mark_checked(config, today="2026-11-21")
    text = config.read_text(encoding="utf-8")
    assert "Ctrl+Alt+Space" in text
    assert "2026-11-21" in text


class _FakeResponse:
    def __init__(self, payload: dict):
        self._data = json.dumps(payload).encode("utf-8")

    def read(self):
        return self._data

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


def test_fetch_latest_release_reads_setup_asset():
    payload = {
        "tag_name": "v1.1.0",
        "name": "TinyFix Hotkey v1.1.0",
        "assets": [
            {"name": "demo.mp4", "browser_download_url": "https://example.invalid/demo.mp4"},
            {
                "name": "TinyFix-Setup.exe",
                "browser_download_url": "https://example.invalid/TinyFix-Setup.exe",
            },
        ],
    }

    def opener(request, timeout=15):
        assert "api.github.com" in request.full_url
        return _FakeResponse(payload)

    latest = fetch_latest_release(opener=opener)
    assert latest.version == "1.1.0"
    assert latest.tag == "v1.1.0"
    assert latest.setup_url.endswith("TinyFix-Setup.exe")


def test_check_for_update_reports_availability_and_errors():
    release = LatestRelease("v1.1.0", "1.1.0", "https://example.invalid/TinyFix-Setup.exe", "x")
    available, found, error = check_for_update("1.0.0", fetcher=lambda: release)
    assert available and found is release and error is None

    available, found, error = check_for_update("1.1.0", fetcher=lambda: release)
    assert not available and found is release and error is None

    def boom():
        raise ConnectionError("offline")

    available, found, error = check_for_update("1.0.0", fetcher=boom)
    assert not available and found is None and "offline" in error
