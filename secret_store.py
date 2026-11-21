"""Store API keys encrypted with Windows DPAPI (CryptProtectData)."""

from __future__ import annotations

import ctypes
import os
import sys
from ctypes import wintypes
from pathlib import Path

CRYPTPROTECT_UI_FORBIDDEN = 0x1
DESCRIPTION = "TinyFix Hotkey"

if sys.platform == "win32":
    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    class DATA_BLOB(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_byte))]

    crypt32.CryptProtectData.argtypes = [
        ctypes.POINTER(DATA_BLOB),
        wintypes.LPCWSTR,
        ctypes.POINTER(DATA_BLOB),
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(DATA_BLOB),
    ]
    crypt32.CryptProtectData.restype = wintypes.BOOL
    crypt32.CryptUnprotectData.argtypes = [
        ctypes.POINTER(DATA_BLOB),
        ctypes.POINTER(wintypes.LPWSTR),
        ctypes.POINTER(DATA_BLOB),
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(DATA_BLOB),
    ]
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
    kernel32.LocalFree.restype = wintypes.HLOCAL
else:
    DATA_BLOB = None  # type: ignore
    crypt32 = None
    kernel32 = None


def _blob_from_bytes(data: bytes) -> "DATA_BLOB":
    blob = DATA_BLOB()
    blob.cbData = len(data)
    blob.pbData = (ctypes.c_byte * len(data)).from_buffer_copy(data)
    return blob


def _bytes_from_blob(blob: "DATA_BLOB") -> bytes:
    return ctypes.string_at(blob.pbData, blob.cbData)


def protect_bytes(data: bytes) -> bytes:
    if sys.platform != "win32":
        raise RuntimeError("API keys can only be stored with Windows DPAPI")
    incoming = _blob_from_bytes(data)
    outgoing = DATA_BLOB()
    if not crypt32.CryptProtectData(
        ctypes.byref(incoming),
        DESCRIPTION,
        None,
        None,
        None,
        CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(outgoing),
    ):
        raise OSError(f"CryptProtectData failed (err={ctypes.get_last_error()})")
    try:
        return _bytes_from_blob(outgoing)
    finally:
        if outgoing.pbData:
            kernel32.LocalFree(outgoing.pbData)


def unprotect_bytes(data: bytes) -> bytes:
    if sys.platform != "win32":
        raise RuntimeError("API keys can only be stored with Windows DPAPI")
    incoming = _blob_from_bytes(data)
    outgoing = DATA_BLOB()
    if not crypt32.CryptUnprotectData(
        ctypes.byref(incoming),
        None,
        None,
        None,
        None,
        CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(outgoing),
    ):
        raise OSError(f"CryptUnprotectData failed (err={ctypes.get_last_error()})")
    try:
        return _bytes_from_blob(outgoing)
    finally:
        if outgoing.pbData:
            kernel32.LocalFree(outgoing.pbData)


def default_secrets_path(root: Path) -> Path:
    return Path(root) / "secrets.dpapi"



def _restrict_to_current_user(path: Path) -> None:
    """Best-effort: stop the encrypted key file being inherited by Everyone."""
    if sys.platform != "win32":
        return
    try:
        import subprocess

        subprocess.run(
            ["icacls", str(path), "/inheritance:r", "/grant:r", f"{os.environ.get('USERNAME', '')}:(R,W)"],
            check=False,
            capture_output=True,
            text=True,
        )
    except Exception:
        pass


def save_api_key(path: Path, api_key: str) -> None:
    path = Path(path)
    if not api_key:
        if path.exists():
            path.unlink()
        return
    protected = protect_bytes(api_key.encode("utf-8"))
    path.write_bytes(protected)
    _restrict_to_current_user(path)


def load_api_key(path: Path) -> str:
    path = Path(path)
    if not path.exists():
        return ""
    try:
        return unprotect_bytes(path.read_bytes()).decode("utf-8")
    except Exception:
        return ""
