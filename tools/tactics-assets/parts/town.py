"""The town kit: architecture for the exploration maps between battles (villages, market squares, keeps).

Two complementary halves:

  * **Block textures**, like `terrain.py`: seamless, world-aligned wall faces (timber framing, whitewash, fieldstone,
    dressed stone, logs, planks), roof surfaces (slate, clay tiles, thatch, shakes), interior floors, floor decals
    (rugs) and wall decals (doors, windows, signs, ivy, cracks). The 3D map builds houses out of blocks with these.
  * **Building props**, pre-rendered in the map's own isometric projection (2:1; a 32 px tile is a 64x32 diamond, a
    16 px height step is 16 px of screen) at the terrain's texel density: cottages, houses, a tavern, smithy, store,
    stalls, chapel, towers, a windmill, modular town walls, stairs, a bridge and market clutter.

The props are not hand-drawn sprites: each is a small set of textured world-space polygons (walls, gable roofs,
chimneys, sails) rasterised here with a z-buffer, lit by the same top-left light as the terrain previews (lit +Z tops,
+V faces at 0.80, +U faces at 0.62) and ray-traced for cast shadows, so eaves shade the walls under them and a tower
shades the roof beside it. They therefore use exactly the block textures above, and a house built from blocks and a
pre-rendered house look like the same village.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from functools import lru_cache
from typing import Callable

import numpy as np
from PIL import Image, ImageFilter

from arcpix import iso, tex
from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, OUTLINE, ramp
from arcpix.preview import Tile, contact_sheet, stack
from . import terrain as TR

T = 128                  # texture span: 4 tiles
TILE = 32
STEP = 16
STOREY = 64              # wall textures repeat once per storey (4 height steps)
DECAL_W = 32

SLOTS = SlotTable()
for _s in ("main", "alt", "grout", "accent", "metal", "glow", "trim", "dark", "leaf", "cloth"):
    SLOTS.id(_s)

TOWN_RAMPS: dict[str, list[str]] = {
    "plaster": ramp("#e4d9c0", shift=0.05, spread=0.62),
    "plaster_ochre": ramp("#d8b77e", shift=0.05, spread=0.7),
    "plaster_rose": ramp("#d6a898", shift=0.05, spread=0.7),
    "lime": ramp("#b6ab95", shift=0.04, spread=0.8),
    "timber": ramp("#5a3a24", shift=0.06, spread=1.05),
    "slate": ramp("#56607a", shift=0.04, spread=1.05),
    "slate_green": ramp("#4e6a66", shift=0.05, spread=1.05),
    "clay": ramp("#b65a3a", shift=0.06),
    "clay_ochre": ramp("#c98a4c", shift=0.06),
    "thatch": ramp("#c9a45a", shift=0.06, spread=0.9),
    "thatch_old": ramp("#9e8c60", shift=0.05, spread=0.9),
    "shake": ramp("#8c6a4a", shift=0.05),
    "shake_grey": ramp("#8a8784", shift=0.04),
    "log": ramp("#8c5c38", shift=0.06),
    "chink": ramp("#a8987a", shift=0.04, spread=0.8),
    "glass": ramp("#41587c", shift=0.04, spread=1.1),
    "glow": ramp("#ffb648", shift=0.06, spread=0.7),
    "forge": ramp("#ff7a2a", shift=0.08, spread=0.8),
    "interior": ramp("#3a2a26", shift=0.05, spread=1.2),
    "canvas": ramp("#e3d6b8", shift=0.05, spread=0.7),
    "paper": ramp("#e9e0c8", shift=0.04, spread=0.6),
    "lead": ramp("#6a6c7a", shift=0.04, spread=1.1),
    "stained_red": ramp("#c0393f", shift=0.06),
    "stained_blue": ramp("#3f6fd0", shift=0.05),
    "bell": ramp("#c9973a", shift=0.06, spread=1.2),
}

TRIMS = {"red": "cloth/crimson", "blue": "cloth/steel_blue", "green": "cloth/forest", "oak": "wood/oak"}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "town":
        return TOWN_RAMPS[name]
    if fam == "prop":
        from . import props as PR
        return PR.PROP_RAMPS[name]
    return FAMILIES[fam][name]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {s: _ref(r) for s, r in variant.items()}
    for name in SLOTS.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


def _canvas(w: int, h: int, shade: np.ndarray | None = None, slot: np.ndarray | str = "main") -> Canvas:
    c = Canvas(w, h, SLOTS)
    if shade is not None:
        c.shade[:] = np.clip(shade, 0, 5).astype(np.uint8)
        c.slot[:] = SLOTS.id(slot) if isinstance(slot, str) else slot
    return c


def _adopt(c: Canvas) -> Canvas:
    """Re-homes a terrain canvas (its own SlotTable) onto ours by slot name, so terrain generators can be reused."""
    out = Canvas(c.w, c.h, SLOTS)
    for sid in np.unique(c.slot):
        if sid:
            out.slot[c.slot == sid] = SLOTS.id(c.slots.name(int(sid)))
    out.shade[:] = c.shade
    return out


def _id(name: str) -> int:
    return SLOTS.id(name)


# ------------------------------------------------------------------------------------------------- wall faces
def _edge_light(mask: np.ndarray, shade: np.ndarray) -> np.ndarray:
    """Bevels a raised feature (beams, logs) lit from the top-left: +1 where the pixel above or left is not part of
    it, -1 where the pixel below or right is not. Wraps, so the texture stays seamless."""
    up = np.roll(mask, 1, 0)
    left = np.roll(mask, 1, 1)
    down = np.roll(mask, -1, 0)
    right = np.roll(mask, -1, 1)
    s = shade.copy()
    s = np.where(mask & (~up | ~left), s + 1, s)
    s = np.where(mask & (~down | ~right) & (up & left), s - 1, s)
    return s


def _plaster(r: np.random.Generator, w: int, h: int, *, mottle: float = 0.72, streaks: bool = True) -> np.ndarray:
    """Lime plaster: soft mottling in clusters, faint rain streaks running down, the odd chip."""
    n = tex.fbm(w, h, 8, r, octaves=3)
    shade = tex.quantize(n, [(mottle, 4)], 3, dither=0.03)
    stain = tex.fbm(w, h, 16, r, octaves=2)
    shade = np.where(stain < 0.12, 2, shade)
    if streaks:
        st = tex.fbm(h, w, 16, r, octaves=2, aspect=5.0).T          # stretched vertically
        shade = np.where((st < 0.16) & (shade == 3), 2, shade)
    shade = tex.despeckle(shade, 2)
    chips = r.random((h, w)) < 0.003
    shade = np.where(chips, 1, shade)
    shade = np.where(np.roll(chips, -1, 0) | np.roll(chips, -1, 1), 4, shade)
    return shade


def _thick_line_mask(mask: np.ndarray, x0: float, y0: float, x1: float, y1: float, width: int) -> None:
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    h, w = mask.shape
    for k in range(n + 1):
        t = k / max(1, n)
        x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
        for d in range(width):
            mask[int(round(y)) % h, int(round(x + d)) % w] = True


def timber_frame(seed: int = 301) -> Canvas:
    """Half-timbering: dark oak posts, rails and braces over a lime-washed infill, one storey per period. Posts sit on
    tile boundaries so a window or door decal placed on a tile lands neatly between two of them."""
    r = tex.rng(seed)
    w, h = T, STOREY
    shade = _plaster(r, w, h)
    beam = np.zeros((h, w), bool)
    beam[0:4] = True                                          # top plate
    beam[h - 5:h] = True                                      # sill beam
    beam[30:33] = True                                        # mid rail
    for k in range(w // TILE):
        beam[:, k * TILE:k * TILE + 4] = True                 # posts
    upper = ["stud", "down", "stud", "up"]
    lower = ["up", "stud", "down", "stud"]
    for k in range(w // TILE):
        x0 = k * TILE + 4
        for kind, (ya, yb) in ((upper[k], (4, 30)), (lower[k], (33, h - 5))):
            if kind == "stud":
                beam[ya:yb, x0 + 12:x0 + 15] = True
            elif kind == "up":
                _thick_line_mask(beam, x0, yb - 1, x0 + 25, ya, 3)
            else:
                _thick_line_mask(beam, x0, ya, x0 + 25, yb - 1, 3)
    gh = tex.fbm(w, h, 16, r, octaves=2, aspect=6.0)
    grain = np.where(gh < 0.3, 2, np.where(gh > 0.75, 4, 3))
    b_shade = _edge_light(beam, grain)
    peg = beam & (r.random((h, w)) < 0.006)
    b_shade = np.where(peg, 1, b_shade)
    # the infill sits back from the beams: a 1 px shadow under each beam and right of each post
    shadow = ~beam & (np.roll(beam, 1, 0) | np.roll(beam, 1, 1))
    shade = np.where(shadow, np.minimum(shade, 2), shade)
    slot = np.where(beam, _id("alt"), _id("main"))
    return _canvas(w, h, np.where(beam, b_shade, shade), slot)


def whitewash(seed: int = 311) -> Canvas:
    """Plain lime-washed render with mottling, rain streaks and hairline cracks."""
    r = tex.rng(seed)
    w, h = T, STOREY
    shade = _plaster(r, w, h, mottle=0.66)
    for _ in range(5):
        x, y = int(r.integers(w)), int(r.integers(h))
        for _ in range(int(r.integers(4, 10))):
            shade[y % h, x % w] = 2
            x += int(r.choice([-1, 0, 1]))
            y += 1
    return _canvas(w, h, shade)


def fieldstone(seed: int = 321) -> Canvas:
    """Rubble walling: rounded, irregular field stones bedded in pale lime mortar (the terrain's Voronoi stones,
    flattened into courses)."""
    c = _adopt(TR._stones(seed, 16, 0.32, 1.8, w=T, h=STOREY, cell_h=8, stagger=True, sx=1.0, sy=1.45,
                          bevel=0.8, tones=(-1, 0, 0, 1, 1), crack_p=0.05))
    return c


def _courses(r: np.random.Generator, w: int, h: int, course_h: int, lengths: tuple[int, int], *,
             margin: bool = True) -> tuple[np.ndarray, np.ndarray]:
    """Rectangular coursed blocks (ashlar, laid flags): per course, random block lengths that close around the wrap.
    Returns (block id per pixel, joint mask)."""
    ids = np.zeros((h, w), int)
    joint = np.zeros((h, w), bool)
    nid = 0
    for row in range(h // course_h):
        y0 = row * course_h
        start = int(r.integers(0, w))
        x = 0
        cuts = []
        while x < w - lengths[0]:
            cuts.append(x)
            x += int(r.integers(lengths[0], lengths[1] + 1))
        for k, cx in enumerate(cuts):
            ex = cuts[k + 1] if k + 1 < len(cuts) else w
            for xx in range(cx, ex):
                X = (xx + start) % w
                ids[y0:y0 + course_h, X] = nid
            joint[y0:y0 + course_h, (ex - 1 + start) % w] = True
            nid += 1
        joint[y0 + course_h - 1, :] = True
    return ids, joint


def dressed_stone(seed: int = 331) -> Canvas:
    """Ashlar for chapels, towers and walls: squared blocks in 16 px courses, drafted margins lit from the top-left,
    tooling speckle, thin dark joints."""
    r = tex.rng(seed)
    w, h = T, STOREY
    ids, joint = _courses(r, w, h, 16, (18, 40))
    tone = r.choice([-1, 0, 0, 1], ids.max() + 1)
    shade = 3 + tone[ids]
    block = ~joint
    up_j = np.roll(joint, 1, 0) | np.roll(joint, 1, 1)
    dn_j = np.roll(joint, -1, 0) | np.roll(joint, -1, 1)
    shade = np.where(block & up_j, shade + 1, shade)
    shade = np.where(block & dn_j & ~up_j, shade - 1, shade)
    n = tex.fbm(w, h, 16, r, octaves=2)
    shade = np.where(block & (n < 0.14), shade - 1, shade)
    shade = np.where(block & (r.random((h, w)) < 0.025), shade + 1, shade)
    shade = np.where(joint, 1, shade)
    slot = np.where(joint, _id("grout"), _id("main"))
    return _canvas(w, h, shade, slot)


def log_wall(seed: int = 341) -> Canvas:
    """Log cabin: round logs 8 px thick, lit along the top, chinked with pale daub; bark streaks and the odd knot."""
    r = tex.rng(seed)
    w, h = T, STOREY
    shade = np.zeros((h, w), int)
    slot = np.full((h, w), _id("main"))
    streak = tex.fbm(w, h, 16, r, octaves=2, aspect=8.0)
    profile = [4, 4, 3, 3, 3, 2, 1]
    for row in range(h // 8):
        y0 = row * 8
        tone = int(r.choice([-1, 0, 0, 1]))
        for dy, p in enumerate(profile):
            shade[y0 + dy] = p + tone
        slot[y0 + 7] = _id("grout")
        shade[y0 + 7] = np.where(r.random(w) < 0.5, 2, 3)
        for _ in range(2):
            kx = int(r.integers(w))
            shade[y0 + 3, kx] = 1
            shade[y0 + 2, kx] = 2
            shade[y0 + 3, (kx + 1) % w] = 2
    main = slot == _id("main")
    shade = np.where(main & (streak < 0.28), shade - 1, shade)
    shade = np.where(main & (streak > 0.8), shade + 1, shade)
    return _canvas(w, h, np.clip(shade, 1, 5), slot)


def plank_shed(seed: int = 351) -> Canvas:
    """Vertical boards with nailed joints (sheds, barns, stalls): the terrain plank generator turned upright."""
    c = TR.planks(seed, w=STOREY, h=T, plank=8)
    out = Canvas(T, STOREY, c.slots)
    out.slot[:] = c.slot.T
    out.shade[:] = c.shade.T
    return _adopt(out)


# ------------------------------------------------------------------------------------------------------ roofs
def _shingles(r: np.random.Generator, course: int, widths: tuple[int, int], *, grain: np.ndarray | None = None,
              tones=(-1, 0, 0, 1), gap: int = 1) -> tuple[np.ndarray, np.ndarray]:
    """Overlapping courses along x (the ridge direction), lower edges down the texture (down the slope). Each unit
    is darker just under the course above, catches light on its butt edge, then a dark shadow line."""
    shade = np.zeros((T, T), int)
    slot = np.full((T, T), _id("main"))
    for row in range(T // course):
        y0 = row * course
        start = int(r.integers(0, T))
        x = 0
        while x < T:
            sw = int(r.integers(widths[0], widths[1] + 1))
            sw = min(sw, T - x) if T - x - sw < widths[0] else sw
            tone = int(r.choice(tones))
            for dx in range(sw):
                X = (x + dx + start) % T
                for dy in range(course):
                    s = 3 + tone
                    if dy == 0:
                        s -= 1
                    elif dy == course - 2:
                        s += 1
                    if dx == 0:
                        s += 1
                    if grain is not None:
                        s += int(grain[y0 + dy, X])
                    shade[y0 + dy, X] = s
                shade[y0 + course - 1, X] = 1
                slot[y0 + course - 1, X] = _id("grout")
            if gap:                                            # the joint between two units
                X = (x + sw - 1 + start) % T
                shade[y0:y0 + course - 1, X] = 1
                slot[y0:y0 + course - 1, X] = _id("grout")
            x += sw
    return shade, slot


def slate(seed: int = 401) -> Canvas:
    """Slate: thin blue-grey slates in 8 px courses, varied tones, a few chipped corners and lichen spots."""
    r = tex.rng(seed)
    shade, slot = _shingles(r, 8, (9, 14), tones=(-1, 0, 0, 0, 1))
    lich = tex.fbm(T, T, 8, r, octaves=2)
    spots = (lich > 0.82) & (r.random((T, T)) < 0.35) & (slot == _id("main"))
    slot = np.where(spots, _id("accent"), slot)
    shade = np.where(spots, 3, shade)
    return _canvas(T, T, tex.despeckle(np.clip(shade, 0, 5), 1), slot)


def shakes(seed: int = 411) -> Canvas:
    """Split wooden shakes: narrow, uneven units with vertical grain and the odd split."""
    r = tex.rng(seed)
    g = tex.fbm(T, T, 16, r, octaves=2, aspect=5.0).T
    grain = np.where(g < 0.3, -1, np.where(g > 0.78, 1, 0))
    shade, slot = _shingles(r, 8, (5, 10), grain=grain, tones=(-1, 0, 0, 1))
    for _ in range(14):
        x, y = int(r.integers(T)), int(r.integers(T // 8)) * 8 + 2
        for dy in range(4):
            shade[y + dy, x] = 1
    return _canvas(T, T, np.clip(shade, 0, 5), slot)


def clay_tiles(seed: int = 421) -> Canvas:
    """Barrel (Roman) tiles: rounded columns running down the slope, lit on their left flank, shadowed channels
    between them, a dark lip where each tile overlaps the next; weathered tile-to-tile tone shifts."""
    r = tex.rng(seed)
    shade = np.zeros((T, T), int)
    slot = np.full((T, T), _id("main"))
    across = [3, 4, 5, 4, 3, 3, 2, 1]
    tile_l = 16
    for col in range(T // 8):
        off = int(r.choice([0, 0, 0, 1, -1])) % tile_l
        for y in range(T):
            k = (y + off) % tile_l
            for dx, p in enumerate(across):
                s = p
                if k == 0:
                    s -= 2 if dx > 0 else 1
                elif k == 1:
                    s -= 1
                elif k == tile_l - 1:
                    s += 1 if 1 <= dx <= 4 else 0
                shade[y, col * 8 + dx] = s
            if across[-1] == 1:
                slot[y, col * 8 + 7] = _id("grout")
        tones = r.choice([-1, 0, 0, 0, 1], T // tile_l + 2)
        for y in range(T):
            t = tones[(y + off) // tile_l]
            shade[y, col * 8:col * 8 + 7] += t
    lich = tex.fbm(T, T, 8, r, octaves=2)
    spots = (lich > 0.84) & (r.random((T, T)) < 0.3) & (slot == _id("main"))
    slot = np.where(spots, _id("accent"), slot)
    shade = np.where(spots, 3, shade)
    return _canvas(T, T, np.clip(shade, 0, 5), slot)


def thatch(seed: int = 431) -> Canvas:
    """Reed thatch: straws running down the slope, laid in bundled courses whose rounded lower edges throw a soft
    shadow on the course beneath."""
    r = tex.rng(seed)
    st = tex.fbm(T, T, 32, r, octaves=2, aspect=6.0).T
    shade = np.where(st < 0.28, 2, np.where(st > 0.72, 4, 3))
    shade = np.where(st > 0.9, 5, shade)
    ys, xs = np.mgrid[0:T, 0:T]
    course = 16
    jit = tex.fbm(T, T, 8, r, octaves=1)
    wob = (np.sin(2 * np.pi * xs / 16.0) * 1.5 + (jit - 0.5) * 3).astype(int)   # scalloped bundle ends
    k = (ys + wob) % course
    shade = np.where(k >= course - 3, shade - 1, shade)
    shade = np.where(k == course - 1, 1, shade)
    shade = np.where(k == 0, np.minimum(shade + 1, 5), shade)
    dark = r.random((T, T)) < 0.04
    shade = np.where(dark & (k < course - 3), shade - 1, shade)
    return _canvas(T, T, np.clip(tex.despeckle(shade, 1), 1, 5))


# ----------------------------------------------------------------------------------------------------- floors
def floorboards(seed: int = 501) -> Canvas:
    return _adopt(TR.planks(seed, plank=8))


def tavern_flags(seed: int = 511) -> Canvas:
    """Laid flagstones: rectangular slabs in 16 px rows, worn smooth in the middle, dark grout."""
    r = tex.rng(seed)
    ids, joint = _courses(r, T, T, 16, (16, 36))
    tone = r.choice([-1, 0, 0, 1], ids.max() + 1)
    shade = 3 + tone[ids]
    up_j = np.roll(joint, 1, 0) | np.roll(joint, 1, 1)
    shade = np.where(~joint & up_j, shade + 1, shade)
    n = tex.fbm(T, T, 8, r, octaves=3)
    shade = np.where(~joint & (n < 0.18), shade - 1, shade)
    shade = np.where(~joint & (n > 0.86), shade + 1, shade)
    shade = np.where(joint, 1, shade)
    slot = np.where(joint, _id("grout"), _id("main"))
    return _canvas(T, T, tex.despeckle(shade, 1), slot)


def rug(seed: int = 521, size: int = 64) -> Canvas:
    """A woven rug spanning 2x2 tiles: patterned border, field with a medallion, tasselled ends."""
    c = _canvas(size, size)
    m = 3
    for y in range(m + 2, size - m - 2):
        for x in range(m, size - m):
            bx = min(x - m, size - m - 1 - x)
            by = min(y - m - 2, size - m - 3 - y)
            b = min(bx, by)
            if b < 1:
                c.px(x, y, "trim", 1)
            elif b < 6:
                s = 3 if (x + y) % 6 < 3 else 2
                if b in (1, 5):
                    s = 4
                c.px(x, y, "trim", s)
            else:
                c.px(x, y, "cloth", 3 if (y // 2) % 2 else 2)
    cx, cy = size / 2 - 0.5, size / 2 - 0.5
    for y in range(size):
        for x in range(size):
            d = abs(x - cx) + abs(y - cy)
            if c.slot[y, x] == _id("cloth"):
                if d < 6:
                    c.px(x, y, "accent", 4 if d < 3 else 3)
                elif d < 8:
                    c.px(x, y, "trim", 3)
                elif 15 < d < 17:
                    c.px(x, y, "accent", 2)
    for x in range(m + 1, size - m - 1, 2):                       # tassels on the two short ends
        c.px(x, m, "accent", 4)
        c.px(x, m + 1, "accent", 3)
        c.px(x, size - m - 2, "accent", 3)
        c.px(x, size - m - 1, "accent", 2)
    _ = seed
    return c


def doormat(seed: int = 531) -> Canvas:
    r = tex.rng(seed)
    c = _canvas(DECAL_W, DECAL_W)
    for y in range(10, 22):
        for x in range(6, 26):
            edge = x in (6, 25) or y in (10, 21)
            c.px(x, y, "main", 2 if edge else (4 if (x // 2 + y) % 3 == 0 else 3))
    for _ in range(4):
        c.px(int(r.integers(8, 24)), int(r.integers(12, 20)), "main", 2)
    return c


# ------------------------------------------------------------------------------------------------ wall decals
# Wall decals are authored as seen face-on: 32 px wide (one tile), STOREY tall, bottom row = the storey's floor.
def _frame(c: Canvas, x0: int, y0: int, x1: int, y1: int, slot: str = "alt") -> None:
    """A lit 1 px frame: light top and left, dark bottom and right."""
    c.rect(x0, y0, x1, y0, slot, 4)
    c.rect(x0, y0, x0, y1, slot, 4)
    c.rect(x0, y1, x1, y1, slot, 2)
    c.rect(x1, y0, x1, y1, slot, 2)


def _arch_top(x: int, x0: int, x1: int, y_spring: int) -> int:
    """The y of a round arch's intrados over column x, springing at y_spring between x0 and x1."""
    rx = (x1 - x0 + 1) / 2
    cx = x0 + rx - 0.5
    d = (x - cx) / rx
    return int(round(y_spring - math.sqrt(max(0.0, 1 - d * d)) * rx * 0.8))


