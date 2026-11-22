"""Shared builders for offline-pack tests."""

from __future__ import annotations

import json
import sys
import zipfile
from pathlib import Path

import offline_pack as op


def make_zip(path: Path, *, python=None, version=None, fmt=None, model=True, lib=True, extra=None, big=0) -> Path:
    manifest = {
        "format": op.PACK_FORMAT if fmt is None else fmt,
        "pack_version": op.REQUIRED_PACK_VERSION if version is None else version,
        "python": python or f"{sys.version_info.major}.{sys.version_info.minor}",
    }
    with zipfile.ZipFile(path, "w", zipfile.ZIP_STORED) as z:
        z.writestr("manifest.json", json.dumps(manifest))
        if model:
            z.writestr(op.MODEL_FILE, b"model")
        if lib:
            z.writestr("lib/numpy/__init__.py", "")
        if big:
            z.writestr("lib/blob.bin", b"x" * big)
        for name, data in (extra or {}).items():
            z.writestr(name, data)
    return path
