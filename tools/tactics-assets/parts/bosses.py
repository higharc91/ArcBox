"""Bosses, big monsters and subclass companions: the units too big, too strange or too friendly for beasts.py.

They follow the beasts' contract so the battle code treats every unit alike: the same animation names in the same
order (idle, walk, attack, hurt, down) plus a `special`, the same two facings (front, back), anchors at the ground
contact pixel, palette variants baked into sheets, and one manifest. What changes is scale: each monster has its own
frame size, a tile footprint (the dragon covers 2x2) and more frames per animation, because a boss is on screen for
a whole fight and a two-frame idle looks dead at that size.

Bodies are drawn as silhouettes (numpy masks) and shaded by `paint`, which lights any shape like a sphere from the
top-left using its distance to the edge. Parts are painted back to front and each casts a 1 px shadow onto what is
behind it, so overlapping limbs separate without inner outlines.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from typing import Callable

import numpy as np
from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, stack, strip
from . import beasts as B
from .props import leaf_texture

SLOTS = SlotTable()
for _s in ("hide", "hide2", "wing", "horn", "eyes", "mouth", "glow", "fx", "accent", "wood", "metal", "cloth", "stone",
           "gold", "hair"):
    SLOTS.id(_s)
_SCRATCH = SlotTable()

ANIMS: dict[str, tuple[float, bool, int]] = {   # name -> (fps, loop, frames)
    "idle": (6.0, True, 4), "walk": (8.0, True, 4), "attack": (10.0, False, 5), "special": (9.0, False, 6),
    "hurt": (8.0, False, 2), "down": (4.0, False, 2),
}
ORDER = ["idle", "walk", "attack", "special", "hurt", "down"]
FX_SLOTS = {"fx"}                       # light and air: never outlined, so breath and shockwaves read as glow


@dataclass
class Pose(B.Pose):
    """beasts.Pose plus what bigger monsters animate. Every monster reads the same table, so a whole encounter plays
    in step; each drawing decides what a value means for its body (a club, a fist, a wing, a tongue)."""
    anim: str = "idle"
    k: int = 0                          # frame within the animation (seeds flicker)
    phase: float = 0                    # 0..1 around a looping animation
    wing: float = 0                     # -1 down-stroke .. 1 raised
    arm: float = 0                      # -1 wound up .. 1 struck through
    tilt: float = 0                     # lean: + forward, - back
    charge: float = 0                   # special build-up 0..1
    burst: float = 0                    # special release 0..1
    fall: float = 0                     # down progress 0..1


def _poses() -> dict[str, list[Pose]]:
    walk_legs = [((3, -3, -3, 3), (0, 2, 2, 0)), ((1, -1, -1, 1), (0, 0, 0, 0)),
                 ((-3, 3, 3, -3), (2, 0, 0, 2)), ((-1, 1, 1, -1), (0, 0, 0, 0))]
    out: dict[str, list[Pose]] = {
        "idle": [Pose(anim="idle", k=k, phase=k / 4, bob=(0, 0.5, 1, 0.5)[k], wing=(0, 0.3, 0.5, 0.3)[k],
                      head=(0, (0, 0, 1, 0)[k])) for k in range(4)],
        "walk": [Pose(anim="walk", k=k, phase=k / 4, bob=(1, 0, 1, 0)[k], legs=walk_legs[k][0], lift=walk_legs[k][1],
                      wing=(1, 0, -1, 0)[k], tilt=(0.2, 0, 0.2, 0)[k]) for k in range(4)],
        "attack": [
            Pose(anim="attack", k=0, lunge=-2, tilt=-0.5, arm=-0.5, head=(-2, -1), wing=0.3),
            Pose(anim="attack", k=1, lunge=-4, tilt=-1, arm=-1, bob=-1, head=(-4, -3), wing=0.8),
            Pose(anim="attack", k=2, lunge=5, tilt=1, arm=1, jaw=True, head=(5, 3), wing=-0.5),
            Pose(anim="attack", k=3, lunge=4, tilt=0.8, arm=1, jaw=True, head=(4, 3), wing=-0.7, burst=1),
            Pose(anim="attack", k=4, lunge=1, tilt=0.2, arm=0.4, head=(1, 1))],
        "special": [
            Pose(anim="special", k=0, charge=0.35, lunge=-1, head=(-2, -2), wing=0.5, tilt=-0.4),
            Pose(anim="special", k=1, charge=0.7, lunge=-2, head=(-4, -5), wing=1, tilt=-0.8, bob=-1),
            Pose(anim="special", k=2, charge=1, lunge=-3, head=(-5, -6), wing=1, tilt=-1, bob=-1),
            Pose(anim="special", k=3, burst=0.6, lunge=3, head=(4, 3), jaw=True, wing=-0.6, tilt=0.8),
            Pose(anim="special", k=4, burst=1, lunge=3, head=(5, 4), jaw=True, wing=-1, tilt=1),
            Pose(anim="special", k=5, burst=0.35, lunge=1, head=(2, 2), jaw=True, tilt=0.3)],
        "hurt": [Pose(anim="hurt", k=0, lunge=-3, bob=1, head=(-2, -1), hurt=True, tilt=-1, wing=0.6),
                 Pose(anim="hurt", k=1, lunge=-1, head=(-1, 0), hurt=True, tilt=-0.4, wing=0.2)],
        "down": [Pose(anim="down", k=0, fall=0.5, bob=2, head=(1, 3), hurt=True, wing=-0.3),
                 Pose(anim="down", k=1, fall=1, down=True, bob=4, head=(2, 6), wing=-0.6)],
    }
    for a in ORDER:
        assert len(out[a]) == ANIMS[a][2], a
    return out


POSES = _poses()


def frame_poses() -> list[tuple[str, int, Pose]]:
    return [(a, i, p) for a in ORDER for i, p in enumerate(POSES[a])]


# ------------------------------------------------------------------------------------------------ shape helpers
def _grid(c: Canvas) -> tuple[np.ndarray, np.ndarray]:
    yy, xx = np.mgrid[0:c.h, 0:c.w]
    return yy + 0.5, xx + 0.5


def ell(c: Canvas, cx: float, cy: float, rx: float, ry: float) -> np.ndarray:
    yy, xx = _grid(c)
    return ((xx - cx) / max(rx, 0.5)) ** 2 + ((yy - cy) / max(ry, 0.5)) ** 2 <= 1.0


def poly(c: Canvas, pts: list[tuple[float, float]]) -> np.ndarray:
    s = Canvas(c.w, c.h, _SCRATCH)
    s.polygon(pts, "x")
    return s.slot > 0


def tube(c: Canvas, pts: list[tuple[float, float]], radii: list[float]) -> np.ndarray:
    """A tapered tube through `pts` (one radius per point): necks, tails, limbs, tentacles of smoke."""
    yy, xx = _grid(c)
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


def _erode(m: np.ndarray) -> np.ndarray:
    e = m.copy()
    e[1:, :] &= m[:-1, :]
    e[:-1, :] &= m[1:, :]
    e[:, 1:] &= m[:, :-1]
    e[:, :-1] &= m[:, 1:]
    e[0, :] = e[-1, :] = False
    e[:, 0] = e[:, -1] = False
    return e


def _dilate(m: np.ndarray) -> np.ndarray:
    d = m.copy()
    d[1:, :] |= m[:-1, :]
    d[:-1, :] |= m[1:, :]
    d[:, 1:] |= m[:, :-1]
    d[:, :-1] |= m[:, 1:]
    return d


def _blur(a: np.ndarray) -> np.ndarray:
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
        m = _erode(m)
        d += m
    g = _blur(_blur(d))
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


def _rng(*seed: int) -> np.random.Generator:
    return np.random.default_rng([abs(int(s)) for s in seed])


def flame(c: Canvas, x0: float, y0: float, ang: float, length: float, width: float, seed: int, *,
          slot: str = "glow") -> None:
    """A cone of breath from (x0, y0) along `ang`: a ragged outer tongue, a brighter band and a white-hot core."""
    yy, xx = _grid(c)
    dx, dy = math.cos(ang), math.sin(ang)
    u = (xx - x0) * dx + (yy - y0) * dy
    v = -(xx - x0) * dy + (yy - y0) * dx
    r = _rng(seed)
    wob = np.interp(u, np.arange(0, 120, 3), r.uniform(-1.6, 1.6, 40))
    t = np.clip(u / max(length, 1), 0, 1)
    half = 1.2 + width * t ** 0.8 + wob * t
    inside = (u >= 0) & (u <= length + wob * 2)
    for frac, shade in ((1.0, 2), (0.72, 3), (0.45, 4), (0.2, 5)):
        flat(c, inside & (np.abs(v) <= half * frac + 0.3), slot, shade)
    for _ in range(int(length / 3)):                                  # sparks thrown past the edge
        uu = r.uniform(length * 0.3, length + 4)
        vv = r.uniform(-1, 1) * (width + 3) * uu / max(length, 1)
        px, py = x0 + dx * uu - dy * vv, y0 + dy * uu + dx * vv
        c.px(px, py, slot, int(r.integers(3, 6)))


# --------------------------------------------------------------------------------------------------- dragon
def scales(c: Canvas, slot: str, seed: int = 0, *, step: int = 6) -> None:
    """Staggered rows of small scale marks: a regular pattern reads as scales where random flecks read as fur."""
    sid = c.slots.id(slot)
    yy, xx = np.mgrid[0:c.h, 0:c.w]
    row = (yy + seed) // 3
    mark = ((yy + seed) % 3 == 0) & ((xx + (row % 2) * (step // 2)) % step == 0)
    m = (c.slot == sid) & (c.flags == 0) & (mark | shift(mark, 1, 0)) & (c.shade >= 3)
    c.shade[m] -= 1


def _wing(c: Canvas, sx: float, sy: float, lift: float, side: int, base: float, *, span: float = 1.0,
          elev: float = 62, root: tuple[float, float] | None = None) -> None:
    """A bat wing hinged at the shoulder (sx, sy). `lift` raises the arm (-1 down-stroke .. 1 up), `side` -1 sweeps
    it to screen-left, +1 to screen-right. The membrane hangs between three fingers; bones are drawn on top."""
    a = math.radians(elev + lift * 32)                                  # arm elevation above horizontal
    L = 30 * span
    ex, ey = sx + side * L * 0.42 * math.cos(a - 0.5), sy - L * 0.42 * math.sin(a - 0.5) - 3
    wx, wy = sx + side * L * math.cos(a), sy - L * math.sin(a)
    tips = []
    for ang, ln in ((a - 1.75, 24), (a - 2.35, 22), (a - 2.95, 18)):
        ln *= span
        tips.append((wx + side * ln * math.cos(ang), wy - ln * math.sin(ang)))
    rt = root or (sx - side * 2, sy + 8)
    pts = [(sx, sy), (ex, ey), (wx, wy)]
    prev = (wx, wy)
    for i, t in enumerate(tips):                                        # scalloped trailing edge
        if i:
            pts.append(((prev[0] + t[0]) / 2 + side * 2.5, (prev[1] + t[1]) / 2 + 2.5))
        pts.append(t)
        prev = t
    pts.append(((tips[-1][0] + rt[0]) / 2 + side * 2, (tips[-1][1] + rt[1]) / 2 + 3))
    pts.append(rt)
    paint(c, poly(c, pts), "wing", base=base, k=1.0, depth=3)
    bone = 2 if base >= 2.8 else 1
    c.thick_line(sx, sy, ex, ey, 3, "hide", bone)
    c.thick_line(ex, ey, wx, wy, 2, "hide", bone)
    for t in tips:
        c.line(wx, wy, *t, "hide", bone - 1)
    c.polygon([(wx - 1, wy), (wx + 1, wy), (wx + side, wy - 3)], "horn", 4)   # wrist claw


def _horn(c: Canvas, pts, radii, shade_base: float = 3.0) -> None:
    paint(c, tube(c, pts, radii), "horn", base=shade_base, k=1.3, depth=2)


def _spikes(c: Canvas, pts: list[tuple[float, float]], size: float, *, every: int = 2, slot: str = "wing",
            shade: int = 2, back: int = 1) -> None:
    """Dorsal spines along a ridge, raked back (towards -x for back=1)."""
    for i, (x, y) in enumerate(pts):
        if i % every:
            continue
        c.polygon([(x - size * 0.7, y + 1.5), (x + size * 0.7, y + 1.5), (x - back * size * 0.5, y - size)],
                  slot, shade)
        c.px(x - back * size * 0.3, y - size + 1, slot, shade + 2)
        c.px(x - back * size * 0.2 - 1, y - size + 2, slot, shade + 1)


def _top_edge(m: np.ndarray, x0: int, x1: int) -> list[tuple[float, float]]:
    out = []
    for x in range(max(0, x0), min(m.shape[1], x1)):
        col = np.nonzero(m[:, x])[0]
        if len(col):
            out.append((x + 0.5, float(col[0])))
    return out


def _plates(c: Canvas, m: np.ndarray, axis: str = "x", every: int = 3) -> None:
    """Belly plates: the hide2 band split into transverse plates by darker seams."""
    yy, xx = np.mgrid[0:c.h, 0:c.w]
    seam = m & (((xx if axis == "x" else yy) % every) == 0)
    c.shade[seam] = np.maximum(c.shade[seam].astype(np.int16) - 1, 1).astype(np.uint8)


def dragon(direction: str, p: Pose) -> Canvas:
    """The dragon: a horned head on a long S-neck, a heavy low body on four thick legs, a tail that sweeps the
    ground and wings that fold at rest, beat when it walks and rear up to breathe. The back facing is drawn from
    behind (wings spread either side, tail towards the camera) rather than mirrored: at boss size the mirror reads
    wrong."""
    if direction == "back":
        return _dragon_back(p)
    c = Canvas(96, 96, SLOTS)
    gy = 90
    drop = p.fall * 12
    bx, by = 37 + p.lunge * 0.6, 65 + p.bob * 1.5 + drop
    tilt = p.tilt
    lean = tilt * 2
    wl = p.wing if not p.fall else -0.4 - p.fall * 0.8
    # ---------------------------------------------------------------- far wing and far legs
    _wing(c, bx + 9, by - 12 - lean, wl + 0.2, -1, 2.0, span=1.15, elev=70)
    for (hx0, hy0, sw, li) in ((bx + 15, by + 1, p.legs[2], p.lift[2]), (bx - 11, by + 1, p.legs[3], p.lift[3])):
        fx, fy = hx0 + 4 + sw, gy - 3 - li
        if p.fall:
            fx, fy = hx0 + 9 * p.fall + 4, min(gy - 3, by + 8)
        paint(c, tube(c, [(hx0, hy0), ((hx0 + fx) / 2 + 2, (hy0 + fy) / 2), (fx, fy)], [5.5, 3.8, 3]), "hide",
              base=2.0, k=0.8, depth=2)
        c.rect(fx - 2, fy + 1, fx + 4, fy + 2, "hide", 1)
    # ---------------------------------------------------------------- tail
    sway = math.sin(p.phase * math.tau) * 2 if p.anim in ("idle", "walk") else -p.lunge * 0.4
    tail = [(bx - 14, by - 3), (bx - 24, by + 1), (bx - 29, by + 10 + p.fall * 2),
            (bx - 31 + sway * 0.4, gy - 6), (bx - 34 + sway * 0.8, gy - 3), (bx - 39 + sway * 1.3, gy - 4 + sway * 0.5)]
    tail = [(x, min(y, gy - 2)) for x, y in tail]
    tm = tube(c, tail, [9, 7, 5.4, 4, 2.8, 1.5])
    paint(c, tm, "hide", base=2.8, depth=4)
    tx, ty = tail[-1]
    paint(c, poly(c, [(tx + 3, ty + 1), (tx - 3, ty - 4), (tx - 8, ty), (tx - 3, ty + 3)]), "wing", depth=2)
    # ---------------------------------------------------------------- body
    body = ell(c, bx, by, 19, 11) | ell(c, bx + 12, by - 2 - lean, 11, 12) | ell(c, bx - 10, by - 1, 11, 10)
    paint(c, body, "hide", base=2.8, depth=7)
    belly = body & ~shift(body, 0, -5) & ~shift(body, -3, -4)
    recolour(c, belly, "hide2", 1)
    _plates(c, belly)
    _spikes(c, _top_edge(body | tm, int(bx - 30), int(bx + 6))[::3], 3, every=1)
    # ---------------------------------------------------------------- neck
    hx = bx + 37 + p.head[0] * 0.5 + tilt
    hy = by - 30 + p.head[1] * 1.6 - tilt * 1.5
    breath = p.anim == "special" and p.burst > 0
    if breath:                                          # the head drops and pours fire on the ground ahead
        hx, hy = bx + 30 - p.burst * 2, by - 21 + p.burst * 3
    if p.fall:
        hx, hy = hx + 4 * p.fall, hy + (gy - 8 - hy) * p.fall
    rise = 1 - p.fall * 0.7
    neck = [(bx + 13, by - 5 - lean), (bx + 24 + tilt, by - 10 * rise - lean), (bx + 29 + tilt * 2, by - 19 * rise),
            (hx - 9, hy + 5), (hx - 4, hy + 1)]
    nm = tube(c, neck, [8.5, 6.4, 5.4, 4.8, 4.6])
    paint(c, nm, "hide", base=2.9, depth=5)
    throat = nm & ~shift(nm, -3, -2) & ~body
    recolour(c, throat, "hide2", 1)
    _plates(c, throat, "y")
    _spikes(c, _top_edge(nm & ~body, int(bx + 6), int(hx - 8))[::3], 2.6, every=1)
    # ---------------------------------------------------------------- near legs
    for (hx0, hy0, sw, li, hind) in ((bx - 11, by + 2, p.legs[1], p.lift[1], True),
                                     (bx + 14, by + 3, p.legs[0], p.lift[0], False)):
        fx, fy = hx0 + sw + 2, gy - 2 - li
        if p.fall:
            fx, fy = hx0 + 10 * p.fall + 3, min(gy - 2, by + 9)
        if hind:
            paint(c, ell(c, hx0 + 1, hy0, 8, 9), "hide", base=2.9, depth=4)
            knee = (hx0 + 5 + sw * 0.3, hy0 + 8)
            paint(c, tube(c, [(hx0 + 2, hy0 + 3), knee, (fx - 1, fy - 3), (fx, fy)], [6, 4.4, 3.4, 3.4]), "hide",
                  depth=3)
        else:
            elbow = (hx0 - 1 + sw * 0.3, (hy0 + fy) / 2 + 1)
            paint(c, tube(c, [(hx0, hy0 - 3), elbow, (fx, fy)], [7, 4.6, 3.6]), "hide", depth=3)
        c.rect(fx - 3, fy + 1, fx + 4, fy + 2, "hide", 2)
        for cx_ in (fx, fx + 3, fx + 6):                                  # claws
            c.px(cx_ - 1, fy + 2, "horn", 4)
    scales(c, "hide", p.k)
    # ---------------------------------------------------------------- near wing, head
    _wing(c, bx + 4, by - 11 - lean, wl, -1, 3.4, span=1.15, elev=66)
    _dragon_head(c, hx, hy, p)
    # ---------------------------------------------------------------- breath
    if p.charge:
        r = _rng(3, p.k)
        glow = (_dilate(throat) & nm | belly & shift(ell(c, bx + 14, by, 9, 9), 0, 0)) & (r.random((96, 96)) <
                                                                                       0.25 + p.charge * 0.6)
        flat(c, glow, "glow", 3 + int(p.charge * 2))
        for i in range(int(1 + p.charge * 3)):                         # smoke and embers curling from the nostril
            c.px(hx + 16 + i, hy - 3 - i * 2, "glow", 5 - i)
    if p.anim == "idle":                                # smoke curling from the nostrils as it breathes
        for i in range(2):
            t = (p.k + i * 2) % 4
            paint(c, ell(c, hx + 16 + t * 0.8 + i, hy - 3 - t * 3, 1.2 + t * 0.4, 1 + t * 0.3), "hair",
                  base=3.6 - t * 0.4, k=1.0, depth=1, cast=False)
    if breath:
        flame(c, hx + 13, hy + 5, math.radians(72), 12 + 22 * p.burst, 2 + 5 * p.burst, 7 + p.k)
        r = _rng(9, p.k)
        pool = ell(c, hx + 20, gy - 1, 4 + 9 * p.burst, 1.5 + 2 * p.burst)
        flat(c, pool, "glow", 3)
        flat(c, pool & ell(c, hx + 19, gy - 1, 2 + 6 * p.burst, 1 + p.burst), "glow", 5)
        for i in range(int(3 + 6 * p.burst)):                          # tongues licking up from the splash
            x = hx + 14 + r.uniform(0, 16 * p.burst + 4)
            h = r.uniform(2, 4 + 5 * p.burst)
            c.line(x, gy - 2, x + r.uniform(-1, 1), gy - 2 - h, "glow", int(r.integers(3, 6)))
    return c


def _dragon_head(c: Canvas, hx: float, hy: float, p: Pose) -> None:
    """Profile head facing +x: a broad skull, a long snout with a jaw that drops open, brow, horns and jaw spikes."""
    _horn(c, curve((hx + 1, hy - 5), (hx - 3, hy - 13), (hx - 9, hy - 16), 5), [2, 1.8, 1.5, 1.2, 0.9, 0.6], 2.2)
    skull = ell(c, hx, hy, 7, 6)
    snout = poly(c, [(hx + 1, hy - 5.5), (hx + 13, hy - 3), (hx + 17, hy - 1), (hx + 17, hy + 2), (hx + 1, hy + 3)])
    jo = 6 if p.jaw else 0
    jaw = poly(c, [(hx - 3, hy + 2), (hx + 15, hy + 2 + jo), (hx + 14, hy + 5 + jo), (hx - 2, hy + 7)])
    if p.jaw:
        flat(c, poly(c, [(hx + 1, hy + 2), (hx + 17, hy + 2), (hx + 15, hy + 3 + jo)]), "mouth", 1)
        paint(c, jaw, "hide", base=2.5, depth=2)
        for t in range(4, 16, 3):
            c.px(hx + t, hy + 3, "horn", 5)
            c.px(hx + t - 1, hy + 1 + jo * t / 15, "horn", 4)
        c.line(hx + 4, hy + 3 + jo * 0.3, hx + 12, hy + 2 + jo * 0.7, "mouth", 3)    # tongue
    paint(c, skull | snout | (jaw if not p.jaw else np.zeros_like(jaw)), "hide", base=2.9, depth=3)
    if not p.jaw:
        c.line(hx + 2, hy + 3, hx + 15, hy + 2, "hide", 1)                 # lip line
        c.px(hx + 10, hy + 4, "horn", 5)                                    # fang over the lip
        c.px(hx + 4, hy + 4, "horn", 4)
    c.px(hx + 15, hy - 1, "mouth", 0)                                       # nostril
    c.line(hx - 1, hy - 5, hx + 6, hy - 4, "hide", 4)                       # brow ridge
    c.line(hx + 1, hy - 4, hx + 5, hy - 3, "hide", 1)
    if p.down:
        c.line(hx + 2, hy - 1, hx + 4, hy - 1, "eyes", 0)
    elif p.hurt:
        c.line(hx + 2, hy - 2, hx + 4, hy - 1, "eyes", 1)
    else:
        c.rect(hx + 2, hy - 2, hx + 4, hy - 2, "eyes", 5)
        c.px(hx + 3, hy - 2, "eyes", 1)                                     # slit pupil
        c.px(hx + 4, hy - 1, "eyes", 3)
    _horn(c, curve((hx - 2, hy - 4), (hx - 11, hy - 8), (hx - 18, hy - 4), 6), [2.6, 2.3, 1.9, 1.5, 1.1, 0.8, 0.5],
          2.7)
    c.polygon([(hx - 5, hy + 3), (hx - 12, hy + 6), (hx - 4, hy + 7)], "wing", 3)     # jaw frills
    c.polygon([(hx - 6, hy), (hx - 12, hy + 1), (hx - 6, hy + 3)], "wing", 2)


def _dragon_back(p: Pose) -> Canvas:
    """The dragon from behind, facing up-left: the far wing spreads to the upper-right, the neck rises to a head seen
    from behind (horns, the back of the jaw), the near wing sweeps out to the left, and the haunches and tail are
    nearest the camera."""
    c = Canvas(96, 96, SLOTS)
    gy = 90
    drop = p.fall * 11
    bx, by = 52 - p.lunge * 0.6, 68 + p.bob * 1.5 + drop
    lean = p.tilt * 2
    wl = p.wing if not p.fall else -0.4 - p.fall * 0.8
    hx = bx - 31 - p.head[0] - p.tilt * 2
    hy = by - 37 + p.head[1] - p.tilt * 1.5
    if p.fall:
        hx, hy = hx - 6 * p.fall, hy + (gy - 14 - hy) * p.fall
    # far forelegs
    for fx0 in (bx - 9, bx + 1):
        paint(c, tube(c, [(fx0, by - 4), (fx0 - 1, gy - 6 - drop * 0.3)], [4.5, 3]), "hide", base=1.8, k=0.8,
              depth=2)
    # head (seen from behind) and neck
    if p.burst and p.anim == "special":
        flame(c, hx - 9, hy - 1, math.radians(205), 8 + 12 * p.burst, 2 + 5 * p.burst, 17 + p.k)
    head = ell(c, hx, hy, 6, 5) | poly(c, [(hx - 2, hy - 4), (hx - 10, hy - 4), (hx - 11, hy - 1), (hx - 2, hy + 3)])
    if p.jaw:
        head |= poly(c, [(hx - 2, hy + 2), (hx - 10, hy + 3), (hx - 9, hy + 6), (hx - 1, hy + 5)])
    paint(c, head, "hide", base=2.6, depth=3)
    rise = 1 - p.fall * 0.6
    neck = [(bx - 16, by - 8 - lean), (bx - 23, by - 15 * rise), (bx - 27, by - 24 * rise), (hx + 3, hy + 4)]
    nm = tube(c, neck, [8, 6.4, 5.4, 4.8])
    paint(c, nm, "hide", base=2.9, depth=4)
    for s in (-1, 1):                                                       # horns, seen from behind
        _horn(c, curve((hx + 3 * s, hy - 3), (hx + 8 * s, hy - 8), (hx + 7 * s + 3, hy - 14), 5),
              [2.4, 2.1, 1.7, 1.3, 1, 0.6])
    if p.charge:
        c.px(hx - 11, hy - 3, "glow", 5)
        for i in range(int(1 + p.charge * 3)):
            c.px(hx - 12 - i, hy - 5 - i * 2, "glow", 5 - i)
    # body, seen from above and behind: shoulders away, haunches near
    body = ell(c, bx + 3, by + 1, 15, 11) | ell(c, bx - 12, by - 5 - lean, 14, 9)
    paint(c, body, "hide", base=2.9, depth=7)
    spine = [(hx + 3, hy + 4)] + neck[::-1][1:] + curve((bx - 10, by - 14 - lean), (bx, by - 12), (bx + 9, by), 6)
    _spikes(c, [(x - 1, y - 1) for x, y in spine[1:]], 2.6, every=1, back=-1, shade=3)
    # hind legs: haunches at either side of the rump
    for s, sw, li in ((-1, p.legs[1], p.lift[1]), (1, p.legs[3], p.lift[3])):
        hx0 = bx + s * 12
        fx, fy = hx0 + s * 1 + sw * 0.5, gy - 2 - li
        if p.fall:
            fx, fy = hx0 + s * 7 * p.fall, min(gy - 2, by + 10)
        paint(c, ell(c, hx0, by + 2, 7, 9), "hide", base=2.9, depth=4)
        paint(c, tube(c, [(hx0, by + 6), (hx0 + s * 1.5, by + 12), (fx, fy)], [5.5, 4.2, 3.4]), "hide", depth=3)
        c.rect(fx - 3, fy + 1, fx + 3, fy + 2, "hide", 2)
        for cx_ in (fx - 2, fx, fx + 2):
            c.px(cx_, fy + 3, "horn", 4)
    # tail, curling towards the camera
    sway = math.sin(p.phase * math.tau) * 2 if p.anim in ("idle", "walk") else p.lunge * 0.4
    tail = [(bx + 2, by + 6), (bx + 6, by + 15), (bx + 16, gy - 3), (bx + 27 + sway, gy - 4),
            (bx + 36 + sway, gy - 7 + sway * 0.4)]
    tail = [(x, min(y, gy - 2)) for x, y in tail]
    paint(c, tube(c, tail, [7.5, 6, 4.6, 3.2, 1.6]), "hide", base=2.9, depth=4)
    tx, ty = tail[-1]
    paint(c, poly(c, [(tx - 2, ty + 1), (tx + 4, ty - 5), (tx + 7, ty), (tx + 2, ty + 3)]), "wing", depth=2)
    _spikes(c, [(x, y - r + 1.5) for (x, y), r in zip(tail[1:4], (6, 4.6, 3.2))], 2.2, every=1, back=-1, shade=3)
    scales(c, "hide", p.k)
    # both wings rise from the shoulders, over the neck's root: from behind they frame the whole body
    _wing(c, bx - 6, by - 13 - lean, wl + 0.1, 1, 2.7, span=1.15, elev=48, root=(bx + 2, by - 6))
    _wing(c, bx - 14, by - 11 - lean, wl, -1, 3.3, span=1.15, elev=44, root=(bx - 8, by - 4))
    return c


# ----------------------------------------------------------------------------------------------------- troll
class _Mirror:
    """Maps drawing coordinates for one facing. The back facing is the front's layout mirrored about the anchor,
    but drawn (not flipped) so the light still comes from the top-left."""

    def __init__(self, cx: float, back: bool):
        self.cx, self.s = cx, (-1 if back else 1)

    def x(self, x: float) -> float:
        return self.cx + self.s * (x - self.cx)

    def p(self, x: float, y: float) -> tuple[float, float]:
        return self.x(x), y

    def pts(self, pts):
        return [self.p(x, y) for x, y in pts]


def _club_pose(p: Pose) -> tuple[tuple[float, float], float]:
    """Where the club hand is (relative to the right shoulder) and the club's angle (0 = hanging straight down,
    + swings it forward / clockwise), per frame."""
    if p.anim == "attack":
        return [((-2, -12), -2.2), ((-5, -17), -2.0), ((8, 4), 0.9), ((7, 10), 1.25), ((4, 12), 0.5)][p.k]
    if p.anim == "special":
        return [((-9, -16), -2.0), ((-12, -19), -1.8), ((-13, -20), -1.75), ((-12, 11), 0.05), ((-12, 13), 0.0),
                ((-10, 12), 0.3)][p.k]
    if p.anim == "hurt":
        return ((2, 12), 0.15)
    if p.anim == "walk":
        return ((3 - p.legs[0], 19), 0.35 - p.legs[0] * 0.05)
    return ((4, 19 - p.bob * 1.5), 0.38 + math.sin(p.phase * math.tau) * 0.14)


def troll(direction: str, p: Pose) -> Canvas:
    """A hunched troll / ogre: a small head slung forward under a hump, a pot belly, long arms with the knuckles at
    the knees and a studded club. The special is a two-handed ground slam that cracks the floor."""
    c = Canvas(64, 64, SLOTS)
    back = direction == "back"
    M = _Mirror(32, back)
    gy = 60
    if p.anim == "down":
        return _troll_down(c, p, M, back)
    sway = math.sin(p.phase * math.tau) * 1.5 if p.anim == "walk" else 0
    slam = p.anim == "special" and p.burst > 0
    crouch = (3 + p.burst * 2) if slam else 0
    stretch = -p.charge * 3 if p.anim == "special" else 0
    bx = 31 + sway + p.lunge * 0.4 + p.tilt * 1.5
    by = 36 + p.bob * (2 if p.anim == "idle" else 1) + crouch + stretch * 0.6
    # legs: short and bowed, feet planted wide
    for side, sw, li in ((-1, p.legs[0], p.lift[0]), (1, p.legs[1], p.lift[1])):
        hip = (bx + side * 6, by + 8)
        foot = (31 + side * (9 + crouch * 0.6) + sw * 0.8, gy - 1 - li)
        knee = ((hip[0] + foot[0]) / 2 + side * 3, (hip[1] + foot[1]) / 2)
        paint(c, tube(c, M.pts([hip, knee, foot]), [5, 4.2, 3.6]), "hide", base=2.8, depth=3)
        fx, fy = M.p(*foot)
        paint(c, ell(c, fx + M.s * side * 1.5, fy, 5, 2.4), "hide", base=2.6, depth=2)
        for t in (-3, 0, 3):
            c.px(fx + M.s * side * 1.5 + t, fy + 2, "horn", 3)
    # the far (club-less) arm hangs behind the body on the back view; on the front it is drawn last
    hump_y = by - 21 + (1 if p.hurt else 0)
    (hdx, hdy), cang = _club_pose(p)
    two_hands = p.anim == "special" and (p.charge or p.burst)
    rs = (bx + 14, by - 9)                                          # right (club) shoulder
    hand = (rs[0] + hdx, rs[1] + hdy)
    club_len = 24
    tip = (hand[0] + math.sin(cang) * club_len, hand[1] + math.cos(cang) * club_len)
    if slam:
        tip = (tip[0], min(tip[1], gy - 2))

    def club() -> None:
        hx_, hy_ = M.p(*hand)
        tx_, ty_ = M.p(*tip)
        pts = [(hx_ - (tx_ - hx_) * 0.15, hy_ - (ty_ - hy_) * 0.15), (hx_, hy_),
               ((hx_ + tx_) / 2, (hy_ + ty_) / 2), (tx_, ty_)]
        cm = tube(c, pts, [1.6, 2, 3.2, 4.6])
        paint(c, cm, "wood", base=3.0, depth=2)
        for f in (0.62, 0.8, 0.95):                                 # iron studs on the head
            sx_, sy_ = hx_ + (tx_ - hx_) * f, hy_ + (ty_ - hy_) * f
            nx_, ny_ = -(ty_ - hy_) / club_len, (tx_ - hx_) / club_len
            for sgn in (-1, 1):
                c.px(sx_ + nx_ * 4 * sgn, sy_ + ny_ * 4 * sgn, "metal", 4 if sgn < 0 else 2)

    def arm(shoulder, hnd, near: bool) -> None:
        elbow = ((shoulder[0] + hnd[0]) / 2 + (3 if hnd[1] > shoulder[1] else -2) * (1 if shoulder[0] > bx else -1),
                 (shoulder[1] + hnd[1]) / 2 + (1 if hnd[1] > shoulder[1] else 3))
        paint(c, tube(c, M.pts([shoulder, elbow, hnd]), [5.2, 3.8, 3.4]), "hide", base=3.0 if near else 2.5, depth=3)
        hx_, hy_ = M.p(*hnd)
        paint(c, ell(c, hx_, hy_ + 1, 4, 3.8), "hide", base=3.0 if near else 2.5, depth=2)   # a big fist
        c.line(hx_ - 2, hy_ + 2, hx_ + 2, hy_ + 2, "hide", 1)

    ls = (bx - 14, by - 9)
    if two_hands:
        lhand = (hand[0] - 3, hand[1] + 2)
    else:
        swing = p.legs[1] * 0.8 if p.anim == "walk" else math.sin(p.phase * math.tau) * 0.6
        lhand = (ls[0] - 3 + swing, ls[1] + 23 - p.bob * 0.5 + (2 if p.hurt else 0))
    # body: hump, chest, belly
    hump = ell(c, *M.p(bx - 1, hump_y + 4), 12, 5)
    torso = ell(c, *M.p(bx, by - 6), 16, 9) | ell(c, *M.p(bx, by + 2), 13, 11)
    paint(c, hump | torso, "hide", base=3.0, depth=6)
    yy, xx = np.mgrid[0:64, 0:64]
    if not back:
        belly = ell(c, *M.p(bx + 1, by + 4), 9, 8) & torso
        recolour(c, belly, "hide2", 0)
        c.px(*M.p(bx + 1, by + 5), "hide2", 1)                      # navel
        for k in range(3):                                          # chest hair / warts
            c.px(*M.p(bx - 4 + k * 4, by - 8 + (k % 2)), "hair", 2)
    else:
        for k in range(6):                                          # spine knobs down the hunched back
            c.px(*M.p(bx - 1, hump_y + k * 3), "hide", 4)
            c.px(*M.p(bx - 1, hump_y + k * 3 + 1), "hide", 2)
    # loincloth and belt
    lc = poly(c, M.pts([(bx - 12, by + 7), (bx + 12, by + 7), (bx + 10, by + 15), (bx + 5, by + 13), (bx + 1, by + 17),
                        (bx - 4, by + 13), (bx - 9, by + 16)]))
    paint(c, lc, "cloth", base=2.8, depth=2)
    c.line(*M.p(bx - 12, by + 7), *M.p(bx + 12, by + 7), "cloth", 1)
    c.rect(*M.p(bx - 1, by + 6), *M.p(bx + 1, by + 8), "metal", 3)
    # head, slung forward under the hump
    look = round(math.sin(p.phase * math.tau)) if p.anim == "idle" else 0          # glances side to side
    hx, hy = bx + 1 + look + p.head[0] * 0.5 + p.tilt, by - 14 + p.head[1] + (2 if p.hurt else 0) + crouch * 0.3
    raised = hand[1] < rs[1] - 6                                    # arms up: they pass behind the head
    if raised and not back:
        arm(ls, lhand, False)
        club()
        arm(rs, hand, True)
    _troll_head(c, M, hx, hy, p, back)
    if not back and not raised:
        arm(ls, lhand, False)
        club()
        arm(rs, hand, True)
    elif back:                                                      # from behind both arms are in front
        arm(ls, lhand, True)
        club()
        arm(rs, hand, True)
    if slam:
        _slam(c, M.x(tip[0]), gy, p.burst, p.k)
    return c


def _troll_head(c: Canvas, M: _Mirror, hx: float, hy: float, p: Pose, back: bool) -> None:
    for side in (-1, 1):                                            # ears stick out sideways
        ex = hx + side * 6
        c.polygon(M.pts([(ex, hy - 2), (ex + side * 5, hy - 4), (ex, hy + 1)]), "hide", 3 if side < 0 else 2)
    paint(c, ell(c, *M.p(hx, hy), 6.5, 6), "hide", base=3.0, depth=3)
    paint(c, ell(c, *M.p(hx, hy - 5), 5, 2.6) & ~ell(c, *M.p(hx, hy - 1), 7, 4), "hair", base=3.0, depth=2)
    for t in (-3, -1, 1, 3):                                         # scraggly tuft
        c.line(*M.p(hx + t, hy - 7), *M.p(hx + t * 1.4, hy - 10 - abs(t) % 2), "hair", 3)
    if back:
        paint(c, tube(c, M.pts([(hx, hy - 5), (hx - 1, hy + 2), (hx - 1, hy + 6)]), [4, 4.5, 3]), "hair", depth=2)
        return
    c.line(*M.p(hx - 4, hy - 2), *M.p(hx + 4, hy - 2), "hide", 1)          # heavy brow
    c.line(*M.p(hx - 4, hy - 3), *M.p(hx + 4, hy - 3), "hide", 4)
    if p.down or p.hurt:
        c.line(*M.p(hx - 3, hy - 1), *M.p(hx - 2, hy - 1), "eyes", 0)
        c.line(*M.p(hx + 2, hy - 1), *M.p(hx + 3, hy - 1), "eyes", 0)
    else:
        for ex in (hx - 3, hx + 2):
            c.px(*M.p(ex, hy - 1), "eyes", 5)
            c.px(*M.p(ex + 1, hy - 1), "eyes", 2)
    paint(c, poly(c, M.pts([(hx - 1, hy - 1), (hx + 2, hy - 1), (hx + 3, hy + 3), (hx + 1, hy + 4), (hx - 1, hy + 3)])),
          "hide", base=3.4, depth=1)                                        # long nose
    mo = 2 if p.jaw or p.anim in ("attack", "special") and (p.arm > 0 or p.burst) else 0
    c.rect(*M.p(hx - 3, hy + 4), *M.p(hx + 3, hy + 4 + mo), "mouth", 1)
    c.px(*M.p(hx - 3, hy + 3), "horn", 5)                                   # underbite tusks
    c.px(*M.p(hx + 3, hy + 3), "horn", 5)
    c.px(*M.p(hx - 3, hy + 2), "horn", 4)
    c.px(*M.p(hx + 3, hy + 2), "horn", 4)


def _slam(c: Canvas, x: float, gy: float, t: float, k: int) -> None:
    """A ground slam: a dust ring spreading out along the ground, cracks and thrown rocks."""
    rx = 6 + 16 * t
    r = _rng(31, k)
    for i in range(9):                                                  # dust puffs riding the ring
        a = math.pi * (i / 8)
        px, py = x + math.cos(a) * rx * (1 if i % 2 else 0.85), gy - 2 - math.sin(a) * rx * 0.3
        rad = 1.6 + 1.8 * t * (1 - abs(i - 4) / 8) + r.uniform(0, 0.8)
        puff = ell(c, px, py - rad * 0.5, rad * 1.3, rad) & (c.slot == 0)
        paint(c, puff, "fx", base=3.6 - (1 - t), k=1.2, depth=2, cast=False)
    for i in range(int(4 + 6 * t)):
        a = r.uniform(math.pi * 1.05, math.pi * 1.95)
        d = r.uniform(3, 6 + 12 * t)
        px, py = x + math.cos(a) * d * 1.4, gy - 3 + math.sin(a) * d
        c.rect(px, py, px + (1 if i % 2 else 0), py + 1, "stone", int(r.integers(2, 5)))
    for sgn in (-1, 1):                                                 # cracks in the floor
        c.line(x, gy - 1, x + sgn * (4 + 6 * t), gy - 2 + sgn, "stone", 0)


def _troll_down(c: Canvas, p: Pose, M: _Mirror, back: bool) -> Canvas:
    """Down: dropped to its knees with the club fallen (frame 0), then face-down on the floor (frame 1)."""
    gy = 60
    if p.fall < 1:
        by = 44
        for side in (-1, 1):
            paint(c, ell(c, *M.p(31 + side * 7, gy - 4), 6, 4), "hide", base=2.6, depth=2)
        paint(c, tube(c, M.pts([(48, gy - 3), (60, gy - 6)]), [2, 4.4]), "wood", depth=2)
        paint(c, ell(c, *M.p(31, by), 14, 10) | ell(c, *M.p(30, by - 9), 11, 7), "hide", depth=5)
        paint(c, poly(c, M.pts([(19, by + 6), (43, by + 6), (40, gy - 2), (22, gy - 2)])), "cloth", base=2.8,
              depth=2)
        for side in (-1, 1):
            paint(c, tube(c, M.pts([(31 + side * 13, by - 6), (31 + side * 16, by + 4), (31 + side * 15, gy - 3)]),
                          [5, 4.2, 4]), "hide", depth=3)
        _troll_head(c, M, 33, by - 8, p, back)
        return c
    paint(c, tube(c, M.pts([(50, gy - 2), (61, gy - 3)]), [2, 4.4]), "wood", depth=2)
    paint(c, ell(c, *M.p(28, gy - 7), 18, 7) | ell(c, *M.p(14, gy - 8), 6, 5), "hide", depth=5)   # body, legs
    paint(c, poly(c, M.pts([(30, gy - 13), (40, gy - 12), (40, gy - 3), (30, gy - 2)])), "cloth", base=2.8, depth=2)
    paint(c, tube(c, M.pts([(36, gy - 9), (48, gy - 5), (54, gy - 3)]), [4.5, 4, 3.8]), "hide", depth=3)  # arm
    paint(c, ell(c, *M.p(47, gy - 9), 6, 5), "hide", depth=3)                                     # head
    paint(c, ell(c, *M.p(46, gy - 13), 4, 2), "hair", depth=2)
    c.line(*M.p(48, gy - 9), *M.p(50, gy - 9), "eyes", 0)
    return c


# ----------------------------------------------------------------------------------------------------- golem
def block(c: Canvas, x0: float, y0: float, x1: float, y1: float, slot: str = "stone", *, d: int = 3,
          base: int = 3, mirror: bool = False) -> np.ndarray:
    """A cut-stone block in 3/4 view: a front face, a lit top face and a shaded side face `d` px deep (the side is
    on the right, or on the left when `mirror`). Returns the block's mask so callers can carve runes into it."""
    x0, y0, x1, y1 = (int(round(v)) for v in (x0, y0, x1, y1))
    if mirror:
        front = (x0 + d, y0 + d, x1, y1)
        top = [(x0, y0), (x1 - d, y0), (x1, y0 + d), (x0 + d, y0 + d)]
        side = [(x0, y0), (x0 + d, y0 + d), (x0 + d, y1 + 1), (x0, y1 - d + 1)]
    else:
        front = (x0, y0 + d, x1 - d, y1)
        top = [(x0 + d, y0), (x1 + 1, y0), (x1 + 1 - d, y0 + d), (x0, y0 + d)]
        side = [(x1 + 1 - d, y0 + d), (x1 + 1, y0), (x1 + 1, y1 - d + 1), (x1 + 1 - d, y1 + 1)]
    m = np.zeros((c.h, c.w), bool)
    sm, tm = poly(c, side), poly(c, top)
    fm = np.zeros_like(m)
    fm[max(0, front[1]):max(0, front[3] + 1), max(0, front[0]):max(0, front[2] + 1)] = True
    fm &= ~tm
    under = (c.slot > 0) & (shift(fm | sm, 1, 1) | shift(fm | sm, 0, 1)) & ~(fm | sm | tm)
    c.shade[under] = np.maximum(c.shade[under].astype(np.int16) - 1, 0).astype(np.uint8)
    flat(c, fm, slot, base)
    flat(c, sm, slot, base - 1)
    flat(c, tm, slot, base + 1)
    edge = fm & ~shift(fm, 1, 0)                                         # chiselled lit edge on the front's left
    flat(c, edge & ~shift(fm, 0, 1), slot, base + 1)
    low = fm & ~shift(fm, 0, -1)                                        # and a dark bottom lip
    flat(c, low, slot, base - 1)
    return fm | sm | tm


