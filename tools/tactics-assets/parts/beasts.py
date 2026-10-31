"""Beasts: monsters that don't fit the humanoid rig. Wolves, boars and bears share a quadruped rig; the drake,
spider, slime and bat have their own drawings. All use 48x48 frames anchored at (24, 44), the animation names
characters use where they overlap (idle, walk, attack, hurt, down) plus a beast `special` (pounce, charge, dive,
breath), and the same two facings (front, back; the game mirrors them), so the battle code treats every unit alike.

Bodies are masks lit as volumes (arcpix.form): a chest, a barrel and a haunch merged into one silhouette, legs as
two-bone limbs that keep their feet planted, and every part casting a 1 px shadow on what it overlaps, so the
masses separate without inner outlines. Texture comes last and in clusters: fur as short combed strokes and ragged
tufts along the belly, scales as staggered rows, both anchored to the body so they travel with it instead of
crawling. The back facing is drawn, not mirrored: the beast seen from behind and above, rump and tail towards the
camera and the head turned away, like the characters' back row.

Every species ships in palette variants (grey wolf / timber wolf / dire wolf ...); a few variants also change the
drawing (`forms`: the alpha wolf's mane and scar, the undead bear's bare ribs, crystal shards on a slime).
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field, replace
from typing import Callable

import numpy as np
from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.form import (curve, dilate, ell, flat, fur, glow_fill, grid, nudge, paint, poly, recolour, rng,
                         rot_ell, scale_rows, shift, tube, tufts)
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, stack, strip
from arcpix.sheet import Sheet

W = H = 48
ANCHOR = (24, 44)
GY = ANCHOR[1]
SLOTS = SlotTable()
for _s in ("fur", "fur2", "eyes", "horn", "mouth", "wing", "glow", "fx", "skin", "cloth", "metal"):
    SLOTS.id(_s)
FX_SLOTS = {"fx"}                        # light and afterimages: never outlined

ANIMS: dict[str, tuple[float, bool, int]] = {   # name -> (fps, loop, frames)
    "idle": (6.0, True, 6), "walk": (10.0, True, 8), "attack": (12.0, False, 7), "special": (11.0, False, 7),
    "hurt": (10.0, False, 3), "down": (8.0, False, 4),
}
ORDER = ["idle", "walk", "attack", "special", "hurt", "down"]

BEAST_RAMPS = {
    "wolf_grey": ramp("#8a8f9c", shift=0.05), "wolf_timber": ramp("#8a6a4a", shift=0.06),
    "wolf_dire": ramp("#3f3a48", shift=0.05, spread=1.2), "wolf_frost": ramp("#cfd9e6", shift=0.05, spread=0.7),
    "wolf_alpha": ramp("#5a5e6c", shift=0.05, spread=1.15), "mane_alpha": ramp("#c9ccd4", shift=0.05, spread=0.8),
    "boar": ramp("#6a4a36", shift=0.06), "boar_dark": ramp("#3f3036", shift=0.05, spread=1.2),
    "bear_brown": ramp("#7a5232", shift=0.06), "bear_black": ramp("#36323c", shift=0.05, spread=1.3),
    "bear_polar": ramp("#e6e2d6", shift=0.05, spread=0.6),
    "bear_rot": ramp("#5c5a48", shift=0.05, spread=1.1), "rot_flesh": ramp("#7a4a52", shift=0.06),
    "belly_light": ramp("#d9cbb0", shift=0.05, spread=0.8), "belly_grey": ramp("#b5b8c2", shift=0.05),
    "spider": ramp("#3a3340", shift=0.05, spread=1.3), "spider_red": ramp("#7a2a30", shift=0.07),
    "spider_phase": ramp("#3c3f78", shift=0.06, spread=1.2), "phase_glow": ramp("#8fd8ff", shift=0.05, spread=0.8),
    "slime_green": ramp("#6fd36a", shift=0.06, spread=0.9), "slime_blue": ramp("#5fa8e8", shift=0.05, spread=0.9),
    "slime_magma": ramp("#ff7b2b", shift=0.08, spread=0.8), "slime_crystal": ramp("#9fe6f0", shift=0.05, spread=0.85),
    "crystal": ramp("#c8b4ff", shift=0.05, spread=0.8),
    "bat": ramp("#4a3a52", shift=0.06, spread=1.1), "bat_wing": ramp("#6a4a62", shift=0.06),
    "bat_vampire": ramp("#2c2230", shift=0.05, spread=1.3), "bat_vampire_wing": ramp("#6e2232", shift=0.07),
    "ivory": ramp("#e3d8bd", shift=0.05, spread=0.85), "mouth": ramp("#8a2a36", shift=0.06),
    "drake_wing_green": ramp("#2f6450", shift=0.07), "drake_wing_blue": ramp("#2c4683", shift=0.06),
    # Druid wild shapes: the beast in leaf-and-bark spirit colours, so allies read as allies
    "wild_fur": ramp("#6f9a52", shift=0.08), "wild_belly": ramp("#c9d98a", shift=0.06, spread=0.8),
    "wild_bark": ramp("#7a5a3a", shift=0.06),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    return BEAST_RAMPS[name] if fam == "beast" else FAMILIES[fam][name]


# ------------------------------------------------------------------------------------------------------ poses
@dataclass
class Pose:
    """One frame's posture. Every beast reads the same table, so a pack plays in step; each drawing decides what a
    value means for its body (a bite, a wingbeat, a jelly slam)."""
    bob: float = 0                      # body drop (+ down)
    lunge: float = 0                    # body shift forward (screen-right in the front view)
    legs: tuple[float, float, float, float] = (0, 0, 0, 0)   # near-front, near-back, far-front, far-back: x swing
    lift: tuple[float, float, float, float] = (0, 0, 0, 0)   # foot lift
    head: tuple[float, float] = (0, 0)
    jaw: bool = False                   # mouth open (attack)
    down: bool = False
    hurt: bool = False
    blink: bool = False
    anim: str = "idle"
    k: int = 0                          # frame within the animation
    phase: float = 0                    # 0..1 around a looping animation
    wing: float = 0                     # -1 down-stroke .. 1 raised
    tilt: float = 0                     # pitch: + chest down and forward
    fall: float = 0                     # down progress 0..1
    tail: float = 0                     # tail swing, lagging the body (-1 .. 1)
    ear: float = 0                      # 0 pricked .. 1 pinned back
    crouch: float = 0                   # 0..1 coiled low before a spring
    air: float = 0                      # px off the ground (leaps)
    gape: float = 0                     # jaw opening 0..1


def _gait(ph: float, stride: float = 3.5, step: float = 2.5) -> tuple[tuple, tuple]:
    """A four-beat lateral walk: each foot spends 60% of the cycle planted and sliding back, 40% swinging forward
    in an arc. Order: near-hind, near-fore, far-hind, far-fore, a quarter cycle apart."""
    legs, lift = [], []
    for off in (0.25, 0.0, 0.75, 0.5):                      # near-front, near-back, far-front, far-back
        a = (ph + off) % 1.0
        if a < 0.6:
            legs.append(stride * (1 - 2 * a / 0.6))
            lift.append(0.0)
        else:
            t = (a - 0.6) / 0.4
            legs.append(stride * (-1 + 2 * t))
            lift.append(math.sin(math.pi * t) * step)
    return tuple(legs), tuple(lift)


def _poses() -> dict[str, list[Pose]]:
    out: dict[str, list[Pose]] = {}
    out["idle"] = []
    for k in range(6):                   # breathe in (1 px rise), hold, out; the head and tail follow a beat late
        ph = k / 6
        out["idle"].append(Pose(anim="idle", k=k, phase=ph, bob=(0, 0, -1, -1, 0, 0)[k],
                                head=(0, (0, 0, 0, -1, -1, 0)[k]),
                                tail=math.sin(ph * math.tau - 0.9), ear=0.5 if k == 4 else 0, blink=k == 5,
                                wing=(0.2, 0.6, 1, 0.4, -0.5, -0.8)[k]))
    out["walk"] = []
    for k in range(8):
        ph = k / 8
        legs, lift = _gait(ph)
        out["walk"].append(Pose(anim="walk", k=k, phase=ph, legs=legs, lift=lift,
                                bob=round(0.5 - 0.5 * math.cos(ph * 2 * math.tau)), head=(0, round(0.5 - 0.5 * math.cos(
                                    ph * 2 * math.tau - 1.2))), tail=math.sin(ph * math.tau - 1.4),
                                wing=math.cos(ph * 2 * math.tau)))
    P = Pose
    # anticipation (coil back, ears pinned), the lunge, the bite held on impact, then recovery
    out["attack"] = [
        P(anim="attack", k=0, lunge=-2, bob=1, tilt=0.4, head=(-1, 2), ear=0.6, crouch=0.4, tail=0.3,
          legs=(-1, -1, -1, -1), wing=0.6),
        P(anim="attack", k=1, lunge=-3, bob=2, tilt=0.6, head=(-2, 3), ear=1, crouch=0.8, tail=0.8,
          legs=(-2, -1, -2, -1), wing=1),
        P(anim="attack", k=2, lunge=4, bob=-1, tilt=-0.2, head=(3, -1), ear=1, gape=1, jaw=True, tail=0.4,
          legs=(4, -2, 3, -2), lift=(2, 0, 2, 0), wing=-0.4),
        P(anim="attack", k=3, lunge=6, tilt=0.3, head=(5, 2), ear=0.8, gape=0.25, jaw=True, tail=-0.6,
          legs=(5, -1, 4, -1), wing=-1),
        P(anim="attack", k=4, lunge=6, tilt=0.4, head=(5, 3), ear=0.6, gape=0.15, jaw=True, tail=-1,
          legs=(5, -1, 4, -1), wing=-0.8),
        P(anim="attack", k=5, lunge=3, tilt=0.1, head=(2, 1), ear=0.3, tail=-0.4, legs=(2, 0, 2, 0), wing=-0.2),
        P(anim="attack", k=6, lunge=1, head=(1, 0), tail=0.1, legs=(1, 0, 1, 0), wing=0.2)]
    # special: crouch, coil, launch, airborne, land on the target, hold, recover (pounce, charge, dive, breath)
    out["special"] = [
        P(anim="special", k=0, lunge=-2, bob=2, tilt=0.5, head=(-1, 2), crouch=0.6, ear=0.7, tail=0.4, wing=0.8,
          legs=(-1, -1, -1, -1)),
        P(anim="special", k=1, lunge=-3, bob=3, tilt=0.7, head=(-2, 3), crouch=1, ear=1, tail=0.9, wing=1,
          legs=(-2, -2, -2, -2)),
        P(anim="special", k=2, lunge=3, air=4, tilt=-0.4, head=(2, -2), ear=1, gape=0.6, jaw=True, tail=0.6,
          wing=0.6, legs=(5, -5, 4, -5), lift=(3, 1, 3, 1)),
        P(anim="special", k=3, lunge=6, air=6, tilt=-0.1, head=(3, -1), ear=1, gape=1, jaw=True, tail=-0.2,
          wing=-0.6, legs=(6, -4, 5, -4), lift=(3, 2, 3, 2)),
        P(anim="special", k=4, lunge=8, tilt=0.5, head=(5, 3), ear=0.8, gape=0.3, jaw=True, tail=-1, wing=-1,
          legs=(5, 1, 4, 0)),
        P(anim="special", k=5, lunge=8, bob=1, tilt=0.5, head=(5, 3), ear=0.6, gape=0.2, jaw=True, tail=-0.6,
          wing=-0.4, legs=(5, 1, 4, 0)),
        P(anim="special", k=6, lunge=3, tilt=0.1, head=(1, 1), tail=0.2, legs=(2, 0, 1, 0))]
    out["hurt"] = [
        P(anim="hurt", k=0, lunge=-3, bob=1, tilt=-0.4, head=(-2, -2), hurt=True, ear=1, tail=0.8,
          legs=(-1, 1, -1, 1), wing=0.8),
        P(anim="hurt", k=1, lunge=-2, tilt=-0.2, head=(-1, -1), hurt=True, ear=0.8, tail=0.3, wing=0.2),
        P(anim="hurt", k=2, lunge=-1, head=(0, 0), ear=0.4, tail=-0.2, wing=0)]
    out["down"] = [
        P(anim="down", k=0, fall=0.25, bob=2, tilt=0.5, head=(1, 3), hurt=True, ear=1, tail=0.4, wing=-0.4),
        P(anim="down", k=1, fall=0.65, head=(2, 4), hurt=True, ear=1, tail=-0.4, wing=-0.8),
        P(anim="down", k=2, fall=1.0, bob=-1, head=(2, 5), down=True, ear=1, tail=0.2, wing=-1),
        P(anim="down", k=3, fall=1.0, head=(2, 6), down=True, ear=1, tail=0, wing=-1)]
    for a in ORDER:
        assert len(out[a]) == ANIMS[a][2], a
    return out


POSES = _poses()


def frame_poses() -> list[tuple[str, int, Pose]]:
    return [(a, i, p) for a in ORDER for i, p in enumerate(POSES[a])]


# ------------------------------------------------------------------------------------------------ limb helpers
def _ik(a: tuple[float, float], b: tuple[float, float], l1: float, l2: float, bend: float) -> tuple[float, float]:
    """The middle joint of a two-bone limb from a to b, bent to the side `bend` (+1 bends towards +x)."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    d = max(1e-3, math.hypot(dx, dy))
    dd = min(d, l1 + l2 - 0.01)
    x = (l1 * l1 - l2 * l2 + dd * dd) / (2 * dd)
    h = math.sqrt(max(0.0, l1 * l1 - x * x))
    ux, uy = dx / d, dy / d
    mx, my = a[0] + ux * x, a[1] + uy * x
    px, py = -uy, ux                                         # perpendicular
    if px * bend < 0:
        px, py = -px, -py
    return mx + px * h, my + py * h


