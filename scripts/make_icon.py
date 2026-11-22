"""Create assets/tinyfix.ico from a simple checkbox mark, plus three "working" frames with three dots.

While TinyFix is fixing or translating, the tray cycles through the frames: the tick turns into three dots and
the bright dot moves along them, so you can see it is working."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets" / "tinyfix.ico"
WORKING_OUT = tuple(ROOT / "assets" / f"tinyfix-working-{n}.ico" for n in (1, 2, 3))
SIZES = [(s, s) for s in (16, 24, 32, 48, 64, 128, 256)]
DOT_COUNT = 3
BADGE_COLORS = ((34, 197, 94, 255), (134, 239, 172, 255))


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


def make_working_icon(size: int, active: int) -> Image.Image:
    """The green tile with three dots instead of the tick. Dot number `active` is bright and bigger."""
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    pad = max(1, size // 32)
    radius = max(2, size // 5)
    draw.rounded_rectangle((pad, pad, size - pad - 1, size - pad - 1), radius=radius, fill=(18, 92, 68, 255))
    dot = max(1.6, size * 0.085)
    gap = size * 0.25
    cy = size * 0.5
    for index in range(DOT_COUNT):
        cx = size * 0.5 + (index - (DOT_COUNT - 1) / 2) * gap
        is_active = index == active
        r = dot * (1.45 if is_active else 1.0)
        color = (255, 255, 255, 255) if is_active else (126, 178, 158, 255)  # solid: ImageDraw does not blend alpha
        draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=color)
    return image


def main() -> int:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    base = make_icon(256)
    base.save(OUT, format="ICO", sizes=SIZES)
    for active, path in enumerate(WORKING_OUT):
        make_working_icon(256, active).save(path, format="ICO", sizes=SIZES)
        print(f"Wrote {path}")
    print(f"Wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