def _chisel(c: Canvas, slot: str, seed: int, density: float = 0.05) -> None:
    """Pocks and short cracks so the blocks read as stone, not plastic."""
    r = _rng(seed)
    sid = c.slots.id(slot)
    ys, xs = np.nonzero((c.slot == sid) & (c.flags == 0))
    for y, x in zip(ys, xs):
        if r.random() < density:
            c.shade[y, x] = max(1, int(c.shade[y, x]) - 1)
            if r.random() < 0.4 and x + 1 < c.w and c.slot[y, x + 1] == sid:
                c.shade[y, x + 1] = max(1, int(c.shade[y, x + 1]) - 1)


def golem(direction: str, p: Pose) -> Canvas:
    """A stone golem: stacked cut blocks, a head sunk between boulder shoulders with a glowing visor slit, fists
    like masonry and runes cut into chest and forearms. The runes pulse at rest; the special charges them from the
    shoulder down to the fist and lands a glowing punch."""
    c = Canvas(64, 64, SLOTS)
    back = direction == "back"
    M = _Mirror(32, back)
    gy = 60
    if p.anim == "down":
        return _golem_down(c, p, M)
    pulse = (3, 4, 5, 4)[p.k % 4] if p.anim == "idle" else 4
    if p.hurt:
        pulse = 2
    by = 34 + p.bob * (2 if p.anim == "idle" else 1.2) + (2 if p.anim == "special" and p.burst else 0)
    bx = 31 + p.lunge * 0.4 + p.tilt
    twist = -p.charge * 3 + p.burst * 3 if p.anim == "special" else 0
    # legs
    for side, sw, li in ((-1, p.legs[0], p.lift[0]), (1, p.legs[1], p.lift[1])):
        lx = 31 + side * 7 + sw * 0.6
        top_y = by + 6
        foot_y = gy - li
        block(c, *_bx(M, lx - 4, top_y, lx + 4, foot_y - 3), mirror=back, d=2, base=2)
        block(c, *_bx(M, lx - 6 + side, foot_y - 4, lx + 5 + side, foot_y), mirror=back, d=2, base=3)
    # the arm behind (screen-left on the front facing)
    lh = (bx - 17 - math.sin(p.phase * math.tau) * 0.6, by + 11 - p.bob * 0.4)
    if p.anim == "walk":
        lh = (lh[0] + p.legs[1] * 0.6, lh[1])
    rh = _golem_fist(p, bx, by)
    # torso: chest block over a waist block
    block(c, *_bx(M, bx - 9, by + 1, bx + 9, by + 9), mirror=back, base=2)
    chest = block(c, *_bx(M, bx - 14 + twist * 0.3, by - 16, bx + 14 + twist * 0.3, by + 3), mirror=back, d=4)
    _arm(c, M, (bx - 16, by - 12), lh, back, False, 0)
    hx = bx + twist * 0.5 + p.head[0] * 0.3
    hy = by - 17 + p.head[1] * 0.5
    block(c, *_bx(M, hx - 5, hy - 7, hx + 5, hy + 3), mirror=back, d=3, base=3)
    if not back:
        vis = 2 if p.hurt else (5 if p.anim == "special" and p.charge else 4)
        c.line(*M.p(hx - 3, hy - 1), *M.p(hx + 2, hy - 1), "eyes", vis)
        c.px(*M.p(hx - 3, hy - 1), "eyes", vis - 1)
    # shoulders: rounded boulders, then the near arm
    for side in (-1, 1):
        sx_ = bx + side * 15 + twist * 0.3
        paint(c, ell(c, *M.p(sx_, by - 13), 7, 6), "stone", base=3.0, depth=3)
        recolour(c, ell(c, *M.p(sx_ - 1, by - 17), 5, 2.4) & c.mask("stone"), "hide2", 0)    # moss on top
    _arm(c, M, (bx + 16 + twist * 0.3, by - 12), rh, back, True, p.charge if p.anim == "special" else 0)
    # runes cut into the chest (a ring and a cross on the front, a tall sigil on the back)
    cx_, cy_ = M.p(bx + twist * 0.3, by - 6)
    if not back:
        ring = ell(c, cx_, cy_, 5, 5) & ~ell(c, cx_, cy_, 3.6, 3.6)
        flat(c, ring & chest, "glow", pulse)
        c.line(cx_, cy_ - 3, cx_, cy_ + 3, "glow", pulse)
        c.line(cx_ - 3, cy_, cx_ + 3, cy_, "glow", pulse - 1)
    else:
        for k in range(4):
            c.line(cx_ - 2 + k % 2 * 4, cy_ - 7 + k * 3, cx_ + 2 - k % 2 * 4, cy_ - 5 + k * 3, "glow", pulse - 1)
        c.line(cx_, cy_ - 8, cx_, cy_ + 6, "glow", pulse)
    _chisel(c, "stone", 71 + p.k)
    if p.hurt and p.k == 0:                                             # chips knocked off
        for dx, dy in ((10, -20), (14, -16), (8, -24)):
            c.rect(*M.p(bx + dx, by + dy), *M.p(bx + dx + 1, by + dy + 1), "stone", 4)
    if p.anim == "special" and p.burst:
        fx_, fy_ = M.p(*rh)
        _rune_burst(c, fx_ + M.s * 6, fy_, p.burst, p.k)
    if p.anim == "attack" and p.k == 3:
        _slam(c, M.x(rh[0]), gy, 0.5, 3)
    return c


