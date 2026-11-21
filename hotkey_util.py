"""Parse and format TinyFix global hotkeys (e.g. Ctrl+Alt+Space)."""

from __future__ import annotations

from dataclasses import dataclass

MOD_ALT = 0x0001
MOD_CONTROL = 0x0002
MOD_SHIFT = 0x0004
MOD_WIN = 0x0008
MOD_NOREPEAT = 0x4000

VK_BACK = 0x08
VK_TAB = 0x09
VK_RETURN = 0x0D
VK_ESCAPE = 0x1B
VK_SPACE = 0x20
VK_PRIOR = 0x21
VK_NEXT = 0x22
VK_END = 0x23
VK_HOME = 0x24
VK_LEFT = 0x25
VK_UP = 0x26
VK_RIGHT = 0x27
VK_DOWN = 0x28
VK_INSERT = 0x2D
VK_DELETE = 0x2E
VK_OEM_PLUS = 0xBB
VK_OEM_COMMA = 0xBC
VK_OEM_MINUS = 0xBD
VK_OEM_PERIOD = 0xBE
VK_OEM_2 = 0xBF  # /
VK_OEM_3 = 0xC0  # `
VK_OEM_4 = 0xDB  # [
VK_OEM_6 = 0xDD  # ]
VK_OEM_1 = 0xBA  # ;
VK_OEM_7 = 0xDE  # '

DEFAULT_HOTKEY = "Ctrl+Alt+Space"

MODIFIER_NAMES: dict[str, int] = {
    "ctrl": MOD_CONTROL,
    "control": MOD_CONTROL,
    "alt": MOD_ALT,
    "shift": MOD_SHIFT,
    "win": MOD_WIN,
    "windows": MOD_WIN,
    "meta": MOD_WIN,
}

_NAMED_KEYS: dict[str, int] = {
    "space": VK_SPACE,
    "tab": VK_TAB,
    "enter": VK_RETURN,
    "return": VK_RETURN,
    "escape": VK_ESCAPE,
    "esc": VK_ESCAPE,
    "backspace": VK_BACK,
    "insert": VK_INSERT,
    "ins": VK_INSERT,
    "delete": VK_DELETE,
    "del": VK_DELETE,
    "home": VK_HOME,
    "end": VK_END,
    "pageup": VK_PRIOR,
    "pgup": VK_PRIOR,
    "pagedown": VK_NEXT,
    "pgdn": VK_NEXT,
    "left": VK_LEFT,
    "up": VK_UP,
    "right": VK_RIGHT,
    "down": VK_DOWN,
    "plus": VK_OEM_PLUS,
    "minus": VK_OEM_MINUS,
    "comma": VK_OEM_COMMA,
    "period": VK_OEM_PERIOD,
    "dot": VK_OEM_PERIOD,
    "slash": VK_OEM_2,
    "backslash": 0xDC,
    "semicolon": VK_OEM_1,
    "quote": VK_OEM_7,
    "apostrophe": VK_OEM_7,
    "leftbracket": VK_OEM_4,
    "rightbracket": VK_OEM_6,
    "backtick": VK_OEM_3,
}

_VK_DISPLAY: dict[int, str] = {
    VK_SPACE: "Space",
    VK_TAB: "Tab",
    VK_RETURN: "Enter",
    VK_ESCAPE: "Esc",
    VK_BACK: "Backspace",
    VK_INSERT: "Insert",
    VK_DELETE: "Delete",
    VK_HOME: "Home",
    VK_END: "End",
    VK_PRIOR: "PageUp",
    VK_NEXT: "PageDown",
    VK_LEFT: "Left",
    VK_UP: "Up",
    VK_RIGHT: "Right",
    VK_DOWN: "Down",
    VK_OEM_PLUS: "Plus",
    VK_OEM_MINUS: "Minus",
    VK_OEM_COMMA: "Comma",
    VK_OEM_PERIOD: "Period",
    VK_OEM_2: "Slash",
    0xDC: "Backslash",
    VK_OEM_1: "Semicolon",
    VK_OEM_7: "Quote",
    VK_OEM_4: "LeftBracket",
    VK_OEM_6: "RightBracket",
    VK_OEM_3: "Backtick",
}

KEY_CHOICES: tuple[str, ...] = (
    tuple(f"F{i}" for i in range(1, 13))
    + ("Space",)
    + tuple(chr(c) for c in range(ord("A"), ord("Z") + 1))
    + tuple(str(d) for d in range(10))
    + (
        "Tab",
        "Enter",
        "Esc",
        "Backspace",
        "Insert",
        "Delete",
        "Home",
        "End",
        "PageUp",
        "PageDown",
        "Left",
        "Right",
        "Up",
        "Down",
    )
)


@dataclass(frozen=True)
class Hotkey:
    modifiers: int
    vk: int
    display: str

    @property
    def register_modifiers(self) -> int:
        return (self.modifiers & 0x0F) | MOD_NOREPEAT

    @property
    def is_function_key(self) -> bool:
        return 0x70 <= self.vk <= 0x7B

    def needs_modifier(self) -> bool:
        return not self.is_function_key

    def is_safe(self) -> bool:
        if self.is_function_key:
            return True
        return (self.modifiers & 0x0F) != 0


def vk_from_name(name: str) -> int:
    raw = name.strip()
    if not raw:
        raise ValueError("Missing key name")
    lower = raw.lower().replace(" ", "")
    if lower in _NAMED_KEYS:
        return _NAMED_KEYS[lower]
    if len(lower) >= 2 and lower[0] == "f" and lower[1:].isdigit():
        number = int(lower[1:])
        if 1 <= number <= 24:
            return 0x70 + number - 1
        raise ValueError(f"Unsupported function key: {raw}")
    if len(raw) == 1:
        char = raw.upper()
        if "A" <= char <= "Z":
            return ord(char)
        if "0" <= char <= "9":
            return ord(char)
    raise ValueError(f"Unsupported key: {raw}")


def display_key(vk: int) -> str:
    if vk in _VK_DISPLAY:
        return _VK_DISPLAY[vk]
    if 0x70 <= vk <= 0x87:
        return f"F{vk - 0x70 + 1}"
    if 0x41 <= vk <= 0x5A or 0x30 <= vk <= 0x39:
        return chr(vk)
    return f"VK_{vk:02X}"


def format_hotkey(modifiers: int, vk: int) -> str:
    parts: list[str] = []
    if modifiers & MOD_CONTROL:
        parts.append("Ctrl")
    if modifiers & MOD_ALT:
        parts.append("Alt")
    if modifiers & MOD_SHIFT:
        parts.append("Shift")
    if modifiers & MOD_WIN:
        parts.append("Win")
    parts.append(display_key(vk))
    return "+".join(parts)


def parse_hotkey(spec: str) -> Hotkey:
    if not spec or not str(spec).strip():
        raise ValueError("Hotkey is empty")
    text = str(spec).strip().replace("-", "+")
    tokens = [token.strip() for token in text.split("+") if token.strip()]
    if not tokens:
        raise ValueError("Hotkey is empty")
    modifiers = 0
    key_token: str | None = None
    for token in tokens:
        mapped = MODIFIER_NAMES.get(token.lower())
        if mapped is not None:
            modifiers |= mapped
            continue
        if key_token is not None:
            raise ValueError(f"More than one key in hotkey: {spec}")
        key_token = token
    if key_token is None:
        raise ValueError(f"Hotkey is missing a key: {spec}")
    vk = vk_from_name(key_token)
    return Hotkey(modifiers=modifiers, vk=vk, display=format_hotkey(modifiers, vk))
