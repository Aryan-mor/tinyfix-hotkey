"""Call Google Gemini or an OpenAI-compatible chat API, with a short timeout."""

from __future__ import annotations

import json
import logging
import socket
import urllib.error
import urllib.request
from dataclasses import dataclass, field

from language_guess import looks_english
from prompts import (
    DEFAULT_FIX_PROMPT,
    DEFAULT_TARGET_LANGUAGE,
    DEFAULT_TRANSLATE_PROMPT,
    render_prompt,
    strip_model_output,
)

PROVIDER_GOOGLE = "google"
PROVIDER_OPENAI = "openai_compatible"
PROVIDERS = (PROVIDER_GOOGLE, PROVIDER_OPENAI)
DEFAULT_TIMEOUT = 6.0
DEFAULT_GOOGLE_MODEL = "gemini-3.5-flash-lite"
DEFAULT_OPENAI_MODEL = "gpt-4o-mini"

def resolve_google_model(value: str | None) -> str:
    """Keep a non-empty chosen model; only empty/unset values get the default."""
    raw = (value or "").strip()
    return raw or DEFAULT_GOOGLE_MODEL

GEMINI_BASE = "https://generativelanguage.googleapis.com/v1beta"
USER_AGENT = "TinyFix-Hotkey-LLM"
TEST_SAMPLE = "Hi frinds"

logger = logging.getLogger(__name__)


class LLMError(Exception):
    """A provider or network failure. The message must never include secrets."""


class LLMTimeout(LLMError):
    """The request exceeded the configured timeout."""


def sanitize_error(message: str, secret: str = "") -> str:
    text = str(message)
    if secret:
        text = text.replace(secret, "***")
    # Belt-and-braces for key-like query params if a URL leaked into the error.
    lowered = text
    for marker in ("key=", "api_key=", "Bearer "):
        idx = lowered.lower().find(marker.lower())
        while idx != -1:
            end = idx + len(marker)
            while end < len(text) and text[end] not in " &\"'<>\n":
                end += 1
            text = text[: idx + len(marker)] + "***" + text[end:]
            lowered = text
            idx = lowered.lower().find(marker.lower(), idx + len(marker) + 3)
    return text


def _assert_safe_base_url(url: str) -> None:
    from urllib.parse import urlparse

    parsed = urlparse(url)
    scheme = (parsed.scheme or "").lower()
    host = (parsed.hostname or "").lower()
    if scheme == "http" and host not in {"127.0.0.1", "localhost", "::1"}:
        raise LLMError("HTTP base URLs are only allowed for localhost. Use HTTPS for a remote provider.")
    if scheme not in {"http", "https"}:
        raise LLMError("The base URL must start with https:// (or http:// for localhost).")


def _normalize_openai_base(url: str) -> str:
    base = (url or "").strip().rstrip("/")
    if not base:
        return "https://api.openai.com/v1"
    _assert_safe_base_url(base)
    if base.endswith("/chat/completions"):
        base = base[: -len("/chat/completions")]
    if not base.endswith("/v1"):
        # Ollama/LM Studio sometimes expose /v1 already; if the user omitted it, add it.
        if "/v1/" not in base + "/":
            base = base + "/v1"
    return base


@dataclass
class LLMConfig:
    provider: str = PROVIDER_GOOGLE
    api_key: str = ""
    model: str = DEFAULT_GOOGLE_MODEL
    base_url: str = ""
    timeout: float = DEFAULT_TIMEOUT
    enabled: bool = False
    fix_prompt: str = field(default_factory=lambda: DEFAULT_FIX_PROMPT)
    translate_prompt: str = field(default_factory=lambda: DEFAULT_TRANSLATE_PROMPT)
    target_language: str = DEFAULT_TARGET_LANGUAGE

    @property
    def ready(self) -> bool:
        if not self.enabled or not (self.api_key or "").strip() or not (self.model or "").strip():
            return False
        if self.provider == PROVIDER_OPENAI:
            return True
        return self.provider == PROVIDER_GOOGLE