def _bx(M: _Mirror, x0, y0, x1, y1) -> tuple[float, float, float, float]:
    a, b = M.x(x0), M.x(x1)
    return min(a, b), y0, max(a, b), y1


def _golem_fist(p: Pose, bx: float, by: float) -> tuple[float, float]:
    if p.anim == "attack":
        return [(bx + 19, by - 18), (bx + 15, by - 24), (bx + 23, by + 12), (bx + 22, by + 18), (bx + 20, by + 10)][p.k]
    if p.anim == "special":
        return [(bx + 12, by - 4), (bx + 8, by - 6), (bx + 6, by - 7), (bx + 21, by - 7), (bx + 22, by - 6),
                (bx + 19, by)][p.k]
    if p.anim == "walk":
        return (bx + 18 - p.legs[0] * 0.6, by + 11)
    if p.anim == "hurt":
        return (bx + 19, by + 8)
    return (bx + 18 + math.sin(p.phase * math.tau) * 0.6, by + 11 - p.bob * 0.4)


def _arm(c: Canvas, M: _Mirror, shoulder, fist, back: bool, near: bool, charge: float) -> None:
    """A golem arm: an upper-arm block and a forearm block towards the fist, then the fist itself. Runes on the
    forearm fill with light as `charge` rises, from the elbow down."""
    sx, sy = shoulder
    fx, fy = fist
    ex, ey = (sx + fx) / 2 + (2 if fx > sx else -2), (sy + fy) / 2
    base = 3 if near else 2
    paint(c, tube(c, M.pts([(sx, sy), (ex, ey)]), [4.5, 4]), "stone", base=base, depth=2)
    paint(c, tube(c, M.pts([(ex, ey), (fx, fy)]), [4.6, 4.6]), "stone", base=base, depth=2)
    for t in (0.25, 0.55, 0.85):                                         # forearm rune notches
        rx, ry = M.p(ex + (fx - ex) * t, ey + (fy - ey) * t)
        lit = charge >= t * 0.9
        c.px(rx, ry, "glow", 5 if lit else (3 if near else 2))
        c.px(rx + 1, ry, "glow", 4 if lit else 2)
    block(c, *_bx(M, fx - 5, fy - 4, fx + 5, fy + 5), mirror=back, d=2, base=base + (1 if charge >= 1 else 0))
    gx, gy_ = M.p(fx, fy + 1)
    c.line(gx - 3, gy_, gx + 2, gy_, "stone", 1)                          # knuckle line
    if charge >= 0.7:
        c.line(gx - 3, gy_ + 2, gx + 2, gy_ + 2, "glow", 5)
        r = _rng(5, int(charge * 10))
        for _ in range(int(charge * 6)):                                   # motes drawn into the fist
            a = r.uniform(0, math.tau)
            c.px(gx + math.cos(a) * 8, gy_ + math.sin(a) * 7, "fx", int(r.integers(3, 6)))


