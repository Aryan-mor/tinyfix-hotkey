"""The Skills tab: one list, a separate edit page, shortcut dialog, and what ends up in the saved data."""

from __future__ import annotations

import tkinter as tk
from tkinter import messagebox

import pytest

from hotkey_util import MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN, parse_hotkey
from settings_ui import SettingsWindow
from shortcut_dialog import ShortcutDialog, current_modifiers, hotkey_from_press, modifiers_from_flags, supported_vk
from skills import (
    INPUT_SELECT_ALL,
    INPUT_SELECTION,
    SKILL_IMPROVE_PROMPT,
    SKILL_REWRITE,
    SkillStore,
    default_skills,
    delete_skill,
    ensure_builtins,
    new_custom_skill,
)
from skills_page import INPUT_LABELS, NO_SHORTCUT, SkillsPage, SkillsPageHooks
from tests.test_settings_pack_ui import FakePack, make_data, pump, tk_root  # noqa: F401  (fixture re-export)


def make_page(tk_root, asked=None, hooks=None, skills=None, menu="Ctrl+Alt+M"):
    var = tk.StringVar(value="English")
    host = tk.Toplevel(tk_root)  # a real, visible window: hidden windows never map their children
    host.geometry("800x620+40+40")
    host.rowconfigure(0, weight=1)
    host.columnconfigure(0, weight=1)
    page = SkillsPage(
        host,
        skills or default_skills(),
        menu,
        [],
        var,
        ["English", "German"],
        hooks or SkillsPageHooks(),
        ask_shortcut=asked or (lambda *a, **k: None),
    )
    page.grid(row=0, column=0, sticky="nsew")
    page.host = host
    for _ in range(10):
        tk_root.update()
    return page


@pytest.fixture
def page(tk_root):
    p = make_page(tk_root)
    yield p
    p.host.destroy()


def names(p):
    return [s.name for s in p.skills]


def _walk(widget):
    yield widget
    for child in widget.winfo_children():
        yield from _walk(child)


def row_buttons(p, label):
    return [w for w in _walk(p.rows) if w.winfo_class() == "TButton" and w.cget("text") == label]


# ------------------------------------------------------------------ one list, no built-in/custom split


def test_every_skill_is_in_one_list_with_the_same_controls(page):
    assert names(page) == ["Fix", "Fix all", "Translate", "Rewrite", "Rewrite to target language", "Improve prompt"]
    for label in ("Edit", "Duplicate", "Delete", "▲", "▼"):
        assert len(row_buttons(page, label)) == 6, label
    text = " ".join(w.cget("text") for w in _walk(page.rows) if w.winfo_class() == "TLabel")
    assert "built-in" not in text.lower() and "custom" not in text.lower()


def test_new_skill_button_is_always_on_the_list_page(page):
    assert page.new_btn.winfo_ismapped()
    assert page.new_btn.cget("text") == "New skill"


def test_move_buttons_are_visible_and_disabled_at_the_ends(page):
    ups, downs = row_buttons(page, "▲"), row_buttons(page, "▼")
    assert ups[0].instate(["disabled"]) and not ups[1].instate(["disabled"])
    assert downs[-1].instate(["disabled"]) and not downs[0].instate(["disabled"])
    assert all(b.winfo_ismapped() for b in ups + downs)


def test_move_up_and_down_change_the_order(page):
    row_buttons(page, "▼")[0].invoke()
    assert names(page)[:2] == ["Fix all", "Fix"]
    row_buttons(page, "▲")[1].invoke()
    assert names(page)[:2] == ["Fix", "Fix all"]


def test_toggle_turns_a_skill_off_and_on(page):
    next(w for w in _walk(page.rows) if w.winfo_class() == "TCheckbutton").invoke()
    assert page.skills[0].enabled is False
    next(w for w in _walk(page.rows) if w.winfo_class() == "TCheckbutton").invoke()
    assert page.skills[0].enabled is True


