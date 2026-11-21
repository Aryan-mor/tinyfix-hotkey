"""Tests for skill storage, prompt templating, and hotkey conflict detection."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from hotkey_util import DEFAULT_HOTKEY, DEFAULT_HOTKEY_ALL, DEFAULT_HOTKEY_MENU, DEFAULT_HOTKEY_TRANSLATE, parse_hotkey
from prompts import DEFAULT_FIX_PROMPT, DEFAULT_TRANSLATE_PROMPT, render_prompt
from skills import (
    EXAMPLE_SKILL_SPECS,
    INPUT_SELECT_ALL,
    INPUT_SELECTION,
    SKILL_FIX,
    SKILL_FIX_ALL,
    SKILL_TRANSLATE,
    Skill,
    default_skills,
    default_store,
    delete_skill,
    duplicate_skill,
    enabled_skills,
    ensure_builtins,
    find_conflicts,
    legacy_from_store,
    load_skill_store,
    move_skill,
    new_custom_skill,
    render_skill_prompt,
    save_skill_store,
)


def test_default_skills_are_fix_fix_all_and_translate():
    skills = default_skills()
    assert [skill.id for skill in skills] == [SKILL_FIX, SKILL_FIX_ALL, SKILL_TRANSLATE]
    assert [skill.name for skill in skills] == ["Fix", "Fix all", "Translate"]
    fix, fix_all, translate = skills
    assert fix.input_mode == INPUT_SELECTION
    assert fix_all.input_mode == INPUT_SELECT_ALL
    assert translate.input_mode == INPUT_SELECTION
    assert fix.offline_fallback and fix_all.offline_fallback and not translate.offline_fallback
    assert fix.needs_ai is False and translate.needs_ai is True
    assert fix.hotkey_obj().display == parse_hotkey(DEFAULT_HOTKEY).display
    assert fix_all.hotkey_obj().display == parse_hotkey(DEFAULT_HOTKEY_ALL).display
    assert translate.hotkey_obj().display == parse_hotkey(DEFAULT_HOTKEY_TRANSLATE).display
    assert DEFAULT_HOTKEY_MENU == "Ctrl+Alt+M"
    assert "{{content}}" in fix.prompt
    assert "{{content}}" in translate.prompt
    assert "{{language}}" in translate.prompt


def test_legacy_hotkeys_and_prompts_are_migrated(tmp_path: Path):
    path = tmp_path / "skills.json"
    store, created = load_skill_store(
        path,
        fix_hotkey="F3",
        fix_all_hotkey="none",
        translate_hotkey="Ctrl+Alt+T",
        translate_select_all=True,
        fix_prompt="Fix this: {{content}}",
        translate_prompt="To {{language}}: {{content}}",
    )
    assert created
    fix = store.get(SKILL_FIX)
    fix_all = store.get(SKILL_FIX_ALL)
    translate = store.get(SKILL_TRANSLATE)
    assert fix is not None and fix.hotkey_obj().display == "F3"
    assert fix_all is not None and fix_all.hotkey_obj().is_disabled
    assert translate is not None and translate.select_all
    assert fix.prompt == "Fix this: {{content}}"
    assert "To {{language}}" in translate.prompt
    save_skill_store(path, store)
    raw = json.loads(path.read_text(encoding="utf-8"))
    assert raw["skill_menu_hotkey"] == DEFAULT_HOTKEY_MENU
    loaded, created_again = load_skill_store(path, fix_hotkey="Ctrl+Alt+Space")
    assert created_again is False
    assert loaded.get(SKILL_FIX).hotkey_obj().display == "F3"


def test_round_trip_custom_skill(tmp_path: Path):
    store = default_store()
    skill = new_custom_skill("Make formal email", EXAMPLE_SKILL_SPECS[0][1])
    skill.hotkey = "Ctrl+Alt+E"
    store.skills.append(skill)
    path = tmp_path / "skills.json"
    save_skill_store(path, store)
    loaded, created = load_skill_store(path)
    assert created is False
    found = loaded.get(skill.id)
    assert found is not None
    assert found.name == "Make formal email"
    assert found.builtin is False
    assert found.offline_fallback is False
    assert found.needs_ai is True
    assert found.hotkey_obj().display == "Ctrl+Alt+E"
    assert "{{content}}" in found.prompt


def test_render_skill_prompt_replaces_tokens():
    skill = new_custom_skill("Translate to German", "Translate into {{language}}:\n{{content}}")
    rendered = render_skill_prompt(skill, "Hello friends", language="German")
    assert "Hello friends" in rendered
    assert "German" in rendered
    assert "{{content}}" not in rendered
    # Shared helper: missing {{content}} appends the text.
    assert "Hi" in render_prompt("Fix this.", "Hi")


def test_conflict_detection_across_skills_and_menu():
    skills = default_skills()
    assert find_conflicts(skills, DEFAULT_HOTKEY_MENU) == []

    skills[0].hotkey = "Ctrl+Alt+M"
    conflicts = find_conflicts(skills, DEFAULT_HOTKEY_MENU)
    assert len(conflicts) == 1
    assert "Fix" in conflicts[0].describe()
    assert "Skill menu" in conflicts[0].describe()
    assert conflicts[0].hotkey == "Ctrl+Alt+M"

    skills[0].hotkey = DEFAULT_HOTKEY
    skills[2].hotkey = DEFAULT_HOTKEY
    conflicts = find_conflicts(skills, DEFAULT_HOTKEY_MENU)
    assert any(item.hotkey == parse_hotkey(DEFAULT_HOTKEY).display for item in conflicts)
    assert any({item.left, item.right} == {"Fix", "Translate"} or {"Fix", "Translate"} <= {item.left, item.right} for item in conflicts)

    skills[2].enabled = False
    assert find_conflicts(skills, DEFAULT_HOTKEY_MENU) == []

    skills[2].enabled = True
    skills[2].hotkey = "none"
    assert find_conflicts(skills, "none") == []


def test_disabled_and_invalid_hotkeys_are_ignored():
    skill = new_custom_skill("Broken")
    skill.hotkey = "not-a-hotkey"
    assert skill.hotkey_obj().is_disabled
    assert find_conflicts([skill], "none") == []


def test_cannot_delete_builtin_skills_but_can_duplicate_and_reorder():
    store = default_store()
    assert delete_skill(store, SKILL_FIX) is False
    assert store.get(SKILL_FIX) is not None
    custom = new_custom_skill("Summarize")
    store.skills.append(custom)
    assert delete_skill(store, custom.id) is True
    assert store.get(custom.id) is None

    store.skills.append(new_custom_skill("A"))
    store.skills.append(new_custom_skill("B"))
    first_custom = store.skills[-2]
    assert move_skill(store, first_custom.id, 1) is True
    assert store.skills[-1].id == first_custom.id
    copy = duplicate_skill(store.get(SKILL_TRANSLATE))
    assert copy.id != SKILL_TRANSLATE
    assert copy.builtin is False
    assert copy.offline_fallback is False
    assert copy.hotkey_obj().is_disabled
    assert copy.name == "Translate copy"


def test_enabled_skills_hide_ai_skills_when_offline():
    store = default_store()
    store.skills.append(new_custom_skill("Summarize"))
    names = [skill.name for skill in enabled_skills(store, ai_ready=False)]
    assert names == ["Fix", "Fix all"]
    names_on = [skill.name for skill in enabled_skills(store, ai_ready=True)]
    assert "Translate" in names_on and "Summarize" in names_on


def test_legacy_from_store_maps_builtins():
    store = default_store(fix_hotkey="F3", translate_select_all=True)
    mapped = legacy_from_store(store)
    assert mapped["hotkey"].display == "F3"
    assert mapped["translate_select_all"] is True
    assert mapped["fix_prompt"] == DEFAULT_FIX_PROMPT
    assert mapped["translate_prompt"] == DEFAULT_TRANSLATE_PROMPT


def test_ensure_builtins_restores_missing_fix():
    store = default_store()
    store.skills = [skill for skill in store.skills if skill.id != SKILL_FIX]
    store.skills.append(new_custom_skill("Custom"))
    restored = ensure_builtins(store)
    assert restored.skills[0].id == SKILL_FIX
    assert restored.get("Custom") is None  # id is uuid; just check count
    assert any(skill.name == "Custom" for skill in restored.skills)


def test_example_specs_are_documented():
    names = [name for name, _prompt in EXAMPLE_SKILL_SPECS]
    assert names == ["Make formal email", "Summarize", "Translate to German"]
    for _name, prompt in EXAMPLE_SKILL_SPECS:
        assert "{{content}}" in prompt
