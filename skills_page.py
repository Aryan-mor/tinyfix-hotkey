"""The Skills tab: one list of every skill, and a separate page to create or edit one.

List page: every skill in one list (ours and yours look the same). Each row can be switched on or off, moved,
edited, duplicated and, except Fix / Fix all / Translate, deleted. "New skill" is always at the top.
Edit page: name, on/off, input (select menu), shortcut (modal key capture), and the prompt.
"""

from __future__ import annotations

import threading
import tkinter as tk
from dataclasses import dataclass
from tkinter import messagebox, ttk
from typing import Callable

from hotkey_util import Hotkey, parse_hotkey
from prompts import DEFAULT_TARGET_LANGUAGE, default_prompt_for_skill
from shortcut_dialog import ShortcutDialog
from skills import (
    INPUT_SELECT_ALL,
    INPUT_SELECTION,
    OPTIONAL_BUILTIN_IDS,
    Skill,
    can_delete,
    duplicate_skill,
    new_custom_skill,
    parse_skill_hotkey,
)

INPUT_LABELS = {
    INPUT_SELECTION: "Use the current selection",
    INPUT_SELECT_ALL: "Select the whole field first",
}
LABEL_TO_MODE = {label: mode for mode, label in INPUT_LABELS.items()}
NO_SHORTCUT = "No shortcut"
MUTED = "#555555"


@dataclass
class SkillsPageHooks:
    record_active: Callable[[bool], None] | None = None  # pause/resume the app's global shortcuts
    test_skill: Callable[[Skill, str], str] | None = None


def _theme_background(widget: tk.Misc) -> str:
    """The page colour of the current theme, so the scrolling list does not show a grey strip."""
    try:
        return ttk.Style(widget).lookup("TFrame", "background") or widget.winfo_toplevel().cget("background")
    except tk.TclError:
        return "#fafafa"


def shortcut_text(spec: str | None) -> str:
    hotkey = parse_skill_hotkey(spec)
    return NO_SHORTCUT if hotkey.is_disabled else hotkey.display


def mode_text(skill: Skill) -> str:
    return INPUT_LABELS.get(skill.input_mode, INPUT_LABELS[INPUT_SELECTION])


def same_hotkey(left: Hotkey, right: Hotkey) -> bool:
    return not left.is_disabled and not right.is_disabled and left.vk == right.vk and (left.modifiers & 0x0F) == (right.modifiers & 0x0F)


