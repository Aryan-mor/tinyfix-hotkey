from __future__ import annotations

import re

CONTRACTIONS: dict[str, str] = {
    "arent": "aren't",
    "cant": "can't",
    "couldnt": "couldn't",
    "didnt": "didn't",
    "doesnt": "doesn't",
    "dont": "don't",
    "hadnt": "hadn't",
    "hasnt": "hasn't",
    "havent": "haven't",
    "im": "I'm",
    "isnt": "isn't",
    "ive": "I've",
    "shouldnt": "shouldn't",
    "thats": "that's",
    "theres": "there's",
    "theyre": "they're",
    "theyve": "they've",
    "wasnt": "wasn't",
    "werent": "weren't",
    "weve": "we've",
    "whats": "what's",
    "wont": "won't",
    "wouldnt": "wouldn't",
    "youre": "you're",
    "youve": "you've",
}

BRITISH_OUR_ENDING = re.compile(r"(.{2,})our(s|ed|ing|ite|ites|ful|able|ably|less|hood|hoods|ly|ist|ists|er|ers)?$")
BRITISH_ISE_ENDING = re.compile(r"(is|ys)(e|es|ed|ing|er|ers|ation|ations|able)$")
BRITISH_DOUBLED_L = re.compile(
    r"^(un|re|mis|pre)?(travel|cancel|model|label|signal|fuel|level|total|marvel|counsel|jewel|tunnel|channel|shovel|dial|equal|quarrel|rival|panel|funnel|pencil|spiral|trial)l(ed|ing|er|ers|or|ors)$"
)
MIN_LENGTH_FOR_ISE_RULE = 7
BRITISH_DOUBLED_L_ENDING = re.compile(r"ll(ed|ing|er|ers|or|ors|ist|ists|ous)$")
BRITISH_RE_ENDING = re.compile(r"([^aeiou])re(s|d)?$")
MIN_LENGTH_FOR_RE_RULE = 5
BRITISH_DIGRAPH = re.compile(r"(ae|oe)")
MIN_LENGTH_FOR_DIGRAPH_RULE = 7

_BRITISH_BASE = (
    "theatre centre centred metre litre fibre calibre sombre spectre defence offence licence pretence catalogue analogue programme judgement "
    "ageing aluminium fulfil enrol skilful wilful jewellery tyre grey mould moulding plough sceptical sceptic cheque kerb draught manoeuvre "
    "paediatric anaesthetic foetus oestrogen moustache pyjamas storey acknowledgement instalment distil instil appal "
    "whisky enquiry amongst whilst"
).split()
BRITISH_WORDS = {word for base in _BRITISH_BASE for word in (base, f"{base}s")}

_NEWER_BASE = (
    "https emoji todo selfie hashtag livestream chatbot vlog vlogger influencer fintech covid podcaster blogger unfollow "
    "unfriend retweet screenshare webinar onboarding offboarding skincare smartwatch earbud airpod wifi bluetooth "
    "async runtime frontend backend dataset username login logout signup checkout touchscreen spreadsheet "
    "workflow timeline toolbar dropdown popup tooltip codebase repo config dev devops cybersecurity crypto "
    "kombucha matcha ramen udon pho bao boba poke quinoa kale hummus falafel tahini sriracha kimchi gochujang "
    "tofu tempeh seitan acai chia granola brunch latte cappuccino espresso macchiato americano frappe smoothie "
    "barista crema robusta arabica croissant focaccia ciabatta brioche sourdough gelato prosecco charcuterie "
    "hygge wellness mindfulness hangry yeet meme binge rewatch preorder crowdfund carpool rideshare "
    "staycation glamping bae vibe vibes ghosting doomscroll doomscrolling cringe adulting"
).split()
NEWER_WORDS = {word for base in _NEWER_BASE for word in (base, f"{base}s")}

SENTENCE_START = re.compile(r"(^|[.!?:\n]\s*|^\s*[-*•]\s+)$")
CHECKABLE_WORD = re.compile(r"^[A-Za-z]+$")
PREVIOUS_NOT_PROSE = re.compile(r"[/@\\_#]|\.\w")


def is_newer_word(lower: str) -> bool:
    return lower in NEWER_WORDS


def is_british_spelling(lower: str, is_known) -> bool:
    if lower in BRITISH_WORDS:
        return True
    if BRITISH_DOUBLED_L.search(lower):
        return True
    with_or = BRITISH_OUR_ENDING.sub(r"\1or\2", lower)
    if with_or != lower and is_known(with_or):
        return True
    single_l = BRITISH_DOUBLED_L_ENDING.sub(r"l\1", lower)
    if single_l != lower and is_known(single_l):
        return True
    if len(lower) >= MIN_LENGTH_FOR_RE_RULE:
        def _re_repl(match: re.Match[str]) -> str:
            before, ending = match.group(1), match.group(2) or ""
            return f"{before}er{'ed' if ending == 'd' else ending}"

        with_er = BRITISH_RE_ENDING.sub(_re_repl, lower)
    else:
        with_er = lower
    if with_er != lower and is_known(with_er):
        return True
    if len(lower) >= MIN_LENGTH_FOR_DIGRAPH_RULE and BRITISH_DIGRAPH.search(lower) and is_known(BRITISH_DIGRAPH.sub("e", lower)):
        return True
    if len(lower) < MIN_LENGTH_FOR_ISE_RULE:
        return False

    def _ise_repl(match: re.Match[str]) -> str:
        stem, ending = match.group(1), match.group(2)
        return f"{'iz' if stem == 'is' else 'yz'}{ending}"

    with_z = BRITISH_ISE_ENDING.sub(_ise_repl, lower)
    return with_z != lower and is_known(with_z)


def is_sentence_start(text_before: str) -> bool:
    return bool(SENTENCE_START.search(text_before))


def match_case(typed: str, replacement: str) -> str:
    if typed and typed[0] != typed[0].lower():
        return replacement[0].upper() + replacement[1:]
    return replacement


def apply_fixed_rule(word: str) -> str | None:
    if word == "i":
        return "I"
    contraction = CONTRACTIONS.get(word.lower())
    if not contraction:
        return None
    return contraction if contraction.startswith("I") else match_case(word, contraction)


def is_checkable(word: str, text_before: str) -> bool:
    if len(word) < 3 or not CHECKABLE_WORD.fullmatch(word):
        return False
    if word == word.upper():
        return False
    if re.search(r"[A-Z]", word[1:]):
        return False
    is_capitalized = word[0] != word[0].lower()
    if is_capitalized and not is_sentence_start(text_before):
        return False
    stripped = text_before.rstrip()
    previous_token = re.split(r"\s", stripped)[-1] if stripped else ""
    return not PREVIOUS_NOT_PROSE.search(previous_token)
