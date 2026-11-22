"""TinyFix Settings window (ttk, with an optional sv-ttk theme)."""

from __future__ import annotations

import logging
import threading
import tkinter as tk
import webbrowser
from dataclasses import dataclass, field, replace
from tkinter import ttk
from typing import Callable

from skills_page import SkillsPage, SkillsPageHooks
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
from llm_client import (
    DEFAULT_GOOGLE_MODEL,
    DEFAULT_TIMEOUT,
    LLMConfig,
    LLMError,
    PROVIDER_GOOGLE,
    PROVIDER_OPENAI,
    find_best_models,
    list_models,
    probe_connection,
    sanitize_error,
)
from prompts import DEFAULT_FIX_PROMPT, DEFAULT_TARGET_LANGUAGE, DEFAULT_TRANSLATE_PROMPT, default_prompt_for_skill
from skills import (
    INPUT_SELECT_ALL,
    INPUT_SELECTION,
    SKILL_FIX,
    SKILL_FIX_ALL,
    SKILL_REWRITE,
    SKILL_REWRITE_LANGUAGE,
    SKILL_IMPROVE_PROMPT,
    SKILL_TRANSLATE,
    Skill,
    default_skills,
    duplicate_skill,
    find_conflicts,
    new_custom_skill,
    parse_skill_hotkey,
)
from tinyfix_lib import DEFAULT_SENSITIVITY, SENSITIVITY_NAMES

TARGET_LANGUAGE_CHOICES = (
    "English",
    "Persian",
    "German",
    "French",
    "Spanish",
    "Arabic",
    "Italian",
    "Portuguese",
    "Dutch",
    "Turkish",
    "Japanese",
    "Chinese",
    "Korean",
    "Hindi",
    "Russian",
)

AISTUDIO_KEY_URL = "https://aistudio.google.com/apikey"
GITHUB_URL = "https://github.com/Aryan-mor/tinyfix-hotkey"
DISABLED_LABEL = "None/Disabled"
KEY_CHOICES_WITH_DISABLED = (DISABLED_LABEL,) + KEY_CHOICES
PROVIDER_LABELS = {
    PROVIDER_GOOGLE: "Google AI Studio (Gemini)",
    PROVIDER_OPENAI: "OpenAI-compatible",
}


@dataclass
class SettingsData:
    version: str
    hotkey: Hotkey
    hotkey_all: Hotkey
    hotkey_translate: Hotkey
    translate_select_all: bool = False
    sensitivity: str = DEFAULT_SENSITIVITY
    llm: LLMConfig = field(default_factory=LLMConfig)
    last_update_check: str = ""
    skills: list[Skill] = field(default_factory=default_skills)
    skill_menu_hotkey: Hotkey = field(default_factory=lambda: parse_hotkey(DEFAULT_HOTKEY_MENU))
    dismissed_skills: list[str] = field(default_factory=list)


@dataclass
class SettingsHooks:
    on_apply: Callable[[SettingsData], None]
    on_check_updates: Callable[[], None] | None = None
    on_live_sensitivity: Callable[[str], None] | None = None
    on_test_skill: Callable[[Skill, str], str] | None = None
    on_recording: Callable[[bool], None] | None = None  # pause/resume the global shortcuts while one is chosen
    # Offline pack (all optional; the tab is hidden when pack_info is missing).
    pack_info: Callable[[], dict] | None = None
    pack_download: Callable[[Callable[[int, int], None], Callable[[str | None], None]], None] | None = None
    pack_cancel: Callable[[], None] | None = None
    pack_set_enabled: Callable[[bool, Callable[[str | None], None]], None] | None = None
    pack_remove: Callable[[], str] | None = None


_open_window: tk.Tk | None = None

_MOD_KEYSYMS = {
    "Shift_L",
    "Shift_R",
    "Control_L",
    "Control_R",
    "Alt_L",
    "Alt_R",
    "Meta_L",
    "Meta_R",
    "Win_L",
    "Win_R",
    "Super_L",
    "Super_R",
}


def _apply_theme(root: tk.Tk) -> None:
    try:
        import sv_ttk

        sv_ttk.set_theme("light")
        return
    except Exception:
        pass
    try:
        style = ttk.Style(root)
        if "vista" in style.theme_names():
            style.theme_use("vista")
        elif "clam" in style.theme_names():
            style.theme_use("clam")
    except Exception:
        pass


def _hotkey_from_row(enabled: bool, display: str, default: str) -> Hotkey:
    if not enabled:
        return disabled_hotkey()
    try:
        parsed = parse_hotkey(display or default)
    except ValueError:
        return parse_hotkey(default)
    return parsed if parsed.is_safe() or parsed.is_disabled else parse_hotkey(default)


def _format_hotkey(hotkey: Hotkey) -> str:
    return DISABLED_LABEL if hotkey.is_disabled else hotkey.display


def _hotkey_spec_from_row(hk_enabled: bool, display: str, default: str = "none") -> str:
    hk = _hotkey_from_row(hk_enabled, display, default)
    return "none" if hk.is_disabled else hk.display


