from __future__ import annotations

import math
import re
from dataclasses import dataclass
from typing import TYPE_CHECKING

from .fixer import Correction
from .rules import is_checkable, is_sentence_start, match_case
from .trie import WordTrie
from .typo_cost import typo_cost

if TYPE_CHECKING:  # numpy/onnxruntime live in the optional offline pack, import them lazily
    import numpy as np

LEFT_WINDOW = 16
RIGHT_WINDOW = 4
MAX_NEIGHBORS = 8
CANDIDATES = MAX_NEIGHBORS + 1

PAD = 0
UNKNOWN = 1
NUMBER = 2
START = 3
NAME = 4
SPECIAL_TOKENS = 5

MAX_KNOWN_MIXUP_COST = 1.5
KNOWN_MIXUP_BONUS = 1
OTHER_WORD_PENALTY = 1
INFLECTION_PENALTY = 1
INFLECTION_ENDINGS = {"", "s", "es", "d", "ed", "ing", "r", "er", "ly"}
MIN_STEM = 3

# JS \\w is ASCII-only unless the /u flag is set. Keep the same behaviour so
# Persian and other non-ASCII letters become one unknown token each.
TOKEN = re.compile(r"[A-Za-z]+(?:'[A-Za-z]+)?|\d+(?:[.,]\d+)*|\n|[^\s\w]", re.ASCII)
SENTENCE_OPENERS = {".", "!", "?", ":", '"'}
WORD = re.compile(r"^[a-z]+(?:'[a-z]+)?$")
LISTED_WORD = re.compile(r"^[A-Za-z]+(?:'[A-Za-z]+)?$")


@dataclass(frozen=True)
class WrongWordSettings:
    threshold: float
    cost_weight: float


CAREFUL_WRONG_WORD = WrongWordSettings(threshold=5, cost_weight=0.5)
EAGER_WRONG_WORD = WrongWordSettings(threshold=3, cost_weight=1.5)


@dataclass
class NearbyWord:
    word: str
    margin: float
    cost: float
    is_known_mixup: bool
    value: float = 0.0


def _pad(ids: list[int], length: int) -> list[int]:
    if len(ids) >= length:
        return list(ids[-length:])
    return [PAD] * (length - len(ids)) + list(ids)


def differs_only_in_ending(first: str, second: str) -> bool:
    shared = 0
    limit = min(len(first), len(second))
    while shared < limit and first[shared] == second[shared]:
        shared += 1
    for stem in range(shared, MIN_STEM - 1, -1):
        if first[stem:] in INFLECTION_ENDINGS and second[stem:] in INFLECTION_ENDINGS:
            return True
    return False


def tokenize(text: str, starts_sentence: bool) -> list[str]:
    tokens: list[str] = []
    normalized = text.replace("\u2019", "'")
    for raw in TOKEN.findall(normalized):
        if raw == "\n":
            if tokens and tokens[-1] not in SENTENCE_OPENERS:
                tokens.append(".")
            continue
        previous = tokens[-1] if tokens else None
        at_sentence_start = starts_sentence if previous is None else previous in SENTENCE_OPENERS
        is_pronoun = raw == "I" or raw.startswith("I'")
        if raw[:1].isupper() and not at_sentence_start and not is_pronoun:
            tokens.append("<name>")
        else:
            tokens.append(raw.lower())
    return tokens


class OnnxModel:
    def __init__(self, path: str) -> None:
        import onnxruntime as ort

        so = ort.SessionOptions()
        so.intra_op_num_threads = 1
        so.inter_op_num_threads = 1
        self.session = ort.InferenceSession(path, so, providers=["CPUExecutionProvider"])

    def run(self, left: np.ndarray, right: np.ndarray, candidates: np.ndarray) -> np.ndarray:
        import numpy as np

        outputs = self.session.run(
            ["scores"],
            {
                "left": left.astype(np.int64),
                "right": right.astype(np.int64),
                "candidates": candidates.astype(np.int64),
            },
        )
        return outputs[0]


