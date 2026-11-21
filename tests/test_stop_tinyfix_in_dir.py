import json
import os
import subprocess
import sys
import time
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "stop-tinyfix-in-dir.ps1"
ISS = ROOT / "installer" / "tinyfix.iss"
POWERSHELL = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"


def test_iss_never_uses_machine_wide_taskkill():
    text = ISS.read_text(encoding="utf-8")
    lowered = text.lower()
    assert "/im tinyfix.exe" not in lowered
    assert "taskkill.exe" not in lowered
    assert "stop-tinyfix-in-dir.ps1" in text
    assert "StopTinyFixInApp" in text


@pytest.mark.skipif(sys.platform != "win32", reason="Windows path-prefix checks")
@pytest.mark.parametrize(
    ("app_dir", "process_path", "expected"),
    [
        (r"D:\Temp\tfx-app", r"D:\Temp\tfx-app\TinyFix.exe", True),
        ("D:\\Temp\\tfx-app\\", r"D:\Temp\tfx-app\TinyFix.exe", True),
        (r"D:\Temp\tfx-app", r"D:\Temp\tfx-app\sub\TinyFix.exe", True),
        (r"D:\Temp\tfx-app", r"D:\Temp\tfx-app-other\TinyFix.exe", False),
        (r"D:\Temp\tfx-app", r"D:\tools\TinyFix Hotkey\TinyFix.exe", False),
        (r"D:\Temp\tfx-app", "", False),
    ],
)
def test_path_prefix_matching(app_dir: str, process_path: str, expected: bool):
    result = subprocess.run(
        [
            str(POWERSHELL),
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(SCRIPT),
            "-AppDir",
            app_dir,
            "-PathCheck",
            process_path,
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    assert result.stdout.strip().lower() == ("true" if expected else "false")


def _build_dummy_tinyfix(exe: Path) -> None:
    exe.parent.mkdir(parents=True, exist_ok=True)
    source = "using System.Threading; class P { static void Main() { Thread.Sleep(120000); } }"
    result = subprocess.run(
        [
            str(POWERSHELL),
            "-NoProfile",
            "-Command",
            "Add-Type -OutputAssembly $env:TFX_OUT -OutputType ConsoleApplication -TypeDefinition $env:TFX_SRC",
        ],
        capture_output=True,
        text=True,
        env={**os.environ, "TFX_OUT": str(exe), "TFX_SRC": source},
    )
    if result.returncode != 0 or not exe.exists():
        raise RuntimeError((result.stdout or "") + "\n" + (result.stderr or ""))


def _start_dummy_tinyfix(folder: Path) -> subprocess.Popen:
    exe = folder / "TinyFix.exe"
    _build_dummy_tinyfix(exe)
    return subprocess.Popen(
        [str(exe)],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )


def _still_running(proc: subprocess.Popen) -> bool:
    return proc.poll() is None


def _user_tinyfix_pids() -> set[int]:
    """PIDs of a TinyFix running from the user's D:\\tools install, if any.

    PowerShell exits with code 1 when Get-Process finds no match, even with
    SilentlyContinue, so this never uses check=True.
    """
    result = subprocess.run(
        [
            str(POWERSHELL),
            "-NoProfile",
            "-Command",
            "$ErrorActionPreference = 'SilentlyContinue'; "
            "Get-Process -Name TinyFix | "
            "Where-Object { $_.Path -like '*\\tools\\TinyFix Hotkey\\*' } | "
            "ForEach-Object { $_.Id }; exit 0",
        ],
        capture_output=True,
        text=True,
    )
    return {int(line.strip()) for line in result.stdout.splitlines() if line.strip().isdigit()}


@pytest.mark.skipif(sys.platform != "win32", reason="Windows process stop")
def test_stops_only_tinyfix_in_app_dir(tmp_path: Path):
    user_pids_before = _user_tinyfix_pids()

    dir_a = tmp_path / "app_a"
    dir_b = tmp_path / "app_b"
    proc_a = _start_dummy_tinyfix(dir_a)
    proc_b = _start_dummy_tinyfix(dir_b)
    try:
        time.sleep(0.4)
        assert _still_running(proc_a) and _still_running(proc_b)

        listed = subprocess.check_output(
            [
                str(POWERSHELL),
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(SCRIPT),
                "-AppDir",
                str(dir_a),
                "-ListOnly",
            ],
            text=True,
        ).strip()
        listed_ids = {item["Id"] for item in json.loads(listed or "[]")}
        assert proc_a.pid in listed_ids
        assert proc_b.pid not in listed_ids

        subprocess.run(
            [
                str(POWERSHELL),
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(SCRIPT),
                "-AppDir",
                str(dir_a),
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        for _ in range(20):
            if not _still_running(proc_a):
                break
            time.sleep(0.1)
        assert not _still_running(proc_a)
        assert _still_running(proc_b)

        if user_pids_before:
            still = _user_tinyfix_pids()
            assert user_pids_before <= still
    finally:
        for proc in (proc_a, proc_b):
            if _still_running(proc):
                proc.kill()
                proc.wait(timeout=5)
