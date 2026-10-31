"""One icon for every player ability in the rules pack, keyed by the ability's id (`wizard.fire_bolt`).

The rules pack has ~370 player abilities and no icon field, so the game needs to find an icon from the id alone. Drawing
370 icons by hand doesn't scale, and inferring them blindly gives a wall of look-alikes, so every icon is composed
from a shared visual grammar that a player learns once:

  * the **plate** (the icon's tinted backing) is the class: crimson barbarian, royal-blue wizard, teal monk ...;
  * the **glyph** is what the ability does, coloured by its damage type or effect: a flame in fire orange, a flame in
    radiant gold, a hand cupping necrotic green;
  * an optional **badge** (a smaller glyph, bottom-right) is the twist: a sword with a flame badge is a searing smite,
    a heart with a hand is cure wounds;
  * a gold **tier tab** (top-right, 1-5) marks a levelled spell; at-will abilities and cantrips have none;
  * a small gold **reaction hook** (bottom-left) marks abilities that fire on someone else's turn, and three dots
    mark an area version of a single-target spell (mass healing word).

The same spell in several classes (cure wounds) keeps its glyph and changes only its plate, so it reads as the same
spell. Every glyph is drawn in a 24-unit space with float geometry and rendered natively at 24x24 and at 48x48 (the
2x is a real redraw with finer edges, not an upscale), so the TV can show crisp icons at 4K.

`SPECS` (below) is the hand-written table: the short ability name (or full id, where two classes reuse a name for
different things) to its glyph, colour and badge. Abilities missing from it fall back to `_infer`, which reads the
ability's damage type, conditions and effects, so new rules content gets a sensible icon until someone tunes it.
"""
from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK
from arcpix.palette import ramp
from parts import icons as I

RULES = PACK / "rules" / "abilities"

SLOTS = SlotTable()
for _s in ("plate", "glow", "gold", "ink", "fx", "fx2", "bx", "bx2", "wood", "skin", "bone", "dark", "leaf", "metal",
           "trim"):
    SLOTS.id(_s)

# ------------------------------------------------------------------------------------------------------- colours
R: dict[str, list[str]] = {
    # damage types and effects (bright, low spread so they glow on the dark plates)
    "fire": ramp("#ff7a2a", shift=0.08, spread=0.85),
    "hell": ramp("#e8452e", shift=0.08, spread=0.9),
    "cold": ramp("#8fd6f2", shift=0.05, spread=0.85),
    "lightning": ramp("#ffd84a", shift=0.06, spread=0.8),
    "thunder": ramp("#9fb2ff", shift=0.06, spread=0.85),
    "acid": ramp("#b4e04a", shift=0.07, spread=0.85),
    "poison": ramp("#6fcf4a", shift=0.07, spread=0.9),
    "necrotic": ramp("#9ac58a", shift=0.08, spread=0.95),
    "psychic": ramp("#ff7fd0", shift=0.06, spread=0.9),
    "radiant": ramp("#ffd970", shift=0.06, spread=0.8),
    "holy": ramp("#f2d48a", shift=0.05, spread=0.85),
    "force": ramp("#7fc8ff", shift=0.05, spread=0.85),
    "arcane": ramp("#6fa8ff", shift=0.05, spread=0.9),
    "eldritch": ramp("#c062f0", shift=0.07, spread=0.95),
    "hex": ramp("#a456e0", shift=0.07),
    "void": ramp("#7a5ad8", shift=0.07, spread=1.05),
    "shadow": ramp("#b0a8e8", shift=0.06, spread=0.9),
    "mist": ramp("#a7b6ec", shift=0.05, spread=0.8),
    "charm": ramp("#ff8ac0", shift=0.06, spread=0.9),
    "wild": ramp("#ff9a3c", shift=0.08, spread=0.9),
    "heal": ramp("#63d36f", shift=0.06, spread=0.9),
    "healing": ramp("#e0384a", shift=0.07),
    "nature": ramp("#5fb04a", shift=0.08),
    "blight": ramp("#9a8a4a", shift=0.07),
    "thorn": ramp("#a8c060", shift=0.06),
    "earth": ramp("#b07a4a", shift=0.06),
    "stone": ramp("#9a9ca6", shift=0.04),
    "wind": ramp("#c8e8f0", shift=0.05, spread=0.75),
    "storm": ramp("#7f8fb8", shift=0.05),
    "ki": ramp("#56d6c2", shift=0.06, spread=0.9),
    "spirit": ramp("#8ae0e8", shift=0.05, spread=0.85),
    "time": ramp("#e8c46a", shift=0.06, spread=0.85),
    "haste": ramp("#ffd447", shift=0.06, spread=0.85),
    "blood": ramp("#e04a4a", shift=0.07),
    "moon": ramp("#d6defa", shift=0.05, spread=0.75),
    "rune": ramp("#5fd0ff", shift=0.05, spread=0.9),
    "smoke": ramp("#9a9aa8", shift=0.04, spread=0.8),
    "silk": ramp("#e8e4dc", shift=0.04, spread=0.75),
    "grease": ramp("#c0a050", shift=0.06),
    "gold": ramp("#e6bd5c", shift=0.06, spread=1.1),
    # materials
    "steel": ramp("#b4c2d6", shift=0.05, spread=1.1),
    "iron": ramp("#8a8e9a", shift=0.04, spread=1.1),
    "slash": ramp("#cfe0f2", shift=0.05, spread=0.9),
    "crimson": ramp("#c0393f", shift=0.07),
    "bark": ramp("#8a5c3a", shift=0.06),
    "leather": ramp("#9a6a44", shift=0.06),
    "wolf": ramp("#9aa0b0", shift=0.05),
    "bear": ramp("#8a5a3a", shift=0.06),
    "fur": ramp("#a87a4e", shift=0.06),
    "bone": ramp("#e3d8bd", shift=0.05, spread=0.8),
    "skin": ramp("#e0aa80", shift=0.05, spread=0.9),
    "wood": ramp("#8a5c34", shift=0.06),
    "ink": ramp("#3a2a1a", shift=0.04),
    "dark": ramp("#3a3448", shift=0.04, spread=1.2),
}

# Ramps that are materials, not magic: their glyphs get no glow behind them.
MUNDANE = {"steel", "iron", "slash", "crimson", "bark", "leather", "wolf", "bear", "fur", "bone", "wood", "stone",
           "earth", "smoke", "gold", "silk", "grease"}

CLASSES: dict[str, str] = {
    # class -> plate colour. Hues are spread round the wheel so neighbouring plates in a hotbar never blur together.
    "barbarian": "#b8323a", "bard": "#c0508a", "cleric": "#c89a3a", "druid": "#3e9a4e", "fighter": "#4f6f9e",
    "monk": "#2f9e92", "paladin": "#8aa6d6", "ranger": "#8a8a34", "rogue": "#5e5478", "sorcerer": "#d0602a",
    "warlock": "#8a3ad0", "wizard": "#3a5ee0", "common": "#6a7080", "race": "#a07040", "item": "#7a5a3a",
}


def _mix(a: str, b: str, t: float) -> str:
    ca, cb = (tuple(int(h[i:i + 2], 16) for i in (1, 3, 5)) for h in (a, b))
    return "#" + "".join(f"{round(x + (y - x) * t):02x}" for x, y in zip(ca, cb))


# Plates are the class colour sunk into the UI's navy glass: tinted enough to name the class at a glance, dark
# enough that every glyph colour (even the class's own) stands off it.
PLATES = {k: ramp(_mix(v, "#1a1e3a", 0.5), shift=0.06, spread=0.9) for k, v in CLASSES.items()}


# --------------------------------------------------------------------------------------------------- drawing kit
class G:
    """A view of a canvas in the 24-unit icon space: scaled by k (1 or 2: the 24 and 48 px renders), then by s and
    moved by (ox, oy) (a badge is the same glyph at s=0.55 in a corner). `sl` renames the glyph's "fx"/"fx2" slots, so
    a badge drawn in its own colours shares the canvas with the main glyph."""

    def __init__(self, c: Canvas, k: int, s: float = 1.0, ox: float = 0.0, oy: float = 0.0, sl=None):
        self.c, self.k, self.s, self.ox, self.oy = c, k, s, ox, oy
        self.m = sl or {}

    def sl(self, slot: str) -> str:
        return self.m.get(slot, slot)

    def P(self, x: float, y: float) -> tuple[float, float]:
        return (self.ox + x * self.s) * self.k, (self.oy + y * self.s) * self.k

    def U(self, px: float, py: float) -> tuple[float, float]:
        return (px / self.k - self.ox) / self.s, (py / self.k - self.oy) / self.s

    @property
    def unit(self) -> float:
        """How many pixels one icon unit is."""
        return self.s * self.k

    def fill(self, fn, slot: str, shade: int = 3, bb=(0, 0, 24, 24)) -> None:
        """Every pixel whose centre (in icon units) passes fn(x, y)."""
        x0, y0 = self.P(bb[0], bb[1])
        x1, y1 = self.P(bb[2], bb[3])
        for py in range(max(0, int(y0) - 1), min(self.c.h, int(y1) + 2)):
            for px in range(max(0, int(x0) - 1), min(self.c.w, int(x1) + 2)):
                u, v = self.U(px + 0.5, py + 0.5)
                if fn(u, v):
                    self.c.px(px, py, self.sl(slot), shade)

    def erase(self, fn, bb=(0, 0, 24, 24)) -> None:
        x0, y0 = self.P(bb[0], bb[1])
        x1, y1 = self.P(bb[2], bb[3])
        for py in range(max(0, int(y0) - 1), min(self.c.h, int(y1) + 2)):
            for px in range(max(0, int(x0) - 1), min(self.c.w, int(x1) + 2)):
                if fn(*self.U(px + 0.5, py + 0.5)):
                    self.c.clear(px, py)

    def poly(self, pts, slot: str, shade: int = 3) -> None:
        self.c.polygon([self.P(x, y) for x, y in pts], self.sl(slot), shade)

    def rect(self, x0, y0, x1, y1, slot: str, shade: int = 3) -> None:
        """A rectangle from (x0, y0) to (x1, y1) in units (exclusive far edge, like a polygon)."""
        self.poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], slot, shade)

    def ell(self, cx, cy, rx, ry, slot: str, shade: int = 3) -> None:
        self.fill(lambda x, y: ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0, slot, shade,
                  (cx - rx, cy - ry, cx + rx, cy + ry))

    def seg(self, x0, y0, x1, y1, w, slot: str, shade: int = 3, w1=None) -> None:
        """A straight stroke w units wide (tapering to w1), as a quad so it scales cleanly."""
        w1 = w if w1 is None else w1
        dx, dy = x1 - x0, y1 - y0
        n = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / n, dx / n
        self.poly([(x0 + nx * w / 2, y0 + ny * w / 2), (x1 + nx * w1 / 2, y1 + ny * w1 / 2),
                   (x1 - nx * w1 / 2, y1 - ny * w1 / 2), (x0 - nx * w / 2, y0 - ny * w / 2)], slot, shade)

    def ring(self, cx, cy, ro, ri, slot: str, shade: int = 3, a0=None, a1=None, ry: float = 1.0) -> None:
        """An annulus, or an arc of one from a0 to a1 degrees (0 = right, 90 = down)."""
        def f(x, y):
            dx, dy = x - cx, (y - cy) / ry
            d = math.hypot(dx, dy)
            if not ri < d <= ro:
                return False
            if a0 is None:
                return True
            a = math.degrees(math.atan2(dy, dx)) % 360
            lo, hi = a0 % 360, a1 % 360
            return lo <= a <= hi if lo <= hi else (a >= lo or a <= hi)
        self.fill(f, slot, shade, (cx - ro, cy - ro * ry, cx + ro, cy + ro * ry))

    def dot(self, x, y, slot: str, shade: int = 5, n: float = 1.0) -> None:
        """A highlight pixel (n units square, at least one pixel)."""
        px, py = self.P(x, y)
        size = max(1, int(round(n * self.unit)))
        self.c.rect(int(px), int(py), int(px) + size - 1, int(py) + size - 1, self.sl(slot), shade)

    def lit(self, slot: str, band: int | None = None, rim: int = 1) -> None:
        I.light(self.c, self.sl(slot), band=band or (2 if self.unit >= 1.5 else 1), rim=rim)

    def ball(self, slot: str, bb=None, **kw) -> None:
        box = None
        if bb is not None:
            x0, y0 = self.P(bb[0], bb[1])
            x1, y1 = self.P(bb[2], bb[3])
            box = (int(x0), int(y0), int(math.ceil(x1)), int(math.ceil(y1)))
        I.ball(self.c, self.sl(slot), box=box, **kw)


