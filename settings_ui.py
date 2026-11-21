"""TinyFix Settings window (ttk, with an optional sv-ttk theme)."""

from __future__ import annotations

import threading
import tkinter as tk
import webbrowser
from dataclasses import dataclass, field, replace
from tkinter import ttk
from typing import Callable

from hotkey_util import (
    DEFAULT_HOTKEY,
    DEFAULT_HOTKEY_ALL,
    DEFAULT_HOTKEY_TRANSLATE,
    KEY_CHOICES,
    Hotkey,
    disabled_hotkey,
    parse_hotkey,
)
from llm_client import (
    DEFAULT_TIMEOUT,
    LLMConfig,
    LLMError,
    PROVIDER_GOOGLE,
    PROVIDER_OPENAI,
    list_models,
    probe_connection,
    sanitize_error,
)
from prompts import DEFAULT_FIX_PROMPT, DEFAULT_TARGET_LANGUAGE, DEFAULT_TRANSLATE_PROMPT
from tinyfix_lib import SENSITIVITY_NAMES

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
    sensitivity: str = "cautious"
    llm: LLMConfig = field(default_factory=LLMConfig)
    last_update_check: str = ""


@dataclass
class SettingsHooks:
    on_apply: Callable[[SettingsData], None]
    on_check_updates: Callable[[], None] | None = None
    on_live_sensitivity: Callable[[str], None] | None = None


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