def _lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


# --------------------------------------------------------------------------------------------- quadruped rig
@dataclass(frozen=True)
class Quad:
    """A quadruped's proportions, in pixels. The body is three masses (chest, barrel, haunch) the legs hang from."""
    length: float                       # chest centre to haunch centre, halved
    chest: tuple[float, float]          # rx, ry
    haunch: tuple[float, float]
    barrel: float                       # ry of the waist joining them
    height: float                       # body centre above the ground
    leg: tuple[float, float]            # upper and lower limb radius
    head: tuple[float, float]           # skull rx, ry
    snout: tuple[float, float]          # muzzle length, depth
    neck: float                         # how far the head stands out in front of the chest
    ears: str                           # pointed | round | small
    tail: str                           # bushy | stub | tuft
    shift: float = 0                    # body centre's x offset from the anchor (keeps long heads in frame)
    tusks: bool = False
    ridge: bool = False                 # bristle crest along the back
    hump: float = 0                     # shoulder hump (bears)
    mane: float = 0                     # neck ruff
    saddle: bool = False                # darker band along the back (wolves)
    belly: int = 3                      # rows of pale underside (0 = none)
    head_y: float = 0.8                 # how high the head is carried (in chest radii above the chest centre)
    face: str = "muzzle"                # muzzle | beak (a hooked beak in a feathered facial disc)


SPECIES: dict[str, Quad] = {
    "wolf": Quad(7.5, (6.5, 5.6), (5.5, 5), 4.2, 17, (2.2, 1.2), (4.6, 4), (5.5, 2.6), 4.5, "pointed", "bushy",
                 shift=-4, saddle=True, mane=1.2),
    "wolf_alpha": Quad(8.5, (7.5, 6.6), (6, 5.6), 4.8, 19, (2.6, 1.4), (5, 4.4), (6, 2.9), 4.5, "pointed", "bushy",
                       shift=-5, saddle=True, mane=3.2),
    "boar": Quad(6, (8, 8), (7, 7), 7, 13, (2.6, 1.7), (5.2, 5), (4.5, 3.6), 0.5, "small", "tuft", shift=-3,
                 tusks=True, ridge=True, belly=0, head_y=0.05),
    "bear": Quad(7, (9, 8.2), (8, 8), 7, 18, (3.6, 2.7), (5.6, 5), (4.2, 3.2), 3.5, "round", "stub", shift=-4,
                 hump=3.5, belly=0, head_y=0.3),
}


def quad(species: str) -> Callable[..., Canvas]:
    def draw(direction: str, p: Pose, form: str = "") -> Canvas:
        q = SPECIES.get(f"{species}_{form}", SPECIES[species])
        if direction == "back":
            return _quad_back(q, p, form, species)
        return _quad_front(q, p, form, species)
    return draw


def _legs_front(c: Canvas, q: Quad, p: Pose, joint, idx: int, front: bool, far: bool, gx: float) -> None:
    """One leg in the profile view: shoulder -> elbow (bent back) -> wrist, or hip -> knee (forward) -> hock, then
    the foot planted at `gx` on the ground (or tucked up in a leap, or thrown out when it falls)."""
    jx, jy = joint
    total = q.height - 0.4 * (q.chest[1] if front else q.haunch[1])
    l1, l2 = total * 0.55, total * 0.55
    fx = gx + p.legs[idx] + (-1.2 if far else 0)
    fy = GY - p.lift[idx] - p.air - (1 if far else 0)
    if p.fall:
        lx, ly = jx + (total * 0.85 if front else total * 0.6) + (0 if not far else -2), min(GY - 1, jy + 3)
        fx, fy = _lerp(fx, lx, p.fall), _lerp(fy, ly, p.fall)
    base = 2.1 if far else 3.0
    if front:
        mid = _ik((jx, jy), (fx, fy - 2.5), l1, l2 * 0.85, -1)
        pts = [(jx, jy), mid, (fx, fy - 2.5), (fx + 0.8, fy - 0.5)]
        rad = [q.leg[0], q.leg[1] + 0.15, q.leg[1], q.leg[1] * 0.9]
    else:
        hock = (fx - 1.5, fy - total * 0.3)
        mid = _ik((jx, jy), hock, l1 * 0.85, l2 * 0.75, 1)
        pts = [(jx, jy), mid, hock, (fx, fy - 0.5)]
        rad = [q.leg[0] + 0.8, q.leg[0] * 0.7, q.leg[1], q.leg[1] * 0.9]
    paint(c, tube(c, pts, rad), "fur", base=base, depth=2, k=1.4)
    paw = ell(c, fx + 1.2, fy - 0.6, q.leg[1] + 0.7, 1.2)
    paint(c, paw, "fur", base=base - 0.4, depth=1, k=1.0)
    c.px(fx + q.leg[1] + 1.5, fy - 0.5, "horn", 2 if far else 3)          # a claw tip


def _quad_front(q: Quad, p: Pose, form: str, species: str) -> Canvas:
    c = Canvas(W, H, SLOTS)
    cx = ANCHOR[0] + q.shift + p.lunge * 0.6
    by = GY - q.height + p.bob - p.air + p.crouch * 2.5
    lie = GY - q.haunch[1] * 0.85 - 1
    by = _lerp(by, lie, p.fall)
    flatten = 1 - 0.12 * p.fall
    chest = (cx + q.length, by + p.tilt * 1.5)
    haunch = (cx - q.length, by - p.tilt * 1.0 - (p.crouch * 1.0 if p.anim == "special" else 0))
    if p.fall:
        chest = (chest[0], _lerp(chest[1], lie + 0.5, p.fall))
        haunch = (haunch[0], _lerp(haunch[1], lie, p.fall))
    shoulder = (chest[0] + 0.5, chest[1] + q.chest[1] * 0.35)
    hip = (haunch[0] - 0.5, haunch[1] + q.haunch[1] * 0.2)
    gx_front, gx_hind = shoulder[0] + 1, hip[0] + 1
    # far legs
    _legs_front(c, q, p, (shoulder[0] - 1.5, shoulder[1] - 1), 2, True, True, gx_front - 1.5)
    _legs_front(c, q, p, (hip[0] - 1.5, hip[1] - 1), 3, False, True, gx_hind - 1.5)
    # tail, behind the haunch
    _quad_tail(c, q, p, (haunch[0] - q.haunch[0] * 0.75, haunch[1] - q.haunch[1] * 0.55), -1)
    # body
    body = ell(c, chest[0], chest[1], q.chest[0], q.chest[1] * flatten) | \
        ell(c, haunch[0], haunch[1], q.haunch[0], q.haunch[1] * flatten) | \
        poly(c, [(haunch[0], haunch[1] - q.haunch[1] * 0.9), (chest[0], chest[1] - q.chest[1] * 0.92),
                 (chest[0], chest[1] + q.chest[1] * 0.85), (haunch[0] + q.haunch[0] * 0.3, haunch[1] + q.barrel * 0.6)])
    if q.hump:
        body |= ell(c, chest[0] - 1, chest[1] - q.chest[1] * 0.75, q.chest[0] * 0.75, q.hump)
    paint(c, body, "fur", base=3.0, depth=6)
    if q.belly:
        und = body & ~shift(body, 0, -q.belly) & (grid(c)[1] > haunch[0] - 1)
        recolour(c, und, "fur2", 0)
    if q.saddle:
        top = body & ~shift(body, 0, 3) & (grid(c)[1] > haunch[0] - 3) & (grid(c)[1] < chest[0] + 2)
        nudge(c, top & c.mask("fur"), -1)
    # near legs, with the shoulder and thigh masses over the body
    _legs_front(c, q, p, shoulder, 0, True, False, gx_front)
    _legs_front(c, q, p, hip, 1, False, False, gx_hind)
    paint(c, ell(c, hip[0] + 0.5, hip[1] - 1, q.haunch[0] * 0.7, q.haunch[1] * 0.85 * flatten), "fur", base=3.1,
          depth=4)
    paint(c, ell(c, shoulder[0] - 0.5, shoulder[1] - 2, q.chest[0] * 0.55, q.chest[1] * 0.7 * flatten), "fur",
          base=3.1, depth=3)
    if form == "undead":                                  # bare ribs showing through a rotted flank
        _ribs(c, cx + 1, by + 1, q)
    # head and neck
    hx = chest[0] + q.chest[0] * 0.55 + q.neck + p.head[0]
    hy = chest[1] - q.chest[1] * q.head_y + p.head[1] + p.tilt * 2
    if p.fall:
        hx, hy = _lerp(hx, hx + 2, p.fall), _lerp(hy, GY - q.head[1] - 0.5, p.fall)
    neck = tube(c, [(chest[0] + q.chest[0] * 0.2, chest[1] - q.chest[1] * 0.25), (hx - 1, hy + 0.5)],
                [q.chest[1] * 0.72, q.head[1] * 0.92])
    paint(c, neck & ~body, "fur", base=3.0, depth=3)
    paint(c, neck & body & ~c.mask("horn"), "fur", base=3.0, depth=3, cast=False)
    if q.mane:
        mane = ell(c, chest[0] + q.chest[0] * 0.45, chest[1] - q.chest[1] * 0.35, q.mane * 1.2 + 2, q.mane * 1.5 + 3)
        paint(c, mane, "fur", base=3.1, depth=3)
        if form == "alpha":                                  # a pale ruff under the throat
            recolour(c, mane & ~shift(mane, 1, -3), "fur2", -1)
        tufts(c, mane, "fur", cx, every=2, edge="bottom")
    _quad_head(c, q, p, hx, hy, form)
    if q.ridge:
        _bristles(c, body | neck, haunch[0] - 2, hx - 1)
    # texture last, on the finished coat
    coat = ("fur", "fur2")
    fur(c, coat, cx, by, seed=3, cell=(4, 3), length=3, slope=(-1, 1), fill=0.6)
    tufts(c, body, "fur", cx, every=3, edge="bottom")
    if form == "undead":
        _rot(c, cx, by)
    _eyes_front(c, q, p, hx, hy, form)
    return c


