"""TinyFix tray app: select text, press the shortcut, the selection is corrected in place."""

from __future__ import annotations

import argparse
import atexit
import configparser
import ctypes
import logging
import os
import sys
import threading
import time
import traceback
from ctypes import wintypes
from pathlib import Path

from hotkey_util import (
    DEFAULT_HOTKEY,
    DEFAULT_HOTKEY_ALL,
    KEY_CHOICES,
    Hotkey,
    disabled_hotkey,
    parse_hotkey,
)
from tinyfix_lib import DEFAULT_SENSITIVITY, SENSITIVITY_NAMES, normalize_sensitivity, plan_clipboard_fix
from update_check import (
    LatestRelease,
    check_for_update,
    default_setup_download_path,
    download_file,
    fetch_latest_release,
    is_newer,
    mark_checked,
    should_auto_check,
    start_silent_update,
)

APP_NAME = "TinyFix"
APP_VERSION = "1.1.1"
MUTEX_NAME = "Local\\TinyFixHotkey"
CONFIG_SECTION = "TinyFix"
HOTKEY_ID = 1
HOTKEY_ALL_ID = 2
WM_HOTKEY = 0x0312
WM_QUIT = 0x0012
WM_APP_RELOAD = 0x8001
WM_APP_PAUSE = 0x8002
WM_APP_RESUME = 0x8003
VK_CONTROL = 0x11
VK_SHIFT = 0x10
VK_MENU = 0x12
VK_LWIN = 0x5B
VK_RWIN = 0x5C
VK_A = 0x41
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
ERROR_ALREADY_EXISTS = 183
NERR_SUCCESS = 0
TOKEN_QUERY = 0x0008
TokenElevation = 20
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
MODIFIER_VKS = (VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN)
DISABLED_KEY_LABEL = "None/Disabled"
KEY_CHOICES_WITH_DISABLED = (DISABLED_KEY_LABEL,) + KEY_CHOICES


def app_root() -> Path:
    """Install directory: config, log, and pid live next to the exe."""
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def resource_root() -> Path:
    """Bundled resources (models, icon). PyInstaller 6 onedir uses _internal."""
    if getattr(sys, "frozen", False):
        meipass = getattr(sys, "_MEIPASS", None)
        if meipass:
            return Path(meipass)
        internal = Path(sys.executable).resolve().parent / "_internal"
        if internal.exists():
            return internal
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


ROOT = app_root()
RESOURCES = resource_root()
os.environ.setdefault("PIP_CACHE_DIR", str(ROOT / "cache" / "pip"))
os.environ.setdefault("HF_HOME", str(ROOT / "cache" / "hf"))
os.environ.setdefault("HUGGINGFACE_HUB_CACHE", str(ROOT / "cache" / "hf"))
os.environ.setdefault("TMP", str(ROOT / "cache" / "tmp"))
os.environ.setdefault("TEMP", str(ROOT / "cache" / "tmp"))
os.environ.setdefault("PYTHONPYCACHEPREFIX", str(ROOT / "cache" / "pycache"))

LOG_PATH = ROOT / "tinyfix.log"
PID_PATH = ROOT / "tinyfix.pid"
CONFIG_PATH = ROOT / "config.ini"
MODELS = RESOURCES / "models"

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)

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
user32.GetForegroundWindow.argtypes = []
user32.GetForegroundWindow.restype = wintypes.HWND
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
user32.GetAsyncKeyState.restype = wintypes.SHORT

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
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
kernel32.GetCurrentProcess.argtypes = []
kernel32.GetCurrentProcess.restype = wintypes.HANDLE

advapi32.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
advapi32.OpenProcessToken.restype = wintypes.BOOL
advapi32.GetTokenInformation.argtypes = [
    wintypes.HANDLE,
    ctypes.c_int,
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD),
]
advapi32.GetTokenInformation.restype = wintypes.BOOL

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
    # MOUSEINPUT must be included so sizeof(INPUT) is 40 on 64-bit Windows.
    _fields_ = (("ki", KEYBDINPUT), ("mi", MOUSEINPUT))


class INPUT(ctypes.Structure):
    _fields_ = (("type", wintypes.DWORD), ("union", INPUT_UNION))


class TOKEN_ELEVATION(ctypes.Structure):
    _fields_ = (("TokenIsElevated", wintypes.DWORD),)


class MSG(ctypes.Structure):
    _fields_ = (
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", wintypes.POINT),
    )


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

SKIP_CLIP_FORMATS = {CF_BITMAP, CF_METAFILEPICT, CF_ENHMETAFILE}

