"""Projectiles drawn natively in 16 directions. Rotating pixel art at arbitrary angles blurs and breaks lines; drawing
each heading with pixel primitives keeps every arrow crisp. The game picks the frame whose heading is nearest the
screen-space flight direction (heading 0 = screen-right, counter-clockwise in 22.5 degree steps).

Animated projectiles (fireball, eldritch orb, magic missile) add 4 frames per heading; the trail streams behind the
head along the heading.
"""
from __future__ import annotations

import math
import shutil

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet

SIZE = 24
HEADINGS = 16
SLOTS = SlotTable()
for _s in ("shaft", "head", "fletch", "glow", "core"):
    SLOTS.id(_s)


def _dir(h: int) -> tuple[float, float]:
    a = 2 * math.pi * h / HEADINGS
    return math.cos(a), -math.sin(a)          # screen y is down


def arrow(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = SIZE / 2, SIZE / 2
    tail, tip = (cx - dx * 8, cy - dy * 8), (cx + dx * 7, cy + dy * 7)
    c.line(*tail, *tip, "shaft", 3)
    px_, py_ = -dy, dx                        # perpendicular
    hx, hy = tip
    c.polygon([(hx + dx * 2, hy + dy * 2), (hx - dx * 2 + px_ * 1.6, hy - dy * 2 + py_ * 1.6),
               (hx - dx * 2 - px_ * 1.6, hy - dy * 2 - py_ * 1.6)], "head", 4)
    c.px(hx + dx * 2, hy + dy * 2, "head", 5)
    tx, ty = tail
    for s in (-1, 1):                          # fletching
        c.line(tx + dx * 3, ty + dy * 3, tx + px_ * 2 * s, ty + py_ * 2 * s, "fletch", 4 if s < 0 else 3)
    return c


def bolt(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = SIZE / 2, SIZE / 2
    c.thick_line(cx - dx * 5, cy - dy * 5, cx + dx * 4, cy + dy * 4, 2, "shaft", 3)
    hx, hy = cx + dx * 5, cy + dy * 5
    c.polygon([(hx + dx * 2.5, hy + dy * 2.5), (hx - dy * 2, hy + dx * 2), (hx + dy * 2, hy - dx * 2)], "head", 4)
    c.px(hx + dx * 2, hy + dy * 2, "head", 5)
    return c


def dagger(h: int, f: int) -> Canvas:
    """A thrown dagger tumbles: its own spin (4 frames) is independent of the flight heading."""
    c = Canvas(SIZE, SIZE, SLOTS)
    a = 2 * math.pi * (h / HEADINGS) + f * math.pi / 2
    dx, dy = math.cos(a), -math.sin(a)
    cx, cy = SIZE / 2, SIZE / 2
    c.line(cx - dx * 2, cy - dy * 2, cx + dx * 6, cy + dy * 6, "head", 5)
    c.line(cx - dx * 5, cy - dy * 5, cx - dx * 3, cy - dy * 3, "shaft", 2)
    c.line(cx - dx * 2 - dy * 2, cy - dy * 2 + dx * 2, cx - dx * 2 + dy * 2, cy - dy * 2 - dx * 2, "fletch", 4)
    return c


def _comet(c: Canvas, h: int, f: int, r: float, tail: int, *, flicker: bool) -> None:
    dx, dy = _dir(h)
    cx, cy = SIZE / 2 + dx * 3, SIZE / 2 + dy * 3
    for k in range(tail, 0, -1):               # the trail, thinning and cooling behind the head
        t = k / tail
        wob = math.sin(f * 1.7 + k) * (0.8 if flicker else 0.3)
        x, y = cx - dx * k * 1.4 - dy * wob, cy - dy * k * 1.4 + dx * wob
        rr = r * (1 - t * 0.7)
        c.ellipse(x + 0.5, y + 0.5, rr, rr, "glow", 3 if t > 0.5 else 4)
    c.ellipse(cx + 0.5, cy + 0.5, r, r, "glow", 4)
    c.ellipse(cx + 0.5 + dx * 0.6, cy + 0.5 + dy * 0.6, r * 0.55, r * 0.55, "core", 5)


def fireball(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    _comet(c, h, f, 3.2, 6, flicker=True)
    return c


def eldritch(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    _comet(c, h, f, 2.6, 4, flicker=False)
    dx, dy = _dir(h)
    cx, cy = SIZE / 2 + dx * 3, SIZE / 2 + dy * 3
    for k in range(3):                         # crackling arcs orbiting the orb
        a = f * math.pi / 2 + k * 2.1
        c.px(cx + math.cos(a) * 4.5, cy + math.sin(a) * 4.5, "core", 5)
    return c


def missile(h: int, f: int) -> Canvas:
    """Magic missile: a dart of force with a sparkling, wavering trail."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = SIZE / 2 + dx * 4, SIZE / 2 + dy * 4
    for k in range(1, 10):
        wob = math.sin(k * 0.9 + f * 1.6) * 1.5
        x, y = cx - dx * k * 1.3 - dy * wob, cy - dy * k * 1.3 + dx * wob
        if k % 2 or k < 4:
            c.px(x, y, "glow", 5 if k < 4 else 3)
    c.polygon([(cx + dx * 3, cy + dy * 3), (cx - dy * 1.6, cy + dx * 1.6), (cx + dy * 1.6, cy - dx * 1.6)], "core", 5)
    return c


def ice(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = SIZE / 2, SIZE / 2
    tip = (cx + dx * 7, cy + dy * 7)
    c.polygon([tip, (cx - dx * 4 - dy * 2.5, cy - dy * 4 + dx * 2.5), (cx - dx * 6, cy - dy * 6),
               (cx - dx * 4 + dy * 2.5, cy - dy * 4 - dx * 2.5)], "glow", 4)
    c.line(cx - dx * 4, cy - dy * 4, *tip, "core", 5)
    for k in (8, 10):                          # frost motes shed behind
        c.px(cx - dx * k - dy * (k % 3 - 1), cy - dy * k + dx * (k % 3 - 1), "glow", 3)
    return c


def rock(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    from .props import blob
    blob(c, "shaft", SIZE / 2, SIZE / 2, 3.5, 3, k=2.2)
    c.px(SIZE // 2 - 1 + (f % 2), SIZE // 2 - 1, "shaft", 5)
    dx, dy = _dir(h)
    for k in (6, 8):
        c.px(SIZE / 2 - dx * k, SIZE / 2 - dy * k, "fletch", 3)
    return c


KINDS = {   # id: (draw, frames, palette, blend)
    "arrow": (arrow, 1, {"shaft": "wood/oak", "head": "metal/steel", "fletch": "cloth/ivory"}, "normal"),
    "bolt": (bolt, 1, {"shaft": "wood/dark", "head": "metal/iron"}, "normal"),
    "dagger": (dagger, 4, {"shaft": "leather/dark", "head": "metal/steel", "fletch": "metal/gold"}, "normal"),
    "rock": (rock, 2, {"shaft": "terrain/stone", "fletch": "terrain/dirt"}, "normal"),
    "fireball": (fireball, 4, {"glow": "magic/fire", "core": "magic/holy"}, "add"),
    "eldritch": (eldritch, 4, {"glow": "magic/shadow", "core": "magic/void"}, "add"),
    "missile": (missile, 4, {"glow": "magic/arcane", "core": "magic/frost"}, "add"),
    "ice": (ice, 1, {"glow": "magic/frost", "core": "cloth/ivory"}, "add"),
}


def _ramps(slots: dict[str, str]) -> dict[str, list[str]]:
    out = {k: FAMILIES[v.split("/")[0]][v.split("/")[1]] for k, v in slots.items()}
    for name in SLOTS.names[1:]:
        out.setdefault(name, ramp("#808080"))
    return out


def build() -> None:
    out = PACK / "projectiles"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles = {}, []
    for pid, (draw, n, slots, blend) in KINDS.items():
        pal = _ramps(slots)
        sheet = Image.new("RGBA", (SIZE * n, SIZE * HEADINGS))
        for h in range(HEADINGS):
            for f in range(n):
                cv = draw(h, f)
                cv.outline()
                sheet.paste(cv.render(pal, outline_mix=0.5 if blend == "normal" else 0.25), (f * SIZE, h * SIZE))
        save(sheet, out / f"{pid}.png")
        entries[pid] = {"file": f"{pid}.png", "frames": n, "fps": 12, "blend": blend}
        row = Image.new("RGBA", (SIZE * HEADINGS, SIZE))
        for h in range(HEADINGS):
            row.alpha_composite(sheet.crop((0, h * SIZE, SIZE, (h + 1) * SIZE)), (h * SIZE, 0))
        tiles.append(Tile(row, pid, f"16 headings × {n} frame{'s' if n > 1 else ''} · {blend}"))
    write_json({"frame": [SIZE, SIZE], "anchor": [SIZE // 2, SIZE // 2], "headings": HEADINGS,
                "layout": "row = heading (0 = screen-right, counter-clockwise, 22.5° steps), column = frame",
                "projectiles": entries}, out / "manifest.json")
    save(contact_sheet("Projectiles · 16 native headings", tiles, columns=1, zoom=3,
                       subtitle="Drawn per heading instead of rotated, so lines stay crisp. Pick the row nearest the "
                                "screen-space flight direction."), PREVIEWS / "33-projectiles.png")
