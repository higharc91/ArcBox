"""Map props: trees, rocks, crates, chests, fences, campfires and the rest of the set dressing that stands on tiles.

Props are upright sprites (billboards) drawn in 3/4 view with light from the top-left, outlined like characters, and
anchored at their ground contact point so the game can stand them on any tile. Box-like props (crates, chests, wells)
are drawn as small isometric boxes so they sit naturally on the 2:1 grid. Everything is drawn in slots, so one drawing
ships in several palettes (green / autumn / blossom trees, mossy rocks, faction banners).

The manifest also carries the gameplay facts the tactics map needs: whether a prop blocks movement, what cover it
gives (none / half / full) and how tall it is in height steps (for line of sight).
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from typing import Callable

import numpy as np
from PIL import Image

from arcpix import tex
from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet

SLOTS = SlotTable()
for _s in ("bark", "leaf", "leaf2", "wood", "stone", "moss", "metal", "cloth1", "cloth2", "glow", "gold", "straw",
           "dark", "fruit"):
    SLOTS.id(_s)

PROP_RAMPS = {
    "straw": ramp("#d9b45a", shift=0.06, spread=0.85),
    "hay": ramp("#c9a447", shift=0.06),
    "ember": ramp("#ff9a3d", shift=0.08, spread=0.8),
    "flame": ramp("#ffc04a", shift=0.06, spread=0.7),
    "soot": ramp("#3b3236", shift=0.04, spread=1.3),
    "berry": ramp("#c43a4a", shift=0.07),
    "apple": ramp("#d9472f", shift=0.07),
    "crystal_blue": ramp("#6fc3ff", shift=0.05, spread=0.9),
    "crystal_violet": ramp("#a77bff", shift=0.06, spread=0.9),
    "crystal_green": ramp("#6be0a0", shift=0.06, spread=0.9),
    "snow": ramp("#e6eef8", shift=0.05, spread=0.55),
    "canvas": ramp("#d8cbb0", shift=0.05, spread=0.8),
    "bone": ramp("#dccfb0", shift=0.05, spread=0.75),
    "whitewash": ramp("#e4dccb", shift=0.04, spread=0.7),
    "cactus": ramp("#5f9a4a", shift=0.07),
    "cypress": ramp("#4f6b3a", shift=0.07),
    "spanish_moss": ramp("#8f9a72", shift=0.05, spread=0.85),
    "toadstool": ramp("#c8473a", shift=0.07),
    "bolete": ramp("#a8693c", shift=0.06),
    "glowcap": ramp("#5fd6c8", shift=0.05, spread=0.85),
    "wisp": ramp("#9fe8ff", shift=0.05, spread=0.75),
    "wisp_green": ramp("#a8f08a", shift=0.06, spread=0.75),
    "lily": ramp("#4f8f45", shift=0.07),
    "lotus": ramp("#f0a8c8", shift=0.05, spread=0.75),
    "ice": ramp("#a8dcf4", shift=0.05, spread=0.85),
    "magma": ramp("#ff8a2a", shift=0.08, spread=0.8),
    "lantern": ramp("#ffc860", shift=0.06, spread=0.7),
    "hide": ramp("#9c7652", shift=0.05),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "prop":
        return PROP_RAMPS[name]
    if fam == "tr":
        from .terrain import TR_RAMPS
        return TR_RAMPS[name]
    return FAMILIES[fam][name]


# ---------------------------------------------------------------------------------------------------- helpers
def blob(c: Canvas, slot: str, cx: float, cy: float, rx: float, ry: float, *, light=(-0.55, -0.85),
         lo: int = 1, hi: int = 5, base: float = 3.0, k: float = 1.9) -> None:
    """A lit ellipse: shade follows a sphere normal against the light, so clusters of blobs read as volume (tree
    canopies, bushes, boulders). Only fills; shading never leaks outside the ellipse."""
    x0, x1 = int(math.floor(cx - rx)), int(math.ceil(cx + rx))
    y0, y1 = int(math.floor(cy - ry)), int(math.ceil(cy + ry))
    lx, ly = light
    for y in range(max(0, y0), min(c.h, y1 + 1)):
        for x in range(max(0, x0), min(c.w, x1 + 1)):
            nx, ny = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            d = nx * nx + ny * ny
            if d > 1.0:
                continue
            nz = math.sqrt(max(0.0, 1 - d))
            dot = (nx * lx + ny * ly) * 0.9 + nz * 0.35 - 0.25            # normal . light
            sh = int(round(base + dot * k))
            # dithered terminator: alternate pixels where the shade is about to step down
            if abs(dot * k - round(dot * k)) > 0.44 and (x + y) % 2:
                sh -= 1
            c.px(x, y, slot, max(lo, min(hi, sh)))


def clump(c: Canvas, slot: str, cx: float, cy: float, rx: float, ry: float, *, base: float = 3.0,
          k: float = 2.4) -> None:
    """A foliage clump that casts a shadow on the clumps behind it: a dark crescent just below-right of its edge,
    then the lit blob. Stacked back to front, clumps separate into the lumpy canopy shape trees need."""
    sid = c.slots.id(slot)
    x0, x1 = int(cx - rx) - 1, int(cx + rx) + 3
    y0, y1 = int(cy - ry) - 1, int(cy + ry) + 3
    for y in range(max(0, y0), min(c.h, y1)):
        for x in range(max(0, x0), min(c.w, x1)):
            nx, ny = (x + 0.5 - cx - 1.2) / rx, (y + 0.5 - cy - 1.6) / ry
            if nx * nx + ny * ny <= 1.0 and c.slot[y, x] == sid:
                c.shade[y, x] = min(int(c.shade[y, x]), 1)
    blob(c, slot, cx, cy, rx, ry, base=base, k=k)


def leaf_texture(c: Canvas, slot: str, seed: int, *, density: float = 0.16) -> None:
    """Breaks a smooth canopy into leaf clumps: small light flecks on the lit side, dark notches on the shadow side."""
    r = tex.rng(seed)
    sid = c.slots.id(slot)
    ys, xs = np.nonzero(c.slot == sid)
    for y, x in zip(ys, xs):
        if r.random() > density:
            continue
        s = int(c.shade[y, x])
        # a leaf clump: a lit pixel with its shadow just below-right (light from the top-left)
        if s >= 3:
            c.shade[y, x] = min(5, s + 1)
            if y + 1 < c.h and x + 1 < c.w and c.slot[y + 1, x + 1] == sid:
                c.shade[y + 1, x + 1] = max(1, s - 1)
        elif s >= 1:
            c.shade[y, x] = s - 1


def iso_box(c: Canvas, cx: int, top: int, hw: int, h: int, slot: str, *, top_shade=4, left_shade=3, right_shade=2,
            top_slot: str | None = None) -> tuple[list, list, list]:
    """An isometric box: a 2:1 diamond top (half-width hw) and two side faces h pixels tall. Returns the three faces'
    polygons so callers can add detail (planks, bands, lids) inside them."""
    t = [(cx, top), (cx + hw, top + hw / 2), (cx, top + hw), (cx - hw, top + hw / 2)]
    left = [(cx - hw, top + hw / 2), (cx, top + hw), (cx, top + hw + h), (cx - hw, top + hw / 2 + h)]
    right = [(cx, top + hw), (cx + hw, top + hw / 2), (cx + hw, top + hw / 2 + h), (cx, top + hw + h)]
    c.polygon(left, slot, left_shade)
    c.polygon(right, slot, right_shade)
    c.polygon(t, top_slot or slot, top_shade)
    return t, left, right


def _underside(c: Canvas, slot: str, frac: float) -> None:
    """Darkens the bottom `frac` of each column of a canopy: the underside never sees the sun."""
    sid = c.slots.id(slot)
    for x in range(c.w):
        col = np.nonzero(c.slot[:, x] == sid)[0]
        if len(col) < 3:
            continue
        cut = col[-1] - int((col[-1] - col[0]) * frac)
        for y in col[col >= cut]:
            c.shade[y, x] = max(1, int(c.shade[y, x]) - 1)


def _canvas(w: int, h: int) -> Canvas:
    return Canvas(w, h, SLOTS)


def _finish(c: Canvas) -> Canvas:
    c.outline()
    return c


# ------------------------------------------------------------------------------------------------------ nature
def leaf_mass(c: Canvas, slot: str, cx: float, cy: float, rx: float, ry: float, r: np.random.Generator, *,
              base: float = 3.0, k: float = 2.4, lobes: int = 3) -> None:
    """One mass of foliage: a lit clump (casting its shadow on the masses behind it), lobes along its sunlit
    top-left rim so its outline is cauliflower-shaped rather than an ellipse, and scalloped leaf clusters inside
    (a lit arc over a shadowed one), denser on the lit side."""
    sid = c.slots.id(slot)
    clump(c, slot, cx, cy, rx, ry, base=base, k=k)
    for i in range(lobes):
        a = math.pi * (1.05 + 0.6 * i / max(1, lobes - 1)) + r.uniform(-0.15, 0.15)
        blob(c, slot, cx + math.cos(a) * rx * 0.72, cy + math.sin(a) * ry * 0.72, rx * 0.38, ry * 0.4,
             base=base + 0.3, k=k)
    mine = np.zeros((c.h, c.w), bool)
    ys, xs = np.mgrid[0:c.h, 0:c.w]
    mine = (((xs + 0.5 - cx) / (rx * 1.1)) ** 2 + ((ys + 0.5 - cy) / (ry * 1.1)) ** 2 <= 1) & (c.slot == sid)
    for x, y in tex.poisson(c.w, c.h, 3.2, r, tries=500):
        if not mine[y, x] or not (1 <= x < c.w - 2 and y < c.h - 2):
            continue
        s0 = int(c.shade[y, x])
        if s0 <= 1:
            continue
        lit = (x - cx) / rx + (y - cy) / ry < 0.2
        for dx in (-1, 0, 1):
            if c.slot[y, x + dx] == sid:
                c.shade[y, x + dx] = min(5, s0 + 1) if lit and dx < 1 else s0
            if c.slot[y + 1, x + dx] == sid:
                c.shade[y + 1, x + dx] = max(1, s0 - 1)


def _branches(c: Canvas, r: np.random.Generator, x: float, y: float, ang: float, length: float, width: int,
              slot: str = "bark") -> None:
    """A trunk's limbs reaching up into the crown, forking twice."""
    if length < 3:
        return
    ex, ey = x + math.cos(ang) * length, y + math.sin(ang) * length
    c.thick_line(x, y, ex, ey, width, slot, 3 if math.cos(ang) < 0 else 2)
    for d in (-0.5, 0.45):
        if r.random() < 0.85:
            _branches(c, r, ex, ey, ang + d + r.normal(0, 0.12), length * 0.6, max(1, width - 1), slot)


# A broadleaf crown as masses of foliage, back to front: (x, y, rx, ry, base shade). The back pair sits in the
# crown's own shade; the front masses get brighter towards the top-left. Gaps are left between the lower masses
# so the limbs (and the sky) show through.
CROWN = [(15, 14, 9, 7, 2.3), (34, 15, 9, 7, 2.2), (23, 8, 10, 7, 3.5), (9, 22, 8, 6, 3.2), (39, 23, 7, 6, 2.6),
         (24, 18, 8, 6, 3.0), (14, 30, 7, 5, 2.9), (35, 31, 6.5, 4.5, 2.4)]


def tree_round(seed: int = 1, *, fruit: bool = False) -> Canvas:
    """A broadleaf tree: a flared trunk lit on its left, limbs forking up into a crown of separate foliage masses
    that shade one another, with gaps you can see the branches and sky through."""
    r = tex.rng(seed)
    c = _canvas(48, 64)
    c.polygon([(21, 62), (27, 62), (26, 36), (22, 36)], "bark", 3)
    c.polygon([(18, 63), (30, 63), (27, 59), (21, 59)], "bark", 2)
    c.rect(25, 38, 26, 61, "bark", 2)
    c.rect(22, 38, 22, 60, "bark", 4)
    for y in range(41, 62, 3):                                     # bark grooves
        c.px(23 + (y % 2), y, "bark", 1)
    for i, (x, y, rx, ry, base) in enumerate(CROWN):
        if i == 2:                                                 # limbs go in front of the back masses
            _branches(c, r, 24, 38, -math.pi / 2 - 0.6, 12, 2)
            _branches(c, r, 24, 38, -math.pi / 2 + 0.55, 12, 2)
        leaf_mass(c, "leaf", x + r.uniform(-0.8, 0.8), y + r.uniform(-0.8, 0.8), rx, ry, r, base=base)
    _underside(c, "leaf", 0.18)
    if fruit:
        for _ in range(9):
            x, y = int(r.integers(10, 38)), int(r.integers(8, 32))
            if c.slot[y, x] == c.slots.id("leaf") and c.shade[y, x] >= 3:
                c.px(x, y, "fruit", 3)
                c.px(x, y - 1 if c.slot[y - 1, x] else y, "fruit", 5)
    return _finish(c)