def test_duplicate_adds_a_copy_without_the_shortcut(page):
    row_buttons(page, "Duplicate")[2].invoke()
    assert names(page)[3] == "Translate copy"
    assert page.skills[3].hotkey == "none"
    assert page.skills[3].id != page.skills[2].id


def test_fix_fix_all_and_translate_can_only_be_switched_off(page, monkeypatch):
    deletes = row_buttons(page, "Delete")
    assert [b.instate(["disabled"]) for b in deletes[:3]] == [True, True, True]
    assert not any(b.instate(["disabled"]) for b in deletes[3:])
    monkeypatch.setattr(messagebox, "askyesno", lambda *a, **k: True)
    page._delete(0)
    assert names(page)[0] == "Fix"


def test_delete_asks_first_and_deleted_ours_do_not_come_back(page, monkeypatch):
    monkeypatch.setattr(messagebox, "askyesno", lambda *a, **k: False)
    page._delete(5)
    assert "Improve prompt" in names(page)
    monkeypatch.setattr(messagebox, "askyesno", lambda *a, **k: True)
    page._delete(5)
    assert "Improve prompt" not in names(page) and page.dismissed == [SKILL_IMPROVE_PROMPT]
    store = SkillStore(skills=list(page.skills), dismissed=list(page.dismissed))
    assert SKILL_IMPROVE_PROMPT not in [s.id for s in ensure_builtins(store).skills]


def test_dismissed_skills_survive_a_save_and_reload():
    store = SkillStore(skills=[s for s in default_skills() if s.id != SKILL_REWRITE], dismissed=[SKILL_REWRITE])
    again = SkillStore.from_dict(store.to_dict())
    assert again.dismissed == [SKILL_REWRITE]
    assert SKILL_REWRITE not in [s.id for s in ensure_builtins(again).skills]
    assert not delete_skill(again, "fix")  # core skills are protected


# ------------------------------------------------------------------ the edit page


def test_edit_page_opens_with_the_skill_and_its_prompt_visible(page):
    page.open_editor(5)
    page.update()
    assert page.is_editing and page.edit_page.winfo_ismapped() and not page.list_page.winfo_ismapped()
    assert page.f_name.get() == "Improve prompt"
    assert "prompt engineer" in page.prompt.get("1.0", "end")
    assert page.prompt.winfo_height() > 100  # not squeezed out of view


def test_input_is_a_select_menu_for_every_skill_including_ours(page):
    page.open_editor(0)  # Fix
    assert str(page.mode_box.cget("state")) == "readonly"
    assert list(page.mode_box.cget("values")) == list(INPUT_LABELS.values())
    page.f_mode.set(INPUT_LABELS[INPUT_SELECT_ALL])
    assert page.save_editing()
    assert page.skills[0].input_mode == INPUT_SELECT_ALL
    page.open_editor(1)  # Fix all
    page.f_mode.set(INPUT_LABELS[INPUT_SELECTION])
    page.save_editing()
    assert page.skills[1].input_mode == INPUT_SELECTION


def test_editing_a_skill_saves_name_prompt_and_state(page):
    page.open_editor(3)
    page.f_name.set("Polish")
    page.f_enabled.set(True)
    page.prompt.delete("1.0", "end")
    page.prompt.insert("1.0", "Polish this: {{content}}")
    assert page.save_editing()
    skill = page.skills[3]
    assert (skill.name, skill.enabled, skill.prompt.strip()) == ("Polish", True, "Polish this: {{content}}")
    page.update()
    assert not page.is_editing and page.list_page.winfo_ismapped()
    assert "Saved" in page.message.get()


def test_new_skill_appears_in_the_list_at_the_end(page):
    page.open_editor(None)
    assert page.edit_title.get() == "New skill"
    page.f_name.set("Summarize")
    page.prompt.delete("1.0", "end")
    page.prompt.insert("1.0", "Summarize in two lines:\n{{content}}")
    assert page.save_editing()
    assert names(page)[-1] == "Summarize" and len(page.skills) == 7
    assert any(w.cget("text").startswith("Summarize") for w in _walk(page.rows) if w.winfo_class() == "TLabel")


