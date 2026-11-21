import sys
from pathlib import Path

import pytest

from secret_store import load_api_key, save_api_key, protect_bytes, unprotect_bytes

pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="DPAPI is Windows-only")


def test_dpapi_round_trip_bytes():
    secret = "sk-test-not-a-real-key-123".encode("utf-8")
    protected = protect_bytes(secret)
    assert protected != secret
    assert b"sk-test-not-a-real-key-123" not in protected
    assert unprotect_bytes(protected) == secret


def test_dpapi_file_round_trip_and_clear(tmp_path: Path):
    path = tmp_path / "secrets.dpapi"
    save_api_key(path, "gemini-test-key")
    assert path.exists()
    raw = path.read_bytes()
    assert b"gemini-test-key" not in raw
    assert load_api_key(path) == "gemini-test-key"
    save_api_key(path, "")
    assert not path.exists()
    assert load_api_key(path) == ""
