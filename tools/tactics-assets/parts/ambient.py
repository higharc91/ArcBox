"""Environment animations: the ambient motion that keeps a map alive between turns.

Five kinds of asset, all horizontal frame strips:

  * animated props: drop-in replacements for static props (same frame size and anchor), so the game can swap a tree
    for one that sways. They are made *from* the static drawings in `props.py`, never redrawn: the outline is
    stripped, the pixels are moved, and the outline is put back, so a swaying tree is still exactly that tree;
  * terrain animations: a waterfall face, foam and ripple decals, lava and swamp bubbles;
  * interactive objects: chests, doors, levers, traps, a portal and a rune stone, with named states;
  * particles: small sprites the game spawns and moves (leaves, flakes, birds, fireflies), each with motion hints;
  * weather overlays: tileable screen layers (rain, snow, fog) and god rays.

Motion follows the same rule as the effects: every loop is built from phases that wrap exactly. A wave advances one
full period per loop, a scrolling texture moves by a whole multiple of its size, a cloud of particles is N copies of
one life cycle offset by 1/N. Frame `n` would equal frame 0, so nothing pops at the seam. Movement is in whole
pixels (rows shear by integers, never resample), so the art stays crisp at every frame.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from typing import Callable

import numpy as np
from PIL import Image

from arcpix import tex
from arcpix.canvas import FLAG_OUTLINE, Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, stack, strip
from . import props as PR

SLOTS = SlotTable()
for _s in ("main", "alt", "accent", "core", "foam", "wood", "metal", "stone", "dark", "leaf", "glow", "smoke"):
    SLOTS.id(_s)

AMB_RAMPS: dict[str, list[str]] = {
    "foam": ramp("#d4ecf4", shift=0.04, spread=0.6),
    "spray": ramp("#b9dcec", shift=0.04, spread=0.7),
    "smoke": ramp("#8c8798", shift=0.04, spread=0.9),
    "soot": ramp("#5e5662", shift=0.04, spread=1.0),
    "steam": ramp("#c9ccd8", shift=0.04, spread=0.7),
    "halo_fire": ramp("#6a3a16", shift=0.015, spread=0.8, sat_boost=0.0),    # additive: stays warm when dim
    "halo_arcane": ramp("#2c4e74", shift=0.015, spread=0.8, sat_boost=0.0),
    "halo_fel": ramp("#36601e", shift=0.015, spread=0.8, sat_boost=0.0),
    "firefly": ramp("#e3ef6a", shift=0.06, spread=0.8),
    "wisp": ramp("#8fe6ff", shift=0.05, spread=0.8),
    "mote": ramp("#ffe2a0", shift=0.06, spread=0.75),
    "ember": ramp("#ff8a3d", shift=0.08, spread=0.85),
    "swallow": ramp("#3b3550", shift=0.05, spread=1.2),
    "gull": ramp("#e6e4ec", shift=0.04, spread=0.75),
    "crow": ramp("#2a2632", shift=0.04, spread=1.3),
    "wing_yellow": ramp("#f2c94a", shift=0.06),
    "wing_blue": ramp("#4fa3ec", shift=0.06),
    "wing_white": ramp("#ece8f2", shift=0.05, spread=0.7),
    "wing_monarch": ramp("#e8792f", shift=0.07),
    "fish_silver": ramp("#a3b6c6", shift=0.05, spread=1.2),
    "fish_koi": ramp("#ea7a3a", shift=0.07),
    "fel": ramp("#8fe05a", shift=0.08, spread=0.9),
    "rune_stone": ramp("#7c7f8e", shift=0.04, spread=1.1),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "amb":
        return AMB_RAMPS[name]
    if fam == "prop":
        return PR.PROP_RAMPS[name]
    if fam == "tr":
        from . import terrain as TR
        return TR.TR_RAMPS[name]
    return FAMILIES[fam][name]


def palette(slots: SlotTable, variant: dict[str, str]) -> dict[str, list[str]]:
    """A variant's ramps, with a neutral grey for any slot the drawing never uses (render needs every name)."""
    out = {s: _ref(r) for s, r in variant.items()}
    for name in slots.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


def _v(**slots: str) -> dict[str, str]:
    return {k: (v if "/" in v else f"amb/{v}") for k, v in slots.items()}


# --------------------------------------------------------------------------------------------------------- drawing
def _canvas(w: int, h: int) -> Canvas:
    return Canvas(w, h, SLOTS)


def _pmax(c: Canvas, x: float, y: float, slot: str, shade: int) -> None:
    """Sets a pixel unless something brighter is already there (particles and glints never darken each other)."""
    x, y = int(math.floor(x)), int(math.floor(y))
    if 0 <= x < c.w and 0 <= y < c.h and shade >= 0:
        if c.slot[y, x] == 0 or c.shade[y, x] <= shade:
            c.slot[y, x] = c.slots.id(slot)
            c.shade[y, x] = min(5, shade)
            c.flags[y, x] = 0


def _twinkle(c: Canvas, x: float, y: float, slot: str, size: int, shade: int = 5) -> None:
    """A four-point star: size 0 is one pixel, each size adds a pixel to every arm, dimming outwards."""
    _pmax(c, x, y, slot, shade)
    for k in range(1, size + 1):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            _pmax(c, x + dx * k, y + dy * k, slot, shade - k - (1 if size == 1 else 0))


def _paint(c: Canvas, mask: np.ndarray, slot: str, shade) -> None:
    shade = np.broadcast_to(np.asarray(shade), mask.shape)
    c.slot[mask] = c.slots.id(slot)
    c.shade[mask] = np.clip(shade[mask], 0, 5).astype(np.uint8)
    c.flags[mask] = 0


def _grid(w: int, h: int) -> tuple[np.ndarray, np.ndarray]:
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    return ys + 0.5, xs + 0.5


def _checker(h: int, w: int, odd: int = 0) -> np.ndarray:
    ys, xs = np.mgrid[0:h, 0:w]
    return (xs + ys + odd) % 2 == 0


def _ring(xs, ys, cx: float, cy: float, rx: float, ry: float) -> np.ndarray:
    """Signed screen distance in pixels to an ellipse outline (negative inside); thresholding it keeps a squashed
    iso ring one pixel thick all the way round."""
    dx, dy = xs - cx, ys - cy
    e = np.hypot(dx / rx, dy / ry)
    g = np.hypot(dx / rx ** 2, dy / ry ** 2) / np.maximum(e, 1e-6)
    return (e - 1) / np.maximum(g, 1e-6)


def _puffs(c: Canvas, puffs: list[tuple[float, float, float, float]], slot: str, noise: np.ndarray, *,
           sy: float = 1.0, rough: float = 0.5) -> None:
    """Smoke: round puffs (x, y, radius, fade), each lit from the top-left like a ball. `fade` 0..1 thins a puff:
    first its rim drops to a checkerboard (pixel-art half transparency), then the checker eats inward, so old smoke
    frays and opens up instead of shrinking. Later puffs paint over earlier ones."""
    ys, xs = _grid(c.w, c.h)
    ck = _checker(c.h, c.w)
    for bx, by, br, fade in puffs:
        q = 1 - np.hypot(xs - bx, (ys - by) * sy) / max(br, 0.5)
        nz = np.roll(noise, (int(round(by)), int(round(bx))), (0, 1))  # the texture travels with its puff
        v = q + (nz - 0.5) * rough * min(1.0, br / 3)
        inside = v > 0
        if not inside.any():
            continue
        nx, ny = (xs - bx) / br, (ys - by) * sy / br
        lit = -(nx + ny) * 0.7071
        shade = 3 + (lit > 0.3).astype(int) + ((lit > 0.65) & (q < 0.45)).astype(int) - (lit < -0.25).astype(int)
        shade = shade - (fade > 0.55)
        sparse = inside & (v < 0.12 + fade * 0.9)                     # the thin rim, widening as it fades
        keep = inside & (~sparse | ck)
        if fade > 0.8:
            keep &= _checker(c.h, c.w, 1) | (v > 0.6)
        _paint(c, keep, slot, np.clip(shade, 1, 5))


def _bare(c: Canvas) -> Canvas:
    """The drawing without its selective outline, so its pixels can move and the outline be redrawn around them."""
    b = c.copy()
    m = (b.flags & FLAG_OUTLINE) > 0
    b.slot[m] = 0
    b.shade[m] = 0
    b.flags[m] = 0
    return b


def _shift(c: Canvas, dx: np.ndarray, dy: np.ndarray | None = None, *, outline: bool = True) -> Canvas:
    """Moves each pixel of a bare drawing by (dx, dy)[y, x] whole pixels, back to front by row, and re-outlines."""
    out = Canvas(c.w, c.h, c.slots)
    ys, xs = np.nonzero(c.slot)
    nx = xs + dx[ys, xs]
    ny = ys + (dy[ys, xs] if dy is not None else 0)
    ok = (nx >= 0) & (nx < c.w) & (ny >= 0) & (ny < c.h)
    out.slot[ny[ok], nx[ok]] = c.slot[ys[ok], xs[ok]]
    out.shade[ny[ok], nx[ok]] = c.shade[ys[ok], xs[ok]]
    if outline:
        out.outline()
    return out


def _hash(w: int, h: int, seed: int) -> np.ndarray:
    return tex.rng(seed).random((h, w))


# ------------------------------------------------------------------------------------------------- animated props
def _sway(base: Canvas, n: int, *, pivot: int, top: int, amp: float, lean: float = 0.35, wave: float = 0.6,
          across: float = 0.0, power: float = 1.4, shimmer: str | None = None, seed: int = 0) -> list[Canvas]:
    """Wind through a drawing. Each pixel moves sideways by amp * weight * (sin(phase) + lean), rounded to whole
    pixels: weight is 0 at `pivot` (the row that stays rooted) rising to 1 at `top`, `wave` delays the phase down
    the rows (a gust travels down through a canopy rather than the whole crown rocking as one board) and `across`
    delays it across columns (a breeze crossing a tuft of grass). `lean` biases the motion downwind: trees bend
    with the wind and spring back, they don't swing evenly both ways. With `shimmer`, leaves on the crest of the
    gust flip and catch the light."""
    bare = _bare(base)
    h, w = bare.slot.shape
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    weight = np.clip((pivot - ys) / max(1, pivot - top), 0, 1) ** power
    flip = _hash(w, h, seed + 1)
    out = []
    for f in range(n):
        ph = 2 * math.pi * f / n - wave * math.pi * (ys - top) / max(1, pivot - top) - across * 2 * math.pi * xs / w
        dx = np.floor(amp * weight * (np.sin(ph) + lean) + 0.5).astype(int)
        c = bare.copy()
        if shimmer:
            sid = c.slots.id(shimmer)
            crest = (np.sin(ph) > 0.8) & (weight > 0.15) & (c.slot == sid) & (flip < 0.22)
            c.shade[crest & (c.shade >= 2)] = np.minimum(5, c.shade[crest & (c.shade >= 2)] + 1)
        out.append(_shift(c, dx))
    return out


def _prop(pid: str) -> PR.Prop:
    return next(p for p in PR.CATALOGUE if p.id == pid)


def tree_sway(pid: str = "tree", n: int = 8) -> list[Canvas]:
    """Broadleaf crowns shear row by row as a gust rolls down through them; the trunk below the fork stays put."""
    return _sway(_prop(pid).frames()[0], n, pivot=40, top=3, amp=1.8, wave=0.7, shimmer="leaf", seed=1)


def pine_sway(pid: str = "pine", n: int = 8) -> list[Canvas]:
    """A conifer is stiffer: the tip moves most, the low tiers barely at all."""
    return _sway(_prop(pid).frames()[0], n, pivot=54, top=1, amp=1.7, wave=0.45, power=1.8, lean=0.3)


def dead_tree_sway(n: int = 8) -> list[Canvas]:
    """Bare branches creak: one pixel at the tips, the trunk still."""
    return _sway(_prop("dead_tree").frames()[0], n, pivot=38, top=4, amp=1.2, wave=0.5, power=1.6)


def swamp_tree_sway(n: int = 8) -> list[Canvas]:
    """A cypress barely moves its crown, but the hanging moss swings well behind it, as secondary motion should."""
    base = _prop("swamp_tree").frames()[0]
    out = _sway(base, n, pivot=34, top=2, amp=1.2, wave=0.5, power=1.6, shimmer="leaf", seed=7)
    return out


def fern_sway(n: int = 8) -> list[Canvas]:
    """Fronds bob from the crown: tips travel most, the wave crossing the clump."""
    return _sway(_prop("fern").frames()[0], n, pivot=23, top=4, amp=1.4, wave=0.0, across=0.6, lean=0.1,
                 power=1.4)


def mushroom_glow(n: int = 8) -> list[Canvas]:
    """Glowcaps breathe: the cap shades lift and fall a step over the loop."""
    base = _bare(_prop("mushroom_cluster").frames()[0])
    sid = base.slots.id("fruit")
    out = []
    for f in range(n):
        c = base.copy()
        up = math.sin(2 * math.pi * f / n) > 0.3
        dn = math.sin(2 * math.pi * f / n) < -0.5
        m = c.slot == sid
        c.shade[m] = np.clip(c.shade[m].astype(int) + (1 if up else -1 if dn else 0), 1, 5)
        c.outline()
        out.append(c)
    return out


def bush_sway(pid: str = "bush", n: int = 8) -> list[Canvas]:
    return _sway(_prop(pid).frames()[0], n, pivot=20, top=3, amp=1.2, wave=0.5, lean=0.25, shimmer="leaf", seed=4)