def _tear(g: G, cx, by, w, h, slot, shade, lean=0.0, tongues=False):
    """A flame-shaped teardrop standing on (cx, by)."""
    pts = [(cx + w * math.cos(a), by - w + w * math.sin(a)) for a in np.linspace(0, math.pi, 12)]
    pts += [(cx - w * 0.98, by - w - (h - w) * 0.25), (cx - w * 0.5 + lean * 0.4, by - w - (h - w) * 0.72),
            (cx + lean, by - h), (cx + w * 0.62 + lean * 0.5, by - w - (h - w) * 0.5), (cx + w, by - w)]
    g.poly(pts, slot, shade)
    if tongues:
        g.poly([(cx - w * 0.85, by - w * 0.9), (cx - w * 1.35 + lean * 0.3, by - h * 0.62),
                (cx - w * 0.2, by - w * 1.5)], slot, shade)
        g.poly([(cx + w * 0.9, by - w * 0.8), (cx + w * 1.3 + lean * 0.4, by - h * 0.55),
                (cx + w * 0.3, by - w * 1.5)], slot, shade)


def _star(cx, cy, ro, ri, n, rot=-90.0):
    return [(cx + (ro if k % 2 == 0 else ri) * math.cos(math.radians(rot + k * 180.0 / n)),
             cy + (ro if k % 2 == 0 else ri) * math.sin(math.radians(rot + k * 180.0 / n))) for k in range(2 * n)]


def _twinkle(g: G, x, y, slot, r=2.0, shade=5):
    g.poly(_star(x, y, r, r * 0.3, 4), slot, shade - 1)
    g.dot(x - 0.5, y - 0.5, slot, shade)


# ------------------------------------------------------------------------------------------------------- glyphs
# Each glyph fills roughly (3, 3)-(21, 21) of the 24-unit box. "fx" is the spec's colour, "fx2" its accent.
def flame(g):
    _tear(g, 12, 21.5, 7, 19, "fx", 2, lean=1.5, tongues=True)
    _tear(g, 12, 21.5, 5.2, 13.5, "fx", 4, lean=1.0)
    _tear(g, 11.8, 21.5, 3, 7.5, "fx", 5, lean=0.4)


def snowflake(g):
    for k in range(6):
        a = math.radians(-90 + k * 60)
        ca, sa = math.cos(a), math.sin(a)
        g.seg(12 + ca * 1.5, 12 + sa * 1.5, 12 + ca * 9.5, 12 + sa * 9.5, 2.2, "fx", 4 if sa < 0.1 else 3)
        for side in (-1, 1):
            b = a + side * math.radians(50)
            bx, by = 12 + ca * 5.8, 12 + sa * 5.8
            g.seg(bx, by, bx + math.cos(b) * 3.2, by + math.sin(b) * 3.2, 1.5, "fx", 3)
        g.dot(12 + ca * 9 - 0.5, 12 + sa * 9 - 0.5, "fx", 5)
    g.poly(_star(12, 12, 3.2, 1.6, 6, rot=-60), "fx", 5)


def bolt(g):
    g.poly([(15.5, 1.5), (5, 13.5), (10.8, 13.5), (7, 22.5), (19.5, 9.2), (13.5, 9.2), (18.5, 1.5)], "fx")
    g.lit("fx")
    g.seg(15.2, 3, 7.5, 12.5, 1.0, "fx", 5)


def skull(g):
    g.ell(12, 10, 8, 7.5, "bone")
    g.poly([(7, 13), (17, 13), (16, 20.5), (8, 20.5)], "bone")
    g.ball("bone", bb=(4, 2.5, 20, 20.5), lo=2)
    for ex in (8.8, 15.2):
        g.ell(ex, 12, 2.4, 2.3, "dark", 0)
        g.dot(ex - 0.7, 11.4, "fx", 5, 1.2)
    g.poly([(12, 14.5), (13.2, 16.6), (10.8, 16.6)], "dark", 0)
    for tx in (9.2, 11.4, 13.6):
        g.rect(tx + 1.0, 18, tx + 1.6, 20.5, "dark", 1)


def sun(g):
    for k in range(8):
        a = math.radians(-90 + k * 45)
        b = math.radians(22)
        r0, r1 = 6.5, 10.8 if k % 2 == 0 else 9.0
        g.poly([(12 + r0 * math.cos(a - b), 12 + r0 * math.sin(a - b)), (12 + r1 * math.cos(a), 12 + r1 * math.sin(a)),
                (12 + r0 * math.cos(a + b), 12 + r0 * math.sin(a + b))], "fx", 4 if k in (0, 5, 6, 7) else 3)
    g.ell(12, 12, 6, 6, "fx")
    g.ball("fx", bb=(6, 6, 18, 18), lo=3)


def leaf(g):
    ax, ay, bx, by = 4.5, 19.5, 20, 4
    pts = []
    for t in np.linspace(0, 1, 14):
        w = math.sin(math.pi * t) ** 0.8 * 5.5
        pts.append((ax + (bx - ax) * t - w * 0.707, ay + (by - ay) * t - w * 0.707))
    for t in np.linspace(1, 0, 14):
        w = math.sin(math.pi * t) ** 0.8 * 5.5
        pts.append((ax + (bx - ax) * t + w * 0.707, ay + (by - ay) * t + w * 0.707))
    g.poly(pts, "fx")
    g.lit("fx", band=2)
    g.seg(ax, ay, bx - 2, by + 2, 1.0, "fx", 2)
    for t in (0.3, 0.5, 0.7):
        x, y = ax + (bx - ax) * t, ay + (by - ay) * t
        g.seg(x, y, x - 1.5, y - 3.5, 0.8, "fx", 2)
        g.seg(x, y, x + 3.5, y + 1.5, 0.8, "fx", 2)
    g.seg(2.5, 21.5, 5, 19, 1.4, "wood", 3)


def fist(g):
    for k in range(4):
        top = 4.5 if k in (1, 2) else 5.5
        g.rect(5.5 + k * 3.3, top, 8.6 + k * 3.3, 11, "skin")
    g.rect(5.5, 10, 18.8, 16, "skin")
    g.lit("skin")
    for k in range(4):
        g.dot(5.8 + k * 3.3, (4.6 if k in (1, 2) else 5.6), "skin", 5)
        g.rect(8.0 + k * 3.3, 6.5, 8.6 + k * 3.3, 10, "skin", 1)
    g.rect(6.5, 12.2, 14, 14, "skin", 4)
    g.rect(6.5, 14, 14, 14.8, "skin", 2)
    g.rect(7, 16, 17.5, 20.5, "fx")
    g.rect(7, 16, 17.5, 17, "fx", 4)
    g.rect(7, 19.5, 17.5, 20.5, "fx", 2)


def fists(g):
    sub = G(g.c, g.k, g.s * 0.72, g.ox + 6.8 * g.s, g.oy + 0.2 * g.s, g.m)
    fist(sub)
    sub2 = G(g.c, g.k, g.s * 0.8, g.ox + 0.5 * g.s, g.oy + 5.5 * g.s, g.m)
    fist(sub2)


def _blade(g, x0, y0, x1, y1, w, slot="fx"):
    """A pointed blade from (x0, y0) to its tip (x1, y1), w units wide, lit along its upper edge."""
    dx, dy = x1 - x0, y1 - y0
    n = math.hypot(dx, dy)
    ux, uy = dx / n, dy / n
    nx, ny = -uy, ux
    tip_back = w * 1.1
    g.poly([(x0 + nx * w / 2, y0 + ny * w / 2), (x1 - ux * tip_back + nx * w / 2, y1 - uy * tip_back + ny * w / 2),
            (x1, y1), (x1 - ux * tip_back - nx * w / 2, y1 - uy * tip_back - ny * w / 2),
            (x0 - nx * w / 2, y0 - ny * w / 2)], slot, 3)
    g.seg(x0 + nx * w * 0.3, y0 + ny * w * 0.3, x1 - ux * 1.5 + nx * w * 0.15, y1 - uy * 1.5 + ny * w * 0.15,
          w * 0.35, slot, 2)
    g.seg(x0 - nx * w * 0.32, y0 - ny * w * 0.32, x1 - ux * 1.2 - nx * w * 0.2, y1 - uy * 1.2 - ny * w * 0.2,
          w * 0.3, slot, 5)


def sword(g, *, length=1.0):
    gx, gy = 7.5, 16.5
    tx, ty = gx + 13.5 * length, gy - 13.5 * length
    _blade(g, gx, gy, tx, ty, 3.4)
    g.seg(4.3, 13.3, 10.7, 19.7, 2.0, "gold", 4)
    g.seg(5.2, 14.6, 9.4, 18.8, 0.8, "gold", 2)
    g.seg(7.5, 16.5, 4, 20, 2.0, "wood", 3)
    g.ell(3.6, 20.4, 1.6, 1.6, "gold", 4)
    g.dot(3, 19.6, "gold", 5)


def swords(g):
    sword(g, length=0.95)
    m = G(g.c, g.k, g.s, g.ox, g.oy, g.m)
    gx, gy = 16.5, 16.5
    _blade(m, gx, gy, 3.5, 3.5, 3.0)
    m.seg(13.3, 19.7, 19.7, 13.3, 2.0, "gold", 4)
    m.seg(16.5, 16.5, 20, 20, 2.0, "wood", 3)
    m.ell(20.4, 20.4, 1.6, 1.6, "gold", 3)


def dagger(g):
    _blade(g, 9.5, 14.5, 20, 4, 3.0)
    g.seg(6.8, 11.8, 12.2, 17.2, 1.8, "gold", 4)
    g.seg(9.5, 14.5, 5, 19, 2.2, "wood", 3)
    g.ell(4.4, 19.6, 1.5, 1.5, "gold", 4)


def axe(g):
    g.seg(6, 21, 17, 4, 2.2, "wood", 3)
    g.lit("wood")
    head = [(13, 4.5), (8, 2.5), (5, 5.5), (4.2, 10), (6, 14), (9.8, 12.6), (14.6, 9.6)]
    g.poly(head, "fx")
    g.poly([(15.2, 6), (20, 5.8), (18.2, 9.5)], "fx")
    g.lit("fx", band=2)
    g.seg(7.8, 3, 4.6, 7.5, 0.9, "fx", 5)
    g.seg(4.6, 7.5, 4.8, 12, 0.9, "fx", 5)
    g.seg(12.6, 6.6, 15.4, 8.8, 2.2, "gold", 3)


def hammer(g):
    g.seg(4.5, 20.5, 14, 11, 2.4, "wood", 3)
    g.lit("wood")
    g.poly([(10.5, 6.5), (17.5, -0.2 + 1.8), (23, 7.5), (16, 14.5)], "fx")
    g.lit("fx", band=2)
    g.seg(11, 6.8, 17.3, 2.4, 1.0, "fx", 5)
    g.seg(12.3, 9.7, 18.5, 3.7, 1.2, "gold", 3)