def _quad_tail(c: Canvas, q: Quad, p: Pose, root: tuple[float, float], side: int) -> None:
    """The tail from its root, swinging with p.tail (which lags the body). Bushy tails hang in an arc with a pale
    tip; stubs and tufts flick."""
    rx, ry = root
    sw = p.tail
    if q.tail == "bushy":
        down = 1 - p.fall * 0.5
        pts = [(rx, ry), (rx + side * 3, ry + 1 + sw * 0.5), (rx + side * (5.5 + sw * 0.8), ry + 4.5 * down + sw),
               (rx + side * (6.5 + sw * 1.4), ry + 8.5 * down + sw * 0.8), (rx + side * (6 + sw * 2), ry + 11.5 * down)]
        m = tube(c, pts, [1.6, 2.4, 2.9, 2.5, 1.4])
        paint(c, m, "fur", base=2.9, depth=3)
        tip = m & ~ell(c, rx, ry, 10.5 * down, 10.5 * down)
        recolour(c, tip, "fur2", 1)
        tufts(c, m, "fur", rx, every=2, edge="bottom")
    elif q.tail == "stub":
        paint(c, ell(c, rx + side * 1.5, ry - 0.5 + sw * 0.4, 2.2, 1.8), "fur", base=3.0, depth=1)
    else:                                                    # tuft: a thin whip with a brush on the end
        pts = [(rx, ry), (rx + side * 2.5, ry + 1.5 + sw), (rx + side * 3 + side * sw, ry + 5)]
        c.line(*pts[0], *pts[1], "fur", 2)
        c.line(*pts[1], *pts[2], "fur", 2)
        paint(c, ell(c, pts[2][0], pts[2][1] + 1, 1.2, 1.8), "fur", base=2.2, depth=1)


def _quad_head(c: Canvas, q: Quad, p: Pose, hx: float, hy: float, form: str) -> None:
    """Profile head facing +x: skull, muzzle and a lower jaw hinged under the eye that swings open with p.gape."""
    if q.face == "beak":
        _beak_head(c, q, p, hx, hy)
        return
    hrx, hry = q.head
    sl, sd = q.snout
    ear_back = p.ear
    # ears behind the skull
    if q.ears == "pointed":
        for far, ex in ((True, hx - 2.5), (False, hx - 0.5)):
            tipx, tipy = ex - 0.5 - ear_back * 4, hy - hry - 3.8 + ear_back * 2.6
            m = poly(c, [(ex - 1.8, hy - hry + 1.5), (ex + 1.8, hy - hry + 1), (tipx, tipy)])
            paint(c, m, "fur", base=2.2 if far else 3.2, depth=1)
            if not far:
                c.line(ex, hy - hry + 0.5, tipx + 0.5, tipy + 1.5, "fur2", 1)
    elif q.ears == "round":
        for far, ex in ((True, hx - 2.5), (False, hx + 0.5)):
            paint(c, ell(c, ex - ear_back, hy - hry + 0.8 + ear_back * 0.6, 1.9, 1.7), "fur",
                  base=2.2 if far else 3.1, depth=1)
            if not far:
                c.px(ex - ear_back, hy - hry + 1, "fur", 1)
    else:
        m = poly(c, [(hx - 3, hy - hry + 1.5), (hx - 0.5, hy - hry + 1), (hx - 3.5 - ear_back * 2, hy - hry - 2.5)])
        paint(c, m, "fur", base=3.0, depth=1)
    skull = ell(c, hx, hy, hrx, hry)
    tipx = hx + hrx * 0.55 + sl
    upper = poly(c, [(hx + hrx * 0.2, hy - hry * 0.65), (tipx - 1, hy - sd * 0.45), (tipx, hy - sd * 0.2),
                     (tipx, hy + sd * 0.35), (hx + hrx * 0.2, hy + hry * 0.45)])
    ang = p.gape * 0.55
    hinge = (hx + hrx * 0.1, hy + hry * 0.35)

    def rot(x: float, y: float) -> tuple[float, float]:
        dx, dy = x - hinge[0], y - hinge[1]
        return hinge[0] + dx * math.cos(ang) - dy * math.sin(ang), hinge[1] + dx * math.sin(ang) + dy * math.cos(ang)
    jaw_pts = [rot(hx - hrx * 0.4, hy + hry * 0.3), rot(tipx - 1.2, hy + sd * 0.35), rot(tipx - 1.5, hy + sd * 0.95),
               rot(hx + hrx * 0.1, hy + hry * 0.95)]
    jaw = poly(c, jaw_pts)
    if p.gape > 0.1:
        inside = poly(c, [(hx + hrx * 0.3, hy + sd * 0.35), (tipx - 0.5, hy + sd * 0.35), jaw_pts[1], jaw_pts[0]])
        flat(c, inside, "mouth", 1)
        flat(c, inside & shift(inside, -2, 0), "mouth", 0)
        paint(c, jaw, "fur", base=2.6, depth=1)
        recolour(c, jaw & ~shift(jaw, 0, -1), "fur2", 0)
        for t in (0.92, 0.55):                                               # fangs, upper and lower
            ux, uy = hx + hrx * 0.3 + (tipx - 1 - hx - hrx * 0.3) * t, hy + sd * 0.35
            c.px(ux, uy + 1, "horn", 5)
            lx, ly = rot(ux, hy + sd * 0.35)
            c.px(lx, ly - 1, "horn", 4)
        c.line(*rot(hx + hrx * 0.5, hy + sd * 0.4 + 0.5), *rot(tipx - 3, hy + sd * 0.5), "mouth", 3)   # tongue
    whole = skull | upper | (jaw if p.gape <= 0.1 else np.zeros_like(jaw))
    nudge(c, dilate(whole) & ~whole & c.mask("fur") & (grid(c)[0] > hy - hry * 0.3), -1)   # chin shadow on the neck
    paint(c, whole, "fur", base=3.2, depth=3)
    if q.belly or form == "alpha":                                          # pale cheek and lower muzzle
        cheek = (upper | jaw | skull) & ~shift(upper | jaw | skull, 0, -2) & (grid(c)[1] > hx - hrx * 0.4)
        recolour(c, cheek & c.mask("fur"), "fur2", 0)
    elif q.ears == "round":                                                 # a bear's paler muzzle
        recolour(c, (upper | jaw) & (grid(c)[1] > hx + hrx * 0.45) & c.mask("fur"), "fur2", -1)
    if q.tusks:
        for dx, sh in ((0, 5), (-2.5, 4)):
            bx_, by_ = rot(tipx - 2 + dx, hy + sd * 0.55)
            c.line(bx_, by_, bx_ + 1, by_ - 2.5, "horn", sh)
            c.px(bx_, by_ + 0.5, "horn", sh - 2)
    if q.tail == "tuft":                                                    # the boar's flat disc snout
        paint(c, ell(c, tipx - 0.3, hy + sd * 0.05, 1.2, sd * 0.55), "fur2", base=2.6, depth=1)
        c.px(tipx - 0.4, hy, "fur", 0)
    else:
        flat(c, ell(c, tipx - 0.6, hy - sd * 0.25, 1.3, 0.9), "fur", 0)                              # nose
        c.px(tipx - 1, hy - sd * 0.25 - 0.5, "fur", 2)
    if p.gape <= 0.1:
        c.line(hx + hrx * 0.45, hy + sd * 0.35, tipx - 1.5, hy + sd * 0.35, "fur", 1)
        c.px(hx + hrx * 0.4, hy + sd * 0.35 - 0.6, "fur", 1)                  # lip corner
    if form == "alpha":                                                      # a scar over the eye
        c.line(hx + 0.5, hy - hry * 0.75, hx + 2.5, hy + 0.5, "fur2", 4)


def _beak_head(c: Canvas, q: Quad, p: Pose, hx: float, hy: float) -> None:
    """A raptor's head on a beast: a round feathered skull with tufted ears, a pale facial disc and a hooked beak
    whose lower half drops with p.gape."""
    hrx, hry = q.head
    for far, ex in ((True, hx - 2.5), (False, hx)):                       # ear tufts, raked back
        tip = (ex - 2 - p.ear * 3, hy - hry - 3.5 + p.ear * 2)
        paint(c, poly(c, [(ex - 1.5, hy - hry + 2), (ex + 1.5, hy - hry + 1.5), tip]), "fur",
              base=2.2 if far else 3.2, depth=1)
    skull = ell(c, hx, hy, hrx, hry)
    nudge(c, dilate(skull) & ~skull & c.mask("fur") & (grid(c)[0] > hy - hry * 0.3), -1)
    paint(c, skull, "fur", base=3.2, depth=3)
    disc = ell(c, hx + hrx * 0.45, hy + 0.3, hrx * 0.6, hry * 0.75) & skull
    recolour(c, disc, "fur2", 0)
    c.line(hx + hrx * 0.1, hy - hry * 0.6, hx + hrx * 0.1, hy + hry * 0.6, "fur", 2)     # the disc's rim
    bx, by = hx + hrx * 0.8, hy + 0.5
    k = hrx / 4.5                                                           # the beak scales with the head
    go = p.gape * 3
    lower = poly(c, [(bx - 1, by + 1.5 * k), (bx + 3 * k, by + 1.8 * k + go), (bx - 0.5, by + 3 * k + go * 0.6)])
    if p.gape > 0.1:
        flat(c, poly(c, [(bx - 1, by + 1.5 * k), (bx + 3.5 * k, by + 1.5 * k), (bx + 3 * k, by + 1.8 * k + go)]),
             "mouth", 1)
    flat(c, lower, "horn", 2)
    upper = poly(c, [(bx - 1.5, by - 2.2 * k), (bx + 2.5 * k, by - 1.6 * k), (bx + 4.6 * k, by + 0.5),
                     (bx + 4 * k, by + 3.4 * k), (bx + 2.5 * k, by + 1.4 * k), (bx - 1.5, by + 1.5 * k)])
    paint(c, upper, "horn", base=3.4, depth=1)
    c.px(bx + 0.5, by - 0.8, "horn", 5)


def _eyes_front(c: Canvas, q: Quad, p: Pose, hx: float, hy: float, form: str) -> None:
    ex, ey = hx + q.head[0] * 0.35, hy - q.head[1] * 0.2
    if q.face == "beak":                                                    # big round raptor eyes in the disc
        ex, ey = hx + q.head[0] * 0.4, hy - q.head[1] * 0.3
        if p.down or p.hurt or p.blink:
            c.line(ex - 1, ey, ex + 1, ey, "fur", 1)
            return
        c.rect(ex - 1, ey - 1, ex + 1, ey + 0.5, "fur", 1)                  # a dark eye ring
        c.rect(ex - 0.5, ey - 0.5, ex + 0.5, ey, "eyes", 5)
        c.px(ex + 0.5, ey, "fur", 0)
        return
    c.line(ex - 1, ey - 1.5, ex + 1.5, ey - 1, "fur", 1)                   # brow
    if p.down or p.hurt or p.blink:
        c.line(ex - 0.5, ey, ex + 1, ey + 0.3, "fur", 1)
        return
    slot = "glow" if form == "undead" else "eyes"
    c.px(ex, ey, slot, 5)
    c.px(ex + 1, ey, slot, 2 if slot == "eyes" else 4)
    if form == "undead":
        c.px(ex + 2, ey - 1, "fx", 3)


def _bristles(c: Canvas, m: np.ndarray, x0: float, x1: float) -> None:
    """A crest of stiff bristles along the back line: short dark spikes raked backwards."""
    ys = np.argmax(m, axis=0)
    for x in range(int(x0), int(x1)):
        if not (0 <= x < c.w) or not m[:, x].any() or x % 2:
            continue
        y = ys[x]
        c.px(x, y - 1, "fur", 1)
        c.px(x - 1, y - 2, "fur", 2)


