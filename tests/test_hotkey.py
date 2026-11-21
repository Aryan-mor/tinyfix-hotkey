from pathlib import Path

import pytest

from hotkey_util import (
    DEFAULT_HOTKEY,
    DEFAULT_HOTKEY_ALL,
    DEFAULT_HOTKEY_TRANSLATE,
    MOD_ALT,
    MOD_CONTROL,
    MOD_NOREPEAT,
    disabled_hotkey,
    is_disabled_spec,
    parse_hotkey,
)


def test_default_hotkey_is_ctrl_alt_space():
    hotkey = parse_hotkey(DEFAULT_HOTKEY)
    assert hotkey.display == "Ctrl+Alt+Space"
    assert hotkey.vk == 0x20
    assert hotkey.modifiers & MOD_CONTROL
    assert hotkey.modifiers & MOD_ALT
    assert hotkey.register_modifiers & MOD_NOREPEAT
    assert hotkey.is_safe()
    assert not hotkey.is_disabled


def test_default_fix_all_hotkey_is_ctrl_f3():
    hotkey = parse_hotkey(DEFAULT_HOTKEY_ALL)
    assert hotkey.display == "Ctrl+F3"
    assert hotkey.vk == 0x72
    assert hotkey.modifiers & MOD_CONTROL
    assert hotkey.is_safe()


def test_parses_function_key_without_modifier():
    hotkey = parse_hotkey("F8")
    assert hotkey.display == "F8"
    assert hotkey.vk == 0x77
    assert hotkey.is_function_key
    assert hotkey.is_safe()


def test_letter_without_modifier_is_unsafe():
    hotkey = parse_hotkey("A")
    assert not hotkey.is_safe()


def test_canonicalizes_separators_and_case():
    hotkey = parse_hotkey("ctrl-alt-space")
    assert hotkey.display == "Ctrl+Alt+Space"


def test_rejects_empty_and_double_keys():
    with pytest.raises(ValueError):
        parse_hotkey("")
    with pytest.raises(ValueError):
        parse_hotkey("Ctrl+Alt")
    with pytest.raises(ValueError):
        parse_hotkey("Ctrl+A+B")


@pytest.mark.parametrize("spec", ["none", "None", "disabled", "Disabled", "off", "no", "None/Disabled", "none / disabled"])
def test_disabled_hotkey_specs(spec):
    assert is_disabled_spec(spec)
    hotkey = parse_hotkey(spec)
    assert hotkey.is_disabled
    assert hotkey.display == "None"
    assert hotkey.is_safe()
    assert hotkey == disabled_hotkey()


def test_both_hotkeys_can_be_disabled():
    selection = parse_hotkey("none")
    fix_all = parse_hotkey("off")
    assert selection.is_disabled and fix_all.is_disabled



def test_default_translate_hotkey_is_ctrl_alt_t():
    hotkey = parse_hotkey(DEFAULT_HOTKEY_TRANSLATE)
    assert hotkey.display == "Ctrl+Alt+T"
    assert hotkey.vk == ord("T")
    assert hotkey.modifiers & MOD_CONTROL
    assert hotkey.modifiers & MOD_ALT
    assert hotkey.is_safe()
