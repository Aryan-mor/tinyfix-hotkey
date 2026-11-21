from __future__ import annotations

KEYBOARD_ROWS = ["qwertyuiop", "asdfghjkl", "zxcvbnm"]
VOWELS = set("aeiou")

COST_SWAP = 1.2
COST_DOUBLED_KEY = 1.2
COST_ADJACENT_KEY = 1.6
COST_VOWEL_FOR_VOWEL = 2
COST_EXTRA_NEARBY_KEY = 1.8
COST_MISSED_KEY = 2.4
COST_EXTRA_KEY = 3
COST_WRONG_KEY = 3.4
COST_FIRST_LETTER_PENALTY = 1.5


def _build_neighbors() -> dict[str, set[str]]:
    neighbors: dict[str, set[str]] = {}
    for row_index, row in enumerate(KEYBOARD_ROWS):
        for key_index, key in enumerate(row):
            near: set[str] = set()
            for other_row, shift in (
                (row_index, -1),
                (row_index, 1),
                (row_index - 1, 0),
                (row_index - 1, 1),
                (row_index + 1, -1),
                (row_index + 1, 0),
            ):
                if 0 <= other_row < len(KEYBOARD_ROWS):
                    other_line = KEYBOARD_ROWS[other_row]
                    other_index = key_index + shift
                    if 0 <= other_index < len(other_line):
                        near.add(other_line[other_index])
            neighbors[key] = near
    return neighbors


NEIGHBORS = _build_neighbors()


def _is_nearby(first: str | None, second: str | None) -> bool:
    return bool(first and second and second in NEIGHBORS.get(first, ()))


def _substitution_cost(typed: str, intended: str) -> float:
    if _is_nearby(typed, intended):
        return COST_ADJACENT_KEY
    if typed in VOWELS and intended in VOWELS:
        return COST_VOWEL_FOR_VOWEL
    return COST_WRONG_KEY


def _extra_key_cost(typed: str, position: int) -> float:
    key = typed[position]
    if (position > 0 and key == typed[position - 1]) or (
        position + 1 < len(typed) and key == typed[position + 1]
    ):
        return COST_DOUBLED_KEY
    left = typed[position - 1] if position > 0 else None
    right = typed[position + 1] if position + 1 < len(typed) else None
    if _is_nearby(key, left) or _is_nearby(key, right):
        return COST_EXTRA_NEARBY_KEY
    return COST_EXTRA_KEY


def _missed_key_cost(intended: str, position: int) -> float:
    if position > 0 and intended[position] == intended[position - 1]:
        return COST_DOUBLED_KEY
    return COST_MISSED_KEY


def _start(position: int) -> float:
    return COST_FIRST_LETTER_PENALTY if position == 0 else 0.0


def typo_cost(typed: str, intended: str) -> float:
    """Weighted edit distance: a lower cost means a more believable slip of the fingers."""
    rows = len(typed) + 1
    columns = len(intended) + 1
    table = [[0.0] * columns for _ in range(rows)]

    for row in range(1, rows):
        table[row][0] = table[row - 1][0] + _extra_key_cost(typed, row - 1) + _start(row - 1)
    for column in range(1, columns):
        table[0][column] = table[0][column - 1] + _missed_key_cost(intended, column - 1) + _start(column - 1)

    for row in range(1, rows):
        for column in range(1, columns):
            typed_key = typed[row - 1]
            intended_key = intended[column - 1]
            first_letter = _start(min(row, column) - 1)
            replace = 0.0 if typed_key == intended_key else _substitution_cost(typed_key, intended_key) + first_letter
            best = min(
                table[row - 1][column] + _extra_key_cost(typed, row - 1) + first_letter,
                table[row][column - 1] + _missed_key_cost(intended, column - 1) + first_letter,
                table[row - 1][column - 1] + replace,
            )
            swapped = (
                row > 1
                and column > 1
                and typed_key == intended[column - 2]
                and typed[row - 2] == intended_key
            )
            if swapped:
                best = min(best, table[row - 2][column - 2] + COST_SWAP)
            table[row][column] = best
    return table[rows - 1][columns - 1]
