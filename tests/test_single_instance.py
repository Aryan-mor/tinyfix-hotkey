"""Starting TinyFix a second time must open Settings in the running copy, never hang or start a duplicate."""

from __future__ import annotations

import sys
import threading
import time
import uuid
from pathlib import Path

import pytest

pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="Windows named events")

ROOT = Path(__file__).resolve().parents[1]


@pytest.fixture
def app_module(monkeypatch):
    import app

    monkeypatch.setattr(app, "OPEN_EVENT_NAME", "Local" + chr(92) + f"TinyFixTest{uuid.uuid4().hex}")
    monkeypatch.setattr(app, "open_event_handle", None)
    yield app
    if app.open_event_handle:
        app.kernel32.CloseHandle(app.open_event_handle)
    app.stop_event.clear()


def test_second_launch_without_a_running_copy_reports_false(app_module):
    assert app_module.signal_running_instance() is False


def test_second_launch_signals_the_running_copy(app_module):
    assert app_module.create_open_event()
    assert app_module.signal_running_instance() is True
    assert app_module.kernel32.WaitForSingleObject(app_module.open_event_handle, 1000) == 0  # signaled
    assert app_module.kernel32.WaitForSingleObject(app_module.open_event_handle, 0) != 0  # auto-reset: one launch, one open


def test_a_signal_sent_before_the_listener_starts_is_not_lost(app_module, monkeypatch):
    opened = []
    monkeypatch.setattr(app_module, "queue_ui", lambda action, payload=None: opened.append(action))
    app_module.create_open_event()
    app_module.signal_running_instance()  # the running copy is still loading its engine
    thread = threading.Thread(target=app_module.open_event_loop, daemon=True)
    thread.start()
    end = time.time() + 5
    while not opened and time.time() < end:
        time.sleep(0.02)
    assert opened == ["open-settings"]
    app_module.stop_event.set()
    thread.join(3)
    assert not thread.is_alive(), "the listener must stop when TinyFix exits"


def test_each_launch_opens_settings_once(app_module, monkeypatch):
    opened = []
    monkeypatch.setattr(app_module, "queue_ui", lambda action, payload=None: opened.append(action))
    app_module.create_open_event()
    thread = threading.Thread(target=app_module.open_event_loop, daemon=True)
    thread.start()
    for _ in range(3):
        app_module.signal_running_instance()
        end = time.time() + 3
        target = len(opened) + 1
        while len(opened) < target and time.time() < end:
            time.sleep(0.02)
    assert opened == ["open-settings"] * 3
    app_module.stop_event.set()
    thread.join(3)


def test_listener_without_an_event_returns_instead_of_spinning(app_module):
    thread = threading.Thread(target=app_module.open_event_loop, daemon=True)
    thread.start()
    thread.join(2)
    assert not thread.is_alive()


def test_main_signals_the_running_copy_and_exits_quietly():
    text = (ROOT / "app.py").read_text(encoding="utf-8")
    start = text.index("    if not acquire_mutex():")
    body = text[start : start + 400]
    assert "signal_running_instance()" in body and "return 0" in body
    assert "create_open_event()" in text


def test_tray_click_opens_settings_and_fix_clipboard_stays_in_the_menu():
    text = (ROOT / "app.py").read_text(encoding="utf-8")
    menu = text[text.index("def setup_tray"): text.index("tray_icon = tray.TrayIcon")]
    assert menu.count("default=True") == 1
    assert 'tray.MenuItem("Settings…", open_settings_window, default=True)' in menu
    assert 'tray.MenuItem("Fix clipboard", on_fix_clipboard)' in menu
    assert "Click to open Settings" in text
