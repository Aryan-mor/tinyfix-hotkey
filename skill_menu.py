"""Skill picker menu near the mouse or caret (native Win32 popup)."""

from __future__ import annotations

import ctypes
import logging
import time
from ctypes import wintypes
from dataclasses import dataclass

from skills import Skill

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

MF_STRING = 0x0000
MF_GRAYED = 0x0001
MF_DISABLED = 0x0002
MF_SEPARATOR = 0x0800
TPM_LEFTALIGN = 0x0000
TPM_TOPALIGN = 0x0000
TPM_LEFTBUTTON = 0x0000
TPM_RIGHTBUTTON = 0x0002
TPM_RETURNCMD = 0x0100
WS_EX_TOOLWINDOW = 0x00000080
WS_EX_TOPMOST = 0x00000008
WS_POPUP = 0x80000000
SW_SHOWNOACTIVATE = 4
SW_SHOW = 5
HWND_TOPMOST = -1
SWP_NOSIZE = 0x0001
SWP_NOMOVE = 0x0002
SWP_SHOWWINDOW = 0x0040
PM_REMOVE = 0x0001
WM_KEYFIRST = 0x0100
WM_KEYLAST = 0x0109
WM_MOUSEFIRST = 0x0200
WM_MOUSELAST = 0x020E
WM_SYSKEYDOWN = 0x0104
WM_SYSKEYUP = 0x0105
VK_SHIFT = 0x10
VK_CONTROL = 0x11
VK_MENU = 0x12
VK_LWIN = 0x5B
VK_RWIN = 0x5C
KEYEVENTF_KEYUP = 0x0002
ERROR_CLASS_ALREADY_EXISTS = 1410
ID_HINT = 1000
ID_SKILL_BASE = 1
CLASS_NAME = "TinyFixSkillMenuOwner"
_MODIFIER_VKS = (VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN)

logger = logging.getLogger(__name__)


class POINT(ctypes.Structure):
    _fields_ = (("x", wintypes.LONG), ("y", wintypes.LONG))


class RECT(ctypes.Structure):
    _fields_ = (
        ("left", wintypes.LONG),
        ("top", wintypes.LONG),
        ("right", wintypes.LONG),
        ("bottom", wintypes.LONG),
    )


class MSG(ctypes.Structure):
    _fields_ = (
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", POINT),
    )


class GUITHREADINFO(ctypes.Structure):
    _fields_ = (
        ("cbSize", wintypes.DWORD),
        ("flags", wintypes.DWORD),
        ("hwndActive", wintypes.HWND),
        ("hwndFocus", wintypes.HWND),
        ("hwndCapture", wintypes.HWND),
        ("hwndMenuOwner", wintypes.HWND),
        ("hwndMoveSize", wintypes.HWND),
        ("hwndCaret", wintypes.HWND),
        ("rcCaret", RECT),
    )


WNDPROC = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)


class WNDCLASSEXW(ctypes.Structure):
    _fields_ = (
        ("cbSize", wintypes.UINT),
        ("style", wintypes.UINT),
        ("lpfnWndProc", WNDPROC),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", wintypes.HINSTANCE),
        ("hIcon", wintypes.HANDLE),
        ("hCursor", wintypes.HANDLE),
        ("hbrBackground", wintypes.HANDLE),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
        ("hIconSm", wintypes.HANDLE),
    )


user32.GetCursorPos.argtypes = [ctypes.POINTER(POINT)]
user32.GetCursorPos.restype = wintypes.BOOL
user32.GetForegroundWindow.restype = wintypes.HWND
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.SetForegroundWindow.restype = wintypes.BOOL
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetGUIThreadInfo.argtypes = [wintypes.DWORD, ctypes.POINTER(GUITHREADINFO)]
user32.GetGUIThreadInfo.restype = wintypes.BOOL
user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(POINT)]
user32.ClientToScreen.restype = wintypes.BOOL
user32.CreatePopupMenu.restype = wintypes.HMENU
user32.AppendMenuW.argtypes = [wintypes.HMENU, wintypes.UINT, ctypes.c_uint, wintypes.LPCWSTR]
user32.AppendMenuW.restype = wintypes.BOOL
user32.TrackPopupMenu.argtypes = [
    wintypes.HMENU,
    wintypes.UINT,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    wintypes.HWND,
    ctypes.c_void_p,
]
user32.TrackPopupMenu.restype = ctypes.c_int
user32.DestroyMenu.argtypes = [wintypes.HMENU]
user32.DestroyMenu.restype = wintypes.BOOL
user32.CreateWindowExW.argtypes = [
    wintypes.DWORD,
    wintypes.LPCWSTR,
    wintypes.LPCWSTR,
    wintypes.DWORD,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    wintypes.HWND,
    wintypes.HMENU,
    wintypes.HINSTANCE,
    wintypes.LPVOID,
]
user32.CreateWindowExW.restype = wintypes.HWND
user32.DestroyWindow.argtypes = [wintypes.HWND]
user32.DestroyWindow.restype = wintypes.BOOL
user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
user32.ShowWindow.restype = wintypes.BOOL
user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT]
user32.SetWindowPos.restype = wintypes.BOOL
user32.BringWindowToTop.argtypes = [wintypes.HWND]
user32.BringWindowToTop.restype = wintypes.BOOL
user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
user32.AttachThreadInput.restype = wintypes.BOOL
user32.PeekMessageW.argtypes = [ctypes.POINTER(MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT, wintypes.UINT]
user32.PeekMessageW.restype = wintypes.BOOL
user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.DefWindowProcW.restype = ctypes.c_ssize_t
user32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEXW)]
user32.RegisterClassExW.restype = wintypes.ATOM
user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
user32.GetAsyncKeyState.restype = wintypes.SHORT
kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
kernel32.GetModuleHandleW.restype = wintypes.HINSTANCE
kernel32.GetCurrentThreadId.restype = wintypes.DWORD


