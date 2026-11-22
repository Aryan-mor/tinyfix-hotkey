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
        return "Hi friends, I hope you are well." if "frinds" in prompt else "Did you go to the shop yesterday"

    ranked = find_best_models(LLMConfig(api_key="k"), complete_fn=fake)
    assert [m for m, _ in ranked] == ["gemini-fast-flash", "gemini-slow-flash"]


def test_find_best_fails_clearly_when_nothing_passes(monkeypatch):
    monkeypatch.setattr(llm_client, "list_models", lambda c, timeout=0: ["gemini-x-flash"])
    with pytest.raises(LLMError):
        find_best_models(LLMConfig(api_key="k"), complete_fn=lambda c, p, t: "wrong")
