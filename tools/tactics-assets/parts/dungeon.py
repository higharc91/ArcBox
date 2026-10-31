"""Dungeons, caves and interiors: the indoor battle maps.

Three halves, each following the outdoor set it extends:

  * **Textures**, like `terrain.py`: seamless, world-aligned floors (dungeon flagstone, rune-cut crypt tiles, cave
    rock, mine gravel and boardwalks, wet stone with rippling puddles, floorboards, parquet and a void for chasms),
    wall sides (hewn dungeon blocks, cave rock, mine shoring, a chasm side that falls away into darkness), lips
    (stalactites, mine beams) and two kinds of decal: ground decals (rails, carpet runners, rugs, rune circles,
    bones, blood) and *wall* decals (sconces, shackles, banners, cobwebs, glowing runes) that sit on a side face.
  * **Props**, like `props.py`: billboards anchored at the ground contact, in palette variants, with the manifest's
    blocks / cover / height facts. Light sources also say where their light sits and what colour it is.
  * **Light**: interiors are lit by their props, not the sky. The set ships additive light pools and halos, a
    flicker curve per kind of flame, and a darkness recipe (manifest `lighting`); the dioramas here are rendered
    with exactly that recipe so the preview shows what a lit dungeon should look like.

The stone floor variants (mossy, cracked, bloody) share one slab layout, so a map can switch variant per tile and
the slabs still line up across the seam.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from functools import lru_cache
from typing import Callable

import numpy as np
from PIL import Image

from arcpix import iso, tex
from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, stack
from . import props as PR
from . import terrain as TR

T = TR.T                 # 128: one texture spans 4 tiles
TILE = TR.TILE
SIDE_H = TR.SIDE_H
DECAL = TR.DECAL
WALL_W, WALL_H = 32, 48  # wall decals: one tile wide, three height steps tall, hung from the wall's top

TS = TR.SLOTS            # texture slots: main, alt, grout, accent, metal, glow (shared with the terrain set)

RAMPS = {
    "crypt": ramp("#74727f", shift=0.05, spread=1.1),
    "crypt_dark": ramp("#4e4c5a", shift=0.05, spread=1.2),
    "crypt_warm": ramp("#857a6c", shift=0.05),
    "marble": ramp("#b9b4ad", shift=0.05, spread=0.85),
    "slate": ramp("#3f4150", shift=0.05, spread=1.25),
    "grout": ramp("#34303c", shift=0.04, spread=1.2),
    "moss": ramp("#55773a", shift=0.08),
    "blood": ramp("#8a2230", shift=0.06),
    "rune_dormant": ramp("#3a3746", shift=0.04, spread=1.1),
    "rune_arcane": ramp("#6fc8ff", shift=0.05, spread=0.85),
    "rune_fel": ramp("#79e88f", shift=0.06, spread=0.85),
    "rune_blood": ramp("#ff5a5a", shift=0.07, spread=0.85),
    "cave": ramp("#6d6460", shift=0.05, spread=1.1),
    "cave_cool": ramp("#5a5f72", shift=0.05, spread=1.15),
    "cave_deep": ramp("#3f3d52", shift=0.05, spread=1.25),
    "gravel": ramp("#6b5f55", shift=0.05),
    "coal": ramp("#2f2b33", shift=0.04, spread=1.3),
    "wet": ramp("#55596a", shift=0.05, spread=1.2),
    "pool": ramp("#2f5a78", shift=0.04, spread=1.1),
    "pool_glow": ramp("#3f9fae", shift=0.05),
    "void": ramp("#2a2440", shift=0.05, spread=1.35),
    "void_mote": ramp("#8f7ad8", shift=0.06, spread=0.9),
    "oak_floor": ramp("#8a5a36", shift=0.06),
    "walnut": ramp("#5a3a28", shift=0.06, spread=1.1),
    "pale_floor": ramp("#b48c5e", shift=0.06, spread=0.9),
    "brass": ramp("#c9a04a", shift=0.06, spread=1.1),
    "flame": ramp("#ffb347", shift=0.07, spread=0.75),
    "candle": ramp("#ffd27a", shift=0.06, spread=0.6),
    "wax": ramp("#e8dcc0", shift=0.05, spread=0.7),
    "ghost_fire": ramp("#7fe0c0", shift=0.05, spread=0.8),
    "parchment": ramp("#e2cf9e", shift=0.05, spread=0.75),
    "potion_green": ramp("#7fe05a", shift=0.06, spread=0.85),
    "potion_violet": ramp("#b07bff", shift=0.06, spread=0.85),
    "potion_red": ramp("#ff5a6a", shift=0.06, spread=0.85),
    "glass": ramp("#a9d4e0", shift=0.04, spread=0.8),
    "ale": ramp("#d9962e", shift=0.06),
    "foam": ramp("#f2ead6", shift=0.04, spread=0.55),
    "web": ramp("#c8c6d2", shift=0.04, spread=0.7),
    "ore": ramp("#8a8f9c", shift=0.05, spread=1.2),
    "portal": ramp("#9f6aff", shift=0.07, spread=0.9),
    "bone": ramp("#d8cfb6", shift=0.05, spread=0.8),
    "gem_red": ramp("#e0405a", shift=0.06),
    "gem_green": ramp("#40d080", shift=0.06),
    "gem_blue": ramp("#4a8fff", shift=0.06),
    "cushion": ramp("#9a2a3a", shift=0.07),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "dungeon":
        return RAMPS[name]
    if fam == "prop":
        return PR.PROP_RAMPS[name]
    if fam == "tr":
        return TR.TR_RAMPS[name]
    return FAMILIES[fam][name]


def ramps(variant: dict[str, str], slots: SlotTable | None = None) -> dict[str, list[str]]:
    out = {s: _ref(r) for s, r in variant.items()}
    for name in (slots or TS).names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


def _tcanvas(w: int, h: int, shade: np.ndarray, slot: np.ndarray | str = "main") -> Canvas:
    return TR._canvas(w, h, shade, slot)


def _sid(name: str) -> int:
    return TS.id(name)


# ---------------------------------------------------------------------------------------------- floor layouts
def _courses(r: np.random.Generator, w: int, h: int, row_h: int | list[int], lengths: tuple[int, ...]):
    """Coursed slabs: rows (`row_h` tall, or a list of heights summing to h) cut into random lengths that wrap round
    x, so the course tiles. Returns the slab id per pixel and each pixel's distance to the slab's left, right, top
    and bottom edge."""
    rows = row_h if isinstance(row_h, list) else [row_h] * (h // row_h)
    assert sum(rows) == h
    idx = np.zeros((h, w), int)
    dl, dr = np.zeros((h, w), int), np.zeros((h, w), int)
    dt, db = np.zeros((h, w), int), np.zeros((h, w), int)
    n = 0
    y0 = 0
    for rh in rows:
        dt[y0:y0 + rh] = np.arange(rh)[:, None]
        db[y0:y0 + rh] = rh - 1 - np.arange(rh)[:, None]
        start = int(r.integers(w))
        x = start
        while x < start + w:
            L = int(r.choice(lengths))
            if start + w - x - L < min(lengths):
                L = start + w - x
            for k in range(L):
                X = (x + k) % w
                idx[y0:y0 + rh, X] = n
                dl[y0:y0 + rh, X] = k
                dr[y0:y0 + rh, X] = L - 1 - k
            x += L
            n += 1
        y0 += rh
    return idx, dl, dr, dt, db, n


@lru_cache(maxsize=None)
def _slab_layout(seed: int = 701):
    """The dungeon floor's slabs, shared by every flagstone state so their slabs line up tile to tile."""
    r = tex.rng(seed)
    return _courses(r, T, T, [20, 14, 18, 12, 16, 22, 12, 14], (18, 26, 30, 38))


def _crack_walk(shade: np.ndarray, r: np.random.Generator, x: int, y: int, n: int, inside=None) -> list:
    """A hairline crack: dark pixels with a lit pixel below-right (the crack's far wall catches the light)."""
    h, w = shade.shape
    path = []
    dx, dy = int(r.choice([-1, 1])), 1
    for _ in range(n):
        X, Y = x % w, y % h
        if inside is not None and not inside[Y, X]:
            break
        shade[Y, X] = 1
        path.append((X, Y))
        if r.random() < 0.65:
            x += dx
        if r.random() < 0.55:
            y += dy
        if r.random() < 0.15:
            dx = -dx
    for X, Y in path:
        Xn, Yn = (X + 1) % w, (Y + 1) % h
        if (Xn, Yn) not in path and shade[Yn, Xn] >= 2:
            shade[Yn, Xn] = min(5, shade[Yn, Xn] + 1)
    return path


def dungeon_floor(state: str = "plain", seed: int = 701) -> Canvas:
    """Hewn slabs in running courses with worn, chipped edges. `state` adds moss in the joints, cracks and broken
    corners, or old blood that has run into the grout, over the same slabs."""
    idx, dl, dr, dt, db, n = _slab_layout(seed)
    r = tex.rng(seed + 1)
    tone = r.choice([-1, 0, 0, 0, 1], n)
    surf = tex.fbm(T, T, 16, r, octaves=2)
    shade = 3 + tone[idx] + (surf > 0.8) - (surf < 0.18)
    wear = tex.fbm(T, T, 2, r, octaves=2)                     # a polished track where feet have walked
    shade = np.where((wear > 0.75) & (surf > 0.55) & (tone[idx] < 1), shade + 1, shade)
    shade = np.where((dt == 0) | (dl == 0), shade + 1, shade)
    shade = np.where(((db == 1) | (dr == 1)) & (dt > 0) & (dl > 0), shade - 1, shade)
    slot = np.full((T, T), _sid("main"))
    grout = (db == 0) | (dr == 0)
    corner = ((dt == 0) & (dl == 0)) | ((db == 1) & (dr == 1)) | ((dt == 0) & (dr == 1)) | ((db == 1) & (dl == 0))
    corner &= r.random((T, T)) < 0.7
    joint = grout | corner
    slot[joint] = _sid("grout")
    shade = np.where(joint, np.where(np.roll(~joint, 1, 0), 1, 2), shade)
    pits = (r.random((T, T)) < 0.012) & ~joint
    shade = np.where(pits, 1, shade)
    shade = np.where(np.roll(pits, 1, 0) & ~joint, shade + 1, shade)
    shade = np.clip(shade, 0, 5)
    for k in range(n):                                      # a few chips bitten out of slab edges
        if r.random() < 0.35:
            ys, xs = np.nonzero((idx == k) & ((dt == 0) | (db == 1)) & (dl > 2) & (dr > 3))
            if len(xs):
                q = int(r.integers(len(xs)))
                for ox, oy in ((0, 0), (1, 0), (0, 1 if dt[ys[q], xs[q]] == 0 else -1)):
                    Y, X = (ys[q] + oy) % T, (xs[q] + ox) % T
                    slot[Y, X], shade[Y, X] = _sid("grout"), 2
    inside = ~joint
    if state in ("plain", "cracked"):
        for k in range(n):
            if r.random() < (0.12 if state == "plain" else 0.55):
                ys, xs = np.nonzero(idx == k)
                q = int(r.integers(len(xs)))
                _crack_walk(shade, r, int(xs[q]), int(ys[q]), int(r.integers(5, 16)), inside)
    if state == "cracked":
        # whole corners broken away, showing the grit underneath
        for k in range(n):
            if r.random() < 0.18:
                ys, xs = np.nonzero((idx == k) & (dr < 7) & (db < 6) & ~joint)
                for y, x in zip(ys, xs):
                    if dr[y, x] + db[y, x] < 7 - int(r.integers(0, 2)):
                        slot[y, x] = _sid("accent")
                        shade[y, x] = 2 + int(r.random() < 0.3) - int(dr[y, x] + db[y, x] > 5)
        shade = np.where((slot == _sid("main")) & np.roll(slot == _sid("accent"), -1, 0), 1, shade)
    if state == "mossy":
        m = tex.fbm(T, T, 8, r, octaves=3)
        near = (np.minimum(np.minimum(dl, dr), np.minimum(dt, db)) <= 1)
        moss = (joint & (m > 0.38)) | (near & (m > 0.62)) | (m > 0.86)
        tuft = moss & (tex.fbm(T, T, 32, r, octaves=1) > 0.5)
        slot[moss] = _sid("alt")
        shade = np.where(moss, np.where(tuft, 4, np.where(joint, 2, 3)), shade)
        shade = np.where(moss & ~np.roll(moss, 1, 0), 4, shade)
        shade = np.where(moss & ~np.roll(moss, -1, 0) & ~joint, 2, shade)
        sp = r.random((T, T))
        shade = np.where(moss & (sp < 0.05), 5, shade)
    if state == "bloody":
        bl = tex.fbm(T, T, 8, r, octaves=3)
        pool = bl > 0.84
        flow = joint & (bl > 0.66)                            # blood runs along the joints
        drops = (r.random((T, T)) < 0.004) & (bl > 0.7)
        wet = pool | flow | drops
        slot[wet] = _sid("accent")
        shade = np.where(wet, np.where(pool, np.where(bl > 0.88, 1, 2), 1), shade)
        shade = np.where(wet & ~np.roll(wet, 1, 0) & pool, 3, shade)          # a wet glint along the top rim
        shade = np.where(wet & ~np.roll(wet, -1, 0), 1, shade)
        gl = r.random((T, T))
        shade = np.where(pool & (gl < 0.01), 5, shade)
    return _tcanvas(T, T, np.clip(shade, 0, 5), slot)


RUNES = [
    ["..###..", ".#...#.", "#..#..#", "#.###.#", "#..#..#", ".#...#.", "..###.."],
    ["#..#..#", "#..#..#", ".#.#.#.", "..###..", "...#...", "...#...", "..###.."],
    ["...##..", "..##...", ".##....", "..###..", "....##.", "...##..", "..##..."],
    ["...#...", "..#.#..", ".#.#.#.", "#.###.#", ".#.#.#.", "..#.#..", "...#..."],
    ["#######", "#.....#", "#.###.#", "#.#.#.#", "#.#.#.#", "#.....#", "###.###"],
    ["#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", "...#...", "...#..."],
    ["..###..", ".##....", "##.....", "##..#..", "##.....", ".##....", "..###.."],
    ["...#...", "..#.#..", "..#.#..", ".#.#.#.", ".#...#.", "#.....#", "#######"],
]


def _engrave(slot: np.ndarray, shade: np.ndarray, x: int, y: int, glyph: list[str], *, mirror: bool = False) -> None:
    """Cuts a glyph into stone: the groove takes the glow slot (dark stone when dormant, light when lit) and its
    far, lower-right wall catches the light."""
    h, w = slot.shape
    pts = set()
    for gy, row in enumerate(glyph):
        for gx, ch in enumerate(row[::-1] if mirror else row):
            if ch == "#":
                pts.add(((x + gx) % w, (y + gy) % h))
    for X, Y in pts:
        slot[Y, X] = _sid("glow")
        shade[Y, X] = 4
    for X, Y in pts:
        Xn, Yn = (X + 1) % w, (Y + 1) % h
        if (Xn, Yn) not in pts and slot[Yn, Xn] != _sid("glow"):
            shade[Yn, Xn] = min(5, shade[Yn, Xn] + 1)
        Xp, Yp = (X - 1) % w, (Y - 1) % h
        if (Xp, Yp) not in pts and slot[Yp, Xp] != _sid("glow"):
            shade[Yp, Xp] = max(1, shade[Yp, Xp] - 1)


