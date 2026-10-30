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
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "prop":
        return PROP_RAMPS[name]
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
def tree_round(seed: int = 1, *, fruit: bool = False) -> Canvas:
    """A broadleaf tree: a flared trunk and a canopy of overlapping lit clusters."""
    r = tex.rng(seed)
    c = _canvas(48, 64)
    # trunk with root flare and a fork into the canopy
    c.polygon([(21, 62), (27, 62), (26, 40), (22, 40)], "bark", 3)
    c.polygon([(18, 63), (30, 63), (27, 59), (21, 59)], "bark", 2)
    c.line(24, 44, 17, 34, "bark", 3)
    c.line(25, 44, 31, 33, "bark", 3)
    for y in range(41, 62, 3):                                     # bark grooves
        c.px(23 + (y % 2), y, "bark", 1)
    c.rect(25, 41, 25, 61, "bark", 2)
    # canopy: back clusters first (darker), then front ones
    clusters = [(20, 8, 7, 5), (30, 9, 7, 5), (24, 14, 12, 9), (14, 21, 10, 8), (34, 20, 10, 8), (10, 29, 8, 6),
                (38, 29, 7, 6), (24, 28, 12, 7)]
    for i, (x, y, rx, ry) in enumerate(clusters):
        clump(c, "leaf", x + r.uniform(-1, 1), y + r.uniform(-1, 1), rx, ry, base=2.5 if i < 2 else 3.0)
    leaf_texture(c, "leaf", seed + 5)
    _underside(c, "leaf", 0.3)
    if fruit:
        for _ in range(9):
            x, y = int(r.integers(10, 38)), int(r.integers(10, 34))
            if c.slot[y, x] == c.slots.id("leaf"):
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
    """Boulders: a few faceted, lit lumps; mossy variants carry moss on their tops."""
    r = tex.rng(seed)
    w, h, lumps = {"small": (16, 12, 2), "medium": (24, 18, 3), "large": (36, 30, 4)}[size]
    c = _canvas(w, h)
    for k in range(lumps):
        rx = w * r.uniform(0.25, 0.36)
        ry = h * r.uniform(0.3, 0.4)
        cx = w / 2 + r.uniform(-w * 0.18, w * 0.18)
        cy = h - ry - 1 - r.uniform(0, h * 0.2) if k else h - ry - 1
        blob(c, "stone", cx, cy, rx, ry, k=2.3)
    # facet cracks
    for _ in range(lumps):
        x, y = int(r.integers(2, w - 3)), int(r.integers(2, h - 3))
        for _ in range(int(r.integers(2, 5))):
            if c.slot[y, x]:
                c.px(x, y, "stone", 1)
            x += int(r.choice([-1, 1]))
            y += 1
    if mossy:
        sid = c.slots.id("stone")
        for x in range(w):
            col = np.nonzero(c.slot[:, x] == sid)[0]
            if len(col):
                top = col[0]
                for y in range(top, min(h, top + int(r.integers(1, 4)))):
                    c.px(x, y, "moss", 4 if y == top else 3)
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

    def frames(self) -> list[Canvas]:
        m = self.make()
        return m if isinstance(m, list) else [m]


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
]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {s: _ref(ref) for s, ref in variant.items()}
    # unused slots still need a ramp to render: fall back to neutral greys
    for name in SLOTS.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


def build() -> None:
    out = PACK / "props"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles = {}, []
    for p in CATALOGUE:
        frames = p.frames()
        w, h = frames[0].w, frames[0].h
        anchor = list(p.anchor or (w // 2, h - 1))
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
    write_json({"props": entries,
                "notes": {"anchor": "the ground contact pixel; stand it on the tile centre",
                          "frames": "animated props are horizontal strips; loop at fps",
                          "cover": "none | half | full, for ranged attacks",
                          "height": "in height steps (16 px), for line of sight"}}, out / "manifest.json")
    save(contact_sheet("Props", tiles, columns=10, zoom=3, checker=True,
                       subtitle="Billboard set dressing in palette variants, anchored at the ground. The manifest "
                                "says what blocks movement, what gives cover and how tall each prop is."),
         PREVIEWS / "13-props.png")
