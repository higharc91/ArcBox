"""Signature ability effects: the one defining move of each of the 30 subclasses, so every subclass reads as itself
on the TV.

parts/fx.py is the shared vocabulary (slashes, fireballs, heals, circles). These are the moves a player picks a
subclass for, so each one is built around an object no other effect uses: a wall of tower shields, a clock face, a
bear trap, a rift in the air, a banner dropped from the sky. They are drawn and shipped exactly like fx: indexed
(slot, shade) canvases built from fx's thresholded fields and particles, palette variants, one horizontal strip per
effect and variant, and a manifest entry with fps, loop, anchor, attach, blend and layer, plus the subclass the
effect belongs to.

Timing follows anticipation -> peak -> dissipation: a small first frame, the biggest and brightest frame early, and
a longer tail that thins and breaks up. Held marks and zones loop only over phases that wrap (rotations by a
symmetry period, rain and noise moved a whole period), so a storm or a clock face never pops at the seam.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from typing import Callable

import numpy as np
from PIL import Image, ImageDraw

from arcpix import tex
from arcpix.canvas import Canvas
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import BG, PANEL, INK, MUTED, Tile, contact_sheet, font, stack, strip
from parts import fx as FX
from parts.fx import (EMPTY, SLOTS, Field, _bres, _checker_s, _composite, _lmax, _mote, _on, _peak, _pmax,
                      _puffs, _q, _ring_d, _starburst, _twinkle)

# fx's helpers paint through its SlotTable, so the extra materials here are registered on it. Slots are only names
# until render, and both sets ship rendered colour (never ramp maps), so the extra ids never reach the game.
for _s in ("cloth", "stone", "turf", "leaf", "petal"):
    SLOTS.id(_s)

SIG_RAMPS: dict[str, list[str]] = {
    "ki": ramp("#ffb347", shift=0.07, spread=0.9),              # a monk's inner force: warm orange-gold
    "abyss": ramp("#2c2150", shift=0.06, spread=1.3),           # the inside of a rift: almost black, still violet
    "thunderhead": ramp("#5b6688", shift=0.05, spread=1.0),     # storm cloud, blue-grey so lightning pops off it
    "feather": ramp("#f6f0de", shift=0.05, spread=0.6),
    "heart": ramp("#ff7fb6", shift=0.06, spread=0.85),          # the p6 pink: charm
    "reticle": ramp("#ff5a4a", shift=0.07, spread=0.9),
    "brand": ramp("#ff4636", shift=0.08, spread=0.95),
    "verdant": ramp("#9be86a", shift=0.07, spread=0.8),         # a transformation's flash: bright spring green
}


def _ramp(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "sig":
        return SIG_RAMPS[name]
    return FX.FX_RAMPS[name] if fam == "fx" else FAMILIES[fam][name]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    return {slot: _ramp(ref) for slot, ref in variant.items()}


def _v(**slots: str) -> dict[str, str]:
    """Bare names are this module's ramps first, then fx's; anything with a family stays as written."""
    def ref(v: str) -> str:
        if "/" in v:
            return v
        return f"sig/{v}" if v in SIG_RAMPS else f"fx/{v}"
    return {k: ref(v) for k, v in slots.items()}


# --------------------------------------------------------------------------------------------------------- drawing
def _iso(cx: float, cy: float, r: float, deg: float, sq: float = 0.5) -> tuple[float, float]:
    """A point on an iso-squashed circle: `deg` clockwise from screen-right, 90 is the near (front) side."""
    a = math.radians(deg)
    return cx + r * math.cos(a), cy + r * sq * math.sin(a)


def _iso_ang(f: Field, cx: float, cy: float) -> np.ndarray:
    """Degrees round an iso-squashed circle (0..360), so wedges and crests stay even after the 2:1 squash."""
    return np.degrees(np.arctan2((f.Y - cy) * 2, f.X - cx)) % 360


def _ring(f: Field, cx: float, cy: float, rx: float, w: float = 0.55, sq: float = 0.5) -> np.ndarray:
    return np.abs(_ring_d(f, cx, cy, rx, rx * sq)) < w


def _seg(f: Field, x0: float, y0: float, x1: float, y1: float) -> tuple[np.ndarray, np.ndarray]:
    """Distance to a segment and the position along it (0..1)."""
    dx, dy = x1 - x0, y1 - y0
    t = np.clip(((f.X - x0) * dx + (f.Y - y0) * dy) / max(dx * dx + dy * dy, 1e-6), 0, 1)
    return np.hypot(f.X - x0 - t * dx, f.Y - y0 - t * dy), t


def _spindle(f: Field, x0: float, y0: float, x1: float, y1: float, w: float, bulge: float = 0.0,
             t0: float = 0.0, t1: float = 1.0, leaf: bool = False) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """A pointed stroke from (x0, y0) to (x1, y1), bowed sideways by `bulge`, showing only t0..t1 of its length.
    Returns (position 0..1 along the shown part, NaN outside; distance from the centre line; local width). A smear
    is thin at the tail and fat behind its point; a `leaf` swells in the middle and points at both ends."""
    L = max(math.hypot(x1 - x0, y1 - y0), 1e-6)
    ux, uy = (x1 - x0) / L, (y1 - y0) / L
    px, py = f.X - x0, f.Y - y0
    t = (px * ux + py * uy) / L
    s = (-px * uy + py * ux) - bulge * np.sin(np.pi * np.clip(t, 0, 1))
    rel = (t - t0) / max(t1 - t0, 1e-6)
    rc = np.clip(rel, 0, 1)
    wd = w * (np.sin(np.pi * rc) ** 0.7 if leaf else rc ** 0.8 * np.clip((1 - rc) * 4, 0, 1))
    inside = (rel >= 0) & (rel <= 1) & (np.abs(s) <= wd / 2 + 0.3)
    return np.where(inside, rel, np.nan), np.abs(s), wd


def _cut(f: Field, x0, y0, x1, y1, w, bulge, t0, t1, dim, slot="main", dash: bool = False) -> None:
    """A blade flash along a spindle: white-hot centre line, the ramp falling off to its edges."""
    u, s, wd = _spindle(f, x0, y0, x1, y1, w, bulge, t0, t1)
    sh = np.select([s < 0.55, s < wd * 0.38], [5, 4], 3).astype(float) - dim - (u < 0.25)
    if dash:
        L = math.hypot(x1 - x0, y1 - y0)
        sh = np.where((u * L * (t1 - t0)) % 4 < 2.5, sh, EMPTY)
    f.add(slot, _on(u, sh))


def _crescent(f: Field, cx: float, cy: float, R: float, tail: float, head: float, wmax: float, dim: int,
              slot: str = "main", thorns: np.ndarray | None = None) -> np.ndarray:
    """fx.slash's smear as a reusable piece: a crescent swept clockwise from `tail` to `head` (degrees), thick and
    bright at its leading edge. Returns the mask so callers can decorate it."""
    r, th = f.dist(cx, cy), f.ang(cx, cy)
    s = ((th - tail) % 360) / max(head - tail, 1e-6)
    wd = wmax * np.clip(s, 0, 1) ** 0.8 * np.clip((1 - s) * 5, 0, 1)
    d = R - r
    lo = -0.45 - (thorns if thorns is not None else 0)
    inside = (s >= 0) & (s <= 1) & (d >= lo) & (d <= wd)
    sh = np.select([d < 0.9, d < wd * 0.45, d < wd * 0.8], [5, 4, 3], 2).astype(float)
    sh -= dim + (s < 0.3) + (s < 0.12)
    if dim:
        inside &= ((th + 400) / 9 % 3) < 2
    f.add(slot, np.where(inside, sh, EMPTY))
    return inside


def _glyph(c: Canvas, x: float, y: float, rows: list[str], slot: str, shade: int, lit: bool = True) -> None:
    """A small pixel sprite from strings ('#' drawn); the top row is one shade brighter when `lit`."""
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            if ch == "#":
                _pmax(c, x + i, y + j, slot, shade + (1 if lit and j == 0 else 0))


def _lightrim(c: Canvas, slot: str, up: int = 1, down: int = 1) -> None:
    """Solid objects: pixels with nothing above them catch the light, pixels with nothing below go dark."""
    m = c.mask(slot) & (c.flags == 0)
    above = np.zeros_like(m)
    above[1:] = m[:-1]
    below = np.zeros_like(m)
    below[:-1] = m[1:]
    sh = c.shade.astype(int)
    sh = np.where(m & ~above, sh + up, sh)
    sh = np.where(m & above & ~below, sh - down, sh)
    c.shade = np.clip(sh, 0, 5).astype(np.uint8)


def _tube(c: Canvas, pts: list[tuple[float, float]], r0: float, r1: float, slot: str, base: int = 3) -> None:
    """A round stem along `pts`, `r0` thick at the start tapering to `r1`, lit from the top-left like a cylinder.
    Later samples paint over earlier ones, so a stem that curls over itself stays readable."""
    n = len(pts)
    for k, (x, y) in enumerate(pts):
        rad = r0 + (r1 - r0) * k / max(n - 1, 1)
        if rad < 0.75:
            c.px(int(math.floor(x)), int(math.floor(y)), slot, base + 1)
            continue
        for yy in range(int(y - rad - 1), int(y + rad + 2)):
            for xx in range(int(x - rad - 1), int(x + rad + 2)):
                dx, dy = xx + 0.5 - x, yy + 0.5 - y
                if dx * dx + dy * dy <= rad * rad + 0.2:
                    lit = -(dx + dy) / max(rad, 0.5)
                    c.px(xx, yy, slot, base + (lit > 0.55) - (lit < -0.5))


def _heart(c: Canvas, cx: float, cy: float, s: float, slot: str, shade: int, dash: bool = False) -> None:
    """A heart outline (the classic parametric curve), `s` px per unit; about 32 s wide."""
    pts = []
    for t in np.linspace(0, 2 * math.pi, 64, endpoint=False):
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((cx + x * s, cy - y * s))
    for k, (p, q) in enumerate(zip(pts, pts[1:] + pts[:1])):
        if dash and (k // 3) % 2:
            continue
        lit = shade + (1 if p[1] < cy - 2 * s and p[0] < cx else 0)
        _lmax(c, p[0] - 0.5, p[1] - 0.5, q[0] - 0.5, q[1] - 0.5, slot, lit)


def _dissolve(c: Canvas, amount: float, seed: int) -> None:
    """Eats `amount` (0..1) of a solid object's pixels in a fixed random order, so it breaks up instead of fading."""
    if amount <= 0:
        return
    keep = tex.rng(seed).random((c.h, c.w)) >= amount
    gone = (c.slot > 0) & ~keep
    c.slot[gone] = 0
    c.flags[gone] = 0


def _swoosh(c: Canvas, cx: float, cy: float, rad: float, head: float, span: float, slot: str, base: int,
            sq: float = 0.42, thick: bool = True) -> None:
    """A curl of wind round an iso circle: an arc ending at `head` degrees, brightening from its tail to its head,
    dimmer on the far side, doubled up near the head."""
    for a in np.arange(head - span, head + 0.1, 2.0):
        s = (a - (head - span)) / span
        x, y = _iso(cx, cy, rad, a, sq)
        front = math.sin(math.radians(a)) > 0
        sh = base - 2 + round(2 * s) - (0 if front else 1)
        _pmax(c, x, y, slot, sh)
        if thick and s > 0.55 and front:
            _pmax(c, x, y - 1, slot, sh - 1)


def _canvas(w: int, h: int) -> Canvas:
    return Canvas(w, h, SLOTS)


# =============================================================================================================
# Fighter
# =============================================================================================================
def shield_wall() -> list[Canvas]:
    """Vanguard: three translucent tower shields burst up in an arc in front of the unit and lock into a wall. The
    centre one rises first and the flanks follow, each overshooting and settling; then the wall holds (frames
    4-9 loop): a glint sweeps across it once per loop and bands of light climb its faces one row a frame."""
    W, H, cx, gy = 48, 56, 23.5, 42.0
    panels = [(147, 0.42, 9.0, 1), (33, -0.42, 9.0, 1), (90, 0.0, 11.0, 0)]     # angle, slope, width, delay
    heights = [6, 19, 27, 24, 24, 24, 24, 24, 24, 24]
    out = []
    for i in range(10):
        loop = i - 4
        f = Field(W, H)
        if i < 4:
            rad = [5, 10, 14, 17][i]
            f.add("main", np.where(_ring(f, cx, gy, rad, 0.6), [4, 3, 2, 1][i], EMPTY))
        for a, slope, w, delay in panels:
            j = i - delay
            bx, by = _iso(cx, gy, 15, a)
            u = f.X - bx
            v = by + slope * u - f.Y
            if j < 0:                                               # a line of light where it will rise
                f.add("main", np.where((np.abs(u) <= w / 2) & (np.abs(v) < 0.6), 4, EMPTY))
                continue
            hh = heights[min(j, len(heights) - 1)]
            top = hh - 3.0 * (2 * u / w) ** 2
            inside = (np.abs(u) <= w / 2) & (v >= 0) & (v <= top)
            rim = inside & ((np.abs(u) > w / 2 - 1) | (v > top - 1.1) | (v < 1))
            lit = (u < 0) | (v > top - 1.1)
            flash = 1 if j == 2 else 0
            band = (np.floor(v) - 2 * max(loop, 0)) % 12 < 1
            inset = (np.abs(u) > w / 2 - 2) | (v > top - 2.1)
            fill = np.where(band, 2, np.where(inset & _checker_s(f), 1, EMPTY))
            f.add("main", np.where(inside & ~rim, fill + flash, EMPTY))
            f.add("main", np.where(rim, np.where(lit, 4, 3) + flash, EMPTY))
            cross = ((np.abs(u) < 0.8) & (v > hh * 0.28) & (v < hh * 0.84)) | \
                    ((np.abs(v - hh * 0.64) < 0.8) & (np.abs(u) < w * 0.3))
            f.add("core", np.where(inside & cross & (hh > 8), 4 + flash, EMPTY))
            if loop >= 0:
                g = f.X - (-4 + 10 * loop) + (f.Y - 30) * 0.5
                f.add("core", np.where(inside & (np.abs(g) < 1.0), 5, EMPTY))
        c = f.canvas()
        if 2 <= i <= 5:
            t = i - 2
            for a, sx in ((147, -2), (147, 2), (33, -2), (33, 2), (90, -3), (90, 3), (90, 0)):
                bx, by = _iso(cx, gy, 15, a)
                _mote(c, bx + sx, by - 25 - 3 * t - abs(sx), "core" if t < 2 else "main", 5 - t, big=t == 0)
        out.append(c)
    return out


def blade_dance() -> list[Canvas]:
    """Weaponmaster: the stance flares at the feet, then three cuts land in a blink, down-right, down-left and
    across, each crossing the last, leaving a hanging asterisk of steel that breaks into sparks."""
    W, H, cx, cy, gy = 48, 48, 23.5, 23.5, 39.0
    cuts = [(7, 6, 41, 40, -3), (41, 6, 7, 40, 3), (3, 21, 45, 27, -2)]
    # per frame: (cut, tail, head, dim, dashed)
    keys = [[], [(0, 0.0, 0.55, 0, False)], [(0, 0.15, 1.0, 0, False), (1, 0.0, 0.35, 1, False)],
            [(0, 0.55, 1.0, 1, True), (1, 0.05, 1.0, 0, False)],
            [(1, 0.55, 1.0, 1, True), (2, 0.0, 1.0, 0, False)],
            [(2, 0.45, 1.0, 1, True), (0, 0.2, 0.9, 2, True), (1, 0.2, 0.9, 2, True)],
            [(2, 0.2, 0.95, 2, True)], [], [], []]
    r = tex.rng(1101)
    sparks = [(a, r.uniform(0.8, 1.25)) for a in np.linspace(0, 360, 10, endpoint=False) + r.uniform(-15, 15, 10)]
    out = []
    for i, ks in enumerate(keys):
        f = Field(W, H)
        if i <= 4:
            rad = [6, 10, 13, 15, 16][i]
            f.add("accent", np.where(_ring(f, cx, gy, rad, 0.6), [5, 4, 4, 3, 2][i], EMPTY))
            if i <= 2:
                for a in (0, 90, 180, 270):                         # blade ticks standing on the ring
                    x, y = _iso(cx, gy, rad, a)
                    d, t = _seg(f, x, y, x, y - (4 - i))
                    f.add("accent", np.where(d < 0.6, 4, EMPTY))
        for k, t0, t1, dim, dash in ks:
            x0, y0, x1, y1, b = cuts[k]
            _cut(f, x0, y0, x1, y1, 3.6 if dim == 0 else 2.4, b, t0, t1, dim, dash=dash)
        c = f.canvas()
        if i == 0:
            for a in (45, 135, 225, 315):                           # the stance draws in
                ra = math.radians(a)
                _lmax(c, cx + 13 * math.cos(ra), cy + 13 * math.sin(ra), cx + 9 * math.cos(ra),
                      cy + 9 * math.sin(ra), "main", 3)
        if 1 <= i <= 4:
            k, t0, t1, dim, dash = ks[-1]
            x0, y0, x1, y1, b = cuts[k]
            if dim == 0:
                _twinkle(c, x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1, "core", 2 if i in (2, 4) else 1)
        if i == 5:
            _twinkle(c, cx, cy, "core", 3)
        if i >= 6:
            t = i - 5
            for a, sp in sparks:
                rad = (6 + 4 * t) * sp
                x = cx + rad * math.cos(math.radians(a))
                y = cy + rad * math.sin(math.radians(a)) + 0.5 * t * t
                if i == 9 and sp < 1.05:
                    continue
                _mote(c, x, y, "core" if t == 1 else "main", 5 - t, big=t == 1)
        out.append(c)
    return out


