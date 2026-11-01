"""Creature forms: silhouettes as numpy masks, lit like volumes, plus the clustered surface textures (fur strokes,
scale rows) that monsters need.

Masks keep drawing simple (union an ellipse, a tube and a polygon, then light the result once), and lighting from
the distance to the silhouette's edge gives any shape a lit rim, a shadowed underside and a flat middle, so a neck,
a wing or a boulder all read as volume. Textures are anchored to a point on the body rather than to the canvas, so
fur strokes ride with the body as it moves instead of crawling across it frame to frame.

The beasts, bosses and bestiary sets share this module; it was lifted out of parts/bosses.py unchanged so those
sets light their monsters identically.
"""
from __future__ import annotations

import math

import numpy as np

from .canvas import Canvas, SlotTable

_SCRATCH = SlotTable()


def grid(c: Canvas) -> tuple[np.ndarray, np.ndarray]:
    yy, xx = np.mgrid[0:c.h, 0:c.w]
    return yy + 0.5, xx + 0.5


def ell(c: Canvas, cx: float, cy: float, rx: float, ry: float) -> np.ndarray:
    yy, xx = grid(c)
    return ((xx - cx) / max(rx, 0.5)) ** 2 + ((yy - cy) / max(ry, 0.5)) ** 2 <= 1.0


def rot_ell(c: Canvas, cx: float, cy: float, rx: float, ry: float, ang: float) -> np.ndarray:
    """An ellipse turned by `ang` radians (clockwise on screen): a sloping back, a tilted head."""
    yy, xx = grid(c)
    ca, sa = math.cos(ang), math.sin(ang)
    u = (xx - cx) * ca + (yy - cy) * sa
    v = -(xx - cx) * sa + (yy - cy) * ca
    return (u / max(rx, 0.5)) ** 2 + (v / max(ry, 0.5)) ** 2 <= 1.0


def poly(c: Canvas, pts: list[tuple[float, float]]) -> np.ndarray:
    s = Canvas(c.w, c.h, _SCRATCH)
    s.polygon(pts, "x")
    return s.slot > 0


def tube(c: Canvas, pts: list[tuple[float, float]], radii: list[float]) -> np.ndarray:
    """A tapered tube through `pts` (one radius per point): necks, tails, limbs, tentacles of smoke."""
    yy, xx = grid(c)
    m = np.zeros((c.h, c.w), bool)
    for (x0, y0), (x1, y1), r0, r1 in zip(pts, pts[1:], radii, radii[1:]):
        n = max(2, int(math.hypot(x1 - x0, y1 - y0) * 2))
        for t in np.linspace(0, 1, n):
            x, y, r = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r0 + (r1 - r0) * t
            m |= (xx - x) ** 2 + (yy - y) ** 2 <= max(r, 0.5) ** 2
    return m