engine = None
mutex_handle = None
hotkey_thread_id = None
hotkey_thread = None
busy = threading.Lock()
stop_event = threading.Event()
paused = threading.Event()
hotkey_ready = threading.Event()
hotkey_failed = threading.Event()
current_hotkey = parse_hotkey(DEFAULT_HOTKEY)
current_hotkey_all = parse_hotkey(DEFAULT_HOTKEY_ALL)
current_sensitivity = DEFAULT_SENSITIVITY
failed_hotkey_ids: set[int] = set()
tray_icon = None
ui_lock = threading.Lock()
pending_ui: list[tuple[str, object]] = []


def setup_logging() -> None:
    LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    logging.basicConfig(
        filename=str(LOG_PATH),
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
        encoding="utf-8",
    )
    stream = logging.StreamHandler(sys.stderr)
    stream.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
    logging.getLogger().addHandler(stream)


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


def config_hotkey_value(hotkey: Hotkey) -> str:
    return "none" if hotkey.is_disabled else hotkey.display


def _load_parser() -> configparser.ConfigParser:
    parser = configparser.ConfigParser()
    if CONFIG_PATH.exists():
        try:
            parser.read(CONFIG_PATH, encoding="utf-8")
        except Exception:
            logging.exception("Could not read %s", CONFIG_PATH)
    if CONFIG_SECTION not in parser:
        parser[CONFIG_SECTION] = {}
    return parser


def _write_parser(parser: configparser.ConfigParser) -> None:
    with CONFIG_PATH.open("w", encoding="utf-8") as handle:
        parser.write(handle)


def load_settings() -> None:
    global current_hotkey, current_hotkey_all, current_sensitivity
    current_hotkey = parse_hotkey(DEFAULT_HOTKEY)
    current_hotkey_all = parse_hotkey(DEFAULT_HOTKEY_ALL)
    current_sensitivity = DEFAULT_SENSITIVITY
    if not CONFIG_PATH.exists():
        return
    parser = _load_parser()
    try:
        current_hotkey = parse_hotkey(parser.get(CONFIG_SECTION, "hotkey", fallback=DEFAULT_HOTKEY))
    except Exception:
        logging.exception("Could not read fix-selection shortcut; using default %s", DEFAULT_HOTKEY)
        current_hotkey = parse_hotkey(DEFAULT_HOTKEY)
    try:
        current_hotkey_all = parse_hotkey(
            parser.get(CONFIG_SECTION, "hotkey_all", fallback=DEFAULT_HOTKEY_ALL)
        )
    except Exception:
        logging.exception("Could not read fix-all shortcut; using default %s", DEFAULT_HOTKEY_ALL)
        current_hotkey_all = parse_hotkey(DEFAULT_HOTKEY_ALL)
    current_sensitivity = normalize_sensitivity(
        parser.get(CONFIG_SECTION, "sensitivity", fallback=DEFAULT_SENSITIVITY)
    )


def save_settings(persist_hotkeys: bool = True, persist_sensitivity: bool = True) -> None:
    parser = _load_parser()
    if persist_hotkeys:
        parser[CONFIG_SECTION]["hotkey"] = config_hotkey_value(current_hotkey)
        parser[CONFIG_SECTION]["hotkey_all"] = config_hotkey_value(current_hotkey_all)
    if persist_sensitivity:
        parser[CONFIG_SECTION]["sensitivity"] = current_sensitivity
    _write_parser(parser)


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


def _modifier_down(vk: int) -> bool:
    return bool(user32.GetAsyncKeyState(vk) & 0x8000)


def wait_for_modifiers_released(timeout: float = 0.5) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if not any(_modifier_down(vk) for vk in MODIFIER_VKS):
            return True
        time.sleep(0.01)
    held = [vk for vk in MODIFIER_VKS if _modifier_down(vk)]
    if held:
        logging.info("Modifiers still held after %.0fms (vk=%s); sending key-up", timeout * 1000, held)
        ups = (_key_event(vk, True) for vk in held)
        events = tuple(ups)
        array = (INPUT * len(events))(*events)
        user32.SendInput(len(events), array, ctypes.sizeof(INPUT))
        time.sleep(0.03)
    return not any(_modifier_down(vk) for vk in MODIFIER_VKS)