def _request_json(url: str, payload: dict | None, headers: dict[str, str], timeout: float, api_key: str) -> dict:
    body = None if payload is None else json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(url, data=body, headers=headers, method="GET" if payload is None else "POST")
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            raw = response.read().decode("utf-8")
    except socket.timeout as exc:
        raise LLMTimeout(sanitize_error(f"Timed out after {timeout}s", api_key)) from exc
    except TimeoutError as exc:
        raise LLMTimeout(sanitize_error(f"Timed out after {timeout}s", api_key)) from exc
    except urllib.error.HTTPError as exc:
        detail = ""
        try:
            detail = exc.read().decode("utf-8", errors="replace")[:300]
        except Exception:
            detail = exc.reason or ""
        raise LLMError(sanitize_error(f"HTTP {exc.code}: {detail or exc.reason}", api_key)) from exc
    except urllib.error.URLError as exc:
        reason = exc.reason
        if isinstance(reason, (socket.timeout, TimeoutError)) or "timed out" in str(reason).lower():
            raise LLMTimeout(sanitize_error(f"Timed out after {timeout}s", api_key)) from exc
        raise LLMError(sanitize_error(f"Network error: {reason}", api_key)) from exc
    try:
        parsed = json.loads(raw) if raw else {}
    except json.JSONDecodeError as exc:
        raise LLMError("Provider returned a non-JSON response") from exc
    if not isinstance(parsed, dict):
        raise LLMError("Provider returned an unexpected payload")
    return parsed


def _gemini_headers(api_key: str) -> dict[str, str]:
    # Prefer the header so the key is less likely to appear in exception URLs.
    return {
        "Content-Type": "application/json",
        "User-Agent": USER_AGENT,
        "x-goog-api-key": api_key,
    }


def list_models(config: LLMConfig, timeout: float | None = None) -> list[str]:
    timeout = float(timeout or max(config.timeout, 5))
    if config.provider == PROVIDER_GOOGLE:
        url = f"{GEMINI_BASE}/models"
        payload = _request_json(url, None, _gemini_headers(config.api_key), timeout, config.api_key)
        names: list[str] = []
        for item in payload.get("models") or []:
            methods = item.get("supportedGenerationMethods") or []
            if methods and "generateContent" not in methods:
                continue
            name = str(item.get("name") or "").strip()
            if name.startswith("models/"):
                name = name[len("models/") :]
            if name:
                names.append(name)
        return names
    if config.provider == PROVIDER_OPENAI:
        base = _normalize_openai_base(config.base_url)
        url = f"{base}/models"
        headers = {
            "Authorization": f"Bearer {config.api_key}",
            "User-Agent": USER_AGENT,
        }
        # Some local servers do not require a key.
        if not config.api_key:
            headers.pop("Authorization", None)
        payload = _request_json(url, None, headers, timeout, config.api_key)
        names = []
        for item in payload.get("data") or []:
            name = str(item.get("id") or "").strip()
            if name:
                names.append(name)
        return names
    raise LLMError(f"Unknown provider: {config.provider}")


def _complete_gemini(config: LLMConfig, prompt: str, timeout: float) -> str:
    model = (config.model or DEFAULT_GOOGLE_MODEL).strip()
    if model.startswith("models/"):
        model = model[len("models/") :]
    url = f"{GEMINI_BASE}/models/{model}:generateContent"
    body = {
        "contents": [{"parts": [{"text": prompt}]}],
        "generationConfig": {"temperature": 0.1},
    }
    payload = _request_json(url, body, _gemini_headers(config.api_key), timeout, config.api_key)
    candidates = payload.get("candidates") or []
    if not candidates:
        raise LLMError("Gemini returned no candidates")
    parts = ((candidates[0] or {}).get("content") or {}).get("parts") or []
    text = "".join(str(part.get("text") or "") for part in parts)
    if not text.strip():
        raise LLMError("Gemini returned empty text")
    return strip_model_output(text)


def _complete_openai(config: LLMConfig, prompt: str, timeout: float) -> str:
    base = _normalize_openai_base(config.base_url)
    url = f"{base}/chat/completions"
    headers = {
        "Content-Type": "application/json",
        "Authorization": f"Bearer {config.api_key}",
        "User-Agent": USER_AGENT,
    }
    if not config.api_key:
        headers.pop("Authorization", None)
    body = {
        "model": config.model or DEFAULT_OPENAI_MODEL,
        "temperature": 0.1,
        "messages": [{"role": "user", "content": prompt}],
    }
    payload = _request_json(url, body, headers, timeout, config.api_key)
    choices = payload.get("choices") or []
    if not choices:
        raise LLMError("The model returned no choices")
    message = (choices[0] or {}).get("message") or {}
    text = message.get("content") or ""
    if isinstance(text, list):
        text = "".join(str(part.get("text") or part) for part in text)
    if not str(text).strip():
        raise LLMError("The model returned empty text")
    return strip_model_output(str(text))


def complete(config: LLMConfig, prompt: str, timeout: float | None = None) -> str:
    timeout = float(config.timeout if timeout is None else timeout)
    if timeout <= 0:
        timeout = DEFAULT_TIMEOUT
    if not (config.api_key or "").strip() and config.provider == PROVIDER_GOOGLE:
        raise LLMError("An API key is required")
    if not (config.model or "").strip():
        raise LLMError("A model name is required")
    if config.provider == PROVIDER_GOOGLE:
        return _complete_gemini(config, prompt, timeout)
    if config.provider == PROVIDER_OPENAI:
        return _complete_openai(config, prompt, timeout)
    raise LLMError(f"Unknown provider: {config.provider}")


