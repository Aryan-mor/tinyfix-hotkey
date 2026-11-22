import llm_client
from llm_client import LLMConfig, LLMError, LLMTimeout


def _config():
    return LLMConfig(api_key="k", model="main", fallback_model="backup", enabled=True)


def test_timeout_uses_fallback_then_sticks_for_the_window(monkeypatch):
    llm_client._fallback_until.clear()
    calls = []

    def fake(config, prompt, timeout):
        calls.append(config.model)
        if config.model == "main":
            raise LLMTimeout("Timed out")
        return "ok"

    monkeypatch.setattr(llm_client, "_complete_gemini", fake)
    assert llm_client.complete(_config(), "p") == "ok"
    assert calls == ["main", "backup"]
    assert llm_client.complete(_config(), "p") == "ok"
    assert calls == ["main", "backup", "backup"]


def test_main_model_returns_after_the_window(monkeypatch):
    llm_client._fallback_until.clear()
    llm_client._fallback_until["main"] = 0.0
    calls = []
    monkeypatch.setattr(llm_client, "_complete_gemini", lambda c, p, t: calls.append(c.model) or "ok")
    llm_client.complete(_config(), "p")
    assert calls == ["main"]


def test_bad_key_does_not_fall_back(monkeypatch):
    llm_client._fallback_until.clear()
    calls = []

    def fake(config, prompt, timeout):
        calls.append(config.model)
        raise LLMError("HTTP 403: key invalid")

    monkeypatch.setattr(llm_client, "_complete_gemini", fake)
    try:
        llm_client.complete(_config(), "p")
    except LLMError:
        pass
    assert calls == ["main"]
