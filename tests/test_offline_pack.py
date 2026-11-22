"""Offline pack: install, validate, remove, download. Includes the Windows file-lock and stall cases."""

from __future__ import annotations

import hashlib
import json
import re
import socketserver
import sys
import threading
import time
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import pytest

import offline_pack as op
from tests.pack_helpers import make_zip


@pytest.fixture
def pack(tmp_path):
    return tmp_path / "pack"


# ------------------------------------------------------------------ manifest checks


@pytest.mark.parametrize(
    "kwargs, fragment",
    [
        ({"python": "2.7"}, "Python"),
        ({"version": 99}, "out of date"),
        ({"fmt": 99}, "layout"),
    ],
)
def test_incompatible_manifest_is_rejected_with_readable_reason(tmp_path, pack, kwargs, fragment):
    z = make_zip(tmp_path / "p.zip", **kwargs)
    with pytest.raises(op.PackError, match=fragment):
        op.install_zip(pack, z)
    assert not (pack / "current").exists()


def test_check_manifest_none_and_garbage():
    assert op.check_manifest(None)
    assert op.check_manifest({})


def test_incomplete_packs_are_rejected(tmp_path, pack):
    with pytest.raises(op.PackError, match="incomplete"):
        op.install_zip(pack, make_zip(tmp_path / "a.zip", model=False))
    with pytest.raises(op.PackError, match="incomplete"):
        op.install_zip(pack, make_zip(tmp_path / "b.zip", lib=False))
    assert not (pack / "current").exists()
    assert not [p for p in pack.iterdir() if p.name.startswith(".tmp-")]


def test_zip_slip_is_rejected(tmp_path, pack):
    z = make_zip(tmp_path / "evil.zip", extra={"../evil.txt": "x"})
    with pytest.raises(op.PackError, match="not safe"):
        op.install_zip(pack, z)
    assert not (tmp_path / "evil.txt").exists()


def test_corrupt_zip_and_wrong_checksum(tmp_path, pack):
    bad = tmp_path / "bad.zip"
    bad.write_bytes(b"not a zip")
    with pytest.raises(op.PackError, match="damaged"):
        op.install_zip(pack, bad)
    good = make_zip(tmp_path / "good.zip")
    with pytest.raises(op.PackError, match="damaged"):
        op.install_zip(pack, good, expected_sha256="0" * 64)
    assert not (pack / "current").exists()


def test_install_ok_and_status(tmp_path, pack):
    z = make_zip(tmp_path / "p.zip")
    digest = op.sha256_file(z)
    op.install_zip(pack, z, expected_sha256=digest)
    st = op.status(pack)
    assert st.installed and st.compatible and st.reason == "Ready." and st.size_bytes > 0
    assert z.exists()  # a zip we were handed (a release file) is never deleted
    z2 = make_zip(tmp_path / "q.zip")
    op.install_zip(pack, z2, delete_source=True)
    assert not z2.exists()  # a downloaded temp file is cleaned up


def test_status_not_installed_and_broken_pack(pack):
    assert not op.status(pack).installed
    (pack / "current").mkdir(parents=True)
    st = op.status(pack)
    assert st.installed and not st.compatible


def test_reinstall_replaces_old_pack_and_failed_reinstall_keeps_it(tmp_path, pack):
    op.install_zip(pack, make_zip(tmp_path / "1.zip", extra={"lib/marker.txt": "one"}))
    op.install_zip(pack, make_zip(tmp_path / "2.zip", extra={"lib/marker.txt": "two"}))
    assert (pack / "current" / "lib" / "marker.txt").read_text() == "two"
    with pytest.raises(op.PackError):
        op.install_zip(pack, make_zip(tmp_path / "3.zip", python="1.0"))
    assert (pack / "current" / "lib" / "marker.txt").read_text() == "two"  # untouched
    assert not [p for p in pack.iterdir() if p.name.startswith((".tmp-", ".old-"))]


def test_cleanup_stale_leaves_current_alone(tmp_path, pack):
    op.install_zip(pack, make_zip(tmp_path / "p.zip"))
    (pack / ".tmp-abc").mkdir()
    (pack / ".old-def").mkdir()
    op.cleanup_stale(pack)
    assert (pack / "current").is_dir()
    assert not (pack / ".tmp-abc").exists() and not (pack / ".old-def").exists()