def grass_wave(n: int = 8) -> list[Canvas]:
    """Blades bend from the root: tips travel furthest, and the wave crosses the tuft from left to right."""
    return _sway(_prop("tall_grass").frames()[0], n, pivot=17, top=3, amp=2.0, wave=0.0, across=0.8, lean=0.15,
                 power=1.5)


def reeds_wave(n: int = 8) -> list[Canvas]:
    """Tall reeds nod their cattail heads; each stem a little behind its left-hand neighbour."""
    return _sway(_prop("reeds").frames()[0], n, pivot=25, top=4, amp=2.0, wave=0.0, across=0.7, lean=0.2,
                 power=1.6)


def crystals_shimmer(n: int = 12) -> list[Canvas]:
    """A glint climbs the crystal faces on a slant (frames 0-7), flares into a star on the tallest tip, then the
    cluster rests so the shimmer reads as an occasional sparkle rather than a strobe."""
    base = _bare(_prop("crystals").frames()[0])
    sid = base.slots.id("glow")
    ys, xs = np.nonzero(base.slot == sid)
    top, bottom = ys.min(), ys.max()
    tip_x = int(xs[ys == top].mean())
    yy, xx = np.mgrid[0:base.h, 0:base.w]
    glow = base.slot == sid
    out = []
    travel = 8
    for f in range(n):
        c = base.copy()
        if f < travel:
            yb = bottom + 3 - (bottom - top + 8) * f / (travel - 1)
            d = yy - yb + (xx - base.w / 2) * 0.5
            band = glow & (np.abs(d) < 1.0)
            near = glow & (np.abs(d) < 2.5) & ~band
            c.shade[band] = np.where(c.shade[band] >= 3, 5, 4)
            c.shade[near] = np.minimum(5, c.shade[near] + 1)
        c.outline()
        star = {travel - 1: 1, travel: 2, travel + 1: 1}.get(f)
        if star is not None:
            _twinkle(c, tip_x, top, "glow", star)
        out.append(c)
    return out


def torch_flicker(n: int = 8) -> list[Canvas]:
    """The torch's flame on an 8-frame loop: height and lean from a few sine harmonics (whole periods, so it wraps)
    for a flicker that never repeats inside the loop, a hot core, and one ember rising off the tip."""
    out = []
    for f in range(n):
        c = Canvas(12, 34, PR.SLOTS)
        c.rect(5, 12, 6, 33, "wood", 3)
        c.rect(6, 12, 6, 33, "wood", 2)
        c.rect(4, 10, 7, 13, "metal", 3)
        c.rect(4, 10, 4, 13, "metal", 4)
        t = 2 * math.pi * f / n
        hgt = 8.5 + 1.4 * math.sin(t) + 0.9 * math.sin(3 * t + 1.1)
        sway = 1.1 * math.sin(2 * t + 0.4) + 0.5 * math.sin(t)
        c.polygon([(2.6, 10.5), (8.4, 10.5), (7.2, 7), (5.5 + sway, 10 - hgt), (3.8, 7)], "glow", 3)
        c.polygon([(3.8, 10.5), (7.2, 10.5), (5.5 + sway * 0.6, 10 - hgt * 0.62)], "glow", 4)
        c.polygon([(4.6, 10.5), (6.4, 10.5), (5.5 + sway * 0.3, 10 - hgt * 0.32)], "glow", 5)
        c.outline()
        life = f % 4                                                  # an ember every half loop
        ex = 5.5 + sway + (1 if f < 4 else -1) * life * 0.5
        _pmax(c, ex, 9 - hgt - life * 1.5, "glow", 5 - life)
        out.append(c)
    return out


def lamp_glow(n: int = 8) -> list[Canvas]:
    """The lamp post's lantern breathing: the glass brightens and dims and the wick flame inside it flickers."""
    out = []
    for f in range(n):
        c = Canvas(16, 44, PR.SLOTS)
        c.rect(7, 12, 8, 42, "metal", 3)
        c.rect(8, 12, 8, 42, "metal", 2)
        c.rect(5, 41, 10, 43, "metal", 2)
        c.polygon([(4, 4), (11, 4), (12, 12), (3, 12)], "metal", 2)
        t = 2 * math.pi * f / n
        pulse = 0.5 + 0.35 * math.sin(t) + 0.15 * math.sin(3 * t + 0.6)
        sway = 1 if math.sin(2 * t + 0.3) > 0.4 else 0
        for y in range(5, 12):                                        # glass lit by the wick from below
            for x in range(5, 11):
                d = math.hypot((x + 0.5 - 8) * 1.2, y + 0.5 - 10)
                c.px(x, y, "glow", 4 if d < 2.6 + 2.4 * pulse else 3)
        c.rect(5, 5, 5, 11, "glow", 4)                                # the pane facing the light
        c.rect(7, 8 - (pulse > 0.55), 8, 10, "glow", 5)               # the flame
        c.px(7 + sway, 7 - (pulse > 0.55), "glow", 5)
        c.rect(7, 11, 8, 11, "metal", 1)                              # the burner
        c.polygon([(3, 4), (12, 4), (7.5, 0)], "metal", 3)
        c.rect(3, 4, 12, 4, "metal", 4)
        c.outline()
        out.append(c)
    return out


def halo(n: int = 8, w: int = 32, h: int = 32, *, flat: bool = False) -> list[Canvas]:
    """A light halo for flames and lamps, for additive blending: banded rings, dimmest outside, with dithered
    borders between bands. It breathes with two harmonics so it reads as firelight, not a metronome. `flat` squashes
    it 2:1 into a pool of light lying on the floor."""
    out = []
    ys, xs = _grid(w, h)
    cx, cy = w / 2, h / 2
    sy = 2.0 if flat else 1.0
    ck = _checker(h, w)
    for f in range(n):
        t = 2 * math.pi * f / n
        k = 1 + 0.07 * math.sin(t) + 0.04 * math.sin(3 * t + 0.7)
        r = np.hypot(xs - cx, (ys - cy) * sy) / (min(w, h * sy) / 2 * k)
        c = _canvas(w, h)
        for edge, shade in ((1.0, 0), (0.72, 1), (0.46, 2), (0.22, 3)):
            hard = r < edge - 0.05
            soft = (r >= edge - 0.05) & (r < edge + 0.05) & ck
            _paint(c, hard | soft, "glow", shade)
        out.append(c)
    return out


def smoke_column(n: int = 12, w: int = 24, h: int = 56, *, puffs: int = 12, base_r: float = 2.0,
                 grow: float = 4.6, drift: float = 7.0, seed: int = 31) -> list[Canvas]:
    """A rising column of smoke: `puffs` copies of one life cycle, each 1/puffs of the loop behind the last, so the
    column is always full and frame n equals frame 0. A puff is born small and dense at the bottom, rises, swells,
    leans downwind (right) and frays to a checkerboard before it vanishes off the top."""
    noise = tex.fbm(w, h, 4, tex.rng(seed), octaves=2)
    cx, by = w * 0.4, h - 2 - base_r
    out = []
    for f in range(n):
        c = _canvas(w, h)
        ps = []
        for i in range(puffs):
            a = (f / n + i / puffs) % 1.0
            x = cx + drift * a ** 1.6 + 0.8 * math.sin(2 * math.pi * (a * 1.5 + i * 0.37))
            y = by - (h - 2 * base_r - grow - 3) * a
            ps.append((a, (x, y, base_r + grow * a ** 0.8, max(0.0, (a - 0.35) / 0.65))))
        ps.sort(key=lambda p: -p[0])                                  # oldest (top) first, young puffs over them
        _puffs(c, [p for _, p in ps], "smoke", noise, rough=0.6)
        out.append(c)
    return out



# ------------------------------------------------------------------------------------------------ terrain animation
SIDE_W, SIDE_H, DECAL = 128, 32, 32


def _fall_field(seed: int, n: int) -> tuple[list[np.ndarray], list[np.ndarray]]:
    """The falling-water field per frame, and the foam dashes riding it. Ropes are pure vertical stripes (noise with
    one lattice row), so they never break into blobs; a coarse brightness pattern slides down them one texture
    height per loop and fine threads two, so the faster threads overtake and the sheet reads as accelerating.
    Foam dashes are short vertical strokes that fall two heights per loop."""
    r = tex.rng(seed)
    ropes = 0.65 * tex.value_noise(SIDE_W, SIDE_H, 32, 1, r) + 0.35 * tex.value_noise(SIDE_W, SIDE_H, 64, 1, r)
    ropes = (ropes - ropes.min()) / (ropes.max() - ropes.min())
    mod = tex.value_noise(SIDE_W, SIDE_H, 16, 2, r)
    threads = tex.value_noise(SIDE_W, SIDE_H, 64, 2, r)
    dash = np.zeros((SIDE_H, SIDE_W))
    for x, y in tex.poisson(SIDE_W, SIDE_H, 7, r, tries=900):
        if ropes[0, x] < 0.42:                                       # foam rides the crests, not the troughs
            continue
        ln = int(r.integers(3, 8))
        for k in range(ln):
            dash[(y + k) % SIDE_H, x] = max(dash[(y + k) % SIDE_H, x], 1 + (k >= ln - 2))
    step = SIDE_H // n
    fields, dashes = [], []
    for f in range(n):
        fields.append(0.5 * ropes + 0.3 * np.roll(mod, f * step, 0) + 0.2 * np.roll(threads, 2 * f * step, 0))
        dashes.append(np.roll(dash, 2 * f * step, 0))
    return fields, dashes


def waterfall_frames(n: int = 8, seed: int = 301) -> list[Canvas]:
    """Falling water for a cliff face, sampled like a terrain side (128 wide, repeats every 32 px of depth): banded
    ropes of water, brightest along their crests, with foam dashes streaking down them."""
    fields, dashes = _fall_field(seed, n)
    out = []
    for v, d in zip(fields, dashes):
        shade = np.select([v < 0.3, v < 0.44, v < 0.6], [1, 2, 3], 4)
        shade = tex.despeckle(shade, 1)
        foam = (v > 0.74) | (d > 0)
        c = _canvas(SIDE_W, SIDE_H)
        c.slot[:] = np.where(foam, SLOTS.id("foam"), SLOTS.id("main"))
        c.shade[:] = np.where(d > 1, 5, np.where(foam, np.where(v > 0.8, 4, 3), shade))
        out.append(c)
    return out


def waterfall_lip_frames(n: int = 8, seed: int = 301, h: int = 10) -> list[Canvas]:
    """The crest where the stream tips over the edge: a bright curl of foam on the top rows, then the face's own
    ropes (same seed, same scroll) thinning out, so the lip hands over to the face below without a join."""
    fields, dashes = _fall_field(seed, n)
    ck = _checker(h, SIDE_W)
    ys = np.arange(h)[:, None]
    out = []
    for v, d in zip(fields, dashes):
        v, d = v[:h], d[:h]
        c = _canvas(SIDE_W, h)
        curl = (ys <= 1) | ((ys == 2) & (v > 0.42)) | ((ys == 3) & (v > 0.6))
        _paint(c, curl, "foam", np.where(ys == 0, 5, np.where(v > 0.55, 4, 3)))
        rope = ~curl & ((v > 0.5 + ys * 0.03) | (d > 0)) & ((ys < 6) | ck)
        _paint(c, rope, "foam", np.where((v > 0.72) | (d > 1), 4, 3))
        out.append(c)
    return out


def _foam_field(seed: int) -> np.ndarray:
    return tex.fbm(DECAL, DECAL, 4, tex.rng(seed), octaves=3)


def waterfall_foam_frames(n: int = 8, seed: int = 311) -> list[Canvas]:
    """Churn on the pool at the foot of a fall, a tile-local decal along the tile's v = 0 edge (rotate it for the
    other edges). The foam is a noise field rolled away from the fall one tile per loop and thinned with distance,
    so it boils at the edge and breaks into drifting flecks; it wraps in u so neighbouring tiles join."""
    nf = _foam_field(seed)
    fine = _hash(DECAL, DECAL, seed + 1)
    ys, xs = np.mgrid[0:DECAL, 0:DECAL]
    ck = _checker(DECAL, DECAL)
    step = DECAL // n
    out = []
    for f in range(n):
        v = np.roll(nf, f * step, 0)
        fl = np.roll(fine, f * step, 0)
        dens = v - ys / 22.0
        c = _canvas(DECAL, DECAL)
        body = (dens > 0.12) | (ys < 2)
        _paint(c, body, "foam", np.where(dens > 0.4, 5, np.where(dens > 0.25, 4, 3)))
        edge = ~body & (dens > 0.02) & ck
        _paint(c, edge, "foam", 3)
        fleck = ~body & ~edge & (fl < 0.035) & (ys < 26)
        fleck |= np.roll(fleck, 1, 1)                                 # flecks in pairs, not lone pixels
        _paint(c, fleck & ~body, "foam", 4)
        shadow = ~body & np.roll(body, 1, 0) & (ys > 1)               # a darker water rim under the foam
        _paint(c, shadow, "main", 1)
        out.append(c)
    return out


def spray_frames(n: int = 8, w: int = 40, h: int = 28, seed: int = 321) -> list[Canvas]:
    """Mist thrown up where a fall hits the pool: a row of puffs, each rising and swelling on the same cycle with
    its own phase, fraying to a checkerboard as it climbs."""
    noise = tex.fbm(w, h, 4, tex.rng(seed), octaves=2)
    r = tex.rng(seed + 1)
    seeds = [(4 + i * (w - 8) / 6 + r.uniform(-1.5, 1.5), r.random()) for i in range(7)]
    out = []
    for f in range(n):
        c = _canvas(w, h)
        ps = []
        for x0, ph in seeds:
            for k in (0, 0.5):
                a = (f / n + ph + k) % 1.0
                ps.append((a, (x0 + 2 * a, h - 4 - (h - 12) * a, 2.4 + 3.2 * a, max(0.0, a * 1.2 - 0.2))))
        ps.sort(key=lambda p: -p[0])
        _puffs(c, [p for _, p in ps], "smoke", noise, rough=0.7)
        out.append(c)
    return out