def test_new_skill_must_have_a_name_and_a_prompt(page):
    page.open_editor(None)
    assert not page.save_editing() and "name" in page.edit_message.get().lower()
    page.f_name.set("X")
    page.prompt.delete("1.0", "end")
    assert not page.save_editing() and "prompt" in page.edit_message.get().lower()
    assert len(page.skills) == 6 and page.is_editing


def test_cancel_discards_and_asks_only_when_something_changed(page, monkeypatch):
    asked = []
    monkeypatch.setattr(messagebox, "askyesno", lambda *a, **k: asked.append(1) or False)
    page.open_editor(0)
    page.cancel_editing()
    assert not page.is_editing and asked == []  # nothing changed: no question
    page.open_editor(0)
    page.f_name.set("Changed")
    page.cancel_editing()
    assert page.is_editing and asked == [1]  # answered No: stay
    monkeypatch.setattr(messagebox, "askyesno", lambda *a, **k: True)
    page.cancel_editing()
    assert not page.is_editing and page.skills[0].name == "Fix"


def test_reset_prompt_restores_the_default(page):
    page.open_editor(5)
    page.prompt.delete("1.0", "end")
    page.prompt.insert("1.0", "junk")
    page._reset_prompt()
    assert "prompt engineer" in page.prompt.get("1.0", "end")


def test_finish_editing_saves_so_ok_never_loses_an_open_form(page):
    page.open_editor(0)
    page.f_name.set("Fix now")
    assert page.finish_editing() and page.skills[0].name == "Fix now"
    page.open_editor(None)
    assert page.finish_editing() is False  # invalid: stays open instead of dropping it
    assert page.is_editing


# ------------------------------------------------------------------ shortcuts and conflicts


def test_change_shortcut_uses_the_dialog_and_shows_the_result(tk_root):
    seen = {}

    def ask(parent, title, current, conflict_of=None, on_pause=None):
        seen.update(title=title, current=current, pause=on_pause)
        return "Ctrl+Alt+K"

    p = make_page(tk_root, asked=ask, hooks=SkillsPageHooks(record_active=lambda on: None))
    try:
        p.open_editor(3)
        p._change_skill_hotkey()
        assert p.f_hotkey.get() == "Ctrl+Alt+K" and "Rewrite" in seen["title"] and seen["current"] == "none"
        assert seen["pause"] is not None  # the app's own shortcuts get paused while choosing
        p.save_editing()
        assert p.skills[3].hotkey == "Ctrl+Alt+K"
        p.open_editor(3)
        p.f_hotkey.set(NO_SHORTCUT)
        p.save_editing()
        assert p.skills[3].hotkey == "none"
    finally:
        p.host.destroy()


def test_the_dialog_is_told_about_shortcuts_already_in_use(tk_root):
    captured = {}

    def ask(parent, title, current, conflict_of=None, on_pause=None):
        captured["clash"] = conflict_of
        return None

    p = make_page(tk_root, asked=ask)
    try:
        p.open_editor(3)
        p._change_skill_hotkey()
        clash = captured["clash"]
        assert "Fix all" in clash(parse_hotkey("Ctrl+F3"))
        assert "skill menu" in clash(parse_hotkey("Ctrl+Alt+M"))
        assert clash(parse_hotkey("Ctrl+Alt+K")) is None
        p.open_editor(1)  # editing Fix all itself: its own shortcut is not a clash
        p._change_skill_hotkey()
        assert captured["clash"](parse_hotkey("Ctrl+F3")) is None
    finally:
        p.host.destroy()


def test_save_refuses_a_shortcut_used_by_another_enabled_skill(page):
    page.open_editor(3)
    page.f_enabled.set(True)
    page.f_hotkey.set("Ctrl+F3")
    assert not page.save_editing() and "Fix all" in page.edit_message.get()
    page.f_enabled.set(False)  # a skill that is off does not clash
    assert page.save_editing()


