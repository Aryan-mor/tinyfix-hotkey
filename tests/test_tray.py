"""Tray tests: struct layout, lifecycle, and the "never hang the message loop" rules."""

from __future__ import annotations

import ctypes
import sys
import threading
import time

import pytest

import tray

pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="Windows tray")


def _wait(predicate, timeout=5.0):
    end = time.time() + timeout
    while time.time() < end:
        if predicate():
            return True
        time.sleep(0.02)
    return False


class Harness:
    def __init__(self, items=None, setup=None):
        self.icon = tray.TrayIcon("TinyFixTest", None, "tip " * 60, items or [tray.MenuItem("x", lambda *_: None)])
        self.thread = threading.Thread(target=lambda: self.icon.run(setup=setup), daemon=True)

    def __enter__(self):
        self.thread.start()
        assert self.icon._ready.wait(20), "tray window never became ready"
        return self.icon

    def __exit__(self, *exc):
        self.icon.stop()
        self.thread.join(5)
        assert not self.thread.is_alive(), "tray message loop did not stop"


def test_notifyicondata_matches_win64_layout():
    # Shell_NotifyIconW rejects or misreads the wrong cbSize; this is the documented x64 size.
    expected = 976 if ctypes.sizeof(ctypes.c_void_p) == 8 else 956
    assert ctypes.sizeof(tray.NOTIFYICONDATAW) == expected
    assert tray.NOTIFYICONDATAW.szTip.size == 256  # 128 WCHAR


def test_lifecycle_adds_and_removes_icon_and_survives_long_tip():
    with Harness() as icon:
        assert icon.visible
        icon.set_title("t" * 500)  # longer than szTip: must be clipped, not crash
        icon.notify("m" * 1000, "T" * 200)
    assert not icon.visible
    assert icon.hwnd is None


def test_setup_callback_runs_off_the_tray_thread():
    seen = {}
    done = threading.Event()

    def setup(icon):
        seen["thread"] = threading.current_thread().name
        done.set()

    with Harness(setup=setup):
        assert done.wait(5)
    assert seen["thread"] == "tinyfix-tray-setup"


def test_actions_run_on_worker_threads_and_a_slow_action_does_not_block_the_loop():
    started = threading.Event()
    release = threading.Event()
    names = []

    def slow(*_):
        names.append(threading.current_thread().name)
        started.set()
        release.wait(10)

    item = tray.MenuItem("slow", slow, default=True)
    with Harness(items=[item]) as icon:
        user32 = ctypes.WinDLL("user32")
        user32.PostMessageW(icon.hwnd, tray.WM_TRAY, 0, tray.WM_LBUTTONUP)  # left click = default item
        assert started.wait(5), "default action did not run"
        assert names == ["tinyfix-tray-action"]
        # The tray thread must still answer messages while the action is blocked.
        t0 = time.time()
        icon.notify("still alive", "TinyFix")
        icon.set_title("responsive")
        assert time.time() - t0 < 1.0
        release.set()


def test_exception_in_action_does_not_kill_the_loop():
    ran = threading.Event()

    def boom(*_):
        ran.set()
        raise RuntimeError("boom")

    item = tray.MenuItem("boom", boom, default=True)
    with Harness(items=[item]) as icon:
        user32 = ctypes.WinDLL("user32")
        user32.PostMessageW(icon.hwnd, tray.WM_TRAY, 0, tray.WM_LBUTTONUP)
        assert ran.wait(5)
        time.sleep(0.2)
        assert icon.visible
        icon.notify("ok", "TinyFix")


def test_icon_is_added_again_after_explorer_restart():
    with Harness() as icon:
        user32 = ctypes.WinDLL("user32")
        user32.SendMessageW.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_size_t, ctypes.c_ssize_t]
        icon._added = False  # what Explorer's restart leaves us with
        user32.SendMessageW(icon.hwnd, icon._taskbar_created, 0, 0)
        assert _wait(lambda: icon.visible)


