"""Terrain textures for the 3D tile map: tops, cliff sides, edge lips and ground decals.

All tops and sides are seamless and *world-aligned*: a 128 px texture spans 4 tiles of 32 px, and the game samples it
by world position (top: UV = world XZ / 4 tiles; side: U = distance along the edge / 4 tiles, V = depth below the top
/ 1 tile). Neighbouring tiles therefore continue one another instead of stamping the same 32 px square everywhere.

Everything is drawn in (slot, shade) like the characters, then rendered per *variant* palette, so "grass" also ships as
lush / dry / autumn without redrawing anything.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Callable

import numpy as np

from arcpix.canvas import Canvas, SlotTable
from arcpix.palette import FAMILIES
from arcpix import tex

T = 128          # texture span: 4 tiles
TILE = 32
SIDE_H = 32      # side textures repeat every tile of height
LIP_H = 12
DECAL = 32

SLOTS = SlotTable()
for _s in ("main", "alt", "grout", "accent", "metal", "glow"):
    SLOTS.id(_s)


def _canvas(w: int, h: int, shade: np.ndarray, slot: np.ndarray | str = "main") -> Canvas:
    c = Canvas(w, h, SLOTS)
    c.shade[:] = np.clip(shade, 0, 5).astype(np.uint8)
    if isinstance(slot, str):
        c.slot[:] = SLOTS.id(slot)
    else:
        c.slot[:] = slot
    return c


def _ids(*names: str) -> list[int]:
    return [SLOTS.id(n) for n in names]


# ------------------------------------------------------------------------------------------------------- tops
# Grass blade tufts, (dx, dy, delta from the local shade). Light tips over a dark root read as blades in 3/4 view.
TUFTS = [
    # a single blade
    [(0, 0, +1), (0, 1, -1)],
    # a pair leaning apart
    [(-1, -1, +1), (1, -1, +1), (-1, 0, +1), (1, 0, +1), (-1, 1, -1), (0, 1, -1), (1, 1, -1)],
    # a tall centre blade with short flanks
    [(0, -2, +2), (0, -1, +1), (-2, 0, +1), (2, -1, +1), (0, 0, +1), (2, 0, +1), (-2, 1, -1), (-1, 1, -1),
     (0, 1, -1), (1, 1, -1), (2, 1, -1)],
    # a clump of three
    [(-1, -1, +1), (1, -2, +2), (1, -1, +1), (3, -1, +1), (-1, 0, +1), (3, 0, +1), (-1, 1, -1), (0, 1, -1),
     (1, 1, -1), (2, 1, -1), (3, 1, -1)],
]


def grass(seed: int = 11, *, density: float = 4.2) -> Canvas:
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=3)
    shade = tex.quantize(n, [(0.30, 2), (0.78, 4)], 3, dither=0.05)
    base = shade.copy()
    for x, y in tex.poisson(T, T, density, r):
        pat = TUFTS[int(r.choice(len(TUFTS), p=[0.35, 0.3, 0.2, 0.15]))]
        b = int(base[y, x])
        tex.stamp(shade, x, y, pat, lambda old, d, b=b: min(5, max(1, b + d)))
    return _canvas(T, T, shade)


def _pebble(shade: np.ndarray, x: int, y: int, w: int) -> None:
    """A rounded pebble lit from the top-left, with a cast shadow below-right."""
    h_, w_ = shade.shape
    def put(dx, dy, v):
        shade[(y + dy) % h_, (x + dx) % w_] = v
    for dx in range(1, w - 1) if w > 2 else range(w):
        put(dx, 0, 4)
    put(1 if w > 2 else 0, 0, 5)
    for dx in range(w):
        put(dx, 1, 3)
    put(w - 1, 1, 2)
    for dx in range(1, w + 1):
        put(dx, 2, 1)


def dirt(seed: int = 21, *, pebbles: float = 9.0, cracks: int = 6) -> Canvas:
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=4)
    shade = tex.quantize(n, [(0.28, 2), (0.76, 4)], 3, dither=0.05)
    fine = tex.fbm(T, T, 32, r, octaves=1)
    shade[(fine < 0.10)] -= 1
    shade = tex.despeckle(shade, 1)
    for _ in range(cracks):
        x, y = int(r.integers(T)), int(r.integers(T))
        for _ in range(int(r.integers(4, 9))):
            shade[y % T, x % T] = 1
            x += int(r.choice([-1, 0, 1, 1]))
            y += int(r.choice([-1, 0, 1]))
    for x, y in tex.poisson(T, T, pebbles, r):
        _pebble(shade, x, y, int(r.integers(2, 5)))
    return _canvas(T, T, shade)


def sand(seed: int = 31) -> Canvas:
    r = tex.rng(seed)
    warp = tex.fbm(T, T, 4, r, octaves=2)
    ys, xs = np.mgrid[0:T, 0:T]
    f = np.sin(2 * np.pi * (5 * xs + 2 * ys) / T + warp * 5.0)
    shade = np.full((T, T), 3)
    crest = f > 0.80
    shadow = np.roll(crest, 1, 0) & ~crest
    shade[crest] = 4
    shade[shadow] = 2
    soft = tex.fbm(T, T, 4, r, octaves=2)
    shade[(soft > 0.75) & (shade == 3)] = 4
    shade = tex.despeckle(shade, 1)
    grains = r.random((T, T))
    shade[grains < 0.006] = 5
    shade[(grains > 0.994)] = 2
    return _canvas(T, T, shade)


def _stones(seed: int, cell: int, jitter: float, grout_w: float, *, bevel: float = 0.6, tones=(-1, 0, 0, 0, 1),
            cell_h: int | None = None, stagger: bool = False, w: int = T, h: int = T, sx=1.0, sy=1.0,
            crack_p: float = 0.15) -> Canvas:
    """Voronoi stone pavement: flagstones, cobbles, cliff blocks. Each stone gets a tone, a bevel lit from the
    top-left, a few stains and the odd crack; the gaps become grout."""
    r = tex.rng(seed)
    pts = tex.jittered_points(w, h, cell, jitter, r, cell_h=cell_h, stagger=stagger)
    f1, f2, idx = tex.voronoi(w, h, pts, sx=sx, sy=sy)
    gap = f2 - f1
    tone = np.array([tones[int(r.integers(len(tones)))] for _ in range(len(pts))])
    height = np.sqrt(np.clip(gap / (cell * 0.35), 0, 1))
    em = tex.emboss(height)
    shade = 3 + tone[idx]
    shade = np.where(em > 0.18 * bevel / 0.6, shade + 1, shade)
    shade = np.where(em < -0.18 * bevel / 0.6, shade - 1, shade)
    stains = tex.fbm(w, h, 8, r, octaves=2)
    shade = np.where(stains < 0.16, shade - 1, shade)
    speck = r.random((h, w))
    shade = np.where(speck < 0.02, shade + 1, shade)
    slot = np.full((h, w), SLOTS.id("main"))
    grout = gap < grout_w
    slot[grout] = SLOTS.id("grout")
    # Grout is darker under a stone's lower edge (the stone above casts onto it).
    below = np.roll(~grout, 1, 0)
    shade = np.where(grout, np.where(below, 1, 2), shade)
    # Cracks across some stones.
    for k in range(len(pts)):
        if r.random() < crack_p:
            x, y = int(pts[k, 0]), int(pts[k, 1])
            for _ in range(int(r.integers(3, cell // 2 + 2))):
                if idx[y % h, x % w] != k:
                    break
                shade[y % h, x % w] = 1
                x += int(r.choice([-1, 1, 1]))
                y += int(r.choice([0, 1]))
    c = _canvas(w, h, shade, slot)
    return c


def flagstone(seed: int = 41) -> Canvas:
    return _stones(seed, 16, 0.22, 1.5)


def cobble(seed: int = 51) -> Canvas:
    return _stones(seed, 8, 0.28, 1.25, bevel=0.9, tones=(-1, 0, 0, 1), crack_p=0.0)


def rock(seed: int = 61) -> Canvas:
    """Natural bedrock: big faceted plates with hairline cracks, no grout."""
    r = tex.rng(seed)
    pts = tex.jittered_points(T, T, 32, 0.35, r)
    f1, f2, idx = tex.voronoi(T, T, pts)
    tone = r.choice([-1, 0, 0, 1], len(pts))
    # Each plate is a tilted plane: a random gradient gives facets that catch the light differently.
    gx, gy = r.uniform(-1, 1, len(pts)), r.uniform(-1, 1, len(pts))
    ys, xs = np.mgrid[0:T, 0:T]
    plane = (gx[idx] * tex.wrapped_delta(xs, pts[idx, 0], T) + gy[idx] * tex.wrapped_delta(ys, pts[idx, 1], T)) / 32
    n = tex.fbm(T, T, 8, r, octaves=3)
    v = plane * 0.8 + (n - 0.5) * 1.2
    shade = 3 + tone[idx] + np.where(v > 0.35, 1, 0) - np.where(v < -0.35, 1, 0)
    gap = f2 - f1
    shade = np.where(gap < 1.0, 1, shade)
    shade = np.where((gap >= 1.0) & (gap < 2.0) & (np.roll(gap, 1, 0) < 1.0), 4, shade)  # lit lip below a crack
    shade = tex.despeckle(shade, 1)
    return _canvas(T, T, shade)


def snow(seed: int = 71) -> Canvas:
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=3)
    shade = tex.quantize(n, [(0.36, 3), (0.14, 2)], 4, dither=0.04)
    drift = tex.fbm(T, T, 8, r, octaves=2, aspect=3.0)
    shade = np.where((drift > 0.74) & (shade == 4), 5, shade)
    # Wind-carved ripples: a lit crest with a soft shadow under it.
    ys, xs = np.mgrid[0:T, 0:T]
    rip = np.sin(2 * np.pi * (1 * xs + 6 * ys) / T + n * 6.0) > 0.93
    shade = np.where(rip & (shade >= 4), 5, shade)
    shade = np.where(np.roll(rip, 1, 0) & ~rip & (shade >= 4), 3, shade)
    sp = r.random((T, T))
    shade[(sp < 0.005) & (shade >= 3)] = 5
    return _canvas(T, T, shade)


def planks(seed: int = 81, *, w: int = T, h: int = T, plank: int = 8) -> Canvas:
    """Boards running along x: staggered butt joints, grain streaks, nail heads, a lit top edge per board."""
    r = tex.rng(seed)
    grain = tex.fbm(w, h, 8, r, octaves=3, aspect=8.0)
    shade = np.full((h, w), 3)
    shade[grain < 0.3] = 2
    shade[grain > 0.72] = 4
    slot = np.full((h, w), SLOTS.id("main"))
    for row in range(h // plank):
        y0 = row * plank
        tone = int(r.choice([-1, 0, 0]))
        shade[y0:y0 + plank] = np.minimum(shade[y0:y0 + plank] + tone, 4)
        shade[y0] = np.maximum(shade[y0], 4)              # lit top edge
        shade[y0 + plank - 1] = 0                          # gap between boards
        start = int(r.integers(0, w))
        x = start
        while x < start + w:                               # seams round the wrap, so the row tiles
            for yy in range(y0, y0 + plank - 1):
                shade[yy, x % w] = 1                       # butt joint
                shade[yy, (x + 1) % w] = min(5, shade[yy, (x + 1) % w] + 1)
            ny = y0 + plank // 2 - 1
            for nx in (x + 2, x - 2):                      # a nail either side of the joint
                slot[ny, nx % w] = SLOTS.id("metal")
                shade[ny, nx % w] = 4
                shade[ny + 1, nx % w] = 1
            x += int(r.integers(24, 56))
        knot = (int(r.integers(w)), y0 + int(r.integers(2, plank - 2)))
        shade[knot[1], knot[0]] = 1
        shade[knot[1], (knot[0] + 1) % w] = 2
    return _canvas(w, h, shade, slot)


def water_frames(seed: int = 91, frames: int = 8) -> list[Canvas]:
    """Animated water: two noise layers drifting in opposite directions, travelling wave crests and glints. Each
    layer moves T/frames px per frame, so frame `frames` equals frame 0 and the loop is seamless."""
    r = tex.rng(seed)
    n1 = tex.fbm(T, T, 4, r, octaves=3)
    n2 = tex.fbm(T, T, 8, r, octaves=2)
    glint = r.random((T, T))
    ys, xs = np.mgrid[0:T, 0:T]
    step = T // frames
    out = []
    for f in range(frames):
        a = tex.wrap_roll(n1, f * step, 0)
        b = tex.wrap_roll(n2, 0, -f * step)
        shade = np.full((T, T), 2)
        shade[a < 0.32] = 1
        shade[(a > 0.66)] = 3
        wave = np.sin(2 * np.pi * (2 * xs + 3 * ys) / T - 2 * np.pi * f / frames + a * 4.0 + b * 2.0)
        crest = wave > 0.86
        shade[crest] = np.maximum(shade[crest], 3) + 1
        g = tex.wrap_roll(glint, f * step, 0)
        shade[(g < 0.004) & crest] = 5
        shade = tex.despeckle(shade, 1)
        out.append(_canvas(T, T, shade))
    return out


def lava_frames(seed: int = 101, frames: int = 8) -> list[Canvas]:
    """Animated lava: dark crust plates on a glowing flow. Cracks breathe (widen and narrow) with a travelling
    pulse, and the glow inside the widest cracks reaches white-hot."""
    r = tex.rng(seed)
    pts = tex.jittered_points(T, T, 16, 0.45, r)
    f1, f2, idx = tex.voronoi(T, T, pts)
    gap = f2 - f1
    height = np.sqrt(np.clip(gap / 5.0, 0, 1))
    em = tex.emboss(height)
    pulse_field = tex.fbm(T, T, 4, r, octaves=2)
    river = np.clip((tex.fbm(T, T, 2, r, octaves=2) - 0.62) * 14, 0, 6)   # wide molten channels
    crust_tone = r.choice([-1, 0, 0], len(pts))
    out = []
    for f in range(frames):
        ph = tex.wrap_roll(pulse_field, f * (T // frames), f * (T // frames))
        width = 1.2 + river + 1.4 * (0.5 + 0.5 * np.sin(2 * np.pi * (ph * 2 - f / frames)))
        glow = gap < width
        slot = np.where(glow, SLOTS.id("glow"), SLOTS.id("alt"))
        shade = np.where(glow, np.where(gap < width * 0.35, 5, np.where(gap < width * 0.7, 4, 3)),
                         2 + crust_tone[idx] + (em > 0.15) - (em < -0.15))
        # Ember speckles on the crust near the cracks.
        near = (~glow) & (gap < width + 1.2)
        shade = np.where(near, np.minimum(shade + 1, 3), shade)
        out.append(_canvas(T, T, shade, slot))
    return out


def ice(seed: int = 111) -> Canvas:
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=3)
    shade = np.full((T, T), 3)
    shade[n > 0.62] = 4
    shade[n < 0.25] = 2
    shade = tex.despeckle(shade, 2)
    # Cracks: branching random walks, each with a bright edge on its lit side.
    for _ in range(7):
        x, y = int(r.integers(T)), int(r.integers(T))
        dx, dy = r.choice([-1, 1]), r.choice([-1, 1])
        for _ in range(int(r.integers(10, 26))):
            shade[y % T, x % T] = 1
            shade[(y - 1) % T, (x - 1) % T] = max(shade[(y - 1) % T, (x - 1) % T], 4)
            if r.random() < 0.6:
                x += dx
            if r.random() < 0.6:
                y += dy
            if r.random() < 0.12:
                dx, dy = -dy, dx
    # Specular streaks, top-left to bottom-right.
    for _ in range(10):
        x, y = int(r.integers(T)), int(r.integers(T))
        for k in range(int(r.integers(2, 5))):
            shade[(y + k) % T, (x + k) % T] = 5
    return _canvas(T, T, shade)


def mud(seed: int = 121) -> Canvas:
    """Swamp ground: mud with standing puddles (accent slot = murky water) and the odd reed tuft (alt slot)."""
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=3)
    shade = tex.quantize(n, [(0.25, 2), (0.74, 4)], 3, dither=0.05)
    slot = np.full((T, T), SLOTS.id("main"))
    pud = tex.fbm(T, T, 4, r, octaves=2)
    wet = pud < 0.26
    slot[wet] = SLOTS.id("accent")
    shade[wet] = 2
    rim = wet & ~np.roll(wet, 1, 0)                         # top edge of a puddle: reflected sky
    shade[rim] = 4
    under = ~wet & np.roll(wet, -1, 0)                     # wet bank above it
    shade[under] = 1
    gl = r.random((T, T))
    shade[wet & (gl < 0.01)] = 5
    for x, y in tex.poisson(T, T, 14, r):
        if not wet[y, x]:
            for dx, dy, d in TUFTS[int(r.integers(len(TUFTS)))]:
                slot[(y + dy) % T, (x + dx) % T] = SLOTS.id("alt")
                shade[(y + dy) % T, (x + dx) % T] = 3 + d
    return _canvas(T, T, shade, slot)


# ------------------------------------------------------------------------------------------------------- sides
def dirt_side(seed: int = 131) -> Canvas:
    """Earth cliff: wavy strata, embedded stones (accent) and roots."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    warp = tex.fbm(w, h, 4, r, octaves=2)
    ys, xs = np.mgrid[0:h, 0:w]
    band = np.sin(2 * np.pi * (ys / 8.0 + warp * 0.9))
    shade = np.full((h, w), 3)
    shade[band > 0.55] = 4
    shade[band < -0.6] = 2
    fine = tex.fbm(w, h, 16, r, octaves=1)
    shade[fine < 0.15] -= 1
    shade = tex.despeckle(shade, 2)
    slot = np.full((h, w), SLOTS.id("main"))
    for x, y in tex.poisson(w, h, 11, r, tries=600):
        sw, sh = int(r.integers(3, 6)), int(r.integers(2, 4))
        for dy in range(sh):
            for dx in range(sw):
                if (dx in (0, sw - 1)) and (dy in (0, sh - 1)):
                    continue                                   # round the corners
                yy, xx = (y + dy) % h, (x + dx) % w
                slot[yy, xx] = SLOTS.id("accent")
                shade[yy, xx] = 4 if dy == 0 else (2 if dy == sh - 1 else 3)
        shade[(y + sh) % h, (x + 1) % w] = 1
    for _ in range(5):
        x, y = int(r.integers(w)), int(r.integers(h))
        for _ in range(int(r.integers(4, 9))):
            if slot[y % h, x % w] == SLOTS.id("main"):
                shade[y % h, x % w] = 1
            x += int(r.choice([-1, 1]))
            y += 1
    return _canvas(w, h, shade, slot)


