"""Fix and Fix all must work for any language through the online model, and never run the English-only engine on
other languages."""

from __future__ import annotations

from pathlib import Path

import pytest

import llm_client
from language_guess import looks_english
from llm_client import LLMConfig, LLMError, LLMTimeout, correct_with_fallback, fix_timeout
from prompts import DEFAULT_FIX_PROMPT, render_prompt

ROOT = Path(__file__).resolve().parents[1]

PERSIAN = "این تنمظات رو برای هممه بذار"
PERSIAN_FIXED = "این تنظیمات رو برای همه بذار"
GERMAN = "Ich habe das gesterm geschickt und es war sher gut"
ENGLISH = "I recieved teh mesage yesterday and it was realy good, but their going to the store tomorow."


class Engine:
    """Stands in for the English-only engine; records whether it was asked to touch anything."""

    def __init__(self, has_model=False):
        self.has_model = has_model
        self.touched = []

    def fix(self, text):
        self.touched.append(text)
        return "OFFLINE:" + text


def cfg(timeout=2.0):
    return LLMConfig(provider="google", api_key="k", enabled=True, timeout=timeout, model="m")


@pytest.fixture
def online(monkeypatch):
    """Control the online model: set .reply to a string or an exception; .timeouts records the wait asked for."""

    class Online:
        reply = PERSIAN_FIXED
        timeouts: list = []

    state = Online()
    state.timeouts = []

    def fake(config, prompt, timeout=None):
        state.timeouts.append(timeout)
        if isinstance(state.reply, Exception):
            raise state.reply
        return state.reply

    monkeypatch.setattr(llm_client, "complete", fake)
    return state


# ------------------------------------------------------------------ guessing the language


@pytest.mark.parametrize(
    "text, english",
    [
        (ENGLISH, True),
        ("Please send me the report by friday", True),
        ("hello wrold", True),
        ("teh", True),
        ("12345 + 6", True),
        ("", True),
        ("def f(x): return x*2  # double the value of the input", True),
        (PERSIAN, False),
        ("این رو با Slack بفرست لطفا", False),
        ("مرحبا كيف حالكم اليوم انا بخير شكرا", False),
        ("こんにちわ、元気ですか", False),
        (GERMAN, False),
        ("Hola, quiero resivir el mensage de ayer, gracias por todo", False),
        ("Je voudrais recevoir le mesage de hier s'il vous plait", False),
    ],
)
def test_looks_english(text, english):
    assert looks_english(text) is english


# ------------------------------------------------------------------ the wait for the online model


def test_non_english_text_waits_at_least_20_seconds_even_with_the_pack():
    pack = Engine(has_model=True)
    assert fix_timeout(cfg(2.0), pack, PERSIAN) == 20.0
    assert fix_timeout(cfg(2.0), pack, GERMAN) == 20.0
    assert fix_timeout(cfg(30.0), pack, PERSIAN) == 30.0  # a larger setting wins


def test_english_keeps_the_short_wait_with_the_pack_and_10s_without():
    assert fix_timeout(cfg(2.0), Engine(has_model=True), ENGLISH) == 2.0
    assert fix_timeout(cfg(2.0), Engine(has_model=False), ENGLISH) == 10.0
    assert fix_timeout(cfg(2.0), Engine(has_model=True)) == 2.0  # no text given: the old rule


def test_the_wait_is_really_passed_to_the_model(online):
    correct_with_fallback(PERSIAN, Engine(), cfg(2.0))
    correct_with_fallback(ENGLISH, Engine(has_model=True), cfg(2.0))
    assert online.timeouts == [20.0, 2.0]


# ------------------------------------------------------------------ any language works through the model


def test_persian_example_is_fixed_by_the_online_model(online):
    assert correct_with_fallback(PERSIAN, Engine(), cfg()) == (PERSIAN_FIXED, "online")