def spear(g):
    g.seg(3, 21, 15, 9, 1.8, "wood", 3)
    g.lit("wood")
    _blade(g, 14, 10, 21.5, 2.5, 3.6)
    g.seg(12.6, 9.4, 14.6, 11.4, 2.4, "gold", 3)


def shield(g):
    pts = [(4, 3), (20, 3), (20, 11.5), (17, 17.5), (12, 21.5), (7, 17.5), (4, 11.5)]
    g.poly(pts, "metal")
    inner = [(6, 5), (18, 5), (18, 11.2), (15.6, 16.2), (12, 19.2), (8.4, 16.2), (6, 11.2)]
    g.poly(inner, "fx")
    g.lit("metal")
    g.ball("fx", bb=(5, 4, 19, 20), lo=2, spec=False)
    g.rect(11, 6, 13, 17, "gold", 4)
    g.rect(7.5, 8.5, 16.5, 10.5, "gold", 4)
    g.rect(12.2, 10.5, 13, 17, "gold", 2)
    g.dot(4.5, 3.5, "metal", 5, 1.5)


def shields(g):
    for i, (ox, oy) in enumerate(((-3.6, 1.5), (3.6, 1.5), (0, -0.5))):
        sub = G(g.c, g.k, g.s * 0.72, g.ox + (ox + 3.4) * g.s, g.oy + (oy + 4.4) * g.s, g.m)
        shield(sub)


def eye(g, *, w=18.0, cy=12.0):
    hw = w / 2
    def f(x, y):
        t = (x - 12) / hw
        if abs(t) > 1:
            return False
        h = (1 - t * t) ** 0.9
        return cy - h * w * 0.33 <= y <= cy + h * w * 0.27
    g.fill(f, "bone", 4, (12 - hw, cy - w * 0.4, 12 + hw, cy + w * 0.35))
    g.fill(lambda x, y: f(x, y) and y > cy + 1.6, "bone", 2, (12 - hw, cy, 12 + hw, cy + w * 0.35))
    r = w * 0.24
    g.ell(12, cy + 0.2, r, r, "fx")
    g.ball("fx", bb=(12 - r, cy + 0.2 - r, 12 + r, cy + 0.2 + r), lo=2)
    g.ell(12, cy + 0.2, r * 0.32, r * 0.75, "dark", 0)
    g.dot(12 - r * 0.6, cy - r * 0.5, "bone", 5, 1.2)


def eye_tri(g):
    def tri(x, y, inset):
        top, bot = 2.5 + inset * 2, 20.5 - inset
        if not top <= y <= bot:
            return False
        half = (y - top) / (bot - top) * (10.5 - inset * 1.7)
        return abs(x - 12) <= half
    g.fill(lambda x, y: tri(x, y, 0) and not tri(x, y, 2.4), "fx", 3, (1, 2, 23, 21))
    g.lit("fx")
    sub = G(g.c, g.k, g.s * 0.55, g.ox + 5.4 * g.s, g.oy + 7.3 * g.s, {**g.m, "fx": g.sl("fx2")})
    eye(sub)


def heart(g, *, cy=7.5, r=5.0):
    g.ell(12 - r * 0.92, cy + r * 0.3, r, r, "fx")
    g.ell(12 + r * 0.92, cy + r * 0.3, r, r, "fx")
    g.poly([(12 - r * 1.88, cy + r * 0.62), (12 + r * 1.88, cy + r * 0.62), (12, cy + r * 2.75)], "fx")
    g.ball("fx", bb=(12 - r * 2, cy - r * 0.7, 12 + r * 2, cy + r * 2.75), lo=2)


def note(g):
    g.ell(7, 18, 3.2, 2.5, "fx")
    g.ell(16.5, 16, 3.2, 2.5, "fx")
    g.rect(8.9, 5.5, 10.3, 18, "fx")
    g.rect(18.4, 3.5, 19.8, 16, "fx")
    g.poly([(8.9, 4.2), (19.8, 2), (19.8, 5.4), (8.9, 7.6)], "fx")
    g.poly([(8.9, 8.6), (19.8, 6.4), (19.8, 8.4), (8.9, 10.6)], "fx")
    g.lit("fx")
    g.dot(5.8, 16.8, "fx", 5)
    g.dot(15.3, 14.8, "fx", 5)


def moon(g):
    g.ell(12, 12, 9, 9, "fx")
    g.erase(lambda x, y: (x - 16.5) ** 2 + (y - 8.5) ** 2 <= 7.6 ** 2)
    g.ball("fx", bb=(3, 3, 21, 21), lo=2)
    g.dot(5.5, 13, "fx", 2, 1.4)
    g.dot(8, 17.5, "fx", 2, 1.2)


def star(g):
    g.poly(_star(12, 12.8, 10.5, 4.4, 5), "fx")
    g.lit("fx", band=2)
    g.poly(_star(12, 12.8, 4.6, 2.2, 5), "fx", 5)


def sparkle(g):
    _twinkle(g, 9, 10, "fx", 6.5)
    _twinkle(g, 17.5, 5.5, "fx", 3.2)
    _twinkle(g, 17, 17.5, "fx", 3.8)
    _twinkle(g, 4.5, 19.5, "fx", 2.2)


def hand(g):
    g.poly([(6.5, 11), (17.5, 11), (17.5, 17), (15.5, 21.5), (8.5, 21.5), (6.5, 17)], "skin")
    for k, (top, x) in enumerate(((5.5, 7.2), (3.5, 10.0), (3.2, 12.8), (4.8, 15.6))):
        g.rect(x - 0.1, top + 0.8, x + 2.1, 12, "skin")
        g.ell(x + 1.0, top + 1.0, 1.1, 1.1, "skin")
    g.poly([(6.5, 15.5), (3, 10.5), (4.6, 9.5), (8.2, 13.2)], "skin")
    g.lit("skin")
    for x in (9.4, 12.2, 15.0):
        g.seg(x, 6, x, 11.5, 0.5, "skin", 2)
    g.ell(12, 15.8, 3.6, 3.1, "fx", 4)
    g.ell(11.6, 15.4, 1.8, 1.5, "fx", 5)
    g.ring(12, 15.8, 5.2, 4.2, "fx", 2, a0=200, a1=340)


def chain(g):
    def link(cx, cy, ang, ro=(5.0, 3.3), ri=(2.9, 1.25)):
        a = math.radians(ang)
        def f(x, y):
            dx, dy = x - cx, y - cy
            u, v = dx * math.cos(a) + dy * math.sin(a), -dx * math.sin(a) + dy * math.cos(a)
            return (u / ro[0]) ** 2 + (v / ro[1]) ** 2 <= 1 and (u / ri[0]) ** 2 + (v / ri[1]) ** 2 > 1
        g.fill(f, "fx", 3, (cx - 6, cy - 6, cx + 6, cy + 6))
    link(6.2, 17.8, -45)
    link(17.8, 6.2, -45)
    g.seg(9.0, 15.0, 15.0, 9.0, 2.8, "fx", 4)
    g.lit("fx")
    g.seg(9.6, 13.6, 13.6, 9.6, 0.8, "fx", 5)


def wave(g):
    for k, (r, sh) in enumerate(((5.0, 5), (10.0, 4), (15.0, 3))):
        g.ring(3, 12, r, r - 2.3, "fx", sh, a0=-48 % 360, a1=48)
    g.ell(3.5, 12, 2.2, 2.2, "fx", 5)


def wind(g):
    for (y, x0, x1, r) in ((6, 3, 14, 3.0), (12, 2, 18, 3.4), (18, 5, 12, 2.6)):
        g.seg(x0, y, x1, y, 1.8, "fx", 4 if y < 15 else 3)
        g.ring(x1, y - r, r + 0.9, r - 0.9, "fx", 4 if y < 15 else 3, a0=270, a1=90)
        g.dot(x0, y - 0.9, "fx", 5)


def arrow(g, *, head_slot="fx"):
    g.seg(4.5, 19.5, 17, 7, 1.6, "wood", 4)
    g.poly([(21.5, 2.5), (14.2, 6.3), (17, 7), (17.7, 9.8)], head_slot)
    g.lit(head_slot)
    g.dot(19.6, 3.8, head_slot, 5)
    for k in range(3):
        fx, fy = 3.5 + k * 1.6, 20.5 - k * 1.6
        g.seg(fx, fy, fx - 1.8, fy - 3.0, 1.2, "fx2", 4)
        g.seg(fx, fy, fx + 3.0, fy + 1.8, 1.2, "fx2", 3)


def volley(g):
    for k, (ox, oy) in enumerate(((-5, 3), (0, 0), (5, -3))):
        sub = G(g.c, g.k, g.s * 0.8, g.ox + (ox + 3.5) * g.s, g.oy + (oy + 3.8) * g.s, g.m)
        arrow(sub)


def footprints(g):
    for (cx, cy) in ((7.5, 15), (16.5, 7.5)):
        g.ell(cx, cy + 1.6, 2.9, 4.6, "fx")
        g.ell(cx + 0.2, cy + 0.2, 2.6, 2.4, "fx", 4)
        for (tx, ty, tr) in ((-2.2, -4.7, 1.05), (0.1, -5.6, 1.15), (2.3, -4.7, 1.0)):
            g.ell(cx + tx, cy + ty, tr, tr, "fx", 4)
    g.lit("fx")


def boot(g):
    g.poly([(8, 3), (14.5, 3), (14.5, 14), (20.5, 16), (21, 20.5), (6, 20.5), (6.5, 14), (8, 13)], "fx")
    g.lit("fx", band=2)
    g.rect(7.6, 3, 14.9, 5.2, "fx", 4)
    g.rect(6, 19.3, 21, 21.2, "dark", 1)
    g.rect(8.2, 9, 14.5, 10.2, "gold", 4)
    g.poly([(14.5, 5), (20.5, 3.5), (18, 7.5), (21.5, 7.8), (14.5, 10.5)], "fx2", 4)


def crown(g):
    g.poly([(3.5, 8), (8, 12), (12, 4.5), (16, 12), (20.5, 8), (19, 18), (5, 18)], "fx")
    g.lit("fx", band=2)
    g.rect(5, 15.5, 19, 18.5, "fx", 4)
    g.rect(5, 18, 19, 18.8, "fx", 2)
    for x, y in ((3.5, 7), (12, 3.5), (20.5, 7)):
        g.ell(x, y, 1.5, 1.5, "fx", 5)
    for x in (8, 12, 16):
        g.ell(x, 16.9, 1.1, 1.1, "fx2", 4)


def cloud(g):
    for (cx, cy, rx, ry) in ((7.5, 13, 4.8, 4.2), (13, 9.5, 6, 5.5), (17.5, 13, 4.6, 4.0)):
        g.ell(cx, cy, rx, ry, "fx")
    g.rect(4, 13, 20.5, 17, "fx")
    g.ell(4.5, 15, 2.2, 2, "fx")
    g.ell(20, 15, 2.2, 2, "fx")
    g.lit("fx", band=2)
    g.dot(10, 5.5, "fx", 5, 1.5)
    g.rect(5, 16, 20, 17, "fx", 2)


def web(g):
    for k in range(8):
        a = math.radians(k * 45 + 10)
        g.seg(12, 12, 12 + math.cos(a) * 11, 12 + math.sin(a) * 11, 1.0, "fx", 4)
    for r in (4.0, 7.5, 10.5):
        pts = [(12 + math.cos(math.radians(k * 45 + 10)) * r, 12 + math.sin(math.radians(k * 45 + 10)) * r)
               for k in range(9)]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            mx, my = (x0 + x1) / 2 - (12 - (x0 + x1) / 2) * 0.12, (y0 + y1) / 2 - (12 - (y0 + y1) / 2) * 0.12
            g.seg(x0, y0, mx, my, 0.9, "fx", 3)
            g.seg(mx, my, x1, y1, 0.9, "fx", 3)
    g.ell(12, 12, 1.4, 1.4, "fx", 5)