def door(seed: int = 601, *, open_: bool = False) -> Canvas:
    """A plank door under a round head: oak jamb and lintel, painted boards (trim), iron straps and a ring pull, a
    stone threshold. Open, the leaf swings inward and the dark interior shows a strip of lamp-lit floor."""
    c = _canvas(DECAL_W, STOREY)
    x0, x1, spring = 6, 25, 14
    for x in range(x0 - 2, x1 + 3):                              # jamb + arch ring
        yt = _arch_top(x, x0 - 2, x1 + 2, spring) - 2
        for y in range(yt, 62):
            c.px(x, y, "alt", 3)
    for x in range(x0 - 2, x1 + 3):
        yt = _arch_top(x, x0 - 2, x1 + 2, spring) - 2
        c.px(x, yt, "alt", 4)
        c.px(x, yt + 1, "alt", 4 if x < 16 else 3)
    c.rect(x0 - 2, spring, x0 - 2, 61, "alt", 4)
    c.rect(x1 + 2, spring, x1 + 2, 61, "alt", 1)
    for x in range(x0, x1 + 1):
        yt = _arch_top(x, x0, x1, spring)
        for y in range(yt, 62):
            if open_:
                c.px(x, y, "dark", 0 if y < 40 else 1)
            else:
                plank = (x - x0) % 5
                s = 3 if plank not in (0, 4) else (4 if plank == 0 else 2)
                c.px(x, y, "trim", s)
        if not open_:
            c.px(x, yt, "trim", 4)
    if open_:
        for y in range(42, 62):                                   # the floor inside, catching lamp light
            for x in range(x0, x1 + 1):
                if (y - 42) > (x1 - x) // 2:
                    c.px(x, y, "dark", 2 if y < 54 else 3)
                    if (y - 42) == (x1 - x) // 2 + 1:
                        c.px(x, y, "glow", 1)
        for x in range(x0, x0 + 4):                               # the leaf, swung in, seen edge-on
            yt = _arch_top(x, x0, x1, spring)
            for y in range(yt + 1, 61):
                c.px(x, y, "trim", 2 if x > x0 else 3)
        c.rect(x0 + 1, 30, x0 + 2, 31, "metal", 3)
    else:
        for yy in (27, 50):                                       # strap hinges
            c.rect(x0, yy, x1 - 4, yy + 1, "metal", 2)
            c.rect(x0, yy, x1 - 4, yy, "metal", 4)
            c.px(x1 - 4, yy + 1, "metal", 1)
        c.ellipse(21.5, 40.5, 1.6, 1.6, "metal", 4)              # ring pull
        c.px(21, 40, "trim", 2)
        c.px(22, 41, "trim", 2)
        c.px(21, 42, "metal", 2)
        c.px(x0 + 1, 25, "trim", 5)
    c.rect(3, 62, 28, 63, "grout", 3)                             # threshold
    c.rect(3, 62, 28, 62, "grout", 4)
    c.rect(28, 62, 28, 63, "grout", 2)
    _ = seed
    return c


def _window_frame(c: Canvas) -> tuple[int, int, int, int]:
    x0, x1, y0, y1 = 10, 21, 22, 40
    c.rect(x0 - 2, y0 - 3, x1 + 2, y0 - 2, "alt", 3)               # lintel
    c.rect(x0 - 2, y0 - 3, x1 + 2, y0 - 3, "alt", 4)
    c.rect(x0 - 1, y0 - 1, x1 + 1, y1 + 1, "alt", 3)
    c.rect(x0 - 3, y1 + 1, x1 + 3, y1 + 3, "grout", 3)             # sill
    c.rect(x0 - 3, y1 + 1, x1 + 3, y1 + 1, "grout", 4)
    c.rect(x0 - 2, y1 + 4, x1 + 2, y1 + 4, "grout", 1)
    return x0, x1, y0, y1


def _shutter(c: Canvas, x0: int, x1: int, y0: int, y1: int, *, lit_edge: bool) -> None:
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            plank = (x - x0) % 3
            c.px(x, y, "trim", 3 if plank else (4 if lit_edge else 2))
    c.rect(x0, y0, x1, y0, "trim", 4)
    c.rect(x0, y1, x1, y1, "trim", 1)
    c.line(x0, y1 - 2, x1, y0 + 3, "trim", 2)                       # Z brace
    c.rect(x0, y0 + 3, x1, y0 + 3, "trim", 2)
    c.rect(x0, y1 - 3, x1, y1 - 3, "trim", 2)


