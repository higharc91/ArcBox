"""Bestiary: new creatures drawn ahead of the game design, waiting for stat blocks.

Every creature in the beasts and bosses sets must have a monster stat block in the rules pack (a C# test checks it),
and adding stat blocks is a design decision, so creatures whose art comes first live here. When a creature gets its
stat block it can move to beasts or bosses; until then nothing in the rules pack refers to these keys.

The set follows the beasts contract exactly: 48x48 frames anchored at (24, 44), beasts.Pose and its animation table
(idle, walk, attack, special, hurt, down, same frame counts), front and back facings with the back drawn from behind,
and palette variants. The manifest still records a frame size per creature, as bosses does, so a later creature can
be bigger. Drawings reuse the beast rig where they can (the warg and the hookbeak are quadrupeds) and arcpix.form for
the rest.
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field, replace
from typing import Callable

import numpy as np
from PIL import Image

from arcpix.canvas import Canvas
from arcpix.form import (curve, dilate, ell, flat, fur, glow_fill, grid, nudge, paint, poly, recolour, rng, shift,
                         tube, tufts)
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, strip
from . import beasts as B
from .beasts import GY, Pose, _lerp

W = H = 48
ANCHOR = B.ANCHOR
SLOTS = B.SLOTS
FX_SLOTS = {"fx"}
PREFIX = "22b"                          # previews sit beside the bosses' (22-*)

RAMPS = {
    "warg": ramp("#4a4038", shift=0.05, spread=1.15), "warg_ash": ramp("#6e6a66", shift=0.05),
    "warg_belly": ramp("#8a7a62", shift=0.05),
    "hook_brown": ramp("#6e5034", shift=0.06), "hook_snow": ramp("#d8d4cc", shift=0.05, spread=0.65),
    "hook_disc": ramp("#d6c49a", shift=0.05, spread=0.8), "hook_disc_snow": ramp("#f0ece4", shift=0.04, spread=0.5),
    "beak": ramp("#c9a24a", shift=0.06, spread=0.9), "beak_dark": ramp("#5a5450", shift=0.05),
    "chitin": ramp("#3c3a2c", shift=0.06, spread=1.2), "chitin_sand": ramp("#b08a4a", shift=0.06),
    "chitin_red": ramp("#8a3022", shift=0.07), "sting": ramp("#d8c070", shift=0.06, spread=0.8),
    "feather": ramp("#7a5a48", shift=0.06), "feather_storm": ramp("#4e5a72", shift=0.05, spread=1.1),
    "harpy_hair": ramp("#3a2a30", shift=0.05, spread=1.2), "harpy_hair_storm": ramp("#c8d0dc", shift=0.04),
    "wisp": ramp("#8fe8d8", shift=0.05, spread=0.8), "wisp_core": ramp("#effff8", shift=0.03, spread=0.4),
    "wisp_red": ramp("#ff9a6a", shift=0.06, spread=0.8), "wisp_red_core": ramp("#fff0d8", shift=0.03, spread=0.4),
    "gargoyle": ramp("#7c7a84", shift=0.05, spread=1.0), "gargoyle_dark": ramp("#4a4652", shift=0.05, spread=1.2),
    "crab": ramp("#b8452e", shift=0.07), "crab_deep": ramp("#3e5a7a", shift=0.06),
    "crab_belly": ramp("#e0c8a0", shift=0.05, spread=0.8), "barnacle": ramp("#c8c0a8", shift=0.05, spread=0.8),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    if fam == "bestiary":
        return RAMPS[name]
    if fam == "beast":
        return B.BEAST_RAMPS[name]
    return FAMILIES[fam][name]


# ---------------------------------------------------------------------------------------------- quadrupeds
WARG = B.Quad(8, (7.5, 7), (6, 5.6), 5, 19, (2.6, 1.5), (5.2, 4.4), (5.8, 3.0), 3.2, "pointed", "bushy", shift=-3,
              saddle=True, mane=2.6, hump=1.5, belly=2)
HOOKBEAK = B.Quad(7, (9, 8.4), (8, 8), 7, 18, (3.6, 2.7), (6.4, 6), (0, 0), 2.0, "pointed", "stub", shift=-4,
                  hump=2.5, belly=0, head_y=0.4, face="beak", mane=2.0)


def warg(direction: str, p: Pose, form: str = "") -> Canvas:
    """A warg: a wolf grown heavy in the shoulder and long in the jaw, with a scruffy mane and a dark saddle. The
    `rider` form carries a goblin on its back, spear levelled, who bobs a beat behind the warg and stabs on the
    attack."""
    c = B._quad_back(WARG, p, "", "warg") if direction == "back" else B._quad_front(WARG, p, "", "warg")
    if form == "rider":
        _rider(c, p, direction == "back")
    return c


def _rider(c: Canvas, p: Pose, back: bool) -> None:
    """A goblin astride the warg: a hunched little body, one leg down the flank, a big-eared head, a hide shirt and
    a spear. The rider lags the mount by half a pixel and leans into the attack."""
    q = WARG
    if p.fall >= 0.6:                                        # thrown off when the warg goes down
        x, y = (14, GY - 3) if not back else (34, GY - 3)
        paint(c, ell(c, x, y, 4, 2.2), "cloth", base=2.6, depth=1)
        paint(c, ell(c, x + 4, y - 1, 2.4, 2), "skin", base=2.8, depth=1)
        c.line(x - 8, GY - 1, x + 6, GY - 2, "metal", 2)
        return
    lag = (B.POSES["idle"][(p.k + 5) % 6].bob if p.anim == "idle" else p.bob * 0.5)
    lean = p.lunge * 0.25 + (1.5 if p.anim == "attack" and p.k in (2, 3, 4) else 0)
    if not back:
        cx = ANCHOR[0] + q.shift + p.lunge * 0.6 + 1 + lean
        by = GY - q.height + p.bob - p.air + p.crouch * 2.5 - q.chest[1] * 0.85 + lag
        paint(c, tube(c, [(cx, by - 1), (cx + 1.5, by + 4), (cx, by + 7)], [1.6, 1.4, 1.2]), "cloth", base=3.0,
              depth=1)                                       # the near leg down the warg's flank
        c.rect(cx - 1, by + 7, cx + 1, by + 8, "cloth", 1)
        body = ell(c, cx, by - 4, 3.2, 4)
        paint(c, body, "cloth", base=3.0, depth=2)
        hx, hy = cx + 2 + lean * 0.3, by - 10
        for far, ex in ((True, hx - 3), (False, hx - 2)):
            c.polygon([(ex, hy - 0.5), (ex - 4.5, hy - 2.5 - (1 if far else 0)), (ex, hy + 1.5)], "skin",
                      2 if far else 3)
        paint(c, ell(c, hx, hy, 2.8, 2.6), "skin", base=3.1, depth=1)
        c.polygon([(hx + 2, hy - 0.5), (hx + 4.5, hy + 0.5), (hx + 2, hy + 1.2)], "skin", 3)     # the long nose
        c.px(hx + 1, hy - 0.8, "eyes", 5)
        c.line(hx - 2.5, hy - 2.5, hx + 2, hy - 2.8, "cloth", 1)                                 # a hide cap
        stab = 4 if p.anim == "attack" and p.k in (2, 3, 4) else 0
        sx, sy = cx + 2 + stab, by - 5
        c.line(sx - 12, sy + 3, sx + 9, sy - 1, "metal", 2)                                     # spear shaft
        c.polygon([(sx + 9, sy - 2.5), (sx + 13, sy - 1.5), (sx + 9, sy + 0.5)], "metal", 4)
        c.px(sx + 10, sy - 1.5, "metal", 5)
        paint(c, ell(c, sx + 1, sy + 1, 1.3, 1.3), "skin", base=3.0, depth=1)                   # the fist
    else:
        cx = ANCHOR[0] + 3 - p.lunge * 0.5 - q.length * 0.6 - lean * 0.5
        by = GY - q.height + 2 + p.bob - p.air - q.length * 0.3 - q.haunch[1] * 0.6 + lag
        c.line(cx - 10, by - 9, cx + 8, by - 2, "metal", 2)
        c.polygon([(cx - 10, by - 11), (cx - 13, by - 10.5), (cx - 10.5, by - 8)], "metal", 4)
        for side in (-1, 1):
            paint(c, tube(c, [(cx + side * 2, by - 1), (cx + side * 4, by + 4), (cx + side * 4, by + 7)],
                          [1.5, 1.3, 1.2]), "cloth", base=2.8, depth=1)
        paint(c, ell(c, cx, by - 4, 3.4, 4), "cloth", base=3.0, depth=2)
        hx, hy = cx - 1, by - 10
        for side in (-1, 1):
            c.polygon([(hx + side * 2, hy - 0.5), (hx + side * 6.5, hy - 2), (hx + side * 2, hy + 1.5)], "skin",
                      3 if side < 0 else 2)
        paint(c, ell(c, hx, hy, 2.7, 2.6), "cloth", base=2.8, depth=1)                         # the cap from behind


def hookbeak(direction: str, p: Pose, form: str = "") -> Canvas:
    """A hookbeak: a bear's bulk under a raptor's head, a feathered ruff, a hooked beak and ear tufts. Its own design
    (no borrowed owlbear); it bites with the beak and rears to swipe for the special."""
    if direction == "back":
        return B._quad_back(HOOKBEAK, p, "", "hookbeak")
    c = B._quad_front(HOOKBEAK, p, "", "hookbeak")
    return c


# ---------------------------------------------------------------------------------------------- scorpion
def scorpion(direction: str, p: Pose, form: str = "") -> Canvas:
    """A giant scorpion: a low plated body on eight legs, two great pincers carried forward that open and snap on
    the attack, and a segmented tail arched over the back, its stinger swaying; the special drives the sting down
    over its own head. Seen from behind, the tail rises nearest the camera."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    sgn = -1 if back else 1
    cx = ANCHOR[0] + sgn * p.lunge * 0.5 - (2 if not back else -2)
    by = GY - 7 + p.bob - p.air + p.crouch * 1.5
    by = _lerp(by, GY - 4, p.fall)
    sting = p.anim == "special" and p.k in (2, 3, 4, 5)
    # legs, four a side, in an alternating gait
    for side in (-1, 1):
        for i in range(4):
            sw, lift = B._spider_gait(p, i, side)
            hx = cx + sgn * (2 - i * 2.6)
            fx = hx + sgn * (6 - i * 4) + side * 1.5 * (1 if back else 0) + sw * sgn
            fy = GY - (1 if side < 0 else 0) - lift
            kx, ky = hx + sgn * (4 - i * 2.6), by - 5 - (1 if side < 0 else 0)
            if p.fall:
                kx, ky, fx, fy = hx + sgn, by - 6, hx + sgn * 2, by - 3
            if back:
                hx, kx, fx = hx + side * 2, kx + side * 5, fx + side * 9
            base = 1.9 if side < 0 else 2.9
            paint(c, tube(c, [(hx, by), (kx, ky), (fx, fy)], [1.3, 1.1, 0.7]), "fur", base=base, depth=1, k=1.1)
    # tail behind (front view) / over everything (back view)
    tail = _scorpion_tail(cx - sgn * 8, by - 2, p, sgn, sting)

    def draw_tail():
        for i, ((x0, y0), (x1, y1)) in enumerate(zip(tail, tail[1:])):
            r = 2.6 - i * 0.25
            paint(c, ell(c, (x0 + x1) / 2, (y0 + y1) / 2, r + 0.4, r), "fur", base=3.1, depth=2)
        tx, ty = tail[-1]
        ang = math.atan2(tail[-1][1] - tail[-2][1], tail[-1][0] - tail[-2][0])
        bulb = ell(c, tx, ty, 2.2, 2.2)
        paint(c, bulb, "fur2", base=3.2, depth=1)
        bx, by_ = tx + math.cos(ang + 0.9) * 4.5, ty + math.sin(ang + 0.9) * 4.5
        c.line(tx + math.cos(ang + 0.6) * 1.5, ty + math.sin(ang + 0.6) * 1.5, bx, by_, "horn", 4)
        c.px(bx, by_, "horn", 5)
        if form == "venom" or sting:
            c.px(bx + 1, by_ + 1, "glow", 5)
    if not back:
        draw_tail()
    # body: plated segments from the tail root to the head
    segs = [(cx - sgn * 7, by - 0.5, 4, 3.6), (cx - sgn * 3.5, by - 1, 4.4, 4), (cx, by - 1, 4.6, 4.2),
            (cx + sgn * 4, by - 0.8, 4.4, 3.8)]
    if back:
        segs = segs[::-1]
    for x, y, rx, ry in segs:
        paint(c, ell(c, x, y, rx, ry), "fur", base=3.0, depth=3)
    for x, y, rx, ry in segs[:-1] if not back else segs[1:]:
        c.line(x + sgn * rx * 0.7, y - ry * 0.8, x + sgn * rx * 0.85, y + ry * 0.4, "fur", 1)   # plate seams
    # pincers on their arms, opening with gape
    for side in (-1, 1):
        far = side < 0
        shx = cx + sgn * 5
        reach = 8 + max(0.0, p.lunge) * 0.6 + (3 if p.anim == "attack" and p.k in (2, 3, 4) else 0)
        clx = shx + sgn * reach + (side * 4 if back else (-2.5 if far else 0))
        cly = by - 4 - (3 if far else 0) - (2 if p.anim == "attack" and p.k in (0, 1) else 0)
        if p.fall:
            cly = by
        if back:
            clx, cly = cx - 7 + side * 7 - p.lunge * 0.3, by - 5 - (2 if side < 0 else 0)
        base = 2.2 if (far and not back) else 3.1
        paint(c, tube(c, [(shx, by - 1), ((shx + clx) / 2, cly - 2), (clx - sgn * 2, cly)], [1.6, 1.4, 1.6]),
              "fur", base=base, depth=1)
        claw = ell(c, clx, cly, 3.4, 2.4)
        paint(c, claw, "fur2" if not far else "fur", base=base + 0.2, depth=2)
        op = 0.35 + p.gape * 0.9                                     # the pincer's fingers, a V that snaps shut
        tip = clx + sgn * 2.6
        for upper in (True, False):
            ang = (-op if upper else op * 0.6)
            ln = 5.5 if upper else 4.5
            ex_, ey_ = tip + sgn * math.cos(ang) * ln, cly - (1 if upper else -1) * 0.6 + math.sin(ang) * ln
            finger = tube(c, [(tip, cly - (0.8 if upper else -0.8)), (ex_, ey_)], [1.4 if upper else 1.1, 0.5])
            paint(c, finger, "fur2" if not far else "fur", base=base + (0.4 if upper else -0.2), depth=1)
    if not back:
        hx = cx + sgn * 7
        for ex in (0, 1.5):
            c.px(hx + ex, by - 3.5, "eyes", 2 if (p.hurt or p.down or p.blink) else 5)
    else:
        draw_tail()
    return c