_BUILTIN_SKILL_IDS = frozenset(
    {SKILL_FIX, SKILL_FIX_ALL, SKILL_TRANSLATE, SKILL_REWRITE, SKILL_REWRITE_LANGUAGE, SKILL_IMPROVE_PROMPT}
)


def _has_style(name: str) -> bool:
    try:
        return bool(ttk.Style().layout(name))
    except tk.TclError:
        return False


def friendly_key_error(message: str) -> str:
    """Turn a provider error into one plain sentence a first-time user can act on."""
    text = (message or "").lower()
    if any(word in text for word in ("api key not valid", "api_key_invalid", "401", "403", "permission", "unauthor", "invalid key", "invalid api key")):
        return "Google did not accept this key. Copy it again from AI Studio and paste the whole key."
    if any(word in text for word in ("429", "quota", "rate limit", "resource_exhausted")):
        return "This key has used up its free allowance for now. Wait a bit and test again."
    if any(word in text for word in ("timed out", "timeout", "getaddrinfo", "urlopen", "name resolution", "unreachable", "connection")):
        return "Could not reach Google. Check your internet connection and test again."
    return message


def key_hint(key: str) -> str:
    """Instant, offline hint about a pasted key. Only the Test button can really confirm it."""
    raw = key or ""
    if not raw.strip():
        return ""
    if any(ch.isspace() for ch in raw.strip()):
        return "The key has spaces in it. Copy the whole key again."
    if raw != raw.strip():
        return "There are spaces around the key. TinyFix ignores them."
    if len(raw) < 20:
        return "That looks too short. Copy the whole key."
    return "Looks like a key. Click Test to check it."


KEY_STEPS = (
    "1.  Click “Open Google AI Studio” and sign in with a Google account.\n"
    "2.  Click “Create API key” (pick or create a project if it asks).\n"
    "3.  Copy the key it shows.\n"
    "4.  Paste it in the API key box above, then click Test."
)
KEY_PRIVACY = "Free to start. The key stays on this PC, encrypted by Windows, and is only sent to Google."