def crypt_tiles(seed: int = 711) -> Canvas:
    """Square-cut crypt tiles in two stones (main / alt, checkered), bevelled, with runes cut into some of them.
    The runes are the glow slot: dormant variants keep them as dark grooves, lit ones make them shine."""
    r = tex.rng(seed)
    ys, xs = np.mgrid[0:T, 0:T]
    cell = 16
    tx, ty = xs // cell, ys // cell
    lx, ly = xs % cell, ys % cell
    checker = (tx + ty) % 2 == 1
    slot = np.where(checker, _sid("alt"), _sid("main"))
    tone = r.choice([-1, 0, 0, 1], (T // cell, T // cell))
    surf = tex.fbm(T, T, 16, r, octaves=2)
    shade = 3 + tone[ty, tx] + (surf > 0.78) - (surf < 0.2)
    shade = np.where((lx == 0) | (ly == 0), shade + 1, shade)
    shade = np.where(((lx == cell - 2) | (ly == cell - 2)) & (lx > 0) & (ly > 0), shade - 1, shade)
    joint = (lx == cell - 1) | (ly == cell - 1)
    slot = np.where(joint, _sid("grout"), slot)
    shade = np.where(joint, 1, shade)
    shade = np.clip(shade, 1, 5)
    for k in range(10):
        X, Y = int(r.integers(T)), int(r.integers(T))
        _crack_walk(shade, r, X, Y, int(r.integers(4, 10)), ~joint)
    order = r.permutation((T // cell) ** 2)
    for q, cell_i in enumerate(order[:12]):
        cx, cy = (cell_i % (T // cell)) * cell, (cell_i // (T // cell)) * cell
        _engrave(slot, shade, cx + 4, cy + 4, RUNES[q % len(RUNES)], mirror=bool(q % 3 == 1))
    return _tcanvas(T, T, shade, slot)


def cave_floor(seed: int = 721) -> Canvas:
    """Natural cave floor: bumpy rock lit from the top-left, damp hollows, a few deep cracks, gravel drifts."""
    r = tex.rng(seed)
    big = tex.fbm(T, T, 4, r, octaves=4)
    mid = tex.fbm(T, T, 16, r, octaves=2)
    height = big * 0.65 + mid * 0.35
    em = tex.emboss(height * 9)
    shade = 3 + (em > 0.10).astype(int) + (em > 0.32) - (em < -0.10) - (em < -0.34)
    shade = np.where(height < 0.28, shade - 1, shade)
    shade = tex.despeckle(np.clip(shade, 1, 5), 2)
    slot = np.full((T, T), _sid("main"))
    pts = tex.jittered_points(T, T, 32, 0.4, r)
    f1, f2, _ = tex.voronoi(T, T, pts)
    gap = f2 - f1
    crack = (gap < 0.9) & (tex.fbm(T, T, 4, r, octaves=1) > 0.42)
    shade = np.where(crack, 0, shade)
    shade = np.where(np.roll(crack, 1, 0) & ~crack, np.minimum(shade + 1, 5), shade)
    drift = height < 0.33
    for x, y in tex.poisson(T, T, 4.2, r):
        if drift[y, x] or r.random() < 0.05:
            w = int(r.integers(2, 4))
            for dx in range(w):
                slot[y, (x + dx) % T] = _sid("alt")
                shade[y, (x + dx) % T] = 4 if dx == 0 else 3
                slot[(y + 1) % T, (x + dx) % T] = _sid("alt")
                shade[(y + 1) % T, (x + dx) % T] = 2
            shade[(y + 2) % T, (x + 1) % T] = max(0, shade[(y + 2) % T, (x + 1) % T] - 1)
    return _tcanvas(T, T, np.clip(shade, 0, 5), slot)


def wet_stone_frames(seed: int = 731, frames: int = 8) -> list[Canvas]:
    """Rounded, glistening stones set in silt, with standing puddles. Drips land in the puddles: each ring is born
    at a point and grows over the loop, staggered so a few are always moving."""
    r = tex.rng(seed)
    pts = tex.jittered_points(T, T, 12, 0.35, r)
    f1, f2, idx = tex.voronoi(T, T, pts)
    gap = f2 - f1
    height = np.sqrt(np.clip(gap / 4.5, 0, 1))
    em = tex.emboss(height)
    tone = r.choice([-1, 0, 0, 1], len(pts))
    base = 3 + tone[idx] + (em > 0.14) - (em < -0.14)
    grout = gap < 1.3
    slot0 = np.where(grout, _sid("grout"), _sid("main"))
    base = np.where(grout, np.where(np.roll(~grout, 1, 0), 1, 2), base)
    # wet speculars: a bright fleck at the top-left of the bigger stones
    for k, (px, py) in enumerate(pts):
        if r.random() < 0.6:
            X, Y = int(px - 2) % T, int(py - 2) % T
            if not grout[Y, X]:
                base[Y, X] = 5
                if not grout[Y, (X + 1) % T]:
                    base[Y, (X + 1) % T] = max(base[Y, (X + 1) % T], 4)
    pud = tex.fbm(T, T, 4, r, octaves=3)
    wet = pud < 0.22
    deep = pud < 0.1
    drops = [(x, y, int(r.integers(frames))) for x, y in tex.poisson(T, T, 18, r) if wet[y, x]][:7]
    ys, xs = np.mgrid[0:T, 0:T]
    out = []
    for f in range(frames):
        slot = np.where(wet, _sid("accent"), slot0)
        shade = np.where(wet, np.where(deep, 1, 2), base)
        shade = np.where(wet & ~np.roll(wet, 1, 0), 4, shade)                 # reflected light on the far rim
        shade = np.where(~wet & np.roll(wet, -1, 0), 1, shade)                # damp bank above the water
        for x, y, ph in drops:
            age = (f - ph) % frames
            rad = 0.8 + age * 1.15
            dx = tex.wrapped_delta(xs, x, T)
            dy = tex.wrapped_delta(ys, y, T) * 2.0                            # rings lie flat: squash them
            d = np.sqrt(dx * dx + dy * dy)
            ring = wet & (np.abs(d - rad * 2) < 0.9) & (age < frames - 1)
            shade = np.where(ring, np.where(dy < 0, 4 - (age > 4), 3), shade)
            if age == 0:
                shade[y, x] = 5
        out.append(_tcanvas(T, T, np.clip(shade, 0, 5), slot))
    return out


def floorboards(seed: int = 741) -> Canvas:
    """Interior boards, narrower than the outdoor planks and waxed: a soft sheen runs across them, the grain is
    fine and the gaps are tight. Joints are staggered so the floor reads as one surface."""
    r = tex.rng(seed)
    grain = tex.fbm(T, T, 8, r, octaves=3, aspect=10.0)
    shade = np.full((T, T), 3)
    shade[grain < 0.28] = 2
    shade[grain > 0.74] = 4
    slot = np.full((T, T), _sid("main"))
    ys, xs = np.mgrid[0:T, 0:T]
    widths = [6, 5, 5] * (T // 16)                        # mixed board widths; the cycle divides 128, so it tiles
    y0 = 0
    for wdt in widths:
        y1 = y0 + wdt
        shade[y0:y1] += int(r.choice([-1, 0, 0, 1]))
        shade[y1 - 1] = 1
        start = int(r.integers(0, T))
        x = start
        while x < start + T:
            for yy in range(y0, y1 - 1):
                shade[yy, x % T] = 1
            for nx in (x + 2, x - 3):
                slot[y0 + 2, nx % T] = _sid("metal")
                shade[y0 + 2, nx % T] = 3
            x += int(r.integers(30, 64))
        if r.random() < 0.6:
            kx = int(r.integers(T))
            shade[y0 + 2, kx] = 1
            shade[y0 + 2, (kx + 1) % T] = 2
            shade[y0 + 3, kx] = 2
        y0 = y1
    sheen = (np.sin(2 * np.pi * (xs + 2 * ys) / 64) > 0.9) & tex.checker(T, T)
    shade = np.where(sheen & (slot == _sid("main")) & (shade >= 2) & (shade < 5), shade + 1, shade)
    wear = tex.fbm(T, T, 4, r, octaves=2)
    shade = np.where((wear < 0.2) & (slot == _sid("main")) & (shade > 1), shade - 1, shade)
    return _tcanvas(T, T, np.clip(shade, 0, 5), slot)


def parquet(seed: int = 751) -> Canvas:
    """Basket-weave parquet for a study or a lord's hall: 16 px squares of four strips, alternating direction."""
    r = tex.rng(seed)
    ys, xs = np.mgrid[0:T, 0:T]
    lx, ly = xs % 16, ys % 16
    horiz = ((xs // 16 + ys // 16) % 2) == 0
    along = np.where(horiz, lx, ly)
    across = np.where(horiz, ly, lx)
    strip = across // 4
    grain_h = tex.fbm(T, T, 8, r, octaves=2, aspect=8.0)
    grain_v = tex.fbm(T, T, 8, r, octaves=2, aspect=8.0).T
    g = np.where(horiz, grain_h, grain_v)
    tone = r.choice([-1, 0, 0, 1], (T // 16, T // 16, 4))
    shade = 3 + tone[ys // 16, xs // 16, strip] + (g > 0.75) - (g < 0.25)
    shade = np.where(across % 4 == 0, shade + 1, shade)
    shade = np.where(across % 4 == 3, 1, shade)
    shade = np.where((lx == 15) | (ly == 15), 0, shade)
    shade = np.where((along == 0) & (across % 4 != 3), shade + 1, shade)
    slot = np.where(horiz, _sid("main"), _sid("alt"))
    return _tcanvas(T, T, np.clip(shade, 0, 5), slot)


def mine_gravel(seed: int = 761) -> Canvas:
    """A mine's floor: packed grit, tamped ruts, scattered ore chips (alt) and coal dust (accent)."""
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=4)
    shade = tex.quantize(n, [(0.3, 2), (0.74, 4)], 3, dither=0.05)
    fine = tex.fbm(T, T, 32, r, octaves=1)
    shade = np.where(fine < 0.18, shade - 1, shade)
    shade = tex.despeckle(shade, 1)
    slot = np.full((T, T), _sid("main"))
    dust = tex.fbm(T, T, 8, r, octaves=2) > 0.74
    dust &= ~((tex.fbm(T, T, 8, r, octaves=2) > 0.78) & tex.checker(T, T))
    slot[dust] = _sid("accent")
    shade = np.where(dust, np.clip(shade + 1, 3, 4), shade)
    for x, y in tex.poisson(T, T, 6.0, r):
        w = int(r.integers(1, 4))
        s = "alt" if r.random() < 0.35 else "main"
        for dx in range(w):
            slot[y, (x + dx) % T] = _sid(s)
            shade[y, (x + dx) % T] = 4 + (dx == 0)
        shade[(y + 1) % T, (x + 1) % T] = 1
    return _tcanvas(T, T, np.clip(shade, 0, 5), slot)


def mine_boards(seed: int = 771) -> Canvas:
    """A rough boardwalk over a drop: wide, uneven boards with dark gaps (the depth showing through), iron
    straps and nail heads."""
    r = tex.rng(seed)
    plank, gapw = 8, 2
    grain = tex.fbm(T, T, 8, r, octaves=3, aspect=8.0)
    shade = np.full((T, T), 3)
    shade[grain < 0.3] = 2
    shade[grain > 0.72] = 4
    slot = np.full((T, T), _sid("main"))
    for row in range(T // plank):
        y0 = row * plank
        sag = int(r.choice([0, 0, 1]))
        shade[y0:y0 + plank] += int(r.choice([-1, 0, 0]))
        shade[y0 + sag] = np.maximum(shade[y0 + sag], 4)
        for g in range(gapw):
            slot[y0 + plank - 1 - g] = _sid("grout")
            shade[y0 + plank - 1 - g] = 0
        x = int(r.integers(T))
        for _ in range(2):
            for yy in range(y0, y0 + plank - gapw):
                slot[yy, x % T] = _sid("grout")
                shade[yy, x % T] = 0
            x += int(r.integers(48, 72))
    for k in range(4):                                     # iron straps across the boards
        x0 = k * 32 + 14 + int(r.integers(-3, 4))
        for yy in range(T):
            if slot[yy, x0] != _sid("grout"):
                for dx in range(3):
                    slot[yy, (x0 + dx) % T] = _sid("metal")
                    shade[yy, (x0 + dx) % T] = 4 if dx == 0 else (3 if dx == 1 else 2)
                if yy % plank == 3:
                    shade[yy, (x0 + 1) % T] = 5
    return _tcanvas(T, T, np.clip(shade, 0, 5), slot)


def void_frames(seed: int = 781, frames: int = 8) -> list[Canvas]:
    """The top of a chasm: no floor, only darkness with mist sliding over it and the odd faint mote. Two fog layers
    drift in opposite directions by a whole period over the loop, so it never jumps."""
    r = tex.rng(seed)
    n1 = tex.fbm(T, T, 4, r, octaves=3)
    n2 = tex.fbm(T, T, 8, r, octaves=2)
    motes = tex.poisson(T, T, 22, r)
    step = T // frames
    out = []
    for f in range(frames):
        a = tex.wrap_roll(n1, f * step, 0)
        b = tex.wrap_roll(n2, -f * step, f * step // 2)
        fog = a * 0.6 + b * 0.4
        shade = np.zeros((T, T), int)
        shade = np.where(fog > 0.52, 1, shade)
        shade = np.where(fog > 0.68, 2, shade)
        ck = tex.checker(T, T)
        shade = np.where((fog > 0.47) & (fog <= 0.52) & ck, 1, shade)
        shade = np.where((fog > 0.63) & (fog <= 0.68) & ck, 2, shade)
        slot = np.full((T, T), _sid("main"))
        for k, (x, y) in enumerate(motes):
            ph = (f + k * 3) % frames
            if ph < 5:
                slot[(y - ph) % T, x] = _sid("glow")
                shade[(y - ph) % T, x] = 2 + (ph in (1, 2, 3)) + (ph == 2)
        out.append(_tcanvas(T, T, shade, slot))
    return out


# ------------------------------------------------------------------------------------------------------- sides
def dungeon_wall(seed: int = 801) -> Canvas:
    """Rough-hewn dungeon blocks: two courses per tile of height, uneven lengths, thick dark mortar, chipped arrises
    and soot and damp stains."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    idx, dl, dr, dt, db, n = _courses(r, w, h, 16, (16, 24, 32, 40))
    tone = r.choice([-1, 0, 0, 1], n)
    surf = tex.fbm(w, h, 8, r, octaves=3, aspect=0.25)
    shade = 3 + tone[idx] + (surf > 0.72) - (surf < 0.25)
    shade = np.where((dt <= 1) & (dl > 0), shade + 1, shade)
    shade = np.where((dl == 1) & (dt > 1), shade + (surf > 0.4), shade)
    shade = np.where(((db == 2) | (dr == 2)) & (dt > 1), shade - 1, shade)
    mortar = (db <= 1) | (dr <= 1) | (dl == 0)
    rnd = r.random((h, w))
    mortar |= (((dt == 0) & (dl <= 2)) | ((db == 2) & (dr <= 3))) & (rnd < 0.6)
    slot = np.where(mortar, _sid("grout"), _sid("main"))
    shade = np.where(mortar, np.where(np.roll(~mortar, 1, 0), 0, 1), shade)
    stains = tex.fbm(w, h, 4, r, octaves=2, aspect=0.25)
    shade = np.where((stains < 0.22) & ~mortar, shade - 1, shade)
    for k in range(n):
        if r.random() < 0.25:
            ys, xs = np.nonzero((idx == k) & ~mortar)
            if len(xs):
                q = int(r.integers(len(xs)))
                _crack_walk(shade, r, int(xs[q]), int(ys[q]), int(r.integers(3, 8)), ~mortar)
    return _tcanvas(w, h, np.clip(shade, 0, 5), slot)


def cave_wall(seed: int = 811) -> Canvas:
    """Cave rock in vertical sheets: faceted, streaked by flowstone, split by tall cracks."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    streak = tex.fbm(h, w, 4, r, octaves=3, aspect=4.0).T
    pts = tex.jittered_points(w, h, 32, 0.4, r, cell_h=16)
    f1, f2, idx = tex.voronoi(w, h, pts, sx=1.0, sy=0.45)
    gx = r.uniform(-1, 1, len(pts))
    ys, xs = np.mgrid[0:h, 0:w]
    plane = gx[idx] * tex.wrapped_delta(xs, pts[idx, 0], w) / 14
    v = plane + (streak - 0.5) * 2.0
    shade = 3 + (v > 0.3).astype(int) + (v > 0.8) - (v < -0.3) - (v < -0.8)
    gap = f2 - f1
    edge = (gap < 0.9) & (tex.fbm(w, h, 8, r, octaves=1) > 0.35)
    shade = np.where(edge, 1, shade)
    shade = np.where(np.roll(edge, -1, 1) & ~edge, np.minimum(shade + 1, 5), shade)   # lit lip left of a crack
    flow = tex.fbm(h, w, 16, r, octaves=1, aspect=6.0).T > 0.8
    shade = np.where(flow & ~edge, np.minimum(shade + 1, 5), shade)
    shade = tex.despeckle(np.clip(shade, 0, 5), 1)
    return _tcanvas(w, h, shade)


def mine_wall(seed: int = 821) -> Canvas:
    """A mine gallery: cave rock shored up with a timber post every two tiles (alt) and the odd iron spike."""
    c = cave_wall(seed)
    shade, slot = c.shade.astype(int), c.slot.copy()
    for x0 in (20, 84):
        for y in range(SIDE_H):
            for dx in range(6):
                X = x0 + dx
                slot[y, X] = _sid("alt")
                shade[y, X] = (4 if dx == 0 else 3 if dx < 3 else 2 if dx < 5 else 1)
            if y % 9 == 4:
                shade[y, x0 + 2] = 2
        shade[:, x0 + 6] = np.minimum(shade[:, x0 + 6], 1)                    # the post's shadow on the rock
    return _tcanvas(T, SIDE_H, np.clip(shade, 0, 5), slot)


def chasm_side(seed: int = 831) -> Canvas:
    """The wall of a chasm, two tiles deep: cave rock that dissolves into the void below (dithered fade). V is
    clamped, not repeated: anything deeper is void."""
    top = cave_wall(seed)
    h = SIDE_H * 2
    shade = np.vstack([top.shade, top.shade]).astype(int)
    slot = np.vstack([top.slot, top.slot])
    ys, xs = np.mgrid[0:h, 0:T]
    t = ys / (h - 1)
    bayer = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16
    th = bayer[ys % 4, xs % 4]
    darken = (t * 3.2 - th).astype(int).clip(0, 3)
    shade = np.where(darken > 0, shade - darken, shade)
    void = t * 1.25 > 0.55 + th * 0.6
    slot = np.where(void, _sid("accent"), slot)
    shade = np.where(void, np.where(t > 0.85, 0, 1), np.clip(shade, 0, 5))
    return _tcanvas(T, h, np.clip(shade, 0, 5), slot)


# -------------------------------------------------------------------------------------------------------- lips
def stalactite_lip(seed: int = 841) -> Canvas:
    """A cave ledge's overhang: a lumpy rock rim with stalactites of every length hanging from it, each lit on its
    left flank and wet at the tip."""
    r = tex.rng(seed)
    w, h = T, 22
    c = Canvas(w, h, TS)
    rim = tex.fbm(w, 16, 16, r, octaves=2)[0]
    for x in range(w):
        depth = 3 + int(rim[x] * 5)
        for y in range(depth):
            c.px(x, y, "main", 4 if y == 0 else (1 if y == depth - 1 else (2 if y == depth - 2 else 3)))
    x = int(r.integers(0, 4))
    while x < w - 3:
        width = int(r.choice([4, 5, 5, 6, 7]))
        length = int(r.integers(8, h - 2)) if r.random() < 0.45 else int(r.integers(3, 8))
        d0 = 2 + int(rim[min(w - 1, x + width // 2)] * 5)
        cx = x + width / 2
        for y in range(d0, d0 + length):
            t = (y - d0) / max(1, length)
            half = (width / 2) * (1 - t) ** 0.7
            for xx in range(int(math.floor(cx - half)), int(math.ceil(cx + half))):
                if 0 <= xx < w:
                    u = (xx + 0.5 - (cx - half)) / max(0.5, 2 * half)
                    s = 4 if u < 0.3 else (3 if u < 0.65 else 2)
                    if y == d0 + length - 1 or (t > 0.75 and u > 0.5):
                        s -= 1
                    c.px(xx, y, "main", max(1, s))
        if length > 5:
            c.px(int(cx), d0 + length, "alt", 4)                          # a drop of water at the tip
        x += width + int(r.integers(0, 5))
    return c


def beam_lip(seed: int = 851) -> Canvas:
    """A mine gallery's header beam over its posts: a squared timber with a lit top arris and bolt heads."""
    r = tex.rng(seed)
    w, h = T, 9
    c = Canvas(w, h, TS)
    grain = tex.fbm(w, 16, 16, r, octaves=2, aspect=4.0)[:h]
    for y in range(7):
        for x in range(w):
            s = 4 if y == 0 else (2 if y >= 5 else 3)
            if 0 < y < 5 and grain[y, x] < 0.3:
                s -= 1
            c.px(x, y, "alt", s)
    for x in (22, 86):
        c.px(x + 2, 3, "metal", 4)
        c.px(x + 2, 4, "metal", 2)
    for x in range(w):
        c.px(x, 7, "alt", 1)
        if x % 64 in range(20, 27):
            c.px(x, 8, "alt", 1)
    return c


# ------------------------------------------------------------------------------------------------------ decals
def _decal(w: int = DECAL, h: int = DECAL) -> Canvas:
    return Canvas(w, h, TS)


def rails(*, kind: str = "straight") -> Canvas:
    """Mine rails in tile-local UV, running along U (transpose for V): sleepers every 8 texels so neighbouring
    tiles continue the track, two rails with a lit top. `curve` turns from -U to +V; `end` adds a buffer stop."""
    c = _decal()
    if kind == "curve":
        # a quarter turn about the tile's (-U, +V) corner; the track's centre line has radius 16
        for y in range(DECAL):
            for x in range(DECAL):
                d = math.hypot(x + 0.5, DECAL - y - 0.5)
                s = (math.pi / 2 - math.atan2(DECAL - y - 0.5, x + 0.5)) * 16       # arc length from the -U edge
                if 6 <= d <= 26 and 2 <= s % 8 < 6:
                    c.px(x, y, "alt", 4 if d < 7 else (2 if d > 25 or s % 8 >= 5 else 3))
        for y in range(DECAL):
            for x in range(DECAL):
                d = math.hypot(x + 0.5, DECAL - y - 0.5)
                for rr in (10.5, 21.5):
                    if abs(d - rr) < 0.8:
                        c.px(x, y, "metal", 4 if d < rr else 3)
                    elif 0.8 <= d - rr < 1.7:
                        c.px(x, y, "grout", 0)
        return c
    for x0 in range(2, DECAL, 8):                                              # sleepers
        for y in range(6, 27):
            for dx in range(4):
                s = 4 if y == 6 else (2 if y == 26 else 3)
                if dx == 3:
                    s -= 1
                c.px(x0 + dx, y, "alt", s)
        c.px(x0 + 1, 9, "metal", 3)
        c.px(x0 + 1, 22, "metal", 3)
    for y0 in (10, 21):                                                        # rails
        for x in range(DECAL):
            c.px(x, y0 - 1, "metal", 5 if x % 6 == 0 else 4)
            c.px(x, y0, "metal", 3)
            c.px(x, y0 + 1, "grout", 0)
    if kind == "end":
        for x in range(DECAL - 7, DECAL - 1):
            for y in range(5, 28):
                c.px(x, y, "alt", 4 if x == DECAL - 7 else 3 if x < DECAL - 3 else 2)
        for x in range(DECAL - 1, DECAL):
            for y in range(DECAL):
                c.clear(x, y)
    return c


def carpet(*, end: bool = False) -> Canvas:
    """A carpet runner in tile-local UV along U: a border, a field with a lozenge every half tile (so it runs on
    without a seam from tile to tile) and, for the end piece, a fringe. Transpose it for a runner along V."""
    c = _decal()
    y0, y1 = 5, 26
    for x in range(DECAL):
        for y in range(y0, y1 + 1):
            if y in (y0, y1):
                c.px(x, y, "alt", 2 if y == y1 else 3)
            elif y in (y0 + 1, y1 - 1):
                c.px(x, y, "accent", 4 if y == y0 + 1 else 3)
            elif y in (y0 + 2, y1 - 2):
                c.px(x, y, "alt", 3)
            else:
                cy = (y0 + y1) / 2
                lx = (x % 16) - 7.5
                d = abs(lx) + abs(y - cy) * 1.2
                if d < 3:
                    c.px(x, y, "accent", 4)
                elif d < 5:
                    c.px(x, y, "alt", 4)
                elif 6.5 < d < 7.6:
                    c.px(x, y, "accent", 3)
                else:
                    c.px(x, y, "alt", 3 if (x + y) % 7 else 2)
        if x % 4 == 0:
            c.px(x, y0 + 2, "accent", 5)
            c.px(x + 2, y1 - 2, "accent", 4)
    for x in range(DECAL):
        c.px(x, y1 + 1, "grout", 0)
    if end:
        for x in range(DECAL - 4, DECAL):
            for y in range(DECAL):
                c.clear(x, y)
        for y in range(y0, y1 + 1):
            c.px(DECAL - 5, y, "accent", 2)
            if y % 2 == 0:
                c.px(DECAL - 4, y, "accent", 4)
                c.px(DECAL - 3, y, "accent", 3)
                c.px(DECAL - 2, y + (y % 4 == 0), "accent", 3)
    return c


def rug() -> Canvas:
    """A 2x2-tile rug (64x64, tile-local to its four tiles: split it into quarters): a medallion, a border of
    stepped lozenges and a fringe on two ends."""
    c = _decal(64, 64)
    for y in range(4, 60):
        for x in range(1, 63):
            bx = min(x - 1, 62 - x)
            by = min(y - 4, 59 - y)
            b = min(bx, by)
            cx, cy = x - 31.5, y - 31.5
            d = abs(cx) + abs(cy)
            if b == 0:
                c.px(x, y, "alt", 1)
            elif b < 3:
                c.px(x, y, "accent", 4 if b == 1 else 3)
            elif b < 8:
                s = ((x if by >= bx else y) // 4) % 2
                c.px(x, y, "alt", 4 if s and b in (4, 5, 6) else 2)
            elif b == 8:
                c.px(x, y, "accent", 3)
            elif d < 6:
                c.px(x, y, "accent", 5 if d < 2.5 else 4)
            elif d < 9:
                c.px(x, y, "alt", 4)
            elif d < 11:
                c.px(x, y, "accent", 3)
            elif d < 22 and int(d) % 4 == 0:
                c.px(x, y, "alt", 4)
            else:
                c.px(x, y, "main", 3 if (x // 2 + y // 2) % 5 else 2)
    for x in range(2, 62, 2):
        for y in (2, 3, 60, 61):
            c.px(x, y, "accent", 4 if y in (2, 61) else 3)
    return c


def rune_circle_frames(frames: int = 8) -> list[Canvas]:
    """A summoning circle burned into the floor: two rings, a star and rune ticks. It pulses, and a bright arc
    runs round the outer ring once per loop."""
    out = []
    ys, xs = np.mgrid[0:DECAL, 0:DECAL]
    dx, dy = xs + 0.5 - 16, ys + 0.5 - 16
    d = np.hypot(dx, dy)
    ang = (np.arctan2(dy, dx) + 2 * np.pi) % (2 * np.pi)
    angs = [-math.pi / 2 + k * 4 * math.pi / 5 for k in range(6)]
    pts = [(16 + 11 * math.cos(a), 16 + 11 * math.sin(a)) for a in angs]
    tmp = _decal()
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        tmp.line(ax, ay, bx, by, "glow", 3)
    star = tmp.slot > 0
    for f in range(frames):
        c = _decal()
        pulse = 0.5 + 0.5 * math.cos(2 * math.pi * f / frames)
        base = 3 + int(round(pulse))
        outer = np.abs(d - 13.5) < 0.8
        inner = np.abs(d - 11) < 0.6
        ticks = (d > 11.6) & (d < 13) & ((ang * 12 / np.pi).astype(int) % 2 == 0)
        head = ((ang - 2 * np.pi * f / frames) % (2 * np.pi)) < 0.9
        c.slot[outer | inner | ticks | star] = _sid("glow")
        sh = np.full((DECAL, DECAL), base)
        sh = np.where(inner | ticks, base - 1, sh)
        sh = np.where(outer & head, 5, sh)
        sh = np.where(star, base, sh)
        c.shade[:] = np.clip(sh, 0, 5)
        scorch = (d < 15.5) & (c.slot == 0) & ((xs + ys) % 2 == 0) & (d > 14.2)
        c.slot[scorch] = _sid("grout")
        c.shade[scorch] = 0
        out.append(c)
    return out


def bone_scatter(seed: int = 901) -> Canvas:
    """Loose bones: ribs, a long bone or two, a jaw."""
    r = tex.rng(seed)
    c = _decal()
    for _ in range(5):
        x, y = r.uniform(5, 25), r.uniform(6, 26)
        a = r.uniform(0, math.pi)
        L = r.uniform(4, 8)
        ex, ey = x + math.cos(a) * L, y + math.sin(a) * L * 0.6
        c.line(x, y, ex, ey, "main", 4)
        c.px(x - 1, y, "main", 4)
        c.px(ex + 1, ey, "main", 3)
        c.line(x + 1, y + 1, ex + 1, ey + 1, "grout", 0)
    for k in range(3):                                                         # ribs: short arcs
        x0, y0 = 8 + k * 3 + int(r.integers(0, 3)), 18 + int(r.integers(0, 4))
        for t in range(5):
            c.px(x0 + t, y0 - int(2 * math.sin(t / 4 * math.pi)), "main", 4 if t < 2 else 3)
    return c


def blood_trail(seed: int = 911) -> Canvas:
    """A dragged smear with drops, across the tile along U."""
    r = tex.rng(seed)
    c = _decal()
    y = 16.0
    for x in range(2, 30):
        y += r.normal(0, 0.35)
        wdt = 1.5 + 1.5 * math.sin(x / 30 * math.pi) + r.uniform(0, 0.8)
        for yy in range(int(y - wdt), int(y + wdt) + 1):
            if r.random() < 0.85:
                c.px(x, yy, "accent", 2 if yy > y else 3)
        c.px(x, int(y - wdt), "accent", 4)
    for _ in range(6):
        c.px(r.uniform(2, 30), r.uniform(4, 28), "accent", 2)
    return c


# ------------------------------------------------------------------------------------------------- wall decals
def _wall(w: int = WALL_W, h: int = WALL_H) -> Canvas:
    return Canvas(w, h, TS)


def _flame(c: Canvas, x: float, y: float, h: float, w: float, ph: float, slot: str = "glow") -> None:
    """A small teardrop flame with a hot core: base at (x, y), tip `h` above, swaying with phase `ph`."""
    sway = math.sin(ph) * 1.2
    hh = h * (1 + 0.18 * math.sin(ph * 2 + 1))
    c.polygon([(x - w, y), (x + w, y), (x + w * 0.7, y - hh * 0.45), (x + sway, y - hh), (x - w * 0.8, y - hh * 0.4)],
              slot, 3)
    c.polygon([(x - w * 0.5, y), (x + w * 0.5, y), (x + sway * 0.6, y - hh * 0.65)], slot, 4)
    c.px(x, y - 1, slot, 5)
    c.px(x - 1 + (sway > 0), y - 2, slot, 5)


def sconce_frames(frames: int = 8) -> list[Canvas]:
    """A wall torch: an iron back plate and cradle holding a pitch torch, its flame flickering. Hung so the flame
    sits a tile and a half above the floor."""
    out = []
    for f in range(frames):
        c = _wall()
        c.rect(14, 22, 17, 30, "metal", 3)                                    # back plate
        c.rect(14, 22, 14, 30, "metal", 4)
        c.rect(17, 22, 17, 30, "metal", 2)
        c.px(15, 24, "metal", 5)
        c.px(15, 28, "metal", 5)
        c.line(16, 27, 20, 23, "metal", 2)                                     # arm
        c.rect(12, 18, 19, 20, "metal", 3)                                    # cradle ring
        c.rect(12, 18, 19, 18, "metal", 4)
        c.rect(14, 12, 17, 21, "alt", 3)                                      # torch head (pitch-wrapped)
        c.rect(14, 12, 14, 21, "alt", 4)
        c.rect(17, 12, 17, 21, "alt", 1)
        c.rect(15, 21, 16, 26, "alt", 2)
        ph = 2 * math.pi * f / frames
        _flame(c, 15.5, 12.5, 10 + 2 * math.sin(ph * 3), 3.2, ph)
        out.append(c)
    return out


def shackles() -> Canvas:
    """Two ring bolts with chains hanging to open manacles."""
    c = _wall()
    for x0, L in ((9, 22), (22, 18)):
        c.rect(x0 - 1, 9, x0 + 1, 11, "metal", 2)
        c.px(x0 - 1, 9, "metal", 4)
        for k in range(L):
            y = 12 + k
            sag = int(round(math.sin(k / L * math.pi) * (1 if x0 < 16 else -1)))
            if k % 2 == 0:
                c.px(x0 + sag, y, "metal", 4)
                c.px(x0 + sag + 1, y, "metal", 2)
            else:
                c.px(x0 + sag, y, "metal", 3)
        y = 12 + L
        c.rect(x0 - 2, y, x0 + 2, y + 3, "metal", 3)
        c.rect(x0 - 1, y + 1, x0 + 1, y + 2, "grout", 0)
        c.rect(x0 - 2, y, x0 + 2, y, "metal", 5)
    return c


def wall_banner(emblem: str = "skull") -> Canvas:
    """A cloth banner hung from an iron rod, swallow-tailed, with a sewn emblem (alt) and a trim (accent)."""
    c = _wall()
    c.rect(5, 4, 26, 5, "metal", 3)
    c.px(5, 4, "metal", 5)
    c.px(26, 5, "metal", 1)
    for y in range(6, 42):
        for x in range(7, 25):
            tail = y > 34 and abs(x - 15.5) < (y - 34) * 1.1
            if tail:
                continue
            s = 3 + (x < 9) - (x > 22) - (y > 36)
            if (x - 7) % 6 == 5 and y < 34:
                s -= 1                                                        # vertical folds
            c.px(x, y, "main", s)
        if y < 35:
            c.px(7, y, "accent", 4)
            c.px(24, y, "accent", 2)
    c.rect(7, 6, 24, 7, "accent", 4)
    if emblem == "skull":
        for dy, row in enumerate(TR.SKULL):
            for dx, ch in enumerate(row):
                if ch == "#":
                    c.px(13 + dx, 15 + dy, "alt", 4 if dy < 2 else 3)
                elif ch in "0":
                    c.px(13 + dx, 15 + dy, "main", 0)
                elif ch == "t":
                    c.px(13 + dx, 15 + dy, "alt", 5)
        c.line(11, 23, 20, 28, "alt", 3)
        c.line(11, 28, 20, 23, "alt", 3)
    else:                                                                     # a crowned shield
        c.polygon([(11, 14), (20, 14), (20, 22), (15.5, 27), (11, 22)], "alt", 3)
        c.rect(11, 14, 20, 14, "alt", 5)
        c.rect(14, 15, 16, 25, "accent", 4)
        for x in (12, 15, 18):
            c.rect(x, 10, x + 1, 12, "accent", 4)
        c.rect(12, 12, 19, 13, "accent", 3)
    return c


def cobweb() -> Canvas:
    """A corner cobweb for the top-left corner of a wall face (mirror it for the right): threads radiating from
    the corner, sagging spirals between them."""
    c = _wall(32, 32)
    spokes = [0.05, 0.38, 0.75, 1.12, 1.5]
    for a in spokes:
        for t in range(0, 30):
            x, y = t * math.cos(a), t * math.sin(a)
            if x < 31 and y < 31:
                c.px(x, y, "accent", 4 if t < 10 else 3)
    for rr in (7, 12, 17, 22, 27):
        for a0, a1 in zip(spokes, spokes[1:]):
            for k in range(9):
                t = k / 8
                a = a0 + (a1 - a0) * t
                sag = math.sin(t * math.pi) * rr * 0.12
                x, y = (rr - sag) * math.cos(a), (rr - sag) * math.sin(a)
                c.px(x, y, "accent", 3 if (k + rr) % 3 else 4)
    c.px(9, 9, "accent", 5)
    return c


def wall_crack() -> Canvas:
    r = tex.rng(921)
    c = _wall()
    x, y = 14.0, 2.0
    for _ in range(40):
        c.px(x, y, "grout", 0)
        c.px(x + 1, y + 1, "main", 4)
        x += r.normal(0, 0.8)
        y += 1
        if y > 44:
            break
        if r.random() < 0.08:
            bx, by = x, y
            for _ in range(int(r.integers(4, 9))):
                bx += r.choice([-1, 1])
                by += 1
                c.px(bx, by, "grout", 1)
    return c


def moss_drip() -> Canvas:
    """Moss creeping over the top of a wall face, with damp runs streaking down from it."""
    r = tex.rng(931)
    c = _wall()
    for x in range(WALL_W):
        d = 3 + int(r.integers(0, 4)) + (int(r.integers(3, 9)) if r.random() < 0.15 else 0)
        for y in range(d):
            c.px(x, y, "alt", 4 if y == 0 else (2 if y == d - 1 else 3))
        if r.random() < 0.25:
            for y in range(d, d + int(r.integers(6, 26))):
                c.px(x, y, "grout", 1 if y % 5 else 0)
    return c


def wall_runes_frames(frames: int = 8) -> list[Canvas]:
    """A column of three runes cut into the wall that glow and fade in turn."""
    out = []
    for f in range(frames):
        c = _wall()
        for k, g in enumerate((0, 3, 7)):
            ph = (f / frames - k / 3) % 1.0
            s = 5 if ph < 0.2 else (4 if ph < 0.45 else 3)
            for gy, row in enumerate(RUNES[g]):
                for gx, ch in enumerate(row):
                    if ch == "#":
                        c.px(12 + gx, 4 + k * 13 + gy, "glow", s)
                        c.px(13 + gx, 5 + k * 13 + gy, "grout", 0) if not c.slot[5 + k * 13 + gy, 13 + gx] else None
        out.append(c)
    return out


# ---------------------------------------------------------------------------------------------- texture catalogue
@dataclass
class DTex:
    id: str
    kind: str                                    # top | side | lip | decal | wall
    make: Callable[[], Canvas | list[Canvas]]
    variants: dict[str, dict[str, str]]
    fps: float = 0.0
    tags: list[str] = field(default_factory=list)
    note: str = ""

    def frames(self) -> list[Canvas]:
        m = self.make()
        return m if isinstance(m, list) else [m]


def _v(**slots: str) -> dict[str, str]:
    return {k: (v if "/" in v else f"dungeon/{v}") for k, v in slots.items()}


_FLAG = {"grey": _v(main="crypt", grout="grout", alt="moss", accent="blood"),
         "dark": _v(main="crypt_dark", grout="grout", alt="moss", accent="blood"),
         "warm": _v(main="crypt_warm", grout="grout", alt="moss", accent="blood")}
_FLAG_CRACKED = {k: {**v, "accent": "dungeon/gravel"} for k, v in _FLAG.items()}
_SLABS = "same slab layout as dungeon_floor: swap per tile freely"

TEXTURES: list[DTex] = [
    DTex("dungeon_floor", "top", dungeon_floor, _FLAG, tags=["stone"], note=_SLABS),
    DTex("dungeon_floor_mossy", "top", lambda: dungeon_floor("mossy"), _FLAG, tags=["stone"], note=_SLABS),
    DTex("dungeon_floor_cracked", "top", lambda: dungeon_floor("cracked"), _FLAG_CRACKED, tags=["stone"],
         note=_SLABS),
    DTex("dungeon_floor_bloody", "top", lambda: dungeon_floor("bloody"), _FLAG, tags=["stone"], note=_SLABS),
    DTex("crypt_tiles", "top", crypt_tiles, {
        "dormant": _v(main="crypt", alt="crypt_dark", grout="grout", glow="rune_dormant"),
        "arcane": _v(main="crypt", alt="crypt_dark", grout="grout", glow="rune_arcane"),
        "fel": _v(main="crypt", alt="crypt_dark", grout="grout", glow="rune_fel"),
        "marble": _v(main="marble", alt="slate", grout="grout", glow="brass")}, tags=["stone", "runes"]),
    DTex("cave_floor", "top", cave_floor, {
        "brown": _v(main="cave", alt="gravel"), "cool": _v(main="cave_cool", alt="cave"),
        "deep": _v(main="cave_deep", alt="cave_cool")}, tags=["rock"]),
    DTex("wet_stone", "top", wet_stone_frames, {
        "cave": _v(main="wet", grout="cave_deep", accent="pool"),
        "glow": _v(main="cave_cool", grout="cave_deep", accent="pool_glow")}, fps=8, tags=["stone", "water"]),
    DTex("mine_gravel", "top", mine_gravel, {"mine": _v(main="gravel", alt="ore", accent="coal")}, tags=["mine"]),
    DTex("mine_boards", "top", mine_boards, {"mine": _v(main="wood/oak", grout="void", metal="metal/iron")},
         tags=["mine", "wood"]),
    DTex("floorboards", "top", floorboards, {
        "oak": _v(main="oak_floor", metal="metal/iron"), "walnut": _v(main="walnut", metal="metal/iron"),
        "pale": _v(main="pale_floor", metal="metal/iron")}, tags=["interior", "wood"]),
    DTex("parquet", "top", parquet, {
        "oak": _v(main="oak_floor", alt="pale_floor"), "walnut": _v(main="walnut", alt="oak_floor")},
         tags=["interior", "wood"]),
    DTex("void", "top", void_frames, {"abyss": _v(main="void", glow="void_mote")}, fps=5, tags=["chasm"],
         note="impassable; sink it well below the floor so chasm sides show"),
    # sides
    DTex("dungeon_wall", "side", dungeon_wall, {
        "grey": _v(main="crypt", grout="grout"), "dark": _v(main="crypt_dark", grout="grout"),
        "warm": _v(main="crypt_warm", grout="grout")}, tags=["stone"]),
    DTex("cave_wall", "side", cave_wall, {"brown": _v(main="cave"), "cool": _v(main="cave_cool"),
                                          "deep": _v(main="cave_deep")}, tags=["rock"]),
    DTex("mine_wall", "side", mine_wall, {"mine": _v(main="cave", alt="wood/oak")}, tags=["mine"]),
    DTex("chasm_side", "side", chasm_side, {"brown": _v(main="cave", accent="void"),
                                            "cool": _v(main="cave_cool", accent="void")}, tags=["chasm"],
         note="128x64: V = depth below the top / 2 tiles, clamped; deeper than that is void"),
    # lips
    DTex("stalactite_lip", "lip", stalactite_lip, {"brown": _v(main="cave", alt="glass"),
                                                   "cool": _v(main="cave_cool", alt="glass")}, tags=["rock"]),
    DTex("beam_lip", "lip", beam_lip, {"mine": _v(alt="wood/oak", metal="metal/iron")}, tags=["mine"]),
    # ground decals
    DTex("rails", "decal", rails, {"iron": _v(alt="walnut", metal="metal/iron", grout="coal")}, tags=["mine"],
         note="runs along U; transpose for V"),
    DTex("rails_curve", "decal", lambda: rails(kind="curve"), {"iron": _v(alt="walnut", metal="metal/iron")},
         tags=["mine"], note="enters at -U, leaves at +V; mirror or rotate for the other turns"),
    DTex("rails_end", "decal", lambda: rails(kind="end"), {"iron": _v(alt="walnut", metal="metal/iron",
                                                                      grout="coal")}, tags=["mine"]),
    DTex("carpet", "decal", carpet, {
        "crimson": _v(alt="cloth/crimson", accent="metal/gold", grout="walnut"),
        "navy": _v(alt="cloth/navy", accent="metal/gold", grout="walnut"),
        "forest": _v(alt="cloth/forest", accent="cloth/ochre", grout="walnut"),
        "violet": _v(alt="cloth/royal_purple", accent="cloth/ivory", grout="walnut")}, tags=["interior"],
         note="runner along U, continues tile to tile; transpose for V"),
    DTex("carpet_end", "decal", lambda: carpet(end=True), {
        "crimson": _v(alt="cloth/crimson", accent="metal/gold", grout="walnut"),
        "navy": _v(alt="cloth/navy", accent="metal/gold", grout="walnut"),
        "forest": _v(alt="cloth/forest", accent="cloth/ochre", grout="walnut"),
        "violet": _v(alt="cloth/royal_purple", accent="cloth/ivory", grout="walnut")}, tags=["interior"],
         note="the runner's +U end, fringed"),
    DTex("rug", "decal", rug, {
        "crimson": _v(main="cloth/wine", alt="cloth/crimson", accent="metal/gold"),
        "teal": _v(main="cloth/navy", alt="cloth/teal", accent="cloth/ivory"),
        "ochre": _v(main="cloth/rust", alt="cloth/ochre", accent="cloth/ivory")}, tags=["interior"],
         note="64x64 over 2x2 tiles: each tile takes its quarter"),
    DTex("rune_circle", "decal", rune_circle_frames, {
        "fel": _v(glow="rune_fel", grout="coal"), "blood": _v(glow="rune_blood", grout="coal"),
        "arcane": _v(glow="rune_arcane", grout="coal")}, fps=8, tags=["runes", "light"]),
    DTex("bone_scatter", "decal", bone_scatter, {"bone": _v(main="bone", grout="coal")}, tags=["crypt"]),
    DTex("blood_trail", "decal", blood_trail, {"red": _v(accent="blood")}, tags=["crypt"]),
    # wall decals
    DTex("sconce", "wall", sconce_frames, {"iron": _v(metal="metal/dark", alt="walnut", glow="flame")}, fps=10,
         tags=["light"]),
    DTex("shackles", "wall", shackles, {"iron": _v(metal="metal/iron", grout="coal")}),
    DTex("wall_banner", "wall", wall_banner, {
        "crimson": _v(main="cloth/crimson", alt="bone", accent="metal/gold", metal="metal/dark"),
        "violet": _v(main="cloth/royal_purple", alt="bone", accent="metal/gold", metal="metal/dark"),
        "black": _v(main="cloth/charcoal", alt="bone", accent="cloth/wine", metal="metal/dark")}),
    DTex("wall_crest", "wall", lambda: wall_banner("crest"), {
        "navy": _v(main="cloth/navy", alt="cloth/ivory", accent="metal/gold", metal="metal/dark"),
        "crimson": _v(main="cloth/crimson", alt="cloth/ivory", accent="metal/gold", metal="metal/dark")}),
    DTex("cobweb", "wall", cobweb, {"web": _v(accent="web")}, note="top-left corner of a face; mirror for the right"),
    DTex("wall_crack", "wall", wall_crack, {"grey": _v(main="crypt", grout="coal")}),
    DTex("moss_drip", "wall", moss_drip, {"moss": _v(alt="moss", grout="coal")}),
    DTex("wall_runes", "wall", wall_runes_frames, {
        "fel": _v(glow="rune_fel", grout="coal"), "arcane": _v(glow="rune_arcane", grout="coal"),
        "blood": _v(glow="rune_blood", grout="coal")}, fps=6, tags=["runes", "light"]),
]


@lru_cache(maxsize=None)
def _tframes(tid: str) -> list[Canvas]:
    return next(x for x in TEXTURES if x.id == tid).frames()


@lru_cache(maxsize=None)
def tex_rgba(tid: str, variant: str | None = None, frame: int = 0) -> np.ndarray:
    """A texture frame as float RGBA, for the iso renderer (cached)."""
    t = next(x for x in TEXTURES if x.id == tid)
    v = t.variants[variant] if variant else next(iter(t.variants.values()))
    fr = _tframes(tid)
    return np.asarray(fr[frame % len(fr)].render(ramps(v))).astype(np.float32)


# ================================================================================================================
# Props
# ================================================================================================================
PS = SlotTable()
for _s in ("stone", "wood", "metal", "gold", "cloth1", "cloth2", "accent", "bone", "paper", "glass", "liquid",
           "glow", "flame", "dark", "wax"):
    PS.id(_s)
EMISSIVE = {"glow", "flame"}
"""Slots that make their own light: never outlined, and left bright by the darkness pass."""

LIGHT = np.array([0.25, 0.5, 0.83])
LIGHT /= np.linalg.norm(LIGHT)
"""The key light in prop space (u right-down, v left-down, z up): it gives a box's top shade 4, its +V face (the
screen-left one) 3 and its +U face 2, the same split the terrain renderer uses for tops and sides."""


class Solid:
    """A tiny z-buffered splat renderer for props drawn in the map's own projection.

    Prop space is u (towards screen lower-right), v (towards screen lower-left) and z (up), in sprite pixels: a tile
    is 16 x 16 and a height step is 8. A point lands at x = ax + u - v, y = ay + (u + v) / 2 - z, so (ax, ay) is the
    tile centre on the ground: the prop's anchor. Surfaces are sampled densely, depth-tested (nearer = larger
    u + v + z) and shaded from their normal against LIGHT, quantised to the ramp with a dithered terminator. Every
    prop therefore agrees on light and perspective with the terrain blocks it stands on."""

    def __init__(self, c: Canvas, ax: float, ay: float):
        self.c, self.ax, self.ay = c, ax, ay
        self.z = np.full((c.h, c.w), -1e9)

    def xy(self, u: float, v: float, z: float = 0.0) -> tuple[float, float]:
        return self.ax + u - v, self.ay + (u + v) / 2 - z

    def plot(self, P: np.ndarray, N: np.ndarray | None, slot: str, *, shade: int | None = None, base: float = 0.0,
             bias: float = 0.0, lo: int = 1, hi: int = 5, dither: bool = True) -> None:
        P = np.atleast_2d(np.asarray(P, float))
        x = np.floor(self.ax + P[:, 0] - P[:, 1] + 0.5).astype(int)
        y = np.floor(self.ay + (P[:, 0] + P[:, 1]) / 2 - P[:, 2] + 0.5).astype(int)
        d = P.sum(1) + bias
        ok = (x >= 0) & (x < self.c.w) & (y >= 0) & (y < self.c.h)
        if shade is None:
            lam = np.clip(np.asarray(N, float) @ LIGHT, 0, 1)
            sv = 1.2 + 3.6 * lam + base
            s = np.floor(sv).astype(int)
            frac = sv - s
            up = frac > 0.62
            if dither:
                up |= (frac > 0.38) & ((x + y) % 2 == 0)
            else:
                up |= frac > 0.5
            s = s + up
        else:
            s = np.broadcast_to(np.asarray(shade), (len(P),)).astype(int)
        s = np.clip(s, lo, hi)
        x, y, d, s = x[ok], y[ok], d[ok], s[ok]
        if not len(x):
            return
        pix = y * self.c.w + x
        order = np.lexsort((d, pix))
        pix, d, s = pix[order], d[order], s[order]
        last = np.r_[pix[1:] != pix[:-1], True]
        pix, d, s = pix[last], d[last], s[last]
        yy, xx = pix // self.c.w, pix % self.c.w
        win = d >= self.z[yy, xx]
        yy, xx = yy[win], xx[win]
        self.z[yy, xx] = d[win]
        self.c.slot[yy, xx] = self.c.slots.id(slot)
        self.c.shade[yy, xx] = s[win]
        self.c.flags[yy, xx] = 0

    # ---------------------------------------------------------------------------------------------- primitives
    @staticmethod
    def _ab(a0, a1, b0, b1, step=0.35):
        a = np.arange(a0, a1 + 1e-6, step)
        b = np.arange(b0, b1 + 1e-6, step)
        A, B = np.meshgrid(a, b)
        return A.ravel(), B.ravel()

    def box(self, u0, v0, z0, u1, v1, z1, slot, *, base=0.0, top_slot=None, **kw) -> None:
        """An axis-aligned box: only its three visible faces (top, +V, +U) are sampled. Flat faces don't dither."""
        kw.setdefault("dither", False)
        A, B = self._ab(u0, u1, v0, v1)
        self.plot(np.c_[A, B, np.full_like(A, z1)], np.tile([0, 0, 1], (len(A), 1)), top_slot or slot, base=base,
                  **kw)
        A, B = self._ab(u0, u1, z0, z1)
        self.plot(np.c_[A, np.full_like(A, v1), B], np.tile([0, 1, 0], (len(A), 1)), slot, base=base, **kw)
        A, B = self._ab(v0, v1, z0, z1)
        self.plot(np.c_[np.full_like(A, u1), A, B], np.tile([1, 0, 0], (len(A), 1)), slot, base=base, **kw)

    def frustum(self, u, v, z0, z1, r0, r1, slot, *, cap=True, seg: int | None = None, lean=(0.0, 0.0), base=0.0,
                twist: float = 0.0, **kw) -> None:
        """A vertical cylinder, cone or frustum (radius r0 at z0 to r1 at z1). `seg` facets it (crystals);
        `lean` shears the top sideways (u, v per unit of height)."""
        cap_slot = kw.pop("cap_slot", slot)
        rmax = max(r0, r1, 0.3)
        th = np.linspace(0, 2 * math.pi, max(12, int(2 * math.pi * rmax * 3)) * (1 if not seg else 4),
                         endpoint=False) + twist
        zs = np.arange(z0, z1 + 1e-6, 0.3)
        TH, Z = np.meshgrid(th, zs)
        TH, Z = TH.ravel(), Z.ravel()
        t = (Z - z0) / max(1e-6, z1 - z0)
        R = r0 + (r1 - r0) * t
        if seg:
            k = np.floor((TH - twist) / (2 * math.pi) * seg)
            tc = (k + 0.5) * 2 * math.pi / seg + twist
            # flat facets: points on the chord, normal through the facet's centre
            half = math.pi / seg
            R = R * math.cos(half) / np.cos(TH - tc)
            nth = tc
        else:
            nth = TH
        if callable(base):
            base = base(Z)
        slope = (r0 - r1) / max(1e-6, z1 - z0)
        N = np.c_[np.cos(nth), np.sin(nth), np.full_like(TH, slope)]
        N /= np.linalg.norm(N, axis=1)[:, None]
        P = np.c_[u + R * np.cos(TH) + lean[0] * (Z - z0), v + R * np.sin(TH) + lean[1] * (Z - z0), Z]
        self.plot(P, N, slot, base=base, **kw)
        if cap and r1 > 0.2:
            rr = np.arange(0, r1 + 1e-6, 0.3)
            RR, TT = np.meshgrid(rr, th)
            RR, TT = RR.ravel(), TT.ravel()
            P = np.c_[u + RR * np.cos(TT) + lean[0] * (z1 - z0), v + RR * np.sin(TT) + lean[1] * (z1 - z0),
                      np.full_like(RR, z1)]
            cb = base if np.isscalar(base) else float(np.mean(base))
            self.plot(P, np.tile([0, 0, 1], (len(P), 1)), cap_slot, base=cb, **kw)

    def cyl(self, u, v, z0, z1, r, slot, **kw) -> None:
        self.frustum(u, v, z0, z1, r, r, slot, **kw)

    def ellipsoid(self, u, v, z, ru, rv, rz, slot, *, zmin=-1e9, zmax=1e9, base=0.0, **kw) -> None:
        """An ellipsoid (a sphere when the radii agree), optionally sliced to zmin..zmax."""
        n = max(16, int(max(ru, rv, rz) * 9))
        th = np.linspace(0, 2 * math.pi, n * 2, endpoint=False)
        ph = np.linspace(0, math.pi, n)
        TH, PH = np.meshgrid(th, ph)
        TH, PH = TH.ravel(), PH.ravel()
        nx, ny, nz = np.sin(PH) * np.cos(TH), np.sin(PH) * np.sin(TH), np.cos(PH)
        P = np.c_[u + ru * nx, v + rv * ny, z + rz * nz]
        N = np.c_[nx / ru, ny / rv, nz / rz]
        N /= np.linalg.norm(N, axis=1)[:, None]
        keep = (P[:, 2] >= zmin) & (P[:, 2] <= zmax)
        self.plot(P[keep], N[keep], slot, base=base, **kw)

    def sphere(self, u, v, z, r, slot, **kw) -> None:
        self.ellipsoid(u, v, z, r, r, r, slot, **kw)

    def quad(self, P0, P1, P2, slot, *, normal=None, **kw) -> None:
        """A parallelogram P0 + s (P1 - P0) + t (P2 - P0): planks, lids, pages, slanted slabs."""
        kw.setdefault("dither", False)
        P0, P1, P2 = (np.asarray(p, float) for p in (P0, P1, P2))
        a, b = P1 - P0, P2 - P0
        na, nb = np.linalg.norm(a), np.linalg.norm(b)
        S, Tt = self._ab(0, 1, 0, 1, step=0.3 / max(na, nb, 1e-6))
        P = P0 + np.outer(S, a) + np.outer(Tt, b)
        if normal is None:
            n = np.cross(a, b)
            n = n / (np.linalg.norm(n) + 1e-9)
            if n @ np.array([1, 1, 1]) < 0:
                n = -n
        else:
            n = np.asarray(normal, float)
        self.plot(P, np.tile(n, (len(P), 1)), slot, **kw)

    def tri(self, P0, P1, P2, slot, **kw) -> None:
        """A triangle (wings, gable points, blades): the parallelogram's lower half."""
        kw.setdefault("dither", False)
        P0, P1, P2 = (np.asarray(p, float) for p in (P0, P1, P2))
        a, b = P1 - P0, P2 - P0
        m = max(np.linalg.norm(a), np.linalg.norm(b), 1e-6)
        S, Tt = self._ab(0, 1, 0, 1, step=0.3 / m)
        k = S + Tt <= 1
        P = P0 + np.outer(S[k], a) + np.outer(Tt[k], b)
        n = np.cross(a, b)
        n = n / (np.linalg.norm(n) + 1e-9)
        if n @ np.array([1, 1, 1]) < 0:
            n = -n
        self.plot(P, np.tile(n, (len(P), 1)), slot, **kw)

    def prism(self, pts: list[tuple[float, float]], z0: float, z1: float, slot, *, base=0.0, **kw) -> None:
        """A vertical prism over a convex polygon in (u, v): coffins, hexagonal plinths."""
        kw.setdefault("dither", False)
        top_slot = kw.pop("top_slot", slot)
        pts = [np.array(p, float) for p in pts]
        cen = np.mean(pts, axis=0)
        for a, b in zip(pts, pts[1:] + pts[:1]):
            e = b - a
            n = np.array([e[1], -e[0], 0.0])
            if n[:2] @ (a - cen) < 0:
                n = -n
            n /= np.linalg.norm(n)
            self.quad((a[0], a[1], z0), (b[0], b[1], z0), (a[0], a[1], z1), slot, normal=n, base=base, **kw)
        us, vs = [p[0] for p in pts], [p[1] for p in pts]
        A, B = self._ab(min(us), max(us), min(vs), max(vs), step=0.3)
        inside = np.ones(len(A), bool)
        for a, b in zip(pts, pts[1:] + pts[:1]):
            e = b - a
            side = e[0] * (B - a[1]) - e[1] * (A - a[0])
            ref = e[0] * (cen[1] - a[1]) - e[1] * (cen[0] - a[0])
            inside &= side * ref >= 0
        P = np.c_[A[inside], B[inside], np.full(inside.sum(), z1)]
        self.plot(P, np.tile([0, 0, 1], (len(P), 1)), top_slot, base=base, **kw)

    def line(self, P0, P1, slot, shade: int, *, bias: float = 0.6, width: float = 0.0) -> None:
        P0, P1 = np.asarray(P0, float), np.asarray(P1, float)
        n = int(np.linalg.norm(P1 - P0) / 0.3) + 2
        t = np.linspace(0, 1, n)[:, None]
        P = P0 + t * (P1 - P0)
        self.plot(P, None, slot, shade=shade, bias=bias)

    def hcyl(self, axis: str, c, half: float, r: float, slot, *, caps: bool = True, base=0.0, **kw) -> None:
        """A cylinder lying along u or v (barrels on their side, logs, axles), centred at c = (u, v, z)."""
        cap_slot = kw.pop("cap_slot", slot)
        th = np.linspace(0, 2 * math.pi, max(16, int(r * 20)), endpoint=False)
        ls = np.arange(-half, half + 1e-6, 0.3)
        TH, L = np.meshgrid(th, ls)
        TH, L = TH.ravel(), L.ravel()
        a, b = np.cos(TH), np.sin(TH)
        if axis == "u":
            P = np.c_[c[0] + L, c[1] + r * a, c[2] + r * b]
            N = np.c_[np.zeros_like(a), a, b]
            capn = [1, 0, 0]
        else:
            P = np.c_[c[0] + r * a, c[1] + L, c[2] + r * b]
            N = np.c_[a, np.zeros_like(a), b]
            capn = [0, 1, 0]
        self.plot(P, N, slot, base=base, **kw)
        if caps:
            rr = np.arange(0, r + 1e-6, 0.3)
            RR, TT = np.meshgrid(rr, th)
            RR, TT = RR.ravel(), TT.ravel()
            if axis == "u":
                P = np.c_[np.full_like(RR, c[0] + half), c[1] + RR * np.cos(TT), c[2] + RR * np.sin(TT)]
            else:
                P = np.c_[c[0] + RR * np.cos(TT), np.full_like(RR, c[1] + half), c[2] + RR * np.sin(TT)]
            self.plot(P, np.tile(capn, (len(P), 1)), cap_slot, base=base, **kw)


def _pcanvas(w: int, h: int) -> Canvas:
    return Canvas(w, h, PS)


def _done(c: Canvas) -> Canvas:
    c.outline(skip=EMISSIVE)
    return c


def _pflame(c: Canvas, x: float, y: float, h: float, w: float, ph: float, slot: str = "flame") -> None:
    _flame(c, x, y, h, w, ph, slot)


def _embers(c: Canvas, x: float, y: float, f: int, n: int, seed: int, spread: float = 3.0, rise: float = 10.0,
            frames: int = 8) -> None:
    """Sparks rising from a fire: each starts low, drifts up and fades over the loop."""
    r = tex.rng(seed)
    for k in range(n):
        ph = (f / frames + k / n) % 1.0
        ex = x + r.normal(0, spread) + math.sin(ph * 6 + k) * 1.2
        ey = y - ph * rise
        if ph < 0.85:
            c.px(ex, ey, "flame", 5 if ph < 0.4 else 4)


def _new(w: int, h: int, ax: float, ay: float) -> tuple[Canvas, Solid]:
    c = _pcanvas(w, h)
    c.anchor = (int(ax), int(ay))                                    # type: ignore[attr-defined]
    c.lights = []                                                    # type: ignore[attr-defined]
    return c, Solid(c, ax, ay)


def _light(c: Canvas, S: Solid, u: float, v: float, z: float) -> None:
    """Records where this prop's light comes from (image pixels), for the manifest and the darkness pass."""
    x, y = S.xy(u, v, z)
    c.lights.append((round(x), round(y)))                            # type: ignore[attr-defined]


def _bone(S: Solid, a, b, r: float = 0.7) -> None:
    """A long bone: a shaft with knobbly ends."""
    a, b = np.asarray(a, float), np.asarray(b, float)
    n = int(np.linalg.norm(b - a) / 0.6) + 2
    for t in np.linspace(0, 1, n):
        S.sphere(*(a + (b - a) * t), r, "bone", base=-0.4)
    for e in (a, b):
        S.sphere(*(e + np.array([0.5, -0.5, 0.1])), r * 1.35, "bone", base=-0.3)
        S.sphere(*(e + np.array([-0.5, 0.5, 0.1])), r * 1.35, "bone", base=-0.3)


def _skull(c: Canvas, S: Solid, u: float, v: float, z: float, r: float = 2.0, *, glow: bool = False) -> None:
    """A skull facing the viewer: a cranium, a jaw and two dark (or glowing) sockets."""
    S.sphere(u, v, z, r, "bone")
    S.ellipsoid(u + 0.5 * r, v + 0.5 * r, z - 0.75 * r, 0.6 * r, 0.6 * r, 0.42 * r, "bone")
    for du, dv in ((0.82, 0.2), (0.2, 0.82)):
        x, y = S.xy(u + du * r, v + dv * r, z - 0.05 * r)
        c.px(x, y, "glow" if glow else "dark", 4 if glow else 0)
        if r > 1.8:
            c.px(x, y + 1, "glow" if glow else "dark", 3 if glow else 0)
    x, y = S.xy(u + 0.7 * r, v + 0.7 * r, z - 0.45 * r)
    c.px(x, y, "dark", 1)


# ---------------------------------------------------------------------------------------------------- crypt
def sarcophagus(open_: bool = False) -> Canvas:
    """A stone sarcophagus along U with carved panels and a gilt band. Closed, the lid carries an effigy holding a
    sword; open, the lid leans against the side and the occupant shows."""
    c, S = _new(48 if open_ else 32, 32, 24 if open_ else 16, 22 if open_ else 23)
    S.box(-8.5, -5, 0, 8.5, 5, 2, "stone", base=-0.5)
    S.box(-7.5, -4.2, 2, 7.5, 4.2, 10.5, "stone")
    for u0, u1 in ((-6.6, -0.9), (0.9, 6.6)):                       # recessed panels: shadowed top-left edges
        S.line((u0, 4.25, 8.6), (u1, 4.25, 8.6), "stone", 1)
        S.line((u0, 4.25, 4.0), (u0, 4.25, 8.6), "stone", 1)
        S.line((u0, 4.25, 4.0), (u1, 4.25, 4.0), "stone", 4)
        S.line((u1, 4.25, 4.0), (u1, 4.25, 8.6), "stone", 4)
    S.line((-7.5, 4.25, 9.8), (7.5, 4.25, 9.8), "gold", 3)
    S.line((7.55, -4.2, 9.8), (7.55, 4.2, 9.8), "gold", 2)
    if not open_:
        S.box(-8.2, -4.8, 10.5, 8.2, 4.8, 12.3, "stone", base=0.15)
        S.line((-8.2, 4.85, 12.3), (8.2, 4.85, 12.3), "gold", 4)
        S.sphere(-5.4, 0, 13.6, 1.7, "stone")
        S.ellipsoid(1.2, 0, 12.3, 5.4, 2.4, 1.5, "stone", zmin=12.3)
        S.ellipsoid(6.6, -1.0, 12.8, 0.9, 0.8, 0.8, "stone")
        S.ellipsoid(6.6, 1.0, 12.8, 0.9, 0.8, 0.8, "stone")
        S.line((-2.6, 0, 14.2), (6.4, 0, 13.6), "metal", 4)
        S.line((-2.0, -1.8, 14.2), (-2.0, 1.8, 14.2), "gold", 4)
        S.sphere(-3.2, 0, 14.2, 0.8, "gold")
    else:
        S.plot(np.c_[Solid._ab(-6.9, 6.9, -3.6, 3.6)[0], Solid._ab(-6.9, 6.9, -3.6, 3.6)[1],
                     np.full_like(Solid._ab(-6.9, 6.9, -3.6, 3.6)[0], 10.45)], None, "dark", shade=1)
        _skull(c, S, -5.0, 0.3, 10.0, 1.6)
        for k in range(4):
            S.line((-2.5 + k * 1.6, -2.4, 10.2), (-2.2 + k * 1.6, 2.4, 10.2), "bone", 4 - (k % 2))
        S.line((-3, 0, 10.3), (5, 0, 10.3), "bone", 3)
        S.quad((-8.2, 4.6, 12.2), (8.2, 4.6, 12.2), (-8.2, 8.8, 0.5), "stone", base=0.25)
        S.quad((-8.2, 8.8, 0.5), (8.2, 8.8, 0.5), (-8.2, 9.6, 0.9), "stone", base=-0.6)
        x0, y0 = S.xy(-2, 6.5, 6.5)
        c.line(x0, y0, x0 + 3, y0 + 4, "stone", 1)
        c.line(x0 + 3, y0 + 4, x0 + 2, y0 + 7, "stone", 1)
    return _done(c)


def coffin() -> Canvas:
    """A plain wooden coffin lying along U: the classic tapered hexagon, a raised lid, brass handles."""
    c, S = _new(32, 24, 16, 15)
    pts = [(-8, -2.0), (-4.6, -3.6), (8, -2.3), (8, 2.3), (-4.6, 3.6), (-8, 2.0)]
    S.prism(pts, 0, 4.5, "wood")
    S.prism([(u * 1.04, v * 1.08) for u, v in pts], 4.5, 6.0, "wood", base=0.2)
    S.line((-3.2, -2.2, 6.1), (-3.2, 2.2, 6.1), "wood", 5)
    S.line((-6.5, 0, 6.1), (5.5, 0, 6.1), "wood", 5)
    for u in (-3.5, 3.0):
        S.line((u - 1, 3.85, 2.5), (u + 1, 3.75, 2.5), "gold", 4)
    return _done(c)


def bone_pile(seed: int = 951) -> Canvas:
    """A heap of bones and skulls in a corner of the crypt."""
    r = tex.rng(seed)
    c, S = _new(32, 24, 16, 16)
    for _ in range(7):
        u, v = r.uniform(-5, 5), r.uniform(-4, 4)
        a = r.uniform(0, math.pi)
        L = r.uniform(3, 4.5)
        z = r.uniform(0.6, 2.0)
        _bone(S, (u - math.cos(a) * L, v - math.sin(a) * L, z), (u + math.cos(a) * L, v + math.sin(a) * L,
                                                                    z + r.uniform(-0.6, 1.0)), 0.55)
    for u, v, z in ((-1.5, -1.0, 3.6), (3.5, 2.0, 2.0), (-4.0, 3.0, 1.8)):
        _skull(c, S, u, v, z, 2.0)
    sid = PS.id("bone")                                              # dark creases where bones cross
    m = c.slot == sid
    c.shade[m & (c.shade >= 3) & ~np.roll(c.shade >= 3, 1, 0) & np.roll(m, 1, 0)] = 1
    return _done(c)


def chains_hanging() -> Canvas:
    """Chains hanging from the ceiling, ending in a hook a little above head height. The top runs off the frame:
    hang it from the dark."""
    c = _pcanvas(16, 64)
    c.anchor = (8, 63)                                               # type: ignore[attr-defined]
    c.lights = []                                                    # type: ignore[attr-defined]
    for x0, y_end, ph in ((6, 30, 0.0), (11, 22, 1.3)):
        y = 0
        k = 0
        while y < y_end:
            sway = int(round(math.sin(y / 14 + ph) * 0.8))
            if k % 2 == 0:                                           # a link seen face-on: a little ring
                c.rect(x0 - 1 + sway, y, x0 + 1 + sway, y + 3, "metal", 3)
                c.px(x0 + sway, y + 1, "dark", 0)
                c.px(x0 + sway, y + 2, "dark", 0)
                c.px(x0 - 1 + sway, y, "metal", 5)
                c.px(x0 + 1 + sway, y + 3, "metal", 1)
                y += 3
            else:                                                    # a link edge-on: a short bar
                c.rect(x0 + sway, y, x0 + sway, y + 2, "metal", 4)
                y += 2
            k += 1
        c.line(x0, y, x0, y + 3, "metal", 3)                         # the hook
        c.line(x0, y + 3, x0 + 2, y + 5, "metal", 3)
        c.line(x0 + 2, y + 5, x0 + 3, y + 3, "metal", 4)
        c.px(x0 - 1, y + 1, "metal", 5)
    return _done(c)


def altar_frames(frames: int = 8) -> list[Canvas]:
    """A dark altar with runes cut into its faces that pulse with light, a skull and two candles burning in the
    altar's colour on top."""
    out = []
    for f in range(frames):
        c, S = _new(32, 40, 16, 31)
        S.box(-8.5, -5.5, 0, 8.5, 5.5, 2, "stone", base=-0.5)
        S.box(-7, -4.2, 2, 7, 4.2, 11, "stone")
        S.box(-7.8, -5, 11, 7.8, 5, 13, "stone", base=0.2)
        pulse = 0.5 + 0.5 * math.cos(2 * math.pi * f / frames)
        for k, g in enumerate((1, 3)):
            for gy, row in enumerate(RUNES[g]):
                for gx, ch in enumerate(row):
                    if ch == "#":
                        S.plot([(-6.2 + k * 6.8 + gx * 0.92, 4.25, 9.6 - gy * 1.0)], None, "glow",
                               shade=4 + int(pulse > 0.5 and (gx + gy) % 2 == 0), bias=0.4)
        for gy, row in enumerate(RUNES[6]):
            for gx, ch in enumerate(row):
                if ch == "#":
                    S.plot([(7.05, 3.2 - gx * 0.92, 9.6 - gy * 1.0)], None, "glow", shade=3 + int(pulse > 0.3),
                           bias=0.4)
        A, B = Solid._ab(-3.5, 3.5, -2.5, 2.5)
        k = ((A / 3.5) ** 2 + (B / 2.5) ** 2) < 1
        S.plot(np.c_[A[k], B[k], np.full(k.sum(), 13.05)], None, "glow", shade=2 + int(pulse > 0.6), bias=0.1)
        _skull(c, S, 0, 0, 15.0, 2.0, glow=True)
        for u, v, hgt in ((-6, -3.5, 5), (6, -3.5, 3.5)):
            S.cyl(u, v, 13, 13 + hgt, 0.75, "wax")
            x, y = S.xy(u, v, 13 + hgt)
            _pflame(c, x, y, 4.5, 1.2, 2 * math.pi * f / frames + u)
        _light(c, S, 0, 0, 18)
        out.append(_done(c))
    return out


def statue(kind: str = "knight") -> Canvas:
    """A stone statue on a plinth: a knight resting on his sword, or a crouching gargoyle with spread wings. The
    accent slot is lichen on the upward faces (map it to the stone ramp for a clean statue); a gargoyle's eyes are
    the glow slot."""
    c, S = _new(32, 64, 16, 55)
    S.box(-6, -6, 0, 6, 6, 4, "stone", base=-0.35)
    S.box(-6.6, -6.6, 4, 6.6, 6.6, 5.4, "stone", base=0.1)
    z0 = 5.4
    if kind == "knight":
        k = 1.3                                                      # the knight is a touch heroic in scale
        for a in (1.4 * k, -1.4 * k):
            S.cyl(a, -a, z0, z0 + 9 * k, 1.35 * k, "stone")
            S.box(a - 1.1, -a - 1.1, z0, a + 1.7, -a + 1.7, z0 + 1.4, "stone")
        S.frustum(0, 0, z0 + 8 * k, z0 + 19 * k, 3.0 * k, 3.7 * k, "stone")
        S.frustum(0, 0, z0 + 7 * k, z0 + 10 * k, 3.3 * k, 3.0 * k, "stone", base=-0.3)
        S.line((0.4 * k, 3.3 * k, z0 + 10 * k), (3.3 * k, 0.4 * k, z0 + 10 * k), "stone", 2)
        for a in (2.9 * k, -2.9 * k):
            S.sphere(a, -a, z0 + 18.5 * k, 1.9 * k, "stone")
        S.sphere(0, 0, z0 + 22.3 * k, 2.3 * k, "stone")
        S.frustum(0, 0, z0 + 23.5 * k, z0 + 25.5 * k, 1.0 * k, 0.3, "stone", lean=(-0.4, -0.4))
        for t in np.linspace(-1, 1, 6):
            x, y = S.xy(1.6 * k + t, 1.6 * k - t, z0 + 22.4 * k)
            c.px(x, y, "dark", 1)
        for a in (2.9 * k, -2.9 * k):                                 # arms down to the pommel
            for t in np.linspace(0, 1, 8):
                S.sphere(a * (1 - t) + 2.2 * k * t, -a * (1 - t) + 2.2 * k * t, z0 + (17.5 - 4.5 * t) * k,
                         1.05 * k, "stone")
        g = 2.7 * k
        S.box(g - 0.5, g - 0.5, z0 + 0.5, g + 0.5, g + 0.5, z0 + 12 * k, "stone", base=0.4)    # blade, point down
        S.line((g - 2.6, g + 2.6, z0 + 12 * k + 0.2), (g + 2.6, g - 2.6, z0 + 12 * k + 0.2), "stone", 4)
        S.sphere(g, g, z0 + 13.6 * k + 0.4, 1.0, "stone")
    else:
        S.ellipsoid(-0.8, -0.8, z0 + 6.5, 3.4, 3.4, 4.6, "stone")
        for a in (2.6, -2.6):
            S.sphere(a - 0.5, -a - 0.5, z0 + 3.0, 2.4, "stone")
            S.ellipsoid(a * 0.6 + 2.6, -a * 0.6 + 2.6, z0 + 0.8, 1.2, 1.2, 0.8, "stone")
        for sgn in (1, -1):                                          # wings, behind the body
            root = np.array([-2.2 + 0.8 * sgn, -2.2 - 0.8 * sgn, z0 + 10])
            tip = np.array([-2.0 + 7.5 * sgn, -2.0 - 7.5 * sgn, z0 + 21])
            low = np.array([-2.6 + 6.0 * sgn, -2.6 - 6.0 * sgn, z0 + 8])
            S.tri(root, tip, low, "stone", base=-0.6)
            S.line(root, tip, "stone", 4, bias=0.2)
            mid = (tip + low) / 2
            S.line(root, mid, "stone", 2, bias=0.2)
        S.sphere(1.3, 1.3, z0 + 12.0, 2.4, "stone")
        S.ellipsoid(2.8, 2.8, z0 + 11.2, 1.4, 1.4, 1.1, "stone")
        for sgn in (1, -1):
            S.frustum(1.0 + 1.3 * sgn, 1.0 - 1.3 * sgn, z0 + 13.5, z0 + 17, 0.8, 0.15, "stone",
                      lean=(-0.35 + 0.2 * sgn, -0.35 - 0.2 * sgn))
        for du, dv in ((2.6, 1.3), (1.3, 2.6)):
            x, y = S.xy(1.3 + du * 0.6, 1.3 + dv * 0.6, z0 + 12.6)
            c.px(x, y, "glow", 4)
    r = tex.rng(953)
    sid = PS.id("stone")
    ys, xs = np.nonzero((c.slot == sid) & (c.shade >= 4))
    for y, x in zip(ys, xs):
        if r.random() < 0.18:
            c.slot[y, x] = PS.id("accent")
    return _done(c)


# ---------------------------------------------------------------------------------------------------- lights
def brazier_frames(frames: int = 8) -> list[Canvas]:
    """An iron fire bowl on a stem and tripod foot, heaped with coals and burning high."""
    out = []
    for f in range(frames):
        c, S = _new(32, 48, 16, 41)
        for a in (0.3, 2.4, 4.5):
            S.line((math.cos(a) * 1.2, math.sin(a) * 1.2, 6), (math.cos(a) * 4.8, math.sin(a) * 4.8, 0), "metal", 3)
            S.sphere(math.cos(a) * 4.8, math.sin(a) * 4.8, 0.6, 0.8, "metal")
        S.cyl(0, 0, 3, 12, 0.9, "metal")
        S.sphere(0, 0, 7, 1.4, "metal")
        S.frustum(0, 0, 11.5, 15.5, 2.0, 5.8, "metal", cap=False)
        S.frustum(0, 0, 15.2, 16.2, 6.1, 6.1, "metal", cap=False, base=0.4)
        r = tex.rng(954)
        for _ in range(14):
            a, d = r.uniform(0, 2 * math.pi), r.uniform(0, 4.6)
            S.sphere(math.cos(a) * d, math.sin(a) * d, 15.6 + r.uniform(0, 0.8), 1.1, "flame", base=-1.6, lo=1,
                     hi=3)
        x, y = S.xy(0, 0, 16.5)
        ph = 2 * math.pi * f / frames
        for i, (dx, hgt, w) in enumerate(((-4, 9, 2.2), (0, 15, 3.4), (4, 10, 2.4), (-1.5, 7, 1.8), (2, 12, 2.0))):
            sway = math.sin(ph + i * 1.7) * 1.4
            hh = hgt * (1 + 0.16 * math.sin(ph * 2 + i * 2.1))
            c.polygon([(x + dx - w, y + 1), (x + dx + w, y + 1), (x + dx + sway, y - hh)], "flame", 3)
            c.polygon([(x + dx - w * 0.5, y + 1), (x + dx + w * 0.5, y + 1), (x + dx + sway * 0.6, y - hh * 0.6)],
                      "flame", 5 if i == 1 else 4)
        _embers(c, x, y - 8, f, 4, 955, spread=3, rise=9, frames=frames)
        _light(c, S, 0, 0, 22)
        out.append(_done(c))
    return out


def candelabra_frames(frames: int = 8) -> list[Canvas]:
    """A tall standing candelabrum: a stepped foot, a knopped stem, two arms and three candles."""
    out = []
    for f in range(frames):
        c, S = _new(32, 48, 16, 43)
        S.frustum(0, 0, 0, 1.5, 3.6, 1.8, "gold")
        S.frustum(0, 0, 1.5, 3.2, 1.6, 0.7, "gold")
        S.cyl(0, 0, 3, 24, 0.55, "gold")
        S.sphere(0, 0, 11, 1.2, "gold")
        S.sphere(0, 0, 21, 1.0, "gold")
        tops = []
        for a in (4.0, -4.0):
            S.line((0, 0, 21), (a * 0.5, -a * 0.5, 20.3), "gold", 4)
            S.line((a * 0.5, -a * 0.5, 20.3), (a, -a, 21.5), "gold", 3)
            S.line((a, -a, 21.5), (a, -a, 25), "gold", 3)
            tops.append((a, -a, 25, 5.0))
        tops.append((0, 0, 27, 6.5))
        for u, v, z, hgt in tops:
            S.cyl(u, v, z - 0.8, z, 1.2, "gold")
            S.cyl(u, v, z, z + hgt, 0.55, "wax")
            x, y = S.xy(u, v, z + hgt)
            c.px(x + 1, y + 2, "wax", 5)
            _pflame(c, x, y, 4.5, 1.1, 2 * math.pi * f / frames + u * 0.7)
        _light(c, S, 0, 0, 32)
        out.append(_done(c))
    return out


def cauldron_frames(frames: int = 8) -> list[Canvas]:
    """A black iron cauldron on three stubby legs over a log fire, its brew glowing and bubbling."""
    out = []
    r = tex.rng(956)
    bubbles = [(r.uniform(-3.5, 3.5), r.uniform(-3, 3), int(r.integers(frames))) for _ in range(4)]
    for f in range(frames):
        c, S = _new(32, 40, 16, 32)
        S.hcyl("u", (0, -1.8, 1.0), 4.5, 1.0, "wood")
        S.hcyl("v", (1.8, 0, 1.6), 4.5, 1.0, "wood")
        for a in (0.6, 2.7, 4.8):
            S.cyl(math.cos(a) * 4.2, math.sin(a) * 4.2, 0, 4, 0.8, "metal", base=-0.3)
        S.ellipsoid(0, 0, 8.5, 6.6, 6.6, 6.0, "metal", zmin=3.0, zmax=12.0, base=-0.2)
        S.frustum(0, 0, 11.6, 12.8, 5.7, 6.1, "metal", cap=False, base=0.5)
        A, B = Solid._ab(-5.6, 5.6, -5.6, 5.6)
        k = A * A + B * B < 5.5 ** 2
        A, B = A[k], B[k]
        sw = np.sin(np.arctan2(B, A) * 2 + np.hypot(A, B) * 0.9 - 2 * math.pi * f / frames)
        shade = np.where(sw > 0.6, 4, 3)
        for bu, bv, ph in bubbles:
            age = (f - ph) % frames
            if age < 4:
                d = np.hypot(A - bu, B - bv)
                shade = np.where(np.abs(d - (0.4 + age * 0.45)) < 0.35, 5, shade)
        S.plot(np.c_[A, B, np.full(len(A), 12.2)], None, "liquid", shade=shade, bias=0.2)
        ph = 2 * math.pi * f / frames
        for i, u in enumerate((-3.0, 0.5, 3.5)):
            x, y = S.xy(u, 3.0 - abs(u) * 0.3, 0.8)
            _pflame(c, x, y, 5 + i % 2 * 2, 1.8, ph + i * 2.0)
        for k in range(3):                                           # vapour curling up from the brew
            t = ((f / frames) + k / 3) % 1.0
            x, y = S.xy(-2 + k * 2, 0, 13 + t * 9)
            c.px(x + math.sin(t * 6 + k) * 1.5, y, "glow", 3 if t < 0.6 else 2)
        _light(c, S, 0, 0, 13)
        out.append(_done(c))
    return out


def alchemy_frames(frames: int = 8) -> list[Canvas]:
    """An alchemist's bench: a round-bottomed flask boiling over a spirit burner, a coil to a receiver, a rack of
    vials and a scatter of notes. The brew is the liquid slot (it glows); the vials use cloth1 and cloth2."""
    out = []
    for f in range(frames):
        c, S = _new(48, 48, 24, 37)
        S.box(-10, -5, 11.5, 10, 5, 13, "wood", base=0.2)
        for u, v in ((-9.2, -4.2), (9.2, -4.2), (-9.2, 4.2), (9.2, 4.2)):
            S.box(u - 0.6, v - 0.6, 0, u + 0.6, v + 0.6, 11.5, "wood")
        S.box(-9.2, -0.4, 3, 9.2, 0.4, 4, "wood", base=-0.3)
        S.quad((-1, 0.5, 13.05), (3.5, -0.5, 13.05), (-0.2, 4.2, 13.05), "paper", normal=(0, 0, 1))
        for k in range(3):
            S.line((-0.2 + k * 0.3, 1.5 + k, 13.1), (3.0 + k * 0.3, 0.8 + k, 13.1), "dark", 2)
        # burner and flask
        S.cyl(-4, 0, 13, 14.5, 1.3, "metal")
        for a in (0.5, 2.6, 4.7):
            S.line((-4 + math.cos(a) * 2.6, math.sin(a) * 2.6, 13), (-4 + math.cos(a) * 2.6, math.sin(a) * 2.6, 17.5),
                   "metal", 3)
        S.frustum(-4, 0, 17.2, 17.8, 2.8, 2.8, "metal", cap=False)
        ph = 2 * math.pi * f / frames
        x, y = S.xy(-4, 0, 14.6)
        c.polygon([(x - 1.2, y), (x + 1.2, y), (x + math.sin(ph), y - 3 - math.sin(ph * 2))], "flame", 4)
        c.px(x, y - 1, "flame", 5)
        S.sphere(-4, 0, 21.5, 4.0, "glass", base=0.3)
        S.ellipsoid(-4, 0, 21.5, 3.7, 3.7, 3.7, "liquid", zmax=23.2, bias=1.8, base=0.5)
        S.cyl(-4, 0, 24.5, 28.5, 0.9, "glass", base=0.3)
        S.line((-4, 0, 28.3), (2.5, 0, 25.5), "glass", 4)
        S.line((2.5, 0, 25.5), (5.5, 0, 19.5), "glass", 3)
        S.cyl(5.5, 0, 13, 18.5, 1.7, "glass", base=0.3)
        S.cyl(5.5, 0, 13.1, 15.8, 1.45, "liquid", bias=0.8, base=0.4)
        x, y = S.xy(-4 - 1.6, -1.6, 23.6)
        c.px(x, y, "glass", 5)
        c.px(x + 1, y - 1, "glass", 5)
        for k in range(3):                                           # bubbles rising in the flask
            t = ((f / frames) * 2 + k / 3) % 1.0
            x, y = S.xy(-4 + (k - 1) * 1.2, 2.6, 18.5 + t * 3.6)
            c.px(x, y, "liquid", 5)
        t = (f / frames) % 1.0                                       # and a puff from the neck
        x, y = S.xy(-4, 0, 29.5 + t * 6)
        c.px(x + math.sin(t * 6) * 1.2, y, "glow", 4 if t < 0.5 else 3)
        S.box(-9.5, -4.5, 13, -6.5, 4.5, 14.5, "wood", base=-0.2)    # vial rack
        for k, slot in enumerate(("cloth1", "cloth2", "liquid", "cloth1")):
            v = -3.3 + k * 2.2
            S.cyl(-8, v, 14.5, 18.5 - (k % 2), 0.7, "glass", base=0.3)
            S.cyl(-8, v, 14.6, 16.6 - (k % 2) * 0.5, 0.6, slot, bias=0.6, base=0.3)
        _light(c, S, -4, 0, 22)
        out.append(_done(c))
    return out


def desk_frames(frames: int = 4) -> list[Canvas]:
    """A scholar's writing desk: drawers in a pedestal, an open book, loose papers, ink and quill, and two
    candles burnt to different heights."""
    out = []
    for f in range(frames):
        c, S = _new(48, 40, 24, 30)
        S.box(-10, -5, 12, 10, 5, 13.5, "wood", base=0.2)
        S.box(3.5, -4.5, 0, 9.5, 4.5, 12, "wood")
        for z in (4, 8):
            S.line((3.6, 4.55, z), (9.5, 4.55, z), "wood", 1)
            S.plot([(6.6, 4.6, z + 2)], None, "gold", shade=4, bias=0.4)
        S.plot([(6.6, 4.6, 2)], None, "gold", shade=4, bias=0.4)
        for u, v in ((-9.3, -4.3), (-9.3, 4.3)):
            S.box(u - 0.6, v - 0.6, 0, u + 0.6, v + 0.6, 12, "wood")
        S.box(-9.3, -4, 1.5, -8.7, 4, 2.5, "wood", base=-0.3)
        # open book: two pages tilted up from the spine
        S.quad((-6, -1.5, 13.6), (-1.0, -1.5, 13.6), (-6, 0.3, 14.6), "paper", base=0.4)
        S.quad((-6, 0.3, 14.6), (-1.0, 0.3, 14.6), (-6, 2.4, 13.6), "paper", base=0.1)
        S.box(-6.3, -1.8, 13.5, -0.7, 2.7, 13.7, "cloth1")
        for k in range(3):
            S.line((-5.3, -1.0 + k * 0.5, 13.9 + k * 0.25), (-1.8, -1.0 + k * 0.5, 13.9 + k * 0.25), "dark", 2)
            S.line((-5.3, 1.2 + k * 0.5, 14.4 - k * 0.25), (-1.8, 1.2 + k * 0.5, 14.4 - k * 0.25), "dark", 2)
        S.quad((1, 0.5, 13.6), (5, -0.5, 13.6), (1.6, 3.8, 13.6), "paper", normal=(0, 0, 1), base=-0.2)
        S.quad((2.5, -3.8, 13.6), (6.5, -3.4, 13.6), (2.1, -0.8, 13.6), "paper", normal=(0, 0, 1))
        S.cyl(7.5, 2.5, 13.5, 15, 0.9, "dark", base=-1)
        S.line((7.5, 2.5, 15), (6.0, 1.0, 20), "paper", 5)
        S.line((7.0, 2.0, 16.5), (6.2, 1.2, 19), "paper", 4)
        for u, v, hgt in ((-8.2, -3.5, 5.5), (8.5, -3.5, 2.5)):
            S.cyl(u, v, 13.5, 14.2, 1.3, "gold")
            S.cyl(u, v, 14.2, 14.2 + hgt, 0.6, "wax")
            x, y = S.xy(u, v, 14.2 + hgt)
            _pflame(c, x, y, 4.0, 1.0, 2 * math.pi * f / frames + u)
            _light(c, S, u, v, 16.5 + hgt)
        out.append(_done(c))
    return out


# --------------------------------------------------------------------------------------------- furniture
def bookshelf(full: bool = True, seed: int = 957) -> Canvas:
    """A tall bookcase against a wall, facing +V (mirror it for a +U wall): four shelves of books in three
    colours (cloth1, cloth2, accent) with gilt spines; the empty one keeps a few strays, a skull and dust."""
    r = tex.rng(seed)
    c, S = _new(32, 64, 16, 53)
    H = 40
    S.box(-7.5, -3, 0, -6.5, 3, H, "wood")
    S.box(6.5, -3, 0, 7.5, 3, H, "wood")
    S.box(-6.5, -3, 0, 6.5, -2.2, H, "wood", base=-1.4)
    S.box(-8, -3.4, H, 8, 3.5, H + 1.6, "wood", base=0.2)
    shelves = (0, 10, 20, 30)
    for z in shelves:
        S.box(-6.5, -2.2, z, 6.5, 3.0, z + 1.1, "wood", base=0.1)
    S.box(-7.5, 2.4, 0, 7.5, 3.1, 1.6, "wood", base=-0.2)
    for si, z in enumerate(shelves):
        u = -6.3
        while u < 6.0:
            w = r.uniform(0.7, 1.5)
            if u + w > 6.3:
                break
            if not full and r.random() < 0.8:
                u += w + r.uniform(0.5, 2)
                continue
            hgt = r.uniform(5.0, 8.4)
            slot = str(r.choice(["cloth1", "cloth2", "accent", "cloth1", "cloth2", "paper"]))
            inset = r.uniform(0, 0.6)
            if r.random() < 0.1 and u < 4:                           # a book lying flat
                S.box(u, -1.8, z + 1.1, u + 3.0, 1.8 - inset, z + 2.3, slot)
                u += 3.2
                continue
            S.box(u, -1.9, z + 1.1, u + w, 2.4 - inset, z + 1.1 + hgt, slot)
            if r.random() < 0.6:
                S.line((u + 0.15, 2.45 - inset, z + hgt - 0.8), (u + w - 0.15, 2.45 - inset, z + hgt - 0.8), "gold", 4)
            u += w + (0.05 if r.random() < 0.8 else r.uniform(0.6, 1.4))
    if not full:
        _skull(c, S, 3.0, 0.5, 22.9, 1.7)
        x, y = S.xy(-6.4, 2.9, 39)
        for k in range(5):
            c.line(x, y, x + 6 - k, y + k * 1.2, "paper", 4)
    return _done(c)


def weapon_rack() -> Canvas:
    """A wooden rack along U holding a sword, a spear, an axe and a mace."""
    c, S = _new(32, 56, 16, 44)
    for u in (-7, 7):
        S.box(u - 1, -2.2, 0, u + 1, 2.2, 1.4, "wood", base=-0.2)
        S.box(u - 0.5, -0.6, 1.4, u + 0.5, 0.6, 27, "wood")
    S.box(-7.5, -0.6, 24.5, 7.5, 0.6, 26, "wood", base=0.1)
    S.box(-7.5, -0.4, 6, 7.5, 1.6, 7.2, "wood", base=0.1)
    # sword
    S.box(-4.6, 1.0, 1.5, -4.0, 1.4, 19, "metal", base=0.3)
    S.line((-6, 1.6, 19), (-2.6, 1.6, 19), "gold", 4)
    S.box(-4.5, 1.0, 19, -4.1, 1.4, 23, "wood", base=-0.3)
    S.sphere(-4.3, 1.2, 23.6, 0.7, "gold")
    # spear
    S.line((-1, 1.4, 1.5), (-1, 0.9, 33), "wood", 3)
    S.line((-0.6, 1.4, 1.5), (-0.6, 0.9, 33), "wood", 2)
    S.tri((-1.9, 0.9, 33), (0.1, 0.9, 33), (-0.9, 0.9, 38), "metal", base=0.6)
    # axe
    S.line((2.6, 1.4, 1.5), (2.6, 1.0, 26), "wood", 3)
    S.line((2.9, 1.4, 1.5), (2.9, 1.0, 26), "wood", 2)
    S.quad((3.0, 1.1, 21.5), (5.4, 1.1, 20), (3.0, 1.1, 25.5), "metal", base=0.5)
    S.line((5.4, 1.15, 20), (5.4, 1.15, 25.5), "metal", 5)
    S.quad((5.4, 1.1, 25.5), (3.0, 1.1, 25.5), (5.4, 1.1, 27), "metal", base=0.5)
    # mace
    S.line((5.2, 1.4, 1.5), (5.2, 1.2, 17), "wood", 3)
    S.sphere(5.2, 1.3, 18.5, 1.6, "metal")
    for a in range(4):
        S.plot([(5.2 + math.cos(a * 1.57) * 2.0, 1.3 + math.sin(a * 1.57) * 2.0, 18.5)], None, "metal", shade=5)
    return _done(c)


def armor_stand() -> Canvas:
    """A suit of plate on a cross-footed stand: breastplate, pauldrons, a plumed helm."""
    c, S = _new(32, 56, 16, 46)
    S.box(-4, -1, 0, 4, 1, 1.5, "wood", base=-0.2)
    S.box(-1, -4, 0, 1, 4, 1.5, "wood", base=-0.2)
    S.cyl(0, 0, 0, 14, 0.7, "wood")
    S.frustum(0, 0, 10, 13.5, 3.4, 3.2, "metal", base=-0.2)
    S.frustum(0, 0, 13.5, 24.5, 3.1, 4.0, "metal")
    S.line((2.3, 2.3, 14), (2.9, 2.9, 24), "gold", 4)
    S.line((0.2, 4.0, 24.4), (4.0, 0.2, 24.4), "gold", 4)
    for a in (3.2, -3.2):
        S.sphere(a, -a, 24.2, 2.3, "metal")
        S.ellipsoid(a * 1.05, -a * 1.05, 21.5, 1.7, 1.7, 2.2, "metal", base=-0.2)
    S.cyl(0, 0, 24.5, 26, 1.0, "metal")
    S.sphere(0, 0, 28.8, 2.8, "metal")
    S.box(-2.0, -2.0, 26.0, 2.2, 2.2, 28.2, "metal", base=-0.1)
    for t in np.linspace(-1, 1, 7):
        S.plot([(2.1 + t * 1.3, 2.1 - t * 1.3, 28.6)], None, "dark", shade=0, bias=0.5)
    S.frustum(0, 0, 31, 35.5, 0.9, 0.3, "cloth1", lean=(-0.5, -0.5))
    S.sphere(-1.6, -1.6, 35.4, 1.2, "cloth1")
    return _done(c)


def throne() -> Canvas:
    """A throne on a dais, facing +V (mirror it for +U): a tall pointed back with a cloth panel, a cushion,
    armrests capped in gold, finials."""
    c, S = _new(32, 64, 16, 52)
    S.box(-8, -7.5, 0, 8, 7.5, 3, "stone", base=-0.35)
    S.box(-5.5, -4.5, 3, 5.5, 4.5, 10, "stone")
    S.box(-4.7, -3.6, 10, 4.7, 4.2, 11.6, "cloth1", base=0.2)
    S.box(-5.5, -5.5, 10, 5.5, -3.6, 33, "stone")
    S.tri((-5.5, -3.6, 33), (5.5, -3.6, 33), (0, -3.6, 39.5), "stone")
    S.box(-4.0, -3.65, 13, 4.0, -3.5, 30.5, "cloth1", base=-0.2)
    S.line((-4.0, -3.45, 30.5), (4.0, -3.45, 30.5), "gold", 4)
    S.line((-4.0, -3.45, 13), (-4.0, -3.45, 30.5), "gold", 4)
    S.line((4.0, -3.45, 13), (4.0, -3.45, 30.5), "gold", 3)
    for k in range(4):
        S.plot([(0, -3.4, 25 - k * 2.5)], None, "gold", shade=5 - (k % 2), bias=0.3)
    for u in (-6.6, 4.6):
        S.box(u, -4.5, 10, u + 2, 4.6, 15, "stone", base=0.1)
        S.sphere(u + 1, 4.6, 15.2, 1.2, "gold")
    for u in (-5.5, 5.5):
        S.sphere(u, -4.5, 34.5, 1.4, "gold")
    S.sphere(0, -4.4, 40.5, 1.2, "gold")
    S.line((-8, 7.55, 3), (8, 7.55, 3), "gold", 3)
    return _done(c)


def tavern_table_frames(frames: int = 4) -> list[Canvas]:
    """A long tavern table: two tankards (one wooden, one pewter) with foam, a loaf on a plate, a bottle and a
    stub of candle."""
    out = []
    for f in range(frames):
        c, S = _new(48, 32, 24, 22)
        S.box(-10, -5.5, 10, 10, 5.5, 11.5, "wood", base=0.2)
        for u, v in ((-9, -4.5), (9, -4.5), (-9, 4.5), (9, 4.5)):
            S.box(u - 0.7, v - 0.7, 0, u + 0.7, v + 0.7, 10, "wood")
        S.box(-9, -0.4, 2.5, 9, 0.4, 3.5, "wood", base=-0.4)
        for k in range(3):
            S.line((-10, -2.5 + k * 2.7, 11.55), (10, -2.5 + k * 2.7, 11.55), "wood", 2)
        S.cyl(-5, 2, 11.5, 15, 1.3, "wood", cap_slot="accent")
        S.line((-5 + 1.3, 2 + 1.3, 12.5), (-5 + 1.3, 2 + 1.3, 14), "metal", 3)
        for z in (12.2, 14.2):
            S.frustum(-5, 2, z, z + 0.4, 1.38, 1.38, "metal", cap=False)
        x, y = S.xy(-5 + 1.5, 2 - 1.5, 13.5)
        c.px(x + 2, y, "wood", 3)
        c.px(x + 2, y + 1, "wood", 2)
        S.cyl(-1.5, -2.5, 11.5, 14.5, 1.2, "metal", cap_slot="accent")
        S.cyl(4.5, 1.5, 11.5, 11.9, 2.5, "metal", base=0.2)
        S.ellipsoid(4.5, 1.5, 12.6, 1.8, 1.2, 1.0, "paper")
        S.line((3.6, 0.8, 13.4), (4.4, 1.6, 13.5), "paper", 2)
        S.cyl(7.5, -3.0, 11.5, 15.5, 0.9, "glass", base=-0.6)
        S.cyl(7.5, -3.0, 15.5, 17.5, 0.4, "glass", base=-0.6)
        S.cyl(7.5, -3.0, 17.5, 18.0, 0.45, "wood")
        S.cyl(1.5, -0.5, 11.5, 13.0, 0.7, "wax")
        x, y = S.xy(1.5, -0.5, 13)
        _pflame(c, x, y, 3.5, 0.9, 2 * math.pi * f / frames)
        _light(c, S, 1.5, -0.5, 15)
        out.append(_done(c))
    return out


def chair() -> Canvas:
    """A ladder-back chair facing +V (mirror it for +U)."""
    c, S = _new(16, 32, 8, 23)
    for u, v in ((-3, -3), (3, -3), (-3, 3), (3, 3)):
        S.box(u - 0.5, v - 0.5, 0, u + 0.5, v + 0.5, 7, "wood")
    S.box(-3.6, -3.6, 7, 3.6, 3.6, 8.3, "wood", base=0.2)
    for u in (-3, 3):
        S.box(u - 0.5, -3.6, 8.3, u + 0.5, -2.6, 19, "wood")
    for z in (12, 15.5, 18.5):
        S.box(-3, -3.4, z, 3, -2.8, z + 1.2, "wood", base=0.1)
    return _done(c)


def stool() -> Canvas:
    c, S = _new(16, 24, 8, 17)
    for a in (0.5, 2.6, 4.7):
        S.line((math.cos(a) * 1.4, math.sin(a) * 1.4, 7), (math.cos(a) * 3.0, math.sin(a) * 3.0, 0), "wood", 2)
        S.line((math.cos(a) * 1.6, math.sin(a) * 1.6, 7), (math.cos(a) * 3.2, math.sin(a) * 3.2, 0), "wood", 3)
    S.cyl(0, 0, 7, 8.4, 3.2, "wood")
    return _done(c)


def bench() -> Canvas:
    c, S = _new(32, 24, 16, 15)
    S.box(-8.5, -2.4, 6.5, 8.5, 2.4, 8, "wood", base=0.15)
    for u in (-7, 7):
        S.box(u - 0.7, -2, 0, u + 0.7, 2, 6.5, "wood")
    S.box(-7, -0.4, 2, 7, 0.4, 3, "wood", base=-0.4)
    return _done(c)


def barrel_tap_frames(frames: int = 4) -> list[Canvas]:
    """A cask on its side in a cradle, its +U end tapped with a brass spigot that drips."""
    out = []
    for f in range(frames):
        c, S = _new(32, 32, 16, 24)
        for u in (-4, 4):
            S.box(u - 1, -4.5, 0, u + 1, 4.5, 2.6, "wood", base=-0.4)
        S.hcyl("u", (0, 0, 6.6), 6, 5, "wood", cap_slot="wood")
        th = np.linspace(0, 2 * math.pi, 120)
        for u in (-4.2, 4.2):
            P = np.c_[np.full_like(th, u), 5.2 * np.cos(th), 6.6 + 5.2 * np.sin(th)]
            S.plot(P, np.c_[np.zeros_like(th), np.cos(th), np.sin(th)], "metal")
        for rr in (1.6, 3.2):
            P = np.c_[np.full_like(th, 6.05), rr * np.cos(th), 6.6 + rr * np.sin(th)]
            S.plot(P, None, "wood", shade=1, bias=0.2)
        for k in range(6):
            a = k * math.pi / 3 + 0.3
            S.line((-5.8, 5.05 * math.cos(a), 6.6 + 5.05 * math.sin(a)),
                   (5.8, 5.05 * math.cos(a), 6.6 + 5.05 * math.sin(a)), "wood", 2, bias=0.1)
        S.hcyl("u", (6.8, 0, 4.0), 1.0, 0.55, "gold")
        S.box(7.2, -0.3, 4.4, 7.7, 0.3, 6.4, "gold")
        x, y = S.xy(7.8, 0, 3.4)
        t = f / frames
        c.px(x, y + t * 8, "accent", 4)
        if f > 0:
            c.px(x, y, "accent", 3)
        S.plot([(7.8 + dx * 0.4, dy * 0.4, 0.05) for dx in range(-3, 4) for dy in range(-2, 3)
                if dx * dx / 9 + dy * dy / 4 <= 1], None, "accent", shade=2)
        out.append(_done(c))
    return out


def fireplace_frames(frames: int = 8) -> list[Canvas]:
    """A stone hearth against a wall, facing +V (mirror it for +U): jambs, a mantel shelf with candles and a jar,
    a chimney breast, and a log fire."""
    out = []
    for f in range(frames):
        c, S = _new(48, 64, 24, 53)
        S.box(-10.5, -4, 0, 10.5, 5.5, 1.8, "stone", base=-0.3)
        S.box(-6, -4, 1.8, 6, -3.0, 17, "stone", base=-2.2)
        S.box(-10, -4, 1.8, -6, 4, 17, "stone")
        S.box(6, -4, 1.8, 10, 4, 17, "stone")
        S.box(-11, -4.5, 17, 11, 5.0, 20, "stone", base=0.2)
        S.box(-9, -4, 20, 9, 2.0, 30, "stone")
        S.box(-6, -4, 30, 6, 0.5, 38, "stone")
        for z in (6, 11.5):
            S.line((-10, 4.05, z), (-6, 4.05, z), "stone", 1)
            S.line((6, 4.05, z), (10, 4.05, z), "stone", 1)
            S.line((10.05, -4, z), (10.05, 4, z), "stone", 1)
        for z in (25, 34):
            S.line((-9, 2.05, z), (9, 2.05, z), "stone", 1) if z < 30 else S.line((-6, 0.55, z), (6, 0.55, z),
                                                                                    "stone", 1)
        A, B = Solid._ab(-6, 6, 1.8, 17)
        S.plot(np.c_[A, np.full_like(A, -2.95), B], None, "dark", shade=1, bias=0.1)
        S.hcyl("u", (0, 0.5, 3.0), 4.5, 1.1, "wood", cap_slot="wood")
        S.hcyl("u", (0.5, -1.2, 4.6), 3.5, 1.0, "wood", cap_slot="wood")
        ph = 2 * math.pi * f / frames
        x, y = S.xy(0, 0.5, 4.0)
        for i, (dx, hgt, w) in enumerate(((-5, 7, 2.0), (-1.5, 11, 2.8), (2.5, 9, 2.4), (5.5, 6, 1.8),
                                          (0.5, 8, 2.0))):
            sway = math.sin(ph + i * 1.9) * 1.3
            hh = hgt * (1 + 0.16 * math.sin(ph * 2 + i * 1.3))
            c.polygon([(x + dx - w, y + 1), (x + dx + w, y + 1), (x + dx + sway, y - hh)], "flame", 3)
            c.polygon([(x + dx - w * 0.5, y + 1), (x + dx + w * 0.5, y + 1), (x + dx + sway * 0.6, y - hh * 0.6)],
                      "flame", 4 if i != 1 else 5)
        _embers(c, x, y - 6, f, 3, 958, spread=3, rise=8, frames=frames)
        for u, hgt in ((-8.5, 3.5), (8.5, 2.5)):
            S.cyl(u, 3.0, 20, 20 + hgt, 0.6, "wax")
            x, y = S.xy(u, 3.0, 20 + hgt)
            _pflame(c, x, y, 3.5, 0.9, ph + u)
        S.frustum(-4, 3, 20, 23, 1.4, 1.0, "glass", base=-0.4)
        _light(c, S, 0, 1.5, 7)
        out.append(_done(c))
    return out


# ------------------------------------------------------------------------------------------- doors and bars
def cell_bars(open_: bool = False) -> Canvas:
    """Iron prison bars across a tile along U (mirror for V), with a gate section that swings open toward +V."""
    c, S = _new(48, 64, 24, 46)
    S.box(-8, -0.6, 0, 8, 0.6, 1.2, "metal", base=-0.2)
    if not open_:
        for z in (16.5, 33.5):
            S.box(-8, -0.5, z, 8, 0.5, z + 1.4, "metal")
        for u in np.arange(-6.4, 7, 3.2):
            S.cyl(u, 0, 1.2, 34, 0.4, "metal")
        S.box(5.4, 0.4, 14, 7.0, 0.9, 19, "metal", base=0.3)
        S.plot([(6.2, 0.95, 15.5)], None, "dark", shade=0, bias=0.5)
    else:
        for z in (16.5, 33.5):
            S.box(-8, -0.5, z, -1.4, 0.5, z + 1.4, "metal")
        for u in (-6.4, -3.2):
            S.cyl(u, 0, 1.2, 34, 0.4, "metal")
        a = math.radians(98)
        d = np.array([math.cos(a), math.sin(a)])
        h0 = np.array([-1.2, 0.0])
        for t in (0, 3.2, 6.4):
            p = h0 + d * (t + 1.6)
            S.cyl(p[0], p[1], 1.2, 34, 0.4, "metal")
        for z in (1.4, 16.5, 33.5):
            for k in np.linspace(0, 9, 30):
                p = h0 + d * k
                S.box(p[0] - 0.4, p[1] - 0.4, z, p[0] + 0.4, p[1] + 0.4, z + 1.2, "metal")
        S.cyl(-1.2, 0, 1.2, 34.5, 0.6, "metal", base=0.3)
    return _done(c)


def door_arch(open_: bool = False) -> Canvas:
    """A wooden door in a stone arch, set in the plane of a wall along U and facing +V (mirror for V). Open, the
    door swings out towards +V and the doorway is black."""
    c, S = _new(48, 64, 24, 47)
    S.box(-9.5, -1.6, 0, -6, 1.6, 24, "stone")
    S.box(6, -1.6, 0, 9.5, 1.6, 24, "stone")
    for z in (8, 16):
        S.line((-9.5, 1.65, z), (-6, 1.65, z), "stone", 1)
        S.line((6, 1.65, z), (9.5, 1.65, z), "stone", 1)
    n = 9
    for k in range(n):                                           # voussoirs
        a0, a1 = math.pi * k / n, math.pi * (k + 1) / n
        th = np.linspace(a0 + 0.02, a1 - 0.02, 20)
        rr = np.arange(6.0, 9.6 + (0.8 if k == n // 2 else 0), 0.3)
        TH, RR = np.meshgrid(th, rr)
        TH, RR = TH.ravel(), RR.ravel()
        P = np.c_[-RR * np.cos(TH), np.full_like(TH, 1.6), 24 + RR * np.sin(TH)]
        S.plot(P, np.tile([0, 1, 0], (len(P), 1)), "stone", base=0.15 if k % 2 else 0.0)
        outer = rr[-1]
        for vv in np.arange(-1.6, 1.6, 0.3):
            P = np.c_[-outer * np.cos(th), np.full_like(th, vv), 24 + outer * np.sin(th)]
            N = np.c_[-np.cos(th), np.zeros_like(th), np.sin(th)]
            S.plot(P, N, "stone")
    inner = lambda s: 24 + math.sqrt(max(0.0, 36 - (s - 6) ** 2))   # noqa: E731
    if not open_:
        A, B = Solid._ab(-6, 6, 0, 30)
        k = B <= np.array([inner(a + 6) for a in A])
        A, B = A[k], B[k]
        base = np.where(((A + 6) % 2.4) < 0.35, -1.4, 0.0)
        S.plot(np.c_[A, np.full_like(A, 0.4), B], np.tile([0, 1, 0], (len(A), 1)), "wood", base=base)
        for z in (5, 18):
            S.box(-6, 0.4, z, 6, 0.7, z + 1.2, "metal", base=0.2)
            for u in (-4.5, -1.5, 1.5, 4.5):
                S.plot([(u, 0.75, z + 0.6)], None, "metal", shade=5, bias=0.4)
        S.frustum(3.6, 0.9, 11, 11.1, 1.0, 1.0, "gold", cap=False)
        th = np.linspace(0, 2 * math.pi, 40)
        S.plot(np.c_[3.6 + np.cos(th), np.full_like(th, 0.9), 11 + np.sin(th)], None, "gold", shade=4, bias=0.3)
    else:
        A, B = Solid._ab(-6, 6, 0, 30)
        k = B <= np.array([inner(a + 6) for a in A])
        S.plot(np.c_[A[k], np.full(k.sum(), -1.0), B[k]], None, "dark", shade=0)
        a = math.radians(78)
        d = np.array([math.cos(a), math.sin(a), 0.0])
        h0 = np.array([-6.0, 0.6, 0.0])
        Ss, Zs = Solid._ab(0, 12, 0, 30)
        k = Zs <= np.array([inner(s) for s in Ss])
        Ss, Zs = Ss[k], Zs[k]
        P = h0 + np.outer(Ss, d) + np.c_[np.zeros_like(Zs), np.zeros_like(Zs), Zs]
        nrm = np.array([-d[1], d[0], 0.0])
        if nrm @ np.array([1, 1, 1]) < 0:
            nrm = -nrm
        base = np.where((Ss % 2.4) < 0.35, -1.4, 0.0)
        S.plot(P, np.tile(nrm, (len(P), 1)), "wood", base=base - 0.2)
        for z in (5, 18):
            for s in np.linspace(0, 12, 40):
                q = h0 + d * s
                S.plot([(q[0] + nrm[0] * 0.3, q[1] + nrm[1] * 0.3, z + dz) for dz in (0, 0.4, 0.8, 1.2)], None,
                       "metal", shade=3, bias=0.2)
    return _done(c)


def portal_frames(frames: int = 8) -> list[Canvas]:
    """A ring of standing stones around a swirling gate (along U, facing +V): spiral arms wind inward over the
    loop. The swirl is the glow slot."""
    out = []
    for f in range(frames):
        c, S = _new(48, 64, 24, 48)
        S.box(-11, -3, 0, 11, 3, 2, "stone", base=-0.4)
        S.box(-10, -1.8, 2, -7, 1.8, 30, "stone")
        S.box(7, -1.8, 2, 10, 1.8, 30, "stone")
        S.box(-11.5, -2.4, 30, 11.5, 2.4, 34, "stone", base=0.15)
        for k, g in enumerate((0, 4, 7)):
            for gy, row in enumerate(RUNES[g]):
                for gx, ch in enumerate(row):
                    if ch == "#":
                        S.plot([(-4.5 + k * 3.5 + gx * 0.42, 2.45, 33.4 - gy * 0.45)], None, "glow",
                               shade=4 if (k + f // 2) % 3 else 5, bias=0.3)
        A, B = Solid._ab(-7, 7, 2, 30)
        ex, ez = A / 7.0, (B - 16) / 14.0
        rr = np.hypot(ex, ez)
        k = rr < 1
        A, B, ex, ez, rr = A[k], B[k], ex[k], ez[k], rr[k]
        ang = np.arctan2(ez, ex)
        sw = np.sin(3 * ang + rr * 7.0 - 2 * math.pi * f / frames)
        shade = np.where(sw > 0.55, 4, np.where(sw > -0.2, 3, 2))
        shade = np.where(rr < 0.22, 5, shade)
        shade = np.where(rr > 0.9, np.minimum(shade, 3), shade)
        S.plot(np.c_[A, np.zeros_like(A), B], None, "glow", shade=shade)
        r = tex.rng(959)
        for q in range(6):                                           # motes spiralling in
            t = ((f / frames) + q / 6) % 1.0
            a0 = r.uniform(0, 2 * math.pi) + t * 4
            rad = 1.3 - t
            x, y = S.xy(7 * rad * math.cos(a0), 1.0, 16 + 14 * rad * math.sin(a0))
            c.px(x, y, "glow", 5)
        _light(c, S, 0, 1, 16)
        out.append(_done(c))
    return out


# --------------------------------------------------------------------------------------------------- caves
def stalagmite(size: str = "medium", seed: int = 960) -> Canvas:
    """Cave stalagmites: tapering cones with mineral banding, a wet tip on the tallest."""
    r = tex.rng(seed)
    dims = {"small": (16, 24, 8, 18, [(0, 0, 9, 3.0), (2.4, 1.6, 5, 1.6)]),
            "medium": (32, 40, 16, 33, [(0, 0, 19, 4.4), (3.5, 2.5, 10, 2.6), (-3.2, 2.2, 6, 2.0)]),
            "large": (32, 64, 16, 57, [(0, -0.5, 38, 6.2), (4.2, 3.0, 20, 3.4), (-4.0, 2.8, 13, 2.8),
                                      (1.5, 5.0, 6, 1.8)])}[size]
    w, h, ax, ay, cones = dims
    c, S = _new(w, h, ax, ay)
    for u, v, hgt, rad in cones:
        ph = r.uniform(0, 6)
        S.frustum(u, v, 0, hgt, rad, 0.25, "stone", base=lambda Z, ph=ph: 0.7 * (np.sin(Z * 0.8 + ph) > 0.3) - 0.35,
                  lean=(r.uniform(-0.05, 0.05), r.uniform(-0.05, 0.05)))
        S.ellipsoid(u, v, 0, rad * 1.35, rad * 1.35, rad * 0.5, "stone", zmin=0, base=-0.2)
    u, v, hgt, _ = cones[0]
    x, y = S.xy(u, v, hgt)
    c.px(x, y, "glass", 5)
    return _done(c)


def glow_mushrooms_frames(frames: int = 4, seed: int = 961) -> list[Canvas]:
    """A cluster of tall cave mushrooms whose caps glow (glow slot) and slowly breathe; spores drift up."""
    out = []
    shrooms = [(-3, 1.5, 10, 3.4), (2.5, -2.0, 7, 2.6), (4.5, 3.0, 4.2, 1.9), (-5.0, -3.0, 3.4, 1.5),
               (0.5, 5.0, 2.6, 1.2), (-1.0, -5.0, 5.0, 1.6)]
    for f in range(frames):
        c, S = _new(32, 32, 16, 26)
        pulse = 0.35 * math.sin(2 * math.pi * f / frames)
        for u, v, hgt, cr in shrooms:
            S.frustum(u, v, 0, hgt, 0.32 * cr + 0.3, 0.22 * cr + 0.2, "bone", base=-0.2)
            S.ellipsoid(u, v, hgt - 0.2, cr, cr, cr * 0.6, "glow", zmin=hgt - 0.4, base=pulse)
            for du, dv in ((-0.4, 0.3), (0.3, -0.5), (0.2, 0.55)):
                x, y = S.xy(u + du * cr, v + dv * cr, hgt + cr * 0.42)
                c.px(x, y, "glow", 5)
        r = tex.rng(seed)
        for q in range(5):
            t = ((f / frames) + q / 5) % 1.0
            x, y = S.xy(r.uniform(-5, 5), r.uniform(-4, 4), 6 + t * 14)
            if t < 0.8:
                c.px(x, y, "glow", 4 if t < 0.4 else 3)
        _light(c, S, -1, 0, 9)
        out.append(_done(c))
    return out


def crystal_big_frames(frames: int = 8) -> list[Canvas]:
    """A great crystal cluster: six-sided prisms leaning out of a rock base. A band of light climbs through the
    crystals over the loop (the pulse), so the cluster seems to breathe."""
    out = []
    xs = [(0, 0, 30, 3.3, (0.0, 0.0), 0.2), (-4, 2.5, 18, 2.3, (-0.2, 0.08), 0.9),
          (4, -2.5, 21, 2.5, (0.17, -0.1), 0.5), (2.5, 4.5, 11, 1.9, (0.12, 0.2), 1.4),
          (-3.5, -4, 13, 1.9, (-0.12, -0.18), 0.1), (5.5, 2.5, 8, 1.4, (0.28, 0.1), 0.7)]
    for f in range(frames):
        c, S = _new(32, 48, 16, 41)
        S.ellipsoid(0, 0, 0, 7.5, 7, 2.6, "stone", zmin=0)
        wave = (f / frames) * 40 - 5
        for u, v, hgt, rad, lean, tw in xs:
            body = hgt * 0.78
            fn = lambda Z, w=wave: 0.9 * np.exp(-((Z - w) / 3.0) ** 2) - 0.1   # noqa: E731
            S.frustum(u, v, 0.5, body, rad, rad * 0.94, "glow", seg=6, lean=lean, cap=False, base=fn, twist=tw)
            tu, tv = u + lean[0] * (body - 0.5), v + lean[1] * (body - 0.5)
            S.frustum(tu, tv, body, hgt, rad * 0.94, 0.08, "glow", seg=6, lean=lean, cap=False, base=fn, twist=tw)
            x, y = S.xy(u + lean[0] * (hgt - 0.5), v + lean[1] * (hgt - 0.5), hgt)
            c.px(x, y, "glow", 5)
        _light(c, S, 0, 0, 14)
        out.append(_done(c))
    return out


def minecart(load: str = "ore", seed: int = 962) -> Canvas:
    """A mine cart along U: a planked tub with iron corners and rim on four spoked wheels, heaped with ore (the
    accent slot), gold, or nothing."""
    r = tex.rng(seed)
    c, S = _new(32, 40, 16, 30)
    th = np.linspace(0, 2 * math.pi, 60)
    for vside in (-4.3, 4.3):
        for u in (-3.8, 3.8):
            rr = np.arange(0, 2.3, 0.3)
            TH, RR = np.meshgrid(th, rr)
            P = np.c_[u + RR.ravel() * np.cos(TH.ravel()), np.full(TH.size, vside), 2.2 + RR.ravel() *
                      np.sin(TH.ravel())]
            S.plot(P, np.tile([0, 1, 0], (len(P), 1)), "metal", base=-0.4 if vside < 0 else 0.0)
            S.plot(np.c_[u + 2.2 * np.cos(th), np.full_like(th, vside + 0.1), 2.2 + 2.2 * np.sin(th)], None, "metal",
                   shade=4, bias=0.2)
            S.plot([(u, vside + 0.2, 2.2)], None, "dark", shade=1, bias=0.4)
    S.box(-6, -3.9, 3.0, 6, 3.9, 9.6, "wood", top_slot="dark")
    S.plot(np.c_[Solid._ab(-5.4, 5.4, -3.3, 3.3)[0], Solid._ab(-5.4, 5.4, -3.3, 3.3)[1],
                 np.full_like(Solid._ab(-5.4, 5.4, -3.3, 3.3)[0], 9.65)], None, "dark", shade=1)
    for z in (5.2, 7.4):
        S.line((-6, 3.95, z), (6, 3.95, z), "wood", 1)
        S.line((6.05, -3.9, z), (6.05, 3.9, z), "wood", 1)
    for u0, u1, v0, v1 in ((-6.3, 6.3, 3.5, 4.3), (-6.3, 6.3, -4.3, -3.5), (-6.3, -5.5, -4.3, 4.3),
                           (5.5, 6.3, -4.3, 4.3)):
        S.box(u0, v0, 9.4, u1, v1, 10.4, "metal")
    for u in (-5.6, 5.6):
        S.box(u - 0.4, 3.9, 3.0, u + 0.4, 4.2, 9.4, "metal", base=0.1)
    if load != "empty":
        slot = "accent" if load == "ore" else "gold"
        for _ in range(16):
            u, v = r.uniform(-4.5, 4.5), r.uniform(-2.6, 2.6)
            S.sphere(u, v, 9.8 + r.uniform(0, 1.6) * (1 - abs(u) / 6), r.uniform(1.1, 1.9), slot)
        sid = PS.id(slot)
        ys, xs = np.nonzero((c.slot == sid) & (c.shade >= 3))
        for y, x in zip(ys, xs):
            if r.random() < 0.1:
                c.shade[y, x] = 5
    return _done(c)


def treasure_hoard(seed: int = 963) -> Canvas:
    """A dragon's-worth of coin: a glinting mound, a goblet, a crown, gems (cloth1, cloth2, accent) and a sword
    stuck in the pile."""
    r = tex.rng(seed)
    c, S = _new(48, 40, 24, 31)
    for u, v, ru, rv, rz in ((0, 0, 10, 8, 7), (7, -4, 5, 4, 4.5), (-6, 5, 5, 4, 3.5), (5, 5, 4, 3, 2.4)):
        S.ellipsoid(u, v, 0, ru, rv, rz, "gold", zmin=0)
    S.frustum(-5, 1.5, 4.2, 5, 1.2, 0.4, "gold")
    S.cyl(-5, 1.5, 5, 7.5, 0.35, "gold")
    S.frustum(-5, 1.5, 7.5, 10.5, 0.6, 1.8, "gold", cap_slot="dark")
    S.sphere(-5, 1.5 + 1.6, 9.2, 0.55, "cloth1")
    S.frustum(4.5, 2.5, 4.4, 6.2, 2.1, 2.2, "gold", cap=False, base=0.2)
    for k in range(5):
        a = k * 2 * math.pi / 5
        S.frustum(4.5 + 2.15 * math.cos(a), 2.5 + 2.15 * math.sin(a), 6.2, 7.6, 0.45, 0.05, "gold")
    S.sphere(4.5 + 1.5, 2.5 + 1.5, 5.3, 0.5, "cloth2")
    S.box(1.6, -3.2, 5, 2.3, -2.5, 17, "metal", base=0.3)
    S.line((0.0, -1.0, 17), (3.9, -4.7, 17), "gold", 4)
    S.box(1.7, -3.1, 17, 2.2, -2.6, 20, "wood")
    S.sphere(1.95, -2.85, 20.6, 0.7, "gold")
    for _ in range(9):
        a, d = r.uniform(0, 2 * math.pi), r.uniform(0.5, 1.0)
        u, v = 9.5 * d * math.cos(a), 7.5 * d * math.sin(a)
        z = max(0.4, 7 * math.sqrt(max(0, 1 - (u / 10) ** 2 - (v / 8) ** 2)))
        S.sphere(u, v, z, 0.6, str(r.choice(["cloth1", "cloth2", "accent"])))
    for _ in range(14):                                              # coins spilled round the foot
        a, d = r.uniform(0, 2 * math.pi), r.uniform(9.5, 12.5)
        u, v = d * math.cos(a), d * 0.8 * math.sin(a)
        S.plot([(u + du, v, 0.2) for du in (-0.3, 0, 0.3)], None, "gold", shade=4)
    sid = PS.id("gold")
    ys, xs = np.nonzero((c.slot == sid) & (c.shade >= 2))
    for y, x in zip(ys, xs):
        q = r.random()
        if q < 0.07:
            c.shade[y, x] = 5
        elif q < 0.16 and c.shade[y, x] > 2:
            c.shade[y, x] -= 1
    return _done(c)


def drip_frames(frames: int = 12) -> list[Canvas]:
    """A drop of water forming on the ceiling, falling and splashing into a ring at the anchor. Hang it over a
    puddle; the top runs off the frame."""
    out = []
    for f in range(frames):
        c = _pcanvas(16, 64)
        c.anchor, c.lights = (8, 60), []                             # type: ignore[attr-defined]
        if f < 4:
            c.px(8, 0, "glass", 3)
            for k in range(f):
                c.px(8, 1 + k, "glass", 4 if k == f - 1 else 3)
        elif f < 8:
            t = (f - 3) / 4
            y = 2 + t * t * 58
            c.px(8, y, "glass", 5)
            c.px(8, y - 1, "glass", 3)
            if t > 0.4:
                c.px(8, y - 2, "glass", 2)
        else:
            k = f - 8
            rx, ry = 1.5 + k * 1.3, 0.6 + k * 0.6
            for a in np.linspace(0, 2 * math.pi, 40):
                c.px(8 + rx * math.cos(a), 60 + ry * math.sin(a), "glass", 4 if math.sin(a) < 0 else 3)
            if k < 2:
                c.px(8, 58 - k * 2, "glass", 5)
                c.px(6 - k, 58 - k, "glass", 4)
                c.px(10 + k, 58 - k, "glass", 4)
        out.append(c)                                                # water has no outline: it reads as light
    return out


# ---------------------------------------------------------------------------------------------- prop catalogue
@dataclass
class DProp:
    id: str
    make: Callable[[], Canvas | list[Canvas]]
    variants: dict[str, dict[str, str]]
    blocks: bool = True
    cover: str = "none"                          # none | half | full
    height: int = 1                              # height steps, for line of sight
    fps: float = 0.0
    light: tuple[str, float, str] | None = None  # (slot whose shade 4 is the light colour, radius in tiles, flicker)
    facing: str = ""                             # "+V": drawn facing +V, mirror for +U; "U": spans the tile along U
    tags: list[str] = field(default_factory=list)

    def frames(self) -> list[Canvas]:
        return _pframes(self.id)


@lru_cache(maxsize=None)
def _pframes(pid: str) -> list[Canvas]:
    m = next(p for p in PROPS if p.id == pid).make()
    return m if isinstance(m, list) else [m]


_PB = {"stone": "dungeon/crypt", "wood": "wood/oak", "metal": "metal/iron", "gold": "metal/gold",
       "cloth1": "cloth/crimson", "cloth2": "cloth/navy", "accent": "dungeon/moss", "bone": "dungeon/bone",
       "paper": "dungeon/parchment", "glass": "dungeon/glass", "liquid": "dungeon/potion_green",
       "glow": "dungeon/rune_fel", "flame": "dungeon/flame", "dark": "dungeon/coal", "wax": "dungeon/wax"}


def _p(**slots: str) -> dict[str, str]:
    return {**_PB, **{k: (v if "/" in v else f"dungeon/{v}") for k, v in slots.items()}}


_CRYPT = {"grey": _p(), "dark": _p(stone="crypt_dark", gold="metal/bronze"),
          "marble": _p(stone="marble", accent="marble")}
_WOODS = {"oak": _p(), "walnut": _p(wood="walnut")}
_BOOKS = {"oak": _p(accent="cloth/forest"), "walnut": _p(wood="walnut", cloth1="cloth/wine", cloth2="cloth/teal",
                                                         accent="cloth/ochre")}
_FIRE = {"fire": _p(), "ghost": _p(flame="ghost_fire"), "arcane": _p(flame="rune_arcane")}
_ROCK = {"brown": _p(stone="cave"), "cool": _p(stone="cave_cool"), "deep": _p(stone="cave_deep")}

PROPS: list[DProp] = [
    # crypt
    DProp("sarcophagus", sarcophagus, _CRYPT, cover="half", height=2, facing="U", tags=["crypt"]),
    DProp("sarcophagus_open", lambda: sarcophagus(True), _CRYPT, cover="half", height=2, facing="U",
          tags=["crypt"]),
    DProp("coffin", coffin, {"oak": _p(), "dark": _p(wood="wood/dark", gold="metal/iron")}, cover="half",
          facing="U", tags=["crypt"]),
    DProp("bone_pile", bone_pile, {"bone": _p()}, blocks=False, height=0, tags=["crypt"]),
    DProp("chains", chains_hanging, {"iron": _p(), "rusty": _p(metal="metal/bronze")}, blocks=False, height=0,
          tags=["crypt", "ceiling"]),
    DProp("altar_runes", altar_frames, {
        "fel": _p(stone="crypt_dark", glow="rune_fel", flame="ghost_fire"),
        "blood": _p(stone="crypt_dark", glow="rune_blood", flame="flame"),
        "arcane": _p(stone="crypt", glow="rune_arcane", flame="rune_arcane")}, cover="half", height=2, fps=8,
          light=("glow", 2.5, "pulse"), tags=["crypt", "light"]),
    DProp("statue_knight", statue, {**_CRYPT, "clean": _p(accent="crypt")}, cover="full", height=4,
          tags=["crypt", "hall"]),
    DProp("statue_gargoyle", lambda: statue("gargoyle"), {
        "grey": _p(glow="crypt_dark"), "watcher": _p(stone="crypt_dark", glow="rune_blood"),
        "marble": _p(stone="marble", accent="marble", glow="marble")}, cover="full", height=4,
          tags=["crypt", "hall"]),
    # light sources
    DProp("brazier", brazier_frames, _FIRE, cover="half", height=2, fps=10, light=("flame", 3.5, "fire"),
          tags=["light"]),
    DProp("candelabra", candelabra_frames, {"gold": _p(), "iron": _p(gold="metal/dark")}, height=3, fps=8,
          light=("flame", 2.5, "candle"), tags=["light"]),
    DProp("cauldron", cauldron_frames, {
        "green": _p(metal="metal/dark", glow="potion_green"),
        "violet": _p(metal="metal/dark", liquid="potion_violet", glow="potion_violet"),
        "red": _p(metal="metal/dark", liquid="potion_red", glow="potion_red")}, cover="half", fps=8,
          light=("liquid", 2.0, "pulse"), tags=["light", "witch"]),
    DProp("alchemy_table", alchemy_frames, {
        "green": _p(cloth1="potion_red", cloth2="potion_violet", glow="potion_green"),
        "violet": _p(liquid="potion_violet", cloth1="potion_green", cloth2="potion_red", glow="potion_violet"),
        "red": _p(liquid="potion_red", cloth1="potion_violet", cloth2="potion_green", glow="potion_red")},
          cover="half", height=2, fps=8, light=("liquid", 2.0, "pulse"), facing="U", tags=["light", "study"]),
    DProp("desk", desk_frames, _WOODS, cover="half", height=2, fps=6, light=("flame", 2.0, "candle"), facing="U",
          tags=["light", "study"]),
    DProp("fireplace", fireplace_frames, {"grey": _p(), "warm": _p(stone="crypt_warm")}, cover="full", height=5,
          fps=10, light=("flame", 4.0, "fire"), facing="+V", tags=["light", "tavern"]),
    # furniture
    DProp("bookshelf", bookshelf, _BOOKS, cover="full", height=5, facing="+V", tags=["study"]),
    DProp("bookshelf_empty", lambda: bookshelf(False, 958), _BOOKS, cover="full", height=5, facing="+V",
          tags=["study", "crypt"]),
    DProp("weapon_rack", weapon_rack, {"oak": _p(metal="metal/steel")}, cover="half", height=3, facing="U",
          tags=["armory"]),
    DProp("armor_stand", armor_stand, {"steel": _p(metal="metal/steel"), "dark": _p(metal="metal/dark",
                                                                                   cloth1="cloth/royal_purple"),
                                       "gold": _p(metal="metal/gold", gold="metal/silver")},
          cover="half", height=4, tags=["armory", "hall"]),
    DProp("throne", throne, {"gold": _p(stone="marble"), "dark": _p(stone="crypt_dark", cloth1="cloth/royal_purple"),
                             "bone": _p(stone="bone", cloth1="cloth/wine", gold="metal/dark")},
          cover="half", height=4, facing="+V", tags=["hall"]),
    DProp("tavern_table", tavern_table_frames, {"oak": _p(accent="foam", paper="prop/hay", glass="cloth/forest")},
          cover="half", fps=6, light=("flame", 1.5, "candle"), facing="U", tags=["tavern", "light"]),
    DProp("chair", chair, _WOODS, blocks=False, facing="+V", tags=["tavern"]),
    DProp("stool", stool, _WOODS, blocks=False, tags=["tavern"]),
    DProp("bench", bench, _WOODS, blocks=False, facing="U", tags=["tavern"]),
    DProp("barrel_tap", barrel_tap_frames, {"oak": _p(accent="ale", gold="dungeon/brass")}, cover="half", height=2,
          fps=6, facing="U", tags=["tavern"]),
    # doors and bars
    DProp("cell_bars", cell_bars, {"iron": _p(), "rusty": _p(metal="metal/bronze")}, cover="half", height=4,
          facing="U", tags=["prison"]),
    DProp("cell_bars_open", lambda: cell_bars(True), {"iron": _p(), "rusty": _p(metal="metal/bronze")},
          blocks=False, height=4, facing="U", tags=["prison"]),
    DProp("door_arch", door_arch, {"oak": _p(), "dark": _p(stone="crypt_dark", wood="walnut")}, cover="full",
          height=4, facing="U", tags=["door"]),
    DProp("door_arch_open", lambda: door_arch(True), {"oak": _p(), "dark": _p(stone="crypt_dark", wood="walnut")},
          blocks=False, height=4, facing="U", tags=["door"]),
    DProp("portal", portal_frames, {"void": _p(stone="crypt_dark", glow="portal"),
                                    "fel": _p(stone="crypt_dark", glow="rune_fel"),
                                    "arcane": _p(stone="crypt", glow="rune_arcane")}, blocks=False, height=4, fps=10,
          light=("glow", 3.5, "pulse"), facing="U", tags=["light", "interactive"]),
    # caves and mines
    DProp("stalagmite_small", lambda: stalagmite("small"), _ROCK, blocks=False, height=1, tags=["cave"]),
    DProp("stalagmite", lambda: stalagmite("medium"), _ROCK, cover="half", height=2, tags=["cave"]),
    DProp("stalagmite_large", lambda: stalagmite("large", 964), _ROCK, cover="full", height=4, tags=["cave"]),
    DProp("glow_mushrooms", glow_mushrooms_frames, {
        "teal": _p(glow="pool_glow", bone="parchment"), "violet": _p(glow="potion_violet", bone="parchment"),
        "amber": _p(glow="candle", bone="parchment")}, blocks=False, fps=4, light=("glow", 1.5, "pulse"),
          tags=["cave", "light"]),
    DProp("crystal_big", crystal_big_frames, {
        "blue": _p(stone="cave_deep", glow="prop/crystal_blue"),
        "violet": _p(stone="cave_deep", glow="prop/crystal_violet"),
        "green": _p(stone="cave_deep", glow="prop/crystal_green"),
        "amber": _p(stone="cave", glow="candle")}, cover="full", height=4, fps=8, light=("glow", 3.6, "pulse"),
          tags=["cave", "light"]),
    DProp("minecart", minecart, {"iron": _p(accent="ore", metal="metal/dark")}, cover="half", facing="U",
          tags=["mine"]),
    DProp("minecart_gold", lambda: minecart("gold"), {"iron": _p(metal="metal/dark")}, cover="half", facing="U",
          tags=["mine", "loot"]),
    DProp("minecart_empty", lambda: minecart("empty"), {"iron": _p(metal="metal/dark")}, cover="half",
          facing="U", tags=["mine"]),
    DProp("treasure_hoard", treasure_hoard, {"gold": _p(cloth1="gem_red", cloth2="gem_blue", accent="gem_green",
                                                        metal="metal/steel")}, cover="half", tags=["loot"]),
    DProp("drip", drip_frames, {"water": _p(glass="glass")}, blocks=False, height=0, fps=10,
          tags=["cave", "ceiling", "fx"]),
]


# ================================================================================================================
# Light
# ================================================================================================================
FLICKER = {
    "torch": [1.0, 0.9, 1.06, 0.94, 0.84, 1.02, 0.92, 1.08],
    "fire": [1.0, 0.93, 1.08, 0.97, 0.88, 1.05, 0.95, 1.1],
    "candle": [1.0, 0.97, 1.02, 0.95, 1.0, 1.03, 0.97, 0.99],
    "pulse": [round(0.82 + 0.18 * math.cos(2 * math.pi * k / 8), 3) for k in range(8)],
    "steady": [1.0] * 8,
}
"""Per-frame light intensity multipliers (8 steps, looped at the prop's fps): flames flicker, magic breathes."""

AMBIENT = {"crypt": (0.25, 0.27, 0.40), "cave": (0.26, 0.33, 0.46), "hall": (0.30, 0.25, 0.26)}
"""Ambient light per kind of interior: what an unlit corner keeps of its colour."""

BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16


def _falloff(d: np.ndarray) -> np.ndarray:
    return np.clip(1 - d, 0, 1) ** 1.6


def light_pool(colour: str, w: int = 64, h: int = 32, *, steps: int = 6) -> Image.Image:
    """An additive pool of light on the floor: a 2:1 ellipse (a circle on the iso ground) whose falloff is cut into
    a few bands with ordered dither between them, so it reads as pixel art rather than an airbrushed glow."""
    ys, xs = np.mgrid[0:h, 0:w]
    d = np.hypot((xs + 0.5 - w / 2) / (w / 2), (ys + 0.5 - h / 2) / (h / 2))
    f = _falloff(d)
    q = np.floor(f * steps + BAYER[ys % 4, xs % 4]) / steps
    rgb = [int(colour[i:i + 2], 16) for i in (1, 3, 5)]
    a = np.zeros((h, w, 4), np.uint8)
    a[..., :3] = rgb
    a[..., 3] = (np.clip(q, 0, 1) * 200).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def halo(colour: str, size: int = 32, *, steps: int = 5) -> Image.Image:
    """An additive round glow to put behind a flame or crystal, banded like the pools."""
    return light_pool(colour, size, size, steps=steps)


def vignette(w: int = 256, h: int = 144) -> Image.Image:
    """The darkness overlay for an interior: transparent in the middle, deepening towards the edges (multiply or
    alpha-blend it over the battle view). Banded and dithered so it never shows a gradient seam."""
    ys, xs = np.mgrid[0:h, 0:w]
    d = np.hypot((xs + 0.5 - w / 2) / (w / 2), (ys + 0.5 - h / 2) / (h / 2))
    f = np.clip((d - 0.45) / 0.85, 0, 1) ** 1.3
    q = np.floor(f * 8 + BAYER[ys % 4, xs % 4]) / 8
    a = np.zeros((h, w, 4), np.uint8)
    a[..., :3] = (10, 8, 18)
    a[..., 3] = (np.clip(q, 0, 1) * 235).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


LIGHT_COLOURS = {"torch": "#ffb35c", "candle": "#ffd890", "fel": "#79e88f", "arcane": "#7fc8ff",
                 "blood": "#ff5a5a", "crystal": "#6fc3ff", "void": "#a77bff", "ember": "#ff7b2b"}


def _hexrgb(h: str) -> np.ndarray:
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], np.float32) / 255


# ================================================================================================================
# Rooms: the dioramas' builder
# ================================================================================================================
@dataclass
class Mat:
    """One map legend entry. Texture refs are (id, variant); ids are looked up in this set, then in terrain."""
    top: tuple[str, str | None]
    side: tuple[str, str | None] | None          # None: no side (a bridge deck shows only its lip)
    lip: tuple[str, str | None] | None = None
    sink: int = 0


TEX_EMISSIVE = {"glow"}


@lru_cache(maxsize=None)
def _any_tex(tid: str, variant: str | None, frame: int, emissive: bool) -> np.ndarray:
    """A texture frame from this set or the terrain set as float RGBA; `emissive` keeps only the glowing slots
    (everything else black), which is what the darkness pass leaves unlit."""
    t = next((x for x in TEXTURES if x.id == tid), None)
    if t is not None:
        fr = _tframes(tid)
        pal = ramps(t.variants[variant] if variant else next(iter(t.variants.values())))
    else:
        t = next(x for x in TR.CATALOGUE if x.id == tid)
        fr = t.frames()
        pal = ramps(t.variants[variant] if variant else next(iter(t.variants.values())))
    if emissive:
        pal = {k: (v if k in TEX_EMISSIVE else ["#000000"] * 6) for k, v in pal.items()}
    return np.asarray(fr[frame % len(fr)].render(pal)).astype(np.float32)


def _emissive_img(img: Image.Image, mask: np.ndarray | None) -> Image.Image:
    """A sprite's emissive twin: its glowing pixels as they are, the rest black (so it still occludes)."""
    a = np.asarray(img).copy()
    if mask is None:
        a[..., :3] = 0
    else:
        a[~mask, :3] = 0
    return Image.fromarray(a, "RGBA")


@dataclass
class _Light:
    ground: tuple[float, float, float]           # (U, V, Z) in texels: where the pool sits on the floor
    offset: tuple[float, float]                  # screen offset of the source from the ground point
    colour: np.ndarray
    radius: float                                # tiles
    flicker: str
    phase: int = 0
    power: float = 1.0


class Room:
    """An interior diorama: an iso.Scene twinned with an emissive scene, plus the lights its props and wall decals
    carry. `render()` applies the darkness recipe the manifest describes."""

    def __init__(self, mats: dict[str, Mat], terrain: list[str], heights: list[str], *, frame: int = 0,
                 ambient=(0.2, 0.22, 0.34), seed: int = 1,
                 decals: dict[str, tuple[float, list[tuple[str, str | None]]]] | None = None):
        self.frame, self.ambient = frame, np.array(ambient, np.float32)
        rng = np.random.default_rng(seed)
        d, w = len(terrain), len(terrain[0])
        self.scene, self.escene = iso.Scene(w, d), iso.Scene(w, d)
        self.lights: list[_Light] = []
        for j in range(d):
            for i in range(w):
                ch = terrain[j][i]
                if ch == ".":
                    continue
                m = mats[ch]
                h = int(heights[j][i], 36)
                cells = []
                for em in (False, True):
                    top = _any_tex(*m.top, frame, em)
                    side = _any_tex(*m.side, frame, em) if m.side else np.zeros((32, TR.T, 4), np.float32)
                    lip = _any_tex(*m.lip, frame, em) if m.lip else None
                    cells.append(iso.Cell(h, top, side, lip=lip, sink=m.sink))
                self.scene.cells[(i, j)], self.escene.cells[(i, j)] = cells
                if decals and ch in decals:
                    p, pool = decals[ch]
                    if rng.random() < p:
                        self.decal(i, j, *pool[int(rng.integers(len(pool)))])

    # ------------------------------------------------------------------------------------------------ floors
    def decal(self, i: int, j: int, tid: str, variant: str | None = None, *, transpose: bool = False,
              flip: bool = False, quarter: tuple[int, int] | None = None) -> None:
        """Lays a ground decal on a tile (blended over any already there). `quarter` picks a 32 px quarter of a
        larger decal (rugs); `transpose` turns a U-running decal to run along V."""
        for sc, em in ((self.scene, False), (self.escene, True)):
            a = _any_tex(tid, variant, self.frame, em)
            if quarter:
                a = a[quarter[1] * 32:quarter[1] * 32 + 32, quarter[0] * 32:quarter[0] * 32 + 32]
            if transpose:
                a = a.transpose(1, 0, 2)
            if flip:
                a = a[:, ::-1]
            c = sc.cells[(i, j)]
            if c.decal is None:
                c.decal = a.copy()
            else:
                al = a[..., 3:4] / 255
                c.decal = c.decal * (1 - al) + a * al
                c.decal[..., 3] = np.maximum(c.decal[..., 3], a[..., 3])
        t = next((x for x in TEXTURES if x.id == tid), None)
        if t is not None and "light" in t.tags:
            c = self.scene.cells[(i, j)]
            Z = c.h * iso.STEP - c.sink
            col = _hexrgb(ramps(t.variants[variant or next(iter(t.variants))])["glow"][4])
            self.lights.append(_Light(((i + 0.5) * 32, (j + 0.5) * 32, Z), (0, -4), col, 1.6, "pulse", power=0.8))

    def rug(self, i: int, j: int, variant: str) -> None:
        for qx in range(2):
            for qy in range(2):
                self.decal(i + qx, j + qy, "rug", variant, quarter=(qx, qy))

    # ------------------------------------------------------------------------------------------------- walls
    def wall(self, i: int, j: int, face: str, tid: str, variant: str | None = None, *, drop: int = 6,
             at: int = 0, mirror: bool = False) -> None:
        """Hangs a wall decal on a cell's +V (`face="v"`, the screen-left face) or +U (`"u"`) side, `drop` texels
        below the top, `at` texels along the face. The cell gets its own copy of the side texture, as tall as the
        wall, with the decal painted in."""
        for sc, em in ((self.scene, False), (self.escene, True)):
            c = sc.cells[(i, j)]
            Z = c.h * iso.STEP - c.sink
            if c.side.shape[0] < Z:
                c.side = np.vstack([c.side] * (-(-Z // c.side.shape[0])))[:Z].copy()
            else:
                c.side = c.side.copy()
            a = _any_tex(tid, variant, self.frame, em)
            if (face == "u") != mirror:
                a = a[:, ::-1]
            base = (i if face == "v" else j) * 32 % TR.T + at
            hh = min(a.shape[0], c.side.shape[0] - drop)
            for x in range(a.shape[1]):
                col = (base + x) % TR.T
                src = a[:hh, x]
                al = src[:, 3:4] / 255
                dst = c.side[drop:drop + hh, col]
                dst[:, :3] = dst[:, :3] * (1 - al) + src[:, :3] * al
                c.side[drop:drop + hh, col] = dst
        t = next(x for x in TEXTURES if x.id == tid)
        if "light" in t.tags:
            c = self.scene.cells[(i, j)]
            Z = c.h * iso.STEP
            u = (i * 32 + at + 15.5) if face == "v" else (i + 1) * 32
            v = (j + 1) * 32 if face == "v" else (j * 32 + 32 - at - 15.5)
            nb = (i, j + 1) if face == "v" else (i + 1, j)
            zf = self.scene.top_z(*nb) if nb in self.scene.cells else 0
            src_z = Z - drop - (12 if tid == "sconce" else 20)
            gu, gv = (u, v + 10) if face == "v" else (u + 10, v)
            sx = (u - v) - (gu - gv)
            sy = ((u + v) / 2 - src_z) - ((gu + gv) / 2 - zf)
            ref = ramps(t.variants[variant or next(iter(t.variants))])["glow"][4]
            self.lights.append(_Light((gu, gv, zf), (sx, sy), _hexrgb(ref), 3.2 if tid == "sconce" else 2.0,
                                      "torch" if tid == "sconce" else "pulse", phase=i * 3 + j,
                                      power=1.25 if tid == "sconce" else 0.7))

    # ----------------------------------------------------------------------------------------------- sprites
    def _add(self, img: Image.Image, emask: np.ndarray | None, anchor, i, j, *, shadow=False, z=None) -> None:
        self.scene.sprites.append(iso.Sprite(img, anchor, i, j, shadow=shadow, z=z))
        self.escene.sprites.append(iso.Sprite(_emissive_img(img, emask), anchor, i, j, shadow=False, z=z))

    def prop(self, pid: str, i: float, j: float, variant: str | None = None, *, mirror: bool = False,
             phase: int = 0, light: float = 1.0) -> None:
        p = next(x for x in PROPS if x.id == pid)
        v = variant or next(iter(p.variants))
        frs = p.frames()
        cv = frs[(self.frame + phase) % len(frs)]
        pal = ramps(p.variants[v], PS)
        img = cv.render(pal)
        emask = np.isin(cv.slot, [PS.id(s) for s in ("glow", "flame", "liquid")])
        ax, ay = cv.anchor
        if mirror:
            img = img.transpose(Image.FLIP_LEFT_RIGHT)
            emask = emask[:, ::-1]
            ax = img.width - 1 - ax
        self._add(img, emask, (ax, ay), i, j)
        if p.light and cv.lights and light > 0:
            slot, radius, fl = p.light
            Z = self.scene.top_z(int(round(i)), int(round(j)))
            for lx, ly in cv.lights:
                if mirror:
                    lx = img.width - 1 - lx
                self.lights.append(_Light(((i + 0.5) * 32, (j + 0.5) * 32, Z), ((lx - ax) * 2, (ly - ay) * 2),
                                          _hexrgb(pal[slot][4]), radius, fl, phase=phase + int(i * 5 + j * 3),
                                          power=light))

    def kit(self, pid: str, i: float, j: float, variant: str | None = None, *, mirror: bool = False,
            phase: int = 0, light: float = 1.0) -> None:
        """An outdoor prop from `props.py`, lit by the same recipe: its emissive mask and its manifest light."""
        p = next(x for x in PR.CATALOGUE if x.id == pid)
        v = variant or next(iter(p.variants))
        frs = p.frames()
        cv = frs[(self.frame + phase) % len(frs)]
        pal = PR.ramps(p.variants[v])
        img = cv.render(pal)
        emask = PR.emissive_mask(p, cv) if p.light else None
        ax, ay = p.anchor_of(cv)
        pts = PR.light_points(p, cv) if p.light else []
        if mirror:
            img = img.transpose(Image.FLIP_LEFT_RIGHT)
            emask = emask[:, ::-1] if emask is not None else None
            ax = img.width - 1 - ax
            pts = [[img.width - 1 - x, y] for x, y in pts]
        self._add(img, emask, (ax, ay), i, j)
        if p.light and light > 0:
            slot, radius, fl = p.light
            Z = self.scene.top_z(int(round(i)), int(round(j)))
            for lx, ly in pts:
                self.lights.append(_Light(((i + 0.5) * 32, (j + 0.5) * 32, Z), ((lx - ax) * 2, (ly - ay) * 2),
                                          _hexrgb(pal[slot][4]), radius, fl, phase=phase + int(i * 5 + j * 3),
                                          power=light))

    def unit(self, rid: str, i: float, j: float, facing: str = "front", mirror: bool = False, anim: str = "idle",
             k: int = 0) -> None:
        from . import dioramas as DI
        s = DI.unit(rid, i, j, facing, mirror, anim, k)
        self._add(s.image, None, s.anchor, i, j, shadow=True)

    def beast(self, bid: str, i: float, j: float, variant: str | None = None, facing: str = "front",
              mirror: bool = False, k: int = 0, glow: bool = False) -> None:
        from . import dioramas as DI
        s = DI.beast(bid, i, j, variant, facing, mirror, k)
        self._add(s.image, None, s.anchor, i, j, shadow=True)

    def boss(self, mid: str, variant: str, i: float, j: float, k: int = 0, facing: str = "front",
             mirror: bool = False, glow_colour: str | None = None) -> None:
        from . import bosses as BS
        m = next(x for x in BS.CATALOGUE if x.id == mid)
        img = BS.frames(m, variant)[k + (len(BS.frame_poses()) if facing == "back" else 0)]
        ax, ay = m.anchor
        if mirror:
            img = img.transpose(Image.FLIP_LEFT_RIGHT)
            ax = img.width - 1 - ax
        self._add(img, None, (ax, ay), i, j, shadow=True)
        if glow_colour:                                              # spectral things give off a little light
            Z = self.scene.top_z(int(round(i)), int(round(j)))
            self.lights.append(_Light(((i + 0.5) * 32, (j + 0.5) * 32, Z), (0, -40), _hexrgb(glow_colour), 2.0,
                                      "pulse", power=0.6))

    def light(self, i: float, j: float, colour: str, radius: float, *, lift: float = 30, flicker: str = "steady",
              power: float = 1.0) -> None:
        Z = self.scene.top_z(int(round(i)), int(round(j)))
        self.lights.append(_Light(((i + 0.5) * 32, (j + 0.5) * 32, Z), (0, -lift), _hexrgb(colour), radius, flicker,
                                  power=power))

    # ------------------------------------------------------------------------------------------------ render
    def render(self, backdrop=None, *, margin: int = 24, parts: bool = False):
        """The darkness recipe: lit = albedo x (ambient + sum of lights), with each light's falloff banded and
        dithered; emissive pixels (the glow, flame and liquid slots) keep their full colour."""
        bd = backdrop or _dark_backdrop
        base = np.asarray(iso.render(self.scene, backdrop=bd, margin=margin)).astype(np.float32)[..., :3]
        em = np.asarray(iso.render(self.escene, backdrop=lambda w, h: Image.new("RGBA", (w, h), (0, 0, 0, 255)),
                                   margin=margin)).astype(np.float32)[..., :3]
        H, W = base.shape[:2]
        max_h = max(c.h for c in self.scene.cells.values()) * iso.STEP
        ox, oy = self.scene.d * iso.TILE + margin, max_h + margin + 50
        ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
        L = np.ones((H, W, 3), np.float32) * self.ambient
        for lt in self.lights:
            U, V, Z = lt.ground
            gx, gy = U - V + ox, (U + V) / 2 - Z + oy
            sx, sy = gx + lt.offset[0], gy + lt.offset[1]
            R = lt.radius * 32
            fl = FLICKER[lt.flicker][(self.frame + lt.phase) % 8]
            x0, x1 = int(max(0, min(gx, sx) - R)), int(min(W, max(gx, sx) + R))
            y0, y1 = int(max(0, sy - R)), int(min(H, gy + R / 2 + 1))
            if x0 >= x1 or y0 >= y1:
                continue
            X, Y = xs[y0:y1, x0:x1], ys[y0:y1, x0:x1]
            dg = np.hypot((X - gx) / R, (Y - gy) / (R / 2))
            ds = np.hypot(X - sx, Y - sy) / (R * 0.8)
            f = np.maximum(_falloff(dg), _falloff(ds) * 0.85)
            L[y0:y1, x0:x1] += (f * fl * lt.power * 1.35)[..., None] * lt.colour
        # band the light so it steps like pixel art, with ordered dither at the steps
        L = np.floor(L * 7 + BAYER[ys.astype(int) % 4, xs.astype(int) % 4][..., None]) / 7
        lum = em.max(axis=2, keepdims=True)
        m = np.clip(lum / 60, 0, 1)
        out = base * np.clip(L, 0, 1.3) * (1 - m) + base * m
        img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
        if not parts:
            return img
        lmap = np.clip(L / 1.3 * 255, 0, 255).astype(np.uint8)
        return (Image.fromarray(base.astype(np.uint8), "RGB"), Image.fromarray(lmap, "RGB"),
                Image.fromarray(np.clip(em, 0, 255).astype(np.uint8), "RGB"), img)


def _dark_backdrop(w: int, h: int) -> Image.Image:
    t = np.linspace(0, 1, h)[:, None, None]
    top, bot = np.array([16, 13, 26], np.float32), np.array([30, 24, 40], np.float32)
    a = np.zeros((h, w, 4), np.uint8)
    a[..., :3] = (top * (1 - t) + bot * t).astype(np.uint8)
    a[..., 3] = 255
    return Image.fromarray(a, "RGBA")


# ------------------------------------------------------------------------------------------------- dioramas
def crypt(frame: int = 0) -> Room:
    """The Ossuary: a crypt hall with a raised rune dais where a necromancer works the circle, sarcophagi down the
    side, a pit into the dark, and the party coming in from the front. Cold ambient, warm sconces, fel light."""
    mats = {
        "W": Mat(("dungeon_floor", "dark"), ("dungeon_wall", "grey")),
        "D": Mat(("dungeon_floor", "dark"), ("dungeon_wall", "grey")),
        "f": Mat(("dungeon_floor", "grey"), ("dungeon_wall", "dark")),
        "m": Mat(("dungeon_floor_mossy", "grey"), ("dungeon_wall", "dark")),
        "k": Mat(("dungeon_floor_cracked", "grey"), ("dungeon_wall", "dark")),
        "b": Mat(("dungeon_floor_bloody", "grey"), ("dungeon_wall", "dark")),
        "c": Mat(("crypt_tiles", "dormant"), ("dungeon_wall", "dark")),
        "r": Mat(("crypt_tiles", "fel"), ("dungeon_wall", "grey")),
        "F": Mat(("dungeon_floor_cracked", "grey"), ("chasm_side", "cool")),
        "p": Mat(("void", None), ("chasm_side", "cool")),
    }
    terrain = ["WWWWWDWWWWWW",
               "Wmffcrrrcfmf",
               "Wffkcrrrcfff",
               "Wbffcccccffk",
               "Wfmfffffbfff",
               "WffffkfffFFF",
               "WfffmfffFppp",
               "WkfffbffFppp",
               "WffmfffbFppp"]
    heights = ["888883888888",
               "833334443333",
               "833334443333",
               "833333333333",
               "833333333333",
               "833333333333",
               "833333333000",
               "833333333000",
               "833333333000"]
    R = Room(mats, terrain, heights, frame=frame, ambient=AMBIENT["crypt"], seed=3,
             decals={"f": (0.12, [("bone_scatter", None), ("blood_trail", None), ("cracks", None)])})
    for i, tid, var, kw in ((1, "cobweb", None, {"drop": 0}), (2, "sconce", None, {}), (3, "shackles", None, {}),
                            (4, "wall_banner", "black", {"drop": 2}), (6, "wall_banner", "black", {"drop": 2}),
                            (7, "moss_drip", None, {"drop": 0}), (8, "sconce", None, {}),
                            (9, "wall_runes", "fel", {"drop": 8}), (10, "shackles", None, {}),
                            (11, "sconce", None, {})):
        R.wall(i, 0, "v", tid, var, **kw)
    for j, tid, var, kw in ((1, "cobweb", None, {"drop": 0, "mirror": True}), (2, "sconce", None, {}),
                            (4, "wall_crack", None, {}), (5, "sconce", None, {}), (7, "moss_drip", None, {"drop": 0}),
                            (8, "shackles", None, {})):
        R.wall(0, j, "u", tid, var, **kw)
    R.decal(6, 3, "rune_circle", "fel")
    R.decal(5, 3, "blood_trail", None)
    R.prop("portal", 5, 0.3, "void")
    R.light(10, 7, LIGHT_COLOURS["void"], 2.2, lift=-20, flicker="pulse", power=0.5)
    R.prop("altar_runes", 6, 1, "fel")
    R.prop("brazier", 4, 1, "ghost", phase=3)
    R.prop("brazier", 8, 1, "ghost", phase=5)
    R.prop("statue_gargoyle", 3, 0.7, "watcher")
    R.prop("statue_gargoyle", 9, 0.7, "watcher", mirror=True)
    R.prop("sarcophagus", 2, 2, "grey")
    R.prop("sarcophagus_open", 2, 4, "grey")
    R.prop("sarcophagus", 2, 6, "dark")
    R.prop("coffin", 10, 2, "dark")
    R.prop("coffin", 11, 3, "oak")
    R.prop("bone_pile", 1, 8)
    R.prop("bone_pile", 11, 1)
    R.prop("candelabra", 1, 1, "iron", phase=2)
    R.prop("candelabra", 10, 4, "iron", phase=6)
    R.prop("chains", 4, 5)
    R.prop("chains", 10, 7, "rusty")
    R.prop("cell_bars", 11, 4.6)
    R.unit("necromancer", 6, 2)
    R.unit("skeleton", 4, 3, mirror=True, k=1)
    R.unit("skeleton", 8, 3, k=2)
    R.unit("skeleton_archer", 9, 2, mirror=True)
    R.unit("zombie", 7, 4)
    R.boss("wraith", "wraith", 3.6, 4.2, 1, mirror=True, glow_colour="#7fe0a0")
    R.unit("kara", 5, 7, "back")
    R.unit("aldric", 3, 7, "back", mirror=True)
    R.unit("ilyra", 7, 7, "back", mirror=True)
    R.unit("wren", 4, 8, "back")
    return R


def cave(frame: int = 0) -> Room:
    """The Glimmerdeep: a crystal cavern split by a chasm. A kobold mine runs out of a tunnel, over a plank
    bridge and down to the party's side; crystals and mushrooms light it cold, a kobold brazier warm, and water
    drips into the glowing puddles."""
    mats = {
        "R": Mat(("cave_floor", "deep"), ("cave_wall", "cool"), ("stalactite_lip", "cool")),
        "M": Mat(("mine_gravel", None), ("mine_wall", None), ("beam_lip", None)),
        "c": Mat(("cave_floor", "cool"), ("cave_wall", "cool"), ("stalactite_lip", "cool")),
        "g": Mat(("cave_floor", "brown"), ("cave_wall", "brown"), ("stalactite_lip", "brown")),
        "L": Mat(("cave_floor", "deep"), ("cave_wall", "deep"), ("stalactite_lip", "cool")),
        "m": Mat(("mine_gravel", None), ("cave_wall", "cool"), ("stalactite_lip", "cool")),
        "w": Mat(("wet_stone", "glow"), ("cave_wall", "cool"), ("stalactite_lip", "cool")),
        "F": Mat(("cave_floor", "cool"), ("chasm_side", "cool"), ("stalactite_lip", "cool")),
        "p": Mat(("void", None), ("chasm_side", "cool")),
        "B": Mat(("mine_boards", None), None, ("beam_lip", None)),
    }
    terrain = ["RRRRRRMRRRRR",
               "RcccccmcccLL",
               "RcwwccmccLLL",
               "RcwwccmccLLL",
               "RFFFFFmFFFFF",
               "RpppppBppppp",
               "RpppppBppppp",
               "RggwwgmggggR",
               "RgwwggmgggwR",
               "RggggggggwwR"]
    heights = ["999999499999",
               "944444444466",
               "944444444666",
               "944444444666",
               "944444444444",
               "900000400000",
               "900000400000",
               "955333333335",
               "955333333336",
               "953333333336"]
    R = Room(mats, terrain, heights, frame=frame, ambient=AMBIENT["cave"], seed=8,
             decals={"c": (0.08, [("pebbles_0", None), ("pebbles_1", None)]),
                     "g": (0.1, [("pebbles_0", None), ("bone_scatter", None)])})
    for j in range(1, 9):
        R.decal(6, j, "rails", None, transpose=True)
    R.decal(6, 9, "rails_end", None, transpose=True)
    R.prop("minecart", 6, 1.2, mirror=True)
    for i, j in ((2, 5), (9, 6), (5, 6)):                            # a faint glow welling up out of the chasm
        R.light(i, j, LIGHT_COLOURS["void"], 2.0, lift=-30, flicker="pulse", power=0.55)
    R.prop("crystal_big", 2, 1, "blue", phase=0)
    R.prop("crystal_big", 10, 1, "violet", phase=3)
    R.prop("crystal_big", 11, 4, "blue", phase=5)
    R.prop("crystal_big", 1, 7, "green", phase=2)
    R.prop("glow_mushrooms", 4, 3, "teal")
    R.prop("glow_mushrooms", 9, 8, "violet", phase=1)
    R.prop("glow_mushrooms", 1, 9, "teal", phase=2)
    R.prop("stalagmite_large", 11, 2, "deep")
    R.prop("stalagmite", 8, 1, "cool")
    R.prop("stalagmite_small", 5, 1, "cool")
    R.prop("stalagmite", 11, 8, "brown")
    R.prop("stalagmite_small", 4, 9, "brown")
    R.prop("treasure_hoard", 10, 2)
    R.prop("brazier", 4, 1, "fire", phase=1)
    R.prop("drip", 3, 2, phase=0)
    R.prop("drip", 3, 7, phase=5)
    R.prop("drip", 10, 9, phase=9)
    R.prop("bone_pile", 8, 3)
    R.unit("kobold", 5, 3, k=1)
    R.unit("kobold", 7, 4, mirror=True)
    R.unit("kobold_slinger", 3, 1)
    R.unit("kobold_slinger", 8, 3, mirror=True, k=2)
    R.beast("spider", 10, 3, "red", mirror=True)
    R.unit("tamsin", 4, 8, "back")
    R.unit("sefa", 7, 8, "back", mirror=True)
    R.unit("class_paladin", 6, 7, "back")
    R.unit("class_druid", 8, 9, "back", mirror=True)
    return R


def hall(frame: int = 0) -> Room:
    """The Hearth & Tome: a tavern whose back room is a wizard's library, on a raised parquet floor. Cultists
    have come for the books; a mimic waits by the door. Warm firelight, candles and a green-lit alchemy bench."""
    mats = {
        "W": Mat(("dungeon_floor", "warm"), ("dungeon_wall", "warm")),
        "f": Mat(("floorboards", "oak"), ("plank_side", None)),
        "P": Mat(("parquet", "oak"), ("plank_side", None)),
        "D": Mat(("floorboards", "oak"), ("dungeon_wall", "warm")),
    }
    terrain = ["WWWDWWWWWWWW",
               "WfffffPPPPPP",
               "WfffffPPPPPP",
               "WfffffPPPPPP",
               "Wfffffffffff",
               "Wfffffffffff",
               "Wfffffffffff",
               "Wfffffffffff",
               "Wfffffffffff"]
    heights = ["888388888888",
               "833333444444",
               "833333444444",
               "833333444444",
               "833333333333",
               "833333333333",
               "833333333333",
               "833333333333",
               "833333333333"]
    R = Room(mats, terrain, heights, frame=frame, ambient=AMBIENT["hall"], seed=12)
    for j in range(1, 8):
        R.decal(3, j, "carpet", "crimson", transpose=True)
    R.decal(3, 8, "carpet_end", "crimson", transpose=True)
    R.rug(7, 5, "teal")
    for i, tid, var, kw in ((1, "sconce", None, {}), (2, "wall_crest", "navy", {"drop": 2}),
                            (5, "sconce", None, {}), (6, "wall_crest", "crimson", {"drop": 2}),
                            (9, "sconce", None, {}), (11, "cobweb", None, {"drop": 0, "mirror": True})):
        R.wall(i, 0, "v", tid, var, **kw)
    for j, tid, var, kw in ((4, "sconce", None, {}), (6, "wall_crest", "navy", {"drop": 2}),
                            (8, "sconce", None, {})):
        R.wall(0, j, "u", tid, var, **kw)
    R.prop("door_arch", 3, 0.25, "oak")
    R.prop("fireplace", 0.72, 2, "grey", mirror=True, light=0.75)
    R.prop("barrel_tap", 1, 5, phase=1)
    R.prop("barrel_tap", 1, 6, phase=3)
    R.prop("tavern_table", 2, 3, phase=0)
    R.prop("bench", 2, 2)
    R.prop("stool", 3.6, 3.2)
    R.prop("tavern_table", 8, 6, phase=2)
    R.prop("chair", 7, 6, mirror=True)
    R.prop("chair", 8, 5)
    R.prop("stool", 9, 7)
    R.prop("weapon_rack", 1, 8, mirror=True)
    R.prop("armor_stand", 5, 1, "steel")
    R.prop("bookshelf", 7, 0.72, "oak")
    R.prop("bookshelf", 8, 0.72, "walnut")
    R.prop("bookshelf", 10, 0.72, "oak")
    R.prop("bookshelf_empty", 11, 0.72, "walnut")
    R.prop("throne", 9, 1, "gold")
    R.prop("candelabra", 6, 1, "gold", phase=3)
    R.prop("desk", 7, 2.3, "walnut", phase=1)
    R.prop("alchemy_table", 10.2, 2.6, "green")
    R.prop("cauldron", 11, 4, "violet")
    R.prop("candelabra", 10, 7, "gold", phase=5)
    R.boss("mimic", "oak", 5, 4, 10, mirror=True)
    R.boss("imp", "violet", 7, 4, 2, mirror=True, glow_colour="#b07bff")
    R.unit("cult_leader", 9, 2, mirror=True)
    R.unit("cultist", 8, 3, mirror=True, k=1)
    R.unit("cultist", 10, 5, mirror=True, k=2)
    R.unit("innkeeper", 2, 6, k=1)
    R.unit("kara", 4, 6, "back")
    R.unit("aldric", 3, 7, "back", mirror=True)
    R.unit("ilyra", 5, 8, "back")
    R.unit("wren", 6, 7, "back", mirror=True)
    return R


SCENES = {"crypt": crypt, "cave": cave, "hall": hall}


def render_scene(name: str, frame: int = 0, *, zoom: int = 2) -> Image.Image:
    return iso.finish(SCENES[name](frame).render(), zoom=zoom, bloom=0.3, vignette=0.55)


# ================================================================================================================
# Build
# ================================================================================================================
GIF_FRAMES = 24
"""The least common multiple of the loops on show: 8-frame fire, crystals and puddles, 4-frame mushrooms and
12-frame drips."""


def _sheet(imgs: list[Image.Image]) -> Image.Image:
    out = Image.new("RGBA", (imgs[0].width * len(imgs), imgs[0].height))
    for k, im in enumerate(imgs):
        out.paste(im, (k * im.width, 0))
    return out


WALL_DROP = {"sconce": 6, "wall_banner": 2, "wall_crest": 2, "cobweb": 0, "moss_drip": 0, "wall_runes": 8}
"""Suggested hanging height of each wall decal: texels below the wall's top edge (others: 6)."""


def _build_textures(out) -> tuple[dict, dict[str, list[Tile]]]:
    entries: dict = {}
    tiles: dict[str, list[Tile]] = {"top": [], "side": [], "lip": [], "decal": [], "wall": []}
    for t in TEXTURES:
        frames = _tframes(t.id)
        files = {}
        for vname, v in t.variants.items():
            imgs = [f.render(ramps(v)) for f in frames]
            rel = f"textures/{t.kind}/{t.id}_{vname}.png"
            save(_sheet(imgs), out / rel)
            files[vname] = rel
            sub = vname + (f" · {len(frames)}f" if len(frames) > 1 else "")
            tiles[t.kind].append(Tile(imgs[0], t.id, sub))
        e = {"kind": t.kind, "variants": files, "frames": len(frames), "fps": t.fps,
             "size": [frames[0].w, frames[0].h], "tags": t.tags}
        if t.note:
            e["note"] = t.note
        if t.kind == "wall":
            e["fromTop"] = WALL_DROP.get(t.id, 6)
        if "light" in t.tags:
            e["light"] = {"colour": {vn: ramps(v)["glow"][4] for vn, v in t.variants.items()},
                          "radius": 3.0 if t.id == "sconce" else 1.6,
                          "flicker": "torch" if t.id == "sconce" else "pulse",
                          "at": [15, 8] if t.id == "sconce" else [16, 20]}
        entries[t.id] = e
    return entries, tiles


def _build_props(out) -> tuple[dict, list[Tile]]:
    entries, tiles = {}, []
    for p in PROPS:
        frames = p.frames()
        w, h = frames[0].w, frames[0].h
        files = {}
        for vname, v in p.variants.items():
            pal = ramps(v, PS)
            imgs = [f.render(pal) for f in frames]
            rel = f"props/{p.id}_{vname}.png"
            save(_sheet(imgs), out / rel)
            files[vname] = rel
            shown = imgs[len(imgs) // 2] if "fx" in p.tags else imgs[0]
            tiles.append(Tile(shown, p.id, vname + (f" · {len(imgs)}f" if len(imgs) > 1 else "")))
        e = {"variants": files, "size": [w, h], "anchor": list(frames[0].anchor), "frames": len(frames),
             "fps": p.fps, "blocks": p.blocks, "cover": p.cover, "height": p.height, "tags": p.tags}
        if p.facing:
            e["facing"] = p.facing
        if p.light and frames[0].lights:
            slot, radius, fl = p.light
            e["light"] = {"at": [list(xy) for xy in frames[0].lights], "radius": radius, "flicker": fl,
                          "colour": {vn: ramps(v, PS)[slot][4] for vn, v in p.variants.items()}}
        entries[p.id] = e
    return entries, tiles


def _build_light(out) -> tuple[dict, list[Tile]]:
    entries: dict = {"pools": {}, "halos": {}}
    tiles = []
    for name, col in LIGHT_COLOURS.items():
        pool, hal = light_pool(col), halo(col)
        save(pool, out / f"light/pool_{name}.png")
        save(hal, out / f"light/halo_{name}.png")
        entries["pools"][name] = {"file": f"light/pool_{name}.png", "size": [64, 32], "anchor": [32, 16],
                                  "colour": col}
        entries["halos"][name] = {"file": f"light/halo_{name}.png", "size": [32, 32], "anchor": [16, 16],
                                  "colour": col}
        bg = Image.new("RGBA", (64, 48), (14, 12, 22, 255))
        a = np.asarray(bg).astype(np.float32)
        for im, (x, y) in ((pool, (0, 16)), (hal, (16, 0))):
            b = np.asarray(im).astype(np.float32)
            a[y:y + im.height, x:x + im.width, :3] += b[..., :3] * b[..., 3:4] / 255
        tiles.append(Tile(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA"), name, col))
    v = vignette()
    save(v, out / "light/vignette.png")
    entries["vignette"] = {"file": "light/vignette.png", "size": list(v.size), "blend": "alpha",
                           "note": "stretch over the battle view, above the map and below the UI"}
    return entries, tiles


LIGHTING = {
    "recipe": [
        "Render the map and sprites as usual (albedo).",
        "Light = ambient (per room kind, below) + the sum of every light: colour x power x flicker[frame] x falloff.",
        "Falloff = max(ground pool, source sphere): the pool is a 2:1 ellipse on the floor under the source, the "
        "sphere is round around the source itself, both (1 - d)^1.6 out to the light's radius in tiles.",
        "Band the light into steps of 1/7 with a 4x4 ordered dither, so pools step like pixel art.",
        "Lit = albedo x min(light, 1.3). Emissive pixels (prop slots glow / flame / liquid, texture slot glow) "
        "skip this: they keep their own colour, then the usual bloom picks them up.",
        "Optionally lay light/vignette.png over the view for the room's edges.",
    ],
    "godot": "Equivalent with real lights: a CanvasModulate / WorldEnvironment ambient of the room colour, an "
             "OmniLight3D (or PointLight2D) per manifest light at its 'at' point, colour and radius, energy driven "
             "by the flicker curve, and an unshaded material (or emission) on the emissive slots.",
    "ambient": {k: [round(c, 3) for c in v] for k, v in AMBIENT.items()},
    "flicker": FLICKER,
    "emissiveSlots": {"props": sorted(EMISSIVE | {"liquid"}), "textures": sorted(TEX_EMISSIVE)},
    "decalPools": "light/pool_*.png are the same falloff baked as additive decals, for a cheap path without "
                  "dynamic lights: add one under each light source, scaled to its radius.",
}


def _scene_preview(name: str, frame: int = 0) -> Image.Image:
    return iso.finish(SCENES[name](frame).render(), zoom=2, bloom=0.3, vignette=0.55)


def _lighting_preview(lt_tiles: list[Tile]) -> Image.Image:
    """The darkness recipe, step by step, on the crypt: albedo, light map, emissive mask, result."""
    base, lmap, em, lit = crypt(0).render(parts=True)
    box = (250, 150, 610, 420)
    steps = [Tile(im.crop(box).convert("RGBA"), lab, sub) for im, lab, sub in (
        (base, "1 · albedo", "the map as drawn"), (lmap, "2 · light", "ambient + pools, banded"),
        (em, "3 · emissive", "glow, flame and liquid slots"), (lit, "4 · lit", "albedo x light, emissive kept"))]
    a = contact_sheet("Darkness recipe", steps, columns=4, zoom=2,
                      subtitle="How the dioramas are lit (manifest 'lighting'): the same steps work in the game.")
    b = contact_sheet("Light pools and halos", lt_tiles, columns=8, zoom=3,
                      subtitle="Additive decals for a no-dynamic-lights path; banded and dithered like the recipe.")
    return stack("Dungeon · light", [a, b], subtitle="Interiors are dark: lights come from props and wall decals.")


def _gif() -> None:
    """previews/anim-dungeon.gif: the crypt and the cave side by side, every light and loop moving."""
    frames = []
    for k in range(GIF_FRAMES):
        a = iso.finish(crypt(k).render(), zoom=1, bloom=0.3, vignette=0.5)
        b = iso.finish(cave(k).render(), zoom=1, bloom=0.3, vignette=0.5)
        im = Image.new("RGB", (a.width + b.width, max(a.height, b.height)), (12, 10, 20))
        im.paste(a, (0, 0))
        im.paste(b, (a.width, 0))
        frames.append(im.crop((40, 60, im.width - 30, im.height - 40)))
    probe = Image.new("RGB", (frames[0].width, frames[0].height * 3))
    for n, k in enumerate((0, 8, 16)):
        probe.paste(frames[k], (0, n * frames[0].height))
    pal = probe.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    gif = [f.quantize(palette=pal, dither=Image.Dither.NONE) for f in frames]
    gif[0].save(PREVIEWS / "anim-dungeon.gif", save_all=True, append_images=gif[1:], duration=110, loop=0,
                optimize=True)


def build() -> None:
    out = PACK / "dungeon"
    shutil.rmtree(out, ignore_errors=True)
    tex_entries, tex_tiles = _build_textures(out)
    prop_entries, prop_tiles = _build_props(out)
    light_entries, light_tiles = _build_light(out)
    write_json({
        "tile": TILE, "span": T // TILE, "heightStep": TILE // 2,
        "sampling": {
            "top": "UV = world XZ / (span * tile): one texture covers span x span tiles",
            "side": "U = distance along the edge / (span * tile); V = depth below the top / tile (repeats); "
                    "chasm_side is two tiles deep and clamps",
            "lip": "drawn over the top band of a side under a top that has one; transparent below its edge",
            "decal": "32x32 alpha on a tile's top in tile-local UV; 'rug' is 64x64 over 2x2 tiles",
            "wall": "32x48 alpha on a side face, one tile wide, hung 'fromTop' texels below the wall's top "
                    "(the texture entry suggests a value); drawn for a +V face, mirror for +U",
            "animated": "frames are laid out left to right; play at fps, looping",
            "sameLayout": "dungeon_floor, _mossy, _cracked and _bloody share one slab layout: mix them per tile",
        },
        "textures": tex_entries,
        "props": prop_entries,
        "propNotes": {"anchor": "the ground contact pixel (the tile centre under the prop); stand it on the tile "
                                "centre",
                      "frames": "animated props are horizontal strips; loop at fps",
                      "cover": "none | half | full, for ranged attacks",
                      "height": "in height steps (16 px), for line of sight",
                      "facing": "'+V': drawn against a wall, facing +V (screen lower-left); mirror for a +U wall. "
                                "'U': spans the tile along U; mirror to run along V",
                      "light": "'at' is the light's source pixel in the frame; colour per variant, radius in tiles, "
                               "flicker names a curve in lighting.flicker"},
        "light": light_entries,
        "lighting": LIGHTING,
    }, out / "manifest.json")

    tops = contact_sheet("Dungeon · floors", tex_tiles["top"], columns=6, zoom=2,
                         subtitle="Seamless and world-aligned (128 px = 4x4 tiles). The four flagstone states share "
                                  "one slab layout; wet stone and the void are animated.")
    sides = contact_sheet("Dungeon · walls and lips", tex_tiles["side"] + tex_tiles["lip"], columns=4, zoom=3,
                          subtitle="Sides repeat every tile of depth; the chasm side fades into the void over two.")
    save(stack("Dungeon · textures", [tops, sides]), PREVIEWS / "32-dungeon-textures.png")
    dec = contact_sheet("Dungeon · ground decals", tex_tiles["decal"], columns=9, zoom=4, checker=True,
                        subtitle="Tile-local: rails and carpet runners continue tile to tile (transpose for V).")
    wal = contact_sheet("Dungeon · wall decals", tex_tiles["wall"], columns=9, zoom=4, checker=True,
                        subtitle="Hung on a side face, one tile wide: sconces flicker, runes pulse.")
    save(stack("Dungeon · decals", [dec, wal]), PREVIEWS / "32-dungeon-decals.png")
    save(contact_sheet("Dungeon · props", prop_tiles, columns=10, zoom=3, checker=True,
                       subtitle="Drawn in the map's projection with one key light, anchored at the tile centre. "
                                "The manifest carries blocks / cover / height, facing and each light source."),
         PREVIEWS / "32-dungeon-props.png")
    save(_lighting_preview(light_tiles), PREVIEWS / "32-dungeon-light.png")
    for name in SCENES:
        save(_scene_preview(name), PREVIEWS / f"32-dungeon-{name}.png")
    _gif()
