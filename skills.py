"""User-defined TinyFix skills: storage, defaults, templating, and hotkey conflicts."""

from __future__ import annotations

import json
import uuid
from dataclasses import dataclass, field
from pathlib import Path

from hotkey_util import (
    DEFAULT_HOTKEY,
    DEFAULT_HOTKEY_ALL,
    DEFAULT_HOTKEY_MENU,
    DEFAULT_HOTKEY_TRANSLATE,
    Hotkey,
    disabled_hotkey,
    is_disabled_spec,
    parse_hotkey,
)
from prompts import (
    DEFAULT_FIX_PROMPT,
    DEFAULT_IMPROVE_PROMPT,
    DEFAULT_REWRITE_LANGUAGE_PROMPT,
    DEFAULT_REWRITE_PROMPT,
    DEFAULT_TRANSLATE_PROMPT,
    LEGACY_FIX_PROMPTS,
    LEGACY_IMPROVE_PROMPTS,
    LEGACY_TRANSLATE_PROMPTS,
    render_prompt,
)

SKILLS_VERSION = 1
SKILLS_FILENAME = "skills.json"

SKILL_FIX = "fix"
SKILL_FIX_ALL = "fix-all"
SKILL_TRANSLATE = "translate"
SKILL_REWRITE = "rewrite"
SKILL_REWRITE_LANGUAGE = "rewrite-language"
SKILL_IMPROVE_PROMPT = "improve-prompt"
BUILTIN_IDS = (
    SKILL_FIX,
    SKILL_FIX_ALL,
    SKILL_TRANSLATE,
    SKILL_REWRITE,
    SKILL_REWRITE_LANGUAGE,
    SKILL_IMPROVE_PROMPT,
)
CORE_BUILTIN_IDS = (SKILL_FIX, SKILL_FIX_ALL, SKILL_TRANSLATE)
OPTIONAL_BUILTIN_IDS = (SKILL_REWRITE, SKILL_REWRITE_LANGUAGE, SKILL_IMPROVE_PROMPT)

INPUT_SELECTION = "selection"
INPUT_SELECT_ALL = "select_all"
INPUT_MODES = (INPUT_SELECTION, INPUT_SELECT_ALL)

DEFAULT_CUSTOM_PROMPT = """Rewrite or transform the text below as instructed. Keep the original language unless you are asked to translate.

Do not add a heading, explanation, or commentary. Do not wrap the result in quotation marks or code fences. Return only the result.

{{content}}
"""

EXAMPLE_SKILL_SPECS = (
    (
        "Make formal email",
        "Rewrite the text below as a clear, polite formal email. Keep the meaning, tighten the wording, and use a professional tone. Return only the email body.\n\n{{content}}\n",
    ),
    (
        "Summarize",
        "Summarize the text below in a few short sentences. Keep the key facts and the original language. Return only the summary.\n\n{{content}}\n",
    ),
    (
        "Translate to German",
        "Translate the text below into German. Keep the meaning, tone, and line breaks. Return only the translation.\n\n{{content}}\n",
    ),
)


def _hotkey_spec(value: str | Hotkey | None, default: str) -> str:
    if value is None or value == "":
        return default
    if isinstance(value, Hotkey):
        return "none" if value.is_disabled else value.display
    text = str(value).strip()
    if not text or is_disabled_spec(text):
        return "none"
    try:
        parsed = parse_hotkey(text)
    except ValueError:
        return default
    return "none" if parsed.is_disabled else parsed.display


def parse_skill_hotkey(spec: str | Hotkey | None) -> Hotkey:
    if isinstance(spec, Hotkey):
        return spec
    if spec is None or str(spec).strip() == "" or is_disabled_spec(str(spec)):
        return disabled_hotkey()
    try:
        return parse_hotkey(str(spec))
    except ValueError:
        return disabled_hotkey()


def normalize_input_mode(value: str | None) -> str:
    raw = (value or "").strip().lower().replace("-", "_").replace(" ", "_")
    if raw in {"select_all", "selectall", "all"}:
        return INPUT_SELECT_ALL
    return INPUT_SELECTION


