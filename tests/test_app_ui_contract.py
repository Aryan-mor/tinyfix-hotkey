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



def test_v132_defaults_and_review_followups():
    app = (ROOT / "app.py").read_text(encoding="utf-8")
    iss = (ROOT / "installer" / "tinyfix.iss").read_text(encoding="utf-8")
    menu = (ROOT / "skill_menu.py").read_text(encoding="utf-8")
    settings = (ROOT / "settings_ui.py").read_text(encoding="utf-8")

    assert "resolve_google_model" in app
    start = app.index("def start_ui_thread")
    end = app.index("def _ui_thread_main")
    body = app[start:end]
    assert "_ui_thread_lock" in body
    assert "with _ui_thread_lock" in body

    start = app.index("def prompt_update")
    end = app.index("def _is_rate_limit_error")
    body = app[start:end]
    assert "tk.Tk(" not in body
    assert "parent = ui_root" in body
    assert "tinyfix-update-download" in body

    assert "aborting setup" in iss or "still running from the install folder, so setup cannot replace" in iss
    assert "Result :=" in iss[iss.index("function PrepareToInstall"):iss.index("function InitializeUninstall")]
    # Only reject {app} inside Pascal brace comments, not ExpandConstant('{app}').
    for raw_line in iss[iss.index("[Code]"):].splitlines():
        line = raw_line.strip()
        if line.startswith("{") and line.endswith("}") and "{app}" in line:
            raise AssertionError("Pascal comment contains {app}: " + line)

    assert "allow_topmost" in menu
    assert "HWND_NOTOPMOST" in menu
    assert "flush_pending_hotkeys" in menu
    assert "WM_HOTKEY" in menu
    assert "DEFAULT_GOOGLE_MODEL" in settings
    assert "Default. A little more willing" in settings


def test_hidden_ui_root_is_never_made_topmost():
    from pathlib import Path

    app = (Path(__file__).resolve().parents[1] / "app.py").read_text(encoding="utf-8")
    assert "def _transient_dialog_host" in app
    info = app.split("def _show_info_dialog", 1)[1].split("def ", 1)[0]
    assert "attributes(\"-topmost\", True)" not in info
    assert "_transient_dialog_host" in info
    # prompt_update should also use the transient host, not the hidden root
    prompt = app.split("def prompt_update", 1)[1]
    prompt = prompt.split("\ndef ", 1)[0] if "\ndef " in prompt else prompt[:4000]
    # The first function body; use a unique marker
    start = app.index("def prompt_update")
    end = app.index("def ", start + 1)
    # There are nested defs (work). Find from prompt_update to start_silent chunk is enough:
    chunk = app[start:start + 2500]
    assert "_transient_dialog_host" in chunk
    assert 'parent.attributes("-topmost", True)' not in chunk


def test_legacy_hotkey_dialog_releases_grab():
    from pathlib import Path

    app = (Path(__file__).resolve().parents[1] / "app.py").read_text(encoding="utf-8")
    start = app.index("def show_hotkey_dialog")
    chunk = app[start:start + 8000]
    assert "grab_set" in chunk
    assert "grab_release" in chunk


def test_send_ctrl_key_sends_keyups_if_incomplete():
    from pathlib import Path

    app = (Path(__file__).resolve().parents[1] / "app.py").read_text(encoding="utf-8")
    start = app.index("def send_ctrl_key")
    end = app.index("def wait_for_clipboard_change")
    chunk = app[start:end]
    assert "_key_event(vk, True)" in chunk
    assert "_key_event(VK_CONTROL, True)" in chunk
    assert "sent != 4" in chunk


def test_settings_save_skips_unchanged_sensitivity_and_single_write():
    from pathlib import Path

    app = (Path(__file__).resolve().parents[1] / "app.py").read_text(encoding="utf-8")
    start = app.index("def apply_settings_data")
    chunk = app[start:start + 2500]
    assert "persist=False" in chunk
    assert "sensitivity unchanged" in chunk
    assert "elapsed=" in chunk
    assert "load_tinyfix" not in chunk


def test_api_key_acl_is_applied_in_background():
    from pathlib import Path

    source = (Path(__file__).resolve().parents[1] / "secret_store.py").read_text(encoding="utf-8")
    start = source.index("def _restrict_to_current_user")
    end = source.index("def save_api_key")
    chunk = source[start:end]
    assert "threading.Thread" in chunk
    assert "icacls" in chunk
