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
from arcpix.palette import FAMILIES, ramp
from arcpix import tex

T = 128          # texture span: 4 tiles
TILE = 32
SIDE_H = 32      # side textures repeat every tile of height
LIP_H = 12
DECAL = 32

SLOTS = SlotTable()
for _s in ("main", "alt", "grout", "accent", "metal", "glow", "root", "moss"):
    SLOTS.id(_s)

TR_RAMPS = {
    # Biome materials the shared palette doesn't carry; variants name them "tr/<name>".
    "sandstone": ramp("#c98f5a", shift=0.06),
    "sandstone_red": ramp("#b8674a", shift=0.06),
    "dune": ramp("#dfbd84", shift=0.05, spread=0.8),
    "basalt": ramp("#4a4652", shift=0.05, spread=1.15),
    "ash": ramp("#6e6468", shift=0.04, spread=1.0),
    "obsidian": ramp("#33293f", shift=0.06, spread=1.2),
    "cinder": ramp("#8a4a32", shift=0.06),
    "bog": ramp("#3f5136", shift=0.05, spread=1.1),
    "peat": ramp("#4e3b2c", shift=0.05, spread=1.1),
    "duckweed": ramp("#86ad3e", shift=0.07),
    "litter": ramp("#7a5532", shift=0.06),
    "needles": ramp("#8a6438", shift=0.06),
    "forest_moss": ramp("#4f7a35", shift=0.08),
    "root": ramp("#8c7058", shift=0.05, spread=1.0),
    "frost": ramp("#bcd9ec", shift=0.04, spread=0.7),
    "glacier": ramp("#7fb6d8", shift=0.05, spread=0.95),
    "paver": ramp("#a49b8e", shift=0.05, spread=0.95),
    "paver_dark": ramp("#6f6a72", shift=0.05, spread=1.05),
    "clay": ramp("#a8744c", shift=0.06),
    "foam": ramp("#d6edf5", shift=0.04, spread=0.6),
    "bone": ramp("#d9cfb4", shift=0.05, spread=0.8),
}


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


def _soft_mask(field: np.ndarray, t: float, dither: float) -> np.ndarray:
    """field > t, with a checkerboard band either side of the threshold: a patch that fades instead of ending in a
    hard blob outline."""
    ck = tex.checker(*field.shape[::-1])
    return (field > t + dither) | ((field > t - dither) & ck)


def grass(seed: int = 11, *, density: float = 4.2) -> Canvas:
    """Meadow grass: broad light and shadow masses, patches of a neighbouring green (the `alt` ramp) so a field is
    never one flat hue, and blades drawn as *clumps*: a dark root pool with three to six tufts standing in it,
    tips lit from the top-left. Sparse single tufts fill the gaps."""
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=3)
    shade = tex.quantize(n, [(0.30, 2), (0.78, 4)], 3, dither=0.05)
    hue = tex.fbm(T, T, 8, r, octaves=2)
    slot = np.where(_soft_mask(hue, 0.74, 0.025), SLOTS.id("alt"), SLOTS.id("main"))
    base = shade.copy()

    def blade(x: int, y: int, b: int, hgt: int, lean: int) -> None:
        for k in range(hgt):
            xx, yy = (x + (lean * k) // 2) % T, (y - k) % T
            shade[yy, xx] = min(5, max(1, b + (2 if k == hgt - 1 and hgt > 2 else 1 if k else -1)))

    for cx, cy in tex.poisson(T, T, 9.0, r, tries=1800):
        b = int(base[cy, cx])
        for dx in range(-2, 3):                                       # the clump's shadowed root pool
            shade[(cy + 1) % T, (cx + dx) % T] = max(1, b - 1)
        shade[(cy + 2) % T, (cx + 1) % T] = max(1, b - 1)
        k = int(r.integers(4, 7))
        for i in range(k):                                            # blades fan out from the root
            off = int(round((i - (k - 1) / 2) * 1.2))
            lean = -1 if off < -1 else (1 if off > 1 else 0)
            blade(cx + off, cy, b, int(r.integers(3, 6)) - (abs(off) > 1), lean)
    for x, y in tex.poisson(T, T, density * 1.5, r, tries=2500):
        if shade[y, x] == base[y, x]:
            blade(x, y, int(base[y, x]), int(r.integers(2, 4)), int(r.choice([-1, 0, 0, 1])))
    return _canvas(T, T, shade, slot)


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
            crack_p: float = 0.15, wear: bool = False) -> Canvas:
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
    if wear:
        # Feet polish the middles of the stones (a soft lit patch) and knock chips out of their edges: a notch of
        # grout with a lit lip on its far side.
        polish = (gap > cell * 0.3) & (tex.fbm(w, h, 8, r, octaves=2) > 0.68)
        shade = tex.despeckle(np.where(polish, shade + 1, shade), 1)
        rim = (gap >= grout_w) & (gap < grout_w + 1.6)
        chips = np.zeros((h, w), bool)
        for x, y in tex.poisson(w, h, cell * 0.55, r, tries=900 if cell >= 12 else 0):
            if rim[y, x]:
                for dx, dy in ((0, 0), (1, 0), (0, 1)):
                    chips[(y + dy) % h, (x + dx) % w] = True
        chips &= ~grout
        lip = np.roll(np.roll(chips, 1, 0), 1, 1) & ~chips & ~grout
        grout = grout | chips
        shade = np.where(lip, shade + 1, shade)
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
    return _stones(seed, 16, 0.22, 1.5, wear=True)


