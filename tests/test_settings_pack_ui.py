"""Settings UI: offline pack tab, first-run offer, Gemini key help. Real Tk, fake pack back end."""

from __future__ import annotations

import threading
import time
import tkinter as tk

import pytest

import settings_ui
from hotkey_util import DEFAULT_HOTKEY, DEFAULT_HOTKEY_ALL, DEFAULT_HOTKEY_TRANSLATE, parse_hotkey
from settings_ui import (
    SettingsData,
    SettingsHooks,
    SettingsWindow,
    friendly_key_error,
    key_hint,
    open_pack_offer,
)


@pytest.fixture(scope="module")
def tk_root():
    try:
        root = tk.Tk()
    except tk.TclError:
        pytest.skip("no display")
    root.withdraw()
    yield root
    root.destroy()


def pump(root, seconds=0.5, until=None):
    """Run a real Tk main loop (worker threads may only call Tk while it runs, like the app's UI thread)."""
    end = time.time() + seconds
    result = {"ok": False}

    def tick():
        if until and until():
            result["ok"] = True
            root.quit()
        elif time.time() >= end:
            root.quit()
        else:
            root.after(10, tick)

    root.after(0, tick)
    root.mainloop()
    return result["ok"] if until else True


class FakePack:
    def __init__(self):
        self.installed = False
        self.enabled = True
        self.active = False
        self.cancelled = False
        self.fail_with = None
        self.release = threading.Event()
        self.release.set()
        self.removed = []

    def info(self):
        return {
            "installed": self.installed, "compatible": self.installed, "active": self.active and self.enabled,
            "enabled": self.enabled, "reason": "Ready.", "removal_pending": False, "note": "",
            "download_mb": 25, "disk_mb": 77, "ram_mb": 40,
        }

    def download(self, progress, done):
        def work():
            for i in range(1, 5):
                if self.cancelled:
                    done("Download cancelled.")
                    return
                progress(i * 25, 100)
                self.release.wait(5)
            if self.fail_with:
                done(self.fail_with)
                return
            self.installed = self.active = True
            done(None)

        threading.Thread(target=work, daemon=True).start()

    def cancel(self):
        self.cancelled = True

    def set_enabled(self, value, done):
        def work():
            self.enabled = value
            done(None)

        threading.Thread(target=work, daemon=True).start()

    def remove(self):
        self.installed = self.active = False
        self.removed.append(1)
        return "The pack was removed."

    def hooks(self):
        return SettingsHooks(
            on_apply=lambda d: None, pack_info=self.info, pack_download=self.download,
            pack_cancel=self.cancel, pack_set_enabled=self.set_enabled, pack_remove=self.remove,
        )


def make_data():
    return SettingsData(
        version="9.9.9",
        hotkey=parse_hotkey(DEFAULT_HOTKEY),
        hotkey_all=parse_hotkey(DEFAULT_HOTKEY_ALL),
        hotkey_translate=parse_hotkey(DEFAULT_HOTKEY_TRANSLATE),
    )


@pytest.fixture
def window(tk_root):
    fake = FakePack()
    win = SettingsWindow(make_data(), fake.hooks(), master=tk_root)
    win.root.withdraw()
    pump(tk_root, 0.1)
    yield win, fake
    win._close()


# ------------------------------------------------------------------ Gemini key help (text + link only)


def test_friendly_key_errors_are_plain_language():
    assert "did not accept" in friendly_key_error("HTTP 400: API key not valid. Please pass a valid API key.")
    assert "did not accept" in friendly_key_error("HTTP 403 permission denied")
    assert "free allowance" in friendly_key_error("HTTP 429 RESOURCE_EXHAUSTED quota")
    assert "internet" in friendly_key_error("<urlopen error [Errno 11001] getaddrinfo failed>")
    assert "internet" in friendly_key_error("The request timed out")
    assert friendly_key_error("something odd") == "something odd"
    assert friendly_key_error("") == ""


@pytest.mark.parametrize(
    "key, expect",
    [("", ""), ("   ", ""), ("abc", "too short"), ("A" * 39, "Looks like a key"),
     ("AAAA BBBB" + "C" * 20, "spaces in it"), (" " + "A" * 39, "around the key")],
)
def test_key_hint(key, expect):
    assert expect in key_hint(key)


def test_key_help_is_steps_and_link_not_images(window):
    win, _ = window
    assert str(win.key_steps.winfo_manager()) == "pack"  # Google is the default provider
    assert "Create API key" in settings_ui.KEY_STEPS and "Test" in settings_ui.KEY_STEPS
    assert settings_ui.AISTUDIO_KEY_URL.startswith("https://aistudio.google.com/")
    # Text plus link only: no image widgets in the help.
    def walk(widget):
        yield widget
        for child in widget.winfo_children():
            yield from walk(child)

    for w in walk(win.key_steps):
        if w.winfo_class() in ("TLabel", "Label"):
            assert str(w.cget("image")) == ""