@dataclass
class Skill:
    id: str
    name: str
    input_mode: str = INPUT_SELECTION
    prompt: str = DEFAULT_CUSTOM_PROMPT
    hotkey: str = "none"
    enabled: bool = True
    builtin: bool = False
    offline_fallback: bool = False

    def __post_init__(self) -> None:
        self.id = (self.id or "").strip() or str(uuid.uuid4())
        self.name = (self.name or "").strip() or "New skill"
        self.input_mode = normalize_input_mode(self.input_mode)
        self.prompt = self.prompt if self.prompt is not None else ""
        self.hotkey = _hotkey_spec(self.hotkey, "none")
        self.enabled = bool(self.enabled)
        self.builtin = bool(self.builtin)
        self.offline_fallback = self.id in (SKILL_FIX, SKILL_FIX_ALL)

    @property
    def needs_ai(self) -> bool:
        return not self.offline_fallback

    @property
    def select_all(self) -> bool:
        return self.input_mode == INPUT_SELECT_ALL

    def hotkey_obj(self) -> Hotkey:
        return parse_skill_hotkey(self.hotkey)

    def display_hotkey(self) -> str:
        hotkey = self.hotkey_obj()
        return "None" if hotkey.is_disabled else hotkey.display

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "name": self.name,
            "input_mode": self.input_mode,
            "prompt": self.prompt,
            "hotkey": self.hotkey,
            "enabled": self.enabled,
            "builtin": self.builtin,
            "offline_fallback": self.offline_fallback,
        }

    @classmethod
    def from_dict(cls, data: dict | None) -> "Skill":
        raw = data or {}
        return cls(
            id=str(raw.get("id") or ""),
            name=str(raw.get("name") or "New skill"),
            input_mode=str(raw.get("input_mode") or INPUT_SELECTION),
            prompt=str(raw.get("prompt") if raw.get("prompt") is not None else DEFAULT_CUSTOM_PROMPT),
            hotkey=str(raw.get("hotkey") if raw.get("hotkey") is not None else "none"),
            enabled=bool(raw.get("enabled", True)),
            builtin=bool(raw.get("builtin", False)),
            offline_fallback=bool(raw.get("offline_fallback", False)),
        )


@dataclass(frozen=True)
class HotkeyConflict:
    left: str
    right: str
    hotkey: str

    def describe(self) -> str:
        return f"{self.left} and {self.right} both use {self.hotkey}"


@dataclass
class SkillStore:
    version: int = SKILLS_VERSION
    skills: list[Skill] = field(default_factory=list)
    skill_menu_hotkey: str = DEFAULT_HOTKEY_MENU
    # Ids of optional built-in skills the user deleted. They are not added back on the next start.
    dismissed: list[str] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "version": int(self.version or SKILLS_VERSION),
            "skill_menu_hotkey": _hotkey_spec(self.skill_menu_hotkey, DEFAULT_HOTKEY_MENU),
            "dismissed": [str(item) for item in self.dismissed],
            "skills": [skill.to_dict() for skill in self.skills],
        }

    @classmethod
    def from_dict(cls, data: dict | None) -> "SkillStore":
        raw = data or {}
        skills = [Skill.from_dict(item) for item in (raw.get("skills") or []) if isinstance(item, dict)]
        return cls(
            version=int(raw.get("version") or SKILLS_VERSION),
            skills=skills,
            skill_menu_hotkey=_hotkey_spec(raw.get("skill_menu_hotkey"), DEFAULT_HOTKEY_MENU),
            dismissed=[str(item) for item in (raw.get("dismissed") or []) if isinstance(item, str)],
        )

    def get(self, skill_id: str) -> Skill | None:
        for skill in self.skills:
            if skill.id == skill_id:
                return skill
        return None

    def menu_hotkey(self) -> Hotkey:
        return parse_skill_hotkey(self.skill_menu_hotkey)


def skills_path(root: Path) -> Path:
    return Path(root) / SKILLS_FILENAME


