from pathlib import Path

import pytest

from tinyfix_lib import Fix, apply_fixes, plan_clipboard_fix


class _StubEngine:
    def __init__(self, mapping: dict[str, list[Fix]]):
        self.mapping = mapping

    def check(self, text: str) -> list[Fix]:
        return list(self.mapping.get(text, []))

    def fix(self, text: str) -> str:
        return apply_fixes(text, self.check(text)) if self.check(text) else text


def test_clipboard_has_no_text():
    engine = _StubEngine({})
    message, new_text = plan_clipboard_fix(engine, None)
    assert message == "Clipboard has no text"
    assert new_text is None
    message, new_text = plan_clipboard_fix(engine, "")
    assert message == "Clipboard has no text"
    assert new_text is None


def test_clipboard_no_typos():
    engine = _StubEngine({})
    message, new_text = plan_clipboard_fix(engine, "Hello friends")
    assert message == "No typos found in clipboard"
    assert new_text is None


def test_clipboard_fixed_plural_and_singular():
    text = "Hi frinds"
    engine = _StubEngine(
        {
            text: [Fix(start=3, end=9, original="frinds", replacement="friends", kind="spelling", confidence=0.9)],
        }
    )
    message, new_text = plan_clipboard_fix(engine, text)
    assert message == "Clipboard fixed: 1 change"
    assert new_text == "Hi friends"

    longer = "Hi frinds recieve this"
    engine = _StubEngine(
        {
            longer: [
                Fix(start=3, end=9, original="frinds", replacement="friends", kind="spelling", confidence=0.9),
                Fix(start=10, end=17, original="recieve", replacement="receive", kind="spelling", confidence=0.9),
            ]
        }
    )
    message, new_text = plan_clipboard_fix(engine, longer)
    assert message == "Clipboard fixed: 2 changes"
    assert new_text == "Hi friends receive this"
