"""A tiny, dependency-free guess: does this text look like English?

Why it exists: the offline engine only understands English. When the online model is slow or down, running that
engine over German, Spanish, or Persian text is useless at best and damaging at worst (it "corrects" real words).
So for text that does not look English, TinyFix waits longer for the online model and, if that still fails,
leaves the text alone instead of falling back.

It is a guess on purpose. Wrong "not English" only means a longer wait and no offline fallback; wrong "English"
means the old behaviour. Short texts (three words or fewer) count as English because there is nothing to tell.
"""

from __future__ import annotations

import re

WORD = re.compile(r"[^\W\d_]+(?:['’][^\W\d_]+)?")
NON_ENGLISH_SCRIPT_SHARE = 0.2  # more than this share of words in another script: not English
STOPWORD_SHARE = 0.12  # English prose is full of these; German, Spanish, French, etc. rarely hit them

ENGLISH_STOPWORDS = frozenset(
    """
    the be to of and a in that have i it for not on with he as you do at this but his by from they we say her she
    or an will my one all would there their what so up out if about who get which go me when make can like time no
    just him know take people into year your good some could them see other than then now look only come its over
    think also back after use two how our work first well way even new want because any these give day most us is
    are was were been has had did does am im dont cant wont didnt isnt its thats theres
    """.split()
)


def words_of(text: str) -> list[str]:
    return WORD.findall(text or "")


def looks_english(text: str) -> bool:
    """True when the text is probably English (or too short or wordless to tell)."""
    words = words_of(text)
    if not words:
        return True
    other_script = sum(1 for word in words if not word.isascii())
    if other_script / len(words) > NON_ENGLISH_SCRIPT_SHARE:
        return False
    ascii_words = [word.lower().replace("’", "'") for word in words if word.isascii()]
    if len(ascii_words) <= 3:
        return True
    hits = sum(1 for word in ascii_words if word in ENGLISH_STOPWORDS or word.replace("'", "") in ENGLISH_STOPWORDS)
    return hits / len(ascii_words) >= STOPWORD_SHARE