def default_skills(
    *,
    fix_hotkey: str | Hotkey = DEFAULT_HOTKEY,
    fix_all_hotkey: str | Hotkey = DEFAULT_HOTKEY_ALL,
    translate_hotkey: str | Hotkey = DEFAULT_HOTKEY_TRANSLATE,
    translate_select_all: bool = False,
    fix_prompt: str = DEFAULT_FIX_PROMPT,
    translate_prompt: str = DEFAULT_TRANSLATE_PROMPT,
) -> list[Skill]:
    return [
        Skill(
            id=SKILL_FIX,
            name="Fix",
            input_mode=INPUT_SELECTION,
            prompt=fix_prompt or DEFAULT_FIX_PROMPT,
            hotkey=_hotkey_spec(fix_hotkey, DEFAULT_HOTKEY),
            enabled=True,
            builtin=True,
            offline_fallback=True,
        ),
        Skill(
            id=SKILL_FIX_ALL,
            name="Fix all",
            input_mode=INPUT_SELECT_ALL,
            prompt=fix_prompt or DEFAULT_FIX_PROMPT,
            hotkey=_hotkey_spec(fix_all_hotkey, DEFAULT_HOTKEY_ALL),
            enabled=True,
            builtin=True,
            offline_fallback=True,
        ),
        Skill(
            id=SKILL_TRANSLATE,
            name="Translate",
            input_mode=INPUT_SELECT_ALL if translate_select_all else INPUT_SELECTION,
            prompt=translate_prompt or DEFAULT_TRANSLATE_PROMPT,
            hotkey=_hotkey_spec(translate_hotkey, DEFAULT_HOTKEY_TRANSLATE),
            enabled=True,
            builtin=True,
            offline_fallback=False,
        ),
        Skill(
            id=SKILL_REWRITE,
            name="Rewrite",
            input_mode=INPUT_SELECTION,
            prompt=DEFAULT_REWRITE_PROMPT,
            hotkey="none",
            enabled=False,
            builtin=True,
            offline_fallback=False,
        ),
        Skill(
            id=SKILL_REWRITE_LANGUAGE,
            name="Rewrite to target language",
            input_mode=INPUT_SELECTION,
            prompt=DEFAULT_REWRITE_LANGUAGE_PROMPT,
            hotkey="none",
            enabled=False,
            builtin=True,
            offline_fallback=False,
        ),
        Skill(
            id=SKILL_IMPROVE_PROMPT,
            name="Improve prompt",
            input_mode=INPUT_SELECTION,
            prompt=DEFAULT_IMPROVE_PROMPT,
            hotkey="none",
            enabled=True,
            builtin=True,
            offline_fallback=False,
        ),
    ]


def default_store(**legacy) -> SkillStore:
    return SkillStore(version=SKILLS_VERSION, skills=default_skills(**legacy), skill_menu_hotkey=DEFAULT_HOTKEY_MENU)


def ensure_builtins(store: SkillStore, **legacy) -> SkillStore:
    have = {skill.id for skill in store.skills}
    defaults = default_skills(**legacy)
    missing_core = [skill for skill in defaults if skill.id in CORE_BUILTIN_IDS and skill.id not in have]
    gone = set(store.dismissed)
    missing_rewrite = [
        skill
        for skill in defaults
        if skill.id in OPTIONAL_BUILTIN_IDS and skill.id not in have and skill.id not in gone
    ]
    if missing_core:
        store.skills = missing_core + list(store.skills)
    if missing_rewrite:
        store.skills = list(store.skills) + missing_rewrite
    for skill in store.skills:
        if skill.id in (SKILL_FIX, SKILL_FIX_ALL):
            skill.builtin = True
            skill.offline_fallback = True
        elif skill.id in (SKILL_TRANSLATE, SKILL_REWRITE, SKILL_REWRITE_LANGUAGE, SKILL_IMPROVE_PROMPT):
            skill.builtin = True
            skill.offline_fallback = False
        if skill.id == SKILL_IMPROVE_PROMPT and skill.prompt.strip() in {p.strip() for p in LEGACY_IMPROVE_PROMPTS}:
            # Unedited copy of an older default: upgrade it. Customised prompts are left alone.
            skill.prompt = DEFAULT_IMPROVE_PROMPT
        if skill.id in (SKILL_FIX, SKILL_FIX_ALL) and skill.prompt.strip() in {p.strip() for p in LEGACY_FIX_PROMPTS}:
            skill.prompt = DEFAULT_FIX_PROMPT
        if skill.id == SKILL_TRANSLATE and skill.prompt.strip() in {p.strip() for p in LEGACY_TRANSLATE_PROMPTS}:
            skill.prompt = DEFAULT_TRANSLATE_PROMPT
    return store


def new_custom_skill(name: str = "New skill", prompt: str | None = None) -> Skill:
    return Skill(
        id=str(uuid.uuid4()),
        name=(name or "").strip() or "New skill",
        input_mode=INPUT_SELECTION,
        prompt=prompt if prompt is not None else DEFAULT_CUSTOM_PROMPT,
        hotkey="none",
        enabled=True,
        builtin=False,
        offline_fallback=False,
    )


