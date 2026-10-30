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
for _s in ("smoke", "main", "accent", "core", "metal", "wood", "feather"):
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
}
EMPTY = -9.0


def _ramp(ref: str) -> list[str]:
    fam, name = ref.split("/")
    return FX_RAMPS[name] if fam == "fx" else FAMILIES[fam][name]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    return {slot: _ramp(ref) for slot, ref in variant.items()}


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


# ------------------------------------------------------------------------------------------------- weapon strikes
def slash() -> list[Canvas]:
    """A sword smear: a crescent swept clockwise from over the shoulder down past the opposite hip. The leading edge
    is thick and bright, the trailing end thin and cool; on the way out the smear breaks into dashes."""
    cx, cy, R = 15.5, 15.5, 12.5
    keys = [(-84, -66, 1.6, 1), (-80, 18, 3.6, 0), (-68, 98, 6.0, 0), (-32, 128, 5.0, 0), (36, 136, 3.4, 1),
            (92, 141, 2.2, 2)]
    out = []
    for i, (tail, head, wmax, dim) in enumerate(keys):
        f = Field(32, 32)
        r, th = f.dist(cx, cy), f.ang(cx, cy)
        s = ((th - tail) % 360) / (head - tail)
        wd = wmax * np.clip(s, 0, 1) ** 0.8 * np.clip((1 - s) * 5, 0, 1)
        d = R - r
        inside = (s >= 0) & (s <= 1) & (d >= -0.45) & (d <= wd)
        sh = np.select([d < 0.9, d < wd * 0.45, d < wd * 0.8], [5, 4, 3], 2).astype(float)
        sh -= dim + (s < 0.3) + (s < 0.12)
        if dim:
            inside &= ((th + 400) / 9 % 3) < 2                       # the smear breaks into dashes
        f.add("main", np.where(inside, sh, EMPTY))
        c = f.canvas()
        if i in (1, 2, 3):                                            # glint riding the blade's tip
            a = math.radians(head - 6)
            _twinkle(c, cx + R * math.cos(a), cy + R * math.sin(a), "main", 2 if i == 2 else 1)
        out.append(c)
    c = Canvas(32, 32, SLOTS)
    for a, sh in ((112, 2), (124, 3), (134, 2), (146, 2)):            # last specks fly off the end of the arc
        ra = math.radians(a)
        _pmax(c, cx + (R + 2) * math.cos(ra), cy + (R + 2) * math.sin(ra), "main", sh)
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


def hit() -> list[Canvas]:
    """An impact spark: a pinpoint, an eight-ray star, a hollow ring, then sparks thrown out and falling."""
    cx = cy = 15.5
    r = tex.rng(301)
    sparks = [(a, r.uniform(0.85, 1.2)) for a in np.linspace(0, 360, 7, endpoint=False) + r.uniform(-20, 20, 7)]
    out = []
    for i in range(6):
        f = Field(32, 32)
        if i == 0:
            f.add("main", _q(1 - f.dist(cx, cy) / 2.6, [(0, 4)]))
            _starburst(f, cx, cy, 4, 0, 1.6)
            f.add("core", _q(1 - f.dist(cx, cy) / 1.6, [(0, 5)]))
        elif i == 1:
            _starburst(f, cx, cy, 10, 6, 3)
            f.add("core", _q(1 - f.dist(cx, cy) / 3.0, [(0, 5)]))
        elif i == 2:
            _starburst(f, cx, cy, 13, 8, 2.2, r0=3)
            ring = 1 - np.abs(f.dist(cx, cy) - 4.3) / 0.9
            f.add("main", _q(ring, [(0, 4)]))
        elif i == 3:
            _starburst(f, cx, cy, 14, 9, 1.6, r0=9)
        c = f.canvas()
        if i >= 3:
            t = i - 2
            for a, sp in sparks:
                rad = (7 + 3.2 * t) * sp
                x = cx + rad * math.cos(math.radians(a))
                y = cy + rad * math.sin(math.radians(a)) + 0.6 * t * t
                if i == 5 and sp < 1.0:
                    continue
                _mote(c, x, y, "core" if i == 3 else "main", 5 - t, big=(i == 3))
        out.append(c)
    return out