def cobble(seed: int = 51) -> Canvas:
    return _stones(seed, 8, 0.28, 1.25, bevel=0.9, tones=(-1, 0, 0, 1), crack_p=0.0, wear=True)


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
    """Fresh snow in soft drifts: a wind-stretched height field lit from the top-left, so each drift has a bright
    windward face and a cool blue lee shadow, with checkerboard-dithered terminators (snow has no hard edges).
    Wind ripples ride the crests and a few two-pixel glints catch the sun."""
    r = tex.rng(seed)
    hgt = tex.fbm(T, T, 4, r, octaves=2, aspect=2.5)
    hgt = hgt + 0.2 * tex.fbm(T, T, 8, r, octaves=1, aspect=3.0)
    em = tex.emboss(hgt, light=(-3, -3))
    em = np.argsort(np.argsort(em.ravel())).reshape(em.shape) / em.size    # rank: an even share of each shade
    shade = tex.quantize(em, [(0.36, 3), (0.12, 2), (0.84, 5)], 4, dither=0.04)
    hollow = (hgt - hgt.min()) / (np.ptp(hgt) + 1e-9) < 0.12
    shade = np.where(hollow & (shade >= 4), 3, shade)
    ys, xs = np.mgrid[0:T, 0:T]
    rip = (np.sin(2 * np.pi * (1 * xs + 8 * ys) / T + hgt * 9.0) > 0.92) & (shade == 4)
    shade = np.where(rip, 5, shade)
    shade = np.where(np.roll(rip, 1, 0) & ~rip & (shade == 4), 3, shade)
    shade = tex.despeckle(shade, 1)
    for x, y in tex.poisson(T, T, 14, r, tries=400):
        if shade[y, x] >= 4:
            shade[y, x] = 5
            shade[y, (x + 1) % T] = 5
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


def _phase(n: np.ndarray, f: int, frames: int, k: float = 1.0) -> np.ndarray:
    """A shimmer that loops: every pixel runs through one sine cycle per loop, offset by a noise field, so the
    pattern churns in place and frame `frames` equals frame 0 exactly."""
    return np.sin(2 * np.pi * (f / frames * k + n * 3.0))