def curve(p0, p1, p2, n: int = 6) -> list[tuple[float, float]]:
    """Quadratic Bezier through p0 and p2, pulled towards p1."""
    out = []
    for i in range(n + 1):
        t = i / n
        out.append(((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
                    (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]))
    return out


def erode(m: np.ndarray) -> np.ndarray:
    e = m.copy()
    e[1:, :] &= m[:-1, :]
    e[:-1, :] &= m[1:, :]
    e[:, 1:] &= m[:, :-1]
    e[:, :-1] &= m[:, 1:]
    e[0, :] = e[-1, :] = False
    e[:, 0] = e[:, -1] = False
    return e


def dilate(m: np.ndarray) -> np.ndarray:
    d = m.copy()
    d[1:, :] |= m[:-1, :]
    d[:-1, :] |= m[1:, :]
    d[:, 1:] |= m[:, :-1]
    d[:, :-1] |= m[:, 1:]
    return d


def blur(a: np.ndarray) -> np.ndarray:
    p = np.pad(a, 1, mode="edge")
    h, w = a.shape
    return sum(p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w] for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9.0


def shift(m: np.ndarray, dx: int, dy: int) -> np.ndarray:
    out = np.zeros_like(m)
    h, w = m.shape
    ys, yd = (slice(0, h - dy), slice(dy, h)) if dy >= 0 else (slice(-dy, h), slice(0, h + dy))
    xs, xd = (slice(0, w - dx), slice(dx, w)) if dx >= 0 else (slice(-dx, w), slice(0, w + dx))
    out[yd, xd] = m[ys, xs]
    return out


def paint(c: Canvas, mask: np.ndarray, slot: str, *, base: float = 3.0, k: float = 1.8, depth: int = 5,
          light=(-0.55, -0.85), lo: int = 1, hi: int = 5, cast: bool = True, dither: bool = False) -> None:
    """Fills `mask` with `slot`, lit like a sphere from the top-left. The surface normal comes from the distance to
    the silhouette's edge, so any shape (a neck, a wing, a boulder) gets a lit rim, a shadowed underside and a flat
    middle. With `cast`, the part shades what it covers one pixel below-right, separating overlapping parts."""
    if not mask.any():
        return
    d = mask.astype(np.float32)
    m = mask.copy()
    for _ in range(depth):
        m = erode(m)
        d += m
    g = blur(blur(d))
    gy, gx = np.gradient(g)
    ln = np.hypot(gx, gy) + 1e-6
    rr = 1.0 - np.clip((d - 1) / depth, 0, 1)
    nx, ny = -gx / ln * rr, -gy / ln * rr
    nz = np.sqrt(np.clip(1 - rr * rr, 0, 1))
    dot = (nx * light[0] + ny * light[1]) * 0.9 + nz * 0.35 - 0.25
    v = base + dot * k
    sh = np.round(v)
    yy, xx = np.mgrid[0:c.h, 0:c.w]
    if dither:
        sh -= ((np.abs(v - np.round(v)) > 0.44) & ((xx + yy) % 2 == 1) & (v < np.round(v))).astype(np.float32)
    sh = np.clip(sh, lo, hi).astype(np.uint8)
    if cast:
        under = (c.slot > 0) & ~mask & (c.flags == 0) & (shift(mask, 1, 1) | shift(mask, 0, 1))
        c.shade[under] = np.maximum(c.shade[under].astype(np.int16) - 1, 0).astype(np.uint8)
    c.slot[mask] = c.slots.id(slot)
    c.shade[mask] = sh[mask]
    c.flags[mask] = 0


def flat(c: Canvas, mask: np.ndarray, slot: str, shade: int) -> None:
    c.slot[mask] = c.slots.id(slot)
    c.shade[mask] = shade
    c.flags[mask] = 0


def recolour(c: Canvas, mask: np.ndarray, slot: str, delta: int = 0) -> None:
    """Moves already-shaded pixels to another slot, keeping their light (belly scales, moss, frost)."""
    m = mask & (c.slot > 0)
    c.slot[m] = c.slots.id(slot)
    c.shade[m] = np.clip(c.shade[m].astype(np.int16) + delta, 0, 5).astype(np.uint8)


def nudge(c: Canvas, mask: np.ndarray, delta: int, *, lo: int = 1, hi: int = 5) -> None:
    """Lightens or darkens drawn pixels under `mask` in place, keeping their slot."""
    m = mask & (c.slot > 0) & (c.flags == 0)
    c.shade[m] = np.clip(c.shade[m].astype(np.int16) + delta, lo, hi).astype(np.uint8)


def rng(*seed: int) -> np.random.Generator:
    return np.random.default_rng([abs(int(s)) for s in seed])


def depth_map(mask: np.ndarray, n: int = 6) -> np.ndarray:
    d = mask.astype(np.int16)
    m = mask.copy()
    for _ in range(n):
        m = erode(m)
        d += m
    return d


def glow_fill(c: Canvas, mask: np.ndarray, slot: str, *, lo: int = 2, hi: int = 5, step: float = 1.5,
              seed: int | None = None, flicker: float = 0.0) -> None:
    """Fills a self-lit body: shade rises with depth from the edge, so the core burns brightest (fire, magic).
    `flicker` darkens or brightens random pixel pairs so consecutive frames shimmer."""
    d = depth_map(mask)
    sh = np.clip(lo + (d - 1) / step, lo, hi).astype(np.int16)
    if seed is not None and flicker:
        r = rng(seed)
        noise = r.random(mask.shape)
        sh = np.where(noise < flicker, sh - 1, np.where(noise > 1 - flicker * 0.6, sh + 1, sh))
    c.slot[mask] = c.slots.id(slot)
    c.shade[mask] = np.clip(sh, lo - 1, hi)[mask].astype(np.uint8)
    c.flags[mask] = 0


# ------------------------------------------------------------------------------------------------- textures
def _hash(ix: np.ndarray, iy: np.ndarray, seed: int) -> np.ndarray:
    """A cheap integer hash per lattice cell, stable for a given seed (textures must not change between builds)."""
    h = (ix.astype(np.int64) * 374761393 + iy.astype(np.int64) * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return (h ^ (h >> 16)) & 0xFFFF


def fur(c: Canvas, slot: str | tuple[str, ...], ox: float, oy: float, *, seed: int = 0, cell: tuple[int, int] = (4, 3),
        length: int = 3, slope: tuple[int, int] = (-1, 1), fill: float = 0.55, mask: np.ndarray | None = None,
        bounce: bool = False) -> None:
    """Fur as strokes: short diagonal runs of one shade darker in the light (and, with `bounce`, one lighter on
    the shadow side, where the coat catches bounce light), laid on a jittered lattice anchored at the body point
    (ox, oy). Every stroke is at least two pixels and lies wholly inside the coat, so the texture reads as combed
    hair, never as salt-and-pepper grime, and it travels with the body from frame to frame."""
    slots = (slot,) if isinstance(slot, str) else slot
    ids = [c.slots.id(s) for s in slots]
    coat = np.isin(c.slot, ids) & (c.flags == 0)
    if mask is not None:
        coat &= mask
    if not coat.any():
        return
    cw, ch = cell
    bx, by = int(round(ox)), int(round(oy))
    ys, xs = np.nonzero(coat)
    lx, ly = xs - bx, ys - by
    ix, iy = np.floor_divide(lx + (np.floor_divide(ly, ch) % 2) * (cw // 2), cw), np.floor_divide(ly, ch)
    hv = _hash(ix, iy, seed)
    jx = hv % cw
    jy = (hv >> 4) % ch
    start = ((lx + (np.floor_divide(ly, ch) % 2) * (cw // 2)) % cw == jx) & (ly % ch == jy) & \
            (((hv >> 8) & 0xFF) < fill * 256)
    sx, sy = slope
    shade = c.shade.astype(np.int16)
    out = shade.copy()
    for x, y in zip(xs[start], ys[start]):
        if sx and sy:                                     # a shallow diagonal: two across for each step down
            pts = [(x + sx * i, y + sy * (i // 2)) for i in range(length)]
        else:
            pts = [(x + sx * i, y + sy * i) for i in range(length)]
        if not all(0 <= px < c.w and 0 <= py < c.h and coat[py, px] for px, py in pts):
            continue
        s0 = int(shade[y, x])
        if s0 < 3 and not bounce:
            continue
        d = -1 if s0 >= 3 else 1
        for px, py in pts:
            out[py, px] = np.clip(int(shade[py, px]) + d, 1, 5)
    c.shade = out.astype(np.uint8)


def tufts(c: Canvas, mask: np.ndarray, slot: str, ox: float, *, every: int = 3, edge: str = "bottom",
          shade: int | None = None, phase: int = 0) -> None:
    """Ragged fur along one edge of a silhouette: single-pixel tufts hanging under a belly or bristling along a back
    line, spaced from the body point `ox` so they ride with it. They break the smooth blob edge that makes a furred
    animal look like a plastic toy."""
    dy = 1 if edge == "bottom" else -1
    rim = mask & ~shift(mask, 0, -dy)
    ys, xs = np.nonzero(rim)
    bx = int(round(ox))
    for x, y in zip(xs, ys):
        if (x - bx + phase) % every:
            continue
        ty = y + dy
        if 0 <= ty < c.h and c.slot[ty, x] == 0:
            c.px(x, ty, slot, shade if shade is not None else max(1, int(c.shade[y, x]) - 1))


def scale_rows(c: Canvas, slot: str, ox: float, oy: float, *, step: int = 4, row: int = 3,
               mask: np.ndarray | None = None) -> None:
    """Scales as staggered rows of small scallops (a dark lower lip under a lit cap), anchored at the body point
    (ox, oy). A regular pattern reads as scales where random flecks read as dirt."""
    sid = c.slots.id(slot)
    yy, xx = np.mgrid[0:c.h, 0:c.w]
    ly, lx = yy - int(round(oy)), xx - int(round(ox))
    r = np.floor_divide(ly, row)
    u = (lx + (r % 2) * (step // 2)) % step
    v = ly % row
    m = (c.slot == sid) & (c.flags == 0)
    if mask is not None:
        m &= mask
    lip = m & (v == row - 1) & (u != 0) & (c.shade >= 2)
    cap = m & (v == 0) & (u == 1) & (c.shade >= 3) & (c.shade <= 4)
    c.shade[lip] -= 1
    c.shade[cap] += 1
