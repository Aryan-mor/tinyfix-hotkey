"""TinyFix background helper: select text, press F3, the selection is corrected in place."""

from __future__ import annotations

import atexit
import ctypes
import logging
import os
import sys
import threading
import time
import traceback
from ctypes import wintypes
from pathlib import Path

ROOT = Path(__file__).resolve().parent
os.environ.setdefault("PIP_CACHE_DIR", str(ROOT / "cache" / "pip"))
os.environ.setdefault("HF_HOME", str(ROOT / "cache" / "hf"))
os.environ.setdefault("HUGGINGFACE_HUB_CACHE", str(ROOT / "cache" / "hf"))
os.environ.setdefault("TMP", str(ROOT / "cache" / "tmp"))
os.environ.setdefault("TEMP", str(ROOT / "cache" / "tmp"))
os.environ.setdefault("PYTHONPYCACHEPREFIX", str(ROOT / "cache" / "pycache"))

LOG_PATH = ROOT / "tinyfix.log"
PID_PATH = ROOT / "tinyfix.pid"
MODELS = ROOT / "models"
MUTEX_NAME = "Local\\TinyFixF3Aryan"
HOTKEY_ID = 1
VK_F3 = 0x72
MOD_NOREPEAT = 0x4000
WM_HOTKEY = 0x0312
WM_QUIT = 0x0012
HWND_MESSAGE = -3
VK_CONTROL = 0x11
VK_C = 0x43
VK_V = 0x56
INPUT_KEYBOARD = 1
KEYEVENTF_KEYUP = 0x0002
CF_UNICODETEXT = 13
CF_BITMAP = 2
CF_METAFILEPICT = 3
CF_ENHMETAFILE = 14
GMEM_MOVEABLE = 0x0002
GMEM_ZEROINIT = 0x0040
GHND = GMEM_MOVEABLE | GMEM_ZEROINIT

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

user32.OpenClipboard.argtypes = [wintypes.HWND]
user32.OpenClipboard.restype = wintypes.BOOL
user32.CloseClipboard.argtypes = []
user32.CloseClipboard.restype = wintypes.BOOL
user32.EmptyClipboard.argtypes = []
user32.EmptyClipboard.restype = wintypes.BOOL
user32.EnumClipboardFormats.argtypes = [wintypes.UINT]
user32.EnumClipboardFormats.restype = wintypes.UINT
user32.GetClipboardData.argtypes = [wintypes.UINT]
user32.GetClipboardData.restype = wintypes.HANDLE
user32.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
user32.SetClipboardData.restype = wintypes.HANDLE
user32.GetClipboardSequenceNumber.argtypes = []
user32.GetClipboardSequenceNumber.restype = wintypes.DWORD
user32.IsClipboardFormatAvailable.argtypes = [wintypes.UINT]
user32.IsClipboardFormatAvailable.restype = wintypes.BOOL

