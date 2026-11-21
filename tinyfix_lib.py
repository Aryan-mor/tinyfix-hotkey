"""Load tinyfix and fix finished text. Mirrors cfahlgren1/tinyfix loadTinyfix / check / apply / fix."""

from __future__ import annotations

import math
import re
from dataclasses import dataclass
from pathlib import Path

from engine.fixer import (
    DEFAULT_WEIGHTS,
    WITH_MODEL_WEIGHTS,
    Correction,
    Fixer,
    FixerWeights,
    parse_word_list,
)
from engine.rules import match_case
from engine.trie import WordTrie
from engine.typo_cost import typo_cost
from engine.wrong_word import (
    CAREFUL_WRONG_WORD,
    CANDIDATES,
    EAGER_WRONG_WORD,
    MAX_NEIGHBORS,
    OnnxModel,
    UNKNOWN,
    WrongWordChecker,
    WrongWordSettings,
)

MODEL_REVISION = "8efdf5cd91b32584f4077aa0e8ecaf48f7651bef"
SPELLING_MIN_CONFIDENCE = 0.55
CONTEXT_BEFORE = 600
CONTEXT_AFTER = 200
DEFAULT_SENSITIVITY = "cautious"
SENSITIVITY_NAMES = ("cautious", "balanced", "aggressive")

WORD = re.compile(r"[A-Za-z']+")
FENCED_CODE = re.compile(r"```[\s\S]*?(?:```|$)", re.MULTILINE)
INLINE_CODE = re.compile(r"`[^`\n]*(?:`|$)", re.MULTILINE)
NOT_PROSE = re.compile(r"[@/\\_#\d]|\.\w")
NON_ENGLISH_LETTER = re.compile(r"[^\W\d_]", re.UNICODE)
ASCII_LETTER = re.compile(r"[A-Za-z]")


@dataclass(frozen=True)
class Fix:
    start: int
    end: int
    original: str
    replacement: str
    kind: str
    confidence: float

    @property
    def from_(self) -> int:
        return self.start


@dataclass(frozen=True)
class SensitivityPreset:
    name: str
    wrong_word: WrongWordSettings
    weights: FixerWeights
    known_word_swap: bool = False
    known_word_max_cost: float = 1.5
    known_word_min_margin: float = 6.0
    known_word_min_value: float = 5.0
    known_word_cost_weight: float = 1.5
    known_word_max_length: int = 5


# cautious = current production defaults (eager real-word checker + 0.55 spelling floor).
SENSITIVITY_PRESETS: dict[str, SensitivityPreset] = {
    "cautious": SensitivityPreset(
        name="cautious",
        wrong_word=EAGER_WRONG_WORD,
        weights=FixerWeights(
            frequency=WITH_MODEL_WEIGHTS.frequency,
            context=WITH_MODEL_WEIGHTS.context,
            keep_cost=WITH_MODEL_WEIGHTS.keep_cost,
            min_confidence=SPELLING_MIN_CONFIDENCE,
        ),
    ),
    "balanced": SensitivityPreset(
        name="balanced",
        wrong_word=WrongWordSettings(threshold=2.5, cost_weight=1.5),
        weights=FixerWeights(
            frequency=WITH_MODEL_WEIGHTS.frequency,
            context=WITH_MODEL_WEIGHTS.context,
            keep_cost=WITH_MODEL_WEIGHTS.keep_cost,
            min_confidence=0.45,
        ),
    ),
    "aggressive": SensitivityPreset(
        name="aggressive",
        wrong_word=WrongWordSettings(threshold=2.0, cost_weight=1.5),
        weights=FixerWeights(
            frequency=WITH_MODEL_WEIGHTS.frequency,
            context=WITH_MODEL_WEIGHTS.context,
            keep_cost=WITH_MODEL_WEIGHTS.keep_cost,
            min_confidence=0.40,
        ),
        known_word_swap=True,
        known_word_max_cost=1.5,
        known_word_min_margin=6.0,
        known_word_min_value=5.0,
        known_word_cost_weight=1.5,
        known_word_max_length=5,
    ),
}