class SkillsPage(ttk.Frame):
    def __init__(
        self,
        parent: tk.Misc,
        skills: list[Skill],
        menu_hotkey: str,
        dismissed: list[str],
        target_language: tk.StringVar,
        target_choices: tuple[str, ...] | list[str],
        hooks: SkillsPageHooks | None = None,
        ask_shortcut: Callable[..., str | None] | None = None,
    ) -> None:
        super().__init__(parent)
        self.hooks = hooks or SkillsPageHooks()
        self.skills: list[Skill] = [Skill.from_dict(skill.to_dict()) for skill in skills]
        self.menu_hotkey = menu_hotkey or "none"
        self.dismissed: list[str] = list(dismissed)
        self.target_language = target_language
        self._ask_shortcut = ask_shortcut or ShortcutDialog.ask
        self.message = tk.StringVar(value="")
        self._editing: Skill | None = None
        self._editing_index: int | None = None
        self._editing_snapshot: dict | None = None
        self._wheel_bound = False

        self.columnconfigure(0, weight=1)
        self.rowconfigure(0, weight=1)
        self._build_list_page(target_choices)
        self._build_edit_page()
        self.show_list()

    # ================================================================== list page

    def _build_list_page(self, target_choices) -> None:
        page = ttk.Frame(self)
        self.list_page = page
        page.grid(row=0, column=0, sticky="nsew")
        page.columnconfigure(0, weight=1)
        page.rowconfigure(1, weight=1)

        head = ttk.Frame(page)
        head.grid(row=0, column=0, sticky="ew", pady=(0, 8))
        head.columnconfigure(0, weight=1)
        ttk.Label(head, text="Skills", font=("Segoe UI", 14, "bold")).grid(row=0, column=0, sticky="w")
        ttk.Label(
            head,
            text="Each skill has its own shortcut, input, and prompt. Switch them on or off, change them, or make your own. Everything except Fix needs an online model.",
            foreground=MUTED,
            wraplength=560,
        ).grid(row=1, column=0, sticky="w")
        self.new_btn = ttk.Button(head, text="New skill", command=lambda: self.open_editor(None))
        try:
            if ttk.Style().layout("Accent.TButton"):
                self.new_btn.configure(style="Accent.TButton")
        except tk.TclError:
            pass
        self.new_btn.grid(row=0, column=1, rowspan=2, sticky="e", padx=(12, 0))

        holder = ttk.Frame(page)
        holder.grid(row=1, column=0, sticky="nsew")
        holder.columnconfigure(0, weight=1)
        holder.rowconfigure(0, weight=1)
        self.canvas = tk.Canvas(holder, highlightthickness=0, borderwidth=0, background=_theme_background(self))
        scroll = ttk.Scrollbar(holder, orient="vertical", command=self.canvas.yview)
        self.canvas.configure(yscrollcommand=scroll.set)
        self.canvas.grid(row=0, column=0, sticky="nsew")
        scroll.grid(row=0, column=1, sticky="ns")
        self.rows = ttk.Frame(self.canvas)
        self._rows_window = self.canvas.create_window((0, 0), window=self.rows, anchor="nw")
        self.rows.bind("<Configure>", lambda _e: self.canvas.configure(scrollregion=self.canvas.bbox("all")))
        self.canvas.bind("<Configure>", lambda e: self.canvas.itemconfigure(self._rows_window, width=e.width))
        self.rows.columnconfigure(0, weight=1)
        for widget in (self.canvas, self.rows):
            widget.bind("<Enter>", self._bind_wheel)
            widget.bind("<Leave>", self._unbind_wheel)

        ttk.Label(page, textvariable=self.message, foreground=MUTED, wraplength=640).grid(row=2, column=0, sticky="w", pady=(6, 0))

        foot = ttk.Frame(page)
        foot.grid(row=3, column=0, sticky="ew", pady=(10, 0))
        foot.columnconfigure(4, weight=1)
        ttk.Label(foot, text="Skill menu shortcut").grid(row=0, column=0, sticky="w")
        self.menu_label = tk.StringVar(value=shortcut_text(self.menu_hotkey))
        ttk.Entry(foot, textvariable=self.menu_label, state="readonly", width=22).grid(row=0, column=1, sticky="w", padx=8)
        ttk.Button(foot, text="Change…", command=self._change_menu_hotkey).grid(row=0, column=2)
        ttk.Button(foot, text="Clear", command=self._clear_menu_hotkey).grid(row=0, column=3, padx=(6, 0))
        ttk.Label(foot, text="Opens a small menu of your skills next to the cursor.", foreground=MUTED).grid(
            row=1, column=0, columnspan=4, sticky="w", pady=(2, 8)
        )
        ttk.Label(foot, text="Target language").grid(row=2, column=0, sticky="w")
        ttk.Combobox(foot, textvariable=self.target_language, values=list(target_choices), width=22).grid(
            row=2, column=1, sticky="w", padx=8
        )
        ttk.Label(foot, text="Used wherever a prompt has {{language}}, such as Translate.", foreground=MUTED).grid(
            row=3, column=0, columnspan=4, sticky="w", pady=(2, 0)
        )

    def _bind_wheel(self, _event=None) -> None:
        if not self._wheel_bound:
            self.canvas.bind_all("<MouseWheel>", self._on_wheel)
            self._wheel_bound = True

    def _unbind_wheel(self, _event=None) -> None:
        if self._wheel_bound:
            self.canvas.unbind_all("<MouseWheel>")
            self._wheel_bound = False

    def _on_wheel(self, event: tk.Event) -> None:
        if self.is_editing:
            return
        first, last = self.canvas.yview()
        if first <= 0.0 and last >= 1.0:
            return
        self.canvas.yview_scroll(int(-event.delta / 120), "units")

    def render_list(self) -> None:
        for child in self.rows.winfo_children():
            child.destroy()
        if not self.skills:
            ttk.Label(self.rows, text="No skills yet. Click New skill to make one.", foreground=MUTED).grid(
                row=0, column=0, sticky="w", pady=20
            )
        last = len(self.skills) - 1
        for index, skill in enumerate(self.skills):
            self._render_row(index, skill, last)
        self.canvas.configure(scrollregion=self.canvas.bbox("all"))

    def _render_row(self, index: int, skill: Skill, last: int) -> None:
        row = ttk.Frame(self.rows, padding=(2, 6))
        row.grid(row=index * 2, column=0, sticky="ew")
        row.columnconfigure(1, weight=1)

        enabled = tk.BooleanVar(value=skill.enabled)
        check = ttk.Checkbutton(row, variable=enabled, command=lambda s=skill, v=enabled: self._toggle(s, v))
        check.grid(row=0, column=0, rowspan=2, padx=(0, 6))
        name = skill.name if skill.enabled else f"{skill.name}  (off)"
        ttk.Label(row, text=name, font=("Segoe UI", 10, "bold")).grid(row=0, column=1, sticky="w")
        detail = mode_text(skill)
        if skill.needs_ai:
            detail += "  ·  needs an online model"
        ttk.Label(row, text=detail, foreground=MUTED).grid(row=1, column=1, sticky="w")
        ttk.Label(row, text=shortcut_text(skill.hotkey), width=16, anchor="w").grid(row=0, column=2, rowspan=2, padx=8)

        buttons = ttk.Frame(row)
        buttons.grid(row=0, column=3, rowspan=2, sticky="e")
        up = ttk.Button(buttons, text="▲", width=3, command=lambda i=index: self._move(i, -1))
        down = ttk.Button(buttons, text="▼", width=3, command=lambda i=index: self._move(i, 1))
        up.pack(side="left")
        down.pack(side="left", padx=(2, 8))
        if index == 0:
            up.state(["disabled"])
        if index == last:
            down.state(["disabled"])
        ttk.Button(buttons, text="Edit", command=lambda i=index: self.open_editor(i)).pack(side="left")
        ttk.Button(buttons, text="Duplicate", command=lambda i=index: self._duplicate(i)).pack(side="left", padx=(6, 0))
        delete = ttk.Button(buttons, text="Delete", command=lambda i=index: self._delete(i))
        delete.pack(side="left", padx=(6, 0))
        if not can_delete(skill):
            delete.state(["disabled"])
        if index != last:
            ttk.Separator(self.rows).grid(row=index * 2 + 1, column=0, sticky="ew")

    # ------------------------------------------------------------------ list actions

    def _toggle(self, skill: Skill, var: tk.BooleanVar) -> None:
        skill.enabled = bool(var.get())
        clash = self._conflicts_for(skill)
        if skill.enabled and clash:
            self.message.set(f"“{skill.name}” is on, but its shortcut {shortcut_text(skill.hotkey)} is also used by {clash}. Change one of them.")
        else:
            self.message.set("")
        self.render_list()

    def _move(self, index: int, delta: int) -> None:
        target = index + delta
        if 0 <= index < len(self.skills) and 0 <= target < len(self.skills):
            self.skills[index], self.skills[target] = self.skills[target], self.skills[index]
            self.render_list()

    def _duplicate(self, index: int) -> None:
        if not (0 <= index < len(self.skills)):
            return
        copy = duplicate_skill(self.skills[index])
        self.skills.insert(index + 1, copy)
        self.message.set(f"Made “{copy.name}”. Click Edit to change it.")
        self.render_list()

    def _delete(self, index: int) -> None:
        if not (0 <= index < len(self.skills)):
            return
        skill = self.skills[index]
        if not can_delete(skill):
            self.message.set(f"{skill.name} cannot be deleted, but you can switch it off.")
            return
        if not messagebox.askyesno("Delete skill", f"Delete “{skill.name}”?", parent=self.winfo_toplevel()):
            return
        del self.skills[index]
        if skill.id in OPTIONAL_BUILTIN_IDS and skill.id not in self.dismissed:
            self.dismissed.append(skill.id)
        self.message.set(f"Deleted “{skill.name}”.")
        self.render_list()

    # ------------------------------------------------------------------ menu shortcut

    def _change_menu_hotkey(self) -> None:
        def clash(hotkey: Hotkey) -> str | None:
            return self._clash_message(hotkey, ignore_skill_id=None, ignore_menu=True)

        result = self._ask_shortcut(
            self, "Shortcut for the skill menu", self.menu_hotkey, conflict_of=clash, on_pause=self.hooks.record_active
        )
        if result is not None:
            self.menu_hotkey = result
            self.menu_label.set(shortcut_text(result))

    def _clear_menu_hotkey(self) -> None:
        self.menu_hotkey = "none"
        self.menu_label.set(NO_SHORTCUT)

    # ------------------------------------------------------------------ conflicts

    def _clash_message(self, hotkey: Hotkey, ignore_skill_id: str | None, ignore_menu: bool = False) -> str | None:
        for skill in self.skills:
            if skill.id == ignore_skill_id or not skill.enabled:
                continue
            if same_hotkey(hotkey, skill.hotkey_obj()):
                return f"{hotkey.display} is already used by {skill.name}."
        if not ignore_menu and same_hotkey(hotkey, parse_skill_hotkey(self.menu_hotkey)):
            return f"{hotkey.display} is already used by the skill menu."
        return None

    def _conflicts_for(self, skill: Skill) -> str | None:
        hotkey = skill.hotkey_obj()
        if hotkey.is_disabled:
            return None
        message = self._clash_message(hotkey, ignore_skill_id=skill.id)
        return message.split(" is already used by ", 1)[-1].rstrip(".") if message else None

    # ================================================================== edit page

    def _build_edit_page(self) -> None:
        page = ttk.Frame(self)
        self.edit_page = page
        page.grid(row=0, column=0, sticky="nsew")
        page.columnconfigure(0, weight=1)
        page.rowconfigure(2, weight=1)

        top = ttk.Frame(page)
        top.grid(row=0, column=0, sticky="ew")
        top.columnconfigure(1, weight=1)
        ttk.Button(top, text="← Back to skills", command=self.cancel_editing).grid(row=0, column=0, sticky="w")
        self.edit_title = tk.StringVar(value="Edit skill")
        ttk.Label(top, textvariable=self.edit_title, font=("Segoe UI", 14, "bold")).grid(row=0, column=1, sticky="w", padx=12)

        form = ttk.Frame(page)
        form.grid(row=1, column=0, sticky="ew", pady=(12, 8))
        form.columnconfigure(1, weight=1)
        self.f_name = tk.StringVar()
        self.f_enabled = tk.BooleanVar(value=True)
        self.f_mode = tk.StringVar(value=INPUT_LABELS[INPUT_SELECTION])
        self.f_hotkey = tk.StringVar(value=NO_SHORTCUT)

        ttk.Label(form, text="Name").grid(row=0, column=0, sticky="w", pady=4)
        self.name_entry = ttk.Entry(form, textvariable=self.f_name)
        self.name_entry.grid(row=0, column=1, sticky="ew", padx=(12, 0), pady=4)
        ttk.Checkbutton(form, text="Turn this skill on", variable=self.f_enabled).grid(row=1, column=1, sticky="w", padx=(12, 0))

        ttk.Label(form, text="Input").grid(row=2, column=0, sticky="w", pady=(10, 4))
        self.mode_box = ttk.Combobox(form, textvariable=self.f_mode, values=list(INPUT_LABELS.values()), state="readonly", width=34)
        self.mode_box.grid(row=2, column=1, sticky="w", padx=(12, 0), pady=(10, 4))

        ttk.Label(form, text="Shortcut").grid(row=3, column=0, sticky="w", pady=4)
        hot = ttk.Frame(form)
        hot.grid(row=3, column=1, sticky="w", padx=(12, 0), pady=4)
        ttk.Entry(hot, textvariable=self.f_hotkey, state="readonly", width=24).pack(side="left")
        ttk.Button(hot, text="Change…", command=self._change_skill_hotkey).pack(side="left", padx=(8, 0))
        ttk.Button(hot, text="Clear", command=lambda: self.f_hotkey.set(NO_SHORTCUT)).pack(side="left", padx=(6, 0))

        prompt_box = ttk.Frame(page)
        prompt_box.grid(row=2, column=0, sticky="nsew")
        prompt_box.columnconfigure(0, weight=1)
        prompt_box.rowconfigure(2, weight=1)
        ttk.Label(prompt_box, text="Prompt", font=("Segoe UI", 10, "bold")).grid(row=0, column=0, sticky="w")
        ttk.Label(
            prompt_box,
            text="Put {{content}} where the text goes (it is added at the end if you leave it out). {{language}} becomes your target language.",
            foreground=MUTED,
            wraplength=640,
        ).grid(row=1, column=0, sticky="w", pady=(0, 4))
        text_frame = ttk.Frame(prompt_box)
        text_frame.grid(row=2, column=0, sticky="nsew")
        text_frame.columnconfigure(0, weight=1)
        text_frame.rowconfigure(0, weight=1)
        self.prompt = tk.Text(text_frame, height=8, wrap="word", undo=True, font=("Segoe UI", 10))
        prompt_scroll = ttk.Scrollbar(text_frame, orient="vertical", command=self.prompt.yview)
        self.prompt.configure(yscrollcommand=prompt_scroll.set)
        self.prompt.grid(row=0, column=0, sticky="nsew")
        prompt_scroll.grid(row=0, column=1, sticky="ns")

        tools = ttk.Frame(page)
        tools.grid(row=3, column=0, sticky="ew", pady=(8, 0))
        ttk.Button(tools, text="Reset prompt", command=self._reset_prompt).pack(side="left")
        ttk.Button(tools, text="Test on sample text…", command=self._test).pack(side="left", padx=(8, 0))

        self.edit_message = tk.StringVar(value="")
        ttk.Label(page, textvariable=self.edit_message, foreground="#b3261e", wraplength=640).grid(row=4, column=0, sticky="w", pady=(8, 0))

        actions = ttk.Frame(page)
        actions.grid(row=5, column=0, sticky="e", pady=(8, 0))
        self.save_btn = ttk.Button(actions, text="Save skill", command=self.save_editing)
        try:
            if ttk.Style().layout("Accent.TButton"):
                self.save_btn.configure(style="Accent.TButton")
        except tk.TclError:
            pass
        ttk.Button(actions, text="Cancel", command=self.cancel_editing).pack(side="right")
        self.save_btn.pack(side="right", padx=(0, 8))

    # ------------------------------------------------------------------ page switching

    @property
    def is_editing(self) -> bool:
        return self._editing is not None

    def show_list(self) -> None:
        self._editing = None
        self._editing_index = None
        self.edit_page.grid_remove()
        self.list_page.grid()
        self.render_list()

    def open_editor(self, index: int | None) -> None:
        if index is None:
            draft = new_custom_skill(name="New skill", prompt=default_prompt_for_skill("new"))
            self._editing_index = None
            self.edit_title.set("New skill")
        else:
            draft = Skill.from_dict(self.skills[index].to_dict())
            self._editing_index = index
            self.edit_title.set(f"Edit “{draft.name}”")
        self._editing = draft
        self.f_name.set("" if index is None else draft.name)
        self.f_enabled.set(bool(draft.enabled))
        self.f_mode.set(INPUT_LABELS.get(draft.input_mode, INPUT_LABELS[INPUT_SELECTION]))
        self.f_hotkey.set(shortcut_text(draft.hotkey))
        self.prompt.delete("1.0", "end")
        self.prompt.insert("1.0", (draft.prompt or "").rstrip() + "\n")
        self.prompt.edit_reset()
        self.edit_message.set("")
        self._editing_snapshot = self._form_values()
        self.list_page.grid_remove()
        self.edit_page.grid()
        self.name_entry.focus_set()

    def _form_values(self) -> dict:
        return {
            "name": self.f_name.get().strip(),
            "enabled": bool(self.f_enabled.get()),
            "mode": LABEL_TO_MODE.get(self.f_mode.get(), INPUT_SELECTION),
            "hotkey": "none" if self.f_hotkey.get() == NO_SHORTCUT else self.f_hotkey.get(),
            "prompt": self.prompt.get("1.0", "end").strip(),
        }

    def _is_dirty(self) -> bool:
        return self._editing is not None and self._form_values() != self._editing_snapshot

    def cancel_editing(self) -> None:
        if not self.is_editing:
            return
        if self._is_dirty() and not messagebox.askyesno(
            "Discard changes?", "You changed this skill. Leave without saving?", parent=self.winfo_toplevel()
        ):
            return
        self.show_list()

    def save_editing(self) -> bool:
        """Validate and store the skill being edited. Returns True when saved (or when nothing is being edited)."""
        if self._editing is None:
            return True
        values = self._form_values()
        if not values["name"]:
            self.edit_message.set("Give the skill a name.")
            self.name_entry.focus_set()
            return False
        if not values["prompt"]:
            self.edit_message.set("The prompt is empty. Write what the model should do with the text.")
            self.prompt.focus_set()
            return False
        draft = self._editing
        draft.name = values["name"]
        draft.enabled = values["enabled"]
        draft.input_mode = values["mode"]
        draft.hotkey = values["hotkey"]
        draft.prompt = values["prompt"] + "\n"
        if draft.enabled:
            clash = self._clash_message(draft.hotkey_obj(), ignore_skill_id=draft.id) if not draft.hotkey_obj().is_disabled else None
            if clash:
                self.edit_message.set(clash + " Pick another shortcut or switch this skill off.")
                return False
        if self._editing_index is None:
            self.skills.append(draft)
            self.message.set(f"Added “{draft.name}”.")
        else:
            self.skills[self._editing_index] = draft
            self.message.set(f"Saved “{draft.name}”.")
        self.show_list()
        return True

    # ------------------------------------------------------------------ edit actions

    def _change_skill_hotkey(self) -> None:
        if self._editing is None:
            return
        editing_id = self._editing.id

        def clash(hotkey: Hotkey) -> str | None:
            return self._clash_message(hotkey, ignore_skill_id=editing_id)

        current = "none" if self.f_hotkey.get() == NO_SHORTCUT else self.f_hotkey.get()
        name = self.f_name.get().strip() or "this skill"
        result = self._ask_shortcut(self, f"Shortcut for {name}", current, conflict_of=clash, on_pause=self.hooks.record_active)
        if result is not None:
            self.f_hotkey.set(shortcut_text(result))

    def _reset_prompt(self) -> None:
        if self._editing is None:
            return
        value = default_prompt_for_skill(self._editing.id if self._editing_index is not None else "new")
        self.prompt.delete("1.0", "end")
        self.prompt.insert("1.0", value.rstrip() + "\n")

    def _test(self) -> None:
        if self._editing is None:
            return
        skill = Skill.from_dict(self._editing.to_dict())
        skill.prompt = self.prompt.get("1.0", "end").strip() + "\n"
        skill.name = self.f_name.get().strip() or skill.name
        dialog = tk.Toplevel(self)
        dialog.title(f"Test “{skill.name}”")
        dialog.geometry("520x420")
        dialog.transient(self.winfo_toplevel())
        dialog.columnconfigure(0, weight=1)
        dialog.rowconfigure(1, weight=1)
        dialog.rowconfigure(3, weight=1)
        ttk.Label(dialog, text="Sample text").grid(row=0, column=0, sticky="w", padx=12, pady=(12, 4))
        sample = tk.Text(dialog, height=6, wrap="word")
        sample.grid(row=1, column=0, sticky="nsew", padx=12)
        sample.insert("1.0", "Hi frinds how are you?")
        ttk.Label(dialog, text="Result").grid(row=2, column=0, sticky="w", padx=12, pady=(8, 4))
        result = tk.Text(dialog, height=8, wrap="word")
        result.grid(row=3, column=0, sticky="nsew", padx=12)
        status = tk.StringVar(value="")
        ttk.Label(dialog, textvariable=status, wraplength=480, foreground=MUTED).grid(row=4, column=0, sticky="w", padx=12, pady=8)
        row = ttk.Frame(dialog)
        row.grid(row=5, column=0, sticky="e", padx=12, pady=(0, 12))
        alive = {"open": True}
        dialog.bind("<Destroy>", lambda e: alive.update(open=False) if e.widget is dialog else None)

        def run_test() -> None:
            text = sample.get("1.0", "end").rstrip("\n")
            status.set("Running…")
            result.delete("1.0", "end")
            hook = self.hooks.test_skill

            def work() -> None:
                error = ""
                output = ""
                try:
                    if hook is None:
                        raise RuntimeError("Testing is not available in this window.")
                    output = hook(skill, text) or ""
                except Exception as exc:
                    error = str(exc)

                def done() -> None:
                    if not alive["open"]:
                        return
                    if error:
                        status.set(error)
                        return
                    result.insert("1.0", output)
                    status.set("Done.")

                try:
                    dialog.after(0, done)
                except (tk.TclError, RuntimeError):
                    pass

            threading.Thread(target=work, name="tinyfix-skill-test", daemon=True).start()

        ttk.Button(row, text="Run test", command=run_test).pack(side="right", padx=(8, 0))
        ttk.Button(row, text="Close", command=dialog.destroy).pack(side="right")
        dialog.focus_force()

    # ------------------------------------------------------------------ for the settings window

    def finish_editing(self) -> bool:
        """Apply/OK while a skill is open: save it first, or stay so the user can fix what is wrong."""
        return self.save_editing() if self.is_editing else True

    def set_message(self, text: str) -> None:
        self.message.set(text)

    def destroy(self) -> None:
        self._unbind_wheel()
        super().destroy()


def default_target_language() -> str:
    return DEFAULT_TARGET_LANGUAGE


__all__ = ["SkillsPage", "SkillsPageHooks", "shortcut_text", "INPUT_LABELS", "parse_hotkey"]