kernel32.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
kernel32.GlobalAlloc.restype = wintypes.HGLOBAL
kernel32.GlobalLock.argtypes = [wintypes.HGLOBAL]
kernel32.GlobalLock.restype = wintypes.LPVOID
kernel32.GlobalUnlock.argtypes = [wintypes.HGLOBAL]
kernel32.GlobalUnlock.restype = wintypes.BOOL
kernel32.GlobalSize.argtypes = [wintypes.HGLOBAL]
kernel32.GlobalSize.restype = ctypes.c_size_t
kernel32.GlobalFree.argtypes = [wintypes.HGLOBAL]
kernel32.GlobalFree.restype = wintypes.HGLOBAL
kernel32.CreateMutexW.argtypes = [wintypes.LPVOID, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.CreateMutexW.restype = wintypes.HANDLE
kernel32.GetLastError.restype = wintypes.DWORD

ULONG_PTR = ctypes.c_size_t


class KEYBDINPUT(ctypes.Structure):
    _fields_ = (
        ("wVk", wintypes.WORD),
        ("wScan", wintypes.WORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ULONG_PTR),
    )


class MOUSEINPUT(ctypes.Structure):
    _fields_ = (
        ("dx", wintypes.LONG),
        ("dy", wintypes.LONG),
        ("mouseData", wintypes.DWORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ULONG_PTR),
    )


class INPUT_UNION(ctypes.Union):
    _fields_ = (("ki", KEYBDINPUT), ("mi", MOUSEINPUT))


class INPUT(ctypes.Structure):
    _fields_ = (("type", wintypes.DWORD), ("union", INPUT_UNION))


user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
user32.SendInput.restype = wintypes.UINT
user32.RegisterHotKey.argtypes = [wintypes.HWND, ctypes.c_int, wintypes.UINT, wintypes.UINT]
user32.RegisterHotKey.restype = wintypes.BOOL
user32.UnregisterHotKey.argtypes = [wintypes.HWND, ctypes.c_int]
user32.UnregisterHotKey.restype = wintypes.BOOL
user32.PostThreadMessageW.argtypes = [wintypes.DWORD, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.PostThreadMessageW.restype = wintypes.BOOL
user32.GetMessageW.argtypes = [ctypes.c_void_p, wintypes.HWND, wintypes.UINT, wintypes.UINT]
user32.TranslateMessage.argtypes = [ctypes.c_void_p]
user32.DispatchMessageW.argtypes = [ctypes.c_void_p]


class MSG(ctypes.Structure):
    _fields_ = (
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", wintypes.POINT),
    )


ERROR_ALREADY_EXISTS = 183
SKIP_CLIP_FORMATS = {CF_BITMAP, CF_METAFILEPICT, CF_ENHMETAFILE}

engine = None
mutex_handle = None
hotkey_thread_id = None
hotkey_thread = None
busy = threading.Lock()
stop_event = threading.Event()
tray_icon = None


def setup_logging() -> None:
    LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    logging.basicConfig(
        filename=str(LOG_PATH),
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
        encoding="utf-8",
    )
    logging.getLogger().addHandler(logging.StreamHandler(sys.stderr)) if sys.stderr else None


def write_pid() -> None:
    PID_PATH.write_text(str(os.getpid()), encoding="utf-8")


def clear_pid() -> None:
    try:
        if PID_PATH.exists() and PID_PATH.read_text(encoding="utf-8").strip() == str(os.getpid()):
            PID_PATH.unlink()
    except OSError:
        pass


def acquire_mutex() -> bool:
    global mutex_handle
    mutex_handle = kernel32.CreateMutexW(None, True, MUTEX_NAME)
    if kernel32.GetLastError() == ERROR_ALREADY_EXISTS:
        return False
    return mutex_handle is not None


def _open_clipboard(retries: int = 8, delay: float = 0.02) -> bool:
    for _ in range(retries):
        if user32.OpenClipboard(None):
            return True
        time.sleep(delay)
    return False


def snapshot_clipboard() -> dict[int, bytes] | None:
    if not _open_clipboard():
        return None
    try:
        data: dict[int, bytes] = {}
        fmt = 0
        while True:
            fmt = user32.EnumClipboardFormats(fmt)
            if fmt == 0:
                break
            if fmt in SKIP_CLIP_FORMATS:
                continue
            handle = user32.GetClipboardData(fmt)
            if not handle:
                continue
            size = kernel32.GlobalSize(handle)
            if not size or size > 32 * 1024 * 1024:
                continue
            ptr = kernel32.GlobalLock(handle)
            if not ptr:
                continue
            try:
                data[fmt] = ctypes.string_at(ptr, size)
            finally:
                kernel32.GlobalUnlock(handle)
        return data
    finally:
        user32.CloseClipboard()


def restore_clipboard(data: dict[int, bytes] | None) -> None:
    if data is None:
        return
    if not _open_clipboard():
        return
    try:
        user32.EmptyClipboard()
        for fmt, blob in data.items():
            handle = kernel32.GlobalAlloc(GHND, len(blob))
            if not handle:
                continue
            ptr = kernel32.GlobalLock(handle)
            if not ptr:
                kernel32.GlobalFree(handle)
                continue
            ctypes.memmove(ptr, blob, len(blob))
            kernel32.GlobalUnlock(handle)
            if not user32.SetClipboardData(fmt, handle):
                kernel32.GlobalFree(handle)
    finally:
        user32.CloseClipboard()


def read_unicode_text() -> str | None:
    if not _open_clipboard():
        return None
    try:
        if not user32.IsClipboardFormatAvailable(CF_UNICODETEXT):
            return None
        handle = user32.GetClipboardData(CF_UNICODETEXT)
        if not handle:
            return None
        ptr = kernel32.GlobalLock(handle)
        if not ptr:
            return None
        try:
            return ctypes.wstring_at(ptr)
        finally:
            kernel32.GlobalUnlock(handle)
    finally:
        user32.CloseClipboard()


def write_unicode_text(text: str) -> bool:
    payload = text.encode("utf-16-le") + b"\x00\x00"
    if not _open_clipboard():
        return False
    try:
        user32.EmptyClipboard()
        handle = kernel32.GlobalAlloc(GHND, len(payload))
        if not handle:
            return False
        ptr = kernel32.GlobalLock(handle)
        if not ptr:
            kernel32.GlobalFree(handle)
            return False
        ctypes.memmove(ptr, payload, len(payload))
        kernel32.GlobalUnlock(handle)
        if not user32.SetClipboardData(CF_UNICODETEXT, handle):
            kernel32.GlobalFree(handle)
            return False
        return True
    finally:
        user32.CloseClipboard()


def _key_event(vk: int, up: bool = False) -> INPUT:
    event = INPUT()
    event.type = INPUT_KEYBOARD
    event.union.ki = KEYBDINPUT(vk, 0, KEYEVENTF_KEYUP if up else 0, 0, 0)
    return event


def send_ctrl_key(vk: int) -> None:
    events = (_key_event(VK_CONTROL), _key_event(vk), _key_event(vk, True), _key_event(VK_CONTROL, True))
    array = (INPUT * 4)(*events)
    sent = user32.SendInput(4, array, ctypes.sizeof(INPUT))
    if sent != 4:
        logging.warning("SendInput sent %s of 4 events (err=%s)", sent, ctypes.get_last_error())


def wait_for_clipboard_change(previous: int, timeout: float = 0.4) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if user32.GetClipboardSequenceNumber() != previous:
            time.sleep(0.02)
            return True
        time.sleep(0.01)
    return False


def on_hotkey() -> None:
    if not busy.acquire(blocking=False):
        return
    try:
        if engine is None:
            return
        previous_seq = user32.GetClipboardSequenceNumber()
        saved = snapshot_clipboard()
        if saved is None:
            logging.warning("Could not snapshot clipboard; aborting F3")
            return
        send_ctrl_key(VK_C)
        if not wait_for_clipboard_change(previous_seq):
            restore_clipboard(saved)
            return
        selected = read_unicode_text()
        if selected is None or selected == "":
            restore_clipboard(saved)
            return
        try:
            corrected = engine.fix(selected)
        except Exception:
            logging.exception("Correction failed")
            restore_clipboard(saved)
            return
        if corrected == selected:
            restore_clipboard(saved)
            return
        if not write_unicode_text(corrected):
            restore_clipboard(saved)
            return
        send_ctrl_key(VK_V)
        time.sleep(0.12)
        restore_clipboard(saved)
        logging.info("Corrected %s character(s)", len(selected))
    finally:
        busy.release()


def hotkey_loop() -> None:
    global hotkey_thread_id
    hotkey_thread_id = kernel32.GetCurrentThreadId()
    if not user32.RegisterHotKey(None, HOTKEY_ID, MOD_NOREPEAT, VK_F3):
        logging.error("Could not register F3 (err=%s). Another app may own this hotkey.", ctypes.get_last_error())
        return
    logging.info("F3 hotkey registered")
    msg = MSG()
    while not stop_event.is_set():
        result = user32.GetMessageW(ctypes.byref(msg), None, 0, 0)
        if result == 0 or result == -1:
            break
        if msg.message == WM_HOTKEY and msg.wParam == HOTKEY_ID:
            try:
                on_hotkey()
            except Exception:
                logging.exception("Hotkey handler crashed")
        else:
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))
    user32.UnregisterHotKey(None, HOTKEY_ID)
    logging.info("F3 hotkey unregistered")