@WNDPROC
def _wndproc(hwnd, msg, wparam, lparam):
    return user32.DefWindowProcW(hwnd, msg, wparam, lparam)


_class_registered = False


def get_foreground_hwnd() -> int:
    return int(user32.GetForegroundWindow() or 0)


def restore_foreground(hwnd: int | None) -> None:
    if not hwnd:
        return
    try:
        _force_foreground(int(hwnd))
    except Exception:
        logger.debug("Could not restore the foreground window", exc_info=True)


def caret_or_cursor_position() -> tuple[int, int]:
    info = GUITHREADINFO()
    info.cbSize = ctypes.sizeof(GUITHREADINFO)
    hwnd = user32.GetForegroundWindow()
    if hwnd:
        thread_id = user32.GetWindowThreadProcessId(hwnd, None)
        if thread_id and user32.GetGUIThreadInfo(thread_id, ctypes.byref(info)):
            caret = int(info.hwndCaret or 0)
            if caret:
                point = POINT(info.rcCaret.left, info.rcCaret.bottom)
                if user32.ClientToScreen(info.hwndCaret, ctypes.byref(point)):
                    return int(point.x), int(point.y)
    point = POINT()
    if user32.GetCursorPos(ctypes.byref(point)):
        return int(point.x), int(point.y)
    return 200, 200


def _key_down(vk: int) -> bool:
    return bool(user32.GetAsyncKeyState(vk) & 0x8000)


