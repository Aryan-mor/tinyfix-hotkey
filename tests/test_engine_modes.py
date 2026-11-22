"""Dictionary-only vs offline-pack engines, and the startup fallback when the pack is broken."""

from __future__ import annotations

import subprocess
import sys
import time
from pathlib import Path

import pytest

import offline_pack as op
from tests.pack_helpers import make_zip
from tinyfix_lib import load_tinyfix

ROOT = Path(__file__).resolve().parents[1]
MODELS = ROOT / "models"

TYPOS = "I recieved teh mesage and it was realy good."
CONTEXT = "Their going to the store."
CLEAN = "The quick brown fox jumps over the lazy dog."

needs_model = pytest.mark.skipif(not (MODELS / "wrong-word.onnx").exists(), reason="model missing")


@pytest.fixture(scope="module")
def base():
    return load_tinyfix(MODELS, use_model=False)


@pytest.fixture(scope="module")
def full():
    return load_tinyfix(MODELS, use_model=True)


def test_base_engine_fixes_spelling_without_numpy():
    code = (
        "import sys; sys.path.insert(0, r'%s');"
        "from pathlib import Path; from tinyfix_lib import load_tinyfix;"
        "e = load_tinyfix(Path(r'%s'), use_model=False);"
        "print(e.fix(%r)); print('numpy' in sys.modules, 'onnxruntime' in sys.modules, e.has_model)"
        % (ROOT, MODELS, TYPOS)
    )
    out = subprocess.run([sys.executable, "-I", "-c", code], capture_output=True, text=True, timeout=60)
    assert out.returncode == 0, out.stderr
    fixed, flags = out.stdout.strip().splitlines()
    assert fixed == "I received the message and it was really good."
    assert flags == "False False False"


def test_base_engine_leaves_clean_text_alone(base):
    assert base.fix(CLEAN) == CLEAN
    assert base.fix("") == ""


@needs_model
def test_pack_engine_adds_context_fixes_base_cannot(base, full):
    assert full.has_model and not base.has_model
    assert base.fix(CONTEXT) == CONTEXT
    assert "They're" in full.fix(CONTEXT)


@needs_model
def test_both_engines_agree_on_plain_typos_and_clean_text(base, full):
    for text in (TYPOS, CLEAN, "Teh cat sat on teh mat.", "Hello wrold"):
        assert base.fix(text) == full.fix(text), text


@pytest.mark.parametrize("name", ["cautious", "balanced", "aggressive"])
def test_every_sensitivity_works_without_the_pack(base, name):
    base.apply_sensitivity(name)
    assert base.fix(TYPOS) == "I received the message and it was really good."
    base.apply_sensitivity("balanced")


def test_large_text_finishes_quickly_in_base_mode(base):
    text = (TYPOS + " ") * 100  # ~4,400 characters, 400 misspellings (measured: about 70 ms per misspelled word)
    t0 = time.time()
    out = base.fix(text)
    assert time.time() - t0 < 60
    assert "recieved" not in out


@needs_model
def test_large_text_finishes_quickly_with_pack(full):
    text = (CONTEXT + " " + TYPOS + " ") * 100
    t0 = time.time()
    full.fix(text)
    assert time.time() - t0 < 60


# ------------------------------------------------------------------ app.load_engine fallbacks


@pytest.fixture
def app_module(tmp_path, monkeypatch):
    import app

    monkeypatch.setattr(app, "PACK_DIR", tmp_path / "pack")
    monkeypatch.setattr(app, "CONFIG_PATH", tmp_path / "config.ini")
    monkeypatch.setattr(app, "current_sensitivity", "balanced", raising=False)
    monkeypatch.setattr(op, "_activated", None)
    monkeypatch.setattr(sys, "path", list(sys.path))
    return app


def test_startup_without_pack_uses_dictionary_engine(app_module):
    engine = app_module.load_engine()
    assert app_module.engine_mode == "dictionary" and not engine.has_model
    assert "not installed" in app_module.engine_note.lower()


def test_startup_with_switch_off_ignores_an_installed_pack(app_module, tmp_path):
    op.install_zip(app_module.PACK_DIR, make_zip(tmp_path / "p.zip"))
    app_module.set_offline_engine_enabled(False)
    engine = app_module.load_engine()
    assert app_module.engine_mode == "dictionary" and not engine.has_model
    assert "switched off" in app_module.engine_note


def test_startup_with_broken_pack_never_crashes(app_module, tmp_path):
    # A pack that passes validation but whose "onnxruntime" cannot load: garbage model, no real libs.
    op.install_zip(app_module.PACK_DIR, make_zip(tmp_path / "p.zip"))
    engine = app_module.load_engine()
    assert app_module.engine_mode == "dictionary" and not engine.has_model
    assert engine.fix(TYPOS) == "I received the message and it was really good."
    assert app_module.engine_note


def test_startup_with_incompatible_pack_explains_why(app_module, tmp_path):
    (app_module.PACK_DIR / "current").mkdir(parents=True)
    (app_module.PACK_DIR / "current" / "manifest.json").write_text('{"format": 1, "pack_version": 0, "python": "3.13"}')
    engine = app_module.load_engine()
    assert not engine.has_model
    assert "out of date" in app_module.engine_note or "different" in app_module.engine_note


def test_pending_removal_is_applied_before_the_pack_is_loaded(app_module, tmp_path):
    op.install_zip(app_module.PACK_DIR, make_zip(tmp_path / "p.zip"))
    op.request_removal(app_module.PACK_DIR)
    app_module.load_engine()
    assert not (app_module.PACK_DIR / "current").exists()
    assert not op.removal_pending(app_module.PACK_DIR)


def test_pack_info_and_remove_flow(app_module, tmp_path):
    info = app_module.pack_info()
    assert not info["installed"] and info["download_mb"] and info["ram_mb"]
    op.install_zip(app_module.PACK_DIR, make_zip(tmp_path / "p.zip"))
    assert app_module.pack_info()["installed"]
    message = app_module.pack_remove()
    assert "removed" in message.lower()
    assert not app_module.pack_info()["installed"]