def _lap(n: int, f: int) -> float:
    """Where the wash line sits, 0 (up the beach) .. 1 (pulled back): a quick run in, a slower draw back."""
    t = (f / n) % 1.0
    if t < 0.35:
        return 1 - math.sin(math.pi / 2 * t / 0.35)
    return ((t - 0.35) / 0.65) ** 1.4


def shore_frames(n: int = 8, *, corner: bool = False, seed: int = 331) -> list[Canvas]:
    """Foam lapping at a bank, a tile-local water decal: the bank is the tile's v = 0 edge (with `corner`, the
    v = 0 and u = 0 edges, for a water tile in the crook of two banks). The wash line runs in, leaves a wet sheen
    and a lace of bubbles behind it, and draws back. Its wobble repeats every 16 px, so edge tiles join."""
    fl = _hash(DECAL, DECAL, seed)
    ys, xs = np.mgrid[0:DECAL, 0:DECAL].astype(np.float64)
    d = np.minimum(xs, ys) if corner else ys
    along = np.where(xs < ys, ys, xs) if corner else xs
    ck = _checker(DECAL, DECAL)
    out = []
    for f in range(n):
        t = 2 * math.pi * f / n
        lap = _lap(n, f)
        line = 1.5 + 4.5 * lap + 0.9 * np.sin(2 * np.pi * along / 16 + t) + 0.6 * np.sin(2 * np.pi * along / 8 - t)
        c = _canvas(DECAL, DECAL)
        sheen = (d < line - 0.5) & (d > line - 3) & ck & (d >= 1)
        _paint(c, sheen, "main", 4)
        crest = np.abs(d - line) < 0.75
        _paint(c, crest, "foam", np.where(np.sin(2 * np.pi * along / 16 + t) > 0.3, 5, 4))
        lace = (d > line + 0.75) & (d < line + 3.5 + 2 * lap) & (fl < 0.16 - (d - line) * 0.02)
        lace |= np.roll(lace, 1, 1) & (d > line + 0.75)                # bubbles in pairs, not lone pixels
        _paint(c, lace, "foam", 3)
        _paint(c, d < 1, "foam", 4)                                   # the waterline against the bank
        out.append(c)
    return out


def ripple_frames(n: int = 8, w: int = 36, h: int = 18) -> list[Canvas]:
    """Rings spreading round a unit standing in water (ground layer, loop): two rings half a loop apart, each
    widening from the feet and dimming as it goes, squashed 2:1 to lie on the water."""
    ys, xs = _grid(w, h)
    cx, cy = w / 2, h / 2
    out = []
    for f in range(n):
        c = _canvas(w, h)
        for k in (0.0, 0.5):
            a = (f / n + k) % 1.0
            rx = 5 + (w / 2 - 6) * a
            d = _ring(xs, ys, cx, cy, rx, rx / 2)
            front = ys > cy
            shade = (4 if a < 0.45 else 3 if a < 0.8 else 2) + front.astype(int) * (a < 0.6)
            _paint(c, (np.abs(d) < 0.55) & ((a < 0.75) | _checker(h, w)), "foam", shade)
        out.append(c)
    return out


def splash_frames(w: int = 32, h: int = 32, seed: int = 341) -> list[Canvas]:
    """A unit stepping into water (once): a crown of drops leaps up and out, arcs and falls back while a ring runs
    out across the surface. Drops are 2x2 lit clusters early, single pixels as they fall."""
    r = tex.rng(seed)
    drops = [(a, r.uniform(1.6, 2.6), r.uniform(3.2, 4.6)) for a in np.linspace(0, 2 * np.pi, 11, endpoint=False)]
    cx, cy = w / 2, h - 7
    ys, xs = _grid(w, h)
    out = []
    for k in range(7):
        c = _canvas(w, h)
        rx = 4 + 2.4 * k
        d = _ring(xs, ys, cx, cy, rx, rx / 2)
        _paint(c, (np.abs(d) < 0.55) & ((k < 4) | _checker(h, w)), "foam", 4 if k < 3 else 3)
        if k < 2:
            _paint(c, _ring(xs, ys, cx, cy, 3, 1.5) < 0, "foam", 5)
        for a, sp, up in drops:
            t = k + 0.6
            x = cx + math.cos(a) * sp * t
            y = cy + math.sin(a) * sp * t * 0.5 - up * t + 0.55 * t * t
            if y > cy + math.sin(a) * sp * t * 0.5 + 0.5 or k >= 6:
                continue
            _pmax(c, x, y, "foam", 5 if k < 3 else 4)
            if k < 3:
                _pmax(c, x + 1, y, "foam", 4)
                _pmax(c, x, y + 1, "foam", 3)
        out.append(c)
    return out