def _is_process_elevated(pid: int | None = None, handle: wintypes.HANDLE | None = None) -> bool | None:
    process = handle
    opened = False
    if process is None:
        if not pid:
            return None
        process = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
        if not process:
            return None
        opened = True
    token = wintypes.HANDLE()
    try:
        if not advapi32.OpenProcessToken(process, TOKEN_QUERY, ctypes.byref(token)):
            return None
        elevation = TOKEN_ELEVATION()
        needed = wintypes.DWORD()
        if not advapi32.GetTokenInformation(
            token, TokenElevation, ctypes.byref(elevation), ctypes.sizeof(elevation), ctypes.byref(needed)
        ):
            return None
        return bool(elevation.TokenIsElevated)
    finally:
        if token:
            kernel32.CloseHandle(token)
        if opened and process:
            kernel32.CloseHandle(process)


def foreground_is_unequally_elevated() -> bool:
    hwnd = user32.GetForegroundWindow()
    if not hwnd:
        return False
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    if not pid.value or pid.value == os.getpid():
        return False
    theirs = _is_process_elevated(pid=pid.value)
    ours = _is_process_elevated(handle=kernel32.GetCurrentProcess())
    return theirs is True and ours is not True


def copy_selection(select_all: bool = False) -> tuple[str | None, dict[int, bytes] | None, str]:
    previous_seq = user32.GetClipboardSequenceNumber()
    saved = snapshot_clipboard()
    if saved is None:
        return None, None, "clipboard_snapshot_failed"
    wait_for_modifiers_released(0.5)
    if select_all:
        send_ctrl_key(VK_A)
        time.sleep(0.05)
        previous_seq = user32.GetClipboardSequenceNumber()
    send_ctrl_key(VK_C)
    if not wait_for_clipboard_change(previous_seq):
        previous_seq = user32.GetClipboardSequenceNumber()
        send_ctrl_key(VK_C)
        if not wait_for_clipboard_change(previous_seq):
            restore_clipboard(saved)
            return None, saved, "copy_failed"
    selected = read_unicode_text()
    if selected is None or selected == "":
        restore_clipboard(saved)
        return None, saved, "empty_selection"
    return selected, saved, "ok"


def on_hotkey(select_all: bool = False) -> None:
    source = "fix-all" if select_all else "fix-selection"
    if paused.is_set():
        logging.info("Hotkey ignored: paused (%s)", source)
        return
    if not busy.acquire(blocking=False):
        logging.info("Hotkey ignored: busy (%s)", source)
        return
    try:
        if engine is None:
            logging.info("Hotkey ignored: engine not ready (%s)", source)
            return
        if foreground_is_unequally_elevated():
            logging.info("Hotkey ignored: target window is elevated (%s)", source)
            notify("TinyFix", "Can't reach an elevated window")
            return
        selected, saved, reason = copy_selection(select_all=select_all)
        if reason != "ok":
            length = 0 if selected is None else len(selected)
            logging.info("Hotkey early return: %s (length=%s, %s)", reason, length, source)
            if reason in ("copy_failed", "empty_selection"):
                notify("TinyFix", "Nothing selected / could not copy the selection")
            elif reason == "clipboard_snapshot_failed":
                notify("TinyFix", "Could not read the clipboard")
            return
        try:
            corrected = engine.fix(selected)
        except Exception:
            logging.exception("Correction failed")
            restore_clipboard(saved)
            return
        if corrected == selected:
            restore_clipboard(saved)
            logging.info("Hotkey early return: no typos found (length=%s, %s)", len(selected), source)
            notify("TinyFix", "No typos found")
            return
        if not write_unicode_text(corrected):
            restore_clipboard(saved)
            logging.info("Hotkey early return: clipboard write failed (length=%s, %s)", len(selected), source)
            return
        send_ctrl_key(VK_V)
        time.sleep(0.12)
        restore_clipboard(saved)
        logging.info("Corrected %s character(s) (%s)", len(selected), source)
    finally:
        busy.release()


def _register_one(hotkey: Hotkey, hotkey_id: int, label: str) -> bool:
    if hotkey.is_disabled:
        logging.info("%s shortcut is disabled", label)
        failed_hotkey_ids.discard(hotkey_id)
        return True
    if not user32.RegisterHotKey(None, hotkey_id, hotkey.register_modifiers, hotkey.vk):
        logging.error(
            "Could not register %s shortcut %s (err=%s). Another app may own this shortcut.",
            label,
            hotkey.display,
            ctypes.get_last_error(),
        )
        failed_hotkey_ids.add(hotkey_id)
        return False
    failed_hotkey_ids.discard(hotkey_id)
    logging.info("%s shortcut registered: %s", label, hotkey.display)
    return True