def test_the_fix_prompt_sends_the_text_and_says_any_language():
    sent = render_prompt(DEFAULT_FIX_PROMPT, PERSIAN)
    assert PERSIAN in sent
    assert "any language" in DEFAULT_FIX_PROMPT and "Do not translate" in DEFAULT_FIX_PROMPT


# ------------------------------------------------------------------ no English fallback for other languages


@pytest.mark.parametrize("failure", [LLMTimeout("timed out"), LLMError("HTTP 503"), RuntimeError("boom")])
def test_failed_online_fix_of_non_english_text_raises_and_never_runs_the_english_engine(online, failure):
    engine = Engine()
    online.reply = failure
    with pytest.raises((LLMTimeout, LLMError)):
        correct_with_fallback(PERSIAN, engine, cfg())
    assert engine.touched == []


def test_empty_online_answer_for_non_english_text_raises(online):
    online.reply = ""
    with pytest.raises(LLMError):
        correct_with_fallback(GERMAN, Engine(), cfg())


def test_non_english_text_without_an_online_model_is_left_alone_with_a_clear_source():
    engine = Engine()
    assert correct_with_fallback(PERSIAN, engine, None) == (PERSIAN, "unsupported")
    disabled = LLMConfig(provider="google", api_key="", enabled=False)
    assert correct_with_fallback(GERMAN, engine, disabled) == (GERMAN, "unsupported")
    assert engine.touched == []


def test_english_still_falls_back_to_the_engine(online):
    engine = Engine()
    online.reply = LLMTimeout("timed out")
    assert correct_with_fallback(ENGLISH, engine, cfg()) == ("OFFLINE:" + ENGLISH, "fallback")
    assert correct_with_fallback(ENGLISH, engine, None) == ("OFFLINE:" + ENGLISH, "offline")


def test_short_ascii_text_keeps_the_old_behaviour(online):
    engine = Engine()
    online.reply = LLMError("down")
    assert correct_with_fallback("teh cat", engine, cfg())[1] == "fallback"


# ------------------------------------------------------------------ what the user is told


def test_the_app_tells_the_user_instead_of_saying_no_typos():
    text = (ROOT / "app.py").read_text(encoding="utf-8")
    assert 'NOT_ENGLISH_MESSAGE = "This text is not English. Turn on an online model in Settings' in text
    assert text.count("NOT_ENGLISH_MESSAGE") >= 4  # defined once, used by skills, the clipboard fix (online and offline)
    start = text.index('if origin == "unsupported":')
    assert text.index('if result == selected:') > start  # checked before the "No typos found" branch
    assert "timed out; the text was left unchanged" in text  # a failed online fix of non-English text says so


# ------------------------------------------------------------------ a slow model, end to end over HTTP


def test_a_slow_model_still_fixes_persian_instead_of_giving_up_at_2_seconds(monkeypatch):
    """The reported bug: the model took longer than the 2 s Fix limit, the English engine took over, and the Persian
    text came back unchanged. Now the Persian text waits for the model."""
    import json
    import threading
    import time
    from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

    class Slow(BaseHTTPRequestHandler):
        def log_message(self, *args):
            return

        def do_POST(self):
            time.sleep(3.0)  # longer than the 2 s setting
            body = json.dumps({"choices": [{"message": {"content": PERSIAN_FIXED}}]}, ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    server = ThreadingHTTPServer(("127.0.0.1", 0), Slow)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        config = LLMConfig(
            provider="openai_compatible", api_key="sk-test", model="m", enabled=True, timeout=2.0,
            base_url=f"http://127.0.0.1:{server.server_address[1]}/v1",
        )
        engine = Engine(has_model=True)
        t0 = time.time()
        result = correct_with_fallback(PERSIAN, engine, config)
        assert result == (PERSIAN_FIXED, "online")
        assert time.time() - t0 >= 2.9 and engine.touched == []
        # The same slow model with English text still gives up at 2 s and uses the engine, as before.
        english = correct_with_fallback(ENGLISH, engine, config)
        assert english[1] == "fallback"
    finally:
        server.shutdown()
        server.server_close()