def _rune_burst(c: Canvas, x: float, y: float, t: float, k: int) -> None:
    """Rune punch impact: a ring of light and spokes thrown out from the fist."""
    r = 3 + 9 * t
    ring = ell(c, x, y, r, r) & ~ell(c, x, y, r - 1.6, r - 1.6) & (c.slot == 0)
    flat(c, ring, "fx", 5 if t > 0.5 else 3)
    for i in range(8):
        a = i * math.pi / 4 + k * 0.3
        c.line(x + math.cos(a) * (r + 1), y + math.sin(a) * (r + 1), x + math.cos(a) * (r + 3 + 3 * t),
               y + math.sin(a) * (r + 3 + 3 * t), "fx", 4)


def _golem_down(c: Canvas, p: Pose, M: _Mirror) -> Canvas:
    """Down: the golem buckles to a crouch with its runes guttering, then falls apart into a cairn of blocks."""
    gy = 60
    back = M.s < 0
    if p.fall < 1:
        block(c, *_bx(M, 18, 44, 46, gy), mirror=back, d=4, base=2)
        block(c, *_bx(M, 20, 30, 44, 48), mirror=back, d=4)
        block(c, *_bx(M, 26, 24, 36, 33), mirror=back, d=3)
        c.line(*M.p(28, 29), *M.p(32, 29), "eyes", 2)
        for side in (-1, 1):
            block(c, *_bx(M, 31 + side * 17 - 5, gy - 9, 31 + side * 17 + 5, gy), mirror=back, d=2)
        c.line(*M.p(29, 38), *M.p(34, 41), "glow", 3)
    else:
        r = _rng(99)
        for (x0, y0, w, h) in ((14, 50, 14, 10), (30, 49, 16, 11), (44, 53, 10, 7), (20, 42, 12, 9), (33, 40, 11, 10),
                               (26, 34, 9, 8), (8, 55, 7, 5)):
            block(c, *_bx(M, x0, y0, x0 + w, y0 + h), mirror=back, d=3, base=int(r.integers(2, 4)))
        c.line(*M.p(36, 45), *M.p(40, 45), "glow", 2)
        c.px(*M.p(29, 38), "eyes", 1)
    _chisel(c, "stone", 7)
    return c


# ------------------------------------------------------------------------------------------------- elementals
def _depth(mask: np.ndarray, n: int = 6) -> np.ndarray:
    d = mask.astype(np.int16)
    m = mask.copy()
    for _ in range(n):
        m = _erode(m)
        d += m
    return d


def glow_fill(c: Canvas, mask: np.ndarray, slot: str, *, lo: int = 2, hi: int = 5, step: float = 1.5,
              seed: int | None = None, flicker: float = 0.0) -> None:
    """Fills a self-lit body: shade rises with depth from the edge, so the core burns brightest (fire, magic).
    `flicker` darkens or brightens random pixel pairs so consecutive frames shimmer."""
    d = _depth(mask)
    sh = np.clip(lo + (d - 1) / step, lo, hi).astype(np.int16)
    if seed is not None and flicker:
        r = _rng(seed)
        noise = r.random(mask.shape)
        sh = np.where(noise < flicker, sh - 1, np.where(noise > 1 - flicker * 0.6, sh + 1, sh))
    c.slot[mask] = c.slots.id(slot)
    c.shade[mask] = np.clip(sh, lo - 1, hi)[mask].astype(np.uint8)
    c.flags[mask] = 0


_ELEMENT_FIST = {
    "attack": [(-3, -6), (-6, -9), (9, -2), (11, -1), (5, 2)],
    "special": [(-2, -4), (-4, -8), (-5, -10), (2, -12), (3, -13), (1, -6)],
}


def _el_hands(p: Pose, bx: float, by: float, reach: float = 1.0) -> tuple[tuple[float, float], tuple[float, float]]:
    """Both hands for an elemental's arms: hanging and swaying at rest, a wind-up and a forward strike on the
    attack, raised to the sky to gather the special."""
    sw = math.sin(p.phase * math.tau)
    lh = (bx - 15 - sw, by + 4 + p.bob * 0.5)
    rh = (bx + 15 + sw, by + 4 - p.bob * 0.5)
    if p.anim == "walk":
        lh, rh = (lh[0] + p.legs[1] * 0.7, lh[1]), (rh[0] + p.legs[0] * 0.7, rh[1])
    if p.anim in _ELEMENT_FIST:
        dx, dy = _ELEMENT_FIST[p.anim][p.k]
        rh = (bx + 15 + dx * reach, by + 4 + dy)
        if p.anim == "special":
            lh = (bx - 15 - dx * reach, by + 4 + dy)
    if p.hurt:
        lh, rh = (lh[0] - 2, lh[1] - 3), (rh[0] + 2, rh[1] - 3)
    return lh, rh


def fire_elemental(direction: str, p: Pose) -> Canvas:
    """A fire elemental: a column of flame with a burning head and arms, no legs, only a swirl where it touches the
    ground. Every frame re-rolls its flame tongues so it never holds still; the special flares into a nova."""
    c = Canvas(64, 64, SLOTS)
    M = _Mirror(32, direction == "back")
    gy = 60
    seed = ORDER.index(p.anim) * 10 + p.k
    r = _rng(41, seed)
    fall = p.fall
    flare = p.burst if p.anim == "special" else 0
    shrink = 1 - fall * 0.55
    bx = 32 + p.lunge * 0.4 + p.tilt
    by = 34 + p.bob + fall * 14 - p.charge * 2
    lh, rh = _el_hands(p, bx, by)
    w = (10 + flare * 3) * shrink
    body = poly(c, M.pts([(bx - w, by - 6), (bx + w, by - 6), (bx + 5, by + 10), (bx + 2.5, gy - 4),
                          (bx - 2.5, gy - 4), (bx - 6, by + 10)])) | ell(c, *M.p(bx, by - 6), w, 6)
    body |= ell(c, *M.p(bx, gy - 3), 6 + flare * 4, 2.2)
    hx, hy = bx + p.head[0] * 0.4, by - 17 * shrink + p.head[1] * 0.5
    body |= ell(c, *M.p(hx, hy), 6.5 * shrink + 0.5, 6.5 * shrink + 0.5)
    for i in range(6):                                                    # flame hair, re-rolled every frame
        x = hx - 6 + i * 2.4
        h = (5 + r.uniform(0, 7) + p.charge * 4 + flare * 4) * shrink
        lean = r.uniform(-2, 2) - p.lunge * 0.3
        body |= poly(c, M.pts([(x - 2, hy - 2), (x + 2, hy - 2), (x + lean, hy - 4 - h)]))
    if fall < 1:
        for sh_, hd in (((bx - 9, by - 5), lh), ((bx + 9, by - 5), rh)):
            mid = ((sh_[0] + hd[0]) / 2, (sh_[1] + hd[1]) / 2 - 2)
            body |= tube(c, M.pts([sh_, mid, hd]), [4, 3, 3.6])
            body |= poly(c, M.pts([(hd[0] - 3, hd[1]), (hd[0] + 3, hd[1]), (hd[0] + r.uniform(-1, 1), hd[1] - 6)]))
    for i in range(int(5 + flare * 8)):                                   # tongues licking off the body's edge
        x = bx + r.uniform(-w, w)
        y = by + r.uniform(-8, 8)
        body |= poly(c, M.pts([(x - 1.5, y), (x + 1.5, y), (x + r.uniform(-2, 2), y - r.uniform(3, 7))]))
    glow_fill(c, body, "hide", lo=2, hi=5, step=1.3, seed=seed + 5, flicker=0.12)
    core = _depth(body) >= 5
    flat(c, core, "hide2", 4 + (p.k % 2))
    if direction != "back" and fall < 1:
        eh = 1 if p.hurt else 2
        for ex in (hx - 3, hx + 2):
            c.rect(*M.p(ex, hy - 1), *M.p(ex + 1, hy - 2 + eh), "eyes", 0)
        if p.jaw or p.burst:
            c.rect(*M.p(hx - 1, hy + 3), *M.p(hx + 1, hy + 4), "eyes", 0)
    for i in range(4):                                                    # embers rising
        ex, ey = bx + r.uniform(-14, 14), by - 24 - r.uniform(0, 14) + fall * 8
        c.px(*M.p(ex, ey), "hide2", int(r.integers(3, 6)))
    if flare:
        _nova(c, bx, by - 4, flare, seed, "fx")
    return c


def _nova(c: Canvas, x: float, y: float, t: float, seed: int, slot: str) -> None:
    """An expanding ring of sparks around the body: every elemental's special release."""
    r = _rng(77, seed)
    rad = 10 + 18 * t
    for i in range(28):
        a = i / 28 * math.tau + r.uniform(-0.08, 0.08)
        d = rad + r.uniform(-2, 2)
        px, py = x + math.cos(a) * d, y + math.sin(a) * d * 0.7
        c.px(px, py, slot, 5 if t > 0.5 else 3)
        c.px(px - math.cos(a) * 1.5, py - math.sin(a), slot, 3)


def water_elemental(direction: str, p: Pose) -> Canvas:
    """A water elemental: a breaking wave for a lower body, a torso and arms rising out of it, foam on every crest
    and light streaks running down. The wave's crest travels round the base as it idles; the special rears up and
    crashes into a ring of spray."""
    c = Canvas(64, 64, SLOTS)
    M = _Mirror(32, direction == "back")
    gy = 60
    seed = ORDER.index(p.anim) * 10 + p.k
    r = _rng(43, seed)
    fall = p.fall
    rear = p.charge * 4 if p.anim == "special" else 0
    bx = 32 + p.lunge * 0.4 + p.tilt
    by = 33 + p.bob - rear + fall * 18
    lh, rh = _el_hands(p, bx, by)
    crest = math.sin(p.phase * math.tau) * 6
    base = ell(c, *M.p(bx, gy - 4), 15 + fall * 4, 5) | ell(c, *M.p(bx - 8 + crest, gy - 8), 5, 5)
    base |= poly(c, M.pts([(bx - 9 + crest, gy - 12), (bx - 3 + crest, gy - 14), (bx - 1 + crest, gy - 10)]))
    paint(c, base, "hide", base=2.8, depth=3)
    if fall < 1:
        torso = tube(c, M.pts([(bx, gy - 6), (bx - 1, by + 6), (bx, by - 6)]), [8, 6, 9])
        head = ell(c, *M.p(bx + p.head[0] * 0.4, by - 16 + p.head[1] * 0.5), 6, 6.5)
        paint(c, torso | head, "hide", depth=4)
        for sh_, hd in (((bx - 8, by - 7), lh), ((bx + 8, by - 7), rh)):
            mid = ((sh_[0] + hd[0]) / 2, (sh_[1] + hd[1]) / 2 - 2)
            paint(c, tube(c, M.pts([sh_, mid, hd]), [4, 3, 3.8]), "hide", depth=2)
            hx_, hy_ = M.p(*hd)
            paint(c, ell(c, hx_, hy_, 4, 4) | poly(c, [(hx_ - 3, hy_), (hx_ + 3, hy_), (hx_ + M.s * 2, hy_ + 6)]),
                  "hide", base=3.2, depth=2)
        hx, hy = M.p(bx + p.head[0] * 0.4, by - 16 + p.head[1] * 0.5)
        if direction != "back":
            for ex in (hx - 3, hx + 2):
                c.rect(ex, hy - 1, ex + 1, hy - 1 + (0 if p.hurt else 1), "eyes", 5 if not p.hurt else 2)
        for k in range(3):                                                  # streaks of light running down
            sx = bx - 4 + k * 3
            y0 = by - 4 + ((p.k + k) % 4) * 4
            c.line(*M.p(sx, y0), *M.p(sx, y0 + 3), "hide", 5)
    m = c.mask("hide")
    foam = m & ~shift(m, 0, 1) | m & ~shift(m, 0, 2) & (r.random(m.shape) < 0.5)
    recolour(c, foam, "hide2", 1)
    for _ in range(5):                                                      # bubbles
        bx_, by_ = bx + r.uniform(-8, 8), by + r.uniform(-4, 18)
        if c.mask("hide")[int(np.clip(by_, 0, 63)), int(np.clip(M.x(bx_), 0, 63))]:
            c.px(*M.p(bx_, by_), "hide2", 5)
    if p.anim == "special" and p.burst:
        _nova(c, bx, by, p.burst, seed, "fx")
        for i in range(int(4 + 8 * p.burst)):                               # spray falling back
            a = r.uniform(math.pi * 1.1, math.pi * 1.9)
            d = r.uniform(6, 12 + 14 * p.burst)
            c.line(*M.p(bx + math.cos(a) * d, by + math.sin(a) * d), *M.p(bx + math.cos(a) * d, by + math.sin(a) * d
                                                                              + 2), "hide2", 4)
    return c