def register_current_hotkeys() -> bool:
    failed_hotkey_ids.clear()
    ok_sel = _register_one(current_hotkey, HOTKEY_ID, "Fix-selection")
    ok_all = _register_one(current_hotkey_all, HOTKEY_ALL_ID, "Fix-all")
    return ok_sel and ok_all


def unregister_current_hotkeys() -> None:
    user32.UnregisterHotKey(None, HOTKEY_ID)
    user32.UnregisterHotKey(None, HOTKEY_ALL_ID)


def post_to_hotkey_thread(message: int) -> None:
    if hotkey_thread_id:
        user32.PostThreadMessageW(hotkey_thread_id, message, 0, 0)


def queue_ui(action: str, payload: object = None) -> None:
    with ui_lock:
        pending_ui.append((action, payload))
    if tray_icon is not None:
        try:
            tray_icon.update_menu()
        except Exception:
            pass


def notify(title: str, message: str) -> None:
    icon = tray_icon
    if icon is not None:
        try:
            icon.notify(message, title)
            return
        except Exception:
            logging.exception("Tray notification failed")
    logging.info("%s: %s", title, message)


def hotkey_loop() -> None:
    global hotkey_thread_id
    hotkey_thread_id = kernel32.GetCurrentThreadId()
    if register_current_hotkeys():
        hotkey_failed.clear()
    else:
        hotkey_failed.set()
        queue_ui("change-failed")
    hotkey_ready.set()
    msg = MSG()
    while not stop_event.is_set():
        result = user32.GetMessageW(ctypes.byref(msg), None, 0, 0)
        if result == 0 or result == -1:
            break
        if msg.message == WM_HOTKEY:
            logging.info("WM_HOTKEY received (id=%s)", int(msg.wParam))
            try:
                if msg.wParam == HOTKEY_ID:
                    on_hotkey(select_all=False)
                elif msg.wParam == HOTKEY_ALL_ID:
                    on_hotkey(select_all=True)
                else:
                    logging.info("Hotkey ignored: unknown id %s", int(msg.wParam))
            except Exception:
                logging.exception("Hotkey handler crashed")
        elif msg.message == WM_APP_PAUSE:
            unregister_current_hotkeys()
            paused.set()
            logging.info("Shortcuts paused")
        elif msg.message == WM_APP_RESUME:
            paused.clear()
            if register_current_hotkeys():
                hotkey_failed.clear()
            else:
                hotkey_failed.set()
                queue_ui("change-failed")
        elif msg.message == WM_APP_RELOAD:
            unregister_current_hotkeys()
            paused.clear()
            if register_current_hotkeys():
                hotkey_failed.clear()
                logging.info(
                    "Shortcuts updated to selection=%s all=%s",
                    config_hotkey_value(current_hotkey),
                    config_hotkey_value(current_hotkey_all),
                )
            else:
                hotkey_failed.set()
                queue_ui("change-failed")
        else:
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))
    unregister_current_hotkeys()
    logging.info("Shortcuts unregistered")


def request_stop() -> None:
    stop_event.set()
    post_to_hotkey_thread(WM_QUIT)
    if tray_icon is not None:
        try:
            tray_icon.stop()
        except Exception:
            pass


def _hotkey_status(hotkey: Hotkey, hotkey_id: int) -> str:
    if hotkey.is_disabled:
        return "off"
    if hotkey_id in failed_hotkey_ids:
        return f"{hotkey.display} unavailable"
    return hotkey.display


def status_text(_item=None) -> str:
    sel = _hotkey_status(current_hotkey, HOTKEY_ID)
    all_ = _hotkey_status(current_hotkey_all, HOTKEY_ALL_ID)
    if paused.is_set():
        return f"Paused ({sel} / {all_})"
    if failed_hotkey_ids:
        return f"Shortcut unavailable ({sel} / {all_})"
    return f"Running ({sel} / {all_})"


def pause_label(_item=None) -> str:
    return "Resume" if paused.is_set() else "Pause"


def toggle_pause(_item=None, _icon=None) -> None:
    if paused.is_set():
        post_to_hotkey_thread(WM_APP_RESUME)
    else:
        post_to_hotkey_thread(WM_APP_PAUSE)


def apply_hotkeys(selection: Hotkey, fix_all: Hotkey, persist: bool = True) -> None:
    global current_hotkey, current_hotkey_all
    current_hotkey = selection
    current_hotkey_all = fix_all
    if persist:
        try:
            save_settings(persist_hotkeys=True, persist_sensitivity=False)
        except Exception:
            logging.exception("Could not save config")
    post_to_hotkey_thread(WM_APP_RELOAD)