def _banner(c: Canvas, px: int, top: int, base: int, k: int) -> None:
    """The warlord's war banner on its pole: crossbar, swallow-tailed cloth rippling in the wind, gold trim and a
    gold crown. Drawn solid (it is outlined afterwards)."""
    for y in range(top - 2, base + 1):
        c.px(px - 1, y, "wood", 4)
        c.px(px, y, "wood", 2)
    for x, y, sh in ((px - 1, top - 5, 5), (px, top - 5, 4), (px - 1, top - 4, 4), (px, top - 4, 3),
                     (px - 1, top - 3, 3), (px, top - 3, 3), (px - 1, top - 6, 4)):
        c.px(x, y, "accent", sh)
    c.line(px - 7, top, px + 6, top, "wood", 3)
    for x in (px - 8, px + 7):
        c.px(x, top, "accent", 4)
        c.px(x, top - 1, "accent", 5)
    for y in range(top + 1, top + 19):
        rr = y - top
        shift = round(math.sin(rr * 0.42 - k * 1.3) * min(1.6, rr / 9))
        for x in range(px - 6, px + 6):
            lim = top + 18 - 5 * (1 - abs(x + 0.5 - px) / 6)
            if y > lim:
                continue
            fold = math.sin((x - px) * 0.9 + rr * 0.35 - k * 1.3)
            sh = 3 + (fold > 0.55) - (fold < -0.55)
            slot = "cloth"
            if x in (px - 6, px + 5) or y > lim - 1:
                slot, sh = "accent", (4 if x == px - 6 else 3)
            c.px(x + shift, y, slot, sh)
    crown = ["#.#.#", "#####", ".###."]
    for j, row in enumerate(crown):
        shift = round(math.sin((6 + j) * 0.42 - k * 1.3) * min(1.6, (6 + j) / 9))
        for i, ch in enumerate(row):
            if ch == "#":
                c.px(px - 3 + i + shift, top + 6 + j, "accent", 5 if j == 0 else 4)


