"""Icons for the phone controller and the TV HUD: abilities, status effects, items, and the rarity frames that hold
them.

Icons are UI, not world sprites, so they ship baked to colour (no runtime recolouring) on transparent backgrounds:
the UI draws them inside a frame. They are drawn in (slot, shade) like everything else, though, so one drawing
becomes several icons by palette swap: every potion is the same bottle, every gem the same cut.

Readability rules, because phones show these at 2-3x and the TV at 3-4x:
  * one bold silhouette per icon, filling most of the cell, with a 1 px margin left for the selective outline;
  * light from the top-left: a lit rim on top/left edges, a shadow band bottom-right, and one strong specular;
  * colour carries the class flavour (holy gold, arcane blue, fire orange, nature green, shadow purple, blood red),
    but the shape alone must still say what the icon is.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from typing import Callable

import numpy as np
from PIL import Image, ImageDraw

from arcpix.canvas import Canvas, SlotTable
from arcpix.palette import BASE, CLOTH, FAMILIES, LEATHER, MAGIC, METAL, SKIN, WOOD, ramp
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import Tile, contact_sheet, font, scale, stack

ICON = 24          # abilities and items
STATUS = 16        # status effects, shown inline next to HP bars
FRAME = 26         # rarity frame: a 1 px ring around a 24 px icon

SLOTS = SlotTable()
for _s in ("metal", "trim", "wood", "leather", "cloth", "skin", "bone", "fx", "fx2", "core", "liquid", "glass", "gem",
           "paper", "dark"):
    SLOTS.id(_s)

# Ramps only icons need. Effects are bright (low spread) so they glow against the navy UI.
RAMPS: dict[str, list[str]] = {
    "slash": ramp("#cfe0f2", shift=0.05, spread=0.9),
    "impact": ramp("#ffb347", shift=0.08, spread=0.85),
    "poison": ramp("#8fd14a", shift=0.07, spread=0.9),
    "ice": ramp("#8fd6f2", shift=0.05, spread=0.85),
    "lightning": ramp("#ffdc5a", shift=0.06, spread=0.8),
    "eldritch": ramp("#c062f0", shift=0.07, spread=0.95),
    "hex": ramp("#9a4fd6", shift=0.07),
    "mist": ramp("#a7b6ec", shift=0.05, spread=0.8),
    "charm": ramp("#ff7fb6", shift=0.06, spread=0.9),
    "ki": ramp("#56d6c2", shift=0.06, spread=0.9),
    "heal": ramp("#63d36f", shift=0.06, spread=0.9),
    "vine": ramp("#4f9a45", shift=0.08),
    "thorn": ramp("#a8c060", shift=0.06),
    "fur": ramp("#9a6a44", shift=0.06),
    "glass": ramp("#c4dbe8", shift=0.04, spread=0.9),
    "parchment": ramp("#e3cf9e", shift=0.05, spread=0.85),
    "rope": ramp("#c49a5e", shift=0.06),
    "meat": ramp("#b5523c", shift=0.07),
    "goblin": SKIN["goblin"],
    "target": ramp("#e2534c", shift=0.07),
    "haste": ramp("#ffd447", shift=0.06, spread=0.85),
    "shell": ramp("#b07a4a", shift=0.06),
    "snail": ramp("#a8c08a", shift=0.06),
    "earth": ramp("#8c5c3a", shift=0.06),
    "bandage": ramp("#e9e2cf", shift=0.05, spread=0.75),
    # liquids and gems: one drawing each, many palettes
    "healing": ramp("#e0384a", shift=0.07),
    "mana": ramp("#3f7fe8", shift=0.06),
    "antidote": ramp("#6fcf4a", shift=0.07),
    "fire_res": ramp("#ff8a2d", shift=0.08),
    "ruby": ramp("#d8304a", shift=0.07, spread=1.1),
    "sapphire": ramp("#3c6fe0", shift=0.06, spread=1.1),
    "emerald": ramp("#2fb86a", shift=0.07, spread=1.1),
    "amethyst": ramp("#9a52d6", shift=0.07, spread=1.1),
    # rarity edges and the navy panel they sit on
    "common": ramp("#9aa0ad", shift=0.04, spread=1.1),
    "uncommon": ramp("#4fc76a", shift=0.06),
    "rare": ramp("#3e9bff", shift=0.05),
    "epic": ramp("#b06cff", shift=0.06),
    "legendary": ramp("#ffb43a", shift=0.07, spread=1.1),
    "panel": ramp("#1e2244", shift=0.03, spread=0.7),
    "ui_gold": ramp("#e6bd5c", shift=0.06, spread=1.1),
    "ui_gold_2": ramp("#a07c33", shift=0.06),
}

DEFAULT: dict[str, list[str]] = {
    "metal": METAL["steel"], "trim": METAL["gold"], "wood": WOOD["oak"], "leather": LEATHER["brown"],
    "cloth": CLOTH["crimson"], "skin": SKIN["fair"], "bone": SKIN["bone"], "fx": MAGIC["arcane"],
    "fx2": MAGIC["holy"], "core": MAGIC["holy"], "liquid": RAMPS["healing"], "glass": RAMPS["glass"],
    "gem": RAMPS["ruby"], "paper": RAMPS["parchment"], "dark": CLOTH["charcoal"],
}


def _r(ref: str) -> list[str]:
    """A ramp by name: an icon ramp ("poison") or a shared one ("magic/fire")."""
    if "/" in ref:
        fam, name = ref.split("/")
        return FAMILIES[fam][name]
    return RAMPS[ref]


# ----------------------------------------------------------------------------------------------------- drawing kit
LIGHT = (-0.55, -0.65, 0.53)
"""Towards the light: up-left and out of the screen (x right, y down, z towards the viewer)."""


def _inside(m: np.ndarray, dx: int, dy: int) -> np.ndarray:
    """inside[y, x] = m[y + dy, x + dx]; False off the canvas."""
    h, w = m.shape
    out = np.zeros_like(m)
    out[max(0, -dy):min(h, h - dy), max(0, -dx):min(w, w - dx)] = \
        m[max(0, dy):min(h, h + dy), max(0, dx):min(w, w + dx)]
    return out


def _box(m: np.ndarray, box) -> np.ndarray:
    if box is None:
        return m
    x0, y0, x1, y1 = box
    b = np.zeros_like(m)
    b[y0:y1 + 1, x0:x1 + 1] = True
    return m & b


def light(c: Canvas, slot: str, *, box=None, band: int = 1, rim: int = 1) -> None:
    """Lights a flat slot from the top-left: top/left edge pixels +rim, bottom/right edge pixels -1, and with
    band=2 a second, inner shadow row (for thick forms). Only base-shade pixels change, so details survive. `box`
    limits it to one part when two parts share a slot."""
    m = _box(c.mask(slot), box)
    flat = m & (c.flags == 0) & (c.shade == BASE)
    lit = ~_inside(m, 0, -1) | ~_inside(m, -1, 0)
    dark = ~_inside(m, 0, 1) | ~_inside(m, 1, 0)
    sh = c.shade.astype(np.int16)
    sh[flat & dark & ~lit] = BASE - 1
    sh[flat & lit & ~dark] = BASE + rim
    if band > 1:
        dark2 = (~_inside(m, 0, 2) | ~_inside(m, 2, 0)) & ~dark & ~lit
        sh[flat & dark2] = BASE - 1
        sh[flat & dark & ~lit] = BASE - 2
    c.shade = np.clip(sh, 0, 5).astype(np.uint8)


def ball(c: Canvas, slot: str, *, box=None, lo: int = 1, hi: int = 5, spec: bool = True) -> None:
    """Rounded shading over a slot's bounding ellipse (orbs, bottles, hearts, gems' bodies): the shade follows a
    sphere's normal lit from the top-left, quantised to the ramp, with a specular glint near the top-left."""
    m = _box(c.mask(slot), box) & (c.flags == 0)
    ys, xs = np.nonzero(m)
    if not len(xs):
        return
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    cx, cy, rx, ry = (x0 + x1) / 2, (y0 + y1) / 2, max(1.0, (x1 - x0) / 2), max(1.0, (y1 - y0) / 2)
    lx, ly, lz = LIGHT
    for y, x in zip(ys, xs):
        nx, ny = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
        nz = math.sqrt(max(0.0, 1 - min(1.0, nx * nx + ny * ny)))
        i = lx * nx + ly * ny + lz * nz
        s = 1 if i < -0.25 else 2 if i < 0.1 else 3 if i < 0.45 else 4 if i < 0.8 else 5
        if not spec and s == 5:
            s = 4
        c.shade[y, x] = max(lo, min(hi, s))


def blade(c: Canvas, x0: int, y0: int, x1: int, y1: int, slot: str = "metal", *, width: int = 3) -> None:
    """A 45-degree blade or haft from (x0, y0) to (x1, y1): a lit edge on the side facing up, a dark edge below.
    width 3 = sword, 2 = dagger/thin haft, 1 = a single line."""
    n = abs(x1 - x0)
    sx, sy = (1 if x1 >= x0 else -1), (1 if y1 >= y0 else -1)
    for t in range(n + 1):
        x, y = x0 + sx * t, y0 + sy * t
        if width >= 2 and t < n:
            c.px(x, y - 1, slot, 5)
        if width >= 3 and t < n:
            c.px(x, y + 1, slot, 2)
        c.px(x, y, slot, 4 if width >= 2 else 3)


def haft(c: Canvas, x0: int, y0: int, x1: int, y1: int, slot: str = "wood") -> None:
    """A 2 px wooden shaft along a 45-degree line: lit top, dark underside."""
    n = abs(x1 - x0)
    sx, sy = (1 if x1 >= x0 else -1), (1 if y1 >= y0 else -1)
    for t in range(n + 1):
        x, y = x0 + sx * t, y0 + sy * t
        c.px(x, y, slot, 4)
        c.px(x, y + 1, slot, 2)


def poly(c: Canvas, pts, slot: str, shade: int = BASE, *, at=(0.0, 0.0), ang: float = 0.0) -> None:
    """A polygon in local coordinates, rotated by `ang` degrees and moved to `at` (shapes drawn upright once)."""
    a = math.radians(ang)
    ca, sa = math.cos(a), math.sin(a)
    c.polygon([(at[0] + x * ca - y * sa, at[1] + x * sa + y * ca) for x, y in pts], slot, shade)


def ring(c: Canvas, cx: float, cy: float, r_out: float, r_in: float, slot: str, shade: int = BASE, *,
         ry_scale: float = 1.0) -> None:
    for y in range(int(cy - r_out - 2), int(cy + r_out + 3)):
        for x in range(int(cx - r_out - 2), int(cx + r_out + 3)):
            dx, dy = x + 0.5 - cx, (y + 0.5 - cy) / ry_scale
            d = math.hypot(dx, dy)
            if r_in < d <= r_out:
                c.px(x, y, slot, shade)