def earth_elemental(direction: str, p: Pose) -> Canvas:
    """An earth elemental: a heap of boulders holding together around a seam of glowing crystal, stumpy rock legs,
    fists of stone and pebbles orbiting it. The special drives both fists down and spikes of rock erupt around it."""
    c = Canvas(64, 64, SLOTS)
    back = direction == "back"
    M = _Mirror(32, back)
    gy = 60
    seed = ORDER.index(p.anim) * 10 + p.k
    r = _rng(47, seed)
    if p.fall >= 1:
        for (x, y, rx, ry) in ((22, 55, 8, 5), (34, 54, 9, 6), (45, 56, 6, 4), (29, 47, 6, 5), (39, 48, 5, 4)):
            paint(c, ell(c, *M.p(x, y), rx, ry), "stone", base=2.8, k=2.0, depth=3)
        c.line(*M.p(30, 52), *M.p(34, 54), "glow", 2)
        return c
    slam = p.anim == "special" and p.burst > 0
    bx = 32 + p.lunge * 0.4 + p.tilt
    by = 33 + p.bob + p.fall * 8 + (3 if slam else 0)
    lh, rh = _el_hands(p, bx, by, 0.8)
    lh, rh = (lh[0] - 2, lh[1] + 5), (rh[0] + 2, rh[1] + 5)
    if slam:
        lh, rh = (bx - 10, gy - 6), (bx + 10, gy - 6)
    for side, sw, li in ((-1, p.legs[0], p.lift[0]), (1, p.legs[1], p.lift[1])):
        paint(c, tube(c, M.pts([(bx + side * 6, by + 10), (bx + side * 8 + sw * 0.5, gy - 5 - li)]), [5, 5.5]),
              "stone", base=2.6, depth=3)
    for (cx_, cy_, rx, ry) in ((0, 1, 13, 11), (-8, -7, 9, 8), (8, -8, 9, 8), (0, 10, 10, 6)):
        paint(c, ell(c, *M.p(bx + cx_, by + cy_), rx, ry), "stone", depth=4)
    seam = [(bx - 3, by - 8), (bx + 1, by - 3), (bx - 2, by + 2), (bx + 2, by + 7)]
    for a, b in zip(seam, seam[1:]):
        c.line(*M.p(*a), *M.p(*b), "glow", 4 if not p.hurt else 2)
    hx, hy = bx + 1 + p.head[0] * 0.4, by - 16 + p.head[1] * 0.5
    paint(c, ell(c, *M.p(hx, hy), 6.5, 6), "stone", base=3.0, depth=3)
    if not back:
        c.line(*M.p(hx - 3, hy), *M.p(hx - 2, hy), "eyes", 5 if not p.hurt else 2)
        c.line(*M.p(hx + 2, hy), *M.p(hx + 3, hy), "eyes", 5 if not p.hurt else 2)
        c.line(*M.p(hx - 3, hy - 2), *M.p(hx + 3, hy - 2), "stone", 1)
    for side, hd in ((-1, lh), (1, rh)):
        sx_, sy_ = bx + side * 15, by - 9
        paint(c, ell(c, *M.p(sx_, sy_), 7.5, 7), "stone", base=3.0, depth=3)
        for f in (0.35, 0.7):
            paint(c, ell(c, *M.p(sx_ + (hd[0] - sx_) * f, sy_ + (hd[1] - sy_) * f), 4.4, 4.2), "stone", base=2.8,
                  depth=2)
        paint(c, ell(c, *M.p(*hd), 6, 5.4), "stone", base=3.0, depth=3)
        cx_, cy_ = M.p(sx_ - side * 1, sy_ - 6)                               # crystals growing from the shoulder
        c.polygon([(cx_ - 2, cy_ + 2), (cx_ + 1, cy_ + 2), (cx_ - 1 + M.s * side, cy_ - 5)], "glow", 4)
        c.polygon([(cx_ + 1, cy_ + 2), (cx_ + 3, cy_ + 2), (cx_ + 3 + M.s * side, cy_ - 2)], "glow", 3)
        c.px(cx_ - 1 + M.s * side, cy_ - 3, "glow", 5)
    recolour(c, c.mask("stone") & ~shift(c.mask("stone"), 0, 2) & (r.random((64, 64)) < 0.55), "hide2", 0)  # moss
    leaf_texture(c, "stone", 13 + p.k, density=0.08)
    for i in range(3):                                                       # orbiting pebbles
        a = p.phase * math.tau + i * math.tau / 3 if p.anim == "idle" else i * 2.1 + p.k
        c.rect(*M.p(bx + math.cos(a) * 19, by - 6 + math.sin(a) * 5), *M.p(bx + math.cos(a) * 19 + 1,
                                                                           by - 6 + math.sin(a) * 5), "stone", 3)
    if slam:
        for i, dx in enumerate((-22, -15, 15, 22, -8, 8)):                   # spikes erupting from the floor
            h = (5 + (i % 3) * 3) * min(1, p.burst * 1.6)
            x = bx + dx
            c.polygon(M.pts([(x - 3, gy), (x + 3, gy), (x + (i % 2) - 0.5, gy - h)]), "stone", 3)
            c.line(*M.p(x - 1, gy - 1), *M.p(x + (i % 2) - 0.5, gy - h + 1), "stone", 4)
        _slam(c, M.x(bx), gy, p.burst, p.k)
    if p.anim == "special" and p.charge:
        for i in range(int(p.charge * 5)):
            c.px(*M.p(bx + r.uniform(-16, 16), gy - r.uniform(0, 3)), "stone", 4)
    return c


def air_elemental(direction: str, p: Pose) -> Canvas:
    """An air elemental: a hovering whirlwind of stacked, tilting rings with two eyes in the vortex and gusts for
    arms. The rings turn every frame, so even the idle spins; the special throws out rings of wind."""
    c = Canvas(64, 64, SLOTS)
    M = _Mirror(32, direction == "back")
    gy = 60
    fall = p.fall
    spin = p.k * 1.1 + ORDER.index(p.anim) * 0.4
    bx = 32 + p.lunge * 0.4 + p.tilt
    hover = 5 - fall * 5
    top, bot = 9 + p.bob * 1.5 + fall * 24 - p.charge * 2, gy - 2 - hover
    flare = p.burst if p.anim == "special" else 0
    rings = 9
    yy, xx = np.mgrid[0:64, 0:64]
    haze = np.zeros((64, 64), bool)
    bands = []
    for i in range(rings):
        t = i / (rings - 1)
        y = top + 3 + t * (bot - top - 3)
        rx = (16 + flare * 4) * (1 - t) ** 1.2 + 2
        cx_ = bx + math.sin(t * 5 + spin) * 3 * (0.3 + t)
        ry = max(1.2, rx * 0.22)
        bands.append((cx_, y, rx, ry, t))
        haze |= ell(c, *M.p(cx_, y + ry), rx * 0.9, ry + 2.2)
    flat(c, haze, "hide", 2)
    c.shade[haze & ((xx + yy) % 2 == 0)] = 1
    for i, (cx_, y, rx, ry, t) in enumerate(bands):                       # each ring: lit front, dim back
        ring = ell(c, *M.p(cx_, y), rx, ry) & ~ell(c, *M.p(cx_, y - 0.8), rx - 1.5, ry - 0.6)
        front = ring & (yy + 0.5 >= y)
        flat(c, ring & ~front, "hide", 3)
        flat(c, front, "hide", 4)
        for j in range(3):                                                 # bright streaks turning round
            a = spin * 1.6 + j * 2.1 + i * 0.7
            if math.sin(a) > -0.2:
                sx = cx_ + math.cos(a) * rx
                c.line(*M.p(sx - 1.5, y + math.sin(a) * ry), *M.p(sx + 1.5, y + math.sin(a) * ry), "hide", 5)
    cy_ = top + 10
    if direction != "back" and fall < 1:
        for ex in (bx - 4, bx + 3):
            c.rect(*M.p(ex, cy_), *M.p(ex + 1, cy_ + (0 if p.hurt else 1)), "eyes", 5)
    if fall < 1:
        lh, rh = _el_hands(p, bx, top + 18)
        for side, hd in ((-1, lh), (1, rh)):                                # gust arms: curling streaks
            sx_ = bx + side * 12
            pts = curve((sx_, top + 10), ((sx_ + hd[0]) / 2 + side * 5, top + 6), hd, 6)
            for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
                c.thick_line(*M.p(x0, y0), *M.p(x1, y1), 2, "hide", 4)
            hx_, hy_ = M.p(*hd)
            for a in np.linspace(0, math.tau * 0.8, 9):                      # each gust ends in a curl
                rr = 3.2 - a * 0.35
                c.px(hx_ + math.cos(a + spin) * rr, hy_ + math.sin(a + spin) * rr, "hide", 5 if a < 2.5 else 4)
    for i in range(6):                                                       # debris whirled round
        a = spin * 1.3 + i * 1.05
        d = 9 + i * 2.5
        c.rect(*M.p(bx + math.cos(a) * d, top + 12 + i * 5 + math.sin(a) * 2),
               *M.p(bx + math.cos(a) * d + (i % 2), top + 12 + i * 5 + math.sin(a) * 2), "hide2", 3 + i % 3)
    if flare:
        for k in range(2):                                                   # wind rings thrown out
            rad = 12 + 14 * flare + k * 6
            for a in np.linspace(0, math.tau, 40, endpoint=False):
                if (int(a * 6) + k) % 3:
                    c.px(bx + math.cos(a + spin) * rad, top + 16 + math.sin(a + spin) * rad * 0.45, "fx", 5 - k)
    if p.anim == "attack" and p.k in (2, 3):
        for k in range(3):                                                   # a gust lashing forward
            c.line(*M.p(bx + 18, top + 16 + k * 3), *M.p(bx + 29, top + 15 + k * 3), "fx", 4 + (k == 1))
    return c


# ---------------------------------------------------------------------------------------------------- wraith
def wraith(direction: str, p: Pose) -> Canvas:
    """A wraith: a hooded, tattered cloak with nothing inside but two eyes, hovering a hand's breadth off the floor
    and trailing ghost-stuff behind it. Bony claws do the attack; the special is a scream that rolls out in rings."""
    c = Canvas(48, 48, SLOTS)
    back = direction == "back"
    M = _Mirror(24, back)
    seed = ORDER.index(p.anim) * 10 + p.k
    r = _rng(61, seed)
    wave = p.phase * math.tau if p.anim in ("idle", "walk") else p.k * 1.3
    if p.fall >= 1:
        heap = ell(c, *M.p(24, 42), 11, 3.5) | poly(c, M.pts([(17, 42), (24, 35), (31, 42)]))
        paint(c, heap, "cloth", base=2.6, depth=2)
        for i in range(3):
            c.px(*M.p(18 + i * 6, 41), "cloth", 1)
        for i in range(3):                                                  # last wisps rising
            c.px(*M.p(22 + i * 2, 31 - i * 3), "fx", 3 - i % 2)
        return c
    float_y = -2 + math.sin(wave) * 1.5 + p.fall * 8
    lean = 2 if p.anim == "walk" else p.tilt * 2
    bx = 24 + p.lunge * 0.5 + lean * 0.5
    ty = 14 + float_y
    scream = p.anim == "special"
    back_tilt = -p.charge * 2 if scream else 0
    # trail: wisps streaming behind (to screen-left on the front facing)
    trail_len = 10 + (6 if p.anim == "walk" else 0) + p.lunge
    for i in range(4):
        y0 = ty + 18 + i * 3
        pts = [(bx - 4, y0)]
        for k in range(1, 5):
            pts.append((bx - 4 - k * trail_len / 4, y0 + math.sin(wave + i + k * 0.9) * 2 + k))
        for (x0, y0_), (x1, y1) in zip(pts, pts[1:]):
            c.line(*M.p(x0, y0_), *M.p(x1, y1), "fx", 3 if i % 2 else 2)
    # robe: shoulders to a ragged hem, tatters swaying
    robe = poly(c, M.pts([(bx - 6, ty + 4), (bx + 6, ty + 4), (bx + 10, ty + 18), (bx + 9, ty + 24),
                          (bx - 10, ty + 24), (bx - 11, ty + 18)]))
    for i in range(6):
        x = bx - 9 + i * 3.6
        ln = 6 + (i * 7 % 5) + math.sin(wave + i * 1.4) * 2
        sway = math.sin(wave + i) * 1.5 - lean
        robe |= poly(c, M.pts([(x - 2, ty + 22), (x + 2, ty + 22), (x + 0.5 + sway, ty + 22 + ln)]))
    paint(c, robe, "cloth", base=2.7, depth=3)
    hem = robe & (_grid(c)[0] > ty + 24)
    yy, xx = np.mgrid[0:48, 0:48]
    c.shade[hem & ((xx + yy) % 2 == 0)] = 1                                  # fraying into nothing
    for i in range(3):                                                       # folds
        x = bx - 5 + i * 5
        c.line(*M.p(x, ty + 9), *M.p(x - 1 + i, ty + 21), "cloth", 1)
    # arms: sleeves reaching forward, bone claws
    if p.anim == "attack":
        hand = [(bx + 9, ty + 2), (bx + 8, ty - 3), (bx + 15, ty + 14), (bx + 15, ty + 17), (bx + 12, ty + 12)][p.k]
    elif scream:
        hand = (bx + 9 - p.charge * 3, ty + 2 - p.charge * 2) if p.charge else (bx + 13, ty + 6)
    else:
        hand = (bx + 11, ty + 11 + math.sin(wave + 1) * 1.2)
    far_hand = (bx - 9, ty + 12 + math.sin(wave) * 1.2) if not scream or not p.charge else (bx - 9, ty + 2)
    for sh_, hd, base in (((bx - 5, ty + 6), far_hand, 2.4), ((bx + 5, ty + 6), hand, 3.0)):
        paint(c, tube(c, M.pts([sh_, ((sh_[0] + hd[0]) / 2, (sh_[1] + hd[1]) / 2 + 1), hd]), [3, 2.6, 3.2]),
              "cloth", base=base, depth=2)
        hx_, hy_ = M.p(*hd)
        for k in (-1, 0, 1):                                                 # claws
            c.line(hx_ + k, hy_ + 2, hx_ + k * 1.5 + M.s, hy_ + 4, "horn", 4)
    # hood
    hx, hy = bx + p.head[0] * 0.3, ty + back_tilt
    hood = ell(c, *M.p(hx, hy + 1), 6.5, 6.5) | poly(c, M.pts([(hx - 5, hy - 2), (hx + 3, hy - 5), (hx - 3, hy - 11),
                                                                 (hx - 6, hy - 3)]))
    paint(c, hood, "cloth", base=3.0, depth=3)
    if not back:
        face = ell(c, *M.p(hx + 1, hy + 2), 4, 4.5)
        flat(c, face, "mouth", 0)
        flat(c, face & ~shift(face, 0, 1), "mouth", 1)
        eye = 5 if not p.hurt else 3
        for ex in (hx - 1, hx + 2):
            c.px(*M.p(ex, hy + 1), "eyes", eye)
            c.px(*M.p(ex, hy + 2), "eyes", eye - 2)
        if scream and (p.burst or p.charge > 0.6):
            c.rect(*M.p(hx, hy + 4), *M.p(hx + 1, hy + 5), "eyes", 4)
    else:
        c.line(*M.p(hx - 1, hy - 4), *M.p(hx + 1, hy + 5), "cloth", 1)       # the hood's back seam
    if scream and p.burst:
        _scream(c, M, hx + 4, hy + 3, p.burst, seed)
    if p.anim == "attack" and p.k in (2, 3):
        for k in range(3):                                                   # claw rake trails
            c.line(*M.p(bx + 9 + k * 2, ty - 1 + k), *M.p(bx + 16 + k * 2, ty + 12 + k), "fx", 5 - k)
    if p.hurt:                                                               # it flickers when struck
        m = (c.slot > 0) & (r.random((48, 48)) < (0.07 if p.k == 0 else 0.03)) & (c.slots.id("fx") != c.slot)
        flat(c, m, "fx", 4)
    if p.fall:
        m = (c.slot > 0) & ((xx + yy) % 4 == 0) & (yy > ty + 14)
        flat(c, m, "fx", 2)
    return c


def _scream(c: Canvas, M: _Mirror, x: float, y: float, t: float, seed: int) -> None:
    """Sound made visible: nested arcs rolling forward from the hood."""
    for k in range(3):
        rad = 4 + k * 5 + t * 6
        if rad > 22:
            continue
        for a in np.linspace(-0.9, 0.9, int(rad * 2.2)):
            px = x + math.cos(a) * rad
            py = y + math.sin(a) * rad * 1.1
            c.px(*M.p(px, py), "fx", max(2, 5 - k))


# ----------------------------------------------------------------------------------------------------- mimic
_MIMIC_AT = (7, 12)                     # where props.chest's 26x26 canvas sits in the 40x40 frame
_CHEST_SLOTS = {"wood": "wood", "metal": "metal", "gold": "gold", "dark": "hair"}


def _chest_import(back: bool) -> Canvas:
    """props.chest, copied pixel for pixel into this set's slots (minus its outline, which the sheet adds back the
    same way). The mimic's resting frames are this canvas, so on the map it is the prop until it moves."""
    from . import props as PR
    src = PR.chest()
    c = Canvas(40, 40, SLOTS)
    ox, oy = _MIMIC_AT
    for y in range(src.h):
        for x in range(src.w):
            sid = int(src.slot[y, x])
            if sid and not src.flags[y, x]:
                c.px(x + ox, y + oy, _CHEST_SLOTS[PR.SLOTS.name(sid)], int(src.shade[y, x]))
    if back:                                                            # the lock is on the far side
        c.rect(ox + 12, oy + 19, ox + 14, oy + 21, "wood", 3)
        c.px(ox + 12, oy + 19, "wood", 2)
    return c