# ------------------------------------------------------------------ removal and Windows locks


def test_removal_is_deferred_and_then_applied(tmp_path, pack):
    op.install_zip(pack, make_zip(tmp_path / "p.zip"))
    op.request_removal(pack)
    assert op.removal_pending(pack)
    assert not op.status(pack).compatible and "pending" in op.status(pack).reason
    assert op.apply_pending_removal(pack) is True
    assert not (pack / "current").exists() and not op.removal_pending(pack)
    assert op.apply_pending_removal(pack) is False  # nothing left to do


@pytest.mark.skipif(sys.platform != "win32", reason="Windows file locks")
def test_removal_with_a_locked_file_does_not_raise_and_retries_later(tmp_path, pack):
    op.install_zip(pack, make_zip(tmp_path / "p.zip"))
    op.request_removal(pack)
    locked = open(pack / "current" / op.MODEL_FILE, "rb")  # a loaded DLL looks the same to Windows
    try:
        assert op.apply_pending_removal(pack) is False
        assert op.removal_pending(pack)  # marker kept so the next start retries
    finally:
        locked.close()
    assert op.apply_pending_removal(pack) is True
    assert not (pack / "current").exists()


@pytest.mark.skipif(sys.platform != "win32", reason="Windows file locks")
def test_reinstall_while_pack_is_in_use_gives_readable_error_and_keeps_old(tmp_path, pack):
    op.install_zip(pack, make_zip(tmp_path / "1.zip", extra={"lib/marker.txt": "one"}))
    locked = open(pack / "current" / "lib" / "marker.txt", "rb")
    try:
        with pytest.raises(op.PackError, match="in use"):
            op.install_zip(pack, make_zip(tmp_path / "2.zip", extra={"lib/marker.txt": "two"}))
    finally:
        locked.close()
    assert (pack / "current" / "lib" / "marker.txt").read_text() == "one"


def test_installing_cancels_a_pending_removal(tmp_path, pack):
    op.request_removal(pack)
    op.install_zip(pack, make_zip(tmp_path / "p.zip"))
    assert not op.removal_pending(pack)


def test_activate_checks_before_touching_sys_path(tmp_path, pack, monkeypatch):
    before = list(sys.path)
    with pytest.raises(op.PackError):
        op.activate(pack)
    assert sys.path == before


def test_activate_adds_lib_once(tmp_path, pack, monkeypatch):
    op.install_zip(pack, make_zip(tmp_path / "p.zip"))
    monkeypatch.setattr(op, "_activated", None)
    monkeypatch.setattr(sys, "path", list(sys.path))
    model = op.activate(pack)
    again = op.activate(pack)
    assert model == again == pack / "current" / op.MODEL_FILE
    assert sys.path.count(str(pack / "current" / "lib")) == 1


# ------------------------------------------------------------------ download against a local server


