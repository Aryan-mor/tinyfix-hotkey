import sys
import threading
import time

import pytest

from skills import default_skills

pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="Win32 popup menu")


def test_skill_menu_stays_open_until_dismissed():
    """A previous bug made TrackPopupMenu return 0 in the same millisecond."""
    import ctypes
    from skill_menu import show_skill_menu

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    VK_ESCAPE = 0x1B
    KEYEVENTF_KEYUP = 0x0002

    def dismiss() -> None:
        time.sleep(0.4)
        user32.keybd_event(VK_ESCAPE, 0, 0, 0)
        user32.keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, 0)

    thread = threading.Thread(target=dismiss, daemon=True)
    thread.start()
    choice = show_skill_menu(
        default_skills(),
        ai_ready=False,
        position=(300, 300),
        wait_for_keys=False,
    )
    thread.join(timeout=2)
    assert choice.elapsed_ms >= 250, (
        f"skill menu closed too quickly ({choice.elapsed_ms:.0f}ms, result={choice.native_result}); "
        "the popup owner window is probably wrong"
    )
    assert choice.cancelled


def _signed_hwnd(value) -> int:
    n = int(value.value) if hasattr(value, "value") else int(value)
    if n >= 2**63:
        n -= 2**64
    return n


def _patch_foreground(monkeypatch, skill_menu, *, setwindowpos=None, setforeground=None, attach=None):
    monkeypatch.setattr(skill_menu.user32, "GetForegroundWindow", lambda: 1)
    monkeypatch.setattr(skill_menu.user32, "GetWindowThreadProcessId", lambda *a: 1)
    monkeypatch.setattr(skill_menu.user32, "AttachThreadInput", attach or (lambda *a: 1))
    monkeypatch.setattr(skill_menu.user32, "BringWindowToTop", lambda *a: True)
    monkeypatch.setattr(skill_menu.user32, "SetForegroundWindow", setforeground or (lambda *a: True))
    if setwindowpos is not None:
        monkeypatch.setattr(skill_menu.user32, "SetWindowPos", setwindowpos)


def test_restore_foreground_does_not_set_topmost(monkeypatch):
    import skill_menu

    inserts = []

    def fake_setwindowpos(hwnd, insert_after, *args):
        inserts.append(_signed_hwnd(insert_after))
        return True

    _patch_foreground(monkeypatch, skill_menu, setwindowpos=fake_setwindowpos)
    monkeypatch.setattr(skill_menu, "_is_our_window", lambda hwnd: False)
    skill_menu.restore_foreground(12345)
    assert skill_menu.HWND_TOPMOST not in inserts


def test_force_foreground_owner_clears_topmost(monkeypatch):
    import skill_menu

    inserts = []

    def fake_setwindowpos(hwnd, insert_after, *args):
        inserts.append(_signed_hwnd(insert_after))
        return True

    _patch_foreground(monkeypatch, skill_menu, setwindowpos=fake_setwindowpos)
    monkeypatch.setattr(skill_menu, "_is_our_window", lambda hwnd: True)
    assert skill_menu._force_foreground(99, allow_topmost=True) is True
    assert skill_menu.HWND_TOPMOST in inserts
    assert skill_menu.HWND_NOTOPMOST in inserts
    assert inserts.index(skill_menu.HWND_NOTOPMOST) > inserts.index(skill_menu.HWND_TOPMOST)


def test_force_foreground_refuses_topmost_for_foreign_window(monkeypatch):
    import skill_menu

    inserts = []

    def fake_setwindowpos(hwnd, insert_after, *args):
        inserts.append(_signed_hwnd(insert_after))
        return True

    _patch_foreground(monkeypatch, skill_menu, setwindowpos=fake_setwindowpos)
    monkeypatch.setattr(skill_menu, "_is_our_window", lambda hwnd: False)
    assert skill_menu._force_foreground(12345, allow_topmost=True) is True
    assert skill_menu.HWND_TOPMOST not in inserts


def test_force_foreground_clears_topmost_and_detaches_on_error(monkeypatch):
    import skill_menu

    inserts = []
    attach_calls = []

    def fake_setwindowpos(hwnd, insert_after, *args):
        inserts.append(_signed_hwnd(insert_after))
        return True

    def fake_attach(src, dest, attach):
        attach_calls.append(bool(attach))
        return 1

    def boom(_hwnd):
        raise OSError("foreground failed")

    _patch_foreground(
        monkeypatch,
        skill_menu,
        setwindowpos=fake_setwindowpos,
        setforeground=boom,
        attach=fake_attach,
    )
    monkeypatch.setattr(skill_menu, "_is_our_window", lambda hwnd: True)
    monkeypatch.setattr(skill_menu.kernel32, "GetCurrentThreadId", lambda: 99)
    try:
        skill_menu._force_foreground(99, allow_topmost=True)
    except OSError:
        pass
    else:
        raise AssertionError("expected SetForegroundWindow to raise")
    assert skill_menu.HWND_TOPMOST in inserts
    assert skill_menu.HWND_NOTOPMOST in inserts
    assert attach_calls == [True, False]


def test_skill_menu_owner_is_shown_without_activation():
    import inspect
    import skill_menu

    source = inspect.getsource(skill_menu.show_skill_menu)
    assert "SW_SHOWNOACTIVATE" in source
    assert "SW_SHOW)" not in source.replace("SW_SHOWNOACTIVATE", "")
    assert "HWND_NOTOPMOST" in source