def apply_sensitivity_setting(name: str, persist: bool = True) -> None:
    global current_sensitivity, engine
    current_sensitivity = normalize_sensitivity(name)
    if engine is not None:
        current_sensitivity = engine.apply_sensitivity(current_sensitivity)
        logging.info("Sensitivity set to %s", current_sensitivity)
    if persist:
        try:
            save_settings(persist_hotkeys=False, persist_sensitivity=True)
        except Exception:
            logging.exception("Could not save sensitivity")
    if tray_icon is not None:
        try:
            tray_icon.update_menu()
        except Exception:
            pass


def _row_to_hotkey(ctrl: bool, alt: bool, shift: bool, win: bool, key: str) -> tuple[Hotkey | None, str]:
    if not key or key == DISABLED_KEY_LABEL or key.lower() in {"none", "disabled"}:
        return disabled_hotkey(), ""
    parts: list[str] = []
    if ctrl:
        parts.append("Ctrl")
    if alt:
        parts.append("Alt")
    if shift:
        parts.append("Shift")
    if win:
        parts.append("Win")
    parts.append(key)
    text = "+".join(parts)
    try:
        parsed = parse_hotkey(text)
    except ValueError as exc:
        return None, str(exc)
    if not parsed.is_safe():
        return parsed, "Please add a modifier (Ctrl, Alt, Shift, or Win) for this key."
    return parsed, ""


def show_hotkey_dialog(reason: str | None = None) -> None:
    import tkinter as tk
    from tkinter import ttk

    result: dict[str, tuple[Hotkey, Hotkey] | None] = {"value": None}

    root = tk.Tk()
    root.title("TinyFix — Choose your shortcuts")
    root.resizable(False, False)
    try:
        root.attributes("-topmost", True)
    except tk.TclError:
        pass
    root.geometry("+400+240")

    frame = ttk.Frame(root, padding=16)
    frame.grid(row=0, column=0, sticky="nsew")

    ttk.Label(frame, text="Choose shortcuts for fixing a selection or the whole field. You can also disable either one.").grid(
        row=0, column=0, columnspan=5, sticky="w", pady=(0, 8)
    )
    if reason:
        ttk.Label(frame, text=reason, foreground="#a40000", wraplength=420).grid(
            row=1, column=0, columnspan=5, sticky="w", pady=(0, 8)
        )

    def make_row(title: str, initial: Hotkey, row: int):
        ttk.Label(frame, text=title, font=("Segoe UI", 9, "bold")).grid(
            row=row, column=0, columnspan=5, sticky="w", pady=(8, 2)
        )
        disabled = initial.is_disabled
        ctrl_var = tk.BooleanVar(value=bool(initial.modifiers & 0x0002) and not disabled)
        alt_var = tk.BooleanVar(value=bool(initial.modifiers & 0x0001) and not disabled)
        shift_var = tk.BooleanVar(value=bool(initial.modifiers & 0x0004) and not disabled)
        win_var = tk.BooleanVar(value=bool(initial.modifiers & 0x0008) and not disabled)
        key_var = tk.StringVar(value=DISABLED_KEY_LABEL if disabled else initial.display.split("+")[-1])
        preview_var = tk.StringVar()
        error_var = tk.StringVar()

        def preview(_event=None) -> Hotkey | None:
            parsed, error = _row_to_hotkey(ctrl_var.get(), alt_var.get(), shift_var.get(), win_var.get(), key_var.get())
            if parsed is None:
                preview_var.set(key_var.get())
                error_var.set(error)
                return None
            preview_var.set("Disabled" if parsed.is_disabled else parsed.display)
            error_var.set(error)
            return parsed if not error else parsed

        ttk.Checkbutton(frame, text="Ctrl", variable=ctrl_var, command=preview).grid(row=row + 1, column=0, sticky="w")
        ttk.Checkbutton(frame, text="Alt", variable=alt_var, command=preview).grid(row=row + 1, column=1, sticky="w")
        ttk.Checkbutton(frame, text="Shift", variable=shift_var, command=preview).grid(row=row + 1, column=2, sticky="w")
        ttk.Checkbutton(frame, text="Win", variable=win_var, command=preview).grid(row=row + 1, column=3, sticky="w")
        ttk.Label(frame, text="Key:").grid(row=row + 2, column=0, sticky="w", pady=(8, 0))
        key_box = ttk.Combobox(frame, textvariable=key_var, values=KEY_CHOICES_WITH_DISABLED, state="readonly", width=16)
        key_box.grid(row=row + 2, column=1, columnspan=3, sticky="w", pady=(8, 0))
        key_box.bind("<<ComboboxSelected>>", preview)
        ttk.Label(frame, text="Shortcut:").grid(row=row + 3, column=0, sticky="w", pady=(6, 0))
        ttk.Label(frame, textvariable=preview_var, font=("Segoe UI", 11, "bold")).grid(
            row=row + 3, column=1, columnspan=3, sticky="w", pady=(6, 0)
        )
        ttk.Label(frame, textvariable=error_var, foreground="#a40000", wraplength=420).grid(
            row=row + 4, column=0, columnspan=5, sticky="w"
        )
        preview()
        return preview, error_var

    preview_sel, error_sel = make_row("Fix selection", current_hotkey, 2)
    preview_all, error_all = make_row("Fix all (select the whole field first)", current_hotkey_all, 8)
    conflict_var = tk.StringVar()
    ttk.Label(frame, textvariable=conflict_var, foreground="#a40000", wraplength=420).grid(
        row=14, column=0, columnspan=5, sticky="w", pady=(8, 0)
    )

    buttons = ttk.Frame(frame)
    buttons.grid(row=15, column=0, columnspan=5, sticky="e", pady=(16, 0))

    def on_ok() -> None:
        selection = preview_sel()
        fix_all = preview_all()
        if selection is None or fix_all is None:
            return
        if error_sel.get() or error_all.get():
            return
        if not selection.is_disabled and not fix_all.is_disabled:
            if selection.display == fix_all.display:
                conflict_var.set("Please choose two different shortcuts, or disable one of them.")
                return
        result["value"] = (selection, fix_all)
        root.destroy()

    def on_cancel() -> None:
        result["value"] = None
        root.destroy()

    ttk.Button(buttons, text="Cancel", command=on_cancel).grid(row=0, column=0, padx=(0, 8))
    ttk.Button(buttons, text="OK", command=on_ok).grid(row=0, column=1)
    root.protocol("WM_DELETE_WINDOW", on_cancel)
    root.transient()
    try:
        root.grab_set()
    except tk.TclError:
        pass
    root.focus_force()
    root.mainloop()

    chosen = result["value"]
    if chosen is not None:
        apply_hotkeys(chosen[0], chosen[1])