def duplicate_skill(skill: Skill) -> Skill:
    copy = Skill.from_dict(skill.to_dict())
    copy.id = str(uuid.uuid4())
    copy.name = f"{skill.name} copy"
    copy.hotkey = "none"
    copy.builtin = False
    copy.offline_fallback = False
    copy.enabled = True
    return copy


def can_delete(skill: Skill) -> bool:
    """Fix, Fix all and Translate can only be turned off: the app's fallback and old settings rely on them."""
    return skill.id not in CORE_BUILTIN_IDS


def delete_skill(store: SkillStore, skill_id: str) -> bool:
    skill = store.get(skill_id)
    if skill is None or not can_delete(skill):
        return False
    store.skills = [item for item in store.skills if item.id != skill_id]
    if skill_id in OPTIONAL_BUILTIN_IDS and skill_id not in store.dismissed:
        store.dismissed.append(skill_id)
    return True


def move_skill(store: SkillStore, skill_id: str, delta: int) -> bool:
    index = next((i for i, skill in enumerate(store.skills) if skill.id == skill_id), -1)
    if index < 0 or delta == 0:
        return False
    target = index + delta
    if target < 0 or target >= len(store.skills):
        return False
    store.skills[index], store.skills[target] = store.skills[target], store.skills[index]
    return True


def render_skill_prompt(skill: Skill, content: str, language: str | None = None) -> str:
    return render_prompt(skill.prompt or "", content, language=language)


def find_conflicts(skills: list[Skill], menu_hotkey: str | Hotkey | None = None) -> list[HotkeyConflict]:
    entries: list[tuple[str, Hotkey]] = []
    for skill in skills:
        if not skill.enabled:
            continue
        hotkey = skill.hotkey_obj()
        if hotkey.is_disabled:
            continue
        entries.append((skill.name, hotkey))
    menu = parse_skill_hotkey(menu_hotkey)
    if not menu.is_disabled:
        entries.append(("Skill menu", menu))
    conflicts: list[HotkeyConflict] = []
    for index, (left, left_hotkey) in enumerate(entries):
        for right, right_hotkey in entries[index + 1 :]:
            if left_hotkey.vk == right_hotkey.vk and (left_hotkey.modifiers & 0x0F) == (right_hotkey.modifiers & 0x0F):
                conflicts.append(HotkeyConflict(left, right, left_hotkey.display))
    return conflicts


def enabled_skills(store: SkillStore, *, ai_ready: bool = True) -> list[Skill]:
    items: list[Skill] = []
    for skill in store.skills:
        if not skill.enabled:
            continue
        if skill.needs_ai and not ai_ready:
            continue
        items.append(skill)
    return items


def visible_menu_skills(store: SkillStore) -> list[Skill]:
    return [skill for skill in store.skills if skill.enabled]


def legacy_from_store(store: SkillStore) -> dict:
    """Map builtin skills back to the older config.ini fields."""
    fix = store.get(SKILL_FIX) or default_skills()[0]
    fix_all = store.get(SKILL_FIX_ALL) or default_skills()[1]
    translate = store.get(SKILL_TRANSLATE) or default_skills()[2]
    return {
        "hotkey": fix.hotkey_obj(),
        "hotkey_all": fix_all.hotkey_obj(),
        "hotkey_translate": translate.hotkey_obj(),
        "translate_select_all": translate.select_all,
        "fix_prompt": fix.prompt,
        "translate_prompt": translate.prompt,
    }


def load_skill_store(path: Path, **legacy) -> tuple[SkillStore, bool]:
    """Load skills.json, or migrate from legacy settings. Returns (store, created)."""
    path = Path(path)
    if path.exists():
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
            if not isinstance(raw, dict):
                raise ValueError("skills.json is not an object")
            store = ensure_builtins(SkillStore.from_dict(raw), **legacy)
            return store, False
        except Exception:
            store = ensure_builtins(default_store(**legacy), **legacy)
            return store, True
    return ensure_builtins(default_store(**legacy), **legacy), True


def save_skill_store(path: Path, store: SkillStore) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(store.to_dict(), ensure_ascii=False, indent=2) + "\n"
    path.write_text(payload, encoding="utf-8")
