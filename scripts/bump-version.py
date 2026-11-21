"""Set or bump the project version in VERSION, app.py, and the Inno Setup script."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION_FILE = ROOT / "VERSION"
APP_PY = ROOT / "app.py"
ISS = ROOT / "installer" / "tinyfix.iss"

sys.path.insert(0, str(ROOT))
from update_check import bump_patch, parse_version


def read_version() -> str:
    return VERSION_FILE.read_text(encoding="utf-8").strip()


def write_version(version: str) -> None:
    normalized = str(parse_version(version))
    VERSION_FILE.write_text(normalized + "\n", encoding="utf-8")
    app = APP_PY.read_text(encoding="utf-8")
    app, count = re.subn(
        r'^APP_VERSION = "[^"]*"',
        f'APP_VERSION = "{normalized}"',
        app,
        count=1,
        flags=re.MULTILINE,
    )
    if count != 1:
        raise SystemExit("Could not update APP_VERSION in app.py")
    APP_PY.write_text(app, encoding="utf-8")
    iss = ISS.read_text(encoding="utf-8")
    iss, count = re.subn(
        r'^#define MyAppVersion "[^"]*"',
        f'#define MyAppVersion "{normalized}"',
        iss,
        count=1,
        flags=re.MULTILINE,
    )
    if count != 1:
        raise SystemExit("Could not update MyAppVersion in tinyfix.iss")
    ISS.write_text(iss, encoding="utf-8")
    print(normalized)


def main() -> int:
    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--set", dest="set_version", help="Set an exact X.Y.Z version")
    group.add_argument("--patch", action="store_true", help="Bump the patch number")
    args = parser.parse_args()
    if args.patch:
        write_version(bump_patch(read_version()))
    else:
        write_version(args.set_version)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