def change_shortcut(_item=None, _icon=None) -> None:
    show_hotkey_dialog()


def failed_shortcut_reason() -> str:
    parts: list[str] = []
    if HOTKEY_ID in failed_hotkey_ids:
        parts.append(f"{current_hotkey.display} (fix selection)")
    if HOTKEY_ALL_ID in failed_hotkey_ids:
        parts.append(f"{current_hotkey_all.display} (fix all)")
    if not parts:
        return "A shortcut is already in use by another program."
    return " and ".join(parts) + " already in use by another program."


def prompt_update(latest: LatestRelease) -> None:
    import tkinter as tk
    from tkinter import messagebox

    root = tk.Tk()
    root.withdraw()
    try:
        root.attributes("-topmost", True)
    except tk.TclError:
        pass
    answer = messagebox.askyesno(
        "TinyFix",
        f"TinyFix v{latest.version} is available. Update now?",
        parent=root,
    )
    root.destroy()
    if not answer:
        return
    try:
        dest = default_setup_download_path()
        logging.info("Downloading %s to %s", latest.setup_url, dest)
        download_file(latest.setup_url, dest)
        start_silent_update(dest)
        logging.info("Started silent update; exiting so files can be replaced")
        request_stop()
    except Exception:
        logging.exception("Could not apply the TinyFix update")


def _is_rate_limit_error(error: str | None) -> bool:
    if not error:
        return False
    lowered = error.lower()
    return "rate limited" in lowered or " 403" in lowered or " 429" in lowered or "(403)" in lowered or "(429)" in lowered


def auto_update_check_worker() -> None:
    try:
        if not should_auto_check(CONFIG_PATH):
            logging.info("Skipping automatic update check (already checked today)")
            return
        available, latest, error = check_for_update(APP_VERSION)
        if error:
            if _is_rate_limit_error(error):
                logging.info("Update check skipped (will retry later): %s", error)
            else:
                logging.info("Update check skipped: %s", error)
            return
        mark_checked(CONFIG_PATH)
        if available and latest is not None:
            queue_ui("update-available", latest)
    except Exception:
        logging.info("Update check skipped", exc_info=True)