# per frame: lid lift (px), hop (px, - is up), lean (px), tongue length, tongue mode, eyes open
_MIMIC_POSES: dict[str, list[tuple[float, float, float, float, str, bool]]] = {
    "idle": [(0, 0, 0, 0, "", False), (0, 0, 0, 0, "", False), (1.5, 0, 0, 0, "", True), (0, 0, 0, 0, "", False)],
    "walk": [(2, 0, 0, 0, "", True), (5, -5, 1, 2, "lick", True), (3, -2, 1, 0, "", True), (0, 0, 0, 0, "", False)],
    "attack": [(3, 0, -1, 0, "", True), (6.5, -1, -2, 4, "lick", True), (5, -3, 4, 9, "out", True),
               (0, 0, 5, 0, "", True), (2, 0, 2, 0, "", True)],
    "special": [(5, 0, 0, 3, "lick", True), (6.5, 0, -1, 6, "coil", True), (6.5, 0, 1, 16, "out", True),
                (6, 0, 1, 11, "out", True), (1, 0, 0, 0, "", True), (2, 0, 0, 2, "lick", True)],
    "hurt": [(5, 0, -3, 4, "limp", False), (3, 0, -1, 2, "limp", False)],
    "down": [(5.5, 0, -1, 6, "limp", False), (6.5, 0, 0, 9, "limp", False)],
}


def mimic(direction: str, p: Pose) -> Canvas:
    """A mimic: indistinguishable from props.chest at rest (idle frame 0 is the chest itself), then the lid lifts
    to show eyes, rows of teeth and a long tongue. It hops to walk, lunges and snaps to attack and, for the special,
    lashes its tongue out to drag its prey in."""
    back = direction == "back"
    lid, hop, lean, tongue, mode, eyes = _MIMIC_POSES[p.anim][p.k]
    if not lid and not hop and not lean:
        c = _chest_import(back)
        if p.anim == "attack" and p.k == 3:
            _chomp(c, 32, 22)
        return c
    c = Canvas(40, 40, SLOTS)
    M = _Mirror(20, back)
    ox, oy = _MIMIC_AT[0] + lean, _MIMIC_AT[1] + hop

    def L(x: float, y: float) -> tuple[float, float]:
        return M.p(ox + x, oy + y)

    from .props import iso_box
    iso_box(c, int(M.x(ox + 13)), int(oy + 8), 11, 8, "wood")
    for bx_ in (5, 21):                                                 # metal bands
        c.line(*L(bx_, 13), *L(bx_, 21), "metal", 3)
    c.line(*L(13, 19), *L(13, 25), "metal", 2)
    front = [L(2, 13.5), L(13, 19), L(24, 13.5)]
    hinge = [L(2, 13.5), L(13, 8), L(24, 13.5)]
    maw = poly(c, [hinge[0], hinge[1], hinge[2], front[1]])
    if not back:
        flat(c, maw, "mouth", 1)
        flat(c, maw & shift(maw, 0, 2) & ~shift(maw, 0, -3), "mouth", 0)       # the throat, deep at the back
        for x in range(3, 24, 3):                                           # lower teeth along the rim
            tx, ty = L(x, 13.5 + (5.5 - abs(13 - x) * 0.5))
            c.polygon([(tx - 1, ty + 0.5), (tx + 1, ty + 0.5), (tx, ty - 2)], "horn", 5 if x < 13 else 4)
    c.rect(*L(12, 20), *L(14, 22), "gold", 4)                               # the lock, still on the body
    c.px(*L(13, 22), "hair", 0)
    # the lid swings up about its back edge; from the front we see its inside, from behind its outside
    fx, fy = L(13, 19 - 2.2 * lid)
    lid_face = poly(c, [hinge[0], (fx, fy), hinge[2], hinge[1]]) if lid > 2.5 else poly(c, [
        hinge[0], (fx, fy), hinge[2], L(13, 8 - lid)])
    dome = poly(c, [L(2, 13.5 - lid * 0.9), (fx, fy - 2), L(24, 13.5 - lid * 0.9), L(24, 11.5 - lid * 0.9),
                    (fx, fy - 4.5), L(2, 11.5 - lid * 0.9)])
    if back:
        paint(c, lid_face | dome, "wood", base=3.4, depth=2)
        c.line(*L(2, 12.5 - lid * 0.9), fx, fy - 3, "metal", 4)
        c.line(fx, fy - 3, *L(24, 12.5 - lid * 0.9), "metal", 3)
        for x in (6, 10, 16, 20):                                           # teeth peeking past the lid's edge
            tx = ox + x
            c.px(*M.p(tx, oy + 13.5 - lid * 0.9 - 2 - (1 if x in (10, 16) else 0)), "horn", 5)
    else:
        flat(c, dome, "wood", 3)
        c.line(*L(2, 12.5 - lid * 0.9), fx, fy - 3, "metal", 4)
        c.line(fx, fy - 3, *L(24, 12.5 - lid * 0.9), "metal", 3)
        if lid > 2.5:
            flat(c, lid_face, "wood", 1)
            flat(c, lid_face & ~shift(lid_face, 0, 1), "wood", 2)
            for t in np.linspace(0.08, 0.92, 7):                           # upper teeth along the lid's front edge
                if t < 0.5:
                    x0, y0 = hinge[0][0] + (fx - hinge[0][0]) * t * 2, hinge[0][1] + (fy - hinge[0][1]) * t * 2
                else:
                    x0, y0 = fx + (hinge[2][0] - fx) * (t - 0.5) * 2, fy + (hinge[2][1] - fy) * (t - 0.5) * 2
                c.polygon([(x0 - 1, y0 - 0.5), (x0 + 1, y0 - 0.5), (x0, y0 + 2.5)], "horn", 5)
            if eyes:
                ey = (hinge[1][1] + fy) / 2 - 1
                for ex in (-3, 2):
                    c.rect(fx + ex, ey, fx + ex + 1, ey, "eyes", 5)
                    c.px(fx + ex + 1, ey, "eyes", 2)
        elif eyes:                                                          # peeking out of a crack
            c.line(*L(4, 13.5 - lid * 0.5), *L(22, 13.5 - lid * 0.5), "mouth", 0)
            c.px(*L(10, 13), "eyes", 5)
            c.px(*L(15, 13), "eyes", 5)
        if tongue:
            _tongue(c, L, M, tongue, mode, p.k)
    if p.anim == "walk" and p.k in (0, 3):                                   # dust kicked up at take-off
        for dx in (-12, 12):
            c.px(*M.p(20 + dx, 37), "fx", 3)
    return c


def _tongue(c: Canvas, L, M: _Mirror, length: float, mode: str, k: int) -> None:
    sx, sy = L(13, 15)
    if mode == "limp":                                                       # lolling out over the front
        pts = [(sx, sy), (sx + M.s * 2, sy + 4), (sx + M.s * 3, sy + 4 + length * 0.6), (sx + M.s * 5, sy + 4 +
                                                                                           length * 0.8)]
    elif mode == "coil":
        pts = [(sx, sy), (sx + M.s * 4, sy - 3), (sx + M.s * 6, sy + 1), (sx + M.s * 3, sy + 3)]
    elif mode == "lick":
        pts = [(sx, sy), (sx + M.s * length * 0.5, sy + 1), (sx + M.s * length, sy - 1)]
    else:                                                                    # lashed out at the target
        pts = [(sx, sy), (sx + M.s * length * 0.35, sy - 2), (sx + M.s * length * 0.7, sy + 1),
               (sx + M.s * length, sy - 1)]
    paint(c, tube(c, pts, [2.2] + [1.8] * (len(pts) - 2) + [1.4]), "hide2", base=3.4, depth=1)
    c.line(*pts[0], *pts[1], "hide2", 2)                                     # the centre groove
    if mode == "out":
        tx, ty = pts[-1]
        c.polygon([(tx, ty - 2), (tx + M.s * 3, ty - 1), (tx, ty + 2)], "hide2", 4)      # forked tip


def _chomp(c: Canvas, x: float, y: float) -> None:
    for a in range(0, 360, 60):
        r = math.radians(a)
        c.line(x + math.cos(r) * 2, y + math.sin(r) * 2, x + math.cos(r) * 5, y + math.sin(r) * 5, "fx", 5)


# ------------------------------------------------------------------------------------------------ companions
def imp(direction: str, p: Pose) -> Canvas:
    """The Fiendbound warlock's imp: a big-headed little devil on beating bat wings, horns, a grin and a spade-tipped
    tail, with its pact collar in the owner's colour. The special conjures a fire mote between its claws and
    throws it."""
    c = Canvas(32, 32, SLOTS)
    back = direction == "back"
    M = _Mirror(16, back)
    gy = 30
    beat = (1, 0.2, -0.9, 0.2)[p.k % 4] if p.anim in ("idle", "walk") else p.wing
    if p.fall >= 1:
        beat = -1
    cx = 15 + p.lunge * 0.6 + p.tilt
    cy = 16 + (math.sin(p.phase * math.tau) * 1.2 if p.anim == "idle" else p.bob) + p.fall * 8
    if p.fall >= 1:
        cy = gy - 6
    # wings (behind, or over the back when seen from behind)
    if not back:
        for side, base in ((-1, 2.4), (1, 2.9)):
            _wing(c, M.x(cx + side * 2), cy - 3, beat, side * M.s, base, span=0.36, elev=40)
    # tail
    sw = math.sin(p.phase * math.tau + 1) * 2 if p.anim in ("idle", "walk") else -p.lunge * 0.5
    tail = M.pts([(cx - 1, cy + 4), (cx - 5, cy + 7), (cx - 8 + sw * 0.4, cy + 5), (cx - 9 + sw, cy + 1)])
    paint(c, tube(c, tail, [1.2, 1, 0.8, 0.7]), "hide", base=2.6, depth=1)
    tx, ty = tail[-1]
    c.polygon([(tx - 1.5, ty + 0.5), (tx + 1.5, ty + 0.5), (tx, ty - 2.5)], "hide", 3)
    # legs dangling, arms
    for side in (-1, 1):
        lx = cx + side * 1.8
        dangle = math.sin(p.phase * math.tau + side) * 0.8 if p.anim == "idle" else -p.lunge * 0.3
        c.thick_line(*M.p(lx, cy + 5), *M.p(lx + side * 0.5 + dangle, cy + 9), 1, "hide", 2 if side < 0 else 3)
        c.px(*M.p(lx + side * 0.5 + dangle + side, cy + 9), "horn", 2)
    paint(c, ell(c, *M.p(cx, cy + 2), 3.5, 4) | ell(c, *M.p(cx, cy - 1), 3, 2.5), "hide", base=3.0, depth=2)
    if not back:
        recolour(c, ell(c, *M.p(cx + 0.5, cy + 3), 2, 2.5) & c.mask("hide"), "hide2", 0)       # belly
    fire = p.anim == "special"
    if p.anim == "attack":
        hand = [(cx + 4, cy - 3), (cx + 3, cy - 6), (cx + 8, cy + 2), (cx + 8, cy + 3), (cx + 6, cy + 1)][p.k]
    elif fire and p.charge:
        hand = (cx + 4, cy - 1 - p.charge * 2)
    elif fire and p.burst:
        hand = (cx + 7, cy - 1)
    else:
        hand = (cx + 4, cy + 2 + math.sin(p.phase * math.tau) * 0.6)
    other = (hand[0] - 7, hand[1]) if fire and p.charge else (cx - 4, cy + 2)
    for sh_, hd in (((cx - 2.5, cy - 1), other), ((cx + 2.5, cy - 1), hand)):
        c.line(*M.p(*sh_), *M.p(*hd), "hide", 3)
        c.px(*M.p(hd[0] + 0.5, hd[1] + 1), "horn", 4)
    # head: big, with horns, ears and a grin
    hx, hy = cx + 0.5 + p.head[0] * 0.3, cy - 6 + p.head[1] * 0.3
    for side in (-1, 1):
        c.polygon(M.pts([(hx + side * 4, hy - 1), (hx + side * 8, hy - 3), (hx + side * 4, hy + 1)]), "hide",
                  3 if side < 0 else 2)
        _horn(c, M.pts(curve((hx + side * 2, hy - 3), (hx + side * 3, hy - 6), (hx + side * 1.5, hy - 8), 4)),
              [1.2, 1, 0.8, 0.6, 0.4], 2.8)
    paint(c, ell(c, *M.p(hx, hy), 4.6, 4), "hide", base=3.1, depth=2)
    flat(c, ell(c, *M.p(cx, cy - 2.2), 3.2, 1) & ~ell(c, *M.p(hx, hy), 4.6, 4), "accent", 4)     # pact collar
    c.px(*M.p(cx + 1, cy - 1.5), "glow", 5)
    if not back:
        if p.hurt or p.fall:
            c.line(*M.p(hx - 2, hy - 1), *M.p(hx - 1, hy), "eyes", 1)
            c.line(*M.p(hx + 2, hy - 1), *M.p(hx + 3, hy), "eyes", 1)
        else:
            for ex in (hx - 2, hx + 2):
                c.px(*M.p(ex, hy - 1), "eyes", 5)
                c.px(*M.p(ex, hy), "eyes", 3)
        c.line(*M.p(hx - 2, hy + 2), *M.p(hx + 2, hy + 2), "mouth", 1)     # the grin
        c.px(*M.p(hx - 2, hy + 1), "mouth", 1)
        c.px(*M.p(hx + 2, hy + 1), "mouth", 1)
        c.px(*M.p(hx + 1, hy + 3), "horn", 5)
    else:
        for side in (-1, 1):
            _wing(c, M.x(cx + side * 2), cy - 3, beat, side * M.s, 2.8 if side < 0 else 2.4, span=0.36, elev=40)
    if fire and p.charge:
        fx, fy = M.p(hand[0] - 3.5, hand[1] - 1)
        rad = 1 + p.charge * 2
        glow_fill(c, ell(c, fx, fy, rad, rad), "glow", lo=3, hi=5, step=0.8)
    if fire and p.burst:
        bx_, by_ = M.p(cx + 9 + p.burst * 6, cy - 1)
        glow_fill(c, ell(c, bx_, by_, 2.5, 2.5), "glow", lo=3, hi=5, step=0.8)
        for k in range(3):
            c.px(bx_ - M.s * (3 + k * 2), by_ + (k % 2), "glow", 4 - k)
    if p.anim == "attack" and p.k in (2, 3):
        for k in range(3):
            c.line(*M.p(cx + 7 + k, cy - 2 + k), *M.p(cx + 11 + k, cy + 3 + k), "fx", 5 - k)
    return c


_POUNCE = [(0, 3, -1, -0.3, False), (0, 4, -2, -0.6, False), (2, -3, -1, 0.5, True), (6, -6, 0, 0.8, True),
           (7, 1, 2, 0.2, True), (3, 0, 0, 0, False)]          # lunge, bob, head dy, stretch, jaw


