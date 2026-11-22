"""A modal "press the shortcut" window.

Why a modal and not "click Record, then press keys in the settings window":
  * TinyFix's own global shortcuts must not fire while someone is choosing a new one. They used to: pressing
    Ctrl+F3 ran Fix all, which sent Ctrl+A into the window and "Ctrl+Alt+A" was recorded. The app pauses its
    global shortcuts while this window is open (on_pause hook).
  * Keys come from Windows virtual-key codes and GetKeyState, not Tk's keysym/state bits. Tk reports
    Num Lock as the Alt modifier (0x8), which also showed up as a phantom Alt.
  * Recording is always visible: the window says what it is waiting for and shows the keys as they are pressed.
"""

from __future__ import annotations

import ctypes
import logging
import sys
import tkinter as tk
from tkinter import ttk
from typing import Callable

from hotkey_util import (
    MOD_ALT,
    MOD_CONTROL,
    MOD_SHIFT,
    MOD_WIN,
    Hotkey,
    display_key,
    format_hotkey,
    parse_hotkey,
)

VK_SHIFT, VK_CONTROL, VK_MENU = 0x10, 0x11, 0x12
VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU = 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5
VK_LWIN, VK_RWIN = 0x5B, 0x5C
VK_ESCAPE, VK_RETURN = 0x1B, 0x0D
MODIFIER_VKS = frozenset(
    {VK_SHIFT, VK_CONTROL, VK_MENU, VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN}
)

NOT_SET_LABEL = "Waiting for your keys…"


def supported_vk(vk: int) -> bool:
    """True for keys TinyFix can register as a global shortcut."""
    return not display_key(vk).startswith("VK_")


def modifiers_from_flags(ctrl: bool, alt: bool, shift: bool, win: bool) -> int:
    return (MOD_CONTROL if ctrl else 0) | (MOD_ALT if alt else 0) | (MOD_SHIFT if shift else 0) | (MOD_WIN if win else 0)


def current_modifiers() -> int:
    """Modifier keys held right now, straight from Windows (not from Tk's event.state)."""
    if sys.platform != "win32":
        return 0
    get = ctypes.WinDLL("user32").GetKeyState
    get.restype = ctypes.c_short

    def down(vk: int) -> bool:
        return bool(get(vk) & 0x8000)

    return modifiers_from_flags(
        ctrl=down(VK_CONTROL),
        alt=down(VK_MENU),
        shift=down(VK_SHIFT),
        win=down(VK_LWIN) or down(VK_RWIN),
    )


def modifier_text(modifiers: int) -> str:
    parts = []
    if modifiers & MOD_CONTROL:
        parts.append("Ctrl")
    if modifiers & MOD_ALT:
        parts.append("Alt")
    if modifiers & MOD_SHIFT:
        parts.append("Shift")
    if modifiers & MOD_WIN:
        parts.append("Win")
    return "+".join(parts)


def hotkey_from_press(vk: int, modifiers: int) -> tuple[Hotkey | None, str]:
    """Turn one key press into a shortcut. Returns (hotkey, problem). hotkey is None when it cannot be used."""
    if vk in MODIFIER_VKS:
        return None, ""
    if not supported_vk(vk):
        return None, "TinyFix cannot use that key. Try a letter, number, F-key, Space, or an arrow key."
    hotkey = Hotkey(modifiers=modifiers & 0x0F, vk=vk, display=format_hotkey(modifiers & 0x0F, vk))
    if not hotkey.is_safe():
        return None, "Hold Ctrl, Alt, Shift, or Win with that key. Only F1 to F12 can be used alone."
    return hotkey, ""