def manual_update_check_worker() -> None:
    try:
        available, latest, error = check_for_update(APP_VERSION)
        if error:
            if _is_rate_limit_error(error):
                logging.info("Update check skipped (will retry later): %s", error)
            else:
                logging.info("Update check skipped: %s", error)
                notify("TinyFix", "Could not check for updates right now")
            return
        mark_checked(CONFIG_PATH)
        if available and latest is not None:
            queue_ui("update-available", latest)
        else:
            notify("TinyFix", f"You already have the latest version (v{APP_VERSION}).")
            logging.info("No update available (current v%s)", APP_VERSION)
    except Exception:
        logging.info("Update check skipped", exc_info=True)


def check_for_updates_menu(_item=None, _icon=None) -> None:
    threading.Thread(target=manual_update_check_worker, name="tinyfix-update-manual", daemon=True).start()


def on_fix_clipboard(_item=None, _icon=None) -> None:
    if paused.is_set():
        logging.info("Clipboard fix ignored: paused")
        notify("TinyFix", "TinyFix is paused")
        return
    if not busy.acquire(blocking=False):
        logging.info("Clipboard fix ignored: busy")
        return
    try:
        if engine is None:
            logging.info("Clipboard fix ignored: engine not ready")
            return
        text = read_unicode_text()
        message, corrected = plan_clipboard_fix(engine, text)
        if corrected is not None:
            if not write_unicode_text(corrected):
                logging.info("Clipboard fix early return: clipboard write failed (length=%s)", 0 if text is None else len(text))
                notify("TinyFix", "Could not write the clipboard")
                return
            logging.info("Clipboard fixed (%s, length=%s)", message, len(corrected))
        else:
            logging.info("Clipboard fix early return: %s (length=%s)", message, 0 if text is None else len(text))
        notify("TinyFix", message)
    except Exception:
        logging.exception("Clipboard fix failed")
    finally:
        busy.release()


def drain_ui_actions() -> None:
    with ui_lock:
        actions = list(pending_ui)
        pending_ui.clear()
    for item in actions:
        action, payload = item if isinstance(item, tuple) else (item, None)
        if action == "change-failed":
            reason = failed_shortcut_reason()
            notify("TinyFix", reason)
            show_hotkey_dialog(reason=reason)
        elif action == "update-available" and isinstance(payload, LatestRelease):
            prompt_update(payload)


def make_icon():
    from PIL import Image, ImageDraw

    bundled = RESOURCES / "tinyfix.ico"
    if not bundled.exists():
        bundled = ROOT / "tinyfix.ico"
    if bundled.exists():
        try:
            return Image.open(bundled)
        except Exception:
            logging.exception("Could not load tray icon file")
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((2, 2, 61, 61), radius=14, fill=(18, 92, 68, 255))
    draw.line((16, 34, 28, 46), fill="white", width=5)
    draw.line((28, 46, 48, 18), fill="white", width=5)
    return image


def _sensitivity_item(name: str):
    import pystray

    def checked(*_args) -> bool:
        return current_sensitivity == name

    def choose(*_args) -> None:
        apply_sensitivity_setting(name)

    return pystray.MenuItem(name.capitalize(), choose, checked=checked, radio=True)


def tray_title() -> str:
    sel = "off" if current_hotkey.is_disabled else current_hotkey.display
    all_ = "off" if current_hotkey_all.is_disabled else current_hotkey_all.display
    return f"TinyFix — {sel} fixes selection, {all_} fixes all. Left-click fixes the clipboard."


def setup_tray() -> None:
    global tray_icon
    import pystray

    menu = pystray.Menu(
        pystray.MenuItem("Fix clipboard", on_fix_clipboard, default=True),
        pystray.MenuItem(status_text, None, enabled=False),
        pystray.MenuItem(pause_label, toggle_pause),
        pystray.MenuItem("Change shortcut…", change_shortcut),
        pystray.MenuItem("Sensitivity", pystray.Menu(*[_sensitivity_item(name) for name in SENSITIVITY_NAMES])),
        pystray.MenuItem("Check for updates", check_for_updates_menu),
        pystray.MenuItem("Exit", lambda *_: request_stop()),
    )
    tray_icon = pystray.Icon(
        APP_NAME,
        make_icon(),
        tray_title(),
        menu,
    )

    def setup(icon: pystray.Icon) -> None:
        icon.visible = True
        drain_ui_actions()
        threading.Thread(target=auto_update_check_worker, name="tinyfix-update", daemon=True).start()

    tray_icon.run(setup=setup)


def load_engine():
    from tinyfix_lib import load_tinyfix

    if not (MODELS / "wrong-word.onnx").exists():
        raise FileNotFoundError(f"TinyFix model files are missing from {MODELS}")
    logging.info("Loading tinyfix model from %s", MODELS)
    loaded = load_tinyfix(MODELS, sensitivity=current_sensitivity)
    logging.info("Model loaded (sensitivity=%s)", loaded.sensitivity)
    return loaded


