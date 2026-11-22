"""Small Windows tray icon built on ctypes (replaces pystray + Pillow).

Rules that keep it from hanging or locking up:
  * one hidden window and its message loop live on the thread that calls run();
  * menu actions never run on that thread: each starts on a short-lived worker thread;
  * the menu is rebuilt every time it opens, so text/checks are always current;
  * SetForegroundWindow before TrackPopupMenu and WM_NULL after it, or the menu will not close;
  * the icon is added again when Explorer restarts (TaskbarCreated).
"""

from __future__ import annotations

import ctypes
import logging
import threading
from ctypes import wintypes
from pathlib import Path
from typing import Any, Callable

user32 = ctypes.WinDLL("user32", use_last_error=True)
shell32 = ctypes.WinDLL("shell32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

WM_NULL = 0x0000
WM_DESTROY = 0x0002
WM_CLOSE = 0x0010
WM_USER = 0x0400
WM_TRAY = WM_USER + 20
WM_BUSY = WM_USER + 21
WM_TIMER = 0x0113
TIMER_SHOW, TIMER_ANIM = 1, 2
SHOW_DELAY_MS = 150  # quick fixes finish before this and never flash the working icon
FRAME_MS = 350
WORKING_TIP = "TinyFix is working…"
WM_LBUTTONUP = 0x0202
WM_RBUTTONUP = 0x0205
WM_CONTEXTMENU = 0x007B

NIM_ADD, NIM_MODIFY, NIM_DELETE = 0, 1, 2
NIF_MESSAGE, NIF_ICON, NIF_TIP, NIF_INFO = 0x1, 0x2, 0x4, 0x10
NIIF_INFO = 0x1
MF_STRING, MF_GRAYED, MF_CHECKED, MF_POPUP, MF_SEPARATOR = 0x0, 0x1, 0x8, 0x10, 0x800
TPM_RIGHTBUTTON, TPM_RETURNCMD, TPM_NONOTIFY = 0x2, 0x100, 0x80
IMAGE_ICON, LR_LOADFROMFILE = 1, 0x10
SM_CXSMICON, SM_CYSMICON = 49, 50
IDI_APPLICATION = 32512
HWND_MESSAGE_NONE = None
MAX_TIP = 127  # szTip holds 128 UTF-16 units including the NUL

LRESULT = ctypes.c_ssize_t
WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)


class WNDCLASSW(ctypes.Structure):
    _fields_ = [
        ("style", wintypes.UINT),
        ("lpfnWndProc", WNDPROC),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", wintypes.HINSTANCE),
        ("hIcon", wintypes.HICON),
        ("hCursor", wintypes.HANDLE),
        ("hbrBackground", wintypes.HBRUSH),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
    ]


class GUID(ctypes.Structure):
    _fields_ = [("Data1", wintypes.DWORD), ("Data2", wintypes.WORD), ("Data3", wintypes.WORD), ("Data4", ctypes.c_ubyte * 8)]


class NOTIFYICONDATAW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("hWnd", wintypes.HWND),
        ("uID", wintypes.UINT),
        ("uFlags", wintypes.UINT),
        ("uCallbackMessage", wintypes.UINT),
        ("hIcon", wintypes.HICON),
        ("szTip", wintypes.WCHAR * 128),
        ("dwState", wintypes.DWORD),
        ("dwStateMask", wintypes.DWORD),
        ("szInfo", wintypes.WCHAR * 256),
        ("uTimeout", wintypes.UINT),
        ("szInfoTitle", wintypes.WCHAR * 64),
        ("dwInfoFlags", wintypes.DWORD),
        ("guidItem", GUID),
        ("hBalloonIcon", wintypes.HICON),
    ]


class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", POINT),
    ]


