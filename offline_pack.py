"""Optional offline pack: numpy + onnxruntime + the wrong-word model, installed outside the exe.

The base app is dictionary-only and small. The pack adds context-aware wrong-word detection.
Layout under <pack_root>/current/: manifest.json, lib/ (numpy, numpy.libs, onnxruntime), wrong-word.onnx.

Windows lock rules this module follows:
  * a loaded DLL cannot be deleted, so removal is a marker file that is applied at next start,
    before anything imports the pack;
  * a download goes to a temp dir, is checked, then renamed into place in one step, so a partial
    pack is never imported;
  * nothing here runs on the UI thread except cheap file checks.
"""

from __future__ import annotations

import hashlib
import http.client
import json
import logging
import os
import shutil
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

PACK_FORMAT = 1  # layout of the zip; bump when the folder layout changes
REQUIRED_PACK_VERSION = 1  # bump when the model/vocab or lib set changes and users must re-download
PACK_TAG = "offline-pack"
PACK_REPO = "Aryan-mor/tinyfix-hotkey"
PACK_ASSET = f"TinyFix-OfflinePack-{REQUIRED_PACK_VERSION}-cp{sys.version_info.major}{sys.version_info.minor}.zip"
PACK_MANIFEST_ASSET = PACK_ASSET + ".json"
PACK_BASE_URL = f"https://github.com/{PACK_REPO}/releases/download/{PACK_TAG}/"
USER_AGENT = "TinyFix-Hotkey-Pack"
# Shown in the UI before the exact numbers are known. Measured on the frozen build:
# download 24.6 MB, 77 MB on disk, base app 55 MB private -> 92 MB private with the pack.
APPROX_DOWNLOAD_MB = 25
APPROX_DISK_MB = 77
APPROX_RAM_MB = 40
CONNECT_TIMEOUT = 20.0
READ_TIMEOUT = 30.0
REMOVE_MARKER = "REMOVE"
MODEL_FILE = "wrong-word.onnx"

ProgressFn = Callable[[int, int], None]


class PackError(Exception):
    """Readable message for the UI; never contains secrets."""


@dataclass(frozen=True)
class PackStatus:
    installed: bool
    compatible: bool
    active: bool
    reason: str
    pack_version: int | None
    size_bytes: int


def pack_root(root: Path | None = None) -> Path:
    override = os.environ.get("TINYFIX_PACK_DIR")
    if override:
        return Path(override)
    if root is None:
        raise ValueError("pack_root needs the app root")
    return Path(root) / "pack"


def current_dir(pack: Path) -> Path:
    return Path(pack) / "current"


def _python_tag() -> str:
    return f"{sys.version_info.major}.{sys.version_info.minor}"