def lava_bubble_frames(w: int = 16, h: int = 16, seed: int = 351) -> list[Canvas]:
    """A lava bubble (once): a dome swells out of the flow with a hot highlight, splits, throws a few gobbets that
    cool as they fall, and leaves a crust ring that sinks back. Spawn at random points, a few per second."""
    r = tex.rng(seed)
    gobs = [(r.uniform(-1.6, 1.6), r.uniform(1.4, 2.4)) for _ in range(5)]
    cx, cy = w / 2, h - 5
    ys, xs = _grid(w, h)
    out = []
    for k in range(10):
        c = _canvas(w, h)
        if k < 5:
            rad = 1.2 + k * 0.9
            dome = np.hypot(xs - cx, (ys - cy) * 1.25) < rad
            dome &= ys < cy + 1
            nx, ny = (xs - cx) / rad, (ys - cy) * 1.25 / rad
            lit = -(nx * 0.6 + ny * 0.8)
            _paint(c, dome, "glow", np.where(lit > 0.55, 5, np.where(lit > 0.0, 4, 3)))
            _paint(c, (np.abs(_ring(xs, ys, cx, cy + 0.5, rad + 1, (rad + 1) / 2)) < 0.5) & (ys > cy) & (c.slot == 0),
                   "alt", 2)
        else:
            t = k - 5
            ring = np.abs(_ring(xs, ys, cx, cy, 3.5 + t * 0.8, 1.75 + t * 0.4)) < 0.55
            hot = t < 2
            _paint(c, ring & ((t < 3) | _checker(h, w)), "glow" if hot else "alt", (5 if t == 0 else 4) if hot else 3)
            if t < 3:
                _paint(c, _ring(xs, ys, cx, cy, 2.5, 1.2) < 0, "alt", 1 + t // 2)   # the crater, crusting over
            for vx, vy in gobs:
                tt = t + 1
                x, y = cx + vx * tt * 1.6, cy - vy * tt * 2.2 + 0.9 * tt * tt
                if y < cy + 1:
                    _pmax(c, x, y, "glow", 5 - t)
        out.append(c)
    return out


def swamp_bubble_frames(w: int = 16, h: int = 16, seed: int = 361) -> list[Canvas]:
    """Marsh gas (once): two bubbles break the surface a beat apart, each a lit bead that swells, pops into a small
    ring and lets out a faint wisp."""
    cx, cy = w / 2, h - 4
    ys, xs = _grid(w, h)
    out = []
    for k in range(10):
        c = _canvas(w, h)
        for ox, oy, start in ((-2.5, 0.5, 0), (2.5, -0.5, 3)):
            t = k - start
            bx, by = cx + ox, cy + oy
            if t < 0 or t > 6:
                continue
            if t < 3:
                rad = 0.9 + t * 0.6
                bead = np.hypot(xs - bx, ys - by) < rad
                _paint(c, bead, "main", 4)
                _pmax(c, bx - rad * 0.4, by - rad * 0.5, "foam", 5)
            else:
                tt = t - 3
                ring = np.abs(_ring(xs, ys, bx, by, 2 + tt * 1.1, 1 + tt * 0.55)) < 0.55
                _paint(c, ring & ((tt < 2) | _checker(h, w)), "main", 4 - (tt > 1))
                for j in range(2):                                    # the wisp
                    _pmax(c, bx + (j + tt) % 2 - 0.5, by - 2 - tt * 1.5 - j, "smoke", 3 - (tt > 1))
        out.append(c)
    return out



# ------------------------------------------------------------------------------------------------------ interactive
def chest_frames() -> list[Canvas]:
    """A chest opening, from `props.chest` (frame 0, exactly) to `props.chest_open` (frame 5, exactly): the lid
    jumps on its catch, cracks open on a sliver of gold light, swings up, and the coins wink once it's open."""
    closed, opened = PR.chest(), PR.chest(open_=True)

    def body() -> Canvas:
        c = Canvas(26, 26, PR.SLOTS)
        PR.iso_box(c, 13, 8, 11, 8, "wood")
        for x in (5, 21):
            c.line(x, 12, x, 21, "metal", 3)
        c.line(13, 19, 13, 25, "metal", 2)
        return c

    def inside(c: Canvas, lip_y: float, light: int) -> None:
        """The open mouth between the lid's front edge and the box's: dark, with gold catching the light."""
        c.polygon([(2, 13.5), (13, lip_y), (24, 13.5), (13, 19)], "dark", 0)
        for x, y in ((9, 15), (12, 16), (15, 15), (17, 14)):
            if y > lip_y + 1:
                c.px(x, y, "gold", 3 + light)
                c.px(x + 1, y, "gold", 2 + light)

    def lid(c: Canvas, lift: float, front: float) -> None:
        """The closed lid raised `lift` px with its front corner at `front` (19 = shut)."""
        c.polygon([(2, 13.5 - lift), (13, 8 - lift), (24, 13.5 - lift), (13, front)], "wood", 4)
        c.polygon([(2, 13 - lift), (13, 7 - lift), (24, 13 - lift), (24, 11 - lift), (13, 4 - lift),
                   (2, 11 - lift)], "wood", 4)
        c.line(2, 12 - lift, 13, 6 - lift, "metal", 4)
        c.line(13, 6 - lift, 24, 12 - lift, "metal", 3)

    out = [closed]
    c = body()                                                        # 1: the catch springs, light leaks out
    inside(c, 18, 1)
    lid(c, 1, 18)
    c.rect(12, 18, 14, 20, "gold", 4)
    c.line(4, 15, 12, 19, "glow", 4)
    c.line(14, 19, 22, 15, "glow", 4)
    c.outline()
    out.append(c)
    c = body()                                                        # 2: cracked open on gold light
    inside(c, 12, 2)
    lid(c, 3, 11)
    for x0 in (8, 13, 18):
        c.line(x0, 15, x0 - 1, 11, "glow", 3)
    c.outline()
    out.append(c)
    c = body()                                                        # 3: lid swinging up, its underside
    inside(c, 9, 2)
    c.polygon([(2, 13), (13, 9), (13, 2), (2, 6)], "wood", 3)
    c.polygon([(13, 9), (24, 13), (24, 6), (13, 2)], "wood", 2)
    c.line(2, 6, 13, 2, "metal", 4)
    c.line(13, 2, 24, 6, "metal", 3)
    c.outline()
    out.append(c)
    c = opened.copy()                                                 # 4: open, the coins wink
    _twinkle(c, 12, 10, "glow", 2)
    _twinkle(c, 17, 12, "glow", 1)
    out.append(c)
    out.append(opened)
    return out


def door_frames(w: int = 26, h: int = 50) -> list[Canvas]:
    """A plank door in one tile of ashlar wall, in true iso 3/4: the wall runs down-right along U with its face to
    the viewer's lower-left. The door is hung on its left jamb, set halfway into the wall, and swings inward, so
    while it opens it is only ever seen through the arch; its face darkens as it turns away from the light. Frames:
    closed, latch lifted, 25, 50, 75 and 90 degrees. For a wall along V, mirror it."""
    L, T, H = 16.0, 4.0, 36.0                                         # one tile edge, in sprite pixels
    u0, u1, top, inset = 2.0, 14.0, 24.0, 2.0                        # doorway
    ox, oy = T + 1, H + 4
    ys, xs = _grid(w, h)
    X, Y = xs - ox, ys - oy

    def arch(u):
        return top + 3.5 * np.sqrt(np.clip(1 - ((u - (u0 + u1) / 2) / ((u1 - u0) / 2)) ** 2, 0, 1))

    # front face V = 0: U = X, z = U / 2 - Y
    fu, fz = X, X / 2 - Y
    front = (fu >= 0) & (fu < L) & (fz >= 0) & (fz < H)
    hole = front & (fu >= u0) & (fu < u1) & (fz < arch(fu))
    # top face z = H: U = (X + 2(Y + H)) / 2, V = (2(Y + H) - X) / 2
    tu, tv = (X + 2 * (Y + H)) / 2, (2 * (Y + H) - X) / 2
    topf = (tu >= 0) & (tu < L) & (tv > -T) & (tv <= 0)
    # right end U = L: V = L - X, z = (L + V) / 2 - Y
    ev = L - X
    ez = (L + ev) / 2 - Y
    endf = (ev > -T) & (ev <= 0) & (ez >= 0) & (ez < H)
    # left jamb U = u0, seen through the hole: V = u0 - X, z = (u0 + V) / 2 - Y
    jv = u0 - X
    jz = (u0 + jv) / 2 - Y
    jamb = hole & (jv > -T) & (jv <= 0) & (jz >= 0)
    # threshold z = 0 inside the doorway
    th_u, th_v = (X + 2 * Y) / 2, (2 * Y - X) / 2
    sill = hole & (th_u >= u0) & (th_u < u1) & (th_v > -T) & (th_v <= 0)

    def ashlar(u, z, shade_face: int) -> np.ndarray:
        course = np.floor(z / 6)
        off = np.where(course % 2 == 0, 0, 4)
        joint = ((z % 6) < 1) | (((u + off) % 8) < 1)
        lit = ((z % 6) >= 5) & ~joint
        return np.where(joint, 1, np.where(lit, shade_face + 1, shade_face))

    out = []
    for k, deg in enumerate((0, 0, 25, 50, 75, 90)):
        c = _canvas(w, h)
        _paint(c, hole, "dark", np.where(fz < 4, 1, 0))
        _paint(c, sill, "stone", 2)
        _paint(c, jamb, "stone", ashlar(jv * 2, jz, 2))
        th = math.radians(deg)
        span = math.cos(th) + math.sin(th)
        s = (X - (u0 + inset)) / span                                 # position along the leaf from the hinge
        U = u0 + s * math.cos(th)
        V = -inset - s * math.sin(th)
        z = (U + V) / 2 - Y
        leaf = hole & (s >= 0) & (s < u1 - u0) & (z >= 0) & (z < arch(u0 + s))
        face = 3 if deg < 40 else 2
        grain = np.where((s % 3) < 1, face - 1, face)
        band = (np.abs(z - 5) < 1) | (np.abs(z - 16) < 1)
        _paint(c, leaf, "wood", np.where(band, 0, grain))
        _paint(c, leaf & band, "metal", np.where(s < 3, 4, 2))
        ring = leaf & (np.hypot(s - 9.5, z - 11) < 1.6) & (np.hypot(s - 9.5, z - 11) > 0.6)
        _paint(c, ring, "metal", 4 if k == 0 else 3)
        if k == 1:                                                    # the latch lifts
            _paint(c, leaf & (np.abs(s - 9.5) < 0.6) & (z > 11.5) & (z < 13.5), "metal", 5)
        if deg:
            edge = hole & (np.abs(s - (u1 - u0 - 0.5)) < 0.5) & (z >= 0) & (z < arch(u1))
            _paint(c, edge, "wood", 4)
        _paint(c, front & ~hole, "stone", ashlar(fu, fz, 3))
        _paint(c, endf, "stone", ashlar(-ev * 2, ez, 2))
        _paint(c, topf, "stone", np.where(tv > -1, 5, 4))
        rim = front & ~hole & (fz < arch(fu) + 1) & (fu >= u0 - 1) & (fu < u1 + 1)
        _paint(c, rim, "stone", 4)                                     # dressed voussoirs round the arch
        c.outline()
        out.append(c)
    return out


def lever_frames(w: int = 26, h: int = 26) -> list[Canvas]:
    """A floor lever on a stone block: the arm swings from leaning left (off) to leaning right (on) in five frames;
    the knob is lit on its top-left whichever way it leans."""
    out = []
    for deg in (-45, -22, 0, 22, 45):
        c = _canvas(w, h)
        PR.iso_box(c, 13, 13, 7, 5, "stone")
        c.polygon([(10, 16.5), (13, 15), (16, 16.5), (13, 18)], "dark", 0)   # the slot
        a = math.radians(deg)
        px, py = 13, 16.5
        ex, ey = px + math.sin(a) * 12, py - math.cos(a) * 12
        c.thick_line(px, py, ex, ey, 2, "metal", 3)
        c.line(px - (1 if deg <= 0 else 0), py, ex - (1 if deg <= 0 else 0), ey, "metal", 4)
        c.ellipse(ex, ey - 0.5, 2.2, 2.2, "accent", 3)
        c.px(ex - 1, ey - 1.5, "accent", 5)
        c.px(ex, ey - 1.5, "accent", 4)
        c.outline()
        out.append(c)
    return out


def spike_trap_frames(w: int = 34, h: int = 30) -> list[Canvas]:
    """A pressure plate with a spike bed: idle, the plate clicks down, tips break the surface, the spikes punch up
    past full height, settle, hold, and draw back. Nine spikes on a 3x3 iso grid, drawn back to front."""
    cx, top = 17, 11
    holes = sorted([(cx + (i - j) * 4.5, top + 6.5 + (i + j) * 2.25) for i in (-1, 0, 1) for j in (-1, 0, 1)],
                   key=lambda p: p[1])
    heights = [0, 0, 2, 11, 9, 9, 5, 2]
    out = []
    for k, hgt in enumerate(heights):
        c = _canvas(w, h)
        sink = 1 if k >= 1 else 0
        PR.iso_box(c, cx, top, 15, 3, "stone", top_shade=3, left_shade=2, right_shade=1)
        c.polygon([(cx, top + 2 + sink), (cx + 12, top + 8 + sink), (cx, top + 14 + sink),
                   (cx - 12, top + 8 + sink)], "metal", 2 if sink else 3)
        c.line(cx - 12, top + 8 + sink, cx, top + 2 + sink, "metal", 4 if not sink else 3)
        for x, y in holes:
            y += sink
            c.rect(x - 1, y, x, y, "dark", 0)
        for x, y in holes:
            if not hgt:
                break
            y += sink
            c.polygon([(x - 1.5, y + 0.5), (x + 1.5, y + 0.5), (x, y - hgt)], "metal", 3)
            c.line(x - 1, y, x, y - hgt + 1, "metal", 5)
            c.line(x + 1, y, x + 1, y - hgt + 3, "metal", 1)
            c.px(x, y - hgt, "metal", 5)
        c.outline()
        out.append(c)
    return out


def portal_frames(n: int = 8, w: int = 40, h: int = 58) -> list[Canvas]:
    """A standing portal: a ring of dressed stones with glowing keystones round a swirling vortex. The vortex is a
    logarithmic spiral with four arms that turns a quarter turn per loop (one arm's worth), so frame n is frame 0;
    motes spiral inward along the arms and vanish in the bright core."""
    cx, cy, rx, ry = w / 2, 25.0, 13.0, 19.0
    ys, xs = _grid(w, h)
    nx, ny = (xs - cx) / rx, (ys - cy) / ry
    rr = np.hypot(nx, ny)
    th = np.arctan2(ny, nx)
    inner = rr < 1.0
    ring = (rr >= 1.0) & (rr < 1.32)
    stone_ang = np.floor((th + np.pi) / (2 * np.pi) * 16)
    out = []
    for f in range(n):
        c = _canvas(w, h)
        c.polygon([(cx - 16, 52), (cx + 16, 52), (cx + 18, 57), (cx - 18, 57)], "stone", 2)   # plinth
        c.polygon([(cx - 15, 50), (cx + 15, 50), (cx + 16, 52), (cx - 16, 52)], "stone", 4)
        spin = 2 * np.pi * f / n / 4
        sw = np.cos(4 * (th - spin) + 5.5 * np.log(rr + 0.05))
        v = 0.55 * sw + 0.75 * (1 - rr)
        shade = np.select([v > 0.95, v > 0.5, v > 0.1, v > -0.3], [5, 4, 3, 2], 1)
        _paint(c, inner, "main", shade)
        _paint(c, inner & (rr < 0.18), "core", 5)
        lit = -(nx * 0.6 + ny * 0.8) / np.maximum(rr, 1e-6)
        joint = ((stone_ang % 2 == 0) & (np.abs(((th + np.pi) / (2 * np.pi) * 16) % 1) < 0.12))
        _paint(c, ring, "stone", np.where(joint, 1, np.where(lit > 0.35, 4, np.where(lit < -0.35, 2, 3))))
        for j in range(4):                                            # keystones at the compass points
            a = j * np.pi / 2 - np.pi / 2
            kx, ky = cx + math.cos(a) * rx * 1.16, cy + math.sin(a) * ry * 1.16
            glow = 5 if (f + 2 * j) % n < 2 else 4
            c.rect(kx - 1, ky - 1, kx, ky, "accent", glow)
        for j in range(6):                                            # motes drawn into the core
            life = ((f / n) + j / 6) % 1.0
            a = j * np.pi / 3 + spin + life * 2.4
            r = 0.95 * (1 - life)
            _pmax(c, cx + math.cos(a) * rx * r, cy + math.sin(a) * ry * r, "core", 5 if life < 0.7 else 4)
        c.outline(skip={"main", "core"})
        out.append(c)
    return out


RUNE_GLYPHS = [["#.#", ".#.", "#.#"], ["##.", "#.#", "##."], [".#.", "###", ".#."]]


def rune_stone_frames(n: int = 8, w: int = 22, h: int = 36) -> list[Canvas]:
    """A standing stone with three carved runes that glow in turn: a pulse runs down the glyphs once per loop, each
    flaring and fading, and a mote lifts off whichever rune is brightest."""
    from .props import blob
    out = []
    for f in range(n):
        c = _canvas(w, h)
        c.polygon([(5, 34), (17, 34), (18, 22), (16, 9), (12, 2), (8, 3), (5, 13), (4, 26)], "stone", 3)
        blob(c, "stone", 10.5, 15, 6.8, 13, k=1.8, hi=4)
        c.polygon([(2, 35), (20, 35), (18, 32), (4, 32)], "dark", 1)
        for col, y in enumerate((8, 15, 22)):
            lv = math.cos(2 * math.pi * (f / n - col / 3))
            sh = 5 if lv > 0.7 else 4 if lv > 0.2 else 3 if lv > -0.3 else None
            for row, line in enumerate(RUNE_GLYPHS[col]):
                for dx, ch in enumerate(line):
                    if ch != "#":
                        continue
                    x, yy = 9 + dx, y + row
                    if sh is None:
                        c.px(x, yy, "stone", 1)                       # dormant: just the carving
                        continue
                    c.px(x, yy, "glow", sh)
                    if sh == 5:                                       # light spilling onto the stone round it
                        for ddx, ddy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                            if c.slot[yy + ddy, x + ddx] == c.slots.id("stone"):
                                c.px(x + ddx, yy + ddy, "stone", 5)
        c.outline()
        brightest = max(range(3), key=lambda col: math.cos(2 * math.pi * (f / n - col / 3)))
        life = (f % (n // 3 + 1))
        _pmax(c, 13 + life % 2, (8, 15, 22)[brightest] - 2 - life * 2, "glow", 5 - life)
        out.append(c)
    return out



# -------------------------------------------------------------------------------------------------------- particles
def _glowdot(c: Canvas, x: float, y: float, slot: str, level: float) -> None:
    """A point light: a bright pixel and, when it's bright enough, a dim cross round it (additive art)."""
    if level <= 0.05:
        return
    core = int(round(1 + level * 4))
    _pmax(c, x, y, slot, core)
    if level > 0.45:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            _pmax(c, x + dx, y + dy, slot, core - 3)


def fireflies_frames(n: int = 16, w: int = 32, h: int = 32, count: int = 5, seed: int = 401) -> list[Canvas]:
    """A loose swarm: each fly drifts on its own Lissajous loop (whole periods, so it closes on itself) and blinks
    on its own schedule, a slow swell and a quick fade."""
    r = tex.rng(seed)
    flies = [(r.uniform(8, w - 8), r.uniform(8, h - 8), r.uniform(3, 7), r.uniform(2, 5), int(r.integers(1, 3)),
              int(r.integers(1, 3)), r.random() * 2 * np.pi, r.random()) for _ in range(count)]
    out = []
    for f in range(n):
        c = _canvas(w, h)
        t = f / n
        for x0, y0, ax, ay, p, q, ph, blink in flies:
            x = x0 + ax * math.sin(2 * math.pi * p * t + ph)
            y = y0 + ay * math.sin(2 * math.pi * q * t + ph * 1.7)
            b = (t + blink) % 1.0
            level = math.sin(math.pi * b / 0.6) if b < 0.6 else 0.0
            _glowdot(c, x, y, "glow", level ** 0.7)
        out.append(c)
    return out


def leaf_frames(n: int = 8, w: int = 9, h: int = 9) -> list[Canvas]:
    """A falling leaf tumbling end over end: it flips about its long axis (lit face, edge, dark underside, edge)
    while rocking side to side. The game moves it; this is the spin."""
    out = []
    shape = [(-4, 0), (-2, -1.6), (1, -1.8), (3.5, -0.6), (4.2, 0), (3.5, 0.6), (1, 1.8), (-2, 1.6)]
    for f in range(n):
        c = _canvas(w, h)
        ph = 2 * math.pi * f / n
        flip = math.cos(ph)
        rock = math.radians(35 * math.sin(ph) - 20)
        pts = []
        for x, y in shape:
            y *= max(abs(flip), 0.22)
            pts.append((w / 2 + x * math.cos(rock) - y * math.sin(rock),
                        h / 2 + x * math.sin(rock) + y * math.cos(rock)))
        c.polygon(pts, "leaf", 4 if flip > 0 else 2)
        if abs(flip) > 0.4:
            (x0, y0), (x1, y1) = pts[0], pts[4]
            c.line(x0, y0, x1, y1, "leaf", 3 if flip > 0 else 1)                       # the midrib
        c.px(pts[1][0], pts[1][1], "leaf", 5 if flip > 0 else 3)
        c.outline()
        out.append(c)
    return out


def snowflake_frames(n: int = 6, w: int = 7, h: int = 7) -> list[Canvas]:
    """A big flake: a six-armed star that turns between its upright and diagonal poses and twinkles once."""
    out = []
    for f in range(n):
        c = _canvas(w, h)
        diag = f % 2 == 1
        arms = ((1, 1), (-1, -1), (1, -1), (-1, 1)) if diag else ((1, 0), (-1, 0), (0, 1), (0, -1))
        for dx, dy in arms:
            for k in (1, 2):
                if diag and k == 2:
                    continue
                _pmax(c, 3 + dx * k, 3 + dy * k, "main", 4 if k == 1 else 3)
        if not diag:
            for dx, dy in ((1, 1), (-1, -1), (1, -1), (-1, 1)):
                _pmax(c, 3 + dx, 3 + dy, "main", 2)
        _pmax(c, 3, 3, "main", 5 if f == 2 else 4)
        out.append(c)
    return out


def rain_splash_frames(w: int = 9, h: int = 6) -> list[Canvas]:
    """A raindrop landing (once, 4 frames): the drop's tail, a tiny crown, two droplets kicked out, a fading ring."""
    out = []
    for k in range(4):
        c = _canvas(w, h)
        if k == 0:
            _pmax(c, 4, 2, "main", 3)
            _pmax(c, 4, 3, "main", 5)
        elif k == 1:
            for x, y, sh in ((3, 3, 4), (5, 3, 4), (4, 4, 5), (2, 4, 3), (6, 4, 3)):
                _pmax(c, x, y, "main", sh)
        elif k == 2:
            for x, y, sh in ((1, 2, 4), (7, 2, 4), (2, 5, 3), (3, 5, 3), (5, 5, 3), (6, 5, 3)):
                _pmax(c, x, y, "main", sh)
        else:
            for x, y in ((1, 4), (2, 5), (6, 5), (7, 4)):
                _pmax(c, x, y, "main", 2)
        out.append(c)
    return out


def motes_frames(n: int = 16, w: int = 32, h: int = 48, count: int = 12, seed: int = 421) -> list[Canvas]:
    """Dust hanging in a sunbeam: specks drift on slow closed loops and catch the light as they turn, so they
    twinkle from dim to bright and back. Meant inside a light shaft, additive."""
    r = tex.rng(seed)
    motes = [(r.uniform(3, w - 3), r.uniform(3, h - 3), r.uniform(1, 4), r.uniform(2, 6), r.random(), r.random(),
              int(r.integers(1, 3))) for _ in range(count)]
    out = []
    for f in range(n):
        c = _canvas(w, h)
        t = f / n
        for x0, y0, ax, ay, ph, tw, k in motes:
            x = x0 + ax * math.sin(2 * math.pi * (t + ph))
            y = y0 - ay * math.sin(2 * math.pi * (t + ph) + 0.8)
            level = 0.5 + 0.5 * math.cos(2 * math.pi * (k * t + tw))
            _pmax(c, x, y, "glow", int(round(1 + level * 4)))
        out.append(c)
    return out


def embers_frames(n: int = 12, w: int = 24, h: int = 48, count: int = 9, seed: int = 431) -> list[Canvas]:
    """Embers rising off a fire or lava: each spark is born hot at the bottom, wobbles up, cools and flickers out;
    the sparks are the same life cycle offset by 1/count of the loop."""
    r = tex.rng(seed)
    sparks = [(r.uniform(5, w - 5), r.uniform(0.7, 1.0), r.uniform(1, 3), r.random()) for _ in range(count)]
    out = []
    for f in range(n):
        c = _canvas(w, h)
        for i, (x0, speed, wob, ph) in enumerate(sparks):
            a = (f / n + i / count) % 1.0
            y = h - 2 - a * (h - 4) * speed
            x = x0 + wob * math.sin(2 * math.pi * (a * 1.5 + ph)) + a * 4
            heat = 1 - a
            flick = (f + i) % 3 != 0 or a < 0.5
            if flick:
                _glowdot(c, x, y, "glow", heat * 0.9 + 0.1)
        out.append(c)
    return out


BIRD = [  # wings up, level, down, level. L lit near wing, R far wing in shadow, B body, H head
    [".L.......R.", "..L.....R..", "...L...R...", "....BHB....", ".....B....."],
    ["...........", ".LLL...RRR.", "L...BHB...R", ".....B.....", "..........."],
    ["...........", "....BHB....", "...L.B.R...", "..L.....R..", ".L.......R."],
    ["...........", ".LLL...RRR.", "L...BHB...R", ".....B.....", "..........."],
]
_BIRD_SHADE = {"L": 4, "R": 2, "B": 3, "H": 5}


def bird_pose(c: Canvas, x: int, y: int, k: int) -> None:
    """One bird as a gull's V, seen from behind and above: it reads as a bird at any heading, so the game can fly it
    any way across the screen. (x, y) is the body's centre."""
    for row, line in enumerate(BIRD[k % 4]):
        for col, ch in enumerate(line):
            if ch in _BIRD_SHADE:
                c.px(x - 5 + col, y - 2 + row, "main", _BIRD_SHADE[ch])


def bird_frames(w: int = 11, h: int = 5) -> list[Canvas]:
    out = []
    for k in range(4):
        c = _canvas(w, h)
        bird_pose(c, 5, 2, k)
        out.append(c)
    return out


def flock_frames(w: int = 56, h: int = 28) -> list[Canvas]:
    """A loose V of five birds; each flaps a beat off its neighbours so the flock shimmers instead of flashing."""
    pos = [(47, 13, 0), (36, 8, 1), (37, 19, 3), (24, 4, 2), (25, 23, 1)]
    out = []
    for k in range(4):
        c = _canvas(w, h)
        for x, y, ph in pos:
            bird_pose(c, x, y, k + ph)
        out.append(c)
    return out


def butterfly_frames(w: int = 11, h: int = 9) -> list[Canvas]:
    """A butterfly seen from above-front: wings open, half, shut (edge on), half. Fore wings big and lit, hind wings
    smaller and darker; the body goes on after the outline so it stays a thin dark line."""
    out = []
    for spread in (1.0, 0.55, 0.12, 0.55):
        c = _canvas(w, h)
        cx, cy = 5, 4
        for side in (-1, 1):
            c.ellipse(cx + 0.5 + side * (0.6 + 2.2 * spread), cy - 1, max(0.5, 2.0 * spread), 1.9, "main",
                      4 if side < 0 else 3)
            c.ellipse(cx + 0.5 + side * (0.6 + 1.5 * spread), cy + 1.6, max(0.5, 1.4 * spread), 1.3, "main",
                      3 if side < 0 else 2)
        if spread > 0.5:
            c.px(cx - 2, cy - 2, "main", 5)
        c.outline()
        c.rect(cx, cy - 2, cx, cy + 2, "dark", 1)
        c.px(cx - 1, cy - 3, "dark", 2)
        c.px(cx + 1, cy - 3, "dark", 2)
        out.append(c)
    return out


def fish_frames(w: int = 32, h: int = 30) -> list[Canvas]:
    """A fish jumping (once): a ripple where it breaks the surface, an arc through the air with its body turning
    along the arc, a splash where it dives, and rings on the water."""
    cx, sy = w / 2, h - 6
    ys, xs = _grid(w, h)
    out = []
    for k in range(12):
        c = _canvas(w, h)
        for x0, start in ((cx - 8, 1), (cx + 8, 8)):                  # rings at take-off and landing
            t = k - start
            if 0 <= t < 4:
                rx = 2 + t * 1.6
                ring = np.abs(_ring(xs, ys, x0, sy, rx, rx / 2)) < 0.55
                _paint(c, ring & ((t < 2) | _checker(h, w)), "foam", 4 if t < 2 else 3)
        if 1 <= k <= 8:
            t = (k - 1) / 7
            x = cx - 8 + 16 * t
            y = sy - 15 * 4 * t * (1 - t)
            ang = math.atan2(-15 * 4 * (1 - 2 * t), 16)
            dx, dy = math.cos(ang), math.sin(ang)
            for j in range(-3, 3):                                    # body, tail behind, head ahead
                bx, by = x + dx * j, y + dy * j
                c.px(bx, by, "main", 4 if j > 0 else 3)
                c.px(bx - dy, by + dx, "main", 2)
            c.px(x - dx * 4 - dy, y - dy * 4 + dx, "main", 3)            # tail fork
            c.px(x - dx * 4 + dy, y - dy * 4 - dx, "main", 3)
            c.px(x + dx * 2 - dy * 0.0, y + dy * 2 - 1, "main", 5)       # glint
        if k in (1, 8, 9):                                           # spray at the surface
            sx = cx - 8 if k == 1 else cx + 8
            for dx, dy in ((-2, -2), (2, -2), (0, -3), (-3, 0), (3, 0)):
                _pmax(c, sx + dx * (1 + (k == 9)), sy + dy - (k == 9), "foam", 5 if k != 9 else 3)
        out.append(c)
    return out


# ------------------------------------------------------------------------------------------------- weather overlays
OVERLAY = 128

_RAIN = {"rain": ((196, 214, 236), 1.0, 9), "storm": ((178, 196, 222), 1.7, 12), "drizzle": ((206, 218, 232), 0.5, 5)}
_SNOW = {"snow": (232, 240, 250), "ash": (176, 168, 168)}
_FOG = {"mist": (220, 228, 238), "miasma": (150, 178, 120), "smoke": (150, 140, 136)}
_RAYS = {"warm": (255, 214, 140), "cool": (170, 210, 255), "holy": (255, 238, 170)}


def _streak_layer(size: int, density: float, length: int, seed: int) -> np.ndarray:
    """Rain streaks (slope 1 across to 4 down) on a size x size torus: 0 empty, 2 head, 1 tail."""
    r = tex.rng(seed)
    a = np.zeros((size, size), np.uint8)
    for x, y in tex.poisson(size, size, 10 / density, r, tries=int(1400 * density)):
        for k in range(length):
            yy, xx = (y - k) % size, (x - k // 4) % size
            a[yy, xx] = max(a[yy, xx], 2 if k < 2 else 1)
    return a


def rain_sheet(variant: str, n: int = 16) -> list[Image.Image]:
    """A tileable falling-rain overlay. Streaks lean one pixel across per four down. The near layer moves (8, 32)
    px a frame, one 128 px tile along the slant per loop; the far layer is a 64 px tile moving half as fast, so it
    also closes after one loop and reads as rain further off."""
    rgb, density, length = _RAIN[variant]
    near = _streak_layer(OVERLAY, density, length, 501)
    far = np.tile(_streak_layer(64, density * 1.4, max(3, length // 2), 502), (2, 2))
    out = []
    for f in range(n):
        a = np.zeros((OVERLAY, OVERLAY, 4), np.uint8)
        fl = np.roll(far, (16 * f, 4 * f), (0, 1))
        nr = np.roll(near, (32 * f, 8 * f), (0, 1))
        a[..., :3] = rgb
        a[..., 3] = np.select([nr == 2, nr == 1, fl == 2, fl == 1], [170, 110, 90, 55], 0)
        out.append(Image.fromarray(a, "RGBA"))
    return out



def snow_sheet(variant: str, n: int = 16) -> list[Image.Image]:
    """A tileable snowfall overlay: near flakes (2x2, lit top-left) fall one tile per loop and far specks (a 64 px
    tile) half as fast; every flake sways on a whole sine period, so the loop closes."""
    rgb = _SNOW[variant]
    r = tex.rng(511)
    near = [(x, y, r.random(), r.uniform(1.0, 2.5)) for x, y in tex.poisson(OVERLAY, OVERLAY, 12, r, tries=1200)]
    far = [(x, y, r.random()) for x, y in tex.poisson(64, 64, 9, r, tries=500)]
    out = []
    for f in range(n):
        alpha = np.zeros((OVERLAY, OVERLAY), np.uint8)
        t = f / n
        for x, y, ph in far:
            xx = int(round(x + math.sin(2 * math.pi * (t + ph)))) % 64
            yy = (y + 4 * f) % 64
            for ox in (0, 64):
                for oy in (0, 64):
                    alpha[yy + oy, xx + ox] = max(alpha[yy + oy, xx + ox], 120)
        for x, y, ph, amp in near:
            xx = int(round(x + amp * math.sin(2 * math.pi * (t + ph))))
            yy = y + 8 * f
            for dx, dy, a in ((0, 0, 235), (1, 0, 170), (0, 1, 170), (1, 1, 120)):
                alpha[(yy + dy) % OVERLAY, (xx + dx) % OVERLAY] = a
        img = np.zeros((OVERLAY, OVERLAY, 4), np.uint8)
        img[..., :3] = rgb
        img[..., 3] = alpha
        out.append(Image.fromarray(img, "RGBA"))
    return out


def _bands(v: np.ndarray, steps: list[tuple[float, int]], soft: float = 0.035) -> np.ndarray:
    """Quantises a 0..1 field to a few alpha levels, checkerboarding a thin band at each step: banded, pixel-art
    translucency instead of a smooth gradient."""
    h, w = v.shape
    ck = _checker(h, w)
    out = np.zeros((h, w), np.uint8)
    for t, a in steps:
        on = (v > t + soft) | ((v > t - soft) & ck)
        out = np.where(on, a, out)
    return out


def fog_band(variant: str, n: int = 16, h: int = 64) -> list[Image.Image]:
    """A drifting band of fog, tileable horizontally: a soft vertical profile whose centre line wanders, broken up
    by two periodic noises, one scrolling right a tile per loop and one scrolling left across a half-tile copy, so
    the bank churns as it moves. Fades to nothing top and bottom; lay it across the map at any height."""
    rgb = _FOG[variant]
    r = tex.rng(521)
    n1 = tex.fbm(OVERLAY, h, 4, r, octaves=3)
    n2 = np.tile(tex.fbm(64, h, 4, r, octaves=2), (1, 2))
    wander = tex.fbm(OVERLAY, 8, 2, r, octaves=2)[0]
    ys = np.arange(h)[:, None].astype(np.float64)
    out = []
    for f in range(n):
        a1 = np.roll(n1, 8 * f, 1)
        a2 = np.roll(n2, -4 * f, 1)
        cy = h / 2 + 10 * (np.roll(wander, 8 * f) - 0.5)
        bell = np.exp(-((ys - cy[None, :]) / (h * 0.27)) ** 2)
        v = bell * (0.55 + 0.45 * a1) * (0.7 + 0.3 * a2)
        img = np.zeros((h, OVERLAY, 4), np.uint8)
        img[..., :3] = rgb
        img[..., 3] = _bands(v, [(0.12, 45), (0.26, 85), (0.42, 125), (0.6, 160)])
        out.append(Image.fromarray(img, "RGBA"))
    return out


def light_shafts(variant: str, n: int = 8, w: int = 128, h: int = 160) -> list[Image.Image]:
    """God rays falling from the top-left, for additive blending: five shafts slanting down-right, widening as
    they fall and fading out low down. Each breathes on its own phase of one loop. Alpha carries the strength in
    a few hard bands, so the light stays pixel art."""
    rgb = _RAYS[variant]
    r = tex.rng(531)
    dx, dy = 0.42, 0.91
    ys, xs = _grid(w, h)
    along = xs * dx + ys * dy
    across = xs * dy - ys * dx
    rays = [(c, r.uniform(5, 11), r.uniform(0.6, 1.0), r.random()) for c in np.linspace(-40, 84, 5) +
            r.uniform(-5, 5, 5)]
    fade = np.clip(along / 18, 0, 1) * np.clip((h * 0.98 - along) / 80, 0, 1)
    fade *= np.clip(xs / 16, 0, 1) * np.clip((w - xs) / 16, 0, 1)            # no hard cut at the sprite's sides
    out = []
    for f in range(n):
        v = np.zeros((h, w))
        for c, hw, inten, ph in rays:
            width = hw * (0.7 + along / 90)
            k = np.clip(1 - np.abs(across - c) / width, 0, 1) ** 0.8
            v = np.maximum(v, k * inten * (0.72 + 0.28 * math.sin(2 * math.pi * (f / n + ph))))
        v *= fade
        img = np.zeros((h, w, 4), np.uint8)
        img[..., :3] = rgb
        img[..., 3] = _bands(v, [(0.16, 18), (0.36, 34), (0.62, 54)], soft=0.03)
        out.append(Image.fromarray(img, "RGBA"))
    return out


# ------------------------------------------------------------------------------------------------------- catalogue
@dataclass
class Amb:
    id: str
    name: str
    group: str                          # props | terrain | interactive | particles | weather
    make: Callable[..., list]           # -> list[Canvas]; with rgba=True, make(variant) -> list[Image]
    variants: dict[str, dict[str, str]]
    fps: float
    anchor: tuple[int, int] | None      # None: bottom centre
    layer: str = "over"                 # ground | over | screen
    blend: str = "normal"               # normal | add
    loop: bool = True
    kind: str = "sprite"                # sprite | side | lip | decal | overlay
    replaces: str | None = None         # the static prop this swaps in for (same frame size and anchor)
    rgba: bool = False
    states: dict[str, list[int]] | None = None
    extra: dict = field(default_factory=dict)
    note: str = ""


def _prop_variants(pid: str) -> dict[str, dict[str, str]]:
    return dict(_prop(pid).variants)


def _replace(pid: str, make: Callable[[], list[Canvas]], fps: float, name: str, note: str = "") -> Amb:
    p = _prop(pid)
    f0 = p.frames()[0]
    return Amb(f"{pid}_anim", name, "props", make, _prop_variants(pid), fps, p.anchor_of(f0),
               replaces=pid, note=note or "start each instance on a random frame so neighbours don't move in step")


_HALO = {"fire": _v(glow="halo_fire"), "arcane": _v(glow="halo_arcane"), "fel": _v(glow="halo_fel")}

CATALOGUE: list[Amb] = [
    _replace("tree", tree_sway, 6, "Broadleaf sway"),
    _replace("tree_fruit", lambda: tree_sway("tree_fruit"), 6, "Fruit tree sway"),
    _replace("pine", pine_sway, 5, "Pine sway"),
    _replace("pine_snowy", lambda: pine_sway("pine_snowy"), 5, "Snowy pine sway"),
    _replace("dead_tree", dead_tree_sway, 5, "Dead tree creak"),
    _replace("bush", bush_sway, 6, "Bush rustle"),
    _replace("bush_berry", lambda: bush_sway("bush_berry"), 6, "Berry bush rustle"),
    _replace("tall_grass", grass_wave, 7, "Tall grass wave"),
    _replace("swamp_tree", swamp_tree_sway, 5, "Cypress sway"),
    _replace("fern", fern_sway, 6, "Fern bob"),
    _replace("mushroom_cluster", mushroom_glow, 6, "Toadstool breathing", "the glow variant's light pulses with it"),
    _replace("reeds", reeds_wave, 6, "Reeds wave"),
    _replace("crystals", crystals_shimmer, 10, "Crystal shimmer"),
    _replace("torch", torch_flicker, 10, "Torch flicker", "replaces the 4-frame torch; pair with halo"),
    _replace("lamp_post", lamp_glow, 8, "Lamp glow", "pair with halo (fire) at the lantern"),
    Amb("halo", "Light halo", "props", halo, _HALO, 8, (16, 16), blend="add",
        extra={"offsets": {"torch": [0, -27], "lamp_post": [0, -35], "campfire": [0, -10]}},
        note="additive glow centred on a flame; offsets are from the light prop's anchor"),
    Amb("light_pool", "Light pool", "props", lambda: halo(8, 48, 24, flat=True), _HALO, 8, (24, 12),
        layer="ground", blend="add", note="additive floor glow under a light prop, centred on its anchor"),
    Amb("campfire_smoke", "Campfire smoke", "props", smoke_column, {"grey": _v(smoke="smoke")}, 8, (9, 55),
        extra={"offsets": {"campfire": [0, -14]}}, note="stand it on the campfire, raised by the offset"),
    Amb("waterfall", "Waterfall face", "terrain", waterfall_frames, {
        "clear": _v(main="terrain/water", foam="foam"), "swamp": _v(main="terrain/water_swamp", foam="steam"),
        "lava": _v(main="terrain/lava", foam="prop/flame")}, 10, (0, 0), layer="ground", kind="side",
        note="a cliff-side texture: U along the edge / 128, V = depth below the top / 32 (repeats); loop"),
    Amb("waterfall_lip", "Waterfall crest", "terrain", waterfall_lip_frames, {
        "clear": _v(foam="foam"), "swamp": _v(foam="steam"), "lava": _v(foam="prop/flame")}, 10, (0, 0),
        layer="ground", kind="lip", note="over the top band of the waterfall face, same U; same scroll as the face"),
    Amb("waterfall_foam", "Waterfall foam", "terrain", waterfall_foam_frames, {
        "clear": _v(main="terrain/water", foam="foam"), "swamp": _v(main="terrain/water_swamp", foam="steam"),
        "lava": _v(main="terrain/crust", foam="prop/flame")}, 8, (0, 0), layer="ground", kind="decal",
        note="on the pool tile at the fall's foot; the fall is at the decal's v = 0 edge (rotate for others)"),
    Amb("waterfall_spray", "Waterfall spray", "terrain", spray_frames, {
        "mist": _v(smoke="spray"), "steam": _v(smoke="steam")}, 8, (20, 25),
        note="billboard at the foot of a fall, anchored on the pool surface where the water lands"),
    Amb("shore_foam", "Shoreline foam", "terrain", shore_frames, {
        "clear": _v(main="terrain/water", foam="foam"), "swamp": _v(main="terrain/water_swamp", foam="steam")},
        6, (0, 0), layer="ground", kind="decal", note="water tile next to a bank on its v = 0 edge (rotate)"),
    Amb("shore_foam_corner", "Shoreline foam (corner)", "terrain", lambda: shore_frames(corner=True), {
        "clear": _v(main="terrain/water", foam="foam"), "swamp": _v(main="terrain/water_swamp", foam="steam")},
        6, (0, 0), layer="ground", kind="decal", note="water tile with banks on its v = 0 and u = 0 edges"),
    Amb("ripple", "Wading ripples", "terrain", ripple_frames, {
        "clear": _v(foam="foam"), "swamp": _v(foam="steam")}, 8, (18, 9), layer="ground",
        note="under a unit standing in water, centred on its anchor"),
    Amb("splash", "Water splash", "terrain", splash_frames, {
        "clear": _v(foam="foam"), "swamp": _v(foam="steam")}, 14, (16, 25), loop=False,
        note="once, when a unit steps into water; anchor on its feet"),
    Amb("lava_bubble", "Lava bubble", "terrain", lava_bubble_frames, {
        "molten": _v(glow="terrain/lava", alt="terrain/crust")}, 10, (8, 11), layer="ground", loop=False,
        note="spawn at random points on lava, 1-3 per tile per few seconds"),
    Amb("swamp_bubbles", "Swamp bubbles", "terrain", swamp_bubble_frames, {
        "marsh": _v(main="terrain/water_swamp", foam="foam", smoke="steam")}, 8, (8, 12), layer="ground",
        loop=False, note="spawn at random points on swamp water"),
    Amb("chest_anim", "Chest opening", "interactive", chest_frames,
        {"oak": dict(_prop("chest").variants["oak"], glow="magic/holy")}, 10, (13, 25), loop=False, replaces="chest",
        states={"closed": [0], "opening": [0, 1, 2, 3, 4, 5], "open": [5], "closing": [5, 3, 2, 0]},
        note="frame 0 is props.chest and frame 5 is props.chest_open, pixel for pixel"),
    Amb("door", "Wooden door", "interactive", door_frames, {
        "oak": _v(wood="wood/oak", metal="metal/iron", stone="terrain/stone", dark="cloth/charcoal"),
        "dark": _v(wood="wood/dark", metal="metal/iron", stone="terrain/stone_dark", dark="cloth/charcoal")},
        8, (13, 44), loop=False,
        states={"closed": [0], "opening": [1, 2, 3, 4, 5], "open": [5], "closing": [4, 3, 2, 0]},
        extra={"wall": "one tile of wall along U, its face on a tile's +V edge; anchor = that edge's midpoint",
               "mirror": "flip horizontally for a wall along V (anchor x becomes width - 1 - x)"},
        note="the door swings inward, behind the wall face; blocks movement while closed"),
    Amb("lever", "Lever", "interactive", lever_frames, {
        "iron": _v(stone="terrain/stone_dark", metal="metal/iron", accent="cloth/crimson", dark="cloth/charcoal"),
        "brass": _v(stone="terrain/stone_warm", metal="metal/bronze", accent="cloth/teal", dark="cloth/charcoal")},
        12, (13, 25), loop=False, states={"off": [0], "switch_on": [0, 1, 2, 3, 4], "on": [4],
                                          "switch_off": [4, 3, 2, 1, 0]}),
    Amb("spike_trap", "Spike trap", "interactive", spike_trap_frames, {
        "iron": _v(stone="terrain/stone", metal="metal/steel", dark="cloth/charcoal"),
        "rusted": _v(stone="terrain/stone_dark", metal="metal/bronze", dark="cloth/charcoal")},
        14, (17, 18), layer="ground", loop=False,
        states={"armed": [0], "trigger": [1, 2, 3, 4, 5], "raised": [5], "retract": [6, 7, 0]},
        note="sits on the tile centre; the spikes rise above it, so sort it as 'over' while raised"),
    Amb("portal", "Portal", "interactive", portal_frames, {
        "arcane": _v(main="magic/arcane", core="magic/frost", accent="magic/frost", stone="terrain/stone_dark"),
        "shadow": _v(main="magic/shadow", core="magic/arcane", accent="magic/shadow", stone="terrain/stone_dark"),
        "fel": _v(main="fel", core="magic/holy", accent="fel", stone="terrain/stone")}, 10, (20, 57),
        note="loops; pair with halo (arcane / fel) behind it"),
    Amb("rune_stone", "Rune stone", "interactive", rune_stone_frames, {
        "arcane": _v(stone="rune_stone", glow="magic/arcane", dark="cloth/charcoal"),
        "holy": _v(stone="terrain/stone_warm", glow="magic/holy", dark="cloth/charcoal"),
        "nature": _v(stone="terrain/stone", glow="magic/nature", dark="cloth/charcoal"),
        "blood": _v(stone="terrain/stone_dark", glow="magic/blood", dark="cloth/charcoal")}, 6, (11, 34),
        note="loops while active; show frame 0 dimmed (or the static prop) when dormant"),
    Amb("fireflies", "Fireflies", "particles", fireflies_frames, {
        "gold": _v(glow="firefly"), "wisp": _v(glow="wisp"), "ember": _v(glow="ember")}, 8, (16, 16), blend="add",
        extra={"motion": {"vx": 3, "vy": 0, "sway": [6, 4.0]}},
        note="a swarm of five; scatter a few over grass and water at dusk and night"),
    Amb("leaf", "Falling leaf", "particles", leaf_frames, {
        "green": _v(leaf="foliage/leaf_light"), "autumn": _v(leaf="foliage/leaf_autumn"),
        "red": _v(leaf="foliage/leaf_red"), "blossom": _v(leaf="foliage/blossom")}, 10, (4, 4),
        extra={"motion": {"vx": 6, "vy": 14, "sway": [7, 2.2]}},
        note="spawn under tree canopies; the spin loops while the game moves it down and lets it settle"),
    Amb("snowflake", "Snowflake", "particles", snowflake_frames, {"snow": _v(main="terrain/snow")}, 6, (3, 3),
        extra={"motion": {"vx": 4, "vy": 12, "sway": [4, 2.5]}},
        note="a big foreground flake; the snow sheet does the rest"),
    Amb("rain_splash", "Rain splash", "particles", rain_splash_frames, {"rain": _v(main="spray")}, 16, (4, 4),
        layer="ground", loop=False, note="random tile points while it rains, 4-8 per tile per second"),
    Amb("motes", "Dust motes", "particles", motes_frames, {"sun": _v(glow="mote"), "arcane": _v(glow="wisp")}, 8,
        (16, 24), blend="add", note="loops in place; set inside light shafts"),
    Amb("embers", "Rising embers", "particles", embers_frames, {"fire": _v(glow="ember")}, 12, (12, 47),
        blend="add", note="over fires and lava; anchor on the source"),
    Amb("bird", "Bird", "particles", bird_frames, {
        "swallow": _v(main="swallow"), "gull": _v(main="gull"), "crow": _v(main="crow")}, 10, (5, 2),
        layer="screen", extra={"motion": {"vx": 48, "vy": -3, "sway": [2, 1.5]}},
        note="seen from behind, so it reads at any heading; fly it any way across the screen"),
    Amb("flock", "Flock", "particles", flock_frames, {
        "swallow": _v(main="swallow"), "gull": _v(main="gull"), "crow": _v(main="crow")}, 10, (28, 14),
        layer="screen", extra={"motion": {"vx": 40, "vy": -2, "sway": [3, 3.0]}},
        note="crosses the screen above the map, point first (mirror to fly left)"),
    Amb("butterfly", "Butterfly", "particles", butterfly_frames, {
        "yellow": _v(main="wing_yellow", dark="cloth/charcoal"), "blue": _v(main="wing_blue", dark="cloth/charcoal"),
        "white": _v(main="wing_white", dark="cloth/charcoal"),
        "monarch": _v(main="wing_monarch", dark="cloth/charcoal")}, 12, (5, 4),
        extra={"motion": {"vx": 10, "vy": 0, "sway": [8, 1.3], "bob": [3, 0.4]}},
        note="flits over flowers; wander in short hops"),
    Amb("fish", "Fish jump", "particles", fish_frames, {
        "silver": _v(main="fish_silver", foam="foam"), "koi": _v(main="fish_koi", foam="foam")}, 12, (16, 24),
        loop=False, note="once, at a random water tile; faces right, mirror for the other way"),
    Amb("chimney_smoke", "Chimney smoke", "particles",
        lambda: smoke_column(16, 20, 52, puffs=10, base_r=1.3, grow=3.6, drift=8.0, seed=33),
        {"grey": _v(smoke="smoke"), "soot": _v(smoke="soot"), "steam": _v(smoke="steam")}, 8, (8, 51),
        note="anchor on the chimney top; leans right with the wind (mirror for a west wind)"),
    Amb("rain", "Rain sheet", "weather", rain_sheet, {k: {} for k in _RAIN}, 16, (0, 0), layer="screen",
        kind="overlay", rgba=True, extra={"tile": [OVERLAY, OVERLAY]},
        note="tile across the screen; the fall is in the frames (no scroll); pair with rain_splash"),
    Amb("snow", "Snow sheet", "weather", snow_sheet, {k: {} for k in _SNOW}, 10, (0, 0), layer="screen",
        kind="overlay", rgba=True, extra={"tile": [OVERLAY, OVERLAY]}, note="tile across the screen"),
    Amb("fog", "Fog band", "weather", fog_band, {k: {} for k in _FOG}, 6, (0, 32), layer="screen", kind="overlay",
        rgba=True, extra={"tile": [OVERLAY, 0]},
        note="tiles horizontally only and drifts right by itself; lay bands low over water, in hollows or below "
             "the map's edge"),
    Amb("light_shafts", "Light shafts", "weather", light_shafts, {k: {} for k in _RAYS}, 4, (0, 0),
        layer="screen", blend="add", kind="overlay", rgba=True,
        note="god rays from the top-left; place a few, overlapping, near the top of the view; add motes inside"),
]


# ----------------------------------------------------------------------------------------------------------- build
GROUPS = {
    "props": ("Animated props", "Drop-in replacements for static props: same frame size and anchor, built from the "
                                "static drawing, so the swap is invisible except for the motion."),
    "terrain": ("Terrain animations", "Waterfall faces, foam and ripple decals, lava and swamp bubbles."),
    "interactive": ("Interactive objects", "Objects with states: play a state's frames, hold its last frame."),
    "particles": ("Ambient particles", "Small sprites the game spawns and moves; the manifest gives motion hints."),
    "weather": ("Weather and light", "Screen overlays that tile, and god rays. Alpha is the layer's opacity."),
}


def frames_of(a: Amb, vname: str) -> list[Image.Image]:
    if a.rgba:
        return a.make(vname)
    canv = a.make()
    pal = palette(canv[0].slots, a.variants[vname])
    return [c.render(pal) for c in canv]


def _render_all() -> dict[str, dict[str, list[Image.Image]]]:
    out: dict[str, dict[str, list[Image.Image]]] = {}
    for a in CATALOGUE:
        if a.rgba:
            out[a.id] = {v: a.make(v) for v in a.variants}
            continue
        canv = a.make()
        out[a.id] = {v: [c.render(palette(c.slots, spec)) for c in canv] for v, spec in a.variants.items()}
    return out


def build() -> None:
    """content/tactics/base/ambient: one horizontal strip per asset and variant, plus a manifest; and the previews."""
    out = PACK / "ambient"
    shutil.rmtree(out, ignore_errors=True)
    rendered = _render_all()
    entries = {}
    for a in CATALOGUE:
        files = {}
        for vname, imgs in rendered[a.id].items():
            w, h = imgs[0].size
            sheet = Image.new("RGBA", (w * len(imgs), h))
            for k, im in enumerate(imgs):
                sheet.paste(im, (k * w, 0))
            rel = f"{a.group}/{a.id}_{vname}.png"
            save(sheet, out / rel)
            files[vname] = rel
        w, h = next(iter(rendered[a.id].values()))[0].size
        e = {"name": a.name, "group": a.group, "kind": a.kind, "frame": [w, h],
             "frames": len(next(iter(rendered[a.id].values()))), "fps": a.fps, "loop": a.loop,
             "anchor": list(a.anchor or (w // 2, h - 1)), "layer": a.layer, "blend": a.blend,
             "variants": files, "default": next(iter(files))}
        if a.replaces:
            e["replaces"] = a.replaces
        if a.states:
            e["states"] = a.states
        e.update(a.extra)
        if a.note:
            e["note"] = a.note
        entries[a.id] = e
    write_json({
        "layout": "each file is one horizontal strip of frames, left to right, all the same size",
        "layer": {"ground": "on the floor under units (decals, ripples, pools of light)",
                  "over": "a world sprite, depth-sorted with units and props at its anchor",
                  "screen": "a screen-space overlay above the map (weather, fog, god rays)"},
        "blend": {"normal": "ordinary alpha blending", "add": "additive: src.rgb * src.a added to the scene"},
        "kind": {"sprite": "a billboard at its anchor", "side": "a cliff-face texture, sampled like terrain sides",
                 "lip": "drawn over the top band of a side, like terrain lips", "decal": "32x32 in tile-local UV",
                 "overlay": "a screen layer; 'tile' gives its repeat in x and y (0 = doesn't repeat that way)"},
        "loop": "loop: true repeats every frame; false plays once (one-shots, transitions)",
        "replaces": "the static prop id this animation swaps in for; frame size and anchor match it exactly",
        "states": "interactive objects: frame lists per state; play the list, then hold its last frame",
        "motion": "particles: suggested drift in px/s at 1x (+y is down) and sway amplitude/period",
        "assets": entries,
    }, out / "manifest.json")
    _preview_sheets(rendered)
    _showcase(rendered)


def _preview_sheets(rendered: dict[str, dict[str, list[Image.Image]]]) -> None:
    for group, (title, sub) in GROUPS.items():
        items = [a for a in CATALOGUE if a.group == group]
        if not items:
            continue
        sections = []
        tiles = []
        for a in items:
            imgs = rendered[a.id][next(iter(a.variants))]
            loop = "loop" if a.loop else "once"
            info = f"{len(imgs)} f · {a.fps:g} fps · {loop} · {a.blend} · {a.layer}"
            if a.replaces:
                info += f" · replaces {a.replaces}"
            pic = _overlay_preview(a, imgs[0]) if a.kind == "overlay" else _strip_preview(a, imgs)
            tiles.append(Tile(pic, f"{a.name}  ({a.id})", info))
        widest = max(t.image.width for t in tiles)
        cols = max(1, min(len(tiles), 2000 // (widest * 3 + 40)))
        sections.append(contact_sheet("Frames", tiles, columns=cols, zoom=3, label_h=40, checker=True))
        vt = []
        for a in items:
            if len(a.variants) < 2:
                continue
            frames = next(iter(rendered[a.id].values()))
            busiest = max(range(len(frames)), key=lambda k: int((np.asarray(frames[k])[..., 3] > 0).sum()))
            first = [fr[busiest] for fr in rendered[a.id].values()]
            pic = (strip([_overlay_preview(a, im) for im in first], 1, gap=6) if a.kind == "overlay"
                   else _strip_preview(a, first, gap=4))
            vt.append(Tile(pic, a.name, " · ".join(rendered[a.id])))
        if vt:
            widest = max(t.image.width for t in vt)
            cols = max(1, min(len(vt), 2000 // (widest * 3 + 40)))
            sections.append(contact_sheet("Palette variants", vt, columns=cols, zoom=3, label_h=40, checker=True))
        save(stack(title, sections, sub), PREVIEWS / f"21-ambient-{group}.png")


def _composite(dst: np.ndarray, src: Image.Image, x: int, y: int, blend: str) -> None:
    """Draws `src` into a float RGB(A) array at (x, y), clipped, blended the way the manifest says."""
    s = np.asarray(src.convert("RGBA")).astype(np.float32)
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


def _backdrop(w: int, h: int, dusk: bool = False) -> np.ndarray:
    """A patch of grass and water to show overlays over (darkened for additive light)."""
    from . import terrain as TR
    green = next(t for t in TR.CATALOGUE if t.id == "grass").variants["green"]
    grass = np.asarray(TR.grass().render(TR.ramps(green))).astype(np.float32)
    water = np.asarray(TR.water_frames()[0].render(TR.ramps({"main": "terrain/water"}))).astype(np.float32)
    ys, xs = np.mgrid[0:h, 0:w]
    out = np.where(((xs + ys) % 256 < 150)[..., None], grass[ys % 128, xs % 128], water[ys % 128, xs % 128])
    if dusk:
        out[..., :3] *= np.array([0.45, 0.42, 0.55], np.float32)
    return out


def _overlay_preview(a: Amb, img: Image.Image) -> Image.Image:
    """An overlay over terrain, blended as the game will: tiling sheets 2x2, a fog band twice across, god rays
    once (they don't tile)."""
    tile = a.extra.get("tile", [0, 0])
    nx = 2 if tile[0] else 1
    ny = 2 if tile[1] else 1
    w, h = img.width * nx + (0 if tile[0] else 48), max(128, img.height * ny)
    dst = _backdrop(w, h, dusk=a.blend == "add")
    y0 = 0 if tile[1] else (h - img.height) // 2
    for ty in range(ny):
        for tx in range(nx):
            _composite(dst, img, tx * img.width, y0 + ty * img.height, a.blend)
    return Image.fromarray(dst.clip(0, 255).astype(np.uint8), "RGBA")


def _strip_preview(a: Amb, imgs: list[Image.Image], gap: int = 2) -> Image.Image:
    """Frames side by side; additive art is shown over a dark backing so it reads as light."""
    s = strip(imgs, 1, gap=gap)
    if a.blend != "add":
        return s
    bg = Image.new("RGBA", s.size, (10, 10, 18, 255))
    arr = np.asarray(bg).astype(np.float32)
    src = np.asarray(s).astype(np.float32)
    arr[..., :3] = np.minimum(255, arr[..., :3] + src[..., :3] * src[..., 3:4] / 255)
    return Image.fromarray(arr.astype(np.uint8), "RGBA")



# -------------------------------------------------------------------------------------------------------- showcase
GIF_FRAMES, GIF_MS = 48, 100
S = 2                                      # sprites are drawn at 2x texels, as in the dioramas

GLADE = [  # material, height: a plateau with a falls, a pool and river, a meadow camp and a ruined shrine gate
    ("GGGGGWWGGGRR", "666666666688"),
    ("GGGGGWWGGGRR", "666666666688"),
    ("gggswwwwsfff", "222222222333"),
    ("ggsswwwwsfff", "222222222333"),
    ("gggsswwssfff", "222222222333"),
    ("pppppbbppfff", "222222222333"),
    ("gggsswwsgggg", "222222222222"),
    ("ggggswwsgggg", "222222222222"),
    ("gggggswwsggg", "222222222222"),
    ("gggggswwsggg", "222222222222"),
]


def _pick(frames: list, fps: float, k: int):
    """The frame an animation shows on GIF frame k. A whole number of its loops fits the GIF (its speed is rounded
    to make that true), so the GIF loops without a seam."""
    n = len(frames)
    cycles = max(1, round(GIF_FRAMES * GIF_MS / 1000 * fps / n))
    return frames[(k * cycles * n // GIF_FRAMES) % n]


def _cue(k: int, beats: list[tuple[int, list[int]]], rest: int) -> int:
    """A scripted one-shot: `beats` are (start GIF frame, frame list); before the first and between beats the last
    shown frame holds (`rest` before anything has played)."""
    shown = rest
    for start, seq in beats:
        if k >= start:
            shown = seq[min(k - start, len(seq) - 1)]
    return shown


def _scene(k: int, R: dict):
    from arcpix import iso
    from . import dioramas as D
    M = D.Mat
    mats = {
        "G": M(("grass", "lush"), ("cliff_side", "grey"), ("grass_lip", "lush")),
        "g": M(("grass", "green"), ("dirt_side", "brown"), ("grass_lip", "green")),
        "R": M(("rock", "grey"), ("cliff_side", "grey")),
        "w": M(("water", "clear"), ("water", "clear"), sink=6, side_of_top=True),
        "W": M(("water", "clear"), ("water", "clear"), sink=6, side_of_top=True),
        "s": M(("sand", None), ("dirt_side", "brown"), sink=2),
        "f": M(("flagstone", "grey"), ("ashlar_side", "grey")),
        "p": M(("path", None), ("dirt_side", "brown")),
        "b": M(("planks", "oak"), ("plank_side", None)),
    }
    terrain = [t for t, _ in GLADE]
    water_frame = (k * 4 * 8 // GIF_FRAMES) % 8
    scene = D.build_scene(mats, terrain, [h for _, h in GLADE], frame=water_frame, seed=21,
                          decals={"g": (0.2, D.FLOWERS), "p": (0.3, [("pebbles_0", None), ("pebbles_1", None)])})
    arr = lambda im: np.asarray(im).astype(np.float32)                                    # noqa: E731
    fall = arr(_pick(R["waterfall"]["clear"], 10, k))
    lip = arr(_pick(R["waterfall_lip"]["clear"], 10, k))
    churn = arr(_pick(R["waterfall_foam"]["clear"], 8, k))
    shore = arr(_pick(R["shore_foam"]["clear"], 6, k))
    wet = lambda c: c in "wW"                                                                # noqa: E731
    for (i, j), cell in scene.cells.items():
        ch = terrain[j][i]
        if ch == "W":
            cell.side, cell.lip = fall, lip
        if not wet(ch):
            continue
        if ch == "w" and j > 0 and terrain[j - 1][i] == "W":
            cell.decal = churn
            continue
        layers = []
        for di, dj, orient in ((0, -1, lambda a: a), (0, 1, lambda a: a[::-1]),
                               (-1, 0, lambda a: a.transpose(1, 0, 2)),
                               (1, 0, lambda a: a.transpose(1, 0, 2)[:, ::-1])):
            ni, nj = i + di, j + dj
            if 0 <= ni < len(terrain[0]) and 0 <= nj < len(terrain) and terrain[nj][ni] not in "wWb":
                layers.append(orient(shore))
        if layers:
            dec = np.zeros((32, 32, 4), np.float32)
            for lay in layers:
                a = lay[..., 3:4] / 255
                dec[..., :3] = dec[..., :3] * (1 - a) + lay[..., :3] * a
                dec[..., 3:4] = np.maximum(dec[..., 3:4], lay[..., 3:4])
            cell.decal = np.ascontiguousarray(dec)

    def amb(aid: str, var: str, i: float, j: float, frame: int | None = None) -> iso.Sprite:
        a = next(x for x in CATALOGUE if x.id == aid)
        frames = R[aid][var]
        img = frames[frame] if frame is not None else _pick(frames, a.fps, k + (int(i * 7 + j * 3) % 8))
        return iso.Sprite(img, a.anchor or (img.width // 2, img.height - 1), i, j, shadow=False)

    ik = (k // 6) % 2
    door = _cue(k, [(6, [1, 2, 3, 4, 5]), (30, [4, 3, 2, 0])], 0)
    lever = _cue(k, [(12, [0, 1, 2, 3, 4]), (34, [4, 3, 2, 1, 0])], 0)
    trap = _cue(k, [(16, [1, 2, 3, 4, 5]), (37, [6, 7, 0])], 0)
    chest = _cue(k, [(20, [0, 1, 2, 3, 4, 5]), (42, [5, 3, 2, 0])], 0)
    sp = scene.sprites
    sp += [
        amb("tree_anim", "green", 0, 0), amb("pine_anim", "green", 4, 0), amb("tree_anim", "autumn", 2, 1),
        amb("pine_anim", "green", 8, 0), amb("bush_anim", "green", 1, 1), amb("tall_grass_anim", "green", 3, 1),
        amb("tall_grass_anim", "green", 7, 1), amb("crystals_anim", "violet", 11, 0), amb("portal", "arcane", 10, 1),
        amb("crystals_anim", "blue", 11, 1),
        amb("reeds_anim", "swamp", 3, 2), D.prop("campfire", 1, 3, frame=_pick(
            list(range(6)), 10, k)), D.unit("sefa", 2, 2, k=ik),
        amb("reeds_anim", "swamp", 8, 2), D.unit("fisher", 8, 3, mirror=True, k=ik), D.prop("tent", 0, 4),
        amb("reeds_anim", "swamp", 4, 4),
        D.unit("aldric", 2, 4, "back", mirror=True, k=1 - ik),
        D.prop("pillar", 9, 2, "grey"), D.prop("pillar", 11, 2, "grey"),
        amb("door", "oak", 10, 2.9999, door),
        amb("torch_anim", "fire", 9, 3), amb("spike_trap", "iron", 10, 3, trap), amb("torch_anim", "fire", 11, 3),
        amb("lever", "iron", 9, 4, lever), D.unit("class_rogue", 9, 5, "back", mirror=True, k=ik),
        amb("chest_anim", "oak", 11, 5, chest),
        amb("lamp_post_anim", "iron", 4, 5), D.unit("wren", 6, 5, "back", mirror=True, k=ik),
        amb("tree_anim", "light", 0, 6), amb("bush_anim", "light", 11, 8),
        amb("tall_grass_anim", "green", 2, 7), amb("ripple", "clear", 5, 7), D.unit("kara", 5, 7, k=1 - ik),
        amb("reeds_anim", "swamp", 7, 7), amb("tall_grass_anim", "dry", 8, 7), amb("rune_stone", "arcane", 10, 7),
        amb("tree_anim", "red", 0, 8), amb("bush_berry_anim", "green", 2, 9), amb("tall_grass_anim", "green", 4, 8),
        D.unit("tamsin", 9, 8, mirror=True, k=ik),
        amb("bush_anim", "autumn", 4, 9), D.prop("rock_mossy", 8, 9, "grey"),
    ]
    splash = _cue(k, [(28, list(range(7)))], -1) if 28 <= k < 35 else -1
    if splash >= 0:
        sp.append(amb("splash", "clear", 5, 7, splash))
    if 2 <= k < 14:
        sp.append(amb("fish", "silver", 6, 3, k - 2))
    return scene


def _screen_xy(scene, i: float, j: float, margin: int = 24) -> tuple[float, float]:
    """A tile centre's ground point on the rendered image (iso.render's projection)."""
    from arcpix import iso
    max_h = max(c.h for c in scene.cells.values()) * iso.STEP
    ox, oy = scene.d * iso.TILE + margin, max_h + margin + 50
    U, V = i * iso.TILE + iso.TILE / 2, j * iso.TILE + iso.TILE / 2
    return U - V + ox, (U + V) / 2 - scene.top_z(int(i), int(j)) + oy


def _put(dst: np.ndarray, img: Image.Image, x: float, y: float, anchor, blend: str = "normal", scale: int = S,
         opacity: float = 1.0) -> None:
    im = img.resize((img.width * scale, img.height * scale), Image.NEAREST)
    if opacity < 1:
        a = np.asarray(im).copy()
        a[..., 3] = (a[..., 3] * opacity).astype(np.uint8)
        im = Image.fromarray(a, "RGBA")
    _composite(dst, im, int(x - anchor[0] * scale), int(y - anchor[1] * scale), blend)


def _frame(k: int, R: dict, backdrop) -> Image.Image:
    """One frame of the showcase: the iso render, a dusk grade, then lights, particles and weather on top."""
    from arcpix import iso
    scene = _scene(k, R)
    base = iso.render(scene, backdrop=backdrop)
    dst = np.asarray(base).astype(np.float32)
    dst[..., :3] *= np.array([0.9, 0.82, 0.86], np.float32)                 # golden-hour grade before the lights
    H, W = dst.shape[:2]
    g = lambda i, j: _screen_xy(scene, i, j)                                # noqa: E731

    def amb(aid, var):
        a = next(x for x in CATALOGUE if x.id == aid)
        return a, R[aid][var]

    a_pool, pool = amb("light_pool", "fire")
    a_halo, halo_f = amb("halo", "fire")
    lights = [((9, 3), (0, -27), 1.0), ((11, 3), (0, -27), 1.0), ((4, 5), (0, -35), 1.0), ((1, 3), (0, -10), 0.45)]
    for n, ((i, j), (ox, oy), power) in enumerate(lights):
        x, y = g(i, j)
        _put(dst, _pick(pool, 8, k + n * 3), x, y, a_pool.anchor, "add", opacity=power)
        _put(dst, _pick(halo_f, 8, k + n * 5), x + ox * S, y + oy * S, a_halo.anchor, "add", opacity=power)
    x, y = g(10, 1)
    _put(dst, _pick(R["halo"]["arcane"], 8, k), x, y - 26 * S, (16, 16), "add", scale=3)
    _put(dst, _pick(R["light_pool"]["arcane"], 8, k), x, y, (24, 12), "add")
    x, y = g(10, 7)
    _put(dst, _pick(R["halo"]["arcane"], 8, k + 4), x, y - 18 * S, (16, 16), "add", opacity=0.6)
    x, y = g(1, 3)
    a, f = amb("campfire_smoke", "grey")
    _put(dst, _pick(f, a.fps, k), x, y - 14 * S, a.anchor)
    a, f = amb("embers", "fire")
    _put(dst, _pick(f, a.fps, k), x, y - 6 * S, a.anchor, "add")
    for (i, j), var, ph in (((7, 3), "gold", 0), ((2, 7), "gold", 5), ((10, 8), "wisp", 9), ((6, 8), "gold", 13),
                            ((1, 5), "gold", 3)):
        a, f = amb("fireflies", var)
        x, y = g(i, j)
        _put(dst, _pick(f, a.fps, k + ph), x, y - 14 * S, a.anchor, "add")
    # butterflies over the meadow: closed loops across the GIF
    for n, (var, (i, j), rx, ry) in enumerate((("yellow", (2, 6), 26, 10), ("blue", (1, 8), 18, 8),
                                                ("white", (9, 7), 20, 7))):
        a, f = amb("butterfly", var)
        t = 2 * math.pi * (k / GIF_FRAMES + n * 0.3)
        x, y = g(i, j)
        _put(dst, _pick(f, a.fps, k + n), x + rx * math.sin(t), y - 30 - ry * math.sin(2 * t) - 6 * abs(
            math.sin(5 * t)), a.anchor)
    # falling leaves from the crowns, each on its own phase of the loop
    for n, ((i, j), var, ph, drift) in enumerate((((2, 1), "autumn", 0.0, 22), ((2, 1), "autumn", 0.5, 14),
                                                  ((0, 6), "green", 0.25, 18), ((0, 8), "red", 0.6, 16),
                                                  ((0, 8), "red", 0.1, 24), ((0, 0), "green", 0.8, 20))):
        life = (k / GIF_FRAMES + ph) % 1.0
        if life > 0.8:
            continue
        a, f = amb("leaf", var)
        x, y = g(i, j)
        t = life / 0.8
        lx = x + drift * t + 8 * math.sin(2 * math.pi * t * 2)
        ly = y - 92 + 90 * t
        _put(dst, _pick(f, a.fps, k + n * 2), lx, ly, a.anchor)
    # god rays from the top-left, dust hanging in them, and mist lying on the pool
    a, f = amb("light_shafts", "warm")
    _put(dst, _pick(f, a.fps, k), 120, -10, (0, 0), "add")
    a, f = amb("motes", "sun")
    _put(dst, _pick(f, a.fps, k), 230, 170, a.anchor, "add")
    _put(dst, _pick(f, a.fps, k + 7), 330, 250, a.anchor, "add")
    a, f = amb("fog", "mist")
    fog = _pick(f, a.fps, k)
    for tx in range(-256, W, fog.width * S):                              # haze lapping at the island's foot
        _put(dst, fog, tx, H - 70, (0, 32), opacity=0.85)
    # a flock crossing the sky once per loop (off-screen at both ends, so it wraps)
    a, f = amb("flock", "swallow")
    t = k / GIF_FRAMES
    _put(dst, _pick(f, a.fps, k), -80 + (W + 160) * t, 70 - 20 * t, a.anchor)
    return Image.fromarray(dst.clip(0, 255).astype(np.uint8), "RGBA")


def _showcase(R: dict) -> None:
    """previews/anim-ambient.gif: a glade where everything moves, and a 2x still of its first frame."""
    from arcpix import iso
    from . import backgrounds as BG
    bd = lambda w, h: BG.compose("dusk", "hills", w, h, horizon=int(h * 0.48))       # noqa: E731
    frames = [_frame(k, R, bd) for k in range(GIF_FRAMES)]
    save(iso.finish(frames[22], zoom=2), PREVIEWS / "21-ambient-diorama.png")
    shown = [iso.finish(f, zoom=1) for f in frames]
    # one shared palette (from a few frames), so colours don't shimmer from frame to frame
    probe = Image.new("RGB", (shown[0].width, shown[0].height * 3))
    for n, k in enumerate((0, 18, 36)):
        probe.paste(shown[k], (0, n * shown[0].height))
    pal = probe.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    gif = [f.quantize(palette=pal, dither=Image.Dither.NONE) for f in shown]
    gif[0].save(PREVIEWS / "anim-ambient.gif", save_all=True, append_images=gif[1:], duration=GIF_MS, loop=0,
                optimize=True)
