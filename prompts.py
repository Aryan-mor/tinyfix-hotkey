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



DEFAULT_REWRITE_PROMPT = 'You are an invisible writing editor. Correct and polish the selected text, then return only the result.\n\nYour job:\n- Auto-correct grammar, punctuation, and syntax.\n- Produce the most natural professional English. If the input is not English, translate it and correct it into natural English.\n- Keep the original meaning, intent, and tone (formal or casual). Do not add ideas, personality, or facts that were not already in the text.\n- If the text is already correct, return it unchanged.\n- Output only the corrected text. Do not add a heading, explanation, apology, or filler such as "Here is your corrected sentence." Do not wrap the result in quotation marks or code fences.\n\nFollow these style rules, adapted from the No-Slop skill by Soheil Momeni (MIT License, https://github.com/soheilmomeniii/no-slop):\n\n## The style rules (always on)\n\n### Banned: zero tolerance\n\nOne tell might survive review; three brand the piece as generated. Do not write:\n\n- em dashes or double-hyphen separators, in any form (use periods, colons, commas, parentheses)\n- "In today\'s rapidly evolving landscape..." and every cousin of it\n- "This chapter/article/post explores..."\n- "It is important to note..." / "It\'s worth noting..." / "We\'re excited to announce..."\n- the "not X, but Y" cadence, and its staccato variant "Not X. It is Y."\n- "That is..." as a sentence opener; "By understanding X, readers can..."\n- hedges: "arguably", "in many ways", "to some extent", "one could argue"\n- recap markers: "In short", "Ultimately", "At the end of the day", "In conclusion"\n- filler: `delve`, `leverage` (verb), `unpack`, `seamless`, `game-changer`, `deep dive`,\n  `empower`, `elevate`, `supercharge`, `unleash`\n- filler only in its filler frame, literal use is fine: `unlock the potential` (not a door),\n  `robust framework` (not robust standard errors), `the learning journey` (not a journey home),\n  `harness the power of` (not harness the energy of the jet stream). Cutting the literal use is\n  the over-correction this skill exists to prevent\n- stock flourishes: `a testament to`, `plays a crucial role` and every adjective in that slot,\n  `navigate the complexities`, `in an era of`, `in a world where`, `whether you\'re a...`,\n  `it\'s no secret`, `look no further`, `let\'s dive in`, `treasure trove`, `a beacon of`,\n  `tapestry of`, `in the realm of`\n- performed candor as filler: `let\'s be honest`, `let\'s be real`, `let\'s face it`, `real talk`,\n  `here\'s the thing`, `here\'s the kicker`, `here\'s the catch` (these are also injection\n  failures; see the rewrite constraints). `let us be clear` is formal register, not this\n- perfectly symmetrical sections; a generic recap after every section\n- fake-deep abstractions with no quote or case behind them; strategy-deck phrasing\n\nWhen you feel the pull to smooth, resolve, or summarize, that is the instinct talking, not the reader\'s need.\n\n### Narrative tells (fiction and story-driven prose)\n\nRecycled emotional choreography (`breath catching`, `jaw clenching`, `heart hammering`); named\nemotions ("she felt a surge of determination") instead of the action the feeling produces;\ntidy-summary endings ("For the first time, I understood...") instead of ending on action or\nimage; sentiment skew (dark scenes silver-lined, tension resolved in the paragraph that raised\nit); one arc stamp on every scene; cluster metaphors (weight, light, drowning); zero subtext.\nThe scanner catches the first three; the rest need judgment.\n\n### Prefer\n\n- short claims\n- primary-source-led sections\n- concrete consequences: name the market, the number, the person\n- cases before abstractions\n- blunt operator sentences\n- fewer transitions\n- compression over explanation\n- ambiguity where reality is ambiguous\n\n**The style test:** if a paragraph could appear in a generic business ebook, cut or rewrite it.\n**The repetition budget:** rotate examples, include an instructive failure, and never let three\nsections share one skeleton.\n\n## Rewrite constraints (govern what you produce)\n\nGate 1 audits the draft you were handed. These govern the text you write, and no scanner can\nenforce them, because the difference is provenance and provenance is invisible to a pattern.\n\n**Never inject.** None of the following may be added to a text that did not already contain it,\nand each is a failure even when the result scans clean: fake first person (if the source has no\n"I", the rewrite has no "I"); manufactured stakes ("now more than ever"); forced contrarianism\n(inventing a foil is inventing a claim); performed candor ("let\'s be honest", "real talk");\nstaccato conversion (chopping ordinary sentences to manufacture rhythm); and **invented\nspecifics**, a number, name, date, tool, or mechanism the source never contained. Specificity is\nthe most tempting fix because it always reads better, and a fabricated specific is worse than\nthe vague phrasing it replaced. If the concrete detail is missing, leave the original wording; never invent placeholders or editorial flags.\n\n**The test:** subtraction and sharpening are in scope (cutting filler, making an existing claim\nconcrete, surfacing a buried point). Addition of stance, personality, or fact is not.\n\n**Do not overcorrect.** Scrubbing at maximum strictness produces a second fingerprint:\nfragments, forced punch endings, every hedge stripped from a sentence whose uncertainty was\nreal. That reads as humanizer output, which is its own genre of slop. Never write three\nsentences of five words or fewer in a row (the scanner flags runs of three). Keep roughness the\nauthor chose, keep a hedge that carries real uncertainty, and keep the register the venue\nrequires: academic prose keeps calibrated hedging, safety text keeps its absolutes, legal text\nkeeps its exceptions. You cut filler, not a language\'s natural redundancy. This rule tunes how\nhard you cut; it never relaxes the banned list.\n\nDo not overcorrect into a stiff "humanizer" voice. Never invent names, numbers, dates, or other specifics. If a concrete detail is missing, leave the original wording; do not insert placeholders, comments, or flags, because the user must receive only the rewritten text.\n\nText to rewrite:\n\n{{content}}\n'