def read_manifest(folder: Path) -> dict | None:
    try:
        return json.loads((Path(folder) / "manifest.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def check_manifest(manifest: dict | None) -> str | None:
    """Return None when the pack fits this app, else a short reason."""
    if not manifest:
        return "The pack has no readable manifest."
    if manifest.get("format") != PACK_FORMAT:
        return "The pack was made for a different TinyFix layout."
    if manifest.get("python") != _python_tag():
        return f"The pack is for Python {manifest.get('python')}, this app uses {_python_tag()}."
    if int(manifest.get("pack_version", 0)) != REQUIRED_PACK_VERSION:
        return "The pack is out of date. Download the new one in Settings."
    return None


def folder_size(path: Path) -> int:
    total = 0
    for base, _dirs, files in os.walk(path):
        for name in files:
            try:
                total += (Path(base) / name).stat().st_size
            except OSError:
                pass
    return total


def removal_pending(pack: Path) -> bool:
    return (Path(pack) / REMOVE_MARKER).exists()


def apply_pending_removal(pack: Path) -> bool:
    """Delete the pack if a removal was requested. Must run before the pack is imported."""
    pack = Path(pack)
    if not removal_pending(pack):
        return False
    try:
        shutil.rmtree(current_dir(pack), ignore_errors=False)
    except FileNotFoundError:
        pass
    except OSError as exc:
        logging.warning("Pack removal is still pending: %s", exc)
        return False
    try:
        (pack / REMOVE_MARKER).unlink()
    except OSError:
        pass
    return True


def request_removal(pack: Path) -> None:
    pack = Path(pack)
    pack.mkdir(parents=True, exist_ok=True)
    (pack / REMOVE_MARKER).write_text("remove", encoding="utf-8")


def cancel_removal(pack: Path) -> None:
    try:
        (Path(pack) / REMOVE_MARKER).unlink()
    except OSError:
        pass


def cleanup_stale(pack: Path) -> None:
    """Remove temp/old folders left by an interrupted install. Never touches current/."""
    pack = Path(pack)
    if not pack.is_dir():
        return
    for child in pack.iterdir():
        if child.name.startswith((".tmp-", ".old-")):
            shutil.rmtree(child, ignore_errors=True)


def status(pack: Path, active: bool = False) -> PackStatus:
    cur = current_dir(pack)
    if removal_pending(pack):
        return PackStatus(False, False, active, "Removal is pending; it finishes when TinyFix restarts.", None, 0)
    if not cur.is_dir():
        return PackStatus(False, False, False, "Not installed.", None, 0)
    manifest = read_manifest(cur)
    reason = check_manifest(manifest)
    model_ok = (cur / MODEL_FILE).is_file()
    if reason is None and not model_ok:
        reason = "The model file is missing."
    version = int(manifest.get("pack_version", 0)) if manifest else None
    return PackStatus(True, reason is None, active, reason or "Ready.", version, folder_size(cur))


_activated: Path | None = None
_dll_handles: list[object] = []


def activate(pack: Path) -> Path:
    """Make numpy/onnxruntime importable from the pack. Returns the model path. Raises PackError."""
    global _activated
    cur = current_dir(pack)
    reason = check_manifest(read_manifest(cur))
    if reason:
        raise PackError(reason)
    model = cur / MODEL_FILE
    lib = cur / "lib"
    if not model.is_file() or not lib.is_dir():
        raise PackError("The pack is incomplete. Download it again in Settings.")
    if _activated is None:
        sys.path.insert(0, str(lib))
        if hasattr(os, "add_dll_directory"):
            for folder in (lib, lib / "numpy.libs", lib / "onnxruntime" / "capi"):
                if folder.is_dir():
                    try:
                        _dll_handles.append(os.add_dll_directory(str(folder)))
                    except OSError:
                        pass
        _activated = lib
    return model


def is_active() -> bool:
    return _activated is not None


# ---------------------------------------------------------------- download / install


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def fetch_remote_manifest(base_url: str | None = None, timeout: float = CONNECT_TIMEOUT) -> dict:
    url = (base_url or os.environ.get("TINYFIX_PACK_URL") or PACK_BASE_URL).rstrip("/") + "/" + PACK_MANIFEST_ASSET
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            data = json.loads(response.read(1024 * 256).decode("utf-8"))
    except (urllib.error.URLError, OSError, ValueError, http.client.HTTPException) as exc:
        raise PackError(f"Could not reach the download page ({exc.__class__.__name__}). Check your internet and try again.") from exc
    reason = check_manifest(data)
    if reason:
        raise PackError(reason)
    if not data.get("sha256") or not data.get("size_bytes"):
        raise PackError("The download description is incomplete.")
    return data


def download_pack(
    pack: Path,
    base_url: str | None = None,
    progress: ProgressFn | None = None,
    cancel: threading.Event | None = None,
    opener: Callable | None = None,
) -> Path:
    """Download, verify and unpack the pack into <pack>/current. Blocking: call from a worker thread."""
    pack = Path(pack)
    pack.mkdir(parents=True, exist_ok=True)
    cleanup_stale(pack)
    base = (base_url or os.environ.get("TINYFIX_PACK_URL") or PACK_BASE_URL).rstrip("/")
    manifest = fetch_remote_manifest(base)
    expected = str(manifest["sha256"]).lower()
    total = int(manifest["size_bytes"])
    part = pack / ".download.part"
    open_url = opener or urllib.request.urlopen

    have = part.stat().st_size if part.exists() else 0
    if have > total:
        part.unlink()
        have = 0
    if have < total:
        headers = {"User-Agent": USER_AGENT}
        if have:
            headers["Range"] = f"bytes={have}-"
        request = urllib.request.Request(f"{base}/{PACK_ASSET}", headers=headers)
        try:
            response = open_url(request, timeout=READ_TIMEOUT)
        except (urllib.error.URLError, OSError, http.client.HTTPException) as exc:
            raise PackError(f"Download failed ({exc.__class__.__name__}). Check your internet and try again.") from exc
        with response:
            status_code = getattr(response, "status", 200)
            if have and status_code != 206:
                have = 0  # server ignored Range: start over
            mode = "ab" if have else "wb"
            try:
                with open(part, mode) as handle:
                    done = have
                    while True:
                        if cancel is not None and cancel.is_set():
                            raise PackError("Download cancelled.")
                        block = response.read(256 * 1024)
                        if not block:
                            break
                        handle.write(block)
                        done += len(block)
                        if progress:
                            progress(done, total)
            except PackError:
                raise
            except (OSError, http.client.HTTPException) as exc:
                raise PackError(f"Download interrupted ({exc.__class__.__name__}). Try again to resume.") from exc
    if part.stat().st_size < total:
        raise PackError("Download interrupted. Try again to resume.")  # keep the partial file for resume
    if part.stat().st_size != total or sha256_file(part) != expected:
        try:
            part.unlink()
        except OSError:
            pass
        raise PackError("The downloaded file is damaged. Try again.")
    return install_zip(pack, part, expected_sha256=expected, delete_source=True)


def install_zip(pack: Path, zip_path: Path, expected_sha256: str | None = None, delete_source: bool = False) -> Path:
    """Unpack a verified zip into a temp folder, validate it, then swap it into place.

    delete_source removes the zip afterwards; only downloads (our own .part file) ask for that."""
    pack = Path(pack)
    pack.mkdir(parents=True, exist_ok=True)
    if expected_sha256 and sha256_file(zip_path) != expected_sha256.lower():
        raise PackError("The pack file is damaged.")
    temp = pack / f".tmp-{uuid.uuid4().hex[:8]}"
    try:
        with zipfile.ZipFile(zip_path) as archive:
            root = temp.resolve()
            for member in archive.namelist():
                target = (temp / member).resolve()
                if root != target and root not in target.parents:
                    raise PackError("The pack file is not safe to unpack.")
            archive.extractall(temp)
        reason = check_manifest(read_manifest(temp))
        if reason:
            raise PackError(reason)
        if not (temp / MODEL_FILE).is_file() or not (temp / "lib").is_dir():
            raise PackError("The pack file is incomplete.")
        cur = current_dir(pack)
        old = pack / f".old-{uuid.uuid4().hex[:8]}"
        if cur.exists():
            try:
                cur.rename(old)
            except OSError as exc:
                raise PackError("The current pack is in use. Restart TinyFix, then try again.") from exc
        try:
            temp.rename(cur)
        except OSError:
            if old.exists():
                old.rename(cur)  # roll back
            raise PackError("Could not put the pack in place.")
        shutil.rmtree(old, ignore_errors=True)
        cancel_removal(pack)
        if delete_source:
            try:
                Path(zip_path).unlink()
            except OSError:
                pass
        return cur
    except zipfile.BadZipFile as exc:
        raise PackError("The pack file is damaged.") from exc
    finally:
        if temp.exists():
            shutil.rmtree(temp, ignore_errors=True)


def format_mb(size_bytes: int) -> str:
    return f"{size_bytes / (1024 * 1024):.0f} MB"
