import time

import pytest

import llm_client
from llm_client import LLMConfig, LLMError, benchmark_candidates, find_best_models, rank_models


def test_candidates_keep_fast_text_models_only():
    names = ["gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-flash-image", "gemini-2.5-pro",
             "gemini-live-flash", "text-embedding-004", "gemini-3.5-flash-lite"]
    picked = benchmark_candidates(names)
    assert picked[:2] == ["gemini-2.5-flash-lite", "gemini-3.5-flash-lite"]
    assert "gemini-2.5-flash-image" not in picked
    assert "gemini-2.5-pro" not in picked
    assert "gemini-live-flash" not in picked


def test_rank_drops_failures_and_sorts_fastest_first():
    ranked = rank_models({"a": (True, 2.0), "b": (False, 0.1), "c": (True, 0.5)})
    assert ranked == [("c", 0.5), ("a", 2.0)]


def test_find_best_picks_fastest_correct_models(monkeypatch):
    monkeypatch.setattr(llm_client, "list_models", lambda c, timeout=0: ["gemini-fast-flash", "gemini-slow-flash", "gemini-bad-flash"])

    def fake(config, prompt, timeout):
        if "bad" in config.model:
            return "nothing fixed"
        time.sleep(0.01 if "fast" in config.model else 0.1)
        if "خاترخواه" in prompt:
            return "ببین آقا بهزاد چقدر خاطرخواه داره"
        return "Hi friends, I hope you are well." if "frinds" in prompt else "Did you go to the shop yesterday"

    ranked = find_best_models(LLMConfig(api_key="k"), complete_fn=fake)
    assert [m for m, _ in ranked] == ["gemini-fast-flash", "gemini-slow-flash"]


def test_find_best_fails_clearly_when_nothing_passes(monkeypatch):
    monkeypatch.setattr(llm_client, "list_models", lambda c, timeout=0: ["gemini-x-flash"])
    with pytest.raises(LLMError):
        find_best_models(LLMConfig(api_key="k"), complete_fn=lambda c, p, t: "wrong")


def test_openrouter_candidates_pick_cheap_new_text_models_across_vendors():
    def item(model_id, price, created, out=("text",)):
        return {"id": model_id, "created": created, "pricing": {"prompt": str(price / 1e6)}, "architecture": {"output_modalities": list(out)}}

    catalog = [
        item("google/gemini-new-flash", 0.2, 30), item("google/gemini-old-flash", 0.2, 10), item("google/gemini-older-flash", 0.2, 5),
        item("google/gemini-new-pro", 0.2, 40),  # pro variants are skipped
        item("google/gemini-new-flash:batch", 0.1, 50),  # batch variants are skipped
        item("openai/gpt-cheap", 0.1, 20), item("openai/gpt-pricey", 5.0, 60),  # over the price cap
        item("openai/gpt-free", 0.0, 70),
        item("deepseek/deepseek-chat", 0.05, 20), item("deepseek/image-flash", 0.05, 99, out=("image",)),
        item("someone/else", 0.1, 90),
    ]
    picked = llm_client.openrouter_candidates(catalog)
    assert picked == ["google/gemini-new-flash", "google/gemini-old-flash", "openai/gpt-cheap", "deepseek/deepseek-chat"]


def test_find_best_works_with_openrouter(monkeypatch):
    monkeypatch.setattr(llm_client, "openrouter_catalog", lambda timeout=0: [
        {"id": "google/a-flash", "created": 2, "pricing": {"prompt": "0.0000001"}, "architecture": {"output_modalities": ["text"]}},
        {"id": "openai/b-mini", "created": 1, "pricing": {"prompt": "0.0000001"}, "architecture": {"output_modalities": ["text"]}},
    ])

    def fake(config, prompt, timeout):
        if "b-mini" in config.model:
            return "wrong"
        if "خاترخواه" in prompt:
            return "ببین آقا بهزاد چقدر خاطرخواه داره"
        return "Hi friends, I hope you are well." if "frinds" in prompt else "Did you go to the shop yesterday"

    ranked = find_best_models(LLMConfig(provider="openrouter", api_key="k"), complete_fn=fake)
    assert [m for m, _ in ranked] == ["google/a-flash"]


def test_moving_latest_aliases_are_not_benchmarked():
    assert benchmark_candidates(["gemini-flash-lite-latest", "gemini-flash-latest", "gemini-3.5-flash-lite"]) == ["gemini-3.5-flash-lite"]