def hound(direction: str, p: Pose) -> Canvas:
    """The Beastmaster ranger's hound: smaller and leaner than the wolf, with floppy ears, a wagging tail and a
    bandana in the owner's colour. Its idle pants and wags; the special is a pounce."""
    c = Canvas(40, 40, SLOTS)
    gy = 37
    lunge, bob, hdy, stretch, jaw = p.lunge, p.bob, p.head[1], 0.0, p.jaw
    if p.anim == "special":
        lunge, bob, hdy, stretch, jaw = _POUNCE[p.k]
    legs, lift = p.legs, p.lift
    if p.anim == "special" and p.k in (2, 3):
        legs, lift = (5, -5, 4, -4), (4, 3, 4, 3)
    cx = 18 + lunge * 0.7
    by = gy - 13 + bob * (0.7 if p.anim != "special" else 1) + p.fall * 6
    shoulder, hip = (cx + 5, by + 2), (cx - 5, by + 2)

    def leg(base, sw, li, far):
        bx_, by_ = base
        fx, fy = bx_ + sw + (-1 if far else 1), gy - 1 - li - (1 if far else 0)
        if p.fall:
            fx, fy = bx_ + 3 + (0 if far else 2), min(fy, by_ + 4)
        kx, ky = (bx_ + fx) / 2 + sw * 0.2, (by_ + fy) / 2
        sh = 2 if far else 3
        c.thick_line(bx_, by_, kx, ky, 2, "hide", sh)
        c.line(kx, ky, fx, fy, "hide", sh - 1 if far else sh)
        c.line(fx - 1, fy, fx + 1, fy, "hide", sh - 1)

    leg(shoulder, legs[2], lift[2], True)
    leg(hip, legs[3], lift[3], True)
    wag = (math.sin(p.phase * math.tau * 2) * 3 if p.anim == "idle" else math.sin(p.phase * math.tau) * 1.5)
    if p.hurt or p.fall:
        wag = -4
    tail = [(cx - 8, by - 1), (cx - 11, by - 4 + wag * 0.3), (cx - 12 + wag * 0.4, by - 8 + abs(wag) * 0.3)]
    paint(c, tube(c, tail, [1.6, 1.4, 1]), "hide", base=3.0, depth=1)
    c.px(*tail[-1], "hide2", 4)                                              # pale tail tip
    body = ell(c, cx - 1, by, 8 + stretch * 2, 4) | ell(c, cx + 4, by + 0.5, 4.5, 5)
    paint(c, body, "hide", base=3.0, depth=3)
    recolour(c, body & ~shift(body, 0, -2) & (_grid(c)[1] > cx - 3), "hide2", 1)   # pale belly and chest
    leg(shoulder, legs[0], lift[0], False)
    leg(hip, legs[1], lift[1], False)
    hx, hy = cx + 9 + p.head[0] * 0.5 + stretch * 2, by - 6 + hdy + (2 if p.fall else 0)
    c.thick_line(cx + 5, by - 2, hx - 1, hy + 1, 4, "hide", 3)              # neck
    paint(c, ell(c, hx, hy, 3.6, 3.2), "hide", base=3.1, depth=2)
    paint(c, ell(c, hx + 3.5, hy + 1, 2.6, 1.6), "hide2", base=3.2, depth=1)  # muzzle
    c.px(hx + 5.5, hy + 0.5, "mouth", 0)                                     # nose
    pant = p.anim == "idle" and p.k % 2 == 1 or p.anim == "walk"
    if jaw:
        c.line(hx + 2, hy + 3, hx + 5, hy + 3, "mouth", 1)
        c.px(hx + 4, hy + 2.5, "horn", 5)
    elif pant and not p.hurt:
        c.line(hx + 2, hy + 2.5, hx + 3, hy + 3.5, "mouth", 3)              # tongue out
        c.px(hx + 3, hy + 4, "mouth", 4)
    else:
        c.line(hx + 2, hy + 2, hx + 4, hy + 2, "hide2", 1)
    ear = poly(c, [(hx - 2, hy - 3), (hx + 0.5, hy - 3), (hx - 1, hy + 3 - (1 if p.anim == "special" else 0)),
                   (hx - 3, hy + 2)])
    paint(c, ear, "hide", base=2.4, depth=1)                                  # the floppy ear
    if p.hurt or p.fall:
        c.line(hx + 0.5, hy - 1, hx + 1.5, hy - 1, "eyes", 0)
    else:
        c.px(hx + 1, hy - 1, "eyes", 1)
        c.px(hx + 1, hy - 2, "eyes", 4)
    nk = poly(c, [(cx + 5, by - 3), (cx + 8, by - 5), (cx + 8, by - 2), (cx + 6, by + 2), (cx + 4, by - 1)])
    paint(c, nk & c.mask(), "accent", base=3.0, depth=1)                       # bandana
    c.polygon([(cx + 5, by), (cx + 7, by), (cx + 6, by + 3)], "accent", 2)
    leaf_texture(c, "hide", 17 + p.k, density=0.08)
    return c.mirrored() if direction == "back" else c


def sapling(direction: str, p: Pose) -> Canvas:
    """The Grovekeeper druid's treant sapling: a knee-high walking tree on root feet, twig arms and a leafy crown,
    with a ribbon in the owner's colour tied round its trunk. The special blooms: flowers open in its crown and
    healing motes drift up."""
    c = Canvas(40, 40, SLOTS)
    back = direction == "back"
    M = _Mirror(20, back)
    gy = 37
    seed = ORDER.index(p.anim) * 10 + p.k
    r = _rng(83, seed)
    sway = math.sin(p.phase * math.tau) * 1.2 if p.anim == "idle" else p.legs[0] * 0.3
    fall = p.fall
    if fall >= 1:
        paint(c, tube(c, M.pts([(9, gy - 3), (24, gy - 4)]), [3, 4]), "wood", base=2.8, depth=2)
        for (x, y, rr) in ((28, gy - 6, 5), (32, gy - 3, 4), (25, gy - 2, 3)):
            paint(c, ell(c, *M.p(x, y), rr, rr * 0.8), "hide", base=2.6, k=2.0, depth=3)
        c.line(*M.p(20, gy - 5), *M.p(21, gy - 5), "eyes", 1)
        return c
    cx = 20 + p.lunge * 0.5 + p.tilt
    by = gy - 6 + p.bob * 0.6 + fall * 4
    tilt = fall * 4 + (-1.5 if p.hurt else 0)
    for side, sw, li in ((-1, p.legs[0], p.lift[0]), (1, p.legs[1], p.lift[1])):   # root feet
        fx = cx + side * 4 + sw * 0.6
        paint(c, tube(c, M.pts([(cx + side * 1.5, by - 2), (fx, gy - 1 - li)]), [2.4, 2]), "wood", base=2.7,
              depth=1)
        c.line(*M.p(fx - 2, gy - li), *M.p(fx + 2, gy - li), "wood", 2)
        c.px(*M.p(fx + side * 3, gy - li), "wood", 1)
    trunk = tube(c, M.pts([(cx, by), (cx + tilt * 0.4, by - 10), (cx + tilt, by - 17)]), [4.6, 4, 3.4])
    paint(c, trunk, "wood", base=3.0, depth=2)
    for k in (-2, 0, 2):                                                       # bark grain
        c.line(*M.p(cx + k, by - 1), *M.p(cx + k + tilt * 0.3, by - 7 - abs(k)), "wood", 2)
    # twig arms
    if p.anim == "attack":
        hand = [(cx + 6, by - 15), (cx + 4, by - 18), (cx + 11, by - 8), (cx + 11, by - 6), (cx + 8, by - 9)][p.k]
    elif p.anim == "special" and (p.charge or p.burst):
        hand = (cx + 6, by - 18)
    else:
        hand = (cx + 7, by - 9 + sway * 0.5)
    other = (cx - 6, by - 18) if p.anim == "special" and (p.charge or p.burst) else (cx - 7, by - 9 - sway * 0.5)
    for hd, sh_ in ((other, 2), (hand, 3)):
        x0, y0 = cx + (1 if hd[0] > cx else -1) * 2.5, by - 11
        c.thick_line(*M.p(x0, y0), *M.p(*hd), 1, "wood", sh_)
        c.line(*M.p(*hd), *M.p(hd[0] + (1 if hd[0] > cx else -1) * 2, hd[1] - 2), "wood", sh_)
        c.px(*M.p(hd[0], hd[1] - 1), "hide", 4)
    if p.anim == "attack" and p.k in (2, 3):
        c.line(*M.p(hand[0] + 1, hand[1] - 5), *M.p(hand[0] + 5, hand[1] + 1), "fx", 5)   # whip of the vine
        c.line(*M.p(hand[0] + 3, hand[1] - 6), *M.p(hand[0] + 7, hand[1]), "fx", 3)
    # ribbon
    rb = trunk & (np.abs(_grid(c)[0] - (by - 4)) < 1.1)
    flat(c, rb, "accent", 3)
    flat(c, rb & ~shift(rb, 1, 0), "accent", 4)
    kx, ky = M.p(cx + 3, by - 4)
    c.line(kx, ky, kx + M.s * 2, ky + 3 + sway * 0.4, "accent", 2)
    c.line(kx, ky, kx + M.s * 1, ky + 4, "accent", 3)
    if not back:                                                               # a face in the bark
        fy = by - 12
        if p.hurt:
            c.line(*M.p(cx - 2, fy), *M.p(cx - 1, fy), "eyes", 1)
            c.line(*M.p(cx + 1, fy), *M.p(cx + 2, fy), "eyes", 1)
        else:
            blink = p.anim == "idle" and p.k == 3
            for ex in (cx - 2, cx + 1.5):
                c.px(*M.p(ex, fy), "eyes", 1 if blink else 5)
                if not blink:
                    c.px(*M.p(ex, fy + 1), "eyes", 3)
        c.line(*M.p(cx - 1, fy + 3), *M.p(cx + 1, fy + 3), "wood", 1)
    # crown: three leaf clumps swaying, a lighter top
    tx, ty = cx + tilt, by - 19
    for (dx, dy, rx, ry, base) in ((-5, 1, 5, 4, 2.6), (5, 1, 5, 4, 2.6), (0, -3, 6.5, 5, 3.1)):
        paint(c, ell(c, *M.p(tx + dx + sway * (1 if dy < 0 else 0.6), ty + dy), rx, ry), "hide", base=base, k=2.0,
              depth=3)
    leaf_texture(c, "hide", 29 + p.k, density=0.22)
    bloom = p.anim == "special" and (p.burst or p.charge > 0.6)
    if bloom:
        for i in range(int(3 + 4 * max(p.burst, p.charge * 0.5))):           # flowers opening in the crown
            fx_, fy_ = M.p(tx + r.uniform(-8, 8), ty + r.uniform(-6, 2))
            c.px(fx_, fy_, "glow", 5)
            for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                c.px(fx_ + ddx, fy_ + ddy, "glow", 3)
    if p.anim == "special":
        for i in range(int(2 + 6 * (p.burst or p.charge))):                    # healing motes drifting up
            mx = tx + r.uniform(-12, 12)
            my = ty + 10 - r.uniform(0, 22) * (0.4 + p.burst)
            c.px(*M.p(mx, my), "fx", int(r.integers(3, 6)))
            if i % 2:
                c.px(*M.p(mx, my - 1), "fx", 3)
    if p.hurt and p.k == 0:
        for i in range(3):                                                      # leaves knocked loose
            c.px(*M.p(tx + 9 + i * 2, ty + 4 + i * 4), "hide", 3)
    return c


# --------------------------------------------------------------------------------------------------- catalogue
BOSS_RAMPS = {
    "wing_red": ramp("#7e2d3a", shift=0.07), "wing_green": ramp("#2f6450", shift=0.07),
    "wing_blue": ramp("#2c4683", shift=0.06), "wing_black": ramp("#2c2a36", shift=0.05, spread=1.2),
    "wing_gold": ramp("#a8743e", shift=0.07), "belly_black": ramp("#6e6a7c", shift=0.05),
    "belly_gold": ramp("#efd9a0", shift=0.05, spread=0.8),
    "acid": ramp("#b6e04a", shift=0.06, spread=0.85), "lightning": ramp("#a6dcff", shift=0.04, spread=0.75),
    "mouth": ramp("#8a2a36", shift=0.06), "ivory": ramp("#e3d8bd", shift=0.05, spread=0.85),
    "troll": ramp("#7d8f6e", shift=0.07), "troll_belly": ramp("#a3ad86", shift=0.06),
    "troll_hair": ramp("#3b4a3c", shift=0.05, spread=1.2), "ogre": ramp("#c0946a", shift=0.06),
    "ogre_belly": ramp("#d8b48a", shift=0.05), "frost": ramp("#a9c4dc", shift=0.05, spread=0.9),
    "frost_belly": ramp("#cfe0ee", shift=0.05, spread=0.8), "dust": ramp("#c9b28a", shift=0.05, spread=0.8),
    "snow_dust": ramp("#dbe8f4", shift=0.04, spread=0.7),
    "flame": ramp("#ff9a3d", shift=0.09, spread=1.1), "flame_core": ramp("#ffd77a", shift=0.05, spread=0.6),
    "azure": ramp("#4fa8ff", shift=0.05, spread=1.1), "azure_core": ramp("#c9f0ff", shift=0.04, spread=0.6),
    "char": ramp("#3a2228", shift=0.04, spread=1.2), "foam": ramp("#e4f4fa", shift=0.04, spread=0.6),
    "gale": ramp("#bfe0ee", shift=0.04, spread=0.8), "storm": ramp("#6f7f9c", shift=0.05, spread=1.1),
    "amber": ramp("#f0a93a", shift=0.06, spread=0.9),
    "wraith": ramp("#3a3a4c", shift=0.05, spread=1.2), "spectre": ramp("#9fb4c9", shift=0.05, spread=0.9),
    "shade": ramp("#4a3866", shift=0.06, spread=1.1), "void": ramp("#1e1a2a", shift=0.04, spread=1.3),
    "ghost_green": ramp("#7fe0a0", shift=0.05, spread=0.8), "ghost_blue": ramp("#a6e4ff", shift=0.04, spread=0.7),
    "tongue": ramp("#c4506a", shift=0.06), "maw": ramp("#5a1f2c", shift=0.05, spread=1.2),
    "imp_red": ramp("#c2463e", shift=0.08), "imp_belly": ramp("#e08a6a", shift=0.06),
    "imp_violet": ramp("#7a52a8", shift=0.07), "imp_violet_belly": ramp("#b08ad0", shift=0.06),
    "imp_wing": ramp("#5a2a3a", shift=0.06), "imp_wing_violet": ramp("#3a2a52", shift=0.06),
    "hound_tan": ramp("#b98a55", shift=0.06), "hound_grey": ramp("#8a8f9c", shift=0.05),
    "hound_black": ramp("#3a3640", shift=0.05, spread=1.2), "cream": ramp("#e3d3b0", shift=0.05, spread=0.8),
    "nose": ramp("#3a2a30", shift=0.04, spread=1.2), "ally": ramp("#e6bd5c", shift=0.06),
    "heal": ramp("#9fef8a", shift=0.05, spread=0.8),
}

PLAYER_ACCENTS = {f"p{i + 1}": ramp(h, shift=0.06) for i, h in enumerate(
    ("#ff8a3d", "#3ec7ff", "#c08cff", "#5fe08a", "#ffd447", "#ff7fb6"))}
"""The six player colours (STYLE.md); a companion's `accent` slot takes its owner's."""


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "boss":
        return BOSS_RAMPS[name]
    if fam == "beast":
        return B.BEAST_RAMPS[name]
    return FAMILIES[fam][name]


@dataclass
class Monster:
    id: str
    name: str
    draw: Callable[[str, Pose], Canvas]
    size: tuple[int, int]
    anchor: tuple[int, int]
    variants: dict[str, dict[str, str]]
    role: str = "monster"               # boss | elite | monster | companion
    footprint: tuple[int, int] = (1, 1)
    flying: bool = False
    tags: list[str] = field(default_factory=list)
    summoner: str | None = None         # companions: the subclass that brings them (class/subclass)


def _v(**slots: str) -> dict[str, str]:
    return slots


def _dragon_v(hide: str, belly: str, wing: str, glow: str, eyes: str = "eyes/gold", horn: str = "boss/ivory"):
    return _v(hide=hide, hide2=belly, wing=wing, glow=glow, eyes=eyes, horn=horn, mouth="boss/mouth",
              fx="magic/holy")