def test_skill_menu_shortcut_change_and_clear(tk_root):
    p = make_page(tk_root, asked=lambda *a, **k: "Ctrl+Alt+J")
    try:
        p._change_menu_hotkey()
        assert p.menu_hotkey == "Ctrl+Alt+J" and p.menu_label.get() == "Ctrl+Alt+J"
        p._clear_menu_hotkey()
        assert p.menu_hotkey == "none" and p.menu_label.get() == NO_SHORTCUT
    finally:
        p.host.destroy()


# ------------------------------------------------------------------ the key capture dialog


def test_ctrl_f3_is_recorded_as_ctrl_f3_not_ctrl_alt_a():
    hotkey, problem = hotkey_from_press(0x72, MOD_CONTROL)
    assert hotkey.display == "Ctrl+F3" and problem == ""
    parsed = parse_hotkey(hotkey.display)
    assert parsed.vk == hotkey.vk and parsed.modifiers == MOD_CONTROL


@pytest.mark.parametrize(
    "vk, mods, expected",
    [
        (0x41, MOD_CONTROL | MOD_ALT, "Ctrl+Alt+A"),
        (0x20, MOD_CONTROL | MOD_ALT, "Ctrl+Alt+Space"),
        (0x7B, 0, "F12"),
        (0x35, MOD_SHIFT | MOD_CONTROL, "Ctrl+Shift+5"),
        (0x4D, MOD_WIN, "Win+M"),
        (0x25, MOD_CONTROL, "Ctrl+Left"),
    ],
)
def test_press_to_shortcut_mapping(vk, mods, expected):
    assert hotkey_from_press(vk, mods)[0].display == expected


def test_unsafe_and_unsupported_presses_explain_themselves():
    hotkey, problem = hotkey_from_press(0x41, 0)  # plain "A"
    assert hotkey is None and "Hold" in problem
    hotkey, problem = hotkey_from_press(0x6B, MOD_CONTROL)  # numpad plus
    assert hotkey is None and "cannot use" in problem
    assert hotkey_from_press(0x11, 0) == (None, "")  # a lone Ctrl press is not a shortcut yet
    assert supported_vk(0x70) and not supported_vk(0x6B)


def test_modifier_flags_do_not_confuse_num_lock_with_alt():
    # Tk reports Num Lock as modifier bit 0x8; the modifiers must come from Windows key state instead.
    assert modifiers_from_flags(ctrl=True, alt=False, shift=False, win=False) == MOD_CONTROL
    assert isinstance(current_modifiers(), int)


def test_dialog_flow_save_clear_cancel_and_pause(tk_root):
    pauses = []
    results = []

    def run(action):
        dialog = ShortcutDialog(tk_root, "Shortcut for Fix all", "Ctrl+F3", on_pause=pauses.append)
        tk_root.update()
        action(dialog)
        results.append(dialog.result)
        assert not dialog.win.winfo_exists()

    run(lambda d: (d._apply_press(0x72, MOD_CONTROL), d._save()))
    run(lambda d: d._clear())
    run(lambda d: d._cancel())
    assert results == ["Ctrl+F3", "none", None]
    assert pauses == [True, False, True, False, True, False]  # always resumed, however it ends


def test_dialog_save_is_disabled_until_there_is_a_valid_shortcut(tk_root):
    d = ShortcutDialog(tk_root, "x", "none")
    try:
        tk_root.update()
        assert d.save_btn.instate(["disabled"]) and "Waiting" in d.display.get()
        d._apply_press(0x41, 0)
        assert d.save_btn.instate(["disabled"]) and d.note.get()
        d._apply_press(0x41, MOD_CONTROL)
        assert not d.save_btn.instate(["disabled"]) and d.display.get() == "Ctrl+A"
    finally:
        d._cancel()


