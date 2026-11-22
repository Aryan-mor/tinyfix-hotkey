"""The app marks itself as working whenever it holds the work lock, so the tray icon can show it."""

from __future__ import annotations

from pathlib import Path

import pytest

import app

ROOT = Path(__file__).resolve().parents[1]


class FakeIcon:
    def __init__(self, fail=False):
        self.calls = []
        self.fail = fail

    def set_busy(self, flag):
        if self.fail:
            raise RuntimeError("tray is gone")
        self.calls.append(flag)


@pytest.fixture
def fake_icon(monkeypatch):
    icon = FakeIcon()
    monkeypatch.setattr(app, "tray_icon", icon)
    return icon


def test_holding_the_work_lock_shows_working_and_releasing_hides_it(fake_icon):
    lock = app.WorkLock()
    assert lock.acquire(blocking=False)
    assert lock.locked() and fake_icon.calls == [True]
    lock.release()
    assert not lock.locked() and fake_icon.calls == [True, False]


def test_a_second_job_that_cannot_start_does_not_touch_the_icon(fake_icon):
    lock = app.WorkLock()
    assert lock.acquire(blocking=False)
    assert lock.acquire(blocking=False) is False
    assert fake_icon.calls == [True]  # still one job; the refused one changed nothing
    lock.release()


def test_a_broken_tray_never_breaks_the_work_lock(monkeypatch):
    monkeypatch.setattr(app, "tray_icon", FakeIcon(fail=True))
    lock = app.WorkLock()
    assert lock.acquire(blocking=False)
    lock.release()
    assert not lock.locked()
    monkeypatch.setattr(app, "tray_icon", None)  # no tray yet (startup) is fine too
    assert lock.acquire(blocking=False)
    lock.release()


def test_every_job_path_uses_the_work_lock():
    text = (ROOT / "app.py").read_text(encoding="utf-8")
    assert "busy = WorkLock()" in text
    assert text.count("busy.acquire(blocking=False)") >= 4  # skills, clipboard, and the two older fix/translate paths
    assert text.count("busy.release()") >= 4


def test_the_tray_is_given_the_working_icons_and_they_ship_in_the_exe():
    paths = app.working_icon_paths()
    assert [p.name for p in paths] == ["tinyfix-working-1.ico", "tinyfix-working-2.ico", "tinyfix-working-3.ico"]
    spec = (ROOT / "tinyfix.spec").read_text(encoding="utf-8")
    assert "tinyfix-working-" in spec
    assert "working_icon_paths=working_icon_paths()" in (ROOT / "app.py").read_text(encoding="utf-8")