def _ribs(c: Canvas, x: float, y: float, q: Quad) -> None:
    """Undead: a patch of rotted hide over the flank with the ribs showing as pale bars."""
    hole = ell(c, x + 1, y + 0.5, q.chest[0] * 0.7, q.barrel * 0.75)
    flat(c, hole & c.mask(), "mouth", 0)
    flat(c, hole & c.mask() & ~shift(hole, 0, 1), "mouth", 1)
    for i in range(4):
        rx = x - 3 + i * 2.6
        c.line(rx, y - q.barrel * 0.6, rx + 1, y + q.barrel * 0.55, "horn", 4 if i % 2 == 0 else 3)
        c.px(rx, y - q.barrel * 0.6, "horn", 5)


def _rot(c: Canvas, ox: float, oy: float) -> None:
    """Undead coat: bald patches in the flesh colour, clustered (2x2 or larger), never single flecks."""
    for dx, dy, rx, ry in ((-9, -3, 1.6, 1.2), (6, -5, 1.4, 1.0), (-4, 4, 1.8, 1.0)):
        m = ell(c, ox + dx, oy + dy, rx, ry) & c.mask("fur")
        recolour(c, m, "fur2", -1)


# ------------------------------------------------------------------------------------- quadruped from behind
def _quad_back(q: Quad, p: Pose, form: str, species: str) -> Canvas:
    """The quadruped seen from behind and above, walking away up-left: the rump and tail nearest the camera at the
    bottom right, the back foreshortened, shoulders and the back of the head beyond, the muzzle just showing past
    the cheek. Hind legs flank the rump; forelegs show under the shoulders."""
    c = Canvas(W, H, SLOTS)
    adv = p.lunge * 0.5                                     # moving away = up and to the left
    rx_, ry_ = ANCHOR[0] + 3 - adv, GY - q.height + 2 + p.bob - p.air - adv * 0.45 + p.crouch * 2
    lie = GY - q.haunch[1] * 0.9
    ry_ = _lerp(ry_, lie, p.fall)
    sx_, sy_ = rx_ - q.length * 1.25, ry_ - q.length * 0.6 - q.chest[1] * 0.15 + p.tilt * 1.5
    if p.fall:
        sx_, sy_ = _lerp(sx_, rx_ - q.length * 1.3, p.fall), _lerp(sy_, lie - 1, p.fall)
    wr = q.haunch[1] * 1.3                                   # rump half-width seen from behind
    wc = q.chest[1] * 1.1
    # forelegs, beyond the body
    for side, idx in ((-1, 2), (1, 0)):
        fx = sx_ + side * wc * 0.55 + p.legs[idx] * 0.25
        fy = GY - 3 - p.lift[idx] - p.air + (p.legs[idx] * 0.3)
        if p.fall:
            fx, fy = _lerp(fx, fx - 5 - side * 2, p.fall), _lerp(fy, GY - 2, p.fall)
        paint(c, tube(c, [(fx + 0.5, sy_ + 1), (fx, fy - 1), (fx, fy)], [q.leg[0], q.leg[1], q.leg[1]]), "fur",
              base=2.0, depth=2, k=1.2)
        paint(c, ell(c, fx, fy, q.leg[1] + 0.8, 1.1), "fur", base=1.8, depth=1)
    # head beyond the shoulders: the back of the skull, ears, a sliver of muzzle to the left
    hx = sx_ - q.chest[0] * 0.45 - q.neck * 0.5 - p.head[0] * 0.6
    hy = sy_ - q.chest[1] * (0.55 + q.head_y * 0.5) + p.head[1] * 0.7 - p.head[0] * 0.25
    if p.fall:
        hx, hy = _lerp(hx, hx - 3, p.fall), _lerp(hy, GY - q.head[1] - 1, p.fall)
    muzzle = poly(c, [(hx - q.head[0] * 0.4, hy - 1), (hx - q.head[0] * 0.5 - q.snout[0] * 0.75, hy + 0.5),
                      (hx - q.head[0] * 0.5 - q.snout[0] * 0.75, hy + q.snout[1] * 0.6),
                      (hx - 1, hy + q.head[1] * 0.6)])
    if p.gape > 0.1:
        jw = poly(c, [(hx - 1, hy + q.head[1] * 0.4), (hx - q.head[0] * 0.5 - q.snout[0] * 0.6, hy + 1 + p.gape * 3),
                      (hx - q.head[0] * 0.5 - q.snout[0] * 0.5, hy + 2.5 + p.gape * 3), (hx, hy + q.head[1] * 0.8)])
        paint(c, jw, "fur", base=2.2, depth=1)
    paint(c, muzzle, "fur", base=2.6, depth=1)
    if q.tusks:
        c.line(hx - q.head[0] * 0.5 - q.snout[0] * 0.5, hy + 2, hx - q.head[0] * 0.5 - q.snout[0] * 0.5 - 1, hy - 1,
               "horn", 4)
    paint(c, ell(c, hx, hy, q.head[0] * 0.9, q.head[1]), "fur", base=3.0, depth=3)
    for side in (-1, 1):
        ex = hx + side * q.head[0] * 0.55
        if q.ears == "pointed":
            tip = (ex + side * (0.5 + p.ear * 1.5), hy - q.head[1] - 3.2 + p.ear * 2.2)
            paint(c, poly(c, [(ex - 1.7, hy - q.head[1] + 1.8), (ex + 1.7, hy - q.head[1] + 1.8), tip]), "fur",
                  base=3.0 if side > 0 else 2.6, depth=1)
            c.line(ex, hy - q.head[1] + 1, tip[0], tip[1] + 1.5, "fur", 1)        # the ear's back seam
        elif q.ears == "round":
            paint(c, ell(c, ex + side * 0.5, hy - q.head[1] + 0.8, 1.9, 1.7), "fur", base=3.0, depth=1)
        else:
            paint(c, poly(c, [(ex - 1.2, hy - q.head[1] + 1.5), (ex + 1.2, hy - q.head[1] + 1.5),
                              (ex + side * 1.5, hy - q.head[1] - 1.5)]), "fur", base=2.8, depth=1)
    # neck and shoulders, then the back and the rump, nearest
    neck = tube(c, [(hx + 0.5, hy + 1), (sx_, sy_ - 1)], [q.head[1] * 0.85, q.chest[1] * 0.75])
    shoulders = ell(c, sx_, sy_, wc, q.chest[1] * 0.75)
    if q.hump:
        shoulders |= ell(c, sx_ + 0.5, sy_ - q.hump * 0.7, wc * 0.8, q.hump + 1)
    paint(c, neck | shoulders, "fur", base=2.9, depth=4)
    if q.mane:
        mane = ell(c, sx_ - 1, sy_ - 2, q.mane * 1.1 + 2.5, q.mane * 1.0 + 2.5)
        paint(c, mane, "fur", base=3.0, depth=3)
        tufts(c, mane, "fur", rx_, every=2, edge="bottom")
    back = rot_ell(c, (sx_ + rx_) / 2, (sy_ + ry_) / 2, math.hypot(rx_ - sx_, ry_ - sy_) / 2 + 3,
                   (wc + wr) / 2 * 0.95, math.atan2(ry_ - sy_, rx_ - sx_))
    rump = ell(c, rx_, ry_, wr, q.haunch[1] * 0.95)
    paint(c, back, "fur", base=3.0, depth=5)
    # hind legs at either side of the rump (the right one nearer the camera)
    for side, idx in ((-1, 3), (1, 1)):
        hx0 = rx_ + side * wr * 0.62
        fx = hx0 + side * 0.8 + p.legs[idx] * 0.2
        fy = GY - p.lift[idx] - p.air + max(0.0, -p.legs[idx]) * 0.4
        if p.fall:
            fx, fy = _lerp(fx, hx0 + side * 4 + 3, p.fall), _lerp(fy, GY - 1, p.fall)
        base = 3.0 if side > 0 else 2.5
        paint(c, tube(c, [(hx0, ry_ + 1), (hx0 + side * 1.2, ry_ + q.haunch[1] * 0.9), (fx, fy - 2), (fx, fy)],
                      [q.leg[0] + 1.2, q.leg[0] * 0.75, q.leg[1], q.leg[1]]), "fur", base=base, depth=2)
        paint(c, ell(c, fx, fy - 0.3, q.leg[1] + 0.6, 1.1), "fur", base=base - 0.5, depth=1)
    paint(c, rump, "fur", base=3.0, depth=5)
    for side in (-1, 1):                                   # the two thigh masses either side of the tail
        paint(c, ell(c, rx_ + side * wr * 0.5, ry_ + 1.5, wr * 0.55, q.haunch[1] * 0.9), "fur",
              base=3.1 if side < 0 else 2.7, depth=3)
    c.line(rx_ + 0.5, ry_ + 1, rx_ + 0.5, ry_ + q.haunch[1] * 0.9, "fur", 1)        # the crease between them
    if q.saddle:
        spine = dilate(poly(c, [(hx + 1, hy + 1), (rx_ - 1, ry_ - 2), (rx_, ry_ - 2), (hx + 2, hy + 1)]))
        nudge(c, spine & c.mask("fur") & ~ell(c, hx, hy, q.head[0], q.head[1]), -1)
    if q.ridge:
        for t in np.linspace(0.05, 0.95, 9):
            x, y = hx + 1 + (rx_ - hx - 1) * t, hy - q.head[1] * 0.4 + (ry_ - q.haunch[1] - hy) * t
            c.px(x, y, "fur", 1)
            c.px(x + 1, y - 1, "fur", 2)
    if form == "undead":
        _ribs(c, (sx_ + rx_) / 2 - 1, (sy_ + ry_) / 2, replace(q, chest=(q.chest[0] * 0.6, q.chest[1]),
                                                               barrel=q.barrel * 0.7))
    # the tail, toward the camera over the rump
    sw = p.tail
    root = (rx_ + 0.5, ry_ - q.haunch[1] * 0.55)
    if q.tail == "bushy":
        pts = [root, (root[0] + 1 + sw * 0.6, root[1] + 3), (root[0] + 1.5 + sw * 1.5, root[1] + 7.5),
               (root[0] + 1.5 + sw * 2.4, root[1] + 12)]
        if p.fall:
            pts = [root, (root[0] + 4, root[1] + 3), (root[0] + 8, root[1] + 5), (root[0] + 11, root[1] + 6)]
        m = tube(c, pts, [2.0, 2.9, 2.9, 1.6])
        nudge(c, dilate(m) & ~m & c.mask("fur"), -1)        # the tail's shadow on the rump separates it
        paint(c, m, "fur", base=3.3, depth=3)
        recolour(c, m & ~ell(c, *root, 10, 10), "fur2", 1)
        tufts(c, m, "fur", rx_, every=2, edge="bottom")
    elif q.tail == "stub":
        paint(c, ell(c, root[0] + sw * 0.5, root[1] + 1, 2, 1.8), "fur", base=3.3, depth=1)
    else:
        c.line(root[0], root[1], root[0] + sw, root[1] + 4, "fur", 2)
        paint(c, ell(c, root[0] + sw * 1.4, root[1] + 5.5, 1.2, 1.8), "fur", base=2.4, depth=1)
    fur(c, ("fur", "fur2"), rx_, ry_, seed=5, cell=(4, 3), length=3, slope=(1, 1), fill=0.55)
    tufts(c, rump | back, "fur", rx_, every=3, edge="bottom")
    if form == "undead":
        _rot(c, rx_ - 3, ry_ - 3)
    return c