def probe_connection(config: LLMConfig, timeout: float | None = None) -> str:
    """Send a tiny typo-correction request. Returns the (stripped) model text on success."""
    prompt = render_prompt(DEFAULT_FIX_PROMPT, TEST_SAMPLE)
    # Give the test a little more room than the tight runtime default, unless the user set more.
    wait = float(timeout if timeout is not None else max(config.timeout, 8))
    return complete(config, prompt, timeout=wait)


# Without the offline pack the fallback is only the basic dictionary engine, which is much weaker than the online
# model, so the online model gets more time before TinyFix gives up on it.
NO_LOCAL_MODEL_TIMEOUT = 10.0
# Text that is not English has no offline fallback at all (the offline engine only knows English), so the online
# model gets the longest wait. Measured: normal answers take 1-2 s, but slow moments take 10-18 s.
NO_FALLBACK_TIMEOUT = 20.0


def fix_timeout(config: LLMConfig, engine, text: str | None = None) -> float:
    """Seconds to wait for the online Fix.

    The user's setting when a local model can back it up, at least 10 s without the offline pack, and at least
    20 s for text that is not English (nothing offline can fix it)."""
    base = float(config.timeout or DEFAULT_TIMEOUT)
    if text is not None and not looks_english(text):
        return max(base, NO_FALLBACK_TIMEOUT)
    if engine is not None and getattr(engine, "has_model", False):
        return base
    return max(base, NO_LOCAL_MODEL_TIMEOUT)


def correct_online(config: LLMConfig, text: str, timeout: float | None = None) -> str:
    prompt = render_prompt(config.fix_prompt or DEFAULT_FIX_PROMPT, text)
    return complete(config, prompt, timeout=config.timeout if timeout is None else timeout)


def translate_online(config: LLMConfig, text: str) -> str:
    language = config.target_language or DEFAULT_TARGET_LANGUAGE
    prompt = render_prompt(config.translate_prompt or DEFAULT_TRANSLATE_PROMPT, text, language=language)
    return complete(config, prompt, timeout=config.timeout)


def correct_with_fallback(text: str, engine, config: LLMConfig | None) -> tuple[str, str]:
    """Return (corrected_text, source) where source is 'online', 'offline', 'fallback', or 'unsupported'.

    'unsupported' means: the text is not English, no online model is available, and the English-only offline
    engine must not touch it. For non-English text a failed online request raises instead of falling back, so the
    caller can say "timed out" rather than a misleading "no typos found"."""
    english = looks_english(text)
    if config is not None and config.ready:
        try:
            result = correct_online(config, text, timeout=fix_timeout(config, engine, text))
            if result:
                return result, "online"
            if not english:
                raise LLMError("The model returned empty text")
            logger.info("Online correction returned empty text; falling back to the offline engine")
        except LLMTimeout as exc:
            if not english:
                raise
            logger.info("Online correction timed out; falling back to the offline engine: %s", exc)
        except LLMError as exc:
            if not english:
                raise
            logger.info("Online correction failed; falling back to the offline engine: %s", exc)
        except Exception as exc:
            if not english:
                raise LLMError(f"Online correction failed ({exc.__class__.__name__})") from exc
            logger.info("Online correction failed; falling back to the offline engine", exc_info=True)
        if engine is None:
            return text, "fallback"
        return engine.fix(text), "fallback"
    if not english:
        return text, "unsupported"
    if engine is None:
        return text, "offline"
    return engine.fix(text), "offline"


# Skills (Translate, Rewrite, Improve prompt, custom) generate longer text and the user is already waiting on a
# "Running…" toast, so they get more time than the 2 s Fix path. Measured: normal answers take 0.5-1.5 s but some
# take 3-10 s.
SKILL_MIN_TIMEOUT = 10.0


def run_skill_online(config: LLMConfig, template: str, text: str, language: str | None = None) -> str:
    """Run a skill prompt online. No offline fallback."""
    if config is None or not config.ready:
        raise LLMError("Turn on an online model in Settings to use this skill")
    prompt = render_prompt(template or "", text, language=language)
    result = complete(config, prompt, timeout=max(float(config.timeout or 0), SKILL_MIN_TIMEOUT))
    cleaned = strip_model_output(result)
    if not cleaned:
        raise LLMError("The model returned empty text")
    return cleaned
