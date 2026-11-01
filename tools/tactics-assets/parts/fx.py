"""Combat and spell effects: weapon smears, impacts, projectiles, spell bursts, clouds, ground circles and auras.

Effects are drawn like everything else, in (slot, shade) on a Canvas, so one drawing ships in several colours (a magic
circle is arcane, holy, fire, shadow or nature by swapping the palette). Most are built from analytic fields (distance
to a ring, a crescent, a tapered spike, a cluster of puffs) quantised into ramp shades rather than placed pixel by
pixel. A field gives the bright core and the hue-shifted falloff in one step, and it stays crisp because it is
thresholded, never blended. Thin shapes are sampled 2x2 per pixel and kept if any sample lands, so a one-pixel line
at any angle never breaks into dots.

Timing follows anticipation -> peak -> dissipation: a small, dim first frame, the largest and brightest frame early,
then a longer tail that thins, breaks up and darkens. Loops are built only from phases that wrap exactly (rotations
by one symmetry period, noise rolled by a whole texture width), so they never pop at the seam.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from typing import Callable

import numpy as np
from PIL import Image, ImageDraw

from arcpix import tex
from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import BG, PANEL, INK, MUTED, Tile, contact_sheet, font, stack, strip

SLOTS = SlotTable()
for _s in ("smoke", "main", "accent", "core", "metal", "wood", "feather", "flash"):
    SLOTS.id(_s)

FX_RAMPS: dict[str, list[str]] = {
    "steel": ramp("#8eaed8", shift=0.05, spread=1.0),          # sword smears: cool steel light
    "spark": ramp("#ffd873", shift=0.08, spread=0.8),          # hot impact sparks
    "smoke": ramp("#8c8798", shift=0.04, spread=0.9),
    "soot": ramp("#5a525e", shift=0.04, spread=1.1),
    "dust": ramp("#b9946a", shift=0.06, spread=0.85),
    "poof": ramp("#d6d2e4", shift=0.05, spread=0.7),
    "poison": ramp("#86b44c", shift=0.12, spread=0.95),
    "bile": ramp("#d9e86a", shift=0.06, spread=0.8),
    "miasma": ramp("#9467c2", shift=0.07, spread=0.95),
    "storm": ramp("#9ad4ff", shift=0.05, spread=0.85),
    "mist": ramp("#bccfe8", shift=0.05, spread=0.75),
    "eldritch": ramp("#b86ef2", shift=0.07, spread=0.9),
    "fel": ramp("#8fe05a", shift=0.08, spread=0.9),
    "fel_dark": ramp("#2f5a3a", shift=0.06, spread=1.2),
    "hex": ramp("#ff5cc8", shift=0.07, spread=0.85),
    "gold": ramp("#f0bc4c", shift=0.06, spread=1.0),         # saturated gold: light falling off a holy core
    "wind": ramp("#cfe2f0", shift=0.05, spread=0.7),
    "fletch": ramp("#e4dccb", shift=0.05, spread=0.75),
    "ice": ramp("#8fd6f2", shift=0.04, spread=1.0),
    "flash": ramp("#fff4dc", shift=0.04, spread=0.5),         # white-hot impact frames; shade 5 stays off pure white
    "necro": ramp("#34503a", shift=0.08, spread=1.25),        # necrotic body: green-black smoke
    "grave": ramp("#9cf06a", shift=0.06, spread=0.85),        # necrotic glow: a sickly green
    "psychic": ramp("#e070e8", shift=0.07, spread=0.9),
    "thunder": ramp("#b8d4ff", shift=0.05, spread=0.8),
    "acid": ramp("#b4dc34", shift=0.09, spread=0.95),
    "force": ramp("#7aa8ff", shift=0.06, spread=0.9),
    "radiant": ramp("#ffe9a8", shift=0.06, spread=0.75),
    "moon": ramp("#c8d4f2", shift=0.04, spread=0.75),
    "rage": ramp("#ff4a3a", shift=0.08, spread=0.95),
    "silk": ramp("#e6e4ee", shift=0.04, spread=0.6),
}
EMPTY = -9.0


def _ramp(ref: str) -> list[str]:
    fam, name = ref.split("/")
    return FX_RAMPS[name] if fam == "fx" else FAMILIES[fam][name]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    """A variant's ramps, plus the shared white-hot `flash` and dark `smoke` unless the variant names its own."""
    out = {slot: _ramp(ref) for slot, ref in variant.items()}
    out.setdefault("flash", FX_RAMPS["flash"])
    out.setdefault("smoke", FX_RAMPS["soot"])
    return out


def _v(**slots: str) -> dict[str, str]:
    return {k: (v if "/" in v else f"fx/{v}") for k, v in slots.items()}


# --------------------------------------------------------------------------------------------------------- drawing
class Field:
    """Shade layers for one frame, sampled `ss` x `ss` per pixel. Each slot's layer is the max of everything added
    to it (overlapping glows brighten, never darken); a pixel takes its brightest sample. Layers paint in the order
    they were first added, so add halos before cores."""

    def __init__(self, w: int, h: int, ss: int = 2):
        self.w, self.h, self.ss = w, h, ss
        o = (np.arange(ss) + 0.5) / ss
        xs = (np.arange(w)[:, None] + o[None, :]).reshape(-1)
        ys = (np.arange(h)[:, None] + o[None, :]).reshape(-1)
        self.Y, self.X = np.meshgrid(ys, xs, indexing="ij")
        self.layers: dict[str, np.ndarray] = {}

    def add(self, slot: str, shade) -> None:
        shade = np.broadcast_to(np.asarray(shade, np.float64), self.X.shape)
        cur = self.layers.get(slot)
        self.layers[slot] = shade.copy() if cur is None else np.maximum(cur, shade)

    def dist(self, cx: float, cy: float, sy: float = 1.0) -> np.ndarray:
        return np.hypot(self.X - cx, (self.Y - cy) * sy)

    def ang(self, cx: float, cy: float, sy: float = 1.0) -> np.ndarray:
        """Degrees, clockwise on screen from +x (y points down)."""
        return np.degrees(np.arctan2((self.Y - cy) * sy, self.X - cx))

    def spike(self, cx: float, cy: float, deg: float, r0: float, r1: float, w: float) -> np.ndarray:
        """Position along a tapered spike, 0 at its base (width w) to 1 at its point; NaN outside it."""
        a = math.radians(deg)
        px, py = self.X - cx, self.Y - cy
        t = px * math.cos(a) + py * math.sin(a)
        s = np.abs(-px * math.sin(a) + py * math.cos(a))
        u = (t - r0) / max(r1 - r0, 1e-6)
        inside = (u >= 0) & (u <= 1) & (s <= w / 2 * (1 - u) + 0.3)
        return np.where(inside, u, np.nan)

    def canvas(self, c: Canvas | None = None) -> Canvas:
        c = c or Canvas(self.w, self.h, SLOTS)
        ss = self.ss
        for slot, v in self.layers.items():
            v = v.reshape(self.h, ss, self.w, ss).max(axis=(1, 3))
            m = v >= 0.5
            c.slot[m] = SLOTS.id(slot)
            c.shade[m] = np.clip(np.rint(v[m]), 0, 5).astype(np.uint8)
        return c


def _q(v: np.ndarray, steps: list[tuple[float, float]]) -> np.ndarray:
    """Quantises a field: each (threshold, shade) applies where v >= threshold, later steps win. EMPTY elsewhere."""
    out = np.full(v.shape, EMPTY)
    for t, s in steps:
        out = np.where(v >= t, s, out)
    return out


def _on(u: np.ndarray, shade) -> np.ndarray:
    """A shade (or shade field) where `u` is defined (not NaN), EMPTY elsewhere."""
    return np.where(np.isnan(u), EMPTY, shade)


def _pmax(c: Canvas, x: float, y: float, slot: str, shade: int) -> None:
    """Sets a pixel unless something brighter is already there."""
    x, y = int(math.floor(x)), int(math.floor(y))
    if 0 <= x < c.w and 0 <= y < c.h and shade >= 1:
        if c.slot[y, x] == 0 or c.shade[y, x] <= shade:
            c.slot[y, x] = SLOTS.id(slot)
            c.shade[y, x] = min(5, shade)


def _bres(x0: float, y0: float, x1: float, y1: float):
    x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx + dy
    while True:
        yield x0, y0
        if x0 == x1 and y0 == y1:
            return
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def _lmax(c: Canvas, x0, y0, x1, y1, slot: str, shade: int) -> None:
    for x, y in _bres(x0, y0, x1, y1):
        _pmax(c, x + 0.5, y + 0.5, slot, shade)


def _twinkle(c: Canvas, x: float, y: float, slot: str, size: int, shade: int = 5) -> None:
    """A four-point star: size 0 is one pixel, each size adds a pixel to every arm, dimming outwards."""
    _pmax(c, x, y, slot, shade)
    for k in range(1, size + 1):
        s = shade - 1 - (k - 1) * (2 if size == 1 else 1) if size > 1 else shade - 2
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            _pmax(c, x + dx * k, y + dy * k, slot, s)


def _mote(c: Canvas, x: float, y: float, slot: str, shade: int, big: bool = False) -> None:
    """A particle: one pixel, or a 2x2 cluster with a lit top-left pixel."""
    if not big:
        _pmax(c, x, y, slot, shade)
        return
    _pmax(c, x, y, slot, shade)
    _pmax(c, x + 1, y, slot, shade - 1)
    _pmax(c, x, y + 1, slot, shade - 1)
    _pmax(c, x + 1, y + 1, slot, shade - 2)


def _dilate(m: np.ndarray, diag: bool = False) -> np.ndarray:
    p = np.pad(m, 1)
    out = m | p[:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, :-2] | p[1:-1, 2:]
    if diag:
        out |= p[:-2, :-2] | p[2:, 2:] | p[:-2, 2:] | p[2:, :-2]
    return out


def _ring_d(f: Field, cx: float, cy: float, rx: float, ry: float) -> np.ndarray:
    """Signed screen-space distance in pixels to an ellipse outline (negative inside). Thresholding this, rather
    than the ellipse equation, keeps an iso-squashed ring one pixel thick all the way round."""
    dx, dy = f.X - cx, f.Y - cy
    e = np.hypot(dx / rx, dy / ry)
    g = np.hypot(dx / rx ** 2, dy / ry ** 2) / np.maximum(e, 1e-6)
    return (e - 1) / np.maximum(g, 1e-6)


def _checker(h: int, w: int, odd: int = 0) -> np.ndarray:
    ys, xs = np.mgrid[0:h, 0:w]
    return (xs + ys + odd) % 2 == 0


def _paint(c: Canvas, mask: np.ndarray, slot: str, shade) -> None:
    shade = np.broadcast_to(np.asarray(shade), mask.shape)
    c.slot[mask] = SLOTS.id(slot)
    c.shade[mask] = np.clip(shade[mask], 0, 5).astype(np.uint8)


def _puffs(c: Canvas, blobs: list[tuple[float, float, float]], slot: str, *, noise: np.ndarray | None = None,
           erode: float = 0.0, rough: float = 0.35, sy: float = 1.0, base: int = 3,
           gas: np.ndarray | None = None) -> None:
    """A cloud: the union of round puffs, each lit from the top-left like a ball, with `noise` raggedising the edge.
    `erode` > 0 eats into the thin parts first, which is how a puff dissipates: it frays and opens holes rather than
    shrinking evenly. With a `gas` field the puffs merge into one body shaded by that field instead of each being a
    lit ball (a lingering gas, which as separate balls reads as foliage)."""
    if not blobs:
        return
    ys, xs = np.mgrid[0:c.h, 0:c.w].astype(np.float64) + 0.5
    best = np.full(xs.shape, -9.0)
    nx = np.zeros_like(xs)
    ny = np.zeros_like(xs)
    for bx, by, br in blobs:
        q = 1 - np.hypot(xs - bx, (ys - by) * sy) / max(br, 0.5)
        sel = q > best
        best = np.where(sel, q, best)
        nx = np.where(sel, (xs - bx) / br, nx)
        ny = np.where(sel, (ys - by) * sy / br, ny)
    v = best + ((noise - 0.5) * rough if noise is not None else 0)
    inside = v > erode
    lit = -(nx + ny) * 0.7071
    shade = (base + (lit > 0.25).astype(int) + ((lit > 0.62) & (best < 0.5)).astype(int)
             - (lit < -0.2).astype(int) - (lit < -0.58).astype(int))
    shade = shade - (v < erode + 0.07).astype(int)
    if gas is not None:
        up = np.zeros_like(inside)
        up[1:] = inside[:-1]
        down = np.zeros_like(inside)
        down[:-1] = inside[1:]
        shade = np.where(gas < 0.38, base - 1, np.where(gas > 0.66, base + 1, base))
        shade = np.where(up & ~np.roll(up, 1, 0), np.maximum(shade, base + 1), shade)
        shade = np.where(~up, base + 1 + (gas > 0.5), shade)              # the top edge catches the light
        shade = np.where(~down, base - 2, shade)
    _paint(c, inside, slot, np.clip(shade, 1, 5))


def _ease(t: float) -> float:
    """Fast out, slow in: bursts expand quickly, then settle."""
    t = min(max(t, 0.0), 1.0)
    return 1 - (1 - t) ** 2.2


# ------------------------------------------------------------------------------------------ layered construction
# The polish pass builds every big effect from the same five layers, in this order: an anticipation (motes or a bead
# gathering), a white-hot `flash` for a frame or two, a bright core, the coloured body, then sparks with motion
# trails and a fading residue. Sparks live in a tiny seeded particle model so they fly fast, slow down and arc over
# like thrown matter instead of drifting at a constant speed.
@dataclass
class _Spark:
    x: float
    y: float
    vx: float
    vy: float
    t0: float
    life: float
    big: bool


def _sparks(seed: int, n: int, cx: float, cy: float, speed: tuple[float, float], *, t0: float = 0.0,
            life: tuple[float, float] = (4.0, 7.0), arc: tuple[float, float] = (0.0, 360.0), sy: float = 1.0,
            up: float = 0.0, spread: float = 0.6, big: float = 0.3) -> list[_Spark]:
    """`n` sparks thrown from (cx, cy) at angles in `arc` (degrees, clockwise from +x), squashed by `sy` (2:1 on the
    ground), with an extra `up` kick; each starts within `spread` frames of t0."""
    r = tex.rng(seed)
    out = []
    for _ in range(n):
        a = math.radians(r.uniform(*arc))
        s = r.uniform(*speed)
        out.append(_Spark(cx, cy, s * math.cos(a), s * math.sin(a) * sy - up, t0 + r.uniform(0, spread),
                          r.uniform(*life), bool(r.random() < big)))
    return out


def _spark_at(p: _Spark, t: float, g: float, drag: float) -> tuple[float, float]:
    """Where a spark is `t` frames after launch: drag bleeds its speed off, gravity `g` bends it over."""
    k = (1 - drag ** t) / (1 - drag)
    return p.x + p.vx * k, p.y + p.vy * k + 0.5 * g * t * t


def _draw_sparks(c: Canvas, sparks: list[_Spark], i: float, *, hot: str = "core", slot: str = "main",
                 g: float = 0.3, drag: float = 0.8, trail: float = 0.9, top: int = 5) -> None:
    """Each live spark as a short streak from where it was `trail` frames ago to where it is: hot and white while
    young, cooling through the body ramp, the biggest ones a 2x2 cluster at first."""
    for p in sparks:
        t = i - p.t0
        if t < 0 or t > p.life:
            continue
        x, y = _spark_at(p, t, g, drag)
        bx, by = _spark_at(p, max(0.0, t - trail), g, drag)
        age = t / p.life
        sh = top if age < 0.25 else top - 1 if age < 0.5 else top - 2 if age < 0.8 else top - 3
        s = hot if age < 0.3 else slot
        if math.hypot(x - bx, y - by) >= 1.5:
            _lmax(c, bx, by, x, y, slot, max(1, sh - 2))
        _mote(c, x, y, s, sh, big=p.big and age < 0.45)


def _flash(f: Field, cx: float, cy: float, r: float, sy: float = 1.0) -> None:
    """The white-hot frame of an impact: a near-white disc, hottest in the middle."""
    f.add("flash", _q(1 - f.dist(cx, cy, sy) / r, [(0, 4), (0.4, 5)]))