# ------------------------------------------------------------------------------------------------------ drake
def drake(direction: str, p: Pose, form: str = "") -> Canvas:
    """A young dragon, built like a reptile rather than a dog: a long low body slung between sprawled, thick-elbowed
    legs, an S-neck carrying a wedge head with swept horns, a heavy tail that drags and curls at the tip, and wings
    folded along the back that lag the body and spread for the special (a fire breath). Scales run in rows; the
    belly is plated."""
    if direction == "back":
        return _drake_back(p)
    c = Canvas(W, H, SLOTS)
    cx = 21 + p.lunge * 0.55
    by = GY - 11 + p.bob - p.air + p.crouch * 2
    by = _lerp(by, GY - 5.5, p.fall)
    chest = (cx + 6, by - 1 + p.tilt)
    hips = (cx - 6, by - p.tilt * 0.5)
    breath = p.anim == "special" and p.k in (3, 4, 5)
    # far wing (folded behind the body, or spread)
    spread = max(0.0, p.wing) if p.anim in ("special", "attack") else 0.15 + 0.15 * p.wing
    _drake_wing(c, cx + 2, by - 5, spread, 2.0, -2)
    # far legs
    for (jx, jy), idx, front in ((chest, 2, True), (hips, 3, False)):
        _drake_leg(c, p, jx - 2, jy + 2, idx, front, True)
    # tail: thick root, tapering, dragging on the ground and curling up at the tip; it lags the body
    sw = p.tail
    tail = [(hips[0] - 2, hips[1] - 1), (hips[0] - 8, hips[1] + 2 + sw * 0.5), (hips[0] - 13, GY - 4 + sw * 0.4),
            (hips[0] - 17 + sw, GY - 3), (hips[0] - 20 + sw * 1.6, GY - 6 - sw),
            (hips[0] - 20 + sw * 2.2, GY - 10 - sw)]
    tail = [(x, min(y, GY - 2)) for x, y in tail]
    tm = tube(c, tail, [4.6, 3.6, 2.8, 2.0, 1.4, 0.8])
    paint(c, tm, "fur", base=2.9, depth=3)
    tx, ty = tail[-1]
    paint(c, poly(c, [(tx - 2.5, ty + 2), (tx + 0.5, ty - 3), (tx + 2.5, ty + 1.5)]), "wing", base=3.0, depth=1)
    # body: chest, belly and hips in one long low mass
    body = ell(c, chest[0], chest[1], 7, 6) | ell(c, hips[0], hips[1], 6.5, 5.5) | ell(c, cx, by + 0.5, 9, 5)
    paint(c, body, "fur", base=3.0, depth=6)
    belly = (body | tm) & ~shift(body | tm, 0, -3)
    recolour(c, belly, "fur2", 0)
    yy, xx = np.mgrid[0:H, 0:W]
    c.shade[belly & ((xx - int(cx)) % 3 == 0) & (c.shade > 1)] -= 1          # belly plates
    # near legs, sprawled: thick upper limbs out from the body, elbows jutting
    for (jx, jy), idx, front in ((hips, 1, False), (chest, 0, True)):
        _drake_leg(c, p, jx, jy + 2, idx, front, False)
    # neck: an S rising from the chest to the head
    hx = chest[0] + 9 + p.head[0] * 0.8
    hy = chest[1] - 9 + p.head[1] * 1.2 + p.tilt * 2
    if breath:
        hx, hy = chest[0] + 9, chest[1] - 6 + (p.k - 3) * 1.5
    if p.fall:
        hx, hy = _lerp(hx, chest[0] + 11, p.fall), _lerp(hy, GY - 3.5, p.fall)
    neck = [(chest[0] + 2, chest[1] - 1), (chest[0] + 5, chest[1] - 4 + p.fall * 2),
            (hx - 4, hy + 3 - p.fall), (hx - 1, hy + 0.5)]
    nm = tube(c, neck, [4.4, 3.4, 2.8, 2.6])
    paint(c, nm, "fur", base=3.0, depth=3)
    throat = nm & ~shift(nm, -2, -2) & ~body
    recolour(c, throat, "fur2", 0)
    # ridge spines along neck, back and tail
    top = (nm | body | tm)
    ys = np.argmax(top, axis=0)
    for x in range(max(0, int(tail[-2][0])), min(W, int(hx - 2))):
        if top[:, x].any() and (x - int(cx)) % 3 == 0:
            y = ys[x]
            c.polygon([(x - 1, y + 1), (x + 1.2, y + 1), (x - 1.2, y - 1.8)], "wing", 3)
    _drake_head(c, p, hx, hy)
    scale_rows(c, "fur", cx, by, step=5, row=4, mask=~belly)
    # near wing over the body
    _drake_wing(c, cx, by - 4, spread, 3.2, -1)
    if breath:
        from .bosses import flame
        t = (0.5, 1.0, 0.6)[p.k - 3]
        flame(c, hx + 6, hy + 2, math.radians(22), 8 + 9 * t, 2 + 3 * t, 7 + p.k, slot="glow")
    if p.anim == "special" and p.k in (1, 2):                    # a glow building in the throat
        c.px(hx + 5, hy - 1, "glow", 5)
        flat(c, throat & (rng(5, p.k).random((H, W)) < 0.35 + 0.25 * p.k), "glow", 3 + p.k)
    return c


def _drake_leg(c: Canvas, p: Pose, jx: float, jy: float, idx: int, front: bool, far: bool) -> None:
    fx = jx + (2 if front else 1) + p.legs[idx] * 0.8 + (-1 if far else 0)
    fy = GY - p.lift[idx] - p.air - (1 if far else 0)
    if p.fall:
        fx, fy = _lerp(fx, jx + 6, p.fall), _lerp(fy, GY - 1, p.fall)
    base = 2.0 if far else 3.0
    elbow = (jx + (-3 if front else 3), (jy + fy) / 2 + 0.5)
    paint(c, tube(c, [(jx, jy - 1), elbow, (fx, fy - 1.5)], [3.4, 2.4, 2.0]), "fur", base=base, depth=2)
    paint(c, ell(c, fx + 1, fy - 0.5, 2.6, 1.2), "fur", base=base - 0.3, depth=1)
    for t in (0, 1.5, 3):
        c.px(fx + t, fy, "horn", 3 if far else 4)


def _drake_wing(c: Canvas, x: float, y: float, spread: float, base: float, dx: float) -> None:
    """A wing folded along the back (spread 0) or raised and fanned (spread 1): an arm bone, three fingers and the
    scalloped membrane between them."""
    a = math.radians(38 + spread * 50)
    L = 11 + spread * 5
    wx, wy = x + dx - math.cos(a) * L * 0.4, y - math.sin(a) * L
    tips = []
    for i, ang in enumerate((a - 0.9 - spread * 0.3, a - 1.5 - spread * 0.5, a - 2.1 - spread * 0.6)):
        ln = (12, 13, 10)[i] * (0.85 + spread * 0.45)
        tips.append((wx - math.cos(ang) * ln, wy - math.sin(ang) * ln * 0.6 + 1))
    pts = [(x + 1, y + 1), (wx, wy)] + tips + [(x - 6, y + 3)]
    paint(c, poly(c, pts), "wing", base=base, depth=2, k=1.2)
    bone = int(base) - 1
    c.thick_line(x, y, wx, wy, 2, "fur", max(1, bone + 1))
    for t in tips:
        c.line(wx, wy, *t, "fur", max(1, bone))
    c.px(wx, wy - 1, "horn", 4)


def _drake_head(c: Canvas, p: Pose, hx: float, hy: float) -> None:
    """A wedge head facing +x: flat skull, long snout, a jaw that drops with p.gape, brow ridge and swept horns."""
    for dx, sh in ((-2, 3), (0, 4)):                                       # horns behind the skull
        pts = curve((hx + dx, hy - 2.5), (hx + dx - 3, hy - 5), (hx + dx - 6, hy - 5.5), 4)
        paint(c, tube(c, pts, [1.3, 1.1, 0.9, 0.6, 0.5]), "horn", base=sh - 0.2, depth=1)
    skull = ell(c, hx, hy, 3.6, 3)
    snout = poly(c, [(hx, hy - 2.8), (hx + 6, hy - 1.5), (hx + 8, hy - 0.5), (hx + 8, hy + 1), (hx, hy + 1.6)])
    go = p.gape * 3.5
    jaw = poly(c, [(hx - 2, hy + 1), (hx + 7, hy + 1.4 + go), (hx + 6.5, hy + 2.8 + go), (hx - 1, hy + 3)])
    if p.gape > 0.1:
        flat(c, poly(c, [(hx, hy + 1.2), (hx + 8, hy + 1.2), (hx + 7, hy + 1.4 + go)]), "mouth", 1)
        paint(c, jaw, "fur", base=2.6, depth=1)
        recolour(c, jaw & ~shift(jaw, 0, -1), "fur2", 0)
        for t in (2.5, 5, 7):
            c.px(hx + t, hy + 2, "horn", 5)
    paint(c, skull | snout | (jaw if p.gape <= 0.1 else np.zeros_like(jaw)), "fur", base=3.1, depth=2)
    c.px(hx + 7.5, hy - 1, "mouth", 0)                                     # nostril
    c.line(hx - 1, hy - 2.5, hx + 3, hy - 2, "fur", 4)                     # brow ridge
    if p.gape <= 0.1:
        c.line(hx + 1, hy + 1.5, hx + 7, hy + 1, "fur", 1)
        c.px(hx + 5, hy + 2.3, "horn", 5)
    if p.down or p.hurt or p.blink:
        c.line(hx + 1, hy - 0.7, hx + 2, hy - 0.5, "fur", 1)
    else:
        c.px(hx + 1, hy - 1, "eyes", 5)
        c.px(hx + 2, hy - 1, "eyes", 2)
    c.polygon([(hx - 2, hy + 1), (hx - 5, hy + 3), (hx - 2, hy + 3)], "wing", 2)   # jaw frill


