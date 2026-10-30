"""Character layer parts. Each part draws one layer for one frame from the rig's keypoints.

Slots used (shared table, so every layer's ramp map agrees):
  skin, eyes, hair, cloth1 (primary), cloth2 (secondary), trim, metal, leather, wood, magic

Conventions: draw the far side first, then the body of the part, then the near side, so the near limb overlaps.
Shading is applied per layer afterwards (shade_volume), then the layer gets its own selective outline.
"""
from __future__ import annotations

import math
from typing import Callable

from arcpix.canvas import Canvas
from .rig import Points, FRAME_W

Part = Callable[[Canvas, Points], None]


# ---------------------------------------------------------------------------------------------------- helpers

def _arm(c: Canvas, p: Points, near: bool, slot: str, *, width: int = 2, to_wrist: bool = True, shade: int = 3) -> None:
    sx, sy = p.shoulder_n if near else p.shoulder_f
    hx, hy = p.hand_n if near else p.hand_f
    if not to_wrist:  # upper arm only (short sleeves)
        hx, hy = (sx + hx) / 2, (sy + hy) / 2
    else:  # stop just short of the hand
        dx, dy = hx - sx, hy - sy
        n = max(1.0, math.hypot(dx, dy))
        hx, hy = hx - dx / n * 1.5, hy - dy / n * 1.5
    c.thick_line(sx, sy, hx, hy, width, slot, shade)


def _torso_poly(p: Points, *, top_pad: int = 0, bottom: int | None = None, flare: int = 0) -> list[tuple[float, float]]:
    """The torso trapezoid: shoulders at the top, hips at the bottom (optionally flared, for coats and robes)."""
    b = p.build
    lean = p.pose.lean * p.near_sign
    top = p.torso_top - top_pad
    bot = p.torso_bot if bottom is None else bottom
    cx_top = p.torso_cx
    cx_bot = p.mx(16)
    _ = lean
    return [
        (cx_top - b.half_w + 0.5, top), (cx_top + b.half_w - 0.5, top),
        (cx_bot + b.hip_half + flare - 0.5, bot), (cx_bot - b.hip_half - flare + 0.5, bot),
    ]


def _head_shape(c: Canvas, p: Points, slot: str, shade: int = 3) -> None:
    """Rounded head block (chin narrower than the cranium)."""
    x0, y0 = p.head_x0, p.head_y0
    w, h = p.build.head_w, p.build.head_h
    for y in range(h):
        inset = 2 if y in (0, h - 1) else (1 if y in (1, h - 2) else 0)
        if y == h - 1:
            inset = 3
        c.rect(x0 + inset, y0 + y, x0 + w - 1 - inset, y0 + y, slot, shade)


def _foot(c: Canvas, p: Points, near: bool, slot: str, shade: int = 3, *, toe: int = 2) -> None:
    fx, fy = p.foot_n if near else p.foot_f
    fwd = p.near_sign  # toes point screen-right in front view
    c.rect(fx - 1, fy, fx + 1, fy + 1, slot, shade)
    c.rect(fx + 1, fy + 1, fx + 1 + fwd * toe if fwd > 0 else fx + 1, fy + 1, slot, shade)
    if fwd < 0:
        c.rect(fx - 1 - toe, fy + 1, fx - 1, fy + 1, slot, shade)


def _leg(c: Canvas, p: Points, near: bool, slot: str, *, width: int = 3, shade: int = 3) -> None:
    hx, hy = p.hip_n if near else p.hip_f
    fx, fy = p.foot_n if near else p.foot_f
    if p.pose.kneel:
        s = p.near_sign
        if near:  # front knee up: thigh forward, shin down to the foot
            kx, ky = hx + 3 * s, fy - 3
        else:     # back knee on the ground
            kx, ky = hx - 1 * s, fy + 1
        c.thick_line(hx, hy, kx, ky, width, slot, shade)
        c.thick_line(kx, ky, fx, fy, width, slot, shade)
        return
    c.thick_line(hx, hy, fx, fy, width, slot, shade)


# ------------------------------------------------------------------------------------------------------- body

def body(ears: str) -> Part:
    """Skin: legs, torso, both arms, head, ears and (front only) the face. ears="skeleton" draws bones instead."""
    if ears == "skeleton":
        return _skeleton

    def draw(c: Canvas, p: Points) -> None:
        _leg(c, p, False, "skin")
        _leg(c, p, True, "skin")
        _arm(c, p, False, "skin")
        c.polygon(_torso_poly(p), "skin")
        _arm(c, p, True, "skin")
        # neck
        nx = p.head_cx
        c.rect(nx - 1, p.head_y0 + p.build.head_h - 1, nx + 1, p.torso_top, "skin", 2)
        _head_shape(c, p, "skin")
        _ears(c, p, ears)
        if p.direction == "front":
            _face(c, p)
    return draw


