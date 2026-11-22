"""TinyFix tray app: select text, press the shortcut, the selection is corrected in place."""

from __future__ import annotations

import argparse
import atexit
import configparser
import ctypes
import logging
import os
import queue
import sys
import threading
import time
import traceback
from ctypes import wintypes
from pathlib import Path

# numpy's OpenBLAS reserves ~600 MB of virtual memory per process at import time
# (one buffer per CPU thread). TinyFix only does tiny matrix work, so one thread
# is enough. This must be set before numpy or onnxruntime is imported.
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
os.environ.setdefault("OMP_NUM_THREADS", "1")

import offline_pack
from language_guess import looks_english
import tray
from hotkey_util import (
    DEFAULT_HOTKEY,
    DEFAULT_HOTKEY_ALL,
    DEFAULT_HOTKEY_MENU,
    DEFAULT_HOTKEY_TRANSLATE,
    KEY_CHOICES,
    Hotkey,
    disabled_hotkey,
    parse_hotkey,
)
from llm_client import DEFAULT_GOOGLE_FALLBACK_MODEL, DEFAULT_OPENROUTER_FALLBACK_MODEL, RETIRED_FALLBACK_MODELS, LLMConfig, LLMError, LLMTimeout, correct_with_fallback, resolve_google_model, resolve_skill_config, run_skill_online, translate_online
from prompts import DEFAULT_FIX_PROMPT, LEGACY_FIX_PROMPTS, DEFAULT_TARGET_LANGUAGE, DEFAULT_TRANSLATE_PROMPT
from secret_store import default_secrets_path, load_api_keys, save_api_keys
from settings_ui import SettingsData, SettingsHooks, open_settings
from skill_menu import get_foreground_hwnd, restore_foreground, show_skill_menu
from skills import (
    Skill,
    SkillStore,
    default_store,
    legacy_from_store,
    load_skill_store,
    save_skill_store,
    visible_menu_skills,
)
from tinyfix_lib import DEFAULT_SENSITIVITY, SENSITIVITY_NAMES, normalize_sensitivity, plan_clipboard_fix
from update_check import (
    LatestRelease,
    check_for_update,
    default_setup_download_path,
    download_file,
    fetch_latest_release,
    format_update_feedback,
    is_newer,
    mark_checked,
    should_auto_check,
    start_silent_update,
)

APP_NAME = "TinyFix"
GITHUB_URL = "https://github.com/Aryan-mor/tinyfix-hotkey"
APP_VERSION = "1.5.13"
MUTEX_NAME = "Local\\TinyFixHotkey" + os.environ.get("TINYFIX_MUTEX_SUFFIX", "")  # the suffix is for tests only
CONFIG_SECTION = "TinyFix"
HOTKEY_ID = 1
HOTKEY_ALL_ID = 2
HOTKEY_TRANSLATE_ID = 3
HOTKEY_MENU_ID = 4
HOTKEY_SKILL_BASE = 100
WM_HOTKEY = 0x0312
WM_QUIT = 0x0012
WM_APP_RELOAD = 0x8001
WM_APP_PAUSE = 0x8002
WM_APP_RESUME = 0x8003
WM_APP_RECORD_ON = 0x8004  # a shortcut is being chosen: our own global shortcuts must not fire
WM_APP_RECORD_OFF = 0x8005
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
SKILLS_PATH = ROOT / "skills.json"
SECRETS_PATH = default_secrets_path(ROOT)
PROMPT_FIX_PATH = ROOT / "prompt_fix.txt"
PROMPT_TRANSLATE_PATH = ROOT / "prompt_translate.txt"
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
class WorkLock:
    """The one-job-at-a-time lock for work on the user's text. While it is held the tray icon shows it is working."""

    def __init__(self) -> None:
        self._lock = threading.Lock()

    def acquire(self, blocking: bool = True) -> bool:
        got = self._lock.acquire(blocking)
        if got:
            _show_working(True)
        return got

    def release(self) -> None:
        self._lock.release()
        _show_working(False)

    def locked(self) -> bool:
        return self._lock.locked()


def _show_working(working: bool) -> None:
    icon = tray_icon
    if icon is not None:
        try:
            icon.set_busy(working)
        except Exception:
            logging.exception("Could not update the tray icon")


busy = WorkLock()
NOT_ENGLISH_MESSAGE = "This text is not English. Turn on an online model in Settings to fix other languages."
stop_event = threading.Event()
paused = threading.Event()
hotkey_ready = threading.Event()
hotkey_failed = threading.Event()
current_hotkey = parse_hotkey(DEFAULT_HOTKEY)
current_hotkey_all = parse_hotkey(DEFAULT_HOTKEY_ALL)
current_hotkey_translate = parse_hotkey(DEFAULT_HOTKEY_TRANSLATE)
current_hotkey_menu = parse_hotkey(DEFAULT_HOTKEY_MENU)
skill_store: SkillStore = default_store()
skill_hotkey_ids: dict[int, str] = {}
translate_select_all = False
current_sensitivity = DEFAULT_SENSITIVITY
llm_config = LLMConfig()
failed_hotkey_ids: set[int] = set()
tray_icon = None
ui_lock = threading.Lock()
pending_ui: list[tuple[str, object]] = []
ui_work_queue: queue.Queue[tuple[str, object]] = queue.Queue()
ui_thread = None
ui_root = None
ui_ready = threading.Event()
_ui_thread_lock = threading.Lock()


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


OPEN_EVENT_NAME = MUTEX_NAME + "OpenSettings"
open_event_handle = None