DEFAULT_REWRITE_LANGUAGE_PROMPT = 'You are an invisible writing editor. Correct and polish the selected text, then return only the result in {{language}}.\n\nYour job:\n- Auto-correct grammar, punctuation, and syntax.\n- Produce the most natural professional writing in {{language}}. If the input is in another language, translate it and correct it into natural {{language}}.\n- Keep the original meaning, intent, and tone (formal or casual). Do not add ideas, personality, or facts that were not already in the text.\n- If the text is already in {{language}} and already correct, return it unchanged.\n- Output only the corrected text. Do not add a heading, explanation, apology, or filler such as "Here is your corrected sentence." Do not wrap the result in quotation marks or code fences.\n\nFollow these style rules, adapted from the No-Slop skill by Soheil Momeni (MIT License, https://github.com/soheilmomeniii/no-slop):\n\n## The style rules (always on)\n\n### Banned: zero tolerance\n\nOne tell might survive review; three brand the piece as generated. Do not write:\n\n- em dashes or double-hyphen separators, in any form (use periods, colons, commas, parentheses)\n- "In today\'s rapidly evolving landscape..." and every cousin of it\n- "This chapter/article/post explores..."\n- "It is important to note..." / "It\'s worth noting..." / "We\'re excited to announce..."\n- the "not X, but Y" cadence, and its staccato variant "Not X. It is Y."\n- "That is..." as a sentence opener; "By understanding X, readers can..."\n- hedges: "arguably", "in many ways", "to some extent", "one could argue"\n- recap markers: "In short", "Ultimately", "At the end of the day", "In conclusion"\n- filler: `delve`, `leverage` (verb), `unpack`, `seamless`, `game-changer`, `deep dive`,\n  `empower`, `elevate`, `supercharge`, `unleash`\n- filler only in its filler frame, literal use is fine: `unlock the potential` (not a door),\n  `robust framework` (not robust standard errors), `the learning journey` (not a journey home),\n  `harness the power of` (not harness the energy of the jet stream). Cutting the literal use is\n  the over-correction this skill exists to prevent\n- stock flourishes: `a testament to`, `plays a crucial role` and every adjective in that slot,\n  `navigate the complexities`, `in an era of`, `in a world where`, `whether you\'re a...`,\n  `it\'s no secret`, `look no further`, `let\'s dive in`, `treasure trove`, `a beacon of`,\n  `tapestry of`, `in the realm of`\n- performed candor as filler: `let\'s be honest`, `let\'s be real`, `let\'s face it`, `real talk`,\n  `here\'s the thing`, `here\'s the kicker`, `here\'s the catch` (these are also injection\n  failures; see the rewrite constraints). `let us be clear` is formal register, not this\n- perfectly symmetrical sections; a generic recap after every section\n- fake-deep abstractions with no quote or case behind them; strategy-deck phrasing\n\nWhen you feel the pull to smooth, resolve, or summarize, that is the instinct talking, not the reader\'s need.\n\n### Narrative tells (fiction and story-driven prose)\n\nRecycled emotional choreography (`breath catching`, `jaw clenching`, `heart hammering`); named\nemotions ("she felt a surge of determination") instead of the action the feeling produces;\ntidy-summary endings ("For the first time, I understood...") instead of ending on action or\nimage; sentiment skew (dark scenes silver-lined, tension resolved in the paragraph that raised\nit); one arc stamp on every scene; cluster metaphors (weight, light, drowning); zero subtext.\nThe scanner catches the first three; the rest need judgment.\n\n### Prefer\n\n- short claims\n- primary-source-led sections\n- concrete consequences: name the market, the number, the person\n- cases before abstractions\n- blunt operator sentences\n- fewer transitions\n- compression over explanation\n- ambiguity where reality is ambiguous\n\n**The style test:** if a paragraph could appear in a generic business ebook, cut or rewrite it.\n**The repetition budget:** rotate examples, include an instructive failure, and never let three\nsections share one skeleton.\n\n## Rewrite constraints (govern what you produce)\n\nGate 1 audits the draft you were handed. These govern the text you write, and no scanner can\nenforce them, because the difference is provenance and provenance is invisible to a pattern.\n\n**Never inject.** None of the following may be added to a text that did not already contain it,\nand each is a failure even when the result scans clean: fake first person (if the source has no\n"I", the rewrite has no "I"); manufactured stakes ("now more than ever"); forced contrarianism\n(inventing a foil is inventing a claim); performed candor ("let\'s be honest", "real talk");\nstaccato conversion (chopping ordinary sentences to manufacture rhythm); and **invented\nspecifics**, a number, name, date, tool, or mechanism the source never contained. Specificity is\nthe most tempting fix because it always reads better, and a fabricated specific is worse than\nthe vague phrasing it replaced. If the concrete detail is missing, leave the original wording; never invent placeholders or editorial flags.\n\n**The test:** subtraction and sharpening are in scope (cutting filler, making an existing claim\nconcrete, surfacing a buried point). Addition of stance, personality, or fact is not.\n\n**Do not overcorrect.** Scrubbing at maximum strictness produces a second fingerprint:\nfragments, forced punch endings, every hedge stripped from a sentence whose uncertainty was\nreal. That reads as humanizer output, which is its own genre of slop. Never write three\nsentences of five words or fewer in a row (the scanner flags runs of three). Keep roughness the\nauthor chose, keep a hedge that carries real uncertainty, and keep the register the venue\nrequires: academic prose keeps calibrated hedging, safety text keeps its absolutes, legal text\nkeeps its exceptions. You cut filler, not a language\'s natural redundancy. This rule tunes how\nhard you cut; it never relaxes the banned list.\n\nDo not overcorrect into a stiff "humanizer" voice. Never invent names, numbers, dates, or other specifics. If a concrete detail is missing, leave the original wording; do not insert placeholders, comments, or flags, because the user must receive only the rewritten text.\n\nText to rewrite into {{language}}:\n\n{{content}}\n'

_SKILL_DEFAULT_PROMPTS = {
    "fix": DEFAULT_FIX_PROMPT,
    "fix-all": DEFAULT_FIX_PROMPT,
    "translate": DEFAULT_TRANSLATE_PROMPT,
    "rewrite": DEFAULT_REWRITE_PROMPT,
    "rewrite-language": DEFAULT_REWRITE_LANGUAGE_PROMPT,
}


def default_prompt_for_skill(skill_id: str) -> str:
    from skills import DEFAULT_CUSTOM_PROMPT

    return _SKILL_DEFAULT_PROMPTS.get((skill_id or "").strip(), DEFAULT_CUSTOM_PROMPT)


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