class WrongWordChecker:
    def __init__(
        self,
        model: OnnxModel,
        vocabulary: list[str],
        confusions: list[str] | None = None,
        settings: WrongWordSettings | None = None,
    ) -> None:
        self.model = model
        self.vocabulary = vocabulary
        self.settings = settings or CAREFUL_WRONG_WORD
        self.ids = {token: index for index, token in enumerate(vocabulary)}
        self.targets: list[int] = []
        for index, token in enumerate(vocabulary):
            if index >= SPECIAL_TOKENS and len(token) >= 2 and WORD.fullmatch(token):
                self.targets.append(index)
        self.trie = WordTrie([vocabulary[index] for index in self.targets])
        self.neighbors: dict[int, list[int]] = {}
        self.known_mixups: dict[int, list[int]] = {}
        for line in confusions or []:
            parts = line.split(" ")
            typed = self.ids.get(parts[0]) if parts else None
            if typed is None:
                continue
            meant = [self.ids[token] for token in parts[1:] if token in self.ids]
            self.known_mixups[typed] = meant

    def token_id(self, token: str) -> int:
        if token == "<name>":
            return NAME
        if token[:1].isdigit():
            return NUMBER
        return self.ids.get(token, UNKNOWN)

    def nearby_ids(self, id_: int) -> list[int]:
        cached = self.neighbors.get(id_)
        if cached is not None:
            return cached
        one_edit = [
            self.targets[match.index]
            for match in self.trie.search(self.vocabulary[id_], 1)
            if self.targets[match.index] != id_
        ]
        one_edit.sort()
        found: list[int] = []
        seen: set[int] = set()
        for item in [*(self.known_mixups.get(id_, [])), *one_edit]:
            if item not in seen:
                seen.add(item)
                found.append(item)
            if len(found) >= MAX_NEIGHBORS:
                break
        self.neighbors[id_] = found
        return found

    def run_model(self, text_before: str, text_after: str, rows: list[list[int]]) -> list[float]:
        before = [self.token_id(token) for token in tokenize(text_before, True)]
        if len(before) >= LEFT_WINDOW:
            left = before[-LEFT_WINDOW:]
        else:
            left = _pad([START, *before], LEFT_WINDOW)
        after_ids = [self.token_id(token) for token in tokenize(text_after, False)[:RIGHT_WINDOW]]
        after_ids.reverse()
        right = _pad(after_ids, RIGHT_WINDOW)
        import numpy as np

        batch = len(rows)
        left_arr = np.tile(np.array(left, dtype=np.int64), (batch, 1))
        right_arr = np.tile(np.array(right, dtype=np.int64), (batch, 1))
        cand_rows = []
        for row in rows:
            padded = list(row) + [PAD] * (CANDIDATES - len(row))
            cand_rows.append(padded[:CANDIDATES])
        candidates = np.array(cand_rows, dtype=np.int64)
        scores = self.model.run(left_arr, right_arr, candidates)
        return [float(value) for value in scores.reshape(-1)]

    def compare(
        self,
        text_before: str,
        word: str,
        text_after: str,
        only_known_mixups: bool = False,
    ) -> list[NearbyWord] | None:
        typed = self.ids.get(word.lower())
        if typed is None or typed < SPECIAL_TOKENS:
            return None
        nearby = self.known_mixups.get(typed, []) if only_known_mixups else self.nearby_ids(typed)
        if not nearby:
            return None
        scores = self.run_model(text_before, text_after, [[typed, *nearby]])
        mixups = self.known_mixups.get(typed, [])
        result: list[NearbyWord] = []
        lower = word.lower()
        for index, id_ in enumerate(nearby):
            nearby_word = self.vocabulary[id_]
            cost = typo_cost(lower, nearby_word)
            is_known_mixup = id_ in mixups
            result.append(
                NearbyWord(
                    word=nearby_word,
                    margin=scores[index + 1] - scores[0],
                    cost=min(cost, MAX_KNOWN_MIXUP_COST) if is_known_mixup else cost,
                    is_known_mixup=is_known_mixup,
                )
            )
        return result

    def score(self, text_before: str, words: list[str]) -> list[float]:
        ids = [UNKNOWN, *[self.ids.get(word.lower(), UNKNOWN) for word in words[1:]]]
        rows: list[list[int]] = []
        for start in range(0, len(ids), MAX_NEIGHBORS):
            rows.append([PAD, *ids[start : start + MAX_NEIGHBORS]])
        scores = self.run_model(text_before, "", rows)
        return [
            scores[(index // MAX_NEIGHBORS) * CANDIDATES + (index % MAX_NEIGHBORS) + 1]
            for index in range(len(ids))
        ]

    def has_known_mixups(self, word: str, text_before: str) -> bool:
        id_ = self.ids.get(word.lower())
        if id_ is None or id_ not in self.known_mixups or not LISTED_WORD.fullmatch(word):
            return False
        if len(word) > 1 and word == word.upper():
            return False
        is_capitalized = word[0] != word[0].lower()
        return (not is_capitalized) or is_sentence_start(text_before)

    def judged_against(self, word: str, text_before: str) -> str | None:
        if is_checkable(word, text_before):
            return "nearby"
        return "knownMixups" if self.has_known_mixups(word, text_before) else None

    def _value(self, item: NearbyWord, typed: str, typed_has_mixups: bool) -> float:
        value = item.margin - self.settings.cost_weight * item.cost
        if item.is_known_mixup:
            return value + KNOWN_MIXUP_BONUS
        penalty = OTHER_WORD_PENALTY if typed_has_mixups else 0
        if differs_only_in_ending(typed, item.word):
            penalty += INFLECTION_PENALTY
        return value - penalty

    def check(self, text_before: str, word: str, text_after: str) -> Correction | None:
        judged = self.judged_against(word, text_before)
        if not judged:
            return None
        nearby = self.compare(text_before, word, text_after, judged == "knownMixups")
        if not nearby:
            return None
        typed_has_mixups = any(item.is_known_mixup for item in nearby)
        ranked = []
        for item in nearby:
            item.value = self._value(item, word.lower(), typed_has_mixups)
            ranked.append(item)
        ranked.sort(key=lambda item: item.value, reverse=True)
        best = ranked[0]
        if best.value < self.settings.threshold:
            return None
        return Correction(
            original=word,
            replacement=match_case(word, best.word),
            confidence=1 / (1 + math.exp(-best.margin)),
            used_context=True,
        )