def _attach_console() -> None:
    """Restore stdout when the frozen windowed exe is launched with --fix."""
    if not getattr(sys, "frozen", False):
        return
    # ATTACH_PARENT_PROCESS = -1; reuse the calling console when possible.
    if not kernel32.AttachConsole(0xFFFFFFFF):
        kernel32.AllocConsole()
    try:
        sys.stdout = open("CONOUT$", "w", encoding="utf-8", buffering=1)
        sys.stderr = open("CONOUT$", "w", encoding="utf-8", buffering=1)
        sys.stdin = open("CONIN$", "r", encoding="utf-8")
    except OSError:
        pass


def run_fix_cli(texts: list[str], output_path: str | None = None) -> int:
    _attach_console()
    setup_logging()
    load_settings()
    loaded = load_engine()
    chunks: list[str] = []
    for text in texts:
        fixed = loaded.fix(text)
        chunks.append(fixed if text.endswith("\n") else fixed + "\n")
    result = "".join(chunks)
    try:
        sys.stdout.write(result)
        sys.stdout.flush()
    except Exception:
        logging.exception("Could not write correction to stdout")
    if output_path:
        Path(output_path).write_text(result, encoding="utf-8")
    return 0


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="TinyFix Hotkey — correct selected English text.")
    parser.add_argument("--fix", action="store_true", help="Correct text and print it, then exit")
    parser.add_argument("--input", "-i", help="Read text to correct from this file")
    parser.add_argument("--output", "-o", help="Write --fix / --check-update output to this file (useful for the windowed exe)")
    parser.add_argument("text", nargs="*", help="Text to correct when using --fix (joined if split)")
    parser.add_argument("--check-update", action="store_true", help="Check GitHub for a newer TinyFix release and exit")
    parser.add_argument("--apply-update", action="store_true", help="With --check-update, download and silently install an update")
    parser.add_argument("--assume-version", help="Pretend the current app version is this value (for tests)")
    parser.add_argument("--version", action="version", version=f"TinyFix {APP_VERSION}")
    return parser.parse_args(argv)


def run_check_update_cli(assume_version: str | None, apply: bool, output_path: str | None) -> int:
    import json

    _attach_console()
    setup_logging()
    current = assume_version or APP_VERSION
    available, latest, error = check_for_update(current)
    result = {
        "ok": error is None,
        "current": current,
        "latest": latest.version if latest else None,
        "tag": latest.tag if latest else None,
        "update_available": bool(available),
        "setup_url": latest.setup_url if latest else None,
        "error": error,
        "applied": False,
    }
    code = 0
    if error:
        logging.info("Update check skipped: %s", error)
        code = 2
    elif available and latest is not None:
        code = 1
        if apply:
            try:
                dest = default_setup_download_path()
                download_file(latest.setup_url, dest)
                start_silent_update(dest)
                result["applied"] = True
                result["setup_path"] = str(dest)
                logging.info("Started silent update from %s", dest)
                code = 0
            except Exception as exc:
                logging.exception("Could not apply the TinyFix update")
                result["ok"] = False
                result["error"] = str(exc)
                code = 2
    payload = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    try:
        sys.stdout.write(payload)
        sys.stdout.flush()
    except Exception:
        logging.exception("Could not write update-check result to stdout")
    if output_path:
        Path(output_path).write_text(payload, encoding="utf-8")
    return code


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    if args.check_update:
        return run_check_update_cli(args.assume_version, args.apply_update, args.output)
    if args.fix:
        if args.input:
            payload = Path(args.input).read_text(encoding="utf-8")
        elif args.text:
            payload = " ".join(args.text)
        else:
            payload = sys.stdin.read()
            if not payload:
                print("Usage: TinyFix.exe --fix [--input file] [--output file] <text>", file=sys.stderr)
                return 2
        return run_fix_cli([payload], output_path=args.output)

    (ROOT / "cache" / "tmp").mkdir(parents=True, exist_ok=True)
    setup_logging()
    if not acquire_mutex():
        logging.info("TinyFix is already running")
        return 0
    write_pid()
    atexit.register(clear_pid)
    try:
        global engine, hotkey_thread
        load_settings()
        engine = load_engine()
        hotkey_thread = threading.Thread(target=hotkey_loop, name="tinyfix-hotkey", daemon=True)
        hotkey_thread.start()
        if not hotkey_ready.wait(timeout=5):
            logging.warning("Hotkey thread did not become ready")
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