def cliff_side(seed: int = 141) -> Canvas:
    return _stones(seed, 16, 0.18, 1.2, w=T, h=SIDE_H, cell_h=8, stagger=True, sy=1.6, crack_p=0.1,
                   tones=(-1, 0, 0, 1))


def brick_side(seed: int = 151) -> Canvas:
    """Laid brick: 16x8 bricks in running bond, 1 px mortar, a bevel per brick and chipped corners."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    shade = np.full((h, w), 3)
    slot = np.full((h, w), SLOTS.id("main"))
    stains = tex.fbm(w, h, 8, r, octaves=2)
    for row in range(h // 8):
        y0 = row * 8
        off = 8 if row % 2 else 0
        for b in range(w // 16):
            x0 = b * 16 + off
            tone = int(r.choice([-1, 0, 0, 1]))
            for yy in range(y0, y0 + 8):
                for xx in range(x0, x0 + 16):
                    X = xx % w
                    if yy == y0 + 7 or xx == x0 + 15:
                        slot[yy, X] = SLOTS.id("grout")
                        shade[yy, X] = 2 if yy == y0 + 7 else 1
                        continue
                    s = 3 + tone
                    if yy == y0:
                        s += 1
                    elif yy == y0 + 6 or xx == x0 + 14:
                        s -= 1
                    if stains[yy, X] < 0.18:
                        s -= 1
                    shade[yy, X] = s
            if r.random() < 0.3:                            # a chipped corner
                cx = (x0 + (0 if r.random() < 0.5 else 13)) % w
                cy = y0 + (0 if r.random() < 0.5 else 5)
                for dy in range(2):
                    for dx in range(2):
                        slot[cy + dy, (cx + dx) % w] = SLOTS.id("grout")
                        shade[cy + dy, (cx + dx) % w] = 2
    return _canvas(w, h, shade, slot)


def ashlar_side(seed: int = 161) -> Canvas:
    """Castle masonry: big dressed blocks (32x16) with tooling marks."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    shade = np.full((h, w), 3)
    slot = np.full((h, w), SLOTS.id("main"))
    n = tex.fbm(w, h, 8, r, octaves=3)
    for row in range(h // 16):
        y0 = row * 16
        off = 16 if row % 2 else 0
        for b in range(w // 32):
            x0 = b * 32 + off
            tone = int(r.choice([-1, 0, 0, 1]))
            for yy in range(y0, y0 + 16):
                for xx in range(x0, x0 + 32):
                    X = xx % w
                    if yy == y0 + 15 or xx == x0 + 31:
                        slot[yy, X] = SLOTS.id("grout")
                        shade[yy, X] = 1
                        continue
                    s = 3 + tone
                    if yy == y0 or xx == x0:
                        s += 1
                    elif yy == y0 + 14 or xx == x0 + 30:
                        s -= 1
                    if n[yy, X] < 0.2:
                        s -= 1
                    elif n[yy, X] > 0.85:
                        s += 1
                    shade[yy, X] = s
    return _canvas(w, h, shade, slot)


def sandstone_side(seed: int = 171) -> Canvas:
    r = tex.rng(seed)
    w, h = T, SIDE_H
    warp = tex.fbm(w, h, 4, r, octaves=2, aspect=2.0)
    ys, xs = np.mgrid[0:h, 0:w]
    layer = ((ys + warp * 6) // 4).astype(int) % 4
    shade = np.choose(layer, [3, 4, 3, 2])
    top = (((ys + warp * 6) % 4) < 1)
    shade = np.where(top, shade + 1, shade)
    pits = r.random((h, w)) < 0.01
    shade = np.where(pits, 1, shade)
    shade = tex.despeckle(shade, 1)
    slot = np.where(layer == 1, SLOTS.id("alt"), SLOTS.id("main"))
    return _canvas(w, h, shade, slot)


def plank_side(seed: int = 181) -> Canvas:
    """Vertical boards (walls, crates of terrain scale): the plank generator, turned 90 degrees."""
    c = planks(seed, w=SIDE_H, h=T)
    out = Canvas(T, SIDE_H, SLOTS)
    out.slot[:] = c.slot.T
    out.shade[:] = c.shade.T
    return out


# -------------------------------------------------------------------------------------------------------- lips
def lip(seed: int, *, kind: str) -> Canvas:
    """The overhang drawn over the top band of a side where it meets a top: grass hanging over the edge, snow
    cornices with icicles, moss with vines. Transparent below its ragged edge."""
    r = tex.rng(seed)
    w, h = T, LIP_H
    n = tex.fbm(w, 16, 16 if kind != "snow" else 8, r, octaves=2)[0]     # 1D noise: one row of a 2D field
    c = Canvas(w, h, SLOTS)
    for x in range(w):
        if kind == "grass":
            depth = 2 + int(n[x] * 4) + (3 if r.random() < 0.08 else 0)
        elif kind == "snow":
            depth = 2 + int(n[x] * 3)
        else:  # moss
            depth = 2 + int(n[x] * 3) + (int(r.integers(3, 8)) if r.random() < 0.06 else 0)
        depth = min(depth, h - 1)
        for y in range(depth):
            if y == 0:
                s = 4
            elif y == depth - 1:
                s = 1
            elif y == depth - 2:
                s = 2
            else:
                s = 3
            c.slot[y, x] = SLOTS.id("main")
            c.shade[y, x] = s
        if kind == "snow" and r.random() < 0.05:              # icicle
            for y in range(depth, min(h, depth + int(r.integers(2, 5)))):
                c.slot[y, x] = SLOTS.id("accent")
                c.shade[y, x] = 4 if y == depth else 3
    if kind == "grass":                                       # blade tips along the rim
        for x in range(0, w, 3):
            c.shade[0, (x + int(r.integers(0, 2))) % w] = 5
    return c


# ------------------------------------------------------------------------------------------------------- decals
def _decal_canvas() -> Canvas:
    return Canvas(DECAL, DECAL, SLOTS)


def flowers(seed: int) -> Canvas:
    """A patch of wildflowers: 5-9 blossoms (accent petals, glow centre) on stems (alt) with the odd leaf."""
    r = tex.rng(seed)
    c = _decal_canvas()
    cx, cy = int(r.integers(10, 22)), int(r.integers(10, 22))
    for _ in range(int(r.integers(5, 10))):
        x = int(np.clip(cx + r.normal(0, 5), 3, DECAL - 4))
        y = int(np.clip(cy + r.normal(0, 4), 3, DECAL - 5))
        if c.slot[y, x]:
            continue
        c.px(x, y + 2, "alt", 2)                                   # stem
        c.px(x, y + 3, "alt", 1)
        if r.random() < 0.5:
            c.px(x + 1, y + 3, "alt", 3)                           # a leaf
        for dx, dy, sh in ((0, -1, 4), (-1, 0, 3), (1, 0, 4), (0, 1, 2)):
            c.px(x + dx, y + dy, "accent", sh)
        c.px(x, y, "glow", 4)
    return c


def _stone(c: Canvas, x: int, y: int, w: int, h: int) -> None:
    """A rounded stone lit from the top-left with a soft shadow under it."""
    for dy in range(h):
        inset = 1 if dy in (0, h - 1) and w > 2 else 0
        for dx in range(inset, w - inset):
            sh = 4 if dy == 0 else (2 if dy == h - 1 else 3)
            if dx == w - 1 - inset and dy > 0:
                sh -= 1
            c.px(x + dx, y + dy, "main", sh)
    c.px(x + 1 if w > 2 else x, y, "main", 5)
    for dx in range(1, w):
        c.px(x + dx, y + h, "grout", 0)


def pebbles(seed: int) -> Canvas:
    r = tex.rng(seed)
    c = _decal_canvas()
    for _ in range(int(r.integers(4, 8))):
        x, y = int(r.integers(4, DECAL - 8)), int(r.integers(4, DECAL - 7))
        _stone(c, x, y, int(r.integers(2, 6)), int(r.integers(2, 4)))
    return c


def leaves(seed: int) -> Canvas:
    """Fallen leaves: little 3-4 px leaf shapes at random angles."""
    r = tex.rng(seed)
    c = _decal_canvas()
    shapes = [[(0, 0), (1, 0), (1, 1), (2, 1)], [(0, 1), (1, 1), (1, 0), (2, 0)], [(0, 0), (0, 1), (1, 1), (1, 2)],
              [(1, 0), (0, 1), (1, 1), (2, 1), (1, 2)]]
    for _ in range(int(r.integers(5, 10))):
        x, y = int(r.integers(3, DECAL - 4)), int(r.integers(3, DECAL - 4))
        s = shapes[int(r.integers(len(shapes)))]
        tone = int(r.choice([2, 3, 3, 4]))
        for dx, dy in s:
            c.px(x + dx, y + dy, "accent", tone)
        c.px(x + s[0][0], y + s[0][1], "accent", min(5, tone + 1))
    return c


SKULL = ["..###..",
         ".#####.",
         "##0#0##",
         "#######",
         ".#t#t#."]


def bones(seed: int) -> Canvas:
    """A skull over crossed bones."""
    r = tex.rng(seed)
    c = _decal_canvas()
    sx, sy = 13 + int(r.integers(-2, 3)), 8 + int(r.integers(-2, 3))
    for (ax, ay), (bx, by) in (((sx - 3, sy + 7), (sx + 9, sy + 13)), ((sx - 3, sy + 13), (sx + 9, sy + 7))):
        c.line(ax, ay, bx, by, "main", 3)
        for ex, ey in ((ax, ay), (bx, by)):                       # knobbly ends
            c.px(ex - 1, ey, "main", 4)
            c.px(ex, ey + 1, "main", 2)
    for dy, row in enumerate(SKULL):
        for dx, ch in enumerate(row):
            if ch == "#":
                c.px(sx + dx, sy + dy, "main", 4 if dy < 2 else 3)
            elif ch == "0":
                c.px(sx + dx, sy + dy, "grout", 0)
            elif ch == "t":
                c.px(sx + dx, sy + dy, "main", 5)
    c.px(sx + 2, sy, "main", 5)
    return c


def cracks(seed: int) -> Canvas:
    """Ground fissures from a heavy impact: jagged arms at random angles, each with a fork."""
    r = tex.rng(seed)
    c = _decal_canvas()

    def arm(x: float, y: float, ang: float, length: int, fork: bool) -> None:
        for i in range(length):
            ang += r.normal(0, 0.28)
            x += np.cos(ang)
            y += np.sin(ang) * 0.6                               # squashed: it lies on the ground
            if not (1 <= x < DECAL - 1 and 1 <= y < DECAL - 1):
                return
            c.px(x, y, "grout", 0)
            if i < length // 3:
                c.px(x, y + 1, "grout", 1)                       # wider near the impact
            if fork and i == length // 2:
                arm(x, y, ang + r.choice([-1, 1]) * 0.9, length // 2, False)

    n = int(r.integers(4, 6))
    for k in range(n):
        arm(16, 16, 2 * np.pi * k / n + r.normal(0, 0.3), int(r.integers(11, 17)), True)
    return c


def puddle(seed: int) -> Canvas:
    r = tex.rng(seed)
    c = _decal_canvas()
    n = tex.fbm(DECAL, DECAL, 4, r, octaves=2)
    ys, xs = np.mgrid[0:DECAL, 0:DECAL]
    d = ((xs - 16) / 12.0) ** 2 + ((ys - 16) / 8.0) ** 2 + (n - 0.5) * 0.8
    wet = d < 1.0
    c.slot[wet] = SLOTS.id("accent")
    c.shade[wet] = 2
    rim = wet & ~np.roll(wet, 1, 0)
    c.shade[rim] = 4
    c.shade[wet & (d < 0.25)] = 1
    for _ in range(3):
        x, y = int(r.integers(10, 22)), int(r.integers(13, 19))
        if wet[y, x]:
            c.px(x, y, "accent", 5)
            c.px(x + 1, y, "accent", 4)
    return c


def blood(seed: int) -> Canvas:
    r = tex.rng(seed)
    c = _decal_canvas()
    for _ in range(int(r.integers(4, 8))):
        x, y = 16 + int(r.normal(0, 4)), 16 + int(r.normal(0, 3))
        rad = r.uniform(0.8, 2.6)
        c.ellipse(x, y, rad * 1.3, rad, "accent", 2)
        c.px(x - 1, y - 1, "accent", 4)
    return c


def scorch(seed: int) -> Canvas:
    """Burn mark from a fire spell: charred centre, a sooty ragged ring and a few embers."""
    r = tex.rng(seed)
    c = _decal_canvas()
    n = tex.fbm(DECAL, DECAL, 4, r, octaves=2)
    ys, xs = np.mgrid[0:DECAL, 0:DECAL]
    d = np.sqrt(((xs - 16) / 13.0) ** 2 + ((ys - 16) / 9.0) ** 2) + (n - 0.5) * 0.5
    c.slot[d < 1.0] = SLOTS.id("grout")
    c.shade[d < 1.0] = 1
    c.shade[d < 0.6] = 0
    c.slot[(d >= 0.85) & (d < 1.0) & (r.random((DECAL, DECAL)) < 0.5)] = 0
    for _ in range(4):
        x, y = 16 + int(r.normal(0, 4)), 16 + int(r.normal(0, 2.5))
        c.px(x, y, "glow", 4)
    return c


# ------------------------------------------------------------------------------------------------------ catalogue
@dataclass
class TerrainTex:
    id: str
    kind: str                                    # top | side | lip | decal
    make: Callable[[], Canvas | list[Canvas]]
    variants: dict[str, dict[str, str]]          # variant -> slot -> "family/ramp"
    fps: float = 0.0
    tags: list[str] = field(default_factory=list)

    def frames(self) -> list[Canvas]:
        m = self.make()
        return m if isinstance(m, list) else [m]


def _v(**slots: str) -> dict[str, str]:
    return {k: (v if "/" in v else f"terrain/{v}") for k, v in slots.items()}


CATALOGUE: list[TerrainTex] = [
    # tops
    TerrainTex("grass", "top", grass, {
        "green": _v(main="grass"), "lush": _v(main="grass_lush"), "dry": _v(main="grass_dry"),
        "autumn": _v(main="grass_autumn")}),
    TerrainTex("dirt", "top", dirt, {"brown": _v(main="dirt"), "dark": _v(main="dirt_dark")}),
    TerrainTex("path", "top", lambda: dirt(23, pebbles=6.5, cracks=2), {"packed": _v(main="path")}),
    TerrainTex("sand", "top", sand, {"desert": _v(main="sand")}),
    TerrainTex("flagstone", "top", flagstone, {
        "grey": _v(main="stone", grout="mortar"), "warm": _v(main="stone_warm", grout="mortar"),
        "dark": _v(main="stone_dark", grout="mortar")}),
    TerrainTex("cobble", "top", cobble, {"grey": _v(main="stone", grout="dirt_dark"),
                                          "warm": _v(main="stone_warm", grout="dirt_dark")}),
    TerrainTex("rock", "top", rock, {"grey": _v(main="stone"), "dark": _v(main="stone_dark")}),
    TerrainTex("snow", "top", snow, {"fresh": _v(main="snow")}),
    TerrainTex("planks", "top", planks, {"oak": _v(main="planks", metal="metal/iron"),
                                          "dark": _v(main="wood/dark", metal="metal/iron")}),
    TerrainTex("ice", "top", ice, {"clear": _v(main="ice")}),
    TerrainTex("mud", "top", mud, {"swamp": _v(main="mud", accent="water_swamp", alt="moss")}),
    TerrainTex("water", "top", water_frames, {"clear": _v(main="water"), "swamp": _v(main="water_swamp")}, fps=6),
    TerrainTex("lava", "top", lava_frames, {"molten": _v(glow="lava", alt="crust")}, fps=6),
    # sides
    TerrainTex("dirt_side", "side", dirt_side, {"brown": _v(main="dirt", accent="stone"),
                                                "dark": _v(main="dirt_dark", accent="stone_dark")}),
    TerrainTex("cliff_side", "side", cliff_side, {"grey": _v(main="stone", grout="stone_dark"),
                                                  "warm": _v(main="stone_warm", grout="dirt_dark"),
                                                  "dark": _v(main="stone_dark", grout="mortar")}),
    TerrainTex("brick_side", "side", brick_side, {"red": _v(main="brick", grout="mortar")}),
    TerrainTex("ashlar_side", "side", ashlar_side, {"grey": _v(main="stone", grout="mortar"),
                                                    "warm": _v(main="stone_warm", grout="mortar")}),
    TerrainTex("sandstone_side", "side", sandstone_side, {"desert": _v(main="sand", alt="stone_warm")}),
    TerrainTex("plank_side", "side", plank_side, {"oak": _v(main="planks", metal="metal/iron")}),
    # lips
    TerrainTex("grass_lip", "lip", lambda: lip(191, kind="grass"), {
        "green": _v(main="grass"), "lush": _v(main="grass_lush"), "dry": _v(main="grass_dry"),
        "autumn": _v(main="grass_autumn")}),
    TerrainTex("snow_lip", "lip", lambda: lip(193, kind="snow"), {"fresh": _v(main="snow", accent="ice")}),
    TerrainTex("moss_lip", "lip", lambda: lip(197, kind="moss"), {"green": _v(main="moss")}),
    # decals
    *[TerrainTex(f"flowers_{i}", "decal", (lambda i=i: flowers(200 + i)), {
        "red": _v(accent="flower_red", alt="grass_lush", glow="flower_yellow"),
        "yellow": _v(accent="flower_yellow", alt="grass_lush", glow="flower_white"),
        "white": _v(accent="flower_white", alt="grass_lush", glow="flower_yellow"),
        "blue": _v(accent="flower_blue", alt="grass_lush", glow="flower_white")}) for i in range(3)],
    *[TerrainTex(f"pebbles_{i}", "decal", (lambda i=i: pebbles(210 + i)), {"grey": _v(main="stone", grout="crust")})
      for i in range(2)],
    *[TerrainTex(f"leaves_{i}", "decal", (lambda i=i: leaves(220 + i)), {
        "autumn": _v(accent="foliage/leaf_autumn"), "red": _v(accent="foliage/leaf_red"),
        "green": _v(accent="foliage/leaf_light")}) for i in range(2)],
    TerrainTex("bones", "decal", lambda: bones(230), {"bone": _v(main="skin/bone", grout="mortar")}),
    TerrainTex("cracks", "decal", lambda: cracks(231), {"dark": _v(grout="crust")}),
    TerrainTex("puddle", "decal", lambda: puddle(232), {"water": _v(accent="water"), "swamp": _v(accent="water_swamp")}),
    TerrainTex("blood", "decal", lambda: blood(233), {"red": _v(accent="magic/blood")}),
    TerrainTex("scorch", "decal", lambda: scorch(234), {"burnt": _v(grout="crust", glow="lava")}),
]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {}
    for slot, ref in variant.items():
        fam, name = ref.split("/")
        out[slot] = FAMILIES[fam][name]
    return out