def normalize_sensitivity(name: str | None) -> str:
    raw = (name or DEFAULT_SENSITIVITY).strip().lower()
    if raw in SENSITIVITY_PRESETS:
        return raw
    return DEFAULT_SENSITIVITY


def get_sensitivity_preset(name: str | None) -> SensitivityPreset:
    return SENSITIVITY_PRESETS[normalize_sensitivity(name)]


def _lines(text: str) -> list[str]:
    return [line for line in text.split("\n") if line]


def _code_ranges(text: str) -> list[tuple[int, int]]:
    ranges: list[tuple[int, int]] = []
    for pattern in (FENCED_CODE, INLINE_CODE):
        for match in pattern.finditer(text):
            ranges.append((match.start(), match.end()))
    return ranges


def _token_around(text: str, start: int, end: int) -> str:
    begin = start
    while begin > 0 and not text[begin - 1].isspace():
        begin -= 1
    finish = end
    while finish < len(text) and not text[finish].isspace():
        finish += 1
    return text[begin:finish]


def apply_fixes(text: str, fixes: list[Fix]) -> str:
    result = text
    for fix in sorted(fixes, key=lambda item: item.start, reverse=True):
        if text[fix.start : fix.end] != fix.original:
            raise ValueError(
                f"This fix expects “{fix.original}” at {fix.start}, "
                f"but the text has “{text[fix.start:fix.end]}”."
            )
        result = result[: fix.start] + fix.replacement + result[fix.end :]
    return result


def plan_clipboard_fix(engine: "Tinyfix", text: str | None) -> tuple[str, str | None]:
    """Decide how to fix clipboard text in place. Returns (notification, new_text_or_None)."""
    if text is None or text == "":
        return "Clipboard has no text", None
    fixes = engine.check(text)
    if not fixes:
        return "No typos found in clipboard", None
    corrected = apply_fixes(text, fixes)
    count = len(fixes)
    label = "change" if count == 1 else "changes"
    return f"Clipboard fixed: {count} {label}", corrected


