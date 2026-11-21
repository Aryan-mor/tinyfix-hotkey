"""Skill picker menu near the mouse or caret (native Win32 popup)."""

from __future__ import annotations

import ctypes
from ctypes import wintypes
from dataclasses import dataclass
from typing import Callable

from skills import Skill

user32 = ctypes.WinDLL("user32", use_last_error=True)

MF_STRING = 0x0000
MF_GRAYED = 0x0001
MF_DISABLED = 0x0002
MF_SEPARATOR = 0x0800
TPM_LEFTALIGN = 0x0000
TPM_TOPALIGN = 0x0000
TPM_RIGHTBUTTON = 0x0002
TPM_RETURNCMD = 0x0100
HWND_MESSAGE = -3
WM_NULL = 0x0000

ID_HINT = 1000
ID_SKILL_BASE = 1


class POINT(ctypes.Structure):
    _fields_ = (("x", wintypes.LONG), ("y", wintypes.LONG))


class RECT(ctypes.Structure):
    _fields_ = (
        ("left", wintypes.LONG),
        ("top", wintypes.LONG),
        ("right", wintypes.LONG),
        ("bottom", wintypes.LONG),
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
user32.CreateWindowExW.restype = wintypes.HWND


def get_foreground_hwnd() -> int:
    return int(user32.GetForegroundWindow() or 0)


def restore_foreground(hwnd: int | None) -> None:
    if not hwnd:
        return
    try:
        user32.SetForegroundWindow(wintypes.HWND(hwnd))
    except Exception:
        pass


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


@dataclass
class MenuChoice:
    skill: Skill | None
    cancelled: bool
    target_hwnd: int


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
) -> MenuChoice:
    """Show a popup menu of skills. Returns the chosen skill, or cancelled=True."""
    hwnd = int(target_hwnd or get_foreground_hwnd())
    x, y = position or caret_or_cursor_position()
    menu = user32.CreatePopupMenu()
    if not menu:
        return MenuChoice(skill=None, cancelled=True, target_hwnd=hwnd)
    try:
        if not skills:
            user32.AppendMenuW(menu, MF_STRING | MF_GRAYED, 0, "No enabled skills")
        for index, skill in enumerate(skills):
            usable = (not skill.needs_ai) or ai_ready
            flags = MF_STRING if usable else (MF_STRING | MF_GRAYED)
            user32.AppendMenuW(menu, flags, ID_SKILL_BASE + index if usable else 0, _item_label(index, skill, usable=usable))
        if hint:
            user32.AppendMenuW(menu, MF_SEPARATOR, 0, None)
            user32.AppendMenuW(menu, MF_STRING | MF_GRAYED, ID_HINT, hint)
        flags = TPM_LEFTALIGN | TPM_TOPALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD
        # A message-only window is not always a valid owner for TrackPopupMenu.
        # Reuse the current foreground window; the menu is temporary.
        owner = user32.GetForegroundWindow() or hwnd
        chosen = int(user32.TrackPopupMenu(menu, flags, int(x), int(y), 0, owner, None) or 0)
    finally:
        user32.DestroyMenu(menu)
    if chosen >= ID_SKILL_BASE:
        index = chosen - ID_SKILL_BASE
        if 0 <= index < len(skills):
            skill = skills[index]
            usable = (not skill.needs_ai) or ai_ready
            if usable:
                return MenuChoice(skill=skill, cancelled=False, target_hwnd=hwnd)
    return MenuChoice(skill=None, cancelled=True, target_hwnd=hwnd)
