from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class TrieMatch:
    index: int
    distance: int


class WordTrie:
    """Compact trie that finds dictionary words within a small edit distance."""

    def __init__(self, words: list[str] | tuple[str, ...] | None = None) -> None:
        self.character = [0]
        self.first_child = [-1]
        self.next_sibling = [-1]
        self.word_index = [-1]
        if words:
            for index, word in enumerate(words):
                self._insert(word, index)

    def _insert(self, word: str, index: int) -> None:
        node = 0
        for position in range(len(word)):
            node = self._find_or_create_child(node, ord(word[position]))
        self.word_index[node] = index

    def _find_or_create_child(self, parent: int, code: int) -> int:
        child = self.first_child[parent]
        while child != -1:
            if self.character[child] == code:
                return child
            child = self.next_sibling[child]
        created = len(self.character)
        self.character.append(code)
        self.first_child.append(-1)
        self.next_sibling.append(self.first_child[parent])
        self.word_index.append(-1)
        self.first_child[parent] = created
        return created

    def search(self, query: str, max_edits: int) -> list[TrieMatch]:
        """Every word within max_edits insertions, deletions, substitutions or adjacent swaps."""
        width = len(query) + 1
        matches: list[TrieMatch] = []
        typed = [ord(ch) for ch in query]

        def walk(node: int, previous_code: int, row: list[int], row_before: list[int] | None) -> None:
            child = self.first_child[node]
            while child != -1:
                code = self.character[child]
                nxt = [0] * width
                nxt[0] = row[0] + 1
                smallest = nxt[0]
                for column in range(1, width):
                    substitution = row[column - 1] + (0 if typed[column - 1] == code else 1)
                    value = min(row[column] + 1, nxt[column - 1] + 1, substitution)
                    is_swap = (
                        row_before is not None
                        and column > 1
                        and typed[column - 1] == previous_code
                        and typed[column - 2] == code
                    )
                    if is_swap:
                        value = min(value, row_before[column - 2] + 1)
                    nxt[column] = value
                    smallest = min(smallest, value)
                distance = nxt[width - 1]
                if self.word_index[child] != -1 and distance <= max_edits:
                    matches.append(TrieMatch(self.word_index[child], distance))
                if smallest <= max_edits:
                    walk(child, code, nxt, row)
                child = self.next_sibling[child]

        walk(0, -1, list(range(width)), None)
        return matches