class Tinyfix:
    def __init__(self, fixer: Fixer, wrong_word: WrongWordChecker | None, model: OnnxModel | None) -> None:
        self.fixer = fixer
        self.wrong_word = wrong_word
        self.model = model
        self.sensitivity = DEFAULT_SENSITIVITY
        self._preset = get_sensitivity_preset(DEFAULT_SENSITIVITY)

    def apply_sensitivity(self, name: str | None) -> str:
        """Update swap/confidence thresholds live without reloading the model."""
        preset = get_sensitivity_preset(name)
        self._preset = preset
        self.sensitivity = preset.name
        self.fixer.weights = preset.weights
        if self.wrong_word is not None:
            self.wrong_word.settings = preset.wrong_word
        return self.sensitivity

    def _known_word_swap(self, text_before: str, word: str, text_after: str) -> Correction | None:
        preset = self._preset
        if not preset.known_word_swap or self.wrong_word is None:
            return None
        if len(word) < 2 or len(word) > preset.known_word_max_length:
            return None
        if not self.fixer.is_known(word):
            return None
        # Only for words the context model cannot score as a typed token (e.g. "fro").
        if self.wrong_word.ids.get(word.lower()) is not None:
            return None
        lower = word.lower()
        options: list[tuple[str, float]] = []
        seen: set[str] = set()
        for match in self.fixer.trie.search(lower, 1):
            entry = self.fixer.words[match.index]
            if entry.word == lower or entry.word in seen:
                continue
            cost = typo_cost(lower, entry.word)
            if cost > preset.known_word_max_cost:
                continue
            seen.add(entry.word)
            options.append((entry.word, cost))
            if len(options) >= MAX_NEIGHBORS:
                break
        if not options:
            return None
        typed_id = UNKNOWN
        option_ids = [self.wrong_word.ids.get(option, UNKNOWN) for option, _cost in options]
        margins: list[float] = []
        for start in range(0, len(option_ids), MAX_NEIGHBORS):
            chunk = option_ids[start : start + MAX_NEIGHBORS]
            raw = self.wrong_word.run_model(text_before, text_after, [[typed_id, *chunk]])
            typed_score = raw[0]
            for index in range(len(chunk)):
                margins.append(raw[index + 1] - typed_score)
        best: tuple[float, str, float] | None = None
        for (option, cost), margin in zip(options, margins):
            value = margin - preset.known_word_cost_weight * cost
            if margin < preset.known_word_min_margin or value < preset.known_word_min_value:
                continue
            if best is None or value > best[0]:
                best = (value, option, margin)
        if best is None:
            return None
        _value, option, margin = best
        confidence = 1 / (1 + math.exp(-margin))
        return Correction(
            original=word,
            replacement=match_case(word, option),
            confidence=confidence,
            used_context=True,
        )

    def check(self, text: str, kinds: list[str] | None = None) -> list[Fix]:
        kinds = kinds or ["spelling", "context"]
        wants_spelling = "spelling" in kinds
        wants_context = "context" in kinds and self.wrong_word is not None
        code = _code_ranges(text)
        fixes: list[Fix] = []
        fixed_so_far = ""
        consumed = 0
        for match in WORD.finditer(text):
            original = match.group(0)
            start, end = match.start(), match.end()
            if any(start < code_end and end > code_start for code_start, code_end in code):
                continue
            if self.fixer.was_undone(original) or NOT_PROSE.search(_token_around(text, start, end)):
                continue
            before = (fixed_so_far + text[consumed:start])[-CONTEXT_BEFORE:]
            correction = self.fixer.correct(before, original) if wants_spelling else None
            if not correction and wants_context and self.wrong_word:
                line_end = text.find("\n", end)
                after_limit = len(text) if line_end == -1 else line_end
                after = text[end : min(after_limit, end + CONTEXT_AFTER)]
                correction = self.wrong_word.check(before, original, after)
                if not correction:
                    correction = self._known_word_swap(before, original, after)
            if not correction or correction.replacement == original:
                continue
            fixes.append(
                Fix(
                    start=start,
                    end=end,
                    original=original,
                    replacement=correction.replacement,
                    kind="context" if correction.used_context else "spelling",
                    confidence=max(0.0, min(1.0, correction.confidence)),
                )
            )
            fixed_so_far = (fixed_so_far + text[consumed:start])[-CONTEXT_BEFORE:] + correction.replacement
            consumed = end
        return fixes

    def fix(self, text: str) -> str:
        if not text:
            return text
        return apply_fixes(text, self.check(text))

    def ignore(self, word: str) -> None:
        self.fixer.remember_undo(word)


def load_tinyfix(model_dir: Path, mode: str = "balanced", sensitivity: str | None = None) -> Tinyfix:
    model_dir = Path(model_dir)
    words_text = (model_dir / "words.txt").read_text(encoding="utf-8")
    vocabulary = _lines((model_dir / "wrong-word-vocab.txt").read_text(encoding="utf-8"))
    confusions = _lines((model_dir / "wrong-word-confusions.txt").read_text(encoding="utf-8"))
    model = OnnxModel(str(model_dir / "wrong-word.onnx"))
    settings = CAREFUL_WRONG_WORD if mode == "careful" else EAGER_WRONG_WORD
    wrong_word = WrongWordChecker(model, vocabulary, confusions, settings)
    word_list = parse_word_list(words_text)
    trie = WordTrie([entry.word for entry in word_list])
    weights = FixerWeights(
        frequency=WITH_MODEL_WEIGHTS.frequency,
        context=WITH_MODEL_WEIGHTS.context,
        keep_cost=WITH_MODEL_WEIGHTS.keep_cost,
        min_confidence=SPELLING_MIN_CONFIDENCE,
    )
    fixer = Fixer(words=word_list, trie=trie, scorer=wrong_word, weights=weights)
    engine = Tinyfix(fixer, wrong_word, model)
    if sensitivity is not None:
        engine.apply_sensitivity(sensitivity)
    elif mode == "careful":
        engine.sensitivity = "cautious"
        engine._preset = get_sensitivity_preset("cautious")
        # Keep the stricter careful wrong-word settings already applied above.
        engine._preset = SensitivityPreset(
            name="cautious",
            wrong_word=CAREFUL_WRONG_WORD,
            weights=engine.fixer.weights,
        )
    else:
        engine.apply_sensitivity(DEFAULT_SENSITIVITY)
    return engine