class SettingsWindow:
    def __init__(self, data: SettingsData, hooks: SettingsHooks, master: tk.Misc | None = None) -> None:
        self.data = data
        self.hooks = hooks
        self._tested_ok = bool(data.llm.ready)
        self._loading = True
        # Use a Toplevel when a dedicated UI thread already has a hidden root.
        # Creating a second Tk() + mainloop() on the pystray thread makes
        # controls appear but ignore clicks and never log a save.
        self.root = tk.Toplevel(master) if master is not None else tk.Tk()
        self._owns_mainloop = master is None
        self.root.title("TinyFix Settings")
        self.root.minsize(760, 620)
        self.root.geometry("800x700+320+60")
        try:
            self.root.attributes("-topmost", True)
            self.root.after(400, lambda: self.root.attributes("-topmost", False))
        except tk.TclError:
            pass
        _apply_theme(self.root)
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=1)

        shell = ttk.Frame(self.root, padding=16)
        shell.grid(row=0, column=0, sticky="nsew")
        shell.columnconfigure(0, weight=1)
        shell.rowconfigure(0, weight=1)

        self.notebook = ttk.Notebook(shell)
        self.notebook.grid(row=0, column=0, sticky="nsew")

        self.target_language = tk.StringVar(value=self.data.llm.target_language or DEFAULT_TARGET_LANGUAGE)
        self._build_skills()
        self._build_sensitivity()
        self._build_offline()
        self._build_online()
        self._build_updates()
        self._build_about()

        buttons = ttk.Frame(shell)
        buttons.grid(row=1, column=0, sticky="e", pady=(16, 0))
        ttk.Button(buttons, text="Cancel", command=self._cancel).pack(side="right", padx=(8, 0))
        ok_style = "Accent.TButton" if _has_style("Accent.TButton") else "TButton"
        ttk.Button(buttons, text="OK", command=self._ok, style=ok_style).pack(side="right")
        ttk.Button(buttons, text="Apply", command=self._apply).pack(side="right", padx=(0, 8))

        self._loading = False
        self._sync_use_online_state()
        self.root.bind("<Return>", self._on_return)
        self.root.bind("<Escape>", self._on_escape)
        self.root.protocol("WM_DELETE_WINDOW", self._cancel)
        try:
            self.root.focus_force()
        except tk.TclError:
            pass

    def _on_return(self, event) -> None:
        """Return confirms like the default button, except where Return has its own job."""
        if self.skills_page.is_editing:
            return  # on the skill page Return belongs to the form, not to OK
        if event.widget.winfo_class() in ("Text", "TButton", "Treeview", "Listbox", "TCombobox", "TCheckbutton", "TRadiobutton"):
            return
        self._ok()

    def _on_escape(self, _event) -> None:
        if self.skills_page.is_editing:
            self.skills_page.cancel_editing()  # Escape steps back from the skill page, it does not close Settings
            return
        self._cancel()

    def _tab(self, title: str) -> ttk.Frame:
        frame = ttk.Frame(self.notebook, padding=16)
        self.notebook.add(frame, text=title)
        frame.columnconfigure(0, weight=1)
        return frame

    def _build_sensitivity(self) -> None:
        tab = self._tab("Sensitivity")
        self.sensitivity = tk.StringVar(value=self.data.sensitivity)
        descriptions = {
            "cautious": "Conservative thresholds; prefers to leave a word alone.",
            "balanced": "Default. A little more willing to fix spelling and real-word mix-ups.",
            "aggressive": "Fixes more, including cases such as creat → create and fro → for when the engine can.",
        }
        ttk.Label(tab, text="This applies immediately, including while the Settings window is open.", wraplength=640).grid(
            row=0, column=0, sticky="w", pady=(0, 12)
        )
        for index, name in enumerate(SENSITIVITY_NAMES, start=1):
            ttk.Radiobutton(
                tab,
                text=name.capitalize(),
                value=name,
                variable=self.sensitivity,
                command=self._on_sensitivity,
            ).grid(row=index, column=0, sticky="w")
            ttk.Label(tab, text=descriptions[name], foreground="#555555", wraplength=620).grid(
                row=index, column=0, sticky="w", padx=(120, 0)
            )

    def _on_sensitivity(self) -> None:
        if self.hooks.on_live_sensitivity:
            self.hooks.on_live_sensitivity(self.sensitivity.get())

    # ------------------------------------------------------------------ offline pack tab

    def _pack(self) -> dict:
        try:
            return self.hooks.pack_info() if self.hooks.pack_info else {}
        except Exception:
            return {}

    def _build_offline(self) -> None:
        self._pack_busy = False
        self._closed = False
        if self.hooks.pack_info is None:
            return
        tab = self._tab("Offline engine")
        ttk.Label(
            tab,
            text="Basic offline fixes (spelling) are always on and need nothing extra.",
            font=("Segoe UI", 10, "bold"),
            wraplength=640,
        ).grid(row=0, column=0, sticky="w")
        ttk.Label(
            tab,
            text="The optional offline pack adds smarter, context-aware fixes, such as their / they’re. "
            "It is a separate download. You can turn it off or remove it at any time.",
            wraplength=640,
        ).grid(row=1, column=0, sticky="w", pady=(6, 10))
        info = self._pack()
        self.pack_sizes = tk.StringVar(value=self._pack_size_text(info))
        ttk.Label(tab, textvariable=self.pack_sizes, foreground="#555555", wraplength=640).grid(row=2, column=0, sticky="w")
        self.pack_status = tk.StringVar(value="")
        ttk.Label(tab, textvariable=self.pack_status, wraplength=640, font=("Segoe UI", 10, "bold")).grid(row=3, column=0, sticky="w", pady=(14, 6))
        self.pack_bar = ttk.Progressbar(tab, mode="determinate", maximum=100)
        self.pack_bar.grid(row=4, column=0, sticky="ew", pady=(0, 8))
        self.pack_bar.grid_remove()
        row = ttk.Frame(tab)
        row.grid(row=5, column=0, sticky="w")
        self.pack_download_btn = ttk.Button(row, text="Download and install", command=self._pack_download_clicked)
        self.pack_download_btn.pack(side="left")
        self.pack_remove_btn = ttk.Button(row, text="Remove files", command=self._pack_remove_clicked)
        self.pack_remove_btn.pack(side="left", padx=(8, 0))
        self.pack_enabled = tk.BooleanVar(value=bool(info.get("enabled", True)))
        self.pack_use = ttk.Checkbutton(tab, text="Use the offline pack", variable=self.pack_enabled, command=self._pack_toggle_clicked)
        self.pack_use.grid(row=6, column=0, sticky="w", pady=(12, 0))
        ttk.Label(
            tab,
            text="Turning it off goes back to basic fixes right away. Windows only gives the memory back "
            "fully the next time TinyFix starts.",
            foreground="#555555",
            wraplength=640,
        ).grid(row=7, column=0, sticky="w", pady=(8, 0))
        self._refresh_pack()

    @staticmethod
    def _pack_size_text(info: dict) -> str:
        return (
            f"Download about {info.get('download_mb', 25)} MB  •  "
            f"uses about {info.get('disk_mb', 77)} MB on disk  •  "
            f"about {info.get('ram_mb', 40)} MB more memory while it is on"
        )

    def _pack_after(self, fn) -> None:
        """Run fn on the Tk thread if the window is still open."""

        def guarded() -> None:
            if not self._closed:
                try:
                    fn()
                except tk.TclError:
                    pass

        try:
            self.root.after(0, guarded)
        except (tk.TclError, RuntimeError):
            pass

    def _refresh_pack(self) -> None:
        info = self._pack()
        installed = bool(info.get("installed"))
        compatible = bool(info.get("compatible"))
        busy = self._pack_busy
        if info.get("removal_pending"):
            text = "Removal is waiting for the next start of TinyFix."
        elif not installed:
            text = "Not installed. TinyFix is using basic offline fixes."
        elif not compatible:
            text = f"Installed, but it cannot be used: {info.get('reason', 'unknown problem')}"
        elif info.get("active"):
            text = "Installed and in use."
        elif not info.get("enabled", True):
            text = "Installed, switched off."
        else:
            text = "Installed. It will load the next time TinyFix starts."
            if info.get("note"):
                text = f"{text} ({info['note']})"
        if not busy:
            self.pack_status.set(text)
        self.pack_enabled.set(bool(info.get("enabled", True)) and installed and compatible)
        self.pack_download_btn.configure(
            text="Update the pack" if installed and not compatible else "Download and install",
            state="disabled" if busy or (installed and compatible) else "normal",
        )
        self.pack_remove_btn.configure(state="disabled" if busy or not installed else "normal")
        self.pack_use.configure(state="disabled" if busy or not (installed and compatible) else "normal")

    def _pack_download_clicked(self) -> None:
        if not self.hooks.pack_download or self._pack_busy:
            return
        self._pack_busy = True
        self.pack_bar.configure(value=0)
        self.pack_bar.grid()
        self.pack_status.set("Downloading…")
        self.pack_download_btn.configure(text="Cancel", command=self._pack_cancel_clicked, state="normal")
        self.pack_remove_btn.configure(state="disabled")
        self.pack_use.configure(state="disabled")

        def progress(done: int, total: int) -> None:
            percent = 100 * done / total if total else 0
            self._pack_after(lambda: (self.pack_bar.configure(value=percent), self.pack_status.set(f"Downloading… {percent:.0f}%")))

        def finished(error: str | None) -> None:
            def ui() -> None:
                self._pack_busy = False
                self.pack_bar.grid_remove()
                self.pack_download_btn.configure(text="Download and install", command=self._pack_download_clicked)
                self._refresh_pack()
                if error:
                    self.pack_status.set(error)

            self._pack_after(ui)

        self.hooks.pack_download(progress, finished)

    def _pack_cancel_clicked(self) -> None:
        if self.hooks.pack_cancel:
            self.hooks.pack_cancel()
        self.pack_status.set("Cancelling…")
        self.pack_download_btn.configure(state="disabled")

    def _pack_toggle_clicked(self) -> None:
        if not self.hooks.pack_set_enabled or self._pack_busy:
            return
        wanted = bool(self.pack_enabled.get())
        self._pack_busy = True
        self.pack_status.set("Switching…")
        self._refresh_pack()

        def finished(error: str | None) -> None:
            def ui() -> None:
                self._pack_busy = False
                self._refresh_pack()
                if error:
                    self.pack_status.set(error)

            self._pack_after(ui)

        self.hooks.pack_set_enabled(wanted, finished)

    def _pack_remove_clicked(self) -> None:
        if not self.hooks.pack_remove or self._pack_busy:
            return
        from tkinter import messagebox

        if not messagebox.askyesno("Remove offline pack", "Remove the offline pack files? You can download it again later.", parent=self.root):
            return
        message = self.hooks.pack_remove()
        self._refresh_pack()
        self.pack_status.set(message)

    def _build_online(self) -> None:
        tab = self._tab("Online AI")
        tab.columnconfigure(1, weight=1)
        ttk.Label(
            tab,
            text="Optional. Without a key, TinyFix keeps using the built-in offline engine. If an online request times out or fails, it falls back silently.",
            wraplength=640,
        ).grid(row=0, column=0, columnspan=3, sticky="w", pady=(0, 12))

        self.provider = tk.StringVar(value=self.data.llm.provider or PROVIDER_GOOGLE)
        self.api_key = tk.StringVar(value=self.data.llm.api_key or "")
        self.show_key = tk.BooleanVar(value=False)
        self.base_url = tk.StringVar(value=self.data.llm.base_url or "")
        initial_model = (self.data.llm.model or "").strip()
        if not initial_model and (self.data.llm.provider or PROVIDER_GOOGLE) == PROVIDER_GOOGLE:
            initial_model = DEFAULT_GOOGLE_MODEL
        self.model = tk.StringVar(value=initial_model)
        self.fallback_model = tk.StringVar(value=self.data.llm.fallback_model or "")
        self.timeout = tk.StringVar(value=str(int(self.data.llm.timeout or DEFAULT_TIMEOUT) if float(self.data.llm.timeout).is_integer() else self.data.llm.timeout))
        self.use_online = tk.BooleanVar(value=bool(self.data.llm.enabled and self.data.llm.api_key))
        self.online_status = tk.StringVar(value="")

        ttk.Label(tab, text="Provider").grid(row=1, column=0, sticky="w")
        provider_box = ttk.Combobox(
            tab,
            state="readonly",
            width=36,
            values=(PROVIDER_LABELS[PROVIDER_GOOGLE], PROVIDER_LABELS[PROVIDER_OPENAI]),
            textvariable=tk.StringVar(value=PROVIDER_LABELS.get(self.provider.get(), PROVIDER_LABELS[PROVIDER_GOOGLE])),
        )
        # Keep a private mapping: store provider id in self.provider, display labels in the combobox.
        self._provider_display = tk.StringVar(value=PROVIDER_LABELS.get(self.provider.get(), PROVIDER_LABELS[PROVIDER_GOOGLE]))
        provider_box.configure(textvariable=self._provider_display)
        provider_box.grid(row=1, column=1, columnspan=2, sticky="ew", pady=4)
        provider_box.bind("<<ComboboxSelected>>", self._on_provider)

        ttk.Label(tab, text="API key").grid(row=2, column=0, sticky="w")
        self.key_entry = ttk.Entry(tab, textvariable=self.api_key, show="•")
        self.key_entry.grid(row=2, column=1, sticky="ew", pady=4)
        ttk.Checkbutton(tab, text="Show", variable=self.show_key, command=self._toggle_key).grid(row=2, column=2, padx=(8, 0))
        self.api_key.trace_add("write", lambda *_: self._invalidate_test())

        # One cell holds the instant key hint plus either the Gemini steps or the OpenAI-style note.
        help_cell = ttk.Frame(tab)
        help_cell.grid(row=3, column=1, columnspan=2, sticky="ew")
        self.key_hint = tk.StringVar(value=key_hint(self.api_key.get()))
        ttk.Label(help_cell, textvariable=self.key_hint, foreground="#555555", wraplength=520).pack(anchor="w")
        self.api_key.trace_add("write", lambda *_: self.key_hint.set(key_hint(self.api_key.get())))
        self.key_help = ttk.Label(help_cell, text="", foreground="#555555", wraplength=520)
        self.key_steps = ttk.Frame(help_cell)
        ttk.Label(self.key_steps, text="How to get a Gemini API key", font=("Segoe UI", 10, "bold")).pack(anchor="w")
        ttk.Label(self.key_steps, text=KEY_STEPS, justify="left", wraplength=500).pack(anchor="w", pady=(4, 0))
        link_row = ttk.Frame(self.key_steps)
        link_row.pack(anchor="w", pady=(8, 0))
        ttk.Button(link_row, text="Open Google AI Studio", command=lambda: webbrowser.open(AISTUDIO_KEY_URL)).pack(side="left")
        ttk.Label(link_row, text=AISTUDIO_KEY_URL, foreground="#0b57d0", cursor="hand2").pack(side="left", padx=(10, 0))
        ttk.Label(self.key_steps, text=KEY_PRIVACY, foreground="#555555", wraplength=500, justify="left").pack(anchor="w", pady=(8, 0))

        self.base_label = ttk.Label(tab, text="Base URL")
        self.base_label.grid(row=4, column=0, sticky="w")
        self.base_entry = ttk.Entry(tab, textvariable=self.base_url)
        self.base_entry.grid(row=4, column=1, columnspan=2, sticky="ew", pady=4)
        self.base_url.trace_add("write", lambda *_: self._invalidate_test())

        ttk.Label(tab, text="Model").grid(row=5, column=0, sticky="w")
        self.model_box = ttk.Combobox(tab, textvariable=self.model, values=self._model_choices())
        self.model_box.grid(row=5, column=1, sticky="ew", pady=4)
        ttk.Button(tab, text="Refresh models", command=self._refresh_models).grid(row=5, column=2, padx=(8, 0))
        self.model.trace_add("write", lambda *_: self._invalidate_test())

        self.fallback_label = ttk.Label(tab, text="Fallback model")
        self.fallback_label.grid(row=6, column=0, sticky="w")
        self.fallback_box = ttk.Combobox(tab, textvariable=self.fallback_model, values=self._model_choices())
        self.fallback_box.grid(row=6, column=1, sticky="ew", pady=4)
        self.best_button = ttk.Button(tab, text="Find best models", command=self._find_best_clicked)
        self.best_button.grid(row=6, column=2, padx=(8, 0))

        ttk.Label(tab, text="Timeout (seconds)").grid(row=7, column=0, sticky="w")
        ttk.Spinbox(tab, from_=1, to=30, textvariable=self.timeout, width=8).grid(row=7, column=1, sticky="w", pady=4)
        ttk.Label(tab, text="Without the offline pack, TinyFix waits at least 10 seconds.", foreground="#555555").grid(row=7, column=2, sticky="w", padx=(8, 0))

        ttk.Button(tab, text="Test", command=self._test_clicked).grid(row=8, column=0, sticky="w", pady=(12, 0))
        ttk.Label(tab, textvariable=self.online_status, wraplength=500).grid(row=8, column=1, columnspan=2, sticky="w", pady=(12, 0))

        self.use_check = ttk.Checkbutton(tab, text="Use online model", variable=self.use_online)
        self.use_check.grid(row=9, column=0, columnspan=3, sticky="w", pady=(12, 0))
        self._sync_use_online_state()
        self._on_provider()

    def _on_provider(self, _event=None) -> None:
        label = self._provider_display.get()
        for key, value in PROVIDER_LABELS.items():
            if value == label:
                self.provider.set(key)
        is_openai = self.provider.get() == PROVIDER_OPENAI
        self.base_entry.configure(state="normal" if is_openai else "disabled")
        if is_openai:
            self.base_label.grid()
            self.base_entry.grid()
        else:
            self.base_label.grid_remove()
            self.base_entry.grid_remove()
        if is_openai:
            self.key_help.configure(text="Works with OpenAI, OpenRouter, Groq, Ollama, LM Studio, and other /v1 APIs")
            self.key_steps.pack_forget()
            self.key_help.pack(anchor="w", pady=(4, 0))
        else:
            self.key_help.pack_forget()
            self.key_steps.pack(anchor="w", fill="x", pady=(4, 8))
            if hasattr(self, "model_box"):
                self.model_box.configure(values=self._model_choices())
                if not (self.model.get() or "").strip():
                    self.model.set(DEFAULT_GOOGLE_MODEL)
        self._invalidate_test()

    def _toggle_key(self) -> None:
        self.key_entry.configure(show="" if self.show_key.get() else "•")

    def _invalidate_test(self) -> None:
        # Changing credentials requires a successful Test before enabling (or re-enabling) online mode.
        if getattr(self, "_loading", True):
            return
        self._tested_ok = False
        self.use_online.set(False)
        self._sync_use_online_state()

    def _sync_use_online_state(self) -> None:
        # Already-enabled configs stay available until the user changes credentials.
        allow = self._tested_ok or (self.data.llm.ready and self.api_key.get() == (self.data.llm.api_key or "")
                                    and self.provider.get() == self.data.llm.provider
                                    and self.model.get() == self.data.llm.model
                                    and self.base_url.get() == (self.data.llm.base_url or ""))
        self.use_check.configure(state="normal" if allow else "disabled")
        if not allow:
            self.use_online.set(False)

    def _current_llm(self, include_prompts: bool = True) -> LLMConfig:
        try:
            timeout = float(self.timeout.get() or DEFAULT_TIMEOUT)
        except ValueError:
            timeout = DEFAULT_TIMEOUT
        timeout = min(30.0, max(1.0, timeout))
        config = LLMConfig(
            provider=self.provider.get() or PROVIDER_GOOGLE,
            api_key=self.api_key.get().strip(),
            model=self.model.get().strip(),
            fallback_model=self.fallback_model.get().strip(),
            base_url=self.base_url.get().strip(),
            timeout=timeout,
            enabled=bool(self.use_online.get()),
            fix_prompt=self.data.llm.fix_prompt,
            translate_prompt=self.data.llm.translate_prompt,
            target_language=self.data.llm.target_language or DEFAULT_TARGET_LANGUAGE,
        )
        if include_prompts:
            by_id = {skill.id: skill for skill in self.skills}
            if SKILL_FIX in by_id:
                config.fix_prompt = by_id[SKILL_FIX].prompt
            if SKILL_TRANSLATE in by_id:
                config.translate_prompt = by_id[SKILL_TRANSLATE].prompt
            config.target_language = self.target_language.get().strip() or DEFAULT_TARGET_LANGUAGE
        return config

    def _model_choices(self, names: list[str] | None = None) -> list[str]:
        values: list[str] = []
        for name in names or []:
            item = (name or "").strip()
            if item and item not in values:
                values.append(item)
        if self.provider.get() == PROVIDER_GOOGLE and DEFAULT_GOOGLE_MODEL not in values:
            values.insert(0, DEFAULT_GOOGLE_MODEL)
        current = (self.model.get() if hasattr(self, "model") else "") or ""
        current = current.strip()
        if current and current not in values:
            values.append(current)
        return values

    def _refresh_models(self) -> None:
        self.online_status.set("Fetching models…")

        def work() -> None:
            error = ""
            names: list[str] = []
            try:
                names = list_models(self._current_llm(include_prompts=False), timeout=8)
            except Exception as exc:
                error = sanitize_error(str(exc), self.api_key.get().strip())
            def done() -> None:
                if error:
                    self.online_status.set(error)
                    return
                self.model_box.configure(values=self._model_choices(names))
                if not (self.model.get() or "").strip():
                    if self.provider.get() == PROVIDER_GOOGLE:
                        self.model.set(DEFAULT_GOOGLE_MODEL)
                    elif names:
                        self.model.set(names[0])
                self.online_status.set(f"Found {len(names)} model(s)." if names else "No models were returned. You can type a model name.")
            self.root.after(0, done)

        threading.Thread(target=work, name="tinyfix-list-models", daemon=True).start()

    def _find_best_clicked(self) -> None:
        if self.provider.get() != PROVIDER_GOOGLE:
            self.online_status.set("Finding the best models only works with Google.")
            return
        self.online_status.set("Timing Gemini models. This takes up to 30 seconds...")
        self.best_button.configure(state="disabled")

        def work() -> None:
            error = ""
            ranked: list[tuple[str, float]] = []
            try:
                ranked = find_best_models(self._current_llm(include_prompts=False))
            except Exception as exc:
                error = friendly_key_error(sanitize_error(str(exc), self.api_key.get().strip()))

            def done() -> None:
                self.best_button.configure(state="normal")
                if error:
                    self.online_status.set(error)
                    return
                best = ranked[0][0]
                backup = ranked[1][0] if len(ranked) > 1 else ""
                self.model.set(best)
                self.fallback_model.set(backup)
                names = [name for name, _ in ranked]
                self.model_box.configure(values=self._model_choices(names))
                self.fallback_box.configure(values=self._model_choices(names))
                summary = ", ".join(f"{n} {s:.1f}s" for n, s in ranked[:3])
                self.online_status.set(f"Main: {best}. Fallback: {backup or 'none found'}. ({summary}) Press Test, then Save.")
            self.root.after(0, done)

        threading.Thread(target=work, name="tinyfix-best-models", daemon=True).start()

    def _test_clicked(self) -> None:
        self.online_status.set("Testing…")

        def work() -> None:
            error = ""
            sample = ""
            try:
                sample = probe_connection(self._current_llm(include_prompts=False), timeout=8)
            except Exception as exc:
                error = sanitize_error(str(exc), self.api_key.get().strip())
                if self.provider.get() == PROVIDER_GOOGLE:
                    error = friendly_key_error(error)
            def done() -> None:
                if error:
                    self._tested_ok = False
                    self.online_status.set(error)
                    self._sync_use_online_state()
                    return
                self._tested_ok = True
                self.online_status.set("Test succeeded. You can now enable the online model.")
                self._sync_use_online_state()
            self.root.after(0, done)

        threading.Thread(target=work, name="tinyfix-llm-test", daemon=True).start()

    def _reset_text(self, widget: tk.Text, value: str) -> None:
        widget.delete("1.0", "end")
        widget.insert("1.0", value.rstrip() + "\n")

    def _build_updates(self) -> None:
        tab = self._tab("Updates")
        ttk.Label(tab, text=f"This version: v{self.data.version}", font=("Segoe UI", 11, "bold")).grid(row=0, column=0, sticky="w")
        last = self.data.last_update_check or "not yet"
        ttk.Label(tab, text=f"Last automatic check: {last}").grid(row=1, column=0, sticky="w", pady=(8, 12))
        ttk.Button(tab, text="Check for updates", command=self._check_updates).grid(row=2, column=0, sticky="w")
        ttk.Label(tab, text="Checks GitHub for a newer TinyFix-Setup.exe. Offline or rate-limited checks are retried later.", wraplength=640, foreground="#555555").grid(
            row=3, column=0, sticky="w", pady=(8, 0)
        )

    def _check_updates(self) -> None:
        if self.hooks.on_check_updates:
            self.hooks.on_check_updates()

    def _build_about(self) -> None:
        tab = self._tab("About")
        ttk.Label(tab, text=f"TinyFix Hotkey  v{self.data.version}", font=("Segoe UI", 14, "bold")).grid(row=0, column=0, sticky="w")
        ttk.Label(tab, text="Correct English typos from any Windows app, offline by default.", wraplength=640).grid(
            row=1, column=0, sticky="w", pady=(8, 0)
        )
        ttk.Label(tab, text="The optional offline pack uses the paperweight/tinyfix model by Chris Ahlgren. Optional online correction is yours to enable and never required.", wraplength=640).grid(
            row=2, column=0, sticky="w", pady=(8, 0)
        )
        link = ttk.Label(tab, text=GITHUB_URL, foreground="#0b57d0", cursor="hand2")
        link.grid(row=3, column=0, sticky="w", pady=(12, 0))
        link.bind("<Button-1>", lambda *_: webbrowser.open(GITHUB_URL))
        ttk.Label(tab, text="After install, text stays on your computer unless you enable an online model. API keys are stored with Windows DPAPI and are never written to config.ini or the log.", wraplength=640, foreground="#555555").grid(
            row=4, column=0, sticky="w", pady=(16, 0)
        )

    def _build_skills(self) -> None:
        tab = self._tab("Skills")
        tab.rowconfigure(0, weight=1)
        menu = self.data.skill_menu_hotkey
        self.skills_page = SkillsPage(
            tab,
            self.data.skills or default_skills(),
            "none" if menu.is_disabled else menu.display,
            self.data.dismissed_skills,
            self.target_language,
            TARGET_LANGUAGE_CHOICES,
            SkillsPageHooks(record_active=self.hooks.on_recording, test_skill=self.hooks.on_test_skill),
        )
        self.skills_page.grid(row=0, column=0, sticky="nsew")

    @property
    def skills(self) -> list[Skill]:
        return self.skills_page.skills

    def _collect(self) -> SettingsData:
        llm = self._current_llm(include_prompts=True)
        skills = [Skill.from_dict(skill.to_dict()) for skill in self.skills]
        by_id = {skill.id: skill for skill in skills}

        def hotkey_of(skill_id: str) -> Hotkey:
            skill = by_id.get(skill_id)
            return skill.hotkey_obj() if skill is not None else disabled_hotkey()

        translate = by_id.get(SKILL_TRANSLATE)
        return SettingsData(
            version=self.data.version,
            hotkey=hotkey_of(SKILL_FIX),
            hotkey_all=hotkey_of(SKILL_FIX_ALL),
            hotkey_translate=hotkey_of(SKILL_TRANSLATE),
            translate_select_all=bool(translate and translate.select_all),
            sensitivity=self.sensitivity.get(),
            llm=llm,
            last_update_check=self.data.last_update_check,
            skills=skills,
            skill_menu_hotkey=parse_skill_hotkey(self.skills_page.menu_hotkey),
            dismissed_skills=list(self.skills_page.dismissed),
        )

    def _show_skills_problem(self, message: str) -> None:
        self.skills_page.set_message(message)
        try:
            self.notebook.select(0)
        except tk.TclError:
            pass

    def _apply(self) -> bool:
        logging.info("Settings apply requested")
        if not self.skills_page.finish_editing():
            self.notebook.select(0)
            return False
        try:
            data = self._collect()
        except Exception:
            logging.exception("Settings could not be collected")
            self._show_skills_problem("Could not save settings. See tinyfix.log for details.")
            return False
        conflicts = find_conflicts(data.skills, data.skill_menu_hotkey)
        if conflicts:
            message = "Shortcut conflict: " + "; ".join(item.describe() for item in conflicts[:3])
            logging.info("Settings apply blocked: %s", message)
            self._show_skills_problem(message)
            return False
        try:
            self.hooks.on_apply(data)
        except Exception:
            logging.exception("Settings apply callback failed")
            self._show_skills_problem("Could not save settings. See tinyfix.log for details.")
            return False
        self.data = data
        logging.info("Settings apply finished")
        return True

    def _ok(self) -> None:
        logging.info("Settings OK clicked")
        if self._apply():
            self._close()

    def _cancel(self) -> None:
        logging.info("Settings cancelled")
        self._close()

    def _close(self) -> None:
        self._closed = True
        global _open_window
        try:
            self.root.destroy()
        finally:
            _open_window = None