def crit() -> list[Canvas]:
    """A critical hit: a long glint, a big gold star with a red shockwave ring, sparks raining out and a few
    twinkles left hanging. Larger, longer and two-toned so it never reads as an ordinary hit."""
    cx = cy = 23.5
    r = tex.rng(311)
    sparks = [(a, r.uniform(0.8, 1.25), bool(r.random() < 0.4))
              for a in np.linspace(0, 360, 12, endpoint=False) + r.uniform(-12, 12, 12)]
    keys = [  # cardinal ray, diagonal ray, width, core radius, ring radius, ring width
        (9, 0, 2, 2.4, 0, 0), (20, 11, 4.2, 5, 0, 0), (23, 14, 4, 5.5, 9, 2.6), (17, 9, 3, 3.5, 14, 2.2),
        (10, 5, 2, 0, 18, 1.6), (0, 0, 0, 0, 21, 1.2), (0, 0, 0, 0, 0, 0), (0, 0, 0, 0, 0, 0)]
    out = []
    for i, (card, diag, w, core, ring_r, ring_w) in enumerate(keys):
        f = Field(48, 48)
        if ring_r:
            ring = 1 - np.abs(f.dist(cx, cy) - ring_r) / (ring_w / 2)
            sh = _q(ring, [(0, 2 if i >= 4 else 3), (0.5, 4 if i < 4 else 3)])
            if i >= 4:
                sh = np.where((f.ang(cx, cy) + 400) / 15 % 2 < 1.2, sh, EMPTY)
            f.add("accent", sh)
        if card:
            _starburst(f, cx, cy, card, diag, w, r0=0 if i < 3 else 4 if i == 3 else 7)
        if core:
            f.add("core", _q(1 - f.dist(cx, cy) / core, [(0, 4), (0.35, 5)]))
        c = f.canvas()
        if 3 <= i <= 6:
            t = i - 2
            for a, sp, big in sparks:
                rad = (9 + 4 * t) * sp
                x = cx + rad * math.cos(math.radians(a))
                y = cy + rad * math.sin(math.radians(a)) + 0.7 * t * t
                if i == 6 and not big:
                    continue
                _mote(c, x, y, "core" if t == 1 else "main", 5 - (t - 1), big=big and t < 3)
        if i >= 5:
            for x, y, s in ((10, 12, 2), (37, 15, 1), (33, 36, 2), (12, 33, 1)):
                if (i == 5) or (i == 6 and s == 2) or (i == 7 and (x, y) == (33, 36)):
                    _twinkle(c, x, y, "core", s if i < 7 else 1, 5 if i < 7 else 4)
        out.append(c)
    return out


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
def explosion() -> list[Canvas]:
    """A fireball's blast: a flash, a ball of fire with a white-hot heart and a ragged rim, which cools from the
    inside out into soot that rolls upward while embers rain down."""
    cx, cy = 23.5, 29.0
    r = tex.rng(331)
    n = tex.fbm(48, 48, 4, r, octaves=3)
    ns = tex.fbm(48, 48, 6, r, octaves=2)
    embers = [(a, r.uniform(0.7, 1.3), r.uniform(-1.5, 0.5)) for a in r.uniform(180, 360, 10)]
    keys = [(3.5, 1.0), (9.5, 1.05), (14.5, 1.0), (16.0, 0.8), (16.5, 0.55), (15.0, 0.32), (13.0, 0.12),
            (0, 0), (0, 0)]
    smoke = [None, None, None, (0.35, 0), (0.7, 0), (1.0, 0.0), (1.2, 0.18), (1.35, 0.42), (1.45, 0.66)]
    out = []
    for i, ((R, heat), sm) in enumerate(zip(keys, smoke)):
        c = Canvas(48, 48, SLOTS)
        if sm:
            grow, erode = sm
            rise = 7 * grow
            blobs = [(cx - 6, cy - 6 - rise, 5 + 4 * grow), (cx + 5, cy - 8 - rise * 1.2, 5 + 4 * grow),
                     (cx, cy - 13 - rise * 1.4, 4 + 4 * grow), (cx - 1, cy - 2 - rise * 0.5, 6 + 2 * grow)]
            _puffs(c, blobs, "smoke", noise=np.roll(ns, -2 * i, 0), erode=erode, rough=0.5, base=2)
        f = Field(48, 48, ss=1)
        if R:
            d = f.dist(cx, cy, 1.1) / R
            e = 1 - d + (n - 0.5) * 0.55 + (heat - 1) * 0.65
            if heat < 0.6:
                e = np.where(n < 0.65 - heat, EMPTY, e)          # the fire tears open as it cools
            f.add("main", _q(e, [(0.0, 1), (0.08, 2), (0.17, 3), (0.28, 4)]))
            f.add("core", _q(e, [(0.4, 4), (0.55, 5)]))
        if 1 <= i <= 3:                                          # light thrown on the ground
            rad = 7 + 3 * i
            g = _ring_d(f, cx, 41.5, rad, rad / 2.6)
            f.add("main", np.where(np.abs(g) < 0.55, 3 if i == 1 else 2, EMPTY))
        c = f.canvas(c)
        if 2 <= i <= 7:
            t = i - 1
            for a, sp, vy in embers:
                rad = 13 * sp * _ease(t / 5) + 3
                x = cx + rad * math.cos(math.radians(a)) * 1.2
                y = cy + rad * math.sin(math.radians(a)) * 0.8 + (vy + 0.55 * t) * t
                _mote(c, x, y, "core" if t < 2 else "main", 5 - t // 2, big=(t < 3 and sp > 1.1))
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


def lightning() -> list[Canvas]:
    """A lightning strike from above: the sky flickers, a leader creeps down, the full bolt hits with a ground
    flash, flickers to a second path and re-strikes, then thins to embers of light. Two pre-made paths alternate so
    the bolt shimmers instead of redrawing from scratch every frame."""
    W, H, gx, gy = 48, 80, 23.5, 75.0
    r = tex.rng(351)
    paths = [_bolt_path(r, gx + 5, -1, gx, gy), _bolt_path(r, gx - 4, -1, gx, gy)]
    branches = []
    for p in paths:
        bs = []
        for k in (6, 11, 16):
            x, y = p[k]
            side = 1 if r.random() < 0.5 else -1
            bs.append(_bolt_path(r, x, y, x + side * r.uniform(7, 12), y + r.uniform(9, 14), step=2.5, jag=1.4))
        branches.append(bs)
    sparks = [(a, r.uniform(0.7, 1.3)) for a in r.uniform(190, 350, 8)]
    # (path, reach 0..1, thick, halo, branches, ground flash, dim)
    keys = [(0, 0.12, False, False, False, 0, 1), (0, 0.65, False, True, False, 0, 0),
            (0, 1.0, True, True, True, 2, 0), (1, 1.0, False, True, True, 1, 0), (0, 1.0, True, True, True, 2, 0),
            (0, 1.0, False, False, False, 1, 1), (0, 1.0, False, False, False, 0, 3), (0, 0, False, False, False, 0, 0)]
    out = []
    for i, (pi, reach, thick, halo, br, flash, dim) in enumerate(keys):
        c = Canvas(W, H, SLOTS)
        core = np.zeros((H, W), bool)
        pts = paths[pi]
        upto = int(round((len(pts) - 1) * reach))
        for (x0, y0), (x1, y1) in zip(pts[:upto], pts[1:upto + 1]):
            for x, y in _bres(x0, y0, x1, y1):
                if 0 <= x < W and 0 <= y < H:
                    core[y, x] = True
        if dim >= 3:
            core &= (np.arange(H)[:, None] // 5) % 3 == 0          # the last of it breaks up
        twig = np.zeros_like(core)
        if br:
            for b in branches[pi]:
                for (x0, y0), (x1, y1) in zip(b, b[1:]):
                    for x, y in _bres(x0, y0, x1, y1):
                        if 0 <= x < W and 0 <= y < H:
                            twig[y, x] = True
        if thick:
            core |= np.roll(core, 1, axis=1)
        if halo:
            h1 = _dilate(core | twig, diag=True)
            h2 = _dilate(h1) & _checker(H, W)
            _paint(c, h2 & ~h1, "main", 2)
            _paint(c, h1 & ~core, "main", 3)
        _paint(c, twig & ~core, "main", 4)
        _paint(c, core, "main", 5 - dim if dim < 3 else 3)
        if i == 0:                                                   # the sky flickers first
            f = Field(W, H, ss=1)
            f.add("main", _q(1 - f.dist(gx + 5, -1, 2.2) / 13, [(0, 1), (0.4, 2), (0.75, 3)]))
            f.canvas(c)
        if flash:
            f = Field(W, H)
            e = f.dist(gx, gy, 2.8)
            f.add("main", _q(1 - np.abs(e - (9 + 3 * flash)) / 0.8, [(0, 3)]))
            f.add("core", _q(1 - e / (5 + 2 * flash), [(0, 4), (0.45, 5)]))
            f.canvas(c)
        if i >= 3:
            t = i - 2
            for a, sp in sparks:
                rad = (4 + 4 * t) * sp
                x = gx + rad * math.cos(math.radians(a))
                y = gy - 1 + rad * math.sin(math.radians(a)) * 0.6 + 0.6 * t * t
                if t <= 4 and not (i == 7 and sp < 1.0):
                    _mote(c, x, y, "main", 5 - t // 2 - (i == 7))
        out.append(c)
    return out


def heal() -> list[Canvas]:
    """Healing: a ring of light opens at the feet and sparkles rise past the body, swelling into crosses at their
    brightest and fading as they climb above the head."""
    cx, cy = 23.5, 43.5
    r = tex.rng(361)
    motes = []
    for j in range(17):
        a = r.uniform(0, 2 * math.pi)
        motes.append((cx + 11 * math.cos(a) * r.uniform(0.3, 1.0), cy + 4 * math.sin(a), r.uniform(0, 3.6),
                      r.uniform(3.6, 5.2), r.uniform(0, 6.28), j % 4 == 0))
    out = []
    for i in range(8):
        c = Canvas(48, 48, SLOTS)
        if i <= 4:
            f = Field(48, 48, ss=1)
            rad = [5, 9, 12, 14, 15][i]
            front = f.Y > cy
            ring = _ring_d(f, cx, cy, rad, rad / 2.4)
            sh = np.where(front, 4, 3) - (i >= 3) - (i >= 4)
            f.add("main", np.where(np.abs(ring) < 0.6, sh, EMPTY))
            if 1 <= i <= 3:
                inner = _ring_d(f, cx, cy, rad - 4, (rad - 4) / 2.4)
                f.add("main", np.where(np.abs(inner) < 0.5, 2, EMPTY))
            f.canvas(c)
        for x0, y0, t0, vy, ph, big in motes:
            age = i - t0
            if age < 0 or age > 4.2:
                continue
            x = x0 + math.sin(ph + age * 1.3) * 1.2
            y = y0 - vy * age - 2
            if age < 0.8:
                _pmax(c, x, y, "main", 3)
            elif age < 1.8:
                _twinkle(c, x, y, "core", 1, 5)
            elif age < 2.8:
                _twinkle(c, x, y, "core", 2 if big else 1, 5)
            elif age < 3.6:
                _twinkle(c, x, y, "main", 1 if big else 0, 4)
            else:
                _pmax(c, x, y, "main", 2)
        out.append(c)
    return out


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


def pillar() -> list[Canvas]:
    """A pillar of light (bless, smite): a thread of light drops from the sky, swells to a column with a ground
    burst, then narrows away leaving motes drifting up."""
    W, H, cx, gy = 32, 64, 15.5, 59.5
    r = tex.rng(371)
    motes = [(cx + r.uniform(-9, 9), gy - r.uniform(0, 10), r.uniform(2.5, 4.5), r.uniform(1, 4)) for _ in range(9)]
    keys = [(0.6, 0.55, 1), (3.0, 1.0, 0), (6.5, 1.0, 0), (8.0, 1.0, 0), (7.0, 1.0, 0), (4.5, 1.0, 1),
            (1.8, 1.0, 2), (0, 0, 0)]
    out = []
    for i, (hw, reach, drop) in enumerate(keys):
        f = Field(W, H, ss=1)
        _beam(f, cx, hw, 0, gy * reach, drop, i)
        if 1 <= i <= 5:
            rad = [0, 6, 11, 14, 15, 14][i]
            e = f.dist(cx, gy, 2.6)
            f.add("main", _q(1 - np.abs(e - rad) / 0.9, [(0, 3 if i < 5 else 2)]))
            f.add("core", _q(1 - e / (hw + 2), [(0, 4), (0.5, 5)]) - (i == 5))
        c = f.canvas()
        if i == 3:
            _twinkle(c, cx, gy - 2, "core", 3)
        for x, y0, vy, t0 in motes:
            age = i - t0
            if 0 <= age <= 4:
                y = y0 - vy * age * 2
                _mote(c, x, y, "core" if age < 2 else "main", 5 - int(age) + (age < 2) - 1, big=age < 1.5)
        out.append(c)
    return out


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


def magic_circle() -> list[Canvas]:
    """A rune circle on the floor (loop), squashed 2:1 to lie flat on the iso grid: two rings with a band of runes
    between them and a hexagram inside. The runes turn one glyph pair (45 degrees) per loop, the hexagram turns back
    one point (60 degrees) and a pulse runs once round the rim, so the eight frames wrap seamlessly. Rings are
    thresholded in screen space so they stay one pixel thick where the squash flattens them."""
    cx, cy = 23.5, 35.5
    out = []
    for k in range(8):
        f = Field(48, 48, ss=1)
        th = np.degrees(np.arctan2((f.Y - cy) * 2, f.X - cx))
        boost = (np.cos(np.radians(th - 45 * k)) > 0.7).astype(float)
        for rx, sh in ((22, 4), (16.5, 3), (4.5, 3)):
            d = _ring_d(f, cx, cy, rx, rx / 2)
            f.add("main", np.where(np.abs(d) < 0.55, sh + boost, EMPTY))
        c = f.canvas()
        for base in (-90, 90):
            pts = [(cx + 14.5 * math.cos(math.radians(base + a - 60 * k / 8)),
                    cy + 7.25 * math.sin(math.radians(base + a - 60 * k / 8))) for a in (0, 120, 240)]
            for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
                _lmax(c, x0 - 0.5, y0 - 0.5, x1 - 0.5, y1 - 0.5, "main", 3)
        for j in range(16):
            a = j * 22.5 + 45 * k / 8
            gx = cx + 19.25 * math.cos(math.radians(a))
            gy = cy + 9.6 * math.sin(math.radians(a))
            lit = 1 if math.cos(math.radians(a - 45 * k)) > 0.7 else 0
            for row, line in enumerate(GLYPHS[j % 2]):
                for col, ch in enumerate(line):
                    if ch == "#":
                        _pmax(c, gx - 1.5 + col, gy - 1 + row, "main", 4 + lit - (row == 1))
        _pmax(c, cx, cy, "core", 5)
        _pmax(c, cx + 1, cy, "core", 4)
        for j in range(6):                                     # motes lifting off the rim
            life = (k + j * 3) % 8
            if life >= 5:
                continue
            a = math.radians(j * 60 + 20)
            x = cx + 22 * math.cos(a)
            y = cy + 11 * math.sin(a) - 1 - life * 2
            _pmax(c, x, y, "core" if life < 2 else "main", 5 - life)
        out.append(c)
    return out


def aura() -> list[Canvas]:
    """A buff aura on the floor (loop): an iso-squashed ring with a bright crest running round it once per loop and
    streaks of light rising off it, each on its own phase of the same eight-frame cycle."""
    cx, cy = 23.5, 37.5
    r = tex.rng(441)
    streaks = [(i * 360 / 14 + r.uniform(-8, 8), int(r.integers(0, 8)), int(r.integers(2, 5))) for i in range(14)]
    out = []
    for k in range(8):
        f = Field(48, 48, ss=1)
        dy = f.Y - cy
        th = np.degrees(np.arctan2(dy * 2, f.X - cx))
        crest = np.cos(np.radians(th - 45 * k))
        ring = _ring_d(f, cx, cy, 17.5, 8.75)
        sh = 3 + (crest > 0.3) + (crest > 0.85) - (dy < 0) * (crest < 0.3)
        f.add("main", np.where((ring < 0.55) & (ring > -1.45), sh - (ring < -0.45), EMPTY))
        inner = _ring_d(f, cx, cy, 13.5, 6.75)
        f.add("main", np.where((np.abs(inner) < 0.5) & (crest > -0.2), 2, EMPTY))
        c = f.canvas()
        for a, ph, length in streaks:
            life = (k + ph) % 8
            if life >= 5:
                continue
            ra = math.radians(a)
            x = cx + 17.5 * math.cos(ra)
            y0 = cy + 8.75 * math.sin(ra) - 1 - life * 2.2
            front = math.sin(ra) > 0
            for j in range(length):
                sh = (5 if j == 0 else 4 if j == 1 else 3) - (life >= 3) - (0 if front else 1)
                _pmax(c, x, y0 + j, "main", sh)
        out.append(c)
    return out


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
    Fx("slash", "Slash", "Weapon strikes", slash, _BLADE, 18, (16, 16), "body",
       note="sword, axe and mace swings; mirror for a left-facing attacker"),
    Fx("thrust", "Thrust", "Weapon strikes", thrust, _BLADE, 18, (16, 16), "body",
       note="spear, dagger and rapier stabs; points right, mirror or rotate to face the target"),
    Fx("hit", "Hit spark", "Weapon strikes", hit, {
        "spark": _v(core="spark", main="magic/fire"), "magic": _v(core="magic/frost", main="magic/arcane"),
        "blood": _v(core="magic/blood", main="magic/blood")}, 20, (16, 16), "body"),
    Fx("crit", "Critical burst", "Weapon strikes", crit, {
        "gold": _v(core="spark", main="magic/fire", accent="magic/blood")}, 16, (24, 24), "body"),
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
        "fire": _v(main="magic/fire", core="magic/holy", smoke="soot")}, 14, (24, 42), "feet"),
    Fx("ice", "Ice shards", "Spell bursts", ice, {"frost": _v(main="ice", accent="magic/frost")}, 14, (24, 39),
       "feet"),
    Fx("lightning", "Lightning strike", "Spell bursts", lightning, {
        "storm": _v(main="storm", core="magic/frost"), "holy": _v(main="magic/holy", core="magic/holy")}, 16,
       (24, 75), "feet"),
    Fx("heal", "Healing", "Spell bursts", heal, {
        "nature": _v(main="magic/nature", core="magic/holy"), "holy": _v(main="magic/holy", core="magic/holy"),
        "arcane": _v(main="magic/arcane", core="magic/frost")}, 12, (24, 44), "feet", target="ally"),
    Fx("pillar", "Pillar of light", "Spell bursts", pillar, {
        "holy": _v(main="gold", core="magic/holy"), "arcane": _v(main="magic/arcane", core="magic/frost"),
        "shadow": _v(main="eldritch", core="hex")}, 12, (16, 60), "feet", target="ally",
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
    Fx("circle", "Magic circle", "Ground and auras", magic_circle, _MAGIC5, 10, (24, 36), "feet", loop=True,
       layer="ground", target="ally", note="casting circle under the caster, or a spell's area marker"),
    Fx("aura", "Buff aura", "Ground and auras", aura, _MAGIC5, 10, (24, 38), "feet", loop=True, layer="ground",
       target="ally"),
    Fx("shield", "Shield bubble", "Ground and auras", shield, {
        "arcane": _v(main="magic/arcane", core="magic/frost"), "holy": _v(main="magic/holy", core="magic/holy"),
        "nature": _v(main="magic/nature", core="magic/holy")}, 10, (24, 45), "feet", loop=True, loop_start=3,
       target="ally"),
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
CELL_W, CELL_H, LABEL_H = 140, 206, 30
SPRITE = 2                       # characters and effects are shown at 2x next to 64 px wide tiles, as in the dioramas


def _tile() -> tuple[Image.Image, tuple[int, int]]:
    """One grass block in the dioramas' projection: a 64x32 top over a short earth side. Returns it and the top's
    centre."""
    from parts import terrain as TR
    top = np.asarray(TR.grass().render(TR.ramps({"main": "terrain/grass"}))).astype(np.float32)
    side = np.asarray(TR.dirt_side().render(TR.ramps({"main": "terrain/dirt", "accent": "terrain/stone"})))
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
    cols = 6
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