def test_switching_provider_swaps_the_help(window):
    win, _ = window
    win._provider_display.set(settings_ui.PROVIDER_LABELS[settings_ui.PROVIDER_OPENAI])
    win._on_provider()
    assert win.key_steps.winfo_manager() == ""
    assert win.key_help.winfo_manager() == "pack"
    win._provider_display.set(settings_ui.PROVIDER_LABELS[settings_ui.PROVIDER_GOOGLE])
    win._on_provider()
    assert win.key_steps.winfo_manager() == "pack"


def test_pasting_a_key_updates_the_hint_immediately(window):
    win, _ = window
    win.api_key.set("short")
    assert "too short" in win.key_hint.get()
    win.api_key.set("A" * 39)
    assert "Looks like a key" in win.key_hint.get()


# ------------------------------------------------------------------ offline pack tab


def test_tab_shows_sizes_and_not_installed(window):
    win, _ = window
    assert "25 MB" in win.pack_sizes.get() and "77 MB" in win.pack_sizes.get() and "40 MB" in win.pack_sizes.get()
    assert "Not installed" in win.pack_status.get()
    assert str(win.pack_download_btn.cget("state")) == "normal"
    assert str(win.pack_remove_btn.cget("state")) == "disabled"
    assert str(win.pack_use.cget("state")) == "disabled"


def test_download_updates_progress_then_enables_controls(window, tk_root):
    win, fake = window
    fake.release.clear()
    win._pack_download_clicked()
    assert win.pack_download_btn.cget("text") == "Cancel"
    fake.release.set()
    assert pump(tk_root, 5, until=lambda: not win._pack_busy)
    assert fake.installed
    assert "in use" in win.pack_status.get()
    assert str(win.pack_remove_btn.cget("state")) == "normal"
    assert str(win.pack_use.cget("state")) == "normal"
    assert win.pack_download_btn.cget("text") == "Download and install"


def test_download_error_is_shown_and_ui_is_usable_again(window, tk_root):
    win, fake = window
    fake.fail_with = "Could not reach the download page. Check your internet and try again."
    win._pack_download_clicked()
    assert pump(tk_root, 5, until=lambda: not win._pack_busy)
    assert "internet" in win.pack_status.get()
    assert str(win.pack_download_btn.cget("state")) == "normal"
    assert not fake.installed


def test_cancel_during_download(window, tk_root):
    win, fake = window
    fake.release.clear()
    win._pack_download_clicked()
    win._pack_cancel_clicked()
    fake.release.set()
    assert pump(tk_root, 5, until=lambda: not win._pack_busy)
    assert fake.cancelled and not fake.installed
    assert "cancelled" in win.pack_status.get().lower()


def test_toggle_off_and_on(window, tk_root):
    win, fake = window
    fake.installed = fake.active = True
    win._refresh_pack()
    win.pack_enabled.set(False)
    win._pack_toggle_clicked()
    assert pump(tk_root, 3, until=lambda: not win._pack_busy)
    assert fake.enabled is False
    assert "switched off" in win.pack_status.get()
    win.pack_enabled.set(True)
    win._pack_toggle_clicked()
    assert pump(tk_root, 3, until=lambda: not win._pack_busy)
    assert fake.enabled is True


def test_remove_asks_first(window, tk_root, monkeypatch):
    import tkinter.messagebox as mb

    win, fake = window
    fake.installed = True
    win._refresh_pack()
    monkeypatch.setattr(mb, "askyesno", lambda *a, **k: False)
    win._pack_remove_clicked()
    assert not fake.removed
    monkeypatch.setattr(mb, "askyesno", lambda *a, **k: True)
    win._pack_remove_clicked()
    assert fake.removed and "removed" in win.pack_status.get()


def test_closing_the_window_during_download_does_not_crash(tk_root):
    fake = FakePack()
    fake.release.clear()
    win = SettingsWindow(make_data(), fake.hooks(), master=tk_root)
    win.root.withdraw()
    win._pack_download_clicked()
    win.root.destroy()  # the worker thread still calls back into a dead window
    win._closed = True
    fake.release.set()
    time.sleep(0.5)
    pump(tk_root, 0.3)  # would raise TclError/RuntimeError if callbacks were not guarded


def test_tab_hidden_without_pack_hooks(tk_root):
    win = SettingsWindow(make_data(), SettingsHooks(on_apply=lambda d: None), master=tk_root)
    try:
        titles = [win.notebook.tab(i, "text") for i in range(win.notebook.index("end"))]
        assert "Offline engine" not in titles
    finally:
        win._close()


# ------------------------------------------------------------------ first-run offer