class ShortcutDialog:
    """Ask for a shortcut. Use ShortcutDialog.ask(...); the result is a spec, "none", or None for cancel."""

    def __init__(
        self,
        parent: tk.Misc,
        title: str,
        current: str = "none",
        conflict_of: Callable[[Hotkey], str | None] | None = None,
        on_pause: Callable[[bool], None] | None = None,
    ) -> None:
        self.result: str | None = None
        self._candidate: Hotkey | None = None
        self._conflict_of = conflict_of
        self._on_pause = on_pause
        self._paused = False

        self.win = tk.Toplevel(parent)
        self.win.title("Choose a shortcut")
        self.win.resizable(False, False)
        self.win.transient(parent.winfo_toplevel())
        frame = ttk.Frame(self.win, padding=20)
        frame.grid(sticky="nsew")
        ttk.Label(frame, text=title, font=("Segoe UI", 12, "bold"), wraplength=380).grid(sticky="w")
        ttk.Label(
            frame,
            text="Press the keys you want to use together, for example Ctrl and F3.",
            wraplength=380,
            foreground="#555555",
        ).grid(sticky="w", pady=(4, 12))

        self.display = tk.StringVar(value=NOT_SET_LABEL)
        shown = ttk.Label(frame, textvariable=self.display, font=("Segoe UI", 20, "bold"), anchor="center", width=22)
        shown.grid(sticky="ew", ipady=14)
        self.note = tk.StringVar(value="")
        ttk.Label(frame, textvariable=self.note, wraplength=380, foreground="#b3261e").grid(sticky="w", pady=(8, 0))

        current_text = "none" if (current or "").strip().lower() in ("", "none", "disabled") else current
        self.current_label = ttk.Label(
            frame,
            text="Now: " + ("no shortcut" if current_text == "none" else current_text),
            foreground="#555555",
        )
        self.current_label.grid(sticky="w", pady=(6, 0))

        buttons = ttk.Frame(frame)
        buttons.grid(sticky="e", pady=(16, 0))
        self.save_btn = ttk.Button(buttons, text="Save", command=self._save, state="disabled")
        accent = "Accent.TButton"
        try:
            if ttk.Style().layout(accent):
                self.save_btn.configure(style=accent)
        except tk.TclError:
            pass
        ttk.Button(buttons, text="Cancel", command=self._cancel).pack(side="right")
        self.save_btn.pack(side="right", padx=(0, 8))
        ttk.Button(buttons, text="No shortcut", command=self._clear).pack(side="right", padx=(0, 8))

        self.win.bind("<KeyPress>", self._on_key_press)
        self.win.bind("<KeyRelease>", self._on_key_release)
        self.win.protocol("WM_DELETE_WINDOW", self._cancel)
        self.win.update_idletasks()
        self._center(parent)
        self._set_paused(True)
        try:
            self.win.grab_set()
        except tk.TclError:
            pass
        self.win.focus_force()

    # ------------------------------------------------------------------ pausing the app's own shortcuts

    def _set_paused(self, paused: bool) -> None:
        if paused == self._paused:
            return
        self._paused = paused
        if self._on_pause is not None:
            try:
                self._on_pause(paused)
            except Exception:
                logging.exception("Could not pause or resume the global shortcuts")

    def _center(self, parent: tk.Misc) -> None:
        try:
            top = parent.winfo_toplevel()
            x = top.winfo_rootx() + (top.winfo_width() - self.win.winfo_width()) // 2
            y = top.winfo_rooty() + max(40, (top.winfo_height() - self.win.winfo_height()) // 3)
            self.win.geometry(f"+{max(0, x)}+{max(0, y)}")
        except tk.TclError:
            pass

    # ------------------------------------------------------------------ keys

    def _on_key_press(self, event: tk.Event) -> str:
        vk = int(event.keycode)
        modifiers = current_modifiers()
        if vk == VK_ESCAPE and not modifiers:
            self._cancel()
            return "break"
        if vk == VK_RETURN and not modifiers and self._candidate is not None:
            self._save()
            return "break"
        if vk in MODIFIER_VKS:
            held = modifier_text(modifiers)
            self.display.set((held + "+…") if held else (self._candidate.display if self._candidate else NOT_SET_LABEL))
            return "break"
        self._apply_press(vk, modifiers)
        return "break"

    def _on_key_release(self, _event: tk.Event) -> str:
        return "break"

    def _apply_press(self, vk: int, modifiers: int) -> None:
        hotkey, problem = hotkey_from_press(vk, modifiers)
        if hotkey is None:
            self._candidate = None
            self.display.set(format_hotkey(modifiers & 0x0F, vk) if supported_vk(vk) else NOT_SET_LABEL)
            self.note.set(problem)
            self.save_btn.configure(state="disabled")
            return
        self._candidate = hotkey
        self.display.set(hotkey.display)
        clash = self._conflict_of(hotkey) if self._conflict_of else None
        self.note.set(clash or "")
        self.save_btn.configure(state="disabled" if clash else "normal")
        if clash:
            self._candidate = None

    # ------------------------------------------------------------------ results

    def _save(self) -> None:
        if self._candidate is None:
            return
        self.result = self._candidate.display
        self._close()

    def _clear(self) -> None:
        self.result = "none"
        self._close()

    def _cancel(self) -> None:
        self.result = None
        self._close()

    def _close(self) -> None:
        self._set_paused(False)
        try:
            self.win.grab_release()
        except tk.TclError:
            pass
        try:
            self.win.destroy()
        except tk.TclError:
            pass

    @classmethod
    def ask(cls, parent: tk.Misc, title: str, current: str = "none", conflict_of=None, on_pause=None) -> str | None:
        dialog = cls(parent, title, current, conflict_of, on_pause)
        parent.wait_window(dialog.win)
        return dialog.result


def spec_is_valid(spec: str) -> bool:
    try:
        parse_hotkey(spec)
    except ValueError:
        return False
    return True
