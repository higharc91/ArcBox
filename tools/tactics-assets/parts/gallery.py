"""previews/00-gallery.png: one page showing every set's main preview as a thumbnail, with a count of what the pack
holds. It runs last and only reads other sets' outputs."""
from __future__ import annotations

from PIL import Image, ImageDraw

from arcpix.out import PACK, PREVIEWS, save
from arcpix.preview import BG, EDGE, INK, MUTED, PANEL, font

THUMB_W = 360


def build() -> None:
    files = sorted(p for p in PREVIEWS.glob("[0-9][0-9]*.png") if not p.name.startswith("00-"))
    thumbs = []
    for p in files:
        im = Image.open(p).convert("RGBA")
        h = int(im.height * THUMB_W / im.width)
        thumbs.append((p.stem, im.resize((THUMB_W, min(h, 420)), Image.LANCZOS) if h <= 420 else
                       im.crop((0, 0, im.width, int(420 * im.width / THUMB_W))).resize((THUMB_W, 420),
                                                                                         Image.LANCZOS)))
    cols = 5
    pad, label = 16, 26
    row_h = []
    for r in range(0, len(thumbs), cols):
        row_h.append(max(t.height for _, t in thumbs[r:r + cols]) + label)
    head = 110
    W = cols * (THUMB_W + pad) + pad
    H = head + sum(h + pad for h in row_h) + pad
    out = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(out)
    pngs = list(PACK.rglob("*.png"))
    sets = sorted({p.relative_to(PACK).parts[0] for p in pngs})
    size = sum(f.stat().st_size for f in PACK.rglob("*") if f.is_file()) / 1e6
    d.text((pad + 6, 18), "ArcBox Tactics · asset library", font=font(34, True), fill=EDGE)
    d.text((pad + 6, 66), f"{len(sets)} sets · {len(pngs)} PNGs · {size:.1f} MB in content/tactics/base · "
                          f"all procedural, deterministic and recolourable", font=font(18), fill=MUTED)
    y = head
    for r, rh in zip(range(0, len(thumbs), cols), row_h):
        for k, (name, t) in enumerate(thumbs[r:r + cols]):
            x = pad + k * (THUMB_W + pad)
            d.rounded_rectangle((x - 4, y - 4, x + THUMB_W + 4, y + rh - 2), 8, fill=PANEL)
            out.alpha_composite(t, (x, y))
            d.text((x + 2, y + t.height + 4), name, font=font(14, True), fill=INK)
        y += rh + pad
    save(out, PREVIEWS / "00-gallery.png")