def thorn(g):
    pts = [(3, 21), (7, 15), (6, 10.5), (10, 6.5), (15, 6), (19, 3)]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        g.seg(x0, y0, x1, y1, 2.2, "fx", 3)
    for (x, y, dx, dy) in ((5.4, 17.8, -2.5, -0.6), (7.6, 13, 2.6, 0.4), (6.5, 10.5, -2.4, -1.4), (9, 7.5, 0, -2.8),
                           (13, 6.2, 1.2, 2.6), (16, 5.3, 0.4, -2.8)):
        g.poly([(x - dy * 0.35, y + dx * 0.35), (x + dx, y + dy), (x + dy * 0.35, y - dx * 0.35)], "fx2", 4)
    g.lit("fx")


def vines(g):
    for (x0, sway, sh) in ((6, 1.0, 3), (12, -1.2, 4), (18, 1.1, 3)):
        pts = [(x0 + math.sin(t * 2.2) * 2.4 * sway, 21 - t * 5.5) for t in np.linspace(0, 3.1, 10)]
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            g.seg(ax, ay, bx, by, 2.0, "fx", sh)
        tx, ty = pts[-1]
        g.ell(tx + 1.5 * sway, ty + 0.5, 1.8, 1.2, "fx2", 4)
    g.rect(2, 20.5, 22, 22, "wood", 2)


def spikes(g):
    g.poly([(1.5, 19), (22.5, 19), (22.5, 22), (1.5, 22)], "wood", 2)
    for k, (x, h) in enumerate(((4, 9), (8.5, 14), (13, 10), (17.5, 15.5), (21, 8))):
        g.poly([(x - 2.4, 19.5), (x + 0.4, 19.5 - h), (x + 2.4, 19.5)], "fx", 3)
    g.lit("fx")


def orb(g):
    g.ell(12, 12, 7.5, 7.5, "fx")
    g.ball("fx", bb=(4.5, 4.5, 19.5, 19.5), lo=1)
    g.ring(12, 12, 10.5, 9.3, "fx2", 4, a0=120, a1=300, ry=0.42)
    g.ring(12, 12, 10.5, 9.3, "fx2", 3, a0=300, a1=120, ry=0.42)
    _twinkle(g, 18.5, 5, "fx2", 2.2)


def metamagic(g):
    g.ring(12, 12, 10.6, 9.4, "fx2", 3, a0=180, a1=360, ry=0.4)
    g.ell(12, 12, 6.5, 6.5, "fx")
    g.ball("fx", bb=(5.5, 5.5, 18.5, 18.5), lo=1)
    g.ring(12, 12, 10.6, 9.4, "fx2", 4, a0=0, a1=180, ry=0.4)
    for (x, y) in ((4, 5), (20, 18)):
        _twinkle(g, x, y, "fx2", 2.0)


def drop(g):
    g.ell(12, 15, 6.5, 6.5, "fx")
    g.poly([(5.8, 13.5), (12, 2.5), (18.2, 13.5)], "fx")
    g.ball("fx", bb=(5.5, 6, 18.5, 21.5), lo=1)
    g.dot(9, 13, "fx", 5, 1.5)


def crystal(g):
    for (pts, lit_side) in (([(12, 2), (16, 9), (14, 21), (10, 21), (8, 9)], 0),
                            ([(4.5, 7), (9, 12), (9.5, 21), (6, 21), (3, 13)], 1),
                            ([(20, 6.5), (21.5, 13), (18.5, 21), (15, 21), (15.5, 12)], 1)):
        g.poly(pts, "fx", 3)
        xs = [p[0] for p in pts]
        mid = (min(xs) + max(xs)) / 2
        g.fill(lambda x, y, pts=pts, mid=mid: x < mid and _inpoly(pts, x, y), "fx", 4, (0, 0, 24, 24))
    g.seg(11, 4.5, 10, 18, 0.8, "fx", 5)
    g.rect(2, 20.5, 22, 22, "fx", 1)


