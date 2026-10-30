"""Status overlays: looping effects drawn on a unit while a condition lasts (stunned stars, sleep Zs, poison
bubbles, flames, frost, an ice block, a hex sigil ...). They pair with the 16x16 status icons in the HUD; these are
what the TV shows on the battlefield.

Frames are the character frame size (32x40, anchor (16, 37)), drawn for the standard build, so the game places an
overlay exactly where the unit's sprite goes. Short and large builds carry their heads lower or higher: the manifest
gives a per-build vertical offset for the overhead effects. Every loop is whole-period, so it can run forever.
"""
from __future__ import annotations

import math
import shutil
from typing import Callable

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES
from arcpix.preview import Tile, contact_sheet, strip
from .rig import BUILDS, FRAME_W, FRAME_H, ANCHOR

SLOTS = SlotTable()
for _s in ("fx", "fx2"):
    SLOTS.id(_s)
HEAD = (16, 13)        # head centre, standard build
BODY = (16, 26)
TWO_PI = 2 * math.pi

STAR = [(0, -1), (-1, 0), (0, 0), (1, 0), (0, 1)]
HEART = [(-1, -1), (1, -1), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (-1, 1), (0, 1), (1, 1), (0, 2)]
NOTE = [(1, -3), (2, -3), (1, -2), (1, -1), (0, 0), (1, 0), (-1, 1), (0, 1)]
PLUS = [(0, -1), (-1, 0), (0, 0), (1, 0), (0, 1)]
ZED = [(-1, -1), (0, -1), (1, -1), (0, 0), (-1, 1), (0, 1), (1, 1)]


def _stamp(c: Canvas, pts, x: float, y: float, slot: str, shade: int) -> None:
    for dx, dy in pts:
        c.px(round(x + dx), round(y + dy), slot, shade)


def _orbit(c: Canvas, f: int, n: int, count: int, shape, rx: float, ry: float, cy: float, *, slot="fx") -> None:
    """Things circling the head: behind the head they're dimmer, in front brighter (a cheap depth cue)."""
    for k in range(count):
        a = TWO_PI * (k / count + f / n)
        x, y = HEAD[0] + math.cos(a) * rx, cy + math.sin(a) * ry
        _stamp(c, shape, x, y, slot, 5 if math.sin(a) > 0 else 3)


def _rising(c: Canvas, f: int, n: int, shape, xs: list[float], *, top: float, bottom: float, slot="fx",
            phase_step: float = 0.37) -> None:
    """Particles rising and fading on a loop: each starts at its own phase so the stream never pulses."""
    for k, x in enumerate(xs):
        t = (f / n + k * phase_step) % 1.0
        y = bottom - (bottom - top) * t
        shade = 5 if t < 0.4 else (4 if t < 0.7 else 2)
        _stamp(c, shape, x + math.sin(TWO_PI * (t + k * 0.2)) * 1.2, y, slot, shade)