CATALOGUE: list[Monster] = [
    Monster("dragon", "Dragon", dragon, (96, 96), (48, 90), {
        "red": _dragon_v("skin/dragon_red", "skin/dragon_gold", "boss/wing_red", "magic/fire"),
        "green": _dragon_v("skin/dragon_green", "skin/dragon_brass", "boss/wing_green", "boss/acid", "eyes/red"),
        "blue": _dragon_v("skin/dragon_blue", "skin/dragon_silver", "boss/wing_blue", "boss/lightning"),
        "black": _dragon_v("skin/dragon_black", "boss/belly_black", "boss/wing_black", "boss/acid", "eyes/green",
                           "horn/bone"),
        "gold": _dragon_v("skin/dragon_gold", "boss/belly_gold", "boss/wing_gold", "magic/fire", "eyes/red")},
        "boss", (2, 2), tags=["dragon", "boss", "huge"]),
    Monster("troll", "Troll", troll, (64, 64), (32, 60), {
        "troll": _v(hide="boss/troll", hide2="boss/troll_belly", hair="boss/troll_hair", eyes="eyes/amber",
                    cloth="leather/brown", wood="wood/dark", metal="metal/iron", horn="boss/ivory",
                    mouth="boss/mouth", stone="terrain/stone", fx="boss/dust"),
        "ogre": _v(hide="boss/ogre", hide2="boss/ogre_belly", hair="hair/brown", eyes="eyes/red",
                   cloth="cloth/rust", wood="wood/oak", metal="metal/iron", horn="boss/ivory", mouth="boss/mouth",
                   stone="terrain/stone_warm", fx="boss/dust"),
        "frost": _v(hide="boss/frost", hide2="boss/frost_belly", hair="hair/white", eyes="eyes/blue",
                    cloth="cloth/slate", wood="wood/pale", metal="metal/steel", horn="boss/ivory",
                    mouth="boss/mouth", stone="terrain/ice", fx="boss/snow_dust")},
        "elite", tags=["giant", "large"]),
    Monster("golem", "Stone Golem", golem, (64, 64), (32, 60), {
        "granite": _v(stone="terrain/stone", hide2="terrain/moss", glow="magic/arcane", eyes="magic/arcane",
                      fx="magic/arcane"),
        "basalt": _v(stone="terrain/stone_dark", hide2="terrain/crust", glow="magic/fire", eyes="magic/fire",
                     fx="magic/fire"),
        "temple": _v(stone="terrain/stone_warm", hide2="terrain/moss", glow="magic/holy", eyes="magic/holy",
                     fx="magic/holy")},
        "elite", tags=["construct", "large"]),
    Monster("fire_elemental", "Fire Elemental", fire_elemental, (64, 64), (32, 60), {
        "ember": _v(hide="boss/flame", hide2="boss/flame_core", eyes="boss/char", fx="magic/fire"),
        "azure": _v(hide="boss/azure", hide2="boss/azure_core", eyes="boss/char", fx="magic/frost")},
        "elite", tags=["elemental", "fire", "large"]),
    Monster("water_elemental", "Water Elemental", water_elemental, (64, 64), (32, 60), {
        "sea": _v(hide="terrain/water", hide2="boss/foam", eyes="magic/frost", fx="boss/foam"),
        "swamp": _v(hide="terrain/water_swamp", hide2="terrain/grass_dry", eyes="eyes/gold", fx="boss/foam")},
        "elite", tags=["elemental", "water", "large"]),
    Monster("earth_elemental", "Earth Elemental", earth_elemental, (64, 64), (32, 60), {
        "stone": _v(stone="terrain/stone_warm", hide2="terrain/moss", glow="beast/slime_blue", eyes="boss/amber",
                    fx="boss/dust"),
        "magma": _v(stone="terrain/crust", hide2="terrain/stone_dark", glow="terrain/lava", eyes="terrain/lava",
                    fx="boss/dust")},
        "elite", tags=["elemental", "earth", "large"]),
    Monster("air_elemental", "Air Elemental", air_elemental, (64, 64), (32, 60), {
        "gale": _v(hide="boss/gale", hide2="foliage/leaf", eyes="magic/holy", fx="boss/gale"),
        "storm": _v(hide="boss/storm", hide2="terrain/stone_dark", eyes="boss/lightning", fx="boss/lightning")},
        "elite", flying=True, tags=["elemental", "air", "large"]),
    Monster("wraith", "Wraith", wraith, (48, 48), (24, 44), {
        "wraith": _v(cloth="boss/wraith", mouth="boss/void", eyes="boss/ghost_green", fx="boss/ghost_green",
                     horn="horn/bone"),
        "spectre": _v(cloth="boss/spectre", mouth="boss/void", eyes="boss/ghost_blue", fx="boss/ghost_blue",
                      horn="horn/bone"),
        "shade": _v(cloth="boss/shade", mouth="boss/void", eyes="magic/blood", fx="magic/shadow",
                    horn="horn/obsidian")},
        flying=True, tags=["undead", "incorporeal"]),
    Monster("mimic", "Mimic", mimic, (40, 40), (20, 37), {
        "oak": _v(wood="wood/oak", metal="metal/iron", gold="metal/gold", hair="cloth/charcoal", mouth="boss/maw",
                  hide2="boss/tongue", horn="boss/ivory", eyes="eyes/gold", fx="boss/dust"),
        "ironbound": _v(wood="wood/dark", metal="metal/steel", gold="metal/silver", hair="cloth/charcoal",
                        mouth="boss/maw", hide2="boss/tongue", horn="boss/ivory", eyes="eyes/red", fx="boss/dust")},
        tags=["aberration", "ambush", "small"]),
    Monster("imp", "Imp", imp, (32, 32), (16, 30), {
        "crimson": _v(hide="boss/imp_red", hide2="boss/imp_belly", wing="boss/imp_wing", horn="horn/obsidian",
                      eyes="eyes/gold", mouth="boss/maw", glow="magic/fire", accent="boss/ally", fx="magic/fire"),
        "violet": _v(hide="boss/imp_violet", hide2="boss/imp_violet_belly", wing="boss/imp_wing_violet",
                     horn="horn/ivory", eyes="eyes/gold", mouth="boss/maw", glow="magic/shadow", accent="boss/ally",
                     fx="magic/shadow")},
        "companion", flying=True, tags=["fiend", "summon", "small"], summoner="warlock/fiendbound"),
    Monster("hound", "Hound", hound, (40, 40), (20, 37), {
        "tan": _v(hide="boss/hound_tan", hide2="boss/cream", eyes="eyes/dark", mouth="boss/tongue",
                  horn="boss/ivory", accent="boss/ally"),
        "grey": _v(hide="boss/hound_grey", hide2="beast/belly_grey", eyes="eyes/dark", mouth="boss/tongue",
                   horn="boss/ivory", accent="boss/ally"),
        "black": _v(hide="boss/hound_black", hide2="boss/hound_tan", eyes="eyes/amber", mouth="boss/tongue",
                    horn="boss/ivory", accent="boss/ally")},
        "companion", tags=["beast", "companion"], summoner="ranger/beastmaster"),
    Monster("sapling", "Treant Sapling", sapling, (40, 40), (20, 37), {
        "spring": _v(wood="foliage/bark", hide="foliage/leaf", eyes="magic/nature", glow="terrain/flower_white",
                     accent="boss/ally", fx="boss/heal"),
        "autumn": _v(wood="foliage/bark", hide="foliage/leaf_autumn", eyes="magic/holy", glow="terrain/flower_red",
                     accent="boss/ally", fx="boss/heal"),
        "blossom": _v(wood="foliage/bark_pale", hide="foliage/blossom", eyes="magic/nature",
                      glow="terrain/flower_white", accent="boss/ally", fx="boss/heal")},
        "companion", tags=["plant", "summon"], summoner="druid/grovekeeper"),
]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {s: _ref(r) for s, r in variant.items()}
    for name in SLOTS.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


_CANVAS_CACHE: dict[tuple[str, str, int], Canvas] = {}


def canvases(m: Monster) -> list[Canvas]:
    """Both facings' frames, outlined, in sheet order (front row, then back row). Drawn once per monster; every
    palette variant renders from the same canvases."""
    out = []
    for d in ("front", "back"):
        for i, (_a, _k, pose) in enumerate(frame_poses()):
            key = (m.id, d, i)
            if key not in _CANVAS_CACHE:
                cv = m.draw(d, pose)
                cv.outline(skip=FX_SLOTS)
                _CANVAS_CACHE[key] = cv
            out.append(_CANVAS_CACHE[key])
    return out


_FRAME_CACHE: dict[tuple, list[Image.Image]] = {}


def frames(m: Monster, variant: str, accent: list[str] | None = None) -> list[Image.Image]:
    key = (m.id, variant, tuple(accent or ()))
    if key not in _FRAME_CACHE:
        pal = ramps(m.variants[variant])
        if accent:
            pal["accent"] = accent
        _FRAME_CACHE[key] = [cv.render(pal) for cv in canvases(m)]
    return _FRAME_CACHE[key]


def anims_meta() -> dict:
    out, i = {}, 0
    for a in ORDER:
        fps, loop, count = ANIMS[a]
        out[a] = {"frames": list(range(i, i + count)), "fps": fps, "loop": loop}
        i += count
    return out


def _sheet(m: Monster, fr: list[Image.Image]) -> Image.Image:
    n = len(fr) // 2
    sh = Image.new("RGBA", (m.size[0] * n, m.size[1] * 2))
    for i, im in enumerate(fr):
        sh.paste(im, ((i % n) * m.size[0], (i // n) * m.size[1]))
    return sh


def _anim_grid(fr: list[Image.Image], gap: int = 3) -> Image.Image:
    """One monster's facing laid out one animation per row, so each sequence reads left to right."""
    w, h = fr[0].size
    cols = max(ANIMS[a][2] for a in ORDER)
    out = Image.new("RGBA", (cols * (w + gap) - gap, len(ORDER) * (h + gap) - gap))
    for r, (a, idx) in enumerate((a, v["frames"]) for a, v in anims_meta().items()):
        for k, i in enumerate(idx):
            out.alpha_composite(fr[i], (k * (w + gap), r * (h + gap)))
    return out


GROUPS = [("Bosses", ("boss",)), ("Big monsters", ("elite",)), ("Monsters", ("monster",)),
          ("Subclass companions", ("companion",))]


def build() -> None:
    out = PACK / "bosses"
    shutil.rmtree(out, ignore_errors=True)
    n = len(frame_poses())
    anims = anims_meta()
    entries: dict[str, dict] = {}
    for m in CATALOGUE:
        files, accents = {}, {}
        for v in m.variants:
            fr = frames(m, v)
            files[v] = f"{m.id}_{v}.png"
            save(_sheet(m, fr), out / files[v])
            if m.role == "companion":
                accents[v] = {}
                for pk, rp in PLAYER_ACCENTS.items():
                    accents[v][pk] = f"{m.id}_{v}_{pk}.png"
                    save(_sheet(m, frames(m, v, rp)), out / accents[v][pk])
        e = {"name": m.name, "role": m.role,
             "frame": {"width": m.size[0], "height": m.size[1], "anchor": list(m.anchor)},
             "footprint": list(m.footprint), "flying": m.flying, "tags": m.tags, "variants": files}
        if m.summoner:
            e["summoner"] = m.summoner
        if accents:
            e["playerAccents"] = accents
        entries[m.id] = e
    write_json({"framesPerDirection": n, "directions": {"front": {"row": 0}, "back": {"row": 1}}, "anims": anims,
                "monsters": entries,
                "notes": {"sheet": "one sheet per monster and variant: row 0 front, row 1 back; frame size per monster",
                          "anchor": "the ground contact pixel; for a 2x2 footprint, the centre of the 2x2 block",
                          "flying": "drawn hovering above the anchor; the game should not snap it to the floor",
                          "playerAccents": "companions: the same sheet with the owner's player colour (p1-p6) on "
                                           "the collar / bandana / ribbon"}}, out / "manifest.json")
    _previews(n)


def _previews(n: int) -> None:
    tiles: dict[str, list[Tile]] = {g: [] for g, _ in GROUPS}
    owners = []
    for m in CATALOGUE:
        group = next(g for g, roles in GROUPS if m.role in roles)
        for v in m.variants:
            fr = frames(m, v)
            tiles[group].append(Tile(strip([fr[0], fr[n]], 1, gap=4), m.name, f"{v} · front, back"))
        if m.role == "companion":
            v0 = next(iter(m.variants))
            owners.append(Tile(strip([frames(m, v0, rp)[0] for rp in PLAYER_ACCENTS.values()], 1, gap=3), m.name,
                               f"{v0} · p1-p6 · {m.summoner}"))
    sections = [contact_sheet(g, tiles[g], columns={"Bosses": 5, "Big monsters": 6}.get(g, 8),
                              zoom={"Bosses": 2}.get(g, 3)) for g, _ in GROUPS]
    sections.append(contact_sheet("Companions in their owner's colour", owners, columns=3, zoom=3))
    save(stack("Bosses, big monsters & companions", sections,
               subtitle="Same contract as beasts (idle, walk, attack, special, hurt, down; front and back rows), "
                        "bigger frames, palette variants. Companions take their owner's player colour."),
         PREVIEWS / "22-bosses.png")
    grids: dict[str, list[Tile]] = {g: [] for g, _ in GROUPS}
    for m in CATALOGUE:
        group = next(g for g, roles in GROUPS if m.role in roles)
        fr = frames(m, next(iter(m.variants)))
        grids[group].append(Tile(_anim_grid(fr[:n]), m.name, "front · idle, walk, attack, special, hurt, down"))
        if m.role == "boss":
            grids[group].append(Tile(_anim_grid(fr[n:]), m.name, "back facing (drawn, not mirrored)"))
    sections = [contact_sheet(g, grids[g], columns={"Bosses": 2, "Big monsters": 4}.get(g, 5), zoom=2)
                for g, _ in GROUPS]
    save(stack("Bosses · animations", sections, subtitle="One animation per row: idle 4 · walk 4 · attack 5 · "
                                                         "special 6 · hurt 2 · down 2"),
         PREVIEWS / "22-bosses-anims.png")
    _gif(n)
    _diorama()


def _gif(n: int) -> None:
    from arcpix.anim import Actor, stage_gif
    actors = []
    for m in CATALOGUE:
        v = next(iter(m.variants))
        # every actor shares one cell size, so anchors are padded to a common 100x100 stage frame
        fr = []
        for im in frames(m, v):
            pad = Image.new("RGBA", (100, 100))
            pad.alpha_composite(im, (50 - m.anchor[0], 94 - m.anchor[1]))
            fr.append(pad)
        actors.append(Actor(m.name, v, fr, (50, 94)))
    k = len(actors)
    a = anims_meta()

    def seq(name, back=False):
        return [f + (n if back else 0) for f in a[name]["frames"]]

    atk, spc = seq("attack"), seq("special")
    beats = [("Idle", [seq("idle") * 3] * k, 160), ("Walk", [seq("walk") * 3] * k, 125),
             ("Walk · away", [seq("walk", True) * 3] * k, 125),
             ("Attack", [[0, 0] + [f for f in atk for _ in range(2)] + [atk[-1]] * 3] * k, 80),
             ("Special", [[0] + [f for f in spc for _ in range(2)] + [spc[-1]] * 3] * k, 90),
             ("Hurt", [seq("hurt") * 3 + [0, 0, 0]] * k, 110), ("Down", [seq("down") * 5] * k, 140)]
    stage_gif(PREVIEWS / "anim-bosses.gif", "Bosses, big monsters & companions", actors, beats, zoom=2, columns=6,
              cell_w=100, cell_h=100)


def _diorama() -> None:
    """A boss fight in the volcano keep: the red dragon on its hoard breathes fire down at the party, with the
    party's three companions in the line and a mimic among the treasure."""
    from arcpix import iso
    from . import backgrounds as BG
    from . import dioramas as D
    mats = {
        "f": D.Mat(("flagstone", "dark"), ("ashlar_side", "grey")),
        "c": D.Mat(("cobble", "grey"), ("brick_side", None)),
        "l": D.Mat(("lava", None), ("lava", None), sink=6, side_of_top=True),
        "r": D.Mat(("rock", "dark"), ("cliff_side", "dark")),
    }
    terrain = ["rrffffffrr",
               "rffffffffr",
               "rffffffffr",
               "ffffccffff",
               "fffffffffl",
               "fffffffffl",
               "ffffffffll",
               "rfffffffll"]
    heights = ["7766666677",
               "7666666667",
               "7666666667",
               "4444554444",
               "4444444442",
               "4444444442",
               "4444444422",
               "4444444422"]
    s = D.build_scene(mats, terrain, heights, seed=4, decals={"f": (0.12, [("scorch", None), ("cracks", None),
                                                                          ("bones", None)])})

    def boss(mid: str, v: str, i: float, j: float, k: int, facing: str = "front", mirror: bool = False,
             big: bool = False) -> list[iso.Sprite]:
        m = next(x for x in CATALOGUE if x.id == mid)
        img = frames(m, v)[k + (len(frame_poses()) if facing == "back" else 0)]
        ax, ay = m.anchor
        if mirror:
            img = img.transpose(Image.FLIP_LEFT_RIGHT)
            ax = img.width - 1 - ax
        if not big:
            return [iso.Sprite(img, (ax, ay), i, j)]
        # a 2x2 unit stands on the block's centre but sorts with its front tile, so the floor never covers it
        shadow = Image.new("RGBA", (40, 12))
        sh = np.zeros((12, 40, 4), np.uint8)
        yy, xx = np.mgrid[0:12, 0:40]
        sh[((xx - 19.5) / 20) ** 2 + ((yy - 5.5) / 6) ** 2 <= 1] = (20, 12, 16, 110)
        shadow = Image.fromarray(sh, "RGBA")
        return [iso.Sprite(shadow, (20, 6 + 8), i + 1, j + 1, shadow=False),
                iso.Sprite(img, (ax, ay + 8), i + 1, j + 1, shadow=False)]

    sp = s.sprites
    sp += [D.prop("pillar", 2, 0, "dark"), D.prop("pillar", 7, 0, "dark"), D.prop("torch", 1, 2),
           D.prop("torch", 8, 1, frame=2), D.prop("chest_open", 7, 2), D.prop("chest", 1, 1),
           D.prop("crystals", 0, 0, "violet"), D.prop("crystals", 9, 0, "violet"), D.prop("rubble", 0, 6, "dark"),
           D.prop("rubble", 7, 7, "dark")]
    sp += boss("dragon", "red", 4, 1, 17, mirror=True, big=True)
    sp += boss("mimic", "oak", 8, 3, 10, mirror=True)
    sp += boss("wraith", "wraith", 2, 3, 1, mirror=True)
    sp += [D.unit("kara", 4, 5, "back"), D.unit("tamsin", 7, 5, "back", mirror=True),
           D.unit("sefa", 2, 6, "back"), D.unit("class_druid", 5, 7, "back", mirror=True)]
    sp += boss("hound", "tan", 6, 5, 1, "back", mirror=True)
    sp += boss("imp", "crimson", 1, 7, 2, "back")
    sp += boss("sapling", "spring", 4, 7, 0, "back")

    def backdrop(w: int, h: int) -> Image.Image:
        return BG.compose("ashfall", "volcano", w, h, horizon=int(h * 0.48))

    save(iso.finish(iso.render(s, backdrop=backdrop)), PREVIEWS / "22-bosses-diorama.png")
