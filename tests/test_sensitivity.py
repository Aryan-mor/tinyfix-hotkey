from pathlib import Path

import pytest

from tinyfix_lib import DEFAULT_SENSITIVITY, load_tinyfix, normalize_sensitivity

ROOT = Path(__file__).resolve().parents[1]
MODELS = ROOT / "models"
SAMPLE = "I got a letter form the bank. Their is a problem, and I am to tired to recieve it."
EXPECTED = "I got a letter from the bank. There is a problem, and I am too tired to receive it."
CLEAN = (
    "The committee will meet tomorrow to discuss the proposal in detail, "
    "and everyone is expected to arrive on time with their notes. "
    "We should also review the budget before Friday and share a short summary "
    "with the rest of the team. They walked to and fro across the room."
)

pytestmark = pytest.mark.skipif(
    not (MODELS / "wrong-word.onnx").exists(),
    reason="model files have not been fetched yet",
)


@pytest.fixture(scope="module")
def engine():
    return load_tinyfix(MODELS, sensitivity=DEFAULT_SENSITIVITY)


def test_normalize_sensitivity_aliases_and_fallback():
    assert normalize_sensitivity("Cautious") == "cautious"
    assert normalize_sensitivity("BALANCED") == "balanced"
    assert normalize_sensitivity("aggressive") == "aggressive"
    assert normalize_sensitivity("unknown") == "cautious"
    assert normalize_sensitivity(None) == "cautious"


def test_each_preset_still_fixes_the_sample(engine):
    for name in ("cautious", "balanced", "aggressive"):
        engine.apply_sensitivity(name)
        assert engine.fix(SAMPLE) == EXPECTED
        assert engine.sensitivity == name


def test_clean_english_is_unchanged_on_every_preset(engine):
    for name in ("cautious", "balanced", "aggressive"):
        engine.apply_sensitivity(name)
        assert engine.fix(CLEAN) == CLEAN


def test_please_creat_is_fixed_on_every_preset(engine):
    text = "Please creat an account and wait for now."
    expected = "Please create an account and wait for now."
    for name in ("cautious", "balanced", "aggressive"):
        engine.apply_sensitivity(name)
        assert engine.fix(text) == expected


def test_cautious_leaves_harder_creatives_and_fro(engine):
    engine.apply_sensitivity("cautious")
    assert "creat permission" in engine.fix("create a plan for creat permission scope in app.")
    assert "scope fro now" in engine.fix("This is the scope fro now and we can continue later.")


def test_aggressive_fixes_creat_and_fro_when_the_engine_can(engine):
    engine.apply_sensitivity("aggressive")
    creat = engine.fix("create a plan for creat permission scope in app.")
    assert "create permission" in creat
    assert "creat permission" not in creat
    fro_text = engine.fix("This is the scope fro now and we can continue later.")
    assert "scope for now" in fro_text
    assert "scope fro now" not in fro_text
    # Do not smash the legitimate phrase "to and fro".
    assert engine.fix("They walked to and fro across the room.") == "They walked to and fro across the room."


def test_apply_sensitivity_is_live_without_reload(engine):
    engine.apply_sensitivity("cautious")
    hard = "This is the scope fro now and we can continue later."
    cautious_out = engine.fix(hard)
    engine.apply_sensitivity("aggressive")
    aggressive_out = engine.fix(hard)
    assert cautious_out != aggressive_out
    assert "scope for now" in aggressive_out