user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.DefWindowProcW.restype = LRESULT
user32.RegisterClassW.argtypes = [ctypes.POINTER(WNDCLASSW)]
user32.RegisterClassW.restype = wintypes.ATOM
user32.UnregisterClassW.argtypes = [wintypes.LPCWSTR, wintypes.HINSTANCE]
user32.CreateWindowExW.argtypes = [
    wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD,
    ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
    wintypes.HWND, wintypes.HMENU, wintypes.HINSTANCE, wintypes.LPVOID,
]
user32.CreateWindowExW.restype = wintypes.HWND
user32.DestroyWindow.argtypes = [wintypes.HWND]
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.GetMessageW.argtypes = [ctypes.POINTER(MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT]
user32.TranslateMessage.argtypes = [ctypes.POINTER(MSG)]
user32.DispatchMessageW.argtypes = [ctypes.POINTER(MSG)]
user32.CreatePopupMenu.restype = wintypes.HMENU
user32.AppendMenuW.argtypes = [wintypes.HMENU, wintypes.UINT, ctypes.c_size_t, wintypes.LPCWSTR]
user32.DestroyMenu.argtypes = [wintypes.HMENU]
user32.SetMenuDefaultItem.argtypes = [wintypes.HMENU, wintypes.UINT, wintypes.UINT]
user32.TrackPopupMenu.argtypes = [wintypes.HMENU, wintypes.UINT, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.HWND, wintypes.LPVOID]
user32.TrackPopupMenu.restype = wintypes.UINT
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.GetCursorPos.argtypes = [ctypes.POINTER(POINT)]
user32.LoadImageW.argtypes = [wintypes.HINSTANCE, wintypes.LPCWSTR, wintypes.UINT, ctypes.c_int, ctypes.c_int, wintypes.UINT]
user32.LoadImageW.restype = wintypes.HANDLE
user32.LoadIconW.argtypes = [wintypes.HINSTANCE, wintypes.LPCWSTR]
user32.LoadIconW.restype = wintypes.HICON
user32.DestroyIcon.argtypes = [wintypes.HICON]
user32.RegisterWindowMessageW.argtypes = [wintypes.LPCWSTR]
user32.RegisterWindowMessageW.restype = wintypes.UINT
user32.SetTimer.argtypes = [wintypes.HWND, ctypes.c_size_t, wintypes.UINT, wintypes.LPVOID]
user32.SetTimer.restype = ctypes.c_size_t
user32.KillTimer.argtypes = [wintypes.HWND, ctypes.c_size_t]
user32.PostQuitMessage.argtypes = [ctypes.c_int]
user32.GetSystemMetrics.argtypes = [ctypes.c_int]
shell32.Shell_NotifyIconW.argtypes = [wintypes.DWORD, ctypes.POINTER(NOTIFYICONDATAW)]
shell32.Shell_NotifyIconW.restype = wintypes.BOOL
kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
kernel32.GetModuleHandleW.restype = wintypes.HMODULE

Text = "str | Callable[..., str]"


class MenuItem:
    """One menu entry. text/enabled/checked may be callables so they are fresh each time the menu opens."""

    def __init__(
        self,
        text: Any,
        action: Callable[..., Any] | None = None,
        *,
        default: bool = False,
        enabled: Any = True,
        checked: Callable[..., bool] | None = None,
        submenu: "list[MenuItem] | None" = None,
    ) -> None:
        self._text = text
        self.action = action
        self.default = default
        self._enabled = enabled
        self.checked = checked
        self.submenu = submenu

    def label(self) -> str:
        value = self._text(self) if callable(self._text) else self._text
        return str(value).replace("\x00", "")

    def is_enabled(self) -> bool:
        value = self._enabled(self) if callable(self._enabled) else self._enabled
        return bool(value) and (self.action is not None or self.submenu is not None)

    def is_checked(self) -> bool:
        try:
            return bool(self.checked(self)) if self.checked else False
        except Exception:
            return False


SEPARATOR = MenuItem("-")


def _clip(text: str, limit: int) -> str:
    return text if len(text) <= limit else text[: limit - 1] + "…"


class TrayIcon:
    def __init__(
        self,
        name: str,
        icon_path: Path | None,
        title: str,
        items: list[MenuItem],
        working_icon_paths: tuple[Path, ...] | list[Path] = (),
    ) -> None:
        self.name = name
        self.icon_path = icon_path
        self.title = title
        self.items = items
        self.hwnd = None
        self._hicon = None
        self._icon_owned = False
        self._class_name = f"TinyFixTray{id(self):x}"
        self._wndproc = WNDPROC(self._proc)  # keep a reference or the callback is collected
        self._taskbar_created = user32.RegisterWindowMessageW("TaskbarCreated")
        self._ready = threading.Event()
        self._stopped = threading.Event()
        self._added = False
        self._actions: dict[int, MenuItem] = {}
        self.working_icon_paths = [Path(p) for p in working_icon_paths]
        self._working_icons: list[object] = []
        self._show_pending = False
        self.working_shown = False  # True while the animated working icon is on screen
        self.working_frame = 0

    # ------------------------------------------------------------------ public

    def run(self, setup: Callable[["TrayIcon"], None] | None = None) -> None:
        """Create the window, show the icon and pump messages until stop(). Blocks the calling thread."""
        instance = kernel32.GetModuleHandleW(None)
        wc = WNDCLASSW()
        wc.lpfnWndProc = self._wndproc
        wc.hInstance = instance
        wc.lpszClassName = self._class_name
        if not user32.RegisterClassW(ctypes.byref(wc)):
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            self.hwnd = user32.CreateWindowExW(0, self._class_name, self.name, 0, 0, 0, 0, 0, None, None, instance, None)
            if not self.hwnd:
                raise ctypes.WinError(ctypes.get_last_error())
            self._load_icon()
            self._add_icon()
            self._ready.set()
            if setup is not None:
                threading.Thread(target=self._run_setup, args=(setup,), name="tinyfix-tray-setup", daemon=True).start()
            msg = MSG()
            while True:
                result = user32.GetMessageW(ctypes.byref(msg), None, 0, 0)
                if result == 0 or result == -1:
                    break
                user32.TranslateMessage(ctypes.byref(msg))
                user32.DispatchMessageW(ctypes.byref(msg))
        finally:
            self._remove_icon()
            if self.hwnd:
                user32.DestroyWindow(self.hwnd)
                self.hwnd = None
            user32.UnregisterClassW(self._class_name, instance)
            if self._hicon and self._icon_owned:
                user32.DestroyIcon(self._hicon)
            self._hicon = None
            for handle in self._working_icons:
                user32.DestroyIcon(handle)
            self._working_icons = []
            self._stopped.set()

    def stop(self) -> None:
        """Safe from any thread."""
        hwnd = self.hwnd
        if hwnd:
            user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)

    def set_busy(self, busy: bool) -> None:
        """Show or hide the animated working icon. Safe from any thread; it only posts a message."""
        hwnd = self.hwnd
        if hwnd:
            user32.PostMessageW(hwnd, WM_BUSY, 1 if busy else 0, 0)

    def update_menu(self) -> None:
        """The menu is rebuilt each time it opens, so there is nothing to do."""

    def set_title(self, title: str) -> None:
        self.title = title
        self._modify(NIF_TIP)

    def notify(self, message: str, title: str = "TinyFix") -> None:
        if not self.hwnd:
            return
        data = self._data(NIF_INFO)
        data.szInfo = _clip(str(message), 255)
        data.szInfoTitle = _clip(str(title), 63)
        data.dwInfoFlags = NIIF_INFO
        shell32.Shell_NotifyIconW(NIM_MODIFY, ctypes.byref(data))

    @property
    def visible(self) -> bool:
        return self._added

    @visible.setter
    def visible(self, value: bool) -> None:
        if value and not self._added and self.hwnd:
            self._add_icon()
        elif not value:
            self._remove_icon()

    # ------------------------------------------------------------------ internals

    def _run_setup(self, setup: Callable[["TrayIcon"], None]) -> None:
        try:
            setup(self)
        except Exception:
            logging.exception("Tray setup failed")

    def _load_file_icon(self, path: Path):
        width, height = user32.GetSystemMetrics(SM_CXSMICON), user32.GetSystemMetrics(SM_CYSMICON)
        if not Path(path).exists():
            return None
        return user32.LoadImageW(None, str(path), IMAGE_ICON, width, height, LR_LOADFROMFILE) or None

    def _load_working_icons(self) -> list[object]:
        if not self._working_icons:
            self._working_icons = [h for h in (self._load_file_icon(path) for path in self.working_icon_paths) if h]
            if self.working_icon_paths and not self._working_icons:
                logging.warning("Working icons are missing; the tray icon will not animate")
        return self._working_icons

    def _set_icon(self, hicon, tip: str) -> None:
        if not (self.hwnd and self._added and hicon):
            return
        data = self._data(NIF_ICON | NIF_TIP)
        data.hIcon = hicon
        data.szTip = _clip(tip, MAX_TIP)
        shell32.Shell_NotifyIconW(NIM_MODIFY, ctypes.byref(data))

    def _on_busy(self, busy: bool) -> None:
        if busy:
            if self.working_shown or self._show_pending:
                return
            self._show_pending = True
            user32.SetTimer(self.hwnd, TIMER_SHOW, SHOW_DELAY_MS, None)
        else:
            self._stop_working()

    def _stop_working(self) -> None:
        if self.hwnd:
            user32.KillTimer(self.hwnd, TIMER_SHOW)
            user32.KillTimer(self.hwnd, TIMER_ANIM)
        self._show_pending = False
        if self.working_shown:
            self.working_shown = False
            self._set_icon(self._hicon, self.title)

    def _on_timer(self, timer_id: int) -> None:
        if timer_id == TIMER_SHOW:
            user32.KillTimer(self.hwnd, TIMER_SHOW)
            if not self._show_pending:
                return
            self._show_pending = False
            icons = self._load_working_icons()
            if not icons:
                return
            self.working_shown = True
            self.working_frame = 0
            self._set_icon(icons[0], WORKING_TIP)
            user32.SetTimer(self.hwnd, TIMER_ANIM, FRAME_MS, None)
        elif timer_id == TIMER_ANIM and self.working_shown and self._working_icons:
            self.working_frame = (self.working_frame + 1) % len(self._working_icons)
            self._set_icon(self._working_icons[self.working_frame], WORKING_TIP)

    def _load_icon(self) -> None:
        width, height = user32.GetSystemMetrics(SM_CXSMICON), user32.GetSystemMetrics(SM_CYSMICON)
        handle = None
        if self.icon_path and Path(self.icon_path).exists():
            handle = user32.LoadImageW(None, str(self.icon_path), IMAGE_ICON, width, height, LR_LOADFROMFILE)
            self._icon_owned = bool(handle)
        if not handle:
            logging.warning("Tray icon file missing or unreadable; using the default Windows icon")
            handle = user32.LoadIconW(None, ctypes.cast(IDI_APPLICATION, wintypes.LPCWSTR))
            self._icon_owned = False
        self._hicon = handle

    def _data(self, flags: int) -> NOTIFYICONDATAW:
        data = NOTIFYICONDATAW()
        data.cbSize = ctypes.sizeof(NOTIFYICONDATAW)
        data.hWnd = self.hwnd
        data.uID = 1
        data.uFlags = flags
        if flags & NIF_MESSAGE:
            data.uCallbackMessage = WM_TRAY
        if flags & NIF_ICON:
            data.hIcon = self._hicon
        if flags & NIF_TIP:
            data.szTip = _clip(self.title, MAX_TIP)
        return data

    def _add_icon(self) -> None:
        data = self._data(NIF_MESSAGE | NIF_ICON | NIF_TIP)
        self._added = bool(shell32.Shell_NotifyIconW(NIM_ADD, ctypes.byref(data)))
        if not self._added:
            logging.warning("Could not add the tray icon (Explorer may not be running yet)")

    def _modify(self, flags: int) -> None:
        if self.hwnd and self._added:
            data = self._data(flags)
            shell32.Shell_NotifyIconW(NIM_MODIFY, ctypes.byref(data))

    def _remove_icon(self) -> None:
        if self.hwnd and self._added:
            data = self._data(0)
            shell32.Shell_NotifyIconW(NIM_DELETE, ctypes.byref(data))
        self._added = False

    def _proc(self, hwnd, message, wparam, lparam) -> int:
        try:
            if message == WM_TRAY:
                event = lparam & 0xFFFF
                if event == WM_LBUTTONUP:
                    self._run_default()
                elif event in (WM_RBUTTONUP, WM_CONTEXTMENU):
                    self._show_menu()
                return 0
            if message == self._taskbar_created and self._taskbar_created:
                # Explorer restarted. Drop any stale entry first: adding a duplicate is rejected.
                stale = self._data(0)
                shell32.Shell_NotifyIconW(NIM_DELETE, ctypes.byref(stale))
                self._added = False
                self._add_icon()
                return 0
            if message == WM_BUSY:
                self._on_busy(bool(wparam))
                return 0
            if message == WM_TIMER:
                self._on_timer(int(wparam))
                return 0
            if message == WM_CLOSE:
                self._stop_working()
                self._remove_icon()
                user32.DestroyWindow(hwnd)
                self.hwnd = None
                return 0
            if message == WM_DESTROY:
                user32.PostQuitMessage(0)
                return 0
        except Exception:
            logging.exception("Tray window procedure failed")
        return user32.DefWindowProcW(hwnd, message, wparam, lparam)

    def _run_default(self) -> None:
        for item in self.items:
            if item.default and item.is_enabled():
                self._invoke(item)
                return

    def _invoke(self, item: MenuItem) -> None:
        action = item.action
        if action is None:
            return

        def work() -> None:
            try:
                action(item, self)
            except Exception:
                logging.exception("Tray action failed: %s", item.label())

        threading.Thread(target=work, name="tinyfix-tray-action", daemon=True).start()

    def _build_menu(self, items: list[MenuItem], handles: list[Any]) -> Any:
        menu = user32.CreatePopupMenu()
        handles.append(menu)
        for item in items:
            if item is SEPARATOR:
                user32.AppendMenuW(menu, MF_SEPARATOR, 0, None)
                continue
            flags = MF_STRING
            if not item.is_enabled():
                flags |= MF_GRAYED
            if item.is_checked():
                flags |= MF_CHECKED
            if item.submenu is not None:
                sub = self._build_menu(item.submenu, handles)
                user32.AppendMenuW(menu, flags | MF_POPUP, sub or 0, item.label())
                continue
            command = 1000 + len(self._actions)
            self._actions[command] = item
            user32.AppendMenuW(menu, flags, command, item.label())
            if item.default:
                user32.SetMenuDefaultItem(menu, command, 0)
        return menu

    def _show_menu(self) -> None:
        self._actions = {}
        handles: list[Any] = []
        try:
            menu = self._build_menu(self.items, handles)
            point = POINT()
            user32.GetCursorPos(ctypes.byref(point))
            user32.SetForegroundWindow(self.hwnd)  # required, or the menu stays open after a click elsewhere
            command = user32.TrackPopupMenu(
                menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, point.x, point.y, 0, self.hwnd, None
            )
            user32.PostMessageW(self.hwnd, WM_NULL, 0, 0)
            item = self._actions.get(command)
            if item is not None:
                self._invoke(item)
        finally:
            # Destroying the top menu also destroys its submenus; destroy only the root.
            if handles:
                user32.DestroyMenu(handles[0])
