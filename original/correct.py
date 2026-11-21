"""CLI: correct one or more English texts with tinyfix. Usage: correct.py [text ...]"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from tinyfix_lib import load_tinyfix

ROOT = Path(__file__).resolve().parent
MODELS = ROOT / "models"


def main() -> int:
    texts = sys.argv[1:]
    if not texts:
        texts = [sys.stdin.read()]
        if not texts[0]:
            print("Usage: correct.py <text>", file=sys.stderr)
            return 2
    engine = load_tinyfix(MODELS)
    results = []
    for text in texts:
        fixed = engine.fix(text)
        fixes = [
            {
                "from": item.start,
                "to": item.end,
                "original": item.original,
                "replacement": item.replacement,
                "kind": item.kind,
                "confidence": round(item.confidence, 4),
            }
            for item in engine.check(text)
        ]
        results.append({"input": text, "output": fixed, "fixes": fixes, "changed": fixed != text})
        if len(texts) == 1 and sys.stdout.isatty():
            print(fixed)
            for item in fixes:
                print(f"  [{item['kind']}] {item['original']} -> {item['replacement']}", file=sys.stderr)
        elif len(texts) == 1:
            print(fixed)
    if len(texts) > 1:
        json.dump(results, sys.stdout, ensure_ascii=False, indent=2)
        print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
