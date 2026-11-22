import json
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest

from llm_client import (
    DEFAULT_GOOGLE_MODEL,
    LLMConfig,
    LLMTimeout,
    complete,
    correct_with_fallback,
    resolve_google_model,
    sanitize_error,
    probe_connection,
)


class _Handler(BaseHTTPRequestHandler):
    delay = 0.0
    status = 200
    body = {"choices": [{"message": {"content": "Hi friends"}}]}

    def log_message(self, format, *args):
        return

    def do_POST(self):
        if self.delay:
            time.sleep(self.delay)
        payload = json.dumps(self.body).encode("utf-8")
        self.send_response(self.status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        if self.status == 200:
            self.wfile.write(payload)

    def do_GET(self):
        self.do_POST()


@pytest.fixture
def openai_server():
    server = ThreadingHTTPServer(("127.0.0.1", 0), _Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    host, port = server.server_address
    yield f"http://127.0.0.1:{port}/v1", server
    server.shutdown()
    server.server_close()


class _StubEngine:
    has_model = True  # offline pack on: the user's short timeout applies

    def fix(self, text: str) -> str:
        return text.replace("frinds", "friends") + " [offline]"


def test_openai_compatible_completion_and_test_button(openai_server):
    base, server = openai_server
    _Handler.delay = 0.0
    _Handler.status = 200
    config = LLMConfig(
        provider="openai_compatible",
        api_key="sk-test-key",
        model="local-model",
        base_url=base,
        timeout=2,
        enabled=True,
    )
    assert complete(config, "Hi frinds") == "Hi friends"
    assert probe_connection(config, timeout=2) == "Hi friends"


def test_timeout_falls_back_to_offline_engine(openai_server):
    base, server = openai_server
    _Handler.delay = 1.5
    _Handler.status = 200
    config = LLMConfig(
        provider="openai_compatible",
        api_key="sk-test-key",
        model="local-model",
        base_url=base,
        timeout=0.2,
        enabled=True,
    )
    with pytest.raises(LLMTimeout):
        complete(config, "Hi frinds")
    result, source = correct_with_fallback("Hi frinds", _StubEngine(), config)
    assert source == "fallback"
    assert result == "Hi friends [offline]"


def test_http_error_falls_back_to_offline_engine(openai_server):
    base, server = openai_server
    _Handler.delay = 0.0
    _Handler.status = 500
    config = LLMConfig(
        provider="openai_compatible",
        api_key="sk-test-key",
        model="local-model",
        base_url=base,
        timeout=2,
        enabled=True,
    )
    result, source = correct_with_fallback("Hi frinds", _StubEngine(), config)
    assert source == "fallback"
    assert "[offline]" in result
    _Handler.status = 200


def test_disabled_or_missing_key_stays_offline():
    engine = _StubEngine()
    result, source = correct_with_fallback("Hi frinds", engine, None)
    assert source == "offline"
    config = LLMConfig(enabled=True, api_key="", provider="google", model=DEFAULT_GOOGLE_MODEL)
    result, source = correct_with_fallback("Hi frinds", engine, config)
    assert source == "offline"
    assert result.endswith("[offline]")


def test_sanitize_error_redacts_key_and_bearer():
    key = "sk-secret-12345"
    text = sanitize_error(f"Could not reach https://example.invalid/v1?key={key} Bearer {key}", key)
    assert key not in text
    assert "***" in text


def test_remote_http_base_url_is_rejected():
    from llm_client import LLMError, _normalize_openai_base

    with pytest.raises(LLMError, match="localhost"):
        _normalize_openai_base("http://example.invalid/v1")
    assert _normalize_openai_base("http://127.0.0.1:1234/v1") == "http://127.0.0.1:1234/v1"
    assert _normalize_openai_base("https://openrouter.ai/api/v1") == "https://openrouter.ai/api/v1"


def test_resolve_google_model_keeps_chosen_and_fills_empty():
    assert DEFAULT_GOOGLE_MODEL == "gemini-3.5-flash-lite"
    assert resolve_google_model("") == DEFAULT_GOOGLE_MODEL
    assert resolve_google_model("   ") == DEFAULT_GOOGLE_MODEL
    assert resolve_google_model(None) == DEFAULT_GOOGLE_MODEL
    assert resolve_google_model("gemini-2.0-flash") == "gemini-2.0-flash"
    assert resolve_google_model("gemini-3.5-flash-lite") == "gemini-3.5-flash-lite"


def test_skills_get_a_longer_timeout_than_the_fix_path(monkeypatch):
    import llm_client

    seen = []
    monkeypatch.setattr(llm_client, "complete", lambda cfg, prompt, timeout=None: seen.append(timeout) or "ok")
    cfg = llm_client.LLMConfig(provider="google", api_key="k", enabled=True, timeout=2.0, model="m")
    assert llm_client.run_skill_online(cfg, "{{content}}", "hi") == "ok"
    assert seen == [llm_client.SKILL_MIN_TIMEOUT]
    cfg.timeout = 30.0  # a larger user setting is respected
    llm_client.run_skill_online(cfg, "{{content}}", "hi")
    assert seen[-1] == 30.0


class _Engine:
    def __init__(self, has_model):
        self.has_model = has_model

    def fix(self, text):
        return "OFFLINE:" + text


def _cfg(timeout):
    import llm_client

    return llm_client.LLMConfig(provider="google", api_key="k", enabled=True, timeout=timeout, model="m")


def test_fix_timeout_is_at_least_10s_without_a_local_model():
    import llm_client

    assert llm_client.fix_timeout(_cfg(2.0), _Engine(False)) == 10.0
    assert llm_client.fix_timeout(_cfg(2.0), None) == 10.0
    assert llm_client.fix_timeout(_cfg(30.0), _Engine(False)) == 30.0  # a higher setting is respected


def test_fix_timeout_keeps_the_users_setting_with_a_local_model():
    import llm_client

    assert llm_client.fix_timeout(_cfg(2.0), _Engine(True)) == 2.0
    assert llm_client.fix_timeout(_cfg(5.0), _Engine(True)) == 5.0


def test_correct_with_fallback_passes_the_right_timeout(monkeypatch):
    import llm_client

    seen = []
    monkeypatch.setattr(llm_client, "complete", lambda cfg, prompt, timeout=None: seen.append(timeout) or "fixed")
    assert llm_client.correct_with_fallback("x", _Engine(False), _cfg(2.0)) == ("fixed", "online")
    assert llm_client.correct_with_fallback("x", _Engine(True), _cfg(2.0)) == ("fixed", "online")
    assert seen == [10.0, 2.0]


def test_a_slow_online_model_still_falls_back_to_the_engine(monkeypatch):
    import llm_client

    def slow(cfg, prompt, timeout=None):
        raise llm_client.LLMTimeout("timed out")

    monkeypatch.setattr(llm_client, "complete", slow)
    assert llm_client.correct_with_fallback("x", _Engine(False), _cfg(2.0)) == ("OFFLINE:x", "fallback")


def test_slow_server_is_waited_for_when_there_is_no_local_model(openai_server):
    """Same slow server as the fallback test, but without the offline pack: 10 s is enough, so no fallback."""
    base, server = openai_server
    _Handler.delay = 0.6
    _Handler.status = 200
    config = LLMConfig(provider="openai_compatible", api_key="sk-test-key", model="local-model", base_url=base, timeout=0.2, enabled=True)

    class NoLocal(_StubEngine):
        has_model = False

    _result, source = correct_with_fallback("Hi frinds", NoLocal(), config)
    assert source == "online"


def test_skill_models_default_better_and_custom():
    from llm_client import BETTER_MODELS, LLMConfig, resolve_skill_config

    base = LLMConfig(provider="google", api_key="g", model="gemini-3.5-flash-lite", fallback_model="gemini-3.6-flash", enabled=True)
    assert resolve_skill_config(base, "default") is base
    better = resolve_skill_config(base, "better")
    assert (better.model, better.fallback_model) == BETTER_MODELS["google"]
    assert better.provider == "google" and better.api_key == "g" and better.fallback_provider == ""
    # a provider without a preset keeps the user's models
    plain = LLMConfig(provider="openai_compatible", api_key="k", model="m", enabled=True)
    assert resolve_skill_config(plain, "better") is plain

    custom = {"provider": "openrouter", "model": "anthropic/claude-sonnet-5.5", "fallback_provider": "google", "fallback_model": "gemini-3.5-flash-lite"}
    cfg = resolve_skill_config(base, "custom", custom, {"openrouter": "or", "google": "g"})
    assert (cfg.provider, cfg.api_key, cfg.model) == ("openrouter", "or", "anthropic/claude-sonnet-5.5")
    assert (cfg.fallback_provider, cfg.fallback_api_key, cfg.fallback_model) == ("google", "g", "gemini-3.5-flash-lite")
    # no key for the chosen provider: fall back to the Online AI settings instead of failing
    assert resolve_skill_config(base, "custom", custom, {}) is base
    # empty custom fields mean "use the global ones"
    assert resolve_skill_config(base, "custom", {}, {}).model == "gemini-3.5-flash-lite"