def star(c: Canvas, cx: float, cy: float, r_out: float, r_in: float, n: int, slot: str, shade: int = BASE, *,
         rot: float = -90.0) -> None:
    pts = []
    for k in range(n * 2):
        r = r_out if k % 2 == 0 else r_in
        a = math.radians(rot + k * 180.0 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    c.polygon(pts, slot, shade)


def twinkle(c: Canvas, x: int, y: int, slot: str, size: int = 2, shade: int = 5) -> None:
    """A four-point sparkle: a bright centre and arms that fade outwards."""
    c.px(x, y, slot, shade)
    for k in range(1, size + 1):
        s = max(2, shade - k)
        for dx, dy in ((k, 0), (-k, 0), (0, k), (0, -k)):
            c.px(x + dx, y + dy, slot, s)


def flame(c: Canvas, cx: float, by: float, w: float, h: float, slot: str = "fx", *, lean: float = 0.0,
          tongues: bool = True) -> None:
    """A teardrop flame standing on (cx, by): a dark outer layer, a bright body and a near-white core, each a
    smaller teardrop sitting lower, so the heat reads as rising from the base."""
    for k, (sc, sh) in enumerate(((1.0, 2), (0.74, 4), (0.44, 5))):
        ww, hh = w * sc, h * sc
        b = by - (1 - sc) * w * 0.35
        tip = (cx + lean * hh, b - hh)
        pts = [(cx + ww * math.cos(a), b - ww + ww * math.sin(a)) for a in np.linspace(0, math.pi, 9)]
        pts += [(cx - ww * 1.0, b - ww - (hh - ww) * 0.2), (cx - ww * 0.55 + lean * hh * 0.5, b - ww - (hh - ww) * 0.6),
                tip, (cx + ww * 0.75 + lean * hh * 0.35, b - ww - (hh - ww) * 0.45), (cx + ww, b - ww)]
        c.polygon(pts, slot, sh)
        if tongues and k == 0:
            c.polygon([(cx - ww * 0.9, b - ww), (cx - ww * 1.25 + lean * hh * 0.3, b - hh * 0.7),
                       (cx - ww * 0.3, b - ww * 1.4)], slot, sh)
            c.polygon([(cx + ww * 0.95, b - ww * 0.8), (cx + ww * 1.2 + lean * hh * 0.4, b - hh * 0.62),
                       (cx + ww * 0.35, b - ww * 1.5)], slot, sh)


def heart(c: Canvas, cx: float, cy: float, r: float, slot: str) -> None:
    """A heart: two lobes and a point. (cx, cy) is the dip between the lobes."""
    c.ellipse(cx - r * 0.92, cy + r * 0.25, r, r, slot)
    c.ellipse(cx + r * 0.92, cy + r * 0.25, r, r, slot)
    c.polygon([(cx - r * 1.9, cy + r * 0.55), (cx + r * 1.9, cy + r * 0.55), (cx, cy + r * 2.55)], slot)


def new(size: int = ICON) -> Canvas:
    return Canvas(size, size, SLOTS)


# ------------------------------------------------------------------------------------------------------ parts
def sword(c: Canvas, px: int, py: int, length: int = 11, *, guard: int = 3, d=(1, -1), grip: int = 3) -> None:
    """A sword from its pommel at (px, py) along a 45-degree direction d: pommel, grip, crossguard, blade."""
    dx, dy = d
    c.rect(px - 1, py - 1 if dy < 0 else py, px, py if dy < 0 else py + 1, "trim", 4)
    c.px(px - 1 if dx > 0 else px, py if dy < 0 else py + 1, "trim", 2)
    for t in range(1, grip + 1):
        c.px(px + dx * t, py + dy * t, "leather", 3)
        c.px(px + dx * t, py + dy * t + 1, "leather", 2)
    gx, gy = px + dx * (grip + 1), py + dy * (grip + 1)
    for k in range(-guard, guard + 1):                 # crossguard runs perpendicular to the blade
        x, y = gx + k * dx, gy - k * dy
        c.px(x, y, "trim", 4 if k * dx <= 0 else 3)
        c.px(x, y + 1, "trim", 2)
    c.px(gx, gy, "trim", 5)
    sx, sy = gx + dx, gy + dy
    blade(c, sx, sy, sx + dx * length, sy + dy * length)


def fist(c: Canvas, x: int, y: int, slot: str = "skin", wrap: str | None = "cloth") -> None:
    """A clenched fist seen from the front, knuckles up: four fingers, a thumb across, a wrapped wrist. 11x12."""
    for k in range(4):
        c.rect(x + k * 3, y + (1 if k in (0, 3) else 0), x + k * 3 + 2, y + 4, slot)
    c.rect(x, y + 4, x + 11, y + 8, slot)
    light(c, slot, box=(x, y, x + 11, y + 8))
    for k in range(4):
        c.px(x + k * 3, y + (1 if k in (0, 3) else 0), slot, 5)
        c.px(x + k * 3 + 2, y + 2, slot, 2)
        c.px(x + k * 3 + 2, y + 3, slot, 2)
    c.rect(x + 1, y + 6, x + 7, y + 7, slot, 4)          # thumb across the fingers
    c.rect(x + 1, y + 8, x + 7, y + 8, slot, 2)
    c.px(x + 1, y + 6, slot, 5)
    if wrap:
        c.rect(x + 2, y + 9, x + 10, y + 11, wrap, 3)
        c.rect(x + 2, y + 9, x + 10, y + 9, wrap, 4)
        c.px(x + 10, y + 10, wrap, 2)
        c.px(x + 10, y + 11, wrap, 2)
        c.line(x + 3, y + 11, x + 6, y + 11, wrap, 2)


def skull(c: Canvas, x: int, y: int, slot: str = "bone") -> None:
    """A skull, 12 wide x 12 tall: dome, dark sockets, nose notch, a row of teeth."""
    c.ellipse(x + 6, y + 5.5, 6, 5.5, slot)
    c.rect(x + 2, y + 8, x + 9, y + 11, slot)
    ball(c, slot, box=(x, y, x + 11, y + 11), lo=2)
    for ex in (x + 2, x + 7):
        c.rect(ex, y + 5, ex + 2, y + 7, "dark", 0)
    c.px(x + 5, y + 8, "dark", 0)
    c.px(x + 6, y + 8, "dark", 0)
    for tx in (x + 3, x + 5, x + 7):
        c.px(tx, y + 10, slot, 4)
        c.px(tx + 1, y + 10, "dark", 1)
    c.line(x + 3, y + 11, x + 8, y + 11, slot, 2)


def shield(c: Canvas, x: int, y: int, w: int = 12, h: int = 15, *, face: str = "cloth", rim: str = "trim",
           emblem: bool = True) -> None:
    """A heater shield: a metal rim around a painted face with a gold cross (Kara's shield from the party)."""
    half = w / 2
    pts = [(x, y), (x + w, y), (x + w, y + h * 0.45), (x + half + w * 0.18, y + h * 0.85), (x + half, y + h),
           (x + half - w * 0.18, y + h * 0.85), (x, y + h * 0.45)]
    c.polygon(pts, rim)
    inner = [(x + 1.6, y + 1.6), (x + w - 1.6, y + 1.6), (x + w - 1.6, y + h * 0.45),
             (x + half + w * 0.13, y + h * 0.8), (x + half, y + h - 2), (x + half - w * 0.13, y + h * 0.8),
             (x + 1.6, y + h * 0.45)]
    c.polygon(inner, face)
    light(c, rim, box=(x - 1, y - 1, x + w + 1, y + h + 1))
    ball(c, face, box=(x, y, x + w, y + h), lo=2, spec=False)
    if emblem:
        mx = int(x + half - 0.5)
        c.rect(mx, y + 3, mx + 1, y + h - 4, rim, 4)
        c.rect(x + 3, y + 5, x + w - 3, y + 6, rim, 4)
        c.px(mx + 1, y + 7, rim, 2)
    c.px(x + 1, y + 1, rim, 5)
    c.px(x + 2, y + 1, rim, 5)


def arrow(c: Canvas, x0: int, y0: int, x1: int, y1: int, *, fletch: str = "cloth", head: str = "metal") -> None:
    """An arrow along a 45-degree line, tail (x0, y0) to tip (x1, y1): a barbed head big enough to read at 1x."""
    sx, sy = (1 if x1 >= x0 else -1), (1 if y1 >= y0 else -1)
    n = abs(x1 - x0)
    for t in range(2, n - 1):
        c.px(x0 + sx * t, y0 + sy * t, "wood", 4)
    tx, ty = x1 + 0.5, y1 + 0.5
    bx, by = tx - sx * 4.2, ty - sy * 4.2
    c.polygon([(tx + sx * 0.6, ty + sy * 0.6), (bx + sy * 2.4, by - sx * 2.4), (bx + sx * 1.3, by + sy * 1.3),
               (bx - sy * 2.4, by + sx * 2.4)], head, 3)
    light(c, head, box=(min(x1, x1 - sx * 6), min(y1, y1 - sy * 6), max(x1, x1 - sx * 6), max(y1, y1 - sy * 6)))
    c.px(x1, y1, head, 5)
    c.px(x1 - sx, y1 - sy, head, 5)
    for k in range(3):                             # fletching: two feathers either side of the nock
        fx, fy = x0 + sx * k, y0 + sy * k
        c.px(fx - sx, fy, fletch, 4)
        c.px(fx, fy - sy, fletch, 3)
    c.px(x0, y0, fletch, 2)


def eye(c: Canvas, cx: int, cy: int, w: int, slot: str = "fx", iris: str = "core", *, lid: float = 0.32) -> None:
    """An almond eye, w wide: two arcs, a round iris and a slit pupil."""
    hw = w / 2
    for x in range(int(cx - hw), int(cx + hw) + 1):
        t = (x + 0.5 - cx) / hw
        hh = max(0.0, (1 - t * t)) * (w * lid)
        for y in range(int(cy - hh), int(cy + hh) + 1):
            c.px(x, y, slot, 4 if y < cy else 3)
    c.ellipse(cx, cy, w * 0.2, w * 0.2, iris, 4)
    c.px(cx - 1, cy - 1, iris, 5)
    c.rect(cx, cy - 1, cx, cy + 1, "dark", 0)


def axe(c: Canvas, x: int = 0) -> None:
    """An upright battle axe, haft at x + 14: a crescent bit to the left, a short spike behind, a wrapped grip."""
    hx = x + 14
    c.rect(hx, 3, hx + 1, 22, "wood", 3)
    light(c, "wood")
    c.rect(hx, 17, hx + 1, 21, "leather", 3)
    for y in (17, 19, 21):
        c.line(hx, y, hx + 1, y, "leather", 4)
    bit = [(hx, 4), (x + 10, 5), (x + 6, 1.5), (x + 3.5, 4), (x + 2, 8), (x + 3.5, 12), (x + 6, 14.5), (x + 10, 11),
           (hx, 11)]
    c.polygon(bit, "metal")
    c.polygon([(hx + 2, 6), (hx + 6, 7.5), (hx + 2, 9)], "metal")
    light(c, "metal", band=2)
    for (px, py) in ((x + 6, 2), (x + 5, 3), (x + 4, 4), (x + 3, 6), (x + 3, 7), (x + 3, 8), (x + 3, 9)):
        c.px(px, py, "metal", 5)                          # the honed edge catches the light
    c.rect(hx - 1, 5, hx + 2, 10, "trim", 3)
    c.line(hx - 1, 5, hx + 2, 5, "trim", 4)
    c.px(hx - 1, 6, "trim", 5)
    c.line(hx + 2, 6, hx + 2, 10, "trim", 2)


# ---------------------------------------------------------------------------------------------- ability icons
def a_slash() -> Canvas:
    """A blade sweeping up-left, its path left behind as a tapered crescent of light."""
    c = new()
    for y in range(24):
        for x in range(24):
            a = math.hypot(x + 0.5 - 15.5, y + 0.5 - 15.5) / 14.0
            b = math.hypot(x + 0.5 - 18.5, y + 0.5 - 18.5) / 14.5
            if a < 1.0 and b > 1.0:
                e = 1.0 - a
                c.px(x, y, "fx", 5 if e < 0.07 else 4 if e < 0.14 else 3 if e < 0.2 else 2)
    sword(c, 20, 20, 9, guard=2, d=(-1, -1), grip=2)
    return c


def a_heavy_strike() -> Canvas:
    """A two-handed maul coming down on a burst: the weight is in the oversized head."""
    c = new()
    star(c, 17, 17, 7.0, 3.0, 8, "fx", 3, rot=-67.5)
    star(c, 17, 17, 4.2, 1.9, 8, "fx", 4, rot=-90)
    c.ellipse(17, 17, 1.6, 1.6, "fx", 5)
    haft(c, 2, 21, 9, 14)
    poly(c, [(-5.5, -3.6), (5.5, -3.6), (5.5, 3.6), (-5.5, 3.6)], "metal", at=(11, 10), ang=45)
    light(c, "metal", band=2)
    for u in (-3.6, 3.6):
        poly(c, [(u - 0.8, -4.1), (u + 0.8, -4.1), (u + 0.8, 4.1), (u - 0.8, 4.1)], "trim", 3, at=(11, 10), ang=45)
    light(c, "trim")
    c.px(8, 5, "metal", 5)
    c.px(7, 6, "metal", 5)
    return c


def a_cleave() -> Canvas:
    """A broad axe over a wide sweep: cleave hits everything in the arc."""
    c = new()
    for y in range(24):
        for x in range(24):
            d = math.hypot((x + 0.5 - 12) / 11.5, (y + 0.5 - 9) / 12.5)
            if 0.8 < d < 1.0 and y > 12:
                e = 1.0 - d
                c.px(x, y, "fx", 5 if e < 0.06 else 4 if e < 0.12 else 3 if e < 0.16 else 2)
    axe(c, 2)
    return c


def a_shield_bash() -> Canvas:
    """A shield driving forward: speed lines behind it, an impact flash where it lands."""
    c = new()
    for k, (y, x0, x1) in enumerate(((7, 1, 5), (11, 0, 4), (15, 1, 5))):
        c.line(x0, y, x1, y, "fx", 4 if k != 1 else 5)
    shield(c, 5, 4, 12, 16)
    star(c, 20, 10, 3.6, 1.4, 4, "fx2", 4, rot=-90)
    c.px(20, 10, "fx2", 5)
    c.px(21, 15, "fx2", 4)
    c.px(22, 5, "fx2", 4)
    return c


def a_shield_wall() -> Canvas:
    """Three shields locked edge to edge: a wall, not a single block."""
    c = new()
    shield(c, 1, 6, 10, 13, face="cloth", emblem=False)
    shield(c, 13, 6, 10, 13, face="cloth", emblem=False)
    shield(c, 6, 4, 12, 16, face="cloth")
    return c


def a_thrust() -> Canvas:
    """A spear driven forward: a long leaf-shaped point, speed lines along the shaft, a spark at the tip."""
    c = new()
    for x0, y0, n in ((1, 14, 4), (8, 21, 4), (4, 9, 2)):
        c.line(x0, y0, x0 + n, y0 - n, "fx", 4)
    haft(c, 2, 21, 14, 9)
    poly(c, [(0, -6.5), (2.6, -1.0), (0, 2.5), (-2.6, -1.0)], "metal", at=(16.5, 7.5), ang=45)
    light(c, "metal", band=2)
    c.line(15, 8, 18, 5, "metal", 5)
    c.rect(12, 10, 13, 11, "trim", 4)
    c.px(13, 11, "trim", 2)
    twinkle(c, 21, 2, "fx2", 1)
    return c


def a_sneak_attack() -> Canvas:
    """A dagger striking out of a shadow: the purple crescent is the rogue's darkness."""
    c = new()
    c.ellipse(11, 11, 10, 10, "fx")
    for y in range(24):
        for x in range(24):
            if math.hypot(x + 0.5 - 15, y + 0.5 - 8) < 8.5:
                c.clear(x, y)
    ball(c, "fx", lo=1, hi=4, spec=False)
    sword(c, 17, 5, 8, guard=2, d=(-1, 1), grip=2)
    c.px(7, 16, "liquid", 3)
    c.px(6, 18, "liquid", 4)
    c.px(6, 19, "liquid", 2)
    return c


def a_dash() -> Canvas:
    """A boot in full stride with speed streaks behind it."""
    c = new()
    for y, x0, x1 in ((6, 1, 6), (10, 0, 5), (14, 2, 6), (18, 1, 4)):
        c.line(x0, y, x1, y, "fx", 4)
    pts = [(10, 3), (16, 3), (16, 12), (21, 15), (22, 18), (21, 20), (9, 20), (8, 16), (10, 12)]
    c.polygon(pts, "leather")
    light(c, "leather", band=2)
    c.rect(9, 3, 16, 5, "leather", 4)
    c.line(10, 3, 15, 3, "leather", 5)
    c.rect(9, 19, 21, 20, "dark", 2)
    c.line(9, 19, 20, 19, "dark", 3)
    c.line(16, 12, 20, 15, "leather", 4)
    c.rect(11, 8, 15, 9, "trim", 3)
    c.px(11, 8, "trim", 5)
    # little wings: a swift boot
    c.polygon([(8, 7), (2, 3), (3, 6), (1, 7), (4, 9), (2, 10), (8, 11)], "fx2", 4)
    c.px(3, 4, "fx2", 5)
    return c


def a_hide() -> Canvas:
    """A deep hood with only the eyes showing."""
    c = new()
    c.polygon([(12, 1), (19, 6), (21, 14), (22, 22), (2, 22), (3, 14), (5, 6)], "cloth")
    light(c, "cloth", band=2)
    c.px(11, 2, "cloth", 5)
    c.px(10, 3, "cloth", 5)
    c.ellipse(12, 12, 5.5, 6, "dark", 0)
    c.rect(8, 16, 16, 22, "dark", 0)
    c.rect(9, 11, 10, 11, "fx", 5)
    c.rect(14, 11, 15, 11, "fx", 5)
    c.px(9, 12, "fx", 3)
    c.px(15, 12, "fx", 3)
    c.line(9, 22, 9, 17, "cloth", 2)
    c.line(15, 22, 15, 17, "cloth", 2)
    return c


def a_poisoned_blade() -> Canvas:
    """A dagger whose edge is coated green, venom dripping off it."""
    c = new()
    sword(c, 3, 20, 11, guard=2, grip=2)
    for t in range(2, 11):                              # the lower edge glistens with venom
        x, y = 7 + t, 16 - t
        c.px(x, y + 1, "fx", 3 if t % 3 else 4)
    for x, n in ((11, 4), (15, 3), (18, 2)):
        y = 16 - (x - 7) + 2
        c.rect(x, y, x, y + n, "fx", 3)
        c.px(x, y, "fx", 4)
        c.ellipse(x + 0.5, y + n + 1.5, 1.4, 1.6, "fx", 3)
        c.px(x, y + n + 1, "fx", 5)
    c.ellipse(13, 21.5, 3.0, 1.0, "fx", 2)
    c.px(12, 21, "fx", 4)
    return c


def a_arrow_shot() -> Canvas:
    c = new()
    arrow(c, 3, 20, 20, 3)
    for x0, y0 in ((2, 14), (8, 20)):
        c.line(x0, y0, x0 + 3, y0 - 3, "fx", 4)
    return c


def a_volley() -> Canvas:
    """Three arrows raining down together."""
    c = new()
    for k, dx in enumerate((-7, 0, 7)):
        arrow(c, 4 + dx, 2 + k * 2 - 2 * (k == 2), 4 + dx + 13, 15 + k * 2 - 2 * (k == 2))
    return c


def a_aimed_shot() -> Canvas:
    """A target with an arrow in the bullseye."""
    c = new()
    ring(c, 12, 12, 10.5, 0, "cloth", 3)
    ring(c, 12, 12, 8.0, 0, "paper", 4)
    ring(c, 12, 12, 5.5, 0, "cloth", 3)
    ring(c, 12, 12, 3.0, 0, "paper", 4)
    ball(c, "cloth", spec=False)
    c.ellipse(12, 12, 1.6, 1.6, "cloth", 2)
    arrow(c, 22, 2, 13, 11)
    return c


def a_fireball() -> Canvas:
    """A roaring ball of fire with a tail streaming up-right: it is flying."""
    c = new()
    flame(c, 15, 15, 5.0, 14, "fx", lean=0.55)
    c.ellipse(10, 14, 7.5, 7.5, "fx", 3)
    ball(c, "fx", box=(2, 6, 17, 22), lo=2)
    c.ellipse(9, 13, 4.5, 4.5, "fx", 4)
    c.ellipse(8, 12, 2.5, 2.5, "fx", 5)
    for x, y in ((4, 20), (17, 21), (2, 10)):
        c.px(x, y, "fx", 4)
    return c


def a_ice_shard() -> Canvas:
    """A cluster of ice crystals: long faceted shards, each split into a lit and a shaded face."""
    c = new()
    for (bx, by, tx, ty, w) in ((11, 22, 9, 1, 3.6), (13, 22, 21, 6, 3.0), (9, 22, 2, 9, 2.8), (12, 22, 16, 13, 2)):
        ang = math.atan2(ty - by, tx - bx)
        nx, ny = -math.sin(ang), math.cos(ang)
        mid = (bx + (tx - bx) * 0.75, by + (ty - by) * 0.75)
        left = [(bx + nx * w * 0.6, by + ny * w * 0.6), (mid[0] + nx * w, mid[1] + ny * w), (tx, ty), (bx, by)]
        right = [(bx, by), (tx, ty), (mid[0] - nx * w, mid[1] - ny * w), (bx - nx * w * 0.6, by - ny * w * 0.6)]
        lit_left = nx < 0 or ny < 0
        c.polygon(left, "fx", 4 if lit_left else 2)
        c.polygon(right, "fx", 2 if lit_left else 4)
        c.line(bx, by, tx, ty, "fx", 5)
    c.ellipse(12, 21.5, 6, 1.6, "fx", 3)
    return c


def a_lightning_bolt() -> Canvas:
    c = new()
    pts = [(14, 1), (6, 12), (11, 12), (7, 23), (19, 9), (13, 9), (18, 1)]
    c.polygon(pts, "fx", 4)
    light(c, "fx")
    c.line(14, 3, 9, 10, "fx", 5)
    c.line(11, 14, 9, 19, "fx", 5)
    for x, y in ((3, 6), (20, 15), (21, 4)):
        twinkle(c, x, y, "fx", 1, 4)
    return c


def a_magic_missile() -> Canvas:
    """Three glowing darts on curving trails, converging on one point."""
    c = new()
    darts = [((2, 21), (9, 12), (17, 7)), ((2, 8), (9, 9), (15, 4)), ((9, 22), (16, 17), (20, 10))]
    for (ax, ay), (bx, by), (tx, ty) in darts:
        prev = None
        for k in range(16):
            t = k / 15
            x = (1 - t) ** 2 * ax + 2 * (1 - t) * t * bx + t * t * tx
            y = (1 - t) ** 2 * ay + 2 * (1 - t) * t * by + t * t * ty
            if prev:
                c.thick_line(prev[0], prev[1], x, y, 2 if t > 0.35 else 1, "fx", 2 if t < 0.45 else 3)
            prev = (x, y)
    for (_, _, (tx, ty)) in darts:
        star(c, tx + 0.5, ty + 0.5, 3.4, 1.4, 4, "fx", 4, rot=-45)
        c.rect(tx, ty, tx, ty, "fx", 5)
        c.px(tx - 1, ty, "fx", 5)
    return c


def a_heal() -> Canvas:
    """A glowing green cross with sparkles: restore hit points."""
    c = new()
    c.rect(9, 3, 14, 20, "fx")
    c.rect(3, 9, 20, 14, "fx")
    light(c, "fx", band=2)
    c.rect(10, 4, 11, 12, "fx", 5)
    c.rect(4, 10, 11, 11, "fx", 5)
    twinkle(c, 19, 4, "core", 2)
    twinkle(c, 4, 19, "core", 1)
    twinkle(c, 20, 19, "core", 1, 4)
    return c


def a_bless() -> Canvas:
    """A golden halo over a radiant star."""
    c = new()
    ring(c, 12, 5, 8.5, 5.6, "fx", 3, ry_scale=0.42)
    light(c, "fx")
    c.line(7, 2, 10, 2, "fx", 5)
    star(c, 12, 15, 7.8, 2.6, 4, "core", 4)
    star(c, 12, 15, 4.8, 2.0, 4, "core", 4, rot=-45)
    c.ellipse(12, 15, 2, 2, "core", 5)
    c.line(12, 8, 12, 12, "core", 5)
    c.line(5, 15, 9, 15, "core", 5)
    return c


def a_revive() -> Canvas:
    """A golden ankh: life returned."""
    c = new()
    ring(c, 12, 7, 6.2, 3.0, "trim", 3, ry_scale=1.1)
    c.rect(4, 12, 19, 15, "trim")
    c.rect(10, 12, 13, 22, "trim")
    c.polygon([(9, 22), (14, 22), (13, 19), (10, 19)], "trim")
    light(c, "trim", band=2)
    c.line(8, 3, 10, 2, "trim", 5)
    c.px(7, 4, "trim", 5)
    c.line(5, 13, 9, 13, "trim", 5)
    c.line(11, 16, 11, 20, "trim", 4)
    twinkle(c, 20, 4, "fx2", 2)
    twinkle(c, 3, 19, "fx2", 1)
    return c


def a_smite() -> Canvas:
    """A sword plunging point-down through a burst of holy light."""
    c = new()
    star(c, 11.5, 17, 9.5, 3.0, 8, "fx2", 3, rot=-90)
    star(c, 11.5, 17, 5.5, 2.2, 8, "fx2", 4, rot=-67.5)
    c.ellipse(11.5, 17, 2.4, 2.4, "fx2", 5)
    c.rect(11, 1, 12, 2, "trim", 4)
    c.rect(11, 3, 12, 5, "leather", 3)
    c.px(12, 4, "leather", 2)
    c.rect(7, 6, 16, 7, "trim", 4)
    c.line(7, 7, 16, 7, "trim", 2)
    c.px(11, 6, "trim", 5)
    c.rect(10, 8, 13, 18, "metal", 4)
    c.rect(10, 8, 10, 18, "metal", 5)
    c.rect(13, 8, 13, 18, "metal", 2)
    c.rect(11, 19, 12, 20, "metal", 4)
    c.px(11, 21, "metal", 5)
    c.line(11, 9, 11, 16, "metal", 5)
    return c


def a_turn_undead() -> Canvas:
    """A skull cracking under radiant rays."""
    c = new()
    for k in range(8):
        a = math.radians(-90 + k * 45)
        c.line(12 + math.cos(a) * 7, 12 + math.sin(a) * 7, 12 + math.cos(a) * 11, 12 + math.sin(a) * 11, "fx2", 4)
    skull(c, 6, 6)
    c.line(9, 6, 10, 8, "dark", 1)
    c.line(10, 8, 9, 10, "dark", 1)
    return c


def a_eldritch_blast() -> Canvas:
    """A crackling orb of warlock force with a beam tearing off it."""
    c = new()
    c.thick_line(11, 13, 22, 2, 3, "fx", 3)
    c.line(11, 13, 22, 2, "fx", 5)
    c.ellipse(9, 15, 7, 7, "fx", 3)
    ball(c, "fx", box=(1, 7, 17, 23), lo=1)
    c.ellipse(9, 15, 3, 3, "fx2", 4)
    c.px(8, 14, "fx2", 5)
    c.px(9, 15, "fx2", 5)
    for (x0, y0, x1, y1) in ((2, 8, 5, 10), (16, 20, 20, 21), (1, 20, 3, 18)):
        c.line(x0, y0, x1, y1, "fx", 4)
    return c


def a_hex() -> Canvas:
    """A cursed eye inside a downward sigil."""
    c = new()
    c.polygon([(1, 3), (23, 3), (12, 22)], "fx")
    c.polygon([(4.5, 5), (19.5, 5), (12, 18.5)], "dark", 1)
    light(c, "fx")
    c.line(2, 3, 11, 3, "fx", 5)
    eye(c, 12, 9, 11, "fx2", "core")
    return c


def a_inspire() -> Canvas:
    """Two golden notes rising: a bard's song."""
    c = new()
    for hx, hy in ((6, 18), (16, 15)):
        c.ellipse(hx, hy, 3.4, 2.6, "fx")
    c.rect(8, 5, 9, 18, "fx")
    c.rect(18, 2, 19, 15, "fx")
    c.polygon([(8, 5), (19, 2), (19, 6), (8, 9)], "fx")
    light(c, "fx")
    c.px(4, 17, "fx", 5)
    c.px(14, 14, "fx", 5)
    c.line(9, 5, 18, 3, "fx", 5)
    twinkle(c, 3, 6, "core", 2)
    twinkle(c, 21, 20, "core", 1)
    return c


def a_wild_shape() -> Canvas:
    """A beast's paw print with a leaf: the druid takes animal form."""
    c = new()
    c.ellipse(11, 16, 6.5, 5.5, "leather")
    for cx, cy, r in ((4, 9, 2.4), (8.5, 5, 2.6), (14, 5, 2.6), (18.5, 9, 2.4)):
        c.ellipse(cx, cy, r, r * 1.2, "leather")
    for box in ((4, 11, 18, 22), (1, 5, 7, 13), (5, 1, 12, 9), (11, 1, 17, 9), (16, 5, 22, 13)):
        ball(c, "leather", box=box, lo=1)
    c.polygon([(16, 22), (22, 14), (23, 21)], "fx")
    light(c, "fx")
    c.line(17, 21, 21, 16, "fx", 2)
    return c


def a_entangle() -> Canvas:
    """Thorny vines coiling up out of the ground."""
    c = new()
    for (ox, ph, amp) in ((7, 0.0, 4.0), (16, 2.2, 3.5)):
        prev = None
        for y in range(22, 2, -1):
            x = ox + amp * math.sin(y * 0.55 + ph) * (y / 22)
            if prev:
                c.thick_line(prev[0], prev[1], x, y, 2, "fx", 3)
            prev = (x, y)
    light(c, "fx")
    for x, y in ((5, 8), (10, 13), (13, 6), (19, 11), (6, 17), (18, 19)):
        c.px(x, y, "fx2", 4)
        c.px(x + (1 if x < 12 else -1), y - 1, "fx2", 5)
    for (lx, ly) in ((9, 4), (20, 7)):
        c.ellipse(lx, ly, 2.2, 1.4, "fx", 4)
        c.px(lx - 1, ly - 1, "fx", 5)
    c.rect(2, 21, 21, 22, "dark", 2)
    c.line(2, 21, 21, 21, "dark", 3)
    return c


def a_flurry() -> Canvas:
    """A fist with two after-images trailing it: many blows in one turn."""
    c = new()
    fist(c, 1, 2, "fx", wrap=None)
    fist(c, 5, 6, "fx2", wrap=None)
    fist(c, 10, 10, wrap="cloth")
    return c


def a_stunning_strike() -> Canvas:
    """A fist landing with stars spinning off the hit."""
    c = new()
    fist(c, 2, 10, wrap="cloth")
    star(c, 16, 7, 5.5, 2.2, 5, "fx", 4)
    c.px(15, 6, "fx", 5)
    c.px(16, 7, "fx", 5)
    star(c, 8, 4, 3.2, 1.3, 5, "fx", 4)
    star(c, 20, 16, 3.2, 1.3, 5, "fx", 4)
    return c


def a_ki() -> Canvas:
    """A meditating figure in a ring of ki."""
    c = new()
    ring(c, 12, 12, 11, 9.2, "fx", 3)
    c.ellipse(12, 6, 2.6, 2.6, "skin")
    ball(c, "skin", lo=2)
    c.polygon([(8, 9), (16, 9), (17, 16), (7, 16)], "cloth")
    c.polygon([(3, 19), (21, 19), (19, 15), (5, 15)], "cloth")
    light(c, "cloth", band=1)
    c.rect(7, 12, 9, 13, "skin", 3)
    c.rect(15, 12, 17, 13, "skin", 3)
    c.ellipse(12, 14, 1.6, 1.6, "fx", 5)
    for k in range(4):
        a = math.radians(225 + k * 30)
        c.px(12 + math.cos(a) * 10.2, 12 + math.sin(a) * 10.2, "fx", 5)
    return c


def a_misty_step() -> Canvas:
    """A swirl of silver mist closing on itself: you vanish here and step out there."""
    c = new()
    prev = None
    for k in range(60):
        t = k / 59
        a = t * 4.2 * math.pi
        r = 1.0 + t * 9.5
        x, y = 12 + math.cos(a) * r, 12 + math.sin(a) * r * 0.85
        if prev:
            c.thick_line(prev[0], prev[1], x, y, 3 if t > 0.25 else 2, "fx", 5 if t < 0.3 else 4 if t < 0.65 else 3)
        prev = (x, y)
    twinkle(c, 20, 4, "fx2", 2)
    twinkle(c, 4, 19, "fx2", 1)
    twinkle(c, 12, 12, "fx2", 1)
    return c


def a_second_wind() -> Canvas:
    """A heart with a plus and a gust behind it (the phone action list uses this one)."""
    c = new()
    for y, x0, x1 in ((6, 0, 4), (12, 0, 3), (18, 1, 5)):
        c.line(x0, y, x1, y, "fx", 4)
    heart(c, 13, 5.5, 4.8, "liquid")
    ball(c, "liquid", lo=1)
    c.rect(12, 8, 14, 16, "paper", 5)
    c.rect(9, 11, 17, 13, "paper", 5)
    return c


# ----------------------------------------------------------------------------------------------- status icons
def s_poisoned() -> Canvas:
    c = new(STATUS)
    c.ellipse(7, 10, 4.5, 4.5, "fx")
    c.polygon([(3, 9), (11, 9), (7, 1)], "fx")
    ball(c, "fx", box=(2, 1, 12, 15))
    c.ellipse(12.5, 4.5, 1.6, 1.6, "fx", 4)
    c.ellipse(13, 9, 1.0, 1.0, "fx", 3)
    c.px(12, 4, "fx", 5)
    return c


def s_burning() -> Canvas:
    c = new(STATUS)
    flame(c, 8, 14.5, 4.2, 13, "fx", lean=0.05)
    return c


def s_chilled() -> Canvas:
    """A six-armed snowflake with branch ticks."""
    c = new(STATUS)
    for k in range(3):
        a = math.radians(90 + k * 60)
        dx, dy = math.cos(a) * 6.5, math.sin(a) * 6.5
        c.line(8 - dx, 8 - dy, 8 + dx, 8 + dy, "fx", 4)
    for k in range(6):
        a = math.radians(90 + k * 60)
        ex, ey = 8 + math.cos(a) * 4, 8 + math.sin(a) * 4
        for s in (-1, 1):
            b = a + s * math.radians(55)
            c.line(ex, ey, ex + math.cos(b) * 2.2, ey + math.sin(b) * 2.2, "fx", 3)
    c.rect(7, 7, 8, 8, "fx", 5)
    c.px(8, 1, "fx", 5)
    return c


def s_stunned() -> Canvas:
    """Stars circling over a dizzy swirl."""
    c = new(STATUS)
    ring(c, 8, 9, 7, 5.6, "fx", 2, ry_scale=0.5)
    for (x, y, r) in ((3.5, 8, 2.8), (12.5, 8, 2.8), (8, 4, 3.4)):
        star(c, x, y, r, r * 0.42, 5, "fx", 4)
    c.px(8, 4, "fx", 5)
    c.px(7, 3, "fx", 5)
    return c


def s_prone() -> Canvas:
    """A figure flat on the ground under a down-arrow: knocked prone."""
    c = new(STATUS)
    c.rect(6, 0, 8, 2, "fx")
    c.polygon([(2.5, 2.5), (12.5, 2.5), (7.5, 7.5)], "fx")
    light(c, "fx")
    c.ellipse(3, 10, 2.2, 2.2, "skin")
    ball(c, "skin", lo=2)
    c.rect(5, 9, 10, 11, "cloth")
    light(c, "cloth")
    c.rect(11, 10, 14, 11, "leather", 3)
    c.line(11, 10, 14, 10, "leather", 4)
    c.rect(1, 13, 14, 14, "wood", 2)
    c.line(1, 13, 14, 13, "wood", 4)
    return c


def s_bleeding() -> Canvas:
    c = new(STATUS)
    for cx, cy, r in ((5.5, 9.5, 3.5), (11.5, 11.5, 2.5)):
        c.ellipse(cx, cy, r, r, "fx")
        c.polygon([(cx - r * 0.9, cy - r * 0.2), (cx + r * 0.9, cy - r * 0.2), (cx, cy - r * 2.3)], "fx")
    ball(c, "fx", box=(1, 1, 9, 14))
    ball(c, "fx", box=(8, 5, 15, 15))
    c.px(10, 2, "fx", 3)
    c.px(10, 3, "fx", 2)
    return c


def s_blessed() -> Canvas:
    """A halo over a radiant sparkle."""
    c = new(STATUS)
    c.ellipse(8, 3, 6.5, 2.7, "fx", 3)
    c.ellipse(8, 3.2, 4.0, 1.0, "fx", 3)
    for y in range(16):
        for x in range(16):
            if ((x + 0.5 - 8) / 4.2) ** 2 + ((y + 0.5 - 3.2) / 1.2) ** 2 < 1:
                c.clear(x, y)
    light(c, "fx")
    star(c, 8, 10.5, 5.0, 1.6, 4, "core", 4)
    star(c, 8, 10.5, 3.0, 1.2, 4, "core", 3, rot=-45)
    c.rect(7, 9, 8, 10, "core", 5)
    c.px(7, 7, "core", 5)
    return c


def s_hasted() -> Canvas:
    """Fast-forward chevrons."""
    c = new(STATUS)
    for x in (1, 7):
        c.polygon([(x, 2), (x + 4, 2), (x + 9, 8), (x + 4, 14), (x, 14), (x + 5, 8)], "fx")
    light(c, "fx")
    return c


def s_slowed() -> Canvas:
    """A snail: a coiled shell on a slow, soft body."""
    c = new(STATUS)
    c.polygon([(1, 14), (15, 14), (15, 12), (12, 11), (3, 12)], "fx2")
    c.ellipse(13.5, 9.5, 1.6, 2.5, "fx2")
    light(c, "fx2")
    c.line(13, 7, 12, 3, "fx2", 4)
    c.line(14, 7, 15, 3, "fx2", 4)
    c.ellipse(6.5, 7.5, 5.4, 5.4, "fx")
    ball(c, "fx", lo=2)
    prev = None
    for k in range(30):
        t = k / 29
        a = t * 3.6 * math.pi
        r = 0.3 + t * 4.4
        x, y = 6.5 + math.cos(a) * r, 7.5 + math.sin(a) * r
        if prev:
            c.line(prev[0], prev[1], x, y, "fx", 1)
        prev = (x, y)
    return c


def s_shielded() -> Canvas:
    c = new(STATUS)
    shield(c, 2, 1, 11, 14, face="fx", rim="metal", emblem=False)
    c.line(4, 4, 4, 7, "fx", 5)
    return c


def s_hidden() -> Canvas:
    """A hood fading into the dark, only the eyes catching light: unseen (the Hide ability, in miniature)."""
    c = new(STATUS)
    c.polygon([(8, 0.5), (12.5, 3.5), (14.5, 15), (1.5, 15), (3.5, 3.5)], "fx")
    light(c, "fx")
    for y in range(9, 16):                               # the cloak dissolves towards its hem
        for x in range(16):
            if c.slot[y, x] and (x + y) % 2 and y > 10:
                c.shade[y, x] = 1
    c.ellipse(8, 7.5, 3.6, 3.8, "dark", 0)
    c.rect(6, 9, 10, 15, "dark", 0)
    c.px(6, 7, "fx2", 5)
    c.px(10, 7, "fx2", 5)
    c.px(7, 2, "fx", 5)
    return c


def s_marked() -> Canvas:
    """A target reticle: hexed, hunter's mark, any 'this one' mark."""
    c = new(STATUS)
    ring(c, 8, 8, 6.5, 4.6, "fx", 3)
    light(c, "fx")
    for x0, y0, x1, y1 in ((8, 0, 8, 4), (8, 12, 8, 15), (0, 8, 4, 8), (12, 8, 15, 8)):
        c.line(x0, y0, x1, y1, "fx", 4)
    c.rect(7, 7, 8, 8, "fx", 5)
    return c


def s_inspired() -> Canvas:
    c = new(STATUS)
    c.ellipse(5, 12, 3.2, 2.5, "fx")
    c.rect(7, 2, 8, 12, "fx")
    c.polygon([(7, 2), (14, 5), (14, 8), (8, 5)], "fx")
    light(c, "fx")
    c.px(4, 11, "fx", 5)
    return c


def s_regenerating() -> Canvas:
    c = new(STATUS)
    heart(c, 8, 4, 3.6, "fx")
    ball(c, "fx", lo=1)
    c.rect(7, 5, 8, 11, "paper", 5)
    c.rect(5, 7, 10, 8, "paper", 5)
    return c


def s_concentrating() -> Canvas:
    """A held spell: a bright orb with a ring orbiting it, kept going until it's broken."""
    c = new(STATUS)
    c.ellipse(8, 8, 4.2, 4.2, "fx")
    ball(c, "fx", lo=2)
    for k in range(64):
        a = k / 64 * 2 * math.pi
        x, y = 8 + math.cos(a) * 7, 8 + math.sin(a) * 2.6
        x, y = 8 + (x - 8) * 0.87 - (y - 8) * 0.5, 8 + (x - 8) * 0.5 + (y - 8) * 0.87
        front = math.sin(a) > 0
        if front or c.slot[int(y), int(x)] == 0:
            c.px(x, y, "fx2", 5 if front else 2)
    return c


def s_frightened() -> Canvas:
    """A pale face: eyes wide, brows up, mouth a round O, a bead of sweat."""
    c = new(STATUS)
    c.ellipse(7.5, 8.5, 6.5, 6.5, "fx")
    ball(c, "fx", lo=2)
    for ex in (3, 9):
        c.rect(ex, 5, ex + 2, 8, "paper", 5)
        c.px(ex + 1, 7, "dark", 0)
        c.px(ex + 1, 6, "dark", 0)
        c.line(ex, 3, ex + 2, 3, "dark", 1)
    c.rect(6, 10, 8, 12, "dark", 0)
    c.px(6, 10, "dark", 1)
    c.ellipse(14, 3.5, 1.4, 2.0, "fx2", 4)
    c.px(13, 3, "fx2", 5)
    return c


def s_charmed() -> Canvas:
    c = new(STATUS)
    heart(c, 7, 4, 3.4, "fx")
    ball(c, "fx", lo=1)
    twinkle(c, 13, 3, "core", 2)
    return c


def s_injured() -> Canvas:
    """Crossed bandages with a blood spot: up again after being downed, but hurt."""
    c = new(STATUS)
    for ang in (45, -45):
        poly(c, [(-7, -2), (7, -2), (7, 2), (-7, 2)], "paper", at=(8, 8), ang=ang)
    light(c, "paper")
    for k in (-4, 4):
        c.px(8 + k, 8 + k, "paper", 2)
        c.px(8 + k, 8 - k, "paper", 2)
    c.ellipse(8, 8, 2.4, 2.4, "fx")
    ball(c, "fx", lo=1)
    return c


# ------------------------------------------------------------------------------------------------- item icons
def i_potion() -> Canvas:
    """A round flask: cork, neck, a glass bulb two-thirds full of liquid, a glint. Recoloured per potion."""
    c = new()
    c.ellipse(12, 15, 7.5, 7, "liquid")
    for y in range(7, 11):
        for x in range(4, 21):
            if c.slot[y, x]:
                c.px(x, y, "glass", 3)
    c.rect(10, 4, 13, 9, "glass")
    ball(c, "liquid", box=(4, 8, 20, 22), lo=1, spec=False)
    ball(c, "glass", lo=2, spec=False)
    c.line(6, 11, 18, 11, "liquid", 5)
    c.rect(9, 3, 14, 4, "glass", 4)
    c.line(14, 4, 9, 4, "glass", 2)
    c.rect(10, 1, 13, 2, "wood", 3)
    c.line(10, 1, 12, 1, "wood", 4)
    c.px(13, 2, "wood", 2)
    for x, y in ((7, 12), (6, 13), (6, 14)):
        c.px(x, y, "glass", 5)
    c.px(8, 9, "glass", 5)
    c.px(15, 17, "liquid", 4)
    c.px(13, 19, "liquid", 4)
    return c


def i_potion_big() -> Canvas:
    """A tall square-shouldered bottle with a label: the big potion."""
    c = new()
    c.rect(5, 8, 18, 22, "liquid")
    c.rect(6, 7, 17, 7, "glass")
    c.rect(5, 8, 18, 10, "glass")
    c.rect(9, 3, 14, 6, "glass")
    ball(c, "liquid", box=(4, 11, 19, 23), lo=1, spec=False)
    light(c, "glass", band=1)
    c.line(5, 11, 18, 11, "liquid", 5)
    c.rect(8, 2, 15, 3, "glass", 4)
    c.rect(9, 0, 14, 1, "wood", 3)
    c.line(9, 0, 13, 0, "wood", 4)
    c.rect(7, 14, 16, 19, "paper", 4)
    c.line(7, 19, 16, 19, "paper", 2)
    c.line(16, 14, 16, 19, "paper", 2)
    heart(c, 11.5, 15, 1.3, "liquid")
    c.rect(6, 12, 6, 20, "glass", 5)
    c.px(7, 9, "glass", 5)
    return c


def i_scroll() -> Canvas:
    """An open scroll between two rollers, lines of script, a red wax seal."""
    c = new()
    c.rect(5, 4, 18, 19, "paper")
    light(c, "paper", band=2)
    for y in (7, 9, 11, 13):
        c.line(7, y, 16 - (y % 4), y, "dark", 2)
    for y0 in (2, 19):
        c.rect(3, y0, 20, y0 + 2, "paper", 3)
        c.line(3, y0, 20, y0, "paper", 5)
        c.line(3, y0 + 2, 20, y0 + 2, "paper", 1)
        c.px(3, y0 + 1, "paper", 4)
        c.rect(1, y0, 2, y0 + 2, "wood", 3)
        c.rect(21, y0, 22, y0 + 2, "wood", 2)
        c.px(1, y0, "wood", 4)
    c.ellipse(15.5, 16, 2.6, 2.6, "liquid")
    ball(c, "liquid", lo=1)
    c.line(14, 18, 13, 21, "cloth", 3)
    c.line(17, 18, 18, 21, "cloth", 2)
    return c


def i_coins() -> Canvas:
    """A heap of gold coins, back to front, with one coin standing on its edge in front."""
    c = new()
    heap = [(12, 8), (8, 11), (16, 11), (4.5, 14.5), (12, 14), (19.5, 14.5), (8, 17.5), (16, 17.5), (3.5, 19.5),
            (12, 20), (20, 19.5)]
    for cx, cy in heap:
        c.ellipse(cx, cy + 1, 3.6, 1.9, "trim", 1)
        c.ellipse(cx, cy, 3.6, 1.9, "trim", 3)
        c.ellipse(cx - 0.5, cy - 0.3, 2.2, 1.0, "trim", 4)
        c.line(cx - 2, cy - 1, cx - 1, cy - 1, "trim", 5)
    c.ellipse(15.5, 16, 4.4, 4.6, "trim", 3)
    ball(c, "trim", box=(11, 11, 20, 21), lo=2)
    ring(c, 15.5, 16, 3.0, 2.0, "trim", 1)
    c.rect(15, 15, 16, 16, "trim", 4)
    c.px(13, 13, "trim", 5)
    return c


def i_gem() -> Canvas:
    """A brilliant cut: table, crown and pavilion facets in fixed shades, so any gem ramp reads as faceted."""
    c = new()
    top, girdle, tip = 4, 10, 21
    c.polygon([(2, girdle), (7, top), (17, top), (22, girdle)], "gem", 4)             # crown
    c.polygon([(2, girdle), (22, girdle), (12, tip)], "gem", 2)                       # pavilion
    c.polygon([(8, top), (16, top), (18, 7), (6, 7)], "gem", 5)                      # table
    c.polygon([(2, girdle), (7, top), (8, top), (6, 7), (5, girdle)], "gem", 4)
    c.polygon([(17, top), (22, girdle), (19, girdle), (18, 7)], "gem", 3)
    c.polygon([(2, girdle), (12, tip), (8, girdle)], "gem", 3)
    c.polygon([(12, girdle), (12, tip), (16, girdle)], "gem", 1)
    c.polygon([(16, girdle), (12, tip), (22, girdle)], "gem", 2)
    c.line(3, girdle, 21, girdle, "gem", 4)
    c.line(5, 8, 7, 5, "gem", 5)
    twinkle(c, 8, 5, "glass", 2)
    return c


def i_key() -> Canvas:
    """An old iron-and-brass key: a round bow, a long shank, a toothed bit."""
    c = new()
    ring(c, 7, 7, 5.6, 2.6, "trim", 3)
    light(c, "trim", band=1)
    blade(c, 10, 10, 20, 20, "trim", width=2)
    for (x, y) in ((16, 18), (19, 15)):
        c.rect(x, y, x + 2, y + 2, "trim", 3)
        c.px(x + 2, y + 2, "trim", 2)
    c.px(17, 14, "trim", 3)
    c.px(4, 4, "trim", 5)
    c.px(5, 3, "trim", 5)
    return c


def i_ring() -> Canvas:
    """A gold band seen at an angle, set with a gem."""
    c = new()
    ring(c, 12, 14, 8.5, 5.5, "trim", 3, ry_scale=0.8)
    light(c, "trim", band=2)
    for y in range(24):
        for x in range(24):
            if c.slot[y, x] and math.hypot(x + 0.5 - 12, (y + 0.5 - 14) / 0.8) < 7.0 and y < 14:
                c.shade[y, x] = min(5, c.shade[y, x] + 1)
    c.rect(9, 4, 15, 7, "trim", 2)
    c.ellipse(12, 4, 3.6, 3.2, "gem")
    ball(c, "gem")
    c.px(11, 3, "glass", 5)
    c.px(5, 12, "trim", 5)
    c.px(5, 13, "trim", 5)
    return c


def i_amulet() -> Canvas:
    """A pendant on a chain: the chain hangs in a V from the top corners."""
    c = new()
    for k in range(9):
        for sgn in (-1, 1):
            x = 12 + sgn * (9 - k * 0.85)
            y = 1 + k * 1.15
            c.px(x, y, "trim", 4 if k % 2 else 2)
    c.ellipse(12, 16, 6.5, 6.5, "trim")
    ball(c, "trim", box=(4, 8, 20, 24))
    c.ellipse(12, 16, 3.6, 3.6, "gem")
    ball(c, "gem", lo=1)
    c.px(11, 14, "glass", 5)
    c.rect(11, 9, 13, 10, "trim", 4)
    return c


def i_sword() -> Canvas:
    c = new()
    sword(c, 4, 19, 12, guard=3)
    return c


def i_dagger() -> Canvas:
    c = new()
    sword(c, 6, 17, 8, guard=2, grip=2)
    return c


def i_axe() -> Canvas:
    c = new()
    axe(c, 1)
    return c


def i_mace() -> Canvas:
    """A flanged mace: a wooden haft, a ridged iron head."""
    c = new()
    haft(c, 3, 21, 13, 11)
    c.rect(3, 19, 5, 21, "leather", 3)
    c.ellipse(16, 8, 5, 5, "metal")
    ball(c, "metal", lo=1)
    for k in range(6):
        a = math.radians(-60 + k * 60)
        c.px(16 + math.cos(a) * 6, 8 + math.sin(a) * 6, "metal", 3)
        c.px(16 + math.cos(a) * 5.5, 8 + math.sin(a) * 5.5, "metal", 4 if a < 0.5 else 2)
    c.rect(11, 12, 13, 14, "trim", 4)
    c.px(13, 14, "trim", 2)
    return c


def i_spear() -> Canvas:
    c = new()
    haft(c, 2, 22, 15, 9)
    poly(c, [(0, -4.5), (2.2, 0), (0, 4.5), (-2.2, 0)], "metal", at=(18.5, 5.5), ang=45)
    light(c, "metal")
    c.line(17, 6, 20, 3, "metal", 5)
    c.rect(14, 9, 15, 10, "trim", 4)
    c.px(13, 12, "cloth", 4)
    c.px(12, 12, "cloth", 3)
    c.px(13, 13, "cloth", 2)
    return c


def i_bow() -> Canvas:
    """A recurve bow, strung, with a leather grip."""
    c = new()
    pts = []
    for k in range(48):
        t = k / 47
        bend = math.sin(t * math.pi) * 6.5 - math.sin(t * 2 * math.pi) * 0.0
        pts.append((3 + t * 17 - bend * 0.75, 21 - t * 18 - bend * 0.75))
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        c.thick_line(x0, y0, x1, y1, 2, "wood", 3)
    light(c, "wood")
    for (x, y, dx, dy) in ((3, 21, 1, 0), (20, 3, 0, 1)):        # recurved tips
        c.px(x + dx, y + dy, "wood", 4)
    c.line(4, 20, 19, 4, "paper", 4)
    c.rect(6, 10, 8, 12, "leather", 3)
    c.px(6, 10, "leather", 4)
    c.px(8, 12, "leather", 2)
    return c


def i_staff() -> Canvas:
    """A wizard's staff: a long haft crowned by a glowing orb in a claw."""
    c = new()
    haft(c, 2, 22, 15, 9)
    c.ellipse(18, 6, 3.6, 3.6, "fx")
    ball(c, "fx", lo=2)
    for (x, y) in ((14, 6), (15, 9), (17, 10), (14, 4)):
        c.px(x, y, "trim", 3)
    c.line(14, 6, 15, 9, "trim", 4)
    c.line(15, 9, 18, 10, "trim", 4)
    twinkle(c, 21, 2, "fx", 1)
    return c


def i_wand() -> Canvas:
    c = new()
    blade(c, 4, 20, 15, 9, "wood", width=2)
    c.rect(4, 18, 6, 20, "trim", 3)
    c.px(4, 18, "trim", 5)
    star(c, 17.5, 6.5, 4.6, 1.8, 5, "fx", 4)
    c.px(17, 6, "fx", 5)
    twinkle(c, 21, 12, "fx", 1, 4)
    twinkle(c, 12, 3, "fx", 1, 4)
    return c


def i_helmet() -> Canvas:
    """An open helm with a nasal guard, cheek plates and a red crest."""
    c = new()
    c.polygon([(9, 1), (15, 1), (14, 4), (10, 4)], "cloth")
    light(c, "cloth")
    c.ellipse(12, 12, 8.5, 8, "metal")
    c.rect(4, 12, 20, 21, "metal")
    for y in range(24):
        for x in range(24):
            if c.slot[y, x] == SLOTS.id("metal") and 8 <= x <= 16 and 13 <= y <= 21:
                c.clear(x, y)
    c.rect(11, 12, 13, 17, "metal")
    ball(c, "metal", box=(3, 3, 21, 22), lo=1, spec=False)
    c.rect(4, 11, 20, 12, "trim", 4)
    c.line(4, 12, 20, 12, "trim", 2)
    c.rect(8, 13, 10, 18, "dark", 0)
    c.rect(14, 13, 16, 18, "dark", 0)
    c.px(7, 6, "metal", 5)
    c.px(8, 5, "metal", 5)
    c.px(6, 7, "metal", 5)
    return c


def i_chest() -> Canvas:
    """A breastplate with pauldrons, a gold-trimmed collar and a belt."""
    c = new()
    c.polygon([(6, 4), (18, 4), (19, 8), (18, 20), (12, 22), (6, 20), (5, 8)], "metal")
    ball(c, "metal", lo=1, spec=False)
    for sx in (1, -1):
        cx = 12 - sx * 8
        c.ellipse(cx, 7.5, 4, 3.5, "trim")
    ball(c, "trim", box=(0, 3, 9, 12))
    ball(c, "trim", box=(15, 3, 23, 12))
    c.polygon([(9, 3), (15, 3), (12, 7)], "dark", 1)
    c.line(9, 3, 12, 6, "trim", 4)
    c.line(15, 3, 12, 6, "trim", 3)
    c.line(12, 7, 12, 16, "metal", 2)
    c.line(11, 8, 11, 15, "metal", 5)
    c.rect(6, 17, 18, 18, "leather", 3)
    c.line(6, 17, 18, 17, "leather", 4)
    c.rect(11, 17, 13, 18, "trim", 4)
    return c


def i_boots() -> Canvas:
    """A pair of leather boots, one behind the other."""
    c = new()
    for k, ox in enumerate((7, 1)):
        pts = [(ox + 3, 2), (ox + 10, 2), (ox + 10, 13), (ox + 15, 16), (ox + 16, 19), (ox + 15, 21), (ox + 2, 21),
               (ox + 2, 15), (ox + 3, 12)]
        c.polygon(pts, "leather" if k else "dark", 3)
    light(c, "dark")
    light(c, "leather", band=2)
    c.rect(2, 2, 11, 4, "leather", 4)
    c.line(3, 2, 10, 2, "leather", 5)
    c.rect(2, 20, 16, 21, "leather", 1)
    c.line(4, 7, 10, 7, "trim", 3)
    c.px(4, 7, "trim", 5)
    c.px(4, 5, "leather", 5)
    c.px(4, 6, "leather", 5)
    return c


def i_gloves() -> Canvas:
    """A gauntlet: fingers together, thumb out, a flared cuff."""
    c = new()
    c.rect(6, 3, 15, 13, "leather")
    for k in range(3):
        c.line(8 + k * 3, 4, 8 + k * 3, 11, "leather", 2)
    c.polygon([(15, 9), (20, 6), (21, 9), (16, 15)], "leather")
    c.rect(6, 13, 16, 16, "leather")
    light(c, "leather", band=2)
    c.polygon([(5, 16), (17, 16), (19, 22), (3, 22)], "metal")
    light(c, "metal", band=2)
    c.line(5, 18, 17, 18, "trim", 4)
    for k in range(4):
        c.px(7 + k * 3, 3, "leather", 4)
        c.px(6 + k * 3, 4, "leather", 5)
    return c


def i_shield() -> Canvas:
    c = new()
    shield(c, 4, 2, 16, 20)
    return c


def i_cloak() -> Canvas:
    """A hooded travelling cloak with a gold clasp."""
    c = new()
    c.polygon([(12, 1), (18, 4), (19, 9), (22, 22), (2, 22), (5, 9), (6, 4)], "cloth")
    light(c, "cloth", band=2)
    c.ellipse(12, 7, 3.5, 3.5, "dark", 0)
    c.rect(9, 7, 15, 9, "dark", 0)
    for x in (8, 12, 16):
        c.line(x, 13, x - (12 - x) * 0.3, 21, "cloth", 2)
    c.ellipse(12, 11.5, 1.8, 1.8, "trim")
    ball(c, "trim")
    c.px(10, 2, "cloth", 5)
    c.px(9, 3, "cloth", 5)
    return c


def i_food() -> Canvas:
    """A roast leg: browned meat on a bone with a knuckle end."""
    c = new()
    poly(c, [(-7, -5), (5, -5), (7, -2), (7, 2), (5, 5), (-7, 5), (-9, 0)], "leather", at=(10, 10), ang=40)
    ball(c, "leather", lo=1)
    blade(c, 15, 15, 19, 19, "bone", width=2)
    for (x, y) in ((20, 18), (18, 20)):
        c.ellipse(x + 0.5, y + 0.5, 1.6, 1.6, "bone", 4)
    c.px(19, 19, "bone", 2)
    c.px(20, 17, "bone", 5)
    for x, y in ((7, 8), (10, 11), (6, 13)):
        c.px(x, y, "leather", 1)
    return c


def i_tome() -> Canvas:
    """A heavy spellbook: leather cover, gold corners, a gem on the front, pages along the edge."""
    c = new()
    c.rect(6, 3, 19, 21, "paper", 4)
    for y in range(5, 21, 2):
        c.line(18, y, 19, y, "paper", 2)
    c.rect(4, 2, 17, 20, "leather")
    light(c, "leather", band=2)
    c.rect(4, 2, 5, 20, "leather", 2)
    for (x, y) in ((15, 2), (15, 18), (6, 2), (6, 18)):
        c.rect(x, y, x + 2, y + 2, "trim", 4)
    c.px(17, 20, "trim", 2)
    c.ellipse(11.5, 11, 3, 3.4, "gem")
    ball(c, "gem")
    ring(c, 11.5, 11, 4.6, 3.3, "trim", 3)
    c.px(9, 8, "trim", 5)
    c.line(5, 21, 18, 21, "paper", 2)
    return c


def i_map() -> Canvas:
    """A folded map: three panels, a dashed trail and a red X."""
    c = new()
    for k in range(3):
        x0 = 2 + k * 7
        c.polygon([(x0, 4 + (k % 2) * 2), (x0 + 7, 4 + ((k + 1) % 2) * 2), (x0 + 7, 20 + ((k + 1) % 2) * 2),
                   (x0, 20 + (k % 2) * 2)], "paper", 4 if k != 1 else 3)
    light(c, "paper")
    c.line(9, 6, 9, 20, "paper", 2)
    c.line(16, 6, 16, 20, "paper", 2)
    for k, (x, y) in enumerate(((4, 17), (6, 15), (8, 14), (10, 12), (12, 11), (14, 10))):
        c.px(x, y, "dark", 1)
    for d in range(-2, 3):
        c.px(17 + d, 9 + d, "liquid", 3)
        c.px(17 + d, 9 - d, "liquid", 3)
    c.px(16, 8, "liquid", 5)
    c.ellipse(6, 9, 1.6, 1.4, "fx", 3)
    c.px(5, 8, "fx", 4)
    return c


def i_torch() -> Canvas:
    c = new()
    haft(c, 4, 21, 13, 12)
    c.rect(12, 10, 15, 13, "cloth")
    light(c, "cloth")
    c.line(12, 12, 15, 12, "cloth", 2)
    flame(c, 15, 11, 4, 11, "fx", lean=0.2)
    return c


def i_rope() -> Canvas:
    """A coil of rope, three loops seen from above, with its end trailing off: twisted strands are diagonal hatching."""
    c = new()
    for r in (10.5, 7.3, 4.1):
        ring(c, 12, 11.5, r, r - 2.2, "fx", 3, ry_scale=0.8)
    for y in range(24):
        for x in range(24):
            if c.slot[y, x] == SLOTS.id("fx"):
                lit = 4 if (y < 11 and x < 15) else 3 if y < 14 else 2
                c.shade[y, x] = lit + (1 if (x - y) % 3 == 0 else 0)
    c.thick_line(18, 18, 22, 22, 2, "fx", 3)
    for k in range(5):
        c.px(18 + k, 18 + k, "fx", 4 if k % 2 else 2)
    c.ellipse(12, 11.5, 1.8, 1.2, "dark", 0)
    return c


def i_lockpick() -> Canvas:
    """A padlock with a pick and tension wrench in its keyhole."""
    c = new()
    ring(c, 10, 9, 6, 3.6, "metal", 3)
    for y in range(9, 24):
        for x in range(24):
            if c.slot[y, x]:
                c.clear(x, y)
    c.rect(4, 9, 6, 12, "metal")
    c.rect(14, 9, 16, 12, "metal")
    light(c, "metal")
    c.rect(2, 11, 18, 22, "trim")
    light(c, "trim", band=2)
    c.ellipse(10, 15, 1.5, 1.5, "dark", 0)
    c.rect(10, 16, 10, 18, "dark", 0)
    c.line(10, 16, 21, 5, "fx", 4)
    c.line(19, 5, 21, 5, "fx", 4)
    c.line(11, 18, 22, 18, "fx", 3)
    c.line(22, 18, 22, 15, "fx", 3)
    c.px(3, 12, "trim", 5)
    return c


def i_trophy() -> Canvas:
    """Goblin fangs strung on a cord: proof of the kill."""
    c = new()
    prev = None
    for k in range(25):
        t = k / 24
        x, y = 1 + t * 21, 3 + math.sin(t * math.pi) * 9
        if prev:
            c.line(prev[0], prev[1], x, y, "leather", 3)
        prev = (x, y)
    for k, (fx, n) in enumerate(((6, 6), (11.5, 9), (17, 6))):
        top = 3 + math.sin((fx - 1) / 21 * math.pi) * 9
        c.polygon([(fx - 2.4, top), (fx + 2.4, top), (fx + 0.8, top + n * 0.7), (fx - 0.4, top + n)], "bone")
        c.rect(int(fx) - 1, int(top) - 1, int(fx) + 1, int(top), "fx", 3)
    light(c, "bone", band=2)
    light(c, "fx")
    for fx, top in ((6, 9), (11, 12), (17, 9)):
        c.line(fx - 1, top + 1, fx - 1, top + 3, "bone", 5)
    return c


# -------------------------------------------------------------------------------------------------- catalogue
@dataclass
class Icon:
    id: str
    category: str                                        # ability | status | item
    make: Callable[[], Canvas]
    pal: dict[str, str] = field(default_factory=dict)    # slot -> ramp name, over DEFAULT
    tags: list[str] = field(default_factory=list)

    def palette(self) -> dict[str, list[str]]:
        return {**DEFAULT, **{k: _r(v) for k, v in self.pal.items()}}

    def render(self) -> Image.Image:
        c = self.make()
        c.outline()
        return c.render(self.palette())


def _A(id_, make, tags, **pal) -> Icon:
    return Icon(id_, "ability", make, pal, tags)


def _S(id_, make, tags, **pal) -> Icon:
    return Icon(id_, "status", make, pal, tags)


def _I(id_, make, tags, **pal) -> Icon:
    return Icon(id_, "item", make, pal, tags)


ABILITIES = [
    _A("slash", a_slash, ["fighter", "paladin", "melee", "weapon"], fx="slash"),
    _A("heavy_strike", a_heavy_strike, ["fighter", "melee", "weapon"], fx="impact", metal="metal/iron"),
    _A("cleave", a_cleave, ["fighter", "melee", "weapon", "area"], fx="slash"),
    _A("shield_bash", a_shield_bash, ["fighter", "paladin", "melee", "shield"], fx="slash", fx2="impact"),
    _A("shield_wall", a_shield_wall, ["fighter", "paladin", "defence", "stance"], cloth="cloth/steel_blue"),
    _A("thrust", a_thrust, ["fighter", "melee", "weapon"], fx="slash"),
    _A("sneak_attack", a_sneak_attack, ["rogue", "melee", "shadow"], fx="magic/shadow", liquid="magic/blood"),
    _A("dash", a_dash, ["rogue", "monk", "movement"], fx="slash", fx2="slash"),
    _A("hide", a_hide, ["rogue", "ranger", "stealth", "shadow"], cloth="cloth/charcoal", fx="lightning"),
    _A("poisoned_blade", a_poisoned_blade, ["rogue", "melee", "poison"], fx="poison"),
    _A("arrow_shot", a_arrow_shot, ["ranger", "ranged", "weapon"], fx="slash"),
    _A("volley", a_volley, ["ranger", "ranged", "area"], fx="slash"),
    _A("aimed_shot", a_aimed_shot, ["ranger", "ranged", "weapon"], cloth="target", paper="bandage"),
    _A("fireball", a_fireball, ["wizard", "spell", "fire", "area"], fx="magic/fire"),
    _A("ice_shard", a_ice_shard, ["wizard", "spell", "cold"], fx="ice"),
    _A("lightning_bolt", a_lightning_bolt, ["wizard", "spell", "lightning", "line"], fx="lightning"),
    _A("magic_missile", a_magic_missile, ["wizard", "spell", "arcane"], fx="magic/arcane"),
    _A("heal", a_heal, ["cleric", "druid", "spell", "healing"], fx="heal", core="magic/holy"),
    _A("bless", a_bless, ["cleric", "paladin", "spell", "holy", "buff"], fx="magic/holy", core="magic/holy"),
    _A("revive", a_revive, ["cleric", "paladin", "spell", "holy", "healing"], fx2="magic/holy"),
    _A("smite", a_smite, ["paladin", "melee", "holy"], fx2="magic/holy"),
    _A("turn_undead", a_turn_undead, ["cleric", "paladin", "holy", "area"], fx2="magic/holy"),
    _A("eldritch_blast", a_eldritch_blast, ["warlock", "spell", "ranged"], fx="eldritch", fx2="charm"),
    _A("hex", a_hex, ["warlock", "spell", "curse"], fx="hex", fx2="magic/blood", core="lightning"),
    _A("inspire", a_inspire, ["bard", "song", "buff"], fx="metal/gold", core="magic/holy"),
    _A("wild_shape", a_wild_shape, ["druid", "transform", "nature"], fx="magic/nature", leather="fur"),
    _A("entangle", a_entangle, ["druid", "ranger", "spell", "nature", "area"], fx="vine", fx2="thorn", dark="earth"),
    _A("flurry_of_blows", a_flurry, ["monk", "melee", "unarmed"], fx="slash", cloth="cloth/ochre"),
    _A("stunning_strike", a_stunning_strike, ["monk", "melee", "unarmed"], fx="lightning", cloth="cloth/ochre"),
    _A("ki", a_ki, ["monk", "meditate", "buff"], fx="ki", cloth="cloth/ochre"),
    _A("misty_step", a_misty_step, ["wizard", "warlock", "spell", "teleport"], fx="mist", fx2="magic/arcane"),
    _A("second_wind", a_second_wind, ["fighter", "healing", "bonus"], fx="slash", liquid="healing",
       paper="bandage"),
]

STATUSES = [
    _S("poisoned", s_poisoned, ["debuff", "poison"], fx="poison"),
    _S("burning", s_burning, ["debuff", "fire"], fx="magic/fire"),
    _S("chilled", s_chilled, ["debuff", "cold"], fx="ice"),
    _S("stunned", s_stunned, ["debuff", "control"], fx="lightning"),
    _S("prone", s_prone, ["debuff", "control"], cloth="cloth/steel_blue", fx="slash", wood="earth"),
    _S("bleeding", s_bleeding, ["debuff", "damage"], fx="healing"),
    _S("blessed", s_blessed, ["buff", "holy"], fx="metal/gold", core="magic/holy"),
    _S("hasted", s_hasted, ["buff", "speed"], fx="haste"),
    _S("slowed", s_slowed, ["debuff", "speed"], fx="shell", fx2="snail"),
    _S("shielded", s_shielded, ["buff", "defence"], fx="rare"),
    _S("hidden", s_hidden, ["buff", "stealth"], fx="cloth/slate", fx2="lightning"),
    _S("marked", s_marked, ["debuff", "curse", "hex", "hunters_mark"], fx="target"),
    _S("inspired", s_inspired, ["buff", "song"], fx="metal/gold"),
    _S("regenerating", s_regenerating, ["buff", "healing"], fx="heal", paper="bandage"),
    _S("concentrating", s_concentrating, ["spell", "concentration"], fx="magic/arcane", fx2="metal/gold"),
    _S("frightened", s_frightened, ["debuff", "control", "fear"], fx="mist", fx2="magic/arcane"),
    _S("charmed", s_charmed, ["debuff", "control", "charm"], fx="charm", core="magic/holy"),
    _S("injured", s_injured, ["debuff", "downed"], paper="bandage", fx="magic/blood"),
]

_POTIONS = {"potion_healing": "healing", "potion_mana": "mana", "potion_antidote": "antidote",
            "potion_fire_resistance": "fire_res"}
_GEMS = {"gem_ruby": "ruby", "gem_sapphire": "sapphire", "gem_emerald": "emerald", "gem_amethyst": "amethyst"}

ITEMS = [
    *[_I(k, i_potion, ["potion", "consumable", "shape:potion"], liquid=v) for k, v in _POTIONS.items()],
    _I("potion_greater_healing", i_potion_big, ["potion", "consumable", "shape:potion_big"], liquid="healing"),
    _I("scroll", i_scroll, ["consumable", "spell"], cloth="cloth/crimson", liquid="healing"),
    _I("gold", i_coins, ["currency", "valuable"]),
    *[_I(k, i_gem, ["gem", "valuable", "shape:gem"], gem=v) for k, v in _GEMS.items()],
    _I("key", i_key, ["quest", "key"], trim="metal/bronze"),
    _I("ring", i_ring, ["jewellery", "equip:ring"], gem="sapphire"),
    _I("amulet", i_amulet, ["jewellery", "equip:neck"], gem="emerald"),
    _I("sword", i_sword, ["weapon", "melee", "equip:weapon"]),
    _I("axe", i_axe, ["weapon", "melee", "equip:weapon"]),
    _I("bow", i_bow, ["weapon", "ranged", "equip:weapon"], paper="bandage"),
    _I("staff", i_staff, ["weapon", "focus", "equip:weapon"], wood="wood/dark", fx="magic/arcane"),
    _I("dagger", i_dagger, ["weapon", "melee", "equip:weapon"]),
    _I("mace", i_mace, ["weapon", "melee", "equip:weapon"]),
    _I("spear", i_spear, ["weapon", "melee", "equip:weapon"]),
    _I("wand", i_wand, ["weapon", "focus", "equip:weapon"], wood="wood/pale", fx="magic/arcane"),
    _I("helmet", i_helmet, ["armour", "equip:head"]),
    _I("chest_armour", i_chest, ["armour", "equip:torso"]),
    _I("boots", i_boots, ["armour", "equip:feet"], dark="leather/dark"),
    _I("gloves", i_gloves, ["armour", "equip:hands"]),
    _I("shield", i_shield, ["armour", "equip:offhand"]),
    _I("cloak", i_cloak, ["armour", "equip:cape"], cloth="cloth/forest"),
    _I("food", i_food, ["consumable", "food"], leather="meat"),
    _I("tome", i_tome, ["focus", "book", "equip:weapon"], leather="leather/red", gem="sapphire"),
    _I("map", i_map, ["quest", "map"], liquid="healing", fx="vine"),
    _I("torch", i_torch, ["tool", "light"], cloth="cloth/sand", fx="magic/fire"),
    _I("rope", i_rope, ["tool"], fx="rope"),
    _I("lockpick", i_lockpick, ["tool", "rogue"], trim="metal/iron", metal="metal/steel", fx="metal/silver"),
    _I("trophy_goblin_fangs", i_trophy, ["trophy", "quest", "goblin"], fx="goblin", leather="rope"),
]

CATALOGUE: list[Icon] = ABILITIES + STATUSES + ITEMS


# ------------------------------------------------------------------------------------------------ rarity frames
RARITIES = ["common", "uncommon", "rare", "epic", "legendary"]
FRAMES = RARITIES + ["ability"]          # "ability": the UI's own gold-on-navy slot for ability icons


def frame(kind: str) -> Canvas:
    """A 26x26 frame: a bevelled ring around a 24x24 well of the UI's navy glass, with gold corner studs like the
    mockups' panels. Rarer frames tint the well's inner edge, then add side gems, then gilding. The ability frame is
    plain UI gold with the uncommon tier's inner line."""
    c = new(FRAME)
    tier = RARITIES.index(kind) if kind in RARITIES else 1
    for y in range(1, FRAME - 1):
        c.rect(1, y, FRAME - 2, y, "dark", 4 if y < 3 else 3 if y < 12 else 2)
    if tier >= 1:                                           # a dim inner line in the rarity colour
        n = FRAME - 3
        for k in range(2, n + 1):
            for (x, y) in ((k, 2), (2, k), (k, n), (n, k)):
                c.px(x, y, "fx2", 1 if tier < 4 else 2)
    for k in range(FRAME):                                  # the ring: lit top/left, shaded bottom/right
        for (x, y) in ((k, 0), (0, k), (k, FRAME - 1), (FRAME - 1, k)):
            c.px(x, y, "fx", 4 if (y == 0 or x == 0) else 2)
    for k in range(1, FRAME - 1):
        for (x, y, s) in ((k, 1, 3), (1, k, 3), (k, FRAME - 2, 1), (FRAME - 2, k, 1)):
            c.px(x, y, "fx", s)
    c.line(3, 0, 10, 0, "fx", 5)
    c.line(0, 3, 0, 7, "fx", 5)
    for (x, y) in ((0, 0), (FRAME - 3, 0), (0, FRAME - 3), (FRAME - 3, FRAME - 3)):
        c.rect(x, y, x + 2, y + 2, "trim", 3)
        c.px(x, y, "trim", 5)
        c.px(x + 1, y, "trim", 4)
        c.px(x, y + 1, "trim", 4)
        c.px(x + 1, y + 1, "trim", 4)
        c.px(x + 2, y + 2, "trim", 1)
    if tier >= 3:                                           # epic+: a gem set in each side's midpoint
        for (x, y) in ((12, 0), (0, 12), (FRAME - 2, 12), (12, FRAME - 2)):
            c.rect(x, y, x + 1, y + 1, "gem", 3)
            c.px(x, y, "gem", 5)
    if tier == 4:                                           # legendary: gilded runs either side of the gems
        for k in (4, 5, 6, 7, 8, 17, 18, 19, 20, 21):
            c.px(k, 0, "trim", 5 if k < 9 else 4)
            c.px(0, k, "trim", 5 if k < 9 else 4)
            c.px(k, FRAME - 1, "trim", 2)
            c.px(FRAME - 1, k, "trim", 2)
    return c


FRAME_PALETTE = {
    "common": {"fx": "common", "gem": "common"},
    "uncommon": {"fx": "uncommon", "gem": "uncommon"},
    "rare": {"fx": "rare", "gem": "rare"},
    "epic": {"fx": "epic", "gem": "charm"},
    "legendary": {"fx": "legendary", "gem": "ruby"},
    "ability": {"fx": "ui_gold", "fx2": "ui_gold_2"},
}


def render_frame(kind: str) -> Image.Image:
    pal = {**DEFAULT, "dark": RAMPS["panel"], "fx2": RAMPS.get(kind, DEFAULT["fx2"])}
    pal.update({k: RAMPS[v] for k, v in FRAME_PALETTE[kind].items()})
    return frame(kind).render(pal)


# ------------------------------------------------------------------------------------------------------- build
def _atlas(images: list[Image.Image], columns: int, cell: int) -> tuple[Image.Image, list[list[int]]]:
    """Packs equal-size images into a grid with a 1 px transparent gutter all round each (no bleeding when the
    engine samples an icon's edge)."""
    pitch = cell + 2
    rows = -(-len(images) // columns)
    sheet = Image.new("RGBA", (columns * pitch, rows * pitch), (0, 0, 0, 0))
    rects = []
    for i, im in enumerate(images):
        x, y = (i % columns) * pitch + 1, (i // columns) * pitch + 1
        sheet.paste(im, (x, y))
        rects.append([x, y, im.width, im.height])
    return sheet, rects


def _label(s: str) -> str:
    """Short captions for the sheets: the family a potion or gem belongs to goes in the sub-label instead."""
    for prefix in ("potion_", "gem_", "trophy_"):
        s = s.removeprefix(prefix)
    return s.replace("_", " ")


def build() -> None:
    out = PACK / "icons"
    shutil.rmtree(out, ignore_errors=True)
    icons: dict[str, dict] = {}
    rendered: dict[str, Image.Image] = {}
    atlases = {}
    for cat, group, cell, cols in (("ability", ABILITIES, ICON, 8), ("status", STATUSES, STATUS, 6),
                                   ("item", ITEMS, ICON, 8)):
        imgs = [ic.render() for ic in group]
        sheet, rects = _atlas(imgs, cols, cell)
        fname = {"ability": "abilities.png", "status": "status.png", "item": "items.png"}[cat]
        save(sheet, out / fname)
        atlases[cat] = {"file": fname, "cell": [cell, cell], "columns": cols, "gutter": 1}
        for ic, im, rect in zip(group, imgs, rects):
            rendered[ic.id] = im
            entry = {"atlas": fname, "rect": rect, "size": [cell, cell], "category": cat,
                     "tags": [t for t in ic.tags if not t.startswith("shape:")]}
            shape = next((t[6:] for t in ic.tags if t.startswith("shape:")), None)
            if shape:
                entry["shape"] = shape                      # icons that are one drawing in different colours
            icons[ic.id] = entry
    frames = [render_frame(k) for k in FRAMES]
    sheet, rects = _atlas(frames, len(frames), FRAME)
    save(sheet, out / "frames.png")
    atlases["frame"] = {"file": "frames.png", "cell": [FRAME, FRAME], "columns": len(frames), "gutter": 1}
    write_json({
        "sizes": {"ability": ICON, "item": ICON, "status": STATUS, "frame": FRAME},
        "atlases": atlases,
        "icons": icons,
        "frames": {k: {"atlas": "frames.png", "rect": rect, "size": [FRAME, FRAME], "iconOffset": [1, 1]}
                   for k, rect in zip(FRAMES, rects)},
        "rarities": RARITIES,
        "notes": {
            "background": "icons are transparent; draw them over a frame or panel",
            "frame": "draw the 26x26 frame, then the 24x24 icon at iconOffset; items take their rarity's frame, "
                     "abilities the 'ability' frame",
            "scaling": "nearest-neighbour at whole multiples only (2x-3x on phones, 3x-4x on the TV)",
        },
    }, out / "manifest.json")
    _previews(rendered, frames)


def _in_frame(icon: Image.Image, fr: Image.Image) -> Image.Image:
    im = fr.copy()
    im.alpha_composite(icon, (1, 1))
    return im


def _previews(rendered: dict[str, Image.Image], frames: list[Image.Image]) -> None:
    def tiles(group):
        return [Tile(rendered[ic.id], _label(ic.id), ic.tags[0]) for ic in group]

    save(contact_sheet("Icons · abilities", tiles(ABILITIES), columns=8, zoom=4,
                       subtitle="24x24, transparent: the phone's action list and the TV's hotbar. Colour follows "
                                "the class (holy gold, arcane blue, fire, nature, shadow, blood)."),
         PREVIEWS / "16-icons-abilities.png")
    save(contact_sheet("Icons · status effects", tiles(STATUSES), columns=9, zoom=5,
                       subtitle="16x16, shown inline beside HP bars and in the turn order."),
         PREVIEWS / "16-icons-status.png")
    save(contact_sheet("Icons · items", tiles(ITEMS), columns=8, zoom=4,
                       subtitle="24x24. Potions share one bottle and gems one cut, recoloured."),
         PREVIEWS / "16-icons-items.png")

    picks = ["sword", "ring", "staff", "tome", "potion_greater_healing", "shield_bash"]
    ft = [Tile(f, k, "frame") for k, f in zip(FRAMES, frames)]
    ft += [Tile(_in_frame(rendered[p], f), k, _label(p)) for k, f, p in zip(FRAMES, frames, picks)]
    sec = [contact_sheet("Frames", ft, columns=6, zoom=5,
                         subtitle="26x26: a ring around the 24x24 icon with gold studs like the UI panels. Five "
                                  "rarities for items, plain UI gold for abilities.")]
    sec.append(_in_context(rendered, dict(zip(FRAMES, frames))))
    save(stack("Icons · frames and scale", sec), PREVIEWS / "16-icons-frames.png")




def _in_context(rendered: dict[str, Image.Image], frames: dict[str, Image.Image]) -> Image.Image:
    """The icons at the sizes players see them: the phone's action list (as in 04-phone-battle.png) at 2x, the bag
    at 2x, a few at 3x, and the status row beside an HP bar."""
    from arcpix.preview import BG, EDGE, INK, MUTED, PANEL
    actions = [("slash", "Longsword", "Melee · d20+5 to hit · 1d8+3 slashing"),
               ("shield_bash", "Shield Bash", "Melee · 1d6+3 · pushes 1 tile, knocks prone"),
               ("shield_wall", "Hold the Line", "Enemies that step next to you stop moving"),
               ("second_wind", "Second Wind", "Heal 1d10+3 · once per short rest"),
               ("heavy_strike", "Action Surge", "Used this battle")]
    bag = [("potion_healing", "common"), ("potion_mana", "common"), ("gold", "common"), ("gem_ruby", "uncommon"),
           ("key", "common"), ("helmet", "uncommon"), ("ring", "rare"), ("staff", "rare"), ("tome", "epic"),
           ("potion_greater_healing", "legendary")]
    large = ["smite", "fireball", "misty_step", "wild_shape", "flurry_of_blows", "hex", "eldritch_blast", "volley"]
    W, H = 1180, 700
    out = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(out)
    f_head, f_name, f_sub = font(15, True), font(18, True), font(14)
    d.text((22, 12), "Phone action list · 2x", font=f_head, fill=EDGE)
    for i, (ic, name, sub) in enumerate(actions):
        y = 40 + i * 70
        used = i == len(actions) - 1
        d.rounded_rectangle((22, y, 542, y + 62), 8, fill=PANEL, outline=(60, 64, 96, 255))
        im = scale(_in_frame(rendered[ic], frames["ability"]), 2)
        if used:                                            # spent abilities are dimmed but still recognisable
            im.putalpha(im.getchannel("A").point(lambda a: a * 2 // 5))
        out.alpha_composite(im, (32, y + 5))
        d.text((100, y + 10), name, font=f_name, fill=MUTED if used else INK)
        d.text((100, y + 34), sub, font=f_sub, fill=MUTED)
    d.text((580, 12), "Bag · 2x, rarity frames", font=f_head, fill=EDGE)
    for i, (ic, rar) in enumerate(bag):
        out.alpha_composite(scale(_in_frame(rendered[ic], frames[rar]), 2), (580 + (i % 5) * 62, 40 + (i // 5) * 62))
    d.text((580, 180), "Status row · 2x, beside an HP bar", font=f_head, fill=EDGE)
    d.rounded_rectangle((580, 208, 900, 220), 4, fill=(40, 44, 70, 255))
    d.rounded_rectangle((580, 208, 790, 220), 4, fill=(99, 211, 111, 255))
    for i, st in enumerate(STATUSES):
        out.alpha_composite(scale(rendered[st.id], 2), (580 + (i % 9) * 38, 230 + (i // 9) * 38))
    d.text((22, 404), "Hotbar · 3x", font=f_head, fill=EDGE)
    for i, ic in enumerate(large):
        out.alpha_composite(scale(_in_frame(rendered[ic], frames["ability"]), 3), (22 + i * 90, 432))
    d.text((22, 532), "1x · actual pixels", font=f_head, fill=EDGE)
    for i, ic in enumerate(CATALOGUE):
        out.alpha_composite(rendered[ic.id], (22 + (i % 41) * 27, 560 + (i // 41) * 28))
    return out.crop((0, 0, W, 560 + 28 * (-(-len(CATALOGUE) // 41)) + 16))