def _skeleton(c: Canvas, p: Points) -> None:
    """Bone body: thin limbs with knobbly joints, a ribcage, a pelvis and a skull with sockets and teeth."""
    for near in (False, True):                       # legs
        hx, hy = p.hip_n if near else p.hip_f
        fx, fy = p.foot_n if near else p.foot_f
        kx, ky = (hx + fx) / 2, (hy + fy) / 2
        c.line(hx, hy, kx, ky, "skin")
        c.line(kx, ky, fx, fy, "skin")
        c.px(kx + 1, ky, "skin", 4)                   # knee
        c.rect(fx - 1, fy + 1, fx + 1, fy + 1, "skin", 2)
    cx = p.mx(16)
    c.rect(int(cx - 2), p.torso_bot - 1, int(cx + 2), p.torso_bot, "skin", 2)   # pelvis
    c.line(p.torso_cx, p.torso_top, cx, p.torso_bot - 1, "skin", 2)            # spine
    for near in (False, True):                       # arms
        sx, sy = p.shoulder_n if near else p.shoulder_f
        hx, hy = p.hand_n if near else p.hand_f
        ex, ey = (sx + hx) / 2, (sy + hy) / 2
        c.line(sx, sy, ex, ey, "skin")
        c.line(ex, ey, hx, hy, "skin")
        c.px(ex, ey, "skin", 4)                       # elbow
    for i, y in enumerate(range(p.torso_top + 1, p.torso_top + 6, 2)):          # ribs
        half = p.build.half_w - 1 - (i // 2)
        c.rect(p.torso_cx - half, y, p.torso_cx + half, y, "skin", 3)
    nx = p.head_cx
    c.rect(nx, p.head_y0 + p.build.head_h - 1, nx, p.torso_top, "skin", 2)    # neck bone
    # skull: a little narrower than a head, with a jaw
    x0, y0, w = p.head_x0 + 1, p.head_y0, p.build.head_w - 2
    for y in range(8):
        inset = 2 if y == 0 else (1 if y in (1, 7) else 0)
        c.rect(x0 + inset, y0 + y, x0 + w - 1 - inset, y0 + y, "skin")
    c.rect(x0 + 2, y0 + 8, x0 + w - 3, y0 + 9, "skin", 2)                      # jaw
    if p.direction == "front":
        for ex in (x0 + 2, x0 + 6):                                              # sockets with a glint
            c.rect(ex, y0 + 4, ex + 1, y0 + 5, "skin", 0)
            c.px(ex + 1, y0 + 5, "eyes", 4)
        c.px(x0 + 5, y0 + 6, "skin", 0)                                          # nose
        for tx in range(x0 + 3, x0 + w - 3, 2):                                  # teeth
            c.px(tx, y0 + 8, "skin", 4)
    else:
        c.px(x0 + 3, y0 + 2, "skin", 2)                                          # a crack
        c.px(x0 + 4, y0 + 3, "skin", 2)


def _ears(c: Canvas, p: Points, kind: str) -> None:
    x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
    ey = y0 + 5
    far_x = x0 - 1 if p.direction == "front" else x0 + w
    near_x = x0 + w if p.direction == "front" else x0 - 1
    s = -1 if p.direction == "front" else 1  # outward direction for the far ear
    if kind == "human":
        c.rect(far_x, ey, far_x, ey + 1, "skin", 2)
        if p.direction == "back":
            c.rect(near_x, ey, near_x, ey + 1, "skin", 2)
    elif kind == "elf":
        n = 4 if p.direction == "front" else 3
        for i in range(n):
            dy = i if p.direction == "front" else i // 2
            c.px(far_x + s * i, ey + 1 - dy, "skin", 3 if i < 3 else 4)
            c.px(far_x + s * i, ey + 2 - dy, "skin", 2)
        if p.direction == "back":
            for i in range(3):
                c.px(near_x - s * i, ey + 1 - (i // 2), "skin", 3)
                c.px(near_x - s * i, ey + 2 - (i // 2), "skin", 2)
    elif kind == "goblin":
        for i in range(5):
            c.px(far_x + s * i, ey - 1 + (i // 2), "skin", 3)
            c.px(far_x + s * i, ey + (i // 2), "skin", 2)
        c.px(far_x + s * 5, ey + 2, "skin", 2)
        if p.direction == "front":
            c.px(near_x, ey, "skin", 3)
            c.px(near_x, ey + 1, "skin", 2)
            c.px(near_x + 1, ey + 1, "skin", 2)
        else:
            for i in range(5):
                c.px(near_x - s * i, ey - 1 + (i // 2), "skin", 3)
                c.px(near_x - s * i, ey + (i // 2), "skin", 2)


def _face(c: Canvas, p: Points) -> None:
    x0, y0 = p.head_x0, p.head_y0
    hurt = p.pose.lean < 0 and p.pose.bob > 0
    down = p.pose.kneel
    ey = y0 + 5
    for ex in (x0 + 4, x0 + 8):  # 3/4 view: the face sits a little screen-right
        if down:
            c.px(ex, ey + 1, "eyes", 0)            # closed
        elif hurt:
            c.px(ex, ey, "eyes", 0)
            c.px(ex, ey + 1, "eyes", 1)
        else:
            c.px(ex, ey, "eyes", 0)
            c.px(ex, ey + 1, "eyes", 3)
    c.px(x0 + 6, y0 + 8, "skin", 1)               # mouth
    c.px(x0 + 9, y0 + 7, "skin", 4)               # cheek light


def hands(kind: str) -> Part:
    slot, shade = {"bare": ("skin", 3), "gloves": ("leather", 3), "gauntlets": ("metal", 3)}[kind]

    def draw(c: Canvas, p: Points) -> None:
        for near in (False, True):
            hx, hy = p.hand_n if near else p.hand_f
            c.rect(hx - 1, hy - 1, hx, hy, slot, shade)
            if kind == "gauntlets":
                c.px(hx - 1, hy - 1, slot, 4)
    return draw


# -------------------------------------------------------------------------------------------- legs and feet

def legs(kind: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        if kind == "pants":
            _leg(c, p, False, "cloth2")
            _leg(c, p, True, "cloth2")
            c.polygon(_hips(p), "cloth2")
        elif kind == "greaves":
            _leg(c, p, False, "cloth2")
            _leg(c, p, True, "cloth2")
            c.polygon(_hips(p), "cloth2")
            for near in (False, True):  # plate on the shins and a knee cop
                fx, fy = p.foot_n if near else p.foot_f
                hx, hy = p.hip_n if near else p.hip_f
                kx, ky = (hx + fx) / 2, (hy + fy) / 2
                c.thick_line(kx, ky, fx, fy - 1, 3, "metal")
                c.rect(int(kx) - 1, int(ky) - 1, int(kx) + 1, int(ky), "metal", 4)
        elif kind == "shorts":
            for near in (False, True):
                hx, hy = p.hip_n if near else p.hip_f
                fx, fy = p.foot_n if near else p.foot_f
                c.thick_line(hx, hy, (hx * 2 + fx) / 3, (hy * 2 + fy) / 3, 3, "cloth2")
            c.polygon(_hips(p), "cloth2")
        elif kind == "skirt":  # robe skirt: a trapezoid to the ankles that follows the feet
            b = p.build
            cx = p.mx(16)
            spread = (p.foot_n[0] - p.foot_f[0]) * p.near_sign
            lo = max(p.foot_n[1], p.foot_f[1]) - 1
            c.polygon([(cx - b.hip_half - 0.5, p.torso_bot - 1), (cx + b.hip_half + 0.5, p.torso_bot - 1),
                       (cx + b.hip_half + 1.5 + max(0, spread) / 2, lo), (cx - b.hip_half - 1.5 - max(0, spread) / 2, lo)],
                      "cloth1")
            c.rect(int(cx - b.hip_half - 1), lo, int(cx + b.hip_half + 1), lo, "trim")
    return draw


def _hips(p: Points) -> list[tuple[float, float]]:
    b = p.build
    cx = p.mx(16)
    return [(cx - b.hip_half - 0.5, p.torso_bot - 1), (cx + b.hip_half + 0.5, p.torso_bot - 1),
            (cx + b.hip_half + 0.5, p.torso_bot + 1), (cx - b.hip_half - 0.5, p.torso_bot + 1)]


def feet(kind: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        for near in (False, True):
            if kind == "boots":
                _foot(c, p, near, "leather")
                fx, fy = p.foot_n if near else p.foot_f
                c.rect(fx - 1, fy - 2, fx + 1, fy - 1, "leather")
                c.px(fx - 1, fy - 2, "leather", 4)
            elif kind == "sabatons":
                _foot(c, p, near, "metal")
                fx, fy = p.foot_n if near else p.foot_f
                c.rect(fx - 1, fy - 2, fx + 1, fy - 1, "metal", 4)
            elif kind == "sandals":
                _foot(c, p, near, "skin")
                fx, fy = p.foot_n if near else p.foot_f
                c.rect(fx - 1, fy + 1, fx + 1, fy + 1, "leather")
    return draw


# ---------------------------------------------------------------------------------------------------- torso

def torso(kind: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        b = p.build
        poly = _torso_poly(p)
        if kind == "tunic":
            _arm(c, p, False, "cloth1", to_wrist=False, width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 1, flare=1), "cloth1")
            _arm(c, p, True, "cloth1", to_wrist=False, width=3)
            _belt(c, p)
            c.px(p.torso_cx, p.torso_top + 1, "cloth1", 1)  # collar dip
        elif kind == "vest":  # leather vest over a shirt
            _arm(c, p, False, "cloth1", width=2)
            c.polygon(poly, "cloth1")
            _arm(c, p, True, "cloth1", width=2)
            c.polygon(_torso_poly(p, bottom=p.torso_bot), "leather")
            cx = p.torso_cx
            c.rect(cx, p.torso_top + 1, cx, p.torso_bot - 2, "cloth1", 4)  # open front
            _belt(c, p)
        elif kind == "chain":
            _arm(c, p, False, "metal", width=3, to_wrist=False)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 2, flare=1), "metal")
            _arm(c, p, True, "metal", width=3, to_wrist=False)
            for y in range(p.torso_top, p.torso_bot + 3):
                for x in range(FRAME_W):
                    if c.slot[y, x] == c.slots.id("metal") and (x + y) % 2 == 0:
                        c.shade[y, x] = 2
            c.polygon(_torso_poly(p, top_pad=-1, bottom=p.torso_bot - 1), "cloth1")  # surcoat panel
            _belt(c, p)
        elif kind == "plate":
            _arm(c, p, False, "metal", width=3)
            c.polygon(poly, "metal")
            _arm(c, p, True, "metal", width=3)
            # pauldrons
            for near in (False, True):
                sx, sy = p.shoulder_n if near else p.shoulder_f
                c.ellipse(sx + 0.5, sy, 2.6, 2.0, "metal", 4)
                c.px(sx, sy - 1, "trim")
            cx = p.torso_cx
            c.rect(cx - 2, p.torso_top + 2, cx + 2, p.torso_top + 2, "trim")  # gorget line
            c.rect(cx, p.torso_top + 3, cx, p.torso_bot - 2, "metal", 4)      # ridge
            c.polygon(_torso_poly(p, top_pad=-(p.torso_bot - p.torso_top - 2), bottom=p.torso_bot + 3, flare=1), "cloth1")  # tabard skirt
            _belt(c, p, slot="trim")
        elif kind == "robe":
            _arm(c, p, False, "cloth1", width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 1, flare=1), "cloth1")
            _arm(c, p, True, "cloth1", width=3)
            for near in (False, True):  # wide cuffs
                hx, hy = p.hand_n if near else p.hand_f
                sx, sy = p.shoulder_n if near else p.shoulder_f
                dx, dy = hx - sx, hy - sy
                n = max(1.0, math.hypot(dx, dy))
                cx_, cy_ = hx - dx / n * 2.5, hy - dy / n * 2.5
                c.rect(int(cx_) - 1, int(cy_) - 1, int(cx_) + 1, int(cy_), "trim")
            cx = p.torso_cx
            c.line(cx - 2, p.torso_top, cx + 1, p.torso_top + 4, "trim")     # wrap collar
            c.line(cx + 2, p.torso_top, cx, p.torso_top + 2, "trim")
            _belt(c, p, slot="cloth2")
        elif kind == "vestments":
            _arm(c, p, False, "cloth1", width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 1, flare=1), "cloth1")
            _arm(c, p, True, "cloth1", width=3)
            cx = p.torso_cx
            c.rect(cx - 1, p.torso_top, cx + 1, p.torso_bot + 1, "cloth2")    # stole
            c.rect(cx - 1, p.torso_top + 3, cx + 1, p.torso_top + 3, "trim")
            c.px(cx, p.torso_top + 4, "trim", 4)                                 # holy symbol
            _belt(c, p, slot="trim")
        elif kind == "coat":
            _arm(c, p, False, "cloth1", width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 4, flare=2), "cloth1")
            _arm(c, p, True, "cloth1", width=3)
            cx = p.torso_cx
            c.rect(cx, p.torso_top + 1, cx, p.torso_bot + 3, "cloth1", 1)      # opening
            c.rect(cx - 2, p.torso_top, cx + 2, p.torso_top, "cloth2")          # collar
            _belt(c, p)
        elif kind == "rags":
            _arm(c, p, False, "cloth1", to_wrist=False, width=2)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 2, flare=1), "cloth1")
            _arm(c, p, True, "cloth1", to_wrist=False, width=2)
            cid = c.slots.id("cloth1")
            for x in range(FRAME_W):  # torn hem: notches of 1-2 px
                for y in (p.torso_bot + 2, p.torso_bot + 1):
                    if c.slot[y, x] == cid and _hash(x, y) % 5 < (2 if y == p.torso_bot + 2 else 1):
                        c.clear(x, y)
            _speckle(c, "cloth1", 0.08, 2)
            _speckle(c, "cloth1", 0.03, 4, salt=7)
            c.line(p.torso_cx - 3, p.torso_top + 1, p.torso_cx + 3, p.torso_bot, "leather")  # strap
            c.px(p.torso_cx - 1, p.torso_top + 3, "cloth2", 3)                                    # patch
            c.px(p.torso_cx - 2, p.torso_top + 3, "cloth2", 4)
        _ = b
    return draw


def _hash(x: int, y: int, salt: int = 0) -> int:
    """A stable per-pixel hash for textures: the same pixel gets the same value every frame and every build."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    return h ^ (h >> 16)


def _speckle(c: Canvas, slot: str, density: float, shade: int, salt: int = 0) -> None:
    """Sprinkles a fixed fraction of a slot's base-shade pixels with another shade (rough cloth, stone, bark)."""
    sid = c.slots.id(slot)
    for y in range(c.h):
        for x in range(c.w):
            if c.slot[y, x] == sid and c.shade[y, x] == 3 and (_hash(x, y, salt) % 1000) < density * 1000:
                c.shade[y, x] = shade


def _belt(c: Canvas, p: Points, slot: str = "leather") -> None:
    b = p.build
    cx = p.mx(16)
    y = p.torso_bot - 1
    c.rect(int(cx - b.hip_half), y, int(cx + b.hip_half), y, slot)
    c.px(cx, y, "metal", 4)


# ----------------------------------------------------------------------------------------------------- capes

def cape(kind: str) -> Part:
    """Front view: the cloak hangs behind the body (draw this layer *before* the body). Back view: over the body."""
    def draw(c: Canvas, p: Points) -> None:
        b = p.build
        top = p.torso_top
        length = (p.build.foot_y - 1) if kind == "cloak" else (p.torso_bot + 2)
        cx = p.torso_cx
        sway = (p.foot_f[0] - p.mx(16 - 1)) * 0 + (1 if p.pose.bob else 0)
        w_top = b.half_w + 1
        w_bot = b.half_w + (3 if kind == "cloak" else 2)
        c.polygon([(cx - w_top + 0.5, top), (cx + w_top - 0.5, top),
                   (cx + w_bot - 0.5 + sway, length), (cx - w_bot + 0.5 + sway, length)], "cloth1")
        # inner lining shows on the edges in the front view
        if p.direction == "front":
            c.line(cx - w_top, top + 1, cx - w_bot + sway, length, "cloth2")
            c.line(cx + w_top - 1, top + 1, cx + w_bot - 1 + sway, length, "cloth2")
        else:
            for k in (-3, 0, 3):  # folds: a shadow line with a lit edge beside it
                c.line(cx + k, top + 3, cx + k + sway, length - 1, "cloth1", 2)
                c.line(cx + k - 1, top + 4, cx + k - 1 + sway, length - 2, "cloth1", 4)
            c.rect(cx - 2, top, cx + 2, top, "trim")  # clasp line
    return draw


# ------------------------------------------------------------------------------------------------------ hair

def hair(style: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
        x1 = x0 + w - 1
        front = p.direction == "front"
        # cap over the cranium (rows 0..3 front, 0..7 back)
        cap_rows = 3 if front else 7
        for y in range(cap_rows + 1):
            inset = 2 if y == 0 else (1 if y == 1 else 0)
            c.rect(x0 + inset - (0 if y else 0), y0 + y - 1 if y == 0 else y0 + y, x1 - inset, y0 + y - 1 if y == 0 else y0 + y, "hair")
        c.rect(x0 + 2, y0 - 1, x1 - 2, y0 - 1, "hair")
        if front:
            # fringe: jagged bangs falling on the forehead, swept screen-left
            for i, x in enumerate(range(x0 + 1, x1)):
                drop = (1, 2, 1, 2, 2, 1, 1, 2, 1, 1)[i % 10]
                c.rect(x, y0 + 3, x, y0 + 2 + drop, "hair")
            c.rect(x0, y0 + 2, x0, y0 + 6, "hair")       # far sideburn
            c.rect(x1, y0 + 2, x1, y0 + 4, "hair")       # near sideburn
        if style == "messy":
            for dx, dy in ((1, -2), (4, -2), (7, -2), (9, -1), (-1, 0)):
                c.px(x0 + dx, y0 + dy, "hair", 4)
        elif style == "long":
            if front:
                c.rect(x0 - 1, y0 + 2, x0, y0 + 11, "hair")
                c.rect(x1, y0 + 4, x1 + 1, y0 + 10, "hair")
            else:
                c.rect(x0, y0 + 7, x1, y0 + 13, "hair")
                c.rect(x0 + 1, y0 + 14, x1 - 1, y0 + 14, "hair")
        elif style == "ponytail":
            tail_x = x0 - 1 if front else x1 + 1
            if front:
                c.rect(tail_x - 1, y0 + 3, tail_x, y0 + 9, "hair")
            else:
                c.rect(p.head_cx - 1, y0 + 6, p.head_cx + 1, y0 + 12, "hair")
                c.px(p.head_cx, y0 + 6, "cloth2")  # tie
        elif style == "bun":
            cx = p.head_cx + (-2 if front else 0)
            c.ellipse(cx + 0.5, y0 - 1.5, 2.5, 2.2, "hair")
        elif style == "mohawk":
            # shaved sides: replace the cap with a crest
            for y in range(y0 - 1, y0 + cap_rows + 1):
                for x in range(x0, x1 + 1):
                    if c.slot[y, x] == c.slots.id("hair") and abs(x - p.head_cx) > 1:
                        c.clear(x, y)
            for i in range(5):
                c.rect(p.head_cx - 1, y0 - 3 + i, p.head_cx + 1, y0 - 3 + i, "hair", 3 + (1 if i == 0 else 0))
        elif style == "braids":
            for bx in ((x0 - 1, x1 + 1) if front else (x0, x1)):
                for i in range(7):
                    c.px(bx, y0 + 4 + i, "hair", 3 if i % 2 else 2)
                c.px(bx, y0 + 11, "cloth2")
        elif style == "short":
            pass
        elif style == "cropped":  # very short, for under helmets and hoods
            if front:
                for x in range(x0 + 1, x1):
                    if c.slot[y0 + 4, x] == c.slots.id("hair"):
                        c.clear(x, y0 + 4)
                    if c.slot[y0 + 5, x] == c.slots.id("hair"):
                        c.clear(x, y0 + 5)
        _hair_texture(c, p)
    return draw


def _hair_texture(c: Canvas, p: Points) -> None:
    """A shine across the crown and strand lines on hair that hangs (back view, long locks), so large hair areas
    read as hair rather than a block. The fringe over the face is left clean."""
    hid = c.slots.id("hair")
    x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
    front = p.direction == "front"
    for i, x in enumerate(range(x0 + 2, x0 + w - 3)):          # crown shine, on the lit side
        y = y0 + (1 if i in (0, 5) else 0)
        if 0 <= y < c.h and c.slot[y, x] == hid and c.flags[y, x] == 0:
            c.shade[y, x] = 4 if i < 4 else 3
    top = y0 + (7 if front else 3)
    for x in range(max(0, x0 - 2), min(c.w, x0 + w + 2)):
        k = (x - x0) % 3
        for y in range(top, min(c.h, y0 + 16)):
            if c.slot[y, x] != hid or c.flags[y, x]:
                continue
            if k == 1:
                c.shade[y, x] = 2                                   # strand shadow
            elif k == 2 and y < top + 3:
                c.shade[y, x] = 4                                   # sheen under the crown


def beard(style: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        if p.direction != "front":
            # only the edges show from behind
            x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
            c.rect(x0, y0 + 7, x0, y0 + 9, "hair", 2)
            c.rect(x0 + w - 1, y0 + 7, x0 + w - 1, y0 + 9, "hair", 2)
            return
        x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
        x1 = x0 + w - 1
        if style == "short":
            c.rect(x0 + 1, y0 + 8, x1 - 1, y0 + 9, "hair")
            c.rect(x0 + 3, y0 + 10, x1 - 3, y0 + 10, "hair")
            c.rect(x0 + 4, y0 + 7, x0 + 8, y0 + 7, "hair", 2)          # moustache
        elif style == "full":
            c.rect(x0, y0 + 6, x0 + 1, y0 + 9, "hair")                # sideburn into beard
            c.rect(x1 - 1, y0 + 6, x1, y0 + 9, "hair")
            c.rect(x0 + 3, y0 + 7, x0 + 9, y0 + 7, "hair", 2)          # moustache
            c.rect(x0 + 1, y0 + 8, x1 - 1, y0 + 10, "hair")
            c.rect(x0 + 2, y0 + 11, x1 - 2, y0 + 12, "hair")
            c.rect(x0 + 4, y0 + 13, x1 - 4, y0 + 13, "hair")
            for i in range(3):                                         # strand lines
                c.px(x0 + 3 + i * 2, y0 + 10 + (i % 2), "hair", 2)
        elif style == "braided":
            c.rect(x0 + 1, y0 + 7, x1 - 1, y0 + 9, "hair")
            for i in range(5):
                c.px(p.head_cx + 1, y0 + 10 + i, "hair", 3 if i % 2 else 2)
            c.px(p.head_cx + 1, y0 + 15, "metal", 4)
        # keep the mouth readable
        if style != "full":
            c.px(x0 + 6, y0 + 8, "skin", 1)
    return draw


def horns(style: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
        for hx, s in ((x0 + 2, -1), (x0 + w - 3, 1)):
            if style == "curled":
                pts = [(0, 0), (s * 1, -1), (s * 2, -2), (s * 3, -2), (s * 4, -1), (s * 4, 0)]
            else:  # "straight": swept back
                pts = [(0, 0), (s * 0, -1), (s * 1, -2), (s * 1, -3), (s * 2, -4)]
            for i, (dx, dy) in enumerate(pts):
                c.px(hx + dx, y0 + dy, "trim", 2 if i < 2 else 3)
    return draw


# ---------------------------------------------------------------------------------------------------- headgear

def headgear(kind: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
        x1 = x0 + w - 1
        front = p.direction == "front"
        if kind == "open_helm":
            for y in range(5):
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - (1 if y >= 2 else 0), y0 - 1 + y, x1 - inset + (1 if y >= 2 else 0), y0 - 1 + y, "metal")
            c.rect(x0 - 1, y0 + 4, x1 + 1, y0 + 4, "metal", 2)       # brim
            if front:
                c.rect(p.head_cx, y0 + 4, p.head_cx, y0 + 6, "metal")  # nasal
                c.rect(x0 - 1, y0 + 5, x0, y0 + 8, "metal")           # far cheek guard
            else:
                c.rect(x0, y0 + 4, x1, y0 + 8, "metal")               # back of the helm
            c.px(p.head_cx, y0 - 2, "cloth1")                          # plume base
            c.rect(p.head_cx - 1, y0 - 4, p.head_cx, y0 - 3, "cloth1", 4)
        elif kind == "great_helm":
            for y in range(p.build.head_h + 1):
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - 1, y0 - 1 + y, x1 - inset + 1, y0 - 1 + y, "metal")
            gy = y0 + p.build.head_h                                    # gorget hides the neck
            c.rect(x0 + 2, gy, x1 - 2, gy + 1, "metal", 2)
            if front:
                c.rect(x0 + 3, y0 + 4, x1 - 1, y0 + 4, "metal", 0)    # visor slit
                for x in range(x0 + 4, x1 - 1, 2):
                    c.px(x, y0 + 6, "metal", 1)                        # breaths
            c.rect(x0 - 1, y0 + 2, x1 + 1, y0 + 2, "trim")
        elif kind == "hood":
            c.rect(x0 + 2, y0 - 2, x1 - 2, y0 - 2, "cloth1")       # covers spiky hair
            for y in range(p.build.head_h + 1):
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - 1, y0 - 1 + y, x1 - inset + 1, y0 - 1 + y, "cloth1")
            if front:  # the face opening
                for y in range(y0 + 3, y0 + 10):
                    l = x0 + 2 if y < y0 + 9 else x0 + 3
                    r = x1 - 1 if y < y0 + 9 else x1 - 2
                    for x in range(l, r + 1):
                        c.clear(x, y)
                c.rect(x0 + 2, y0 + 2, x1 - 1, y0 + 2, "cloth1", 2)   # shadowed brow
            else:
                c.rect(p.head_cx - 1, y0 + 9, p.head_cx + 1, y0 + 11, "cloth1")
        elif kind == "wizard_hat":
            c.rect(x0 - 2, y0 + 1, x1 + 2, y0 + 1, "cloth1", 2)        # brim
            c.rect(x0 - 1, y0 + 2, x1 + 1, y0 + 2, "cloth1", 1)
            for i, (l, r) in enumerate([(1, 9), (2, 8), (2, 7), (3, 7), (3, 6), (4, 6), (5, 6), (5, 5)]):
                c.rect(x0 + l, y0 - i, x0 + r, y0 - i, "cloth1")
            c.px(x0 + 6, y0 - 8, "cloth1")
            c.px(x0 + 7, y0 - 9, "cloth1")
            c.px(x0 + 8, y0 - 9, "cloth1", 4)
            c.rect(x0 + 1, y0, x0 + 9, y0, "trim")                     # band
        elif kind == "circlet":
            c.rect(x0, y0 + 2, x1, y0 + 2, "metal")
            if front:
                c.px(p.head_cx, y0 + 2, "magic", 4)
                c.px(p.head_cx, y0 + 1, "metal", 4)
        elif kind == "bandana":
            c.rect(x0, y0, x1, y0 + 2, "cloth1")
            c.rect(x0 + 1, y0 - 1, x1 - 1, y0 - 1, "cloth1")
            knot_x = x0 - 1 if front else x1 + 1
            c.rect(knot_x - (1 if front else 0), y0 + 2, knot_x + (0 if front else 1), y0 + 4, "cloth1", 2)
        elif kind == "crown":
            c.rect(x0 + 1, y0, x1 - 1, y0 + 1, "trim")
            for x in range(x0 + 1, x1, 2):
                c.px(x, y0 - 1, "trim", 4)
            if front:
                c.px(p.head_cx, y0 + 1, "magic", 4)
    return draw


# ------------------------------------------------------------------------------------------- weapons, off-hand

def _along(p: Points, dist: float) -> tuple[float, float]:
    a = math.radians(p.weapon)
    return p.hand_n[0] + math.sin(a) * dist, p.hand_n[1] - math.cos(a) * dist


def weapon(kind: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        hx, hy = p.hand_n
        if kind == "sword":
            pommel = _along(p, -3)
            c.line(*pommel, *_along(p, -1), "leather")                       # grip
            gx, gy = _along(p, 0)
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)                                # guard perpendicular
            c.line(gx - px_ * 2, gy - py_ * 2, gx + px_ * 2, gy + py_ * 2, "trim")
            c.thick_line(*_along(p, 1), *_along(p, 10), 2 if abs(math.sin(a)) < 0.5 else 1, "metal", 4)
            c.line(*_along(p, 1), *_along(p, 10), "metal", 3)
            c.px(*_along(p, 11), "metal", 5)
            c.px(*pommel, "trim")
        elif kind == "greatsword":
            c.line(*_along(p, -4), *_along(p, 0), "leather")
            gx, gy = _along(p, 1)
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)
            c.line(gx - px_ * 3, gy - py_ * 3, gx + px_ * 3, gy + py_ * 3, "trim")
            c.thick_line(*_along(p, 2), *_along(p, 15), 2, "metal", 4)
            c.line(*_along(p, 2), *_along(p, 15), "metal", 3)
        elif kind == "axe":
            c.thick_line(*_along(p, -3), *_along(p, 10), 1, "wood")
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a) * p.near_sign, math.sin(a) * p.near_sign
            for d in range(6, 11):
                ex, ey = _along(p, d)
                span = 3 if d in (7, 8, 9) else 2
                c.line(ex, ey, ex + px_ * span, ey + py_ * span, "metal", 4 if d == 8 else 3)
        elif kind == "mace":
            c.thick_line(*_along(p, -2), *_along(p, 7), 1, "wood")
            mx, my = _along(p, 9)
            c.ellipse(mx + 0.5, my + 0.5, 2.2, 2.2, "metal")
            c.px(mx, my - 2, "metal", 5)
        elif kind == "dagger":
            c.line(*_along(p, -2), *_along(p, 0), "leather")
            c.line(*_along(p, 1), *_along(p, 5), "metal", 4)
            gx, gy = _along(p, 0)
            c.px(gx - 1, gy, "trim")
            c.px(gx + 1, gy, "trim")
        elif kind == "spear":
            c.line(*_along(p, -9), *_along(p, 12), "wood")
            c.thick_line(*_along(p, 12), *_along(p, 15), 2, "metal", 4)
            c.px(*_along(p, 16), "metal", 5)
        elif kind == "staff":
            # carried upright: drawn straight up from the hand, a little tilted by the pose
            ang = p.weapon * 0.35
            a = math.radians(ang)
            top = (hx + math.sin(a) * 13, hy - math.cos(a) * 13)
            bot = (hx - math.sin(a) * 9, hy + math.cos(a) * 9)
            c.line(*bot, *top, "wood")
            c.ellipse(top[0] + 0.5, top[1] - 0.5, 2.0, 2.0, "magic")
            c.px(top[0] - 1, top[1] - 2, "magic", 5)
            c.px(top[0] + 1, top[1] + 1, "wood", 2)
        elif kind == "bow":
            # held upright in the near hand; when the pose aims, the string runs back to the far hand
            s = p.near_sign
            pts = [(1, -9), (2, -8), (2, -6), (3, -4), (3, -2), (3, 0), (3, 2), (3, 4), (2, 6), (2, 8), (1, 9)]
            for dx, dy in pts:
                c.px(hx + s * dx, hy + dy, "wood", 3)
            c.px(hx + s * 3, hy - 1, "leather", 3)            # grip wrap
            top, bot = (hx + s * 1, hy - 9), (hx + s * 1, hy + 9)
            if p.pose.aim:
                fx, fy = p.hand_f
                c.line(*top, fx, fy, "cloth2", 4)
                c.line(fx, fy, *bot, "cloth2", 4)
                c.line(fx, fy, hx + s * 5, fy, "wood", 4)        # the arrow
                c.px(hx + s * 6, fy, "metal", 5)
            else:
                c.line(*top, *bot, "cloth2", 4)
        elif kind == "tome":
            fx, fy = p.hand_f
            c.rect(fx - 2, fy - 3, fx + 2, fy + 1, "cloth1")
            c.rect(fx - 2, fy - 3, fx - 2, fy + 1, "trim")
            c.rect(fx - 1, fy - 3, fx + 2, fy - 3, "cloth2", 5)  # page edge
            c.px(fx + 1, fy - 1, "magic", 4)
        elif kind == "wand":
            c.line(*_along(p, 0), *_along(p, 5), "wood")
            c.px(*_along(p, 6), "magic", 5)
    return draw


def offhand(kind: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        fx, fy = p.hand_f
        if kind == "round_shield":
            c.ellipse(fx + 0.5, fy - 0.5, 4.2, 4.2, "wood")
            c.ellipse(fx + 0.5, fy - 0.5, 4.2, 4.2, "wood")
            for ang in range(0, 360, 20):
                a = math.radians(ang)
                c.px(fx + 0.5 + math.cos(a) * 4, fy - 0.5 + math.sin(a) * 4, "metal", 3)
            c.rect(fx, fy - 1, fx + 1, fy, "metal", 5)  # boss
        elif kind == "kite_shield":
            c.polygon([(fx - 4.5, fy - 6), (fx + 5.5, fy - 6), (fx + 5.5, fy + 1), (fx + 0.5, fy + 7), (fx - 4.5, fy + 1)], "trim")
            c.polygon([(fx - 3.5, fy - 5), (fx + 4.5, fy - 5), (fx + 4.5, fy + 0.5), (fx + 0.5, fy + 5.5), (fx - 3.5, fy + 0.5)], "cloth1")
            c.rect(fx, fy - 4, fx + 1, fy + 3, "trim", 4)  # charge: a pale stripe
        elif kind == "buckler":
            c.ellipse(fx + 0.5, fy - 0.5, 2.6, 2.6, "metal")
            c.px(fx, fy - 1, "metal", 5)
        elif kind == "lantern":
            c.line(fx, fy, fx, fy + 2, "metal", 2)
            c.rect(fx - 1, fy + 3, fx + 1, fy + 6, "metal", 2)
            c.rect(fx, fy + 4, fx, fy + 5, "magic", 5)
        elif kind == "orb":
            c.ellipse(fx + 0.5, fy - 1.5, 2.2, 2.2, "magic")
            c.px(fx - 1, fy - 3, "magic", 5)
    return draw