def _drake_back(p: Pose) -> Canvas:
    """The drake from behind, walking away up-left: the tail and haunches nearest, the plated back with its spines,
    folded wings either side and the horned head beyond."""
    c = Canvas(W, H, SLOTS)
    adv = p.lunge * 0.5
    rx_, ry_ = 28 - adv, GY - 9 + p.bob - p.air - adv * 0.4 + p.crouch * 2
    ry_ = _lerp(ry_, GY - 5, p.fall)
    sx_, sy_ = rx_ - 9, ry_ - 7 + p.tilt
    if p.fall:
        sx_, sy_ = _lerp(sx_, rx_ - 13, p.fall), _lerp(sy_, GY - 6, p.fall)
    breath = p.anim == "special" and p.k in (3, 4, 5)
    for side, idx in ((-1, 2), (1, 0)):                                     # forelegs beyond
        fx = sx_ + side * 6 + p.legs[idx] * 0.2
        fy = GY - 4 - p.lift[idx] - p.air + p.legs[idx] * 0.3
        paint(c, tube(c, [(sx_ + side * 3, sy_ + 1), (sx_ + side * 6.5, sy_ + 3), (fx, fy)], [2.6, 2.2, 1.8]), "fur",
              base=2.0, depth=2)
    hx = sx_ - 6 - p.head[0] * 0.6
    hy = sy_ - 8 + p.head[1] - p.head[0] * 0.3
    if p.fall:
        hx, hy = _lerp(hx, sx_ - 8, p.fall), _lerp(hy, GY - 4, p.fall)
    if breath:
        from .bosses import flame
        t = (0.5, 1.0, 0.6)[p.k - 3]
        flame(c, hx - 5, hy + 1, math.radians(200), 7 + 8 * t, 2 + 3 * t, 17 + p.k, slot="glow")
    paint(c, poly(c, [(hx - 1, hy - 1), (hx - 7, hy), (hx - 7, hy + 2), (hx, hy + 2.5)]), "fur", base=2.6, depth=1)
    paint(c, ell(c, hx, hy, 3.2, 3), "fur", base=3.0, depth=2)
    for side in (-1, 1):
        pts = curve((hx + side * 1.5, hy - 2), (hx + side * 3.5, hy - 5), (hx + side * 2.5 + 2, hy - 7.5), 4)
        paint(c, tube(c, pts, [1.2, 1.1, 0.9, 0.6, 0.5]), "horn", base=3.6 if side > 0 else 3.0, depth=1)
    neck = tube(c, [(hx + 1, hy + 2), (sx_ - 2, sy_ - 3), (sx_, sy_)], [2.6, 3.0, 4.0])
    paint(c, neck, "fur", base=3.0, depth=3)
    spread = max(0.0, p.wing) if p.anim in ("special", "attack") else 0.15 + 0.15 * p.wing
    for side in (1, -1):                                                    # wings rise either side of the back
        _back_wing(c, sx_ + side * 4, sy_ - 1, spread, side, 2.4 if side > 0 else 3.0)
    back = rot_ell(c, (sx_ + rx_) / 2, (sy_ + ry_) / 2, math.hypot(rx_ - sx_, ry_ - sy_) / 2 + 4, 6,
                   math.atan2(ry_ - sy_, rx_ - sx_))
    paint(c, back, "fur", base=3.0, depth=5)
    for side, idx in ((-1, 3), (1, 1)):                                    # haunches and hind legs
        hx0 = rx_ + side * 6
        fx = hx0 + side * 2 + p.legs[idx] * 0.2
        fy = GY - p.lift[idx] - p.air
        if p.fall:
            fx, fy = _lerp(fx, hx0 + side * 4, p.fall), _lerp(fy, GY - 1, p.fall)
        paint(c, ell(c, hx0, ry_ + 1, 4.2, 5), "fur", base=3.1 if side < 0 else 2.7, depth=3)
        paint(c, tube(c, [(hx0, ry_ + 3), (hx0 + side * 3, ry_ + 6), (fx, fy - 1)], [3, 2.4, 2]), "fur",
              base=3.0 if side < 0 else 2.6, depth=2)
        paint(c, ell(c, fx, fy - 0.3, 2.4, 1.1), "fur", base=2.6, depth=1)
    sw = p.tail
    tail = [(rx_, ry_ + 2), (rx_ + 2 + sw * 0.5, ry_ + 6), (rx_ + 6 + sw, GY - 3), (rx_ + 12 + sw * 1.5, GY - 3),
            (rx_ + 16 + sw * 2, GY - 6 - sw)]
    tail = [(x, min(y, GY - 2)) for x, y in tail]
    paint(c, tube(c, tail, [4.2, 3.4, 2.4, 1.5, 0.8]), "fur", base=3.1, depth=3)
    tx, ty = tail[-1]
    paint(c, poly(c, [(tx - 1, ty + 2), (tx + 2, ty - 2.5), (tx + 3.5, ty + 1)]), "wing", base=3.0, depth=1)
    spine = [(hx + 1, hy - 1)] + curve((sx_, sy_ - 5), ((sx_ + rx_) / 2, (sy_ + ry_) / 2 - 6), (rx_, ry_ - 3), 5) + \
        [(x, y - 2) for x, y in tail[1:4]]
    for i, (x, y) in enumerate(spine[1:]):
        c.polygon([(x - 1, y + 1), (x + 1, y + 1), (x + 0.5, y - 1.8)], "wing", 3 + (i % 2))
    scale_rows(c, "fur", rx_, ry_, step=5, row=4)
    return c


def _back_wing(c: Canvas, x: float, y: float, spread: float, side: int, base: float) -> None:
    a = math.radians(55 + spread * 30)
    L = 8 + spread * 6
    wx, wy = x + side * math.cos(a) * L * 0.5, y - math.sin(a) * L
    tips = [(wx + side * (6 + spread * 5), wy + 4), (wx + side * (5 + spread * 3), wy + 9), (wx + side * 2, wy + 12)]
    paint(c, poly(c, [(x, y + 2), (wx, wy)] + tips + [(x + side * 1, y + 6)]), "wing", base=base, depth=2, k=1.2)
    c.thick_line(x, y, wx, wy, 2, "fur", 2)
    for t in tips[:2]:
        c.line(wx, wy, *t, "fur", 2)


# ----------------------------------------------------------------------------------------------------- spider
def _spider_gait(p: Pose, i: int, side: int) -> tuple[float, float]:
    """An alternating tetrapod: legs 1 and 3 on one side step with 2 and 4 on the other, half a cycle apart."""
    if p.anim == "walk":
        off = 0.0 if (i % 2 == 0) == (side > 0) else 0.5
        a = (p.phase + off) % 1.0
        if a < 0.5:
            return 2.2 * (1 - 4 * a), 0.0
        t = (a - 0.5) / 0.5
        return 2.2 * (-1 + 2 * t), math.sin(math.pi * t) * 2.5
    if p.anim == "idle":
        return 0.0, (1.0 if (p.k == 2 and i == 0) or (p.k == 4 and i == 1) else 0.0)     # a leg taps
    return 0.0, 0.0


def spider(direction: str, p: Pose, form: str = "") -> Canvas:
    """A giant spider: a bulbous patterned abdomen, a cephalothorax with a cluster of glowing eyes and fangs, and
    eight jointed legs that arch high and walk in an alternating gait. From behind, the abdomen fills the
    foreground and the legs splay out on both sides."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    cx = ANCHOR[0] + (p.lunge * 0.6 if not back else -p.lunge * 0.4)
    by = GY - 10 + p.bob - p.air + p.crouch * 2 - (p.lunge * 0.25 if back else 0)
    by = _lerp(by, GY - 5, p.fall)
    if form == "phase":                                                   # an afterimage trailing a step behind
        ghost = Canvas(W, H, SLOTS)
        _spider_body(ghost, replace(p, lunge=p.lunge - 3, gape=0), cx - (3 if not back else -2), by - 1, back, form)
        yy, xx = np.mgrid[0:H, 0:W]
        m = (ghost.slot > 0) & ((xx + yy + p.k) % 2 == 0)
        flat(c, m, "fx", 2)
    _spider_body(c, p, cx, by, back, form)
    if form == "phase":
        r = rng(19, ORDER.index(p.anim), p.k)
        for _ in range(5):                                              # motes of the phase shimmer
            x, y = cx + r.uniform(-14, 14), by + r.uniform(-12, 4)
            c.px(x, y, "fx", int(r.integers(3, 6)))
            c.px(x, y - 1, "fx", 3)
    return c


def _spider_body(c: Canvas, p: Pose, cx: float, by: float, back: bool, form: str) -> None:
    curl = p.fall
    legs_far, legs_near = [], []
    for side in (-1, 1):
        for i in range(4):
            spread = (-1.0, -0.35, 0.35, 1.0)[i]
            reach = (16, 13, 13, 16)[i]
            sw, lift = _spider_gait(p, i, side)
            if back:
                hx, hy = cx + side * 2.5, by - 1 + spread * 2.5
                kx = cx + side * (6 + abs(spread) * 3) + spread * 1.5
                ky = by - 10 + abs(spread) * 2 - (1 if side < 0 else 0)
                fx = cx + side * (reach - 1) + spread * 3 + sw * 0.5 * side
                fy = GY - (3 if spread < 0 else 0) + spread * 1.5 - lift
            else:
                hx, hy = cx + 4 + spread * 2, by - 1
                kx = cx + 4 + spread * reach * 0.6 + sw * 0.6 + (1 if side > 0 else -1)
                ky = by - 15 - (2 if side < 0 else 0) + abs(spread) * 1.5
                fx = cx + 4 + spread * reach + sw
                fy = GY - (2 if side < 0 else 0) - lift
            if p.anim == "attack" and i == 0 and p.k in (1, 2, 3, 4):        # front legs rear up and strike
                kx, ky = hx + (8 if not back else -side * 2), by - 15 + (p.k - 1) * 2
                fx, fy = hx + (13 if not back else -side * 1), by - 8 + (p.k - 1) * 4
            if curl:
                kx, ky = _lerp(kx, hx + (kx - hx) * 0.4, curl), _lerp(ky, by - 5, curl)
                fx, fy = _lerp(fx, hx + (fx - hx) * 0.35, curl), _lerp(fy, by - 1, curl)
            far = side < 0 or back
            (legs_far if far else legs_near).append(((hx, hy), (kx, ky), (fx, fy), far))
    for (h, k, f, far) in legs_far:
        _spider_leg(c, h, k, f, far)
    if back:
        paint(c, ell(c, cx - 3, by - 4, 5.5, 4.5), "fur", base=2.6, depth=3)    # cephalothorax, beyond
        for ex, ey in ((cx - 6, by - 7), (cx - 4, by - 8)):
            c.px(ex, ey, "glow", 3)
        ab = ell(c, cx + 2, by + 1, 9.5, 8)
        paint(c, ab, "fur", base=3.0, depth=5)
        _spider_marks(c, cx + 2, by + 1, form, True)
    else:
        ab = ell(c, cx - 8, by - 3, 9, 7.5)
        paint(c, ab, "fur", base=3.0, depth=5)
        _spider_marks(c, cx - 8, by - 3, form, False)
        paint(c, ell(c, cx + 4, by + 0.5, 6, 4.5), "fur", base=3.1, depth=3)   # cephalothorax
        hx, hy = cx + 9 + p.head[0] * 0.3, by + 0.5 + p.head[1] * 0.3
        paint(c, ell(c, hx, hy, 3.4, 3), "fur", base=3.2, depth=2)
        lit = 2 if (p.hurt or p.blink or p.down) else 5
        for ex, ey, s in ((hx + 1, hy - 1.5, 0), (hx + 2.5, hy - 1, 0), (hx, hy - 2.5, 1), (hx + 1.8, hy - 2.8, 1),
                          (hx + 2.8, hy + 0.3, 1)):
            c.px(ex, ey, "glow", lit - s)
        gap = p.gape * 2
        for dx, sh in ((0.5, 3), (2.5, 4)):                                 # chelicerae and fangs
            paint(c, ell(c, hx + dx, hy + 2.5, 1.2, 1.6), "fur", base=2.5, depth=1)
            c.line(hx + dx + (dx - 1.5) * gap * 0.5, hy + 3.5, hx + dx + 0.8 + (dx - 1.5) * gap, hy + 5.5 + gap * 0.5,
                   "horn", sh + 1)
        c.line(hx + 3, hy + 1, hx + 5, hy + 2, "fur", 2)                      # pedipalp
    for (h, k, f, far) in legs_near:
        _spider_leg(c, h, k, f, far)
    fur(c, "fur", cx, by, seed=11, cell=(3, 3), length=2, slope=(-1, 1), fill=0.4, mask=ab)


def _spider_leg(c: Canvas, h, k, f, far: bool) -> None:
    base = 1.9 if far else 3.0
    paint(c, tube(c, [h, k], [1.6, 1.3]), "fur", base=base, depth=1, k=1.2)
    paint(c, tube(c, [k, f], [1.1, 0.6]), "fur", base=base - 0.4, depth=1, k=1.0)
    c.px(k[0], k[1] - 1, "fur", 4 if not far else 2)                            # a lit knee
    mx, my = (k[0] + f[0]) / 2, (k[1] + f[1]) / 2
    c.px(mx + 1, my, "fur", 1)                                                   # a bristle


def _spider_marks(c: Canvas, x: float, y: float, form: str, back: bool) -> None:
    """The abdomen's marking: a red hourglass seen from behind, a chevron stripe from the side; phase spiders carry
    glowing runes."""
    slot = "fx" if form == "phase" else "fur2"
    if back:
        for dy in range(-3, 4):
            w = abs(dy) * 0.6 + 0.6
            c.line(x - w, y + dy, x + w, y + dy, slot, 4 if dy < 0 else 3)
    else:
        for i in range(4):
            px, py = x - 5 + i * 3, y - 4 + i * 0.8
            c.line(px, py, px + 1.5, py + 1.5, slot, 4)
            c.line(px + 1.5, py + 1.5, px + 3, py, slot, 3)


# ------------------------------------------------------------------------------------------------------ slime
def slime(direction: str, p: Pose, form: str = "") -> Canvas:
    """A slime: squash and stretch carries all its motion. A translucent body (a bright core, a darker rim, light
    caught on the top-left), bubbles drifting up inside, a puddle foot and two beady eyes. The attack rears up and
    slams; the special is a leap and a splat; down melts it into a puddle. Crystal slimes grow shards."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    sp = {"idle": (0, 0.6, 1.0, 0.6, 0, -0.4), "walk": (0, 1.5, 2.5, 1, -1.5, -2.5, -1, 0)}
    if p.anim in sp:
        st = sp[p.anim][p.k]
    elif p.anim == "attack":
        st = (-2, -3, 5, -4, -3.5, 1, 0)[p.k]
    elif p.anim == "special":
        st = (-3, -4, 4, 3, -5, -4, 0)[p.k]
    elif p.anim == "hurt":
        st = (-3, 1.5, 0)[p.k]
    else:
        st = (-1, -3, -5, -6)[p.k]
    hop = (0, 0, 2, 3, 2, 0, 0, 0)[p.k] if p.anim == "walk" else 0
    rx = 10 - st * 0.55
    ry = 8 + st * 0.7
    if p.anim == "down":
        rx, ry = 10 + p.fall * 5, max(2.5, 8 - p.fall * 5.5)
    cx = ANCHOR[0] + (p.lunge * 0.5 if not back else -p.lunge * 0.3)
    lift = hop + p.air
    cy = GY - ry - lift + 0.5
    body = ell(c, cx, cy, rx, ry) | (ell(c, cx, GY - 1.5, rx + 1.5, 2.2) if not lift else np.zeros((H, W), bool))
    if p.anim == "down":
        body |= ell(c, cx + 4, GY - 1, rx * 0.6, 1.5)
    paint(c, body, "fur", base=3.2, depth=5, k=1.5)
    core = ell(c, cx + 0.5, cy + ry * 0.15, rx * 0.55, ry * 0.5) & body
    nudge(c, core, 1, hi=4)
    rim = body & ~shift(body, 0, 1)
    nudge(c, rim & ~ell(c, cx - rx * 0.5, cy - ry * 0.5, rx, ry), -1)
    r = rng(23, ORDER.index(p.anim), p.k)
    for i in range(3):                                                     # bubbles rising through the jelly
        bx_ = cx - rx * 0.4 + i * rx * 0.4 + math.sin(p.k + i) * 1.5
        by_ = cy + ry * 0.6 - ((p.k * 1.5 + i * 3.3) % (ry * 1.4))
        if body[int(np.clip(by_, 0, H - 1)), int(np.clip(bx_, 0, W - 1))]:
            c.px(bx_, by_, "fur", 5)
            c.px(bx_ + 1, by_ + 1, "fur", 2)
    gx, gy_ = cx - rx * 0.45, cy - ry * 0.55                               # the gloss highlight, a curved streak
    c.line(gx, gy_ + 1, gx + 1, gy_, "fur", 5)
    c.line(gx + 1, gy_, gx + 3, gy_, "fur", 5)
    c.px(gx - 0.5, gy_ + 2.5, "fur", 5)
    if form == "crystal":
        for i, (dx, h, lean) in enumerate(((-4, 6, -1.5), (0, 8, 0.5), (4, 5, 2), (-1, 4, -2.5))):
            x0, y0 = cx + dx * rx / 10, cy - ry * 0.75 + abs(dx) * 0.35
            shard = poly(c, [(x0 - 1.6, y0 + 2), (x0 + 1.6, y0 + 2), (x0 + lean, y0 - h * (ry / 8))])
            flat(c, shard, "horn", 3)
            flat(c, shard & ~shift(shard, 1, 0), "horn", 5)
            flat(c, shard & ~shift(shard, -1, 0), "horn", 2)
        for i in range(2):                                                 # shards caught inside
            c.px(cx - 3 + i * 5, cy + 2 - i, "horn", 4)
            c.px(cx - 3 + i * 5, cy + 3 - i, "horn", 3)
    if p.anim == "attack" and p.k in (3, 4):                               # splash on the slam
        for i, dx in enumerate((-1, 1)):
            for j in range(3):
                c.px(cx + dx * (rx + 2 + j * 2), GY - 2 - j * (2 - i * 0.5) + (p.k - 3) * 2, "fur", 4 - j % 2)
    if p.anim == "special" and p.k in (4, 5):
        for j in range(5):
            a = math.pi * (0.1 + j * 0.2)
            c.px(cx + math.cos(a) * (rx + 3), GY - 1 - math.sin(a) * 5, "fur", 4)
    if back:
        return c
    ex, ey = cx + 2 + p.head[0] * 0.3, cy - ry * 0.05
    if p.down or p.hurt or p.blink:
        c.line(ex - 3.5, ey, ex - 2, ey + 0.5, "eyes", 0)
        c.line(ex + 2, ey + 0.5, ex + 3.5, ey, "eyes", 0)
    else:
        for x in (ex - 3, ex + 2.5):
            c.rect(x, ey - 1, x + 1, ey + 1, "eyes", 0)
            c.px(x, ey - 1, "eyes", 5)
    if p.jaw or p.gape > 0.3:
        m = ell(c, ex, ey + 3.5, 1.8 + p.gape, 1 + p.gape)
        flat(c, m, "mouth", 1)
        flat(c, m & ~shift(m, 0, 1), "mouth", 0)
    return c


