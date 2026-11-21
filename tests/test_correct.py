from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
MODELS = ROOT / "models"
SAMPLE = "I got a letter form the bank. Their is a problem, and I am to tired to recieve it."
EXPECTED = "I got a letter from the bank. There is a problem, and I am too tired to receive it."

pytestmark = pytest.mark.skipif(
    not (MODELS / "wrong-word.onnx").exists(),
    reason="model files have not been fetched yet",
)


@pytest.fixture(scope="module")
def engine():
    from tinyfix_lib import load_tinyfix

    return load_tinyfix(MODELS, mode="balanced")


def test_real_word_and_spelling_corrections(engine):
    assert engine.fix(SAMPLE) == EXPECTED


def test_individual_expected_replacements(engine):
    fixed = engine.fix(SAMPLE)
    assert "from" in fixed
    assert "There" in fixed
    assert "too tired" in fixed
    assert "receive" in fixed
    assert "form" not in fixed
    assert "Their" not in fixed
    assert "recieve" not in fixed


def test_persian_text_is_left_unchanged(engine):
    text = "سلام خوبی؟ این یک متن فارسی است."
    assert engine.fix(text) == text