def test_menu_builds_with_submenu_disabled_checked_and_default_items():
    calls = []
    sub = [tray.MenuItem("A", lambda *_: calls.append("A"), checked=lambda *_: True), tray.MenuItem("B", lambda *_: None)]
    items = [
        tray.MenuItem("Default", lambda *_: None, default=True),
        tray.MenuItem(lambda _i: "dynamic text", None, enabled=False),
        tray.SEPARATOR,
        tray.MenuItem("Sub", submenu=sub),
        tray.MenuItem("Exit", lambda *_: None),
    ]
    icon = tray.TrayIcon("t", None, "tip", items)
    handles = []
    menu = icon._build_menu(items, handles)
    try:
        assert menu
        assert len(handles) == 2  # root + one submenu
        assert len(icon._actions) == 5  # every non-separator, non-submenu entry (the disabled one is grayed, not skipped)
        assert icon.items[1].label() == "dynamic text"
        assert not icon.items[1].is_enabled()
        assert sub[0].is_checked() and not sub[1].is_checked()
    finally:
        ctypes.WinDLL("user32").DestroyMenu(handles[0])


def test_checked_callback_failure_is_treated_as_unchecked():
    item = tray.MenuItem("x", lambda *_: None, checked=lambda *_: 1 / 0)
    assert item.is_checked() is False


def test_stop_before_run_and_double_stop_are_safe():
    icon = tray.TrayIcon("t", None, "tip", [])
    icon.stop()
    icon.stop()
    with Harness() as live:
        live.stop()
        live.stop()


# ------------------------------------------------------------------ the "working" animation


ASSETS = __import__("pathlib").Path(__file__).resolve().parents[1] / "assets"
WORKING = [ASSETS / f"tinyfix-working-{n}.ico" for n in (1, 2, 3)]


class WorkHarness(Harness):
    def __init__(self, paths):
        self.icon = tray.TrayIcon("TinyFixTest", None, "tip", [tray.MenuItem("x", lambda *_: None)], working_icon_paths=paths)
        self.thread = threading.Thread(target=lambda: self.icon.run(), daemon=True)


def test_working_icon_files_exist_and_have_the_tray_size():
    from PIL import Image

    for path in WORKING:
        assert path.exists(), path
        with Image.open(path) as image:
            assert (16, 16) in image.info.get("sizes", {(16, 16)}) or image.size[0] >= 16


def test_working_icon_appears_animates_and_goes_back():
    with WorkHarness(WORKING) as icon:
        assert not icon.working_shown
        icon.set_busy(True)
        assert _wait(lambda: icon.working_shown, 2), "the working icon never appeared"
        frames = set()
        end = time.time() + 2
        while time.time() < end and len(frames) < 3:
            frames.add(icon.working_frame)
            time.sleep(0.05)
        assert frames == {0, 1, 2}, f"the dots did not move through all frames: {frames}"
        icon.set_busy(False)
        assert _wait(lambda: not icon.working_shown, 2), "the normal icon did not come back"
        assert icon.visible  # still in the tray


def test_quick_work_never_flashes_the_working_icon():
    with WorkHarness(WORKING) as icon:
        icon.set_busy(True)
        time.sleep(0.05)  # well under the 150 ms delay
        icon.set_busy(False)
        end = time.time() + 0.6
        while time.time() < end:
            assert not icon.working_shown
            time.sleep(0.02)


def test_two_jobs_in_a_row_keep_one_animation_and_one_stop_ends_it():
    with WorkHarness(WORKING) as icon:
        icon.set_busy(True)
        icon.set_busy(True)
        assert _wait(lambda: icon.working_shown, 2)
        icon.set_busy(False)
        assert _wait(lambda: not icon.working_shown, 2)
        time.sleep(0.5)
        assert not icon.working_shown  # no leftover timer brings it back


def test_missing_working_icons_do_not_break_the_tray():
    with WorkHarness([ASSETS / "does-not-exist.ico"]) as icon:
        icon.set_busy(True)
        time.sleep(0.5)
        assert not icon.working_shown and icon.visible
        icon.set_busy(False)
        icon.notify("still fine", "TinyFix")


def test_stopping_while_working_exits_cleanly_and_set_busy_is_always_safe():
    icon = tray.TrayIcon("t", None, "tip", [], working_icon_paths=WORKING)
    icon.set_busy(True)  # before the window exists
    icon.set_busy(False)
    with WorkHarness(WORKING) as live:
        live.set_busy(True)
        assert _wait(lambda: live.working_shown, 2)
    # leaving the context stopped the tray while the animation was running: it must have exited without hanging
    assert live.hwnd is None and not live.working_shown
    live.set_busy(True)  # after stop: no crash