def _flare(c: Canvas, cx: float, cy: float, length: int, slot: str = "flash", vertical: bool = True) -> None:
    """Lens streaks through a flash: a long horizontal line (and a shorter vertical one), dimming outwards."""
    for k in range(-length, length + 1):
        _pmax(c, cx + k, cy, slot, 5 - min(3, abs(k) * 4 // max(1, length)))
        if vertical and abs(k) <= length // 2:
            _pmax(c, cx, cy + k, slot, 5 - min(3, abs(k) * 8 // max(1, length)))


def _iso_ring(f: Field, cx: float, cy: float, rx: float, w: float = 0.6, sq: float = 0.5) -> np.ndarray:
    """A 1 px ring on the floor, 2:1 squashed, as a boolean mask."""
    return np.abs(_ring_d(f, cx, cy, rx, rx * sq)) < w


def _jag(r: np.random.Generator, x0: float, y0: float, x1: float, y1: float, step: float = 3.0,
         jag: float = 2.2) -> list[tuple[float, float]]:
    """A jagged path between two points in any direction: a random walk sideways, pinned at both ends."""
    L = math.hypot(x1 - x0, y1 - y0)
    n = max(2, int(L / step))
    walk = np.concatenate([[0], np.cumsum(r.normal(0, jag, n))])
    t = np.linspace(0, 1, n + 1)
    walk -= walk[-1] * t
    nx, ny = -(y1 - y0) / max(L, 1e-6), (x1 - x0) / max(L, 1e-6)
    return [(x0 + (x1 - x0) * tt + nx * w, y0 + (y1 - y0) * tt + ny * w) for tt, w in zip(t, walk)]


def _fork(r: np.random.Generator, x0: float, y0: float, x1: float, y1: float, depth: int, *, step: float = 3.0,
          jag: float = 2.2, chance: float = 0.28, level: int = 0,
          out: list | None = None) -> list[tuple[int, list[tuple[float, float]]]]:
    """A branching bolt: the trunk, plus forks that split off it at 20-50 degrees and fork again, each level
    shorter and finer. Returns (level, points) per stroke, trunk first."""
    out = [] if out is None else out
    pts = _jag(r, x0, y0, x1, y1, step, jag)
    out.append((level, pts))
    if depth > 0:
        base = math.atan2(y1 - y0, x1 - x0)
        L = math.hypot(x1 - x0, y1 - y0)
        for k in range(2, len(pts) - 2):
            if r.random() < chance:
                x, y = pts[k]
                a = base + math.radians(r.uniform(20, 50)) * (1 if r.random() < 0.5 else -1)
                ln = L * (1 - k / len(pts)) * r.uniform(0.3, 0.55)
                _fork(r, x, y, x + ln * math.cos(a), y + ln * math.sin(a), depth - 1, step=step * 0.8,
                      jag=jag * 0.7, chance=chance * 0.8, level=level + 1, out=out)
    return out


def _raster(W: int, H: int, pts: list[tuple[float, float]], upto: float = 1.0, ymax: float | None = None
            ) -> np.ndarray:
    """A stroke as a 1 px mask, cut to its first `upto` fraction (a leader still growing) or above `ymax`."""
    m = np.zeros((H, W), bool)
    n = int(round((len(pts) - 1) * upto))
    for (x0, y0), (x1, y1) in zip(pts[:n], pts[1:n + 1]):
        for x, y in _bres(x0, y0, x1, y1):
            if 0 <= x < W and 0 <= y < H and (ymax is None or y <= ymax):
                m[y, x] = True
    return m


def _bolt_paint(c: Canvas, strokes: list, *, upto: float = 1.0, ymax: float | None = None, thick: bool = False,
                halo: bool = True, hot: bool = True, dim: int = 0, levels: int = 9) -> None:
    """Paints a forked bolt: trunk white-hot (or core), forks in the body ramp, a halo two rings deep."""
    trunk = _raster(c.w, c.h, strokes[0][1], upto, ymax)
    if thick:
        trunk |= np.roll(trunk, 1, axis=1)
    twig1 = np.zeros_like(trunk)
    twig2 = np.zeros_like(trunk)
    for lv, pts in strokes[1:]:
        if lv > levels:
            continue
        m = _raster(c.w, c.h, pts, 1.0, ymax)
        (twig1 if lv == 1 else twig2)[...] |= m
    if halo:
        h1 = _dilate(trunk | twig1, diag=True)
        h2 = _dilate(h1) & _checker(c.h, c.w)
        _paint(c, h2 & ~h1, "main", max(1, 2 - dim))
        _paint(c, h1 & ~trunk, "main", max(1, 3 - dim))
    _paint(c, twig2 & ~trunk, "main", max(1, 3 - dim))
    _paint(c, twig1 & ~trunk, "main", max(1, 4 - dim))
    if hot and dim == 0:
        _paint(c, trunk, "flash", 5)
    else:
        _paint(c, trunk, "core", max(1, 5 - dim))


def _plus(c: Canvas, x: float, y: float, arm: int, slot: str, shade: int, hot: str = "core") -> None:
    """A healing cross: a lit centre and four arms `arm` px long."""
    for k in range(1, arm + 1):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            _pmax(c, x + dx * k, y + dy * k, slot, shade - (k == arm and arm > 1))
    _pmax(c, x, y, hot, min(5, shade + 1))


def _hexagon(c: Canvas, cx: float, cy: float, r: float, rot: float, slot: str, shade: int,
             fill: int | None = None) -> None:
    """A hexagon, outlined, optionally filled a shade darker: force magic's shard."""
    pts = [(cx + r * math.cos(math.radians(rot + 60 * k)), cy + r * math.sin(math.radians(rot + 60 * k)))
           for k in range(6)]
    if fill is not None and r >= 1.5:
        c.polygon(pts, slot, fill)
    for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
        _lmax(c, x0, y0, x1, y1, slot, shade)


# ------------------------------------------------------------------------------------------------- weapon strikes
def _crescent_field(f: Field, cx: float, cy: float, R: float, tail: float, head: float, wmax: float, dim: int,
                    slot: str = "main") -> None:
    th, r = f.ang(cx, cy), f.dist(cx, cy)
    s = ((th - tail) % 360) / (head - tail)
    wd = wmax * np.clip(s, 0, 1) ** 0.8 * np.clip((1 - s) * 5, 0, 1)
    d = R - r
    inside = (s >= 0) & (s <= 1) & (d >= -0.45) & (d <= wd)
    sh = np.select([d < 0.9, d < wd * 0.45, d < wd * 0.8], [5, 4, 3], 2).astype(float)
    sh -= dim + (s < 0.3) + (s < 0.12)
    if dim:
        inside &= ((th + 400) / 9 % 3) < 2                       # the smear breaks into dashes
    f.add(slot, np.where(inside, sh, EMPTY))


def slash() -> list[Canvas]:
    """A sword smear at 48 px: a glint where the blade starts, a thick crescent swept clockwise from over the
    shoulder to the opposite hip with a fainter afterimage inside it, a white-hot flash where it bites (front-right)
    throwing sparks along the swing, then the smear breaks into dashes and the last specks fly off its end."""
    cx, cy, R = 23.5, 23.5, 18.5
    keys = [(-92, -80, 1.4, 1), (-86, 14, 4.8, 0), (-72, 100, 8.2, 0), (-38, 132, 6.8, 0), (24, 140, 4.6, 1),
            (82, 146, 3.0, 2), (118, 148, 1.8, 3)]
    bite = math.radians(30)
    bx, by = cx + (R - 3) * math.cos(bite), cy + (R - 3) * math.sin(bite)
    sparks = _sparks(305, 10, bx, by, (3.5, 6.5), t0=2.0, arc=(95, 150), life=(2.5, 4.5))
    out = []
    for i in range(8):
        f = Field(48, 48)
        if i < len(keys):
            tail, head, wmax, dim = keys[i]
            if i in (2, 3):
                _crescent_field(f, cx, cy, R - 5, tail + 30, head - 6, wmax * 0.45, 2)
            _crescent_field(f, cx, cy, R, tail, head, wmax, dim)
        if i == 2:
            _flash(f, bx, by, 4.5)
        c = f.canvas()
        if i == 0:
            a = math.radians(-86)
            _twinkle(c, cx + R * math.cos(a), cy + R * math.sin(a), "flash", 3)
        if i in (1, 2, 3):
            a = math.radians(keys[i][1] - 6)
            _twinkle(c, cx + R * math.cos(a), cy + R * math.sin(a), "flash" if i == 2 else "main", 2 if i == 2 else 1)
        if i == 2:
            _flare(c, bx, by, 7, vertical=False)
        if i == 7:
            for a, sh in ((136, 2), (148, 3), (158, 2), (170, 2)):
                ra = math.radians(a)
                _pmax(c, cx + (R + 3) * math.cos(ra), cy + (R + 3) * math.sin(ra), "main", sh)
        _draw_sparks(c, sparks, i, hot="flash", g=0.35)
        out.append(c)
    return out


def hit() -> list[Canvas]:
    """An impact at 48 px: a pinpoint that blows out into a white-hot flash (two frames), an eight-ray star with a
    shockwave ring, then fourteen sparks with motion trails flying out, slowing and falling, and a few twinkles
    left hanging."""
    cx = cy = 23.5
    sparks = _sparks(301, 14, cx, cy, (4.5, 8.0), t0=1.6, life=(3.5, 6.5))
    out = []
    for i in range(9):
        f = Field(48, 48)
        if i == 0:
            f.add("main", _q(1 - f.dist(cx, cy) / 5, [(0, 3)]))
            _starburst(f, cx, cy, 8, 3, 2)
            _flash(f, cx, cy, 3.4)
        elif i == 1:
            f.add("main", _q(1 - f.dist(cx, cy) / 10, [(0, 2), (0.3, 3)]))
            _starburst(f, cx, cy, 18, 11, 3.6)
            _flash(f, cx, cy, 6.5)
        elif i == 2:
            _starburst(f, cx, cy, 20, 12, 2.6, r0=3, rot=6)
            f.add("main", _q(1 - np.abs(f.dist(cx, cy) - 8.5) / 1.3, [(0, 3), (0.5, 4)]))
            f.add("core", _q(1 - f.dist(cx, cy) / 3.6, [(0, 4), (0.5, 5)]))
        elif i == 3:
            _starburst(f, cx, cy, 21, 13, 1.6, r0=10, rot=6)
            ring = 1 - np.abs(f.dist(cx, cy) - 12.5) / 0.9
            f.add("main", np.where(((f.ang(cx, cy) + 400) / 20 % 2 < 1.5), _q(ring, [(0, 3)]), EMPTY))
        elif i == 4:
            ring = 1 - np.abs(f.dist(cx, cy) - 15.5) / 0.7
            f.add("main", np.where(((f.ang(cx, cy) + 400) / 15 % 2 < 1), _q(ring, [(0, 2)]), EMPTY))
        c = f.canvas()
        if i == 1:
            _flare(c, cx, cy, 11)
        _draw_sparks(c, sparks, i, g=0.32)
        if i >= 5:
            for x, y, s in ((12, 14, 1), (35, 18, 1), (31, 34, 1), (14, 31, 0)):
                if i - 5 < 3 - s:
                    _twinkle(c, x, y + (i - 5), "core", max(0, 1 - (i - 5) // 2), 5 - (i - 5))
        out.append(c)
    return out


def crit() -> list[Canvas]:
    """A critical hit at 64 px: a long glint, two frames of white-hot flash with lens streaks, a big gold star
    inside a red shockwave that rings out twice, twenty sparks raining out with trails, and twinkles left hanging.
    Larger, longer and two-toned so it never reads as an ordinary hit."""
    cx = cy = 31.5
    sparks = _sparks(311, 22, cx, cy, (5.5, 10.0), t0=2.6, life=(4.0, 8.0), big=0.4)
    keys = [  # cardinal ray, diagonal ray, width, core radius, ring radius, ring width
        (12, 4, 2, 2.6, 0, 0), (27, 15, 5.4, 6, 0, 0), (31, 19, 5, 6.5, 12, 3.2), (24, 13, 3.6, 4, 18, 2.6),
        (15, 8, 2.4, 0, 23, 2.0), (0, 0, 0, 0, 27, 1.5), (0, 0, 0, 0, 30, 1.1)] + [(0, 0, 0, 0, 0, 0)] * 5
    out = []
    for i, (card, diag, w, core, ring_r, ring_w) in enumerate(keys):
        f = Field(64, 64)
        if ring_r:
            d = f.dist(cx, cy)
            ring = 1 - np.abs(d - ring_r) / (ring_w / 2)
            sh = _q(ring, [(0, 2 if i >= 4 else 3), (0.5, 4 if i < 4 else 3)])
            if i >= 4:
                sh = np.where((f.ang(cx, cy) + 400) / 15 % 2 < 1.2, sh, EMPTY)
            f.add("accent", sh)
            if 3 <= i <= 5:                                        # the second, inner ring
                f.add("main", np.where(np.abs(d - ring_r * 0.62) < 0.6, 3 - (i == 5), EMPTY))
        if card:
            _starburst(f, cx, cy, card, diag, w, r0=0 if i < 3 else 5 if i == 3 else 9)
        if core:
            f.add("core", _q(1 - f.dist(cx, cy) / core, [(0, 4), (0.35, 5)]))
        if i in (1, 2):
            _flash(f, cx, cy, [0, 8, 5][i])
        c = f.canvas()
        if i == 0:
            _flare(c, cx, cy, 14)
        if i == 1:
            _flare(c, cx, cy, 26)
        _draw_sparks(c, sparks, i, g=0.34)
        if i >= 6:
            for x, y, s in ((13, 15, 2), (50, 19, 1), (45, 48, 2), (16, 44, 1), (32, 9, 1)):
                age = i - 6 + (s == 1)
                if age < 5:
                    _twinkle(c, x, y + age * 0.5, "core", max(0, s - age // 2), 5 - age // 2)
        out.append(c)
    return out


def thrust() -> list[Canvas]:
    """A stab: a spindle-shaped streak that shoots to the right, flashes at the point, then retracts from its tail.
    Speed lines above and below sell the lunge."""
    cy = 15.5
    keys = [(8, 14, 0.7, 1, 0), (3, 24, 1.7, 0, 0), (1, 28, 2.9, 0, 2), (9, 29, 2.2, 0, 1), (17, 29, 1.2, 1, 0),
            (24, 29, 0.6, 2, 0)]
    out = []
    for i, (x0, x1, hh, dim, flash) in enumerate(keys):
        f = Field(32, 32)
        u = (f.X - x0) / (x1 - x0)
        prof = np.clip(np.sin(np.pi * np.clip(u, 0, 1) ** 1.6), 0, 1) ** 0.7
        d = np.abs(f.Y - cy) / np.maximum(hh * prof, 1e-3)
        inside = (u >= 0) & (u <= 1) & (np.abs(f.Y - cy) <= hh * prof + 0.25)
        sh = np.select([d < 0.4, d < 0.75], [5, 4], 3).astype(float) - dim - (u < 0.3)
        f.add("main", np.where(inside, sh, EMPTY))
        c = f.canvas()
        if 1 <= i <= 4:
            for ly, length, off in ((10, 7, 0), (21, 6, 3), (12, 4, 6), (19, 5, 8)):
                lx = x0 + off + i * 2
                if lx + length < x1 - 2:
                    _lmax(c, lx, ly, lx + length, ly, "main", 3 - (i >= 3) - dim)
        if flash:
            _twinkle(c, 29.5, cy, "main", flash + 1)
        if i == 3:
            for a in range(0, 360, 45):
                _pmax(c, 29.5 + 3.4 * math.cos(math.radians(a)), cy + 3.4 * math.sin(math.radians(a)), "main", 3)
        if i == 5:
            for x, y in ((29, 12), (30, 19), (31, 15)):
                _pmax(c, x, y, "main", 2)
        out.append(c)
    return out


def _starburst(f: Field, cx: float, cy: float, card: float, diag: float, w: float, r0: float = 0.0,
               rot: float = 0.0) -> None:
    """Four long and four short tapered rays: bright core near the centre, the main ramp out at the tips."""
    for k in range(8):
        length = card if k % 2 == 0 else diag
        if length <= r0 + 0.5:
            continue
        u = f.spike(cx, cy, rot + k * 45, r0, length, w if k % 2 == 0 else w * 0.7)
        f.add("main", _on(u, np.where(u < 0.75, 4, 3)))
        f.add("core", _on(u, np.where(u < 0.3, 5, np.where(u < 0.55, 4, EMPTY))))


def _arrow(c: Canvas, tip: int, y: int) -> None:
    """A fletched arrow pointing right, tip at (tip, y): steel head, wooden shaft, pale fletching."""
    tail = tip - 19
    c.line(tail + 1, y, tip - 3, y, "wood", 3)
    c.px(tip - 4, y, "wood", 4)
    for x, sh in ((tip, 4), (tip - 1, 5), (tip - 2, 4)):
        c.px(x, y, "metal", sh)
    for dy, sh in ((-1, 4), (1, 2)):
        c.px(tip - 2, y + dy, "metal", sh)
        c.px(tip - 3, y + dy, "metal", sh - 1)
    for k in range(4):                                   # two vanes, swept back
        c.px(tail + 1 + k, y - 1 - (k < 2), "feather", 4 if k < 3 else 3)
        c.px(tail + 1 + k, y + 1 + (k < 2), "feather", 2)
    c.px(tail, y, "feather", 3)
    c.px(tail + 1, y - 2, "feather", 5)
    c.outline()


def arrow() -> list[Canvas]:
    c = Canvas(32, 32, SLOTS)
    _arrow(c, 27, 15)
    return [c]


def arrow_trail() -> list[Canvas]:
    """The arrow in flight: three wind lines stream off the fletching. Dashes scroll left a whole period per loop."""
    out = []
    for k in range(4):
        c = Canvas(32, 32, SLOTS)
        for y, length, phase in ((14, 7, 0), (15, 11, 2), (16, 6, 1)):
            for x in range(8 - length, 8):
                if (x + k + phase) % 4 == 0:
                    continue
                d = 8 - x
                _pmax(c, x, y, "accent", 4 if d < 3 else 3 if d < 7 else 2)
        _arrow(c, 27, 15)
        out.append(c)
    return out


# ---------------------------------------------------------------------------------------------------- projectiles
def fireball() -> list[Canvas]:
    """A fireball in flight, facing right: a hot round head and a flame tail torn by noise that streams backwards.
    The noise rolls a quarter of its (wrapping) width per frame, so four frames loop exactly."""
    cx, cy, R = 22.5, 15.5, 4.6
    r = tex.rng(321)
    n1 = tex.fbm(32, 32, 4, r, octaves=2, aspect=2.0)
    n2 = tex.fbm(32, 32, 8, r, octaves=1)
    embers = [(r.uniform(0, 24), r.uniform(-3, 3), r.uniform(0, 6.28)) for _ in range(6)]
    out = []
    for k in range(4):
        f = Field(32, 32, ss=1)
        nn = 0.6 * np.roll(n1, -8 * k, axis=1) + 0.4 * np.roll(n2, -16 * k, axis=1)
        dx, dy = f.X - cx, f.Y - cy
        behind = np.clip(-dx / 16, 0, 1)
        sx = np.where(dx > 0, 1.0, 3.6)
        d = np.hypot(dx / sx, dy * (1 + 0.8 * behind)) / R
        v = 1 - d + (nn - 0.5) * (0.3 + 1.2 * behind)
        f.add("main", _q(v, [(-0.02, 1), (0.1, 2), (0.24, 3), (0.38, 4)]))
        f.add("core", _q(v - behind * 0.6, [(0.5, 4), (0.68, 5)]))
        c = f.canvas()
        for x0, y0, ph in embers:
            x = (x0 - 8 * k) % 32 - 8
            if x < 0:
                continue
            y = cy + y0 + math.sin(ph + x * 0.4)
            _mote(c, x, y, "main", 4 if x > 10 else 3 if x > 4 else 2)
        out.append(c)
    return out


def eldritch() -> list[Canvas]:
    """An eldritch blast bolt, facing right: a dark orb with a hot violet rim and a pinprick core, a three-armed
    swirl turning inside it, and two writhing wisps behind. Swirl and wisps advance one symmetry period per loop."""
    cx, cy, R = 21.5, 15.5, 5.2
    out = []
    for k in range(6):
        f = Field(32, 32)
        d = f.dist(cx, cy) / R
        th = np.radians(f.ang(cx, cy))
        f.add("core", _q(1 - d, [(0, 1), (0.35, 2)]))
        swirl = np.sin(3 * th + d * 5.5 - k * (2 * np.pi / 6))
        f.add("main", np.where((d < 0.85) & (d > 0.25) & (swirl > 0.55), 3, EMPTY))
        f.add("main", _q(1 - np.abs(d - 0.92) / 0.16, [(0, 4), (0.55, 5)]))
        f.add("accent", _q(1 - f.dist(cx, cy) / 1.4, [(0, 5)]))
        c = f.canvas()
        for sgn, ph in ((1, 0.0), (-1, math.pi)):
            for x in range(3, int(cx - R) + 1):
                u = (cx - R - x) / (cx - R - 3)
                amp = 3.4 * (1 - u * 0.5) * min(1, (1 - u) * 6 + 0.2)
                y = cy + sgn * amp * math.sin(x * 0.55 + k * 2 * math.pi / 6 + ph)
                _pmax(c, x, y, "main", 5 if u < 0.15 else 4 if u < 0.45 else 3 if u < 0.75 else 2)
        for j in range(2):                                   # crackle: short zigzags off the rim
            a = math.radians(k * 67 + j * 170 + 20)
            x, y = cx + (R + 1) * math.cos(a), cy + (R + 1) * math.sin(a)
            x2, y2 = x + 2 * math.cos(a + 0.7), y + 2 * math.sin(a + 0.7)
            _lmax(c, x, y, x2, y2, "main", 4)
            _pmax(c, x2 + 2 * math.cos(a - 0.6), y2 + 2 * math.sin(a - 0.6), "main", 3)
        out.append(c)
    return out




# ---------------------------------------------------------------------------------------------------- spell bursts
def _blast(W: int, H: int, cx: float, gy: float, R: float, seed: int, *, tongues: bool) -> list[Canvas]:
    """A fire blast: a bead of fire gathers, a white flash with lens streaks, a ball of fire with a white-hot heart
    and a ragged rim (flame tongues licking up off it when `tongues`), a shockwave racing out along the ground,
    debris and embers thrown out on arcs, then the fire tears open and cools into a column of soot that rolls up
    and frays. Used by the fx fireball blast and, bigger and with tongues, by the Fireball spell."""
    cy = gy - R * 0.95
    r = tex.rng(seed)
    n = tex.fbm(W, H, 4, r, octaves=3)
    nl = tex.fbm(W, H, 6, r, octaves=2, aspect=0.4)
    ns = tex.fbm(W, H, 6, r, octaves=2)
    gather = [(a, r.uniform(2.2, 3.2)) for a in np.linspace(0, 360, 12, endpoint=False) + r.uniform(-12, 12, 12)]
    embers = _sparks(seed + 1, 30, cx, cy, (R * 0.25, R * 0.5), t0=3.0, life=(5, 10), up=R * 0.08, big=0.25)
    debris = _sparks(seed + 2, 12, cx, cy + R * 0.3, (R * 0.22, R * 0.4), t0=3.2, life=(6, 10), arc=(190, 350),
                     up=R * 0.12, big=1.0)
    ball = [0, 0, 0, 0.8, 1.02, 1.14, 1.2, 1.16, 1.06, 0.92, 0.75, 0, 0, 0, 0]
    heat = [0, 0, 0, 1.05, 1.0, 0.9, 0.78, 0.62, 0.48, 0.34, 0.2, 0, 0, 0, 0]
    out = []
    for i in range(15):
        c = Canvas(W, H, SLOTS)
        if i >= 5:                                              # the soot column, behind the fire
            g = min(1.0, (i - 5) / 6)
            rise = R * 1.3 * g
            rr = R * (0.35 + 0.35 * g)
            blobs = [(cx - R * 0.45, cy - rise * 0.6, rr), (cx + R * 0.45, cy - rise * 0.7, rr),
                     (cx, cy - rise * 1.1, rr * 1.15), (cx - R * 0.6, cy - rise * 1.15, rr * 0.8),
                     (cx + R * 0.65, cy - rise * 1.2, rr * 0.8), (cx, cy - rise * 0.3, rr * 0.8)]
            erode = max(0.0, (i - 9) * 0.13)
            _puffs(c, blobs, "smoke", noise=np.roll(ns, -2 * i, 0), erode=erode, rough=0.55, base=2)
        f = Field(W, H, ss=1)
        if 2 <= i <= 9:                                         # the shockwave running out over the ground
            rmax = W / 2 - 3
            rad = R * 0.7 + (rmax - R * 0.7) * _ease((i - 2) / 7)
            d = _ring_d(f, cx, gy, rad, rad * 0.42)
            front = (d > -0.6) & (d < 0.7)
            trail = (d > -2.4) & (d <= -0.6) & _checker_s(f)
            age = i - 2
            f.add("main", np.where(front, max(1, 4 - age // 2), np.where(trail, max(1, 2 - age // 3), EMPTY)))
        if i <= 1:
            f.add("main", _q(1 - f.dist(cx, cy) / (2.5 + 2 * i), [(0, 3), (0.5, 4)]))
            f.add("core", _q(1 - f.dist(cx, cy) / (1.4 + i), [(0, 5)]))
        if i == 2:
            f.add("main", _q(1 - f.dist(cx, cy, 1.1) / (R * 0.95), [(0, 3), (0.4, 4)]))
            _flash(f, cx, cy, R * 0.72, 1.1)
        if ball[i]:
            Rb = R * ball[i]
            dx, dy = f.X - cx, f.Y - cy
            up = np.clip(-dy / Rb, 0, 1.5)
            d = np.hypot(dx, dy * (1.1 - (0.35 * up if tongues else 0))) / Rb
            e = 1 - d + (n - 0.5) * 0.55 + (heat[i] - 1) * 0.4
            if tongues:
                lick = np.roll(nl, -3 * i, 0)
                e = e + (lick - 0.45) * 0.9 * np.clip(up, 0, 1)
            if heat[i] < 0.6:
                e = np.where(n < 0.66 - heat[i], EMPTY, e)       # the fire tears open as it cools
            f.add("main", _q(e, [(0.0, 1), (0.08, 2), (0.17, 3), (0.28, 4)]))
            f.add("core", _q(e, [(0.4, 4), (0.55, 5)]))
            if i <= 4:
                f.add("flash", _q(e, [(0.72, 5)]))
        c = f.canvas(c)
        if i <= 1:
            for a, sp in gather:                                # motes sucked into the bead
                rad = R * sp * (1 - 0.45 * i) * 0.6
                ra = math.radians(a + 25 * i)
                _lmax(c, cx + rad * math.cos(ra), cy + rad * math.sin(ra), cx + rad * 0.8 * math.cos(ra),
                      cy + rad * 0.8 * math.sin(ra), "main", 3 + i)
        if i == 2:
            _flare(c, cx, cy, int(R * 1.6))
        for p in debris:                                       # chunks of ground, trailing a spark
            t = i - p.t0
            if 0 <= t <= p.life:
                x, y = _spark_at(p, t, R * 0.05, 0.86)
                if y > gy + 3:
                    continue
                bx, by = _spark_at(p, max(0, t - 1), R * 0.05, 0.86)
                _pmax(c, bx, by, "main", 3 if t < 4 else 2)
                c.px(x, y, "smoke", 2)
                c.px(x + 1, y, "smoke", 1)
                c.px(x, y + 1, "smoke", 1)
                c.px(x - 1, y - 1 + (int(t) % 2), "smoke", 3)
        _draw_sparks(c, embers, i, g=R * 0.012, drag=0.84)
        out.append(c)
    return out


def explosion() -> list[Canvas]:
    """The fireball's blast at 96 px (see _blast): roughly a tile and a half across at its widest."""
    return _blast(96, 96, 47.5, 80.0, 19.0, 331, tongues=False)


def lightning() -> list[Canvas]:
    """A lightning strike from above, 128 px tall: the sky flickers, a stepped leader creeps down forking as it
    goes, the return stroke hits white-hot with every fork lit and a ground flash and shockwave, flickers to a
    second forked path and re-strikes, then breaks into fading fragments while small arcs crawl over the ground and
    sparks fall. Two pre-made forked paths alternate so the bolt shimmers instead of redrawing every frame."""
    W, H, gx, gy = 64, 128, 31.5, 122.0
    r = tex.rng(351)
    paths = [_fork(r, gx + 7, -2, gx, gy, 2, step=4.0, jag=3.0, chance=0.3),
             _fork(r, gx - 6, -2, gx, gy, 2, step=4.0, jag=3.0, chance=0.3)]
    sparks = _sparks(352, 14, gx, gy - 1, (1.4, 3.4), t0=5.0, arc=(190, 350), life=(3, 6), sy=0.6, up=1.0)
    crawl = tex.rng(353)
    out = []
    for i in range(12):
        c = Canvas(W, H, SLOTS)
        f = Field(W, H, ss=1)
        if i <= 1:                                                   # the sky lights up first
            f.add("main", _q(1 - f.dist(gx + 6, -2, 2.4) / (16 + 6 * i), [(0, 1), (0.4, 2), (0.75, 3)]))
        if 3 <= i <= 8:                                              # ground flash and shockwave
            age = i - 3
            rad = 6 + 5 * age
            f.add("main", np.where(_iso_ring(f, gx, gy, rad, 0.7, 0.4), max(1, 4 - age // 2), EMPTY))
            if age <= 2:
                _flash(f, gx, gy - 1, [8, 5, 6][age], 2.4)
                f.add("main", _q(1 - f.dist(gx, gy, 2.4) / (12 - 2 * age), [(0, 2)]))
        f.canvas(c)
        if i == 0:
            _bolt_paint(c, paths[0], ymax=gy * 0.14, halo=False, hot=False, dim=2, levels=1)
        elif i in (1, 2):
            _bolt_paint(c, paths[0], ymax=gy * (0.45 if i == 1 else 0.8), halo=False, hot=False, dim=1)
        elif i == 3:
            _bolt_paint(c, paths[0], thick=True)
        elif i == 4:
            _bolt_paint(c, paths[1])
        elif i == 5:
            _bolt_paint(c, paths[0], thick=True)
        elif i == 6:
            _bolt_paint(c, paths[0], halo=False, hot=False, dim=1, levels=1)
        elif i in (7, 8):                                            # the channel breaks up into fragments
            frag = Canvas(W, H, SLOTS)
            _bolt_paint(frag, paths[0][:1], halo=False, hot=False, dim=i - 5)
            rows = np.arange(H)[:, None] // (6 if i == 7 else 4) % 3
            frag.slot[np.broadcast_to(rows == 0 if i == 7 else rows != 0, (H, W))] = 0
            c.blit(frag)
        if 4 <= i <= 10:                                             # little arcs crawling over the ground
            for _ in range(3 if i < 8 else 1):
                a = crawl.uniform(0, 2 * math.pi)
                rad = crawl.uniform(4, 12 + i)
                x0, y0 = gx + rad * math.cos(a), gy + rad * 0.4 * math.sin(a)
                x1, y1 = x0 + crawl.uniform(-6, 6), y0 + crawl.uniform(-4, 1)
                for x, y in _jag(crawl, x0, y0, x1, y1, step=1.6, jag=1.0):
                    _pmax(c, x, y, "core" if i < 7 else "main", 5 if i < 7 else 3)
        _draw_sparks(c, sparks, i, g=0.3)
        out.append(c)
    return out


def heal() -> list[Canvas]:
    """Healing at 64x80: motes drift in over the ground and a ring of light opens at the feet, a soft curtain of
    rays climbs the body while two spirals of sparkles and a stream of crosses rise past it (swelling at their
    brightest), and a few twinkles are left hanging above the head as the ring fades."""
    cx, gy = 31.5, 72.5
    r = tex.rng(361)
    crosses = [(cx + r.uniform(-13, 13), gy - r.uniform(2, 10), 2 + k * 0.7, r.uniform(3.0, 4.4), r.uniform(0, 6.28),
                k % 3 == 0) for k in range(9)]
    helix = [(k * 360 / 10 + r.uniform(-10, 10), 1.5 + (k % 5) * 1.1) for k in range(10)]
    inward = [(r.uniform(0, 360), r.uniform(20, 30)) for _ in range(12)]
    rings = [4, 9, 14, 18, 20, 21, 21, 21, 20, 19, 17, 15, 0, 0]
    out = []
    for i in range(14):
        f = Field(64, 80, ss=1)
        rad = rings[i]
        if rad:
            front = f.Y > gy
            sh = np.where(front, 4, 3) - (i >= 9) - (i >= 11)
            f.add("main", np.where(_iso_ring(f, cx, gy, rad, 0.6, 0.42), sh, EMPTY))
            if 2 <= i <= 9:
                f.add("main", np.where(_iso_ring(f, cx, gy, rad - 5, 0.5, 0.42) & _checker_s(f), 2, EMPTY))
        if 2 <= i <= 10:
            hw = [0, 0, 5, 9, 11, 12, 12, 11, 9, 6, 3][i]
            top = gy - (gy - 8) * min(1.0, (i - 1) / 3)
            _beam(f, cx, hw, top, gy, 1 if i < 8 else 2, i, hollow=True)
        if i == 2:
            f.add("core", _q(1 - f.dist(cx, gy, 2.4) / 9, [(0, 3), (0.5, 4)]))
        c = f.canvas()
        if i <= 2:
            for a, d0 in inward:                                     # motes drawn in over the ground
                d = d0 * (1 - 0.32 * i)
                ra = math.radians(a + 20 * i)
                _pmax(c, cx + d * math.cos(ra), gy + d * 0.42 * math.sin(ra) - 1, "core" if i == 2 else "main",
                      3 + i)
        for a0, t0 in helix:                                         # two spirals of sparkles climbing
            age = i - t0
            if 0 <= age <= 7:
                a = math.radians(a0 + age * 48)
                rr = 13 - age * 0.6
                x = cx + rr * math.cos(a)
                y = gy - 4 - age * 6.5 + rr * 0.35 * math.sin(a)
                front = math.sin(a) > 0
                sh = (5 if age < 4 else 4 if age < 6 else 3) - (not front)
                _mote(c, x, y, "core" if front and age < 5 else "main", sh, big=front and age < 3)
        for x0, y0, t0, vy, ph, big in crosses:                      # crosses rising, swelling, then fading
            age = i - t0
            if age < 0 or age > 6:
                continue
            x = x0 + math.sin(ph + age * 1.1) * 1.4
            y = y0 - vy * age - 4
            if age < 1:
                _pmax(c, x, y, "main", 3)
            elif age < 4:
                _plus(c, x, y, 2 if big or age > 2 else 1, "main", 4)
            elif age < 5.5:
                _plus(c, x, y, 1, "main", 3)
            else:
                _pmax(c, x, y, "main", 2)
        if i >= 9:
            for x, y, s in ((cx - 9, 16, 1), (cx + 8, 11, 2), (cx + 2, 22, 1), (cx - 4, 7, 1)):
                age = i - 9
                if age < 4:
                    _twinkle(c, x, y - age, "core", max(0, s - age // 2), 5 - age // 2)
        out.append(c)
    return out


def _checker_s(f: Field) -> np.ndarray:
    return (np.floor(f.X) + np.floor(f.Y)) % 2 == 0


def ice() -> list[Canvas]:
    """Ice shards: frost spreads on the ground, crystals spike up and outward, glint, crack, then shatter into
    fragments that tumble away."""
    cx, cy = 23.5, 39.0
    r = tex.rng(341)
    angs = [200, 245, 290, 335, 20, 65, 115, 160]
    shards = []                                          # (angle, length, width, base radius)
    for a in angs:
        shards.append((a + r.uniform(-10, 10), r.uniform(9, 14), r.uniform(3.5, 5), r.uniform(4, 7)))
    shards.append((270, 21, 7, 0))                          # the central spire
    shards.sort(key=lambda s: math.sin(math.radians(s[0])) * s[3])   # back to front
    rime = []
    for _ in range(12):
        a, d = r.uniform(0, 2 * math.pi), math.sqrt(r.uniform(0.1, 0.8))
        rime.append((int(round(d * math.cos(a) * 10)) / 10, int(round(d * math.sin(a) * 10)) / 10,
                     int(r.integers(0, 3))))
    grow = [0, 0.45, 1.1, 1.0, 1.0]
    out = []

    def geom(a, length, w, br, g):
        ra = math.radians(a)
        bx, by = cx + br * math.cos(ra), cy + br * 0.5 * math.sin(ra)
        lean = 0.0 if br == 0 else 0.55
        tx = bx + math.cos(ra) * length * lean * g
        ty = by + math.sin(ra) * length * lean * 0.5 * g - length * 0.95 * g
        return bx, by, tx, ty

    for i in range(8):
        c = Canvas(48, 48, SLOTS)
        mist_r = [7, 11, 14, 15, 15, 15, 14, 13][i]
        mist = Field(48, 48, ss=1)
        ring = _ring_d(mist, cx, cy, mist_r, mist_r / 2)
        mist.add("accent", np.where(np.abs(ring) < 0.55, [3, 4, 4, 3, 3, 3, 2, 1][i], EMPTY))
        mist.canvas(c)
        for j, (fx_, fy_, t0) in enumerate(rime):                  # frost crystals creeping over the ground
            if t0 <= i < 7 and (i < 6 or j % 2 == 0):
                x, y = cx + fx_ * mist_r, cy + fy_ * mist_r / 2
                _pmax(c, x, y, "accent", 4 if i - t0 < 2 else 2)
                _pmax(c, x + 1, y, "accent", 3 if i - t0 < 2 else 1)
        if i < 5:
            g = grow[i]
            for a, length, w, br in shards:
                if g <= 0:
                    continue
                bx, by, tx, ty = geom(a, length, w, br, g)
                hw = w / 2
                c.polygon([(bx - hw, by + 0.5), (tx, ty), (bx + 0.5, by + 0.5)], "main", 4)
                c.polygon([(bx + 0.5, by + 0.5), (tx, ty), (bx + hw, by + 0.5)], "main", 2)
                c.line(bx - hw + 0.5, by, tx, ty, "main", 5 if i in (2, 3) else 4)
                c.line(bx + 0.5, by, tx, ty, "main", 3)
                if i == 3:
                    _twinkle(c, tx, ty + 1, "main", 1 if length < 15 else 2)
                if i == 4:                                        # cracks run up the shard before it breaks
                    for t in (0.3, 0.6):
                        x, y = bx + (tx - bx) * t, by + (ty - by) * t
                        c.px(x - 1, y - 1, "main", 1)
                        c.px(x, y, "main", 1)
                        c.px(x + 1, y, "main", 1)
                        c.px(x, y - 1, "main", 5)
        else:
            t = i - 4
            for a, length, w, br in shards:
                bx, by, tx, ty = geom(a, length, w, br, 1.0)
                ra = math.radians(a)
                out_x = math.cos(ra) if br else 0
                for j, u in enumerate((0.3, 0.6, 0.92)):
                    x0, y0 = bx + (tx - bx) * u, by + (ty - by) * u
                    vx = out_x * 1.6 + (j - 1) * 0.6
                    vy = -1.2 - 0.6 * u
                    x = x0 + vx * t * 1.6
                    y = y0 + vy * t * 1.6 + 0.9 * t * t
                    if i == 7 and j != 1:
                        continue
                    sh = 5 - t
                    _pmax(c, x, y, "main", sh)
                    if t < 3:
                        _pmax(c, x + 1, y, "main", sh - 2)
                        _pmax(c, x, y + 1, "main", sh - 1)
        out.append(c)
    return out


def _bolt_path(r: np.random.Generator, x0: float, y0: float, x1: float, y1: float, step: float = 3.0,
               jag: float = 2.6) -> list[tuple[float, float]]:
    """A jagged path: a random walk across, straightened so it lands exactly at (x1, y1)."""
    n = max(2, int(abs(y1 - y0) / step))
    walk = np.concatenate([[0], np.cumsum(r.normal(0, jag, n))])
    t = np.linspace(0, 1, n + 1)
    walk -= walk[-1] * t
    return [(x0 + (x1 - x0) * tt + w, y0 + (y1 - y0) * tt) for tt, w in zip(t, walk)]


def _beam(f: Field, cx: float, hw: float, top: float, bottom: float, shade_drop: int, k: int,
          hollow: bool = False) -> None:
    """A vertical beam of light: a hot centre, cooler sides with bright streaks climbing them, and an outer glow
    of rising dashed rays. A `hollow` beam is only the rays, a curtain to put other shapes in front of."""
    if hw <= 0:
        return
    u = np.abs(f.X - cx) / hw
    col = np.floor(f.X)
    inside = (f.Y >= top) & (f.Y <= bottom)
    sh = np.select([u < 0.3, u < 0.6], [5, 3], 2).astype(float) - shade_drop
    streak = np.sin(f.Y * 0.35 + k * 2.4 + col * 2.3) > 0.8                   # streaks climb the beam
    sh = np.where(streak & (u < 0.6) & (u >= 0.3), 4 - shade_drop, sh)
    # the outer glow is rays, not a fill: every other column, broken into dashes that rise
    ray = (col % 2 == 0) & ((np.floor(f.Y) + 3 * k + col * 5) % 9 < 6)
    fade = np.clip((f.Y - top) / 6, 0, 1)                 # it comes down out of the sky
    sh = sh - (fade < 0.5) - (fade < 0.2)
    ok = inside & ((u < 1.0) & ray if hollow else (u < 0.6) | ((u < 1.0) & ray))
    if hollow:
        ray = (col % 3 == 0) & ((np.floor(f.Y) + 3 * k + col * 5) % 11 < 4)
        ok = inside & (u < 1.0) & ray
        sh = np.where(u < 0.5, 3, 2) - shade_drop - (fade < 0.5) - (fade < 0.2)
    f.add("main", np.where(ok & (u >= 0.3), sh, EMPTY))
    f.add("core", np.where(inside & (u < 0.3) & ~hollow, sh, EMPTY))


def levelup() -> list[Canvas]:
    """Level up: a golden column rises from a rippling ring, chevrons stream upward through it and sparkles
    scatter. The chevrons are the read: up, up, up."""
    W, H, cx, gy = 32, 64, 15.5, 59.5
    r = tex.rng(381)
    chevrons = [1, 2, 3, 4, 5]                              # spawn frames
    sparks = [(cx + r.uniform(-12, 12), gy - r.uniform(4, 40), r.uniform(1, 6), r.random() < 0.4) for _ in range(9)]
    keys = [(2.0, 0.3), (5.0, 0.7), (8.0, 1.0), (9.0, 1.0), (9.0, 1.0), (8.0, 1.0), (6.0, 1.0), (4.0, 1.0), (0, 1)]
    out = []
    for i, (hw, reach) in enumerate(keys):
        f = Field(W, H, ss=1)
        top = gy - (gy - 2) * reach
        _beam(f, cx, hw, top, gy, 1 if i >= 6 else 0, i, hollow=True)
        if i <= 5:
            rad = [3, 7, 11, 14, 15, 15][i]
            e = f.dist(cx, gy, 2.6)
            f.add("main", _q(1 - np.abs(e - rad) / 0.9, [(0, 4 if i < 3 else 3 if i < 5 else 2)]))
        c = f.canvas()
        for t0 in chevrons:
            age = i - t0
            y = gy - 12 - age * 10
            if age < 0 or y < 3:
                continue
            x = cx - 0.5
            for dy, slot, sh in ((0, "core", 5), (1, "accent", 4 if y > 14 else 3)):   # lit top edge, gold body
                arm = list(_bres(x - 6, y + 6 + dy, x, y + dy)) + list(_bres(x, y + dy, x + 6, y + 6 + dy))
                for px_, py_ in arm:
                    c.px(px_, py_, slot, sh - (y < 14))
        for x, y, t0, big in sparks:
            age = i - t0
            if 0 <= age < 3:
                _twinkle(c, x, y - age * 2, "accent", (2 if big else 1) if age < 1.5 else 0, 5 - int(age))
        out.append(c)
    return out


def misty() -> list[Canvas]:
    """Misty step / teleport: mist gathers at the feet and spirals up around the body, the spiral tightens to a
    slit of light, the figure winks out in a flash, and the last motes drift away."""
    cx, gy = 23.5, 44.0
    r = tex.rng(391)
    n = tex.fbm(48, 48, 6, r, octaves=2)
    motes = [(r.uniform(0, 6.28), r.uniform(2, 8), r.uniform(10, 34), r.random() < 0.35) for _ in range(10)]
    keys = [  # (number of rings, ring radius, rotation step)
        (1, 10), (3, 10), (5, 9), (7, 7.5), (8, 3.0)]
    out = []
    for i in range(8):
        c = Canvas(48, 48, SLOTS)
        if i <= 2 or i == 6:                                 # ground mist
            k = [0.2, 0.5, 0.65, 0, 0, 0, 0.8, 1][i]
            blobs = [(cx + dx, gy - 1 + dy, 3.5) for dx, dy in ((-9, 0), (-4, 1), (2, 1), (8, 0), (-1, -1))]
            _puffs(c, blobs, "main", noise=n, erode=0.1 + 0.6 * abs(k - 0.5) * (i == 6) + (0.25 if i == 0 else 0),
                   rough=0.6, sy=1.8, base=2)
        if i <= 4:
            rings, rx = keys[i]
            for j in range(rings):
                y = gy - 2 - j * 5.2
                a0 = j * 137 + i * 55
                rr = rx * (1 - 0.18 * j / 8)
                for s in range(0, 150, 6):
                    a = math.radians(a0 + s)
                    x, yy = cx + rr * math.cos(a), y + rr * 0.32 * math.sin(a)
                    front = math.sin(a) > 0
                    sh = (4 if front else 2) + (1 if (front and i == 3) else 0) - (s < 30) - 2 * (i == 4)
                    _pmax(c, x, yy, "main", sh)
                    if front and i >= 2:
                        _pmax(c, x, yy - 1, "main", sh - 1)
        if i == 4:
            for y in range(8, int(gy)):
                edge = min(y - 8, gy - y)
                c.px(cx, y, "core", 5 if edge > 6 else 4 if edge > 2 else 3)
                if edge > 9:
                    c.px(cx - 1, y, "core", 3)
                    c.px(cx + 1, y, "core", 2)
        if i == 5:
            _twinkle(c, cx, 26, "core", 6)
            _mote(c, cx - 0.5, 25.5, "core", 5, big=True)
            for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
                for k in (2, 3):
                    _pmax(c, cx + dx * k, 26 + dy * k, "core", 6 - k)
        if i >= 5:
            t = i - 4
            for a, vr, y0, big in motes:
                x = cx + math.cos(a) * vr * t * 1.4
                y = y0 - t * 2.5 + math.sin(a) * vr * 0.4
                if i == 7 and not big:
                    continue
                _mote(c, x, y, "core" if t == 1 else "main", 5 - t, big=big and t < 3)
        out.append(c)
    return out


def hex_mark() -> list[Canvas]:
    """A hex hanging over a cursed target: wisps converge, a sigil (ring, inverted triangle, glaring eye) flashes in,
    then hovers and pulses while the wisps orbit (a third of a turn per loop, the wisps' own symmetry) and a dark
    drop falls from it. Frames 4-7 loop."""
    cx, cy = 15.5, 14.5
    out = []

    def sigil(f: Field, scale: float, shade: int, rot: float = 0.0) -> None:
        R = 6.6 * scale
        e = f.dist(cx, cy)
        f.add("main", _q(1 - np.abs(e - R) / 0.65, [(0, shade)]))
        pts = [(cx + R * 0.9 * math.cos(math.radians(a + rot)), cy + R * 0.9 * math.sin(math.radians(a + rot)))
               for a in (90, 210, 330)]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
            px_, py_ = f.X - x0, f.Y - y0
            dx, dy = x1 - x0, y1 - y0
            t = np.clip((px_ * dx + py_ * dy) / (dx * dx + dy * dy), 0, 1)
            d = np.hypot(px_ - t * dx, py_ - t * dy)
            f.add("main", _q(1 - d / 0.6, [(0, shade - 1)]))
        for a in (90, 210, 330):                                    # barbs outside the ring
            f.add("main", _on(f.spike(cx, cy, a + rot, R, R + 3.2 * scale, 2.2), shade - 1))
        eye = f.dist(cx, cy, 1.8)
        f.add("accent", _q(1 - eye / (2.2 * scale), [(0, shade - 1), (0.5, min(5, shade + 1))]))

    for i in range(8):
        f = Field(32, 32)
        if i == 1:
            f.add("main", np.where((np.abs(f.dist(cx, cy) - 7) < 0.7) & _checker_s(f), 2, EMPTY))
        elif i == 2:
            sigil(f, 1.15, 5)
            f.add("main", _q(1 - np.abs(f.dist(cx, cy) - 11) / 0.6, [(0, 2)]))
        elif i == 3:
            sigil(f, 1.0, 4)
        elif i >= 4:
            sigil(f, 1.0, [4, 3, 3, 4][i - 4])
        c = f.canvas()
        if i <= 1:
            rad = 13 - 5 * i
            for k in range(3):
                a = math.radians(k * 120 + 30 + i * 40)
                for tr in range(3):                                  # a short tail trailing each wisp
                    aa = a - math.radians(tr * 14)
                    _pmax(c, cx + (rad + tr) * math.cos(aa), cy + (rad + tr) * math.sin(aa), "main", 4 - tr)
        if i >= 4:
            k = i - 4
            for j in range(3):
                a = math.radians(j * 120 + k * 30)
                x, y = cx + 10 * math.cos(a), cy + 4.5 * math.sin(a) + 1
                _mote(c, x, y, "main", 4 if math.sin(a) > 0 else 2, big=math.sin(a) > 0)
                aa = a - math.radians(20)
                _pmax(c, cx + 10 * math.cos(aa), cy + 4.5 * math.sin(aa) + 1, "main", 2)
            y = cy + 9 + 2 * k
            _pmax(c, cx, y, "accent", 3 - (k >= 3))
            if k < 2:
                _pmax(c, cx, y - 1, "accent", 2)
        out.append(c)
    return out


# --------------------------------------------------------------------------------------------------------- clouds
def smoke() -> list[Canvas]:
    """A smoke puff: three balls swell out of a small pop, drift up, then fray from their thin edges inward."""
    r = tex.rng(401)
    n = tex.fbm(32, 32, 4, r, octaves=2)
    out = []
    for i in range(7):
        t = i / 6
        g = _ease(min(1, t * 1.6))
        rise = 7 * t
        rad = 2.5 + 4 * g
        blobs = [(16 - 4 * g, 25 - rise, rad * 0.9), (16 + 4 * g, 24 - rise, rad * 0.85), (16, 21 - rise * 1.3, rad)]
        if i == 0:
            blobs = [(16, 25, 3.0)]
        c = Canvas(32, 32, SLOTS)
        _puffs(c, blobs, "smoke", noise=np.roll(n, -i, 0), erode=max(0, (t - 0.4) * 1.1), rough=0.55)
        out.append(c)
    return out


def dust() -> list[Canvas]:
    """A dust puff for landings and footsteps: low, flat clumps roll out to both sides and a few grains hop."""
    r = tex.rng(411)
    n = tex.fbm(32, 32, 4, r, octaves=2)
    grains = [(-1, r.uniform(1.2, 2.0), r.uniform(1.6, 2.2)), (1, r.uniform(1.2, 2.0), r.uniform(1.6, 2.2)),
              (-1, r.uniform(0.6, 1.0), r.uniform(2.2, 2.8)), (1, r.uniform(0.6, 1.0), r.uniform(2.2, 2.8))]
    out = []
    for i in range(6):
        t = i / 5
        g = _ease(min(1, t * 1.5))
        c = Canvas(32, 32, SLOTS)
        blobs = []
        for s in (-1, 1):
            x = 16 + s * (3 + 8 * g)
            blobs += [(x, 26 - 2 * t, 2.8 + 3.2 * g), (x - s * 4 * g, 25 - 3 * t, 2.2 + 2.8 * g)]
        _puffs(c, blobs, "smoke", noise=n, erode=max(0, (t - 0.35) * 1.15), rough=0.5, sy=1.4)
        if 1 <= i <= 4:
            for s, vx, vy in grains:
                tt = i
                x = 16 + s * (3 + vx * tt * 1.8)
                y = 26 - vy * tt * 1.6 + 0.6 * tt * tt
                _pmax(c, x, y, "smoke", 2 if i < 4 else 1)
        out.append(c)
    return out


def poison() -> list[Canvas]:
    """A lingering poison cloud (loop): a low mound of puffs that breathe out of phase, ragged edges rolling
    sideways, bubbles rising and popping. Every motion completes a whole cycle in the eight frames."""
    r = tex.rng(421)
    n = np.tile(tex.fbm(24, 48, 2, r, octaves=2), (1, 2))      # period 24: drifts 3 px a frame and still wraps
    body = np.tile(tex.fbm(24, 48, 2, r, octaves=2), (1, 2))
    wisps = [(8, 24, 0, 4), (30, 20, 3, 5), (19, 18, 5, 3), (36, 27, 6, 4)]
    base = [(13, 37, 7), (23, 33, 9), (33, 36, 7.5), (19, 28, 6.5), (29, 28, 6), (39, 39, 5), (7, 40, 4.5)]
    phases = r.uniform(0, 2 * math.pi, len(base))
    bubbles = [(16, 31, 0), (26, 30, 3), (33, 34, 5), (21, 36, 6)]
    out = []
    for k in range(8):
        w = 2 * math.pi * k / 8
        blobs = [(x, y + 1.0 * math.sin(w + p), rr + 0.7 * math.sin(w + p + 1.3)) for (x, y, rr), p in
                 zip(base, phases)]
        c = Canvas(48, 48, SLOTS)
        _puffs(c, blobs, "main", noise=np.roll(n, 3 * k, 1), erode=0.06, rough=0.7, sy=1.35, base=3,
               gas=np.roll(body, -3 * k, 1))
        for x, y, p, length in wisps:                          # threads of gas curling off the top
            life = (k + p) % 8
            if life < 5:
                xx, yy = x + life, y - life * 0.6
                for j in range(length):
                    _pmax(c, xx + j, yy + (1 if j in (0, length - 1) else 0), "main", 4 - life // 2 - (j == 0))
        for x, y, p in bubbles:
            life = (k + p) % 8
            if life < 4:
                by = y - life * 1.5
                for dx, dy, sh in ((0, -1, 4), (-1, 0, 4), (1, 0, 3), (0, 1, 2)):
                    _pmax(c, x + dx, by + dy, "accent", sh)
                _pmax(c, x - 1, by - 1, "accent", 5) if life >= 2 else None
            elif life == 4:
                by = y - 6.5
                for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
                    _pmax(c, x + dx * 2, by + dy * 2, "accent", 3)
        out.append(c)
    return out


def poof() -> list[Canvas]:
    """A defeat poof: the body pops into a ring of round puffs that bursts outward with a few cartoon stars, then
    frays away. The figure is hidden from the second frame on."""
    cx, cy = 15.5, 19.0
    r = tex.rng(431)
    n = tex.fbm(32, 32, 4, r, octaves=2)
    out = []
    for i in range(7):
        t = i / 6
        c = Canvas(32, 32, SLOTS)
        if i == 0:
            blobs = [(cx, cy, 6.5), (cx - 3, cy + 6, 4.5), (cx + 3, cy + 6, 4.5)]
            erode = -0.1
        else:
            spread = 4 + 7 * _ease(t * 1.4)
            rad = 5.2 - 1.5 * t
            blobs = [(cx + spread * math.cos(math.radians(a)), cy + spread * 0.85 * math.sin(math.radians(a)), rad)
                     for a in range(30, 390, 60)]
            if i <= 2:
                blobs.append((cx, cy, 5.5 - 1.5 * i))
            erode = max(0, (t - 0.3) * 1.2)
        _puffs(c, blobs, "smoke", noise=np.roll(n, -i, 0), erode=erode, rough=0.45, base=4 if i < 2 else 3)
        if 1 <= i <= 4:
            for a, size in ((-60, 2), (200, 1), (40, 1)):
                rad = 8 + 4 * i
                x = cx + rad * math.cos(math.radians(a))
                y = cy + rad * 0.85 * math.sin(math.radians(a))
                _twinkle(c, x, y, "core", size if i < 4 else 0, 5 if i < 3 else 4)
        out.append(c)
    return out


# --------------------------------------------------------------------------------------------------- ground loops
GLYPHS = [["#.#", "###"], ["##.", "#.#"]]


def shield() -> list[Canvas]:
    """A shield bubble around one unit: it pops in (small and bright, overshoot, settle) and then holds as a loop
    (frames 3-8): a rim lit from the top-left, a specular arc, a glint travelling round the rim once per loop and a
    faint band of hex cells sweeping up through it."""
    cx, cy, rx, ry = 23.5, 25.0, 17.0, 20.0
    out = []
    for i in range(9):
        f = Field(48, 48)
        if i < 3:
            s, rim_base = [0.4, 0.82, 1.07][i], [5, 5, 4][i]
        else:
            s, rim_base = 1.0, 3
        sx_, sy_ = rx * s, ry * s
        ccy = 45 - sy_
        ex, ey = (f.X - cx) / sx_, (f.Y - ccy) / sy_
        e = np.hypot(ex, ey)
        th = np.degrees(np.arctan2(ey, ex))
        lit = -(ex + ey) / np.maximum(e, 1e-3) * 0.7071
        rim = (e <= 1.0) & (e > 1 - 1.3 / (sx_ + 1e-3) * 1.0 - 0.02)
        sh = rim_base + (lit > 0.45) - (lit < -0.45)
        if i >= 3:
            glint = np.cos(np.radians(th - 60 * (i - 3) + 120))
            sh = sh + (glint > 0.9)
        f.add("main", np.where(rim, sh, EMPTY))
        if i == 1:
            f.add("main", np.where(np.abs(e - 0.8) < 0.05, 3, EMPTY))
        spec = (np.abs(e - 0.78) < 0.06) & (th > -150) & (th < -112)
        f.add("core", np.where(spec, 5, EMPTY))
        if i >= 3:
            band_y = 45 - (i - 3) / 6 * (2 * ry) - 2
            ys, xs = np.floor(f.Y), np.floor(f.X)
            cells = (ys % 2 == 0) & ((xs + (ys // 2 % 2) * 2) % 4 == 0)
            band = (np.abs(f.Y - band_y) < 2.5) & (e < 0.9) & cells
            f.add("main", np.where(band, 2, EMPTY))
        out.append(f.canvas())
    return out


def pillar() -> list[Canvas]:
    """A pillar of light at 48x96 (bless, smite, banish): motes gather on the ground, a thread of light drops from
    the sky and hits with a white flash and a shockwave, swells to a column with rays streaming up it, then
    narrows away leaving motes drifting up."""
    W, H, cx, gy = 48, 96, 23.5, 90.5
    r = tex.rng(371)
    motes = [(cx + r.uniform(-12, 12), gy - r.uniform(0, 14), r.uniform(2.5, 4.5), r.uniform(4, 8)) for _ in range(14)]
    inward = [(r.uniform(0, 360), r.uniform(14, 20)) for _ in range(10)]
    keys = [(0, 0, 0), (0.7, 1.0, 1), (4.0, 1.0, 0), (8.0, 1.0, 0), (10.0, 1.0, 0), (9.0, 1.0, 0), (7.5, 1.0, 0),
            (5.5, 1.0, 1), (3.5, 1.0, 1), (1.6, 1.0, 2), (0, 0, 0), (0, 0, 0)]
    out = []
    for i, (hw, reach, drop) in enumerate(keys):
        f = Field(W, H, ss=1)
        if hw:
            _beam(f, cx, hw, 0, gy * reach, drop, i)
        if 2 <= i <= 8:
            age = i - 2
            rad = 6 + 3.2 * age
            f.add("main", np.where(_iso_ring(f, cx, gy, rad, 0.7, 0.38), max(1, 4 - age // 2), EMPTY))
            f.add("core", _q(1 - f.dist(cx, gy, 2.6) / (hw + 3), [(0, 4), (0.5, 5)]) - (age >= 4))
        if i == 2:
            _flash(f, cx, gy - 1, 7, 2.6)
        c = f.canvas()
        if i == 0:
            for a, d in inward:
                ra = math.radians(a)
                _pmax(c, cx + d * math.cos(ra), gy + d * 0.4 * math.sin(ra), "main", 3)
            _pmax(c, cx, gy, "core", 4)
        if i == 2:
            _flare(c, cx, gy - 1, 14, vertical=False)
        if i == 4:
            _twinkle(c, cx, gy - 3, "core", 3)
        for x, y0, vy, t0 in motes:
            age = i - t0
            if 0 <= age <= 4:
                y = y0 - vy * age * 2
                _mote(c, x, y, "core" if age < 2 else "main", 5 - int(age), big=age < 1.5)
        out.append(c)
    return out


def magic_circle() -> list[Canvas]:
    """A rune circle on the floor at 64x48 (loop), squashed 2:1 to lie flat on the iso grid: an outer ring with tick
    marks, a band of runes, an inner ring and a hexagram, a hot centre. Runes and ticks turn one glyph pair (45
    degrees) per loop, the hexagram turns back one point (60 degrees), a crest of light runs once round the rim,
    and short shafts of light flicker up off the runes and motes lift away; every motion completes in the twelve
    frames, so it wraps seamlessly."""
    cx, cy = 31.5, 33.5
    N = 12
    out = []
    for k in range(N):
        f = Field(64, 48, ss=1)
        th = np.degrees(np.arctan2((f.Y - cy) * 2, f.X - cx))
        crest = np.cos(np.radians(th - 360 * k / N))
        boost = (crest > 0.7).astype(float) + (crest > 0.95)
        for rx, sh in ((30, 3), (22.5, 3), (6.5, 3)):
            f.add("main", np.where(np.abs(_ring_d(f, cx, cy, rx, rx / 2)) < 0.55, sh + boost, EMPTY))
        f.add("main", np.where(np.abs(_ring_d(f, cx, cy, 27.5, 13.75)) < 0.5, 1 + (crest > 0.6), EMPTY))
        f.add("core", _q(1 - f.dist(cx, cy, 2) / 3.2, [(0, 3), (0.5, 4)]))
        c = f.canvas()
        for base in (-90, 90):
            pts = [(cx + 20 * math.cos(math.radians(base + a - 60 * k / N)),
                    cy + 10 * math.sin(math.radians(base + a - 60 * k / N))) for a in (0, 120, 240)]
            for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
                _lmax(c, x0 - 0.5, y0 - 0.5, x1 - 0.5, y1 - 0.5, "main", 3)
        for j in range(24):                                     # tick marks on the outer ring
            a = math.radians(j * 15 + 45 * k / N)
            if j % 3:
                _pmax(c, cx + 31.5 * math.cos(a), cy + 15.75 * math.sin(a), "main", 2 + (math.sin(a) > 0))
        for j in range(16):
            a = j * 22.5 + 45 * k / N
            gx = cx + 26.25 * math.cos(math.radians(a))
            gy = cy + 13.1 * math.sin(math.radians(a))
            lit = 1 if math.cos(math.radians(a - 360 * k / N)) > 0.7 else 0
            for row, line in enumerate(GLYPHS[j % 2]):
                for col, ch in enumerate(line):
                    if ch == "#":
                        _pmax(c, gx - 1.5 + col, gy - 1 + row, "main", 4 + lit - (row == 1))
        _pmax(c, cx, cy, "core", 5)
        _pmax(c, cx + 1, cy, "core", 5)
        for j in range(8):                                      # shafts of light flickering up off the runes
            life = (k + j * 5) % N
            if life >= 4:
                continue
            a = math.radians(j * 45 + 22.5 + 45 * k / N)
            x, y = cx + 26.25 * math.cos(a), cy + 13.1 * math.sin(a) - 2
            hgt = [3, 7, 9, 6][life]
            front = math.sin(a) > 0
            for d in range(hgt):
                _pmax(c, x, y - d, "core" if d < 2 and life < 2 else "main", (5 if d < 2 else 4 if d < 5 else 3)
                      - (life >= 2) - (not front))
        for j in range(8):                                      # motes lifting off the rim
            life = (k + j * 3) % N
            if life >= 7:
                continue
            a = math.radians(j * 45 + 10)
            x = cx + 30 * math.cos(a) + math.sin(life * 0.9 + j) * 0.8
            y = cy + 15 * math.sin(a) - 1 - life * 2.2
            _pmax(c, x, y, "core" if life < 3 else "main", 5 - life // 2)
        out.append(c)
    return out


def aura() -> list[Canvas]:
    """A buff aura on the floor at 64x64 (loop): an iso ring with a crest of light running round it once per loop,
    a dashed inner ring, streaks of light rising off the rim, and motes spiralling up round the unit, each on its
    own phase of the same twelve-frame cycle."""
    cx, cy = 31.5, 52.5
    N = 12
    r = tex.rng(441)
    streaks = [(i * 360 / 18 + r.uniform(-8, 8), int(r.integers(0, N)), int(r.integers(3, 7))) for i in range(18)]
    spiral = [(j * 60.0, j * 2) for j in range(6)]
    out = []
    for k in range(N):
        f = Field(64, 64, ss=1)
        dy = f.Y - cy
        th = np.degrees(np.arctan2(dy * 2, f.X - cx))
        crest = np.cos(np.radians(th - 360 * k / N))
        ring = _ring_d(f, cx, cy, 25, 12.5)
        sh = 3 + (crest > 0.3) + (crest > 0.85) - (dy < 0) * (crest < 0.3)
        f.add("main", np.where((ring < 0.55) & (ring > -1.45), sh - (ring < -0.45), EMPTY))
        inner = _ring_d(f, cx, cy, 19.5, 9.75)
        f.add("main", np.where((np.abs(inner) < 0.5) & (crest > -0.2) & ((np.floor(th / 12) + k) % 2 == 0), 2,
                               EMPTY))
        glow = _ring_d(f, cx, cy, 22, 11)
        f.add("main", np.where((glow < 0) & (glow > -3) & _checker_s(f) & (crest > 0.5), 1, EMPTY))
        c = f.canvas()
        for a, ph, length in streaks:
            life = (k + ph) % N
            if life >= 6:
                continue
            ra = math.radians(a)
            x = cx + 25 * math.cos(ra)
            y0 = cy + 12.5 * math.sin(ra) - 1 - life * 3.0
            front = math.sin(ra) > 0
            for j in range(length):
                s = (5 if j == 0 else 4 if j == 1 else 3) - (life >= 3) - (life >= 5) - (0 if front else 1)
                _pmax(c, x, y0 + j, "main", s)
        for a0, ph in spiral:                                    # motes spiralling up round the unit
            life = (k + ph) % N
            a = math.radians(a0 + life * 30)
            rr = 22 - life * 0.9
            x, y = cx + rr * math.cos(a), cy - 2 - life * 3.4 + rr * 0.5 * math.sin(a)
            front = math.sin(a) > 0
            _mote(c, x, y, "core" if front and life < 6 else "main", (5 if life < 6 else 3) - (not front),
                  big=front and life < 4)
        out.append(c)
    return out


# ----------------------------------------------------------------------------------------------- damage elements
# One effect per damage type, each with its own shape language so the element reads before the colour does:
# fire licks upward, frost grows facets, lightning forks, necrotic coils and grins, radiant rays out, psychic
# ripples, thunder rings, acid splashes and bubbles, poison clouds, force breaks into hexagons.
def flames() -> list[Canvas]:
    """A target engulfed (fire damage on a unit, burning hands, hellish rebuke): a lick of flame catches at the feet
    with a small flash, tongues of fire climb the body, torn by noise that scrolls upward so they lick rather than
    pulse, embers float off, and the fire sinks back to a few low tongues and drifting wisps."""
    W, H, cx, gy = 48, 64, 23.5, 59.5
    r = tex.rng(461)
    n = tex.fbm(W, H, 4, r, octaves=3, aspect=0.35)
    embers = _sparks(462, 16, cx, gy - 14, (0.6, 1.8), t0=2, life=(4, 8), arc=(200, 340), spread=6, big=0.2)
    hts = [5, 14, 26, 36, 42, 44, 42, 37, 30, 21, 12, 5]
    wid = [7, 10, 13, 14, 15, 15, 15, 14, 13, 11, 9, 7]
    out = []
    for i in range(12):
        f = Field(W, H, ss=1)
        u = (f.X - cx) / wid[i]
        hgt = gy - f.Y
        lick = np.roll(n, -5 * i, 0)
        local = hts[i] * (0.3 + 0.95 * lick) * np.clip(1 - u * u, 0, 1)
        v = 1 - hgt / np.maximum(local, 1e-3)
        v = np.where((hgt >= -1) & (np.abs(u) < 1), v, EMPTY)
        f.add("main", _q(v, [(0.0, 1), (0.12, 2), (0.3, 3), (0.5, 4)]))
        f.add("core", _q(v - np.abs(u) * 0.4, [(0.62, 4), (0.8, 5)]))
        if i == 1:
            _flash(f, cx, gy - 3, 5, 1.6)
        if i <= 3:
            f.add("main", np.where(_iso_ring(f, cx, gy, 8 + 3 * i, 0.6, 0.4), 3 - i // 2, EMPTY))
        c = f.canvas()
        _draw_sparks(c, embers, i, g=-0.06, drag=0.9, top=5)
        if i >= 8:
            for j in range(4):
                x = cx - 9 + j * 6 + math.sin(i + j) * 1.5
                y = gy - hts[i] - 6 - (i - 8) * 3 - j % 2 * 3
                _pmax(c, x, y, "main", 2)
                _pmax(c, x + 1, y - 1, "main", 1)
        out.append(c)
    return out


def frost() -> list[Canvas]:
    """A frost burst on the body (ray of frost, cone of cold, chill): mist rings close in with sparkles, a flash,
    then crystalline shards grow outward as faceted spikes (lit face, shadow face, a bright edge), glint and crack,
    and shatter into glittering fragments that fall through a fading ring of mist."""
    cx, cy = 31.5, 31.5
    r = tex.rng(471)
    shards = [(a + r.uniform(-8, 8), r.uniform(20, 27) * (1 if k % 2 == 0 else 0.6), 8.0 if k % 2 == 0 else 5.0)
              for k, a in enumerate(range(0, 360, 30))]
    frags = []
    for a, length, w in shards:
        for u in (0.45, 0.85):
            ra = math.radians(a)
            frags.append(_Spark(cx + math.cos(ra) * length * u, cy + math.sin(ra) * length * u,
                                math.cos(ra) * r.uniform(1.0, 2.2), math.sin(ra) * r.uniform(1.0, 2.2) - 0.8,
                                7 + r.uniform(0, 0.6), r.uniform(3.5, 5), bool(r.random() < 0.5)))
    grow = [0, 0, 0.25, 0.7, 1.05, 1.0, 1.0, 1.0]
    out = []
    for i in range(12):
        f = Field(64, 64, ss=1)
        if i <= 2:
            rad = 24 - 7 * i
            f.add("accent", np.where(np.abs(f.dist(cx, cy, 1.0) - rad) < 0.7, 2 + i, EMPTY))
        if i == 2:
            _flash(f, cx, cy, 5)
        if i >= 7:
            rad = 16 + 3 * (i - 7)
            ring = np.abs(f.dist(cx, cy) - rad) < 1.2
            f.add("accent", np.where(ring & _checker_s(f), max(1, 3 - (i - 7) // 2), EMPTY))
        c = f.canvas()
        if i <= 2:
            for j in range(8):
                a = math.radians(j * 45 + i * 20)
                rad = 22 - 7 * i
                _twinkle(c, cx + rad * math.cos(a), cy + rad * math.sin(a), "core", 1 if j % 2 else 0, 4 + (i == 2))
        if 2 <= i <= 7:
            g = grow[i]
            for a, length, w in sorted(shards, key=lambda s: math.sin(math.radians(s[0]))):
                ra = math.radians(a)
                ux, uy = math.cos(ra), math.sin(ra)
                px_, py_ = -uy, ux
                tip = (cx + ux * length * g, cy + uy * length * g)
                mid = (cx + ux * length * g * 0.35, cy + uy * length * g * 0.35)
                hw = w / 2 * min(1.0, g + 0.3)
                left = (mid[0] + px_ * hw, mid[1] + py_ * hw)
                right = (mid[0] - px_ * hw, mid[1] - py_ * hw)
                lit_left = (px_ + py_) < 0                              # which face looks up-left
                c.polygon([(cx, cy), left, tip], "main", 4 if lit_left else 2)
                c.polygon([(cx, cy), right, tip], "main", 2 if lit_left else 4)
                _lmax(c, cx, cy, *tip, "main", 5 if i in (4, 5) else 4)
                if i == 5:
                    _twinkle(c, *tip, "core", 1)
                if i == 6:
                    for t in (0.4, 0.7):
                        x, y = cx + (tip[0] - cx) * t, cy + (tip[1] - cy) * t
                        _pmax(c, x + px_, y + py_, "main", 1)
                        _pmax(c, x, y, "main", 1)
            _hexagon(c, cx, cy, 3.5 if i >= 3 else 2, 30, "core", 5, fill=4)
        if i == 6:
            _twinkle(c, cx - 6, cy - 9, "core", 3)
        for p in frags:
            t = i - p.t0
            if 0 <= t <= p.life:
                x, y = _spark_at(p, t, 0.35, 0.85)
                sh = 5 if t < 1.5 else 4 if t < 3 else 2
                _pmax(c, x, y, "main", sh)
                if p.big and t < 3:
                    _pmax(c, x + 1, y, "main", sh - 2)
                    _pmax(c, x, y - 1, "core", sh)
        out.append(c)
    return out


def shock() -> list[Canvas]:
    """Lightning on a body (shocking grasp, witch bolt, lightning damage): a white flash at the chest, then forked
    arcs leap between points round the unit, a new set every frame so they crackle, thinning to a last few
    flickers while sparks spit off and fall."""
    W, H, cx, cy = 48, 64, 23.5, 37.5
    sparks = _sparks(482, 14, cx, cy, (1.6, 3.6), t0=1, life=(3, 6), spread=4)
    out = []
    for i in range(10):
        c = Canvas(W, H, SLOTS)
        f = Field(W, H)
        if i <= 1:
            f.add("main", _q(1 - f.dist(cx, cy) / (6 + 4 * i), [(0, 2), (0.4, 3)]))
            _flash(f, cx, cy, 3.5 + 1.5 * i)
        f.canvas(c)
        r = tex.rng(480 + i)
        arcs = [4, 5, 5, 4, 4, 3, 3, 2, 2, 1][i]
        dim = 0 if i < 5 else 1 if i < 8 else 2
        for _ in range(arcs):
            a0 = r.uniform(0, 2 * math.pi)
            a1 = a0 + r.uniform(2.2, 3.4) * (1 if r.random() < 0.5 else -1)
            rx, ry = 10 + r.uniform(-2, 3), 17 + r.uniform(-3, 3)
            x0, y0 = cx + rx * math.cos(a0), cy + ry * math.sin(a0)
            x1, y1 = cx + rx * math.cos(a1), cy + ry * math.sin(a1)
            strokes = _fork(r, x0, y0, x1, y1, 1, step=2.6, jag=1.8, chance=0.25)
            _bolt_paint(c, strokes, halo=i < 2, hot=i < 3, dim=dim)
        _draw_sparks(c, sparks, i, g=0.3)
        out.append(c)
    return out


SKULL = ["..#####..", ".#######.", "#########", "#..###..#", "#..###..#", "####.####", ".#######.",
         "..#.#.#..", "..#####.."]


def _skull(c: Canvas, cx: float, cy: float, shade: int, eyes: int, slot: str = "core") -> None:
    """A 9x9 skull, lit from the top-left, with glowing pinprick eyes in dark sockets."""
    x0, y0 = int(cx) - 4, int(cy) - 4
    for y, row in enumerate(SKULL):
        for x, ch in enumerate(row):
            if ch == "#":
                lit = (x + y < 6) - (x > 6 or y > 6)
                c.px(x0 + x, y0 + y, slot, max(1, min(5, shade + lit)))
            elif 3 <= y <= 4 and row[x] == ".":
                c.px(x0 + x, y0 + y, "smoke", 0)
    if eyes:
        c.px(x0 + 1, y0 + 3, "main", eyes)
        c.px(x0 + 7, y0 + 3, "main", eyes)
    c.px(x0 + 4, y0 + 5, "smoke", 1)


def necrotic() -> list[Canvas]:
    """Necrotic harm (toll the dead, chill touch, inflict wounds, life drain): green-black wisps spiral in round the
    target trailing smoke, close into a dark knot, a grinning skull flares green in it, then the skull dissolves
    upward and the wisps unwind and drift off. Drawn for normal blending: dark magic has to darken."""
    cx, cy = 23.5, 33.5
    N = 12
    r = tex.rng(491)
    n = tex.fbm(48, 64, 4, r, octaves=2)
    wisps = [(k * 72 + r.uniform(-10, 10), r.uniform(0.9, 1.1)) for k in range(5)]
    out = []
    for i in range(N):
        c = Canvas(48, 64, SLOTS)
        knot = [0, 0, 0.3, 0.7, 1.0, 1.0, 0.9, 0.75, 0.55, 0.35, 0.15, 0][i]
        if knot:
            blobs = [(cx + 5 * math.cos(a), cy + 4 * math.sin(a) - (i - 6) * 1.2 * (i > 6), 5 * knot + 1)
                     for a in (0.3, 2.4, 4.4)]
            _puffs(c, blobs, "smoke", noise=np.roll(n, -2 * i, 0), erode=max(0, (i - 7) * 0.12), rough=0.6,
                   base=2)
        for a0, sp in wisps:                                     # each wisp a tapered trail of points
            if i <= 4:
                rad, ang = (22 - 4.2 * i) * sp, a0 + i * 55
            elif i >= 7:
                rad, ang = (6 + 4.5 * (i - 7)) * sp, a0 + i * 40
            else:
                continue
            pts = []
            for j in range(7):
                a = math.radians(ang - j * 13)
                rr = rad + j * (1.4 if i <= 4 else -0.8)
                pts.append((cx + rr * math.cos(a), cy + rr * 0.8 * math.sin(a) - (i - 7) * 2 * (i >= 7)))
            for j in range(6, 0, -1):                             # tail first, so the head paints over it
                (x0, y0), (x1, y1) = pts[j], pts[j - 1]
                if j < 3:
                    _lmax(c, x0, y0, x1, y1, "main", 4 - j + (i < 4) - (i >= 9))
                else:
                    _lmax(c, x0, y0, x1, y1, "smoke", 3 - (j > 4))
                    _lmax(c, x0, y0 + 1, x1, y1 + 1, "smoke", 1)
            _mote(c, *pts[0], "core" if i < 4 else "main", 5 - (i >= 9), big=i < 9)
        if 4 <= i <= 9:
            lift = max(0, i - 6) * 2
            sk = Canvas(48, 64, SLOTS)
            _skull(sk, cx, cy - 2 - lift, [3, 4, 4, 3, 3, 2][i - 4], [5, 5, 4, 5, 4, 3][i - 4])
            if i >= 7:
                gone = tex.rng(500 + i).random((64, 48)) < (i - 6) * 0.22
                sk.slot[gone & (np.arange(64)[:, None] < cy + 3 - lift)] = 0
            c.blit(sk)
            if i == 4:
                f = Field(48, 64)
                f.add("main", np.where(np.abs(f.dist(cx, cy - 2) - 8) < 0.7, 4, EMPTY))
                f.canvas(c)
        out.append(c)
    return out


def radiant() -> list[Canvas]:
    """Radiant light on a target (sacred flame, guiding bolt's hit, divine smite): motes stream in, a white flash,
    then twelve rays burst out and turn slowly as they shorten, a halo ring opens behind them, and flecks of light
    drift down like dust in a sunbeam."""
    cx, cy = 31.5, 31.5
    r = tex.rng(501)
    flecks = [(cx + r.uniform(-20, 20), cy + r.uniform(-24, 6), r.uniform(0.6, 1.4), r.uniform(0, 6.28))
              for _ in range(16)]
    inward = [(a, r.uniform(20, 28)) for a in np.linspace(0, 360, 10, endpoint=False)]
    out = []
    for i in range(12):
        f = Field(64, 64)
        L = [0, 30, 28, 25, 21, 16, 11, 6, 0, 0, 0, 0][i]
        rot = i * 3
        for k in range(12):
            if not L:
                break
            length = L if k % 2 == 0 else L * 0.6
            u = f.spike(cx, cy, rot + k * 30, 2 + max(0, i - 3) * 1.5, length, 3.6 if k % 2 == 0 else 2.4)
            f.add("main", _on(u, np.where(u < 0.6, 4, 3) - (i >= 5)))
            f.add("core", _on(u, np.where(u < 0.3, 5, EMPTY)))
        if 2 <= i <= 8:
            rad = 6 + 2.6 * (i - 2)
            f.add("accent", np.where(np.abs(f.dist(cx, cy) - rad) < 0.8, max(1, 4 - (i - 2) // 2), EMPTY))
        if 1 <= i <= 5:
            f.add("core", _q(1 - f.dist(cx, cy) / (7 - i), [(0, 4), (0.4, 5)]))
        if i in (1, 2):
            _flash(f, cx, cy, 6 - 2 * (i - 1))
        c = f.canvas()
        if i == 0:
            for a, d in inward:
                ra = math.radians(a)
                _lmax(c, cx + d * math.cos(ra), cy + d * math.sin(ra), cx + (d - 4) * math.cos(ra),
                      cy + (d - 4) * math.sin(ra), "main", 3)
            _twinkle(c, cx, cy, "core", 2)
        if i == 1:
            _flare(c, cx, cy, 20)
        for x0, y0, vy, ph in flecks:
            age = i - 4
            if 0 <= age < 8:
                x = x0 + math.sin(ph + age * 0.7) * 1.5
                y = y0 + vy * age
                if age < 3:
                    _twinkle(c, x, y, "accent", 1, 5)
                else:
                    _pmax(c, x, y, "accent", 5 - (age - 3) // 2)
        out.append(c)
    return out


def psychic() -> list[Canvas]:
    """A psychic blow (vicious mockery, dissonant whispers, mind sliver): rings ripple out from the target's head,
    each one wobbling as it spreads so it reads as a mind being shaken rather than a shockwave, with jagged static
    sparking between them and an eye that flashes open and shut at the centre."""
    cx, cy = 31.5, 23.5
    r = tex.rng(511)
    static = [(r.uniform(0, 360), r.uniform(6, 26), int(r.integers(1, 10))) for _ in range(14)]
    births = [0, 2, 4, 6]
    out = []
    for i in range(12):
        f = Field(64, 48)
        th = np.radians(f.ang(cx, cy, 1.6))
        for b in births:
            age = i - b
            if age < 0 or age > 6:
                continue
            rad = 4 + age * 4.4
            wob = rad * (1 + 0.1 * np.sin(5 * th + age * 1.7 + b))
            d = np.abs(f.dist(cx, cy, 1.6) - wob)
            sh = max(1, 5 - age // 2 - (age > 4))
            f.add("main", np.where(d < 0.75, sh, EMPTY))
        if 1 <= i <= 4:
            eye = f.dist(cx, cy, [0, 2.6, 1.6, 1.6, 3.2][i])
            f.add("main", _q(1 - eye / 4.5, [(0, 3), (0.4, 4)]))
            f.add("core", _q(1 - f.dist(cx, cy) / 1.6, [(0, 5)]))
        if i == 1:
            _flash(f, cx, cy, 3)
        c = f.canvas()
        for a, d, t0 in static:
            if t0 <= i < t0 + 2:
                ra = math.radians(a)
                x, y = cx + d * math.cos(ra), cy + d * math.sin(ra) / 1.6
                pts = [(x, y), (x + 2, y - 1), (x + 1, y + 1), (x + 3, y)]
                for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
                    _lmax(c, x0, y0, x1, y1, "core", 5 - (i - t0))
        out.append(c)
    return out


def thunder() -> list[Canvas]:
    """A thunderclap (thunderwave, thunderclap, shatter's ring): a white flash at chest height, then three
    concentric shockwaves race out over the ground one after another, a dome of compressed air flickers above
    them, and pebbles are kicked out. Concentric shock, not fire: no smoke, no glow body, just rings."""
    W, H, cx, gy = 96, 64, 47.5, 48.0
    pebbles = _sparks(521, 16, cx, gy, (2.0, 4.0), t0=1.5, arc=(0, 360), sy=0.45, up=2.0, life=(3, 6), big=0.5)
    births = [1, 3, 5]
    out = []
    for i in range(12):
        f = Field(W, H, ss=1)
        for j, b in enumerate(births):
            age = i - b
            if age < 0 or age > 6:
                continue
            rad = 6 + 38 * _ease(age / 6)
            d = _ring_d(f, cx, gy, rad, rad * 0.45)
            sh = max(1, 5 - age // 2 - j)
            front = (d > -0.6) & (d < 0.9)
            behind = (d > -3.0) & (d <= -0.6) & _checker_s(f)
            if age > 3:
                front &= (np.floor(f.ang(cx, gy, 2.2) / 10) % 3) < 2
            f.add("main", np.where(front, sh, np.where(behind, max(1, sh - 2), EMPTY)))
        if 1 <= i <= 5:                                           # the dome of air over the blast
            rad = 8 + (i - 1) * 7
            d = np.abs(f.dist(cx, gy - 4, 1.0 / 0.6) - rad)
            dome = (d < 0.6) & (f.Y < gy - 3)
            f.add("core", np.where(dome & ((np.floor(f.X) + i) % 3 > 0), max(1, 4 - (i - 1) // 2), EMPTY))
        if i <= 1:
            _flash(f, cx, gy - 14, 5 + 2 * i)
            f.add("main", _q(1 - f.dist(cx, gy - 14) / 9, [(0, 2)]))
        c = f.canvas()
        if i == 1:
            _flare(c, cx, gy - 14, 16)
        for p in pebbles:
            t = i - p.t0
            if 0 <= t <= p.life:
                x, y = _spark_at(p, t, 0.45, 0.85)
                if y <= gy + 6:
                    _mote(c, x, y, "main", 4 - int(t) // 2, big=p.big and t < 3)
        out.append(c)
    return out


def acid() -> list[Canvas]:
    """An acid splash (acid splash, acid spit): a glob bursts on the chest with a wet highlight, droplets spray out
    on arcs and patter to the ground, acid runs down the target, and a puddle spreads at its feet where bubbles
    swell and pop and thin fumes rise. Normal blending: it's a liquid, not light."""
    W, H, cx, cy, gy = 48, 64, 23.5, 38.5, 59.5
    drops = _sparks(531, 16, cx, cy, (2.4, 4.4), t0=1, arc=(180, 360), up=1.5, life=(5, 8), big=0.8)
    r = tex.rng(532)
    bubbles = [(cx + r.uniform(-10, 10), gy + r.uniform(-1.5, 1.5), int(r.integers(4, 11))) for _ in range(8)]
    out = []
    for i in range(12):
        c = Canvas(W, H, SLOTS)
        f = Field(W, H, ss=1)
        pud = [0, 0, 0, 3, 6, 9, 11, 12, 12, 12, 11, 10][i]
        if pud:
            e = f.dist(cx, gy, 2.4) / pud
            f.add("main", _q(1 - e, [(0, 2), (0.25, 3)]))
            f.add("core", np.where((np.abs(e - 0.55) < 0.12) & (f.Y < gy), 4, EMPTY))
        if i <= 2:
            rr = [6.5, 9.5, 7][i]
            sy = [1.0, 1.5, 2.1][i]
            e = f.dist(cx, cy, sy) / rr
            f.add("main", _q(1 - e, [(0, 2), (0.2, 3), (0.5, 4)]))
            f.add("core", _q(1 - f.dist(cx - rr * 0.35, cy - rr * 0.3 / sy) / (rr * 0.35), [(0, 5)]))
        if 2 <= i <= 8:                                          # acid running down the body
            for x0, ln in ((cx - 4, 1.0), (cx + 4, 0.7)):
                yy = cy + 2 + (i - 2) * 3.2 * ln
                run = (np.abs(f.X - x0 - np.sin(f.Y * 0.5)) < 1.0) & (f.Y > cy) & (f.Y < yy) & (f.Y < gy - 1)
                f.add("main", np.where(run, 3, EMPTY))
                f.add("core", np.where((np.abs(f.X - x0 - np.sin(f.Y * 0.5)) < 1.3) & (np.abs(f.Y - yy) < 1.3), 4,
                                       EMPTY))
        c = f.canvas(c)
        for p in drops:
            t = i - p.t0
            if 0 <= t <= p.life:
                x, y = _spark_at(p, t, 0.5, 0.88)
                if y > gy:
                    continue
                _pmax(c, x, y, "main", 3)
                if p.big:
                    _pmax(c, x + 1, y, "main", 2)
                    _pmax(c, x, y + 1, "main", 2)
                _pmax(c, x - (p.big and 0), y - (p.big and 1), "core", 4)
        for x, y, t0 in bubbles:
            age = i - t0
            if 0 <= age < 2:
                _pmax(c, x, y - 1, "core", 4)
                if age == 1:
                    for dx, dy in ((-1, 0), (1, 0), (0, -2)):
                        _pmax(c, x + dx, y - 1 + dy, "main", 4)
            elif age == 2:
                for dx, dy in ((-2, -1), (2, -1), (0, -3)):
                    _pmax(c, x + dx, y + dy, "core", 3)
        if i >= 5:
            for j in range(3):
                life = (i - 5 + j * 2) % 6
                x = cx - 6 + j * 6 + math.sin(life + j) * 1.2
                _pmax(c, x, gy - 3 - life * 3, "accent", 3 - life // 2)
                _pmax(c, x + 1, gy - 4 - life * 3, "accent", 2 - life // 3)
        out.append(c)
    return out


def toxin() -> list[Canvas]:
    """A poison spray on a target (poison spray, ray of sickness, venom): a spray of droplets hits from the left,
    a sickly cloud blooms where it lands and rolls outward with bubbles, then frays away. Normal blending."""
    W = 48
    cx, cy = 23.5, 25.5
    r = tex.rng(541)
    n = tex.fbm(W, W, 4, r, octaves=2)
    body = tex.fbm(W, W, 3, r, octaves=2)
    spray = [(r.uniform(-3, 3), r.uniform(0, 1)) for _ in range(14)]
    drips = _sparks(542, 10, cx, cy, (1.2, 2.6), t0=2, arc=(200, 340), up=0.5, life=(4, 7), big=0.4)
    out = []
    for i in range(10):
        c = Canvas(W, W, SLOTS)
        g = [0, 0, 0.35, 0.7, 0.95, 1.05, 1.1, 1.12, 1.12, 1.1][i]
        if g:
            blobs = [(cx - 7 * g, cy + 2, 5 * g + 1), (cx + 6 * g, cy + 1, 5.5 * g + 1), (cx, cy - 4 * g, 6 * g + 1),
                     (cx + 2, cy + 4, 4.5 * g + 1), (cx - 3 * g, cy - 7 * g, 3.5 * g + 1)]
            _puffs(c, blobs, "main", noise=np.roll(n, -i, 0), erode=max(0, (i - 5) * 0.14), rough=0.6, sy=1.1,
                   gas=np.roll(body, 2 * i, 1))
        for dy, ph in spray:                                       # the spray arriving from the left
            if i <= 2:
                x = cx - 20 + i * 9 + ph * 6
                _lmax(c, x - 3, cy + dy * (1 - i * 0.3), x, cy + dy * (1 - i * 0.3), "accent", 3 + i // 2)
        _draw_sparks(c, drips, i, hot="accent", slot="main", g=0.35)
        out.append(c)
    return out


def force() -> list[Canvas]:
    """A force impact (magic missile, eldritch blast's hit, spiritual weapon): a hexagon of force snaps into being,
    flashes and spreads into a honeycomb, cracks from the centre, then breaks into hexagonal shards that spin out
    and fade. Hexagons are force's mark across the set."""
    W = 48
    cx = cy = 23.5
    cells = []
    for q in range(-2, 3):
        for rr in range(-2, 3):
            if abs(q + rr) <= 2:
                x = cx + 5.2 * (q + rr / 2)
                y = cy + 4.5 * rr
                cells.append((x, y))
    r = tex.rng(551)
    shards = [(x, y, (x - cx) * 0.28 + r.uniform(-0.4, 0.4), (y - cy) * 0.28 + r.uniform(-0.6, 0.2),
               r.uniform(-40, 40)) for x, y in cells]
    out = []
    for i in range(10):
        c = Canvas(W, W, SLOTS)
        if i == 0:
            _hexagon(c, cx, cy, 4, 30, "main", 4, fill=3)
            _pmax(c, cx, cy, "core", 5)
        elif i == 1:
            f = Field(W, W)
            f.add("main", _q(1 - f.dist(cx, cy) / 13, [(0, 2)]))
            _flash(f, cx, cy, 6)
            f.canvas(c)
            for x, y in cells:
                _hexagon(c, x, y, 3, 30, "main", 4)
            _flare(c, cx, cy, 14)
        elif i == 2:
            for x, y in cells:
                _hexagon(c, x, y, 3, 30, "main", 5 if (x, y) == (cx, cy) else 4, fill=2)
            for a in (20, 100, 160, 250, 310):
                ra = math.radians(a)
                _lmax(c, cx, cy, cx + 12 * math.cos(ra), cy + 12 * math.sin(ra), "core", 5)
        else:
            t = i - 2
            for x, y, vx, vy, spin in shards:
                k = (1 - 0.82 ** t) / 0.18
                sx, sy = x + vx * k * 2.2, y + vy * k * 2.2 + 0.15 * t * t
                size = max(0.0, 3.2 - t * 0.3)
                sh = 5 - t // 3
                if sh <= 1:
                    continue
                if size >= 1.2:
                    _hexagon(c, sx, sy, size, 30 + spin * t, "main", sh, fill=max(1, sh - 2))
                else:
                    _pmax(c, sx, sy, "core", sh)
            if t <= 2:
                f = Field(W, W)
                f.add("main", np.where(np.abs(f.dist(cx, cy) - (10 + 5 * t)) < 0.7, 4 - t, EMPTY))
                f.canvas(c)
        out.append(c)
    return out


# ------------------------------------------------------------------------------------------------ boons and wards
def restore() -> list[Canvas]:
    """A cleansing (lesser and greater restoration, remove curse): a halo of light forms over the head and sinks
    down the body like a wiping ring, shedding sparkles that fall, then flashes at the feet and sends a few motes
    back up. Downward motion, so it never reads as a heal (which rises)."""
    W, H, cx, gy = 48, 80, 23.5, 74.5
    r = tex.rng(561)
    falls = [(r.uniform(-12, 12), r.uniform(0, 360)) for _ in range(14)]
    out = []
    for i in range(12):
        f = Field(W, H, ss=1)
        y = [22, 22, 30, 40, 50, 60, 68, 74, 74, 0, 0, 0][i]
        rad = [5, 10, 13, 14, 14, 14, 15, 17, 20, 0, 0, 0][i]
        if y:
            ring = _iso_ring(f, cx, y, rad, 0.65, 0.4)
            front = f.Y > y
            f.add("main", np.where(ring, np.where(front, 5, 3) - (i >= 8), EMPTY))
            band = (f.Y > y - 2) & (f.Y < y + 2) & (np.abs(f.X - cx) < rad - 1) & _checker_s(f)
            f.add("main", np.where(band & (i >= 2), 2, EMPTY))
        if i == 7:
            _flash(f, cx, gy, 6, 2.4)
        c = f.canvas()
        if 2 <= i <= 11:
            for dx, ph in falls:
                age = (i - 2) - (ph % 3)
                if age < 0:
                    continue
                y0 = [22, 22, 30, 40, 50, 60, 68, 74, 74, 74, 74, 74][i] - 2
                x = cx + dx + math.sin(math.radians(ph) + age) * 0.8
                yy = y0 - 6 + age * 3 + (ph % 10)
                if yy < gy and age < 6:
                    _pmax(c, x, yy, "core" if age < 2 else "main", 5 - age // 2)
        if i >= 8:
            for j, dx in enumerate((-10, -4, 3, 9)):
                age = i - 8
                _mote(c, cx + dx, gy - 6 - age * 5 - (j % 2) * 3, "core", 5 - age, big=age < 2)
        if i == 1:
            _twinkle(c, cx + 8, 20, "core", 2)
        out.append(c)
    return out


def ward() -> list[Canvas]:
    """A protective ward (shield of faith, mage armor, resistance, death ward): six rune plates fly in from all
    round, lock into a ring about the unit with a flash, glow as the bubble between them lights, then fade to a
    shimmer of the bubble's outline."""
    W, H, cx, cy = 48, 64, 23.5, 37.5
    out = []
    for i in range(12):
        f = Field(W, H)
        if 4 <= i <= 9:
            e = np.hypot((f.X - cx) / 17, (f.Y - cy) / 23)
            lit = -((f.X - cx) / 17 + (f.Y - cy) / 23) / np.maximum(e, 1e-3) * 0.7
            rim = (e <= 1) & (e > 0.93)
            sh = (4 if i == 4 else 3 if i < 7 else 2) + (lit > 0.4) - (lit < -0.4)
            f.add("main", np.where(rim & ((i < 7) | _checker_s(f)), sh, EMPTY))
        if i == 4:
            _flash(f, cx, cy, 4)
        c = f.canvas()
        if i <= 8:
            d = [3.0, 2.0, 1.4, 1.08, 1.0, 1.0, 1.0, 1.0, 1.0][i]
            for k in range(6):
                a = math.radians(k * 60 + 30 + (1 - min(1, d - 0.0)) * 0 + (3 - d) * 25)
                x = cx + 17 * d * math.cos(a)
                y = cy + 9 * d * math.sin(a) - 2
                front = math.sin(a) > 0
                sh = (5 if i in (4, 5) else 4) - (not front) - (i >= 7)
                _hexagon(c, x, y, 3.2, 0, "main", sh, fill=max(1, sh - 2))
                _pmax(c, x, y, "core", sh)
                _pmax(c, x, y - 1, "core", sh - 1)
        if i == 4:
            _flare(c, cx, cy, 12)
        if i >= 9:
            for k in range(6):
                a = math.radians(k * 60 + 30 + 50)
                _pmax(c, cx + 17 * math.cos(a), cy - 2 + 9 * math.sin(a) - (i - 9) * 2, "core", 4 - (i - 9))
        out.append(c)
    return out


def bless() -> list[Canvas]:
    """A blessing (bless, aid, heroism, bardic boons): a halo ring blooms over the head with a twinkle, golden motes
    sift down from it past the body, and a soft ring of light answers at the feet."""
    W, H, cx, gy = 48, 80, 23.5, 74.5
    hy = gy - 46
    r = tex.rng(571)
    motes = [(cx + r.uniform(-11, 11), hy + r.uniform(-2, 4), r.uniform(0, 4), r.uniform(2.6, 4.2), r.uniform(0, 6.28),
              r.random() < 0.3) for _ in range(18)]
    out = []
    for i in range(12):
        f = Field(W, H, ss=1)
        rad = [3, 7, 9, 10, 10, 10, 10, 10, 9, 8, 0, 0][i]
        if rad:
            front = f.Y > hy
            f.add("main", np.where(_iso_ring(f, cx, hy, rad, 0.6, 0.35), np.where(front, 5, 4) - (i >= 7), EMPTY))
            f.add("main", np.where(_iso_ring(f, cx, hy, rad + 2, 0.5, 0.35) & _checker_s(f), 2, EMPTY))
        grad = [0, 0, 0, 6, 10, 13, 15, 16, 16, 15, 13, 0][i]
        if grad:
            f.add("main", np.where(_iso_ring(f, cx, gy, grad, 0.6, 0.42), 3 - (i >= 9), EMPTY))
        c = f.canvas()
        if i == 1:
            _twinkle(c, cx + 7, hy - 3, "core", 3)
        for x0, y0, t0, vy, ph, big in motes:
            age = i - 1 - t0
            if 0 <= age <= 7:
                x = x0 + math.sin(ph + age * 0.9) * 1.6
                y = y0 + vy * age
                if y < gy:
                    if age < 3 and big:
                        _twinkle(c, x, y, "core", 1, 5)
                    else:
                        _pmax(c, x, y, "core" if age < 3 else "main", 5 - age // 3)
        out.append(c)
    return out


NOTE_GLYPH = ["..###", "..#+#", "..#..", "..#..", "###..", "##+..", "###.."]


def inspire() -> list[Canvas]:
    """Inspiration (bardic inspiration, heroism, rally): a ring of gold flashes round the chest and a burst of
    notes flies out from it, each note then floating up and fading with a sparkle at its tail."""
    W, H, cx, cy = 48, 64, 23.5, 37.5
    r = tex.rng(581)
    notes = [(a + r.uniform(-15, 15), r.uniform(1.8, 2.8)) for a in (200, 240, 280, 320, 340, 20, 160)]
    out = []
    for i in range(12):
        f = Field(W, H)
        if i <= 4:
            rad = 4 + 4 * i
            f.add("main", np.where(np.abs(f.dist(cx, cy, 1.4) - rad) < 0.7, 5 - i, EMPTY))
        if i == 1:
            _flash(f, cx, cy, 4)
        c = f.canvas()
        for a, sp in notes:
            t = i - 1
            if t < 0:
                continue
            k = (1 - 0.75 ** t) / 0.25
            x = cx + math.cos(math.radians(a)) * sp * k * 2.2
            y = cy + math.sin(math.radians(a)) * sp * k * 1.4 - max(0, t - 3) * 2.2
            sh = 5 if t < 4 else 4 if t < 7 else 3 if t < 9 else 2
            for dy, row in enumerate(NOTE_GLYPH):
                for dx, ch in enumerate(row):
                    if ch != ".":
                        _pmax(c, x - 2 + dx, y - 3 + dy, "core" if ch == "+" else "main", sh)
            if 2 <= t <= 8:
                _pmax(c, x - 1, y + 4 + t % 2, "core", sh - 1)
        out.append(c)
    return out


def rage() -> list[Canvas]:
    """A rage or war cry (rage, action surge, warcry, frenzy): a red flash at the chest, then a shell of flame
    flares up round the unit's outline (hollow in the middle so the unit shows), heat shimmer streaks climb out of
    it, and it gutters down. Fire-shaped like flames, but a hollow shell and blood-red, so it reads as a state."""
    W, H, cx, gy = 48, 64, 23.5, 59.5
    r = tex.rng(591)
    n = tex.fbm(W, H, 4, r, octaves=3, aspect=0.35)
    streaks = _sparks(592, 14, cx, gy - 20, (0.3, 0.8), t0=2, arc=(250, 290), life=(3, 6), spread=6, big=0)
    hts = [0, 30, 42, 48, 50, 50, 48, 44, 38, 28, 16, 6]
    out = []
    for i in range(12):
        f = Field(W, H, ss=1)
        if hts[i]:
            u = (f.X - cx) / 15
            hgt = gy - f.Y
            lick = np.roll(n, -6 * i, 0)
            local = hts[i] * (0.45 + 0.75 * lick) * np.clip(1 - u * u, 0, 1)
            shell = np.clip(np.abs(u) * 1.6, 0, 1)                    # hollow centre: the shell wraps the unit
            v = (1 - hgt / np.maximum(local, 1e-3)) * shell
            v = np.where((hgt >= -1) & (np.abs(u) < 1), v, EMPTY)
            f.add("main", _q(v, [(0.02, 1), (0.12, 2), (0.26, 3), (0.42, 4)]))
            f.add("core", _q(v, [(0.58, 4), (0.72, 5)]))
        if i in (0, 1):
            _flash(f, cx, gy - 22, [3, 6][i])
            f.add("main", _q(1 - f.dist(cx, gy - 22) / [8, 14][i], [(0, 2), (0.4, 3)]))
        c = f.canvas()
        if i == 1:
            _flare(c, cx, gy - 22, 14, vertical=False)
        for p in streaks:
            t = i - p.t0
            if 0 <= t <= p.life:
                x, y = _spark_at(p, t, -0.25, 0.95)
                for d in range(3):
                    _pmax(c, x, y + d, "core" if d == 0 else "main", 4 - d)
        out.append(c)
    return out


def hold() -> list[Canvas]:
    """Binding magic (hold person, hold monster, command): three rings of runes close in round the unit at chest,
    waist and knee height, lock tight with a flash, then pulse and tremble as the hold takes."""
    W, H, cx, cy = 48, 64, 23.5, 37.5
    levels = [-10, 0, 10]
    out = []
    for i in range(10):
        f = Field(W, H, ss=1)
        rad = [21, 17, 13.5, 11, 10, 10.5, 10, 10.5, 10, 10][i]
        for j, dy in enumerate(levels):
            y = cy + dy + (0 if i < 5 else (1 if (i + j) % 2 else 0))
            rr = rad - abs(dy) * 0.12
            ring = _iso_ring(f, cx, y, rr, 0.65, 0.35)
            front = f.Y > y
            sh = np.where(front, 5 if i == 4 else 4, 2) - (i >= 8)
            f.add("main", np.where(ring, sh, EMPTY))
        if i == 4:
            _flash(f, cx, cy, 3.5)
        c = f.canvas()
        for j, dy in enumerate(levels):                            # rune ticks riding each ring
            y = cy + dy
            rr = rad - abs(dy) * 0.12
            for k in range(6):
                a = math.radians(k * 60 + i * (12 if i < 4 else 4) * (1 if j % 2 else -1))
                if math.sin(a) > -0.2:
                    _pmax(c, cx + rr * math.cos(a), y + rr * 0.35 * math.sin(a) - 1, "core", 5 if i >= 4 else 4)
        if i == 4:
            _flare(c, cx, cy, 10)
        out.append(c)
    return out


def dispel() -> list[Canvas]:
    """Dispelling (dispel magic, counterspell): a sigil of the spell being undone appears (a ring, runes, a
    triangle), cracks across, and shatters into angular shards that fly apart, with a flash at the break."""
    W = 48
    cx = cy = 23.5
    r = tex.rng(601)
    shards = _sparks(602, 18, cx, cy, (1.8, 3.6), t0=4, life=(3, 6), big=0.7)
    out = []
    for i in range(10):
        f = Field(W, W)
        if i <= 4:
            s = [0.5, 0.9, 1.0, 1.0, 1.0][i]
            sh = [3, 5, 4, 4, 5][i]
            f.add("main", np.where(np.abs(f.dist(cx, cy) - 13 * s) < 0.7, sh, EMPTY))
            f.add("main", np.where(np.abs(f.dist(cx, cy) - 9 * s) < 0.55, sh - 1, EMPTY))
        if i == 4:
            _flash(f, cx, cy, 5)
        if i == 5:
            f.add("main", np.where(np.abs(f.dist(cx, cy) - 16) < 0.8, 3, EMPTY))
        c = f.canvas()
        if 1 <= i <= 4:
            pts = [(cx + 12 * math.cos(math.radians(a)), cy + 12 * math.sin(math.radians(a))) for a in (-90, 30, 150)]
            for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
                _lmax(c, x0, y0, x1, y1, "main", 4)
            for k in range(8):
                a = math.radians(k * 45 + 22.5)
                _pmax(c, cx + 11 * math.cos(a), cy + 11 * math.sin(a), "core", 5 if (k + i) % 3 else 3)
        if i in (3, 4):
            for a, ln in ((15, 14), (130, 12), (250, 13)):
                pts = _jag(r, cx, cy, cx + ln * math.cos(math.radians(a)), cy + ln * math.sin(math.radians(a)),
                           step=2, jag=1.0)
                for x, y in pts:
                    _pmax(c, x, y, "core" if i == 4 else "accent", 5)
        if i == 4:
            _flare(c, cx, cy, 14)
        for p in shards:
            t = i - p.t0
            if 0 <= t <= p.life:
                x, y = _spark_at(p, t, 0.3, 0.84)
                sh = 5 if t < 1.5 else 4 if t < 3 else 2
                a = math.atan2(p.vy, p.vx) + t
                _lmax(c, x, y, x + 2 * math.cos(a), y + 2 * math.sin(a), "main", sh)
                _pmax(c, x, y, "core", sh)
        out.append(c)
    return out


def web() -> list[Canvas]:
    """A web thrown over a tile (web, spider web): strands shoot out from the centre to eight anchor points, the
    spiral threads appear ring by ring, and the whole web sags and trembles as it settles, glints running along
    it. Normal blending, pale silk."""
    W, H, cx, cy = 64, 48, 31.5, 33.5
    spokes = [k * 45 + 10 for k in range(8)]
    out = []
    for i in range(10):
        c = Canvas(W, H, SLOTS)
        reach = [0.35, 0.75, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0][i]
        sag = [0, 0, 0, 1.5, 1.0, 0.5, 0.8, 0.4, 0.6, 0.5][i]
        for a in spokes:
            ra = math.radians(a)
            x1, y1 = cx + 28 * reach * math.cos(ra), cy + 14 * reach * math.sin(ra)
            _lmax(c, cx, cy, x1, y1, "main", 4 if math.sin(ra) > 0 else 3)
            if i <= 1:
                _pmax(c, x1, y1, "core", 5)
        rings = [0, 0, 1, 2, 3, 4, 4, 4, 4, 4][i]
        for j in range(rings):
            rr = 7 + j * 6
            pts = []
            for a in spokes + [spokes[0] + 360]:
                ra = math.radians(a)
                pts.append((cx + rr * math.cos(ra), cy + rr * 0.5 * math.sin(ra) + sag * (1 if j % 2 else 0.6)))
            for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
                mx, my = (x0 + x1) / 2, (y0 + y1) / 2 + sag
                _lmax(c, x0, y0, mx, my, "main", 3)
                _lmax(c, mx, my, x1, y1, "main", 3)
        if i >= 3:
            k = (i - 3) * 2
            for a in spokes[::2]:
                ra = math.radians(a)
                _pmax(c, cx + (6 + k * 3) * math.cos(ra), cy + (3 + k * 1.5) * math.sin(ra), "core", 5)
        _pmax(c, cx, cy, "core", 5)
        c.outline()
        out.append(c)
    return out


# ------------------------------------------------------------------------------------------------------ catalogue
@dataclass
class Fx:
    id: str
    name: str
    group: str
    make: Callable[[], list[Canvas]]
    variants: dict[str, dict[str, str]]
    fps: int
    anchor: tuple[int, int]
    attach: str                         # feet | body | overhead | projectile
    blend: str = "add"                  # add | normal
    loop: bool = False
    loop_start: int = 0
    layer: str = "over"                 # over (drawn above units) | ground (drawn on the floor, under units)
    target: str | None = "foe"          # preview only: who stands in the cell
    hides_target: int | None = None     # preview only: the frame from which the unit is gone (defeat, teleport)
    note: str = ""
    tags: list[str] = field(default_factory=list)


_BLADE = {"steel": _v(main="steel"), "fire": _v(main="magic/fire"), "frost": _v(main="magic/frost"),
          "holy": _v(main="magic/holy"), "shadow": _v(main="eldritch")}
_MAGIC5 = {k: _v(main=f"magic/{k}", core=f"magic/{c}", accent=f"magic/{c}") for k, c in
           (("arcane", "frost"), ("holy", "holy"), ("fire", "holy"), ("shadow", "arcane"), ("nature", "holy"))}

CATALOGUE: list[Fx] = [
    Fx("slash", "Slash", "Weapon strikes", slash, _BLADE, 20, (24, 24), "body",
       note="sword, axe and mace swings; mirror for a left-facing attacker"),
    Fx("thrust", "Thrust", "Weapon strikes", thrust, _BLADE, 18, (16, 16), "body",
       note="spear, dagger and rapier stabs; points right, mirror or rotate to face the target"),
    Fx("hit", "Hit spark", "Weapon strikes", hit, {
        "spark": _v(core="spark", main="magic/fire"), "magic": _v(core="magic/frost", main="magic/arcane"),
        "blood": _v(core="magic/blood", main="magic/blood")}, 24, (24, 24), "body"),
    Fx("crit", "Critical burst", "Weapon strikes", crit, {
        "gold": _v(core="spark", main="magic/fire", accent="magic/blood")}, 20, (32, 32), "body"),
    Fx("arrow", "Arrow", "Projectiles", arrow, {"oak": _v(wood="wood/oak", metal="metal/steel", feather="fletch")},
       1, (27, 15), "projectile", blend="normal", target=None, note="points right; rotate to the flight direction"),
    Fx("arrow_trail", "Arrow in flight", "Projectiles", arrow_trail, {
        "oak": _v(wood="wood/oak", metal="metal/steel", feather="fletch", accent="wind")}, 16, (27, 15),
       "projectile", blend="normal", loop=True, target=None, note="points right; rotate to the flight direction"),
    Fx("fireball", "Fireball", "Projectiles", fireball, {"fire": _v(main="magic/fire", core="magic/holy")}, 12,
       (22, 16), "projectile", loop=True, target=None, note="points right; rotate to the flight direction"),
    Fx("eldritch", "Eldritch blast", "Projectiles", eldritch, {
        "violet": _v(main="eldritch", core="magic/shadow", accent="hex"),
        "fel": _v(main="fel", core="fel_dark", accent="bile")}, 12, (22, 16), "projectile", blend="normal",
       loop=True, target=None, note="points right; rotate to the flight direction"),
    Fx("explosion", "Fireball blast", "Spell bursts", explosion, {
        "fire": _v(main="magic/fire", core="magic/holy", smoke="soot")}, 16, (48, 80), "feet"),
    Fx("ice", "Ice shards", "Spell bursts", ice, {"frost": _v(main="ice", accent="magic/frost")}, 14, (24, 39),
       "feet"),
    Fx("lightning", "Lightning strike", "Spell bursts", lightning, {
        "storm": _v(main="storm", core="magic/frost"), "holy": _v(main="magic/holy", core="magic/holy")}, 18,
       (32, 122), "feet"),
    Fx("heal", "Healing", "Spell bursts", heal, {
        "nature": _v(main="magic/nature", core="magic/holy"), "holy": _v(main="magic/holy", core="magic/holy"),
        "arcane": _v(main="magic/arcane", core="magic/frost")}, 14, (32, 73), "feet", target="ally"),
    Fx("pillar", "Pillar of light", "Spell bursts", pillar, {
        "holy": _v(main="gold", core="magic/holy"), "arcane": _v(main="magic/arcane", core="magic/frost"),
        "shadow": _v(main="eldritch", core="hex")}, 14, (24, 91), "feet", target="ally",
       note="bless (holy, on an ally) or smite (on a foe)"),
    Fx("levelup", "Level up", "Spell bursts", levelup, {"gold": _v(main="gold", core="magic/holy", accent="spark")},
       12, (16, 60), "feet", target="ally"),
    Fx("misty", "Misty step", "Spell bursts", misty, {
        "mist": _v(main="mist", core="magic/frost"), "fey": _v(main="magic/nature", core="magic/holy"),
        "shadow": _v(main="eldritch", core="hex")}, 12, (24, 44), "feet", target="ally", hides_target=4,
       note="play at the start tile (unit hidden from frame 4), then reversed at the destination"),
    Fx("hex", "Hex mark", "Spell bursts", hex_mark, {
        "shadow": _v(main="eldritch", accent="hex"), "blood": _v(main="magic/blood", accent="spark")}, 10, (16, 27),
       "overhead", blend="normal", loop=True, loop_start=4),
    Fx("smoke", "Smoke puff", "Clouds", smoke, {"grey": _v(smoke="smoke")}, 12, (16, 28), "feet", blend="normal",
       target=None),
    Fx("dust", "Dust puff", "Clouds", dust, {"earth": _v(smoke="dust"), "sand": _v(smoke="terrain/sand"),
                                             "snow": _v(smoke="terrain/snow")}, 14, (16, 27), "feet",
       blend="normal", target="ally", note="landings and footsteps"),
    Fx("poison", "Poison cloud", "Clouds", poison, {"poison": _v(main="poison", accent="bile"),
                                                    "miasma": _v(main="miasma", accent="hex")}, 8, (24, 40), "feet",
       blend="normal", loop=True),
    Fx("poof", "Defeat poof", "Clouds", poof, {"white": _v(smoke="poof", core="spark")}, 12, (16, 31), "feet",
       blend="normal", hides_target=1),
    Fx("circle", "Magic circle", "Ground and auras", magic_circle, _MAGIC5, 12, (32, 34), "feet", loop=True,
       layer="ground", target="ally", note="casting circle under the caster, or a spell's area marker"),
    Fx("aura", "Buff aura", "Ground and auras", aura, _MAGIC5, 12, (32, 53), "feet", loop=True, layer="ground",
       target="ally"),
    Fx("shield", "Shield bubble", "Ground and auras", shield, {
        "arcane": _v(main="magic/arcane", core="magic/frost"), "holy": _v(main="magic/holy", core="magic/holy"),
        "nature": _v(main="magic/nature", core="magic/holy")}, 10, (24, 45), "feet", loop=True, loop_start=3,
       target="ally"),
    # Damage elements: one shape language per damage type
    Fx("flames", "Engulfing flames", "Elements", flames, {
        "fire": _v(main="magic/fire", core="magic/holy"), "hellfire": _v(main="fel", core="bile"),
        "azure": _v(main="magic/frost", core="magic/holy")}, 14, (24, 60), "feet",
       note="fire damage on a unit: tongues climb the body; hellfire for warlock and fiend fire"),
    Fx("frost", "Frost burst", "Elements", frost, {
        "frost": _v(main="ice", core="magic/frost", accent="mist")}, 14, (32, 32), "body",
       note="cold damage: faceted crystals grow out of the target and shatter"),
    Fx("shock", "Shock", "Elements", shock, {
        "storm": _v(main="storm", core="magic/frost"), "violet": _v(main="eldritch", core="hex")}, 18, (24, 38),
       "body", note="lightning damage on a unit: forked arcs crackle round it"),
    Fx("necrotic", "Necrotic grasp", "Elements", necrotic, {
        "grave": _v(smoke="necro", main="grave", core="magic/holy"),
        "blood": _v(smoke="necro", main="magic/blood", core="magic/fire")}, 12, (24, 34), "body", blend="normal",
       note="necrotic damage: wisps coil in and a skull flares; blood for life drain"),
    Fx("radiant", "Radiant flare", "Elements", radiant, {
        "dawn": _v(main="gold", core="magic/holy", accent="radiant"),
        "moon": _v(main="moon", core="magic/holy", accent="magic/frost")}, 14, (32, 32), "body",
       note="radiant damage: rays burst and light sifts down"),
    Fx("psychic", "Psychic ripple", "Elements", psychic, {
        "mind": _v(main="psychic", core="magic/holy"), "dread": _v(main="eldritch", core="hex")}, 14, (32, 24),
       "overhead", note="psychic damage: wobbling rings spread from the head"),
    Fx("thunder", "Thunderclap", "Elements", thunder, {
        "thunder": _v(main="thunder", core="magic/frost")}, 16, (48, 48), "feet",
       note="thunder damage: concentric shockwaves over the ground, about a tile and a half across"),
    Fx("acid", "Acid splash", "Elements", acid, {
        "acid": _v(main="acid", core="bile", accent="poof")}, 14, (24, 60), "feet", blend="normal",
       note="acid damage: a splash on the chest that runs down and puddles"),
    Fx("toxin", "Poison spray", "Elements", toxin, {
        "venom": _v(main="poison", accent="bile"), "sickness": _v(main="miasma", accent="hex")}, 12, (24, 26),
       "body", blend="normal", note="poison damage on one target; the spray arrives from the left, mirror to face"),
    Fx("force", "Force shatter", "Elements", force, {
        "force": _v(main="force", core="magic/frost"), "eldritch": _v(main="eldritch", core="hex")}, 16, (24, 24),
       "body", note="force damage: a honeycomb of force breaks into hexagon shards"),
    # Boons, wards and binding
    Fx("restore", "Restoration", "Boons and wards", restore, {
        "holy": _v(main="magic/holy", core="magic/holy"), "nature": _v(main="magic/nature", core="magic/holy")},
       12, (24, 75), "feet", target="ally", note="cleansing: a ring sinks head to foot (heals rise instead)"),
    Fx("ward", "Ward", "Boons and wards", ward, {
        "holy": _v(main="gold", core="magic/holy"), "arcane": _v(main="magic/arcane", core="magic/frost"),
        "nature": _v(main="magic/earth", core="magic/nature")}, 12, (24, 38), "body", target="ally",
       note="shield of faith, mage armor, barkskin: rune plates lock round the unit"),
    Fx("bless", "Blessing", "Boons and wards", bless, {
        "gold": _v(main="gold", core="magic/holy"), "silver": _v(main="moon", core="magic/holy")}, 12, (24, 75),
       "feet", target="ally", note="bless, aid: a halo over the head and motes sifting down"),
    Fx("inspire", "Inspiration", "Boons and wards", inspire, {
        "gold": _v(main="gold", core="magic/holy"), "charm": _v(main="foliage/blossom", core="magic/holy")}, 12,
       (24, 38), "body", target="ally", note="bardic inspiration, heroism, rally: notes burst and float up"),
    Fx("rage", "Rage", "Boons and wards", rage, {
        "rage": _v(main="rage", core="magic/fire"), "valor": _v(main="magic/fire", core="magic/holy")}, 14,
       (24, 60), "feet", target="ally", note="rage, action surge, war cries: a hollow shell of flame"),
    Fx("hold", "Hold", "Boons and wards", hold, {
        "arcane": _v(main="magic/arcane", core="magic/frost"), "holy": _v(main="gold", core="magic/holy"),
        "shadow": _v(main="eldritch", core="hex"), "nature": _v(main="magic/nature", core="magic/holy")}, 12,
       (24, 38), "body", note="hold person or monster, command: rune rings close and lock"),
    Fx("dispel", "Dispel", "Boons and wards", dispel, {
        "arcane": _v(main="magic/arcane", core="magic/frost", accent="magic/holy"),
        "shadow": _v(main="eldritch", core="hex", accent="magic/holy")}, 14, (24, 24), "body",
       note="dispel magic, counterspell: a sigil cracks and shatters"),
    Fx("web", "Web", "Boons and wards", web, {"silk": _v(main="silk", core="magic/holy")}, 12, (32, 34), "feet",
       blend="normal", layer="ground", note="web thrown over a tile; the hazards set's web holds it afterwards"),
]

ATTACH = {
    "feet": "anchor goes on the target's ground point (the character's own anchor, 16,37 in its frame)",
    "body": "anchor goes on the target's chest: 15 px above its ground point",
    "overhead": "anchor goes just above the target's head: 36 px above its ground point",
    "projectile": "anchor moves along the flight path; the art faces +x (right), rotate it to the direction",
}


# ------------------------------------------------------------------------------------------------------------ build
def build() -> None:
    """content/tactics/base/fx: one horizontal strip per effect and variant, plus a manifest; and the previews."""
    out = PACK / "fx"
    shutil.rmtree(out, ignore_errors=True)
    effects = {}
    rendered: dict[str, dict[str, list[Image.Image]]] = {}
    for e in CATALOGUE:
        frames = e.make()
        w, h = frames[0].w, frames[0].h
        files = {}
        rendered[e.id] = {}
        for vname, v in e.variants.items():
            imgs = [fr.render(ramps(v)) for fr in frames]
            sheet = Image.new("RGBA", (w * len(imgs), h))
            for k, im in enumerate(imgs):
                sheet.paste(im, (k * w, 0))
            rel = f"{e.id}_{vname}.png"
            save(sheet, out / rel)
            files[vname] = rel
            rendered[e.id][vname] = imgs
        entry = {"name": e.name, "group": e.group, "frame": [w, h], "frames": len(frames), "fps": e.fps,
                 "loop": e.loop, "anchor": list(e.anchor), "attach": e.attach, "blend": e.blend, "layer": e.layer,
                 "variants": files, "default": next(iter(files))}
        if e.loop and e.loop_start:
            entry["loopStart"] = e.loop_start
        if e.note:
            entry["note"] = e.note
        effects[e.id] = entry
    write_json({
        "layout": "each file is one horizontal strip of frames, left to right, all the same size",
        "blend": {"add": "additive (glows, light): src.rgb * src.a added to the scene",
                  "normal": "ordinary alpha blending (smoke, dust, solid objects, dark magic)"},
        "layer": {"over": "drawn above units (sort with the target, just in front of it)",
                  "ground": "drawn on the floor under units, like a decal"},
        "loop": "loop: true repeats frames loopStart..end (loopStart defaults to 0); false plays once and is removed",
        "attach": ATTACH,
        "scale": "drawn at the characters' pixel scale: show at the same multiple as the sprites",
        "effects": effects,
    }, out / "manifest.json")
    _preview_sheet(rendered)
    _preview_gif(rendered)


def _preview_sheet(rendered: dict[str, dict[str, list[Image.Image]]]) -> None:
    sections = []
    for group in dict.fromkeys(e.group for e in CATALOGUE):
        fxs = [e for e in CATALOGUE if e.group == group]
        tiles = []
        for e in fxs:
            imgs = rendered[e.id][next(iter(e.variants))]
            loop = (f"loop from {e.loop_start}" if e.loop_start else "loop") if e.loop else "once"
            sub = f"{len(imgs)} f · {e.fps} fps · {loop} · {e.blend} · {e.attach}"
            tiles.append(Tile(strip(imgs, 1, gap=2), f"{e.name}  ({e.id})", sub))
        widest = max(t.image.width for t in tiles)
        cols = max(1, min(len(tiles), 1800 // (widest * 3 + 40)))
        sections.append(contact_sheet(group, tiles, columns=cols, zoom=3, label_h=40))
    tiles = []
    for e in CATALOGUE:
        if len(e.variants) < 2:
            continue
        frames = rendered[e.id]
        peak = _peak(list(frames.values())[0])
        tiles.append(Tile(strip([fr[peak] for fr in frames.values()], 1, gap=3), e.name,
                          " · ".join(frames)))
    sections.append(contact_sheet("Colour variants (one drawing, several palettes)", tiles, columns=3, zoom=3,
                                  label_h=40))
    save(stack("Combat and spell effects", sections,
               "Frames left to right as shipped (content/tactics/base/fx). Light from the top-left, hue-shifted "
               "ramps, no anti-aliasing. 3x zoom."), PREVIEWS / "17-fx.png")


def _peak(imgs: list[Image.Image]) -> int:
    """The frame with the most bright pixels: the one the variant swatches show."""
    def score(im):
        a = np.asarray(im).astype(int)
        return int(((a[..., 3] > 0) * a[..., :3].sum(-1)).sum())
    return max(range(len(imgs)), key=lambda k: score(imgs[k]))


# ------------------------------------------------------------------------------------------------------ animated
GIF_TICK = 50                    # ms per GIF frame
GIF_TICKS = 80
CELL_W, CELL_H, LABEL_H = 204, 290, 30
SPRITE = 2                       # characters and effects are shown at 2x next to 64 px wide tiles, as in the dioramas


def _render_any(c: Canvas, pal: dict[str, list[str]]) -> Image.Image:
    """Renders a borrowed terrain canvas even if the terrain set has grown slots this preview doesn't name: those
    take the main ramp, which is close enough for a backdrop."""
    used = {c.slots.name(int(s)) for s in np.unique(c.slot) if s}
    return c.render({**{n: pal["main"] for n in used}, **pal})


def _tile() -> tuple[Image.Image, tuple[int, int]]:
    """One grass block in the dioramas' projection: a 64x32 top over a short earth side. Returns it and the top's
    centre."""
    from parts import terrain as TR
    top = np.asarray(_render_any(TR.grass(), TR.ramps({"main": "terrain/grass"}))).astype(np.float32)
    side = np.asarray(_render_any(TR.dirt_side(), TR.ramps({"main": "terrain/dirt", "accent": "terrain/stone"})))
    side = side.astype(np.float32)
    W, H, depth = 66, 33 + 14, 14
    img = np.zeros((H, W, 4), np.float32)
    ox, oy = 33, 0
    for sy in range(H):
        for sx in range(W):
            a, b = sx - ox, 2 * (sy - oy)
            U, V = (a + b) / 2, (b - a) / 2
            if 0 <= U < 32 and 0 <= V < 32:
                rim = 1.12 if U >= 31 or V >= 31 else 1.0
                img[sy, sx, :3] = np.clip(top[int(V) + 40, int(U) + 40, :3] * rim, 0, 255)
                img[sy, sx, 3] = 255
    for sx in range(1, W - 1):                       # the two visible sides, darker on the right
        edge_y = oy + 32 - abs(sx - ox) // 2
        for d in range(depth):
            y = edge_y + d
            if 0 <= y < H and img[y, sx, 3] == 0:
                col = side[d % side.shape[0], (sx * 2) % side.shape[1], :3]
                img[y, sx, :3] = col * (0.8 if sx < ox else 0.62)
                img[y, sx, 3] = 255
    return Image.fromarray(img.astype(np.uint8), "RGBA"), (ox, 16)


def _composite(dst: np.ndarray, src: Image.Image, x: int, y: int, blend: str) -> None:
    s = np.asarray(src).astype(np.float32)
    h, w = s.shape[:2]
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(dst.shape[1], x + w), min(dst.shape[0], y + h)
    if x0 >= x1 or y0 >= y1:
        return
    sub = s[y0 - y:y1 - y, x0 - x:x1 - x]
    a = sub[..., 3:4] / 255.0
    if blend == "add":
        dst[y0:y1, x0:x1, :3] = np.minimum(255, dst[y0:y1, x0:x1, :3] + sub[..., :3] * a)
    else:
        dst[y0:y1, x0:x1, :3] = dst[y0:y1, x0:x1, :3] * (1 - a) + sub[..., :3] * a


def _frame_at(e: Fx, n: int, ms: int) -> int | None:
    """Which frame an effect shows `ms` into the preview: loops run forever (intro once), one-shots replay with a
    pause between."""
    k = int(ms * e.fps / 1000)
    if e.loop:
        if k < n:
            return k
        span = n - e.loop_start
        return e.loop_start + (k - e.loop_start) % span
    period = n + max(4, e.fps * 2 // 3)
    k %= period
    return k if k < n else None


def _preview_gif(rendered: dict[str, dict[str, list[Image.Image]]]) -> None:
    """Every effect playing in its own cell over a grass block, at the dioramas' scale, with a unit standing in
    for the target. Additive effects are blended additively, so the GIF shows them as the game will."""
    from parts import characters as C
    from parts.recipes import ALL
    units = {}
    for key, rid in (("foe", "goblin_cutthroat"), ("ally", "kara")):
        im = C.recipe_sheet(next(r for r in ALL if r.id == rid)).frames[0]
        units[key] = im.resize((im.width * SPRITE, im.height * SPRITE), Image.NEAREST)
    tile, (tx, ty) = _tile()
    cols = 8
    rows = -(-len(CATALOGUE) // cols)
    head = 56
    W = cols * (CELL_W + 8) + 8
    H = head + rows * (CELL_H + LABEL_H + 8) + 8
    base = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(base)
    d.text((14, 12), "Combat and spell effects", font=font(20, True), fill=INK)
    d.text((14, 36), "2x, as in the dioramas · additive effects blended additively", font=font(12), fill=MUTED)
    cells = []
    for i, e in enumerate(CATALOGUE):
        x = 8 + (i % cols) * (CELL_W + 8)
        y = head + (i // cols) * (CELL_H + LABEL_H + 8)
        d.rounded_rectangle((x, y, x + CELL_W, y + CELL_H), 8, fill=PANEL)
        gx, gy = CELL_W // 2, CELL_H - 34                            # the unit's ground point, in the cell
        base.alpha_composite(tile, (x + gx - tx, y + gy - ty))
        n = len(rendered[e.id][next(iter(e.variants))])
        d.text((x + 4, y + CELL_H + 3), e.name, font=font(12, True), fill=INK)
        d.text((x + 4, y + CELL_H + 17), f"{n}f {e.fps}fps {'loop' if e.loop else 'once'} {e.blend}",
               font=font(10), fill=MUTED)
        big = [im.resize((im.width * SPRITE, im.height * SPRITE), Image.NEAREST)
               for im in rendered[e.id][next(iter(e.variants))]]
        cells.append((e, x, y, gx, gy, big))
    base_arr = np.asarray(base).astype(np.float32)
    frames = []
    for t in range(GIF_TICKS):
        ms = t * GIF_TICK
        canvas = base_arr.copy()
        for e, x, y, gx, gy, big in cells:
            cell = canvas[y + 4:y + CELL_H - 4, x + 4:x + CELL_W - 4]       # a view: effects stay in their panel
            gx, gy = gx - 4, gy - 4
            k = _frame_at(e, len(big), ms)
            fx = big[k] if k is not None else None
            ax, ay = e.anchor[0] * SPRITE, e.anchor[1] * SPRITE
            unit = units.get(e.target or "")
            gone = e.hides_target is not None and k is not None and k >= e.hides_target
            if fx is not None and e.layer == "ground":
                _composite(cell, fx, gx - ax, gy - ay, e.blend)
            if unit is not None and not gone:
                _composite(cell, unit, gx - 16 * SPRITE, gy - 37 * SPRITE, "normal")
            if fx is None or e.layer == "ground":
                continue
            if e.attach == "projectile":                                  # fly across the cell at chest height
                px = int(-20 + (ms % 1600) / 1600 * (CELL_W + 40))
                _composite(cell, fx, px - ax, gy - 15 * SPRITE - ay, e.blend)
            else:
                lift = {"feet": 0, "body": 15, "overhead": 36}[e.attach] * SPRITE
                _composite(cell, fx, gx - ax, gy - lift - ay, e.blend)
        img = Image.fromarray(canvas.clip(0, 255).astype(np.uint8), "RGBA").convert("RGB")
        frames.append(img.quantize(colors=255, method=Image.Quantize.MEDIANCUT))
    frames[0].save(PREVIEWS / "anim-fx.gif", save_all=True, append_images=frames[1:], duration=GIF_TICK, loop=0,
                   optimize=True, disposal=1)