def request_stop() -> None:
    stop_event.set()
    if hotkey_thread_id:
        user32.PostThreadMessageW(hotkey_thread_id, WM_QUIT, 0, 0)
    if tray_icon is not None:
        try:
            tray_icon.stop()
        except Exception:
            pass


def make_icon():
    from PIL import Image, ImageDraw

    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((2, 2, 61, 61), radius=14, fill=(18, 92, 68, 255))
    draw.line((16, 34, 28, 46), fill="white", width=5)
    draw.line((28, 46, 48, 18), fill="white", width=5)
    return image


def setup_tray() -> None:
    global tray_icon
    import pystray

    menu = pystray.Menu(
        pystray.MenuItem("TinyFix is running (F3)", None, enabled=False),
        pystray.MenuItem("Exit", lambda *_: request_stop()),
    )
    tray_icon = pystray.Icon("TinyFix", make_icon(), "TinyFix — select text, press F3", menu)
    tray_icon.run()


def main() -> int:
    (ROOT / "cache" / "tmp").mkdir(parents=True, exist_ok=True)
    setup_logging()
    if not acquire_mutex():
        logging.info("TinyFix is already running")
        return 0
    write_pid()
    atexit.register(clear_pid)
    try:
        global engine, hotkey_thread
        from tinyfix_lib import load_tinyfix

        logging.info("Loading tinyfix model from %s", MODELS)
        engine = load_tinyfix(MODELS, mode="balanced")
        logging.info("Model loaded")
        hotkey_thread = threading.Thread(target=hotkey_loop, name="tinyfix-hotkey", daemon=True)
        hotkey_thread.start()
        setup_tray()
        request_stop()
        if hotkey_thread.is_alive():
            hotkey_thread.join(timeout=2)
        logging.info("TinyFix exited")
        return 0
    except Exception:
        logging.error("Startup failed:\n%s", traceback.format_exc())
        return 1
    finally:
        clear_pid()


if __name__ == "__main__":
    raise SystemExit(main())