def tree_pine(seed: int = 2, *, snowy: bool = False) -> Canvas:
    """A conifer: stacked drooping tiers, darker underneath, optional snow on each tier's top."""
    c = _canvas(40, 64)
    c.rect(18, 52, 21, 63, "bark", 3)
    c.rect(21, 52, 21, 63, "bark", 2)
    tiers = [(56, 17), (46, 15), (36, 12), (27, 9), (18, 7), (10, 4)]
    for k, (y, half) in enumerate(tiers):
        pts = [(20, y - 12 + k), (20 + half, y), (20 + half - 3, y + 1), (20, y - 1), (20 - half + 3, y + 1),
               (20 - half, y)]
        c.polygon(pts, "leaf", 3)
        # light on the left flank, shadow on the right and under the hem
        for yy in range(y - 12 + k, y + 2):
            for xx in range(0, 40):
                if c.slot[yy, xx] == c.slots.id("leaf") and yy >= y - 12 + k:
                    if xx < 20 - (yy - (y - 12 + k)) // 3:
                        c.shade[yy, xx] = 4
                    elif xx > 21:
                        c.shade[yy, xx] = 2
        c.rect(20 - half + 2, y, 20 + half - 2, y, "leaf", 1)
        if snowy:
            for xx in range(20 - half // 2, 20 + half // 3):
                yy = y - 12 + k + 1 + abs(xx - 20) // 2
                c.px(xx, yy, "glow", 4 if xx < 20 else 3)
    c.px(20, 1, "leaf", 4)
    leaf_texture(c, "leaf", 77, density=0.1)
    return _finish(c)


def tree_dead(seed: int = 3) -> Canvas:
    """A bare, twisted tree for swamps, graveyards and the blight."""
    r = tex.rng(seed)
    c = _canvas(44, 60)
    c.polygon([(19, 59), (25, 59), (24, 34), (21, 34)], "bark", 3)
    c.polygon([(15, 59), (29, 59), (26, 55), (18, 55)], "bark", 2)

    def branch(x: float, y: float, ang: float, length: float, width: int) -> None:
        if length < 3:
            return
        ex, ey = x + math.cos(ang) * length, y + math.sin(ang) * length
        c.thick_line(x, y, ex, ey, width, "bark", 3 if ang < -math.pi / 2 else 2)
        for d in (-0.55, 0.5):
            if r.random() < 0.8:
                branch(ex, ey, ang + d + r.normal(0, 0.15), length * 0.62, max(1, width - 1))

    branch(22, 36, -math.pi / 2 - 0.45, 13, 2)
    branch(23, 36, -math.pi / 2 + 0.5, 12, 2)
    branch(22, 42, -math.pi / 2 - 1.2, 9, 1)
    c.px(21, 46, "dark", 1)                                         # a knot hole
    c.px(21, 47, "dark", 0)
    return _finish(c)


def bush(seed: int = 4, *, berries: bool = False) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(28, 22)
    for x, y, rx, ry in ((9, 13, 7, 6), (19, 13, 7, 6), (14, 9, 8, 6), (14, 15, 10, 5)):
        clump(c, "leaf", x, y, rx, ry)
    leaf_texture(c, "leaf", seed + 3, density=0.2)
    _underside(c, "leaf", 0.3)
    if berries:
        for _ in range(7):
            x, y = int(r.integers(5, 23)), int(r.integers(5, 18))
            if c.slot[y, x] == c.slots.id("leaf"):
                c.px(x, y, "fruit", 4)
                c.px(x + 1, y + 1, "fruit", 2) if c.slot[y + 1, x + 1] else None
    return _finish(c)


def rock(size: str, seed: int = 5, *, mossy: bool = False) -> Canvas:
    """Boulders, faceted like the kit's (see `facet_rock`): flat lit faces with crisp edges, a smaller stone or two
    leaning on the big one, cracks; mossy variants grow moss on their upward faces."""
    r = tex.rng(seed)
    w, h, ax, ay, lumps = {"small": (20, 16, 10, 11, [(0, 0, 3.2, 2.8, 3.0)]),
                           "medium": (32, 26, 16, 18, [(0, 0, 5.5, 5, 6), (4, 3, 2.6, 2.4, 2.4)]),
                           "large": (44, 40, 22, 29, [(-1, -1, 8.5, 8, 11), (5.5, 5, 4, 3.6, 4.5),
                                                      (-6.5, 5.5, 2.4, 2.2, 2)])}[size]
    c, S = _kit(w, h, ax, ay)
    for u, v, ru, rv, rz in lumps:
        facet_rock(S, u, v, 0, ru, rv, rz, r, n=12 if ru > 4 else 8, moss=0.35 if mossy else 0.0)
    _cracks(c, "stone", r, len(lumps) + 1)
    return _finish(c)


def stump(seed: int = 6) -> Canvas:
    c = _canvas(24, 18)
    c.polygon([(5, 16), (19, 16), (18, 7), (6, 7)], "bark", 3)
    c.rect(15, 7, 18, 16, "bark", 2)
    c.polygon([(3, 17), (21, 17), (19, 14), (5, 14)], "bark", 2)
    c.ellipse(12, 6.5, 6.5, 3, "wood", 4)                           # cut face with rings
    c.ellipse(12, 6.5, 4, 1.8, "wood", 3)
    c.px(12, 6, "wood", 2)
    return _finish(c)


def log(seed: int = 7) -> Canvas:
    c = _canvas(34, 16)
    c.polygon([(4, 6), (28, 2), (31, 9), (7, 13)], "bark", 3)
    c.line(5, 11, 30, 6, "bark", 2)
    c.line(6, 7, 27, 3, "bark", 4)
    c.ellipse(29.5, 5.8, 3.2, 4, "wood", 4)
    c.ellipse(29.5, 5.8, 1.6, 2, "wood", 3)
    return _finish(c)


def tall_grass(seed: int = 8) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(18, 18)
    for _ in range(11):
        x = int(r.integers(2, 16))
        hgt = int(r.integers(6, 15))
        lean = r.choice([-1, 0, 1])
        for k in range(hgt):
            c.px(x + (lean * k) // 6, 17 - k, "leaf", 4 if k > hgt - 3 else (3 if k > 3 else 2))
    return _finish(c)


def reeds(seed: int = 9) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(18, 26)
    for i in range(7):
        x = 3 + i * 2 + int(r.integers(-1, 2))
        hgt = int(r.integers(12, 23))
        for k in range(hgt):
            c.px(x, 25 - k, "leaf", 3 if k < hgt - 4 else 4)
        if r.random() < 0.6:                                        # cattail head
            c.rect(x, 25 - hgt, x + 1, 25 - hgt + 3, "bark", 3)
            c.px(x, 25 - hgt, "bark", 4)
    return _finish(c)


def mushrooms(seed: int = 10) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(18, 16)
    for x, y, s in ((5, 14, 3), (11, 15, 4), (14, 12, 2)):
        c.rect(x, y - s, x + 1, y, "cloth2", 4)                     # stems
        blob(c, "fruit", x + 0.5, y - s - 1, s + 0.8, s * 0.6 + 0.6, k=1.6)
        c.px(x - 1, y - s - 2, "cloth2", 5)                         # spots
        c.px(x + 2, y - s - 1, "cloth2", 5)
    _ = r
    return _finish(c)


def crystals(seed: int = 11) -> Canvas:
    """A crystal cluster: faceted prisms with a lit face, a shadow face and a bright tip."""
    c = _canvas(24, 28)
    for x, top, w, lean in ((7, 9, 3, -2), (12, 2, 4, 0), (17, 11, 3, 2), (10, 14, 2, -1)):
        bx = x
        c.polygon([(bx - w, 26), (bx + w, 26), (bx + w + lean, top + 4), (bx + lean, top), (bx - w + lean, top + 4)],
                  "glow", 3)
        c.polygon([(bx, 26), (bx + w, 26), (bx + w + lean, top + 4), (bx + lean, top)], "glow", 2)
        c.line(bx - w + 1 + lean, top + 5, bx - w + 1, 25, "glow", 4)
        c.px(bx + lean, top, "glow", 5)
    c.polygon([(3, 27), (21, 27), (18, 24), (6, 24)], "stone", 2)
    return _finish(c)


# ------------------------------------------------------------------------------------------------------- built
def crate(seed: int = 20) -> Canvas:
    """A wooden crate as a small iso box: planked faces, a darker frame and corner nails."""
    c = _canvas(26, 28)
    t, left, right = iso_box(c, 13, 3, 12, 12, "wood")
    for k in (1, 2):                                                # plank lines on the side faces
        y = 3 + 6 + 4 * k
        c.line(1, y, 13, y + 6, "wood", 2)
        c.line(13, y + 6, 25, y, "wood", 1)
    c.line(1, 9, 1, 21, "wood", 2)                                 # frame edges
    c.line(13, 15, 13, 27, "wood", 1)
    c.line(25, 9, 25, 21, "wood", 1)
    c.line(4, 9, 13, 4, "wood", 5)                                 # lit top edge
    for x, y in ((3, 11), (11, 15), (15, 15), (23, 11)):
        c.px(x, y, "metal", 4)
    return _finish(c)


def barrel(seed: int = 21) -> Canvas:
    c = _canvas(18, 26)
    c.polygon([(3, 6), (15, 6), (16, 13), (15, 22), (3, 22), (2, 13)], "wood", 3)
    blob(c, "wood", 9, 14, 7.2, 9, k=1.4)
    for y in (8, 19):                                               # iron hoops
        c.rect(2, y, 16, y, "metal", 3)
        c.px(4, y, "metal", 5)
    c.ellipse(9, 5.5, 6.2, 2.4, "wood", 4)                          # lid
    c.ellipse(9, 5.5, 4.5, 1.4, "wood", 3)
    c.rect(5, 5, 13, 5, "wood", 2)
    for x in (6, 9, 12):                                            # staves
        c.line(x, 9, x, 18, "wood", 2)
    return _finish(c)


def sack(seed: int = 22) -> Canvas:
    c = _canvas(16, 16)
    blob(c, "straw", 8, 10, 6.5, 5.5)
    c.polygon([(6, 3), (10, 3), (9, 6), (7, 6)], "straw", 3)
    c.rect(6, 5, 10, 5, "cloth2", 2)                                # tie
    c.px(7, 10, "straw", 2)
    c.px(10, 12, "straw", 2)
    return _finish(c)


def chest(seed: int = 23, *, open_: bool = False) -> Canvas:
    """A treasure chest: iso box body, banded lid, a lock; open shows the lid tipped back and gold inside."""
    c = _canvas(26, 26)
    t, left, right = iso_box(c, 13, 8, 11, 8, "wood")
    for poly_x in (5, 21):                                          # metal bands
        c.line(poly_x, 12 + (0 if poly_x < 13 else 0), poly_x, 21, "metal", 3)
    c.line(13, 19, 13, 25, "metal", 2)
    if open_:
        c.polygon([(2, 13), (13, 18), (13, 8), (2, 3)], "wood", 3)      # lid tipped back, seen from inside
        c.polygon([(13, 8), (24, 3), (24, 13), (13, 18)], "wood", 2)
        for x, y in ((9, 14), (12, 15), (15, 14), (11, 13), (14, 13), (17, 13)):
            c.px(x, y, "gold", 5)
            c.px(x + 1, y, "gold", 3)
        c.px(13, 12, "gold", 5)
    else:
        c.polygon([(2, 13), (13, 7), (24, 13), (24, 11), (13, 4), (2, 11)], "wood", 4)  # rounded lid band
        c.line(2, 12, 13, 6, "metal", 4)
        c.line(13, 6, 24, 12, "metal", 3)
        c.rect(12, 19, 14, 21, "gold", 4)                           # lock
        c.px(13, 21, "dark", 0)
    return _finish(c)


def fence(seed: int = 24) -> Canvas:
    """One fence segment along the iso 'down-right' direction (mirror it for the other axis). Two posts and two
    rails; segments butt together tile to tile."""
    c = _canvas(36, 30)
    for x, y in ((3, 10), (33, 25)):                                # posts
        c.rect(x - 1, y - 10, x + 1, y + 2, "wood", 3)
        c.rect(x + 1, y - 10, x + 1, y + 2, "wood", 2)
        c.px(x - 1, y - 10, "wood", 5)
    for dy in (-7, -2):                                             # rails, following the 2:1 slope
        c.thick_line(4, 10 + dy, 32, 24 + dy, 2, "wood", 3)
        c.line(4, 10 + dy, 32, 24 + dy, "wood", 4)
    return _finish(c)


def campfire_frames(seed: int = 25, frames: int = 6) -> list[Canvas]:
    """Logs in a stone ring with a flickering flame; the flame tongues move on a loop."""
    out = []
    for f in range(frames):
        c = _canvas(28, 30)
        for k in range(9):                                          # stone ring
            a = 2 * math.pi * k / 9
            blob(c, "stone", 14 + math.cos(a) * 10, 25 + math.sin(a) * 4, 2.6, 2.0, k=1.5)
        c.thick_line(7, 25, 21, 21, 2, "bark", 2)                  # crossed logs
        c.thick_line(8, 21, 21, 26, 2, "bark", 3)
        ph = 2 * math.pi * f / frames
        for i, (dx, hgt, w) in enumerate(((-4, 9, 3), (0, 14, 4), (4, 10, 3), (-1, 7, 2))):
            sway = math.sin(ph + i * 1.7) * 1.5
            hh = hgt + math.sin(ph * 2 + i) * 2
            c.polygon([(14 + dx - w, 23), (14 + dx + w, 23), (14 + dx + sway, 23 - hh)], "glow", 3)
            c.polygon([(14 + dx - w + 1.5, 23), (14 + dx + w - 1.5, 23), (14 + dx + sway * 0.6, 23 - hh * 0.6)],
                      "glow", 5)
        r = tex.rng(seed + f)
        for _ in range(3):                                          # rising embers
            c.px(int(14 + r.normal(0, 3)), int(r.integers(2, 10)), "glow", 4)
        out.append(_finish(c))
    return out


def torch_frames(seed: int = 26, frames: int = 4) -> list[Canvas]:
    out = []
    for f in range(frames):
        c = _canvas(12, 34)
        c.rect(5, 12, 6, 33, "wood", 3)
        c.rect(6, 12, 6, 33, "wood", 2)
        c.rect(4, 10, 7, 13, "metal", 3)
        ph = 2 * math.pi * f / frames
        sway = math.sin(ph) * 1.2
        c.polygon([(3, 10), (8, 10), (5.5 + sway, 1 + math.sin(ph * 2))], "glow", 3)
        c.polygon([(4.5, 10), (6.5, 10), (5.5 + sway * 0.5, 5)], "glow", 5)
        out.append(_finish(c))
    return out


def lamp_post(seed: int = 27) -> Canvas:
    c = _canvas(16, 44)
    c.rect(7, 12, 8, 42, "metal", 3)
    c.rect(8, 12, 8, 42, "metal", 2)
    c.rect(5, 41, 10, 43, "metal", 2)
    c.polygon([(4, 4), (11, 4), (12, 12), (3, 12)], "metal", 2)    # lantern frame
    c.rect(5, 5, 10, 11, "glow", 4)
    c.rect(6, 6, 7, 9, "glow", 5)
    c.polygon([(3, 4), (12, 4), (7.5, 0)], "metal", 3)
    return _finish(c)


def banner_frames(seed: int = 28, frames: int = 4) -> list[Canvas]:
    """A faction banner on a pole; the cloth ripples. cloth1 is the field, cloth2 the charge."""
    out = []
    for f in range(frames):
        c = _canvas(24, 48)
        c.rect(4, 2, 5, 47, "wood", 3)
        c.rect(5, 2, 5, 47, "wood", 2)
        c.px(4, 1, "gold", 5)
        c.rect(3, 4, 18, 4, "wood", 3)                             # crossbar
        ph = 2 * math.pi * f / frames
        for x in range(6, 19):
            wave = math.sin(ph + x * 0.5) * 1.3
            y0, y1 = 5 + wave * 0.3, 26 + wave + (3 if x in (11, 12, 13) else 0)
            for y in range(int(y0), int(y1)):
                s = 3 + (1 if math.cos(ph + x * 0.5) > 0.3 else (-1 if math.cos(ph + x * 0.5) < -0.3 else 0))
                c.px(x, y, "cloth1", s)
        for y in range(10, 18):                                     # the charge: a simple chevron
            c.px(12 - abs(y - 14) // 1, y, "cloth2", 4)
            c.px(12 + abs(y - 14) // 1, y, "cloth2", 4)
        out.append(_finish(c))
    return out


def signpost(seed: int = 29) -> Canvas:
    c = _canvas(24, 34)
    c.rect(10, 6, 12, 33, "wood", 3)
    c.rect(12, 6, 12, 33, "wood", 2)
    c.polygon([(3, 8), (19, 8), (22, 11), (19, 14), (3, 14)], "wood", 4)
    c.polygon([(5, 17), (21, 17), (21, 22), (5, 22), (2, 19.5)], "wood", 3)
    for x in range(6, 17, 3):
        c.px(x, 11, "dark", 1)
        c.px(x + 1, 19, "dark", 1)
    return _finish(c)


def well(seed: int = 30) -> Canvas:
    c = _canvas(34, 40)
    iso_box(c, 17, 20, 14, 10, "stone", top_slot="dark", top_shade=0)
    c.polygon([(7, 25), (17, 30), (27, 25), (17, 21)], "dark", 1)  # water inside
    c.px(15, 25, "glow", 4)
    for x in (5, 29):                                               # posts and roof
        c.rect(x - 1, 6, x, 24, "wood", 3)
    c.polygon([(1, 8), (17, 0), (33, 8), (17, 13)], "wood", 4)
    c.line(1, 9, 17, 14, "wood", 2)
    c.line(17, 14, 33, 9, "wood", 1)
    c.line(17, 12, 17, 22, "dark", 1)                               # rope
    c.rect(16, 21, 18, 23, "wood", 3)                               # bucket
    return _finish(c)


def tent(seed: int = 31) -> Canvas:
    c = _canvas(44, 34)
    c.polygon([(4, 30), (22, 3), (24, 3), (24, 33)], "cloth1", 4)
    c.polygon([(24, 3), (40, 26), (40, 30), (24, 33)], "cloth1", 2)
    c.polygon([(18, 31), (23, 14), (25, 31)], "dark", 0)            # doorway
    c.line(22, 3, 4, 30, "cloth1", 5)
    c.line(24, 2, 24, 0, "wood", 3)
    c.line(4, 30, 2, 33, "dark", 1)
    c.line(40, 30, 43, 33, "dark", 1)
    return _finish(c)


def haystack(seed: int = 32) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(30, 24)
    blob(c, "straw", 15, 14, 13, 9, k=1.6)
    for _ in range(26):                                             # straw strands
        x, y = int(r.integers(4, 26)), int(r.integers(6, 21))
        if c.slot[y, x]:
            c.line(x, y, x + int(r.choice([-1, 1])), y + 2, "straw", int(r.choice([2, 4])))
    return _finish(c)


def gravestone(seed: int = 33, *, cross: bool = False) -> Canvas:
    c = _canvas(18, 24)
    if cross:
        c.rect(7, 3, 10, 22, "stone", 3)
        c.rect(3, 7, 14, 10, "stone", 3)
        c.rect(10, 3, 10, 22, "stone", 2)
        c.rect(7, 3, 7, 22, "stone", 4)
    else:
        c.polygon([(3, 22), (14, 22), (14, 8), (12, 4), (5, 4), (3, 8)], "stone", 3)
        c.rect(13, 6, 14, 22, "stone", 2)
        c.rect(4, 6, 4, 21, "stone", 4)
        c.rect(6, 10, 11, 10, "stone", 1)                           # inscription
        c.rect(6, 13, 10, 13, "stone", 1)
    c.polygon([(1, 23), (17, 23), (15, 21), (3, 21)], "moss", 3)
    return _finish(c)


def pillar(seed: int = 34, *, broken: bool = False) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(22, 56)
    top = 18 if broken else 6
    c.rect(4, 50, 17, 55, "stone", 2)                               # plinth
    c.rect(3, 48, 18, 50, "stone", 4)
    for x in range(6, 16):                                          # fluted shaft, lit from the left
        s = 4 if x < 9 else (3 if x < 13 else 2)
        c.rect(x, top, x, 47, "stone", s)
        if x % 3 == 0:
            c.rect(x, top + 2, x, 46, "stone", max(1, s - 1))
    if broken:
        for x in range(6, 16):                                      # jagged break
            c.rect(x, top - int(r.integers(0, 5)), x, top, "stone", 3)
        for k in range(4):
            c.rect(14 + k, 52 - k % 2, 15 + k, 53, "stone", 3)
    else:
        c.rect(4, 3, 17, 6, "stone", 4)                             # capital
        c.rect(5, 1, 16, 2, "stone", 3)
    return _finish(c)


def altar(seed: int = 35) -> Canvas:
    c = _canvas(32, 28)
    iso_box(c, 16, 6, 14, 10, "stone")
    c.polygon([(9, 13), (16, 16), (23, 13), (16, 10)], "cloth1", 3)  # altar cloth
    c.rect(15, 2, 16, 9, "gold", 4)                                  # candle-holder / idol
    c.px(15, 1, "glow", 5)
    return _finish(c)


def rubble(seed: int = 36) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(28, 16)
    for _ in range(7):
        blob(c, "stone", r.uniform(5, 23), r.uniform(8, 13), r.uniform(2, 4.5), r.uniform(1.6, 3), k=2.0)
    return _finish(c)


# ------------------------------------------------------------------------------------------------- the kit
# Props from here on are drawn in the map's own projection by the dungeon set's `Solid` splat renderer (prop space:
# u towards screen lower-right, v towards lower-left, z up; a tile is 16 x 16 sprite px and a height step 8), so
# their light and perspective agree with the terrain blocks and the dungeon props. Each canvas carries its anchor
# (the tile centre on the ground) and, for light sources, where its light sits (`lights`, image pixels).
def _kit(w: int, h: int, ax: float, ay: float):
    from .dungeon import Solid                                       # lazy: dungeon imports this module
    c = _canvas(w, h)
    c.anchor = (int(ax), int(ay))                                    # type: ignore[attr-defined]
    c.lights = []                                                    # type: ignore[attr-defined]
    return c, Solid(c, ax, ay)


def _lit(c: Canvas, S, u: float, v: float, z: float) -> None:
    x, y = S.xy(u, v, z)
    c.lights.append((round(x), round(y)))                            # type: ignore[attr-defined]


def _kdone(c: Canvas) -> Canvas:
    """Outline everything but the light itself (a flame or glow with a dark rim reads as a sticker)."""
    c.outline(skip={"glow"})
    return c


def facet_rock(S, u: float, v: float, z0: float, ru: float, rv: float, rz: float, r: np.random.Generator, *,
               n: int = 18, slot: str = "stone", base: float = 0.0, moss: float = 0.0, moss_slot: str = "moss",
               lean: tuple[float, float] = (0.0, 0.0)) -> None:
    """A faceted boulder: the intersection of `n` randomly tilted half-spaces (a real polyhedron, so each face is
    one flat shade with crisp edges between them), sitting on a flat base at z0. With `moss`, the upward-facing
    faces grow moss (or snow, or ash: whatever `moss_slot` is mapped to) in proportion."""
    Nf = r.normal(size=(n, 3))
    Nf[:, 2] = np.abs(Nf[:, 2]) * 0.9 + 0.1
    Nf = np.vstack([Nf, [[0, 0, 1]]])
    Nf /= np.linalg.norm(Nf, axis=1)[:, None]
    D = np.r_[r.uniform(0.8, 1.0, n), 0.92]
    nt = int(2 * math.pi * max(ru, rv) * 3.2) + 24
    nph = int(max(ru, rv, rz) * 4.5) + 12
    th = np.linspace(0, 2 * math.pi, nt, endpoint=False)
    ph = np.linspace(0, math.pi / 2 + 0.25, nph)
    TH, PH = np.meshgrid(th, ph)
    d = np.c_[(np.sin(PH) * np.cos(TH)).ravel(), (np.sin(PH) * np.sin(TH)).ravel(), np.cos(PH).ravel()]
    dots = d @ Nf.T
    t = np.where(dots > 1e-3, D[None] / np.maximum(dots, 1e-3), np.inf)
    k = np.argmin(t, axis=1)
    rad = np.minimum(t[np.arange(len(d)), k], 1.35)
    zz = rz * rad * d[:, 2]
    P = np.c_[u + ru * rad * d[:, 0] + lean[0] * zz, v + rv * rad * d[:, 1] + lean[1] * zz, z0 + zz]
    N = Nf[k] / np.array([ru, rv, rz])
    N /= np.linalg.norm(N, axis=1)[:, None]
    keep = P[:, 2] >= z0
    S.plot(P[keep], N[keep], slot, base=base, dither=False)
    if moss > 0:
        up = keep & (N[:, 2] > 1 - moss * 0.8)
        S.plot(P[up], N[up], moss_slot, base=base - 0.3, dither=False)


def _cracks(c: Canvas, slot: str, r: np.random.Generator, n: int) -> None:
    """Hairline cracks over a drawn surface: dark pixels with a lit pixel to their right."""
    sid = c.slots.id(slot)
    ys, xs = np.nonzero(c.slot == sid)
    if not len(ys):
        return
    for _ in range(n):
        i = int(r.integers(len(ys)))
        x, y = int(xs[i]), int(ys[i])
        for _ in range(int(r.integers(3, 7))):
            if not (0 <= x < c.w - 1 and 0 <= y < c.h) or c.slot[y, x] != sid:
                break
            c.shade[y, x] = 1
            if c.slot[y, x + 1] == sid:
                c.shade[y, x + 1] = min(5, int(c.shade[y, x + 1]) + 1)
            x += int(r.choice([-1, 0, 1]))
            y += 1


def boulder(seed: int = 501) -> Canvas:
    """A big faceted boulder with a smaller one leaning on it: a whole tile of cover."""
    r = tex.rng(seed)
    c, S = _kit(56, 56, 28, 42)
    facet_rock(S, -1, -1, 0, 12, 11, 17, r, n=13, moss=0.3)
    facet_rock(S, 7, 6.5, 0, 6, 5.5, 7, r, n=10, moss=0.3)
    facet_rock(S, -8.5, 8, 0, 3.4, 3, 2.8, r, n=8, moss=0.2)
    _cracks(c, "stone", r, 4)
    return _finish(c)


def crag(seed: int = 511) -> Canvas:
    """A rock spire: stacked faceted blocks narrowing and leaning as they rise, two height steps of wall."""
    r = tex.rng(seed)
    c, S = _kit(56, 84, 28, 68)
    facet_rock(S, 0, 0, 0, 11, 10.5, 14, r, n=13, moss=0.25)
    facet_rock(S, -1.5, -1.0, 10, 8.5, 8, 16, r, n=12, moss=0.25, lean=(-0.08, -0.05))
    facet_rock(S, -3.5, -2.5, 24, 5.5, 5, 13, r, n=10, moss=0.4, lean=(-0.12, -0.06))
    facet_rock(S, 8, 6, 0, 5, 4.5, 6, r, n=9, moss=0.3)
    _cracks(c, "stone", r, 6)
    return _finish(c)


def rock_cluster(seed: int = 521) -> Canvas:
    """A scatter of mid-sized stones that fills a tile without blocking sight: half cover."""
    r = tex.rng(seed)
    c, S = _kit(48, 36, 24, 24)
    for u, v, rr, hh in ((-4, -3.5, 5.5, 6.5), (4.5, -2.5, 4.4, 5), (-1, 5, 4.6, 4.2), (6.5, 5.5, 2.8, 2.6),
                         (-8, 2, 2.6, 2.2)):
        facet_rock(S, u, v, 0, rr, rr * 0.9, hh, r, n=9, moss=0.25)
    _cracks(c, "stone", r, 3)
    return _finish(c)


def ice_spikes(seed: int = 531) -> Canvas:
    """Ice crystals forcing up out of a frozen bank: faceted prisms with a lit face, a cold shadow face and a
    white tip."""
    r = tex.rng(seed)
    c, S = _kit(36, 48, 18, 38)
    facet_rock(S, 0, 0, 0, 7, 6.5, 2.5, r, n=10, slot="stone", moss=0.6)
    for u, v, hgt, rad, lean in ((0, 0, 26, 2.6, (-0.1, -0.05)), (-4, 2.5, 15, 1.8, (-0.25, 0.1)),
                                 (4, -2, 18, 2.0, (0.2, -0.12)), (2.5, 4, 9, 1.4, (0.15, 0.2)),
                                 (-3.5, -3.5, 11, 1.5, (-0.15, -0.2))):
        body = hgt * 0.75
        S.frustum(u, v, 0.5, body, rad, rad * 0.9, "glow", seg=6, lean=lean, cap=False, twist=r.uniform(0, 1))
        tu, tv = u + lean[0] * body, v + lean[1] * body
        S.frustum(tu, tv, body, hgt, rad * 0.9, 0.08, "glow", seg=6, lean=lean, cap=False)
        x, y = S.xy(u + lean[0] * hgt, v + lean[1] * hgt, hgt)
        c.px(x, y, "glow", 5)
    c.outline()
    return c


def crystal_large_frames(frames: int = 8) -> list[Canvas]:
    """A big crystal outcrop on a faceted rock: prisms lean out in a fan and a band of light climbs through them
    (the pulse). A light source: see the manifest's `light`."""
    out = []
    xs = [(0, 0, 32, 3.4, (0.0, 0.0), 0.2), (-4.5, 2.5, 20, 2.4, (-0.22, 0.1), 0.9),
          (4.5, -2.5, 23, 2.6, (0.18, -0.1), 0.5), (2.5, 5, 12, 1.9, (0.12, 0.22), 1.4),
          (-4, -4.5, 14, 2.0, (-0.14, -0.2), 0.1), (6, 3, 8, 1.5, (0.3, 0.1), 0.7)]
    for f in range(frames):
        r = tex.rng(541)
        c, S = _kit(40, 56, 20, 46)
        facet_rock(S, 0, 0, 0, 8.5, 8, 4, r, n=12)
        wave = (f / frames) * 44 - 6
        for u, v, hgt, rad, lean, tw in xs:
            body = hgt * 0.78
            fn = lambda Z, w=wave: 0.9 * np.exp(-((Z - w) / 3.0) ** 2) - 0.1   # noqa: E731
            S.frustum(u, v, 2.5, body, rad, rad * 0.94, "glow", seg=6, lean=lean, cap=False, base=fn, twist=tw)
            tu, tv = u + lean[0] * (body - 2.5), v + lean[1] * (body - 2.5)
            S.frustum(tu, tv, body, hgt, rad * 0.94, 0.08, "glow", seg=6, lean=lean, cap=False, base=fn, twist=tw)
            x, y = S.xy(u + lean[0] * (hgt - 2.5), v + lean[1] * (hgt - 2.5), hgt)
            c.px(x, y, "glow", 5)
        _lit(c, S, 0, 0, 16)
        out.append(_kdone(c))
    return out


# ---- fences and walls: one segment runs along U, across a tile edge to edge (mirror the sprite for V)
def fence_stone(seed: int = 551) -> Canvas:
    """A dry-stone field wall: rough courses of faceted stones with a row of cope stones on top."""
    r = tex.rng(seed)
    c, S = _kit(40, 34, 20, 18)
    for z, rad, n in ((0, 2.6, 6), (3.2, 2.4, 6), (6.4, 2.2, 6)):
        for k in range(n + 1):
            u = -8 + (k + 0.5 * ((z // 3) % 2)) * 16 / n
            if u > 8.5:
                continue
            facet_rock(S, u, r.uniform(-0.3, 0.3), z, rad * r.uniform(0.9, 1.2), 2.6, 2.0, r, n=7,
                       moss=0.3 if z >= 6 else 0.0)
    _cracks(c, "stone", r, 3)
    return _finish(c)


def fence_picket(seed: int = 561) -> Canvas:
    """A painted picket fence (cloth1 is the paint) on two rails between square posts."""
    c, S = _kit(40, 34, 20, 18)
    for u in (-7.5, 7.5):
        S.box(u - 0.8, -0.8, 0, u + 0.8, 0.8, 12, "wood")
        S.frustum(u, 0, 12, 13.4, 0.9, 0.1, "wood", seg=4, twist=math.pi / 4)
    for z in (3.5, 8.5):
        S.box(-7.5, -0.4, z, 7.5, 0.3, z + 1.1, "wood", base=-0.3)
    for k in range(9):
        u = -6.0 + k * 1.5
        S.box(u - 0.5, 0.35, 0.5, u + 0.5, 0.9, 10.5, "cloth1")
        S.tri((u - 0.5, 0.9, 10.5), (u + 0.5, 0.9, 10.5), (u, 0.9, 11.8), "cloth1")
    return _finish(c)


def fence_iron(seed: int = 571) -> Canvas:
    """Wrought-iron railings between stone piers: spear-tipped bars and a scroll band."""
    c, S = _kit(40, 44, 20, 26)
    for u in (-7.5, 7.5):
        S.box(u - 1.6, -1.6, 0, u + 1.6, 1.6, 15, "stone")
        S.box(u - 2.0, -2.0, 15, u + 2.0, 2.0, 16.4, "stone", base=0.3)
        S.frustum(u, 0, 16.4, 18.2, 1.2, 0.2, "stone", seg=4, twist=math.pi / 4)
    S.box(-6, -0.3, 1.0, 6, 0.3, 1.8, "metal")
    S.box(-6, -0.3, 11.5, 6, 0.3, 12.2, "metal")
    for k in range(9):
        u = -5.2 + k * 1.3
        S.cyl(u, 0, 1, 14.5, 0.32, "metal")
        S.frustum(u, 0, 14.5, 16.2, 0.6, 0.0, "gold" if k % 2 else "metal", seg=4)
    for k in range(4):                                               # scrollwork between the rails
        u = -4.6 + k * 3.0
        th = np.linspace(0, 2 * math.pi, 18)
        for t0, t1 in zip(th[:-1], th[1:]):
            S.line((u + 0.8 * math.cos(t0), 0.35, 6.5 + 1.2 * math.sin(t0)),
                   (u + 0.8 * math.cos(t1), 0.35, 6.5 + 1.2 * math.sin(t1)), "metal", 2 if t0 > math.pi else 4)
    return _finish(c)


def palisade(seed: int = 581) -> Canvas:
    """A palisade of sharpened logs lashed together: full cover, two height steps."""
    r = tex.rng(seed)
    c, S = _kit(40, 56, 20, 36)
    for k in range(8):
        u = -7.0 + k * 2.0
        hgt = 19 + r.uniform(-1.5, 1.5)
        S.cyl(u, 0, 0, hgt, 1.0, "bark", base=0.35 if k % 2 else -0.25)
        S.frustum(u, 0, hgt, hgt + 3.2, 1.05, 0.05, "wood", seg=5, twist=r.uniform(0, 1))
    for z in (5.5, 13.0):                                            # rope lashings
        S.line((-8, 1.1, z), (8, 1.1, z), "straw", 2)
        S.line((-8, 1.1, z + 0.5), (8, 1.1, z + 0.5), "straw", 3)
    return _finish(c)


def masonry(S, r: np.random.Generator, u0: float, u1: float, v0: float, v1: float, z0: float, z1: float, *,
            course: float = 4.0, lengths=(4.5, 6.5), tops=None, slot: str = "stone") -> None:
    """Coursed blocks between u0..u1 (a wall along U, v0..v1 thick), each with its own tone. The blocks are too
    small for their joints to survive as geometry, so the joints are then painted onto the face the camera sees
    (+V): every pixel there is mapped back to its (u, z) on the wall, and bed and head joints go in as dark lines
    with a lit arris above each bed joint. `tops(u)` ragged-tops a ruin: a block is laid only if it fits under."""
    c = S.c
    heads: list[tuple[float, float, list[float]]] = []
    row = 0
    z = z0
    while z < z1 - 0.5:
        u = u0 - (lengths[0] * 0.5 if row % 2 else 0)
        cuts = []
        while u < u1 - 0.3:
            w = r.uniform(*lengths)
            a, b = max(u, u0), min(u + w, u1)
            top = tops(0.5 * (a + b)) if tops else z1
            if b - a > 0.6 and z + course <= top + 0.1:
                S.box(a, v0, z, b, v1, z + course, slot, base=float(r.choice([-0.35, 0, 0, 0.3])))
                cuts.append(b)
            u += w
        heads.append((z, z + course, cuts))
        row += 1
        z += course
    sid = c.slots.id(slot)
    for y in range(c.h):
        for x in range(c.w):
            if c.slot[y, x] != sid:
                continue
            u = x + 0.5 - S.ax + v1
            zz = S.ay + (u + v1) / 2 - (y + 0.5)
            if not (u0 - 0.2 <= u <= u1 + 0.2):
                continue
            for za, zb, cuts in heads:
                if za - 0.2 <= zz < zb:
                    on_top = abs(zz - zb) < 0.7 and not any(za2 == zb for za2, _, _ in heads)
                    if on_top:
                        break
                    if zz < za + 0.9:
                        c.shade[y, x] = 1                            # bed joint
                    elif zz > zb - 1.3:
                        c.shade[y, x] = min(5, int(c.shade[y, x]) + 1)   # the arris catches the light
                    elif any(abs(u - k) < 0.6 for k in cuts[:-1]):
                        c.shade[y, x] = 1                            # head joint
                    break


def _ivy(c: Canvas, r: np.random.Generator, frac: float = 0.6, thresh: float = 0.68) -> None:
    ys, xs = np.nonzero(c.slot == c.slots.id("stone"))
    ivy = tex.fbm(c.w, c.h, 4, r, octaves=2)
    for y, x in zip(ys, xs):
        if ivy[y, x] > thresh and x < c.w * frac:
            c.slot[y, x] = c.slots.id("moss")
            c.shade[y, x] = 4 if ivy[y, x] > thresh + 0.1 else 3


def ruin_wall(seed: int = 591) -> Canvas:
    """A broken stretch of castle wall: dressed blocks in courses, the top edge ragged where the upper courses
    fell, a few fallen blocks at its foot and ivy (moss) climbing the lit face."""
    r = tex.rng(seed)
    c, S = _kit(44, 64, 22, 42)
    knots = r.uniform(8, 24, 6)
    knots[2] = 24
    tops = lambda u: float(np.interp(u, np.linspace(-8, 8, 6), knots))   # noqa: E731
    masonry(S, r, -8, 8, -1.8, 1.8, 0, 24, tops=tops)
    for u, v in ((-4, 4.5), (3, 4.0), (6.5, 6.5)):
        S.box(u - 1.6, v - 1.2, 0, u + 1.6, v + 1.2, 2.4, "stone", base=r.uniform(-0.3, 0.2))
    _ivy(c, r)
    return _finish(c)


def ruin_arch(seed: int = 601) -> Canvas:
    """A ruined archway: two piers and a half-fallen arch of voussoirs, rubble below. Units walk through it."""
    r = tex.rng(seed)
    c, S = _kit(48, 80, 24, 56)
    for u in (-7.5, 7.5):
        masonry(S, r, u - 2.4, u + 2.4, -2.4, 2.4, 0, 28, lengths=(4.8, 5.0), course=4.0,
                tops=(lambda x: 21.0) if u > 0 else None)
    for k in range(9):
        a = math.pi - k * math.pi / 8
        if k >= 6:
            continue                                                 # the right-hand voussoirs have fallen
        uc, zc = 7.5 * math.cos(a), 27 + 7.5 * math.sin(a)
        S.box(uc - 1.3, -2.2, zc - 1.3, uc + 1.3, 2.2, zc + 1.3, "stone", base=float(r.choice([-0.2, 0, 0.2])))
    for u, v in ((5, 5), (8, 3)):
        facet_rock(S, u, v, 0, 2.2, 2, 2, r, n=8)
    _ivy(c, r, 1.0, 0.72)
    return _finish(c)


# ---- carts, ladders, cages, barricades
def cart(seed: int = 611) -> Canvas:
    """A two-wheeled hand cart along U, its bed heaped with sacks (straw slot) and its shafts resting down."""
    c, S = _kit(48, 40, 24, 26)
    S.box(-7, -4.5, 5, 6, 4.5, 6.2, "wood")
    for v in (-4.5, 4.0):
        S.box(-7, v, 6.2, 6, v + 0.5, 9.5, "wood", base=-0.15)
    S.box(-7.5, -4.5, 6.2, -7, 4.5, 9.5, "wood", base=-0.2)
    S.box(6, -4.5, 6.2, 6.5, 4.5, 9.5, "wood")
    for k in range(3):
        S.line((-6.5 + k * 4, 4.55, 6.4), (-6.5 + k * 4, 4.55, 9.4), "wood", 2)
    for u, v, rr in ((-3.5, -1.2, 2.6), (0.5, 1.4, 2.8), (3.6, -1.6, 2.4)):
        S.ellipsoid(u, v, 7.8, rr, rr * 0.85, rr * 0.75, "straw")
    th = np.linspace(0, 2 * math.pi, 80)
    for vw in (5.3, -5.3):                                           # wheels on the V faces
        P = np.c_[np.cos(th) * 4.6, np.full_like(th, vw), 4.6 + np.sin(th) * 4.6]
        for dr in (0, 0.35, 0.7):
            P2 = np.c_[np.cos(th) * (4.6 - dr), np.full_like(th, vw), 4.6 + np.sin(th) * (4.6 - dr)]
            S.plot(P2, None, "wood", shade=3 if dr < 0.5 else 2)
        S.plot(P[th < 2.4], None, "metal", shade=4)
        for a in np.linspace(0, math.pi, 4, endpoint=False):
            S.line((math.cos(a) * 4.2, vw, 4.6 + math.sin(a) * 4.2), (-math.cos(a) * 4.2, vw, 4.6 - math.sin(a) * 4.2),
                   "wood", 2)
        S.sphere(0, vw, 4.6, 0.8, "metal")
    for v in (-3, 3):
        S.line((6.5, v, 6), (13, v, 0.4), "wood", 3)
    return _finish(c)


def ladder(seed: int = 621) -> Canvas:
    """A ladder leaning on a ledge two height steps up: place it on the tile at the foot of the cliff, on the
    side facing the ledge (mirror for the other axis)."""
    c, S = _kit(28, 52, 14, 44)
    for v in (-2.2, 2.2):
        S.box(-1.5 - 0.4, v - 0.4, 0, -1.5 + 0.4, v + 0.4, 0.1, "wood")
        S.line((2.0, v, 0), (-6.5, v, 19), "wood", 3 if v > 0 else 2)
        S.line((2.5, v, 0), (-6.0, v, 19), "wood", 2)
    for k in range(7):
        t = (k + 0.6) / 7.5
        u, z = 2.2 - 8.6 * t, 19 * t
        S.line((u, -2.2, z), (u, 2.2, z), "wood", 4)
    return _finish(c)


def cage(seed: int = 631) -> Canvas:
    """A standing iron cage: a round base, bars to a domed top with a hanging ring. Half cover."""
    c, S = _kit(32, 52, 16, 40)
    S.cyl(0, 0, 0, 1.4, 6.2, "metal", base=-0.3)
    for k in range(12):
        a = 2 * math.pi * k / 12
        S.line((math.cos(a) * 5.8, math.sin(a) * 5.8, 1.4), (math.cos(a) * 5.8, math.sin(a) * 5.8, 17),
               "metal", 4 if math.cos(a) + math.sin(a) < -0.4 else (2 if math.cos(a) + math.sin(a) > 0.6 else 3))
        S.line((math.cos(a) * 5.8, math.sin(a) * 5.8, 17), (0, 0, 21), "metal", 3)
    th = np.linspace(0, 2 * math.pi, 60)
    for z in (9, 17):
        S.plot(np.c_[np.cos(th) * 5.9, np.sin(th) * 5.9, np.full_like(th, z)], None, "metal", shade=3)
    S.sphere(0, 0, 21.4, 1.0, "metal")
    th2 = np.linspace(0, 2 * math.pi, 24)
    S.plot(np.c_[np.zeros_like(th2), np.cos(th2) * 1.2, 23.4 + np.sin(th2) * 1.2], None, "metal", shade=4)
    return _finish(c)


def barricade(seed: int = 641) -> Canvas:
    """A cheval-de-frise: a log along U bristling with sharpened stakes in crossed pairs. Half cover; blocks."""
    r = tex.rng(seed)
    c, S = _kit(48, 36, 24, 22)
    S.hcyl("u", (0, 0, 4.2), 8.5, 1.3, "bark", cap_slot="wood")
    for k in range(6):
        u = -7 + k * 2.8
        for sgn in (1, -1):
            a = 0.85 * sgn + r.normal(0, 0.06)
            tip = (u, math.sin(a) * 9, 4.2 + math.cos(a) * 9)
            S.line((u, -math.sin(a) * 2, 4.2 - math.cos(a) * 2), tip, "wood", 3 if sgn > 0 else 2)
            S.line((u + 0.35, -math.sin(a) * 2, 4.2 - math.cos(a) * 2), tip, "wood", 2)
            x, y = S.xy(*tip)
            c.px(x, y, "wood", 5)
    for k in range(4):                                               # rope lashings
        S.hcyl("u", (-6 + k * 4, 0, 4.2), 0.3, 1.45, "straw")
    return _finish(c)


def mantlet(seed: int = 651) -> Canvas:
    """A siege mantlet: a tall shield wall of planks on a prop, faced with hide (cloth1). Full cover."""
    c, S = _kit(44, 60, 22, 40)
    for k in range(6):
        u = -7.5 + k * 3.0
        S.box(u, -0.6, 0, u + 2.8, 0.6, 22 + (k % 2), "wood", base=-0.1 + 0.15 * (k % 2))
    S.quad((-7, 0.65, 18), (7, 0.65, 18), (-7, 0.65, 8), "cloth1", base=0.1)
    for z in (5, 19):
        S.box(-8, 0.6, z, 9, 1.2, z + 1.2, "wood", base=-0.3)
    for u in (-5, 5):
        S.line((u, -0.6, 14), (u, -7, 0), "wood", 2)
    for u in (-6, -2, 2, 6):
        for z in (8.3, 17.7):
            x, y = S.xy(u, 0.7, z)
            c.px(x, y, "metal", 4)
    return _finish(c)


def skull_pike(seed: int = 661) -> Canvas:
    """A warning post: a skull on a sharpened stake over a little cairn of bones and stones."""
    r = tex.rng(seed)
    c, S = _kit(28, 52, 14, 42)
    for u, v in ((-2.5, 2), (2.5, 1.5), (0, -2.5), (1.5, 3.5)):
        facet_rock(S, u, v, 0, 1.8, 1.6, 1.6, r, n=8)
    for a in (0.4, 2.2):
        S.line((math.cos(a) * 3, math.sin(a) * 3, 0.8), (-math.cos(a) * 3, -math.sin(a) * 3, 1.6), "straw", 4)
    S.cyl(0, 0, 0, 20, 0.6, "wood")
    S.sphere(0, 0, 22.5, 2.6, "straw")
    S.ellipsoid(0.6, 0.6, 20.6, 1.6, 1.6, 1.2, "straw", base=-0.2)
    for du, dv in ((2.0, 0.4), (0.4, 2.0)):
        x, y = S.xy(du * 0.85, dv * 0.85, 22.8)
        c.px(x, y, "dark", 0)
        c.px(x, y + 1, "dark", 1)
    x, y = S.xy(1.6, 1.6, 21.0)
    c.px(x, y, "dark", 1)
    return _finish(c)


def ribcage(seed: int = 671) -> Canvas:
    """The bleached ribs and skull of some great beast, half sunk in the sand. Half cover."""
    c, S = _kit(56, 40, 28, 26)
    S.hcyl("u", (0, -1, 2.5), 11, 0.8, "straw")                     # spine
    for k in range(7):
        u = -8 + k * 2.6
        hgt = 9 - abs(k - 2.5) * 1.1
        th = np.linspace(0.05, math.pi - 0.05, 30)
        for sgn in (1, -1):
            P = np.c_[np.full_like(th, u), -1 + sgn * np.sin(th) * 6.5, 2.5 + np.sin(th * 0.5 + 0.2) * hgt]
            P[:, 1] = -1 + sgn * (1 - np.cos(th)) * 3.6
            P[:, 2] = 2.5 + np.sin(th) * hgt
            S.plot(P, None, "straw", shade=4 if sgn < 0 else 3)
            S.plot(P + [0.35, 0, -0.3], None, "straw", shade=2)
    S.ellipsoid(13, -1, 3.0, 3.8, 2.6, 2.6, "straw")
    S.ellipsoid(16, -1, 2.0, 2.4, 1.6, 1.4, "straw", base=-0.2)
    for dv in (-1.4, 1.0):
        x, y = S.xy(13.6, -1 + dv, 3.6)
        c.px(x, y, "dark", 0)
    S.line((11, -3.5, 4.5), (8, -7, 9), "straw", 4)                   # a horn
    return _finish(c)


# ---- plants
def mushroom_cluster(seed: int = 681) -> Canvas:
    """A clump of big toadstools on a mossy hummock: domed caps lit from the top-left with pale spots (cloth2),
    gills in shadow underneath. The `glow` variants make their caps a light source."""
    r = tex.rng(seed)
    c, S = _kit(32, 36, 16, 26)
    S.ellipsoid(0, 0, 0, 7, 6.5, 2.0, "moss", zmin=0)
    shrooms = [(-2.5, 1.5, 9, 3.6), (3, -1.5, 6.5, 2.8), (4, 4, 3.8, 2.0), (-4.5, -3, 3.2, 1.6), (0.5, 5, 2.4, 1.3)]
    for u, v, hgt, cr in shrooms:
        S.frustum(u, v, 0, hgt, 0.35 * cr + 0.35, 0.25 * cr + 0.25, "cloth2", base=-0.2)
        S.ellipsoid(u, v, hgt - 0.3, cr, cr, cr * 0.4, "dark", zmax=hgt - 0.2, base=-0.4)
        S.ellipsoid(u, v, hgt - 0.3, cr, cr, cr * 0.75, "fruit", zmin=hgt - 0.3)
        for du, dv in ((-0.45, 0.2), (0.25, -0.45), (0.15, 0.5), (-0.1, -0.1)):
            x, y = S.xy(u + du * cr, v + dv * cr, hgt - 0.3 + cr * 0.65)
            if c.slot[int(round(y)), int(round(x))] == c.slots.id("fruit"):
                c.px(x, y, "cloth2", 5)
    _ = r
    return _finish(c)


def fern(seed: int = 691) -> Canvas:
    """A forest fern: arching fronds, each a lit spine with leaflets that droop towards the tips."""
    r = tex.rng(seed)
    c = _canvas(32, 24)
    for k in range(9):
        a = math.pi * (0.1 + 0.8 * k / 8) + r.normal(0, 0.06)
        L = r.uniform(9, 14)
        x0, y0 = 16.0, 22.0
        pts = []
        for t in np.linspace(0, 1, 14):
            x = x0 - math.cos(a) * L * t
            y = y0 - math.sin(a) * L * t * 1.7 + (t ** 2) * L * 0.75
            pts.append((x, y))
        back = math.sin(a) > 0.9
        for i, (x, y) in enumerate(pts):
            sh = 2 if back else (4 if math.cos(a) > 0 else 3)
            c.px(x, y, "leaf", sh)
            if 2 < i < 13 and i % 2 == 0:
                ln = 2 if i < 10 else 1
                for d in (-1, 1):
                    for q in range(1, ln + 1):
                        c.px(x + d * q * 0.7 * math.sin(a), y + q * 0.6 + d * q * 0.4 * math.cos(a), "leaf",
                             sh - (1 if d > 0 else 0))
    _underside(c, "leaf", 0.25)
    return _finish(c)


def lily_pads(seed: int = 701) -> Canvas:
    """Lily pads floating flat on the water (a ground-layer prop: draw it under units), one in flower."""
    r = tex.rng(seed)
    c = _canvas(40, 24)
    for x, y, rx in ((12, 10, 5.5), (25, 14, 6.5), (16, 18, 4), (31, 7, 3.6), (6, 17, 3)):
        ry = rx * 0.55
        c.ellipse(x, y, rx, ry, "leaf", 3)
        c.ellipse(x - 0.6, y - 0.4, rx - 1.2, ry - 0.9, "leaf", 4)
        c.line(x, y, x + rx, y + int(r.integers(-1, 2)), "leaf", 1)      # the notch
        c.px(x - rx * 0.4, y - ry * 0.5, "leaf", 5)
        for xx in range(int(x - rx), int(x + rx) + 1):                   # waterline shadow under the rim
            yy = int(y + ry) + 1
            if 0 <= yy < c.h and c.slot[yy - 1, xx]:
                c.px(xx, yy, "dark", 1)
    for dx, dy, sh in ((0, -2, 5), (-1, -1, 4), (1, -1, 4), (-2, 0, 3), (2, 0, 4), (0, -1, 5), (0, 0, 3)):
        c.px(25 + dx, 12 + dy, "fruit", sh)
    c.px(25, 11, "gold", 5)
    return _finish(c)


def cactus(seed: int = 711) -> Canvas:
    """A saguaro: a ribbed column with two arms, lit on its left flank, spines as pale flecks along the ribs."""
    c, S = _kit(32, 60, 16, 50)
    S.cyl(0, 0, 0, 26, 2.6, "leaf", seg=10)
    S.sphere(0, 0, 26, 2.6, "leaf")
    for (du, dv), z0, z1 in (((1, -1), 9, 19), ((-1, 1), 13, 21)):        # arms: out, then up
        for t in np.linspace(0, 1, 10):
            S.sphere(du * (2.4 + 2.6 * t), dv * (2.4 + 2.6 * t), z0 + 1.2 * t * t, 1.6, "leaf")
        S.cyl(du * 5.0, dv * 5.0, z0 + 1.2, z1, 1.6, "leaf", seg=8)
        S.sphere(du * 5.0, dv * 5.0, z1, 1.6, "leaf")
    sid = c.slots.id("leaf")
    ys, xs = np.nonzero(c.slot == sid)
    for y, x in zip(ys, xs):
        if (y + x * 3) % 11 == 0 and c.shade[y, x] >= 4:
            c.px(x, y, "straw", 5)
    for z, du in ((27.5, 0), (21.5, -5.0)):
        x, y = S.xy(0 if du == 0 else 0, -du, z)
        c.px(x, y, "fruit", 4)
        c.px(x + 1, y, "fruit", 3)
    return _finish(c)


def cactus_pear(seed: int = 721) -> Canvas:
    """A prickly pear: flat oval pads growing out of one another, fruit on the top pads."""
    r = tex.rng(seed)
    c = _canvas(32, 32)
    pads = [(16, 25, 5, 6, 3), (10, 17, 4, 5.5, 3), (21, 16, 4.5, 5.5, 2), (14, 9, 3.6, 4.6, 4), (25, 8, 3, 4, 2)]
    for x, y, rx, ry, b in pads:
        blob(c, "leaf", x, y, rx, ry, base=b, k=1.6)
    for x, y, rx, ry, _b in pads:
        for _ in range(6):
            px, py = x + r.uniform(-rx * 0.7, rx * 0.7), y + r.uniform(-ry * 0.7, ry * 0.7)
            c.px(px, py, "straw", 5)
    for x, y in ((13, 4), (16, 5), (25, 4)):
        c.px(x, y, "fruit", 4)
        c.px(x, y - 1, "fruit", 5)
        c.px(x + 1, y, "fruit", 2)
    return _finish(c)


def swamp_tree(seed: int = 731) -> Canvas:
    """A swamp cypress: a buttressed trunk flaring into roots and knees, a ragged high crown and curtains of
    hanging moss (straw slot) dripping from its limbs."""
    r = tex.rng(seed)
    c = _canvas(52, 72)
    c.polygon([(23, 70), (29, 70), (28, 30), (24, 30)], "bark", 3)
    c.polygon([(14, 71), (38, 71), (31, 62), (21, 62)], "bark", 2)
    for x0, x1 in ((15, 22), (30, 37)):
        c.polygon([(x0, 71), (x1, 71), ((x0 + x1) / 2 + 1, 64)], "bark", 3 if x0 < 20 else 2)
    c.rect(27, 32, 28, 66, "bark", 2)
    c.rect(24, 32, 24, 66, "bark", 4)
    for x in (9, 42):                                               # cypress knees poking out of the water
        c.polygon([(x - 2, 71), (x + 2, 71), (x, 66)], "bark", 3)
    _branches(c, r, 26, 34, -math.pi / 2 - 0.9, 13, 2)
    _branches(c, r, 26, 32, -math.pi / 2 + 0.8, 13, 2)
    for x, y, rx, ry, base in ((14, 16, 8, 5, 2.4), (37, 15, 8, 5, 2.3), (26, 8, 9, 5.5, 3.4), (10, 23, 6, 4, 3.0),
                               (41, 23, 6, 4, 2.5), (26, 18, 7, 4, 3.0)):
        leaf_mass(c, "leaf", x, y, rx, ry, r, base=base, lobes=2)
    for _ in range(14):                                             # hanging moss
        x = int(r.integers(6, 46))
        col = np.nonzero(c.slot[:, x])[0]
        if not len(col):
            continue
        y0 = int(col[-1]) if col[-1] < 40 else int(r.integers(18, 30))
        for k in range(int(r.integers(5, 14))):
            xx = x + (1 if (k // 3) % 2 else 0)
            if c.slot[y0 + k, xx] in (0, c.slots.id("bark")):
                c.px(xx, y0 + k, "straw", 3 if k % 3 else 4)
    _underside(c, "leaf", 0.2)
    return _finish(c)


def will_o_wisp_frames(frames: int = 8) -> list[Canvas]:
    """A will-o'-the-wisp: a pale flame bobbing over the marsh with a trailing tail and a faint ring; it gives off
    light (see `light`), and never blocks."""
    out = []
    for f in range(frames):
        c = _canvas(16, 40)
        ph = 2 * math.pi * f / frames
        y = 14 + math.sin(ph) * 2.5
        x = 8 + math.sin(ph * 2) * 1.0
        for k in range(5):                                           # the tail, lagging the bob
            tp = ph - 0.5 * (k + 1)
            c.px(8 + math.sin(tp * 2) * 1.0 - (k + 1) * 0.3, 14 + math.sin(tp) * 2.5 + (k + 1) * 1.3, "glow",
                 3 if k < 2 else 2)
        c.ellipse(x, y, 3.6, 4.0, "glow", 2)
        c.ellipse(x, y, 2.6, 3.0, "glow", 3)
        c.ellipse(x - 0.4, y - 0.4, 1.6, 2.0, "glow", 4)
        c.px(x - 0.5, y - 1, "glow", 5)
        c.px(x, y - 4 - (f % 2), "glow", 4)
        c.lights = [(round(x), round(y))]                            # type: ignore[attr-defined]
        c.anchor = (8, 39)                                           # type: ignore[attr-defined]
        out.append(c)
    return out


# ---- shrines, tombs, lanterns, training ground
def shrine(seed: int = 741) -> Canvas:
    """A wayside shrine: a little gabled stone house on a plinth sheltering an idol and a lit candle, with
    offerings at its foot. A small light source."""
    c, S = _kit(36, 52, 18, 40)
    S.box(-5, -5, 0, 5, 5, 3, "stone", base=-0.3)
    S.box(-4, -4, 3, 4, 4, 13, "stone")
    S.box(-3, 3.9, 4.5, 3, 4.1, 11, "dark", base=-1.0)               # the niche
    S.sphere(0, 3.0, 8.0, 1.4, "gold")
    S.box(-0.8, 2.8, 4.5, 0.8, 3.6, 6.8, "gold", base=-0.2)
    S.box(1.8, 3.5, 4.5, 2.4, 4.0, 6.2, "cloth2")
    x, y = S.xy(2.1, 3.9, 6.6)
    c.px(x, y, "glow", 5)
    c.px(x, y - 1, "glow", 4)
    _lit(c, S, 2.1, 3.9, 7.5)
    for sgn in (1, -1):                                              # the roof: two slabs over the ridge
        S.quad((-5.5, 0, 17.5), (5.5, 0, 17.5), (-5.5, sgn * 5.5, 12.5), "wood", base=0.2 if sgn > 0 else -0.5)
    S.line((-5.5, 0, 17.6), (5.5, 0, 17.6), "wood", 5)
    for u, v in ((-3, 6.2), (3, 6.4)):
        S.ellipsoid(u, v, 0, 1.2, 1.0, 1.1, "fruit", zmin=0)
    return _kdone(c)


def tomb(seed: int = 751) -> Canvas:
    """A raised stone tomb with a carved lid and a weathered effigy plaque; moss in the joints. Full cover."""
    r = tex.rng(seed)
    c, S = _kit(48, 40, 24, 28)
    S.box(-10, -6.5, 0, 10, 6.5, 2, "stone", base=-0.4)
    S.box(-9, -5.5, 2, 9, 5.5, 9, "stone")
    S.box(-9.8, -6.3, 9, 9.8, 6.3, 10.6, "stone", base=0.25)
    S.prism([(-8.5, -4), (8.5, -4), (8.5, 4), (-8.5, 4)], 10.6, 11.6, "stone", base=0.1)
    for u0, u1 in ((-7.5, -1.2), (1.2, 7.5)):                        # carved panels
        S.line((u0, 5.55, 7.5), (u1, 5.55, 7.5), "stone", 1)
        S.line((u0, 5.55, 3.5), (u0, 5.55, 7.5), "stone", 1)
        S.line((u0, 5.55, 3.5), (u1, 5.55, 3.5), "stone", 4)
        S.line((u1, 5.55, 3.5), (u1, 5.55, 7.5), "stone", 4)
    S.line((-2, 0, 11.7), (2, 0, 11.7), "stone", 4)
    S.line((0, -3, 11.7), (0, 3, 11.7), "stone", 4)
    sid = c.slots.id("stone")
    n = tex.fbm(c.w, c.h, 4, r, octaves=2)
    m = (c.slot == sid) & (n > 0.72)
    c.slot[m] = c.slots.id("moss")
    _cracks(c, "stone", r, 4)
    return _finish(c)


def lantern_frames(frames: int = 8) -> list[Canvas]:
    """A standing lantern: a stone post (a garden lantern) with a lit fire box and a little roof. A light source."""
    out = []
    for f in range(frames):
        c, S = _kit(24, 48, 12, 42)
        S.box(-3, -3, 0, 3, 3, 1.5, "stone", base=-0.3)
        S.cyl(0, 0, 1.5, 12, 1.3, "stone", seg=6)
        S.box(-3, -3, 12, 3, 3, 13, "stone")
        S.box(-2.4, -2.4, 13, 2.4, 2.4, 18, "stone", base=-0.6)
        fl = 0.3 * math.sin(2 * math.pi * f / frames) + 0.15 * math.sin(6 * math.pi * f / frames)
        S.box(-1.4, 2.35, 14, 1.4, 2.45, 17, "glow", base=1.0 + fl)
        S.box(2.35, -1.4, 14, 2.45, 1.4, 17, "glow", base=0.6 + fl)
        S.frustum(0, 0, 18, 21, 4.4, 0.4, "stone", seg=4, twist=math.pi / 4)
        S.sphere(0, 0, 21.4, 0.8, "stone")
        _lit(c, S, 0, 0, 15.5)
        out.append(_kdone(c))
    return out


def training_dummy(seed: int = 761) -> Canvas:
    """A straw training dummy on a post with a crossbar, a sack head and a battered shield (cloth1)."""
    c, S = _kit(32, 52, 16, 42)
    S.cyl(0, 0, 0, 20, 0.8, "wood")
    S.box(-6.5, -0.6, 15, 6.5, 0.6, 16.2, "wood")
    S.ellipsoid(0, 0, 13, 3.0, 2.6, 5.5, "straw")
    S.sphere(0, 0, 21, 2.6, "cloth2")
    S.line((-2.4, 0.5, 19.2), (2.4, 0.5, 19.2), "dark", 1)
    for u in (-6.5, 6.5):
        S.ellipsoid(u, 0, 15.6, 1.2, 1.2, 1.6, "straw")
    S.cyl(2.5, 3.2, 9, 15, 3.0, "cloth1", cap=False)
    x, y = S.xy(2.5, 3.6, 12)
    c.px(x, y, "metal", 4)
    for z in (11, 15):                                                # binding cord
        S.line((-3, 0.4, z), (3, 0.4, z), "dark", 1)
    return _finish(c)


def archery_target(seed: int = 771) -> Canvas:
    """A straw butt on an easel with a painted target (cloth1 rings on straw) and two arrows in it."""
    c, S = _kit(36, 48, 18, 38)
    for u in (-4, 4):
        S.line((u, -1, 0), (u * 0.8, 0.5, 16), "wood", 3)
    S.line((0, -5, 0), (0, 0.5, 14), "wood", 2)
    th = np.linspace(0, 2 * math.pi, 120)
    for rr, sl, sh in ((6.5, "straw", 3), (5.0, "cloth1", 3), (3.6, "straw", 4), (2.2, "cloth1", 4),
                       (0.9, "gold", 5)):
        R = np.linspace(0, rr, int(rr * 4) + 2)
        TT, RR = np.meshgrid(th, R)
        P = np.c_[(np.cos(TT) * RR).ravel(), np.full(TT.size, 1.0 + (6.5 - rr) * 0.01),
                  (11 + np.sin(TT) * RR).ravel()]
        S.plot(P, None, sl, shade=sh, bias=(6.5 - rr) * 0.02)
    for (u, z), (du, dz) in (((1.5, 12.5), (-1.5, 1.0)), ((-2.5, 9.5), (-1.0, -1.5))):
        S.line((u, 1.1, z), (u + du * 2, 4.5, z + dz * 2), "wood", 4)
        x, y = S.xy(u + du * 2, 4.5, z + dz * 2)
        c.px(x, y, "cloth2", 5)
    return _finish(c)


def vent_frames(frames: int = 8) -> list[Canvas]:
    """A volcanic fumarole: a cracked cone of crust with glowing magma in its throat. A light source."""
    out = []
    for f in range(frames):
        r = tex.rng(781)
        c, S = _kit(36, 30, 18, 20)
        S.frustum(0, 0, 0, 4.5, 8, 3.2, "stone", cap=False)
        pulse = 0.4 * math.sin(2 * math.pi * f / frames)
        S.ellipsoid(0, 0, 4.0, 3.0, 3.0, 0.6, "glow", base=0.8 + pulse)
        for _ in range(5):
            a = r.uniform(0, 2 * math.pi)
            for t in np.linspace(0, 1, 7):
                rr = 3.4 + t * 4.2
                x, y = S.xy(math.cos(a) * rr, math.sin(a) * rr, 4.5 - t * 4.5 + 0.2)
                if c.slot[int(y), int(x)] == c.slots.id("stone"):
                    c.px(x, y, "glow", 3 if t > 0.5 else 4)
        _lit(c, S, 0, 0, 5)
        out.append(_kdone(c))
    return out


# --------------------------------------------------------------------------------------------------- catalogue
@dataclass
class Prop:
    id: str
    make: Callable[[], Canvas | list[Canvas]]
    variants: dict[str, dict[str, str]]          # variant -> slot -> ramp ref ("family/name" or "prop/name")
    anchor: tuple[int, int] | None = None        # default: bottom centre
    blocks: bool = True
    cover: str = "none"                          # none | half | full
    height: int = 1                              # height steps, for line of sight
    fps: float = 0.0
    tags: list[str] = field(default_factory=list)
    light: tuple[str, float, str] | None = None  # (slot whose shade 4 is the light colour, radius in tiles, flicker)
    layer: str = "over"                          # over: a billboard; ground: lies flat under units (lily pads)

    def frames(self) -> list[Canvas]:
        m = self.make()
        return m if isinstance(m, list) else [m]

    def anchor_of(self, c: Canvas) -> tuple[int, int]:
        """Kit props carry their anchor (the tile centre on the ground); the older drawings stand on bottom centre."""
        return self.anchor or getattr(c, "anchor", None) or (c.w // 2, c.h - 1)


def _v(**slots: str) -> dict[str, str]:
    return slots


_TREE = {
    "green": _v(bark="foliage/bark", leaf="foliage/leaf"),
    "light": _v(bark="foliage/bark", leaf="foliage/leaf_light"),
    "autumn": _v(bark="foliage/bark", leaf="foliage/leaf_autumn"),
    "red": _v(bark="foliage/bark", leaf="foliage/leaf_red"),
    "blossom": _v(bark="foliage/bark_pale", leaf="foliage/blossom"),
}
_WOOD = {"oak": _v(wood="wood/oak", metal="metal/iron", gold="metal/gold", dark="cloth/charcoal")}
_STONE = {"grey": _v(stone="terrain/stone", moss="terrain/moss"), "warm": _v(stone="terrain/stone_warm",
                                                                             moss="terrain/moss"),
          "dark": _v(stone="terrain/stone_dark", moss="terrain/moss")}

_ROCK = {"grey": _v(stone="terrain/stone", moss="terrain/moss"),
         "warm": _v(stone="terrain/stone_warm", moss="terrain/moss"),
         "dark": _v(stone="terrain/stone_dark", moss="terrain/moss"),
         "bare": _v(stone="terrain/stone", moss="terrain/stone"),
         "snowy": _v(stone="terrain/stone_dark", moss="prop/snow"),
         "sandstone": _v(stone="tr/sandstone", moss="tr/dune"),
         "basalt": _v(stone="tr/basalt", moss="tr/ash"),
         "obsidian": _v(stone="tr/obsidian", moss="tr/cinder")}
_ROCK_BUILT = {k: dict(_ROCK[k], dark="cloth/charcoal") for k in ("grey", "warm", "dark", "sandstone", "snowy")}

CATALOGUE: list[Prop] = [
    Prop("tree", tree_round, _TREE, cover="full", height=4, tags=["nature"]),
    Prop("tree_fruit", lambda: tree_round(12, fruit=True),
         {"apple": _v(bark="foliage/bark", leaf="foliage/leaf", fruit="prop/apple")}, cover="full", height=4),
    Prop("pine", tree_pine, {"green": _v(bark="foliage/bark", leaf="foliage/pine", glow="prop/snow")},
         cover="full", height=4, tags=["nature"]),
    Prop("pine_snowy", lambda: tree_pine(2, snowy=True),
         {"snow": _v(bark="foliage/bark", leaf="foliage/pine", glow="prop/snow")}, cover="full", height=4),
    Prop("dead_tree", tree_dead, {"grey": _v(bark="foliage/bark_pale", dark="cloth/charcoal"),
                                  "dark": _v(bark="foliage/dead", dark="cloth/charcoal")}, cover="half", height=3),
    Prop("bush", bush, {k: v for k, v in _TREE.items() if k != "blossom"}, blocks=False, cover="half"),
    Prop("bush_berry", lambda: bush(14, berries=True), {"green": _v(leaf="foliage/leaf", fruit="prop/berry")},
         blocks=False, cover="half"),
    Prop("rock_small", lambda: rock("small", 5), _STONE, blocks=False),
    Prop("rock_medium", lambda: rock("medium", 15), _STONE, cover="half"),
    Prop("rock_large", lambda: rock("large", 25), _STONE, cover="full", height=2),
    Prop("rock_mossy", lambda: rock("large", 35, mossy=True), _STONE, cover="full", height=2),
    Prop("stump", stump, {"oak": _v(bark="foliage/bark", wood="wood/pale")}, cover="half"),
    Prop("log", log, {"oak": _v(bark="foliage/bark", wood="wood/pale")}, cover="half"),
    Prop("tall_grass", tall_grass, {"green": _v(leaf="terrain/grass"), "dry": _v(leaf="terrain/grass_dry")},
         blocks=False),
    Prop("reeds", reeds, {"swamp": _v(leaf="terrain/moss", bark="foliage/bark")}, blocks=False),
    Prop("mushrooms", mushrooms, {"red": _v(fruit="prop/berry", cloth2="cloth/ivory"),
                                  "violet": _v(fruit="magic/shadow", cloth2="cloth/ivory")}, blocks=False),
    Prop("crystals", crystals, {"blue": _v(glow="prop/crystal_blue", stone="terrain/stone_dark"),
                                "violet": _v(glow="prop/crystal_violet", stone="terrain/stone_dark"),
                                "green": _v(glow="prop/crystal_green", stone="terrain/stone_dark")},
         cover="half", height=2),
    Prop("crate", crate, _WOOD, cover="half"),
    Prop("barrel", barrel, _WOOD, cover="half"),
    Prop("sack", sack, {"burlap": _v(straw="prop/canvas", cloth2="leather/brown")}, blocks=False),
    Prop("chest", chest, _WOOD, cover="half"),
    Prop("chest_open", lambda: chest(23, open_=True), _WOOD, cover="half"),
    Prop("fence", fence, {"oak": _v(wood="wood/oak")}, cover="half"),
    Prop("campfire", campfire_frames, {"fire": _v(stone="terrain/stone", bark="foliage/bark", glow="prop/flame")},
         fps=10, tags=["light"]),
    Prop("torch", torch_frames, {"fire": _v(wood="wood/dark", metal="metal/iron", glow="prop/flame")},
         blocks=False, fps=8, tags=["light"]),
    Prop("lamp_post", lamp_post, {"iron": _v(metal="metal/dark", glow="prop/flame")}, height=3, tags=["light"]),
    Prop("banner", banner_frames, {
        "crimson": _v(wood="wood/dark", cloth1="cloth/crimson", cloth2="metal/gold", gold="metal/gold"),
        "navy": _v(wood="wood/dark", cloth1="cloth/navy", cloth2="cloth/ivory", gold="metal/gold"),
        "forest": _v(wood="wood/dark", cloth1="cloth/forest", cloth2="metal/gold", gold="metal/gold"),
        "goblin": _v(wood="wood/dark", cloth1="cloth/rust", cloth2="cloth/charcoal", gold="metal/iron")},
         blocks=False, height=3, fps=6),
    Prop("signpost", signpost, {"oak": _v(wood="wood/oak", dark="cloth/charcoal")}, blocks=False),
    Prop("well", well, {"stone": _v(stone="terrain/stone", wood="wood/oak", dark="cloth/navy", glow="magic/frost")},
         cover="full", height=2),
    Prop("tent", tent, {"canvas": _v(cloth1="prop/canvas", dark="cloth/charcoal", wood="wood/oak"),
                        "red": _v(cloth1="cloth/crimson", dark="cloth/charcoal", wood="wood/oak")},
         cover="full", height=3),
    Prop("haystack", haystack, {"hay": _v(straw="prop/hay")}, cover="full", height=2),
    Prop("gravestone", gravestone, _STONE, cover="half"),
    Prop("grave_cross", lambda: gravestone(33, cross=True), _STONE, cover="half"),
    Prop("pillar", pillar, _STONE, cover="full", height=4),
    Prop("pillar_broken", lambda: pillar(34, broken=True), _STONE, cover="full", height=3),
    Prop("altar", altar, {"holy": _v(stone="terrain/stone", cloth1="cloth/ivory", gold="metal/gold",
                                     glow="magic/holy"),
                          "dark": _v(stone="terrain/stone_dark", cloth1="cloth/wine", gold="metal/dark",
                                     glow="magic/blood")}, cover="half"),
    Prop("rubble", rubble, _STONE, blocks=False),
    # ---- the kit: rocks
    Prop("boulder", boulder, _ROCK, cover="full", height=2, tags=["nature", "rock"]),
    Prop("crag", crag, _ROCK, cover="full", height=4, tags=["nature", "rock"]),
    Prop("rock_cluster", rock_cluster, _ROCK, cover="half", tags=["nature", "rock"]),
    Prop("ice_spikes", ice_spikes, {"frost": _v(stone="terrain/stone_dark", moss="prop/snow", glow="prop/ice")},
         cover="half", height=2, tags=["snow"]),
    Prop("crystal_large", crystal_large_frames, {
        "blue": _v(glow="prop/crystal_blue", stone="terrain/stone_dark"),
        "violet": _v(glow="prop/crystal_violet", stone="terrain/stone_dark"),
        "green": _v(glow="prop/crystal_green", stone="terrain/stone_dark"),
        "fire": _v(glow="prop/magma", stone="tr/basalt")},
         cover="full", height=3, fps=8, light=("glow", 3.5, "pulse"), tags=["light", "cave"]),
    # ---- fences and walls (one segment along U; mirror the sprite for V)
    Prop("fence_stone", fence_stone, _ROCK, cover="half", tags=["fence"]),
    Prop("fence_picket", fence_picket, {"white": _v(wood="wood/oak", cloth1="prop/whitewash"),
                                        "oak": _v(wood="wood/dark", cloth1="wood/oak"),
                                        "red": _v(wood="wood/dark", cloth1="cloth/rust")}, cover="half",
         tags=["fence"]),
    Prop("fence_iron", fence_iron, {"iron": _v(metal="metal/dark", gold="metal/gold", stone="terrain/stone"),
                                    "rust": _v(metal="cloth/rust", gold="metal/bronze", stone="terrain/stone_warm")},
         cover="half", height=2, tags=["fence", "courtyard"]),
    Prop("palisade", palisade, {"log": _v(bark="foliage/bark", wood="wood/pale", straw="prop/hay")},
         cover="full", height=3, tags=["fence", "siege"]),
    Prop("ruin_wall", ruin_wall, {**_ROCK_BUILT}, cover="full", height=3, tags=["ruin"]),
    Prop("ruin_arch", ruin_arch, {**_ROCK_BUILT}, blocks=False, cover="none", height=5, tags=["ruin"]),
    # ---- dressing
    Prop("cart", cart, {"oak": _v(wood="wood/oak", metal="metal/iron", straw="prop/canvas"),
                        "hay": _v(wood="wood/oak", metal="metal/iron", straw="prop/hay")}, cover="half",
         height=2, tags=["town"]),
    Prop("ladder", ladder, {"oak": _v(wood="wood/oak")}, blocks=False, height=2,
         tags=["climb"]),
    Prop("cage", cage, {"iron": _v(metal="metal/iron"), "rust": _v(metal="cloth/rust")}, cover="half",
         height=3, tags=["dungeon"]),
    Prop("barricade", barricade, {"oak": _v(bark="foliage/bark", wood="wood/pale", straw="prop/hay")},
         cover="half", tags=["siege"]),
    Prop("mantlet", mantlet, {"hide": _v(wood="wood/oak", cloth1="prop/hide", metal="metal/iron"),
                              "crimson": _v(wood="wood/dark", cloth1="cloth/crimson", metal="metal/iron")},
         cover="full", height=3, tags=["siege"]),
    Prop("skull_pike", skull_pike, {"bone": _v(wood="wood/dark", straw="prop/bone", stone="terrain/stone",
                                               dark="cloth/charcoal")}, blocks=False, cover="none", height=2,
         tags=["bones"]),
    Prop("ribcage", ribcage, {"bleached": _v(straw="prop/bone", dark="cloth/charcoal")}, cover="half",
         tags=["bones", "desert"]),
    # ---- plants
    Prop("mushroom_cluster", mushroom_cluster, {
        "red": _v(fruit="prop/toadstool", cloth2="cloth/ivory", moss="tr/forest_moss", dark="cloth/charcoal"),
        "brown": _v(fruit="prop/bolete", cloth2="cloth/ivory", moss="tr/forest_moss", dark="cloth/charcoal"),
        "glow": _v(fruit="prop/glowcap", cloth2="cloth/ivory", moss="tr/forest_moss", dark="cloth/charcoal")},
         blocks=False, cover="none", light=("fruit", 1.5, "pulse"), tags=["forest", "light:glow"]),
    Prop("fern", fern, {"green": _v(leaf="foliage/leaf"), "autumn": _v(leaf="foliage/leaf_autumn")},
         blocks=False, tags=["forest"]),
    Prop("lily_pads", lily_pads, {"pink": _v(leaf="prop/lily", fruit="prop/lotus", gold="metal/gold",
                                             dark="terrain/water_swamp"),
                                  "white": _v(leaf="prop/lily", fruit="cloth/ivory", gold="metal/gold",
                                              dark="terrain/water_swamp")}, blocks=False, height=0,
         layer="ground", tags=["swamp", "water"]),
    Prop("cactus", cactus, {"green": _v(leaf="prop/cactus", straw="prop/bone", fruit="prop/berry")},
         cover="half", height=3, tags=["desert"]),
    Prop("cactus_pear", cactus_pear, {"green": _v(leaf="prop/cactus", straw="prop/bone", fruit="prop/berry")},
         blocks=False, cover="half", tags=["desert"]),
    Prop("swamp_tree", swamp_tree, {"cypress": _v(bark="foliage/dead", leaf="prop/cypress",
                                                  straw="prop/spanish_moss")}, cover="full", height=4,
         tags=["swamp", "nature"]),
    Prop("will_o_wisp", will_o_wisp_frames, {"blue": _v(glow="prop/wisp"), "green": _v(glow="prop/wisp_green")},
         blocks=False, height=0, fps=6, light=("glow", 2.0, "pulse"), tags=["swamp", "light"]),
    # ---- shrines, tombs, lights, the training ground
    Prop("shrine", shrine, {"stone": _v(stone="terrain/stone", wood="wood/dark", gold="metal/gold",
                                        cloth2="cloth/ivory", dark="cloth/charcoal", glow="prop/flame",
                                        fruit="prop/apple")}, cover="half", height=2,
         light=("glow", 1.5, "candle"), tags=["light"]),
    Prop("tomb", tomb, {"grey": _v(stone="terrain/stone", moss="terrain/moss"),
                        "dark": _v(stone="terrain/stone_dark", moss="terrain/moss"),
                        "sandstone": _v(stone="tr/sandstone", moss="tr/dune")}, cover="full", height=2,
         tags=["crypt"]),
    Prop("lantern", lantern_frames, {"stone": _v(stone="terrain/stone", glow="prop/lantern"),
                                     "moss": _v(stone="terrain/stone_dark", glow="prop/lantern")},
         cover="half", height=2, fps=8, light=("glow", 3.0, "candle"), tags=["light"]),
    Prop("training_dummy", training_dummy, {"straw": _v(wood="wood/oak", straw="prop/hay", cloth2="prop/canvas",
                                                         cloth1="cloth/crimson", metal="metal/iron",
                                                         dark="cloth/charcoal")}, cover="half", height=2,
         tags=["courtyard"]),
    Prop("archery_target", archery_target, {"straw": _v(wood="wood/oak", straw="prop/hay", cloth1="cloth/crimson",
                                                        gold="metal/gold", cloth2="cloth/ivory")},
         cover="half", height=2, tags=["courtyard"]),
    Prop("vent", vent_frames, {"magma": _v(stone="tr/basalt", glow="prop/magma")}, blocks=False, fps=8,
         light=("glow", 2.0, "fire"), tags=["volcanic", "light"]),
]

# Older drawings that are light sources: (slot, radius in tiles, flicker). Kit props declare theirs inline.
for _p in CATALOGUE:
    _p.light = _p.light or {"campfire": ("glow", 4.0, "fire"), "torch": ("glow", 3.0, "torch"),
                            "lamp_post": ("glow", 3.5, "candle"), "crystals": ("glow", 2.0, "pulse"),
                            "altar": ("glow", 1.5, "candle")}.get(_p.id)


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {s: _ref(ref) for s, ref in variant.items()}
    # unused slots still need a ramp to render: fall back to neutral greys
    for name in SLOTS.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


FLICKER = {
    # One loop of 8 multipliers on a light's power, played at the prop's own fps (or 8 fps for still props). The
    # same curves as the dungeon set, so a torch flickers alike indoors and out.
    "fire": [1.0, 0.93, 1.08, 0.97, 0.88, 1.05, 0.95, 1.1],
    "torch": [1.0, 0.9, 1.06, 0.94, 0.84, 1.02, 0.92, 1.08],
    "candle": [1.0, 0.97, 1.02, 0.95, 1.0, 1.03, 0.97, 0.99],
    "pulse": [1.0, 0.947, 0.82, 0.693, 0.64, 0.693, 0.82, 0.947],
    "steady": [1.0] * 8,
}


def light_points(p: Prop, c: Canvas) -> list[list[int]]:
    """Where a prop's light sits, in image pixels: kit props record it while drawing; the older drawings get the
    centre of their brightest emissive pixels (the flame's heart, the lantern's pane)."""
    if getattr(c, "lights", None):
        return [list(xy) for xy in c.lights]
    slot = p.light[0] if p.light else "glow"
    m = (c.slot == c.slots.id(slot)) & (c.shade >= 4) & ((c.flags & 1) == 0)
    if not m.any():
        m = c.slot == c.slots.id(slot)
    ys, xs = np.nonzero(m)
    return [[int(round(xs.mean())), int(round(ys.mean()))]] if len(xs) else []


def emissive_mask(p: Prop, c: Canvas) -> np.ndarray:
    """The pixels that make their own light: the light slot, outline excluded. The renderer leaves these unlit
    (and feeds them to bloom) while everything else takes the scene's lighting."""
    return (c.slot == c.slots.id(p.light[0])) & ((c.flags & 1) == 0)


def build() -> None:
    out = PACK / "props"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles = {}, []
    for p in CATALOGUE:
        frames = p.frames()
        w, h = frames[0].w, frames[0].h
        anchor = list(p.anchor_of(frames[0]))
        files = {}
        for vname, v in p.variants.items():
            pal = ramps(v)
            imgs = [f.render(pal) for f in frames]
            sheet = Image.new("RGBA", (w * len(imgs), h))
            for k, im in enumerate(imgs):
                sheet.paste(im, (k * w, 0))
            rel = f"{p.id}_{vname}.png"
            save(sheet, out / rel)
            files[vname] = rel
            tiles.append(Tile(imgs[0], p.id, vname + (f" · {len(imgs)}f" if len(imgs) > 1 else "")))
        entries[p.id] = {"variants": files, "size": [w, h], "anchor": anchor, "frames": len(frames), "fps": p.fps,
                         "blocks": p.blocks, "cover": p.cover, "height": p.height, "tags": p.tags}
        if p.layer != "over":
            entries[p.id]["layer"] = p.layer
        if p.light:
            slot, radius, flicker = p.light
            mask = Image.new("L", (w * len(frames), h))
            for k, f in enumerate(frames):
                mask.paste(Image.fromarray((emissive_mask(p, f) * 255).astype(np.uint8), "L"), (k * w, 0))
            save(mask, out / "emissive" / f"{p.id}.png")
            entries[p.id]["light"] = {
                "at": [light_points(p, f) for f in frames] if len(frames) > 1 else light_points(p, frames[0]),
                "radius": radius, "flicker": flicker,
                "colour": {vn: ramps(v)[slot][4] for vn, v in p.variants.items()},
                "emissive": f"emissive/{p.id}.png"}
    write_json({"props": entries,
                "notes": {"anchor": "the ground contact pixel; stand it on the tile centre",
                          "frames": "animated props are horizontal strips; loop at fps",
                          "cover": "none | half | full, for ranged attacks",
                          "height": "in height steps (16 px), for line of sight",
                          "layer": "absent = over (a billboard, sorted with units); ground = lies flat under units",
                          "light": "a point light: see lighting"},
                "lighting": {
                    "light": "at: image px of the light's source in frame 0 (or one list per frame for animated "
                             "props, since flames sway); radius: in tiles; colour: per variant, hex; flicker: a "
                             "curve name in lighting.flicker; emissive: an L8 mask strip (same frames as the "
                             "sprite) of the pixels that glow by themselves",
                    "place": "world position = the prop's anchor on its tile + (at - anchor) in sprite px, one "
                             "sprite px = 1/16 tile across and 1/8 height step up the screen (sprites draw at 2x)",
                    "godot": "an OmniLight3D per light at that point: light_color = colour, omni_range = radius "
                             "tiles, light_energy = base energy x flicker[(frame + phase) % 8] where phase varies "
                             "per instance; on the sprite material, emission = albedo x emissive mask, so the "
                             "glowing pixels skip lighting and feed bloom",
                    "flicker": FLICKER}}, out / "manifest.json")
    save(contact_sheet("Props", tiles, columns=12, zoom=3, checker=True,
                       subtitle="Billboard set dressing in palette variants, anchored at the ground. The manifest "
                                "says what blocks movement, what gives cover, how tall each prop is and, for light "
                                "sources, where the light sits, its colour, radius and flicker."),
         PREVIEWS / "13-props.png")