def _scorpion_tail(x: float, y: float, p: Pose, sgn: int, sting: bool) -> list[tuple[float, float]]:
    """The tail's joints: five segments arching up and over towards the head; the curl tightens and the tip
    swings with p.tail, and the sting drives it forward."""
    a = math.radians(-150 if sgn > 0 else -30)
    bend = math.radians(32 + p.tail * 4)
    ln = 4.2
    if sting:
        bend = math.radians(44)
        ln = 4.8
    if p.anim == "special" and p.k in (0, 1):
        bend = math.radians(24)                         # drawn back before the strike
    if p.fall:
        bend = math.radians(_lerp(32, 8, p.fall))
        a = math.radians(-165 if sgn > 0 else -15)
    pts = [(x, y)]
    for i in range(5):
        x += math.cos(a) * ln
        y += math.sin(a) * ln
        pts.append((x, y))
        a += bend * sgn
    return pts


# ----------------------------------------------------------------------------------------------- harpy
def harpy(direction: str, p: Pose, form: str = "") -> Canvas:
    """A harpy: a woman's torso and wild hair on a bird's wings and taloned legs, hovering above its tile. The
    wings beat through every loop (the body rising on the down-stroke), the hair and tail feathers trail a beat
    behind, the attack is a talon swoop and the special a shriek that rolls out in rings."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    sgn = -1 if back else 1
    beat = p.wing
    if p.anim in ("idle", "walk"):
        beat = math.cos(p.phase * math.tau * (1 if p.anim == "idle" else 2))
    lag = math.cos(p.phase * math.tau * (1 if p.anim == "idle" else 2) - 1.2) if p.anim in ("idle", "walk") else \
        beat * 0.5
    swoop = p.anim == "attack" and p.k in (2, 3, 4)
    cx = ANCHOR[0] + sgn * p.lunge * 0.5
    cy = 22 - beat * 1.5 + (5 if swoop else 0) + p.bob
    if p.fall:
        beat = _lerp(beat, -0.4, p.fall)
        cy = _lerp(cy, GY - 6, p.fall)
    # wings first (both behind the body in the front view; spread either side from behind)
    for side in (-1, 1):
        far = side < 0 and not back
        _feather_wing(c, cx + side * 2.5, cy - 4, side, beat, lag, 2.3 if far else 3.1)
    # tail feathers and taloned legs
    tx = cx - sgn * 2
    for i, dx in enumerate((-2, 0, 2)):
        c.polygon([(tx + dx - 1, cy + 6), (tx + dx + 1, cy + 6), (tx + dx * 1.6 - sgn * lag * 1.5, cy + 12 + i % 2)],
                  "fur", 2 + i % 2)
    for side in (-1, 1):
        lx = cx + side * 1.8 + (sgn * 3 if swoop else 0)
        ky = cy + 9
        fy = cy + 14 + (2 if swoop else 0)
        fx = lx + sgn * (3 if swoop else 0.5) - lag * 0.5
        if p.fall:
            fy = min(fy, GY - 1)
        paint(c, tube(c, [(lx, cy + 5), (lx + sgn * 0.5, ky), (fx, fy)], [1.8, 1.0, 0.8]), "fur",
              base=2.4 if side < 0 else 3.0, depth=1)
        for t in (-1, 0, 1):
            c.line(fx, fy, fx + t * 1.2 + sgn * 0.6, fy + 1.5, "horn", 4)
    # torso, arms becoming the wings' leading edge, head and hair
    torso = ell(c, cx, cy + 1, 3.2, 4.5)
    paint(c, torso, "skin", base=3.1, depth=2)
    feathers = ell(c, cx, cy + 5, 3.6, 2.4)
    paint(c, feathers, "fur", base=3.0, depth=1)
    tufts(c, feathers, "fur", cx, every=2, edge="bottom")
    hx, hy = cx + sgn * 0.5, cy - 6
    hair = tube(c, [(hx, hy - 2), (hx - sgn * 3, hy), (hx - sgn * (5 + lag), hy + 4 + lag), (hx - sgn * (6 + lag * 1.6),
                                                                                           hy + 8)], [3.2, 3, 2.4, 1.2])
    paint(c, hair, "fur2", base=3.0, depth=2)
    if not back:
        paint(c, ell(c, hx + 0.5, hy, 2.6, 3), "skin", base=3.2, depth=1)
        paint(c, ell(c, hx - 0.5, hy - 2.2, 2.8, 1.6), "fur2", base=3.2, depth=1)                  # fringe
        if p.hurt or p.down or p.blink:
            c.line(hx + 0.5, hy, hx + 2, hy, "fur2", 1)
        else:
            c.px(hx + 1, hy - 0.3, "eyes", 5)
            c.px(hx + 2, hy - 0.3, "eyes", 1)
        shriek = p.anim == "special" and p.k in (2, 3, 4, 5)
        if shriek or p.gape > 0.4:
            c.rect(hx + 1, hy + 1.5, hx + 2, hy + 2.5, "mouth", 1)
        else:
            c.px(hx + 1.5, hy + 1.7, "mouth", 2)
        if shriek:
            t = (p.k - 2) / 3
            for kk in range(3):
                rad = 4 + kk * 4 + t * 5
                for a in np.linspace(-0.8, 0.8, int(rad * 2.2)):
                    c.px(hx + 3 + math.cos(a) * rad, hy + 1 + math.sin(a) * rad, "fx", max(2, 5 - kk))
    else:
        paint(c, ell(c, hx, hy, 2.8, 3), "fur2", base=3.0, depth=1)
    if swoop and p.k in (2, 3):                                              # talon rake trail
        for kk in range(3):
            c.line(cx + sgn * (1 + kk), cy + 10 + kk, cx + sgn * (8 + kk), cy + 16 + kk, "fx", 5 - kk)
    return c


def _feather_wing(c: Canvas, sx: float, sy: float, side: int, beat: float, lag: float, base: float) -> None:
    """A feathered wing: a covert mass along the arm and a fan of primaries whose tips lag the stroke."""
    a = beat * 0.9
    L = 8
    wx, wy = sx + side * L * math.cos(a), sy - L * math.sin(a)
    cov = tube(c, [(sx, sy), (wx, wy)], [2.4, 1.8])
    tips = []
    for i in range(5):
        ang = a * 0.5 - 0.25 - i * 0.32 - lag * 0.2 * (i / 4)
        ln = 12 - abs(i - 1) * 1.2
        tips.append((wx + side * ln * math.cos(ang), wy - ln * math.sin(ang)))
    for i, t in enumerate(tips):                                             # each primary a long narrow feather
        paint(c, tube(c, [(wx - side * i * 0.8, wy + i * 0.8), t], [1.6, 0.7]), "wing", base=base - 0.3 * (i % 2),
              depth=1, k=1.0)
    paint(c, cov, "wing", base=base + 0.3, depth=1)
    c.line(sx, sy, wx, wy, "wing", min(5, int(base) + 1))


# ------------------------------------------------------------------------------------------- will-o'-wisp
def wisp(direction: str, p: Pose, form: str = "") -> Canvas:
    """A will-o'-wisp: a bright core in a flickering flame that streams back the way it came, three motes circling
    it, hovering at head height. It never holds still: the flame tongues rise and peel off frame to frame. The
    attack is a darting flare, the special a beguiling spiral of lights, and down gutters it out to an ember."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    sgn = -1 if back else 1
    t = p.phase if p.anim in ("idle", "walk") else p.k / 7
    cx = ANCHOR[0] + sgn * p.lunge * 0.7 + math.sin(t * math.tau) * 1.5
    cy = 20 + math.sin(t * math.tau * 2) * 1.5 + p.bob
    dim = 1 - (0.35 if p.hurt else 0) - p.fall * 0.7
    if p.fall >= 1:
        cy = GY - 3 + (p.k - 2) * 0.5
    r = 5.0 * (0.6 + 0.4 * dim) + (1.5 if p.anim == "attack" and p.k in (2, 3) else 0)
    seed = B.ORDER.index(p.anim) * 20 + p.k
    rr = rng(31, seed)
    # the trail: soft puffs streaming behind, drifting up, dithered thin at the end
    for i in range(5):
        d = 3 + i * 2.6 + (3 if p.anim == "walk" or p.lunge > 2 else 0)
        px = cx - sgn * d
        py = cy + 1 - i * 0.8 + math.sin(t * math.tau * 2 + i) * 1.2
        m = ell(c, px, py, max(0.8, r * 0.8 - i * 0.7), max(0.8, r * 0.7 - i * 0.6))
        yy, xx = np.mgrid[0:H, 0:W]
        if i >= 2:
            m &= (xx + yy + i) % 2 == 0
        flat(c, m & (c.slot == 0), "fx", max(1, 4 - i))
    # flame body: tongues rising from the core, a fresh set each frame
    body = ell(c, cx, cy, r, r * 1.05)
    for i in range(5):
        a = -math.pi / 2 + (i - 2) * 0.45 + rr.uniform(-0.15, 0.15)
        h = r * (1.0 + rr.uniform(0.3, 1.0)) * dim
        bx_, by_ = cx + math.cos(a) * r * 0.5, cy + math.sin(a) * r * 0.5
        body |= poly(c, [(bx_ - 1.5, by_ + 1), (bx_ + 1.5, by_ + 1), (bx_ + math.cos(a) * h - sgn * 1.2,
                                                                       by_ + math.sin(a) * h)])
    glow_fill(c, body, "glow", lo=2, hi=5, step=1.0)
    core = ell(c, cx + 0.5, cy + 0.5, r * 0.5, r * 0.5)
    flat(c, core, "fur2", 4)
    flat(c, core & ell(c, cx, cy, r * 0.3, r * 0.3), "fur2", 5)
    # motes circling, and the special's spiral
    n = 3 if p.anim != "special" else 3 + p.k * 2
    for i in range(n):
        a = t * math.tau * 1.5 + i * math.tau / 3 + (i // 3) * 0.6
        d = r + 4 + (i // 3) * 3 + (p.k * 1.5 if p.anim == "special" else 0)
        mx, my = cx + math.cos(a) * d, cy + math.sin(a) * d * 0.55
        if p.fall >= 1:
            break
        c.px(mx, my, "fur2", 5)
        c.px(mx - sgn, my, "glow", 3)
    if p.anim == "attack" and p.k in (2, 3):                              # the flare darting out
        for i in range(4):
            c.line(cx + sgn * (r + 1 + i * 2), cy - 1 + i % 2, cx + sgn * (r + 3 + i * 2), cy - 1 + i % 2, "glow",
                   5 - i % 2)
    return c


# -------------------------------------------------------------------------------------------- gargoyle
def gargoyle(direction: str, p: Pose, form: str = "") -> Canvas:
    """A gargoyle: a crouched stone fiend on its haunches, knuckles on the ground, horns, a jutting jaw and bat
    wings folded high behind the shoulders. It lopes on all fours, rakes with a claw (one smear frame), spreads its
    wings and leaps for the special, chips when struck and crumbles to rubble when it falls."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    sgn = -1 if back else 1
    if p.fall >= 1:
        r = rng(57)
        for (x, y, rx, ry) in ((18, 41, 6, 3.5), (27, 40, 7, 4.5), (35, 42, 4, 2.5), (23, 36, 4, 3), (30, 35, 3, 2.5)):
            paint(c, ell(c, ANCHOR[0] + sgn * (x - 24), y + 1, rx, ry), "fur", base=2.6 + r.uniform(0, 0.6), depth=2)
        c.polygon([(24 + sgn * 6, 33), (24 + sgn * 10, 28), (24 + sgn * 9, 33)], "horn", 3)       # a horn on top
        c.px(24 + sgn * 2, 37, "glow", 2)
        return c
    cx = ANCHOR[0] + sgn * (p.lunge * 0.5 - 1)
    by = GY - 13 + p.bob - p.air + p.crouch * 2 + p.fall * 4
    spread = max(0.0, p.wing) if p.anim == "special" else 0.1 + 0.08 * p.wing
    lift = 0.9 if p.anim == "special" and p.k in (2, 3) else spread
    # wings behind
    for side in ((-1, 1) if back else (-1,)):
        _stone_wing(c, cx - sgn * 3 + side * (3 if back else 0), by - 8, lift, side if back else -sgn, 2.4)
    # haunch and hind leg, crouched
    hip = (cx - sgn * 4, by + 2)
    foot = (cx - sgn * 2 + p.legs[1] * 0.4 * sgn, GY - p.lift[1] - p.air)
    paint(c, ell(c, *hip, 4.5, 4.2), "fur", base=2.9, depth=3)
    paint(c, tube(c, [hip, (cx + sgn * 1, by + 6), (foot[0] - sgn, foot[1] - 3), foot], [3.4, 2.4, 1.8, 1.6]), "fur",
          base=3.0, depth=2)
    for t in (0, 1.5):
        c.px(foot[0] + sgn * (1 + t), foot[1], "horn", 4)
    # torso hunched forward
    chest = (cx + sgn * 3 + p.tilt * sgn, by - 4)
    torso = ell(c, *chest, 5.5, 6) | tube(c, [hip, chest], [4.5, 5])
    paint(c, torso, "fur", base=3.0, depth=4)
    if not back:
        recolour(c, ell(c, chest[0] + 1.5, chest[1] + 2, 2.8, 3.5) & torso, "fur2", 0)        # a carved chest
    # arms: knuckling the ground, the near one swings on the attack
    swipe = p.anim == "attack"
    for side, idx in ((-1, 2), (1, 0)):
        sh = (chest[0] + sgn * 1 + side * (0 if not back else 4), chest[1] - 2)
        hand = (chest[0] + sgn * 5 + p.legs[idx] * 0.5 * sgn, GY - p.lift[idx] - p.air)
        if swipe and side > 0:
            hand = [(chest[0] - sgn * 2, chest[1] - 9), (chest[0] - sgn * 3, chest[1] - 11), (chest[0] + sgn * 9,
                                                                                               chest[1] + 2),
                    (chest[0] + sgn * 9, chest[1] + 5), (chest[0] + sgn * 8, chest[1] + 6),
                    (chest[0] + sgn * 6, chest[1] + 7), (chest[0] + sgn * 5, GY - 1)][p.k]
        base = 2.3 if side < 0 and not back else 3.1
        elbow = ((sh[0] + hand[0]) / 2 - sgn * 1.5, (sh[1] + hand[1]) / 2)
        paint(c, tube(c, [sh, elbow, hand], [2.6, 2.0, 1.8]), "fur", base=base, depth=2)
        for t in (-1, 0, 1):
            c.line(hand[0] + t, hand[1], hand[0] + t + sgn, hand[1] + 1.5, "horn", 4 if side > 0 else 2)
    if swipe and p.k == 2:                                                   # the one smear frame of the rake
        for kk in range(3):
            pts = curve((chest[0] - sgn * 3, chest[1] - 11 + kk), (chest[0] + sgn * 10, chest[1] - 6 + kk),
                        (chest[0] + sgn * 9, chest[1] + 3 + kk), 6)
            for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
                c.line(x0, y0, x1, y1, "fx", 5 - kk)
    # head: low between the shoulders, horns, a jutting jaw
    hx, hy = chest[0] + sgn * (3 + p.head[0] * 0.5), chest[1] - 6 + p.head[1] * 0.6
    for side in (-1, 1):
        pts = curve((hx - sgn * 1 + side * 1.5, hy - 2.5), (hx - sgn * 4 + side * 2.5, hy - 6),
                    (hx - sgn * 6 + side * 2, hy - 5), 4)
        paint(c, tube(c, pts, [1.3, 1.1, 0.9, 0.6, 0.5]), "horn", base=3.4 if side > 0 else 2.6, depth=1)
    paint(c, ell(c, hx, hy, 3.4, 3.2), "fur", base=3.2, depth=2)
    if not back:
        jo = p.gape * 2
        paint(c, poly(c, [(hx, hy + 1), (hx + sgn * 4, hy + 1.5 + jo), (hx + sgn * 3.5, hy + 3 + jo),
                          (hx - sgn, hy + 3)]), "fur", base=2.8, depth=1)
        c.px(hx + sgn * 3, hy + 1 + jo, "horn", 5)                        # an underbite fang
        lit = 2 if p.hurt else (5 if (p.k % 3 != 2 or p.anim != "idle") else 4)
        c.px(hx + sgn * 1, hy - 0.8, "glow", lit)
        c.px(hx + sgn * 2.2, hy - 0.8, "glow", lit - 1)
        c.line(hx - sgn * 0.5, hy - 2, hx + sgn * 3, hy - 1.5, "fur", 1)   # the brow
    # the near wing, folded over the shoulder (front view)
    if not back:
        _stone_wing(c, cx - sgn * 3, by - 8, lift, -sgn, 3.1)
    fur(c, "fur", cx, by, seed=41, cell=(5, 4), length=2, slope=(1, 0), fill=0.4)        # chisel marks
    if p.hurt and p.k == 0:
        for dx, dy in ((7, -10), (9, -7), (6, -13)):
            c.px(cx + sgn * dx, by + dy, "fur", 4)
            c.px(cx + sgn * dx + 1, by + dy, "fur", 2)
    if p.fall:
        nudge(c, (c.slot > 0) & ((grid(c)[0].astype(int) + grid(c)[1].astype(int)) % 5 == 0), -1)  # cracking
    return c


def _stone_wing(c: Canvas, x: float, y: float, spread: float, side: int, base: float) -> None:
    a = math.radians(78 + spread * 25)
    L = 10 + spread * 5
    wx, wy = x + side * math.cos(a) * L, y - math.sin(a) * L
    tips = [(wx + side * (4 + spread * 9), wy + 4 + spread), (wx + side * (4 + spread * 7), wy + 10),
            (wx + side * (2 + spread * 4), wy + 14)]
    paint(c, poly(c, [(x, y + 2), (wx, wy)] + tips + [(x + side, y + 6)]), "wing", base=base, depth=2, k=1.2)
    c.thick_line(x, y, wx, wy, 2, "fur", int(base))
    for t in tips[:2]:
        c.line(wx, wy, *t, "fur", int(base) - 1)
    c.px(wx, wy - 1, "horn", 4)


# ------------------------------------------------------------------------------------------------- crab
def crab(direction: str, p: Pose, form: str = "") -> Canvas:
    """A giant crab: a broad shell seen three-quarters on, eyes on stalks, one great crushing claw and one smaller,
    four legs a side that scuttle in a ripple. It snaps the big claw on the attack, rears both claws and slams for
    the special, and flips onto its back when it goes down. Barnacles crust the deep-sea variant."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    sgn = -1 if back else 1
    cx = ANCHOR[0] + sgn * (p.lunge * 0.4 - 3)
    by = GY - 9 + p.bob - p.air + p.crouch * 1.5
    flip = p.fall >= 1
    by = _lerp(by, GY - 6, min(1.0, p.fall))
    # legs: four a side, splayed out sideways, rippling
    for side in (-1, 1):
        for i in range(4):
            ph = p.phase * math.tau * 2 + i * 1.3 + (0 if side > 0 else math.pi)
            lift = max(0.0, math.sin(ph)) * 2 if p.anim == "walk" else 0
            hx = cx + side * 6 + side * i * 0.5
            hy = by + 1 + i * 0.5 - (1 if side < 0 else 0)
            kx, ky = hx + side * (5 + i * 0.8), by - 3 + i * 0.6 - lift
            fx, fy = hx + side * (8 + i * 1.6), GY - (2 if side < 0 else 0) - i * 0.4 - lift
            if flip:
                ky, fy = by - 6 - i, by - 9 - i * 1.5 + math.sin(i + p.k) * 1.5
            base = 1.8 if (side < 0) != back else 2.4
            paint(c, tube(c, [(hx, hy), (kx, ky), (fx, fy)], [1.5, 1.2, 0.7]), "fur", base=base, depth=1, k=1.1)
    # the shell
    shell = ell(c, cx, by - 1, 10.5, 6.5)
    if flip:
        paint(c, shell, "fur2", base=3.0, depth=4)
        for i in range(3):
            c.line(cx - 5 + i * 5, by - 3, cx - 4 + i * 5, by + 2, "fur2", 1)
        return c
    paint(c, shell, "fur", base=3.0, depth=5)
    rim = shell & ~shift(shell, 0, -2)
    recolour(c, rim, "fur2", -1)                                                 # the pale underside lip
    for i in range(-2, 3):                                                       # serrated front edge
        c.polygon([(cx + i * 4 - 1, by - 6.5), (cx + i * 4 + 1, by - 6.5), (cx + i * 4, by - 8)], "fur", 3)
    c.line(cx - 4, by - 3, cx + 4, by - 3, "fur", 2)                             # a ridge across the carapace
    c.px(cx - 4, by - 4, "fur", 4)
    if form == "barnacles":
        for x, y in ((cx - 6, by - 3), (cx + 3, by - 4), (cx + 6, by - 1), (cx - 2, by + 1)):
            paint(c, ell(c, x, y, 1.4, 1.2), "horn", base=3.4, depth=1)
            c.px(x, y, "horn", 1)
    # claws: arms out front, the big claw on the near side
    snap = p.anim == "attack" and p.k in (2, 3, 4)
    rear = p.anim == "special" and p.k in (1, 2, 3)
    for side, big in ((-1, False), (1, True)):
        sx = cx + sgn * 6 + side * 2
        ex = cx + sgn * 10 + side * 1
        clx = cx + sgn * ((13 if big else 10) + (3 if snap and big else 0))
        cly = by + (2 if big else -3) - (8 if rear else 0) - (3 if snap and big and p.k == 2 else 0)
        if back:
            clx, cly = cx + side * 10, by - 5 - (6 if rear else 0)
        size = 5.2 if big else 3.2
        base = 3.4 if (side > 0) != back else 2.7
        paint(c, tube(c, [(sx, by), (ex, by - 2), (clx - sgn * size * 0.6, cly)], [1.8, 1.6, 1.8]), "fur", base=base,
              depth=1)
        claw = ell(c, clx, cly, size, size * 0.75)
        paint(c, claw, "fur", base=base + 0.2, depth=2)
        op = (p.gape * 3 + (2 if rear else 0)) * (1 if big else 0.6)
        tip = clx + sgn * size
        c.polygon([(tip - sgn, cly - 1.5), (tip + sgn * 3, cly - 2.5 - op), (tip + sgn * 1, cly - 0.5)], "fur",
                  base + 1 if base < 3 else 4)
        c.polygon([(tip - sgn, cly + 0.5), (tip + sgn * 2.5, cly + 1.5 + op * 0.5), (tip, cly + 1.5)], "fur2", 3)
        recolour(c, claw & ~shift(claw, 0, -2), "fur2", -1)                     # the pale underside of the claw
    # eyes on stalks
    if not back:
        for ex, h in ((cx + sgn * 1, 5.5), (cx + sgn * 4, 5)):
            sway = math.sin(p.phase * math.tau + ex) * 0.6
            c.line(ex, by - 6, ex + sway, by - 6 - h, "fur", 2)
            c.px(ex + sway, by - 6 - h, "eyes", 1 if (p.hurt or p.blink) else 5)
            c.px(ex + sway + sgn, by - 6 - h, "eyes", 1)
    if p.anim == "special" and p.k in (4, 5):                                   # the slam
        for i in range(6):
            a = math.pi * (0.1 + i * 0.16)
            c.px(cx + math.cos(a) * 15, GY - 1 - math.sin(a) * 4, "fx", 4 - i % 2)
    return c


# ---------------------------------------------------------------------------------------------- catalogue
@dataclass
class Creature:
    id: str
    name: str
    draw: Callable[..., Canvas]
    variants: dict[str, dict[str, str]]
    tags: list[str] = field(default_factory=list)
    flying: bool = False
    forms: dict[str, str] = field(default_factory=dict)
    size: tuple[int, int] = (W, H)
    anchor: tuple[int, int] = ANCHOR
    note: str = ""


def _v(**slots: str) -> dict[str, str]:
    return slots


_WARG = dict(fur2="bestiary/warg_belly", eyes="eyes/amber", horn="beast/ivory", mouth="beast/mouth",
             skin="skin/goblin", cloth="leather/brown", metal="metal/iron")
CATALOGUE: list[Creature] = [
    Creature("warg", "Warg", warg, {
        "black": _v(fur="bestiary/warg", **_WARG), "ash": _v(fur="bestiary/warg_ash", **_WARG),
        "rider": _v(fur="bestiary/warg", **_WARG)}, ["beast", "mount"], forms={"rider": "rider"},
        note="the rider variant carries a goblin spearman; a game may treat it as one unit or split it on down"),
    Creature("hookbeak", "Hookbeak", hookbeak, {
        "brown": _v(fur="bestiary/hook_brown", fur2="bestiary/hook_disc", eyes="eyes/amber", horn="bestiary/beak",
                    mouth="beast/mouth"),
        "snow": _v(fur="bestiary/hook_snow", fur2="bestiary/hook_disc_snow", eyes="eyes/gold",
                   horn="bestiary/beak_dark", mouth="beast/mouth")}, ["beast", "large", "hybrid"]),
    Creature("scorpion", "Giant Scorpion", scorpion, {
        "sand": _v(fur="bestiary/chitin_sand", fur2="bestiary/chitin_sand", eyes="eyes/dark", horn="bestiary/sting",
                   glow="magic/fel"),
        "black": _v(fur="bestiary/chitin", fur2="bestiary/chitin", eyes="eyes/red", horn="bestiary/sting",
                    glow="magic/fel"),
        "ember": _v(fur="bestiary/chitin_red", fur2="bestiary/chitin", eyes="eyes/gold", horn="bestiary/sting",
                    glow="magic/fire")}, ["beast", "venom"], forms={"black": "venom"}),
    Creature("harpy", "Harpy", harpy, {
        "dusk": _v(skin="skin/tan", fur="bestiary/feather", fur2="bestiary/harpy_hair", wing="bestiary/feather",
                   eyes="eyes/gold", horn="beast/ivory", mouth="beast/mouth", fx="magic/wild"),
        "storm": _v(skin="skin/ash", fur="bestiary/feather_storm", fur2="bestiary/harpy_hair_storm",
                    wing="bestiary/feather_storm", eyes="eyes/blue", horn="beast/ivory", mouth="beast/mouth",
                    fx="magic/storm")}, ["monstrosity", "flying"], flying=True),
    Creature("wisp", "Will-o'-Wisp", wisp, {
        "marsh": _v(glow="bestiary/wisp", fur2="bestiary/wisp_core", fx="bestiary/wisp"),
        "ember": _v(glow="bestiary/wisp_red", fur2="bestiary/wisp_red_core", fx="bestiary/wisp_red")},
        ["fey", "incorporeal", "light"], flying=True,
        note="self-lit: worth an additive glow or a point light in the game"),
    Creature("gargoyle", "Gargoyle", gargoyle, {
        "granite": _v(fur="bestiary/gargoyle", fur2="terrain/stone", wing="bestiary/gargoyle_dark", horn="horn/bone",
                      glow="magic/fire", fx="beast/ivory"),
        "obsidian": _v(fur="bestiary/gargoyle_dark", fur2="bestiary/gargoyle", wing="bestiary/gargoyle_dark",
                       horn="horn/obsidian", glow="magic/arcane", fx="beast/ivory")}, ["construct", "flying"]),
    Creature("crab", "Giant Crab", crab, {
        "shore": _v(fur="bestiary/crab", fur2="bestiary/crab_belly", eyes="eyes/dark", horn="bestiary/barnacle",
                    fx="terrain/water"),
        "deep": _v(fur="bestiary/crab_deep", fur2="bestiary/crab_belly", eyes="eyes/gold", horn="bestiary/barnacle",
                   fx="terrain/water")}, ["beast", "aquatic"], forms={"deep": "barnacles"}),
]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {s: _ref(r) for s, r in variant.items()}
    for name in SLOTS.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


_CANVAS_CACHE: dict[tuple[str, str, str, int], Canvas] = {}
_FRAME_CACHE: dict[tuple[str, str], list[Image.Image]] = {}


def frame_poses():
    return B.frame_poses()


def canvases(m: Creature, form: str = "") -> list[Canvas]:
    out = []
    for d in ("front", "back"):
        for i, (_a, _k, pose) in enumerate(frame_poses()):
            key = (m.id, form, d, i)
            if key not in _CANVAS_CACHE:
                cv = m.draw(d, pose, form)
                cv.outline(skip=FX_SLOTS)
                _CANVAS_CACHE[key] = cv
            out.append(_CANVAS_CACHE[key])
    return out


def frames(m: Creature, variant: str) -> list[Image.Image]:
    key = (m.id, variant)
    if key not in _FRAME_CACHE:
        pal = ramps(m.variants[variant])
        _FRAME_CACHE[key] = [cv.render(pal) for cv in canvases(m, m.forms.get(variant, ""))]
    return _FRAME_CACHE[key]


def _sheet(m: Creature, fr: list[Image.Image]) -> Image.Image:
    n = len(fr) // 2
    sh = Image.new("RGBA", (m.size[0] * n, m.size[1] * 2))
    for i, im in enumerate(fr):
        sh.paste(im, ((i % n) * m.size[0], (i // n) * m.size[1]))
    return sh


def _anim_grid(fr: list[Image.Image]) -> Image.Image:
    return B.anim_grid(fr, B.anims_meta(), gap=3)


def build() -> None:
    out = PACK / "bestiary"
    shutil.rmtree(out, ignore_errors=True)
    n = len(frame_poses())
    anims = B.anims_meta()
    entries = {}
    for m in CATALOGUE:
        files = {}
        for v in m.variants:
            files[v] = f"{m.id}_{v}.png"
            save(_sheet(m, frames(m, v)), out / files[v])
        e = {"name": m.name, "frame": {"width": m.size[0], "height": m.size[1], "anchor": list(m.anchor)},
             "footprint": [1, 1], "flying": m.flying, "tags": m.tags, "variants": files,
             "status": "art waiting for a stat block"}
        if m.note:
            e["note"] = m.note
        entries[m.id] = e
    write_json({"status": "art waiting for stat blocks: no rules-pack monster refers to these yet. Give a creature "
                          "a stat block, then move it to beasts or bosses (a C# test requires every creature there "
                          "to have one).",
                "framesPerDirection": n, "directions": {"front": {"row": 0}, "back": {"row": 1}}, "anims": anims,
                "creatures": entries,
                "notes": {"sheet": "one sheet per creature and variant: row 0 front (facing down-right), row 1 back "
                                   "(drawn from behind, facing up-left); frame size per creature",
                          "contract": "the beasts animation table: idle 6 · walk 8 · attack 7 · special 7 · hurt 3 "
                                      "· down 4 (the last frame is the body)",
                          "flying": "drawn hovering above the anchor; the game should not snap it to the floor"}},
               out / "manifest.json")
    _previews(n, anims)


def _previews(n: int, anims: dict) -> None:
    tiles = []
    for m in CATALOGUE:
        for v in m.variants:
            fr = frames(m, v)
            tiles.append(Tile(strip([fr[0], fr[n]], 1, gap=4), m.name, f"{v} · front, back"))
    save(contact_sheet("Bestiary", tiles, columns=6, zoom=3,
                       subtitle="New creatures drawn ahead of their stat blocks (not in the rules pack yet). Same "
                                "contract as beasts: 48x48, anchor (24, 44), idle/walk/attack/special/hurt/down."),
         PREVIEWS / f"{PREFIX}-bestiary.png")
    rows = []
    for m in CATALOGUE:
        for v in [next(iter(m.variants))] + [v for v in m.forms if v != next(iter(m.variants))]:
            fr = frames(m, v)
            rows.append(Tile(_anim_grid(fr[:n]), f"{m.name} · {v}", "front"))
            rows.append(Tile(_anim_grid(fr[n:]), m.name, "back (drawn from behind)"))
    save(contact_sheet("Bestiary · animations", rows, columns=4, zoom=2,
                       subtitle="One animation per row: " + " · ".join(f"{a} {len(v['frames'])}"
                                                                       for a, v in anims.items())),
         PREVIEWS / f"{PREFIX}-bestiary-anims.png")
    _scale(n)
    _gif(n, anims)


def _scale(n: int) -> None:
    """The bestiary beside a human hero and two beasts on one ground line, 1:1."""
    from . import dioramas as D
    hero = D.unit("kara", 0, 0)
    fig = [(hero.image, hero.anchor)]
    wolf = next(b for b in B.CATALOGUE if b.id == "wolf")
    fig.append((B.sheet(wolf, "grey").frames[0], B.ANCHOR))
    for m in CATALOGUE:
        for v in [next(iter(m.variants))] + list(m.forms):
            fig.append((frames(m, v)[0], m.anchor))
    gap = 6
    wsum = sum(im.width for im, _a in fig) + gap * (len(fig) + 1)
    img = Image.new("RGBA", (wsum, 60))
    x = gap
    for im, (ax, ay) in fig:
        img.alpha_composite(im, (x, 52 - ay))
        x += im.width + gap
    img.alpha_composite(Image.new("RGBA", (wsum, 1), (90, 84, 110, 255)), (0, 53))
    save(contact_sheet("Bestiary · scale", [Tile(img, "One ground line, 1:1",
                                                 "hero · wolf · " + " · ".join(m.name for m in CATALOGUE))],
                       columns=1, zoom=4, subtitle="A human hero is about 24 px tall in a 32x40 frame."),
         PREVIEWS / f"{PREFIX}-bestiary-scale.png")


def _gif(n: int, anims: dict) -> None:
    from arcpix.anim import Actor, stage_gif
    actors = [Actor(m.name, v, frames(m, v), m.anchor)
              for m in CATALOGUE for v in [next(iter(m.variants))] + [v for v in m.forms
                                                                      if v != next(iter(m.variants))]]
    k = len(actors)

    def seq(name: str, back: bool = False) -> list[int]:
        return [f + (n if back else 0) for f in anims[name]["frames"]]

    beats = [("Idle", [seq("idle") * 3] * k, 160), ("Walk", [seq("walk") * 3] * k, 100),
             ("Walk · away", [seq("walk", True) * 3] * k, 100),
             ("Attack", [seq("idle")[:2] + seq("attack") + seq("attack")[-1:] * 3] * k, 85),
             ("Special", [seq("idle")[:2] + seq("special") + seq("special")[-1:] * 3] * k, 90),
             ("Hurt", [seq("hurt") * 2 + seq("idle")[:3]] * k, 100),
             ("Down", [seq("down") + seq("down")[-1:] * 8] * k, 110)]
    stage_gif(PREVIEWS / f"anim-bestiary.gif", "Bestiary (art waiting for stat blocks)", actors, beats, zoom=3,
              columns=5, cell_w=56, cell_h=56)
