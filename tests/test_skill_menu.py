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