def open_settings(data: SettingsData, hooks: SettingsHooks, master: tk.Misc | None = None) -> None:
    global _open_window
    logging.info("Opening the Settings window")
    if _open_window is not None:
        try:
            _open_window.deiconify()
            _open_window.lift()
            _open_window.focus_force()
            logging.info("Settings window already open; brought to front")
            return
        except tk.TclError:
            _open_window = None
    window = SettingsWindow(data, hooks, master=master)
    _open_window = window.root
    try:
        window.root.deiconify()
        window.root.lift()
        window.root.focus_force()
    except tk.TclError:
        logging.exception("Could not focus the Settings window")
    if window._owns_mainloop:
        window.root.mainloop()
        _open_window = None
    logging.info("Settings window is ready")


def open_pack_offer(info: dict, hooks: SettingsHooks, master: tk.Misc, on_decline: Callable[[], None] | None = None) -> tk.Toplevel:
    """First-run question: download the optional offline pack now, or keep the basic offline fixes."""
    win = tk.Toplevel(master)
    win.title("TinyFix — smarter offline fixes?")
    win.resizable(False, False)
    _apply_theme(win)
    state = {"busy": False, "closed": False}
    frame = ttk.Frame(win, padding=18)
    frame.grid(sticky="nsew")
    ttk.Label(frame, text="Want smarter offline fixes?", font=("Segoe UI", 13, "bold")).grid(sticky="w")
    ttk.Label(
        frame,
        text="TinyFix already fixes spelling offline. The optional offline pack also understands context, "
        "such as their / they’re. It is a separate download, and you can turn it off or remove it later in Settings.",
        wraplength=420,
        justify="left",
    ).grid(sticky="w", pady=(8, 8))
    ttk.Label(frame, text=SettingsWindow._pack_size_text(info), foreground="#555555", wraplength=420).grid(sticky="w")
    status = tk.StringVar(value="")
    ttk.Label(frame, textvariable=status, wraplength=420).grid(sticky="w", pady=(10, 4))
    bar = ttk.Progressbar(frame, mode="determinate", maximum=100, length=420)
    bar.grid(sticky="ew")
    bar.grid_remove()
    buttons = ttk.Frame(frame)
    buttons.grid(sticky="e", pady=(14, 0))
    later = ttk.Button(buttons, text="Not now")
    later.pack(side="right", padx=(8, 0))
    go = ttk.Button(buttons, text=f"Download ({info.get('download_mb', 25)} MB)")
    go.pack(side="right")

    def alive(fn):
        def guarded():
            if not state["closed"]:
                try:
                    fn()
                except tk.TclError:
                    pass

        try:
            win.after(0, guarded)
        except (tk.TclError, RuntimeError):
            pass

    def close() -> None:
        state["closed"] = True
        try:
            win.destroy()
        except tk.TclError:
            pass

    def decline() -> None:
        if state["busy"] and hooks.pack_cancel:
            hooks.pack_cancel()
        if on_decline and not state["busy"]:
            on_decline()
        close()

    def start() -> None:
        if state["busy"] or not hooks.pack_download:
            return
        state["busy"] = True
        go.configure(state="disabled")
        later.configure(text="Cancel")
        bar.grid()
        status.set("Downloading…")

        def progress(done: int, total: int) -> None:
            percent = 100 * done / total if total else 0
            alive(lambda: (bar.configure(value=percent), status.set(f"Downloading… {percent:.0f}%")))

        def finished(error: str | None) -> None:
            def ui() -> None:
                state["busy"] = False
                if error:
                    status.set(error)
                    go.configure(state="normal", text="Try again")
                    later.configure(text="Not now")
                    bar.grid_remove()
                else:
                    status.set("Done. Smarter offline fixes are on.")
                    win.after(1500, close)

            alive(ui)

        hooks.pack_download(progress, finished)

    go.configure(command=start)
    later.configure(command=decline)
    win.protocol("WM_DELETE_WINDOW", decline)
    try:
        # Come to the front once so the question is not hidden behind other apps, then behave normally.
        win.attributes("-topmost", True)
        win.after(600, lambda: win.attributes("-topmost", False) if win.winfo_exists() else None)
        win.lift()
        win.focus_force()
    except tk.TclError:
        pass
    return win
