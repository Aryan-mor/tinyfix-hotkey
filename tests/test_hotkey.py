from hotkey_util import DEFAULT_HOTKEY, MOD_ALT, MOD_CONTROL, MOD_NOREPEAT, parse_hotkey


def test_default_hotkey_is_ctrl_alt_space():
    hotkey = parse_hotkey(DEFAULT_HOTKEY)
    assert hotkey.display == "Ctrl+Alt+Space"
    assert hotkey.vk == 0x20
    assert hotkey.modifiers & MOD_CONTROL
    assert hotkey.modifiers & MOD_ALT
    assert hotkey.register_modifiers & MOD_NOREPEAT
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
    import pytest

    with pytest.raises(ValueError):
        parse_hotkey("")
    with pytest.raises(ValueError):
        parse_hotkey("Ctrl+Alt")
    with pytest.raises(ValueError):
        parse_hotkey("Ctrl+A+B")