kernel32.CreateEventW.argtypes = [wintypes.LPVOID, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.CreateEventW.restype = wintypes.HANDLE
kernel32.OpenEventW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.OpenEventW.restype = wintypes.HANDLE
kernel32.SetEvent.argtypes = [wintypes.HANDLE]
kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
user32.AllowSetForegroundWindow.argtypes = [wintypes.DWORD]
EVENT_MODIFY_STATE = 0x0002
ASFW_ANY = 0xFFFFFFFF
WAIT_OBJECT_0 = 0


def create_open_event() -> bool:
    """The running TinyFix listens on this event; starting TinyFix again sets it to open Settings."""
    global open_event_handle
    open_event_handle = kernel32.CreateEventW(None, False, False, OPEN_EVENT_NAME)  # auto-reset: an early signal is kept
    return bool(open_event_handle)


def signal_running_instance() -> bool:
    """Called by a second launch: ask the TinyFix that is already running to open Settings."""
    try:
        user32.AllowSetForegroundWindow(ASFW_ANY)  # lets the running copy bring its window to the front
        handle = kernel32.OpenEventW(EVENT_MODIFY_STATE, False, OPEN_EVENT_NAME)
        if not handle:
            return False
        try:
            return bool(kernel32.SetEvent(handle))
        finally:
            kernel32.CloseHandle(handle)
    except Exception:
        logging.exception("Could not signal the running TinyFix")
        return False


def open_event_loop() -> None:
    """Wait (in short slices, so exit is never blocked) for a second launch and open Settings for it."""
    while not stop_event.is_set():
        if open_event_handle and kernel32.WaitForSingleObject(open_event_handle, 500) == WAIT_OBJECT_0:
            logging.info("TinyFix was started again; opening Settings")
            queue_ui("open-settings")
        elif not open_event_handle:
            return


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


provider_keys: dict[str, str] = {}  # every provider's saved key, so switching provider in Settings keeps the others


def _load_provider_keys(legacy_provider: str) -> dict[str, str]:
    global provider_keys
    provider_keys = load_api_keys(SECRETS_PATH, legacy_provider)
    return provider_keys


def _saved_fallback_provider(parser) -> str:
    return parser.get(CONFIG_SECTION, "llm_fallback_provider", fallback="").strip()


def _current_fallback_model(parser, provider: str = "google") -> str:
    default = DEFAULT_OPENROUTER_FALLBACK_MODEL if provider == "openrouter" else DEFAULT_GOOGLE_FALLBACK_MODEL
    saved = parser.get(CONFIG_SECTION, "llm_fallback_model", fallback=default).strip()
    return default if saved in RETIRED_FALLBACK_MODELS else saved


def _read_prompt_file(path: Path, default: str, legacy: tuple[str, ...] = ()) -> str:
    try:
        if path.exists():
            text = path.read_text(encoding="utf-8")
            if text.strip():
                if text.strip() in {p.strip() for p in legacy}:
                    return default  # an unedited older default: upgrade it
                return text
    except Exception:
        logging.exception("Could not read %s", path)
    return default


def _write_prompt_file(path: Path, text: str) -> None:
    path.write_text(text if text.endswith("\n") else text + "\n", encoding="utf-8")


def _parse_bool(raw: str, default: bool = False) -> bool:
    text = (raw or "").strip().lower()
    if text in {"1", "true", "yes", "on"}:
        return True
    if text in {"0", "false", "no", "off"}:
        return False
    return default


def load_settings() -> None:
    global current_hotkey, current_hotkey_all, current_hotkey_translate
    global current_sensitivity, translate_select_all, llm_config
    current_hotkey = parse_hotkey(DEFAULT_HOTKEY)
    current_hotkey_all = parse_hotkey(DEFAULT_HOTKEY_ALL)
    current_hotkey_translate = parse_hotkey(DEFAULT_HOTKEY_TRANSLATE)
    translate_select_all = False
    current_sensitivity = DEFAULT_SENSITIVITY
    llm_config = LLMConfig()
    parser = _load_parser() if CONFIG_PATH.exists() else None
    if parser is not None:
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
        try:
            current_hotkey_translate = parse_hotkey(
                parser.get(CONFIG_SECTION, "hotkey_translate", fallback=DEFAULT_HOTKEY_TRANSLATE)
            )
        except Exception:
            logging.exception("Could not read translate shortcut; using default %s", DEFAULT_HOTKEY_TRANSLATE)
            current_hotkey_translate = parse_hotkey(DEFAULT_HOTKEY_TRANSLATE)
        current_sensitivity = normalize_sensitivity(
            parser.get(CONFIG_SECTION, "sensitivity", fallback=DEFAULT_SENSITIVITY)
        )
        translate_select_all = _parse_bool(parser.get(CONFIG_SECTION, "translate_select_all", fallback="false"))
        try:
            timeout = float(parser.get(CONFIG_SECTION, "llm_timeout", fallback=str(5)))
        except ValueError:
            timeout = 5.0
        provider = parser.get(CONFIG_SECTION, "llm_provider", fallback="google") or "google"
        raw_model = parser.get(CONFIG_SECTION, "llm_model", fallback="").strip()
        if raw_model:
            model = raw_model
        elif provider == "google":
            model = resolve_google_model("")
        else:
            model = raw_model
        llm_config = LLMConfig(
            provider=provider,
            api_key=_load_provider_keys(provider).get(provider, ""),
            model=model,
            fallback_model=_current_fallback_model(parser, provider),
            fallback_provider=_saved_fallback_provider(parser),
            fallback_api_key=provider_keys.get(_saved_fallback_provider(parser), ""),
            base_url=parser.get(CONFIG_SECTION, "llm_base_url", fallback=""),
            timeout=max(1.0, min(30.0, timeout)),
            enabled=_parse_bool(parser.get(CONFIG_SECTION, "llm_enabled", fallback="false")),
            fix_prompt=_read_prompt_file(PROMPT_FIX_PATH, DEFAULT_FIX_PROMPT, LEGACY_FIX_PROMPTS),
            translate_prompt=_read_prompt_file(PROMPT_TRANSLATE_PATH, DEFAULT_TRANSLATE_PROMPT),
            target_language=parser.get(CONFIG_SECTION, "llm_target_language", fallback=DEFAULT_TARGET_LANGUAGE)
            or DEFAULT_TARGET_LANGUAGE,
        )
    else:
        llm_config.api_key = _load_provider_keys(llm_config.provider).get(llm_config.provider, "")
        llm_config.fallback_api_key = provider_keys.get(llm_config.fallback_provider, "")
        llm_config.fix_prompt = _read_prompt_file(PROMPT_FIX_PATH, DEFAULT_FIX_PROMPT, LEGACY_FIX_PROMPTS)
        llm_config.translate_prompt = _read_prompt_file(PROMPT_TRANSLATE_PATH, DEFAULT_TRANSLATE_PROMPT)
    _load_skill_store_from_disk()


def _load_skill_store_from_disk() -> None:
    global skill_store, current_hotkey_menu
    store, created = load_skill_store(
        SKILLS_PATH,
        fix_hotkey=current_hotkey,
        fix_all_hotkey=current_hotkey_all,
        translate_hotkey=current_hotkey_translate,
        translate_select_all=translate_select_all,
        fix_prompt=llm_config.fix_prompt or DEFAULT_FIX_PROMPT,
        translate_prompt=llm_config.translate_prompt or DEFAULT_TRANSLATE_PROMPT,
    )
    skill_store = store
    current_hotkey_menu = store.menu_hotkey()
    _sync_legacy_from_skills()
    if created:
        try:
            save_skill_store(SKILLS_PATH, skill_store)
        except Exception:
            logging.exception("Could not write skills.json")


def _sync_legacy_from_skills() -> None:
    global current_hotkey, current_hotkey_all, current_hotkey_translate, translate_select_all, llm_config
    mapped = legacy_from_store(skill_store)
    current_hotkey = mapped["hotkey"]
    current_hotkey_all = mapped["hotkey_all"]
    current_hotkey_translate = mapped["hotkey_translate"]
    translate_select_all = mapped["translate_select_all"]
    llm_config.fix_prompt = mapped["fix_prompt"]
    llm_config.translate_prompt = mapped["translate_prompt"]


def save_settings(
    persist_hotkeys: bool = True,
    persist_sensitivity: bool = True,
    persist_llm: bool = False,
    persist_skills: bool = True,
) -> None:
    if persist_skills:
        skill_store.skill_menu_hotkey = "none" if current_hotkey_menu.is_disabled else current_hotkey_menu.display
        try:
            save_skill_store(SKILLS_PATH, skill_store)
        except Exception:
            logging.exception("Could not save skills.json")
        _sync_legacy_from_skills()
    parser = _load_parser()
    if persist_hotkeys:
        parser[CONFIG_SECTION]["hotkey"] = config_hotkey_value(current_hotkey)
        parser[CONFIG_SECTION]["hotkey_all"] = config_hotkey_value(current_hotkey_all)
        parser[CONFIG_SECTION]["hotkey_translate"] = config_hotkey_value(current_hotkey_translate)
        parser[CONFIG_SECTION]["translate_select_all"] = "true" if translate_select_all else "false"
    if persist_sensitivity:
        parser[CONFIG_SECTION]["sensitivity"] = current_sensitivity
    if persist_llm:
        parser[CONFIG_SECTION]["llm_provider"] = llm_config.provider
        parser[CONFIG_SECTION]["llm_model"] = llm_config.model
        parser[CONFIG_SECTION]["llm_base_url"] = llm_config.base_url
        parser[CONFIG_SECTION]["llm_timeout"] = str(llm_config.timeout)
        parser[CONFIG_SECTION]["llm_fallback_model"] = llm_config.fallback_model
        parser[CONFIG_SECTION]["llm_fallback_provider"] = llm_config.fallback_provider
        parser[CONFIG_SECTION]["llm_enabled"] = "true" if llm_config.enabled else "false"
        parser[CONFIG_SECTION]["llm_target_language"] = llm_config.target_language
        # Never write the API key to config.ini.
        provider_keys[llm_config.provider] = llm_config.api_key
        save_api_keys(SECRETS_PATH, provider_keys)
        _write_prompt_file(PROMPT_FIX_PATH, llm_config.fix_prompt or DEFAULT_FIX_PROMPT)
        _write_prompt_file(PROMPT_TRANSLATE_PATH, llm_config.translate_prompt or DEFAULT_TRANSLATE_PROMPT)
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
        # Always complete the key-ups so Ctrl or the letter cannot stick.
        ups = (_key_event(vk, True), _key_event(VK_CONTROL, True))
        up_array = (INPUT * 2)(*ups)
        user32.SendInput(2, up_array, ctypes.sizeof(INPUT))


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


def run_skill(skill: Skill, target_hwnd: int | None = None) -> None:
    source = skill.name
    if paused.is_set():
        logging.info("Skill ignored: paused (%s)", source)
        return
    if skill.needs_ai and (not llm_config or not llm_config.ready):
        logging.info("Skill ignored: online model is not enabled (%s)", source)
        notify("TinyFix", "Turn on an online model in Settings to use this skill")
        return
    if skill.offline_fallback and engine is None and not (llm_config and llm_config.ready):
        logging.info("Skill ignored: engine not ready (%s)", source)
        return
    if not busy.acquire(blocking=False):
        logging.info("Skill ignored: busy (%s)", source)
        return
    try:
        if target_hwnd:
            restore_foreground(target_hwnd)
            time.sleep(0.05)
        if foreground_is_unequally_elevated():
            logging.info("Skill ignored: target window is elevated (%s)", source)
            notify("TinyFix", "Can't reach an elevated window")
            return
        selected, saved, reason = copy_selection(select_all=skill.select_all)
        if reason != "ok":
            length = 0 if selected is None else len(selected)
            logging.info("Skill early return: %s (length=%s, %s)", reason, length, source)
            if reason in ("copy_failed", "empty_selection"):
                notify("TinyFix", "Nothing selected / could not copy the selection")
            elif reason == "clipboard_snapshot_failed":
                notify("TinyFix", "Could not read the clipboard")
            return
        notify("TinyFix", f"Running {skill.name}…")
        try:
            skill_config = _skill_llm_config(skill)
            if skill.offline_fallback:
                result, origin = correct_with_fallback(selected, engine, skill_config)
            else:
                result = run_skill_online(
                    skill_config,
                    skill.prompt,
                    selected,
                    language=llm_config.target_language,
                )
                origin = "online"
        except LLMTimeout:
            logging.info("Skill timed out; leaving the text unchanged (%s)", source)
            restore_clipboard(saved)
            notify_failure("TinyFix", f"{skill.name} timed out; the text was left unchanged")
            return
        except LLMError as exc:
            logging.info("Skill failed; leaving the text unchanged (%s): %s", source, exc)
            restore_clipboard(saved)
            notify_failure("TinyFix", f"{skill.name} failed; the text was left unchanged")
            return
        except Exception:
            logging.exception("Skill failed (%s)", source)
            restore_clipboard(saved)
            notify_failure("TinyFix", f"{skill.name} failed; the text was left unchanged")
            return
        if not result:
            restore_clipboard(saved)
            logging.info("Skill early return: empty result (length=%s, %s)", len(selected), source)
            notify_failure("TinyFix", f"{skill.name} failed; the text was left unchanged")
            return
        if origin == "unsupported":
            restore_clipboard(saved)
            logging.info("Skill early return: text is not English and no online model is on (%s)", source)
            notify("TinyFix", NOT_ENGLISH_MESSAGE)
            return
        if origin == "fallback":
            # Both online models failed and only the offline engine answered: tell the user.
            logging.info("Online models failed; the offline engine handled %s", source)
            notify_failure("TinyFix", "The online model failed; used the offline engine instead")
            if result == selected:
                restore_clipboard(saved)
                logging.info("Skill early return: unchanged after offline fallback (%s)", source)
                return
        if result == selected:
            restore_clipboard(saved)
            logging.info("Skill early return: unchanged (length=%s, %s)", len(selected), source)
            if skill.offline_fallback:
                notify("TinyFix", "No typos found")
            else:
                notify("TinyFix", f"{skill.name}: no changes")
            return
        if target_hwnd:
            restore_foreground(target_hwnd)
            time.sleep(0.02)
        if not write_unicode_text(result):
            restore_clipboard(saved)
            logging.info("Skill early return: clipboard write failed (length=%s, %s)", len(selected), source)
            return
        send_ctrl_key(VK_V)
        time.sleep(0.12)
        restore_clipboard(saved)
        logging.info("Skill %s changed %s character(s) (%s)", source, len(selected), origin)
    finally:
        busy.release()


def _skill_llm_config(skill: Skill) -> LLMConfig:
    """The model settings this skill runs with (see Skill.model_mode)."""
    return resolve_skill_config(llm_config, skill.model_mode, skill.model_custom(), provider_keys)


def test_skill_text(skill: Skill, sample: str) -> str:
    if skill.offline_fallback:
        result, _origin = correct_with_fallback(sample, engine, _skill_llm_config(skill))
        return result
    return run_skill_online(
        _skill_llm_config(skill),
        skill.prompt,
        sample,
        language=llm_config.target_language,
    )


def on_skill_menu_hotkey() -> None:
    if paused.is_set():
        logging.info("Skill menu ignored: paused")
        return
    hwnd = get_foreground_hwnd()
    items = visible_menu_skills(skill_store)
    if not items:
        notify("TinyFix", "No enabled skills")
        return
    hint = None
    if not (llm_config and llm_config.ready) and any(item.needs_ai for item in items):
        hint = "Turn on an online model in Settings to use AI skills"
    choice = show_skill_menu(
        items,
        ai_ready=bool(llm_config and llm_config.ready),
        target_hwnd=hwnd,
        hint=hint,
    )
    if choice.cancelled or choice.skill is None:
        restore_foreground(hwnd)
        logging.info(
            "Skill menu cancelled (elapsed=%.0fms, result=%s)",
            getattr(choice, "elapsed_ms", 0),
            getattr(choice, "native_result", 0),
        )
        return
    run_skill(choice.skill, target_hwnd=choice.target_hwnd)


def on_hotkey(select_all: bool = False) -> None:
    skill_id = "fix-all" if select_all else "fix"
    skill = skill_store.get(skill_id)
    if skill is None:
        logging.info("Hotkey ignored: missing skill %s", skill_id)
        return
    run_skill(skill)
    return
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
            corrected, source = correct_with_fallback(selected, engine, llm_config)
        except Exception:
            logging.exception("Correction failed")
            restore_clipboard(saved)
            return
        logging.info("Correction source: %s (length=%s)", source, len(selected))
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


def on_translate_hotkey() -> None:
    skill = skill_store.get("translate")
    if skill is None:
        logging.info("Hotkey ignored: missing translate skill")
        return
    run_skill(skill)
    return
    source = "translate"
    if paused.is_set():
        logging.info("Hotkey ignored: paused (%s)", source)
        return
    if not llm_config or not llm_config.ready:
        logging.info("Hotkey ignored: online model is not enabled (%s)", source)
        notify("TinyFix", "Turn on an online model in Settings to translate")
        return
    if not busy.acquire(blocking=False):
        logging.info("Hotkey ignored: busy (%s)", source)
        return
    try:
        if foreground_is_unequally_elevated():
            logging.info("Hotkey ignored: target window is elevated (%s)", source)
            notify("TinyFix", "Can't reach an elevated window")
            return
        selected, saved, reason = copy_selection(select_all=translate_select_all)
        if reason != "ok":
            length = 0 if selected is None else len(selected)
            logging.info("Hotkey early return: %s (length=%s, %s)", reason, length, source)
            if reason in ("copy_failed", "empty_selection"):
                notify("TinyFix", "Nothing selected / could not copy the selection")
            return
        try:
            translated = translate_online(llm_config, selected)
        except (LLMTimeout, LLMError) as exc:
            logging.info("Translation failed; leaving the text unchanged: %s", exc)
            restore_clipboard(saved)
            notify_failure("TinyFix", "Translation failed; the text was left unchanged")
            return
        except Exception:
            logging.exception("Translation failed")
            restore_clipboard(saved)
            notify_failure("TinyFix", "Translation failed; the text was left unchanged")
            return
        if not translated:
            restore_clipboard(saved)
            logging.info("Hotkey early return: empty translation (length=%s, %s)", len(selected), source)
            notify_failure("TinyFix", "Translation failed; the text was left unchanged")
            return
        if translated == selected:
            restore_clipboard(saved)
            logging.info("Hotkey early return: translation unchanged (length=%s, %s)", len(selected), source)
            notify("TinyFix", "Already in the target language")
            return
        if not write_unicode_text(translated):
            restore_clipboard(saved)
            logging.info("Hotkey early return: clipboard write failed (length=%s, %s)", len(selected), source)
            return
        send_ctrl_key(VK_V)
        time.sleep(0.12)
        restore_clipboard(saved)
        logging.info("Translated %s character(s) (%s)", len(selected), source)
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
    skill_hotkey_ids.clear()
    ok = _register_one(current_hotkey_menu, HOTKEY_MENU_ID, "Skill menu")
    for index, skill in enumerate(skill_store.skills):
        hotkey_id = HOTKEY_SKILL_BASE + index
        skill_hotkey_ids[hotkey_id] = skill.id
        user32.UnregisterHotKey(None, hotkey_id)
        if not skill.enabled:
            failed_hotkey_ids.discard(hotkey_id)
            continue
        hotkey = skill.hotkey_obj()
        if skill.needs_ai and not llm_config.ready:
            failed_hotkey_ids.discard(hotkey_id)
            if hotkey.is_disabled:
                logging.info("%s shortcut is disabled", skill.name)
            else:
                logging.info("%s shortcut is idle until an online model is enabled", skill.name)
            continue
        if not _register_one(hotkey, hotkey_id, skill.name):
            ok = False
    # Drop leftover ids if the user deleted skills since the last register.
    for leftover in range(HOTKEY_SKILL_BASE + len(skill_store.skills), HOTKEY_SKILL_BASE + 64):
        user32.UnregisterHotKey(None, leftover)
    return ok


def unregister_current_hotkeys() -> None:
    user32.UnregisterHotKey(None, HOTKEY_MENU_ID)
    user32.UnregisterHotKey(None, HOTKEY_ID)
    user32.UnregisterHotKey(None, HOTKEY_ALL_ID)
    user32.UnregisterHotKey(None, HOTKEY_TRANSLATE_ID)
    for hotkey_id in list(skill_hotkey_ids):
        user32.UnregisterHotKey(None, hotkey_id)
    for leftover in range(HOTKEY_SKILL_BASE, HOTKEY_SKILL_BASE + 64):
        user32.UnregisterHotKey(None, leftover)


def post_to_hotkey_thread(message: int) -> None:
    if hotkey_thread_id:
        user32.PostThreadMessageW(hotkey_thread_id, message, 0, 0)



def safe_tray_action(fn):
    """Tray callbacks run on worker threads; always log failures and tell the user."""

    def wrapper(*args, **kwargs):
        try:
            return fn(*args, **kwargs)
        except Exception:
            logging.exception("Tray menu action failed: %s", getattr(fn, "__name__", fn))
            try:
                notify("TinyFix", "Something went wrong. See tinyfix.log for details.")
            except Exception:
                pass

    wrapper.__name__ = getattr(fn, "__name__", "tray_action")
    wrapper.__wrapped__ = fn  # type: ignore[attr-defined]
    return wrapper


def model_revision_text() -> str:
    for path in (RESOURCES / "model-revision.txt", ROOT / "model-revision.txt"):
        if path.exists():
            value = path.read_text(encoding="utf-8").strip()
            if value:
                return value
    return "unknown"


def about_info() -> dict[str, str]:
    return {
        "version": APP_VERSION,
        "model_revision": model_revision_text(),
        "install_path": str(ROOT),
        "github": GITHUB_URL,
    }


def start_ui_thread() -> None:
    global ui_thread
    with _ui_thread_lock:
        if ui_thread is not None and ui_thread.is_alive():
            return
        ui_ready.clear()
        thread = threading.Thread(target=_ui_thread_main, name="tinyfix-ui", daemon=True)
        ui_thread = thread
        thread.start()
    if not ui_ready.wait(timeout=5):
        logging.warning("UI thread did not become ready")


def _ui_thread_main() -> None:
    global ui_root
    import tkinter as tk

    root = tk.Tk()
    root.withdraw()
    try:
        root.attributes("-topmost", False)
    except tk.TclError:
        pass
    ui_root = root
    ui_ready.set()
    logging.info("UI thread ready")

    def pump() -> None:
        try:
            while True:
                action, payload = ui_work_queue.get_nowait()
                try:
                    _dispatch_ui(action, payload)
                except Exception:
                    logging.exception("UI action failed: %s", action)
        except queue.Empty:
            pass
        if not stop_event.is_set():
            try:
                root.after(50, pump)
            except tk.TclError:
                pass
        else:
            try:
                root.quit()
            except tk.TclError:
                pass

    root.after(50, pump)
    try:
        root.mainloop()
    finally:
        ui_root = None
        logging.info("UI thread stopped")


def _dispatch_ui(action: str, payload: object = None) -> None:
    if action == "open-settings":
        _open_settings_on_ui_thread()
    elif action == "about":
        _show_about_window()
    elif action == "pack-offer":
        _show_pack_offer()
    elif action == "update-available" and isinstance(payload, LatestRelease):
        prompt_update(payload)
    elif action == "update-latest":
        _show_info_dialog("TinyFix", str(payload or f"You are on the latest version (v{APP_VERSION})."))
    elif action == "update-error":
        _show_info_dialog("TinyFix", str(payload or "Couldn't check for updates."))
    elif action == "change-failed":
        reason = failed_shortcut_reason()
        notify("TinyFix", reason)
        _open_settings_on_ui_thread()
    else:
        logging.info("Unknown UI action: %s", action)


def _transient_dialog_host(parent):
    """A short-lived topmost Toplevel for message boxes.

    The hidden UI root must never be made topmost or deiconified: that can
    leave a window above every other app and make them feel stuck.
    """
    import tkinter as tk

    if parent is None:
        return None
    try:
        host = tk.Toplevel(parent)
        host.withdraw()
        host.overrideredirect(True)
        host.geometry("1x1+0+0")
        host.attributes("-topmost", True)
        return host
    except tk.TclError:
        return None


def _destroy_transient_dialog_host(host) -> None:
    import tkinter as tk

    if host is None:
        return
    try:
        host.attributes("-topmost", False)
    except tk.TclError:
        pass
    try:
        host.destroy()
    except tk.TclError:
        pass


def _show_info_dialog(title: str, message: str) -> None:
    import tkinter as tk
    from tkinter import messagebox

    parent = ui_root
    if parent is None:
        notify(title, message)
        return
    host = _transient_dialog_host(parent)
    try:
        messagebox.showinfo(title, message, parent=host or parent)
    finally:
        _destroy_transient_dialog_host(host)
    logging.info("Shown message box: %s", message)


def _open_settings_on_ui_thread() -> None:
    data = SettingsData(
        version=APP_VERSION,
        hotkey=current_hotkey,
        hotkey_all=current_hotkey_all,
        hotkey_translate=current_hotkey_translate,
        translate_select_all=translate_select_all,
        sensitivity=current_sensitivity,
        llm=llm_config,
        provider_keys=dict(provider_keys),
        last_update_check=_last_update_check_value(),
        skills=[Skill.from_dict(skill.to_dict()) for skill in skill_store.skills],
        skill_menu_hotkey=current_hotkey_menu,
        dismissed_skills=list(skill_store.dismissed),
    )
    hooks = SettingsHooks(
        on_apply=apply_settings_data,
        on_check_updates=lambda: check_for_updates_menu(),
        on_live_sensitivity=lambda name: apply_sensitivity_setting(name, persist=True),
        on_test_skill=test_skill_text,
        on_recording=set_hotkeys_recording,
        **_pack_hook_args(),
    )
    logging.info("Presenting Settings on the UI thread")
    open_settings(data, hooks, master=ui_root)


def _show_pack_offer() -> None:
    from settings_ui import open_pack_offer

    hooks = SettingsHooks(on_apply=lambda _data: None, **_pack_hook_args())
    open_pack_offer(pack_info(), hooks, ui_root)


def _show_about_window() -> None:
    import tkinter as tk
    import webbrowser
    from tkinter import ttk

    info = about_info()
    logging.info(
        "Opening About (version=%s, model=%s, path=%s)",
        info["version"],
        info["model_revision"],
        info["install_path"],
    )
    parent = ui_root
    window = tk.Toplevel(parent) if parent is not None else tk.Tk()
    window.title("About TinyFix")
    window.resizable(False, False)
    window.geometry("+420+220")
    try:
        window.attributes("-topmost", True)
        window.after(400, lambda: window.attributes("-topmost", False))
    except tk.TclError:
        pass
    frame = ttk.Frame(window, padding=20)
    frame.grid(row=0, column=0, sticky="nsew")
    ttk.Label(frame, text=f"TinyFix Hotkey  v{info['version']}", font=("Segoe UI", 14, "bold")).grid(
        row=0, column=0, sticky="w"
    )
    ttk.Label(frame, text=f"Model revision: {info['model_revision']}", wraplength=420).grid(
        row=1, column=0, sticky="w", pady=(12, 0)
    )
    ttk.Label(frame, text=f"Install path: {info['install_path']}", wraplength=420).grid(
        row=2, column=0, sticky="w", pady=(8, 0)
    )
    link = ttk.Label(frame, text=info["github"], foreground="#0b57d0", cursor="hand2")
    link.grid(row=3, column=0, sticky="w", pady=(12, 0))
    link.bind("<Button-1>", lambda *_: webbrowser.open(info["github"]))
    buttons = ttk.Frame(frame)
    buttons.grid(row=4, column=0, sticky="e", pady=(20, 0))
    ttk.Button(buttons, text="Check for updates", command=lambda: check_for_updates_menu()).pack(side="left", padx=(0, 8))
    ttk.Button(buttons, text="OK", command=window.destroy).pack(side="left")
    try:
        window.focus_force()
    except tk.TclError:
        pass


def queue_ui(action: str, payload: object = None) -> None:
    with ui_lock:
        pending_ui.append((action, payload))
    start_ui_thread()
    ui_work_queue.put((action, payload))
    if tray_icon is not None:
        try:
            tray_icon.update_menu()
        except Exception:
            pass


def notify_failure(title: str, message: str) -> None:
    """Like notify, and also bounce a red alert icon in the tray so the user sees something failed."""
    notify(title, message)
    icon = tray_icon
    if icon is not None:
        try:
            icon.set_alert()
        except Exception:
            logging.exception("Could not show the tray alert")


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
                if msg.wParam == HOTKEY_MENU_ID:
                    on_skill_menu_hotkey()
                elif int(msg.wParam) in skill_hotkey_ids:
                    skill = skill_store.get(skill_hotkey_ids[int(msg.wParam)])
                    if skill is None:
                        logging.info("Hotkey ignored: missing skill for id %s", int(msg.wParam))
                    else:
                        run_skill(skill)
                elif msg.wParam == HOTKEY_ID:
                    on_hotkey(select_all=False)
                elif msg.wParam == HOTKEY_ALL_ID:
                    on_hotkey(select_all=True)
                elif msg.wParam == HOTKEY_TRANSLATE_ID:
                    on_translate_hotkey()
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
        elif msg.message == WM_APP_RECORD_ON:
            unregister_current_hotkeys()
            logging.info("Shortcuts released while a new one is chosen")
        elif msg.message == WM_APP_RECORD_OFF:
            if not paused.is_set():
                if register_current_hotkeys():
                    hotkey_failed.clear()
                else:
                    hotkey_failed.set()
                    queue_ui("change-failed")
            logging.info("Shortcuts back on")
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
    # Prefer the registered skill ids when present.
    for hotkey_id, skill_id in skill_hotkey_ids.items():
        if skill_id == "fix":
            sel = _hotkey_status(current_hotkey, hotkey_id)
        elif skill_id == "fix-all":
            all_ = _hotkey_status(current_hotkey_all, hotkey_id)
    if paused.is_set():
        return f"Paused ({sel} / {all_})"
    if failed_hotkey_ids:
        return f"Shortcut unavailable ({sel} / {all_})"
    extra = " · AI on" if llm_config.ready else ""
    return f"Running ({sel} / {all_}){extra}"


def pause_label(_item=None) -> str:
    return "Resume" if paused.is_set() else "Pause"


@safe_tray_action
def toggle_pause(_item=None, _icon=None) -> None:
    if paused.is_set():
        post_to_hotkey_thread(WM_APP_RESUME)
    else:
        post_to_hotkey_thread(WM_APP_PAUSE)


def set_hotkeys_recording(active: bool) -> None:
    """Called by the shortcut window: while it is open, TinyFix's own shortcuts are released."""
    post_to_hotkey_thread(WM_APP_RECORD_ON if active else WM_APP_RECORD_OFF)


def apply_hotkeys(
    selection: Hotkey,
    fix_all: Hotkey,
    translate: Hotkey | None = None,
    persist: bool = True,
) -> None:
    global current_hotkey, current_hotkey_all, current_hotkey_translate
    current_hotkey = selection
    current_hotkey_all = fix_all
    if translate is not None:
        current_hotkey_translate = translate
    fix = skill_store.get("fix")
    fix_all_skill = skill_store.get("fix-all")
    tr = skill_store.get("translate")
    if fix is not None:
        fix.hotkey = "none" if selection.is_disabled else selection.display
    if fix_all_skill is not None:
        fix_all_skill.hotkey = "none" if fix_all.is_disabled else fix_all.display
    if tr is not None and translate is not None:
        tr.hotkey = "none" if translate.is_disabled else translate.display
    if persist:
        try:
            save_settings(persist_hotkeys=True, persist_sensitivity=False, persist_skills=True)
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
    grabbed = False
    try:
        root.grab_set()
        grabbed = True
    except tk.TclError:
        pass
    try:
        root.focus_force()
        root.mainloop()
    finally:
        if grabbed:
            try:
                if root.winfo_exists():
                    root.grab_release()
            except tk.TclError:
                pass
        try:
            if root.winfo_exists():
                root.attributes("-topmost", False)
        except tk.TclError:
            pass

    chosen = result["value"]
    if chosen is not None:
        apply_hotkeys(chosen[0], chosen[1])


@safe_tray_action
def change_shortcut(_item=None, _icon=None) -> None:
    open_settings_window()


def _last_update_check_value() -> str:
    parser = _load_parser()
    return parser.get(CONFIG_SECTION, "last_update_check", fallback="").strip()


def apply_settings_data(data: SettingsData) -> None:
    global translate_select_all, llm_config, skill_store, current_hotkey_menu, provider_keys
    started = time.perf_counter()
    logging.info("Settings save starting")
    new_sensitivity = normalize_sensitivity(data.sensitivity)
    if new_sensitivity != current_sensitivity:
        # Thresholds only; this does not reload the model.
        apply_sensitivity_setting(new_sensitivity, persist=False)
        logging.info("Settings save: sensitivity applied in %.0fms", (time.perf_counter() - started) * 1000)
    else:
        logging.info("Settings save: sensitivity unchanged (%s)", current_sensitivity)
    skill_store = SkillStore(
        version=getattr(skill_store, "version", 1),
        skills=[Skill.from_dict(skill.to_dict()) for skill in (data.skills or [])] or list(skill_store.skills),
        skill_menu_hotkey="none" if data.skill_menu_hotkey.is_disabled else data.skill_menu_hotkey.display,
        dismissed=list(data.dismissed_skills),
    )
    current_hotkey_menu = data.skill_menu_hotkey
    _sync_legacy_from_skills()
    translate_select_all = data.translate_select_all
    llm_config = data.llm
    provider_keys = {**provider_keys, **{k: v for k, v in (data.provider_keys or {}).items()}}
    llm_config.fallback_api_key = provider_keys.get(llm_config.fallback_provider, "")
    apply_hotkeys(data.hotkey, data.hotkey_all, data.hotkey_translate, persist=False)
    logging.info("Settings save: hotkeys posted in %.0fms", (time.perf_counter() - started) * 1000)
    try:
        save_settings(persist_hotkeys=True, persist_sensitivity=True, persist_llm=True, persist_skills=True)
    except Exception:
        logging.exception("Could not save settings")
    logging.info(
        "Settings saved (online=%s, provider=%s, sensitivity=%s, elapsed=%.0fms)",
        llm_config.ready,
        llm_config.provider,
        current_sensitivity,
        (time.perf_counter() - started) * 1000,
    )


@safe_tray_action
def open_settings_window(_item=None, _icon=None) -> None:
    logging.info("Settings requested from the tray")
    start_ui_thread()
    ui_work_queue.put(("open-settings", None))


def failed_shortcut_reason() -> str:
    parts: list[str] = []
    if HOTKEY_MENU_ID in failed_hotkey_ids:
        parts.append(f"{current_hotkey_menu.display} (skill menu)")
    for hotkey_id, skill_id in skill_hotkey_ids.items():
        if hotkey_id not in failed_hotkey_ids:
            continue
        skill = skill_store.get(skill_id)
        label = skill.name if skill is not None else skill_id
        hotkey = skill.hotkey_obj() if skill is not None else current_hotkey
        parts.append(f"{hotkey.display} ({label})")
    if not parts:
        return "A shortcut is already in use by another program."
    return " and ".join(parts) + " already in use by another program."


def prompt_update(latest: LatestRelease) -> None:
    import tkinter as tk
    from tkinter import messagebox

    parent = ui_root
    if parent is None:
        logging.warning("Update prompt skipped: UI thread is not ready")
        notify("TinyFix", f"TinyFix v{latest.version} is available. Open About to update.")
        return
    host = _transient_dialog_host(parent)
    try:
        answer = messagebox.askyesno(
            "TinyFix",
            f"TinyFix v{latest.version} is available. Update now?",
            parent=host or parent,
        )
    finally:
        _destroy_transient_dialog_host(host)
    if not answer:
        logging.info("User declined the update to v%s", latest.version)
        return

    def work() -> None:
        try:
            dest = default_setup_download_path()
            logging.info("Downloading %s to %s", latest.setup_url, dest)
            download_file(latest.setup_url, dest)
            start_silent_update(dest)
            logging.info("Started silent update; exiting so files can be replaced")
            request_stop()
        except Exception:
            logging.exception("Could not apply the TinyFix update")
            ui_work_queue.put(("update-error", "Couldn't install the update. See tinyfix.log for details."))

    threading.Thread(target=work, name="tinyfix-update-download", daemon=True).start()


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
    # Manual checks always run, even if we already auto-checked today.
    logging.info("Manual update check starting (current v%s)", APP_VERSION)
    try:
        available, latest, error = check_for_update(APP_VERSION)
        kind, message = format_update_feedback(APP_VERSION, available, latest, error)
        logging.info("Manual update check result: %s (%s)", kind, message)
        if error:
            start_ui_thread()
            ui_work_queue.put(("update-error", message))
            return
        mark_checked(CONFIG_PATH)
        if kind == "available" and latest is not None:
            start_ui_thread()
            ui_work_queue.put(("update-available", latest))
        else:
            start_ui_thread()
            ui_work_queue.put(("update-latest", message))
    except Exception as exc:
        logging.exception("Manual update check failed")
        kind, message = format_update_feedback(APP_VERSION, False, None, str(exc))
        start_ui_thread()
        ui_work_queue.put(("update-error", message))


@safe_tray_action
def check_for_updates_menu(_item=None, _icon=None) -> None:
    logging.info("Check for updates clicked")
    notify("TinyFix", "Checking…")
    threading.Thread(target=manual_update_check_worker, name="tinyfix-update-manual", daemon=True).start()


@safe_tray_action
def open_about_window(_item=None, _icon=None) -> None:
    logging.info("About TinyFix clicked")
    start_ui_thread()
    ui_work_queue.put(("about", None))


@safe_tray_action
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
        if llm_config.ready and text:
            try:
                corrected, source = correct_with_fallback(text, engine, llm_config)
            except Exception:
                logging.exception("Clipboard fix failed")
                notify_failure("TinyFix", "Could not correct the clipboard")
                return
            if source == "unsupported":
                message, corrected = NOT_ENGLISH_MESSAGE, None
            elif corrected == text:
                message, corrected = "No typos found in clipboard", None
            else:
                # Keep the notification consistent with the offline path; count via the engine when possible.
                try:
                    count = len(engine.check(text)) if source != "online" else max(1, sum(1 for a, b in zip(text, corrected) if a != b) + abs(len(text) - len(corrected)))
                except Exception:
                    count = 1
                if source == "online":
                    message, corrected_out = "Clipboard fixed with the online model", corrected
                    corrected = corrected_out
                else:
                    label = "change" if count == 1 else "changes"
                    message, corrected = f"Clipboard fixed: {count} {label}", corrected
                logging.info("Clipboard correction source: %s", source)
        elif text and not looks_english(text):
            message, corrected = NOT_ENGLISH_MESSAGE, None
        else:
            message, corrected = plan_clipboard_fix(engine, text)
        if corrected is not None:
            if not write_unicode_text(corrected):
                logging.info("Clipboard fix early return: clipboard write failed (length=%s)", 0 if text is None else len(text))
                notify_failure("TinyFix", "Could not write the clipboard")
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
    # Settings, About, and update dialogs run on the dedicated UI thread.
    # Draining here would open a second Tk on the tray thread, so just clear.
    with ui_lock:
        pending_ui.clear()


def working_icon_paths() -> list[Path]:
    """The three-dot frames the tray cycles through while TinyFix works."""
    found: list[Path] = []
    for number in (1, 2, 3):
        name = f"tinyfix-working-{number}.ico"
        for base in (RESOURCES, ROOT, ROOT / "assets"):
            if (base / name).exists():
                found.append(base / name)
                break
    return found


def alert_icon_paths() -> list[Path]:
    """The red bouncing "!" frames the tray shows when something fails."""
    found: list[Path] = []
    for number in range(1, 9):
        name = f"tinyfix-alert-{number}.ico"
        for base in (RESOURCES, ROOT, ROOT / "assets"):
            if (base / name).exists():
                found.append(base / name)
                break
    return found


def icon_path() -> Path | None:
    for base in (RESOURCES, ROOT):
        candidate = base / "tinyfix.ico"
        if candidate.exists():
            return candidate
    return None


def _sensitivity_item(name: str):
    def checked(*_args) -> bool:
        return current_sensitivity == name

    def choose(*_args) -> None:
        apply_sensitivity_setting(name)

    return tray.MenuItem(name.capitalize(), choose, checked=checked)


def tray_title() -> str:
    sel = "off" if current_hotkey.is_disabled else current_hotkey.display
    all_ = "off" if current_hotkey_all.is_disabled else current_hotkey_all.display
    menu = "off" if current_hotkey_menu.is_disabled else current_hotkey_menu.display
    return (
        f"TinyFix v{APP_VERSION} — {sel} fixes selection, {all_} fixes all, "
        f"{menu} opens skills. Click to open Settings."
    )


def setup_tray() -> None:
    global tray_icon

    menu = [
        tray.MenuItem("Settings…", open_settings_window, default=True),
        tray.MenuItem("Fix clipboard", on_fix_clipboard),
        tray.MenuItem(status_text, None, enabled=False),
        tray.MenuItem(pause_label, toggle_pause),
        tray.MenuItem("Change shortcut…", change_shortcut),
        tray.MenuItem("Sensitivity", submenu=[_sensitivity_item(name) for name in SENSITIVITY_NAMES]),
        tray.MenuItem("Check for updates", check_for_updates_menu),
        tray.MenuItem("About TinyFix", open_about_window),
        tray.MenuItem("Exit", lambda *_: request_stop()),
    ]
    tray_icon = tray.TrayIcon(APP_NAME, icon_path(), tray_title(), menu, working_icon_paths=working_icon_paths(), alert_icon_paths=alert_icon_paths())

    def setup(icon: tray.TrayIcon) -> None:
        icon.visible = True
        start_ui_thread()
        drain_ui_actions()
        threading.Thread(target=auto_update_check_worker, name="tinyfix-update", daemon=True).start()
        maybe_offer_pack()

    tray_icon.run(setup=setup)


PACK_DIR = offline_pack.pack_root(ROOT)
# "dictionary" until the offline pack is loaded; the settings window reads this.
engine_mode = "dictionary"
engine_note = ""


def offline_engine_enabled() -> bool:
    """User switch for the offline pack (default on, only matters once the pack is installed)."""
    return _parse_bool(_load_parser().get(CONFIG_SECTION, "offline_engine", fallback="true"), True)


def set_offline_engine_enabled(enabled: bool) -> None:
    parser = _load_parser()
    parser[CONFIG_SECTION]["offline_engine"] = "true" if enabled else "false"
    _write_parser(parser)


def load_engine():
    """Load the dictionary engine, plus the offline pack when it is installed and enabled.

    The pack is optional: any problem with it is logged and the dictionary engine is used instead,
    so starting TinyFix never fails because of the pack.
    """
    global engine_mode, engine_note
    from tinyfix_lib import load_tinyfix

    try:
        offline_pack.apply_pending_removal(PACK_DIR)
        offline_pack.cleanup_stale(PACK_DIR)
    except Exception:
        logging.exception("Could not clean up the offline pack folder")
    model_path = None
    engine_note = ""
    if offline_engine_enabled():
        if (PACK_DIR / "current").is_dir():
            try:
                model_path = offline_pack.activate(PACK_DIR)
            except offline_pack.PackError as exc:
                engine_note = str(exc)
                logging.warning("Offline pack not used: %s", exc)
        else:
            engine_note = "Offline pack not installed."
    else:
        engine_note = "Offline pack is switched off."
    if model_path is not None:
        try:
            loaded = load_tinyfix(MODELS, sensitivity=current_sensitivity, use_model=True, model_path=model_path)
            engine_mode = "pack"
            logging.info("Loaded engine with offline pack (sensitivity=%s)", loaded.sensitivity)
            return loaded
        except Exception as exc:
            engine_note = f"The offline pack could not start ({exc.__class__.__name__})."
            logging.exception("Offline pack failed to load; using the dictionary engine")
    engine_mode = "dictionary"
    loaded = load_tinyfix(MODELS, sensitivity=current_sensitivity, use_model=False)
    logging.info("Loaded dictionary engine (sensitivity=%s)", loaded.sensitivity)
    return loaded


_pack_cancel = threading.Event()
_engine_lock = threading.Lock()


def pack_info() -> dict:
    """Everything the Settings tab and the first-run offer need, in one cheap, UI-safe call."""
    status = offline_pack.status(PACK_DIR, active=engine_mode == "pack")
    disk_mb = round(status.size_bytes / (1024 * 1024)) if status.size_bytes else offline_pack.APPROX_DISK_MB
    return {
        "installed": status.installed,
        "compatible": status.compatible,
        "active": engine_mode == "pack",
        "enabled": offline_engine_enabled(),
        "reason": status.reason,
        "removal_pending": offline_pack.removal_pending(PACK_DIR),
        "note": engine_note,
        "download_mb": offline_pack.APPROX_DOWNLOAD_MB,
        "disk_mb": disk_mb,
        "ram_mb": offline_pack.APPROX_RAM_MB,
    }


def reload_engine() -> None:
    """Rebuild the engine (with or without the pack) and swap it in. Blocking: call from a worker thread."""
    global engine
    with _engine_lock:
        engine = load_engine()


def pack_download_async(progress, done) -> None:
    _pack_cancel.clear()

    def work() -> None:
        error = None
        try:
            offline_pack.download_pack(PACK_DIR, progress=progress, cancel=_pack_cancel)
            set_offline_engine_enabled(True)
            reload_engine()
            if engine_mode != "pack":
                error = engine_note or "The pack was installed but could not start."
        except offline_pack.PackError as exc:
            error = str(exc)
        except Exception:
            logging.exception("Offline pack download failed")
            error = "Something went wrong. See tinyfix.log for details."
        done(error)

    threading.Thread(target=work, name="tinyfix-pack-download", daemon=True).start()


def pack_cancel() -> None:
    _pack_cancel.set()


def pack_set_enabled_async(enabled: bool, done) -> None:
    def work() -> None:
        error = None
        try:
            set_offline_engine_enabled(enabled)
            reload_engine()
            if enabled and engine_mode != "pack":
                error = engine_note or "The pack could not be turned on."
        except Exception:
            logging.exception("Could not switch the offline pack")
            error = "Something went wrong. See tinyfix.log for details."
        done(error)

    threading.Thread(target=work, name="tinyfix-pack-toggle", daemon=True).start()


def pack_remove() -> str:
    """Delete the pack. A loaded pack cannot be deleted on Windows, so that case waits for the next start."""
    _pack_cancel.set()
    was_active = offline_pack.is_active()
    try:
        part = PACK_DIR / ".download.part"
        part.unlink(missing_ok=True)
    except OSError:
        pass
    if was_active:
        offline_pack.request_removal(PACK_DIR)
        set_offline_engine_enabled(False)
        threading.Thread(target=reload_engine, name="tinyfix-pack-unload", daemon=True).start()
        return "TinyFix will delete the pack files the next time it starts. Basic fixes are on now."
    try:
        import shutil

        shutil.rmtree(PACK_DIR / "current", ignore_errors=False)
    except FileNotFoundError:
        pass
    except OSError:
        offline_pack.request_removal(PACK_DIR)
        return "Some files are in use. TinyFix will finish removing them the next time it starts."
    return "The pack was removed."


def _pack_hook_args() -> dict:
    return {
        "pack_info": pack_info,
        "pack_download": pack_download_async,
        "pack_cancel": pack_cancel,
        "pack_set_enabled": pack_set_enabled_async,
        "pack_remove": pack_remove,
    }


def maybe_offer_pack() -> None:
    """Ask once, on the first start without the pack, whether to download it."""
    try:
        parser = _load_parser()
        if _parse_bool(parser.get(CONFIG_SECTION, "pack_offer_shown", fallback="false")):
            return
        info = pack_info()
        if info["installed"] or info["removal_pending"]:
            return
        parser[CONFIG_SECTION]["pack_offer_shown"] = "true"
        _write_parser(parser)
        queue_ui("pack-offer")
    except Exception:
        logging.exception("Could not offer the offline pack")


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
        logging.info("TinyFix is already running; asking it to open Settings")
        signal_running_instance()
        return 0
    create_open_event()
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
        threading.Thread(target=open_event_loop, name="tinyfix-open-event", daemon=True).start()
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