def stunned(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    _orbit(c, f, n, 3, STAR, 7, 2, 5)
    return c


def charmed(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    _orbit(c, f, n, 2, HEART, 8, 2.2, 5)
    return c


def sleeping(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k in range(3):
        t = (f / n + k / 3) % 1.0
        x, y = HEAD[0] + 4 + t * 6, HEAD[1] - 3 - t * 11
        if t < 0.3:
            c.px(x, y, "fx", 4)
        else:
            _stamp(c, ZED, x, y, "fx", 5 if t < 0.7 else 3)
    return c


def poisoned(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k, x in enumerate((11, 15, 19, 13, 21)):
        t = (f / n + k * 0.29) % 1.0
        y = 33 - t * 24
        if t > 0.85:                                   # pop: a ring
            for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                c.px(x + dx, y + dy, "fx", 3)
        else:
            r = 1 if t > 0.3 else 0
            c.ellipse(x + 0.5, y + 0.5, r + 0.6, r + 0.6, "fx", 3)
            c.px(x - r, y - r, "fx", 5)
    return c


def burning(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    ph = TWO_PI * f / n
    for k, (x, base, hgt) in enumerate(((10, 36, 9), (14, 34, 12), (19, 35, 11), (22, 36, 8), (16, 30, 7))):
        h = hgt + math.sin(ph + k * 1.9) * 2.5
        sway = math.sin(ph * 2 + k) * 1.2
        c.polygon([(x - 2, base), (x + 2, base), (x + sway, base - h)], "fx", 3)
        c.polygon([(x - 1, base), (x + 1, base), (x + sway * 0.6, base - h * 0.55)], "fx", 5)
    _rising(c, f, n, [(0, 0)], [12, 20], top=8, bottom=22, slot="fx2")
    return c


def chilled(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    spots = [(9, 14), (23, 18), (11, 28), (22, 31), (16, 8), (8, 22)]
    for k, (x, y) in enumerate(spots):
        t = ((f + k * 2) % n) / n
        if t < 0.5:
            _stamp(c, STAR if t < 0.25 else [(0, 0)], x, y, "fx", 5 if t < 0.25 else 3)
    for x in range(9, 24):                              # rime at the feet
        if (x + f) % 3:
            c.px(x, 37, "fx", 4)
    return c


def frozen(f: int, n: int) -> Canvas:
    """An ice block around the unit: its facets and edges only (the unit shows through the middle), with a glint
    sweeping across."""
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    x0, x1, y0, y1 = 7, 25, 6, 38
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            edge = x in (x0, x1) or y in (y0, y1)
            facet = (x - x0) in (1, 2) and y < y1 - 2 or (y - y0) in (1,)
            if edge:
                c.px(x, y, "fx", 4 if (x == x0 or y == y0) else 2)
            elif facet:
                c.px(x, y, "fx", 3)
    g = x0 + (f / n) * (x1 - x0 + 12) - 6                # glint: a diagonal streak sweeping right
    for k in range(10):
        x, y = int(g + k * 0.6), y0 + 2 + k * 3
        if x0 < x < x1:
            c.px(x, y, "fx", 5)
    for x in range(x0 + 1, x1):                          # frosted base
        c.px(x, y1 - 1, "fx", 3)
    return c


def bleeding(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k, x in enumerate((13, 19)):
        t = (f / n + k * 0.5) % 1.0
        y = 24 + t * 13
        if t < 0.85:
            c.px(x, y, "fx", 3)
            c.px(x, y - 1, "fx", 4)
        else:
            c.rect(x - 1, 37, x + 1, 37, "fx", 2)       # splash
    return c


def blessed(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    _rising(c, f, n, [(0, 0), (0, -1)], [9, 13, 18, 22, 16], top=4, bottom=34)
    return c


def hasted(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k, y in enumerate((16, 21, 26, 31)):            # speed streaks trailing behind
        ln = 4 + ((f + k) % n) * 2
        c.line(9 - ln, y, 8, y, "fx", 4 if k % 2 else 5)
    return c


def slowed(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    x, y = HEAD[0], 2                                    # an hourglass overhead, sand trickling
    c.rect(x - 2, y, x + 2, y, "fx2", 4)
    c.rect(x - 2, y + 6, x + 2, y + 6, "fx2", 4)
    c.polygon([(x - 1.5, y + 1), (x + 2.5, y + 1), (x + 0.5, y + 3)], "fx", 3)
    c.polygon([(x + 0.5, y + 3), (x + 2.5, y + 5), (x - 1.5, y + 5)], "fx", 2)
    c.px(x, y + 3 + (f % 2), "fx", 5)
    return c


def shielded(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k in range(48):
        a = TWO_PI * k / 48
        x, y = BODY[0] + math.cos(a) * 11, BODY[1] - 3 + math.sin(a) * 15
        hl = abs(((a / TWO_PI - f / n) % 1.0) - 0.5) < 0.08
        if k % 2 == 0 or hl:
            c.px(x, y, "fx", 5 if hl else 3)
    return c


def marked(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    x, y = HEAD[0], 3
    for dx, dy in ((0, -2), (-2, 0), (2, 0), (0, 2), (-1, -1), (1, -1), (-1, 1), (1, 1)):
        c.px(x + dx, y + dy, "fx", 3)
    c.px(x, y, "fx", 5)
    for k in range(4):                                    # a ring of dots turning around the sigil
        a = TWO_PI * (k / 4 + f / n)
        c.px(x + math.cos(a) * 5, y + math.sin(a) * 1.8, "fx", 4)
    return c


def inspired(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    _rising(c, f, n, NOTE, [9, 22, 15], top=2, bottom=24, phase_step=0.33)
    return c


def regenerating(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    _rising(c, f, n, PLUS, [10, 21, 15], top=6, bottom=32, phase_step=0.33)
    return c


def concentrating(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k in range(24):
        a = TWO_PI * k / 24
        x, y = HEAD[0] + math.cos(a) * 9, HEAD[1] - 6 + math.sin(a) * 2.5
        bright = abs(((k / 24 - f / n) % 1.0) - 0.5) > 0.42
        if k % 3 != 2 or bright:
            c.px(x, y, "fx", 5 if bright else (3 if math.sin(a) > 0 else 2))
    return c


def frightened(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k in range(3):                                   # wavering wisps around the head
        ph = TWO_PI * (f / n + k / 3)
        x0 = HEAD[0] - 9 + k * 9
        for i in range(5):
            c.px(x0 + math.sin(ph + i * 0.9) * 1.5, HEAD[1] - 8 + i * 2, "fx", 4 if i < 2 else 2)
    t = (f / n) % 1.0                                     # a sweat drop sliding down
    c.px(HEAD[0] + 6, HEAD[1] - 2 + t * 4, "fx2", 4)
    c.px(HEAD[0] + 6, HEAD[1] - 1 + t * 4, "fx2", 3)
    return c


def hidden(f: int, n: int) -> Canvas:
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for k, x in enumerate((8, 12, 16, 20, 24)):
        t = (f / n + k * 0.23) % 1.0
        y = 37 - t * 14
        r = 1.8 + t * 2.2
        if t < 0.9:
            c.ellipse(x + 0.5, y, r, r * 0.75, "fx", 4 if t < 0.4 else 3)
            c.px(x - r * 0.4, y - r * 0.4, "fx", 5)
    return c


STATUSES: dict[str, tuple[Callable[[int, int], Canvas], int, str, str, str]] = {
    # id: (draw, frames, fx ramp, fx2 ramp, layer)
    "stunned": (stunned, 8, "metal/gold", "metal/gold", "over"),
    "charmed": (charmed, 8, "foliage/blossom", "magic/blood", "over"),
    "sleeping": (sleeping, 8, "magic/frost", "magic/frost", "over"),
    "poisoned": (poisoned, 8, "magic/nature", "magic/nature", "over"),
    "burning": (burning, 6, "magic/fire", "magic/holy", "over"),
    "chilled": (chilled, 6, "magic/frost", "magic/frost", "over"),
    "frozen": (frozen, 8, "terrain/ice", "magic/frost", "over"),
    "bleeding": (bleeding, 6, "magic/blood", "magic/blood", "over"),
    "blessed": (blessed, 8, "magic/holy", "magic/holy", "over"),
    "hasted": (hasted, 4, "magic/arcane", "magic/arcane", "under"),
    "slowed": (slowed, 4, "magic/frost", "metal/gold", "over"),
    "shielded": (shielded, 8, "magic/arcane", "magic/arcane", "over"),
    "marked": (marked, 8, "magic/shadow", "magic/shadow", "over"),
    "inspired": (inspired, 9, "metal/gold", "metal/gold", "over"),
    "regenerating": (regenerating, 9, "magic/nature", "magic/nature", "over"),
    "concentrating": (concentrating, 8, "magic/arcane", "magic/arcane", "over"),
    "frightened": (frightened, 6, "magic/shadow", "magic/frost", "over"),
    "hidden": (hidden, 6, "cloth/charcoal", "cloth/charcoal", "under"),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    return FAMILIES[fam][name]


def build() -> None:
    out = PACK / "status"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles = {}, []
    for sid, (draw, n, r1, r2, layer) in STATUSES.items():
        pal = {"fx": _ref(r1), "fx2": _ref(r2)}
        imgs = []
        for f in range(n):
            cv = draw(f, n)
            cv.outline()
            imgs.append(cv.render(pal, outline_mix=0.45))
        sheet = Image.new("RGBA", (FRAME_W * n, FRAME_H))
        for k, im in enumerate(imgs):
            sheet.paste(im, (k * FRAME_W, 0))
        save(sheet, out / f"{sid}.png")
        entries[sid] = {"file": f"{sid}.png", "frames": n, "fps": 8 if n >= 8 else 6, "loop": True, "layer": layer}
        tiles.append(Tile(strip(imgs[: min(n, 6)], 1, gap=2), sid, f"{n} frames · {layer}"))
    write_json({"frame": [FRAME_W, FRAME_H], "anchor": list(ANCHOR),
                "headOffset": {b: BUILDS[b].head_top - BUILDS["standard"].head_top for b in BUILDS},
                "statuses": entries,
                "notes": {"placement": "draw at the unit's anchor; shift overhead effects by headOffset[build]",
                          "layer": "over: after the unit; under: before it"}}, out / "manifest.json")
    save(contact_sheet("Status overlays", tiles, columns=3, zoom=3,
                       subtitle="Looping on-unit effects that pair with the HUD status icons. Same frame and anchor "
                                "as the characters."), PREVIEWS / "25-status.png")
    _gif()


def _gif() -> None:
    """Every status looping on a sample hero, so the motion can be judged in place."""
    from arcpix.anim import Actor, stage_gif
    from . import characters as C
    from .recipes import ALL
    hero = next(r for r in ALL if r.id == "tamsin")
    base = C.recipe_sheet(hero).frames
    idle = C.anim_table()[0].frames
    actors, seqs = [], []
    for sid, (draw, n, r1, r2, layer) in STATUSES.items():
        pal = {"fx": _ref(r1), "fx2": _ref(r2)}
        frames = []
        for k in range(24):
            ov = draw(k % n, n)
            ov.outline()
            o = ov.render(pal, outline_mix=0.45)
            unit = base[idle[(k // 6) % len(idle)]]
            img = Image.new("RGBA", unit.size)
            if layer == "under":
                img.alpha_composite(o)
                img.alpha_composite(unit)
            else:
                img.alpha_composite(unit)
                img.alpha_composite(o)
            frames.append(img)
        actors.append(Actor(sid, layer, frames, ANCHOR))
        seqs.append(list(range(24)))
    stage_gif(PREVIEWS / "anim-status.gif", "Status overlays", actors, [("Statuses", seqs, 125)], zoom=3, columns=6,
              cell_w=40, cell_h=46)