class SettingsWindow:
    def __init__(self, data: SettingsData, hooks: SettingsHooks) -> None:
        self.data = data
        self.hooks = hooks
        self._tested_ok = bool(data.llm.ready)
        self._loading = True
        self._record_target: tk.StringVar | None = None
        self._record_enabled: tk.BooleanVar | None = None
        self.root = tk.Tk()
        self.root.title("TinyFix Settings")
        self.root.minsize(640, 520)
        self.root.geometry("720x600+360+160")
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

        self._build_shortcuts()
        self._build_sensitivity()
        self._build_online()
        self._build_prompts()
        self._build_updates()
        self._build_about()

        buttons = ttk.Frame(shell)
        buttons.grid(row=1, column=0, sticky="e", pady=(16, 0))
        ttk.Button(buttons, text="Cancel", command=self._cancel).pack(side="right", padx=(8, 0))
        ttk.Button(buttons, text="OK", command=self._ok).pack(side="right")
        ttk.Button(buttons, text="Apply", command=self._apply).pack(side="right", padx=(0, 8))

        self._loading = False
        self._sync_use_online_state()
        self.root.bind("<KeyPress>", self._on_global_key)
        self.root.protocol("WM_DELETE_WINDOW", self._cancel)
        try:
            self.root.focus_force()
        except tk.TclError:
            pass

    def _tab(self, title: str) -> ttk.Frame:
        frame = ttk.Frame(self.notebook, padding=16)
        self.notebook.add(frame, text=title)
        frame.columnconfigure(0, weight=1)
        return frame

    def _build_shortcuts(self) -> None:
        tab = self._tab("Shortcuts")
        ttk.Label(
            tab,
            text="Each shortcut can be turned off. Click Record and press a new combination, or choose None/Disabled.",
            wraplength=640,
        ).grid(row=0, column=0, sticky="w", pady=(0, 12))

        self.sel_enabled = tk.BooleanVar(value=not self.data.hotkey.is_disabled)
        self.all_enabled = tk.BooleanVar(value=not self.data.hotkey_all.is_disabled)
        self.tr_enabled = tk.BooleanVar(value=not self.data.hotkey_translate.is_disabled)
        self.sel_key = tk.StringVar(value=_format_hotkey(self.data.hotkey))
        self.all_key = tk.StringVar(value=_format_hotkey(self.data.hotkey_all))
        self.tr_key = tk.StringVar(value=_format_hotkey(self.data.hotkey_translate))
        self.translate_select_all = tk.BooleanVar(value=self.data.translate_select_all)
        self.record_status = tk.StringVar(value="")

        self._shortcut_row(tab, 1, "Fix selection", "Correct the highlighted text.", self.sel_enabled, self.sel_key)
        self._shortcut_row(tab, 2, "Fix all", "Select the whole field, then correct it.", self.all_enabled, self.all_key)
        self._shortcut_row(
            tab,
            3,
            "Translate",
            "Replace the selection with a translation. Only runs when an online model is enabled.",
            self.tr_enabled,
            self.tr_key,
        )
        ttk.Checkbutton(
            tab,
            text="Translate: select the whole field first",
            variable=self.translate_select_all,
        ).grid(row=4, column=0, sticky="w", pady=(8, 0))
        ttk.Label(tab, textvariable=self.record_status, foreground="#555555").grid(
            row=5, column=0, sticky="w", pady=(12, 0)
        )

    def _shortcut_row(
        self,
        tab: ttk.Frame,
        row: int,
        title: str,
        hint: str,
        enabled: tk.BooleanVar,
        key_var: tk.StringVar,
    ) -> None:
        box = ttk.LabelFrame(tab, text=title, padding=10)
        box.grid(row=row, column=0, sticky="ew", pady=(0, 10))
        box.columnconfigure(1, weight=1)
        ttk.Checkbutton(box, text="Enabled", variable=enabled).grid(row=0, column=0, sticky="w")
        ttk.Entry(box, textvariable=key_var, state="readonly", width=28).grid(row=0, column=1, sticky="ew", padx=8)
        ttk.Button(box, text="Record", command=lambda: self._start_record(key_var, enabled)).grid(row=0, column=2)
        ttk.Button(box, text="Disable", command=lambda: self._disable_row(key_var, enabled)).grid(row=0, column=3, padx=(8, 0))
        ttk.Label(box, text=hint, foreground="#555555").grid(row=1, column=0, columnspan=4, sticky="w", pady=(6, 0))

    def _disable_row(self, key_var: tk.StringVar, enabled: tk.BooleanVar) -> None:
        enabled.set(False)
        key_var.set(DISABLED_LABEL)
        self.record_status.set("")
        self._record_target = None

    def _start_record(self, key_var: tk.StringVar, enabled: tk.BooleanVar) -> None:
        self._record_target = key_var
        self._record_enabled = enabled
        self.record_status.set("Press the new shortcut, or Esc to cancel.")
        self.root.focus_force()

    def _on_global_key(self, event: tk.Event) -> None:
        if self._record_target is None:
            return
        if event.keysym in _MOD_KEYSYMS:
            return
        if event.keysym in {"Escape"}:
            self._record_target = None
            self.record_status.set("Recording cancelled.")
            return
        parts: list[str] = []
        state = int(event.state)
        if state & 0x4:
            parts.append("Ctrl")
        if state & 0x20000 or state & 0x8:
            parts.append("Alt")
        if state & 0x1:
            parts.append("Shift")
        if state & 0x40000:
            parts.append("Win")
        key = event.keysym
        mapping = {
            "space": "Space",
            "Return": "Enter",
            "Escape": "Esc",
            "BackSpace": "Backspace",
            "Prior": "PageUp",
            "Next": "PageDown",
            "plus": "Plus",
            "minus": "Minus",
        }
        if key.startswith("F") and key[1:].isdigit():
            name = key
        elif key in mapping:
            name = mapping[key]
        elif len(key) == 1:
            name = key.upper()
        else:
            name = key
        parts.append(name)
        spec = "+".join(parts)
        try:
            parsed = parse_hotkey(spec)
        except ValueError:
            self.record_status.set(f"Could not use {spec}. Try again.")
            return
        if not parsed.is_safe():
            self.record_status.set("Please include a modifier (Ctrl, Alt, Shift, or Win) unless this is a function key.")
            return
        self._record_target.set(parsed.display)
        if self._record_enabled is not None:
            self._record_enabled.set(True)
        self.record_status.set(f"Set to {parsed.display}.")
        self._record_target = None
        self._record_enabled = None

    def _build_sensitivity(self) -> None:
        tab = self._tab("Sensitivity")
        self.sensitivity = tk.StringVar(value=self.data.sensitivity)
        descriptions = {
            "cautious": "Default. Conservative thresholds; prefers to leave a word alone.",
            "balanced": "A little more willing to fix spelling and real-word mix-ups.",
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
        self.model = tk.StringVar(value=self.data.llm.model or "")
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

        self.key_help = ttk.Label(tab, text="Get a Gemini API key from Google AI Studio", foreground="#0b57d0", cursor="hand2")
        self.key_help.grid(row=3, column=1, sticky="w")
        self.key_help.bind("<Button-1>", lambda *_: webbrowser.open(AISTUDIO_KEY_URL))

        ttk.Label(tab, text="Base URL").grid(row=4, column=0, sticky="w")
        self.base_entry = ttk.Entry(tab, textvariable=self.base_url)
        self.base_entry.grid(row=4, column=1, columnspan=2, sticky="ew", pady=4)
        self.base_url.trace_add("write", lambda *_: self._invalidate_test())

        ttk.Label(tab, text="Model").grid(row=5, column=0, sticky="w")
        self.model_box = ttk.Combobox(tab, textvariable=self.model, values=())
        self.model_box.grid(row=5, column=1, sticky="ew", pady=4)
        ttk.Button(tab, text="Refresh models", command=self._refresh_models).grid(row=5, column=2, padx=(8, 0))
        self.model.trace_add("write", lambda *_: self._invalidate_test())

        ttk.Label(tab, text="Timeout (seconds)").grid(row=6, column=0, sticky="w")
        ttk.Spinbox(tab, from_=1, to=30, textvariable=self.timeout, width=8).grid(row=6, column=1, sticky="w", pady=4)

        ttk.Button(tab, text="Test", command=self._test_clicked).grid(row=7, column=0, sticky="w", pady=(12, 0))
        ttk.Label(tab, textvariable=self.online_status, wraplength=500).grid(row=7, column=1, columnspan=2, sticky="w", pady=(12, 0))

        self.use_check = ttk.Checkbutton(tab, text="Use online model", variable=self.use_online)
        self.use_check.grid(row=8, column=0, columnspan=3, sticky="w", pady=(12, 0))
        self._sync_use_online_state()
        self._on_provider()

    def _on_provider(self, _event=None) -> None:
        label = self._provider_display.get()
        for key, value in PROVIDER_LABELS.items():
            if value == label:
                self.provider.set(key)
        is_openai = self.provider.get() == PROVIDER_OPENAI
        state = "normal" if is_openai else "disabled"
        self.base_entry.configure(state=state)
        if is_openai:
            self.key_help.configure(text="Works with OpenAI, OpenRouter, Groq, Ollama, LM Studio, and other /v1 APIs")
        else:
            self.key_help.configure(text="Get a Gemini API key from Google AI Studio")
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
            base_url=self.base_url.get().strip(),
            timeout=timeout,
            enabled=bool(self.use_online.get()),
            fix_prompt=self.data.llm.fix_prompt,
            translate_prompt=self.data.llm.translate_prompt,
            target_language=self.data.llm.target_language or DEFAULT_TARGET_LANGUAGE,
        )
        if include_prompts and hasattr(self, "fix_prompt"):
            config.fix_prompt = self.fix_prompt.get("1.0", "end").rstrip("\n") + "\n"
            config.translate_prompt = self.translate_prompt.get("1.0", "end").rstrip("\n") + "\n"
            config.target_language = self.target_language.get().strip() or DEFAULT_TARGET_LANGUAGE
        return config

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
                self.model_box.configure(values=names)
                if names and self.model.get() not in names:
                    self.model.set(names[0])
                self.online_status.set(f"Found {len(names)} model(s)." if names else "No models were returned. You can type a model name.")
            self.root.after(0, done)

        threading.Thread(target=work, name="tinyfix-list-models", daemon=True).start()

    def _test_clicked(self) -> None:
        self.online_status.set("Testing…")

        def work() -> None:
            error = ""
            sample = ""
            try:
                sample = probe_connection(self._current_llm(include_prompts=False), timeout=8)
            except Exception as exc:
                error = sanitize_error(str(exc), self.api_key.get().strip())
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

    def _build_prompts(self) -> None:
        tab = self._tab("Prompts")
        tab.rowconfigure(1, weight=1)
        tab.rowconfigure(4, weight=1)
        tab.columnconfigure(0, weight=1)
        ttk.Label(tab, text="Use {{content}} for the selected text. If you omit it, the text is added at the end. Translate prompts may also use {{language}}.", wraplength=640).grid(
            row=0, column=0, columnspan=2, sticky="w", pady=(0, 8)
        )
        ttk.Label(tab, text="Correction prompt").grid(row=1, column=0, sticky="nw")
        self.fix_prompt = tk.Text(tab, height=8, wrap="word", undo=True)
        self.fix_prompt.grid(row=1, column=0, sticky="nsew", pady=(18, 0))
        self.fix_prompt.insert("1.0", (self.data.llm.fix_prompt or DEFAULT_FIX_PROMPT).rstrip() + "\n")
        ttk.Button(tab, text="Reset to default", command=lambda: self._reset_text(self.fix_prompt, DEFAULT_FIX_PROMPT)).grid(
            row=2, column=0, sticky="e", pady=6
        )
        lang_row = ttk.Frame(tab)
        lang_row.grid(row=3, column=0, sticky="ew", pady=(4, 4))
        ttk.Label(lang_row, text="Target language").pack(side="left")
        self.target_language = tk.StringVar(value=self.data.llm.target_language or DEFAULT_TARGET_LANGUAGE)
        ttk.Entry(lang_row, textvariable=self.target_language, width=24).pack(side="left", padx=(8, 0))
        ttk.Label(tab, text="Translate prompt").grid(row=4, column=0, sticky="nw")
        self.translate_prompt = tk.Text(tab, height=8, wrap="word", undo=True)
        self.translate_prompt.grid(row=4, column=0, sticky="nsew", pady=(18, 0))
        self.translate_prompt.insert("1.0", (self.data.llm.translate_prompt or DEFAULT_TRANSLATE_PROMPT).rstrip() + "\n")
        ttk.Button(tab, text="Reset to default", command=lambda: self._reset_text(self.translate_prompt, DEFAULT_TRANSLATE_PROMPT)).grid(
            row=5, column=0, sticky="e", pady=6
        )

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
        ttk.Label(tab, text="The built-in model is paperweight/tinyfix by Chris Ahlgren, bundled at build time. Optional online correction is yours to enable and never required.", wraplength=640).grid(
            row=2, column=0, sticky="w", pady=(8, 0)
        )
        link = ttk.Label(tab, text=GITHUB_URL, foreground="#0b57d0", cursor="hand2")
        link.grid(row=3, column=0, sticky="w", pady=(12, 0))
        link.bind("<Button-1>", lambda *_: webbrowser.open(GITHUB_URL))
        ttk.Label(tab, text="After install, text stays on your computer unless you enable an online model. API keys are stored with Windows DPAPI and are never written to config.ini or the log.", wraplength=640, foreground="#555555").grid(
            row=4, column=0, sticky="w", pady=(16, 0)
        )

    def _collect(self) -> SettingsData:
        llm = self._current_llm(include_prompts=True)
        return SettingsData(
            version=self.data.version,
            hotkey=_hotkey_from_row(self.sel_enabled.get(), self.sel_key.get(), DEFAULT_HOTKEY),
            hotkey_all=_hotkey_from_row(self.all_enabled.get(), self.all_key.get(), DEFAULT_HOTKEY_ALL),
            hotkey_translate=_hotkey_from_row(self.tr_enabled.get(), self.tr_key.get(), DEFAULT_HOTKEY_TRANSLATE),
            translate_select_all=bool(self.translate_select_all.get()),
            sensitivity=self.sensitivity.get(),
            llm=llm,
            last_update_check=self.data.last_update_check,
        )

    def _apply(self) -> bool:
        data = self._collect()
        if not data.hotkey.is_disabled and not data.hotkey_all.is_disabled and data.hotkey.display == data.hotkey_all.display:
            self.record_status.set("Please choose different shortcuts for Fix selection and Fix all.")
            self.notebook.select(0)
            return False
        self.hooks.on_apply(data)
        self.data = data
        return True

    def _ok(self) -> None:
        if self._apply():
            self._close()

    def _cancel(self) -> None:
        self._close()

    def _close(self) -> None:
        global _open_window
        try:
            self.root.destroy()
        finally:
            _open_window = None


def open_settings(data: SettingsData, hooks: SettingsHooks) -> None:
    global _open_window
    if _open_window is not None:
        try:
            _open_window.deiconify()
            _open_window.lift()
            _open_window.focus_force()
            return
        except tk.TclError:
            _open_window = None
    window = SettingsWindow(data, hooks)
    _open_window = window.root
    window.root.mainloop()
    _open_window = None
