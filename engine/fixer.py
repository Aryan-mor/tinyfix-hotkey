from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Callable, Protocol

from .rules import apply_fixed_rule, is_british_spelling, is_checkable, is_newer_word, match_case
from .trie import WordTrie
from .typo_cost import typo_cost


class ContextScorer(Protocol):
    def score(self, text_before: str, words: list[str]) -> list[float]:
        ...


@dataclass(frozen=True)
class FixerWeights:
    frequency: float = 0.5
    context: float = 0.0
    keep_cost: float = 10.0
    min_confidence: float = 0.7


DEFAULT_WEIGHTS = FixerWeights()
WITH_MODEL_WEIGHTS = FixerWeights(frequency=0.5, context=0.25, keep_cost=10.0, min_confidence=0.7)


@dataclass(frozen=True)
class WordEntry:
    word: str
    count: int


@dataclass
class Candidate:
    word: str
    cost: float
    log_frequency: float
    context_gain: float = 0.0


@dataclass(frozen=True)
class Correction:
    original: str
    replacement: str
    confidence: float
    used_context: bool = False


def parse_word_list(text: str) -> list[WordEntry]:
    entries: list[WordEntry] = []
    for line in text.split("\n"):
        parts = line.strip().split()
        if len(parts) >= 2 and parts[0] and parts[1]:
            try:
                entries.append(WordEntry(parts[0], int(float(parts[1]))))
            except ValueError:
                continue
    return entries


def pick_candidate(candidates: list[Candidate], weights: FixerWeights) -> tuple[str, float] | None:
    if not candidates:
        return None
    scored = [
        (
            candidate.word,
            -candidate.cost + weights.frequency * candidate.log_frequency + weights.context * candidate.context_gain,
        )
        for candidate in candidates
    ]
    best_word, best_score = max(scored, key=lambda item: item[1])
    total = math.exp(-weights.keep_cost - best_score)
    for _, score in scored:
        total += math.exp(score - best_score)
    confidence = 1 / total
    if confidence >= weights.min_confidence:
        return best_word, confidence
    return None


class Fixer:
    def __init__(
        self,
        words: list[WordEntry],
        trie: WordTrie | None = None,
        scorer: ContextScorer | None = None,
        weights: FixerWeights | None = None,
        max_candidates: int = 12,
    ) -> None:
        self.words = words
        self.known = {entry.word for entry in words}
        self.trie = trie or WordTrie([entry.word for entry in words])
        self.scorer = scorer
        self.weights = weights or DEFAULT_WEIGHTS
        self.max_candidates = max_candidates
        self.total_count = sum(entry.count for entry in words)
        self.undone: set[str] = set()

    def learn(self, word: str) -> None:
        self.known.add(word.lower())

    def is_known(self, word: str) -> bool:
        lower = word.lower()
        return lower in self.known or is_newer_word(lower) or is_british_spelling(lower, lambda candidate: candidate in self.known)

    def analyze(self, text_before: str, word: str) -> list[Candidate] | None:
        if not is_checkable(word, text_before) or self.is_known(word):
            return None
        lower = word.lower()
        max_edits = 1 if len(lower) <= 4 else 2
        ranked: list[Candidate] = []
        for match in self.trie.search(lower, max_edits):
            entry = self.words[match.index]
            ranked.append(
                Candidate(
                    word=entry.word,
                    cost=typo_cost(lower, entry.word),
                    log_frequency=math.log(entry.count / self.total_count),
                    context_gain=0.0,
                )
            )
        ranked.sort(key=lambda item: item.log_frequency - item.cost, reverse=True)
        ranked = ranked[: self.max_candidates]
        if not self.scorer or not ranked:
            return ranked
        scores = self.scorer.score(text_before, [word, *[match_case(word, candidate.word) for candidate in ranked]])
        typed_score = scores[0]
        for index, candidate in enumerate(ranked):
            candidate.context_gain = scores[index + 1] - typed_score
        return ranked

    def correct(self, text_before: str, word: str) -> Correction | None:
        ruled = apply_fixed_rule(word)
        if ruled and ruled != word and not self.was_undone(word):
            return Correction(original=word, replacement=ruled, confidence=1)
        candidates = self.analyze(text_before, word)
        if not candidates:
            return None
        pick = pick_candidate(candidates, self.weights)
        if not pick:
            return None
        best, confidence = pick
        return Correction(original=word, replacement=match_case(word, best), confidence=confidence)

    def remember_undo(self, word: str) -> None:
        self.undone.add(word.lower())
        self.learn(word)

    def was_undone(self, word: str) -> bool:
        return word.lower() in self.undone
