# -*- mode: python ; coding: utf-8 -*-
from pathlib import Path

from PyInstaller.utils.hooks import collect_all

root = Path(SPECPATH)
datas = []
binaries = []
hiddenimports = [
    "tkinter",
    "_tkinter",
    "engine",
    "engine.fixer",
    "engine.rules",
    "engine.trie",
    "engine.typo_cost",
    "engine.wrong_word",
    "tinyfix_lib",
    "hotkey_util",
    "update_check",
    "prompts",
    "secret_store",
    "llm_client",
    "settings_ui",
    "skills",
    "skill_menu",
    "sv_ttk",
]

for pkg in ("onnxruntime", "pystray", "PIL", "sv_ttk"):
    collected_datas, collected_binaries, collected_hidden = collect_all(pkg)
    datas += collected_datas
    binaries += collected_binaries
    hiddenimports += collected_hidden

for name in ("wrong-word.onnx", "wrong-word-vocab.txt", "wrong-word-confusions.txt", "words.txt"):
    datas.append((str(root / "models" / name), "models"))

rev = root / "model-revision.txt"
if rev.exists():
    datas.append((str(rev), "."))

icon = root / "assets" / "tinyfix.ico"
if icon.exists():
    datas.append((str(icon), "."))

no_slop = root / "third_party" / "no-slop"
if no_slop.is_dir():
    datas.append((str(no_slop), "third_party/no-slop"))

a = Analysis(
    ["app.py"],
    pathex=[str(root)],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="TinyFix",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    icon=str(icon) if icon.exists() else None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    name="TinyFix",
)