def _inpoly(pts, x, y):
    inside = False
    j = len(pts) - 1
    for i in range(len(pts)):
        (xi, yi), (xj, yj) = pts[i], pts[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def hourglass(g):
    g.rect(4.5, 2, 19.5, 4.5, "wood", 4)
    g.rect(4.5, 19.5, 19.5, 22, "wood", 3)
    g.rect(5.5, 4.5, 6.8, 19.5, "wood", 2)
    g.rect(17.2, 4.5, 18.5, 19.5, "wood", 2)
    g.poly([(7.5, 4.5), (16.5, 4.5), (12.8, 12), (16.5, 19.5), (7.5, 19.5), (11.2, 12)], "bone", 4)
    g.poly([(9, 6.5), (15, 6.5), (12, 11)], "fx", 4)
    g.poly([(12, 15), (16, 19.5), (8, 19.5)], "fx", 3)
    g.rect(11.6, 11, 12.4, 19.5, "fx", 5)


def rune(g):
    g.ring(12, 12, 10.5, 8.6, "fx", 3)
    g.lit("fx")
    g.seg(12, 4.5, 12, 19.5, 1.8, "fx", 5)
    g.seg(12, 7, 17, 11, 1.6, "fx", 5)
    g.seg(12, 12, 7, 15.5, 1.6, "fx", 5)
    g.seg(12, 16, 16.5, 18, 1.4, "fx", 4)
    for k in range(4):
        a = math.radians(45 + k * 90)
        g.ell(12 + math.cos(a) * 9.5, 12 + math.sin(a) * 9.5, 1.3, 1.3, "fx", 5)


def rock(g):
    g.poly([(3, 19), (4.5, 11), (9, 5.5), (15, 4.5), (20, 9), (21.5, 17), (18, 21), (6, 21)], "fx")
    g.lit("fx", band=2)
    g.seg(9, 5.5, 11, 12, 0.8, "fx", 2)
    g.seg(11, 12, 16, 14, 0.8, "fx", 2)
    g.seg(11, 12, 8, 18, 0.8, "fx", 2)
    g.poly([(9.2, 6.5), (14.5, 5.5), (12, 9)], "fx", 5)


def spire(g):
    g.poly([(7, 21), (11, 2.5), (16, 21)], "fx", 3)
    g.poly([(13.5, 21), (18, 8.5), (21.5, 21)], "fx", 3)
    g.poly([(2.5, 21), (5.5, 12), (8.5, 21)], "fx", 3)
    g.fill(lambda x, y: (_inpoly([(7, 21), (11, 2.5), (16, 21)], x, y) and x < 11 - (y - 2.5) * 0.02), "fx", 4)
    g.fill(lambda x, y: _inpoly([(13.5, 21), (18, 8.5), (21.5, 21)], x, y) and x < 18, "fx", 4)
    g.fill(lambda x, y: _inpoly([(2.5, 21), (5.5, 12), (8.5, 21)], x, y) and x < 5.5, "fx", 4)
    g.rect(1.5, 20.5, 22.5, 22.5, "wood", 2)


def quake(g):
    g.poly([(1.5, 13), (10.5, 11), (12, 15), (10, 22), (1.5, 22)], "fx", 3)
    g.poly([(13.5, 13.5), (22.5, 14.5), (22.5, 22), (12, 22), (14.5, 18)], "fx", 3)
    g.lit("fx")
    g.poly([(10.5, 11), (13.5, 13.5), (14.5, 18), (12, 22), (10, 22), (12, 15)], "fx2", 4)
    g.seg(12.2, 13.5, 12, 21, 0.9, "fx2", 5)
    for (x, y, r) in ((5, 6.5, 2.0), (12.5, 3.5, 2.4), (19, 7.5, 1.8)):
        g.poly([(x - r, y + 0.2), (x - 0.3, y - r), (x + r, y - 0.3), (x + 0.4, y + r)], "fx", 3)
        g.dot(x - 0.6, y - r * 0.5, "fx", 5)


def fissure(g):
    g.poly([(1.5, 9), (22.5, 7), (22.5, 21), (1.5, 21)], "fx", 2)
    g.lit("fx")
    pts = [(2, 20), (7, 15), (9, 16.5), (14, 11), (16, 12), (22, 6)]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        g.seg(x0, y0, x1, y1, 2.6, "fx2", 4)
        g.seg(x0, y0, x1, y1, 1.0, "fx2", 5)


def paw(g):
    g.ell(12, 15.5, 5.6, 4.8, "fx")
    g.ell(9.5, 18.5, 2.6, 2.2, "fx")
    g.ell(14.5, 18.5, 2.6, 2.2, "fx")
    for (x, y) in ((4.8, 10), (9, 6), (15, 6), (19.2, 10)):
        g.ell(x, y, 2.5, 2.9, "fx")
        g.poly([(x - 1, y - 2.4), (x + 0.3, y - 5), (x + 1.1, y - 2.2)], "bone", 4)
    g.ball("fx", lo=1)


def fang(g):
    g.ring(12, 4, 11, 7.5, "fx", 3, a0=20, a1=160, ry=0.9)
    g.lit("fx")
    for (x, h) in ((6.5, 14.5), (17.5, 14.5)):
        g.poly([(x - 2.8, 8), (x + 2.8, 8), (x + 0.4 * (1 if x > 12 else -1), 8 + h * 0.85)], "bone", 4)
    for x in (9.8, 12, 14.2):
        g.poly([(x - 1.1, 9.5), (x + 1.1, 9.5), (x, 12.5)], "bone", 3)
    g.ball("bone", lo=2)


def claw(g):
    for k, ox in enumerate((-5, 0, 5)):
        x0, y0, x1, y1 = 7 + ox, 3, 15 + ox, 21
        g.poly([(x0, y0), (x0 + 2.8, y0 + 3), ((x0 + x1) / 2 + 1.4, (y0 + y1) / 2), (x1, y1),
                ((x0 + x1) / 2 - 0.6, (y0 + y1) / 2 + 0.4)], "fx", 4 if k == 0 else 3)
        g.seg(x0 + 1.4, y0 + 2.2, (x0 + x1) / 2, (y0 + y1) / 2, 0.7, "fx", 5)


def plus(g):
    g.rect(8.5, 3, 15.5, 21, "fx")
    g.rect(3, 8.5, 21, 15.5, "fx")
    g.lit("fx", band=2)
    g.rect(9.5, 4, 11, 9.5, "fx", 5)
    g.rect(4, 9.5, 9.5, 11, "fx", 5)


def wings(g):
    """A pair of raised wings: three long primaries fanning up and out from each shoulder, lit on the left wing."""
    for side in (-1, 1):
        sx = 12 + side * 1.6
        for k, (ang, ln, w) in enumerate(((-62, 10.5, 3.2), (-35, 10.0, 3.0), (-8, 8.5, 2.8), (18, 6.5, 2.4))):
            a = math.radians(ang)
            dx, dy = math.cos(a) * side, math.sin(a)
            sy = 13 + k * 1.6
            sh = (4 if k % 2 == 0 else 3) - (1 if side > 0 else 0)
            g.seg(sx, sy, sx + dx * ln, sy + dy * ln, w, "fx", sh, w1=0.9)
    g.ell(12, 13.5, 1.8, 4.5, "fx2", 4)
    g.dot(11.4, 11, "fx2", 5)

def mask(g):
    g.poly([(3.5, 6), (12, 4), (20.5, 6), (19.5, 14), (15, 20), (9, 20), (4.5, 14)], "fx")
    g.ball("fx", lo=2, spec=False)
    for x in (8.2, 15.8):
        g.poly([(x - 2.6, 10.5), (x, 9), (x + 2.6, 10.5), (x, 12.5)], "dark", 0)
    g.ring(12, 13.5, 4.2, 3.0, "dark", 0, a0=20, a1=160)
    g.poly([(3.5, 6), (1.5, 3.5), (5.5, 5)], "fx2", 4)
    g.poly([(20.5, 6), (22.5, 3.5), (18.5, 5)], "fx2", 4)


def spiral(g):
    prev = None
    for t in np.linspace(0, 3.2 * math.pi, 60):
        r = 1.0 + t * 0.92
        p = (12 + math.cos(t) * r, 12 + math.sin(t) * r)
        if prev:
            g.seg(prev[0], prev[1], p[0], p[1], 2.0 if t > 3 else 1.6, "fx", 4 if math.sin(t) < 0 else 3)
        prev = p
    g.ell(12, 12, 1.4, 1.4, "fx", 5)
    _twinkle(g, 19.5, 4.5, "fx2", 2.2)


def portal(g):
    def arch(x, y, inset):
        if y < 8:
            return (x - 12) ** 2 + (y - 8) ** 2 <= (7.5 - inset) ** 2
        return abs(x - 12) <= 7.5 - inset and y <= 21 - inset * 0.3
    g.fill(lambda x, y: arch(x, y, 0) and not arch(x, y, 2), "fx", 3, (3, 0, 21, 22))
    g.lit("fx")
    g.fill(lambda x, y: arch(x, y, 2), "dark", 1, (3, 0, 21, 22))
    sub = G(g.c, g.k, g.s * 0.5, g.ox + 6 * g.s, g.oy + 7.2 * g.s, g.m)
    spiral(sub)
    g.rect(2.5, 20.5, 21.5, 22.5, "fx", 2)


def book(g):
    g.poly([(5, 3), (18, 3), (19.5, 4.5), (19.5, 21), (6.5, 21), (5, 19.5)], "bone", 4)
    g.poly([(4, 3.5), (17, 3.5), (17, 20), (4, 20)], "fx")
    g.lit("fx", band=2)
    g.rect(4, 3.5, 6, 20, "fx", 2)
    g.rect(17, 5, 19.5, 20.5, "bone", 3)
    for y in (7, 10, 13, 16):
        g.seg(17.3, y, 19.3, y, 0.5, "bone", 2)
    g.ell(11.5, 11.5, 3.2, 3.2, "fx2", 4)
    g.ell(11, 11, 1.2, 1.2, "fx2", 5)


def die(g):
    top = [(12, 2.5), (20.5, 7), (12, 11.5), (3.5, 7)]
    g.poly(top, "fx", 4)
    g.poly([(3.5, 7), (12, 11.5), (12, 21.5), (3.5, 17)], "fx", 3)
    g.poly([(12, 11.5), (20.5, 7), (20.5, 17), (12, 21.5)], "fx", 2)
    g.ell(12, 7, 1.4, 0.9, "dark", 0)
    for (x, y) in ((6, 11), (9.5, 16.5), (14.5, 13), (18, 11.5), (14.5, 18.5), (18, 16.5)):
        g.ell(x, y, 1.0, 1.1, "dark", 0)


def tornado(g):
    for k, (y, w) in enumerate(((4, 9.5), (7.5, 8), (11, 6.2), (14.5, 4.5), (18, 3), (21, 1.6))):
        cx = 12 + math.sin(k * 0.9) * 1.6
        g.ell(cx, y, w, 1.9, "fx", 4 if k % 2 == 0 else 3)
        g.seg(cx - w * 0.6, y - 0.6, cx + w * 0.2, y - 0.6, 0.6, "fx", 5)


def tree(g):
    g.poly([(10.5, 21.5), (11, 12), (13, 12), (13.5, 21.5)], "wood", 3)
    g.seg(12, 15, 8, 12, 1.2, "wood", 3)
    g.seg(12.5, 14, 16, 11, 1.2, "wood", 3)
    g.lit("wood")
    for (cx, cy, r) in ((8, 10, 4.6), (16, 10, 4.6), (12, 6.5, 5.5), (12, 11, 4.5)):
        g.ell(cx, cy, r, r * 0.9, "fx")
    g.ball("fx", bb=(3, 1, 21, 15), lo=1)
    g.rect(5, 21, 19, 22.5, "fx", 1)


def flower(g):
    for k in range(5):
        a = math.radians(-90 + k * 72)
        g.ell(12 + math.cos(a) * 5.5, 11 + math.sin(a) * 5.5, 3.8, 3.8, "fx")
    g.ball("fx", bb=(2, 1, 22, 21), lo=2)
    g.ell(12, 11, 2.8, 2.8, "fx2", 4)
    g.dot(11, 10, "fx2", 5)
    g.seg(12, 16, 12, 22, 1.4, "leaf", 3)


def burst(g):
    g.poly(_star(12, 12, 11, 5.0, 8, rot=-90), "fx", 3)
    g.poly(_star(12, 12, 7.2, 3.6, 8, rot=-67.5), "fx", 4)
    g.ell(12, 12, 3.0, 3.0, "fx", 5)


def shatter(g):
    for k in range(8):
        a = math.radians(k * 45 + 20)
        r0, r1 = (5.5, 10.5) if k % 2 == 0 else (6.0, 9.0)
        ca, sa = math.cos(a), math.sin(a)
        w = 2.0 if k % 2 == 0 else 1.5
        g.poly([(12 + ca * r0 - sa * w, 12 + sa * r0 + ca * w), (12 + ca * r1, 12 + sa * r1),
                (12 + ca * r0 + sa * w, 12 + sa * r0 - ca * w)], "fx", 4 if sa < 0.2 else 3)
    g.poly(_star(12, 12, 4.8, 1.8, 4, rot=-45), "fx2", 4)
    g.ell(12, 12, 1.3, 1.3, "fx2", 5)


def ankh(g):
    g.ring(12, 7.5, 5, 2.5, "fx", 3, ry=1.25)
    g.rect(10.4, 12, 13.6, 21.5, "fx")
    g.rect(4.5, 11.5, 19.5, 14.5, "fx")
    g.lit("fx", band=2)


def halo(g):
    g.ring(12, 6, 8, 5.8, "fx", 4, ry=0.45)
    g.ring(12, 6, 8, 5.8, "fx", 5, a0=180, a1=300, ry=0.45)
    g.poly(_star(12, 15.5, 7, 2.4, 4), "fx", 4)
    g.dot(11.5, 15, "fx", 5, 1.4)
    for (x, y) in ((4, 13), (20, 17)):
        g.dot(x, y, "fx", 4)


def bomb(g):
    g.ell(11, 14, 7.5, 7.5, "dark")
    g.ball("dark", lo=1, hi=4)
    g.rect(13, 5, 17, 8, "metal", 3)
    g.seg(15, 5, 17.5, 2.5, 1.0, "wood", 4)
    _twinkle(g, 18.5, 2.5, "fx2", 2.6)
    g.dot(7, 10, "dark", 5, 1.5)


def trap(g):
    g.ring(12, 13, 10, 8.4, "fx", 3, a0=180, a1=360, ry=0.85)
    g.ring(12, 13, 10, 8.4, "fx", 2, a0=0, a1=180, ry=0.85)
    for k in range(5):
        x = 5 + k * 3.5
        g.poly([(x - 1.3, 6.8 + abs(k - 2) * 0.6), (x + 1.3, 6.8 + abs(k - 2) * 0.6), (x, 11.5)], "fx", 4)
        g.poly([(x - 1.3, 19.2 - abs(k - 2) * 0.6), (x + 1.3, 19.2 - abs(k - 2) * 0.6), (x, 14.5)], "fx", 3)
    g.ell(12, 13, 2, 1.3, "fx2", 4)


def hood(g):
    g.poly([(12, 2), (19, 7), (21, 21.5), (3, 21.5), (5, 7)], "fx")
    g.lit("fx", band=2)
    g.ell(12, 12, 5, 5.8, "dark", 0)
    g.rect(7, 12, 17, 20, "dark", 0)
    for x in (9.8, 14.2):
        g.rect(x - 0.9, 11.2, x + 0.9, 12.6, "fx2", 5)


def wisp(g):
    def f(x, y):
        if y < 10:
            return (x - 12) ** 2 + (y - 10) ** 2 <= 7 ** 2
        return abs(x - 12) <= 7 - (y - 10) * 0.08 and y <= 19 + math.sin(x * 1.3) * 2
    g.fill(f, "fx", 3, (4, 2, 20, 22))
    g.ball("fx", bb=(5, 3, 19, 21), lo=2)
    for x in (9.5, 14.5):
        g.ell(x, 9.5, 1.3, 1.8, "dark", 0)
    g.ell(12, 14, 1.4, 1.6, "dark", 1)


def bell(g):
    g.poly([(12, 2.5), (16.5, 5.5), (17.5, 14), (21, 18), (3, 18), (6.5, 14), (7.5, 5.5)], "metal")
    g.lit("metal", band=2)
    g.rect(3, 17, 21, 18.8, "metal", 4)
    g.ell(12, 20.2, 2, 1.8, "metal", 2)
    g.ring(12, 11, 11.5, 10.3, "fx", 4, a0=200, a1=240)
    g.ring(12, 11, 11.5, 10.3, "fx", 4, a0=300, a1=340)
    g.dot(9, 6, "metal", 5)


def beam(g):
    g.seg(2.5, 21.5, 18, 6, 5.2, "fx", 2, w1=2.4)
    g.seg(2.5, 21.5, 18, 6, 3.0, "fx", 4, w1=1.4)
    g.seg(3, 21, 18, 6, 1.0, "fx", 5)
    g.poly(_star(18.5, 5.5, 5, 1.6, 4, rot=-45), "fx", 4)
    g.ell(18.5, 5.5, 1.6, 1.6, "fx", 5)


def comet(g):
    g.poly([(3, 21), (12, 9.5), (17.5, 15)], "fx", 2)
    g.poly([(6, 19), (13, 11), (15.5, 13.5)], "fx", 4)
    g.ell(15, 9, 6, 6, "fx", 3)
    g.ball("fx", bb=(9, 3, 21, 15), lo=3)
    g.ell(16, 8, 2.4, 2.4, "fx", 5)


def missiles(g):
    for (ox, oy, s) in ((-3.5, -3.5, 0.55), (5.5, -1, 0.55), (-1, 5.5, 0.55)):
        sub = G(g.c, g.k, g.s * s, g.ox + (ox + 5.4) * g.s, g.oy + (oy + 5.4) * g.s, g.m)
        comet(sub)


def cone(g):
    g.poly([(3, 12), (21.5, 3), (21.5, 21)], "fx", 2)
    g.poly([(3, 12), (21, 6), (21, 18)], "fx", 3)
    g.poly([(3, 12), (17, 9), (17, 15)], "fx", 4)
    g.poly([(3, 12), (11, 10.8), (11, 13.2)], "fx", 5)
    for (x, y) in ((19, 4.5), (15, 18), (20, 12)):
        g.dot(x, y, "fx", 5)


def potion(g):
    g.rect(10, 2.5, 14, 4.5, "wood", 4)
    g.rect(10.5, 4.5, 13.5, 8.5, "bone", 4)
    g.ell(12, 14.5, 7, 7, "bone", 4)
    g.ell(12, 15.5, 6, 5.5, "fx")
    g.fill(lambda x, y: y < 12 and (x - 12) ** 2 + (y - 14.5) ** 2 < 36, "bone", 4, (5, 8, 19, 13))
    g.ball("fx", bb=(6, 10, 18, 21), lo=1)
    g.dot(8, 11, "bone", 5, 1.5)


def banner(g):
    g.seg(5, 2, 5, 22, 1.8, "wood", 3)
    g.ell(5, 2, 1.4, 1.4, "gold", 4)
    g.poly([(6, 3.5), (20, 3.5), (18, 9), (20, 14.5), (6, 14.5)], "fx")
    g.lit("fx", band=2)
    g.poly(_star(12, 9, 3.5, 1.4, 4), "gold", 4)


def wall(g):
    for row in range(5):
        y = 7 + row * 3
        off = 0 if row % 2 == 0 else 2.5
        for k in range(-1, 4):
            x = 3 + off + k * 5
            g.rect(max(3, x), y, min(21, x + 4.5), y + 2.5, "fx", 3 if (row + k) % 2 else 4)
    g.rect(3, 22, 21, 22.6, "fx", 1)


def zzz(g):
    for (x, y, s, sh) in ((3, 13, 7, 4), (12, 8, 5.2, 4), (17.5, 3, 4, 5)):
        g.rect(x, y, x + s, y + s * 0.22 + 0.4, "fx", sh)
        g.seg(x + s, y + s * 0.2, x, y + s - 0.2, s * 0.24 + 0.4, "fx", sh)
        g.rect(x, y + s - s * 0.22, x + s, y + s + 0.4, "fx", sh - 1)


def bubble(g):
    g.poly([(3, 4), (21, 4), (21, 15), (12, 15), (7, 20.5), (8, 15), (3, 15)], "fx", 4)
    g.lit("fx")
    for (x0, y0, x1, y1) in ((6, 7, 9, 12), (9, 12, 12, 7), (12, 7, 15, 12), (15, 12, 18, 7)):
        g.seg(x0, y0, x1, y1, 1.4, "fx2", 1)


def target(g):
    g.ring(12, 12, 9.5, 7.3, "fx", 3)
    g.ring(12, 12, 5, 3, "fx", 4)
    g.lit("fx")
    for (x0, y0, x1, y1) in ((12, 0.5, 12, 6), (12, 18, 12, 23.5), (0.5, 12, 6, 12), (18, 12, 23.5, 12)):
        g.seg(x0, y0, x1, y1, 1.6, "fx", 4)
    g.ell(12, 12, 1.4, 1.4, "fx", 5)


def nope(g):
    g.ring(12, 12, 9.5, 7, "fx", 3)
    g.seg(6.2, 6.2, 17.8, 17.8, 2.6, "fx", 3)
    g.lit("fx")


def _chevrons(g, flip):
    for k, x in enumerate((5, 12)):
        pts = [(x, 4), (x + 4, 4), (x + 10, 12), (x + 4, 20), (x, 20), (x + 6, 12)]
        if flip:
            pts = [(24 - px, py) for px, py in pts]
        g.poly(pts, "fx", 4 if k else 3)
    g.lit("fx")


def push(g):
    _chevrons(g, False)


def pull(g):
    _chevrons(g, True)


def up(g):
    g.poly([(12, 2.5), (21, 12), (15.5, 12), (15.5, 21.5), (8.5, 21.5), (8.5, 12), (3, 12)], "fx")
    g.lit("fx", band=2)


def down(g):
    g.poly([(12, 21.5), (21, 12), (15.5, 12), (15.5, 2.5), (8.5, 2.5), (8.5, 12), (3, 12)], "fx")
    g.lit("fx", band=2)


def leap(g):
    g.ring(12, 17, 9, 6.8, "fx", 4, a0=180, a1=330)
    g.poly([(18.5, 8.5), (22.5, 15.5), (15.5, 14.5)], "fx", 4)
    g.lit("fx")
    g.rect(2, 19.5, 22, 21.5, "fx", 2)


def spin(g):
    """Two tapering crescents chasing each other round: a whirling cut."""
    for start in (200, 20):
        def f(x, y, start=start):
            d = math.hypot(x - 12, y - 12)
            a = (math.degrees(math.atan2(y - 12, x - 12)) - start) % 360
            if a > 170:
                return False
            t = a / 170
            return 10.5 - 3.6 * t <= d <= 10.5 if start == 200 else 10.5 - 3.6 * t <= d <= 10.5
        g.fill(f, "fx", 4 if start == 200 else 3)
    g.poly([(3.4, 7.2), (1.5, 13.5), (6.5, 10.5)], "fx", 4)
    g.poly([(20.6, 16.8), (22.5, 10.5), (17.5, 13.5)], "fx", 3)
    g.ell(12, 12, 1.6, 1.6, "fx", 5)


def club(g):
    g.seg(5, 20, 15, 8, 2.4, "fx", 3, w1=5.5)
    g.ell(16, 7, 3.6, 3.6, "fx")
    g.lit("fx", band=2)
    g.ell(18.5, 11, 2, 1.4, "leaf", 4)
    g.ell(12.5, 4.5, 2, 1.4, "leaf", 4)
    g.ring(12, 12, 11, 9.8, "fx2", 4, a0=200, a1=260)


def lotus(g):
    g.ell(12, 6, 2.8, 2.8, "skin")
    g.poly([(8, 9.5), (16, 9.5), (17, 16), (7, 16)], "fx")
    g.poly([(3.5, 17), (20.5, 17), (18, 20.5), (6, 20.5)], "fx", 2)
    g.lit("fx")
    g.lit("skin")
    g.ring(12, 12, 11, 9.8, "fx2", 4, a0=180, a1=360)
    g.ring(12, 12, 11, 9.8, "fx2", 3, a0=0, a1=180)
    g.ell(12, 12.5, 1.4, 1.4, "fx2", 5)


def tentacles(g):
    for (x0, sway, sh) in ((5, 1, 3), (12, -1, 4), (19, 1, 3)):
        prev = None
        for t in np.linspace(0, 1, 12):
            p = (x0 + math.sin(t * 4.5) * 2.4 * sway, 21.5 - t * 17)
            if prev:
                g.seg(prev[0], prev[1], p[0], p[1], 3.2 * (1 - t) + 0.9, "fx", sh)
            prev = p
    g.ell(12, 21.5, 10, 2, "dark", 0)
    for (x, y) in ((6, 15), (11, 11), (18, 13)):
        g.dot(x, y, "fx", 5)


GLYPHS = {f.__name__: f for f in (
    flame, snowflake, bolt, skull, sun, leaf, fist, fists, sword, swords, dagger, axe, hammer, spear, shield, shields,
    eye, eye_tri, heart, note, moon, star, sparkle, hand, chain, wave, wind, arrow, volley, footprints, boot, crown,
    cloud, web, thorn, vines, spikes, orb, metamagic, drop, crystal, hourglass, rune, rock, spire, quake, fissure, paw,
    fang, claw, plus, wings, mask, spiral, portal, book, die, tornado, tree, flower, burst, shatter, ankh, halo, bomb,
    trap, hood, wisp, bell, beam, comet, missiles, cone, potion, banner, wall, zzz, bubble, target, nope, push, pull,
    up, down, leap, spin, club, lotus, tentacles)}

# Material slots: glyph parts that keep their own colour whatever the spec says.
MATERIALS = {"gold": "gold", "ink": "ink", "wood": "wood", "skin": "skin", "bone": "bone", "dark": "dark",
             "leaf": "nature", "metal": "iron", "trim": "gold"}

# --------------------------------------------------------------------------------------------------------- specs
# "glyph:colour[,accent] [slot=ramp ...] [+ badge:colour[,accent] ...] [@area]". Keys are the ability's short name
# (after the dot), or its full id where two classes use one short name for different things.
SPECS: dict[str, str] = {
    # barbarian
    "rage": "flame:blood + fist:crimson", "reckless_attack": "skull:blood + axe:steel",
    "cleaving_blow": "axe:steel + push:steel", "savage_roar": "fang:blood + wave:fire",
    "crushing_leap": "quake:earth,fire + leap:fire", "whirlwind_slash": "spin:slash + axe:steel",
    "barbarian.earthshaker": "quake:earth,fire", "brutal_strike": "hammer:steel + burst:blood",
    "relentless_rage": "heart:blood + flame:fire",
    "frenzy_strike": "claw:blood + flame:blood", "retaliation": "axe:steel + burst:blood",
    "bloodlust_roar": "fang:blood + wave:psychic",
    "quake_slam": "hammer:earth + quake:earth,fire", "tectonic_rift": "fissure:earth,fire",
    "cataclysm": "spire:earth + burst:fire",
    "ancestral_ward": "wisp:spirit + shield:steel", "spirit_shield": "shield:spirit",
    "vengeful_ancestors": "wisp:spirit + sword:spirit",
    # bard
    "bardic_inspiration": "note:gold + star:gold", "song_of_rest": "note:heal + heart:heal",
    "countercharm": "note:charm + shield:charm", "vicious_mockery": "bubble:blood",
    "thunderclap": "hand:thunder + wave:thunder", "minor_illusion": "mask:charm,gold",
    "healing_word": "bubble:heal + plus:heal", "cure_wounds": "plus:heal + hand:heal",
    "dissonant_whispers": "bubble:psychic + wave:psychic", "faerie_fire": "sparkle:charm",
    "heroism": "heart:holy + star:gold", "thunderwave": "wave:thunder + push:thunder", "shatter": "shatter:thunder",
    "hold_person": "chain:psychic", "lesser_restoration": "drop:heal + plus:holy",
    "silence": "note:arcane + nope:blood", "hypnotic_pattern": "spiral:charm",
    "mass_healing_word": "bubble:heal + plus:heal @area", "dispel_magic": "rune:arcane + nope:blood",
    "greater_invisibility": "hood:mist,mist + sparkle:mist", "dimension_door": "portal:arcane,arcane",
    "hold_monster": "chain:psychic + fang:bone", "greater_restoration": "sun:holy + plus:heal",
    "combat_inspiration": "note:gold + sword:steel", "battle_magic": "sword:arcane + rune:arcane",
    "cutting_words": "bubble:gold + claw:blood", "lore.counterspell": "hand:arcane + nope:blood",
    "magical_secrets": "book:arcane,arcane + rune:arcane", "peerless_skill": "die:gold",
    "alluring_verse": "note:charm + heart:charm", "enthralling_aria": "note:charm + spiral:charm",
    "sirens_call": "wave:charm + pull:charm",
    # cleric
    "sacred_flame": "flame:radiant", "toll_the_dead": "bell:necrotic", "word_of_radiance": "sun:radiant + wave:radiant",
    "resistance": "shield:holy", "bless": "halo:holy", "bane": "halo:necrotic + down:blood",
    "guiding_bolt": "comet:radiant", "inflict_wounds": "skull:necrotic + hand:necrotic",
    "shield_of_faith": "shield:holy + sparkle:holy", "command": "bubble:holy + down:holy",
    "aid": "heart:holy + up:heal", "spiritual_weapon": "hammer:radiant + sparkle:radiant",
    "blindness": "eye:blood + nope:blood", "spirit_guardians": "wisp:radiant + spin:radiant",
    "bestow_curse": "skull:hex + down:hex", "banishment": "portal:void,void",
    "death_ward": "shield:holy + skull:bone", "flame_strike": "flame:fire + sun:radiant",
    "mass_cure_wounds": "plus:heal + hand:heal @area", "divine_spark": "star:holy + heart:heal",
    "turn_undead": "skull:bone + sun:holy", "sear_undead": "skull:bone + flame:radiant",
    "cleric.divine_intervention": "wings:holy,gold + plus:heal",
    "preserve_life": "heart:heal + sun:holy @area", "word_of_renewal": "ankh:holy + plus:heal",
    "mass_revival": "ankh:holy + sun:holy @area", "guided_strike": "target:holy + sword:steel",
    "war_priest": "hammer:steel + sun:holy", "avatar_of_battle": "crown:gold,blood + sword:steel",
    "radiance_of_the_dawn": "sun:radiant", "warding_flare": "burst:radiant + shield:holy",
    "corona_of_light": "halo:radiant + sun:radiant",
    # common, items
    "attack": "sword:steel", "dash": "boot:leather,wind + wind:wind", "disengage": "footprints:wind",
    "dodge": "shield:steel + wind:wind", "help": "hand:gold + up:heal", "shove": "hand:steel + push:steel",
    "use_item": "potion:arcane", "potion_of_healing": "potion:healing",
    "greater_healing": "potion:healing + star:gold", "alchemists_fire": "potion:fire + flame:fire",
    "smoke_bomb": "bomb:steel,fire + cloud:smoke",
    # druid
    "wild_shape_wolf": "paw:wolf + moon:moon", "wild_shape_bear": "paw:bear + leaf:nature",
    "wild_shape_dire_wolf": "fang:bone + paw:wolf", "thorn_whip": "thorn:nature,thorn + pull:nature",
    "produce_flame": "hand:fire + flame:fire", "shillelagh": "club:bark,nature", "entangle": "vines:nature,thorn",
    "fog_cloud": "cloud:mist", "spike_growth": "spikes:thorn", "barkskin": "shield:bark + leaf:nature",
    "moonbeam": "moon:moon + sparkle:moon", "call_lightning": "cloud:storm + bolt:lightning",
    "sleet_storm": "cloud:cold + snowflake:cold", "conjure_animals": "rune:nature + paw:wolf",
    "ice_storm": "cloud:cold + crystal:cold", "blight": "leaf:blight + skull:bone",
    "dire_form": "fang:blood + paw:wolf", "elder_bear_form": "paw:bear + crown:gold,blood",
    "primal_mend": "leaf:heal + plus:heal", "gale_burst": "wind:wind + push:wind",
    "chain_tempest": "bolt:lightning + chain:lightning", "wrath_of_the_tempest": "tornado:storm + bolt:lightning",
    "bloom": "flower:charm,gold + plus:heal", "sanctuary_grove": "tree:nature + plus:heal",
    "ancient_canopy": "tree:heal + sun:holy",
    # fighter
    "second_wind": "heart:heal + wind:wind", "action_surge": "bolt:haste + sword:steel",
    "trip_attack": "sword:steel + down:steel", "pushing_attack": "sword:steel + push:steel",
    "menacing_attack": "sword:steel + skull:bone", "precision_attack": "sword:steel + target:blood",
    "goading_attack": "sword:steel + bubble:blood", "rally": "banner:crimson + heart:heal",
    "feinting_attack": "sword:steel + mask:charm,gold", "charge": "shield:crimson + push:steel",
    "challenge": "shield:crimson + bubble:blood", "shield_wall": "shields:crimson",
    "mass_challenge": "banner:crimson + shield:steel", "cleave": "spin:slash + sword:steel",
    "blade_dance": "swords:steel + spin:slash", "execution_strike": "axe:steel + skull:blood",
    "commanders_strike": "bubble:gold + sword:steel", "maneuvering_order": "banner:gold + boot:leather,wind",
    "rallying_cry": "banner:crimson + up:heal", "to_arms": "banner:gold + swords:steel",
    # monk
    "unarmed_strike": "fist:ki", "martial_arts": "fist:ki + star:gold", "flurry_of_blows": "fists:ki",
    "heightened_flurry": "fists:ki + up:gold", "step_of_the_wind": "footprints:ki + wind:wind",
    "patient_defense": "lotus:ki,ki", "deflect_attacks": "hand:ki + arrow:steel",
    "stunning_strike": "fist:ki + bolt:lightning", "palm_strike": "hand:ki + push:ki",
    "sweeping_kick": "spin:ki + down:ki", "wholeness_of_body": "lotus:heal,heal + plus:heal",
    "quivering_palm": "hand:necrotic + wave:necrotic", "gale_leap": "leap:wind + wind:wind",
    "cyclone_kick": "tornado:wind + boot:ki", "tempest_dash": "wind:storm + bolt:lightning",
    "iron_skin": "shield:iron + fist:ki", "counter_palm": "hand:ki + burst:ki",
    "adamantine_form": "lotus:steel,gold + star:gold",
    # paladin
    "lay_on_hands": "hand:heal + heart:heal", "divine_smite": "sword:steel + burst:radiant",
    "divine_smite_2": "sword:steel + burst:radiant", "divine_smite_3": "sword:steel + burst:radiant",
    "compelled_duel": "swords:steel + target:blood", "elemental_weapon": "sword:steel + rune:wild",
    "sanctuary_aegis": "shield:holy + heart:heal", "righteous_challenge": "banner:holy + target:blood",
    "guardian.divine_intervention": "wings:holy,gold + shield:holy", "aegis_of_faith": "shield:holy + sun:holy @area",
    "searing_smite": "sword:steel + flame:fire", "consecrated_ground": "rune:radiant + flame:radiant",
    "dawnbreaker": "sun:radiant + sword:steel", "vow_of_enmity": "eye:blood + target:blood",
    "abjure_enemy": "hand:holy + skull:bone", "relentless_step": "footprints:radiant + sparkle:holy",
    "soul_of_vengeance": "wisp:blood + sword:steel",
    # races
    "fey_step": "spiral:charm,charm + leaf:nature", "stoneskin": "rock:stone + shield:steel",
    "hellish_rebuke": "flame:hell + skull:bone", "healing_hands": "hand:holy + plus:heal",
    "stones_endurance": "rock:stone + heart:heal", "breath_weapon": "cone:fire",
    # ranger
    "hunters_mark": "target:blood + paw:wolf", "vanish": "hood:nature,gold + leaf:nature",
    "ensnaring_strike": "vines:nature,thorn + arrow:steel", "hail_of_thorns": "volley:thorn + thorn:nature,thorn",
    "lightning_arrow": "arrow:lightning + bolt:lightning", "conjure_barrage": "volley:arcane + sparkle:arcane",
    "deadeye": "target:gold + arrow:steel", "piercing_shot": "beam:slash + arrow:steel", "volley": "volley:steel",
    "sic": "paw:fur + target:blood", "pack_bond": "paw:fur + heart:heal", "alpha_howl": "moon:moon + fang:bone",
    "vine_snare": "vines:nature,thorn + pull:nature", "natures_veil": "leaf:nature + shield:bark",
    "guardian_tree": "tree:nature + shield:bark",
    # rogue
    "cunning_dash": "boot:shadow,wind + wind:wind", "cunning_disengage": "footprints:shadow",
    "cunning_hide": "hood:shadow,gold", "steady_aim": "target:gold + eye:gold", "trip": "dagger:steel + down:steel",
    "poison_strike": "dagger:steel + drop:poison", "withdraw": "dagger:steel + footprints:wind",
    "daze": "dagger:steel + star:lightning", "knock_out": "dagger:steel + zzz:arcane",
    "obscure": "dagger:steel + cloud:smoke", "uncanny_dodge": "wind:shadow + dagger:steel",
    "shadowstep": "spiral:shadow,shadow + footprints:shadow", "cloak_of_shadows": "hood:void,mist + moon:shadow",
    "caltrops": "spikes:steel", "snap_trap": "trap:steel,blood", "flashbang": "bomb:steel,radiant + burst:radiant",
    "riposte": "swords:steel + burst:slash", "parry_stance": "sword:steel + shield:steel",
    "master_flourish": "sword:steel + star:gold",
    # sorcerer
    "innate_sorcery": "hand:wild + star:wild", "font_of_magic_mana": "drop:arcane + star:gold",
    "font_of_magic_sorcery": "drop:wild + star:gold", "metamagic_twinned": "metamagic:wild,gold + orb:wild,gold",
    "metamagic_extended": "metamagic:wild,gold + hourglass:time",
    "metamagic_careful": "metamagic:wild,gold + shield:wild",
    "metamagic_quickened": "metamagic:wild,gold + bolt:haste", "metamagic_distant": "metamagic:wild,gold + arrow:wild",
    "fire_bolt": "comet:fire", "ray_of_frost": "beam:cold + snowflake:cold",
    "shocking_grasp": "hand:lightning + bolt:lightning", "acid_splash": "drop:acid + burst:acid",
    "chill_touch": "hand:necrotic skin=bone + snowflake:cold", "poison_spray": "cone:poison",
    "chromatic_orb": "orb:wild,arcane + sparkle:arcane", "burning_hands": "cone:fire + hand:fire",
    "magic_missile": "missiles:arcane", "shield": "shield:arcane + sparkle:arcane",
    "sleep": "zzz:arcane + moon:moon", "scorching_ray": "beam:fire + flame:fire", "misty_step": "spiral:mist,mist",
    "web": "web:silk", "fireball": "burst:fire + flame:fire", "lightning_bolt": "bolt:lightning",
    "haste": "hourglass:haste + wind:haste", "slow": "hourglass:time + down:blood",
    "counterspell": "hand:arcane + nope:blood", "wall_of_fire": "wall:stone + flame:fire",
    "cone_of_cold": "cone:cold + snowflake:cold", "dragon_breath": "cone:fire + wings:blood,gold",
    "dragon_wings": "wings:blood,gold", "tides_of_chaos": "die:wild + spiral:wild",
    "wild_surge": "spiral:wild,wild + burst:wild", "bend_luck": "die:wild + hand:wild",
    "controlled_surge": "orb:wild,gold + burst:wild", "ember_rune": "rune:fire", "frost_rune": "rune:cold",
    "grand_sigil": "rune:rune + star:gold",
    # warlock
    "eldritch_blast": "comet:eldritch", "hex": "eye_tri:hex,blood", "fiendish_vigor": "heart:hell + up:hell",
    "maddening_hex": "eye_tri:psychic,psychic + spiral:psychic", "one_with_shadows": "moon:shadow + hood:shadow,gold",
    "shroud_of_shadow": "hood:void,eldritch + sparkle:eldritch", "arms_of_hadar": "tentacles:void",
    "armor_of_agathys": "shield:cold + snowflake:cold", "witch_bolt": "bolt:eldritch",
    "darkness": "cloud:void", "hunger_of_hadar": "eye:void + tentacles:void",
    "vampiric_touch": "fang:blood + drop:blood", "shadow_of_moil": "hood:void,fire + flame:void",
    "synaptic_static": "burst:psychic + bolt:psychic", "hexblades_curse": "sword:hex + eye_tri:hex,blood",
    "cursed_slash": "sword:hex + drop:blood", "armor_of_hexes": "shield:hex + eye_tri:hex,blood",
    "hellfire_surge": "flame:hell + burst:hell", "hurl_through_hell": "portal:hell,hell + flame:fire",
    "rift_step": "portal:void,void + footprints:void", "gravitational_pull": "orb:void,eldritch + pull:void",
    "entropic_ward": "shield:void + spiral:void", "abyssal_singularity": "spiral:void,eldritch + orb:void,eldritch",
    # wizard
    "mage_armor": "shield:arcane + rune:arcane", "ray_of_sickness": "beam:poison + skull:poison",
    "grease": "drop:grease + down:steel", "blur": "mask:mist,mist + wind:mist",
    "invisibility": "hood:mist,mist", "cloudkill": "cloud:poison + skull:poison",
    "arcane_recovery": "book:arcane,arcane + up:arcane", "sculpted_nova": "burst:force + shield:steel",
    "sculpted_inferno": "burst:fire + shield:steel", "time_shift": "hourglass:time + spiral:time,time",
    "accelerate": "hourglass:time + up:time", "temporal_stasis": "hourglass:time + chain:time",
    "raise_earth": "spire:earth + up:earth", "sink_earth": "quake:earth,fire + down:earth",
    "tremor": "quake:earth,fire + wave:earth", "stone_spire": "spire:earth",
}

# Signature and cantrip abilities, hand-picked for the per-class sheet's top row: the ones players see most.
SIGNATURE = {
    "barbarian": ["rage", "reckless_attack", "whirlwind_slash", "earthshaker"],
    "bard": ["bardic_inspiration", "vicious_mockery", "healing_word", "hypnotic_pattern"],
    "cleric": ["sacred_flame", "guiding_bolt", "bless", "spirit_guardians"],
    "druid": ["wild_shape_bear", "thorn_whip", "entangle", "call_lightning"],
    "fighter": ["second_wind", "action_surge", "shield_wall", "blade_dance"],
    "monk": ["flurry_of_blows", "stunning_strike", "patient_defense", "quivering_palm"],
    "paladin": ["lay_on_hands", "divine_smite", "searing_smite", "aegis_of_faith"],
    "ranger": ["hunters_mark", "volley", "hail_of_thorns", "deadeye"],
    "rogue": ["cunning_hide", "poison_strike", "shadowstep", "flashbang"],
    "sorcerer": ["fire_bolt", "chromatic_orb", "fireball", "metamagic_quickened"],
    "warlock": ["eldritch_blast", "hex", "arms_of_hadar", "hunger_of_hadar"],
    "wizard": ["fire_bolt", "magic_missile", "fireball", "misty_step"],
}


# ---------------------------------------------------------------------------------------------------- inference
_DAMAGE_GLYPH = {"fire": "flame", "cold": "snowflake", "lightning": "bolt", "thunder": "wave", "acid": "drop",
                 "poison": "drop", "necrotic": "skull", "psychic": "eye", "radiant": "sun", "force": "burst",
                 "bludgeoning": "hammer", "piercing": "spear", "slashing": "sword"}
_DAMAGE_RAMP = {"bludgeoning": "steel", "piercing": "steel", "slashing": "steel"}
_CONDITION_GLYPH = {"prone": "down", "frightened": "skull", "charmed": "heart", "paralysed": "chain",
                    "restrained": "vines", "blinded": "nope", "stunned": "star", "slowed": "hourglass",
                    "poisoned": "drop", "burning": "flame", "hidden": "hood", "invisible": "hood"}


def _infer(ab: dict) -> str:
    """A spec from the ability's own data, for abilities the table doesn't name yet: its damage type decides the
    glyph and colour, a condition it inflicts becomes the badge, and buffs, heals and moves get their family glyph."""
    s = json.dumps(ab["effects"])
    dmg = next((t for t in _DAMAGE_GLYPH if f'"type": "{t}"' in s), None)
    cond = next((c for c in _CONDITION_GLYPH if f'"apply": "{c}"' in s), None)
    tags = ab.get("ai", {}).get("tags", [])
    if dmg:
        main = f"{_DAMAGE_GLYPH[dmg]}:{_DAMAGE_RAMP.get(dmg, dmg)}"
    elif "heal" in tags:
        main = "plus:heal"
    elif '"teleport"' in s:
        main = "spiral:mist"
    elif "move" in tags or "escape" in tags:
        main = "footprints:wind"
    elif "defend" in tags:
        main = "shield:steel"
    elif "buff" in tags:
        main = "up:gold"
    elif "control" in tags:
        main = "chain:psychic"
    else:
        main = "sword:steel" if ab.get("isWeaponAttack") else "star:arcane"
    if cond and _CONDITION_GLYPH[cond] != main.split(":")[0]:
        return f"{main} + {_CONDITION_GLYPH[cond]}:{'blood' if cond in ('frightened', 'blinded') else 'arcane'}"
    return main


@dataclass
class Layer:
    glyph: str
    fx: str
    fx2: str
    overrides: dict


@dataclass
class Spec:
    main: Layer
    badge: Layer | None
    area: bool


def parse(spec: str) -> Spec:
    area = "@area" in spec
    parts = [p.strip() for p in spec.replace("@area", "").split("+")]
    layers = []
    for p in parts:
        words = p.split()
        g, _, cols = words[0].partition(":")
        fx, _, fx2 = (cols or "steel").partition(",")
        ov = dict(w.split("=") for w in words[1:])
        if g not in GLYPHS:
            raise KeyError(f"unknown glyph {g!r} in {spec!r}")
        for r in (fx, fx2 or "gold", *ov.values()):
            if r not in R:
                raise KeyError(f"unknown colour {r!r} in {spec!r}")
        layers.append(Layer(g, fx, fx2 or "gold", ov))
    return Spec(layers[0], layers[1] if len(layers) > 1 else None, area)


# ---------------------------------------------------------------------------------------------------- compositor
_DIGITS = {1: (".#.", "##.", ".#.", ".#.", "###"), 2: ("##.", "..#", ".#.", "#..", "###"),
           3: ("##.", "..#", ".#.", "..#", "##."), 4: ("#.#", "#.#", "###", "..#", "..#"),
           5: ("###", "#..", "##.", "..#", "##.")}


def _plate(c: Canvas, k: int) -> None:
    """The class-tinted backing: a dark well that brightens towards the top-left (the light), with a 1 px lit bevel
    and chamfered corners, so the glyph sits in a lit recess rather than on a flat swatch."""
    n = 24 * k
    for y in range(n):
        for x in range(n):
            if min(x, n - 1 - x) + min(y, n - 1 - y) < k:          # chamfer
                continue
            d = math.hypot((x + 0.5) / k - 9, (y + 0.5) / k - 8) / 17
            sh = 2 if d < 0.55 else 1 if d < 0.95 else 0
            c.px(x, y, "plate", sh)
    for t in range(n):
        for (x, y, sh) in ((t, 0, 3), (0, t, 3), (t, n - 1, 0), (n - 1, t, 0)):
            if c.slot[y, x]:
                c.px(x, y, "plate", sh)
    for t in range(1, n - 1):
        for (x, y, sh) in ((t, 1, 2), (1, t, 2)):
            if c.slot[y, x] and c.shade[y, x] < 2:
                c.px(x, y, "plate", sh)
    c.px(1, 1, "plate", 4)
    c.px(2, 1, "plate", 4)
    c.px(1, 2, "plate", 4)


def _glow(c: Canvas, k: int, cx: float, cy: float, r: float) -> None:
    """A halo of the glyph's deepest shade behind it (magic only): one flat disc, since a dithered rim read as grime."""
    for y in range(c.h):
        for x in range(c.w):
            if c.slot[y, x] != c.slots.id("plate"):
                continue
            d = math.hypot((x + 0.5) / k - cx, (y + 0.5) / k - cy) / r
            if d < 0.85:
                c.px(x, y, "glow", 0)


def _yellow(r: list[str]) -> bool:
    """Golds and yellows darken to mud, so their glyphs go without a halo (the plate's own light is enough)."""
    import colorsys
    h, _, _ = colorsys.rgb_to_hsv(*(int(r[3][i:i + 2], 16) / 255 for i in (1, 3, 5)))
    return 0.07 < h < 0.2


def _layer_canvas(layer: Layer, k: int, s: float, ox: float, oy: float, fx: str, fx2: str) -> Canvas:
    c = Canvas(24 * k, 24 * k, SLOTS)
    GLYPHS[layer.glyph](G(c, k, s, ox, oy, {"fx": fx, "fx2": fx2}))
    c.outline()
    return c


def _tab(c: Canvas, k: int, tier: int) -> None:
    """The tier tab: a gold corner flag top-right with the spell level in a 3x5 digit."""
    n = 24 * k
    w, h = 7 * k, 8 * k
    x0 = n - w
    for y in range(h):
        for x in range(x0, n):
            if (x - x0) + (h - 1 - y) < k:                      # chamfer the bottom-left corner
                continue
            edge = y >= h - k or x < x0 + k
            c.px(x, y, "gold", 2 if edge else (4 if y < 2 * k else 3))
    for x in range(x0 - 1, n):
        c.px(x, h, "dark", 0)
    for y in range(h + 1):
        c.px(x0 - 1, y, "dark", 0)
    for r, row in enumerate(_DIGITS[tier]):
        for q, ch in enumerate(row):
            if ch == "#":
                c.rect(x0 + (2 + q) * k, (1 + r) * k + k // 2, x0 + (3 + q) * k - 1, (2 + r) * k + k // 2 - 1, "ink", 1)


def _react(c: Canvas, k: int) -> None:
    """The reaction hook, bottom-left: a gold curved arrow on a dark chip, 'this fires on someone else's turn'."""
    pts = ["..###..", ".#...#.", "#.....#", "#.#....", "###....", ".......", "......."]
    oy = 24 - 7
    c.rect(0, oy * k, 7 * k - 1, 24 * k - 1, "dark", 1)
    for r, row in enumerate(pts):
        for q, ch in enumerate(row):
            if ch == "#":
                c.rect(q * k, (oy + 1 + r) * k, q * k + k - 1, (oy + 2 + r) * k - 1, "gold", 4 if r < 2 else 3)


def _area(c: Canvas, k: int) -> None:
    """Three gold dots bottom-left: the area version of a single-target spell."""
    for (x, y) in ((1, 21), (4, 21), (2.5, 18.5)):
        c.rect(int(x * k), int(y * k), int(x * k) + 2 * k - 1, int(y * k) + 2 * k - 1, "gold", 4)
        c.px(int(x * k) + 2 * k - 1, int(y * k) + 2 * k - 1, "gold", 2)


def compose(spec: Spec, cls: str, tier: int, react: bool, k: int) -> Image.Image:
    c = Canvas(24 * k, 24 * k, SLOTS)
    _plate(c, k)
    main, badge = spec.main, spec.badge
    s, ox, oy = (0.84, 0.0, 0.0) if badge else (1.0, 0.0, 0.0)
    if main.fx not in MUNDANE and not _yellow(R[main.fx]):
        _glow(c, k, ox + 12 * s, oy + 12 * s, 11 * s)
    c.blit(_layer_canvas(main, k, s, ox, oy, "fx", "fx2"))
    if badge:
        c.blit(_layer_canvas(badge, k, 0.54, 11.4, 11.4, "bx", "bx2"))
    if tier:
        _tab(c, k, tier)
    if react:
        _react(c, k)
    elif spec.area:
        _area(c, k)
    pal = {"plate": PLATES[cls], "glow": R[main.fx], "gold": R["gold"], "ink": R["ink"], "fx": R[main.fx],
           "fx2": R[main.fx2], "bx": R[badge.fx] if badge else R["gold"], "bx2": R[badge.fx2] if badge else R["gold"],
           **{slot: R[r] for slot, r in MATERIALS.items() if slot not in ("gold", "ink")}}
    for ov in [main.overrides] + ([badge.overrides] if badge else []):
        pal.update({slot: R[r] for slot, r in ov.items()})
    return c.render(pal)


# ---------------------------------------------------------------------------------------------------------- data
@dataclass
class Ability:
    id: str
    name: str
    cls: str
    tier: int
    react: bool
    spec: str
    tuned: bool


def abilities() -> list[Ability]:
    """Every player ability (monsters.json is for enemies), in rules-pack order, with its class plate and spec."""
    out = []
    for f in sorted(RULES.glob("*.json")):
        if f.stem == "monsters":
            continue
        cls = {"common": "common", "races": "race"}.get(f.stem, f.stem)
        for ab in json.loads(f.read_text())["abilities"]:
            aid = ab["id"]
            c = "item" if aid.startswith("item.") else cls
            short = aid.split(".", 1)[1]
            spec = SPECS.get(aid) or SPECS.get(short)
            tags = ab.get("ai", {}).get("tags", [])
            out.append(Ability(aid, ab["name"], c, int(ab.get("tier", 0)), any(t.startswith("reactsTo") for t in tags),
                               spec or _infer(ab), spec is not None))
    return out


def key_of(ability_id: str) -> str:
    """The art key for an ability's icon: the icons set plus the ability id, unchanged (`icons.wizard.fire_bolt`)."""
    return f"icons.{ability_id}"
