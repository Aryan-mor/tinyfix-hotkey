"""Draw the tray alert frames: a white "!" on a red circle that bounces. Writes assets/tinyfix-alert-N.ico."""

from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 512  # drawn big, then scaled down for crisp 16 to 48 px icons
RED = (229, 48, 47, 255)
WHITE = (255, 255, 255, 255)
# Vertical offset of the circle as a fraction of the canvas, and its squash (1 = round): a drop, a squash, a bounce.
FRAMES = [(-0.20, 1.0), (-0.10, 1.0), (0.0, 1.0), (0.10, 0.88), (0.0, 1.0), (-0.09, 1.0), (0.0, 1.0), (0.05, 0.94)]


def frame(offset: float, squash: float) -> Image.Image:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    diameter = SIZE * 0.74
    height = diameter * squash
    cx = SIZE / 2
    cy = SIZE * 0.5 + offset * SIZE * 0.5 + (diameter - height) / 2
    left, right = cx - diameter / 2, cx + diameter / 2
    top, bottom = cy - height / 2, cy + height / 2
    draw.ellipse([left, top, right, bottom], fill=RED)
    bar_w = diameter * 0.15
    bar_top = top + height * 0.2
    bar_bottom = top + height * 0.60
    draw.rounded_rectangle([cx - bar_w / 2, bar_top, cx + bar_w / 2, bar_bottom], radius=bar_w / 2, fill=WHITE)
    dot = diameter * 0.085
    dot_cy = top + height * 0.77
    draw.ellipse([cx - dot, dot_cy - dot, cx + dot, dot_cy + dot], fill=WHITE)
    return img


def main() -> None:
    out = Path(__file__).resolve().parents[1] / "assets"
    for number, (offset, squash) in enumerate(FRAMES, start=1):
        frame(offset, squash).save(out / f"tinyfix-alert-{number}.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48)])
    print(f"wrote {len(FRAMES)} frames to {out}")


if __name__ == "__main__":
    main()
