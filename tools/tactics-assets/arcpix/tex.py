"""Seamless texture helpers: periodic noise, periodic Voronoi, emboss lighting and pixel clean-up.

Every field here wraps around its edges (it lives on a torus), so a texture built from them tiles with itself.
Terrain textures are *world-aligned*: one 128x128 texture covers a 4x4 block of 32 px tiles, and the game samples it
by world position, so neighbouring tiles continue each other instead of visibly repeating every tile.
"""
from __future__ import annotations

import numpy as np


def rng(seed: int) -> np.random.Generator:
    return np.random.default_rng(seed)


def _smooth(t: np.ndarray) -> np.ndarray:
    return t * t * (3 - 2 * t)


def value_noise(w: int, h: int, cells_x: int, cells_y: int, r: np.random.Generator) -> np.ndarray:
    """Smooth periodic value noise in 0..1; `cells_*` lattice cells across the texture (must divide evenly)."""
    g = r.random((cells_y, cells_x))
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    xs *= cells_x / w
    ys *= cells_y / h
    x0, y0 = np.floor(xs).astype(int), np.floor(ys).astype(int)
    fx, fy = _smooth(xs - x0), _smooth(ys - y0)
    x1, y1 = (x0 + 1) % cells_x, (y0 + 1) % cells_y
    x0, y0 = x0 % cells_x, y0 % cells_y
    top = g[y0, x0] * (1 - fx) + g[y0, x1] * fx
    bot = g[y1, x0] * (1 - fx) + g[y1, x1] * fx
    return top * (1 - fy) + bot * fy


def fbm(w: int, h: int, cells: int, r: np.random.Generator, *, octaves: int = 3, gain: float = 0.5,
        aspect: float = 1.0) -> np.ndarray:
    """Fractal periodic noise normalised to 0..1. `aspect` > 1 stretches features horizontally."""
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    c = cells
    for _ in range(octaves):
        cx = max(1, round(c / aspect)) if aspect != 1.0 else c
        cy = c
        # Lattice counts must divide the size for the noise to wrap exactly (and can't exceed it).
        cx, cy = min(cx, w), min(cy, h)
        while w % cx:
            cx += 1
        while h % cy:
            cy += 1
        out += amp * value_noise(w, h, cx, cy, r)
        total += amp
        amp *= gain
        c *= 2
    out /= total
    lo, hi = out.min(), out.max()
    return (out - lo) / (hi - lo + 1e-9)


def jittered_points(w: int, h: int, cell: int, jitter: float, r: np.random.Generator,
                    cell_h: int | None = None, stagger: bool = False) -> np.ndarray:
    """One point per grid cell, nudged randomly: even-sized Voronoi cells (flagstones, cobbles, bricks)."""
    ch = cell_h or cell
    pts = []
    for j in range(h // ch):
        off = (cell / 2) if (stagger and j % 2) else 0
        for i in range(w // cell):
            pts.append(((i + 0.5) * cell + off + r.uniform(-jitter, jitter) * cell,
                        (j + 0.5) * ch + r.uniform(-jitter, jitter) * ch))
    return np.array(pts) % [w, h]


def voronoi(w: int, h: int, pts: np.ndarray, *, sx: float = 1.0, sy: float = 1.0):
    """Periodic Voronoi. Returns (f1, f2, idx): distance to the nearest and second-nearest point, and the nearest
    point's index. `sx`/`sy` weight the axes (sy > 1 squashes cells vertically)."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    dx = np.abs(xs[None] - pts[:, 0, None, None])
    dy = np.abs(ys[None] - pts[:, 1, None, None])
    dx = np.minimum(dx, w - dx) * sx
    dy = np.minimum(dy, h - dy) * sy
    d = np.sqrt(dx * dx + dy * dy)
    order = np.argsort(d, axis=0)
    idx = order[0]
    f1 = np.take_along_axis(d, order[0:1], 0)[0]
    f2 = np.take_along_axis(d, order[1:2], 0)[0]
    return f1, f2, idx


def emboss(height: np.ndarray, light=(-1, -1)) -> np.ndarray:
    """Slope towards the light: positive on faces lit from `light` (top-left by default), negative on the far side.
    Wraps, so it keeps a texture seamless."""
    lx, ly = light
    return height - np.roll(np.roll(height, -ly, 0), -lx, 1)


def wrap_roll(a: np.ndarray, dx: int, dy: int) -> np.ndarray:
    return np.roll(np.roll(a, dy, 0), dx, 1)


def despeckle(shade: np.ndarray, passes: int = 1, *, wrap: bool = True) -> np.ndarray:
    """Removes lone pixels: a pixel whose four neighbours all agree on another value takes that value. Lone pixels
    read as noise at 1:1; clusters read as texture."""
    s = shade.copy()
    for _ in range(passes):
        if wrap:
            n = [np.roll(s, 1, 0), np.roll(s, -1, 0), np.roll(s, 1, 1), np.roll(s, -1, 1)]
        else:
            p = np.pad(s, 1, mode="edge")
            n = [p[:-2, 1:-1], p[2:, 1:-1], p[1:-1, :-2], p[1:-1, 2:]]
        same = (n[0] == n[1]) & (n[1] == n[2]) & (n[2] == n[3]) & (n[0] != s)
        s = np.where(same, n[0], s)
    return s


def stamp(arr: np.ndarray, x: int, y: int, pattern, op) -> None:
    """Applies `op(old, value)` at (x+dx, y+dy) for each (dx, dy, value) in `pattern`, wrapping around the edges."""
    h, w = arr.shape
    for dx, dy, v in pattern:
        yy, xx = (y + dy) % h, (x + dx) % w
        arr[yy, xx] = op(arr[yy, xx], v)


def poisson(w: int, h: int, min_d: float, r: np.random.Generator, tries: int = 4000) -> list[tuple[int, int]]:
    """Dart-throwing Poisson-disc points on a torus: scattered features that never clump."""
    pts: list[tuple[int, int]] = []
    for _ in range(tries):
        x, y = int(r.integers(0, w)), int(r.integers(0, h))
        ok = True
        for px, py in pts:
            dx = min(abs(x - px), w - abs(x - px))
            dy = min(abs(y - py), h - abs(y - py))
            if dx * dx + dy * dy < min_d * min_d:
                ok = False
                break
        if ok:
            pts.append((x, y))
    return pts


def checker(w: int, h: int) -> np.ndarray:
    ys, xs = np.mgrid[0:h, 0:w]
    return (xs + ys) % 2 == 0


def quantize(field: np.ndarray, steps: list[tuple[float, int]], base: int, *, dither: float = 0.0) -> np.ndarray:
    """Maps a 0..1 field to shades. `steps` are (threshold, shade) applied in order: field < t (for t < 0.5) or
    field > t (for t >= 0.5) takes `shade`. Within `dither` of a threshold the border is checkerboarded, the
    classic pixel-art soft edge, so patches fade instead of ending in a hard blob outline."""
    h, w = field.shape
    out = np.full((h, w), base)
    ck = checker(w, h)
    for t, shade in steps:
        if t < 0.5:
            hard = field < t - dither
            soft = (field >= t - dither) & (field < t + dither) & ck
        else:
            hard = field > t + dither
            soft = (field <= t + dither) & (field > t - dither) & ck
        out = np.where(hard | soft, shade, out)
    return out


def wrapped_delta(a: np.ndarray, b: np.ndarray, size: int) -> np.ndarray:
    """a - b on a ring of `size`: the shortest signed difference."""
    d = (a - b) % size
    return np.where(d > size / 2, d - size, d)
