"""Build the optional offline pack zip from the current Python environment.

Usage: python scripts/build_pack.py [--out DIR]
Writes TinyFix-OfflinePack-<n>-cp<XY>.zip and its .json description (sha256, size, RAM numbers).
Run it with the same Python that builds the exe, because numpy/onnxruntime are compiled per version.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.metadata as md
import json
import shutil
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import offline_pack as pack  # noqa: E402

DROP_ORT = {"tools", "quantization", "transformers", "datasets", "backend"}
DROP_ANY = {"__pycache__", "tests", "test"}


def _copy_tree(src: Path, dst: Path, drop: set[str]) -> None:
    for item in src.iterdir():
        if item.name in drop or item.suffix == ".pyc":
            continue
        target = dst / item.name
        if item.is_dir():
            target.mkdir(parents=True, exist_ok=True)
            _copy_tree(item, target, drop)
        else:
            shutil.copy2(item, target)


def build(out_dir: Path, ram_mb: int | None = None) -> tuple[Path, Path]:
    import numpy
    import onnxruntime

    out_dir.mkdir(parents=True, exist_ok=True)
    staging = out_dir / "_staging"
    if staging.exists():
        shutil.rmtree(staging)
    lib = staging / "lib"
    lib.mkdir(parents=True)

    site = Path(numpy.__file__).resolve().parent.parent
    for name in ("numpy", "onnxruntime"):
        src = site / name
        dst = lib / name
        dst.mkdir()
        _copy_tree(src, dst, DROP_ANY | (DROP_ORT if name == "onnxruntime" else set()))
    libs = site / "numpy.libs"
    if libs.is_dir():
        (lib / "numpy.libs").mkdir()
        _copy_tree(libs, lib / "numpy.libs", DROP_ANY)
    # onnxruntime needs a few pure-Python helpers at import time; copy the ones that are installed.
    for extra in ("flatbuffers", "packaging", "google"):
        src = site / extra
        if src.is_dir():
            (lib / extra).mkdir()
            _copy_tree(src, lib / extra, DROP_ANY)
    shutil.copy2(ROOT / "models" / pack.MODEL_FILE, staging / pack.MODEL_FILE)

    manifest = {
        "format": pack.PACK_FORMAT,
        "pack_version": pack.REQUIRED_PACK_VERSION,
        "python": f"{sys.version_info.major}.{sys.version_info.minor}",
        "numpy": md.version("numpy"),
        "onnxruntime": md.version("onnxruntime"),
        "model_revision": (ROOT / "model-revision.txt").read_text(encoding="utf-8").strip(),
    }
    (staging / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    zip_path = out_dir / pack.PACK_ASSET
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(staging.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(staging).as_posix())
    digest = hashlib.sha256(zip_path.read_bytes()).hexdigest()
    unpacked = pack.folder_size(staging)
    description = dict(
        manifest,
        sha256=digest,
        size_bytes=zip_path.stat().st_size,
        unpacked_bytes=unpacked,
        ram_mb=ram_mb,
    )
    json_path = out_dir / pack.PACK_MANIFEST_ASSET
    json_path.write_text(json.dumps(description, indent=2), encoding="utf-8")
    shutil.rmtree(staging)
    return zip_path, json_path


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=str(ROOT / "dist" / "pack"))
    parser.add_argument("--ram-mb", type=int, default=None, help="Measured extra RAM the pack costs, shown in the UI")
    args = parser.parse_args()
    z, j = build(Path(args.out), args.ram_mb)
    print(f"{z} ({z.stat().st_size / 1048576:.1f} MB)\n{j}")