def test_dialog_blocks_a_shortcut_that_is_already_used(tk_root):
    d = ShortcutDialog(
        tk_root, "x", "none", conflict_of=lambda h: "Ctrl+F3 is already used by Fix all." if h.display == "Ctrl+F3" else None
    )
    try:
        d._apply_press(0x72, MOD_CONTROL)
        assert d.save_btn.instate(["disabled"]) and "Fix all" in d.note.get()
        d._apply_press(0x73, MOD_CONTROL)
        assert not d.save_btn.instate(["disabled"]) and d.note.get() == ""
    finally:
        d._cancel()


def test_dialog_escape_cancels_and_resumes_shortcuts(tk_root):
    pauses = []
    d = ShortcutDialog(tk_root, "x", "none", on_pause=pauses.append)
    tk_root.update()
    assert d._on_key_press(type("E", (), {"keycode": 0x1B})()) == "break"
    assert d.result is None and pauses == [True, False]


# ------------------------------------------------------------------ what the settings window saves


@pytest.fixture
def window(tk_root):
    win = SettingsWindow(make_data(), FakePack().hooks(), master=tk_root)
    win.root.withdraw()
    pump(tk_root, 0.1)
    yield win
    win._close()


def test_settings_collect_comes_from_the_one_skills_list(window):
    page = window.skills_page
    page.skills[0].hotkey = "Ctrl+Alt+F"
    page.skills[1].enabled = False
    page.skills[2].input_mode = INPUT_SELECT_ALL
    page.skills[2].prompt = "Translate {{content}} into {{language}}!\n"
    page.skills[0].prompt = "Fix please: {{content}}\n"
    page.menu_hotkey = "Ctrl+Alt+J"
    page.dismissed.append(SKILL_REWRITE)
    data = window._collect()
    assert data.hotkey.display == "Ctrl+Alt+F"
    assert data.hotkey_all.display == "Ctrl+F3"  # switching a skill off does not erase its shortcut value
    assert data.translate_select_all is True
    assert data.llm.translate_prompt.startswith("Translate {{content}}") and data.llm.fix_prompt.startswith("Fix please")
    assert data.skill_menu_hotkey.display == "Ctrl+Alt+J"
    assert data.dismissed_skills == [SKILL_REWRITE]
    assert [s.enabled for s in data.skills][:2] == [True, False]


def test_apply_refuses_conflicts_and_shows_why_on_the_skills_tab(window):
    window.skills_page.skills[3].enabled = True
    window.skills_page.skills[3].hotkey = "Ctrl+F3"
    applied = []
    window.hooks.on_apply = applied.append
    assert window._apply() is False and applied == []
    assert "conflict" in window.skills_page.message.get().lower()


def test_apply_saves_an_open_form_first(window):
    applied = []
    window.hooks.on_apply = applied.append
    window.skills_page.open_editor(None)
    window.skills_page.f_name.set("Fresh")
    window.skills_page.prompt.delete("1.0", "end")
    window.skills_page.prompt.insert("1.0", "Do it: {{content}}")
    assert window._apply() is True
    assert "Fresh" in [s.name for s in applied[0].skills]


def test_old_shortcuts_and_prompts_tabs_are_gone(window):
    titles = [window.notebook.tab(i, "text") for i in range(window.notebook.index("end"))]
    assert titles[0] == "Skills" and "Shortcuts" not in titles and "Prompts" not in titles


def test_skills_list_scrolls_with_many_skills_and_new_skill_stays_reachable(tk_root):
    many = default_skills() + [new_custom_skill(f"Extra {i}") for i in range(12)]
    p = make_page(tk_root, skills=many)
    try:
        assert len(p.skills) == 18
        tk_root.update()
        assert p.canvas.bbox("all")[3] > p.canvas.winfo_height()  # more rows than room: the scrollbar takes over
        assert p.new_btn.winfo_ismapped()  # the New skill button never scrolls away
    finally:
        p.host.destroy()
