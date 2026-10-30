"""Contact sheets for reviewing assets: scaled-up pixel art with captions on the UI's navy background."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence

from PIL import Image, ImageDraw, ImageFont

BG = (13, 15, 26, 255)
PANEL = (24, 27, 48, 255)
EDGE = (230, 189, 92, 255)
INK = (244, 236, 214, 255)
MUTED = (160, 152, 128, 255)

_FONTS = "/usr/share/fonts/truetype/dejavu/"


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    try:
        return ImageFont.truetype(_FONTS + ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"), size)
    except OSError:
        return ImageFont.load_default(size)


def scale(img: Image.Image, k: int) -> Image.Image:
    return img.resize((img.width * k, img.height * k), Image.NEAREST)


@dataclass
class Tile:
    image: Image.Image
    label: str = ""
    sub: str = ""


def contact_sheet(title: str, tiles: Sequence[Tile], *, columns: int, zoom: int = 4, subtitle: str = "",
                  cell_bg=PANEL, checker: bool = False, label_h: int = 34) -> Image.Image:
    """Lays tiles out in a grid; every image is scaled by `zoom` (nearest) and centred in a uniform cell."""
    cw = max(t.image.width for t in tiles) * zoom + 24
    ch = max(t.image.height for t in tiles) * zoom + 24
    rows = -(-len(tiles) // columns)
    head = 86 if subtitle else 62
    W = columns * (cw + 14) + 34
    H = head + rows * (ch + label_h + 14) + 20
    out = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(out)
    d.text((22, 18), title, font=font(26, True), fill=INK)
    if subtitle:
        d.text((22, 54), subtitle, font=font(15), fill=MUTED)
    f_label, f_sub = font(14, True), font(12)
    for i, t in enumerate(tiles):
        x = 20 + (i % columns) * (cw + 14)
        y = head + (i // columns) * (ch + label_h + 14)
        d.rounded_rectangle((x, y, x + cw, y + ch), 8, fill=cell_bg, outline=(60, 64, 96, 255))
        if checker:
            for cy in range(y + 4, y + ch - 4, 12):
                for cx in range(x + 4, x + cw - 4, 12):
                    if ((cx - x) // 12 + (cy - y) // 12) % 2 == 0:
                        d.rectangle((cx, cy, min(cx + 11, x + cw - 5), min(cy + 11, y + ch - 5)), fill=(32, 36, 60, 255))
        im = scale(t.image, zoom)
        out.alpha_composite(im, (x + (cw - im.width) // 2, y + (ch - im.height) // 2))
        if t.label:
            d.text((x + 2, y + ch + 4), t.label, font=f_label, fill=INK)
        if t.sub:
            d.text((x + 2, y + ch + 20), t.sub, font=f_sub, fill=MUTED)
    return out


def strip(images: Sequence[Image.Image], zoom: int, gap: int = 2, bg=(0, 0, 0, 0)) -> Image.Image:
    """Frames side by side at `zoom` — animation strips inside a contact-sheet cell."""
    w, h = images[0].size
    out = Image.new("RGBA", (len(images) * (w + gap) - gap, h), bg)
    for i, im in enumerate(images):
        out.alpha_composite(im, (i * (w + gap), 0))
    return out if zoom == 1 else scale(out, zoom)
