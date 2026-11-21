"""Default prompts and helpers for online LLM correction and translation."""

from __future__ import annotations

import re

CONTENT_TOKEN = "{{content}}"
LANGUAGE_TOKEN = "{{language}}"

DEFAULT_FIX_PROMPT = """You are a precise copy editor. Correct spelling, grammar, and typographical errors in the text below, and nothing else.

Keep the original meaning, tone, voice, formatting, punctuation style, line breaks, spacing, and language. Do not translate, summarize, rewrite for style, or add or remove content.

Do not add a heading, explanation, apology, or commentary. Do not wrap the result in quotation marks or code fences. Return only the corrected text.

{{content}}
"""

DEFAULT_TRANSLATE_PROMPT = """Translate the text below into {{language}}.

Keep the original meaning, tone, voice, formatting, and line breaks. Do not add a heading, explanation, or commentary. Do not wrap the result in quotation marks or code fences. Return only the translation.

{{content}}
"""

DEFAULT_TARGET_LANGUAGE = "English"

_FENCE_RE = re.compile(r"^```[^\n]*\n?([\s\S]*?)\n?```$", re.MULTILINE)
_WRAPPED_QUOTE_RE = re.compile(r'^([\'"])([\s\S]*)\1$')


def render_prompt(template: str, content: str, language: str | None = None) -> str:
    """Replace {{content}} and {{language}}. If {{content}} is missing, append the text."""
    text = template if template is not None else ""
    if language is not None:
        text = text.replace(LANGUAGE_TOKEN, language)
    if CONTENT_TOKEN in text:
        text = text.replace(CONTENT_TOKEN, content)
    else:
        if text and not text.endswith("\n"):
            text += "\n"
        if text:
            text += "\n"
        text += content
    return text


def strip_model_output(text: str | None) -> str:
    """Remove a wrapping code fence or a single layer of quotes a model may add."""
    if text is None:
        return ""
    cleaned = text.strip()
    if not cleaned:
        return ""
    fence = re.match(r"^```[^\n]*\n([\s\S]*?)\n```$", cleaned)
    if fence:
        cleaned = fence.group(1).strip()
    elif cleaned.startswith("```") and cleaned.endswith("```") and cleaned.count("```") >= 2:
        inner = cleaned[3:]
        newline = inner.find("\n")
        if newline != -1:
            inner = inner[newline + 1 :]
        if inner.endswith("```"):
            inner = inner[:-3]
        cleaned = inner.strip()
    if len(cleaned) >= 2 and cleaned[0] == cleaned[-1] and cleaned[0] in {'"', "'"}:
        # Only unwrap when the text is a single quoted block, not dialogue with many quotes.
        body = cleaned[1:-1]
        if cleaned[0] not in body:
            cleaned = body.strip()
    return cleaned
