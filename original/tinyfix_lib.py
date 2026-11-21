"""Load tinyfix and fix finished text. Mirrors cfahlgren1/tinyfix loadTinyfix / check / apply / fix."""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

from engine.fixer import DEFAULT_WEIGHTS, WITH_MODEL_WEIGHTS, Fixer, FixerWeights, parse_word_list
from engine.trie import WordTrie
from engine.wrong_word import CAREFUL_WRONG_WORD, EAGER_WRONG_WORD, OnnxModel, WrongWordChecker

MODEL_REVISION = "8efdf5cd91b32584f4077aa0e8ecaf48f7651bef"
SPELLING_MIN_CONFIDENCE = 0.55
CONTEXT_BEFORE = 600
CONTEXT_AFTER = 200

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


class Tinyfix:
    def __init__(self, fixer: Fixer, wrong_word: WrongWordChecker | None, model: OnnxModel | None) -> None:
        self.fixer = fixer
        self.wrong_word = wrong_word
        self.model = model

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


def load_tinyfix(model_dir: Path, mode: str = "balanced") -> Tinyfix:
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
    return Tinyfix(fixer, wrong_word, model)
