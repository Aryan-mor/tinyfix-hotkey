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