def rally_banner() -> list[Canvas]:
    """Warlord: a war banner drops out of the sky and plants beside the unit; the impact throws a gold shockwave
    out across the tiles around it and gold motes rise off the ring, the banner snapping in the wind, before it
    breaks up into nothing."""
    W, H, cx, gy = 64, 76, 31.5, 58.0
    px, base = 19, 56
    r = tex.rng(1201)
    n = tex.fbm(64, 76, 6, r, octaves=2)
    motes = [(a, r.uniform(0, 1), r.random() < 0.35) for a in np.linspace(0, 360, 14, endpoint=False)]
    drop = [-30, -11, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    rings = [0, 0, 5, 12, 19, 25, 29, 31, 0, 0, 0]
    out = []
    for i in range(11):
        c = _canvas(W, H)
        if 2 <= i <= 6:
            g = (i - 2) / 4
            blobs = [(px - 4 - 4 * g, base + 1, 2.5 + 2.5 * g), (px + 4 + 4 * g, base + 1, 2.5 + 2.5 * g),
                     (px, base - 1 - 2 * g, 2 + 2 * g)]
            _puffs(c, blobs, "smoke", noise=n, erode=max(0, g - 0.35), rough=0.5, sy=1.4)
        if rings[i]:
            f = Field(W, H)
            rad = rings[i]
            front = f.Y > gy
            sh = np.where(front, 5 if i < 5 else 4, 3) - (i >= 6)
            ok = _ring(f, cx, gy, rad, 0.75 if i < 5 else 0.55)
            if i >= 6:
                ok &= (_iso_ang(f, cx, gy) // 12) % 2 == 0
            f.add("main", np.where(ok, sh, EMPTY))
            if 3 <= i <= 5:
                f.add("main", np.where(_ring(f, cx, gy, rad - 4, 0.5), 2, EMPTY))
            f.canvas(c)
        b = _canvas(W, H)
        top = 22 + drop[i]
        _banner(b, px, top, base + drop[i], i)
        b.outline()
        if i >= 8:
            _dissolve(b, [0.3, 0.6, 0.88][i - 8], 1202)
        c.blit(b)
        if i == 0:
            for x in (px - 4, px, px + 4):                          # it falls: streaks above it
                _lmax(c, x, 0, x, top - 10, "main", 3)
        if i == 2:
            _twinkle(c, px - 0.5, base, "core", 3)
        if 3 <= i <= 10:
            for a, ph, big in motes:
                t = i - 3 - ph * 2
                if t < 0 or t > 5:
                    continue
                x, y = _iso(cx, gy, 13 + 13 * ph, a)
                y -= 2 + 3.2 * t
                if big and t < 3:
                    _twinkle(c, x, y, "core", 1, 5 - int(t))
                else:
                    _pmax(c, x, y, "core" if t < 2 else "main", 5 - int(t))
        out.append(c)
    return out


# =============================================================================================================
# Rogue
# =============================================================================================================
def shadowstep() -> list[Canvas]:
    """Shadow: black smoke boils up from the feet and swallows the unit, a pair of eyes glints inside it with the
    flash of a drawn dagger, and the smoke frays away with nobody left in it. The unit is gone from frame 2."""
    cx, gy = 23.5, 43.0
    r = tex.rng(1301)
    n = tex.fbm(48, 48, 6, r, octaves=3)
    body = tex.fbm(48, 48, 4, r, octaves=2)
    keys = [
        ([(18, 41, 3), (30, 41, 3), (24, 40, 4)], 0.0, 0),
        ([(17, 39, 5.5), (31, 39, 5.5), (24, 33, 7), (21, 25, 6), (27, 20, 5)], 0.0, 0),
        ([(15, 38, 7.5), (33, 38, 7.5), (24, 31, 9.5), (18, 24, 7.5), (30, 22, 7.5), (24, 14, 7.5)], -0.05, 0),
        ([(14, 38, 8), (34, 37, 8), (24, 30, 10), (17, 22, 8), (31, 21, 8), (24, 13, 8)], 0.0, 1),
        ([(13, 37, 8), (35, 36, 8), (24, 28, 10), (16, 20, 8), (32, 19, 8), (24, 11, 8)], 0.12, 2),
        ([(13, 35, 8.5), (35, 34, 8.5), (24, 26, 10.5), (16, 18, 8.5), (32, 17, 8.5), (24, 9, 8.5)], 0.3, 3),
        ([(14, 32, 9), (34, 31, 9), (24, 23, 11), (17, 15, 9), (31, 14, 9), (24, 7, 8.5)], 0.5, 0),
        ([(15, 29, 9), (33, 28, 9), (24, 20, 11), (18, 12, 9), (30, 11, 9)], 0.68, 0),
        ([(16, 26, 9), (32, 25, 9), (24, 17, 10), (20, 10, 8)], 0.84, 0),
    ]
    out = []
    for i, (blobs, erode, eyes) in enumerate(keys):
        c = _canvas(48, 48)
        _puffs(c, blobs, "smoke", noise=np.roll(n, -2 * i, 0), erode=erode, rough=0.8, base=2,
               gas=np.roll(body, -2 * i, 0))
        if eyes:
            ey = 19 - (eyes > 2)
            for ex in (21, 26):
                if eyes == 3 and ex == 21:
                    continue
                c.px(ex, ey, "accent", 5)
                c.px(ex + 1, ey, "accent", 4 if eyes < 3 else 3)
        if eyes == 2:                                               # the dagger leaves the sheath
            for k, (x, y) in enumerate(_bres(30, 27, 36, 21)):
                c.px(x, y, "core", 5 if k > 3 else 4)
            _twinkle(c, 36, 21, "core", 2)
        if i == 0:
            for x in (16, 32):
                _pmax(c, x, gy - 4, "main", 3)
        if 1 <= i <= 3:                                             # dark wisps at the rim, violet-lit
            for a in (200, 250, 300, 340):
                x, y = _iso(cx, 30 - 3 * i, 13 + 2 * i, a, 0.9)
                _pmax(c, x, y, "main", 3)
                _pmax(c, x + 1, y - 1, "main", 2)
        out.append(c)
    return out


def snap_trap() -> list[Canvas]:
    """Trickster: a bear trap lies open on the tile, glints, then its toothed jaws slam shut with a spark and a
    puff of dust either side, rattle, and hold the foe's ankle. The jaws are two half-rings hinged along screen x
    that swing from flat to upright; drawn in 3D and projected, so the snap reads at any frame."""
    W, H, cx, cy, R = 44, 40, 21.5, 31.0, 10.5
    angles = [0, 0, 48, 94, 90, 90, 90, 90, 90]
    shake = [0, 0, 0, 1, -1, 1, 0, 0, 0]
    r = tex.rng(1401)
    n = tex.fbm(44, 40, 4, r, octaves=2)
    sparks = [(a, r.uniform(0.8, 1.3)) for a in r.uniform(200, 340, 6)]
    out = []
    for i, ang in enumerate(angles):
        c = _canvas(W, H)
        if 3 <= i <= 7:
            g = (i - 3) / 4
            blobs = [(cx - 10 - 5 * g, cy + 0.5, 2.5 + 2.5 * g), (cx + 10 + 5 * g, cy + 0.5, 2.5 + 2.5 * g)]
            _puffs(c, blobs, "smoke", noise=n, erode=max(0, g - 0.3), rough=0.5, sy=1.5)
        o = _canvas(W, H)
        a = math.radians(ang)
        ox = cx + shake[i]
        open_k = 1 if i == 1 else 0

        def jaw(sign: int, rad: float) -> list[tuple[float, float]]:
            pts = []
            for t in np.linspace(0, math.pi, 30):
                lx, ly = rad * math.cos(t), rad * math.sin(t)
                pts.append((ox + lx, cy + sign * ly * math.cos(a) * 0.5 - ly * math.sin(a)))
            return pts

        def draw_jaw(sign: int, tooth_off: float) -> None:
            for rad, sh in ((R, 3), (R - 0.8, 2)):
                pts = jaw(sign, rad)
                for p, q in zip(pts, pts[1:]):
                    o.line(p[0] - 0.5, p[1] - 0.5, q[0] - 0.5, q[1] - 0.5, "metal", sh)
            for k in range(7):
                t = (k + tooth_off) * math.pi / 7.6 + 0.22
                lx, ly = R * math.cos(t), R * math.sin(t)
                bx, by = ox + lx, cy + sign * ly * math.cos(a) * 0.5 - ly * math.sin(a)
                tl = 0.62
                tx_, ty_ = ox + lx * tl, cy + sign * ly * tl * math.cos(a) * 0.5 - ly * tl * math.sin(a)
                if ang >= 80:
                    ty_ = by + 3
                    tx_ = bx
                o.line(bx - 0.5, by - 0.5, tx_ - 0.5, ty_ - 0.5, "metal", 4)
                o.px(tx_ - 0.5, ty_ - 0.5, "metal", 5)

        draw_jaw(-1, 0.0)
        o.ellipse(ox, cy + open_k * 0.5, 3.6, 1.9, "metal", 2)           # the pan, pressed down on the trigger
        o.px(ox - 1, cy - 1 + open_k, "metal", 4)
        for s in (-1, 1):                                                 # spring levers at the hinges
            x = ox + s * (R + 2.5)
            for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                o.px(x + dx, cy + dy, "metal", 3 if dy <= 0 else 2)
        for k in range(4):                                                # chain trailing off to the stake
            x = ox + R + 5 + k * 2.2
            o.px(x, cy + 2 + k * 0.5, "metal", 3)
            o.px(x + 1, cy + 2 + k * 0.5, "metal", 2 if k % 2 else 4)
        draw_jaw(1, 0.5)
        _lightrim(o, "metal")
        o.outline()
        c.blit(o)
        if i == 0:
            _twinkle(c, ox - 1, cy - 1, "core", 1)
        if i == 2:                                                        # swing lines
            for s in (-1, 1):
                for k in range(3):
                    _pmax(c, ox + s * (R + 1), cy - 3 - 2 * k, "core", 3 - k)
        if i == 3:
            _twinkle(c, ox, cy - R - 1, "core", 3)
        if 3 <= i <= 6:
            t = i - 2
            for aa, sp in sparks:
                rad = (3 + 3 * t) * sp
                x = ox + rad * math.cos(math.radians(aa))
                y = cy - R - 1 + rad * math.sin(math.radians(aa)) + 0.7 * t * t
                _mote(c, x, y, "core", 5 - t + 1, big=t == 1)
        out.append(c)
    return out


def riposte() -> list[Canvas]:
    """Duelist: a blade comes in and is turned aside: two steel lines meet in a cross, ring out in a parry
    spark, and in the same breath the duelist's point darts straight back through the opening with a red flash
    at the tip."""
    W, H, px, py, ty = 40, 32, 21.5, 13.5, 15.5
    r = tex.rng(1501)
    deflect = [(a, r.uniform(0.8, 1.2)) for a in r.uniform(200, 330, 6)]
    out = []
    for i in range(9):
        f = Field(W, H)
        if i == 0:
            for x0, y0, x1, y1 in ((px + 9, py - 9, px + 3, py - 3), (px - 8, py - 8, px - 3, py - 3)):
                d, t = _seg(f, x0, y0, x1, y1)
                f.add("main", np.where(d < 0.6, np.where(t > 0.6, 4, 3), EMPTY))
        if i in (1, 2, 3):
            ext = [6, 7, 5][i - 1]
            for sx in (1, -1):
                d, t = _seg(f, px + sx * ext, py - ext, px - sx * (ext - 2), py + (ext - 2))
                f.add("main", np.where(d < 0.6, [4, 3, 2][i - 1], EMPTY))
        if i == 1:
            f.add("core", _q(1 - f.dist(px, py) / 2.0, [(0, 5)]))
        if i == 2:
            _starburst(f, px, py, 10, 6, 2.6)
            f.add("core", _q(1 - np.abs(f.dist(px, py) - 4.0) / 0.7, [(0, 4)]))
        if i == 3:
            _starburst(f, px, py, 11, 7, 1.6, r0=4)
            f.add("main", np.where((np.abs(f.dist(px, py) - 7) < 0.55) & ((f.ang(px, py) + 400) // 20 % 2 == 0), 3,
                                   EMPTY))
        if i == 4:
            u, s, wd = _spindle(f, 12, ty, 38, ty, 2.6)
            f.add("main", _on(u, np.where(s < 0.55, 5, 4) - (u < 0.3)))
        if i == 5:
            u, s, wd = _spindle(f, 12, ty, 38, ty, 2.0, t0=0.55)
            f.add("main", _on(u, np.where(s < 0.55, 4, 3)))
        if i in (5, 6, 7):
            rad = [2.2, 3.4, 4.6][i - 5]
            e = np.hypot((f.X - 37.5) / (rad * 0.55), (f.Y - ty) / rad)
            ok = np.abs(e - 1) < 0.6 / rad * 1.6
            if i == 7:
                ok &= (f.Y.astype(int) % 2) == 0
            f.add("accent", np.where(ok, [5, 4, 3][i - 5], EMPTY))
        c = f.canvas()
        if i == 5:
            _twinkle(c, 38, ty, "core", 2)
        if 3 <= i <= 6:
            t = i - 2
            for a, sp in deflect:
                rad = (4 + 3 * t) * sp
                _pmax(c, px + rad * math.cos(math.radians(a)), py + rad * math.sin(math.radians(a)) + 0.5 * t * t,
                      "core" if t == 1 else "main", 5 - t + 1)
        if i == 8:
            for x, y in ((36, 11), (39, 18), (34, 19)):
                _pmax(c, x, y, "accent", 2)
        out.append(c)
    return out


# =============================================================================================================
# Wizard
# =============================================================================================================
def sculpted_nova() -> list[Canvas]:
    """Evoker: a ring of fire bursts out across the tiles around the caster, flames licking up off it, but it is
    sculpted: two wedges stay cold, bracketed by arcane ward marks, so allies standing in them are spared. The ring
    cools from the inside, breaks into dashes, and leaves embers rising."""
    W, H, cx, cy = 64, 48, 31.5, 33.0
    gaps = [(45, 24), (205, 24)]
    r = tex.rng(1601)
    ph = r.uniform(0, 6.28, 3)
    embers = [(r.uniform(0, 360), r.uniform(0.6, 1.0), r.uniform(0, 2)) for _ in range(16)]
    rads = [2, 8, 14, 20, 25, 28, 29.5, 30, 30, 30]
    heat = [1, 1, 1, 1, 0.9, 0.75, 0.55, 0.38, 0.22, 0.1]

    def in_gap(a: float, pad: float = 0.0) -> bool:
        return any(abs((a - g + 180) % 360 - 180) < hw + pad for g, hw in gaps)

    out = []
    for i in range(10):
        c = _canvas(W, H)
        rx, ht = rads[i], heat[i]
        if 2 <= i <= 7:                                             # scorch glow inside the ring
            f = Field(W, H, ss=1)
            e = np.hypot((f.X - cx) / rx, (f.Y - cy) / (rx / 2))
            th = _iso_ang(f, cx, cy)
            gap = np.zeros_like(e, bool)
            for g, hw in gaps:
                gap |= np.abs((th - g + 180) % 360 - 180) < hw
            speck = tex.rng(1602).random(e.shape) < 0.05
            ok = (e < 0.9) & (e > 0.3) & ~gap & speck
            f.add("main", np.where(ok, 3 if i < 5 else 2, EMPTY))
            f.canvas(c)
        if i == 0:
            _twinkle(c, cx, cy, "core", 3)
            _mote(c, cx - 0.5, cy - 0.5, "core", 5, big=True)
        for a in np.arange(0, 360, 1.2):
            if in_gap(a):
                continue
            x, y = _iso(cx, cy, rx, a)
            front = math.sin(math.radians(a)) > 0
            if ht < 0.45 and (a // 10) % 3 == 0:
                continue
            if ht < 0.25 and (a // 10) % 3 == 1:
                continue
            wav = 0.5 + 0.5 * math.sin(a * 0.13 + i * 1.7 + ph[0]) * math.sin(a * 0.071 - i * 0.9 + ph[1])
            h = ht * (2 + 7 * wav) * (1 if i > 0 else 0.4) * min(1, rx / 10)
            for dy in range(int(h) + 1):
                u = dy / max(h, 1)
                sh = 5 if (u < 0.25 and front and ht > 0.5) else 4 if u < 0.5 else 3 if u < 0.8 else 2
                sh -= (0 if front else 1) + (ht < 0.5)
                slot = "core" if sh >= 5 else "main"
                _pmax(c, x + math.sin(dy * 0.7 + a) * 0.6 * u, y - dy, slot, sh)
        if 2 <= i <= 8:                                             # ward brackets at the cold wedges
            dim = 0 if i < 6 else i - 5
            for g, hw in gaps:
                for s in (-1, 1):
                    x, y = _iso(cx, cy, rx, g + s * (hw + 1))
                    for k in range(5):
                        _pmax(c, x, y - k, "accent", 5 - dim - (k > 2))
                    _pmax(c, x - s, y - 4, "accent", 4 - dim)
                for k, a in enumerate(np.arange(g - hw + 4, g + hw - 3, 4)):
                    x, y = _iso(cx, cy, rx, a)
                    _pmax(c, x, y, "accent", 3 - dim + (k % 2))
        if i >= 4:
            for a, sp, t0 in embers:
                t = i - 4 - t0
                if t < 0 or in_gap(a, 4):
                    continue
                x, y = _iso(cx, cy, rads[min(i, 9)] * sp, a)
                y -= 4 + 3.4 * t
                _mote(c, x + math.sin(t + a) * 1.2, y, "core" if t < 1.5 else "main", 5 - int(t))
        out.append(c)
    return out


def time_dial() -> list[Canvas]:
    """Chronomancer: a clock face spreads on the floor under the unit, ticks flare, and two hands sweep round it:
    the minute hand twice and the hour hand once per loop, so frames 2-13 wrap exactly; a dashed outer ring turns
    the other way and sand-grain motes rise off the rim. Played forward the hands race (haste); the game plays the
    loop backwards for slow."""
    W, H, cx, cy, RX = 56, 32, 27.5, 15.5, 24.0
    out = []
    for i in range(14):
        k = i - 2
        f = Field(W, H, ss=1)
        th = _iso_ang(f, cx, cy)
        if i == 0:
            f.add("main", np.where(_ring(f, cx, cy, 12), 3, EMPTY))
            f.add("core", np.where(_ring(f, cx, cy, 4), 4, EMPTY))
        else:
            crest = np.cos(np.radians(th - (30 * k - 90 if k >= 0 else 0))) > 0.82
            flash = 1 if i == 1 else 0
            f.add("main", np.where(_ring(f, cx, cy, RX), 4 + flash + (crest & (k >= 0)), EMPTY))
            f.add("main", np.where(_ring(f, cx, cy, RX - 4.5), 3 + flash, EMPTY))
            orbit = _ring(f, cx, cy, RX + 2.5) & (((th + 2.5 * max(k, 0)) % 15) < 6.5)
            f.add("accent", np.where(orbit, 3, EMPTY))
            f.add("main", np.where(_ring(f, cx, cy, 5.5), 2 + flash, EMPTY))
        c = f.canvas()
        if i >= 1:
            for j in range(12):
                a = j * 30 - 90
                major = j % 3 == 0
                x0, y0 = _iso(cx, cy, RX - 4.0 if major else RX - 3.0, a)
                x1, y1 = _iso(cx, cy, RX - 1.0, a)
                _lmax(c, x0 - 0.5, y0 - 0.5, x1 - 0.5, y1 - 0.5, "core" if major else "main", 5 if major else 3)
                if major:
                    _twinkle(c, *_iso(cx, cy, RX - 2.2, a), "core", 1, 5)
        if k >= 0:
            am, ah = 60 * k - 90, 30 * k - 90
            for g, sh in ((40, 2), (20, 3)):                         # afterimages: it is spinning, not ticking
                x, y = _iso(cx, cy, 18, am - g)
                _lmax(c, cx - 0.5, cy - 0.5, x - 0.5, y - 0.5, "main", sh)
            x, y = _iso(cx, cy, 19, am)
            _lmax(c, cx - 0.5, cy - 0.5, x - 0.5, y - 0.5, "core", 5)
            _lmax(c, cx - 0.5, cy - 1.5, x - 0.5, y - 1.5, "core", 4)
            _twinkle(c, x, y - 1, "core", 1)
            x, y = _iso(cx, cy, 13, ah)
            _lmax(c, cx - 0.5, cy - 0.5, x - 0.5, y - 0.5, "accent", 5)
            _lmax(c, cx - 0.5, cy + 0.5, x - 0.5, y + 0.5, "accent", 4)
            _mote(c, cx - 0.5, cy - 0.5, "core", 5, big=True)
            for j in range(8):
                life = (k + j * 3) % 12
                if life >= 6:
                    continue
                x, y = _iso(cx, cy, RX, j * 45 + 20)
                _pmax(c, x, y - 1 - life * 1.6, "core" if life < 2 else "accent", 5 - life // 2 - (life >= 4))
        if i == 1:
            _twinkle(c, *_iso(cx, cy, RX, -90), "core", 2)
        out.append(c)
    return out


def _rock_box(c: Canvas, cx: float, ty: float, gy: float, hw: float, hh: float, seed: int, turf: bool = True) -> None:
    """A rough stone column seen from the iso camera: a diamond top (`hw` x `hh` half-sizes) at height `ty`, two
    visible faces down to the ground at `gy` (left lit, right in shade), strata that ride up with the rock and
    noise cracks. The top carries the tile's turf when `turf`."""
    if gy - ty < 0.5 and not turf:
        return
    r = tex.rng(seed)
    n = tex.fbm(64, 64, 8, r, octaves=2)
    h = gy - ty
    for y in range(int(ty - hh - 1), int(gy + hh + 2)):
        for x in range(int(cx - hw - 1), int(cx + hw + 1)):
            X, Y = x + 0.5, y + 0.5
            dx = X - cx
            top_d = abs(dx) / hw + abs(Y - ty) / hh
            if top_d <= 1.0:
                if not turf:
                    continue
                edge = top_d > 0.78
                sh = 3 + (dx < 0 and Y < ty and edge) - (Y > ty and edge) + (n[y % 64, x % 64] > 0.7)
                c.px(x, y, "turf", sh)
                continue
            if abs(dx) > hw:
                continue
            ey = ty + hh * (1 - abs(dx) / hw)                          # the top's front edge above this column
            by = gy + hh * (1 - abs(dx) / hw)
            if not (ey <= Y <= by):
                continue
            depth = Y - ey
            nv = n[(y - int(round(ty))) % 64, x % 64]
            sh = (3 if dx < 0 else 2) + (depth < 1.5) - (nv < 0.28)
            if int(depth) % 5 == 4:
                sh -= 1
            if abs(dx) < 0.6:
                sh = 4 if depth < h * 0.6 else 3
            c.px(x, y, "stone", sh)
    if turf:
        for k, dx in enumerate((-6, -3, 2, 5)):                       # tufts poking over the rim
            c.px(cx + dx, ty - hh * (1 - abs(dx) / hw) - 1, "turf", 4 if k % 2 else 3)


def _cracks(c: Canvas, cx: float, gy: float, r0: float, r1: float, glow: int, seed: int, n: int = 7) -> None:
    """Jagged ground cracks radiating out from a footprint: glowing earth-light early (`glow` shade), dark after."""
    r = tex.rng(seed)
    for k in range(n):
        a = k * 360 / n + r.uniform(-16, 16)
        pts = [_iso(cx, gy, r0, a)]
        rad = r0
        while rad < r1:
            rad += r.uniform(2, 3.5)
            pts.append(_iso(cx, gy, rad, a + r.uniform(-14, 14)))
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            if glow:
                _lmax(c, x0, y0, x1, y1, "main", glow)
            else:
                for x, y in _bres(x0, y0, x1, y1):
                    if 0 <= x < c.w and 0 <= y < c.h and c.slot[y, x] == 0:
                        c.px(x, y, "stone", 1)


def stone_rise() -> list[Canvas]:
    """Geomancer, raise: the ground cracks with earth-light, then a stone column shoves the unit's tile up two
    height steps, overshooting and settling, throwing rubble and dust off its base. Ground layer: the unit stands
    on top (the manifest's unitLift says how far up, per frame)."""
    W, H, cx, gy = 48, 64, 23.5, 54.0
    heights = [0, 6, 14, 19, 17, 16, 16, 16, 16, 16]
    r = tex.rng(1701)
    n = tex.fbm(48, 64, 6, r, octaves=2)
    debris = [(r.uniform(0, 360), r.uniform(1.0, 2.2), r.uniform(2.6, 4.2), r.random() < 0.5) for _ in range(9)]
    out = []
    for i, h in enumerate(heights):
        c = _canvas(W, H)
        _cracks(c, cx, gy, 9, 20, [5, 4, 3][i] if i < 3 else 0, 1702)
        _rock_box(c, cx, gy - h, gy, 10, 5, 1703)
        b = _canvas(W, H)
        if 2 <= i <= 8:
            g = (i - 2) / 6
            blobs = [(cx - 11 - 3 * g, gy + 1, 3 + 3 * g), (cx + 11 + 3 * g, gy + 1, 3 + 3 * g),
                     (cx - 5, gy + 5, 2.5 + 2.5 * g), (cx + 5, gy + 5, 2.5 + 2.5 * g)]
            _puffs(b, blobs, "smoke", noise=np.roll(n, -i, 0), erode=max(0, g - 0.25), rough=0.5, sy=1.5)
        for a, vx, vy, big in debris:
            t = i - 1
            if t < 0:
                continue
            x0, y0 = _iso(cx, gy, 10, a)
            x = x0 + math.cos(math.radians(a)) * vx * t * 1.5
            y = y0 - (vy * t - 0.55 * t * t) * 1.5
            if y > y0 + 1 or t > 7:
                continue
            b.px(x, y, "stone", 4)
            if big:
                b.px(x + 1, y, "stone", 3)
                b.px(x, y + 1, "stone", 2)
                b.px(x + 1, y + 1, "stone", 1)
        c.outline(skip={"smoke", "main"})
        c.blit(b)
        out.append(c)
    return out


def stone_sink() -> list[Canvas]:
    """Geomancer, lower: cracks of earth-light run round the tile, its top breaks loose and drops one height
    step into a pit with rubble tumbling in after it and dust welling out. Ground layer; the unit sinks with the
    tile (unitLift is negative)."""
    W, H, cx, gy = 48, 48, 23.5, 30.0
    hw, hh = 10.0, 5.0
    depth = [0, 0, 3, 7, 10, 9, 8, 8, 8, 8]
    r = tex.rng(1801)
    n = tex.fbm(48, 48, 6, r, octaves=2)
    rubble = [(r.uniform(-7, 7), r.uniform(-2, 2), r.uniform(0, 1.5), r.random() < 0.5) for _ in range(8)]
    out = []
    for i, d in enumerate(depth):
        c = _canvas(W, H)
        _cracks(c, cx, gy, 11, 19, [5, 4, 4][i] if i < 3 else 0, 1802, n=8)
        if d > 0:
            pit = _canvas(W, H)
            for y in range(int(gy - hh - 1), int(gy + hh + d + 2)):
                for x in range(int(cx - hw - 1), int(cx + hw + 1)):
                    X, Y = x + 0.5, y + 0.5
                    if abs(X - cx) / hw + abs(Y - gy) / hh > 1.0:
                        continue                                     # only inside the opening
                    back = gy - hh * (1 - abs(X - cx) / hw)          # the opening's back edge
                    floor = back + d                                  # the sunk top's back edge
                    if Y < floor:
                        depth_in = Y - back
                        sh = (1 if X < cx else 2) + (depth_in < 1)
                        pit.px(x, y, "stone", sh)
                    elif abs(X - cx) / hw + abs(Y - gy - d) / hh <= 1.0:
                        pit.px(x, y, "turf", 2 + (n[y % 48, x % 48] > 0.6))
            c.blit(pit)
            for x in range(int(cx - hw), int(cx + hw) + 1):            # the cut lip on the near edges
                y = gy + hh * (1 - abs(x + 0.5 - cx) / hw)
                c.px(x, y - 0.5, "stone", 4)
        else:
            plate = _canvas(W, H)
            _rock_box(plate, cx + (1 if i == 1 else 0), gy, gy, hw, hh, 1803)
            c.blit(plate)
        if 2 <= i <= 6:
            t = i - 2
            for dx, dy, t0, big in rubble:
                tt = t - t0
                if tt < 0 or tt > 3:
                    continue
                x = cx + dx * (1 - tt / 4)
                y = gy + dy - 6 + tt * tt * 1.6
                c.px(x, y, "stone", 4)
                if big:
                    c.px(x + 1, y, "stone", 3)
                    c.px(x, y + 1, "stone", 2)
        if 3 <= i <= 9:
            g = (i - 3) / 6
            blobs = [(cx - 6, gy - 2 - 5 * g, 3 + 2.5 * g), (cx + 5, gy - 3 - 6 * g, 3 + 2.5 * g),
                     (cx, gy - 5 - 8 * g, 2.5 + 2.5 * g)]
            _puffs(c, blobs, "smoke", noise=np.roll(n, -2 * i, 0), erode=max(0, g - 0.2) * 1.1, rough=0.5)
        out.append(c)
    return out


# =============================================================================================================
# Cleric
# =============================================================================================================
def revive() -> list[Canvas]:
    """Life Domain: a thread of light reaches down to a fallen ally and a white feather rides it, rocking from
    side to side; when it touches, the beam swells, a ring of light bursts from the ground, two wings of light
    open behind the ally as they rise, and motes drift up."""
    W, H, cx, gy = 32, 72, 15.5, 66.0
    beam = [1.0, 1.0, 1.2, 1.3, 1.4, 1.6, 5.0, 7.0, 6.0, 4.0, 2.0, 0.0]
    r = tex.rng(1901)
    motes = [(cx + r.uniform(-10, 10), gy - r.uniform(0, 14), r.uniform(2.5, 4.5), r.uniform(6, 9)) for _ in range(10)]
    out = []
    for i in range(12):
        f = Field(W, H, ss=1)
        hw = beam[i]
        if hw:
            FX._beam(f, cx, hw, 0, gy, 0 if 6 <= i <= 8 else 1 if i < 10 else 2, i)
        if 6 <= i <= 9:
            rad = [5, 10, 13, 15][i - 6]
            e = f.dist(cx, gy, 2.6)
            f.add("main", _q(1 - np.abs(e - rad) / 0.9, [(0, 4 if i < 8 else 3)]))
            f.add("core", _q(1 - e / (hw + 1.5), [(0, 4), (0.5, 5)]))
        c = f.canvas()
        if i <= 5:                                                   # the feather comes down the beam
            g = Field(W, H)
            y = 6 + i * 10.4
            x = cx + 4 * math.sin(i * 1.2)
            tilt = math.radians(28 * math.sin(i * 1.2 + 1.2))
            dx, dy = 4.5 * math.cos(tilt), 4.5 * math.sin(tilt)
            u, s, wd = _spindle(g, x - dx, y - dy, x + dx, y + dy, 3.6, bulge=0.8, leaf=True)
            g.add("feather", _on(u, np.where(s < 0.5, 5, np.where(s < wd * 0.35, 4, 3))))
            g.canvas(c)
            _pmax(c, x - dx * 1.25, y - dy * 1.25, "feather", 4)
            if i >= 1:
                _pmax(c, x - 4 * math.sin((i - 1) * 1.2) * 0.3, y - 9, "core", 3)
        if i == 6:
            _twinkle(c, cx, gy - 3, "core", 3)
        if 7 <= i <= 10:                                             # wings of light behind the ally
            g = Field(W, H)
            sh = [5, 5, 4, 3][i - 7]
            spread = [1.0, 1.3, 1.4, 1.45][i - 7]
            for s in (-1, 1):
                for k, (ang, ln) in enumerate(((-48, 9), (-22, 10), (4, 8.5), (28, 6.5))):
                    a = math.radians(ang)
                    x0, y0 = cx + s * (2 + 1.5 * k), gy - 26 + 2.5 * k
                    x1 = x0 + s * ln * spread * math.cos(a)
                    y1 = y0 + ln * spread * math.sin(a)
                    u, ss_, wd = _spindle(g, x0, y0, x1, y1, 2.0, bulge=-s * 0.8, leaf=True)
                    g.add("core", _on(u, np.where(u > 0.6, sh, sh - 1)))
            g.canvas(c)
        for x, y0, vy, t0 in motes:
            age = i - t0
            if 0 <= age <= 3:
                _mote(c, x, y0 - vy * age * 2, "core" if age < 1.5 else "main", 5 - int(age), big=age < 1)
        out.append(c)
    return out


def divine_strike() -> list[Canvas]:
    """War Domain: light gathers on the raised weapon, the swing comes down as a broad double crescent of gold,
    and the hit lands as a radiant Latin cross with a ring, sparks and rays thrown off it."""
    W, H, ax, ay = 48, 48, 21.5, 9.5
    cx, cy, R = 21.5, 24.0, 14.0
    hx, hy = 31.5, 33.5
    r = tex.rng(2001)
    sparks = [(a, r.uniform(0.8, 1.25)) for a in np.linspace(0, 360, 9, endpoint=False) + r.uniform(-15, 15, 9)]
    gather = [(a, r.uniform(0.8, 1.2)) for a in np.linspace(0, 360, 6, endpoint=False) + 20]
    keys = {2: (-100, -10, 4.0, 0), 3: (-80, 70, 6.5, 0), 4: (-20, 95, 4.0, 1), 5: (40, 100, 2.6, 2)}
    out = []
    for i in range(9):
        f = Field(W, H)
        if i in keys:
            tail, head, wmax, dim = keys[i]
            _crescent(f, cx, cy, R, tail, head, wmax, dim)
            if i == 3:
                _crescent(f, cx, cy, R + 3.5, -40, 60, 2.0, 1, slot="main")
        if i == 1:
            f.add("main", _q(1 - f.dist(ax, ay) / 3.4, [(0, 4)]))
            f.add("core", _q(1 - f.dist(ax, ay) / 2.0, [(0, 5)]))
        if 4 <= i <= 6:                                              # the hit: a cross of light
            t = i - 4
            arms = [(270, 10 + 3 * t), (180, 8 + 3 * t), (0, 8 + 3 * t), (90, 15 + 4 * t)]
            for deg, ln in arms:
                u = f.spike(hx, hy, deg, 1.5 + 4 * t, ln, 4.2 - 1.0 * t)
                f.add("main", _on(u, np.where(u < 0.6, 4, 3) - t))
                f.add("core", _on(u, np.where(u < 0.35, 5, EMPTY)))
            ring = 1 - np.abs(f.dist(hx, hy) - (4 + 3 * t)) / 0.7
            ok = ring >= 0
            if t:
                ok &= (f.ang(hx, hy) + 400) // 20 % 2 == 0
            f.add("accent", np.where(ok, 4 - t, EMPTY))
            if t == 0:
                f.add("core", _q(1 - f.dist(hx, hy) / 2.6, [(0, 5)]))
        c = f.canvas()
        if i == 0:
            for a, sp in gather:
                rad = 11 * sp
                _pmax(c, ax + rad * math.cos(math.radians(a)), ay + rad * math.sin(math.radians(a)), "main", 3)
            _twinkle(c, ax, ay, "core", 1)
        if i == 1:
            _twinkle(c, ax, ay, "core", 3)
            for a, sp in gather:
                rad = 5 * sp
                _pmax(c, ax + rad * math.cos(math.radians(a)), ay + rad * math.sin(math.radians(a)), "main", 4)
        if i == 3:
            _twinkle(c, *(cx + R * math.cos(math.radians(64)), cy + R * math.sin(math.radians(64))), "core", 2)
        if i >= 5:
            t = i - 4
            for a, sp in sparks:
                rad = (6 + 4 * t) * sp
                x = hx + rad * math.cos(math.radians(a))
                y = hy + rad * math.sin(math.radians(a)) * 0.8 - 2 * t + 0.4 * t * t
                if i == 8 and sp < 1.05:
                    continue
                _mote(c, x, y, "core" if t == 1 else "accent", 5 - t + 1, big=t == 1)
        out.append(c)
    return out


def radiant_burst() -> list[Canvas]:
    """Light Domain: a flash at the chest bursts into a sun of long thin rays while a ring of light races out
    over the tiles; where the ring passes, sparkles hang in the air (what was hidden is revealed) before it fades
    in dashes."""
    W, H, cx, gy = 64, 56, 31.5, 44.0
    sy = gy - 16
    rings = [0, 6, 13, 20, 26, 30, 31, 31, 31, 0]
    r = tex.rng(2101)
    reveal = [(a, r.uniform(0.55, 1.0)) for a in np.linspace(0, 360, 10, endpoint=False) + 9]
    out = []
    for i in range(10):
        f = Field(W, H)
        if rings[i]:
            rad = rings[i]
            front = f.Y > gy
            sh = np.where(front, 5 if i < 4 else 4, 4 if i < 4 else 3) - (i >= 6) - (i >= 8)
            ok = _ring(f, cx, gy, rad, 0.8 if i < 5 else 0.55)
            if i >= 6:
                ok &= (_iso_ang(f, cx, gy) // 10) % 2 == (i % 2)
            f.add("main", np.where(ok, sh, EMPTY))
            if 2 <= i <= 4:
                f.add("accent", np.where(_ring(f, cx, gy, rad - 3, 0.5), 3, EMPTY))
        if 1 <= i <= 4:
            t = i - 1
            for k in range(16):
                ln = (8 + 9 * t if k % 2 == 0 else 5 + 6 * t) * (1 - 0.15 * (t == 3))
                r0 = 0 if t < 2 else 4 * (t - 1)
                if ln <= r0 + 1:
                    continue
                u = f.spike(cx, sy, k * 22.5 - 90, r0, ln, 2.2 if k % 2 == 0 else 1.6)
                f.add("main", _on(u, np.where(u < 0.5, 4, 3) - (t == 3)))
                f.add("core", _on(u, np.where(u < 0.25, 5, EMPTY)))
            f.add("core", _q(1 - f.dist(cx, sy) / [4, 5, 3.5, 2][t], [(0, 4), (0.4, 5)]))
        if i == 0:
            f.add("core", _q(1 - f.dist(cx, sy) / 2.2, [(0, 5)]))
        c = f.canvas()
        if i == 0:
            _twinkle(c, cx, sy, "core", 2)
        for a, sp in reveal:
            when = 2 + sp * 4
            age = i - when
            if 0 <= age < 4:
                x, y = _iso(cx, gy, 31 * sp, a)
                _twinkle(c, x, y - 3 - age * 1.5, "core", 2 if age < 1 else 1 if age < 2.5 else 0, 5 - int(age))
        out.append(c)
    return out


# =============================================================================================================
# Ranger
# =============================================================================================================
def deadeye() -> list[Canvas]:
    """Sharpshooter (on the target): a red reticle closes on the target and locks with a scope glint back at the
    shooter, then the shot arrives as one long streak of light from the left, punches through with a star and the
    reticle's quarters fly apart. The streak spans 64 px: the shooter stands two tiles away."""
    W, H, tx, ty = 80, 40, 63.5, 19.5
    out = []
    for i in range(11):
        f = Field(W, H)
        rr = {0: 16, 1: 12, 2: 9.5, 3: 9.0, 4: 9.0, 5: 7.0}.get(i)
        rot = {0: 45, 1: 20}.get(i, 0)
        if rr is not None:
            d = f.dist(tx, ty)
            th = f.ang(tx, ty)
            near_tick = np.zeros_like(d, bool)
            for k in range(4):
                near_tick |= np.abs((th - rot - 90 * k + 180) % 360 - 180) < 14
            sh = [2, 3, 4, 5, 4, 3][i]
            f.add("accent", np.where((np.abs(d - rr) < 0.6) & ~near_tick, sh, EMPTY))
            for k in range(4):
                a = math.radians(rot + 90 * k)
                dd, t = _seg(f, tx + (rr - 3.5) * math.cos(a), ty + (rr - 3.5) * math.sin(a),
                             tx + (rr + 2) * math.cos(a), ty + (rr + 2) * math.sin(a))
                f.add("accent", np.where(dd < 0.6, sh, EMPTY))
            if i >= 2:
                f.add("accent", _q(1 - f.dist(tx, ty) / 1.2, [(0, sh)]))
        if i == 4:                                                   # the streak arrives
            d, t = _seg(f, 1, ty, 60, ty)
            f.add("main", np.where(d < 1.6, np.where(t > 0.7, 4, np.where(t > 0.3, 3, 2)), EMPTY))
            f.add("core", np.where((d < 0.6) & (t > 0.35), 5, EMPTY))
            u = f.spike(57, ty, 0, 0, 6, 3.2)
            f.add("core", _on(u, 5))
        if i == 5:
            d, t = _seg(f, 30, ty, 60, ty)
            f.add("main", np.where(d < 0.6, np.where(t > 0.6, 4, 2), EMPTY))
            _starburst(f, tx, ty, 10, 7, 2.6)
            f.add("core", _q(1 - f.dist(tx, ty) / 2.4, [(0, 5)]))
        if i == 6:
            d, t = _seg(f, 46, ty, 60, ty)
            f.add("main", np.where((d < 0.6) & (np.floor(f.X) % 3 != 0), 2, EMPTY))
            _starburst(f, tx, ty, 12, 8, 1.6, r0=5)
            f.add("main", _q(1 - np.abs(f.dist(tx, ty) - 6) / 0.6, [(0, 3)]))
        if i in (7, 8):                                              # the reticle's quarters fly apart
            out_r = [13, 17][i - 7]
            for k in range(4):
                a = 45 + 90 * k
                th = f.ang(tx, ty)
                ok = (np.abs(f.dist(tx, ty) - out_r) < 0.6) & (np.abs((th - a + 180) % 360 - 180) < 20)
                f.add("accent", np.where(ok, [3, 2][i - 7], EMPTY))
        c = f.canvas()
        if i == 3:
            _twinkle(c, 3, ty - 2, "core", 3)                        # the scope glint, back at the shooter
            _twinkle(c, tx, ty, "core", 1)
        if i in (2, 3):
            _pmax(c, 3, ty - 2, "core", 3 + (i == 3))
        if 5 <= i <= 9:
            t = i - 4
            for a in (200, 160, 30, 330, 100, 260):
                rad = 4 + 3.5 * t
                _pmax(c, tx + rad * math.cos(math.radians(a)), ty + rad * math.sin(math.radians(a)) + 0.4 * t * t,
                      "core" if t < 2 else "main", 5 - t + 1)
        out.append(c)
    return out


PAW = [(0.0, 3.2, 4.4, 3.4), (-5.4, -2.6, 1.8, 2.3), (-2.0, -5.8, 1.8, 2.4), (2.0, -5.8, 1.8, 2.4),
       (5.4, -2.6, 1.8, 2.3)]


def hunt_mark() -> list[Canvas]:
    """Beastmaster: a glowing paw print stamps down over the target (the beast's next order), overshooting and
    settling with a ring, then hovers (frames 4-9 loop) pulsing, raked once a loop by three red claw marks while
    two motes circle it."""
    W, H, cx, cy = 32, 34, 15.5, 15.0
    scales = [1.65, 1.18, 0.93, 1.0, 1, 1, 1, 1, 1, 1]
    out = []
    for i, s in enumerate(scales):
        k = i - 4
        f = Field(W, H)
        e = np.full(f.X.shape, 9.0)
        lit = np.zeros_like(e)
        for ox, oy, rx, ry in PAW:
            ee = np.hypot((f.X - cx - ox * s) / (rx * s), (f.Y - cy - oy * s) / (ry * s))
            sel = ee < e
            e = np.where(sel, ee, e)
            lit = np.where(sel, -((f.X - cx - ox * s) + (f.Y - cy - oy * s)), lit)
        pulse = [0, 1, 0, 0, -1, 0][k] if k >= 0 else 0
        if i == 0:
            f.add("main", np.where((e < 1) & (e > 1 - 0.22), 3, EMPTY))
        else:
            fill = 3 + (i == 1) + pulse
            f.add("main", np.where(e < 1, np.where(e > 0.72, np.where(lit > 0, 5, 3), fill), EMPTY))
            f.add("core", np.where((e < 0.45) & (lit > 0.5), 5, EMPTY))
        if i in (2, 3):
            rad = [10, 13][i - 2]
            ok = np.abs(f.dist(cx, cy + 1, 1.1) - rad) < 0.6
            if i == 3:
                ok &= _checker_s(f)
            f.add("accent", np.where(ok, [4, 2][i - 2], EMPTY))
        if k in (2, 3):
            for j in range(3):
                x0 = cx - 7 + j * 4
                _cut(f, x0, cy - 9, x0 + 6, cy + 8, 2.4 if k == 2 else 1.6, 0.6, 0, 1, 0 if k == 2 else 1,
                     slot="accent", dash=k == 3)
        c = f.canvas()
        if k >= 0:
            for j in range(2):
                a = j * 180 + k * 60
                x, y = _iso(cx, cy + 2, 12, a, 0.35)
                front = math.sin(math.radians(a)) > 0
                _mote(c, x, y, "core" if front else "main", 5 if front else 3, big=front)
        out.append(c)
    return out


def vine_snare() -> list[Canvas]:
    """Warden: the ground tears and bark-brown roots whip up round the target: two lash across the shins and hook
    shut, two arch in from the sides and two tall ones rear up behind, curling over; then they hold (frames 6-9
    loop), flexing as the target strains, leaves shivering. Brown roots and pale thorns, so they stay readable on
    green goblins and green grass alike."""
    W, H, cx, gy = 48, 44, 23.5, 35.0
    r = tex.rng(2301)
    n = tex.fbm(48, 44, 4, r, octaves=2)
    vines = [  # base angle, control and tip (relative to the base), thickness; drawn back to front
        (215, (-9, -13), (-2, -18), 1.6), (325, (9, -13), (2, -17), 1.6),
        (180, (-7, -9), (-1, -14), 1.5), (0, (7, -11), (1, -16), 1.5),
        (135, (2, -13), (11, -9), 1.9), (55, (-3, -7), (-10, -5), 1.9)]
    grow = [0.15, 0.45, 0.8, 1.0, 1.05, 1.0, 1.0, 1.0, 1.0, 1.0]
    out = []
    for i, g in enumerate(grow):
        k = i - 6
        c = _canvas(W, H)
        if i <= 4:
            gg = i / 4
            blobs = [(*_iso(cx, gy, 10, a), 2 + 1.5 * gg) for a, *_ in vines]
            _puffs(c, blobs, "smoke", noise=n, erode=max(0, gg - 0.3), rough=0.5, sy=1.5)
        o = _canvas(W, H)
        for j, (a, (c1x, c1y), (tx, ty), thick) in enumerate(vines):
            bx, by = _iso(cx, gy, 10, a)
            sway = 0.9 * math.sin(k * math.pi / 2 + j * 1.7) if k >= 0 else 0
            p1 = (bx + c1x, by + c1y)
            p2 = (bx + tx + sway, by + ty + abs(sway) * 0.5)
            pts = []
            for t in np.arange(0, min(g, 1.0) + 1e-6, 0.025):
                pts.append(((1 - t) ** 2 * bx + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
                            (1 - t) ** 2 * by + 2 * (1 - t) * t * p1[1] + t * t * p2[1]))
            if g >= 1.0:                                              # the tip curls over into a hook
                dx, dy = p2[0] - p1[0], p2[1] - p1[1]
                ln = math.hypot(dx, dy)
                ux, uy = dx / ln, dy / ln
                side = 1 if tx * c1y - ty * c1x > 0 else -1
                nx, ny = -uy * side, ux * side
                for th in np.linspace(0, 1.4 * math.pi, 14):
                    rr = 2.0 * (1 - th / (2 * math.pi))
                    pts.append((p2[0] + nx * rr + (ux * math.sin(th) - nx * math.cos(th)) * rr,
                                p2[1] + ny * rr + (uy * math.sin(th) - ny * math.cos(th)) * rr))
            if len(pts) < 2:
                continue
            _tube(o, pts, thick, 0.5, "wood")
            m = len(pts)
            for s_leaf, flip in ((0.42, 1), (0.72, -1)):
                if g >= s_leaf + 0.15:
                    x, y = pts[int(s_leaf * (m - 1))]
                    flick = 1 if (k >= 0 and (k + j) % 4 == 0) else 0
                    if flip > 0:
                        _glyph(o, x + 1, y - 2 - flick, [".##", "##."], "leaf", 3)
                    else:
                        _glyph(o, x - 3, y - 2 - flick, ["##.", ".##"], "leaf", 3)
            for s_th in (0.3, 0.6):
                if g >= s_th:
                    x, y = pts[int(s_th * (m - 1))]
                    o.px(x + (1.6 if a in (0, 55, 325) else -2.6), y - 1, "accent", 4)
        _lightrim(o, "leaf")
        o.outline()
        c.blit(o)
        out.append(c)
    return out


# =============================================================================================================
# Paladin
# =============================================================================================================
def bulwark_link() -> list[Canvas]:
    """Guardian: a bright tether arcs from the guardian to the ally they are covering and a translucent kite
    shield snaps up in front of the ally; then it holds (frames 4-9 loop): pulses run along the arc towards the
    ally and a glint slides down the shield. The art spans 64 px from the anchor along +x; the game rotates and
    stretches it to the real pair of units."""
    W, H, sx, sy, tx, ty = 80, 48, 8.0, 30.0, 72.0, 30.0

    def arc(t: float, peak: float) -> tuple[float, float]:
        return sx + (tx - sx) * t, sy - 4 * peak * t * (1 - t)

    reach = [0.25, 0.65, 1.0, 1.0]
    shield_s = [0, 0, 1.75, 1.35]
    out = []
    for i in range(10):
        k = i - 4
        c = _canvas(W, H)
        rch = reach[i] if i < 4 else 1.0
        for peak, base in ((17, 4), (12, 2)):
            prev = None
            for t in np.linspace(0, rch, 160):
                x, y = arc(t, peak)
                pulse = k >= 0 and ((t * 64 - 64 / 6 * k) % (64 / 3)) < 3.2
                head = i < 2 and t > rch - 0.06
                sh = base + (1 if pulse else 0) + (1 if head else 0) - (0 if t > 0.15 else 1)
                if prev is None or (int(x), int(y)) != prev:
                    _pmax(c, x, y, "core" if (pulse or head) and base == 4 else "main", sh)
                prev = (int(x), int(y))
        if i < 2:
            _twinkle(c, *arc(rch, 17), "core", 2)
        f = Field(W, H)
        f.add("main", _q(1 - np.abs(f.dist(sx, sy) - 3) / 0.6, [(0, 4 if i < 4 else 3)]))
        s = shield_s[i] if i < 4 else 1.35
        if s:
            u = (f.X - (tx - 7)) / s
            v = (f.Y - (ty - 8 * s)) / s                                 # 0 at the shield's top edge
            halfw = np.where(v < 7, 6.0 - 0.06 * v * v, 6.0 * np.clip(1 - (v - 7) / 9, 0, 1) ** 0.8)
            inside = (v >= 0) & (v <= 16) & (np.abs(u) <= halfw)
            rim = inside & ((np.abs(u) > halfw - 1.1 / s) | (v < 1.1))
            flash = 1 if i == 2 else 0
            f.add("main", np.where(inside & ~rim & _checker_s(f), 1 + flash, EMPTY))
            f.add("main", np.where(rim, np.where((u < 0) | (v < 1.1), 4, 3) + flash, EMPTY))
            cross = inside & (((np.abs(u) < 0.8) & (v > 3) & (v < 13)) | ((np.abs(v - 6.5) < 0.8) & (np.abs(u) < 3.6)))
            f.add("core", np.where(cross, 4 + flash, EMPTY))
            if k >= 0:
                g = v - (k * 3.4 - 2) + u * 0.6
                f.add("core", np.where(inside & (np.abs(g) < 0.8), 5, EMPTY))
        f.canvas(c)
        out.append(c)
    return out


def smite() -> list[Canvas]:
    """Crusader: a cross of light kindles high above the target, then drops: a column of holy light slams down
    with the cross burning inside it and a cross flares on the ground along the tile's axes, the shock ring
    racing out; then it all narrows away into rising motes."""
    W, H, cx, gy = 48, 88, 23.5, 82.0
    r = tex.rng(2501)
    motes = [(cx + r.uniform(-10, 10), gy - r.uniform(0, 16), r.uniform(2.5, 4.5), r.uniform(5, 8)) for _ in range(10)]
    beam = [0, 0, 1.6, 6.0, 7.5, 5.0, 3.0, 1.2, 0, 0, 0]
    out = []

    def cross(f: Field, x: float, y: float, s: float, shade: int) -> None:
        for x0, y0, x1, y1 in ((x, y - 6 * s, x, y + 13 * s), (x - 7 * s, y, x + 7 * s, y)):
            d, t = _seg(f, x0, y0, x1, y1)
            f.add("main", np.where(d < 1.6 * s, shade - 1, EMPTY))
            f.add("core", np.where(d < 0.6 * s + 0.2, shade, EMPTY))

    for i in range(11):
        f = Field(W, H, ss=1)
        if beam[i]:
            FX._beam(f, cx, beam[i], 0, gy, 0 if i <= 5 else 1, i)
        if i == 0:
            cross(f, cx, 14, 0.6, 4)
        if i == 1:
            cross(f, cx, 14, 1.0, 5)
            _starburst(f, cx, 14, 9, 5, 1.6, r0=3)
        if i == 2:
            cross(f, cx, 40, 1.0, 5)
        if i in (3, 4, 5):
            cross(f, cx, gy - 40, 1.6 - 0.25 * (i - 3), 5 - (i == 5))
        if 3 <= i <= 9:                                              # the cross on the ground, along the tile axes
            t = i - 3
            sh = [5, 5, 4, 4, 3, 2, 2][t]
            for deg, ln in ((26.57, 17), (153.43, 13), (206.57, 13), (333.43, 13)):
                ln = ln * min(1, 0.6 + 0.2 * t)
                u = f.spike(cx, gy, deg, 0, ln, 3.2)
                f.add("main", _on(u, np.where(u < 0.5, sh, sh - 1)))
            if t <= 3:
                rad = [6, 12, 17, 20][t]
                ok = _ring(f, cx, gy, rad, 0.6)
                if t == 3:
                    ok &= (_iso_ang(f, cx, gy) // 12) % 2 == 0
                f.add("accent", np.where(ok, [5, 4, 3, 2][t], EMPTY))
                f.add("core", _q(1 - f.dist(cx, gy, 2.4) / (beam[i] + 2), [(0, 4), (0.5, 5)]))
        c = f.canvas()
        if i == 3:
            _twinkle(c, cx, gy - 2, "core", 3)
        for x, y0, vy, t0 in motes:
            age = i - t0
            if 0 <= age <= 3.5:
                _mote(c, x, y0 - vy * age * 2, "core" if age < 1.5 else "main", 5 - int(age), big=age < 1)
        out.append(c)
    return out


def vengeance_brand() -> list[Canvas]:
    """Avenger: a red brand sears onto whoever hurt an ally: a sword point-down through a ring, flanked by flame
    horns. It strikes white-hot and oversized, cools to red as it settles, then hovers (frames 5-10 loop) with a
    heartbeat pulse, embers lifting off the horns and a drop of red falling from the point."""
    W, H, cx, cy = 32, 36, 15.5, 14.5
    scales = [0.4, 1.3, 1.06, 1.0, 1.0]
    out = []
    for i in range(11):
        k = i - 5
        f = Field(W, H)
        s = scales[i] if i < 5 else 1.0
        beat = [1, 0, 1, 0, 0, 0][k] if k >= 0 else 0
        hot = {1: 2, 2: 1}.get(i, 0)
        if i == 0:
            f.add("core", _q(1 - f.dist(cx, cy) / 3, [(0, 4), (0.5, 5)]))
        else:
            f.add("main", _q(1 - np.abs(f.dist(cx, cy + 1 * s) - 7 * s) / 0.75, [(0, 3 + hot + beat)]))
            for sgn in (-1, 1):                                       # flame horns
                u = f.spike(cx + sgn * 6 * s, cy - 2 * s, -90 + sgn * 38, 0, 8 * s, 3.4 * s)
                f.add("main", _on(u, np.where(u < 0.5, 4, 3) + min(hot, 1) + beat))
                f.add("accent", _on(u, np.where(u > 0.7, 5, EMPTY)))
            top, tip = cy - 9 * s, cy + 13 * s
            blade = (np.abs(f.X - cx) < 1.4 * s * np.clip((tip - f.Y) / (5 * s), 0, 1) + 0.15) & \
                    (f.Y > cy - 4.5 * s) & (f.Y < tip)
            guard = (np.abs(f.Y - (cy - 4.5 * s)) < 0.8) & (np.abs(f.X - cx) < 5 * s)
            grip = (np.abs(f.X - cx) < 0.7) & (f.Y > top) & (f.Y < cy - 4.5 * s)
            pommel = f.dist(cx, top) < 1.4 * s
            f.add("core", np.where(blade | guard | grip | pommel, np.where(f.X < cx, 5, 4) if hot or beat else
                                   np.where(f.X < cx, 4, 3), EMPTY))
        c = f.canvas()
        if i == 1:
            for a in range(0, 360, 45):
                x = cx + 13 * math.cos(math.radians(a))
                y = cy + 13 * math.sin(math.radians(a))
                _pmax(c, x, y, "accent", 4)
        if k >= 0:
            for sgn, ph in ((-1, 0), (1, 3)):
                life = (k + ph) % 6
                if life < 4:
                    x = cx + sgn * (6 + 8 * 0.6) + sgn * 0.5 * life
                    y = cy - 2 - 7 - 1.6 * life
                    _pmax(c, x, y, "accent", 5 - life)
            dy = [0, 1, 3, 5, 8, 11][k]
            if k < 5:
                _pmax(c, cx, cy + 14 + dy, "main", 4 - (k >= 3))
                if k >= 2:
                    _pmax(c, cx, cy + 13 + dy, "main", 2)
        out.append(c)
    return out


# =============================================================================================================
# Bard
# =============================================================================================================
def war_horn() -> list[Canvas]:
    """College of Valor: the horn sounds: three soundwaves roll out to both sides from the bard, each a bold
    arc that thins and breaks as it spreads, a shock ring crosses the floor, and gold motes rise on the call."""
    W, H, cx, cy, gy = 64, 60, 31.5, 26.0, 50.0
    spawns = [0, 2, 4]
    r = tex.rng(2701)
    motes = [(r.uniform(4, 60), r.uniform(26, 44), r.uniform(2, 5), r.random() < 0.35) for _ in range(12)]
    out = []
    for i in range(10):
        f = Field(W, H)
        th = f.ang(cx, cy)
        side = (np.abs(th) < 52) | (np.abs(th) > 128)
        for s0 in spawns:
            a = i - s0
            if a < 0 or a > 5:
                continue
            rad = 5 + 5.4 * a
            w = 1.3 - 0.15 * a
            d = np.abs(np.hypot(f.X - cx, (f.Y - cy) * 1.25) - rad)
            ok = (d < w) & side
            if a >= 3:
                ok &= ((th + 400) // 8) % 3 != 0
            f.add("main", np.where(ok, np.where(d < w * 0.45, 5 - a // 2, 4 - a // 2), EMPTY))
            if a == 0:
                f.add("core", np.where(ok, 5, EMPTY))
        if 1 <= i <= 5:
            rad = [5, 11, 17, 22, 26][i - 1]
            ok = _ring(f, cx, gy, rad, 0.6)
            if i == 5:
                ok &= _checker_s(f)
            f.add("accent", np.where(ok, [4, 4, 3, 3, 2][i - 1], EMPTY))
        c = f.canvas()
        if i == 0:
            _twinkle(c, cx, cy, "core", 2)
        for x, y, t0, big in motes:
            age = i - 2 - t0
            if 0 <= age < 4:
                if big and age < 2:
                    _twinkle(c, x, y - age * 2.5, "core", 1, 5 - int(age))
                else:
                    _pmax(c, x, y - age * 2.5, "core" if age < 2 else "main", 5 - int(age))
        out.append(c)
    return out


RUNES = [["#.#.#", ".###.", "..#..", "..#..", "..#.."], ["..#..", ".#.#.", "..#..", ".#.#.", "#...#"],
         ["#....", "##...", "#.#..", "##...", "#...."], ["#...#", "##.##", "#.#.#", "##.##", "#...#"],
         ["..#..", ".###.", "#.#.#", "..#..", "..#.."], ["#...#", ".#.#.", "..#..", ".#.#.", "#...#"]]


def glyph_echo() -> list[Canvas]:
    """College of Lore: runes peel up round the bard and spiral in and upward (the near ones bright, the far ones
    dim), converge over the raised hands with a flash, and are written into a spectral open book that glows and
    fades: an enemy's trick, copied."""
    W, H, cx = 48, 48, 23.5
    out = []
    for i in range(12):
        c = _canvas(W, H)
        if i <= 7:
            t = i / 7
            rad = 17 * (1 - t) ** 0.85 + 2
            yc = 38 - 25 * t ** 1.1
            for j, rune in enumerate(RUNES):
                a = j * 60 + i * 34
                x, y = _iso(cx, yc, rad, a, 0.42)
                front = math.sin(math.radians(a)) > 0
                sh = (4 if front else 2) + (1 if 2 <= i <= 5 and front else 0) - (i == 0)
                if i == 0 and j % 2:
                    continue
                if i >= 1:
                    tp = (i - 1) / 7
                    xp, yp = _iso(cx, 38 - 25 * tp ** 1.1, 17 * (1 - tp) ** 0.85 + 2, a - 34, 0.42)
                    _pmax(c, xp, yp, "main", 2)
                _glyph(c, x - 2.5, y - 2.5, rune, "main", sh)
        if i == 8:
            f = Field(W, H)
            f.add("core", _q(1 - np.abs(f.dist(cx, 11) - 5) / 0.7, [(0, 4)]))
            f.canvas(c)
            _twinkle(c, cx, 11, "core", 3)
        if 9 <= i <= 11:                                              # the open book, copied rune on its page
            sh = [5, 4, 3][i - 9]
            bx, by = cx, 14
            for s in (-1, 1):
                pts = [(bx, by + 1), (bx + s * 10, by - 1), (bx + s * 10, by - 9), (bx, by - 7)]
                for k, (p, q) in enumerate(zip(pts, pts[1:] + pts[:1])):
                    if i == 11 and k % 2:
                        continue
                    _lmax(c, p[0], p[1], q[0], q[1], "accent", sh)
            for ln in (2, 4):
                _lmax(c, bx - 8, by - 7 + ln, bx - 2, by - 6 + ln, "accent", sh - 2)
            _glyph(c, bx + 3, by - 7, RUNES[3], "core", sh, lit=False)
            if i == 9:
                for a in (200, 240, 300, 340):
                    x, y = _iso(bx, by - 4, 14, a, 0.6)
                    _pmax(c, x, y, "main", 3)
        out.append(c)
    return out


NOTES = [[".#.", ".##", ".#.", "##.", "##."], [".####", ".#..#", ".#..#", "##.##", "##.##"]]


def charm_song() -> list[Canvas]:
    """College of the Siren: the lyre's song streams out towards the target as bobbing notes, and two pink
    heart-waves swell out along it, the second chasing the first, then break up as the charm takes."""
    W, H, sx, sy = 56, 48, 21.5, 28.0
    out = []
    for i in range(12):
        c = _canvas(W, H)
        for n in range(8):
            a = i - n
            if a < 0 or a > 4.5:
                continue
            x = sx + 1 + 4.6 * a
            y = sy - 2 - 1.6 * a + 3 * math.sin(a * 1.3 + n * 1.9)
            sh = 5 if a < 1 else 4 if a < 3 else 3
            _glyph(c, x, y - 5, NOTES[n % 2], "accent", sh - 1)
        for s0 in (2, 5):
            a = i - s0
            if a < 0 or a > 5:
                continue
            s = 0.2 + 0.1 * a
            _heart(c, sx + 9 + 4.2 * a, sy - 6, s, "main", 5 - a // 2, dash=a >= 3)
            if a == 0:
                _heart(c, sx + 9, sy - 6, 0.08, "core", 5)
        if i in (6, 8, 10):
            for x, y in ((40, 12), (47, 22), (36, 26)):
                _twinkle(c, x + (i - 6), y - (i - 6), "core", 1 if i < 10 else 0, 5 - (i - 6) // 2)
        out.append(c)
    return out


# =============================================================================================================
# Druid
# =============================================================================================================
LEAVES = [[(2, 0, 4), (1, 1, 4), (2, 1, 3), (0, 2, 3), (1, 2, 2)], [(0, 0, 4), (0, 1, 3), (1, 1, 4), (1, 2, 3),
                                                                     (2, 2, 2)],
          [(1, 0, 4), (2, 0, 4), (0, 1, 3), (1, 1, 3), (2, 1, 2), (3, 0, 3)], [(0, 0, 4), (0, 1, 4), (1, 1, 3),
                                                                               (0, 2, 3), (1, 2, 2)]]


def wild_shape() -> list[Canvas]:
    """Shapeshifter: leaves lift off the ground in a loose ring, wind up into a tight vortex round the druid with
    green streaks of power, and burst outward in a puff of green light; what stands in it afterwards is the beast
    (the unit swaps at frame 5). The leaves tumble and drift down as they thin out."""
    W, H, cx, gy = 48, 52, 23.5, 44.0
    r = tex.rng(2901)
    n = tex.fbm(48, 52, 4, r, octaves=2)
    body = tex.fbm(48, 52, 4, r, octaves=2)
    leaves = [(r.uniform(0, 360), r.uniform(0, 1), r.uniform(0.85, 1.15), int(r.integers(0, 4)), r.random() < 0.18,
               r.uniform(-0.6, 0.6)) for _ in range(26)]
    out = []
    for i in range(11):
        c = _canvas(W, H)
        if 5 <= i <= 7:
            blobs = [(cx, gy - 14, 10), (cx - 7, gy - 6, 7), (cx + 7, gy - 6, 7), (cx, gy - 25, 8)]
            if i > 5:
                blobs = [(x + (x - cx) * 0.3 * (i - 5), y - 2 * (i - 5), rr * (1 + 0.15 * (i - 5)))
                         for x, y, rr in blobs]
            _puffs(c, blobs, "core", noise=np.roll(n, -i, 0), erode=[-0.05, 0.3, 0.6][i - 5], rough=0.7, base=4,
                   gas=np.roll(body, -2 * i, 0))
        if 2 <= i <= 4:
            for j in range(3):
                _swoosh(c, cx, gy - 6 - 11 * j - 2 * (i - 2), 11 - j, 60 * i + 120 * j, 200, "core", 5)
        for k, (a0, hf, sp, kind, autumn, vy) in enumerate(leaves):
            if i <= 1:
                rad = 14 * sp
                a = a0 + 25 * i
                y0 = gy - 1 - hf * (3 + 8 * i)
            elif i <= 4:
                rad = [12, 11, 10.5][i - 2] * sp
                a = a0 + 60 * i
                y0 = gy - 3 - hf * [18, 28, 34][i - 2]
            else:
                t = i - 4
                if t > 2 and k % 5 < (t - 2):
                    continue
                rad = (12 + 5.5 * t) * sp
                a = a0 + 60 * 4 + 12 * t
                y0 = gy - 3 - hf * 34 - 1.5 * t + (0.35 + vy * 0.2) * t * t
            x, y = _iso(cx, y0, rad, a, 0.45)
            front = math.sin(math.radians(a)) > 0
            for dx, dy, sh in LEAVES[(kind + i) % 4]:
                sh = sh - (0 if front else 1) - (i >= 9)
                c.px(x + dx, y + dy, "accent" if autumn else "leaf", sh)
        out.append(c)
    return out


def storm_zone() -> list[Canvas]:
    """Stormcaller: a thunderhead gathers over the tiles and rain starts; then it holds as a zone (frames 3-10
    loop): the cloud churns, rain streaks fall and splash inside the zone's ellipse, puddle rings spread, and
    once a loop lightning cracks down with the cloud lit from below. Every motion moves a whole period per loop."""
    W, H, cx, gy = 64, 84, 31.5, 72.0
    RX, RY = 26.0, 13.0
    r = tex.rng(3001)
    body = np.tile(tex.fbm(32, 84, 4, r, octaves=2), (1, 2))
    edge = np.tile(tex.fbm(32, 84, 4, r, octaves=2), (1, 2))
    base = [(14, 20, 7), (22, 14, 8), (33, 12, 9.5), (44, 15, 8.5), (52, 20, 6.5), (20, 23, 6.5), (32, 24, 7.5),
            (44, 24, 6.5), (27, 19, 7)]
    phases = r.uniform(0, 2 * math.pi, len(base))
    drops = []
    for _ in range(30):
        x = r.uniform(cx - RX * 0.95, cx + RX * 0.95)
        lim = RY * math.sqrt(max(0.0, 1 - ((x - cx) / RX) ** 2))
        drops.append((x, gy + r.uniform(-lim, lim), r.uniform(0, 1)))
    puddles = [(cx - 12, gy + 3, 0), (cx + 9, gy - 4, 3), (cx + 3, gy + 7, 5), (cx - 4, gy - 6, 6)]
    bolt = FX._bolt_path(r, 40, 26, 36, gy - 1, step=3, jag=2.2)
    out = []
    for i in range(11):
        k = i - 3
        kk = max(k, 0)
        c = _canvas(W, H)
        w = 2 * math.pi * kk / 8
        grow = [0.55, 0.8, 0.95][i] if i < 3 else 1.0
        blobs = [(cx + (x - cx) * grow, y + 0.8 * math.sin(w + p) + (1 - grow) * 4,
                  rr * grow + 0.6 * math.sin(w + p + 1.1)) for (x, y, rr), p in zip(base, phases)]
        _puffs(c, blobs, "smoke", noise=np.roll(edge, 4 * kk, 1), erode=[0.3, 0.15, 0.06][i] if i < 3 else 0.04,
               rough=0.6, base=2, gas=np.roll(body, -4 * kk, 1))
        if k == 5:                                                     # lit from below by the strike
            m = c.mask("smoke")
            below = np.zeros_like(m)
            below[:-3] = m[3:]
            lit = m & ~below
            c.shade[lit] = np.minimum(5, c.shade[lit] + 2)
        f = Field(W, H, ss=1)
        ok = _ring(f, cx, gy, RX + 1, 0.55, RY / RX) & ((_iso_ang(f, cx, gy) + 45 * kk) // 15 % 2 == 0)
        f.add("main", np.where(ok, 2, EMPTY))
        f.canvas(c)
        if i >= 1:
            for x, ly, ph in drops:
                if i < 3 and ph > [0, 0.35, 0.7][i]:
                    continue
                top = 26 + (x - cx) * 0.1
                fall = ly - top
                u = (ph + kk / 8) % 1.0 if i >= 3 else ph * 0.6 + 0.1 * i
                y = top + u * fall
                xs = x - 0.18 * (y - top)
                if y < ly - 1:
                    _lmax(c, xs, y, xs + 0.6, y - 3, "main", 4)
                    _pmax(c, xs, y, "core", 4)
                if i >= 3 and u < 1 / 8:
                    lx = x - 0.18 * fall
                    _pmax(c, lx - 1, ly - 1, "main", 3)
                    _pmax(c, lx + 1, ly - 1, "main", 3)
                    _pmax(c, lx, ly, "main", 2)
        if i >= 3:
            for x, y, ph in puddles:
                life = (kk + ph) % 8
                if life < 5:
                    g = Field(W, H, ss=1)
                    rad = 1.5 + life * 1.3
                    g.add("main", np.where(_ring(g, x, y, rad, 0.5, 0.45), 3 - life // 2, EMPTY))
                    g.canvas(c)
        if k in (5, 6):
            core = np.zeros((H, W), bool)
            for (x0, y0), (x1, y1) in zip(bolt, bolt[1:]):
                for x, y in _bres(x0, y0, x1, y1):
                    if 0 <= x < W and 0 <= y < H:
                        core[y, x] = True
            if k == 5:
                halo = FX._dilate(core, diag=True)
                FX._paint(c, halo & ~core, "main", 4)
                FX._paint(c, core, "core", 5)
                g = Field(W, H)
                e = g.dist(36, gy - 1, 2.6)
                g.add("core", _q(1 - e / 6, [(0, 4), (0.45, 5)]))
                g.add("main", _q(1 - np.abs(e - 10) / 0.8, [(0, 3)]))
                g.canvas(c)
            else:
                core &= (np.arange(H)[:, None] // 4) % 3 != 0
                FX._paint(c, core, "main", 3)
        out.append(c)
    return out


def bloom_circle() -> list[Canvas]:
    """Grovekeeper: a ring of green light draws itself on the tile, flowers pop up round it one after another,
    and a sapling shoots up in the middle, unfurling a leaf pair at a time into a crown, while petals drift off.
    The game swaps the companion in on the last frame."""
    W, H, cx, gy = 48, 48, 23.5, 36.0
    r = tex.rng(3101)
    flowers = sorted([(j * 36 + r.uniform(-8, 8), 2 + (j * 3) % 4 * 0.7) for j in range(10)],
                     key=lambda fl: math.sin(math.radians(fl[0])))
    petals = [(r.uniform(0, 360), r.uniform(0.6, 1.0), r.uniform(0, 2.5)) for _ in range(12)]
    stem = [0, 0, 0, 3, 7, 11, 14, 17, 18, 18, 18, 18]
    out = []
    for i in range(12):
        c = _canvas(W, H)
        f = Field(W, H, ss=1)
        th = _iso_ang(f, cx, gy)
        if i <= 9:
            span = 360 if i >= 1 else 180
            sh = [4, 5, 4, 4, 4, 3, 3, 3, 2, 2][i]
            f.add("main", np.where(_ring(f, cx, gy, 15) & (((th + 90) % 360) < span), sh, EMPTY))
            if 1 <= i <= 6:
                f.add("main", np.where(_ring(f, cx, gy, 11) & ((th // 15) % 2 == 0), 2, EMPTY))
        f.canvas(c)
        o = _canvas(W, H)
        for a, t0 in flowers:
            age = i - t0
            if age < 0:
                continue
            x, y = _iso(cx, gy, 15, a)
            x, y = int(round(x)), int(round(y))
            o.px(x, y, "leaf", 3)
            if age >= 1:
                o.px(x, y - 1, "leaf", 4)
                o.px(x + 1, y, "leaf", 2)
            if 1 <= age < 2:
                o.px(x, y - 2, "petal", 3)
            elif age >= 2:
                for dx, dy, sh in ((0, -4, 4), (-1, -3, 4), (1, -3, 3), (0, -2, 2), (-1, -4, 3), (1, -2, 2)):
                    o.px(x + dx, y + dy, "petal", sh)
                o.px(x, y - 3, "accent", 5)
        hs = stem[i]
        if hs:
            for y in range(int(gy) - hs, int(gy) + 1):
                o.px(cx - 0.5 + (1 if y < gy - 11 else 0), y, "wood", 3)
                o.px(cx + 0.5 + (1 if y < gy - 11 else 0), y, "wood", 2)
            for at, size in ((6, 1), (11, 2)):
                if hs >= at + 1:
                    unf = min(size, hs - at)
                    y = int(gy) - at
                    for s in (-1, 1):
                        x0 = cx + (1 if s > 0 else -1) + (1 if at >= 11 else 0)
                        o.ellipse(x0 + s * (1.5 + unf * 0.6), y - 1, 1.2 + unf * 0.6, 1.1, "leaf", 3)
            if hs >= 14:
                g = (hs - 13) / 5
                top = int(gy) - hs
                o.ellipse(cx + 1, top + 1, 3 + 2.5 * g, 2.5 + 1.6 * g, "leaf", 3)
                o.ellipse(cx - 1.5, top + 3, 2 + 1.5 * g, 1.5 + 1 * g, "leaf", 3)
                o.shade_volume("leaf")
        o.outline()
        c.blit(o)
        if i >= 6:
            for a, sp, t0 in petals:
                age = i - 6 - t0
                if 0 <= age < 4:
                    x, y = _iso(cx, gy, 15 * sp, a)
                    _pmax(c, x + age * 1.4, y - 4 - age * 3, "petal", 5 - int(age))
        if i == 2:
            _twinkle(c, cx, gy, "core", 2)
        out.append(c)
    return out


# =============================================================================================================
# Warlock
# =============================================================================================================
def cursed_edge() -> list[Canvas]:
    """Hexblade: the swing leaves a cursed smear: a dark crescent with a hot pink edge bristling with thorns, a
    ghost of the stroke trailing behind it, and curse dripping off it in dark drops after the blade has passed."""
    W, H, cx, cy, R = 52, 52, 22.5, 25.5, 16.5
    keys = [(-84, -66, 2.0, 1), (-80, 18, 5.0, 0), (-68, 98, 8.4, 0), (-32, 128, 7.0, 0), (36, 136, 4.6, 1),
            (92, 141, 2.8, 2)]
    r = tex.rng(3201)
    drips = [(r.uniform(20, 120), r.uniform(0, 1.5), r.uniform(1.6, 2.6)) for _ in range(7)]
    out = []
    for i in range(9):
        f = Field(W, H)
        th = f.ang(cx, cy)
        thorns = 3.0 * np.clip(np.sin(np.radians(th) * 9), 0, 1) ** 6
        if i < len(keys):
            tail, head, wmax, dim = keys[i]
            if i == 3:
                _crescent(f, cx, cy, R, tail - 26, head - 34, wmax * 0.7, 2, slot="core")
            r_, sarr = f.dist(cx, cy), ((th - tail) % 360) / (head - tail)
            wd = wmax * np.clip(sarr, 0, 1) ** 0.8 * np.clip((1 - sarr) * 5, 0, 1)
            d = R - r_
            inside = (sarr >= 0) & (sarr <= 1) & (d >= -0.45 - thorns * (wd > 1.5)) & (d <= wd)
            if dim:
                inside &= ((th + 400) / 9 % 3) < 2
            edge = d < 1.7
            body = np.where(d < wd * 0.5, 2, 1) - (sarr < 0.25)
            f.add("main", np.where(inside & ~edge, body, EMPTY))
            f.add("accent", np.where(inside & edge, 5 - dim - (sarr < 0.3), EMPTY))
        c = f.canvas()
        if i == 0:
            for k in range(4):
                _pmax(c, cx + 2 + k, cy - R - 2 - k % 2, "main", 2)
        if 3 <= i <= 8:
            t = i - 3
            for a, t0, vy in drips:
                age = t - t0
                if age < 0 or age > 4:
                    continue
                x = cx + (R - 1) * math.cos(math.radians(a))
                y = cy + (R - 1) * math.sin(math.radians(a)) + vy * age * (1 + age * 0.4)
                _pmax(c, x, y, "accent", 4 - int(age))
                _pmax(c, x, y - 1, "main", 2)
                if age < 2:
                    _pmax(c, x, y - 2, "main", 1)
        if 4 <= i <= 7:                                               # curse smoke curling up off the stroke
            t = i - 4
            for a in (60, 100, 130):
                x = cx + R * math.cos(math.radians(a)) + math.sin(t + a) * 1.5
                y = cy + R * math.sin(math.radians(a)) - 2 - 2.5 * t
                _pmax(c, x, y, "main", 2)
                _pmax(c, x + 1, y - 1, "main", 1)
        out.append(c)
    return out


def infernal_gate() -> list[Canvas]:
    """Fiendbound: a burning pentagram scores itself into the tile, flames lick up off its rim, then a column of
    fire erupts out of it with a horned shape glaring from the flames (the imp coming through) before it
    collapses into embers. The game swaps the imp in as the column falls."""
    W, H, cx, gy = 48, 64, 23.5, 52.0
    RX = 16.0
    r = tex.rng(3301)
    n = tex.fbm(48, 64, 4, r, octaves=3, aspect=0.5)
    embers = [(r.uniform(-10, 10), r.uniform(0, 1), r.uniform(2.5, 4.5)) for _ in range(14)]
    flame_h = [0, 0, 4, 8, 5, 3, 2, 0, 0, 0, 0, 0]
    col_h = [0, 0, 0, 0, 30, 42, 36, 18, 0, 0, 0, 0]
    out = []
    for i in range(12):
        c = _canvas(W, H)
        ring_sh = [3, 5, 5, 5, 5, 4, 4, 3, 3, 2, 2, 1][i]
        f = Field(W, H, ss=1)
        th = _iso_ang(f, cx, gy)
        span = 360 if i else 200
        f.add("main", np.where(_ring(f, cx, gy, RX) & (((th + 90) % 360) < span), ring_sh, EMPTY))
        f.add("accent", np.where(_ring(f, cx, gy, RX - 3) & ((th // 20) % 2 == 0), max(ring_sh - 2, 1), EMPTY))
        f.canvas(c)
        pts = [_iso(cx, gy, RX - 3, -90 + j * 72) for j in range(5)]
        for j in range(5 if i else 2):
            (x0, y0), (x1, y1) = pts[j], pts[(j + 2) % 5]
            _lmax(c, x0 - 0.5, y0 - 0.5, x1 - 0.5, y1 - 0.5, "main", ring_sh - (i >= 7))
        if flame_h[i]:
            for a in np.arange(0, 360, 1.5):
                x, y = _iso(cx, gy, RX, a)
                front = math.sin(math.radians(a)) > 0
                h = flame_h[i] * (0.5 + 0.5 * abs(math.sin(a * 0.09 + i * 1.3)))
                for dy in range(int(h) + 1):
                    u = dy / max(h, 1)
                    sh = (5 if u < 0.3 and front else 4 if u < 0.6 else 3 if u < 0.85 else 2) - (0 if front else 1)
                    _pmax(c, x + math.sin(dy * 0.8 + a) * 0.6 * u, y - dy, "core" if sh >= 5 else "main", sh)
        if col_h[i]:
            g = Field(W, H, ss=1)
            hc = col_h[i]
            up = np.clip((gy - g.Y) / hc, 0, 1)
            hwid = 8 * (1 - up ** 1.6) + 1
            sway = np.sin(g.Y * 0.28 + i * 1.4) * 1.5 * up
            v = 1 - np.abs(g.X - cx - sway) / hwid + (np.roll(n, 6 * i, 0) - 0.5) * 0.9 - (i >= 7) * 0.45
            v = np.where((g.Y <= gy + 2) & (gy - g.Y < hc), v, -1)
            g.add("main", _q(v, [(0.0, 2), (0.15, 3), (0.32, 4)]))
            g.add("core", _q(v - up * 0.3, [(0.5, 4), (0.66, 5)]))
            g.canvas(c)
        if i == 5:                                                       # the imp's horned head in the fire
            hx, hy = cx, gy - 27
            for x, y in ((hx - 3, hy - 3), (hx - 4, hy - 4), (hx - 4, hy - 5), (hx + 3, hy - 3), (hx + 4, hy - 4),
                         (hx + 4, hy - 5)):
                c.px(x, y, "core", 5)
            for x in (hx - 2, hx + 1):
                c.px(x, hy, "accent", 1)
                c.px(x + 1, hy, "accent", 1)
        if i >= 4:
            for x0, ph, vy in embers:
                age = i - 4 - ph * 3
                if 0 <= age < 5:
                    _mote(c, cx + x0 + math.sin(age + x0) * 1.5, gy - 8 - vy * age * 2,
                          "core" if age < 1.5 else "main", 5 - int(age))
        out.append(c)
    return out


def rift_step() -> list[Canvas]:
    """Void: space splits: a hairline tear opens into a rift as tall as the unit, starless black with a crackling
    violet rim, motes spiral in, the unit is pulled through (gone from frame 4), and the rift snaps shut in a flash.
    Played again at the destination, reversed."""
    W, H, cx, cy = 48, 52, 23.5, 25.0
    hw = [0.6, 2.2, 5.0, 7.0, 7.6, 6.2, 3.5, 0.7, 0, 0]
    r = tex.rng(3401)
    n = tex.fbm(48, 52, 8, r, octaves=1)
    stars = [(r.uniform(-1, 1), r.uniform(-0.8, 0.8), int(r.integers(0, 3))) for _ in range(10)]
    motes = [(r.uniform(0, 360), r.uniform(15, 22), r.uniform(0, 1.5)) for _ in range(14)]
    out = []
    for i in range(10):
        c = _canvas(W, H)
        w = hw[i]
        if w:
            f = Field(W, H, ss=1)
            v = (f.Y - cy) / 17
            prof = w * np.clip(1 - v * v, 0, 1) ** 0.65
            jag = (np.roll(n, 3 * i, 0) - 0.5) * 1.6 * min(1, w / 3)
            d = np.abs(f.X - cx) - prof - jag
            inside = (d < 0) & (np.abs(v) < 1)
            rim = (d >= 0) & (d < 1.1) & (np.abs(v) < 1.05)
            glow = (d >= 1.1) & (d < 2.3) & (np.abs(v) < 1.05) & _checker_s(f)
            f.add("main", np.where(glow, 2, EMPTY))
            f.add("main", np.where(rim, np.where(f.X < cx, 5, 4), EMPTY))
            f.add("core", np.where(inside, np.where(np.abs(f.X - cx) < prof * 0.4, 0, 1), EMPTY))
            f.canvas(c)
            for sx, sy, ph in stars:
                if (i + ph) % 3 == 0 or w < 3:
                    continue
                x = cx + sx * w * 0.6
                y = cy + sy * 15
                c.px(x, y, "accent", 4)
            if 2 <= i <= 6:                                           # crackle off the rim
                for j in range(3):
                    yy = cy + (j - 1) * 9 + (i % 2) * 3
                    s = 1 if (i + j) % 2 else -1
                    x0 = cx + s * (w * 0.9 + 1)
                    _lmax(c, x0, yy, x0 + s * 2, yy - 2, "main", 4)
                    _pmax(c, x0 + s * 3, yy - 1, "main", 3)
        if 1 <= i <= 6:
            for a0, r0, t0 in motes:
                t = (i - 1 - t0) / 5
                if t < 0 or t > 1:
                    continue
                rad = r0 * (1 - t)
                a = math.radians(a0 + 160 * t)
                x, y = cx + rad * math.cos(a), cy + rad * math.sin(a) * 0.9
                _pmax(c, x, y, "main", 4)
                _pmax(c, x + math.cos(a) * 1.4, y + math.sin(a) * 1.2, "main", 2)
        if i == 7:
            _lmax(c, cx - 7, cy, cx + 7, cy, "main", 5)
            _lmax(c, cx, cy - 12, cx, cy + 12, "main", 4)
            _twinkle(c, cx, cy, "accent", 3)
        if i in (8, 9):
            rad = [6, 9][i - 8]
            for a in range(0, 360, 40):
                _pmax(c, cx + rad * math.cos(math.radians(a + 20 * i)), cy + rad * math.sin(math.radians(a)),
                      "main", 4 - (i == 9) * 2)
        out.append(c)
    return out


# =============================================================================================================
# Monk
# =============================================================================================================
def palm_strike() -> list[Canvas]:
    """Way of the Open Hand: ki gathers at the striking hand, an open palm of light lands, and the force goes on
    through: shock arcs roll out to the right, one inside the next, with speed lines, while the palm fades."""
    W, H, px, py = 56, 40, 25.5, 18.5
    fingers = [(-3, 4.2), (-1, 5.6), (1, 5.3), (3, 4.0)]
    out = []

    def palm(f: Field, s: float, shade: int) -> None:
        e = np.hypot((f.X - px) / (3.8 * s), (f.Y - py - 2 * s) / (3.4 * s))
        m = e < 1
        for fx_, ln in fingers:
            d, t = _seg(f, px + fx_ * s, py - 1 * s, px + fx_ * s, py - (1 + ln) * s)
            m |= d < 0.65 * s
        d, t = _seg(f, px - 3.6 * s, py + 2 * s, px - 6.2 * s, py - 0.8 * s)
        m |= d < 0.7 * s
        f.add("main", np.where(m, shade - 1, EMPTY))
        f.add("core", np.where(m & (e < 0.55), shade, EMPTY))

    for i in range(9):
        f = Field(W, H)
        if i == 1:
            palm(f, 0.8, 4)
        if i == 2:
            _starburst(f, px, py, 10, 6, 2.2)
            palm(f, 1.1, 5)
        if i in (3, 4, 5):
            palm(f, 1.0, [5, 4, 3][i - 3])
        for s0 in (2, 3, 4):
            a = i - s0
            if a < 0 or a > 4:
                continue
            rad = 4 + 4.5 * a
            ox = px + 2 + rad * 0.55
            d = _ring_d(f, ox, py, rad * 0.5, rad)
            ok = (np.abs(d) < 0.9 - 0.1 * a) & (f.X > ox)
            if a >= 3:
                ok &= (np.floor(f.Y) % 3) != 0
            f.add("accent" if s0 == 2 else "main", np.where(ok, 5 - a, EMPTY))
        c = f.canvas()
        if i == 0:
            for a in range(0, 360, 60):
                rr = 7
                _pmax(c, px + rr * math.cos(math.radians(a)), py + rr * math.sin(math.radians(a)), "main", 3)
                _pmax(c, px + 4 * math.cos(math.radians(a + 20)), py + 4 * math.sin(math.radians(a + 20)), "main", 4)
        if 3 <= i <= 6:
            t = i - 3
            for y, ln in ((py - 7, 6), (py + 6, 7), (py - 3, 4), (py + 9, 4)):
                x0 = px + 8 + t * 5
                if x0 + ln < W:
                    _lmax(c, x0, y, x0 + ln, y, "main", 3 - (t >= 2))
        if i >= 7:
            for x, y in ((48, 12), (51, 22), (44, 27)):
                _pmax(c, x + (i - 7) * 2, y, "main", 2)
        out.append(c)
    return out


def gale_leap() -> list[Canvas]:
    """Way of the Windstep: wind rushes in to the feet, the leap blows a ring of air out across the tile and curls
    of wind wind up round the monk like a small tornado as they rise, peeling off the top as chevrons, and the gust
    streams away behind in long hooked dash lines."""
    W, H, cx, gy = 56, 60, 27.5, 50.0
    out = []
    for i in range(10):
        f = Field(W, H, ss=1)
        if 1 <= i <= 4:
            rad = [7, 13, 17, 20][i - 1]
            ok = _ring(f, cx, gy, rad, 0.6)
            if i == 4:
                ok &= (_iso_ang(f, cx, gy) // 15) % 2 == 0
            f.add("main", np.where(ok, np.where(f.Y > gy, [5, 4, 3, 2][i - 1], [4, 3, 2, 1][i - 1]), EMPTY))
        c = f.canvas()
        if i == 0:
            for s in (-1, 1):
                for y, ln in ((gy - 1, 6), (gy - 4, 4), (gy + 2, 5)):
                    x0 = cx + s * 9
                    _lmax(c, x0, y, x0 + s * ln, y, "main", 3)
            _mote(c, cx - 0.5, gy - 1, "core", 4, big=True)
        if 1 <= i <= 6:
            t = i - 1
            for j in range(4):
                y = gy - 3 - 9 * j - 3 * t
                if y < 6 or j > t + 1:
                    continue
                rad = 7 + 1.8 * j + 0.6 * t
                dim = (1 if i >= 5 else 0) + (1 if i >= 6 else 0)
                _swoosh(c, cx, y, rad, 100 + 115 * t + 80 * j, 230, "core" if j % 2 == 0 else "main", 5 - dim)
        if 4 <= i <= 7:
            t = i - 4
            for k in range(2):
                y = gy - 44 - 4 * t - 4 * k
                for s in (-1, 1):
                    _lmax(c, cx, y, cx + s * 4, y + 3, "main", 4 - t - k)
        if 3 <= i <= 8:
            t = i - 3
            for y, ln, off in ((gy - 30, 14, 0), (gy - 21, 18, 3), (gy - 12, 11, 6), (gy - 37, 9, 2)):
                x1 = cx - 8 - off - 4 * t
                x0 = x1 - ln
                for x in range(int(x0), int(x1)):
                    if (x + 2 * t) % 7 == 0 or x < 0:
                        continue
                    sh = (4 if x > x1 - 5 else 3) - (t >= 3) - (t >= 5)
                    _pmax(c, x, y, "main", sh)
                if x0 > 2:                                               # the gust's tail hooks up
                    _pmax(c, x0 - 1, y - 1, "main", 3 - (t >= 3))
                    _pmax(c, x0 - 1, y - 2, "main", 2)
                    _pmax(c, x0, y - 3, "main", 2)
        out.append(c)
    return out


def iron_skin() -> list[Canvas]:
    """Way of Iron Body: plates of light fly in and lock into a faceted shell round the monk with a clang, a glint
    slides down it, and when the blow comes the counter flashes off the front in a hard star with anvil sparks
    spraying out and falling, as the shell breaks away."""
    W, H, cx, cy = 40, 48, 19.5, 24.0
    verts = [(-6, -17), (6, -17), (10, -11), (10, 12), (6, 17), (-6, 17), (-10, 12), (-10, -11)]
    hx, hy = cx + 11, cy - 5
    r = tex.rng(3601)
    sparks = [(a, r.uniform(0.7, 1.3), r.uniform(-1.5, 0.0)) for a in r.uniform(-80, 60, 10)]
    offs = [6, 2, 0, 0, 0, 0, 0, 0, 0, 0]
    shades = [2, 4, 5, 4, 5, 4, 3, 2, 0, 0]
    out = []
    for i in range(10):
        f = Field(W, H)
        sh = shades[i]
        if sh:
            for k, ((x0, y0), (x1, y1)) in enumerate(zip(verts, verts[1:] + verts[:1])):
                mx, my = (x0 + x1) / 2, (y0 + y1) / 2
                ln = math.hypot(mx, my)
                o = offs[i]
                ox, oy = mx / ln * o, my / ln * o
                ax, ay = x0 + (x1 - x0) * 0.12, y0 + (y1 - y0) * 0.12
                bx, by = x0 + (x1 - x0) * 0.88, y0 + (y1 - y0) * 0.88
                d, t = _seg(f, cx + ax + ox, cy + ay + oy, cx + bx + ox, cy + by + oy)
                ok = d < 0.75
                if i >= 6:
                    ok &= (t * 10 + k) % 3 < 2
                lit = (mx + my) < 0
                f.add("main", np.where(ok, sh if lit else sh - 1, EMPTY))
            if 2 <= i <= 5:
                inner = np.hypot((f.X - cx) / 9, (f.Y - cy) / 16) < 1
                plates = inner & (np.floor(f.Y) % 4 == 0) & _checker_s(f)
                f.add("main", np.where(plates, 1, EMPTY))
            if i in (3, 4, 5):
                g = (f.Y - cy) - ((i - 3) * 14 - 16) + (f.X - cx) * 0.8
                inner = np.hypot((f.X - cx) / 10, (f.Y - cy) / 17) < 1
                f.add("core", np.where(inner & (np.abs(g) < 1.0), 5, EMPTY))
        if i == 4:
            _starburst(f, hx, hy, 12, 7, 2.6)
            f.add("core", _q(1 - f.dist(hx, hy) / 2.6, [(0, 5)]))
        if i == 5:
            _starburst(f, hx, hy, 10, 6, 1.6, r0=4)
            f.add("accent", _q(1 - np.abs(f.dist(hx, hy) - 5) / 0.6, [(0, 4)]))
        c = f.canvas()
        if i == 2:
            _twinkle(c, cx - 6, cy - 17, "core", 2)
        if i >= 5:
            t = i - 4
            for a, sp, vy in sparks:
                rad = (4 + 3.5 * t) * sp
                x = hx + rad * math.cos(math.radians(a))
                y = hy + rad * math.sin(math.radians(a)) + (vy + 0.6 * t) * t
                if i >= 8 and sp < 1.0:
                    continue
                _lmax(c, x, y, x - math.cos(math.radians(a)) * 1.5, y - math.sin(math.radians(a)) * 1.5, "accent",
                      5 - t + 1)
        out.append(c)
    return out


# ------------------------------------------------------------------------------------------------------ catalogue
@dataclass
class Stage:
    """How the preview GIF plays an effect: where the hero and any second unit stand, the hero's pose, and when
    the target flinches. Preview only; nothing here ships."""
    pose: str = "cast"                  # rig animation the hero plays as the effect starts
    lead: int = 0                       # ms from the pose starting to effect frame 0
    hero: str = "C"                     # tile: C centre, L / R the side corners, FR front-right
    other: str | None = None            # "foe" | "ally": a second unit
    other_at: str = "R"
    at: str = "hero"                    # hero | other | a tile key: where the effect's anchor goes
    hit: int | None = None              # effect frame at which the other unit flinches
    morph: tuple[int, str] | None = None  # (frame, beast id): the hero becomes this beast from that frame
    downed: int | None = None           # the hero lies downed until this frame, then gets up


@dataclass
class Sig:
    id: str
    subclass: str
    name: str
    make: Callable[[], list[Canvas]]
    variants: dict[str, dict[str, str]]
    fps: int
    anchor: tuple[int, int]
    attach: str                         # feet | body | overhead | link
    blend: str = "add"
    loop: bool = False
    loop_start: int = 0
    layer: str = "over"
    hides: int | None = None            # the unit is gone from this frame (vanish, teleport)
    lift: list[int] | None = None       # px the unit is raised per frame (tile raise / lower)
    span: int | None = None             # link effects: px from the anchor to the far unit, along +x
    note: str = ""
    stage: Stage = field(default_factory=Stage)


_SHIELD = {"gold": _v(main="gold", core="magic/holy"), "azure": _v(main="magic/arcane", core="magic/frost")}
_TURF = {"grass": _v(stone="terrain/stone", turf="terrain/grass", main="magic/earth", smoke="dust"),
         "sand": _v(stone="terrain/stone_warm", turf="terrain/sand", main="magic/earth", smoke="terrain/sand"),
         "snow": _v(stone="terrain/stone_dark", turf="terrain/snow", main="magic/frost", smoke="terrain/snow")}

CATALOGUE: list[Sig] = [
    # Fighter
    Sig("vanguard_shield_wall", "vanguard", "Shield Wall", shield_wall, _SHIELD, 10, (24, 42), "feet", loop=True,
        loop_start=4, note="raised in front of the unit; mirror for the other facings",
        stage=Stage(pose="block")),
    Sig("weaponmaster_blade_dance", "weaponmaster", "Blade Dance", blade_dance, {
        "steel": _v(main="steel", core="magic/holy", accent="magic/blood")}, 16, (24, 24), "body",
        stage=Stage(pose="spin")),
    Sig("warlord_rally", "warlord", "Rally Banner", rally_banner, {
        "royal": _v(cloth="cloth/navy", accent="metal/gold", wood="wood/oak", main="gold", core="magic/holy",
                    smoke="dust")}, 12, (32, 58), "feet", blend="normal",
        note="the banner plants behind-left of the unit; mirror for the other facings",
        stage=Stage(pose="victory")),
    # Rogue
    Sig("shadow_shadowstep", "shadow", "Shadowstep", shadowstep, {
        "night": _v(smoke="soot", accent="spark", core="steel", main="eldritch")}, 12, (24, 43), "feet",
        blend="normal", hides=2, note="play at the start tile (unit hidden from frame 2), then reversed where it "
        "reappears behind the target", stage=Stage(pose="dodge")),
    Sig("trickster_snap_trap", "trickster", "Snap Trap", snap_trap, {
        "steel": _v(metal="metal/steel", core="spark", smoke="dust")}, 14, (22, 31), "feet", blend="normal",
        note="frames 0-1 can hold on a hidden-but-armed trap until something steps on it",
        stage=Stage(pose="item", hero="L", other="foe", at="other", hit=3)),
    Sig("duelist_riposte", "duelist", "Riposte", riposte, {
        "silver": _v(main="steel", core="magic/holy", accent="magic/blood")}, 18, (14, 16), "body",
        note="parry in front of the unit, counter-thrust to the right; mirror for a left-facing duelist",
        stage=Stage(pose="thrust", lead=40, hero="C", other="foe", other_at="FR", hit=4)),
    # Wizard
    Sig("evoker_sculpted_nova", "evoker", "Sculpted Nova", sculpted_nova, {
        "fire": _v(main="magic/fire", core="magic/holy", accent="magic/arcane")}, 14, (32, 33), "feet",
        note="the cold wedges face front-right and back-left; the game rotates the gaps to where allies stand",
        stage=Stage(other="ally", other_at="FR")),
    Sig("chronomancer_time_dial", "chronomancer", "Time Dial", time_dial, {
        "haste": _v(main="magic/frost", core="magic/holy", accent="gold"),
        "slow": _v(main="magic/void", core="magic/arcane", accent="eldritch")}, 12, (28, 16), "feet", loop=True,
        loop_start=2, layer="ground", note="haste plays forward; slow plays the loop frames in reverse"),
    Sig("geomancer_raise", "geomancer", "Raise Earth", stone_rise, _TURF, 12, (24, 54), "feet", blend="normal",
        layer="ground", lift=[0, 6, 14, 19, 17, 16, 16, 16, 16, 16],
        note="pick the variant of the tile's top; the unit rides up by unitLift and the tile ends two steps higher"),
    Sig("geomancer_lower", "geomancer", "Sink Earth", stone_sink, _TURF, 12, (24, 30), "feet", blend="normal",
        layer="ground", lift=[0, 0, -3, -7, -10, -9, -8, -8, -8, -8],
        note="pick the variant of the tile's top; the unit sinks by unitLift and the tile ends one step lower"),
    # Cleric
    Sig("life_revive", "life", "Revivify", revive, {
        "holy": _v(main="gold", core="magic/holy", feather="feather")}, 12, (16, 66), "feet",
        note="on a downed ally: they get up as the feather touches (frame 6)", stage=Stage(downed=6)),
    Sig("war_divine_strike", "war", "Divine Strike", divine_strike, {
        "gold": _v(main="gold", core="magic/holy", accent="spark")}, 14, (20, 24), "body",
        stage=Stage(pose="attack", other="foe", other_at="FR", hit=4)),
    Sig("light_radiant_burst", "light", "Radiant Burst", radiant_burst, {
        "dawn": _v(main="magic/holy", core="magic/holy", accent="gold")}, 12, (32, 44), "feet",
        note="the ring of light reaches about two tiles; hidden units in it are revealed"),
    # Ranger
    Sig("sharpshooter_deadeye", "sharpshooter", "Deadeye", deadeye, {
        "scope": _v(main="magic/frost", core="magic/holy", accent="reticle")}, 16, (64, 20), "body",
        note="on the target; the streak arrives from 64 px to the left (two tiles); mirror or rotate to the shooter",
        stage=Stage(pose="shoot", hero="L", other="foe", at="other", hit=5)),
    Sig("beastmaster_hunt_mark", "beastmaster", "Hunt Mark", hunt_mark, {
        "amber": _v(main="magic/earth", core="magic/holy", accent="magic/blood")}, 10, (16, 30), "overhead",
        loop=True, loop_start=4, note="marks the companion's target until it strikes",
        stage=Stage(pose="item", hero="L", other="foe", at="other")),
    Sig("warden_vine_snare", "warden", "Vine Snare", vine_snare, {
        "root": _v(wood="foliage/bark", leaf="foliage/leaf_light", accent="foliage/bark_pale", smoke="dust")}, 12,
        (24, 34), "feet", blend="normal", loop=True, loop_start=6,
        stage=Stage(pose="shoot", hero="L", other="foe", at="other", hit=2)),
    # Paladin
    Sig("guardian_bulwark", "guardian", "Bulwark", bulwark_link, {
        "azure": _v(main="magic/arcane", core="magic/frost"), "holy": _v(main="gold", core="magic/holy")}, 12,
        (8, 30), "link", loop=True, loop_start=4, span=64,
        note="from the guardian's chest to the covered ally's; rotate to the ally and stretch x to the distance",
        stage=Stage(pose="block", hero="L", other="ally")),
    Sig("crusader_smite", "crusader", "Smite", smite, {
        "radiant": _v(main="gold", core="magic/holy", accent="spark")}, 14, (24, 82), "feet",
        stage=Stage(pose="slam", hero="L", other="foe", at="other", hit=3)),
    Sig("avenger_vengeance_brand", "avenger", "Vengeance Brand", vengeance_brand, {
        "ember": _v(main="brand", core="magic/fire", accent="spark")}, 10, (16, 32), "overhead", loop=True,
        loop_start=5, note="hangs over whoever hit an ally until the avenger collects",
        stage=Stage(pose="cast", hero="L", other="foe", at="other", hit=1)),
    # Bard
    Sig("valor_war_horn", "valor", "War Horn", war_horn, {
        "brass": _v(main="gold", core="magic/holy", accent="magic/fire")}, 14, (32, 35), "body",
        stage=Stage(pose="item")),
    Sig("lore_glyph_echo", "lore", "Cutting Echo", glyph_echo, {
        "arcane": _v(main="magic/arcane", core="magic/frost", accent="magic/holy")}, 12, (24, 26), "body"),
    Sig("siren_charm_song", "siren", "Siren Song", charm_song, {
        "charm": _v(main="heart", core="magic/holy", accent="magic/frost")}, 12, (16, 26), "body",
        note="sings to the right; mirror for a left-facing bard",
        stage=Stage(hero="L", other="foe", other_at="C")),
    # Druid
    Sig("shapeshifter_wild_shape", "shapeshifter", "Wild Shape", wild_shape, {
        "spring": _v(leaf="foliage/leaf", accent="foliage/leaf_autumn", core="verdant")}, 12, (24, 44), "feet",
        blend="normal", note="swap the unit for its beast form at frame 5, inside the puff",
        stage=Stage(morph=(5, "wolf"))),
    Sig("stormcaller_storm", "stormcaller", "Call Storm", storm_zone, {
        "storm": _v(smoke="thunderhead", main="storm", core="magic/frost")}, 10, (32, 72), "feet", blend="normal",
        loop=True, loop_start=3, note="a zone over about one tile around its centre; lightning strikes once a loop",
        stage=Stage(hero="L", other="foe", other_at="C", at="other", hit=8)),
    Sig("grovekeeper_bloom", "grovekeeper", "Grove Bloom", bloom_circle, {
        "blossom": _v(main="magic/nature", core="magic/holy", wood="foliage/bark", leaf="foliage/leaf_light",
                      petal="foliage/blossom", accent="terrain/flower_yellow")}, 12, (24, 36), "feet",
        blend="normal", note="on the summon's tile; swap the sapling companion in on the last frame",
        stage=Stage(hero="L", at="C")),
    # Warlock
    Sig("hexblade_cursed_edge", "hexblade", "Cursed Edge", cursed_edge, {
        "hex": _v(main="eldritch", accent="hex", core="magic/shadow"),
        "fel": _v(main="fel_dark", accent="fel", core="fel_dark")}, 16, (20, 26), "body", blend="normal",
        note="mirror for a left-facing attacker", stage=Stage(pose="attack", lead=80, other="foe", other_at="FR",
                                                              hit=2)),
    Sig("fiendbound_infernal_gate", "fiendbound", "Infernal Gate", infernal_gate, {
        "hellfire": _v(main="magic/fire", core="magic/holy", accent="magic/blood")}, 12, (24, 52), "feet",
        note="on the summon's tile; swap the imp in at frame 7 as the column falls", stage=Stage(hero="L", at="C")),
    Sig("void_rift_step", "void", "Rift Step", rift_step, {
        "void": _v(main="magic/void", core="abyss", accent="magic/frost")}, 12, (24, 44), "feet", blend="normal",
        hides=4, note="play at the start tile (unit gone from frame 4), then reversed at the destination; "
        "on an enemy it is the pull", stage=Stage(pose="cast")),
    # Monk
    Sig("open_hand_palm_strike", "open_hand", "Quivering Palm", palm_strike, {
        "ki": _v(main="ki", core="magic/holy", accent="magic/blood")}, 18, (14, 20), "body",
        note="strikes to the right; mirror for a left-facing monk",
        stage=Stage(pose="bash", lead=60, other="foe", other_at="FR", hit=2)),
    Sig("windstep_gale_leap", "windstep", "Gale Leap", gale_leap, {
        "gale": _v(main="wind", core="magic/frost")}, 12, (28, 50), "feet", stage=Stage(pose="jump")),
    Sig("iron_body_iron_skin", "iron_body", "Iron Skin", iron_skin, {
        "steel": _v(main="steel", core="magic/holy", accent="magic/fire")}, 12, (20, 24), "body",
        note="the counter flash is on the front-right; mirror for the other facings",
        stage=Stage(pose="block", other="foe", other_at="FR", hit=4)),
]

ATTACH = {
    **FX.ATTACH,
    "link": "anchor goes on the source unit's chest (15 px above its ground point); the art runs `span` px along +x "
            "to the other unit's chest: rotate it to face that unit and stretch x to the real distance",
}


# ------------------------------------------------------------------------------------------------------------ build
def build() -> None:
    """content/tactics/base/signature: one horizontal strip per effect and variant, plus a manifest that also maps
    every subclass to its effects; and the previews."""
    from parts.creator import SUBCLASSES
    out = PACK / "signature"
    shutil.rmtree(out, ignore_errors=True)
    effects, rendered = {}, {}
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
        sub = SUBCLASSES[e.subclass]
        entry = {"name": e.name, "subclass": e.subclass, "class": sub.cls, "frame": [w, h], "frames": len(frames),
                 "fps": e.fps, "loop": e.loop, "anchor": list(e.anchor), "attach": e.attach, "blend": e.blend,
                 "layer": e.layer, "variants": files, "default": next(iter(files))}
        if e.loop and e.loop_start:
            entry["loopStart"] = e.loop_start
        if e.span:
            entry["span"] = e.span
        if e.hides is not None:
            entry["hideUnitFrom"] = e.hides
        if e.lift:
            entry["unitLift"] = e.lift
        if e.note:
            entry["note"] = e.note
        effects[e.id] = entry
    by_sub: dict[str, list[str]] = {s: [] for s in SUBCLASSES}
    for e in CATALOGUE:
        by_sub[e.subclass].append(e.id)
    write_json({
        "layout": "each file is one horizontal strip of frames, left to right, all the same size",
        "blend": {"add": "additive (glows, light): src.rgb * src.a added to the scene",
                  "normal": "ordinary alpha blending (smoke, solid objects, dark magic)"},
        "layer": {"over": "drawn above units (sort with the target, just in front of it)",
                  "ground": "drawn on the floor under units, like a decal"},
        "loop": "loop: true repeats frames loopStart..end (loopStart defaults to 0); false plays once and is removed",
        "attach": ATTACH,
        "hideUnitFrom": "the unit is not drawn from this frame until it reappears (vanish, teleport)",
        "unitLift": "per frame, px to raise the unit's sprite (negative lowers it); the last value holds",
        "scale": "drawn at the characters' pixel scale: show at the same multiple as the sprites",
        "subclasses": by_sub,
        "effects": effects,
    }, out / "manifest.json")
    _preview_sheet(rendered)
    _preview_gif(rendered)


CLASS_ORDER = ["fighter", "rogue", "wizard", "cleric", "ranger", "paladin", "bard", "druid", "warlock", "monk"]


def _preview_sheet(rendered: dict[str, dict[str, list[Image.Image]]]) -> None:
    from parts.creator import SUBCLASSES, CLASSES
    sections = []
    for cls in CLASS_ORDER:
        tiles = []
        for e in [x for x in CATALOGUE if SUBCLASSES[x.subclass].cls == cls]:
            imgs = rendered[e.id][next(iter(e.variants))]
            loop = (f"loop from {e.loop_start}" if e.loop_start else "loop") if e.loop else "once"
            sub = f"{len(imgs)} f · {e.fps} fps · {loop} · {e.blend} · {e.attach} · {e.layer}"
            tiles.append(Tile(strip(imgs, 1, gap=2), f"{SUBCLASSES[e.subclass].name}: {e.name}  ({e.id})", sub))
        widest = max(t.image.width for t in tiles)
        cols = max(1, min(len(tiles), 1900 // (widest * 3 + 40)))
        sections.append(contact_sheet(CLASSES[cls].name, tiles, columns=cols, zoom=3, label_h=40))
    tiles = []
    for e in CATALOGUE:
        if len(e.variants) < 2:
            continue
        frames = rendered[e.id]
        peak = _peak(list(frames.values())[0])
        tiles.append(Tile(strip([fr[peak] for fr in frames.values()], 1, gap=3), e.name, " · ".join(frames)))
    sections.append(contact_sheet("Colour variants", tiles, columns=3, zoom=3, label_h=40))
    save(stack("Signature abilities · one per subclass", sections,
               "Frames left to right as shipped (content/tactics/base/signature). Light from the top-left, "
               "hue-shifted ramps, no anti-aliasing. 3x zoom."), PREVIEWS / "28-signature.png")


# ------------------------------------------------------------------------------------------------------ animated
GIF_TICK = 50
GIF_TICKS = 96
CELL_W, CELL_H, LABEL_H = 204, 232, 30
SPRITE = 2
TILES = {"C": (0, 0), "L": (-1, 1), "R": (1, -1), "FR": (1, 0), "FL": (0, 1), "BR": (0, -1), "BL": (-1, 0)}
LIFT = {"feet": 0, "body": 15, "overhead": 36, "link": 15}
SUBCLASS_RACE = {"fighter": "human", "rogue": "halfling", "wizard": "elf", "cleric": "dwarf", "ranger": "half_elf",
                 "paladin": "dragonborn", "bard": "tiefling", "druid": "gnome", "warlock": "human",
                 "monk": "half_orc"}            # as in build.py's subclass sheet, so the heroes match 06b


def _short(name: str) -> str:
    """Subclass names short enough for a GIF cell label."""
    for pre in ("Way of the ", "Way of ", "College of the ", "College of "):
        name = name.removeprefix(pre)
    return name.removesuffix(" Domain")


def _tile_px(key: str) -> tuple[int, int]:
    """A tile's ground point relative to the centre tile, in character pixels (a tile is 32 x 16 of them)."""
    i, j = TILES[key]
    return (i - j) * 16, (i + j) * 8


def _floor() -> tuple[Image.Image, tuple[int, int]]:
    """A 3x3 patch of grass blocks (fx's preview tile), so effects that reach the neighbouring tiles land on
    ground. Returns it and the centre tile top's centre."""
    tile, (tx, ty) = FX._tile()
    W, H = tile.width + 128, tile.height + 64
    img = Image.new("RGBA", (W, H))
    ox, oy = W // 2, 32 + ty
    for s in range(-2, 3):
        for i in range(-1, 2):
            j = s - i
            if -1 <= j <= 1:
                x, y = ox + (i - j) * 32, oy + (i + j) * 16
                img.alpha_composite(tile, (x - tx, y - ty))
    return img, (ox, oy)


class _Units:
    """Composed 2x frames for the heroes and stand-ins, built on demand and cached."""

    def __init__(self) -> None:
        from parts.characters import compose
        from parts.creator import Look, SUBCLASSES, make_character
        from parts.recipes import ALL
        from parts.rig import ANIMS
        self.compose, self.ANIMS = compose, ANIMS
        self.recipes = {}
        for sub in SUBCLASSES.values():
            fem = [s for s in SUBCLASSES.values() if s.cls == sub.cls].index(sub) == 1
            self.recipes[sub.id] = make_character(sub.name, SUBCLASS_RACE[sub.cls], sub.cls,
                                                  Look(body_type="feminine" if fem else "masculine"), subclass=sub.id)
        self.recipes["foe"] = next(r for r in ALL if r.id == "goblin_cutthroat")
        self.recipes["ally"] = next(r for r in ALL if r.id == "kara")
        self.cache: dict[tuple[str, str], list[Image.Image]] = {}

    def frames(self, who: str, anim: str) -> list[Image.Image]:
        key = (who, anim)
        if key not in self.cache:
            rec = self.recipes[who]
            pal = rec.ramps()
            out = []
            for pose in self.ANIMS[anim][2]:
                im = self.compose(rec, "front", pose).render(pal)
                out.append(im.resize((im.width * SPRITE, im.height * SPRITE), Image.NEAREST))
            self.cache[key] = out
        return self.cache[key]

    def beast(self, bid: str) -> Image.Image:
        key = ("beast", bid)
        if key not in self.cache:
            from parts import beasts as B
            b = next(x for x in B.CATALOGUE if x.id == bid)
            cv = b.draw("front", B.POSES["idle"][0])
            cv.outline()
            im = cv.render(B.ramps(b.variants["wildshape"]))
            self.cache[key] = [im.resize((im.width * SPRITE, im.height * SPRITE), Image.NEAREST)]
        return self.cache[key][0]

    def at(self, who: str, anim: str, ms: float) -> Image.Image:
        fps, loop, poses = self.ANIMS[anim]
        k = int(ms * fps / 1000)
        fr = self.frames(who, anim)
        return fr[k % len(fr)] if loop else fr[min(k, len(fr) - 1)]


def _hero_frame(u: _Units, e: Sig, ms: float, k: int | None, after: bool) -> tuple[Image.Image | None, int, int]:
    """The hero's sprite at `ms` into the cell's cycle: its pose, then idle; downed, morphed or hidden as the
    effect says. Returns (image or None, anchor x, anchor y) in 2x pixels."""
    st = e.stage
    gone = e.hides is not None and ((k is not None and k >= e.hides) or after)
    if gone:
        return None, 0, 0
    if st.morph and ((k is not None and k >= st.morph[0]) or after):
        return u.beast(st.morph[1]), 24 * SPRITE, 44 * SPRITE
    if st.downed is not None:
        if k is None and not after or (k is not None and k < st.downed):
            return u.at(e.subclass, "down", 0), 16 * SPRITE, 37 * SPRITE
        t_up = (k - st.downed) * 1000 / e.fps if k is not None else 10_000
        rise = u.ANIMS["rise"]
        dur = len(rise[2]) * 1000 / rise[0]
        if t_up < dur:
            return u.at(e.subclass, "rise", t_up), 16 * SPRITE, 37 * SPRITE
        return u.at(e.subclass, "idle", t_up - dur), 16 * SPRITE, 37 * SPRITE
    anim = u.ANIMS[st.pose]
    dur = len(anim[2]) * 1000 / anim[0]
    if ms < dur:
        return u.at(e.subclass, st.pose, ms), 16 * SPRITE, 37 * SPRITE
    return u.at(e.subclass, "idle", ms - dur), 16 * SPRITE, 37 * SPRITE


def _preview_gif(rendered: dict[str, dict[str, list[Image.Image]]], only: list[str] | None = None,
                 path=None, cols: int = 6) -> None:
    """Every signature effect playing on (or around) a hero of its subclass, over a 3x3 patch of grass at the
    dioramas' 2x scale, with a stand-in foe or ally where the move needs one. Additive effects blend additively."""
    from parts.creator import SUBCLASSES
    cat = [e for e in CATALOGUE if only is None or e.id in only]
    u = _Units()
    floor, (fx0, fy0) = _floor()
    rows = -(-len(cat) // cols)
    head = 56
    W = cols * (CELL_W + 8) + 8
    H = head + rows * (CELL_H + LABEL_H + 8) + 8
    base = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(base)
    d.text((14, 12), "Signature abilities", font=font(20, True), fill=INK)
    d.text((14, 36), "2x, as in the dioramas · one per subclass, on a hero of that subclass · additive effects "
                     "blended additively", font=font(12), fill=MUTED)
    cells = []
    for n, e in enumerate(cat):
        x = 8 + (n % cols) * (CELL_W + 8)
        y = head + (n // cols) * (CELL_H + LABEL_H + 8)
        d.rounded_rectangle((x, y, x + CELL_W, y + CELL_H), 8, fill=PANEL)
        gx, gy = CELL_W // 2, CELL_H - 62
        base.alpha_composite(floor, (x + gx - fx0, y + gy - fy0))
        imgs = rendered[e.id][next(iter(e.variants))]
        d.text((x + 4, y + CELL_H + 3), f"{_short(SUBCLASSES[e.subclass].name)}: {e.name}", font=font(12, True),
               fill=INK)
        d.text((x + 4, y + CELL_H + 17), f"{len(imgs)}f {e.fps}fps {'loop' if e.loop else 'once'} {e.blend} "
                                         f"{e.attach} {e.layer}", font=font(10), fill=MUTED)
        big = [im.resize((im.width * SPRITE, im.height * SPRITE), Image.NEAREST) for im in imgs]
        cells.append((e, x, y, gx, gy, big))
    base_arr = np.asarray(base).astype(np.float32)
    frames = []
    for t in range(GIF_TICKS):
        ms = t * GIF_TICK
        canvas = base_arr.copy()
        for e, x, y, gx, gy, big in cells:
            st = e.stage
            cell = canvas[y + 4:y + CELL_H - 4, x + 4:x + CELL_W - 4]
            gx, gy = gx - 4, gy - 4
            n = len(big)
            dur = n * 1000 / e.fps
            if e.loop:
                tc = ms
                kk = int((tc - st.lead) * e.fps / 1000) if tc >= st.lead else None
                k = None if kk is None else (kk if kk < n else e.loop_start + (kk - e.loop_start) % (n - e.loop_start))
                after = False
            else:
                period = st.lead + dur + 900
                tc = ms % period
                kk = int((tc - st.lead) * e.fps / 1000) if tc >= st.lead else None
                k = kk if kk is not None and kk < n else None
                after = kk is not None and kk >= n
            hx, hy = _tile_px(st.hero)
            units = []
            img, ax, ay = _hero_frame(u, e, tc, k, after)
            lift = 0
            if e.lift:
                lift = e.lift[k] if k is not None else (e.lift[-1] if after else 0)
            if img is not None:
                units.append((hy, img, gx + hx * SPRITE - ax, gy + hy * SPRITE - ay - lift * SPRITE, lift < 0))
            ox = oy = None
            if st.other:
                ox, oy = _tile_px(st.other_at)
                hurt = st.hit is not None and kk is not None and 0 <= (tc - st.lead) - st.hit * 1000 / e.fps < 280
                oim = u.at(st.other, "hurt" if hurt else "idle", tc).transpose(Image.FLIP_LEFT_RIGHT)
                units.append((oy, oim, gx + ox * SPRITE - (32 - 16) * SPRITE, gy + oy * SPRITE - 37 * SPRITE, False))
            if st.at == "hero":
                px_, py_ = hx, hy
            elif st.at == "other":
                px_, py_ = ox, oy
            else:
                px_, py_ = _tile_px(st.at)
            fxim = big[k] if k is not None else None
            ex = gx + px_ * SPRITE - e.anchor[0] * SPRITE
            ey = gy + py_ * SPRITE - LIFT[e.attach] * SPRITE - e.anchor[1] * SPRITE
            if fxim is not None and e.layer == "ground":
                _composite(cell, fxim, ex, ey, e.blend)
            for _, im, ux, uy, sink in sorted(units, key=lambda q: q[0]):
                if sink:                                             # a sinking tile hides the unit's lower part
                    cut = gy + py_ * SPRITE + 2 - uy
                    im = im.crop((0, 0, im.width, max(0, min(im.height, cut))))
                    if im.height == 0:
                        continue
                _composite(cell, im, ux, uy, "normal")
            if fxim is not None and e.layer != "ground":
                _composite(cell, fxim, ex, ey, e.blend)
        img = Image.fromarray(canvas.clip(0, 255).astype(np.uint8), "RGBA").convert("RGB")
        frames.append(img.quantize(colors=255, method=Image.Quantize.MEDIANCUT))
    frames[0].save(path or PREVIEWS / "anim-signature.gif", save_all=True, append_images=frames[1:],
                   duration=GIF_TICK, loop=0, optimize=True, disposal=1)