def wait_for_hotkey_keys_released(timeout: float = 0.5) -> bool:
    """Wait until Ctrl/Alt/Shift/Win are up so they cannot dismiss the popup."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if not any(_key_down(vk) for vk in _MODIFIER_VKS):
            return True
        time.sleep(0.01)
    return not any(_key_down(vk) for vk in _MODIFIER_VKS)


def flush_input_queue() -> int:
    """Drop pending key/mouse messages so a hotkey key-up is not read as cancel."""
    msg = MSG()
    count = 0
    while user32.PeekMessageW(ctypes.byref(msg), None, WM_KEYFIRST, WM_KEYLAST, PM_REMOVE):
        count += 1
    while user32.PeekMessageW(ctypes.byref(msg), None, WM_MOUSEFIRST, WM_MOUSELAST, PM_REMOVE):
        count += 1
    return count


def _ensure_class() -> None:
    global _class_registered
    if _class_registered:
        return
    wc = WNDCLASSEXW()
    wc.cbSize = ctypes.sizeof(WNDCLASSEXW)
    wc.lpfnWndProc = _wndproc
    wc.hInstance = kernel32.GetModuleHandleW(None)
    wc.lpszClassName = CLASS_NAME
    atom = user32.RegisterClassExW(ctypes.byref(wc))
    if not atom:
        err = ctypes.get_last_error()
        if err != ERROR_CLASS_ALREADY_EXISTS:
            logger.warning("Could not register the skill-menu window class (err=%s)", err)
    _class_registered = True


def _force_foreground(hwnd: int) -> bool:
    if not hwnd:
        return False
    target = wintypes.HWND(hwnd)
    foreground = user32.GetForegroundWindow()
    our_thread = kernel32.GetCurrentThreadId()
    fg_thread = user32.GetWindowThreadProcessId(foreground, None) if foreground else 0
    attached = False
    if fg_thread and fg_thread != our_thread:
        attached = bool(user32.AttachThreadInput(our_thread, fg_thread, True))
    try:
        user32.BringWindowToTop(target)
        user32.SetWindowPos(target, wintypes.HWND(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW)
        return bool(user32.SetForegroundWindow(target))
    finally:
        if attached:
            user32.AttachThreadInput(our_thread, fg_thread, False)


def _create_owner_window(x: int, y: int) -> int:
    _ensure_class()
    hwnd = user32.CreateWindowExW(
        WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
        CLASS_NAME,
        "TinyFix",
        WS_POPUP,
        int(x),
        int(y),
        1,
        1,
        None,
        None,
        kernel32.GetModuleHandleW(None),
        None,
    )
    return int(hwnd or 0)


@dataclass
class MenuChoice:
    skill: Skill | None
    cancelled: bool
    target_hwnd: int
    elapsed_ms: float = 0.0
    native_result: int = 0


def _item_label(index: int, skill: Skill, *, usable: bool) -> str:
    prefix = f"&{index + 1}  " if index < 9 else "     "
    hotkey = skill.display_hotkey()
    suffix = "" if hotkey == "None" else f"\t{hotkey}"
    if not usable:
        return f"{prefix}{skill.name} (needs online AI){suffix}"
    return f"{prefix}{skill.name}{suffix}"


def show_skill_menu(
    skills: list[Skill],
    *,
    ai_ready: bool,
    target_hwnd: int | None = None,
    position: tuple[int, int] | None = None,
    hint: str | None = None,
    wait_for_keys: bool = True,
) -> MenuChoice:
    """Show a popup menu of skills. Returns the chosen skill, or cancelled=True.

    The menu is owned by a tiny window created on this thread. Using another
    process's foreground window as the owner makes TrackPopupMenu return 0
    immediately, which is what users saw as 'Skill menu cancelled'.
    """
    target = int(target_hwnd or get_foreground_hwnd())
    x, y = position or caret_or_cursor_position()
    if wait_for_keys:
        released = wait_for_hotkey_keys_released(0.5)
        if not released:
            logger.info("Skill menu: modifiers still held after waiting")
    flushed = flush_input_queue()
    if flushed:
        logger.info("Skill menu: flushed %s pending input message(s)", flushed)

    menu = user32.CreatePopupMenu()
    if not menu:
        logger.error("Skill menu: CreatePopupMenu failed (err=%s)", ctypes.get_last_error())
        return MenuChoice(skill=None, cancelled=True, target_hwnd=target)

    owner = 0
    started = time.perf_counter()
    chosen = 0
    try:
        if not skills:
            user32.AppendMenuW(menu, MF_STRING | MF_GRAYED, 0, "No enabled skills")
        for index, skill in enumerate(skills):
            usable = (not skill.needs_ai) or ai_ready
            flags = MF_STRING if usable else (MF_STRING | MF_GRAYED)
            ident = ID_SKILL_BASE + index if usable else 0
            user32.AppendMenuW(menu, flags, ident, _item_label(index, skill, usable=usable))
        if hint:
            user32.AppendMenuW(menu, MF_SEPARATOR, 0, None)
            user32.AppendMenuW(menu, MF_STRING | MF_GRAYED, ID_HINT, hint)

        owner = _create_owner_window(x, y)
        if not owner:
            logger.error("Skill menu: could not create an owner window (err=%s)", ctypes.get_last_error())
            return MenuChoice(skill=None, cancelled=True, target_hwnd=target, elapsed_ms=0, native_result=0)

        user32.ShowWindow(wintypes.HWND(owner), SW_SHOW)
        _force_foreground(owner)
        flush_input_queue()

        flags = TPM_LEFTALIGN | TPM_TOPALIGN | TPM_LEFTBUTTON | TPM_RETURNCMD
        chosen = int(user32.TrackPopupMenu(menu, flags, int(x), int(y), 0, wintypes.HWND(owner), None) or 0)
    finally:
        elapsed_ms = (time.perf_counter() - started) * 1000
        if owner:
            user32.DestroyWindow(wintypes.HWND(owner))
        user32.DestroyMenu(menu)

    logger.info(
        "Skill menu closed (result=%s, elapsed=%.0fms, owner=%s, target=%s)",
        chosen,
        elapsed_ms,
        owner,
        target,
    )
    if chosen >= ID_SKILL_BASE:
        index = chosen - ID_SKILL_BASE
        if 0 <= index < len(skills):
            skill = skills[index]
            usable = (not skill.needs_ai) or ai_ready
            if usable:
                return MenuChoice(skill=skill, cancelled=False, target_hwnd=target, elapsed_ms=elapsed_ms, native_result=chosen)
    return MenuChoice(skill=None, cancelled=True, target_hwnd=target, elapsed_ms=elapsed_ms, native_result=chosen)