# -------------------------------------------------------------------------------------------------------- bat
def bat(direction: str, p: Pose, form: str = "") -> Canvas:
    """A giant bat hovering a body-height above its tile. The wings beat through a full stroke on every loop: the
    body rises on the down-stroke and sinks as the wings lift, the wingtips trail the arm by a frame, and the ears
    and feet swing with it. From behind we see its back and the wings' upper side."""
    c = Canvas(W, H, SLOTS)
    back = direction == "back"
    beat = p.wing                                     # -1 down .. 1 up
    if p.anim in ("idle", "walk"):
        beat = math.cos(p.phase * math.tau * (1 if p.anim == "idle" else 2))
    if p.fall:
        beat = _lerp(beat, -0.6, p.fall)
    lag = math.cos(p.phase * math.tau * (1 if p.anim == "idle" else 2) - 1.2) if p.anim in ("idle", "walk") else \
        beat * 0.6
    lunge = p.lunge if not back else -p.lunge * 0.6
    dive = p.anim == "special" and p.k in (2, 3, 4)
    cx = ANCHOR[0] + lunge * 0.55
    cy = 20 - beat * 1.5 + p.head[1] * 0.6 + (p.k * 2.5 if dive else 0) + (2 if p.hurt else 0)
    cy = _lerp(cy, GY - 5, p.fall)
    big = 1.15 if form == "vampire" else 1.0
    if dive:
        beat = 0.9
    # wings: arm to wrist, three fingers whose tips lag the arm, a scalloped membrane
    for side in (-1, 1):
        far = side < 0 and not back
        base = 2.3 if far else 3.0
        if back:
            base = 2.8 if side > 0 else 2.5
        _bat_wing(c, cx + side * 2.5, cy - 1, side, beat, lag, base, big * (0.75 if p.fall >= 1 else 1))
    body = ell(c, cx, cy + 1, 4.5 * big, 6 * big)
    paint(c, body, "fur", base=3.0, depth=3)
    ruff = ell(c, cx, cy - 2.5, 4.8 * big, 2.6 * big)
    paint(c, ruff, "fur", base=3.3, depth=2)
    tufts(c, ruff, "fur", cx, every=2, edge="bottom")
    if not back:
        recolour(c, ell(c, cx + 0.8, cy + 2.5, 2.5 * big, 3.5 * big) & body, "fur", -1)       # darker belly fur
    fur(c, "fur", cx, cy, seed=13, cell=(3, 3), length=2, slope=(0, 1), fill=0.5, mask=body)
    for side in (-1, 1):                                                   # feet dangling, swinging with the beat
        fx = cx + side * 1.5 + lag * 0.5
        c.line(fx, cy + 6 * big, fx + lag * 0.6, cy + 8 * big, "fur", 2)
        c.px(fx + lag * 0.6 + side * 0.6, cy + 8.5 * big, "horn", 3)
    hx, hy = cx + (0.5 if not back else 0), cy - 4.5 * big
    for side in (-1, 1):                                                   # ears, lagging the beat
        ex = hx + side * 2.2
        tip = (ex + side * (1.2 if form == "vampire" else 0.6) - lag * 0.4, hy - (6.5 if form == "vampire" else 5)
               - lag * 0.6)
        paint(c, poly(c, [(ex - 1.6, hy - 1), (ex + 1.6, hy - 1), tip]), "fur", base=3.2 if side < 0 else 2.7,
              depth=1)
        if not back:
            c.line(ex, hy - 1.5, (ex + tip[0]) / 2, (hy + tip[1]) / 2, "wing", 2)
    paint(c, ell(c, hx, hy, 3.2 * big, 2.8 * big), "fur", base=3.2, depth=2)
    if back:
        return c
    c.px(hx, hy + 1.5, "mouth", 0)                                         # snout
    if form == "vampire":
        c.line(hx - 0.5, hy + 0.5, hx + 0.5, hy - 0.5, "wing", 3)           # nose-leaf
    if p.down or p.blink or p.hurt:
        c.line(hx - 2, hy - 0.5, hx - 1, hy - 0.5, "glow", 1)
        c.line(hx + 1.5, hy - 0.5, hx + 2.5, hy - 0.5, "glow", 1)
    else:
        c.px(hx - 1.5, hy - 0.5, "glow", 5)
        c.px(hx + 2, hy - 0.5, "glow", 5)
    if p.jaw or p.gape > 0.3 or form == "vampire":
        open_ = p.jaw or p.gape > 0.3
        if open_:
            c.rect(hx - 1, hy + 2, hx + 1, hy + 2.5, "mouth", 1)
        c.px(hx - 1, hy + (2.5 if open_ else 2), "horn", 5)
        c.px(hx + 1, hy + (2.5 if open_ else 2), "horn", 5)
    return c


def _bat_wing(c: Canvas, sx: float, sy: float, side: int, beat: float, lag: float, base: float, span: float) -> None:
    a = beat * 0.9                                                         # arm angle above horizontal
    L = 9 * span
    wx, wy = sx + side * L * math.cos(a), sy - L * math.sin(a)
    tips = []
    for i, (off, ln) in enumerate(((0.25, 11), (-0.35, 10), (-0.95, 8))):
        ang = a * 0.6 + off - lag * 0.35 * (i + 1) / 3
        tips.append((wx + side * ln * span * math.cos(ang), wy - ln * span * math.sin(ang)))
    root = (sx - side * 1, sy + 6)
    pts = [(sx, sy - 1), (wx, wy)]
    prev = (wx, wy)
    for i, t in enumerate(tips):
        if i:
            pts.append(((prev[0] + t[0]) / 2 - side * 0.5, (prev[1] + t[1]) / 2 + 2))
        pts.append(t)
        prev = t
    pts += [((tips[-1][0] + root[0]) / 2, (tips[-1][1] + root[1]) / 2 + 2), root]
    paint(c, poly(c, pts), "wing", base=base, depth=2, k=1.1)
    bone = 2 if base >= 2.8 else 1
    c.thick_line(sx, sy, wx, wy, 2, "fur", bone + 1)
    for t in tips:
        c.line(wx, wy, *t, "fur", bone)
    c.px(wx, wy - 1, "horn", 4)


# --------------------------------------------------------------------------------------------------- catalogue
@dataclass
class Beast:
    id: str
    name: str
    draw: Callable[..., Canvas]
    variants: dict[str, dict[str, str]]
    tags: list[str] = field(default_factory=list)
    flying: bool = False
    forms: dict[str, str] = field(default_factory=dict)       # variant -> drawing form ("" = the base drawing)