def test_offer_download_flow_closes_itself(tk_root):
    fake = FakePack()
    win = open_pack_offer(fake.info(), fake.hooks(), tk_root)
    try:
        buttons = [w for w in _all(win) if w.winfo_class() == "TButton"]
        go = next(b for b in buttons if "Download" in b.cget("text"))
        go.invoke()
        assert pump(tk_root, 6, until=lambda: not _exists(win))
        assert fake.installed
    finally:
        if _exists(win):
            win.destroy()


def test_offer_not_now_and_failure_retry(tk_root):
    fake = FakePack()
    declined = []
    win = open_pack_offer(fake.info(), fake.hooks(), tk_root, on_decline=lambda: declined.append(1))
    later = next(w for w in _all(win) if w.winfo_class() == "TButton" and w.cget("text") == "Not now")
    later.invoke()
    assert declined == [1] and not _exists(win)

    fake2 = FakePack()
    fake2.fail_with = "Download failed."
    win2 = open_pack_offer(fake2.info(), fake2.hooks(), tk_root)
    try:
        go = next(w for w in _all(win2) if w.winfo_class() == "TButton" and "Download" in w.cget("text"))
        go.invoke()
        assert pump(tk_root, 6, until=lambda: go.cget("text") == "Try again")
    finally:
        if _exists(win2):
            win2.destroy()


def _all(widget):
    yield widget
    for child in widget.winfo_children():
        yield from _all(child)


def _exists(widget) -> bool:
    try:
        return bool(widget.winfo_exists())
    except tk.TclError:
        return False


# ------------------------------------------------------------------ layout and keyboard (Apple HIG-inspired polish)


def test_base_url_is_hidden_for_google_and_shown_for_openai_style(window):
    win, _ = window
    assert win.base_entry.winfo_manager() == "" and win.base_label.winfo_manager() == ""
    win._provider_display.set(settings_ui.PROVIDER_LABELS[settings_ui.PROVIDER_OPENAI])
    win._on_provider()
    assert win.base_entry.winfo_manager() == "grid" and win.base_label.winfo_manager() == "grid"
    assert str(win.base_entry.cget("state")) == "normal"


def test_online_tab_fits_in_the_default_window(tk_root):
    """The Online AI tab must show its last row ("Use online model") without scrolling or clipping."""
    win = SettingsWindow(make_data(), FakePack().hooks(), master=tk_root)
    try:
        win.root.deiconify()
        for i in range(win.notebook.index("end")):
            if win.notebook.tab(i, "text") == "Online AI":
                win.notebook.select(i)
        for _ in range(20):
            tk_root.update()
            time.sleep(0.01)
        tab_bottom = win.notebook.winfo_rooty() + win.notebook.winfo_height()
        checkbox_bottom = win.use_check.winfo_rooty() + win.use_check.winfo_height()
        assert checkbox_bottom <= tab_bottom, f"clipped by {checkbox_bottom - tab_bottom}px"
    finally:
        win._close()


class _Key:
    def __init__(self, widget, keysym):
        self.widget, self.keysym, self.state = widget, keysym, 0


def test_escape_cancels_and_return_confirms_like_a_default_button(window, monkeypatch):
    win, _ = window
    calls = []
    monkeypatch.setattr(win, "_cancel", lambda: calls.append("cancel"))
    monkeypatch.setattr(win, "_ok", lambda: calls.append("ok"))
    win._on_escape(_Key(win.root, "Escape"))
    win._on_return(_Key(win.key_entry, "Return"))
    assert calls == ["cancel", "ok"]


def test_return_is_left_alone_in_text_boxes_and_buttons(window, monkeypatch):
    win, _ = window
    calls = []
    monkeypatch.setattr(win, "_ok", lambda: calls.append("ok"))
    text = tk.Text(win.root)
    for widget in (text, win.pack_download_btn):
        win._on_return(_Key(widget, "Return"))
    assert calls == []


def test_escape_on_the_skill_page_steps_back_instead_of_closing_settings(window, monkeypatch):
    win, _ = window
    calls = []
    monkeypatch.setattr(win, "_cancel", lambda: calls.append("cancel"))
    win.skills_page.open_editor(0)
    assert win.skills_page.is_editing
    win._on_escape(_Key(win.root, "Escape"))
    assert not win.skills_page.is_editing and calls == []
    win._on_escape(_Key(win.root, "Escape"))
    assert calls == ["cancel"]


def test_return_on_the_skill_page_does_not_press_ok(window, monkeypatch):
    win, _ = window
    calls = []
    monkeypatch.setattr(win, "_ok", lambda: calls.append("ok"))
    win.skills_page.open_editor(0)
    win._on_return(_Key(win.skills_page.name_entry, "Return"))
    assert calls == []


def test_ok_button_is_the_primary_style_when_the_theme_has_one(window):
    win, _ = window
    ok = next(w for w in _all(win.root) if w.winfo_class() == "TButton" and w.cget("text") == "OK")
    expected = "Accent.TButton" if settings_ui._has_style("Accent.TButton") else "TButton"
    assert str(ok.cget("style")) == expected
