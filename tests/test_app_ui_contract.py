"""Source-level contracts for tray UI behaviour that is hard to unit-test in isolation."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def test_manual_update_check_always_has_visible_feedback():
    text = (ROOT / "app.py").read_text(encoding="utf-8")
    assert "Checking…" in text or "Checking..." in text
    assert "def safe_tray_action" in text
    assert "pystray swallows" in text or "Tray menu action failed" in text
    assert "should_auto_check" in text
    # The manual worker must not call should_auto_check.
    start = text.index("def manual_update_check_worker")
    end = text.index("def check_for_updates_menu")
    body = text[start:end]
    assert "should_auto_check" not in body
    assert "format_update_feedback" in body
    assert "update-error" in body
    assert "update-latest" in body


def test_about_menu_and_versioned_tooltip_exist():
    text = (ROOT / "app.py").read_text(encoding="utf-8")
    assert "About TinyFix" in text
    assert "TinyFix v{APP_VERSION}" in text
    assert "def about_info" in text
    assert "model_revision" in text
    assert "start_ui_thread" in text
    iss = (ROOT / "installer" / "tinyfix.iss").read_text(encoding="utf-8")
    assert "AppWasRunning" in iss
    assert "RunningCheck" in iss
    assert "AppWasRunning or HasCommandlineFlag('LAUNCH')" in iss
    assert "PrepareToInstall" in iss
    assert "taskkill.exe" not in iss.lower()