def _v(**slots: str) -> dict[str, str]:
    return slots


CATALOGUE: list[Beast] = [
    Beast("wolf", "Wolf", quad("wolf"), {
        "grey": _v(fur="beast/wolf_grey", fur2="beast/belly_grey", eyes="eyes/amber", horn="beast/ivory",
                   mouth="beast/mouth"),
        "timber": _v(fur="beast/wolf_timber", fur2="beast/belly_light", eyes="eyes/amber", horn="beast/ivory",
                     mouth="beast/mouth"),
        "dire": _v(fur="beast/wolf_dire", fur2="beast/belly_grey", eyes="eyes/red", horn="beast/ivory",
                   mouth="beast/mouth"),
        "frost": _v(fur="beast/wolf_frost", fur2="beast/belly_grey", eyes="eyes/blue", horn="beast/ivory",
                    mouth="beast/mouth"),
        "wildshape": _v(fur="beast/wild_fur", fur2="beast/wild_belly", eyes="magic/nature", horn="beast/ivory",
                        mouth="beast/mouth"),
        "alpha": _v(fur="beast/wolf_alpha", fur2="beast/mane_alpha", eyes="eyes/gold", horn="beast/ivory",
                    mouth="beast/mouth")}, ["beast", "wildshape"], forms={"alpha": "alpha"}),
    Beast("boar", "Boar", quad("boar"), {
        "brown": _v(fur="beast/boar", fur2="beast/belly_light", eyes="eyes/red", horn="beast/ivory",
                    mouth="beast/mouth"),
        "dark": _v(fur="beast/boar_dark", fur2="beast/boar", eyes="eyes/red", horn="beast/ivory",
                   mouth="beast/mouth")}, ["beast"]),
    Beast("bear", "Bear", quad("bear"), {
        "brown": _v(fur="beast/bear_brown", fur2="beast/belly_light", eyes="eyes/dark", horn="beast/ivory",
                    mouth="beast/mouth"),
        "black": _v(fur="beast/bear_black", fur2="beast/bear_brown", eyes="eyes/amber", horn="beast/ivory",
                    mouth="beast/mouth"),
        "polar": _v(fur="beast/bear_polar", fur2="beast/belly_grey", eyes="eyes/dark", horn="beast/ivory",
                    mouth="beast/mouth"),
        "wildshape": _v(fur="beast/wild_bark", fur2="beast/wild_fur", eyes="magic/nature", horn="beast/ivory",
                        mouth="beast/mouth"),
        "undead": _v(fur="beast/bear_rot", fur2="beast/rot_flesh", eyes="eyes/dark", horn="horn/bone",
                     mouth="beast/mouth", glow="magic/fel", fx="magic/fel")},
          ["beast", "large", "wildshape"], forms={"undead": "undead"}),
    Beast("spider", "Giant Spider", spider, {
        "black": _v(fur="beast/spider", fur2="beast/spider_red", glow="magic/blood", horn="beast/ivory"),
        "red": _v(fur="beast/spider_red", fur2="beast/ivory", glow="magic/fire", horn="beast/ivory"),
        "phase": _v(fur="beast/spider_phase", fur2="beast/phase_glow", glow="beast/phase_glow", horn="beast/ivory",
                    fx="beast/phase_glow")}, ["beast"], forms={"phase": "phase"}),
    Beast("slime", "Slime", slime, {
        "green": _v(fur="beast/slime_green", eyes="eyes/dark", mouth="beast/mouth"),
        "blue": _v(fur="beast/slime_blue", eyes="eyes/dark", mouth="beast/mouth"),
        "magma": _v(fur="beast/slime_magma", eyes="eyes/dark", mouth="beast/mouth"),
        "crystal": _v(fur="beast/slime_crystal", eyes="eyes/dark", mouth="beast/mouth", horn="beast/crystal")},
          ["ooze"], forms={"crystal": "crystal"}),
    Beast("bat", "Giant Bat", bat, {
        "dusk": _v(fur="beast/bat", wing="beast/bat_wing", glow="magic/blood", horn="beast/ivory"),
        "vampire": _v(fur="beast/bat_vampire", wing="beast/bat_vampire_wing", glow="eyes/red", horn="beast/ivory",
                      mouth="beast/mouth")}, ["beast"], flying=True, forms={"vampire": "vampire"}),
    Beast("drake", "Drake", drake, {
        "red": _v(fur="skin/dragon_red", fur2="skin/dragon_gold", wing="skin/dragon_copper", eyes="eyes/gold",
                  horn="beast/ivory", mouth="beast/mouth", glow="magic/fire"),
        "green": _v(fur="skin/dragon_green", fur2="skin/dragon_brass", wing="beast/drake_wing_green", eyes="eyes/red",
                    horn="beast/ivory", mouth="beast/mouth", glow="magic/nature"),
        "blue": _v(fur="skin/dragon_blue", fur2="skin/dragon_silver", wing="beast/drake_wing_blue", eyes="eyes/gold",
                   horn="beast/ivory", mouth="beast/mouth", glow="magic/storm")}, ["dragon", "boss"]),
]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {s: _ref(r) for s, r in variant.items()}
    for name in SLOTS.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


_CANVAS_CACHE: dict[tuple[str, str, str, int], Canvas] = {}


def canvases(b: Beast, form: str = "") -> list[Canvas]:
    """Both facings' outlined frames for one drawing form, in sheet order; palette variants share them."""
    out = []
    for d in ("front", "back"):
        for i, (_a, _k, pose) in enumerate(frame_poses()):
            key = (b.id, form, d, i)
            if key not in _CANVAS_CACHE:
                cv = b.draw(d, pose, form) if form else b.draw(d, pose)
                cv.outline(skip=FX_SLOTS)
                _CANVAS_CACHE[key] = cv
            out.append(_CANVAS_CACHE[key])
    return out


def sheet(b: Beast, variant: str) -> Sheet:
    pal = ramps(b.variants[variant])
    s = Sheet(W, H, len(frame_poses()), ANCHOR)
    s.frames = [cv.render(pal) for cv in canvases(b, b.forms.get(variant, ""))]
    return s


def anims_meta() -> dict:
    out, i = {}, 0
    for a in ORDER:
        fps, loop, count = ANIMS[a]
        out[a] = {"frames": list(range(i, i + count)), "fps": fps, "loop": loop}
        i += count
    return out


def anim_grid(fr: list[Image.Image], anims: dict, gap: int = 2) -> Image.Image:
    """One facing laid out one animation per row, so each sequence reads left to right."""
    w, h = fr[0].size
    cols = max(len(v["frames"]) for v in anims.values())
    out = Image.new("RGBA", (cols * (w + gap) - gap, len(anims) * (h + gap) - gap))
    for r, v in enumerate(anims.values()):
        for k, i in enumerate(v["frames"]):
            out.alpha_composite(fr[i], (k * (w + gap), r * (h + gap)))
    return out


def build() -> None:
    out = PACK / "beasts"
    shutil.rmtree(out, ignore_errors=True)
    n = len(frame_poses())
    anims = anims_meta()
    entries, tiles = {}, []
    for b in CATALOGUE:
        files = {}
        for v in b.variants:
            sh = sheet(b, v)
            rel = f"{b.id}_{v}.png"
            save(sh.image(), out / rel)
            files[v] = rel
            tiles.append(Tile(strip([sh.frames[0], sh.frames[n]], 1, gap=4), b.name, f"{v} · front, back"))
        entries[b.id] = {"name": b.name, "variants": files, "tags": b.tags, "flying": b.flying}
    write_json({"frame": {"width": W, "height": H, "anchor": list(ANCHOR)}, "framesPerDirection": n,
                "directions": {"front": {"row": 0}, "back": {"row": 1}}, "anims": anims, "beasts": entries,
                "notes": {"sheet": "one sheet per beast and variant: row 0 front (facing down-right), row 1 back "
                                   "(drawn from behind, facing up-left); mirror either for the other two diagonals",
                          "anims": "idle 6 · walk 8 (a four-beat gait) · attack 7 (coil, lunge, held bite, recover) "
                                   "· special 7 (crouch, leap, land) · hurt 3 · down 4 (the last frame is the "
                                   "body)"}},
               out / "manifest.json")
    save(contact_sheet("Beasts", tiles, columns=6, zoom=3,
                       subtitle="Monsters off the humanoid rig: same animation names, facings and anchor rules as "
                                "characters, in palette variants. Back facings are drawn from behind."),
         PREVIEWS / "18-beasts.png")
    rows = []
    for b in CATALOGUE:
        sh = sheet(b, next(iter(b.variants)))
        rows.append(Tile(anim_grid(sh.frames[:n], anims), b.name, "front · " + " · ".join(
            f"{a} {ANIMS[a][2]}" for a in ORDER)))
        rows.append(Tile(anim_grid(sh.frames[n:], anims), b.name, "back (drawn from behind)"))
    save(contact_sheet("Beasts · animations", rows, columns=4, zoom=2,
                       subtitle="One animation per row: idle, walk, attack, special, hurt, down"),
         PREVIEWS / "18-beasts-anims.png")
    _scale_preview()
    _gif(n, anims)


def _gif(n: int, anims: dict) -> None:
    from arcpix.anim import Actor, stage_gif
    actors = [Actor(b.name, v, sheet(b, v).frames, ANCHOR)
              for b in CATALOGUE for v in [next(iter(b.variants))] + [v for v in b.forms]]
    k = len(actors)

    def seq(name: str, back: bool = False) -> list[int]:
        return [f + (n if back else 0) for f in anims[name]["frames"]]

    atk, spc = seq("attack"), seq("special")
    beats = [("Idle", [seq("idle") * 3] * k, 160), ("Walk", [seq("walk") * 3] * k, 100),
             ("Walk · away", [seq("walk", True) * 3] * k, 100),
             ("Attack", [seq("idle")[:2] + atk + [atk[-1]] * 3] * k, 85),
             ("Attack · away", [seq("idle", True)[:2] + seq("attack", True) + [n + atk[-1]] * 3] * k, 85),
             ("Special", [seq("idle")[:2] + spc + [spc[-1]] * 3] * k, 90),
             ("Hurt", [seq("hurt") * 2 + seq("idle")[:3]] * k, 100), ("Down", [seq("down") + seq("down")[-1:] * 8] * k,
                                                                     110)]
    stage_gif(PREVIEWS / "anim-beasts.gif", "Beasts", actors, beats, zoom=3, columns=6, cell_w=56, cell_h=56)


def _scale_preview() -> None:
    """Every beast beside a human hero on one ground line, at 1:1 scale: the check that a wolf reads as a wolf and a
    bear towers over a person."""
    from . import dioramas as D
    hero = D.unit("kara", 0, 0)
    fig = [(hero.image, hero.anchor, "Kara (hero)")]
    for b in CATALOGUE:
        for v in [next(iter(b.variants))] + list(b.forms):
            fig.append((sheet(b, v).frames[0], ANCHOR, f"{b.name} · {v}"))
    gap = 6
    wsum = sum(im.width for im, _a, _l in fig) + gap * (len(fig) + 1)
    img = Image.new("RGBA", (wsum, 60), (0, 0, 0, 0))
    x = gap
    for im, (ax, ay), _l in fig:
        img.alpha_composite(im, (x, 52 - ay))
        x += im.width + gap
    line = Image.new("RGBA", (wsum, 1), (90, 84, 110, 255))
    img.alpha_composite(line, (0, 53))
    save(contact_sheet("Beasts · scale", [Tile(img, "One ground line, 1:1", " · ".join(l for _i, _a, l in fig))],
                       columns=1, zoom=4, subtitle="A human hero is about 24 px tall in a 32x40 frame."),
         PREVIEWS / "18-beasts-scale.png")