def window(seed: int = 611, *, kind: str = "open") -> Canvas:
    """A casement window with an oak frame, a stone sill and painted shutters (trim). `open`: glazing reflects the
    sky; `shuttered`: closed shutters with a crescent cut-out; `lit`: warm lamplight behind leaded panes, for night."""
    c = _canvas(DECAL_W, STOREY)
    x0, x1, y0, y1 = _window_frame(c)
    if kind == "shuttered":
        _shutter(c, x0, (x0 + x1) // 2, y0, y1, lit_edge=True)
        _shutter(c, (x0 + x1) // 2 + 1, x1, y0, y1, lit_edge=False)
        c.rect(x0 + 2, y0 + 7, x0 + 3, y0 + 8, "dark", 0)           # crescent cut-out
        c.px(x0 + 2, y0 + 6, "dark", 0)
        c.rect((x0 + x1) // 2, y0, (x0 + x1) // 2, y1, "trim", 1)
        return c
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if kind == "lit":
                d = abs(x - (x0 + x1) / 2) / 6 + abs(y - (y0 + y1) / 2) / 12
                c.px(x, y, "glow", 5 if d < 0.55 else (4 if d < 0.95 else 3))
            else:
                s = 2 if (y - y0) > (x - x0) + 6 else 3
                if 0 <= (x - x0) - (y - y0) + 3 <= 1 or 0 <= (x - x0) - (y - y0) + 9 <= 0:
                    s = 4
                c.px(x, y, "accent", s)
    mx, my = (x0 + x1) // 2, (y0 + y1) // 2
    lead = "metal" if kind == "lit" else "alt"
    c.rect(mx, y0, mx, y1, lead, 2)
    c.rect(x0, my, x1, my, lead, 2)
    if kind == "lit":
        for x in range(x0, x1 + 1, 3):
            for y in range(y0, y1 + 1, 4):
                c.px(x, y, "metal", 2)
    else:
        c.px(x0 + 1, y0 + 1, "accent", 5)
        c.px(mx + 2, y0 + 1, "accent", 5)
    if kind in ("open", "lit"):                                    # open shutters fold back against the wall
        _shutter(c, x0 - 8, x0 - 3, y0 - 1, y1 + 1, lit_edge=True)
        _shutter(c, x1 + 3, x1 + 8, y0 - 1, y1 + 1, lit_edge=False)
    if kind == "lit":
        c.rect(x0 - 2, y1 + 1, x1 + 2, y1 + 1, "glow", 2)          # light on the sill
    _ = seed
    return c


def lancet(seed: int = 621, *, lit: bool = False) -> Canvas:
    """A chapel's tall pointed window: stone surround, stained glass in red/blue/gold quarries, lead cames."""
    c = _canvas(DECAL_W, STOREY)
    x0, x1, y0, y1 = 12, 19, 14, 46
    cx = (x0 + x1) / 2
    for y in range(y0 - 3, y1 + 3):
        for x in range(x0 - 2, x1 + 3):
            dy = y - (y0 - 2)
            half = (x1 - x0) / 2 + 2
            if dy < 8 and abs(x - cx) > half * math.sqrt(max(0.0, dy / 8)) + 0.2:
                continue
            c.px(x, y, "grout", 4 if x < cx else 2)
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            dy = y - y0
            half = (x1 - x0) / 2
            if dy < 6 and abs(x - cx) > half * math.sqrt(max(0.0, dy / 6)) + 0.2:
                continue
            q = ((x - x0) // 2 + (y - y0) // 3) % 3
            sl = ("accent", "cloth", "glow")[q]
            if (x - x0) % 2 == 0 and (y - y0) % 3 == 0:
                c.px(x, y, "metal", 1)
                continue
            c.px(x, y, sl, (5 if lit else 3) if (x + y) % 4 else (4 if lit else 2))
    c.rect(x0 - 3, y1 + 2, x1 + 3, y1 + 3, "grout", 3)
    _ = seed
    return c


def rose_window(seed: int = 631, *, lit: bool = False) -> Canvas:
    """A round rose window (32x32) for a gable: stone ring, radial tracery, coloured glass."""
    c = _canvas(DECAL_W, DECAL_W)
    cx = cy = 15.5
    for y in range(32):
        for x in range(32):
            d = math.hypot(x - cx, y - cy)
            if d > 13.5:
                continue
            if d > 11:
                c.px(x, y, "grout", 4 if (x + y) < 31 else 2)
            elif d < 2.5:
                c.px(x, y, "glow", 5 if lit else 4)
            else:
                a = math.atan2(y - cy, x - cx)
                spoke = abs(((a / (2 * math.pi) * 8) % 1) - 0.5) > 0.42
                if spoke or 6.5 < d < 7.5:
                    c.px(x, y, "grout", 3)
                else:
                    sl = "accent" if int(a / (2 * math.pi) * 8 + 8) % 2 else "cloth"
                    c.px(x, y, sl, (5 if lit else 3) if d < 6.5 else (4 if lit else 2))
    _ = seed
    return c


EMBLEMS = {
    "mug": ["..###..", ".#####.", ".#...##", ".#...#.#", ".#...##", ".#####.", "..###.."],
    "anvil": ["#######", ".######", "...###.", "...###.", "..#####", ".......", "......."],
    "bag": ["...#...", "..###..", ".#####.", "#######", "#######", ".#####.", "......."],
    "blank": [],
}


def sign_bracket(seed: int = 641, *, emblem: str = "blank") -> Canvas:
    """A wrought-iron bracket with a scroll, and a painted board hanging from two short chains. As a decal it reads
    as the sign seen against the wall; `overlays` ship a swinging version for building props."""
    c = _canvas(DECAL_W, STOREY)
    c.rect(2, 14, 3, 22, "metal", 2)                               # wall plate
    c.rect(2, 14, 2, 22, "metal", 4)
    c.rect(3, 16, 26, 16, "metal", 3)                               # arm
    c.rect(3, 15, 26, 15, "metal", 4)
    c.line(4, 22, 14, 17, "metal", 2)                               # brace
    for x, y in ((14, 18), (15, 19), (16, 19), (17, 18)):           # scroll
        c.px(x, y, "metal", 3)
    _hanging_board(c, 6, 19, emblem)
    _ = seed
    return c


def _hanging_board(c: Canvas, x0: int, y0: int, emblem: str, *, tilt: int = 0) -> None:
    """Board on two chains under an arm; `tilt` (-1, 0, 1) shears it as it swings."""
    for x in (x0 + 4, x0 + 15):
        c.px(x, y0 - 2, "metal", 2)
        c.px(x + tilt, y0 - 1, "metal", 3)
    for y in range(13):
        dx = (tilt * y) // 8
        for x in range(x0 + 1, x0 + 20):
            edge = y in (0, 12) or x in (x0 + 1, x0 + 19)
            c.px(x + dx, y0 + y, "trim" if edge else "alt", (4 if y == 0 else 2) if edge else (4 if x < x0 + 6 else 3))
    pat = EMBLEMS[emblem]
    for ey, row in enumerate(pat):
        for ex, ch in enumerate(row):
            if ch == "#":
                c.px(x0 + 7 + ex + (tilt * (ey + 3)) // 8, y0 + 3 + ey, "metal", 5 if ey < 2 else 4)


def ivy(seed: int = 651) -> Canvas:
    """Ivy climbing from the foot of a wall: wandering stems with clusters of lit leaves, thinning upwards."""
    r = tex.rng(seed)
    c = _canvas(DECAL_W, STOREY)
    for k in range(4):
        x, y = 4 + k * 7 + float(r.integers(-2, 3)), 63.0
        top = int(r.integers(10, 34))
        while y > top:
            c.px(x, y, "alt", 2)
            if r.random() < 0.45:
                cx, cy = x + r.choice([-2, -1, 1, 2]), y + r.uniform(-1, 1)
                for dx, dy, s in ((0, 0, 3), (-1, 0, 4), (0, -1, 4), (1, 0, 2), (0, 1, 2), (1, 1, 1)):
                    if 0 <= cx + dx < DECAL_W:
                        c.px(cx + dx, cy + dy, "leaf", s)
                if r.random() < 0.3:
                    c.px(cx - 1, cy - 1, "leaf", 5)
            x = min(DECAL_W - 2, max(1, x + r.choice([-1, 0, 0, 1])))
            y -= 1
    return c


def crack(seed: int = 661) -> Canvas:
    """A spalled patch (32x32, placed anywhere on a face): render broken away to show brick, with cracks running
    out of it."""
    r = tex.rng(seed)
    c = _canvas(DECAL_W, DECAL_W)
    n = tex.fbm(DECAL_W, DECAL_W, 4, r, octaves=2)
    ys, xs = np.mgrid[0:DECAL_W, 0:DECAL_W]
    d = ((xs - 15) / 9.0) ** 2 + ((ys - 16) / 6.0) ** 2 + (n - 0.5) * 0.9
    hole = d < 1.0
    for y in range(DECAL_W):
        for x in range(DECAL_W):
            if not hole[y, x]:
                continue
            if y % 4 == 3 or (x + (4 if (y // 4) % 2 else 0)) % 8 == 7:
                c.px(x, y, "grout", 1)
            else:
                c.px(x, y, "accent", 4 if y % 4 == 0 else 3)
    rim_dn = hole & ~np.roll(hole, -1, 0)
    rim_up = hole & ~np.roll(hole, 1, 0)
    for y, x in zip(*np.nonzero(rim_up)):
        c.px(x, y, "dark", 1)
    for y, x in zip(*np.nonzero(rim_dn)):
        if y + 1 < DECAL_W:
            c.px(x, y + 1, "main", 5)

    def arm(x: float, y: float, ang: float, length: int) -> None:
        for _ in range(length):
            ang += r.normal(0, 0.35)
            x += math.cos(ang)
            y += math.sin(ang)
            if not (0 <= x < DECAL_W and 0 <= y < DECAL_W):
                return
            if not c.slot[int(y), int(x)]:
                c.px(x, y, "dark", 1)

    for a in (-2.6, -0.4, 1.9, 0.9):
        arm(15 + math.cos(a) * 8, 16 + math.sin(a) * 5, a, int(r.integers(6, 11)))
    return c


def wall_lantern(seed: int = 671) -> Canvas:
    """A lantern on a wall bracket (lit: the game adds bloom at night)."""
    c = _canvas(DECAL_W, STOREY)
    c.rect(14, 14, 17, 15, "metal", 3)
    c.rect(15, 16, 16, 17, "metal", 2)
    c.polygon([(12, 18), (19, 18), (20, 21), (11, 21)], "metal", 2)
    c.rect(12, 21, 19, 28, "metal", 2)
    c.rect(13, 22, 18, 27, "glow", 4)
    c.rect(14, 23, 15, 25, "glow", 5)
    c.rect(11, 28, 20, 29, "metal", 3)
    c.px(15, 30, "metal", 2)
    _ = seed
    return c


def arrow_slit(seed: int = 681) -> Canvas:
    """A defensive slit for towers and walls: a dressed-stone surround and a dark cross-shaped opening."""
    c = _canvas(DECAL_W, STOREY)
    c.rect(13, 14, 18, 44, "grout", 3)
    c.rect(13, 14, 13, 44, "grout", 4)
    c.rect(18, 14, 18, 44, "grout", 2)
    c.rect(15, 17, 16, 41, "dark", 0)
    c.rect(13, 27, 18, 28, "dark", 0)
    c.rect(15, 17, 15, 41, "dark", 1)
    _ = seed
    return c


def notices(seed: int = 691) -> Canvas:
    """Pinned notices for a board (32x32): bills and a wanted poster with ink lines and red wax seals."""
    r = tex.rng(seed)
    c = _canvas(DECAL_W, DECAL_W)
    for x0, y0, w, h in ((3, 6, 9, 12), (13, 4, 8, 10), (22, 7, 8, 11), (12, 16, 10, 11), (4, 20, 7, 8)):
        c.rect(x0, y0, x0 + w - 1, y0 + h - 1, "main", 4)
        c.rect(x0 + w - 1, y0 + 1, x0 + w - 1, y0 + h - 1, "main", 2)
        c.rect(x0, y0 + h - 1, x0 + w - 1, y0 + h - 1, "main", 3)
        for ly in range(y0 + 2, y0 + h - 2, 2):
            lw = int(r.integers(w // 2, w - 2))
            c.rect(x0 + 1, ly, x0 + lw, ly, "dark", 2)
        c.px(x0 + w // 2, y0, "trim", 4)
    return c


def belfry(seed: int = 701) -> Canvas:
    """A belfry opening: a tall round-headed arch in a stone surround, the bell hanging in the dark inside."""
    c = _canvas(DECAL_W, STOREY)
    x0, x1, spring = 8, 23, 22
    for x in range(x0 - 2, x1 + 3):
        yt = _arch_top(x, x0 - 2, x1 + 2, spring) - 1
        for y in range(yt, 54):
            c.px(x, y, "grout", 4 if x < 16 else 3)
    for x in range(x0, x1 + 1):
        for y in range(_arch_top(x, x0, x1, spring), 52):
            c.px(x, y, "dark", 0 if y < 30 else 1)
    c.rect(15, 14, 16, 19, "alt", 2)                                    # headstock
    c.polygon([(11, 31), (20, 31), (19, 25), (17, 21), (14, 21), (12, 25)], "metal", 3)
    c.rect(12, 22, 13, 30, "metal", 4)
    c.rect(18, 23, 19, 30, "metal", 2)
    c.rect(10, 31, 21, 32, "metal", 2)
    c.rect(10, 31, 14, 31, "metal", 5)
    c.rect(15, 33, 16, 34, "metal", 1)                                  # clapper
    c.rect(x0 - 3, 52, x1 + 3, 54, "grout", 3)
    c.rect(x0 - 3, 52, x1 + 3, 52, "grout", 4)
    _ = seed
    return c


def hearth(seed: int = 711) -> Canvas:
    """An inglenook fireplace for interior walls: a stone surround and mantel, a sooty firebox, logs and flames."""
    c = _canvas(DECAL_W, STOREY)
    c.rect(1, 24, 30, 63, "grout", 3)                                  # surround
    c.rect(1, 24, 30, 24, "grout", 4)
    c.rect(1, 24, 1, 63, "grout", 4)
    c.rect(30, 24, 30, 63, "grout", 2)
    for y in range(28, 63, 6):
        c.rect(1, y, 4, y, "grout", 2)
        c.rect(27, y, 30, y, "grout", 2)
    c.rect(0, 20, 31, 23, "alt", 3)                                    # mantel beam
    c.rect(0, 20, 31, 20, "alt", 4)
    c.rect(0, 23, 31, 23, "alt", 1)
    for x in range(5, 27):                                             # firebox
        top = _arch_top(x, 5, 26, 34)
        for y in range(top, 61):
            c.px(x, y, "dark", 0 if y < 50 else 1)
    c.rect(8, 56, 23, 58, "main", 2)                                   # logs
    c.rect(10, 54, 21, 55, "main", 3)
    for x0, h in ((9, 10), (13, 15), (17, 12), (21, 8)):               # flames
        c.polygon([(x0 - 2, 56), (x0 + 3, 56), (x0 + 0.5, 56 - h)], "glow", 3)
        c.polygon([(x0 - 0.5, 56), (x0 + 1.5, 56), (x0 + 0.5, 56 - h * 0.55)], "glow", 5)
    c.rect(4, 61, 27, 63, "grout", 2)
    _ = seed
    return c


# ---------------------------------------------------------------------------------------------- catalogue
@dataclass
class TownTex:
    id: str
    kind: str                                    # wall | roof | floor | floor_decal | wall_decal
    make: Callable[[], Canvas]
    variants: dict[str, dict[str, str]]          # variant -> slot -> ramp ref
    tags: list[str] = field(default_factory=list)


def _v(**slots: str) -> dict[str, str]:
    return {k: (v if "/" in v else f"town/{v}") for k, v in slots.items()}


def _trimmed(**base: str) -> dict[str, dict[str, str]]:
    """One variant per trim paint (red, blue, green, plain oak)."""
    return {name: _v(**base, trim=ref) for name, ref in TRIMS.items()}


_DOOR = dict(alt="wood/oak", metal="metal/iron", grout="terrain/stone_warm", dark="interior", glow="glow")
_WIN = dict(alt="wood/dark", metal="lead", grout="terrain/stone_warm", accent="glass", glow="glow", dark="interior")

CATALOGUE: list[TownTex] = [
    # walls: 128x64, one storey per period
    TownTex("timber_frame", "wall", timber_frame, {
        "white": _v(main="plaster", alt="timber"), "ochre": _v(main="plaster_ochre", alt="timber"),
        "rose": _v(main="plaster_rose", alt="timber")}),
    TownTex("whitewash", "wall", whitewash, {"white": _v(main="plaster"), "ochre": _v(main="plaster_ochre"),
                                             "rose": _v(main="plaster_rose")}),
    TownTex("fieldstone", "wall", fieldstone, {"grey": _v(main="terrain/stone", grout="lime"),
                                               "warm": _v(main="terrain/stone_warm", grout="lime")}),
    TownTex("dressed_stone", "wall", dressed_stone, {"grey": _v(main="terrain/stone", grout="terrain/mortar"),
                                                     "warm": _v(main="terrain/stone_warm", grout="terrain/mortar")}),
    TownTex("log_wall", "wall", log_wall, {"pine": _v(main="log", grout="chink"),
                                           "dark": _v(main="wood/dark", grout="chink")}),
    TownTex("plank_shed", "wall", plank_shed, {"oak": _v(main="terrain/planks", metal="metal/iron"),
                                               "weathered": _v(main="shake_grey", metal="metal/iron")}),
    # roofs: 128x128, courses along U, down-slope along V
    TownTex("slate", "roof", slate, {"blue": _v(main="slate", grout="wood/dark", accent="terrain/moss"),
                                     "green": _v(main="slate_green", grout="wood/dark", accent="terrain/moss")}),
    TownTex("clay_tiles", "roof", clay_tiles, {"red": _v(main="clay", grout="wood/dark", accent="terrain/moss"),
                                               "ochre": _v(main="clay_ochre", grout="wood/dark",
                                                           accent="terrain/moss")}),
    TownTex("thatch", "roof", thatch, {"fresh": _v(main="thatch"), "old": _v(main="thatch_old")}),
    TownTex("shakes", "roof", shakes, {"cedar": _v(main="shake", grout="wood/dark"),
                                       "grey": _v(main="shake_grey", grout="wood/dark")}),
    # interior floors: 128x128 tops
    TownTex("floorboards", "floor", floorboards, {"oak": _v(main="wood/oak", metal="metal/iron"),
                                                  "honey": _v(main="wood/pale", metal="metal/iron"),
                                                  "dark": _v(main="wood/dark", metal="metal/iron")}),
    TownTex("tavern_flags", "floor", tavern_flags, {"warm": _v(main="terrain/stone_warm", grout="terrain/mortar"),
                                                    "grey": _v(main="terrain/stone", grout="terrain/mortar")}),
    # floor decals
    TownTex("rug", "floor_decal", rug, {"crimson": _v(cloth="cloth/crimson", trim="cloth/navy", accent="metal/gold"),
                                        "navy": _v(cloth="cloth/navy", trim="cloth/ivory", accent="cloth/ochre"),
                                        "forest": _v(cloth="cloth/forest", trim="cloth/ochre", accent="cloth/ivory")}),
    TownTex("doormat", "floor_decal", doormat, {"straw": _v(main="prop/hay")}),
    # wall decals
    TownTex("door", "wall_decal", door, _trimmed(**_DOOR)),
    TownTex("door_open", "wall_decal", lambda: door(open_=True), _trimmed(**_DOOR)),
    TownTex("window", "wall_decal", window, _trimmed(**_WIN)),
    TownTex("window_shuttered", "wall_decal", lambda: window(kind="shuttered"), _trimmed(**_WIN)),
    TownTex("window_lit", "wall_decal", lambda: window(kind="lit"), _trimmed(**_WIN, )),
    TownTex("lancet", "wall_decal", lancet, {"stained": _v(grout="terrain/stone", accent="stained_red",
                                                           cloth="stained_blue", glow="metal/gold", metal="lead")}),
    TownTex("lancet_lit", "wall_decal", lambda: lancet(lit=True), {
        "stained": _v(grout="terrain/stone", accent="stained_red", cloth="stained_blue", glow="glow", metal="lead")}),
    TownTex("rose_window", "wall_decal", rose_window, {"stained": _v(grout="terrain/stone", accent="stained_red",
                                                                     cloth="stained_blue", glow="metal/gold")}),
    TownTex("rose_window_lit", "wall_decal", lambda: rose_window(lit=True), {
        "stained": _v(grout="terrain/stone", accent="stained_red", cloth="stained_blue", glow="glow")}),
    *[TownTex(f"sign_{e}" if e != "blank" else "sign_bracket", "wall_decal", (lambda e=e: sign_bracket(emblem=e)),
              _trimmed(alt="wood/pale", metal="metal/iron")) for e in ("blank", "mug", "anvil", "bag")],
    TownTex("ivy", "wall_decal", ivy, {"green": _v(alt="foliage/bark", leaf="foliage/leaf"),
                                       "autumn": _v(alt="foliage/bark", leaf="foliage/leaf_red")}),
    TownTex("crack", "wall_decal", crack, {"plaster": _v(main="plaster", accent="terrain/brick",
                                                         grout="terrain/mortar", dark="timber"),
                                           "stone": _v(main="terrain/stone", accent="terrain/stone_dark",
                                                       grout="terrain/mortar", dark="terrain/mortar")}),
    TownTex("wall_lantern", "wall_decal", wall_lantern, {"iron": _v(metal="metal/dark", glow="glow")}),
    TownTex("arrow_slit", "wall_decal", arrow_slit, {"grey": _v(grout="terrain/stone", dark="interior"),
                                                     "warm": _v(grout="terrain/stone_warm", dark="interior")}),
    TownTex("belfry", "wall_decal", belfry, {"bronze": _v(grout="terrain/stone", dark="interior", metal="bell",
                                                          alt="wood/dark")}),
    TownTex("hearth", "wall_decal", hearth, {"stone": _v(grout="terrain/stone_warm", alt="wood/dark",
                                                         dark="prop/soot", main="foliage/bark", glow="prop/flame")}),
    TownTex("notices", "wall_decal", notices, {"paper": _v(main="paper", dark="cloth/charcoal",
                                                           trim="cloth/crimson")}),
]
_BY_ID = {t.id: t for t in CATALOGUE}


@lru_cache(maxsize=None)
def _canvas_of(tid: str) -> Canvas:
    return _BY_ID[tid].make()


@lru_cache(maxsize=None)
def tex_image(tid: str, variant: str | None = None) -> Image.Image:
    """A town texture (or, for ids it doesn't know, a terrain one) rendered in one of its variants."""
    if tid in _BY_ID:
        t = _BY_ID[tid]
        return _canvas_of(tid).render(ramps(t.variants[variant or next(iter(t.variants))]))
    t = next(x for x in TR.CATALOGUE if x.id == tid)
    v = t.variants[variant] if variant else next(iter(t.variants.values()))
    return t.frames()[0].render(TR.ramps(v))


@lru_cache(maxsize=None)
def tex_rgba(tid: str, variant: str | None = None) -> np.ndarray:
    return np.asarray(tex_image(tid, variant)).astype(np.float32)


# ------------------------------------------------------------------------------------------------ the renderer
# World axes as in arcpix.iso: U (the map's X, screen down-right), V (the map's Z, screen down-left), Z up, in texels.
# Projection: screen x = U - V, screen y = (U + V) / 2 - Z. The camera looks along -(1, 1, 1).
VIEW = np.array([1.0, 1.0, 1.0])
LIGHT = np.array([-0.6, 0.3425, 0.723])
LIGHT /= np.linalg.norm(LIGHT)
AMB = 0.62                                   # an unlit face: the +U faces in arcpix.iso
KD = (1.0 - AMB) / LIGHT[2]                  # so a flat top is exactly 1.0 and a +V face 0.80, as in arcpix.iso
SUN = np.array([-0.35, 0.2, 0.92])           # steeper, for ground shadows only, so towers don't shade half the map
SUN /= np.linalg.norm(SUN)
TINT_U = np.array([0.95, 0.97, 1.08])
TINT_V = np.array([1.0, 1.0, 1.04])
OUT_RGB = np.array([int(OUTLINE[i:i + 2], 16) for i in (1, 3, 5)], np.float32)
SHADOW_RGBA = (22, 20, 44, 96)

Fn = Callable[[np.ndarray, np.ndarray, np.ndarray], np.ndarray]


@dataclass
class Face:
    """A planar polygon: world point = o + a*e1 + b*e2 for (a, b) inside `rings` (even-odd, so rings can cut holes).
    `fn(a, b, world)` gives its RGBA; None makes it a shadow caster only (the faces the camera never sees)."""
    o: np.ndarray
    e1: np.ndarray
    e2: np.ndarray
    rings: list[list[tuple[float, float]]]
    n: np.ndarray
    fn: Fn | None
    part: int = 0
    cast: bool = True
    shade: float | None = None               # fixed light multiplier: glowing or deep-interior faces


@dataclass
class Blob:
    """A lit sphere (fruit, a bell, a lantern pane), depth-tested against the faces."""
    p: tuple[float, float, float]
    r: float
    colours: list[str]
    part: int = 0
    squash: float = 1.0
    glow: bool = False


@dataclass
class Model:
    fw: int
    fd: int
    faces: list[Face] = field(default_factory=list)
    blobs: list[Blob] = field(default_factory=list)
    attach: dict[str, list[tuple[float, float, float]]] = field(default_factory=dict)
    ground_shadow: bool = True
    _part: int = 0

    def part(self) -> int:
        self._part += 1
        return self._part

    # ---------------------------------------------------------------------------------------------- primitives
    def poly(self, pts, fn: Fn | None, n, *, part: int | None = None, holes=(), cast: bool = True,
             shade: float | None = None) -> int:
        """A planar polygon from world points, listed from its screen-left bottom corner so that `a` runs to the
        right and `b` upwards across the face as seen (decals and wall textures rely on that)."""
        P = [np.array(p, float) for p in pts]
        o = P[0]
        e1 = P[1] - P[0]
        e1 /= np.linalg.norm(e1)
        d = P[-1] - P[0]
        d = d - d.dot(e1) * e1
        e2 = d / np.linalg.norm(d)
        rings = [[((p - o).dot(e1), (p - o).dot(e2)) for p in P]]
        for h in holes:
            rings.append([((np.array(p, float) - o).dot(e1), (np.array(p, float) - o).dot(e2)) for p in h])
        p = self.part() if part is None else part
        self.faces.append(Face(o, e1, e2, rings, np.array(n, float) / np.linalg.norm(n), fn, p, cast, shade))
        return p

    def box(self, x0, x1, y0, y1, z0, z1, side: Fn | None, top: Fn | None = None, *, side_u: Fn | None = None,
            part: int | None = None, cast: bool = True, shade: float | None = None) -> int:
        """An axis-aligned block. `side` paints the +V face (screen left), `side_u` the +U face (defaults to side),
        `top` the lid. The three faces the camera never sees still cast shadows."""
        p = self.part() if part is None else part
        su = side_u or side
        self.poly([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], side, (0, 1, 0), part=p, cast=cast,
                  shade=shade)
        self.poly([(x1, y1, z0), (x1, y0, z0), (x1, y0, z1), (x1, y1, z1)], su, (1, 0, 0), part=p, cast=cast,
                  shade=shade)
        self.poly([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], top, (0, 0, 1), part=p, cast=cast,
                  shade=shade)
        self.poly([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], None, (0, -1, 0), part=p, cast=cast)
        self.poly([(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)], None, (-1, 0, 0), part=p, cast=cast)
        if z0 > 0:
            self.poly([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], None, (0, 0, -1), part=p, cast=cast)
        return p


def _inside(a: np.ndarray, b: np.ndarray, rings) -> np.ndarray:
    ins = np.zeros(a.shape, bool)
    for ring in rings:
        n = len(ring)
        for k in range(n):
            (x1, y1), (x2, y2) = ring[k], ring[(k + 1) % n]
            if y1 == y2:
                continue
            cond = (y1 > b) != (y2 > b)
            xi = x1 + (b - y1) * (x2 - x1) / (y2 - y1)
            ins ^= cond & (a < xi)
    return ins


def _projv(d: np.ndarray) -> tuple[float, float]:
    return d[0] - d[1], (d[0] + d[1]) / 2 - d[2]


def _occluded(P: np.ndarray, ray: np.ndarray, faces: list[Face], *, eps: float = 0.75) -> np.ndarray:
    """True where the ray from P towards the light hits any shadow-casting face."""
    hit = np.zeros(len(P), bool)
    for f in faces:
        if not f.cast:
            continue
        den = ray.dot(f.n)
        if abs(den) < 1e-6:
            continue
        t = (f.o - P).dot(f.n) / den
        ok = (t > eps) & ~hit
        if not ok.any():
            continue
        Q = P[ok] + t[ok, None] * ray
        a = (Q - f.o).dot(f.e1)
        b = (Q - f.o).dot(f.e2)
        sub = _inside(a, b, f.rings)
        idx = np.nonzero(ok)[0]
        hit[idx[sub]] = True
    return hit


@dataclass
class Rendered:
    image: Image.Image                      # lit, outlined RGBA
    shadow: Image.Image                     # ground shadow, same frame (draw on the ground before units)
    origin: tuple[int, int]                 # pixel of the footprint's back corner (local U = V = 0, ground)
    fw: int
    fd: int
    attach: dict[str, list[tuple[int, int]]]

    @property
    def anchor(self) -> tuple[int, int]:
        """Pixel of the footprint's ground centre."""
        ox, oy = self.origin
        return ox + (self.fw - self.fd) * TILE // 2, oy + (self.fw + self.fd) * TILE // 4


def _hex_rgb(h: str) -> np.ndarray:
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], np.float32)


def model_bounds(m: Model, pad: int = 4) -> tuple[int, int, int, int]:
    """Screen extent of a model relative to its origin, including where its ground shadow lands."""
    xs, ys = [0.0], [0.0]
    for f in m.faces:
        for a, b in f.rings[0]:
            p = f.o + a * f.e1 + b * f.e2
            sx, sy = _projv(p)
            xs.append(sx)
            ys.append(sy)
            if p[2] > 0:
                gx, gy = _projv(p - p[2] / SUN[2] * SUN)
                xs.append(gx)
                ys.append(gy)
    for bl in m.blobs:
        sx, sy = _projv(np.array(bl.p))
        xs += [sx - bl.r, sx + bl.r]
        ys += [sy - bl.r, sy + bl.r]
    return (int(math.floor(min(xs))) - pad, int(math.floor(min(ys))) - pad,
            int(math.ceil(max(xs))) + pad, int(math.ceil(max(ys))) + pad)


def union(bs) -> tuple[int, int, int, int]:
    bs = list(bs)
    return min(b[0] for b in bs), min(b[1] for b in bs), max(b[2] for b in bs), max(b[3] for b in bs)


def render(m: Model, *, pad: int = 4, bounds: tuple[int, int, int, int] | None = None) -> Rendered:
    """Rasterises a model: z-buffered faces and blobs, top-left lighting with cast shadows, selective outlines,
    and the ground shadow it throws. `bounds` (x0, y0, x1, y1 in screen units relative to the origin) fixes the
    frame, so every frame of an animation shares one size and origin."""
    vis = [f for f in m.faces if f.fn is not None and f.n.dot(VIEW) > 1e-3]
    bounds = bounds or model_bounds(m, pad)
    OX, OY = -bounds[0], -bounds[1]
    W, H = bounds[2] - bounds[0] + 1, bounds[3] - bounds[1] + 1
    col = np.zeros((H, W, 4), np.float32)
    zbuf = np.full((H, W), -1e9)
    fid = np.full((H, W), -1, int)
    part = np.zeros((H, W), int)
    world = np.zeros((H, W, 3))
    for k, f in enumerate(vis):
        p0 = _projv(f.o)
        d1, d2 = _projv(f.e1), _projv(f.e2)
        det = d1[0] * d2[1] - d2[0] * d1[1]
        if abs(det) < 1e-6:
            continue
        corners = [_projv(f.o + a * f.e1 + b * f.e2) for a, b in f.rings[0]]
        x0 = max(0, int(math.floor(min(c[0] for c in corners) + OX)) - 1)
        x1 = min(W, int(math.ceil(max(c[0] for c in corners) + OX)) + 2)
        y0 = max(0, int(math.floor(min(c[1] for c in corners) + OY)) - 1)
        y1 = min(H, int(math.ceil(max(c[1] for c in corners) + OY)) + 2)
        if x1 <= x0 or y1 <= y0:
            continue
        YY, XX = np.mgrid[y0:y1, x0:x1]
        rx = XX + 0.5 - OX - p0[0]
        ry = YY + 0.5 - OY - p0[1]
        a = (d2[1] * rx - d2[0] * ry) / det
        b = (-d1[1] * rx + d1[0] * ry) / det
        ins = _inside(a, b, f.rings)
        P = f.o + a[..., None] * f.e1 + b[..., None] * f.e2
        depth = P.sum(-1)
        sel = ins & (depth > zbuf[y0:y1, x0:x1] + 1e-6)
        if not sel.any():
            continue
        rgba = f.fn(a[sel], b[sel], P[sel])
        keep = rgba[:, 3] >= 128
        yy, xx = YY[sel][keep], XX[sel][keep]
        col[yy, xx] = rgba[keep]
        zbuf[yy, xx] = depth[sel][keep]
        fid[yy, xx] = k
        part[yy, xx] = f.part
        world[yy, xx] = P[sel][keep]
    # lighting
    drawn = fid >= 0
    mul = np.ones((H, W), np.float32)
    tint = np.ones((H, W, 3), np.float32)
    normals = np.array([f.n for f in vis]) if vis else np.zeros((0, 3))
    fixed = np.array([f.shade if f.shade is not None else -1.0 for f in vis])
    if drawn.any():
        n = normals[fid[drawn]]
        ndl = n.dot(LIGHT)
        mu = AMB + KD * np.clip(ndl, 0, None)
        P = world[drawn]
        lit = ndl > 0.02
        sh = np.zeros(len(P), bool)
        sh[lit] = _occluded(P[lit], LIGHT, m.faces)
        mu = np.where(sh, AMB * 0.96, mu)
        wall = np.abs(n[:, 2]) < 0.3
        mu = np.where(wall & (P[:, 2] < 12), mu * (0.84 + 0.16 * np.clip(P[:, 2], 0, 12) / 12), mu)
        fx = fixed[fid[drawn]]
        mu = np.where(fx >= 0, fx, mu)
        mul[drawn] = np.minimum(mu, 1.12)
        tint[drawn] = 1 + np.clip(n[:, 0:1], 0, 1) ** 2 * (TINT_U - 1) + np.clip(n[:, 1:2], 0, 1) ** 2 * (TINT_V - 1)
        tint[drawn] = np.where((fx >= 0)[:, None], 1.0, tint[drawn])
    rgb = col[..., :3] * mul[..., None] * tint
    alpha = np.where(drawn, 255.0, 0.0)
    # blobs: lit spheres, after the faces, depth-tested
    for bl in m.blobs:
        sx, sy = _projv(np.array(bl.p))
        cx, cy = sx + OX, sy + OY
        ry = bl.r * bl.squash
        ramp_rgb = [_hex_rgb(c) for c in bl.colours]
        pdepth = sum(bl.p)
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - bl.r - 1), int(cx + bl.r + 2)):
                if not (0 <= x < W and 0 <= y < H):
                    continue
                nx, ny = (x + 0.5 - cx) / bl.r, (y + 0.5 - cy) / ry
                d = nx * nx + ny * ny
                if d > 1:
                    continue
                nz = math.sqrt(1 - d)
                dep = pdepth + nz * bl.r * 1.5
                if dep <= zbuf[y, x]:
                    continue
                dot = (-0.55 * nx - 0.85 * ny) * 0.9 + nz * 0.35 - 0.25
                s = int(round(3 + dot * 1.9))
                if bl.glow:
                    s = 5 if d < 0.35 else 4
                c = ramp_rgb[max(1, min(5, s))]
                if not bl.glow and _occluded(np.array([bl.p]), LIGHT, m.faces)[0]:
                    c = c * 0.78
                rgb[y, x] = c
                alpha[y, x] = 255
                zbuf[y, x] = dep
                part[y, x] = bl.part
                fid[y, x] = 10 ** 6
    rgb = _outline(rgb, alpha, zbuf, part)
    img = np.dstack([np.clip(rgb, 0, 255), np.where(rgb[..., 0] >= 0, alpha, 0)])
    img[..., 3] = np.maximum(img[..., 3], np.where(_silhouette(alpha), 255, 0))
    # the ground shadow (z = 0, outside the footprint)
    YY, XX = np.mgrid[0:H, 0:W]
    sxr, syr = XX + 0.5 - OX, YY + 0.5 - OY
    U, V = syr + sxr / 2, syr - sxr / 2
    fp = (U >= 0) & (U <= m.fw * TILE) & (V >= 0) & (V <= m.fd * TILE)
    cand = ~fp & (img[..., 3] == 0)
    G = np.stack([U[cand], V[cand], np.zeros(cand.sum())], -1)
    shadow = np.zeros((H, W, 4), np.uint8)
    if m.ground_shadow:
        hit = _occluded(G, SUN, m.faces, eps=0.5)
        sy_, sx_ = np.nonzero(cand)
        shadow[sy_[hit], sx_[hit]] = SHADOW_RGBA
    attach = {k: [(int(round(_projv(np.array(p))[0] + OX)), int(round(_projv(np.array(p))[1] + OY))) for p in v]
              for k, v in m.attach.items()}
    return Rendered(Image.fromarray(img.astype(np.uint8), "RGBA"), Image.fromarray(shadow, "RGBA"), (OX, OY),
                    m.fw, m.fd, attach)


def _silhouette(alpha: np.ndarray) -> np.ndarray:
    filled = alpha > 0
    near = np.zeros_like(filled)
    near[:, :-1] |= filled[:, 1:]
    near[:, 1:] |= filled[:, :-1]
    near[:-1] |= filled[1:]
    near[1:] |= filled[:-1]
    return near & ~filled


def _outline(rgb: np.ndarray, alpha: np.ndarray, zbuf: np.ndarray, part: np.ndarray) -> np.ndarray:
    """Selective outlines, as on every other sprite: empty pixels next to the building take the neighbour's colour
    darkened towards #140f1a; inside, a pixel just behind the edge of a nearer part (a chimney against its roof,
    an eave against the wall) is darkened the same way, more lightly, so forms separate without a black grid."""
    out = rgb.copy()
    filled = alpha > 0
    H, W = alpha.shape
    done = np.zeros((H, W), bool)
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        ys = slice(max(0, -dy), H - max(0, dy))
        xs = slice(max(0, -dx), W - max(0, dx))
        ns = slice(max(0, dy), H - max(0, -dy) if dy < 0 else H)
        nxs = slice(max(0, dx), W - max(0, -dx) if dx < 0 else W)
        nb_rgb = rgb[ns, nxs]
        nb_fill = filled[ns, nxs]
        here_fill = filled[ys, xs]
        sil = ~here_fill & nb_fill & ~done[ys, xs]
        o = out[ys, xs]
        o[sil] = nb_rgb[sil] * 0.42 * 0.4 + OUT_RGB * 0.6
        inner = here_fill & nb_fill & (part[ns, nxs] != part[ys, xs]) & (zbuf[ns, nxs] > zbuf[ys, xs] + 5) \
            & ~done[ys, xs]
        o[inner] = o[inner] * 0.5 + (nb_rgb[inner] * 0.35 * 0.5 + OUT_RGB * 0.5) * 0.5
        out[ys, xs] = o
        done[ys, xs] |= sil | inner
    return out


# ------------------------------------------------------------------------------------------- face materials
Mat = tuple[str, str | None]
Decal = tuple[float, float, Mat]               # (a, b, decal): left edge and bottom in the face's own (a, b)


def _paste(out: np.ndarray, a: np.ndarray, b: np.ndarray, decals: list[Decal]) -> None:
    for da, db, dm in decals:
        d = tex_rgba(*dm)
        dh, dw = d.shape[:2]
        x = np.floor(a - da + 1e-4).astype(int)
        y = dh - 1 - np.floor(b - db + 1e-4).astype(int)
        m = (x >= 0) & (x < dw) & (y >= 0) & (y < dh)
        if not m.any():
            continue
        px = d[y[m], x[m]]
        al = px[:, 3:4] / 255.0
        out[m, :3] = out[m, :3] * (1 - al) + px[:, :3] * al
        out[m, 3] = np.maximum(out[m, 3], px[:, 3])


def wall(mat: Mat, along, *, z0: float = 0.0, decals: list[Decal] = (), u0: float = 0.0) -> Fn:
    """A wall texture, world-aligned: U = world distance along `along` (a unit vector), V = height above `z0`, one
    storey per period, with decals pasted in the face's (a, b)."""
    img = tex_rgba(*mat)
    H, W = img.shape[:2]
    t = np.array(along, float)

    def f(a, b, P):
        u = np.floor(P.dot(t) + u0 + 1e-4).astype(int)
        z = np.floor(P[:, 2] - z0 + 1e-4).astype(int)
        out = img[(H - 1 - z) % H, u % W].copy()
        _paste(out, a, b, list(decals))
        return out
    return f


def V_WALL(mat: Mat, **kw) -> Fn:
    """A +V face (screen left): U runs left to right."""
    return wall(mat, (1, 0, 0), **kw)


def U_WALL(mat: Mat, **kw) -> Fn:
    """A +U face (screen right): -V runs left to right, so decals read the right way round."""
    return wall(mat, (0, -1, 0), **kw)


def surface(mat: Mat, u_axis, v_of: Callable[[np.ndarray], np.ndarray], *, ridge: float = 2.5,
            eave: float | None = None) -> Fn:
    """A roof (or any top) texture: U along `u_axis`, V = `v_of(world)` (distance down the slope). A ridge cap of
    `ridge` texels and, at `eave`, a darker last course where the tiles kick out over the fascia."""
    img = tex_rgba(*mat)
    H, W = img.shape[:2]
    t = np.array(u_axis, float)

    def f(a, b, P):
        u = np.floor(P.dot(t) + 1e-4).astype(int)
        v = v_of(P)
        out = img[np.floor(v + 1e-4).astype(int) % H, u % W].copy()
        if ridge:
            out[:, :3] *= np.where(v < 1, 1.16, np.where(v < ridge, 0.9, 1.0))[:, None]
        if eave is not None:
            out[:, :3] *= np.where(v > eave - 1.5, 0.82, 1.0)[:, None]
        return out
    return f


def flat_top(mat: Mat) -> Fn:
    img = tex_rgba(*mat)
    H, W = img.shape[:2]

    def f(a, b, P):
        return img[np.floor(P[:, 1] + 1e-4).astype(int) % H, np.floor(P[:, 0] + 1e-4).astype(int) % W].copy()
    return f


def solid(ref: str, shade: int = 3, *, grain: bool = False, edge: int | None = None, length: float = 0.0) -> Fn:
    """One ramp shade, optionally with wood grain streaks along `a` and a lit top edge (`edge` texels)."""
    rp = [_hex_rgb(c) for c in _ref(ref)]

    def f(a, b, P):
        s = np.full(len(a), shade)
        if grain:
            g = (np.floor(b) * 7 + np.floor(a / 5) * 3).astype(int) % 5
            s = s + np.where(g == 0, -1, 0)
        if edge is not None:
            s = np.where(b > length - edge, shade + 1, s)
        rgb = np.array(rp)[np.clip(s, 0, 5)]
        return np.concatenate([rgb, np.full((len(a), 1), 255.0)], 1).astype(np.float32)
    return f


def image_fn(img: np.ndarray, *, scale: float = 1.0) -> Fn:
    """Paints a small RGBA image across a face's (a, b), bottom row at b = 0 (crate sides, notice boards)."""
    h, w = img.shape[:2]

    def f(a, b, P):
        x = np.clip(np.floor(a * scale).astype(int), 0, w - 1)
        y = np.clip(h - 1 - np.floor(b * scale).astype(int), 0, h - 1)
        return img[y, x].copy()
    return f


# --------------------------------------------------------------------------------------------- roof building
def gable(m: Model, x0, x1, y0, y1, z_eave, rise, *, axis: str, roof: Mat, gable_fn: Callable[..., Fn] | None,
          trim: str = "wood/dark", oh: float = 4, ohg: float = 3, thick: float = 3, fascia: Fn | None = None,
          gable_decals: list[Decal] = (), z0: float = 0.0, part: int | None = None,
          roof_fn: Fn | None = None, verge: bool = True) -> dict:
    """A pitched roof over the rectangle [x0, x1] x [y0, y1]: two slopes (eaves overhanging by `oh`, gables by
    `ohg`), the gable walls under them (`gable_fn(wall_maker, decals)`), fascia and verge boards `thick` deep.
    `axis` is the ridge direction: 'u' (slopes face +V, lit, and -V) or 'v' (slopes face +U,
    shaded, and -U). Returns the ridge geometry for chimneys and finials."""
    p = m.part() if part is None else part
    fas = fascia or solid(trim, 3, edge=1, length=thick)
    if axis == "u":
        half = (y1 - y0) / 2
        Vr, Zr = y0 + half, z_eave + rise
        th = math.atan2(rise, half)
        c, s = math.cos(th), math.sin(th)
        run = half + oh
        zl = Zr - run * math.tan(th)
        L = run / c
        xa, xb = x0 - ohg, x1 + ohg
        vf = lambda P: np.abs(P[:, 1] - Vr) / c  # noqa: E731
        m.poly([(xa, y1 + oh, zl), (xb, y1 + oh, zl), (xb, Vr, Zr), (xa, Vr, Zr)],
               roof_fn or surface(roof, (1, 0, 0), vf, eave=L), (0, s, c), part=p)
        m.poly([(xb, y0 - oh, zl), (xa, y0 - oh, zl), (xa, Vr, Zr), (xb, Vr, Zr)],
               roof_fn or surface(roof, (1, 0, 0), vf, eave=L), (0, -s, c), part=p)
        if gable_fn is not None:
            m.poly([(x1, y1, z_eave), (x1, y0, z_eave), (x1, Vr, Zr)], gable_fn(U_WALL, gable_decals),
                   (1, 0, 0), part=p)
            m.poly([(x0, y1, z_eave), (x0, y0, z_eave), (x0, Vr, Zr)], None, (-1, 0, 0), part=p)
        m.poly([(xa, y1 + oh, zl - thick), (xb, y1 + oh, zl - thick), (xb, y1 + oh, zl), (xa, y1 + oh, zl)],
               fas, (0, 1, 0), part=p)
        if verge:
            m.poly([(xb, y1 + oh, zl - thick), (xb, Vr, Zr - thick), (xb, y0 - oh, zl - thick), (xb, y0 - oh, zl),
                    (xb, Vr, Zr), (xb, y1 + oh, zl)], solid(trim, 3), (1, 0, 0), part=p)
        return {"ridge": ((xa, Vr, Zr), (xb, Vr, Zr)), "slope": (th, Vr, Zr)}
    half = (x1 - x0) / 2
    Ur, Zr = x0 + half, z_eave + rise
    th = math.atan2(rise, half)
    c, s = math.cos(th), math.sin(th)
    run = half + oh
    zl = Zr - run * math.tan(th)
    L = run / c
    ya, yb = y1 + ohg, y0 - ohg
    vf = lambda P: np.abs(P[:, 0] - Ur) / c  # noqa: E731
    m.poly([(x1 + oh, ya, zl), (x1 + oh, yb, zl), (Ur, yb, Zr), (Ur, ya, Zr)],
           roof_fn or surface(roof, (0, -1, 0), vf, eave=L), (s, 0, c), part=p)
    m.poly([(x0 - oh, yb, zl), (x0 - oh, ya, zl), (Ur, ya, Zr), (Ur, yb, Zr)],
           roof_fn or surface(roof, (0, -1, 0), vf, eave=L), (-s, 0, c), part=p)
    if gable_fn is not None:
        m.poly([(x0, y1, z_eave), (x1, y1, z_eave), (Ur, y1, Zr)], gable_fn(V_WALL, gable_decals), (0, 1, 0), part=p)
        m.poly([(x0, y0, z_eave), (x1, y0, z_eave), (Ur, y0, Zr)], None, (0, -1, 0), part=p)
    m.poly([(x1 + oh, ya, zl - thick), (x1 + oh, yb, zl - thick), (x1 + oh, yb, zl), (x1 + oh, ya, zl)],
           fas, (1, 0, 0), part=p)
    if verge:
        m.poly([(x0 - oh, ya, zl - thick), (Ur, ya, Zr - thick), (x1 + oh, ya, zl - thick), (x1 + oh, ya, zl),
                (Ur, ya, Zr), (x0 - oh, ya, zl)], solid(trim, 3), (0, 1, 0), part=p)
    return {"ridge": ((Ur, ya, Zr), (Ur, yb, Zr)), "slope": (th, Ur, Zr)}


def roof_z(g: dict, axis: str, u: float, v: float) -> float:
    """Height of a gable roof's surface over (u, v)."""
    th, r, zr = g["slope"]
    return zr - abs((v if axis == "u" else u) - r) * math.tan(th)


def chimney(m: Model, x0, x1, y0, y1, z0, z1, mat: Mat, *, smoke: bool = True) -> None:
    """A chimney stack with a cap course and a sooty flue; registers a smoke attach point at its mouth."""
    p = m.part()
    m.box(x0, x1, y0, y1, z0, z1, V_WALL(mat), None, side_u=U_WALL(mat), part=p)
    soot = solid("prop/soot", 1)
    m.box(x0 - 1, x1 + 1, y0 - 1, y1 + 1, z1, z1 + 3, solid("terrain/stone_dark", 3, edge=1, length=3),
          soot, part=p)
    if smoke:
        m.attach.setdefault("smoke", []).append(((x0 + x1) / 2, (y0 + y1) / 2, z1 + 3))


# ------------------------------------------------------------------------------------------------ buildings
def _win(night: bool, trim: str, shut: bool = False) -> Mat:
    return ("window_lit" if night else ("window_shuttered" if shut else "window"), trim)


def _gable_wall(mat: Mat, z0: float = 0.0):
    return lambda W, d: W(mat, z0=z0, decals=d)


def stripes(ref_a: str, ref_b: str, axis=(1, 0, 0), *, width: int = 6, shade: int = 3) -> Fn:
    """Awning canvas: alternating stripes along `axis`, with a darker lower hem."""
    ra = np.array([_hex_rgb(c) for c in _ref(ref_a)])
    rb = np.array([_hex_rgb(c) for c in _ref(ref_b)])
    t = np.array(axis, float)

    def f(a, b, P):
        k = np.floor(P.dot(t) / width + 1e-4).astype(int) % 2
        sh = np.where(b < 1.5, shade - 1, shade)
        sh = np.where((np.floor(P.dot(t) + 1e-4).astype(int) % width) == 0, sh + 1, sh)
        rgb = np.where(k[:, None] == 0, ra[np.clip(sh, 0, 5)], rb[np.clip(sh, 0, 5)])
        return np.concatenate([rgb, np.full((len(a), 1), 255.0)], 1).astype(np.float32)
    return f


def valance(m: Model, x0: float, x1: float, y: float, z_top: float, depth: float, fn: Fn, frame: int, *,
            part: int | None = None, frames: int = 4) -> None:
    """A scalloped cloth hem hanging off an awning's front edge (a +V face); it flutters over `frames`."""
    pts = []
    for k in range(int(x1 - x0) + 1):
        x = x0 + k
        tongue = 1 - abs(math.sin(math.pi * ((x - x0) % 6) / 6))
        flut = 0.9 * math.sin(2 * math.pi * frame / frames + (x - x0) * 0.45)
        pts.append((x, y, z_top - depth + 2.2 * tongue + flut))
    pts += [(x1, y, z_top), (x0, y, z_top)]
    m.poly(pts, fn, (0, 1, 0), part=part)


def joists(m: Model, x0, x1, y, z, *, axis: str = "u") -> None:
    """Joist ends poking out under a jettied upper storey."""
    p = m.part()
    f = solid("town/timber", 3, edge=1, length=3)
    k = x0 + 3
    while k + 3 <= x1:
        if axis == "u":
            m.box(k, k + 3, y - 3, y, z - 3, z, f, f, part=p, cast=False)
        else:
            m.box(y - 3, y, k, k + 3, z - 3, z, f, f, part=p, cast=False)
        k += 9


def cottage(v: dict, night: bool = False, frame: int = 0) -> Model:
    """A one-storey cottage (2x2): lime-washed walls, a deep thatch (or slate) roof with the ridge along U, a
    fieldstone chimney on the gable end."""
    m = Model(2, 2)
    walls, roof, trim = v["wall"], v["roof"], v["trim"]
    x0, x1, y0, y1, h = 3, 57, 5, 59, 70
    front = [(0, 0, ("door", trim)), (25, 0, _win(night, trim))]
    side = [(6, 0, _win(night, trim, shut=True))]
    if v.get("ivy"):
        front.append((40, 0, ("ivy", "green")))
    m.box(x0, x1, y0, y1, 0, h, V_WALL(walls, decals=front), None, side_u=U_WALL(walls, decals=side))
    thatched = roof[0] == "thatch"
    fascia = surface(roof, (1, 0, 0), lambda P: 120 - P[:, 2] * 2, ridge=0) if thatched else None
    g = gable(m, x0, x1, y0, y1, h, 22, axis="u", roof=roof, gable_fn=_gable_wall(walls), oh=6 if thatched else 4,
              ohg=4, thick=5 if thatched else 3, fascia=fascia)
    chimney(m, x1, x1 + 6, 14, 26, 0, roof_z(g, "u", 0, 20) + 18, ("fieldstone", "warm"))
    m.attach["door"] = [(x0 + 16, y1, 0)]
    return m


def house(v: dict, night: bool = False, frame: int = 0) -> Model:
    """A two-storey town house (2x2): a stone or rendered ground floor, a jettied timber-framed upper floor with
    joist ends showing, and a gable facing the street (ridge along V)."""
    m = Model(2, 2)
    base, upper, roof, trim = v["base"], v["wall"], v["roof"], v["trim"]
    m.box(4, 58, 4, 58, 0, 64, V_WALL(base, decals=[(0, 0, ("door", trim)), (25, 0, _win(night, trim))]), None,
          side_u=U_WALL(base, decals=[(12, 0, _win(night, trim, shut=True))]))
    joists(m, 4, 58, 61, 64)
    joists(m, 4, 58, 61, 64, axis="v")
    m.box(4, 61, 4, 61, 64, 128, V_WALL(upper, decals=[(-4, 0, _win(night, trim)), (28, 0, _win(night, trim))]),
          None, side_u=U_WALL(upper, decals=[(-3, 0, _win(night, trim)), (29, 0, _win(night, trim, shut=True))]))
    g = gable(m, 4, 61, 4, 61, 128, 24, axis="v", roof=roof, gable_fn=_gable_wall(upper), oh=4, ohg=4)
    chimney(m, 44, 54, 12, 24, 110, roof_z(g, "v", 49, 18) + 20, ("fieldstone", "grey"))
    m.attach["door"] = [(20, 58, 0)]
    return m


def tavern(v: dict, night: bool = False, frame: int = 0) -> Model:
    """The inn (3x2): fieldstone ground floor, jettied timber upper floor, a long clay roof (ridge along U), two
    chimneys, lamplit windows at night; its swinging sign is an overlay hung on the `sign` attach point."""
    m = Model(3, 2)
    base, upper, roof, trim = v["base"], v["wall"], v["roof"], v["trim"]
    w = _win(night, trim)
    m.box(3, 93, 4, 60, 0, 64, V_WALL(base, decals=[(-3, 0, w), (29, 0, ("door_open" if night else "door", trim)),
                                                    (61, 0, w)]), None,
          side_u=U_WALL(base, decals=[(12, 0, w), (34, 8, ("wall_lantern", "iron"))]))
    joists(m, 3, 93, 63, 64)
    joists(m, 4, 60, 96, 64, axis="v")
    m.box(3, 96, 4, 63, 64, 128, V_WALL(upper, decals=[(-3, 0, w), (29, 0, w), (61, 0, w)]), None,
          side_u=U_WALL(upper, decals=[(-1, 0, _win(night, trim, shut=True)), (31, 0, w)]))
    g = gable(m, 3, 96, 4, 63, 128, 25, axis="u", roof=roof, gable_fn=_gable_wall(upper), oh=5, ohg=4)
    chimney(m, 10, 20, 24, 36, 110, roof_z(g, "u", 15, 30) + 18, ("fieldstone", "warm"))
    chimney(m, 70, 80, 12, 22, 110, roof_z(g, "u", 75, 17) + 16, ("fieldstone", "warm"))
    m.attach["door"] = [(48, 60, 0)]
    m.attach["sign"] = [(14, 63, 110)]
    return m


def smithy(v: dict, night: bool = False, frame: int = 0) -> Model:
    """The smithy (2x2): a fieldstone workshop with its gable to the street, and an open lean-to on its +U side
    sheltering the forge: a stone hearth with glowing coals, a hood and a tall chimney, and an anvil."""
    m = Model(2, 2)
    walls, roof, trim = v["wall"], v["roof"], v["trim"]
    m.box(2, 38, 3, 60, 0, 64, V_WALL(walls, decals=[(2, 0, ("door_open", trim))]), None,
          side_u=U_WALL(walls, decals=[(0, 0, _win(night, trim, shut=True))]))
    gable(m, 2, 38, 3, 60, 64, 15, axis="v", roof=roof, gable_fn=_gable_wall(walls), oh=3, ohg=3)
    # lean-to: a shed roof from the wall down towards +U, on two posts
    p = m.part()
    post = solid("town/timber", 3, grain=True)
    for y in (24, 57):
        m.box(57, 60, y - 3, y, 0, 46, post, None, part=p)
    th = math.atan2(14, 24)
    m.poly([(62, 60, 44), (62, 18, 44), (38, 18, 58), (38, 60, 58)],
           surface(roof, (0, -1, 0), lambda P: (P[:, 0] - 38) / math.cos(th), ridge=0, eave=26), (math.sin(th), 0,
                                                                                                 math.cos(th)), part=p)
    m.poly([(62, 60, 41), (62, 18, 41), (62, 18, 44), (62, 60, 44)], solid("town/timber", 3, edge=1, length=3),
           (1, 0, 0), part=p)
    m.poly([(38, 60, 55), (62, 60, 41), (62, 60, 44), (38, 60, 58)], solid("town/timber", 3), (0, 1, 0), part=p)
    # hearth, hood and chimney
    stone = ("fieldstone", "grey")
    hp = m.box(40, 54, 22, 38, 0, 18, V_WALL(stone), solid("town/forge", 3), side_u=U_WALL(stone))
    glow = 1.12 if night else 1.0                                      # the coals glow, whatever the light
    m.faces[-3].shade = glow
    m.poly([(42, 38.3, 6), (52, 38.3, 6), (52, 38.3, 12), (50, 38.3, 15), (44, 38.3, 15), (42, 38.3, 12)],
           solid("town/forge", 4), (0, 1, 0), part=hp, shade=glow)
    m.poly([(43, 38.4, 6), (51, 38.4, 6), (51, 38.4, 9), (43, 38.4, 9)], solid("town/forge", 5), (0, 1, 0),
           part=hp, shade=glow)
    m.box(40, 54, 22, 38, 30, 40, V_WALL(stone), solid("terrain/stone_dark", 2), side_u=U_WALL(stone))
    chimney(m, 42, 52, 24, 34, 40, 118, stone)
    # anvil on a stump
    a = m.part()
    m.box(44, 50, 46, 51, 0, 8, solid("foliage/bark", 3, grain=True), solid("wood/pale", 4), part=a)
    iron = solid("metal/dark", 3, edge=1, length=4)
    m.box(42, 53, 45, 52, 8, 12, iron, solid("metal/dark", 4), part=a)
    m.blobs.append(Blob((54.5, 48.5, 11), 1.8, _ref("metal/dark"), part=a, squash=0.7))
    m.attach["door"] = [(20, 60, 0)]
    m.attach["light"] = [(47, 38, 10)]
    m.attach["sign"] = [(6, 60, 54)]
    return m


def store(v: dict, night: bool = False, frame: int = 0) -> Model:
    """The general store (2x2): timber-framed, a painted name board and a striped awning over the shopfront (it
    flutters, 4 frames), goods stacked under it."""
    m = Model(2, 2)
    walls, roof, trim, awn = v["wall"], v["roof"], v["trim"], v["awning"]
    w = _win(night, trim)
    m.box(3, 60, 3, 50, 0, 80, V_WALL(walls, decals=[(-3, 0, w), (19, 0, ("door", trim)), (31, 0, w)]), None,
          side_u=U_WALL(walls, decals=[(6, 0, _win(night, trim, shut=True)), (26, 8, ("ivy", "green"))]))
    gable(m, 3, 60, 3, 50, 80, 20, axis="u", roof=roof, gable_fn=_gable_wall(walls), oh=4, ohg=3)
    board = m.part()
    m.box(12, 48, 50, 51.5, 66, 78, image_fn(_shop_board(36, 12)), solid("wood/dark", 3), part=board)
    cloth = stripes(TRIMS[awn] if awn in TRIMS else awn, "town/canvas")
    ap = m.part()
    th = math.atan2(10, 12)
    m.poly([(2, 62, 54), (61, 62, 54), (61, 50, 64), (2, 50, 64)], cloth, (0, math.sin(th), math.cos(th)), part=ap)
    valance(m, 2, 61, 62, 54, 5, cloth, frame, part=ap)
    for x in (3, 58):
        m.box(x, x + 2, 60, 62, 0, 54, solid("town/timber", 3), None, part=ap)
    _crate(m, 6, 52, 0, 10, 8, 9)
    _crate(m, 7, 53, 9, 8, 6, 7)
    _barrel(m, 50, 56, 0, 4.5, 12)
    _basket(m, 39, 56, 0, 4.5, "prop/apple")
    m.attach["door"] = [(35, 50, 0)]
    return m


def _shop_board(w: int, h: int) -> np.ndarray:
    c = _canvas(w, h)
    c.rect(0, 0, w - 1, h - 1, "trim", 2)
    c.rect(1, 1, w - 2, h - 2, "alt", 3)
    c.rect(1, 1, w - 2, 1, "alt", 4)
    for x in range(4, w - 4, 3):                                       # lettering, suggested
        c.rect(x, 4, x + 1, 7, "metal", 4 if x % 2 else 5)
    return np.asarray(c.render(ramps(_v(trim="cloth/ochre", alt="wood/dark", metal="metal/gold")))).astype(
        np.float32)


def stall(v: dict, night: bool = False, frame: int = 0) -> Model:
    """A market stall (1x1), sized for the characters (the counter reaches their waist): four posts, a plank counter
    heaped with goods, a striped canvas canopy whose front hem flutters (4 frames)."""
    m = Model(1, 1)
    cloth = stripes(v["canopy"], "town/canvas", width=5)
    p = m.part()
    post = solid("town/timber", 3)
    for x, y, h in ((2, 5, 66), (28, 5, 66), (2, 30, 58), (28, 30, 58)):
        m.box(x, x + 2, y - 2, y, 0, h, post, None, part=p)
    gable(m, 1, 31, 3, 30, 58, 12, axis="u", roof=None, gable_fn=None, roof_fn=cloth, oh=3, ohg=2, thick=1,
          fascia=cloth, verge=False, part=m.part())
    valance(m, 0, 32, 33, 56.5, 5, cloth, frame, part=m.part())
    counter = m.part()
    planks = ("plank_shed", "oak")
    m.box(3, 29, 17, 29, 0, 22, V_WALL(planks), solid("wood/oak", 4, grain=True), side_u=U_WALL(planks),
          part=counter)
    _crate(m, 6, 5, 0, 12, 10, 12)
    _crate(m, 7, 6, 12, 10, 8, 9)
    goods = v["goods"]
    r = tex.rng(len(goods) * 7 + 3)
    for k in range(26):
        x = 5.5 + (k % 13) * 1.75 + r.uniform(-0.3, 0.3)
        y = 19.5 + (k // 13) * 5 + r.uniform(-0.6, 0.6)
        rad = 1.9 if goods != "prop/canvas" else 2.2
        m.blobs.append(Blob((x, y, 23.5 + (k // 13) * 0.6), rad, _ref(goods), part=counter + 100 + k % 3))
    return m


def chapel(v: dict, night: bool = False, frame: int = 0) -> Model:
    """The chapel (2x3): a dressed-stone nave with buttresses and lancet windows, a west front with a rose window
    facing the street, and a bell tower behind it with open belfry arches and a slate spire."""
    m = Model(2, 3)
    stone, roof = v["wall"], v["roof"]
    lan = ("lancet_lit" if night else "lancet", "stained")
    rose = ("rose_window_lit" if night else "rose_window", "stained")
    # tower
    tw = m.part()
    m.box(16, 48, 2, 34, 0, 176,
          V_WALL(stone, decals=[(0, 116, ("belfry", "bronze")), (0, 64, ("arrow_slit", "grey"))]), None,
          side_u=U_WALL(stone, decals=[(0, 116, ("belfry", "bronze")), (0, 80, ("arrow_slit", "grey"))]), part=tw)
    m.box(14, 50, 0, 36, 170, 176, solid("terrain/stone", 4, edge=1, length=6), None,
          side_u=solid("terrain/stone", 3, edge=1, length=6), part=tw)
    sp = m.part()
    apex = (32, 18, 228)
    sl = surface(roof, (1, 0, 0), lambda P: (228 - P[:, 2]) * 1.15, ridge=0)
    m.poly([(13, 37, 176), (51, 37, 176), apex], sl, (0, 0.82, 0.58), part=sp)
    m.poly([(51, 37, 176), (51, -1, 176), apex], surface(roof, (0, -1, 0), lambda P: (228 - P[:, 2]) * 1.15, ridge=0),
           (0.82, 0, 0.58), part=sp)
    m.poly([(13, -1, 176), (13, 37, 176), apex], None, (-0.82, 0, 0.58), part=sp)
    m.poly([(51, -1, 176), (13, -1, 176), apex], None, (0, -0.82, 0.58), part=sp)
    gold = solid("metal/gold", 4)
    m.box(31, 33, 17, 19, 226, 242, gold, gold, part=sp)
    m.box(27, 37, 17, 19, 235, 237, gold, gold, part=sp)
    # nave
    m.box(6, 58, 30, 92, 0, 88, V_WALL(stone, decals=[(10, 0, ("door", "oak")), (10, 56, ("rose_window", "stained") if
                                                                                not night else rose)]),
          None, side_u=U_WALL(stone, decals=[(0, 16, lan), (21, 16, lan), (40, 16, lan)]))
    gable(m, 6, 58, 30, 92, 88, 21, axis="v", roof=roof, gable_fn=_gable_wall(stone), oh=4, ohg=3)
    bp = m.part()
    for y in (90, 70, 50, 32):                                         # buttresses on the visible flank
        m.box(58, 62, y - 4, y, 0, 52, V_WALL(stone), solid("terrain/stone", 4), side_u=U_WALL(stone), part=bp)
        m.poly([(62, y, 52), (62, y - 4, 52), (58, y - 4, 60), (58, y, 60)], solid("terrain/stone", 3),
               (0.9, 0, 0.45), part=bp)
    m.attach["door"] = [(32, 92, 0)]
    return m


def watchtower(v: dict, night: bool = False, frame: int = 0) -> Model:
    """A stone watchtower (1x1, ten height steps): arrow slits, a corbelled, crenellated parapet and a slate
    cap inside it."""
    m = Model(1, 1)
    stone, roof = v["wall"], v["roof"]
    slit = ("arrow_slit", "grey")
    m.box(4, 28, 4, 28, 0, 150, V_WALL(stone, decals=[(-4, 0, ("door", v["trim"])), (-4, 64, slit), (-4, 112, slit)]),
          None, side_u=U_WALL(stone, decals=[(-4, 40, slit), (-4, 96, slit)]))
    cp = m.part()
    cor = solid("terrain/stone", 3, edge=1, length=3)
    for k in range(4, 28, 6):
        m.box(k, k + 3, 28, 30, 144, 150, cor, cor, part=cp, cast=False)
        m.box(28, 30, k, k + 3, 144, 150, cor, cor, part=cp, cast=False)
    m.box(2, 30, 2, 30, 150, 160, V_WALL(stone), flat_top(("flagstone", "grey")), side_u=U_WALL(stone), part=cp)
    _merlons(m, 2, 30, 2, 30, 160, 8, stone)
    apex = (16, 16, 196)
    m.poly([(6, 26, 160), (26, 26, 160), apex], surface(roof, (1, 0, 0), lambda P: (196 - P[:, 2]) * 1.1, ridge=0),
           (0, 0.87, 0.5), part=m.part())
    m.poly([(26, 26, 160), (26, 6, 160), apex], surface(roof, (0, -1, 0), lambda P: (196 - P[:, 2]) * 1.1, ridge=0),
           (0.87, 0, 0.5))
    m.poly([(6, 6, 160), (6, 26, 160), apex], None, (-0.87, 0, 0.5))
    m.poly([(26, 6, 160), (6, 6, 160), apex], None, (0, -0.87, 0.5))
    m.box(15, 17, 15, 17, 194, 204, solid("metal/gold", 4), solid("metal/gold", 3))
    m.attach["door"] = [(12, 28, 0)]
    m.attach["flag"] = [(16, 16, 204)]
    return m


def _merlons(m: Model, x0, x1, y0, y1, z, h, stone: Mat, *, period: int = 8, edges: str = "all") -> None:
    """Crenellations round a rectangle's rim: merlons `period` apart, half a period wide."""
    p = m.part()
    sw, su = V_WALL(stone), U_WALL(stone)
    top = solid("terrain/stone", 4)
    t = 3
    if edges in ("all", "u"):
        k = x0
        while k + period / 2 <= x1:
            for ya, yb in ((y1 - t, y1), (y0, y0 + t)):
                m.box(k, k + period / 2, ya, yb, z, z + h, sw, top, side_u=su, part=p)
            k += period
    if edges in ("all", "v"):
        k = y0
        while k + period / 2 <= y1:
            for xa, xb in ((x1 - t, x1), (x0, x0 + t)):
                m.box(xa, xb, k, k + period / 2, z, z + h, sw, top, side_u=su, part=p)
            k += period


def windmill(v: dict, night: bool = False, frame: int = 0) -> Model:
    """A tower mill (2x2): a tapering whitewashed tower, a shingled cap and four lattice sails that turn (8
    frames; the sails have four-fold symmetry, so a quarter turn loops)."""
    m = Model(2, 2)
    walls, roof, trim = v["wall"], v["roof"], v["trim"]
    b0, b1, t0, t1, H = 9, 55, 16, 48, 120
    wall_dec = [(7, 0, ("door", trim)), (7, 56, _win(night, trim, shut=True))]
    p = m.part()
    m.poly([(b0, b1, 0), (b1, b1, 0), (t1, t1, H), (t0, t1, H)], V_WALL(walls, decals=wall_dec),
           (0, H, b1 - t1), part=p)
    m.poly([(b1, b1, 0), (b1, b0, 0), (t1, t0, H), (t1, t1, H)],
           U_WALL(walls, decals=[(9, 30, _win(night, trim))]), (H, 0, b1 - t1), part=p)
    m.poly([(b0, b0, 0), (b1, b0, 0), (t1, t0, H), (t0, t0, H)], None, (0, -H, b1 - t1), part=p)
    m.poly([(b0, b1, 0), (b0, b0, 0), (t0, t0, H), (t0, t1, H)], None, (-H, 0, b1 - t1), part=p)
    gable(m, 13, 51, 13, 52, H, 17, axis="v", roof=roof, gable_fn=_gable_wall(("plank_shed", "oak"), z0=H), oh=3,
          ohg=3)
    hub = np.array([32.0, 59.0, 130.0])
    m.box(30, 34, 52, 58, 126, 134, solid("wood/dark", 3), solid("wood/dark", 4), side_u=solid("wood/dark", 2))
    m.blobs.append(Blob((32, 59, 130), 3.2, _ref("wood/dark"), part=m.part()))
    sails = m.part()
    phase = 2 * math.pi * frame / 32 + 0.35
    lattice = _sail_fn(hub, v["sail"], phase)
    for k in range(4):
        ang = phase + k * math.pi / 2
        d = np.array([math.cos(ang), 0, math.sin(ang)])
        q = np.array([-math.sin(ang), 0, math.cos(ang)])
        y = 60.0 + 0.01 * k

        def pt(r, s):
            w = hub + d * r + q * s
            return (w[0], y, w[2])
        m.poly(_ccw([pt(4, -1.4), pt(80, -1.4), pt(80, 1.4), pt(4, 1.4)]), solid("wood/dark", 3), (0, 1, 0),
               part=sails)
        m.poly(_ccw([pt(16, 1.4), pt(78, 1.4), pt(78, 15), pt(16, 12)]), lattice, (0, 1, 0), part=sails)
    m.attach["door"] = [(32, 55, 0)]
    return m


def _ccw(pts):
    """Orders a +V face's points from its screen-left bottom corner, so a rotating quad keeps a sane (a, b)."""
    i = min(range(len(pts)), key=lambda k: (pts[k][2] - 0.001 * pts[k][0]))
    pts = pts[i:] + pts[:i]
    if pts[1][0] < pts[0][0]:
        pts = [pts[0]] + pts[1:][::-1]
    return pts


def _sail_fn(hub: np.ndarray, cloth: str, phase: float) -> Fn:
    """Sail lattice: wooden bars on a grid in the sail's own frame, canvas between them."""
    wood = [_hex_rgb(c) for c in _ref("wood/oak")]
    can = [_hex_rgb(c) for c in _ref(cloth)]

    def f(a, b, P):
        rel = P - hub
        r = np.hypot(rel[:, 0], rel[:, 2])
        ang = np.arctan2(rel[:, 2], rel[:, 0]) - phase
        s = r * np.sin(ang - np.round(ang / (np.pi / 2)) * (np.pi / 2))
        bar = (np.floor(r) % 6 == 0) | (np.floor(np.abs(s)) % 4 == 0)
        rgb = np.where(bar[:, None], np.array(wood[3]), np.where((np.floor(r) % 6 == 1)[:, None], np.array(can[2]),
                                                                np.array(can[3])))
        return np.concatenate([rgb, np.full((len(a), 1), 255.0)], 1).astype(np.float32)
    return f


# ------------------------------------------------------------------------------------------ modular pieces
class Run:
    """Authors a modular piece once, along U, and places it along V too: (a, c) -> (U, V) is the identity for run
    'u' and a quarter turn, (c, L - a), for run 'v'. A rotation rather than a mirror, so lighting, shadows and the
    handedness of decals all stay right. Material specs are an Fn, a Mat, or (Mat, decals); a Mat becomes a +V or
    +U wall depending on where the face ends up."""

    def __init__(self, m: Model, run: str, length: float):
        self.m, self.u, self.L = m, run == "u", length

    def p(self, a, c, z):
        return (a, c, z) if self.u else (c, self.L - a, z)

    def n(self, na, nc, nz):
        return (na, nc, nz) if self.u else (nc, -na, nz)

    @staticmethod
    def fn(spec, n_world) -> Fn | None:
        if spec is None or callable(spec):
            return spec
        mat, decals = (spec[0], spec[1]) if isinstance(spec[0], tuple) else (spec, [])
        if abs(n_world[2]) > 0.9:
            return flat_top(mat)
        return V_WALL(mat, decals=decals) if abs(n_world[1]) > abs(n_world[0]) else U_WALL(mat, decals=decals)

    def poly(self, pts, spec, n, *, holes=(), **kw) -> int:
        wn = self.n(*n)
        return self.m.poly([self.p(*q) for q in pts], self.fn(spec, wn), wn,
                           holes=[[self.p(*q) for q in h] for h in holes], **kw)

    def box(self, a0, a1, c0, c1, z0, z1, long, end=None, top=None, **kw) -> int:
        end = long if end is None else end
        if self.u:
            return self.m.box(a0, a1, c0, c1, z0, z1, self.fn(long, (0, 1, 0)), self.fn(top, (0, 0, 1)),
                              side_u=self.fn(end, (1, 0, 0)), **kw)
        return self.m.box(c0, c1, self.L - a1, self.L - a0, z0, z1, self.fn(end, (0, 1, 0)), self.fn(top, (0, 0, 1)),
                          side_u=self.fn(long, (1, 0, 0)), **kw)

    def merlons(self, a0, a1, c0, c1, z, h, stone: Mat, *, period: int = 16) -> None:
        """Crenellations along both long edges; with period 16 and merlons at 2..10 they tile tile-to-tile."""
        p = self.m.part()
        top = solid("terrain/stone", 4)
        k = a0 + 2
        while k + period / 2 <= a1:
            for ca, cb in ((c1 - 3, c1), (c0, c0 + 3)):
                self.box(k, k + period / 2, ca, cb, z, z + h, stone, stone, top, part=p)
            k += period


def _arch(cx: float, z0: float, r: float, c: float, *, steps: int = 10, run: Run | None = None):
    """A round-headed opening's outline at depth c: straight jambs from z0 up to the springing, then a semicircle."""
    pts = [(cx - r, c, z0 - 40)]
    for k in range(steps + 1):
        t = math.pi - math.pi * k / steps
        pts.append((cx + r * math.cos(t), c, z0 + r * math.sin(t)))
    pts.append((cx + r, c, z0 - 40))
    return pts


def wall_piece(v: dict, night: bool = False, frame: int = 0, *, run: str = "u") -> Model:
    """A town-wall segment (1 tile): a 12-texel-thick ashlar curtain, five height steps, with a flagstone wall-walk
    and crenellations on both faces. Segments butt together tile to tile; the merlons keep their rhythm."""
    m = Model(1, 1)
    R = Run(m, run, 32)
    stone = v["wall"]
    R.box(0, 32, 10, 22, 0, 80, (stone, [(0, 20, ("arrow_slit", "grey"))]), stone, ("flagstone", "grey"))
    R.merlons(0, 32, 10, 22, 80, 10, stone)
    return m


def tower_piece(v: dict, night: bool = False, frame: int = 0) -> Model:
    """A corner tower (1 tile, seven height steps) that wall segments meet in either direction: a square shaft, a
    corbelled parapet with a flagstone fighting platform and merlons all round."""
    m = Model(1, 1)
    stone = v["wall"]
    slit = ("arrow_slit", "grey")
    m.box(3, 29, 3, 29, 0, 102, V_WALL(stone, decals=[(-3, 28, slit)]), None,
          side_u=U_WALL(stone, decals=[(-3, 52, slit)]))
    cp = m.part()
    cor = solid("terrain/stone", 3, edge=1, length=3)
    for k in range(5, 27, 6):
        m.box(k, k + 3, 29, 31, 96, 102, cor, cor, part=cp, cast=False)
        m.box(29, 31, k, k + 3, 96, 102, cor, cor, part=cp, cast=False)
    m.box(1, 31, 1, 31, 102, 112, V_WALL(stone), flat_top(("flagstone", "grey")), side_u=U_WALL(stone), part=cp)
    _merlons(m, 1, 31, 1, 31, 112, 9, stone, period=10)
    m.attach["flag"] = [(16, 16, 112)]
    return m


def gate_piece(v: dict, night: bool = False, frame: int = 0, *, run: str = "u") -> Model:
    """A gatehouse (3 tiles along its run): two flanking towers and an arched passage through the middle tile with
    the portcullis raised. Only the middle tile is walkable."""
    fw, fd = (3, 1) if run == "u" else (1, 3)
    m = Model(fw, fd)
    R = Run(m, run, 96)
    stone = v["wall"]
    slit = ("arrow_slit", "grey")
    for a0 in (0, 64):
        R.box(a0 + 1, a0 + 31, 3, 29, 0, 112, (stone, [(-1, 40, slit)]), stone, ("flagstone", "grey"))
        R.merlons(a0 + 1, a0 + 31, 3, 29, 112, 10, stone, period=10)
    mid = m.part()
    hole = _arch(48, 40, 11, 0)
    front = [(p[0], 26, p[2]) for p in hole]
    back = [(p[0], 6, p[2]) for p in hole]
    R.poly([(31, 26, 0), (65, 26, 0), (65, 26, 96), (31, 26, 96)], stone, (0, 1, 0), holes=[front], part=mid)
    R.poly([(65, 6, 0), (31, 6, 0), (31, 6, 96), (65, 6, 96)], None, (0, -1, 0), holes=[back], part=mid)
    R.poly([(31, 6, 96), (65, 6, 96), (65, 26, 96), (31, 26, 96)], ("flagstone", "grey"), (0, 0, 1), part=mid)
    R.merlons(31, 65, 6, 26, 96, 10, stone)
    dark = solid("terrain/stone_dark", 2)
    R.poly([(37, 26, 0), (37, 6, 0), (37, 6, 52), (37, 26, 52)], dark, (1, 0, 0), part=mid, shade=0.55)
    R.poly([(59, 6, 0), (59, 26, 0), (59, 26, 52), (59, 6, 52)], dark, (-1, 0, 0), part=mid, shade=0.55)
    R.poly([(37, 6, 51), (59, 6, 51), (59, 26, 51), (37, 26, 51)], None, (0, 0, -1), part=mid)
    bars = _portcullis_fn()
    R.poly([(37, 18, 34), (59, 18, 34), (59, 18, 52), (37, 18, 52)], bars, (0, 1, 0), part=m.part(), cast=False)
    return m


def _portcullis_fn() -> Fn:
    iron = [_hex_rgb(c) for c in _ref("metal/dark")]

    def f(a, b, P):
        bar = (np.floor(a) % 4 == 0) | (np.floor(b) % 5 == 0)
        rgb = np.where((np.floor(a) % 4 == 0)[:, None], np.array(iron[3]), np.array(iron[2]))
        alpha = np.where(bar | (b < 2), 255.0, 0.0)
        return np.concatenate([rgb, alpha[:, None]], 1).astype(np.float32)
    return f


def stairs_piece(v: dict, night: bool = False, frame: int = 0, *, run: str = "u", rise: int = 32) -> Model:
    """A flight of steps filling one tile, climbing `rise` texels (one or two height steps) towards -V (run 'u')
    or -U (run 'v'), in 4-texel risers. The open side shows the stepped profile."""
    m = Model(1, 1)
    R = Run(m, run, 32)
    n = rise // 4
    d = 32 / n
    side, top = v["side"], v["top"]
    p = m.part()
    for k in range(n):
        R.box(1, 31, 32 - (k + 1) * d, 32 - k * d, 0, (k + 1) * 4, side, side, top, part=p)
    return m


def bridge_piece(v: dict, night: bool = False, frame: int = 0, *, run: str = "u") -> Model:
    """One span of a stone arch bridge (1 tile): a cobbled deck at the origin's height (z = 0), parapets, and a
    round arch with voussoirs dropping 36 texels to the water. Spans repeat tile to tile along the run."""
    m = Model(1, 1, ground_shadow=False)
    R = Run(m, run, 32)
    stone = v["wall"]
    deck = m.part()
    R.poly([(0, 4, 0), (32, 4, 0), (32, 28, 0), (0, 28, 0)], v["deck"], (0, 0, 1), part=deck)
    for c in (28, 4):
        hole = [(p[0], c, p[2]) for p in _arch(16, -36, 12.5, c)[1:-1]]
        hole = [(3.5, c, -36.001)] + hole + [(28.5, c, -36.001)]
        R.poly([(0, c, -36), (32, c, -36), (32, c, 0), (0, c, 0)],
               _voussoirs(Run.fn(stone, R.n(0, 1, 0)), 16, 12.5) if c == 28 else None,
               (0, 1 if c == 28 else -1, 0), holes=[hole], part=deck)
    steps = 10
    for k in range(steps):
        t0, t1 = math.pi * k / steps, math.pi * (k + 1) / steps
        pa = (16 + 12.5 * math.cos(t0), -36 + 12.5 * math.sin(t0))
        pb = (16 + 12.5 * math.cos(t1), -36 + 12.5 * math.sin(t1))
        tm = (t0 + t1) / 2
        R.poly([(pa[0], 4, pa[1]), (pb[0], 4, pb[1]), (pb[0], 28, pb[1]), (pa[0], 28, pa[1])],
               solid("terrain/stone_dark", 2), (-math.cos(tm), 0, -math.sin(tm)), part=deck, shade=0.5)
    for c0 in (4, 25):
        R.box(0, 32, c0, c0 + 3, 0, 9, stone, stone, solid("terrain/stone", 4), part=m.part())
    return m


def _voussoirs(base: Fn, cx: float, r: float) -> Fn:
    """Wraps a spandrel's wall texture with a ring of wedge-shaped arch stones round the opening."""
    def f(a, b, P):
        out = base(a, b, P)
        d = np.hypot(a - cx, b)
        ring = (d >= r) & (d < r + 4.5) & (b > -1)
        ang = np.arctan2(b, a - cx)
        joint = (np.abs(((ang / math.pi * 9) % 1) - 0.5) > 0.42) | (d >= r + 3.5)
        out[ring, :3] *= np.where(joint[ring], 0.62, np.where(d[ring] < r + 1, 1.15, 1.04))[:, None]
        return out
    return f


def notice_piece(v: dict, night: bool = False, frame: int = 0, *, run: str = "u") -> Model:
    """A village notice board (1 tile): two posts, a board of pinned bills and wanted posters, a little shingled
    roof. Faces +V (run 'u') or +U (run 'v')."""
    m = Model(1, 1)
    R = Run(m, run, 32)
    post = solid("town/timber", 3, grain=True)
    p = m.part()
    for a in (4, 25):
        R.box(a, a + 3, 15, 18, 0, 60, post, post, solid("town/timber", 4), part=p)
    R.box(2, 30, 17, 19, 24, 52, image_fn(_board_img(), scale=26 / 28), solid("wood/oak", 2), solid("wood/oak", 4),
          part=p)
    if run == "u":
        gable(m, 1, 31, 12, 24, 59, 6, axis="u", roof=("shakes", "cedar"), gable_fn=None, oh=1, ohg=1, thick=2)
    else:
        gable(m, 12, 24, 1, 31, 59, 6, axis="v", roof=("shakes", "cedar"), gable_fn=None, oh=1, ohg=1, thick=2)
    return m


@lru_cache(maxsize=None)
def _board_img() -> np.ndarray:
    r = tex.rng(5)
    img = np.zeros((26, 26, 4), np.float32)
    wood = np.array([_hex_rgb(c) for c in _ref("wood/oak")])
    sh = 2 + (r.random((26, 26)) < 0.3)
    sh[0] = 4
    sh[:, 0] = 4
    sh[-1] = 1
    sh[:, -1] = 1
    img[..., :3] = wood[sh]
    img[..., 3] = 255
    n = tex_rgba("notices", "paper")
    sub = n[2:26, 3:27]
    al = sub[..., 3:4] / 255
    img[1:25, 1:25, :3] = img[1:25, 1:25, :3] * (1 - al) + sub[..., :3] * al
    return img


# -------------------------------------------------------------------------------------------- market clutter
@lru_cache(maxsize=None)
def _crate_img(w: int, h: int, top: bool = False) -> np.ndarray:
    """A crate side: a lit frame, horizontal boards inside, a diagonal brace (lids: boards only)."""
    c = _canvas(w, h)
    c.rect(0, 0, w - 1, h - 1, "alt", 3)
    for y in range(1, h - 1):
        c.rect(1, y, w - 2, y, "alt", 2 if y % 4 == 0 else 3)
    if not top:
        c.line(1, h - 2, w - 2, 1, "alt", 4)
        c.rect(0, 0, w - 1, 0, "alt", 4)
        c.rect(0, 0, 0, h - 1, "alt", 4)
        c.rect(0, h - 1, w - 1, h - 1, "alt", 1)
        c.rect(w - 1, 0, w - 1, h - 1, "alt", 2)
        for x, y in ((1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2)):
            c.px(x, y, "metal", 4)
    else:
        c.rect(0, 0, w - 1, 0, "alt", 4)
    return np.asarray(c.render(ramps(_v(alt="wood/pale", metal="metal/iron")))).astype(np.float32)


def _crate(m: Model, x: float, y: float, z: float, w: int, d: int, h: int) -> None:
    m.box(x, x + w, y, y + d, z, z + h, image_fn(_crate_img(w, h)), image_fn(_crate_img(w, d, True)),
          side_u=image_fn(_crate_img(d, h)))


def _cylinder(m: Model, cx, cy, z0, profile: list[tuple[float, float]], fn_side: Fn, fn_top: Fn | None, *,
              sides: int = 14, part: int | None = None) -> int:
    """A lathe shape from (radius, height) rings, bottom to top, as frustum quads (barrels, baskets, buckets)."""
    p = m.part() if part is None else part
    for (r0, h0), (r1, h1) in zip(profile, profile[1:]):
        for k in range(sides):
            t0, t1 = 2 * math.pi * k / sides, 2 * math.pi * (k + 1) / sides
            tm = (t0 + t1) / 2
            n = (math.cos(tm) * (h1 - h0), math.sin(tm) * (h1 - h0), r0 - r1)
            # screen-left first: of the two corners, the one with the larger V - U
            q = [(cx + r0 * math.cos(t0), cy + r0 * math.sin(t0), z0 + h0),
                 (cx + r0 * math.cos(t1), cy + r0 * math.sin(t1), z0 + h0),
                 (cx + r1 * math.cos(t1), cy + r1 * math.sin(t1), z0 + h1),
                 (cx + r1 * math.cos(t0), cy + r1 * math.sin(t0), z0 + h1)]
            if (q[0][0] - q[0][1]) > (q[1][0] - q[1][1]):
                q = [q[1], q[0], q[3], q[2]]
            vis = np.dot(n, VIEW) > 0
            m.poly(q, fn_side if vis else None, n, part=p)
    r, h = profile[-1]
    ring = [(cx + r * math.cos(2 * math.pi * k / sides), cy + r * math.sin(2 * math.pi * k / sides), z0 + h)
            for k in range(sides)]
    m.poly(ring, fn_top, (0, 0, 1), part=p)
    return p


def _barrel_fn(z0: float, h: float) -> Fn:
    wood = np.array([_hex_rgb(c) for c in _ref("wood/oak")])
    iron = np.array([_hex_rgb(c) for c in _ref("metal/iron")])

    def f(a, b, P):
        z = P[:, 2] - z0
        hoop = ((z > 1.5) & (z < 3)) | ((z > h - 3.5) & (z < h - 2))
        stave = np.where(a < 1, 2, np.where(a > 2.6, 2, 3))
        rgb = np.where(hoop[:, None], iron[np.where(z % 1.5 < 0.75, 4, 3)], wood[stave])
        return np.concatenate([rgb, np.full((len(a), 1), 255.0)], 1).astype(np.float32)
    return f


def _barrel(m: Model, cx: float, cy: float, z: float, r: float, h: float) -> None:
    prof = [(r * 0.84, 0), (r, h * 0.5), (r * 0.84, h)]
    _cylinder(m, cx, cy, z, prof, _barrel_fn(z, h), solid("wood/pale", 3, grain=True))


def _basket(m: Model, cx: float, cy: float, z: float, r: float, goods: str) -> None:
    wick = [_hex_rgb(c) for c in _ref("prop/hay")]

    def weave(a, b, P):
        s = np.where((np.floor(a) + np.floor(b / 1.5)) % 2 == 0, 3, 2)
        s = np.where(b > 5, 4, s)
        rgb = np.array(wick)[s]
        return np.concatenate([rgb, np.full((len(a), 1), 255.0)], 1).astype(np.float32)
    p = _cylinder(m, cx, cy, z, [(r * 0.8, 0), (r, 6)], weave, solid("prop/hay", 1), sides=12)
    col = _ref(goods)
    for k, (dx, dy, dz) in enumerate(((-1.6, -1.2, 0), (1.4, -1.4, 0), (-1.2, 1.6, 0), (1.8, 1.2, 0),
                                      (0, 0, 1.8), (0.2, -2.6, 0.2))):
        m.blobs.append(Blob((cx + dx * r / 4.5, cy + dy * r / 4.5, z + 6 + dz), 2.2, col, part=p + 200 + k % 2))


def clutter(v: dict, night: bool = False, frame: int = 0) -> Model:
    """Market clutter for one tile: `kind` crates (stacked crates and a sack), barrels (a cluster and a keg) or
    produce (baskets of fruit and greens round a crate)."""
    m = Model(1, 1)
    kind = v["kind"]
    if kind == "crates":
        _crate(m, 2, 11, 0, 17, 17, 16)
        _crate(m, 19, 14, 0, 12, 12, 12)
        _crate(m, 4, 13, 16, 13, 13, 12)
        m.blobs.append(Blob((22, 6, 5), 5.5, _ref("prop/canvas"), part=m.part(), squash=0.85))
    elif kind == "table":
        top = solid("wood/oak", 4, grain=True)
        leg = solid("wood/dark", 3)
        p = m.part()
        for x, y in ((6, 9), (24, 9), (6, 24), (24, 24)):
            m.box(x, x + 2, y, y + 2, 0, 18, leg, None, part=p)
        m.box(4, 28, 7, 27, 18, 21, solid("wood/oak", 2), top, part=p)
        for y0 in (1, 29):                                             # benches
            m.box(5, 27, y0, y0 + 3, 9, 11, solid("wood/oak", 2), solid("wood/oak", 4), part=m.part())
            for x in (7, 24):
                m.box(x, x + 2, y0, y0 + 3, 0, 9, leg, None, part=m.part())
        for x, y, ref in ((10, 12, "metal/iron"), (20, 18, "metal/iron"), (15, 22, "metal/bronze")):
            _cylinder(m, x, y, 21, [(1.8, 0), (1.8, 4)], solid(ref, 3), solid("prop/straw", 5), sides=8)
        m.blobs.append(Blob((22, 11, 22.5), 2.4, _ref("prop/hay"), part=m.part(), squash=0.7))
    elif kind == "barrels":
        for cx, cy, r, h in ((9, 9, 7, 20), (23, 11, 7, 20), (11, 23, 7, 20), (24, 24, 5, 12)):
            _barrel(m, cx, cy, 0, r, h)
    else:
        _crate(m, 7, 4, 0, 15, 12, 12)
        _basket(m, 9, 22, 0, 6.5, "prop/apple")
        _basket(m, 23, 20, 0, 6.5, "foliage/leaf_light")
        _basket(m, 24, 7, 0, 5.5, "town/thatch")
    return m


# -------------------------------------------------------------------------------------------------- overlays
def sign_model(emblem: str, frame: int, frames: int = 8) -> Model:
    """A projecting inn sign for the `sign` attach point: an iron arm out of the wall (+V) and a painted board
    swinging about it. The origin is the arm's root on the wall."""
    m = Model(0, 0, ground_shadow=False)
    iron = solid("metal/dark", 3, edge=1, length=2)
    m.box(-1, 1, 0, 22, -1, 1, iron, solid("metal/dark", 4), part=m.part(), cast=False)
    m.poly([(0, 0, -8), (0, 0, -1), (0, 10, -1)], None, (1, 0, 0))
    phi = 0.28 * math.sin(2 * math.pi * frame / frames)

    def rot(vv, dz):
        return (-dz * math.sin(phi), vv, -1 + dz * math.cos(phi))
    img = _sign_img(emblem)
    p = m.part()
    m.poly([rot(19, -15), rot(3, -15), rot(3, -3), rot(19, -3)], image_fn(img), (math.cos(phi), 0, math.sin(phi)),
           part=p, cast=False)
    for vv in (5, 17):
        m.poly([rot(vv + 0.6, -3), rot(vv - 0.6, -3), rot(vv - 0.6, 0), rot(vv + 0.6, 0)], solid("metal/iron", 2),
               (1, 0, 0), part=p, cast=False)
    return m


@lru_cache(maxsize=None)
def _sign_img(emblem: str) -> np.ndarray:
    c = _canvas(16, 12)
    c.rect(0, 0, 15, 11, "trim", 2)
    c.rect(1, 1, 14, 10, "alt", 3)
    c.rect(1, 1, 14, 1, "alt", 4)
    for ey, row in enumerate(EMBLEMS[emblem]):
        for ex, ch in enumerate(row):
            if ch == "#":
                c.px(4 + ex, 2 + ey, "metal", 5 if ey < 2 else 4)
    return np.asarray(c.render(ramps(_v(trim="cloth/crimson", alt="wood/pale", metal="metal/gold")))).astype(
        np.float32)


def smoke_frames(frames: int = 8, seed: int = 801) -> list[Image.Image]:
    """Chimney smoke for the `smoke` attach points: puffs rising, swelling, drifting right and thinning out. Four
    puffs at evenly spaced phases of one life cycle, so the loop is seamless."""
    from .props import blob
    sl = SlotTable()
    sl.id("smoke")
    pal = {"smoke": ramp("#b4b0be", shift=0.04, spread=0.7)}
    r = tex.rng(seed)
    wob = r.uniform(0, 2 * math.pi, 4)
    out = []
    for f in range(frames):
        c = Canvas(32, 64, sl)
        for i in range(4):
            t = ((i + f / frames) / 4) % 1.0
            y = 59 - t * 52
            x = 12 + t * 9 + math.sin(wob[i] + t * 5) * 1.6
            rad = 2.4 + t * 5.6
            blob(c, "smoke", x, y, rad, rad * 0.85, k=1.4, base=3.4)
        for t_y in range(64):                                           # thin out as it rises: dithered coverage
            t = 1 - t_y / 64
            for x in range(32):
                if c.slot[t_y, x] and ((t > 0.55 and (x + t_y) % 2) or (t > 0.8 and (x * 3 + t_y) % 4)):
                    c.slot[t_y, x] = 0
        out.append(c.render(pal))
    return out


# ------------------------------------------------------------------------------------------------- catalogue
@dataclass
class Building:
    id: str
    make: Callable[..., Model]
    variants: dict[str, dict]
    frames: int = 1
    fps: float = 0.0
    blocks: list[list[int]] | None = None        # per footprint tile, rows = j (V), columns = i (U); default all
    cover: str = "full"
    walkable: bool = False
    pair: str | None = None                       # the same piece along the other axis
    tags: list[str] = field(default_factory=list)
    extra: dict = field(default_factory=dict)


_TRIMMED = {"red": "red", "blue": "blue", "green": "green"}
_STONE = {"grey": dict(wall=("dressed_stone", "grey")), "warm": dict(wall=("dressed_stone", "warm"))}

BUILDINGS: list[Building] = [
    Building("cottage", cottage, {
        "thatch_red": dict(wall=("whitewash", "white"), roof=("thatch", "fresh"), trim="red", ivy=True),
        "thatch_green": dict(wall=("whitewash", "ochre"), roof=("thatch", "old"), trim="green"),
        "slate_blue": dict(wall=("fieldstone", "grey"), roof=("slate", "blue"), trim="blue"),
        "shakes_oak": dict(wall=("log_wall", "pine"), roof=("shakes", "cedar"), trim="oak"),
    }, tags=["home"]),
    Building("house", house, {
        "red": dict(base=("fieldstone", "warm"), wall=("timber_frame", "white"), roof=("clay_tiles", "red"),
                    trim="red"),
        "blue": dict(base=("whitewash", "white"), wall=("timber_frame", "ochre"), roof=("slate", "blue"),
                     trim="blue"),
        "green": dict(base=("fieldstone", "grey"), wall=("timber_frame", "rose"), roof=("slate", "green"),
                      trim="green"),
    }, tags=["home"]),
    Building("tavern", tavern, {
        "red": dict(base=("fieldstone", "warm"), wall=("timber_frame", "white"), roof=("clay_tiles", "red"),
                    trim="red"),
        "green": dict(base=("fieldstone", "grey"), wall=("timber_frame", "ochre"), roof=("slate", "green"),
                      trim="green"),
    }, tags=["shop", "inn"], extra={"sign": "sign_mug"}),
    Building("smithy", smithy, {
        "oak": dict(wall=("fieldstone", "grey"), roof=("shakes", "cedar"), trim="oak"),
        "slate": dict(wall=("fieldstone", "warm"), roof=("slate", "blue"), trim="red"),
    }, tags=["shop", "light"], extra={"sign": "sign_anvil"}),
    Building("store", store, {
        "red": dict(wall=("timber_frame", "white"), roof=("clay_tiles", "red"), trim="red", awning="cloth/crimson"),
        "blue": dict(wall=("timber_frame", "ochre"), roof=("slate", "blue"), trim="blue", awning="cloth/steel_blue"),
        "green": dict(wall=("timber_frame", "rose"), roof=("shakes", "cedar"), trim="green", awning="cloth/forest"),
    }, frames=4, fps=6, tags=["shop"]),
    Building("stall", stall, {
        "red": dict(canopy="cloth/crimson", goods="prop/apple"),
        "blue": dict(canopy="cloth/steel_blue", goods="metal/silver"),
        "green": dict(canopy="cloth/forest", goods="foliage/leaf_light"),
        "yellow": dict(canopy="cloth/ochre", goods="town/thatch"),
        "purple": dict(canopy="cloth/royal_purple", goods="prop/canvas"),
    }, frames=4, fps=6, cover="half", tags=["market"]),
    Building("chapel", chapel, {"grey": dict(wall=("dressed_stone", "grey"), roof=("slate", "blue")),
                                "warm": dict(wall=("dressed_stone", "warm"), roof=("slate", "green"))},
             tags=["temple"]),
    Building("watchtower", watchtower, {"grey": dict(wall=("dressed_stone", "grey"), roof=("slate", "blue"),
                                                     trim="oak"),
                                        "warm": dict(wall=("fieldstone", "warm"), roof=("shakes", "cedar"),
                                                     trim="red")}, tags=["defence"]),
    Building("windmill", windmill, {
        "white": dict(wall=("whitewash", "white"), roof=("shakes", "cedar"), trim="red", sail="town/canvas"),
        "stone": dict(wall=("fieldstone", "grey"), roof=("thatch", "old"), trim="green", sail="cloth/ivory"),
    }, frames=8, fps=8, tags=["farm"]),
    Building("wall_u", wall_piece, _STONE, pair="wall_v", tags=["defence", "modular"], extra={"run": "u"}),
    Building("wall_v", lambda v, n=False, f=0: wall_piece(v, n, f, run="v"), _STONE, pair="wall_u",
             tags=["defence", "modular"], extra={"run": "v"}),
    Building("wall_tower", tower_piece, _STONE, tags=["defence", "modular"]),
    Building("gate_u", gate_piece, _STONE, blocks=[[1, 0, 1]], pair="gate_v", tags=["defence", "modular"],
             extra={"run": "u"}),
    Building("gate_v", lambda v, n=False, f=0: gate_piece(v, n, f, run="v"), _STONE, blocks=[[1], [0], [1]],
             pair="gate_u", tags=["defence", "modular"], extra={"run": "v"}),
    *[Building(f"{name}_{run}", (lambda v, n=False, f=0, run=run, rise=rise: stairs_piece(v, n, f, run=run, rise=rise)),
               {"stone": dict(side=("dressed_stone", "grey"), top=("flagstone", "grey")),
                "wood": dict(side=("plank_shed", "oak"), top=("floorboards", "oak"))},
               cover="none", walkable=True, pair=f"{name}_{'v' if run == 'u' else 'u'}", tags=["modular"],
               extra={"run": run, "rise": rise // STEP, "climbs": "-V" if run == "u" else "-U"})
      for name, rise in (("steps", 16), ("stairs", 32)) for run in ("u", "v")],
    *[Building(f"bridge_{run}", (lambda v, n=False, f=0, run=run: bridge_piece(v, n, f, run=run)),
               {"grey": dict(wall=("dressed_stone", "grey"), deck=("cobble", "grey")),
                "warm": dict(wall=("dressed_stone", "warm"), deck=("cobble", "warm"))},
               cover="none", walkable=True, pair=f"bridge_{'v' if run == 'u' else 'u'}", tags=["modular"],
               extra={"run": run, "drop": 36}) for run in ("u", "v")],
    *[Building(f"notice_board_{run}", (lambda v, n=False, f=0, run=run: notice_piece(v, n, f, run=run)),
               {"oak": {}}, cover="half", pair=f"notice_board_{'v' if run == 'u' else 'u'}", tags=["market"],
               extra={"run": run}) for run in ("u", "v")],
    Building("clutter_crates", clutter, {"oak": dict(kind="crates")}, cover="half", tags=["market"]),
    Building("clutter_barrels", clutter, {"oak": dict(kind="barrels")}, cover="half", tags=["market"]),
    Building("clutter_produce", clutter, {"mixed": dict(kind="produce")}, cover="half", tags=["market"]),
    Building("tavern_table", clutter, {"oak": dict(kind="table")}, cover="half", tags=["interior"]),
]
_BUILDING = {b.id: b for b in BUILDINGS}


@lru_cache(maxsize=None)
def render_frames(bid: str, variant: str | None = None, night: bool = False) -> tuple[Rendered, ...]:
    """Every frame of one building variant, in one shared frame (size and origin)."""
    b = _BUILDING[bid]
    v = b.variants[variant or next(iter(b.variants))]
    models = [b.make(v, night, f) for f in range(b.frames)]
    bounds = union(model_bounds(md) for md in models)
    if night:                                    # day and night share a frame too, so the lights overlay lines up
        bounds = union([bounds] + [model_bounds(b.make(v, False, f)) for f in range(b.frames)])
    else:
        bounds = union([bounds] + [model_bounds(b.make(v, True, f)) for f in range(b.frames)])
    return tuple(render(md, bounds=bounds) for md in models)


def render_building(bid: str, variant: str | None = None, *, night: bool = False, frame: int = 0) -> Rendered:
    return render_frames(bid, variant, night)[frame % _BUILDING[bid].frames]


# ------------------------------------------------------------------------------------------- diorama placement
def place(r: Rendered, i0: int, j0: int, *, ground: Callable[[int, int], bool] | None = None, lift: int = 0
          ) -> tuple[list[iso.Sprite], list[iso.Sprite]]:
    """Stands a pre-rendered building on the map at footprint corner (i0, j0) for arcpix.iso: the image is cut
    into 32 px screen columns, each depth-sorted at the frontmost footprint tile in that column, so units walk in
    front of and behind multi-tile buildings correctly (the game's sprite sorter does the same). Also returns the
    ground shadow cut per tile (only onto tiles `ground(i, j)` accepts: present and level with the building), to
    draw before the units on those tiles. `lift` raises it by that many texels (a bridge deck over sunken water).
    Returns (shadows, strips)."""
    img = r.image
    W, H = img.size
    ox, oy = r.origin
    fw, fd = r.fw, r.fd
    strips = []
    for c in range(-fd, fw):
        x0 = 0 if c == -fd else ox + TILE * c
        x1 = W if c == fw - 1 else ox + TILE * (c + 1)
        x0, x1 = max(0, x0), min(W, x1)
        if x1 <= x0:
            continue
        a, b = max(((a, b) for a in range(fw) for b in range(fd) if a - b in (c, c + 1)), key=lambda t: sum(t))
        U, V = TILE * a + TILE // 2, TILE * b + TILE // 2
        strips.append(iso.Sprite(img.crop((x0, 0, x1, H)), (U - V + ox - x0, (U + V) // 2 + oy + lift), i0 + a,
                                 j0 + b, scale=1, shadow=False))
    shadows = []
    if ground is not None:
        sh = np.asarray(r.shadow)
        ys, xs = np.nonzero(sh[..., 3])
        if len(ys):
            sx, sy = xs + 0.5 - ox, ys + 0.5 - oy
            ci = np.floor((sy + sx / 2) / TILE).astype(int)
            cj = np.floor((sy - sx / 2) / TILE).astype(int)
            for a, b in sorted(set(zip(ci.tolist(), cj.tolist()))):
                if not ground(i0 + a, j0 + b):
                    continue
                sel = (ci == a) & (cj == b)
                piece = np.zeros_like(sh)
                piece[ys[sel], xs[sel]] = sh[ys[sel], xs[sel]]
                U, V = TILE * a + TILE // 2, TILE * b + TILE // 2
                shadows.append(iso.Sprite(Image.fromarray(piece, "RGBA"), (U - V + ox, (U + V) // 2 + oy),
                                          i0 + a, j0 + b, scale=1, shadow=False))
    return shadows, strips


def overlay(r: Rendered, i0: int, j0: int, point: tuple[int, int], img: Image.Image,
            anchor: tuple[int, int]) -> iso.Sprite:
    """An overlay (smoke, a swinging sign) pinned to a building's attach pixel, sorted just after the building."""
    ox, oy = r.origin
    a, b = r.fw - 1, r.fd - 1
    U, V = TILE * a + TILE // 2, TILE * b + TILE // 2
    cx, cy = U - V + ox, (U + V) // 2 + oy
    return iso.Sprite(img, (anchor[0] - (point[0] - cx), anchor[1] - (point[1] - cy)), i0 + a, j0 + b, scale=1,
                      shadow=False)


@lru_cache(maxsize=None)
def overlay_frames(oid: str) -> tuple[tuple[Image.Image, ...], tuple[int, int]]:
    """(frames, anchor) of an overlay: `smoke`, or `sign_<emblem>` (anchor = the arm's root on the wall)."""
    if oid == "smoke":
        return tuple(smoke_frames()), (12, 62)
    emblem = oid.split("_", 1)[1]
    models = [sign_model(emblem, f) for f in range(8)]
    bounds = union(model_bounds(md, 2) for md in models)
    rs = [render(md, bounds=bounds) for md in models]
    return tuple(x.image for x in rs), rs[0].origin


# --------------------------------------------------------------------------------------------------- dioramas
MARKET_TERRAIN = ["gggggggggggggggg",
                  "gggggggggggggggg",
                  "ggggggppppppgggg",
                  "ggppcccccccccpgg",
                  "ggpcccccccccccpg",
                  "ggpccccffcccccpg",
                  "ggpcccffffccccpg",
                  "ggpccccffcccccpg",
                  "ggpcccccccccccpg",
                  "ggpcccccccccccpg",
                  "ggppcccccccccppg",
                  "gggggggppggggggg",
                  "gggggggppggggggg"]

# (building, variant, i0, j0): footprint back corners on the map above
MARKET_BUILDINGS = [
    ("windmill", "white", 0, 0), ("chapel", "grey", 4, 0), ("tavern", "red", 7, 0), ("store", "blue", 11, 0),
    ("watchtower", "grey", 14, 0), ("house", "blue", 0, 3), ("cottage", "thatch_red", 0, 6), ("smithy", "oak", 0, 9),
    ("house", "green", 14, 3), ("cottage", "slate_blue", 14, 6),
    ("stall", "red", 4, 4), ("stall", "green", 4, 8), ("stall", "blue", 11, 4), ("stall", "yellow", 10, 8),
    ("clutter_crates", "oak", 12, 6), ("clutter_barrels", "oak", 3, 6), ("clutter_produce", "mixed", 14, 9),
    ("notice_board_u", "oak", 10, 2),
]


def market_scene(frame: int = 0, *, night: bool = False, dark_glow: bool = False) -> iso.Scene:
    """Emberwick's market square: the chapel, inn and general store along the back, houses, a cottage and the
    smithy down the left, a watchtower and more houses on the right; stalls, a well, lamps and banners in the
    square, townsfolk going about their business and the party gathered round the mayor."""
    from . import dioramas as D
    mats = {
        "g": D.Mat(("grass", "green"), ("dirt_side", "brown"), ("grass_lip", "green")),
        "c": D.Mat(("cobble", "grey"), ("dirt_side", "brown")),
        "f": D.Mat(("flagstone", "warm"), ("dirt_side", "brown")),
        "p": D.Mat(("path", None), ("dirt_side", "brown")),
    }
    heights = ["2" * 16] * len(MARKET_TERRAIN)
    s = D.build_scene(mats, MARKET_TERRAIN, heights, frame=frame, seed=17,
                      decals={"g": (0.18, D.FLOWERS), "p": (0.3, [("pebbles_0", None), ("pebbles_1", None)]),
                              "c": (0.04, [("leaves_0", "autumn")])})
    shadows, sprites = [], []
    for bid, var, i0, j0 in MARKET_BUILDINGS:
        b = _BUILDING[bid]
        r = render_building(bid, var, night=night, frame=frame % b.frames)
        sh, st = place(r, i0, j0, ground=lambda i, j, z=s.top_z(i0, j0): (i, j) in s.cells and s.top_z(i, j) == z)
        shadows += sh
        sprites += st
        for pt in r.attach.get("smoke", []):
            fr, anc = overlay_frames("smoke")
            sprites.append(overlay(r, i0, j0, pt, fr[frame % len(fr)], anc))
        if "sign" in b.extra and "sign" in r.attach:
            fr, anc = overlay_frames(b.extra["sign"])
            sprites.append(overlay(r, i0, j0, r.attach["sign"][0], fr[frame % len(fr)], anc))
    lamp = "lamp_post"
    ik = (frame // 2) % 2
    sprites += [
        D.prop("well", 7, 6), D.prop(lamp, 3, 3), D.prop(lamp, 13, 3), D.prop(lamp, 3, 10), D.prop(lamp, 12, 10),
        D.prop("banner", 6, 2, "crimson", frame=frame), D.prop("banner", 13, 2, "navy", frame=frame),
        D.prop("tree", 1, 12, "autumn"), D.prop("tree", 15, 10, "light"), D.prop("tree", 6, 0, "light"),
        D.prop("bush", 4, 12), D.prop("bush", 11, 12, "autumn"), D.prop("bush", 15, 11),
        D.prop("barrel", 10, 1), D.prop("haystack", 2, 2), D.prop("tall_grass", 3, 1), D.prop("sack", 2, 3),
        D.unit("mayor", 7, 8, k=ik), D.unit("kara", 5, 9, "back", k=1 - ik), D.unit("aldric", 6, 10, "back", k=ik),
        D.unit("ilyra", 8, 9, "back", mirror=True, k=1 - ik), D.unit("wren", 4, 10, "back", k=ik),
        D.unit("tamsin", 9, 10, "back", mirror=True, k=1 - ik), D.unit("sefa", 7, 11, "back", k=ik),
        D.unit("merchant", 4, 3, k=ik), D.unit("farmer", 11, 3, mirror=True, k=1 - ik),
        D.unit("farmhand", 5, 7, k=1 - ik), D.unit("fisher", 10, 7, mirror=True, k=ik),
        D.unit("innkeeper", 8, 2, k=ik), D.unit("barmaid", 9, 2, mirror=True, k=1 - ik),
        D.unit("priest", 5, 3, k=ik), D.unit("blacksmith", 2, 10, mirror=True, k=1 - ik),
        D.unit("guard", 13, 1, mirror=True, k=ik), D.unit("guard_captain", 13, 5, mirror=True, k=1 - ik),
        D.unit("child", 9, 6, k=ik), D.unit("noble", 11, 7, mirror=True, k=1 - ik), D.unit("sage", 2, 5, k=ik),
        D.unit("minstrel", 6, 4, k=1 - ik), D.unit("stranger", 13, 10, mirror=True, k=ik),
        D.unit("hermit", 2, 1, k=1 - ik),
    ]
    s.sprites = shadows + sprites
    return s


def _prop_img(pid: str, variant: str | None, frame: int = 0, **override: str) -> Image.Image:
    """A props.py sprite with some slots re-pointed (an unlit lamp for the dusk pass's day half)."""
    from . import props as PR
    p = next(x for x in PR.CATALOGUE if x.id == pid)
    v = dict(p.variants[variant] if variant else next(iter(p.variants.values())))
    v.update(override)
    fr = p.frames()
    return fr[frame % len(fr)].render(PR.ramps(v))


DIORAMA_MARGIN = 150


def render_market(frame: int = 0, *, sky: str = "day", night: bool = False, unlit: bool = False) -> Image.Image:
    """The market diorama over a sky backdrop, cropped to the scene. `unlit` swaps the lamp-post lanterns for dark
    glass (the day half of the dusk pass)."""
    from . import backgrounds as BG
    s = market_scene(frame, night=night)
    if unlit:
        for sp in s.sprites:
            if sp.scale == 2 and sp.image.height == 44:                  # lamp posts
                sp.image = _prop_img("lamp_post", None, glow="cloth/charcoal")
    img = iso.render(s, margin=DIORAMA_MARGIN,
                     backdrop=lambda w, h: BG.compose(sky, "hills", w, h, horizon=int(h * 0.42)))
    m = DIORAMA_MARGIN - 24
    return img.crop((m + 10, 20, img.width - m - 10, img.height - m - 40))


def dusk_market(frame: int = 0) -> Image.Image:
    """Dusk, as in the explore-and-vote mockup: the day render graded cool and dark, then every light (lit windows,
    open doors, lanterns, the forge) put back at full strength with a warm glow spilling round it. Lights are found
    by rendering the same scene twice (lights off, lights on) and keeping the pixels that changed, so occlusion is
    exactly the renderer's."""
    off = np.asarray(render_market(frame, sky="dusk", unlit=True).convert("RGB")).astype(np.float32)
    on = np.asarray(render_market(frame, sky="dusk", night=True).convert("RGB")).astype(np.float32)
    lit = np.abs(on - off).sum(-1) > 24
    grade = np.array([0.42, 0.40, 0.62], np.float32)
    out = off * grade
    out[lit] = on[lit]
    glow = np.zeros_like(on)
    glow[lit] = on[lit]
    g8 = Image.fromarray(glow.astype(np.uint8))
    near = np.asarray(g8.filter(ImageFilter.GaussianBlur(8))).astype(np.float32)
    far = np.asarray(g8.filter(ImageFilter.GaussianBlur(28))).astype(np.float32)
    out += (near * 1.5 + far * 2.6) * np.array([1.0, 0.7, 0.4])
    return Image.fromarray(out.clip(0, 255).astype(np.uint8))


def dev_diorama(out) -> None:
    save(iso.finish(render_market()), out / "town-diorama.png")


# ------------------------------------------------------------------------------------------- block-built demo
def _side_with(tid: str, var: str | None, decals: list[tuple[int, Mat]], *, mirror: bool = False) -> np.ndarray:
    """A side texture with wall decals baked in at texture columns (for arcpix.iso, which has no side decals; the
    game draws decals on the face instead). `mirror` for iso's +U faces, which it samples right to left."""
    base = tex_rgba(tid, var).copy()
    for col, dm in decals:
        d = tex_rgba(*dm)
        if mirror:
            d = d[:, ::-1]
        h, w = d.shape[:2]
        sub = base[base.shape[0] - h:, col:col + w]
        al = d[..., 3:4] / 255
        sub[..., :3] = sub[..., :3] * (1 - al) + d[..., :3] * al
    return base


def interior_scene(frame: int = 0) -> iso.Scene:
    """The inn's common room, built from blocks like the 3D map would: timber-framed back walls with a hearth,
    lamplit windows, a lantern and a door as wall decals; a bar counter of plank blocks; flagstones by the hearth
    and floorboards elsewhere, with a rug decal across four tiles; tables, barrels and the regulars."""
    from . import dioramas as D
    W_, D_ = 11, 9
    s = iso.Scene(W_, D_)
    boards = tex_rgba("floorboards", "oak")
    flags = tex_rgba("tavern_flags", "warm")
    pside = tex_rgba("plank_side", "oak")
    top_dark = np.asarray(TR.planks(83).render(TR.ramps({"main": "wood/dark", "metal": "metal/iron"}))).astype(
        np.float32)
    rug = tex_rgba("rug", "crimson")
    win = ("window_lit", "red")
    back_decals = {1: [(0, win)], 2: [(0, ("hearth", "stone"))], 4: [(0, win)], 5: [(0, ("wall_lantern", "iron"))],
                   7: [(0, win)], 9: [(0, ("door", "oak"))]}
    left_decals = {2: [(0, win)], 4: [(0, ("sign_mug", "red"))], 6: [(0, win)]}
    for j in range(D_):
        for i in range(W_):
            if j == 0 or i == 0:
                if j == 0 and i in back_decals:
                    side = _side_with("timber_frame", "white", [((i * 32) % T, dm) for _, dm in back_decals[i]])
                elif i == 0 and j in left_decals:
                    side = _side_with("timber_frame", "white", [((j * 32) % T, dm) for _, dm in left_decals[j]],
                                      mirror=True)
                else:
                    side = tex_rgba("timber_frame", "white")
                s.cells[(i, j)] = iso.Cell(6, top_dark, side)
            elif j == 3 and 5 <= i <= 9 or i == 9 and j in (1, 2):
                s.cells[(i, j)] = iso.Cell(4, boards, pside)              # the bar counter, waist high
            else:
                top = flags if (i <= 4 and j <= 4) else boards
                dec = None
                if 5 <= i <= 6 and 5 <= j <= 6:
                    a, b = (i - 5) * 32, (j - 5) * 32
                    dec = rug[b:b + 32, a:a + 32]
                s.cells[(i, j)] = iso.Cell(2, top, pside, decal=dec)
    ik = (frame // 2) % 2
    shadows, sprites = [], []
    for bid, var, i0, j0 in (("tavern_table", "oak", 3, 6), ("tavern_table", "oak", 8, 6),
                             ("clutter_barrels", "oak", 10, 1), ("tavern_table", "oak", 2, 3)):
        r = render_building(bid, var)
        sh, st = place(r, i0, j0, ground=lambda i, j: (i, j) in s.cells and s.cells[(i, j)].h == 2)
        shadows += sh
        sprites += st
    sprites += [
        D.prop("barrel", 10, 4), D.prop("crate", 1, 8), D.prop("barrel", 1, 1), D.prop("sack", 8, 1),
        D.unit("innkeeper", 7, 2, k=ik), D.unit("barmaid", 4, 5, k=1 - ik), D.unit("minstrel", 1, 4, k=ik),
        D.unit("guard", 2, 7, mirror=True, k=1 - ik), D.unit("farmer", 9, 7, mirror=True, k=ik),
        D.unit("stranger", 10, 8, mirror=True, k=1 - ik),
        D.unit("kara", 5, 6, "back", k=ik), D.unit("ilyra", 6, 7, "back", mirror=True, k=1 - ik),
        D.unit("aldric", 6, 5, mirror=True, k=1 - ik),
    ]
    s.sprites = shadows + sprites
    return s


# ---------------------------------------------------------------------------------------------- modular demo
def kit_scene(frame: int = 0) -> iso.Scene:
    """The modular pieces assembled: a town wall with corner towers and a gatehouse, a stone bridge spanning the
    moat-stream outside it, stairs and steps up to a terrace, a notice board and clutter."""
    from . import dioramas as D
    mats = {
        "g": D.Mat(("grass", "green"), ("dirt_side", "brown"), ("grass_lip", "green")),
        "G": D.Mat(("grass", "green"), ("ashlar_side", "grey"), ("grass_lip", "green")),
        "c": D.Mat(("cobble", "grey"), ("dirt_side", "brown")),
        "C": D.Mat(("cobble", "grey"), ("ashlar_side", "grey")),
        "p": D.Mat(("path", None), ("dirt_side", "brown")),
        "w": D.Mat(("water", "clear"), ("water", "clear"), sink=6, side_of_top=True),
        "s": D.Mat(("sand", None), ("dirt_side", "brown"), sink=2),
    }
    terrain = ["GGGGGGGGGGGG",
               "GGGGGGGGGGGG",
               "ggggcccggggg",
               "ggggcccggggg",
               "ggggcccggggg",
               "ggggcccggggg",
               "ggggcccggggg",
               "sssspppsssss",
               "wwwwwwwwwwww",
               "wwwwwwwwwwww",
               "ssssppppssss",
               "gggggppggggg"]
    heights = ["444444444444",
               "444444444444",
               "222222222333",
               "222222222333",
               "222222222333",
               "222222222222",
               "222222222222",
               "222222222222",
               "000000000000",
               "000000000000",
               "222222222222",
               "222222222222"]
    s = D.build_scene(mats, terrain, heights, frame=frame, seed=23,
                      decals={"g": (0.15, D.FLOWERS), "p": (0.3, [("pebbles_0", None)])})
    shadows, sprites = [], []
    pieces = [("wall_tower", "grey", 0, 2), ("wall_v", "grey", 0, 3), ("wall_v", "grey", 0, 4),
              ("wall_tower", "grey", 0, 5), ("wall_u", "grey", 1, 5), ("wall_u", "grey", 2, 5),
              ("gate_u", "grey", 3, 5),
              ("wall_u", "grey", 6, 5), ("wall_u", "grey", 7, 5), ("wall_tower", "grey", 8, 5),
              ("bridge_v", "grey", 5, 8), ("bridge_v", "grey", 5, 9),
              ("stairs_u", "stone", 7, 2), ("steps_u", "wood", 10, 5), ("notice_board_v", "oak", 6, 3),
              ("clutter_crates", "oak", 2, 3), ("clutter_barrels", "oak", 11, 2), ("clutter_produce", "mixed", 9, 3),
              ("watchtower", "warm", 10, 0)]
    for bid, var, i0, j0 in pieces:
        r = render_building(bid, var, frame=frame % _BUILDING[bid].frames)
        lift = 38 if bid.startswith("bridge") else 0
        z = s.top_z(i0, j0)
        sh, st = place(r, i0, j0, lift=lift,
                       ground=lambda i, j, z=z: (i, j) in s.cells and s.top_z(i, j) == z)
        shadows += sh
        sprites += st
    ik = (frame // 2) % 2
    sprites += [
        D.prop("pine", 0, 0), D.prop("pine", 2, 1), D.prop("tree", 0, 11, "autumn"), D.prop("reeds", 9, 10),
        D.prop("banner", 3, 4, "navy", frame=frame), D.prop("banner", 6, 4, "navy", frame=frame),
        D.prop("lamp_post", 4, 7), D.prop("lamp_post", 7, 7), D.prop("rock_mossy", 10, 10),
        D.unit("guard", 4, 6, k=ik), D.unit("guard", 6, 6, mirror=True, k=1 - ik),
        D.unit("guard_captain", 5, 4, "back", k=ik), D.unit("tamsin", 5, 11, "back", k=1 - ik),
        D.unit("wren", 6, 10, "back", mirror=True, k=ik), D.unit("sefa", 4, 11, "back", k=1 - ik),
        D.unit("fisher", 9, 10, mirror=True, k=ik), D.unit("hermit", 6, 1, k=1 - ik),
        D.unit("merchant", 10, 2, mirror=True, k=ik),
    ]
    s.sprites = shadows + sprites
    return s


def _scene_img(scene: iso.Scene, sky: str, *, margin: int = 120, crop=(10, 10, 10, 60)) -> Image.Image:
    from . import backgrounds as BG
    img = iso.render(scene, margin=margin,
                     backdrop=lambda w, h: BG.compose(sky, "hills", w, h, horizon=int(h * 0.42)))
    m = margin - 24
    return img.crop((m + crop[0], crop[1], img.width - m - crop[2], img.height - m - crop[3]))


# ------------------------------------------------------------------------------------------------------ build
_DIRS = {"wall": "wall", "roof": "roof", "floor": "floor", "floor_decal": "decal/floor", "wall_decal": "decal/wall"}


def _strip(imgs: list[Image.Image]) -> Image.Image:
    w, h = imgs[0].size
    out = Image.new("RGBA", (w * len(imgs), h))
    for k, im in enumerate(imgs):
        out.paste(im, (k * w, 0))
    return out


def _height_steps(m: Model) -> int:
    top = max((f.o + a * f.e1 + b * f.e2)[2] for f in m.faces for a, b in f.rings[0])
    return int(math.ceil(top / STEP))


def build() -> None:
    out = PACK / "town"
    shutil.rmtree(out, ignore_errors=True)
    textures, tiles = {}, {k: [] for k in _DIRS}
    for t in CATALOGUE:
        files = {}
        for vname in t.variants:
            img = tex_image(t.id, vname)
            rel = f"{_DIRS[t.kind]}/{t.id}_{vname}.png"
            save(img, out / rel)
            files[vname] = rel
            tiles[t.kind].append(Tile(img, t.id, vname))
        w, h = _canvas_of(t.id).w, _canvas_of(t.id).h
        textures[t.id] = {"kind": t.kind, "variants": files, "size": [w, h]}

    buildings, sheet, pieces = {}, [], []
    for b in BUILDINGS:
        files = {}
        for vname in b.variants:
            day = render_frames(b.id, vname)
            night = render_frames(b.id, vname, True)
            r0 = day[0]
            entry = {"image": f"buildings/{b.id}_{vname}.png"}
            save(_strip([r.image for r in day]), out / entry["image"])
            if np.asarray(r0.shadow)[..., 3].any():
                entry["shadow"] = f"buildings/{b.id}_{vname}_shadow.png"
                save(r0.shadow, out / entry["shadow"])
            lights = []
            for d, n in zip(day, night):
                da, na = np.asarray(d.image).astype(int), np.asarray(n.image)
                diff = np.abs(da - na).sum(-1) > 24
                lights.append(Image.fromarray(np.where(diff[..., None], na, 0).astype(np.uint8), "RGBA"))
            if any(np.asarray(li)[..., 3].any() for li in lights):
                entry["lights"] = f"buildings/{b.id}_{vname}_lights.png"
                save(_strip(lights), out / entry["lights"])
            files[vname] = entry
            small = "modular" in b.tags or "market" in b.tags or "interior" in b.tags
            (pieces if small else sheet).append(Tile(r0.image, b.id, vname + (f" · {b.frames}f" if b.frames > 1
                                                                               else "")))
        r0 = render_frames(b.id)[0]
        model = b.make(next(iter(b.variants.values())), False, 0)
        fw, fd = r0.fw, r0.fd
        entry = {
            "variants": files, "size": list(r0.image.size), "frames": b.frames, "fps": b.fps,
            "footprint": [fw, fd], "origin": list(r0.origin), "anchor": list(r0.anchor),
            "height": _height_steps(model), "blocks": b.blocks or [[0 if b.walkable else 1] * fw for _ in range(fd)],
            "walkable": b.walkable, "cover": b.cover, "tags": b.tags,
            "attach": {k: [list(p) for p in v] for k, v in r0.attach.items()},
        }
        if b.pair:
            entry["pair"] = b.pair
        entry.update({k: v for k, v in b.extra.items()})
        buildings[b.id] = entry

    overlays = {}
    for oid in ("smoke", "sign_mug", "sign_anvil", "sign_bag"):
        frames, anchor = overlay_frames(oid)
        rel = f"overlays/{oid}.png"
        save(_strip(list(frames)), out / rel)
        overlays[oid] = {"image": rel, "size": list(frames[0].size), "frames": len(frames), "fps": 8,
                         "anchor": list(anchor)}

    write_json({
        "tile": TILE, "span": T // TILE, "heightStep": STEP, "storey": STOREY,
        "projection": "2:1 isometric at the terrain's texel density: screen x = U - V, screen y = (U + V) / 2 - Z, in "
                      "texels (U = world X, V = world Z, Z = height); a 32 px tile is a 64x32 diamond, a 16 px "
                      "height step 16 px of screen. Light from the top-left: tops 1.0, +Z faces 0.80, +X faces 0.62",
        "sampling": {
            "wall": "128x64, one storey per period. U = world distance along the face / (span * tile), measured "
                    "left to right as the face is seen (along +X on +Z faces, along -Z on +X faces); V = height "
                    "above the storey's floor / storey (repeats). Posts of timber_frame fall on tile edges",
            "roof": "128x128. On a slope: U = distance along the ridge / (span * tile), V = distance down the slope "
                    "from the ridge / (span * tile): courses run along U, lower edges down V. As a flat top: UV = "
                    "world XZ / (span * tile)",
            "floor": "128x128 tops: UV = world XZ / (span * tile), as terrain tops",
            "decal/wall": "alpha, authored face-on: 32 px (one tile) wide, bottom row = the storey's floor (doors "
                          "and windows land at the right height on any storey); rose_window, crack and notices are "
                          "32x32 and go anywhere on a face. Placed in face space, never mirrored",
            "decal/floor": "alpha, tile-local on tops: rug is 64x64 across 2x2 tiles (from its back corner tile), "
                           "doormat 32x32",
        },
        "textures": textures,
        "buildings": buildings,
        "overlays": overlays,
        "notes": {
            "image": "frames left to right, each `size`; loop at fps. Pre-lit, outlined, drawn at 1 px = 1 texel",
            "origin": "pixel of the footprint's back corner (min X, min Z) at ground level; put it on that corner's "
                      "projection. A footprint [w, d] covers tiles i..i+w-1 (X) and j..j+d-1 (Z)",
            "anchor": "pixel of the footprint's ground centre (origin + ((w - d) * 16, (w + d) * 8))",
            "sorting": "draw a building in 32 px screen columns from the origin, each sorted at the frontmost "
                       "footprint tile in that column, so units can pass behind and in front of it",
            "shadow": "ground shadow in the same frame, alpha; draw on the ground before units, outside the footprint",
            "lights": "night overlay in the same frame: lit windows, open doors, forge glow. Fade in at dusk over "
                      "the darkened building, ideally with bloom",
            "attach": "pixels in frame 0: smoke = chimney mouths (overlays/smoke), sign = the projecting sign's "
                      "root (overlays named by the building's `sign`), door = door threshold, light/flag = points "
                      "for a light source or a banner",
            "blocks": "per footprint tile, rows = Z, columns = X: 1 blocks movement",
            "height": "in height steps, for line of sight",
            "run": "modular pieces come in pairs: _u runs along X, _v along Z (a quarter turn, lit correctly)",
            "rise": "stairs and steps climb `rise` height steps across their tile, towards `climbs`",
            "drop": "bridge spans: origin is at deck level; the arch drops this many texels below it",
            "overlays": "anchor = the pixel to pin on the attach point; smoke and signs loop at fps",
        },
    }, out / "manifest.json")

    # ---------------------------------------------------------------------------------------------- previews
    sub = "World-aligned and seamless like the terrain: walls repeat once per storey (64 px), roofs and floors span "
    save(stack("Town kit · block textures", [
        contact_sheet("Walls", tiles["wall"], columns=6, zoom=2, subtitle=sub + "4x4 tiles."),
        contact_sheet("Roofs and floors", tiles["roof"] + tiles["floor"], columns=6, zoom=2,
                      subtitle="Roof courses run along the ridge (U) and lap down the slope (V)."),
    ]), PREVIEWS / "24-town-textures.png")
    save(contact_sheet("Town kit · decals", tiles["wall_decal"] + tiles["floor_decal"], columns=10, zoom=4,
                       checker=True, subtitle="Wall decals are one tile wide and a storey tall (bottom = floor), "
                                              "trim-coloured variants; floor decals are tile-local."),
         PREVIEWS / "24-town-decals.png")
    save(contact_sheet("Town kit · buildings", sheet, columns=8, zoom=2, checker=True,
                       subtitle="Pre-rendered in the map's projection from the block textures, lit from the top-left "
                                "with cast shadows. Each has a footprint, origin, anchor, ground shadow and night "
                                "lights."),
         PREVIEWS / "24-town-buildings.png")
    save(contact_sheet("Town kit · pieces", pieces, columns=8, zoom=3, checker=True,
                       subtitle="Modular walls, gates, towers, stairs and bridge spans (each along U and along V), "
                                "market stalls, notice boards, clutter and furniture."),
         PREVIEWS / "24-town-pieces.png")
    night_tiles = []
    for bid, var in (("cottage", "thatch_red"), ("house", "red"), ("tavern", "red"), ("smithy", "oak"),
                     ("store", "red"), ("chapel", "grey"), ("windmill", "white"), ("watchtower", "grey")):
        d = render_building(bid, var).image
        lights = Image.open(out / buildings[bid]["variants"][var]["lights"]).crop((0, 0) + d.size) \
            if "lights" in buildings[bid]["variants"][var] else Image.new("RGBA", d.size)
        dark = Image.fromarray((np.asarray(d).astype(np.float32) * np.array([0.42, 0.4, 0.62, 1.0])).astype(np.uint8))
        dark.alpha_composite(lights)
        night_tiles.append(Tile(dark, bid, "night: lights overlay on a darkened building"))
    save(contact_sheet("Town kit · night lights", night_tiles, columns=8, zoom=2, checker=True),
         PREVIEWS / "24-town-night.png")
    save(iso.finish(render_market()), PREVIEWS / "24-town-diorama.png")
    save(iso.finish(dusk_market(), bloom=0.3), PREVIEWS / "24-town-dusk.png")
    save(iso.finish(_scene_img(interior_scene(), "dusk", margin=110)), PREVIEWS / "24-town-interior.png")
    save(iso.finish(_scene_img(kit_scene(), "day", margin=110)), PREVIEWS / "24-town-kit.png")
    frames = [iso.finish(render_market(k), zoom=1).convert("P", palette=Image.Palette.ADAPTIVE) for k in range(8)]
    frames[0].save(PREVIEWS / "anim-town.gif", save_all=True, append_images=frames[1:], duration=140, loop=0)