def water_frames(seed: int = 91, frames: int = 16, *, duckweed: bool = False) -> list[Canvas]:
    """Animated water that flows: deep and shallow masses that churn in place, wave crests travelling one
    wavelength per loop down the flow (+U, +V), a web of caustic light on the shallows and glints riding the
    crests. Every motion is a whole cycle per loop, so the 16 frames loop seamlessly at any fps.

    With `duckweed`, rafts of weed (accent slot) lie on it, for still bog water."""
    r = tex.rng(seed)
    n1 = tex.fbm(T, T, 4, r, octaves=3)
    n2 = tex.fbm(T, T, 8, r, octaves=2)
    n3 = tex.fbm(T, T, 4, r, octaves=2)
    c1, c2 = tex.fbm(T, T, 8, r, octaves=2), tex.fbm(T, T, 8, r, octaves=2)
    glint = r.random((T, T))
    weed = tex.fbm(T, T, 8, r, octaves=3)
    ys, xs = np.mgrid[0:T, 0:T]
    out = []
    for f in range(frames):
        a = n1 + 0.05 * _phase(n3, f, frames)
        shade = np.full((T, T), 2)
        shade[a < 0.34] = 1
        shade[a > 0.64] = 3
        wave = np.sin(2 * np.pi * (2 * xs + 3 * ys) / T - 2 * np.pi * f / frames * 2 + a * 4.0 + n2 * 2.0)
        crest = wave > 0.88
        trough = np.roll(crest, -1, 0) & ~crest                       # the wave's shaded back, just above its crest
        shade = np.where(trough & (shade > 1), shade - 1, shade)
        shade = np.where(crest, np.maximum(shade, 3) + 1, shade)
        v = _phase(c1, f, frames)
        w = _phase(c2, f, frames, k=-1.0)
        caustic = (np.abs(v - w) < 0.09) & (a > 0.52) & ~crest
        shade = np.where(caustic, np.maximum(shade, 3) + (a > 0.62), shade)
        g = tex.wrap_roll(glint, f * (T // frames), f * (T // frames))
        shade = np.where((g < 0.006) & crest, 5, shade)
        shade = tex.despeckle(np.clip(shade, 0, 5), 1)
        slot = np.full((T, T), SLOTS.id("main"))
        if duckweed:                                                   # bog water is still: the weed stays put
            wd = weed
            raft = _soft_mask(wd, 0.7, 0.02)
            slot = np.where(raft, SLOTS.id("accent"), slot)
            lit = raft & ~np.roll(raft, 1, 0)
            shade = np.where(raft, np.where(lit, 4, np.where(wd > 0.8, 3, 2)), shade)
            rim = ~raft & np.roll(raft, 1, 0)                         # the raft's shadow on the water
            shade = np.where(rim, 1, shade)
        out.append(_canvas(T, T, shade, slot))
    return out


def lava_frames(seed: int = 101, frames: int = 16) -> list[Canvas]:
    """Animated lava: crust plates riding a glowing flow. Molten channels carry streaks that travel down the flow
    (one wavelength per loop), the cracks between plates breathe with a pulse that runs along them, and the glow
    is white-hot only in the widest seams. Plates darken towards their middles: they cool from the inside."""
    r = tex.rng(seed)
    pts = tex.jittered_points(T, T, 16, 0.45, r)
    f1, f2, idx = tex.voronoi(T, T, pts)
    gap = f2 - f1
    height = np.sqrt(np.clip(gap / 5.0, 0, 1))
    em = tex.emboss(height)
    pulse_field = tex.fbm(T, T, 4, r, octaves=2)
    river = np.clip((tex.fbm(T, T, 2, r, octaves=2) - 0.62) * 14, 0, 6)   # wide molten channels
    crust_tone = r.choice([-1, 0, 0], len(pts))
    ys, xs = np.mgrid[0:T, 0:T]
    warp = tex.fbm(T, T, 4, r, octaves=2)
    out = []
    for f in range(frames):
        ph = _phase(pulse_field, f, frames)
        width = 1.2 + river + 1.2 * (0.5 + 0.5 * ph)
        glow = gap < width
        flow = np.sin(2 * np.pi * (xs + ys) / 32 - 2 * np.pi * f / frames + warp * 6)
        slot = np.where(glow, SLOTS.id("glow"), SLOTS.id("alt"))
        hot = np.where(gap < width * 0.35, 5, np.where(gap < width * 0.7, 4, 3))
        hot = np.where((river > 1) & glow, np.where(flow > 0.55, 5, np.where(flow < -0.6, 3, 4)), hot)
        crust = 2 + crust_tone[idx] + (em > 0.15) - (em < -0.15) - (gap > 5.5)
        shade = np.where(glow, hot, crust)
        near = (~glow) & (gap < width + 1.2)                           # crust edges warmed by the seam
        shade = np.where(near, np.minimum(shade + 1, 3), shade)
        out.append(_canvas(T, T, np.clip(shade, 0, 5), slot))
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


def forest_floor(seed: int = 251, *, needles: bool = False) -> Canvas:
    """Woodland ground: dark earth half hidden under drifts of leaf litter (or pine needles), cushions of moss lit
    on their top-left rims, and fallen twigs. Litter lies in clusters, so the floor reads as layers, not grit."""
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=3)
    shade = tex.quantize(n, [(0.32, 2), (0.76, 4)], 3, dither=0.04)
    slot = np.full((T, T), SLOTS.id("main"))
    _moss_patches(shade, slot, r, cells=4, thresh=0.76)
    drift = tex.fbm(T, T, 8, r, octaves=2)
    leaf_shapes = [[(0, 0), (1, 0), (1, 1), (2, 1)], [(0, 1), (1, 1), (1, 0), (2, 0)], [(0, 0), (0, 1), (1, 1)],
                   [(1, 0), (0, 1), (1, 1), (2, 1), (1, 2)]]
    for x, y in tex.poisson(T, T, 2.4, r, tries=7000):
        if slot[y, x] == SLOTS.id("moss") or drift[y, x] < 0.45 + 0.2 * r.random():
            continue
        if needles:
            dx = int(r.choice([-1, 1]))
            for k in range(int(r.integers(3, 5))):
                xx, yy = (x + k * dx) % T, (y + k // 2) % T
                slot[yy, xx] = SLOTS.id("accent")
                shade[yy, xx] = 4 if k == 0 else 3
            shade[(y + 1) % T, x % T] = max(1, int(shade[(y + 1) % T, x % T]) - 1)
            continue
        tone = int(r.choice([2, 3, 3, 4]))
        shp = leaf_shapes[int(r.integers(len(leaf_shapes)))]
        for dx, dy in shp:
            slot[(y + dy) % T, (x + dx) % T] = SLOTS.id("accent")
            shade[(y + dy) % T, (x + dx) % T] = tone
        slot[y % T, x % T] = SLOTS.id("accent")
        shade[y % T, x % T] = min(5, tone + 1)
        lx, ly = (x + shp[-1][0] + 1) % T, (y + shp[-1][1] + 1) % T          # its shadow on the ground
        if slot[ly, lx] == SLOTS.id("main"):
            shade[ly, lx] = 1
    for _ in range(9):                                                         # twigs
        x, y = int(r.integers(T)), int(r.integers(T))
        dx, dy = int(r.choice([-1, 1])), int(r.choice([0, 1]))
        for k in range(int(r.integers(4, 9))):
            xx, yy = (x + k * dx) % T, (y + (k * dy) // 2) % T
            slot[yy, xx] = SLOTS.id("root")
            shade[yy, xx] = 4 if k % 3 == 0 else 3
            shade[(yy + 1) % T, xx] = 1 if slot[(yy + 1) % T, xx] != SLOTS.id("root") else shade[(yy + 1) % T, xx]
    return _canvas(T, T, shade, slot)


def dunes(seed: int = 261) -> Canvas:
    """Desert dunes: long asymmetric ridges (a gentle windward slope, then a steep slip face) running across the
    texture, with a sunlit crest line, a shadowed slip face and fine wind ripples on the windward side."""
    r = tex.rng(seed)
    warp = tex.fbm(T, T, 2, r, octaves=3)
    ys, xs = np.mgrid[0:T, 0:T]
    ph = ((1 * xs + 2 * ys) / T * 2.0 + warp * 0.7) % 1.0               # two ridges per wrap
    shade = np.full((T, T), 3)
    wind = ph < 0.78
    shade = np.where(wind & (ph > 0.42), 4, shade)
    shade = np.where(~wind, 2, shade)
    shade = np.where((ph > 0.74) & (ph <= 0.78), 5, shade)               # crest
    shade = np.where((ph > 0.95), 1, shade)                              # the deepest shade at the slip toe
    rip = np.sin(2 * np.pi * (6 * xs + 11 * ys) / T + warp * 6) > 0.7
    shade = np.where(rip & wind & (shade >= 3) & (ph < 0.72), shade - 1, shade)
    soft = tex.checker(T, T)
    edge = (np.abs(ph - 0.42) < 0.02) & soft
    shade = np.where(edge, 3, shade)
    shade = tex.despeckle(shade, 1)
    g = r.random((T, T))
    shade = np.where((g < 0.004) & (shade >= 3), 5, shade)
    return _canvas(T, T, shade)


def sandstone_top(seed: int = 271) -> Canvas:
    """Wind-worn sandstone pavement: big plates split along the bedding (alt slot), each plate scoured into
    layered ledges that catch the light."""
    r = tex.rng(seed)
    pts = tex.jittered_points(T, T, 32, 0.3, r)
    f1, f2, idx = tex.voronoi(T, T, pts, sx=1.0, sy=1.3)
    gap = f2 - f1
    ys, xs = np.mgrid[0:T, 0:T]
    warp = tex.fbm(T, T, 4, r, octaves=2)
    layer = ((ys + xs * 0.25 + warp * 14) // 5).astype(int)
    shade = 3 + (layer % 3 == 0) - (layer % 4 == 3)
    edge = ((ys + xs * 0.25 + warp * 14) % 5) < 1
    shade = np.where(edge, shade + 1, shade)
    tone = r.choice([-1, 0, 0, 1], len(pts))
    shade = shade + tone[idx]
    slot = np.where(layer % 5 == 2, SLOTS.id("alt"), SLOTS.id("main"))
    shade = np.where(gap < 1.2, 1, shade)
    shade = np.where((gap >= 1.2) & (gap < 2.2) & (np.roll(gap, 1, 0) < 1.2), 4, shade)
    pits = tex.fbm(T, T, 16, r, octaves=1) < 0.08
    shade = np.where(pits, shade - 1, shade)
    shade = tex.despeckle(np.clip(shade, 1, 5), 1)
    return _canvas(T, T, shade, slot)


def ash(seed: int = 281) -> Canvas:
    """Volcanic ash flats: soft grey drifts, scattered cinders that cast tiny shadows, and a few embers still
    glowing in the ash (glow slot, emissive)."""
    r = tex.rng(seed)
    n = tex.fbm(T, T, 4, r, octaves=3, aspect=1.5)
    shade = tex.quantize(n, [(0.3, 2), (0.72, 4)], 3, dither=0.04)
    slot = np.full((T, T), SLOTS.id("main"))
    for x, y in tex.poisson(T, T, 11, r):
        sw = int(r.integers(2, 5))
        for dy in range(2):
            for dx in range(sw - dy):
                slot[(y + dy) % T, (x + dx + dy) % T] = SLOTS.id("alt")
                shade[(y + dy) % T, (x + dx + dy) % T] = (4 if dx == 0 else 3) if dy == 0 else 2
        for dx in range(1, sw + 1):
            if slot[(y + 2) % T, (x + dx) % T] == SLOTS.id("main"):
                shade[(y + 2) % T, (x + dx) % T] = 1
    for x, y in tex.poisson(T, T, 18, r, tries=300):
        slot[y, x] = SLOTS.id("glow")
        shade[y, x] = 5
        slot[y, (x + 1) % T] = SLOTS.id("glow")
        shade[y, (x + 1) % T] = 3
    return _canvas(T, T, shade, slot)


def _hex_points(cell: int, r: np.random.Generator, jitter: float = 0.08) -> np.ndarray:
    """Points on a hexagonal lattice that wraps the texture: basalt columns crack into near-perfect hexagons."""
    rows = T // (cell * 3 // 4) // 2 * 2
    dy = T / rows
    cols = T // cell
    pts = []
    for j in range(rows):
        for i in range(cols):
            pts.append(((i + 0.5 * (j % 2)) * cell + r.uniform(-jitter, jitter) * cell,
                        j * dy + r.uniform(-jitter, jitter) * cell))
    return np.array(pts) % [T, T]


def basalt(seed: int = 291) -> Canvas:
    """Columnar basalt seen from above: hexagonal column tops at slightly different heights, each lit on its
    top-left rim, with dark joints and a dusting of ash in the lowest columns."""
    r = tex.rng(seed)
    pts = _hex_points(16, r)
    f1, f2, idx = tex.voronoi(T, T, pts)
    gap = f2 - f1
    lvl = r.choice([-1, 0, 0, 1], len(pts))
    height = np.sqrt(np.clip(gap / 4, 0, 1)) + lvl[idx] * 0.4
    em = tex.emboss(height)
    shade = 3 + lvl[idx] + (em > 0.25) - (em < -0.25)
    shade = np.where(gap < 1.1, 0, shade)
    n = tex.fbm(T, T, 8, r, octaves=2)
    slot = np.where((lvl[idx] < 0) & (n > 0.55) & (gap > 2), SLOTS.id("alt"), SLOTS.id("main"))
    shade = np.where(slot == SLOTS.id("alt"), 2 + (n > 0.7), shade)
    shade = tex.despeckle(np.clip(shade, 0, 5), 1)
    return _canvas(T, T, shade, slot)


def pavers(seed: int = 301) -> Canvas:
    """Castle courtyard paving: big dressed slabs (32x16) laid in running bond, worn hollow in their middles,
    chipped at their corners, with moss and weeds in the joints."""
    r = tex.rng(seed)
    shade = np.full((T, T), 3)
    slot = np.full((T, T), SLOTS.id("main"))
    n = tex.fbm(T, T, 8, r, octaves=3)
    ys, xs = np.mgrid[0:T, 0:T]
    for row in range(T // 16):
        y0 = row * 16
        off = (16 if row % 2 else 0) + int(r.integers(-2, 3))
        for b in range(T // 32 + 1):
            x0 = b * 32 + off
            tone = int(r.choice([-1, 0, 0, 0, 1]))
            for yy in range(y0, y0 + 16):
                for xx in range(x0, x0 + 32):
                    X = xx % T
                    if yy == y0 + 15 or xx == x0 + 31:
                        slot[yy, X] = SLOTS.id("grout")
                        shade[yy, X] = 1 if yy == y0 + 15 else 2
                        continue
                    sv = 3 + tone
                    if yy == y0 or xx == x0:
                        sv += 1
                    elif yy == y0 + 14 or xx == x0 + 30:
                        sv -= 1
                    cx, cy = (xx - x0 - 15) / 13, (yy - y0 - 7) / 6
                    if cx * cx + cy * cy < 0.5 and n[yy, X] > 0.55:
                        sv += 1                                       # polished by feet
                    if n[yy, X] < 0.18:
                        sv -= 1
                    shade[yy, X] = sv
            for _ in range(int(r.integers(0, 3))):                    # chipped corners
                cx = (x0 + int(r.choice([0, 29]))) % T
                cy = y0 + int(r.choice([0, 13]))
                for dy in range(2):
                    for dx in range(2):
                        slot[cy + dy, (cx + dx) % T] = SLOTS.id("grout")
                        shade[cy + dy, (cx + dx) % T] = 2
    grout = slot == SLOTS.id("grout")
    weed = grout & (tex.fbm(T, T, 8, r, octaves=2) > 0.68)
    slot = np.where(weed, SLOTS.id("moss"), slot)
    shade = np.where(weed, np.where(np.roll(weed, 1, 0), 3, 4), shade)
    return _canvas(T, T, np.clip(shade, 0, 5), slot)


# ------------------------------------------------------------------------------------------------------- sides
def _root(shade: np.ndarray, slot: np.ndarray, x: int, y: int, length: int, r: np.random.Generator) -> None:
    """A hanging root: a 1-2 px strand that wanders down, lit on its left edge, thinning to a hair at the tip."""
    h, w = shade.shape
    for k in range(length):
        yy, xx = (y + k) % h, x % w
        slot[yy, xx] = SLOTS.id("root")
        shade[yy, xx] = 3 if k < length - 2 else 2
        if k < length * 2 // 3:
            slot[yy, (xx + 1) % w] = SLOTS.id("root")
            shade[yy, (xx + 1) % w] = 1
            shade[yy, xx] = 4
        if r.random() < 0.3:
            x += int(r.choice([-1, 1]))


def _crack_v(shade: np.ndarray, slot: np.ndarray, x: int, y: int, length: int, r: np.random.Generator,
             only: int | None = None) -> None:
    """A vertical hairline crack with a lit pixel on its right wall (light from the top-left lands there)."""
    h, w = shade.shape
    for _ in range(length):
        yy, xx = y % h, x % w
        if only is None or slot[yy, xx] == only:
            shade[yy, xx] = 0
            if shade[yy, (xx + 1) % w] >= 2:
                shade[yy, (xx + 1) % w] = min(5, shade[yy, (xx + 1) % w] + 1)
        x += int(r.choice([-1, 0, 0, 1]))
        y += 1


def _moss_patches(shade: np.ndarray, slot: np.ndarray, r: np.random.Generator, *, cells: int = 8,
                  thresh: float = 0.8, on: tuple[int, ...] | None = None) -> None:
    """Moss growing in clumps: a noise threshold, lit on top, shadowed along its lower rim, dithered at its edge."""
    h, w = shade.shape
    n = tex.fbm(w, h, cells, r, octaves=2)
    m = _soft_mask(n, thresh, 0.025)
    if on is not None:
        m &= np.isin(slot, on)
    top = m & ~np.roll(m, 1, 0)
    bot = m & ~np.roll(m, -1, 0)
    fine = tex.fbm(w, h, 32, r, octaves=1)
    body = np.where(fine > 0.62, 4, np.where(fine < 0.3, 2, 3))      # cushions within the patch
    slot[m] = SLOTS.id("moss")
    shade[m] = np.where(top[m], 5, np.where(bot[m], 1, body[m]))


def dirt_side(seed: int = 131) -> Canvas:
    """Earth cliff: four strata per tile of depth (dark topsoil, a pale clay band, plain earth and a gravel bed full
    of stones), each with a lit upper ledge and a shadowed underside, plus hanging roots, hairline cracks and the
    odd clump of moss. The bands wander, but repeat exactly every tile of depth."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    warp = tex.fbm(w, h, 4, r, octaves=3, aspect=2.0)
    ys, xs = np.mgrid[0:h, 0:w]
    pos = (ys + warp * 6.0 + 1.5 * np.sin(2 * np.pi * xs / w * 3)) % h
    edges = np.array([0, 6, 13, 22, 32])                           # uneven strata: thin topsoil, thick gravel
    band = np.searchsorted(edges, pos, side="right") - 1
    inner = pos - edges[band]
    thick = edges[band + 1] - edges[band]
    shade = np.choose(band, [2, 3, 3, 3])
    shade = np.where(inner < 1, shade + 1, shade)
    shade = np.where(inner >= thick - 1, shade - 1, shade)
    fine = tex.fbm(w, h, 16, r, octaves=2)
    shade = np.where(fine < 0.18, shade - 1, np.where(fine > 0.86, shade + 1, shade))
    shade = tex.despeckle(np.clip(shade, 1, 5), 2)
    slot = np.where(band == 1, SLOTS.id("alt"), SLOTS.id("main"))
    for x, y in tex.poisson(w, h, 7, r, tries=900):
        if band[y, x] not in (0, 3) or (band[y, x] == 0 and r.random() < 0.7):
            continue
        sw, sh = int(r.integers(3, 6)), int(r.integers(2, 4))
        for dy in range(sh):
            for dx in range(sw):
                if (dx in (0, sw - 1)) and (dy in (0, sh - 1)):
                    continue                                   # round the corners
                yy, xx = (y + dy) % h, (x + dx) % w
                slot[yy, xx] = SLOTS.id("accent")
                shade[yy, xx] = (5 if dx == 1 else 4) if dy == 0 else (2 if dy == sh - 1 else 3)
        shade[(y + sh) % h, (x + 1) % w] = 1
        shade[(y + sh) % h, (x + 2) % w] = 1
    for _ in range(4):
        _crack_v(shade, slot, int(r.integers(w)), int(r.integers(h)), int(r.integers(5, 11)), r,
                 only=SLOTS.id("main"))
    for x in tex.poisson(w, 1, 22, r, tries=200):
        _root(shade, slot, x[0], int(r.integers(0, 3)), int(r.integers(10, 18)), r)
    return _canvas(w, h, shade, slot)


def cliff_side(seed: int = 141) -> Canvas:
    """Coursed rock: the Voronoi block wall, then weathering on top of it: dark rain streaks running down from the
    joints, a few long cracks through several blocks and moss in the lower joints."""
    c = _stones(seed, 16, 0.18, 1.2, w=T, h=SIDE_H, cell_h=8, stagger=True, sy=1.6, crack_p=0.1,
                tones=(-1, 0, 0, 1))
    r = tex.rng(seed + 1)
    shade, slot = c.shade.astype(int), c.slot.copy()
    streak = tex.value_noise(T, SIDE_H, 32, 1, r)
    run = tex.value_noise(T, SIDE_H, 32, 4, r)
    stain = (streak > 0.72) & (run > 0.45) & (slot == SLOTS.id("main"))
    shade = np.where(stain, shade - 1, shade)
    for _ in range(3):
        _crack_v(shade, slot, int(r.integers(T)), int(r.integers(SIDE_H)), int(r.integers(10, 20)), r)
    grout = slot == SLOTS.id("grout")
    near = grout | np.roll(grout, 1, 0) | np.roll(grout, -1, 1)
    n = tex.fbm(T, SIDE_H, 8, r, octaves=2)
    moss = near & (n > 0.72)
    slot = np.where(moss, SLOTS.id("moss"), slot)
    shade = np.where(moss, np.where(np.roll(moss, 1, 0), 3, 4), shade)
    return _canvas(T, SIDE_H, shade, slot)


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


def lava_side_frames(seed: int = 321, frames: int = 16) -> list[Canvas]:
    """A crusted lava cliff, not the lava top on its side: wide slabs of cooled crust stacked in ledges, each lit
    on its upper rim and shadowed underneath, with most joints dark and cold. Only some joints glow, where the
    molten core shows through, plus a few cracks that wander down the face; heat pulses run *down* those seams
    (one tile of depth per loop) and drips of lava trickle through them."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    pts = tex.jittered_points(w, h, 24, 0.3, r, cell_h=8, stagger=True)
    f1, f2, idx = tex.voronoi(w, h, pts, sy=2.4)
    gap = f2 - f1
    ys, xs = np.mgrid[0:h, 0:w]
    rel_y = tex.wrapped_delta(ys, pts[idx, 1], h)
    tone = r.choice([-1, 0, 0, 1], len(pts))
    crust = 2 + tone[idx] + (rel_y < -1.5) - (rel_y > 1.5)
    hot = tex.fbm(w, h, 4, r, octaves=2) > 0.58                        # where the core shows through the joints
    seam = (gap < 1.1) & hot
    cold = (gap < 1.1) & ~hot
    crack = np.zeros((h, w), bool)
    for _ in range(5):
        x, y = int(r.integers(w)), int(r.integers(h))
        for _ in range(int(r.integers(10, 26))):
            crack[y % h, x % w] = True
            x += int(r.choice([-1, 0, 0, 1]))
            y += 1
    seam |= crack
    warm = ~seam & (np.roll(seam, 1, 1) | np.roll(seam, -1, 1) | np.roll(seam, -1, 0))
    run = tex.value_noise(w, h, 8, 2, r)
    out = []
    for f in range(frames):
        flow = np.roll(run, f * h // frames, 0)
        shade = np.where(cold, 0, np.clip(crust, 1, 3))
        shade = np.where(warm, np.maximum(shade, 2) + (flow > 0.6), shade)
        glow = np.where(flow > 0.62, 5, np.where(flow > 0.35, 4, 3))
        shade = np.where(seam, glow, shade)
        slot = np.where(seam, SLOTS.id("glow"), SLOTS.id("alt"))
        out.append(_canvas(w, h, shade, slot))
    return out


def ice_side(seed: int = 331) -> Canvas:
    """A glacier face: tall fracture planes of blue ice, each lit on its left and shadowed on its right, crossed by
    bands of compacted snow (alt slot), with trapped bubbles and a few bright specular streaks."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    pts = tex.jittered_points(w, h, 12, 0.4, r, cell_h=32)
    f1, f2, idx = tex.voronoi(w, h, pts, sx=1.0, sy=0.35)
    gap = f2 - f1
    ys, xs = np.mgrid[0:h, 0:w]
    rel = tex.wrapped_delta(xs, pts[idx, 0], w)
    tone = r.choice([-1, 0, 0, 1], len(pts))
    shade = 3 + tone[idx] + (rel < -2) - (rel > 2)
    shade = np.where(gap < 0.9, 1, shade)
    warp = tex.fbm(w, h, 4, r, octaves=2, aspect=3.0)
    bandpos = (ys + warp * 6) % h
    band = (bandpos > 10) & (bandpos < 13)
    slot = np.where(band, SLOTS.id("alt"), SLOTS.id("main"))
    shade = np.where(band, np.where(bandpos < 11, 5, 4), shade)
    for x, y in tex.poisson(w, h, 6, r, tries=300):
        if not band[y, x]:
            shade[y, x] = 5
            shade[(y + 1) % h, x] = max(1, int(shade[(y + 1) % h, x]) - 1)
    for _ in range(6):
        x, y = int(r.integers(w)), int(r.integers(h))
        for k in range(int(r.integers(4, 9))):
            if not band[(y + k) % h, x]:
                shade[(y + k) % h, x] = 5
    return _canvas(w, h, np.clip(shade, 0, 5), slot)


def basalt_side(seed: int = 341) -> Canvas:
    """Basalt columns from the side: vertical prisms of uneven width, each face lit on the left and shaded on the
    right, with horizontal cross-joints breaking them every so often."""
    r = tex.rng(seed)
    w, h = T, SIDE_H
    shade = np.full((h, w), 3)
    slot = np.full((h, w), SLOTS.id("main"))
    x = 0
    while x < w:
        cw = int(r.integers(6, 11))
        tone = int(r.choice([-1, 0, 0, 1]))
        joint = int(r.integers(h))
        for xx in range(x, min(w, x + cw)):
            k = xx - x
            sv = 3 + tone + (1 if k < 2 else 0) - (1 if k >= cw - 2 else 0)
            shade[:, xx] = sv
            shade[joint, xx] = 1
            shade[(joint + 1) % h, xx] = min(5, sv + 1)
        shade[:, min(w - 1, x + cw - 1)] = 0
        x += cw
    n = tex.fbm(w, h, 8, r, octaves=2)
    shade = np.where(n < 0.15, shade - 1, shade)
    return _canvas(w, h, np.clip(shade, 0, 5), slot)


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
        "green": _v(main="grass", alt="grass_lush"), "lush": _v(main="grass_lush", alt="grass"),
        "dry": _v(main="grass_dry", alt="grass"), "autumn": _v(main="grass_autumn", alt="grass_dry"),
        "frost": _v(main="tr/frost", alt="snow")}),
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
    TerrainTex("water", "top", water_frames, {"clear": _v(main="water"), "swamp": _v(main="water_swamp"),
                                               "bog": _v(main="tr/bog")}, fps=8),
    TerrainTex("lava", "top", lava_frames, {"molten": _v(glow="lava", alt="crust")}, fps=8, tags=["emissive"]),
    TerrainTex("bog", "top", lambda: water_frames(97, duckweed=True), {
        "duckweed": _v(main="tr/bog", accent="tr/duckweed"), "murk": _v(main="water_swamp", accent="moss")}, fps=6),
    TerrainTex("forest_floor", "top", forest_floor, {
        "leafy": _v(main="dirt_dark", moss="tr/forest_moss", accent="foliage/leaf_autumn", root="tr/root"),
        "mossy": _v(main="tr/peat", moss="tr/forest_moss", accent="foliage/leaf", root="tr/root")}),
    TerrainTex("forest_needles", "top", lambda: forest_floor(253, needles=True), {
        "pine": _v(main="dirt_dark", moss="tr/forest_moss", accent="tr/needles", root="tr/root"),
        "snowy": _v(main="tr/peat", moss="snow", accent="tr/needles", root="tr/root")}),
    TerrainTex("dunes", "top", dunes, {"gold": _v(main="tr/dune"), "pale": _v(main="sand")}),
    TerrainTex("sandstone", "top", sandstone_top, {"ochre": _v(main="tr/sandstone", alt="tr/sandstone_red"),
                                                    "red": _v(main="tr/sandstone_red", alt="tr/sandstone")}),
    TerrainTex("ash", "top", ash, {"grey": _v(main="tr/ash", alt="tr/obsidian", glow="lava")}, tags=["emissive"]),
    TerrainTex("basalt", "top", basalt, {"dark": _v(main="tr/basalt", alt="tr/ash")}),
    TerrainTex("pavers", "top", pavers, {"grey": _v(main="tr/paver", grout="mortar", moss="moss"),
                                          "dark": _v(main="tr/paver_dark", grout="mortar", moss="moss"),
                                          "warm": _v(main="stone_warm", grout="mortar", moss="moss")}),
    # sides
    TerrainTex("dirt_side", "side", dirt_side, {
        "brown": _v(main="dirt", accent="stone", alt="tr/clay", root="tr/root", moss="moss"),
        "dark": _v(main="dirt_dark", accent="stone_dark", alt="dirt", root="tr/root", moss="moss")}),
    TerrainTex("cliff_side", "side", cliff_side, {"grey": _v(main="stone", grout="stone_dark", moss="moss"),
                                                  "warm": _v(main="stone_warm", grout="dirt_dark", moss="moss"),
                                                  "dark": _v(main="stone_dark", grout="mortar", moss="moss")}),
    TerrainTex("brick_side", "side", brick_side, {"red": _v(main="brick", grout="mortar")}),
    TerrainTex("ashlar_side", "side", ashlar_side, {"grey": _v(main="stone", grout="mortar"),
                                                    "warm": _v(main="stone_warm", grout="mortar")}),
    TerrainTex("sandstone_side", "side", sandstone_side, {"desert": _v(main="sand", alt="stone_warm"),
                                                          "ochre": _v(main="tr/sandstone", alt="tr/dune"),
                                                          "red": _v(main="tr/sandstone_red", alt="tr/sandstone")}),
    TerrainTex("lava_side", "side", lava_side_frames, {"molten": _v(glow="lava", alt="tr/basalt"),
                                                       "cooling": _v(glow="tr/cinder", alt="tr/obsidian")},
               fps=8, tags=["emissive"]),
    TerrainTex("ice_side", "side", ice_side, {"glacier": _v(main="tr/glacier", alt="snow"),
                                              "clear": _v(main="ice", alt="snow")}),
    TerrainTex("basalt_side", "side", basalt_side, {"dark": _v(main="tr/basalt")}),
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


LIGHTS = {
    # Point lights for glowing textures, for the renderer to scatter over them: one light per `every` tiles (in a
    # staggered grid), lifted `lift` texels above the surface, `radius` in tiles, with a flicker curve from the
    # props manifest's lighting.flicker. Lava lights its surroundings; ash and cooling crust only glimmer.
    "lava": {"every": 2, "radius": 2.5, "lift": 4, "flicker": "pulse", "power": 0.8},
    "lava_side": {"every": 2, "radius": 1.5, "lift": 0, "flicker": "pulse", "power": 0.5},
    "ash": {"every": 4, "radius": 1.0, "lift": 2, "flicker": "pulse", "power": 0.25},
}


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {}
    for slot, ref in variant.items():
        fam, name = ref.split("/")
        out[slot] = TR_RAMPS[name] if fam == "tr" else FAMILIES[fam][name]
    return out