class _Server:
    """Serves a manifest and a zip. Supports Range. Behaviours: normal, stall, tamper, truncate."""

    def __init__(self, zip_path: Path, behaviour="normal", honour_range=True):
        data = zip_path.read_bytes()
        self.data = data
        self.sent = []
        outer = self
        manifest = dict(
            format=op.PACK_FORMAT, pack_version=op.REQUIRED_PACK_VERSION,
            python=f"{sys.version_info.major}.{sys.version_info.minor}",
            sha256=hashlib.sha256(data).hexdigest(), size_bytes=len(data),
        )

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_GET(self):
                if self.path.endswith(".json"):
                    body = json.dumps(manifest).encode()
                    self.send_response(200)
                    self.send_header("Content-Length", str(len(body)))
                    self.end_headers()
                    self.wfile.write(body)
                    return
                start = 0
                rng = self.headers.get("Range")
                if rng and honour_range:
                    start = int(re.match(r"bytes=(\d+)-", rng).group(1))
                body = outer.data[start:]
                if behaviour == "tamper":
                    body = b"Z" + body[1:]
                self.send_response(206 if start else 200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                outer.sent.append(start)
                try:
                    if behaviour == "stall":
                        self.wfile.write(body[:100])
                        self.wfile.flush()
                        time.sleep(5)
                    elif behaviour == "truncate":
                        self.wfile.write(body[: len(body) // 2])
                        self.wfile.flush()
                        self.connection.close()
                    else:
                        self.wfile.write(body)
                except OSError:
                    pass

        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.httpd.daemon_threads = True
        self.url = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def close(self):
        self.httpd.shutdown()
        self.httpd.server_close()


@pytest.fixture
def served(tmp_path):
    made = []

    def start(**kw):
        z = make_zip(tmp_path / "served.zip", big=200_000)
        s = _Server(z, **kw)
        made.append(s)
        return s

    yield start
    for s in made:
        s.close()


def test_download_installs_and_reports_progress(served, pack):
    s = served()
    seen = []
    op.download_pack(pack, base_url=s.url, progress=lambda done, total: seen.append((done, total)))
    assert op.status(pack).compatible
    assert seen and seen[-1][0] == seen[-1][1]
    assert not (pack / ".download.part").exists()


def test_download_rejects_tampered_file_and_installs_nothing(served, pack):
    s = served(behaviour="tamper")
    with pytest.raises(op.PackError, match="damaged"):
        op.download_pack(pack, base_url=s.url)
    assert not (pack / "current").exists()
    assert not (pack / ".download.part").exists()  # bad partial is discarded, not resumed forever


def test_download_resumes_from_partial_file(served, pack):
    s = served()
    pack.mkdir()
    (pack / ".download.part").write_bytes(s.data[:50_000])
    op.download_pack(pack, base_url=s.url)
    assert op.status(pack).compatible
    assert s.sent == [50_000]  # only the rest was requested


def test_download_restarts_when_server_ignores_range(served, pack):
    s = served(honour_range=False)
    pack.mkdir()
    (pack / ".download.part").write_bytes(s.data[:50_000])
    op.download_pack(pack, base_url=s.url)
    assert op.status(pack).compatible


def test_download_server_down_fails_fast_with_readable_error(pack):
    t0 = time.time()
    with pytest.raises(op.PackError, match="internet|reach"):
        op.download_pack(pack, base_url="http://127.0.0.1:9")  # nothing listens here
    assert time.time() - t0 < 10


def test_download_stall_times_out_instead_of_hanging(served, pack, monkeypatch):
    s = served(behaviour="stall")
    monkeypatch.setattr(op, "READ_TIMEOUT", 0.5)
    t0 = time.time()
    with pytest.raises(op.PackError):
        op.download_pack(pack, base_url=s.url)
    assert time.time() - t0 < 4
    assert not (pack / "current").exists()


def test_download_cut_connection_keeps_partial_for_resume(served, pack):
    s = served(behaviour="truncate")
    with pytest.raises(op.PackError):
        op.download_pack(pack, base_url=s.url)
    assert not (pack / "current").exists()
    part = pack / ".download.part"
    assert part.exists() and 0 < part.stat().st_size < len(s.data)


def test_download_can_be_cancelled(served, pack):
    s = served()
    cancel = threading.Event()
    cancel.set()
    with pytest.raises(op.PackError, match="cancelled"):
        op.download_pack(pack, base_url=s.url, cancel=cancel)
    assert not (pack / "current").exists()


def test_remote_manifest_for_wrong_python_is_refused(tmp_path, monkeypatch):
    z = make_zip(tmp_path / "x.zip")
    s = _Server(z)
    try:
        s.data = s.data  # the served manifest describes this Python, so it is accepted
        assert op.fetch_remote_manifest(s.url)["python"] == f"{sys.version_info.major}.{sys.version_info.minor}"
    finally:
        s.close()
    assert op.check_manifest({"format": op.PACK_FORMAT, "pack_version": op.REQUIRED_PACK_VERSION, "python": "0.0"})


def test_pack_dir_override_via_environment(tmp_path, monkeypatch):
    monkeypatch.setenv("TINYFIX_PACK_DIR", str(tmp_path / "elsewhere"))
    assert op.pack_root(tmp_path) == tmp_path / "elsewhere"
    monkeypatch.delenv("TINYFIX_PACK_DIR")
    assert op.pack_root(tmp_path) == tmp_path / "pack"
