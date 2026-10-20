#!/usr/bin/env python3
"""Generates the notification-area icons from the brand artwork in artwork/Branding.

    python3 -m venv .venv && .venv/bin/pip install -r scripts/ci/requirements-icons.txt
    .venv/bin/python scripts/ci/make-icons.py            # needs rsvg-convert on PATH (librsvg)

Writes src/TimeTug.App/Assets/Tray/tray-{light,dark,color,attention}.ico. Each file holds the puppy at 16, 20, 24 and 32 px,
the notification area's icon sizes at 100, 125, 150 and 200 percent display scaling, so Windows picks the entry that matches
without resampling.

  light      quiet puppy for a light taskbar (dark ink)        from timetug-menubar-puppy-light.svg
  dark       quiet puppy for a dark taskbar (light ink)        from timetug-menubar-puppy-dark.svg
  color      full-colour puppy, a meeting is near              from timetug-menubar-color-master.png
  attention  the colour puppy with a badge, the person is needed

The Mac menu-bar icons use the same artwork. The output is committed; run this only when the artwork changes. The icons are
brand artwork, covered by artwork/LICENSE.md.
"""
import io
import shutil
import struct
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
BRANDING = ROOT / "artwork" / "Branding"
OUT = ROOT / "src" / "TimeTug.App" / "Assets" / "Tray"
SIZES = (16, 20, 24, 32)
MASTER = 1024  # every variant is drawn this large and reduced once, so all sizes come from the same picture
BADGE = (229, 72, 77, 255)
BADGE_RING = (255, 255, 255, 255)


def render_svg(name: str) -> Image.Image:
    exe = shutil.which("rsvg-convert")
    if exe is None:
        sys.exit("rsvg-convert (librsvg) is required to render the SVG artwork")
    data = subprocess.run([exe, "-w", str(MASTER), "-h", str(MASTER), str(BRANDING / name)], check=True, capture_output=True).stdout
    return Image.open(io.BytesIO(data)).convert("RGBA")


def remove_halo(image: Image.Image) -> Image.Image:
    """The colour master carries a translucent whitish halo around the puppy; it shows as a box on a dark taskbar. Drop it."""
    cleaned = image.copy()
    pixels = cleaned.load()
    for y in range(cleaned.height):
        for x in range(cleaned.width):
            r, g, b, a = pixels[x, y]
            if a < 250 and min(r, g, b) > 200:
                pixels[x, y] = (0, 0, 0, 0)
    return cleaned


def fill_canvas(image: Image.Image) -> Image.Image:
    """Crops to the drawn pixels and centres them on a square, so every variant fills its icon the same way."""
    box = image.getchannel("A").getbbox()
    if box is None:
        sys.exit("empty artwork")
    drawn = image.crop(box)
    side = max(drawn.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(drawn, ((side - drawn.width) // 2, (side - drawn.height) // 2))
    return square.resize((MASTER, MASTER), Image.LANCZOS)


def with_badge(image: Image.Image) -> Image.Image:
    """A red dot with a white ring at the lower right, large enough to read at 16 px."""
    canvas = image.copy()
    draw = ImageDraw.Draw(canvas)
    radius = int(MASTER * 0.17)
    cx = MASTER - radius - int(MASTER * 0.01)
    cy = MASTER - radius - int(MASTER * 0.01)
    ring = int(MASTER * 0.04)
    draw.ellipse((cx - radius - ring, cy - radius - ring, cx + radius + ring, cy + radius + ring), fill=BADGE_RING)
    draw.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), fill=BADGE)
    return canvas


def write_ico(path: Path, master: Image.Image) -> None:
    pngs = []
    for size in SIZES:
        buffer = io.BytesIO()
        master.resize((size, size), Image.LANCZOS).save(buffer, "PNG", optimize=True)
        pngs.append((size, buffer.getvalue()))
    header = struct.pack("<HHH", 0, 1, len(pngs))
    offset = len(header) + 16 * len(pngs)
    entries = b""
    for size, data in pngs:
        entries += struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(header + entries + b"".join(data for _, data in pngs))


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    color = fill_canvas(remove_halo(Image.open(BRANDING / "timetug-menubar-color-master.png").convert("RGBA")))
    variants = {
        "light": fill_canvas(render_svg("timetug-menubar-puppy-light.svg")),
        "dark": fill_canvas(render_svg("timetug-menubar-puppy-dark.svg")),
        "color": color,
        "attention": with_badge(color),
    }
    for name, master in variants.items():
        write_ico(OUT / f"tray-{name}.ico", master)
        print(f"wrote {OUT / f'tray-{name}.ico'}")


if __name__ == "__main__":
    main()
