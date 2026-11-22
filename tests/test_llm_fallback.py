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


def test_retired_fallback_model_is_replaced_by_the_default():
    import configparser

    import app
    from llm_client import DEFAULT_GOOGLE_FALLBACK_MODEL

    parser = configparser.ConfigParser()
    parser[app.CONFIG_SECTION] = {"llm_fallback_model": "gemini-2.5-flash-lite"}
    assert app._current_fallback_model(parser) == DEFAULT_GOOGLE_FALLBACK_MODEL
    parser[app.CONFIG_SECTION]["llm_fallback_model"] = "my-own-model"
    assert app._current_fallback_model(parser) == "my-own-model"


def test_missing_model_counts_as_fallback_worthy():
    from llm_client import LLMError, _fallback_worthy

    assert _fallback_worthy(LLMError("HTTP 404: model no longer available"))
    assert not _fallback_worthy(LLMError("HTTP 400: bad request"))


def test_fallback_can_be_another_provider_with_its_own_key(monkeypatch):
    import llm_client
    from llm_client import LLMConfig, LLMTimeout, complete

    llm_client._fallback_until.clear()
    calls = []

    def fake_gemini(config, prompt, timeout):
        calls.append(("google", config.model, config.api_key))
        raise LLMTimeout("Timed out after 2.0s")

    def fake_openai(config, prompt, timeout):
        calls.append((config.provider, config.model, config.api_key))
        return "fixed"

    monkeypatch.setattr(llm_client, "_complete_gemini", fake_gemini)
    monkeypatch.setattr(llm_client, "_complete_openai", fake_openai)
    config = LLMConfig(
        provider="google", api_key="g-key", model="main", fallback_model="openai/gpt-x",
        fallback_provider="openrouter", fallback_api_key="or-key", enabled=True,
    )
    assert complete(config, "p") == "fixed"
    assert calls == [("google", "main", "g-key"), ("openrouter", "openai/gpt-x", "or-key")]


def test_fallback_on_another_provider_is_skipped_without_its_key():
    from llm_client import LLMConfig, fallback_config

    config = LLMConfig(provider="google", api_key="g", model="m", fallback_model="x", fallback_provider="openrouter", fallback_api_key="")
    assert fallback_config(config) is None
    config.fallback_api_key = "k"
    assert fallback_config(config).provider == "openrouter"
    same = LLMConfig(provider="openrouter", api_key="k", model="a", fallback_model="b")
    assert fallback_config(same).model == "b" and fallback_config(same).api_key == "k"
    assert fallback_config(LLMConfig(provider="openrouter", api_key="k", model="a", fallback_model="a")) is None


def test_openrouter_uses_its_own_endpoint(monkeypatch):
    import llm_client

    seen = {}

    def fake_request(url, body, headers, timeout, key):
        seen.update(url=url, model=body["model"], auth=headers.get("Authorization"))
        return {"choices": [{"message": {"content": "ok"}}]}

    monkeypatch.setattr(llm_client, "_request_json", fake_request)
    config = llm_client.LLMConfig(provider="openrouter", api_key="sk-or", model="deepseek/deepseek-v4.1-flash", enabled=True)
    assert llm_client.complete(config, "p") == "ok"
    assert seen == {"url": "https://openrouter.ai/api/v1/chat/completions", "model": "deepseek/deepseek-v4.1-flash", "auth": "Bearer sk-or"}
