"""Create assets/tinyfix.ico from a simple checkbox mark."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets" / "tinyfix.ico"


def make_icon(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    pad = max(1, size // 32)
    radius = max(2, size // 5)
    draw.rounded_rectangle((pad, pad, size - pad - 1, size - pad - 1), radius=radius, fill=(18, 92, 68, 255))
    width = max(2, size // 12)
    start = (int(size * 0.25), int(size * 0.52))
    mid = (int(size * 0.44), int(size * 0.72))
    end = (int(size * 0.76), int(size * 0.28))
    draw.line((start, mid, end), fill=(255, 255, 255, 255), width=width, joint="curve")
    return image


def main() -> int:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    base = make_icon(256)
    base.save(OUT, format="ICO", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    print(f"Wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
