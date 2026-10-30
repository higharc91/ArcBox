"""Character layer parts. Each part draws one layer for one frame from the rig's keypoints.

Slots used (shared table, so every layer's ramp map agrees):
  skin, eyes, hair, cloth1 (primary), cloth2 (secondary), trim, metal (armour), leather, wood, magic, horn, paint,
  blade (weapon metal, separate so loot can upgrade a weapon without recolouring the armour)

Conventions: draw the far side first, then the body of the part, then the near side, so the near limb overlaps.
Shading is applied per layer afterwards (shade_form). Outlines are applied once, to the composed character.
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
    if b.feminine and not flare:
        # a waist two thirds of the way down, then out to the hips
        wy = top + (bot - top) * 0.62
        wx = (cx_top + cx_bot) / 2
        ww = min(b.half_w, b.hip_half) - 0.5
        return [
            (cx_top - b.half_w + 0.5, top), (cx_top + b.half_w - 0.5, top), (wx + ww - 0.5, wy),
            (cx_bot + b.hip_half + 0.5, bot), (cx_bot - b.hip_half - 0.5, bot), (wx - ww + 0.5, wy),
        ]
    return [
        (cx_top - b.half_w + 0.5, top), (cx_top + b.half_w - 0.5, top),
        (cx_bot + b.hip_half + flare - 0.5, bot), (cx_bot - b.hip_half - flare + 0.5, bot),
    ]


def _head_shape(c: Canvas, p: Points, slot: str, shade: int = 3) -> None:
    """Rounded head block (chin narrower than the cranium)."""
    x0, y0 = p.head_x0, p.head_y0
    w, h = p.build.head_w, p.build.head_h
    soft = p.build.feminine            # a softer, narrower chin
    for y in range(h):
        inset = 2 if y in (0, h - 1) else (1 if y in (1, h - 2) else 0)
        if y == h - 1:
            inset = 4 if soft else 3
        elif y == h - 2 and soft:
            inset = 2
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
    if p.pose.sit:                      # seated: the thigh runs forward level, the shin drops to the foot
        c.thick_line(hx, hy, fx, hy, width, slot, shade)
        c.thick_line(fx, hy, fx, fy, width, slot, shade)
        return
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

def body(kind: str) -> Part:
    """Skin: legs, torso, both arms, head, ears and (front only) the face.

    kind is the race's body: human, elf, half_elf, gnome, goblin, orc (tusks, heavy brow), dragon (a scaled head
    with a snout) or skeleton (bones instead of skin)."""
    if kind == "skeleton":
        return _skeleton

    def draw(c: Canvas, p: Points) -> None:
        _leg(c, p, False, "skin")
        _leg(c, p, True, "skin")
        _arm(c, p, False, "skin")
        c.polygon(_torso_poly(p), "skin")
        _arm(c, p, True, "skin")
        nx = p.head_cx
        neck = 2 if kind in ("dragon", "orc") else 1
        c.rect(nx - neck, p.head_y0 + p.build.head_h - 1, nx + neck, p.torso_top, "skin", 2)
        if kind == "dragon":
            _dragon_head(c, p)
            return
        _head_shape(c, p, "skin")
        _ears(c, p, {"orc": "human", "gnome": "half_elf"}.get(kind, kind))
        if p.direction == "front":
            _face(c, p)
            x0, y0 = p.head_x0 + _face_dx(p), p.head_y0
            if kind == "orc":
                c.rect(x0 + 3, y0 + 4, x0 + 9, y0 + 4, "skin", 2)          # heavy brow
                for tx in (x0 + 5, x0 + 7):                                # tusks over the upper lip
                    c.px(tx, y0 + 8, "horn", 4)
                    c.px(tx, y0 + 7, "horn", 5)
            elif kind == "gnome":
                c.px(x0 + 9, y0 + 6, "skin", 4)                            # a proud nose
                c.px(x0 + 10, y0 + 7, "skin", 3)
                c.px(x0 + 9, y0 + 7, "skin", 3)
                c.px(x0 + 3, y0 + 7, "skin", 4)                            # rosy cheeks
    return draw


def _face_dx(p: Points) -> int:
    """Faces are laid out for an 11 px head; wider heads (the large build) shift the features to stay centred."""
    return (p.build.head_w - 11) // 2


def _dragon_head(c: Canvas, p: Points) -> None:
    """Dragonborn: a rounded skull with a snout jutting forward (screen-right in the front view), a brow ridge over
    the eyes, scale flecks on the crown and a row of spines down the back of the head."""
    x0, y0, w, h = p.head_x0, p.head_y0, p.build.head_w, p.build.head_h
    x1 = x0 + w - 1
    _head_shape(c, p, "skin")
    if p.direction == "front":
        # (row, from, to, shade) relative to the near edge of the head: a long, flat muzzle and a lower jaw
        for dy, x_a, x_b, sh in ((4, -2, 1, 4), (5, -2, 4, 4), (6, -2, 4, 3), (7, -2, 4, 3), (8, -3, 3, 2),
                                 (9, -4, 1, 2)):
            c.rect(x1 + x_a, y0 + dy, x1 + x_b, y0 + dy, "skin", sh)
        c.px(x1 + 4, y0 + 5, "skin", 1)                                        # nostril
        c.rect(x1 - 2, y0 + 8, x1 + 3, y0 + 8, "skin", 1)                      # mouth line
        c.px(x1 + 2, y0 + 9, "horn", 5)                                        # a fang
        c.rect(x0 + 3, y0 + 3, x1 - 1, y0 + 3, "skin", 4)                      # brow ridge
        hurt = p.pose.lean < 0 and p.pose.bob > 0
        for ex in (x0 + 4, x0 + 8):
            if p.pose.kneel or hurt or p.pose.blink:
                c.px(ex, y0 + 5, "eyes", 0)
            else:
                c.px(ex, y0 + 4, "eyes", 4)                                    # bright reptile eyes
                c.px(ex, y0 + 5, "eyes", 1)
        for sx in range(x0 + 2, x1 - 1, 3):                                    # scale flecks on the crown
            c.px(sx, y0 + 1, "skin", 2)
        c.px(x0 + 1, y0 + 6, "skin", 2)                                        # cheek scales
        c.px(x0 + 2, y0 + 7, "skin", 2)
        for i, (dx, dy) in enumerate(((-1, 3), (-2, 4), (-1, 5), (-2, 6))):    # frill at the back of the jaw
            c.px(x0 + dx, y0 + dy, "horn", 3 if i % 2 else 4)
    else:
        c.rect(x0 - 2, y0 + 6, x0, y0 + 7, "skin", 2)                          # the snout, just visible
        for dy in (1, 3, 5, 7, 9):                                             # spines down the back of the skull
            if dy < h:
                c.px(p.head_cx, y0 + dy, "horn", 4)
                c.px(p.head_cx, y0 + dy + 1, "horn", 2)
        for sx in range(x0 + 2, x1 - 1, 3):
            c.px(sx, y0 + 2, "skin", 2)


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
    elif kind == "half_elf":
        c.px(far_x, ey, "skin", 3)
        c.px(far_x + s, ey - 1, "skin", 4)
        c.px(far_x, ey + 1, "skin", 2)
        if p.direction == "back":
            c.px(near_x, ey, "skin", 3)
            c.px(near_x - s, ey - 1, "skin", 4)
            c.px(near_x, ey + 1, "skin", 2)
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
    """Eyes, mouth and a cheek light, laid out in 3/4 view (the face sits a little screen-right). Expressions only
    move eyes and mouth: brows would sit under most fringes, so the eyes carry the mood."""
    x0, y0 = p.head_x0 + _face_dx(p), p.head_y0
    hurt = p.pose.lean < 0 and p.pose.bob > 0
    down = p.pose.kneel or p.pose.blink
    mood = p.pose.expression
    ey = y0 + 5
    for ex in (x0 + 4, x0 + 8):
        if down:
            c.px(ex, ey + 1, "eyes", 0)            # closed
        elif mood == "happy":                      # closed in a smile: little arches
            c.px(ex, ey, "eyes", 0)
            c.px(ex - 1, ey + 1, "skin", 1)
            c.px(ex + 1, ey + 1, "skin", 1)
        elif mood == "angry":                      # narrowed, with a hard lid line slanting in
            c.px(ex, ey + 1, "eyes", 0)
            c.px(ex, ey, "skin", 1)
            c.px(ex + (1 if ex < x0 + 6 else -1), ey, "skin", 0)
        elif mood == "sad":                        # looking down, a heavy lid
            c.px(ex, ey, "skin", 2)
            c.px(ex, ey + 1, "eyes", 0)
            c.px(ex, ey + 2, "eyes", 3)
        elif mood == "surprised":                  # wide open with a catchlight
            c.px(ex, ey - 1, "eyes", 0)
            c.px(ex, ey, "eyes", 0)
            c.px(ex, ey + 1, "eyes", 3)
            c.px(ex + 1, ey - 1, "eyes", 5)
        elif hurt:
            c.px(ex, ey, "eyes", 0)
            c.px(ex, ey + 1, "eyes", 1)
        else:
            c.px(ex, ey, "eyes", 0)
            c.px(ex, ey + 1, "eyes", 3)
    if p.build.feminine and not down and mood not in ("happy", "angry"):
        c.px(x0 + 3, ey - 1, "eyes", 0)            # lashes flick up and out at each outer corner
        c.px(x0 + 9, ey - 1, "eyes", 0)
    my = y0 + 8
    if mood == "happy":                            # a smile, and a little blush
        c.px(x0 + 5, my, "skin", 1)
        c.px(x0 + 6, my + 1, "skin", 1)
        c.px(x0 + 7, my, "skin", 1)
        c.px(x0 + 3, y0 + 7, "skin", 4)
    elif mood == "angry":                          # a hard, wide line
        c.rect(x0 + 5, my, x0 + 7, my, "skin", 0)
    elif mood == "sad":                            # turned down at the corners
        c.px(x0 + 6, my, "skin", 1)
        c.px(x0 + 5, my + 1, "skin", 1)
        c.px(x0 + 7, my + 1, "skin", 1)
    elif mood == "surprised":                      # an O
        c.px(x0 + 6, my, "skin", 0)
        c.px(x0 + 6, my + 1, "skin", 0)
    elif p.build.feminine:
        c.px(x0 + 6, my, "skin", 1)
        c.px(x0 + 7, my, "skin", 2)                # fuller lips
    else:
        c.px(x0 + 6, my, "skin", 1)                # mouth
    c.px(x0 + 9, y0 + 7, "skin", 4)                # cheek light


def hands(kind: str) -> Part:
    slot, shade = {"bare": ("skin", 3), "gloves": ("leather", 3), "gauntlets": ("metal", 3),
                   "wraps": ("skin", 3)}[kind]

    def draw(c: Canvas, p: Points) -> None:
        for near in (False, True):
            hx, hy = p.hand_n if near else p.hand_f
            c.rect(hx - 1, hy - 1, hx, hy, slot, shade)
            if kind == "gauntlets":
                c.px(hx - 1, hy - 1, slot, 4)
            elif kind == "wraps":                      # cloth bandages over the knuckles and wrist
                c.rect(hx - 1, hy - 1, hx, hy - 1, "cloth2", 4)
                c.px(hx - 1, hy + 1 if hy + 1 < c.h else hy, "cloth2", 3)
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
                kx, ky = ((fx, hy) if p.pose.sit else ((hx + fx) / 2, (hy + fy) / 2))
                c.thick_line(kx, ky, fx, fy - 1, 3, "metal")
                c.rect(int(kx) - 1, int(ky) - 1, int(kx) + 1, int(ky), "metal", 4)
        elif kind == "shorts":
            for near in (False, True):
                hx, hy = p.hip_n if near else p.hip_f
                fx, fy = p.foot_n if near else p.foot_f
                if p.pose.sit:
                    fy = hy
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
        elif kind == "kilt":   # a hide kilt to the knee, trimmed with fur, over bare legs and leather-bound shins
            b = p.build
            cx = p.mx(16)
            for near in (False, True):
                hx, hy = p.hip_n if near else p.hip_f
                fx, fy = p.foot_n if near else p.foot_f
                if p.pose.sit:
                    c.thick_line(hx, hy, (hx + fx) / 2, hy, 4, "cloth2")
                    c.thick_line(fx, hy + 2, fx, fy - 2, 3, "leather")
                    sy0, sy1 = int(hy) + 2, int(fy) - 2
                elif p.pose.kneel:
                    c.thick_line(hx, hy, hx + 2 * p.near_sign, hy + 2, 4, "cloth2")
                    continue
                else:
                    kx, ky = hx + (fx - hx) * 0.45, hy + (fy - hy) * 0.45
                    c.thick_line(hx, hy, kx, ky, 4, "cloth2")
                    c.thick_line(hx + (fx - hx) * 0.7, hy + (fy - hy) * 0.7, fx, fy - 1, 3, "leather")
                    sy0, sy1 = int(hy + (fy - hy) * 0.7), int(fy) - 1
                lid = c.slots.id("leather")
                for y in range(sy0, sy1 + 1):          # the wrapping: alternate lit and shadowed turns
                    for x in range(max(0, int(fx) - 3), min(c.w, int(fx) + 4)):
                        if c.slot[y, x] == lid:
                            c.shade[y, x] = 4 if (y + x // 2) % 2 == 0 else 2
            lo = p.torso_bot + (2 if p.pose.sit or p.pose.kneel else 4)
            c.polygon([(cx - b.hip_half - 0.5, p.torso_bot - 1), (cx + b.hip_half + 0.5, p.torso_bot - 1),
                       (cx + b.hip_half + 1.5, lo), (cx - b.hip_half - 1.5, lo)], "cloth2")
            kid = c.slots.id("cloth2")
            for x in range(c.w):                        # the fur trim, ragged: every other tuft hangs lower
                for y in (lo - 1, lo):
                    if 0 <= y < c.h and c.slot[y, x] == kid:
                        if y == lo and _hash(x, 0, 5) % 2:
                            c.clear(x, y)
                        else:
                            c.px(x, y, "cloth1", 4 if (x + y) % 3 == 0 else 3)
            for x in range(int(cx - b.hip_half), int(cx + b.hip_half) + 1, 3):     # seams between the panels
                for y in range(p.torso_bot + 1, lo - 1):
                    if 0 <= y < c.h and c.slot[y, x] == kid:
                        c.shade[y, x] = 2
            c.rect(cx - b.hip_half, p.torso_bot - 1, cx + b.hip_half - 1, p.torso_bot - 1, "leather", 2)
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
            for y in range(p.torso_top, min(c.h, p.torso_bot + 3)):
                for x in range(FRAME_W):
                    if c.slot[y, x] == c.slots.id("metal") and (x + y) % 2 == 0:
                        c.shade[y, x] = 2
            c.polygon(_torso_poly(p, top_pad=-1, bottom=p.torso_bot - 1), "cloth1")  # surcoat panel
            _belt(c, p)
        elif kind == "leather":            # studded leather: a fitted jerkin with pauldrons and a row of studs
            _arm(c, p, False, "leather", width=3, to_wrist=False)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 1), "leather")
            _arm(c, p, True, "leather", width=3, to_wrist=False)
            for y in range(p.torso_top + 2, p.torso_bot, 3):
                for x in range(int(p.torso_cx) - 3, int(p.torso_cx) + 4, 3):
                    if c.slot[y, x] == c.slots.id("leather"):
                        c.px(x, y, "metal", 4)
            c.line(p.torso_cx, p.torso_top + 1, p.torso_cx, p.torso_bot, "leather", 2)   # lacing seam
            _belt(c, p)
        elif kind == "scale":              # scale mail: overlapping rows of scales, each lit on its upper edge
            _arm(c, p, False, "metal", width=3, to_wrist=False)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 3, flare=1), "metal")
            _arm(c, p, True, "metal", width=3, to_wrist=False)
            for y in range(p.torso_top, min(c.h, p.torso_bot + 4)):
                for x in range(FRAME_W):
                    if c.slot[y, x] == c.slots.id("metal"):
                        lx = (x + ((y - p.torso_top) // 2) % 2 * 2) % 4    # 4-wide scales, alternate rows offset
                        ly = (y - p.torso_top) % 2
                        if ly == 0:
                            c.shade[y, x] = 4 if lx in (1, 2) else 3       # the scale's lit crown
                        else:
                            c.shade[y, x] = 1 if lx == 0 else (2 if lx == 3 else 3)   # gaps between scales
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
        elif kind == "apron":             # a work tunic under a long apron tied at the waist
            _arm(c, p, False, "cloth1", to_wrist=False, width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 1, flare=1), "cloth1")
            _arm(c, p, True, "cloth1", to_wrist=False, width=3)
            cx, hx = p.torso_cx, p.mx(16)
            hw = max(2, b.half_w - 2)
            c.polygon([(cx - hw + 0.5, p.torso_top + 3), (cx + hw - 0.5, p.torso_top + 3),
                       (hx + hw + 1.5, p.torso_bot + 5), (hx - hw - 1.5, p.torso_bot + 5)], "cloth2", 4)
            c.line(cx - hw, p.torso_top + 3, cx - hw - 1, p.torso_top, "cloth2", 3)   # neck strap
            c.rect(hx - b.hip_half, p.torso_bot - 2, hx + b.hip_half - 1, p.torso_bot - 2, "cloth2", 2)  # tie
            c.px(hx + p.near_sign * (b.hip_half - 1), p.torso_bot - 1, "cloth2", 2)
        elif kind == "dress":             # a laced bodice with puffed sleeves (pair with the skirt legs)
            _arm(c, p, False, "cloth2", to_wrist=False, width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 1), "cloth1")
            _arm(c, p, True, "cloth2", to_wrist=False, width=3)
            for near in (False, True):    # puffs at the shoulder
                sx, sy = p.shoulder_n if near else p.shoulder_f
                c.ellipse(sx + 0.5, sy + 0.5, 2.2, 1.8, "cloth2", 4)
            cx = p.torso_cx
            if p.direction == "front":
                for y in range(p.torso_top + 2, p.torso_bot - 1, 2):   # lacing
                    c.px(cx - 1, y, "trim", 4)
                    c.px(cx + 1, y + 1, "trim", 3)
                c.rect(cx - 2, p.torso_top, cx + 2, p.torso_top, "cloth2", 4)   # neckline
        elif kind == "doublet":           # a noble's padded doublet: high collar, gold buttons, slashed sleeves
            _arm(c, p, False, "cloth1", width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 1, flare=1), "cloth1")
            _arm(c, p, True, "cloth1", width=3)
            cx = p.torso_cx
            c.rect(cx - 2, p.torso_top - 1, cx + 2, p.torso_top, "cloth2", 4)    # ruff collar
            if p.direction == "front":
                for y in range(p.torso_top + 2, p.torso_bot, 2):
                    c.px(cx, y, "trim", 5)
            for near in (False, True):
                sx, sy = p.shoulder_n if near else p.shoulder_f
                c.px(sx, sy + 2, "cloth2", 3)
                c.px(sx, sy + 4, "cloth2", 3)
            _belt(c, p, slot="trim")
        elif kind == "tabard":            # plate under a long surcoat bearing the order's emblem
            _arm(c, p, False, "metal", width=3)
            c.polygon(poly, "metal")
            _arm(c, p, True, "metal", width=3)
            for near in (False, True):
                sx, sy = p.shoulder_n if near else p.shoulder_f
                c.ellipse(sx + 0.5, sy, 2.6, 2.0, "metal", 4)
            cx = p.torso_cx
            hw = max(2, b.half_w - 2)
            c.polygon([(cx - hw + 0.5, p.torso_top + 1), (cx + hw - 0.5, p.torso_top + 1),
                       (p.mx(16) + hw + 0.5, p.torso_bot + 4), (p.mx(16) - hw - 0.5, p.torso_bot + 4)], "cloth1")
            if p.direction == "front":        # the emblem: a cross
                ey = p.torso_top + 4
                c.rect(cx, ey - 1, cx, ey + 4, "cloth2", 4)
                c.rect(cx - 1, ey, cx + 1, ey, "cloth2", 4)
            _belt(c, p, slot="trim")
        elif kind == "gi":                # an open-fronted wrap jacket with a sash: the monk's chest shows
            _arm(c, p, False, "cloth1", to_wrist=False, width=3)
            c.polygon(_torso_poly(p, bottom=p.torso_bot + 2, flare=1), "cloth1")
            _arm(c, p, True, "cloth1", to_wrist=False, width=3)
            if p.direction == "front":
                cx = p.torso_cx
                for k in range(5):            # the crossed lapels leave a V of skin
                    c.rect(cx - 2 + k // 2, p.torso_top + k, cx + 1 - k // 2, p.torso_top + k, "skin", 3)
                c.line(cx - 3, p.torso_top, cx + 1, p.torso_top + 5, "cloth2", 4)   # lapel edge
            c.rect(p.mx(16) - b.hip_half, p.torso_bot - 1, p.mx(16) + b.hip_half - 1, p.torso_bot, "cloth2", 3)
            c.px(p.mx(16) + p.near_sign * (b.hip_half - 1), p.torso_bot + 1, "cloth2", 2)   # sash knot tail
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
        elif kind == "harness":           # a barbarian goes bare-chested: fur mantle, crossed straps, a broad belt
            cx, hx = p.torso_cx, p.mx(16)
            top, bot = p.torso_top, p.torso_bot
            if b.feminine:                # a fur wrap across the chest
                c.polygon(_torso_poly(p, top_pad=-2, bottom=top + 6), "cloth1")
                c.rect(cx - b.half_w + 1, top + 6, cx + b.half_w - 1, top + 6, "cloth1", 2)
            elif p.direction == "front":  # muscle: a pec line and the top of the abs, on the body's own skin
                for dx in (-3, -2, 1, 2):
                    c.px(cx + dx, top + 4, "skin", 2)
                c.px(cx, top + 5, "skin", 2)
                c.px(cx, top + 7, "skin", 2)
            else:                         # shoulder blades from behind
                c.px(cx - 2, top + 4, "skin", 2)
                c.px(cx + 2, top + 4, "skin", 2)
                c.rect(cx, top + 3, cx, top + 7, "skin", 2)
            sx, sy = p.shoulder_n
            fx_ = p.hip_f[0]
            c.line(sx - p.near_sign, top + 2, fx_, bot - 2, "leather", 3)        # the axe strap, shoulder to hip
            c.line(sx - 2 * p.near_sign, top + 2, fx_ + p.near_sign, bot - 2, "leather", 2)
            for near in (False, True):    # the mantle: a ragged fur roll over each shoulder
                qx, qy = p.shoulder_n if near else p.shoulder_f
                c.ellipse(qx + 0.5, qy - 0.5, 2.6, 2.2, "cloth1", 3)
            c.ellipse(cx + 0.5, top + 0.5, b.half_w + 0.5, 1.8, "cloth1", 3)
            c.rect(hx - b.hip_half, bot - 2, hx + b.hip_half - 1, bot - 1, "leather", 3)   # the broad belt
            c.rect(hx - 1, bot - 2, hx, bot - 1, "trim", 4)                                  # its round buckle
            c.px(hx - 1, bot - 2, "trim", 5)
            _speckle(c, "cloth1", 0.18, 4, salt=11)
            _speckle(c, "cloth1", 0.10, 2, salt=13)
        elif kind in ("silk_robe", "drake_robe", "motley_robe"):
            _silk_robe(c, p, kind)
        _ = b
    return draw


def _silk_robe(c: Canvas, p: Points, kind: str) -> None:
    """A sorcerer's robe: no pockets, no spellbook, just flowing silk. Bell sleeves, a high flared collar, a sash
    whose tail hangs at the hip. The drake robe adds scaled pauldrons; the motley robe is split down the middle in two
    clashing colours (mi-parti), sleeves matching their halves, with a zigzag hem."""
    b = p.build
    cx, hx = p.torso_cx, p.mx(16)
    top, bot = p.torso_top, p.torso_bot
    motley = kind == "motley_robe"
    _arm(c, p, False, "cloth1", width=3)
    c.polygon(_torso_poly(p, bottom=bot + 2, flare=1), "cloth1")
    if motley:                             # the near half in the second colour
        cid, c2 = c.slots.id("cloth1"), c.slots.id("cloth2")
        for y in range(max(0, top - 1), min(c.h, bot + 3)):
            for x in range(c.w):
                if c.slot[y, x] == cid and (x - cx) * p.near_sign > 0:
                    c.slot[y, x] = c2
        for x in range(c.w):               # zigzag hem
            y = bot + 2
            if 0 <= y < c.h and c.slot[y, x] and (x + (1 if p.direction == "front" else 0)) % 3 == 0:
                c.clear(x, y)
    _arm(c, p, True, "cloth2" if motley else "cloth1", width=3)
    for near in (False, True):             # bell sleeves: a wide trimmed cuff opening at the wrist
        hx_, hy_ = p.hand_n if near else p.hand_f
        sx_, sy_ = p.shoulder_n if near else p.shoulder_f
        dx, dy = hx_ - sx_, hy_ - sy_
        n = max(1.0, math.hypot(dx, dy))
        ux, uy = dx / n, dy / n
        bx, by = hx_ - ux * 2.5, hy_ - uy * 2.5
        sl = "cloth2" if motley and near else "cloth1"
        c.thick_line(bx - uy * 2, by + ux * 2, bx + uy * 2, by - ux * 2, 2, sl, 2)
        c.line(bx - uy * 2 + ux, by + ux * 2 + uy, bx + uy * 2 + ux, by - ux * 2 + uy, "trim", 4)
    if p.direction == "front":             # a deep V of gold trim down the front
        c.line(cx - 2, top, cx, top + 4, "trim", 4)
        c.line(cx + 2, top, cx, top + 4, "trim", 3)
        c.rect(cx, top + 5, cx, bot - 3, "trim", 3)
    else:
        c.rect(cx, top + 2, cx, bot - 3, "cloth1" if not motley else "cloth2", 2)   # the back seam
    collar = "trim" if motley else "cloth2"
    for s in (-1, 1):                      # the high collar flares up behind the head on both sides
        c.line(cx + s * 3, top, cx + s * 4, top - 3, collar, 4 if s < 0 else 3)
        c.px(cx + s * 3, top - 1, collar, 3)
    sash = "trim" if motley else "cloth2"
    c.rect(hx - b.hip_half, bot - 2, hx + b.hip_half - 1, bot - 1, sash, 3)
    c.rect(hx - b.hip_half, bot - 2, hx + b.hip_half - 1, bot - 2, sash, 4)
    if not (p.pose.sit or p.pose.kneel):
        tx = hx + p.near_sign * (b.hip_half - 1)
        c.line(tx, bot, tx + p.near_sign, bot + 5, sash, 3)                   # the sash's tail
        c.line(tx - p.near_sign, bot, tx, bot + 4, sash, 2)
    if kind == "drake_robe":               # scaled pauldrons: overlapping rows, each scale lit along its crown
        for near in (False, True):
            qx, qy = p.shoulder_n if near else p.shoulder_f
            c.ellipse(qx + 0.5, qy, 3.0, 2.4, "cloth2", 3)
            for y in range(int(qy) - 3, int(qy) + 3):
                for x in range(int(qx) - 4, int(qx) + 5):
                    if 0 <= x < c.w and 0 <= y < c.h and c.slot[y, x] == c.slots.id("cloth2"):
                        lx, ly = (x + (y % 2) * 2) % 4, y % 2
                        c.shade[y, x] = (4 if lx in (1, 2) else 3) if ly == 0 else (1 if lx == 0 else 2)
            c.px(qx + (1 if near else -1) * p.near_sign * 2, qy - 3, "horn", 4)   # a spine on each
            c.px(qx + (1 if near else -1) * p.near_sign * 2, qy - 2, "horn", 2)


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
    """Front view: the cloak hangs behind the body (draw this layer *before* the body). Back view: over the body.
    The back slot also carries things worn on the back: a quiver, a war banner, a fur pelt."""
    if kind in ("quiver", "banner_back"):
        return _back_item(kind)

    def draw(c: Canvas, p: Points) -> None:
        b = p.build
        top = p.torso_top
        length = (p.build.foot_y - 1) if kind == "cloak" else (p.torso_bot + 2)
        if kind == "pelt":
            length = p.torso_bot + 4
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
        if kind == "pelt":                 # fur: a ragged hem, flecked texture and a thick ruff at the collar
            lid = c.slots.id("cloth1")
            for x in range(int(cx - w_bot - 1), int(cx + w_bot + 2)):
                if 0 <= x < c.w and (x % 2 == 0):
                    for y in range(length - 1, length + 1):
                        if 0 <= y < c.h and c.slot[y, x] == lid:
                            c.clear(x, y)
            _speckle(c, "cloth1", 0.12, 4, salt=7)
            c.ellipse(cx + 0.5, top + 0.5, w_top + 0.5, 2.0, "cloth2", 3)
            c.px(cx - 2, top - 1, "cloth2", 4)
    return draw


def _back_item(kind: str) -> Part:
    """Gear strapped to the back. In the front view it peeks out above the far shoulder; from behind, all of it."""
    def draw(c: Canvas, p: Points) -> None:
        top = p.torso_top
        cx = p.torso_cx
        s = -p.near_sign                       # towards the far side
        if kind == "quiver":
            bx, by = cx + s * 2, top + 7       # bottom of the quiver, the mouth up by the far shoulder
            tx, ty = cx + s * 4, top - 3
            c.thick_line(bx, by, tx, ty, 3, "leather", 3)
            c.line(bx - s, by, tx - s, ty, "leather", 2)
            for k, dx in enumerate((-1, 0, 1)):  # fletchings
                c.px(tx + dx, ty - 1 - (k % 2), "cloth2", 4)
                c.px(tx + dx, ty - 2 - (k % 2), "cloth2", 3)
            c.rect(min(bx, tx) , (by + ty) // 2, max(bx, tx), (by + ty) // 2, "trim", 3)
        else:                                  # a war banner on a pole beside the head, like a sashimono
            px_ = cx + s * 6
            ty = max(1, p.head_y0 - 7)
            c.line(px_, top + 6, px_, ty, "wood", 2)
            for y in range(ty + 1, ty + 9):    # the flag flies outward from the pole
                for x in range(1, 7):
                    if y == ty + 8 and x in (3, 4):
                        continue               # swallowtail notch
                    c.px(px_ + s * x, y, "cloth1", 4 if y == ty + 1 else (3 if x < 5 else 2))
            for y in range(ty + 3, ty + 7):    # the charge
                c.px(px_ + s * 3, y, "cloth2", 4)
                c.px(px_ + s * 4, y, "cloth2", 4)
            c.px(px_, ty - 1, "trim", 5)
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
        elif style == "curly":
            # big rounded curls: the cap swells a pixel past the head and runs down the sides
            cx = p.head_cx
            c.ellipse(cx + 0.5, y0 + (2.5 if front else 3.5), w / 2 + 1.2, 4.6 if front else 6.2, "hair")
            if front:
                c.rect(x0 - 1, y0 + 3, x0, y0 + 8, "hair")
                c.rect(x1, y0 + 3, x1 + 1, y0 + 6, "hair")
                for x in range(x0 + 1, x1):                        # keep the forehead and eyes clear
                    for y in range(y0 + 4, y0 + 7):
                        if c.slot[y, x] == c.slots.id("hair"):
                            c.clear(x, y)
            for x in range(x0 - 2, x1 + 3):                        # curl texture: a lit curl every other pixel
                for y in range(y0 - 3, y0 + 10):
                    if 0 <= y < c.h and 0 <= x < c.w and c.slot[y, x] == c.slots.id("hair") and (x + 2 * y) % 4 == 0:
                        c.shade[y, x] = 4
        elif style == "pixie":
            if front:   # a long fringe swept down to the far temple
                for i, x in enumerate(range(x0 + 1, x1)):
                    c.rect(x, y0 + 3, x, y0 + 3 + max(0, 3 - i // 3), "hair")
                c.rect(x1 - 1, y0 + 3, x1, y0 + 3, "hair", 4)
            else:
                c.rect(x0 + 1, y0 + 8, x1 - 1, y0 + 8, "hair", 2)
        elif style == "topknot":
            for y in range(y0 - 1, y0 + cap_rows + 1):            # shaved sides, a short crown
                for x in range(x0, x1 + 1):
                    if c.slot[y, x] == c.slots.id("hair") and abs(x - p.head_cx) > 3:
                        c.clear(x, y)
            c.ellipse(p.head_cx + 0.5, y0 - 2.5, 2.2, 2.0, "hair")
            c.rect(p.head_cx - 1, y0 - 1, p.head_cx + 1, y0 - 1, "cloth2")   # tie
        elif style == "twintails":
            for bx in (x0 - 2, x1 + 2):
                for i in range(8):
                    c.rect(bx - (1 if bx < x0 else 0), y0 + 3 + i, bx + (0 if bx < x0 else 1), y0 + 3 + i, "hair",
                           3 if i % 3 else 2)
                c.px(bx, y0 + 3, "cloth2")
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
        if p.direction != "front" and style in ("moustache", "stubble", "goatee"):
            return
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
        elif style == "goatee":
            c.rect(x0 + 4, y0 + 7, x0 + 8, y0 + 7, "hair", 2)
            c.rect(x0 + 5, y0 + 9, x0 + 7, y0 + 10, "hair")
            c.px(x0 + 6, y0 + 11, "hair", 2)
        elif style == "moustache":
            c.rect(x0 + 4, y0 + 7, x0 + 8, y0 + 7, "hair")
            c.px(x0 + 3, y0 + 8, "hair", 2)
            c.px(x0 + 9, y0 + 8, "hair", 2)
        elif style == "stubble":
            for x in range(x0 + 2, x1 - 1):
                for y in (y0 + 8, y0 + 9):
                    if (x + y) % 2 == 0:
                        c.px(x, y, "hair", 2)
        # keep the mouth readable
        if style != "full":
            c.px(x0 + 6, y0 + 8, "skin", 1)
    return draw


def horns(style: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        x0, y0, w = p.head_x0, p.head_y0, p.build.head_w
        if style == "swept":
            # both horns lie back along the skull (towards screen-left when facing front)
            front = p.direction == "front"
            for start, path in ((3, [(0, 0), (-1, -1), (-2, -1), (-3, -2), (-4, -2), (-5, -3)]),
                                (6, [(0, 0), (-1, -1), (-1, -2), (-2, -3), (-3, -3), (-4, -4)])):
                for i, (dx, dy) in enumerate(path):
                    x = x0 + start + dx if front else x0 + (w - 1 - start) - dx
                    c.px(x, y0 + 1 + dy, "horn", 2 if i < 2 else (3 if i < len(path) - 1 else 5))
            return
        for hx, s in ((x0 + 2, -1), (x0 + w - 3, 1)):
            if style == "curled":
                pts = [(0, 0), (s * 1, -1), (s * 2, -2), (s * 3, -2), (s * 4, -1), (s * 4, 0)]
            elif style == "ram":   # thick spirals over the ears
                pts = [(0, 0), (s * 1, -1), (s * 2, -1), (s * 3, 0), (s * 3, 1), (s * 3, 2), (s * 2, 2),
                       (s * 1, 1), (s * 2, 0), (s * 2, 1)]
            elif style == "small":
                pts = [(0, 0), (0, -1), (s * 1, -2)]
            else:  # "straight": swept back
                pts = [(0, 0), (s * 0, -1), (s * 1, -2), (s * 1, -3), (s * 2, -4)]
            for i, (dx, dy) in enumerate(pts):
                c.px(hx + dx, y0 + dy, "horn", 2 if i < 2 else (3 if i < len(pts) - 1 else 5))
    return draw


# -------------------------------------------------------------------------------------------------------- marks

def marks(kind: str) -> Part:
    """Face markings, front view only: freckles, a scar, war paint, tattoos. Painted kinds use the "paint" slot so
    the player picks their colour."""
    def draw(c: Canvas, p: Points) -> None:
        if p.direction != "front":
            return
        x0, y0 = p.head_x0 + _face_dx(p), p.head_y0
        eyes = {(x0 + 4, y0 + 5), (x0 + 4, y0 + 6), (x0 + 5, y0 + 5), (x0 + 8, y0 + 5), (x0 + 8, y0 + 6),
                (x0 + 9, y0 + 5)}
        if kind == "freckles":
            for dx, dy in ((3, 7), (5, 7), (7, 7), (9, 6), (2, 6), (8, 8)):
                c.px(x0 + dx, y0 + dy, "skin", 2)
        elif kind == "scar":
            for i in range(5):                                   # down across the near brow and cheek
                x, y = x0 + 9 + (i // 3), y0 + 3 + i
                if (x, y) not in eyes:
                    c.px(x, y, "skin", 5)
                    c.px(x + 1, y, "skin", 1)
        elif kind == "warpaint":
            for x in range(x0 + 2, x0 + 11):                     # a band across the eyes
                for y in (y0 + 4, y0 + 5, y0 + 6):
                    if (x, y) not in eyes:
                        c.px(x, y, "paint", 3 if y < y0 + 6 else 2)
        elif kind == "tattoo":
            for dx, dy in ((3, 7), (3, 8), (2, 9), (9, 7), (9, 8), (10, 9), (6, 10), (5, 10), (7, 10)):
                c.px(x0 + dx, y0 + dy, "paint", 2)
        elif kind == "stripes":
            for ex in (x0 + 4, x0 + 8):                          # two lines falling from each eye
                c.rect(ex, y0 + 7, ex, y0 + 9, "paint", 3)
        elif kind == "scales":                                   # a dragon bloodline surfacing: scales on the
            for dx, dy, sh in ((1, 4, 2), (1, 6, 2), (2, 5, 4), (2, 7, 4), (3, 8, 2),     # temples and cheekbones,
                               (10, 5, 4), (10, 7, 2), (9, 8, 4), (11, 6, 2)):          # in the skin's own ramp
                if (x0 + dx, y0 + dy) not in eyes:
                    c.px(x0 + dx, y0 + dy, "skin", sh)
        elif kind == "runes":                                    # glowing rune marks, in magic so they shine
            for dx, dy, sh in ((3, 7, 4), (3, 8, 5), (2, 9, 4), (9, 7, 4), (9, 8, 5), (10, 9, 4), (6, 10, 4)):
                c.px(x0 + dx, y0 + dy, "magic", sh)
    return draw


# -------------------------------------------------------------------------------------------------------- tails

def tail(kind: str) -> Part:
    """Tiefling ("devil": thin, with a spade tip) and dragonborn ("dragon": thick, tapering) tails. They leave the
    lower back and swing out to the far side; in the back view (mirrored) that puts them over the legs."""
    def draw(c: Canvas, p: Points) -> None:
        bx, by = p.mx(16), p.torso_bot - 1
        sway = p.pose.bob + (1 if p.pose.lean > 0 else 0)
        if kind == "devil":
            path = [(0, 0), (-2, 2), (-4, 4), (-6, 5), (-8, 5), (-9, 4), (-10, 2 + sway)]
            for (ax, ay), (bx2, by2) in zip(path, path[1:]):
                c.line(p.mx(16 + ax), by + ay, p.mx(16 + bx2), by + by2, "skin", 2)
            tx, ty = p.mx(16 - 10), by + 2 + sway
            c.polygon([(tx - 1.5, ty - 1), (tx + 1.5, ty - 1), (tx, ty - 4)], "skin", 3)   # spade
            c.px(tx, ty - 2, "skin", 4)
        else:
            path = [(0, 0, 3), (-3, 2, 3), (-6, 4, 3), (-9, 5, 2), (-11, 4 - sway, 2), (-12, 2 - sway, 1),
                    (-12, 0 - sway, 1)]
            for (ax, ay, wa), (bx2, by2, _wb) in zip(path, path[1:]):
                c.thick_line(p.mx(16 + ax), by + ay, p.mx(16 + bx2), by + by2, wa, "skin", 3)
            for ax, ay, _w in path[1:4]:                          # lighter belly scales along the underside
                c.px(p.mx(16 + ax), by + ay + 1, "skin", 4)
        _ = bx
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
        elif kind == "horned_helm":
            for y in range(5):
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - 1, y0 - 1 + y, x1 - inset + 1, y0 - 1 + y, "metal")
            c.rect(x0 - 1, y0 + 3, x1 + 1, y0 + 3, "trim")
            if front:
                c.rect(p.head_cx, y0 + 3, p.head_cx, y0 + 6, "metal", 4)   # nasal guard
            for hx, s_ in ((x0 - 1, -1), (x1 + 1, 1)):                      # curving horns
                for i, (dx, dy) in enumerate(((0, 1), (s_, 0), (2 * s_, -1), (2 * s_, -2), (2 * s_, -3), (s_, -4))):
                    c.px(hx + dx, y0 + dy, "horn", 3 if i < 3 else (4 if i < 5 else 5))
        elif kind == "feathered_cap":
            c.rect(x0, y0 + 1, x1 + 1, y0 + 2, "cloth1", 2)               # brim, tilted
            for i, (l, r) in enumerate([(1, 9), (1, 9), (2, 8), (3, 7)]):
                c.rect(x0 + l, y0 - i, x0 + r, y0 - i, "cloth1")
            c.rect(x0 + 2, y0, x0 + 8, y0, "trim")                        # band
            fx = x0 + 1 if front else x1 - 1
            sgn = -1 if front else 1
            for i in range(6):                                           # a long plume sweeping back
                c.px(fx + sgn * (i // 2), y0 - 1 - i, "cloth2", 4 if i % 2 else 3)
                c.px(fx + sgn * (i // 2) + sgn, y0 - i, "cloth2", 2)
        elif kind == "straw_hat":
            c.ellipse(p.head_cx + 0.5, y0 + 1.5, w / 2 + 3, 2.2, "cloth2", 3)   # wide brim
            c.ellipse(p.head_cx + 0.5, y0 - 0.5, w / 2 - 1, 2.6, "cloth2", 4)   # crown
            c.rect(x0 + 2, y0 + 1, x1 - 2, y0 + 1, "cloth1", 3)                 # band
            for x in range(x0 - 2, x1 + 3, 2):                                   # weave
                c.px(x, y0 + 2, "cloth2", 2)
        elif kind == "cap":
            for y in range(4):
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - 1, y0 - 1 + y, x1 - inset + 1, y0 - 1 + y, "cloth1")
            if front:
                c.rect(x0 + 2, y0 + 3, x1 + 1, y0 + 3, "cloth1", 2)               # brim
            c.px(p.head_cx, y0 - 2, "cloth1", 4)
        elif kind == "mitre":
            for i in range(9):                                                   # a tall, tapered priest's hat
                half = 4 - i // 3
                c.rect(p.head_cx - half, y0 + 1 - i, p.head_cx + half, y0 + 1 - i, "cloth1", 4 if i < 7 else 3)
            c.rect(p.head_cx, y0 - 6, p.head_cx, y0, "trim", 5)
            c.rect(p.head_cx - 2, y0 - 4, p.head_cx + 2, y0 - 4, "trim", 5)
            c.rect(x0, y0 + 1, x1, y0 + 2, "trim", 3)
        elif kind == "mask_hood":
            for y in range(p.build.head_h + 1):
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - 1, y0 - 1 + y, x1 - inset + 1, y0 - 1 + y, "cloth1")
            c.rect(x0 + 2, y0 - 2, x1 - 2, y0 - 2, "cloth1")
            if front:                                       # eyes only: the mask covers nose and mouth
                for y in range(y0 + 3, y0 + 7):
                    for x in range(x0 + 2, x1):
                        c.clear(x, y)
                c.rect(x0 + 2, y0 + 2, x1 - 1, y0 + 2, "cloth1", 2)
                c.rect(x0 + 1, y0 + 7, x1 - 1, y0 + 9, "cloth2", 3)
                c.rect(x0 + 2, y0 + 7, x1 - 2, y0 + 7, "cloth2", 4)
        elif kind == "winged_helm":
            for y in range(5):
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - 1, y0 - 1 + y, x1 - inset + 1, y0 - 1 + y, "metal")
            c.rect(x0 - 1, y0 + 3, x1 + 1, y0 + 3, "trim")
            for wx, s_ in ((x0 - 1, -1), (x1 + 1, 1)):      # feathered wings sweep up and back
                for i in range(5):
                    c.px(wx + s_ * (i // 2), y0 + 2 - i, "cloth2", 5 if i > 2 else 4)
                    c.px(wx + s_ * (i // 2) + s_, y0 + 3 - i, "cloth2", 3)
        elif kind == "halo":
            for k in range(16):                               # a thin ring floating above the head
                a = 2 * math.pi * k / 16
                c.px(p.head_cx + math.cos(a) * (w / 2 - 0.5), y0 - 3 + math.sin(a) * 1.2, "magic",
                     5 if math.sin(a) < 0 else 4)
        elif kind == "beret":
            c.ellipse(p.head_cx + (1 if front else -1) + 0.5, y0 + 0.5, w / 2 + 1, 2.4, "cloth1")
            c.rect(x0 + 1, y0 + 2, x1 - 1, y0 + 2, "cloth1", 2)
            c.px(p.head_cx + (1 if front else -1), y0 - 2, "cloth1", 4)
            c.px(x1 if front else x0, y0 + 1, "cloth2", 4)    # a feather pin
        elif kind == "antlers":
            c.rect(x0, y0 + 2, x1, y0 + 2, "leather")         # the band that holds them
            for hx, s_ in ((x0 + 2, -1), (x1 - 2, 1)):
                pts = [(0, 1), (0, 0), (s_, -1), (s_, -2), (2 * s_, -3), (2 * s_, -4), (3 * s_, -5),
                       (s_ * 3, -2), (s_ * 4, -3), (0, -2), (-s_, -3), (-s_, -4)]
                for i, (dx, dy) in enumerate(pts):
                    c.px(hx + dx, y0 + dy, "horn", 3 if i < 4 else 4)
        elif kind == "flower_crown":
            for i, x in enumerate(range(x0, x1 + 1, 2)):
                c.px(x, y0 + 1, "magic", 2)                   # vine
                c.px(x + 1, y0 + 1, "magic", 3)
                if i % 2 == 0:                                # blossoms
                    c.px(x, y0, "cloth2", 4)
                    c.px(x + 1, y0 + 1, "cloth2", 4)
                    c.px(x, y0 + 1, "trim", 5)
        elif kind == "headband":
            c.rect(x0, y0 + 2, x1, y0 + 3, "cloth2", 3)
            c.rect(x0, y0 + 2, x1, y0 + 2, "cloth2", 4)
            tx = x0 - 1 if front else x1 + 1                  # the knot's tails flutter behind
            c.line(tx, y0 + 3, tx - (2 if front else -2), y0 + 6, "cloth2", 3)
            c.line(tx, y0 + 3, tx - (1 if front else -1), y0 + 7, "cloth2", 2)
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
        elif kind == "wolf_pelt":         # a wolf's head worn as a hood: ears up, the muzzle over the brow
            for y in range(4):            # the skull of the pelt over the crown
                inset = 2 if y == 0 else (1 if y == 1 else 0)
                c.rect(x0 + inset - 1, y0 - 2 + y, x1 - inset + 1, y0 - 2 + y, "cloth1")
            for ex in (x0 + 1, x1 - 2):   # ears
                c.rect(ex, y0 - 3, ex + 1, y0 - 3, "cloth1", 3)
                c.px(ex + (0 if ex < p.head_cx else 1), y0 - 4, "cloth1", 4)
                c.px(ex + (1 if ex < p.head_cx else 0), y0 - 3, "cloth1", 1)      # the ear's hollow
            if front:
                # the muzzle juts out over the forehead towards screen-right, jaws open on the brow
                c.polygon([(p.head_cx - 2, y0 - 1), (x1 + 3, y0 + 0.5), (x1 + 5, y0 + 1), (x1 + 5, y0 + 3),
                           (p.head_cx - 2, y0 + 3)], "cloth1", 3)
                c.rect(p.head_cx - 1, y0, x1 + 3, y0, "cloth1", 4)               # the lit bridge of the nose
                c.rect(x1 + 4, y0 + 1, x1 + 5, y0 + 1, "cloth1", 0)              # nose
                c.rect(p.head_cx - 1, y0 + 2, x1 + 4, y0 + 2, "cloth1", 1)       # the shadow under the jaw
                for tx in range(p.head_cx, x1 + 5, 2):                           # teeth along the upper jaw
                    c.px(tx, y0 + 3, "horn", 5)
                c.px(p.head_cx + 1, y0, "eyes", 5)                               # the pelt's glinting eye
                c.px(p.head_cx, y0, "cloth1", 1)
                c.rect(x0 - 1, y0 + 2, x0, y0 + 9, "cloth1", 3)                  # the hide falls to the shoulders
                c.rect(x1 + 1, y0 + 4, x1 + 1, y0 + 8, "cloth1", 2)
            else:
                c.rect(x0 - 1, y0 + 2, x1 + 1, y0 + p.build.head_h + 1, "cloth1")   # the hide over the back
                for x in range(x0, x1 + 1, 2):                                        # a ragged lower edge
                    c.px(x, y0 + p.build.head_h + 2, "cloth1", 2)
                c.rect(p.head_cx, y0 - 1, p.head_cx, y0 + 5, "cloth1", 2)            # the spine of the pelt
            fid = c.slots.id("cloth1")
            for y in range(max(0, y0 - 4), min(c.h, y0 + 14)):                         # fur flecks
                for x in range(max(0, x0 - 2), min(c.w, x1 + 4)):
                    if c.slot[y, x] == fid and c.shade[y, x] == 3 and _hash(x, y, 23) % 5 == 0:
                        c.shade[y, x] = 4
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


SMEAR_REACH = {"sword": 10, "greatsword": 15, "axe": 10, "mace": 9, "warhammer": 10, "flail": 8, "scimitar": 9,
               "halberd": 14, "quarterstaff": 12, "staff": 12, "spear": 14, "greataxe": 15, "maul": 14}


def _smear(c: Canvas, p: Points, reach: float) -> None:
    """The motion arc of a fast swing: the blade's tip path from the previous angle to this one, bright at the
    leading edge and fading behind it. One frame of this sells the speed better than any number of in-betweens."""
    a0 = -p.pose.smear if p.direction == "back" else p.pose.smear
    a1 = p.weapon
    reach *= 0.8
    steps = max(8, int(abs(a1 - a0) / 3))
    for i in range(steps + 1):
        t = i / steps
        a = math.radians(a0 + (a1 - a0) * t)
        shade = 5 if t > 0.7 else (4 if t > 0.35 else 3)
        width = 1 + int(t * 2.5)                       # the crescent thickens towards the blade
        for k in range(width):
            r_ = reach - k
            x = p.hand_n[0] + math.sin(a) * r_
            y = p.hand_n[1] - math.cos(a) * r_
            if 0 <= x < c.w and 0 <= y < c.h and c.slot[int(y), int(x)] == 0:
                c.px(x, y, "blade", shade if k == 0 else max(2, shade - k))


def weapon(kind: str) -> Part:
    def draw(c: Canvas, p: Points) -> None:
        hx, hy = p.hand_n
        if p.pose.smear is not None and kind in SMEAR_REACH:
            _smear(c, p, SMEAR_REACH[kind])
        if kind == "sword":
            pommel = _along(p, -3)
            c.line(*pommel, *_along(p, -1), "leather")                       # grip
            gx, gy = _along(p, 0)
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)                                # guard perpendicular
            c.line(gx - px_ * 2, gy - py_ * 2, gx + px_ * 2, gy + py_ * 2, "trim")
            c.thick_line(*_along(p, 1), *_along(p, 10), 2 if abs(math.sin(a)) < 0.5 else 1, "blade", 4)
            c.line(*_along(p, 1), *_along(p, 10), "blade", 3)
            c.px(*_along(p, 11), "blade", 5)
            c.px(*pommel, "trim")
        elif kind == "greatsword":
            c.line(*_along(p, -4), *_along(p, 0), "leather")
            gx, gy = _along(p, 1)
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)
            c.line(gx - px_ * 3, gy - py_ * 3, gx + px_ * 3, gy + py_ * 3, "trim")
            c.thick_line(*_along(p, 2), *_along(p, 15), 2, "blade", 4)
            c.line(*_along(p, 2), *_along(p, 15), "blade", 3)
        elif kind == "axe":
            c.thick_line(*_along(p, -3), *_along(p, 10), 1, "wood")
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a) * p.near_sign, math.sin(a) * p.near_sign
            for d in range(6, 11):
                ex, ey = _along(p, d)
                span = 3 if d in (7, 8, 9) else 2
                c.line(ex, ey, ex + px_ * span, ey + py_ * span, "blade", 4 if d == 8 else 3)
        elif kind == "mace":
            c.thick_line(*_along(p, -2), *_along(p, 7), 1, "wood")
            mx, my = _along(p, 9)
            c.ellipse(mx + 0.5, my + 0.5, 2.2, 2.2, "blade")
            c.px(mx, my - 2, "blade", 5)
        elif kind == "dagger":
            c.line(*_along(p, -2), *_along(p, 0), "leather")
            c.line(*_along(p, 1), *_along(p, 5), "blade", 4)
            gx, gy = _along(p, 0)
            c.px(gx - 1, gy, "trim")
            c.px(gx + 1, gy, "trim")
        elif kind == "spear":
            c.line(*_along(p, -9), *_along(p, 12), "wood")
            c.thick_line(*_along(p, 12), *_along(p, 15), 2, "blade", 4)
            c.px(*_along(p, 16), "blade", 5)
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
                c.px(hx + s * 6, fy, "blade", 5)
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
        elif kind in ("hourglass_staff", "crystal_staff", "holy_staff", "storm_staff", "living_staff"):
            ang = p.weapon * 0.35
            a = math.radians(ang)
            top = (hx + math.sin(a) * 13, hy - math.cos(a) * 13)
            bot = (hx - math.sin(a) * 9, hy + math.cos(a) * 9)
            tx, ty = top
            if kind == "living_staff":                       # twisted wood that sprouts leaves
                c.line(*bot, *top, "wood")
                for i in range(0, 20, 4):
                    t = i / 20
                    c.px(bot[0] + (tx - bot[0]) * t + 1, bot[1] + (ty - bot[1]) * t, "wood", 2)
                for dx, dy in ((-2, -1), (1, -2), (2, 0), (-1, -3), (0, 1)):
                    c.px(tx + dx, ty + dy, "magic", 3 if dy < 0 else 2)
                c.px(tx - 2, ty - 2, "magic", 5)
            else:
                c.line(*bot, *top, "wood")
            if kind == "hourglass_staff":
                c.rect(tx - 2, ty - 6, tx + 2, ty - 6, "trim", 4)   # frame top and bottom
                c.rect(tx - 2, ty, tx + 2, ty, "trim", 3)
                c.polygon([(tx - 1.5, ty - 5), (tx + 2.5, ty - 5), (tx + 0.5, ty - 3)], "magic", 3)
                c.polygon([(tx + 0.5, ty - 3), (tx + 2.5, ty - 1), (tx - 1.5, ty - 1)], "magic", 4)
                c.px(tx, ty - 3, "magic", 5)
            elif kind == "crystal_staff":
                c.line(tx - 2, ty, tx - 1, ty - 3, "wood", 2)        # claw
                c.line(tx + 2, ty, tx + 1, ty - 3, "wood", 2)
                c.polygon([(tx - 1.5, ty - 2), (tx + 1.5, ty - 2), (tx + 0.5, ty - 9), (tx - 0.5, ty - 9)], "magic", 3)
                c.line(tx - 1, ty - 3, tx, ty - 8, "magic", 5)
            elif kind == "holy_staff":
                c.ellipse(tx + 0.5, ty - 2.5, 2.6, 2.6, "trim", 4)    # a sun disc with rays
                c.px(tx, ty - 3, "magic", 5)
                for dx, dy in ((0, -6), (-4, -3), (4, -3), (-3, -5), (3, -5)):
                    c.px(tx + dx, ty + dy, "trim", 5)
            elif kind == "storm_staff":
                c.line(tx, ty, tx - 2, ty - 3, "blade", 3)            # a forked iron head around a storm orb
                c.line(tx, ty, tx + 2, ty - 3, "blade", 3)
                c.ellipse(tx + 0.5, ty - 4.5, 1.8, 1.8, "magic", 4)
                for dx, dy in ((-3, -7), (2, -8), (3, -5)):
                    c.px(tx + dx, ty + dy, "magic", 5)
        elif kind == "pitchfork":
            c.line(*_along(p, -8), *_along(p, 11), "wood")
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)
            bx_, by_ = _along(p, 11)
            c.line(bx_ - px_ * 2, by_ - py_ * 2, bx_ + px_ * 2, by_ + py_ * 2, "blade", 3)
            for k in (-2, 0, 2):
                tx, ty = bx_ + px_ * k, by_ + py_ * k
                c.line(tx, ty, tx + math.sin(a) * 4, ty - math.cos(a) * 4, "blade", 4)
        elif kind == "smith_hammer":
            c.thick_line(*_along(p, -2), *_along(p, 6), 1, "wood")
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)
            hx_, hy_ = _along(p, 7)
            c.thick_line(hx_ - px_ * 2, hy_ - py_ * 2, hx_ + px_ * 3, hy_ + py_ * 3, 3, "blade", 2)
            c.px(hx_ - px_ * 2, hy_ - py_ * 2 - 1, "blade", 4)
        elif kind == "broom":
            c.line(*_along(p, -2), *_along(p, 14), "wood")
            tx, ty = _along(p, -3)
            c.polygon([(tx - 2, ty - 1), (tx + 2, ty - 1), (tx + 3, ty + 4), (tx - 3, ty + 4)], "cloth2", 3)
            for k in (-2, 0, 2):
                c.px(tx + k, ty + 4, "cloth2", 2)
        elif kind == "tankard":
            c.rect(hx - 1, hy - 4, hx + 2, hy, "wood", 3)
            c.rect(hx - 1, hy - 3, hx + 2, hy - 3, "blade", 4)       # iron band
            c.rect(hx - 1, hy - 5, hx + 2, hy - 5, "cloth2", 5)       # foam
            c.px(hx + 3, hy - 3, "wood", 2)                           # handle
            c.px(hx + 3, hy - 2, "wood", 2)
        elif kind == "scroll":
            c.rect(hx - 3, hy - 2, hx + 2, hy, "cloth2", 4)
            c.rect(hx - 3, hy - 2, hx - 3, hy, "wood", 3)
            c.rect(hx + 2, hy - 2, hx + 2, hy, "wood", 3)
            c.rect(hx - 2, hy - 1, hx + 1, hy - 1, "cloth2", 2)       # writing
        elif kind == "lyre":
            s_ = p.near_sign
            bx_, by_ = hx - s_ * 2, hy - 3
            for side in (-1, 1):                              # the two curved arms
                c.line(bx_ + side * 2, by_ + 3, bx_ + side * 3, by_ - 2, "wood", 3)
                c.px(bx_ + side * 2, by_ - 3, "wood", 4)
            c.rect(bx_ - 2, by_ + 3, bx_ + 2, by_ + 4, "wood", 2)   # sound box
            c.rect(bx_ - 2, by_ - 2, bx_ + 2, by_ - 2, "trim", 4)   # crossbar
            for x in (-1, 0, 1):
                c.line(bx_ + x, by_ - 1, bx_ + x, by_ + 2, "trim", 3)
        elif kind == "warhammer":
            c.thick_line(*_along(p, -3), *_along(p, 10), 1, "wood")
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)
            hx_, hy_ = _along(p, 10)
            c.thick_line(hx_ - px_ * 3, hy_ - py_ * 3, hx_ + px_ * 3, hy_ + py_ * 3, 3, "blade", 3)   # head
            c.px(hx_ - px_ * 3, hy_ - py_ * 3 - 1, "blade", 5)
        elif kind == "halberd":
            c.line(*_along(p, -9), *_along(p, 14), "wood")
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a) * p.near_sign, math.sin(a) * p.near_sign
            for d in range(9, 14):                                          # axe blade
                ex, ey = _along(p, d)
                span = 3 if d in (10, 11, 12) else 2
                c.line(ex, ey, ex + px_ * span, ey + py_ * span, "blade", 4 if d == 11 else 3)
            c.thick_line(*_along(p, 14), *_along(p, 17), 1, "blade", 4)  # spike
            c.px(*_along(p, 18), "blade", 5)
        elif kind == "flail":
            c.thick_line(*_along(p, -2), *_along(p, 4), 1, "wood")
            ex, ey = _along(p, 4)
            sway = 2 * p.near_sign
            c.line(ex, ey, ex + sway, ey + 4, "blade", 2)                   # chain
            c.ellipse(ex + sway + 0.5, ey + 5.5, 2.2, 2.2, "blade", 3)       # spiked ball
            for dx, dy in ((-2, 0), (2, 0), (0, -2), (0, 2)):
                c.px(ex + sway + dx * 1.5, ey + 5.5 + dy * 1.5, "blade", 4)
        elif kind == "scimitar":
            c.line(*_along(p, -2), *_along(p, 0), "leather")
            gx, gy = _along(p, 0)
            c.px(gx - 1, gy, "trim", 4)
            c.px(gx + 1, gy, "trim", 4)
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a) * p.near_sign, math.sin(a) * p.near_sign
            for d in range(1, 10):                                          # a blade that curves outward
                bx_, by_ = _along(p, d)
                curve = (d / 9) ** 2 * 2.5
                c.px(bx_ + px_ * curve, by_ + py_ * curve, "blade", 4)
                c.px(bx_ + px_ * (curve - 1), by_ + py_ * (curve - 1), "blade", 3)
        elif kind == "quarterstaff":
            ang = p.weapon * 0.35
            a = math.radians(ang)
            top = (hx + math.sin(a) * 13, hy - math.cos(a) * 13)
            bot = (hx - math.sin(a) * 9, hy + math.cos(a) * 9)
            c.line(*bot, *top, "wood")
            for t in (0.15, 0.85):                                           # iron-shod ends
                c.px(bot[0] + (top[0] - bot[0]) * t, bot[1] + (top[1] - bot[1]) * t, "blade", 3)
        elif kind == "crossbow":
            # held level at the hip, stock along the weapon angle, a bow across the front
            s_ = p.near_sign
            c.thick_line(hx - s_ * 3, hy, hx + s_ * 4, hy - 1, 2, "wood", 3)
            c.line(hx + s_ * 4, hy - 4, hx + s_ * 4, hy + 3, "blade", 3)   # prod
            c.line(hx + s_ * 4, hy - 4, hx + s_ * 1, hy - 1, "cloth2", 4)  # string
            c.line(hx + s_ * 4, hy + 3, hx + s_ * 1, hy - 1, "cloth2", 4)
            if p.pose.aim:
                c.px(hx + s_ * 6, hy - 1, "blade", 5)                      # the bolt
        elif kind == "rapier":
            c.line(*_along(p, -2), *_along(p, 0), "leather")
            gx, gy = _along(p, 0)
            s_ = p.near_sign
            for dx, dy in ((-1, 0), (-1, 1), (0, 2), (1, 1), (1, 0)):              # swept hilt
                c.px(gx + dx * s_, gy + dy - 1, "trim", 4 if dy == 0 else 3)
            c.line(*_along(p, 1), *_along(p, 12), "blade", 4)
            c.px(*_along(p, 12), "blade", 5)
        elif kind == "greataxe":
            # a long haft and a broad bearded blade: wider than the axe, set a step further out
            c.thick_line(*_along(p, -6), *_along(p, 15), 1, "wood")
            for d in (-2, 0):                                               # grip wraps
                c.px(*_along(p, d), "leather", 3)
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a) * p.near_sign, math.sin(a) * p.near_sign
            for d, span in ((9, 2), (10, 3), (11, 5), (12, 5), (13, 5), (14, 4), (15, 2)):
                ex, ey = _along(p, d)
                c.line(ex, ey, ex + px_ * span, ey + py_ * span, "blade", 4 if d in (11, 12) else 3)
                c.px(ex + px_ * span, ey + py_ * span, "blade", 5 if d < 14 else 4)  # the honed edge
            for d in (11, 12, 13):                                          # a short spike behind the haft
                ex, ey = _along(p, d)
                c.px(ex - px_ * 1, ey - py_ * 1, "blade", 2)
            c.px(*_along(p, 12), "blade", 1)                                # the eye of the head
            c.px(*_along(p, 16), "blade", 4)
        elif kind == "maul":
            # a two-handed maul: a squared stone head bound to a thick haft, a rune cut in its face
            c.thick_line(*_along(p, -5), *_along(p, 11), 1, "wood")
            for d in (-3, -1):
                c.px(*_along(p, d), "leather", 3)
            a = math.radians(p.weapon)
            px_, py_ = math.cos(a), math.sin(a)
            mx_, my_ = _along(p, 13)
            ux, uy = math.sin(a), -math.cos(a)                              # along the haft
            c.polygon([(mx_ + px_ * sx * 4 + ux * sy * 3, my_ + py_ * sx * 4 + uy * sy * 3)
                       for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))], "blade", 3)
            c.line(mx_ - px_ * 3 + ux, my_ - py_ * 3 + uy, mx_ + px_ * 1, my_ + py_ * 1 - uy, "blade", 1)  # a crack
            bx_, by_ = _along(p, 11)
            c.line(bx_ - px_ * 2, by_ - py_ * 2, bx_ + px_ * 2, by_ + py_ * 2, "leather", 2)  # the binding
            c.px(mx_ + px_ * 2, my_ + py_ * 2, "magic", 5)                                    # the glowing rune
            c.px(mx_ - px_ * 4 + math.sin(a), my_ - py_ * 4 - math.cos(a), "blade", 5)
        elif kind == "spellfire":
            # no focus at all: raw magic cupped over the palm, flickering as the hand moves
            ox, oy = hx, hy - 3
            c.ellipse(ox + 0.5, oy - 0.5, 2.3, 2.3, "magic", 3)
            c.ellipse(ox, oy - 1, 1.2, 1.2, "magic", 4)
            c.px(ox - 1, oy - 2, "magic", 5)
            c.px(ox, oy - 1, "magic", 5)
            h = _hash(int(hx), int(hy), 31)
            for k in range(3):                                              # sparks shed off it
                ang = (h >> (k * 4)) % 16 * math.pi / 8
                r_ = 3.5 + ((h >> (k * 3 + 12)) % 2)
                c.px(ox + math.cos(ang) * r_, oy - 1 + math.sin(ang) * r_, "magic", 5 if k == 0 else 4)
            c.px(ox, oy - 4, "magic", 4)                                    # a tongue licking upward
        elif kind == "wild_motes":
            # chaos: a lopsided knot of magic with motes of the wrong colours crackling round it
            ox, oy = hx, hy - 3
            c.ellipse(ox + 0.5, oy - 0.5, 2.0, 2.0, "magic", 3)
            c.px(ox - 1, oy - 2, "magic", 5)
            c.px(ox, oy - 1, "magic", 4)
            h = _hash(int(hx), int(hy), 37)
            for k, (sl, sh) in enumerate((("cloth2", 5), ("trim", 5), ("magic", 5), ("cloth2", 4))):
                ang = ((h >> (k * 4)) % 16 + k * 4) * math.pi / 8
                r_ = 3.5 + ((h >> (k + 16)) % 3) * 0.7
                c.px(ox + math.cos(ang) * r_, oy - 1 + math.sin(ang) * r_, sl, sh)
            zx, zy = ox + 2, oy - 2                                          # a crackle arcing off the top
            for dx, dy in ((1, -1), (0, -2), (1, -3), (2, -3)):
                c.px(zx + dx, zy + dy, "magic", 4 if dy > -3 else 5)
        elif kind == "rune_staff":
            # carved from dark wood, runes cut down its length glowing, a gold cap and a rune hovering above it
            ang = p.weapon * 0.35
            a = math.radians(ang)
            top = (hx + math.sin(a) * 13, hy - math.cos(a) * 13)
            bot = (hx - math.sin(a) * 9, hy + math.cos(a) * 9)
            c.thick_line(*bot, *top, 1, "wood")
            for t in (0.22, 0.42, 0.62, 0.8):                               # the cut runes along the shaft
                c.px(bot[0] + (top[0] - bot[0]) * t, bot[1] + (top[1] - bot[1]) * t, "magic", 5 if t > 0.7 else 4)
            tx, ty = top
            c.rect(tx - 1, ty - 1, tx + 1, ty, "trim", 4)                   # the cap
            c.px(tx - 1, ty - 1, "trim", 5)
            for dx, dy, sh in ((0, -4, 5), (-1, -5, 4), (1, -5, 4), (0, -6, 5), (0, -3, 3)):  # the floating rune
                c.px(tx + dx, ty + dy, "magic", sh)
        elif kind == "lute":
            # held across the body: the bowl at the near hand, the neck rising towards the far shoulder
            s_ = p.near_sign
            bx_, by_ = hx - s_ * 3, hy - 2
            c.ellipse(bx_ + 0.5, by_ + 0.5, 3.8, 3.2, "wood", 3)
            c.ellipse(bx_ + 0.5 - s_, by_ - 0.5, 1.8, 1.4, "wood", 4)              # the lit face of the bowl
            c.px(bx_, by_, "wood", 0)                                               # sound hole
            c.px(bx_ + s_ * 2, by_ + 1, "trim", 4)                                  # bridge
            c.thick_line(bx_ - s_ * 3, by_ - 2, bx_ - s_ * 7, by_ - 8, 2, "wood", 2)  # neck
            c.rect(min(bx_ - s_ * 7, bx_ - s_ * 9), by_ - 10, max(bx_ - s_ * 7, bx_ - s_ * 9), by_ - 9, "wood", 1)
            c.px(bx_ - s_ * 8, by_ - 11, "trim", 4)                                 # tuning peg
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
        elif kind == "tower_shield":                          # a tall rectangular shield: the wall a guardian is
            c.polygon([(fx - 5, fy - 8), (fx + 6, fy - 8), (fx + 6, fy + 6), (fx + 0.5, fy + 8), (fx - 5, fy + 6)],
                      "metal", 3)
            c.polygon([(fx - 4, fy - 7), (fx + 5, fy - 7), (fx + 5, fy + 5), (fx + 0.5, fy + 7), (fx - 4, fy + 5)],
                      "cloth1", 3)
            c.rect(fx, fy - 6, fx + 1, fy + 5, "cloth2", 4)
            c.rect(fx - 3, fy - 3, fx + 4, fy - 2, "cloth2", 4)
            c.rect(fx - 5, fy - 8, fx + 6, fy - 8, "metal", 5)
        elif kind in ("blade", "dagger", "main_gauche"):      # a second weapon for dual wielding
            n = {"blade": 9, "dagger": 5, "main_gauche": 6}[kind]
            s_ = -p.near_sign
            c.line(fx, fy + 1, fx, fy, "leather", 3)
            c.line(fx + s_ * 0, fy - 1, fx + s_ * (n // 3), fy - n, "blade", 4)
            c.px(fx + s_ * (n // 3), fy - n - 1, "blade", 5)
            if kind == "main_gauche":                         # the wide parrying guard
                c.rect(fx - 2, fy, fx + 2, fy, "trim", 4)
                c.px(fx - 2, fy - 1, "trim", 3)
                c.px(fx + 2, fy - 1, "trim", 3)
            else:
                c.rect(fx - 1, fy, fx + 1, fy, "trim", 3)
        elif kind == "bomb":
            c.ellipse(fx + 0.5, fy - 1.5, 2.4, 2.4, "blade", 1)
            c.px(fx - 1, fy - 3, "blade", 3)
            c.line(fx + 1, fy - 4, fx + 2, fy - 6, "leather", 3)  # fuse
            c.px(fx + 2, fy - 7, "magic", 5)
        elif kind == "hand_axe":                              # a second axe for a berserker's frenzy
            s_ = -p.near_sign
            c.line(fx, fy + 2, fx + s_ * 0, fy - 5, "wood", 3)
            for dy, span in ((-6, 2), (-5, 3), (-4, 3), (-3, 2)):
                c.line(fx + s_, fy + dy, fx + s_ * span, fy + dy, "blade", 4 if dy in (-5, -4) else 3)
                c.px(fx + s_ * (span + (1 if dy in (-5, -4) else 0)), fy + dy, "blade", 5 if dy == -5 else 4)
            c.px(fx - s_, fy - 5, "blade", 2)
        elif kind == "spirit_totem":                          # a bone fetish on a short stick, an ancestor's wisp
            c.line(fx, fy + 3, fx, fy - 5, "wood", 3)
            c.rect(fx - 1, fy - 8, fx + 1, fy - 6, "horn", 4)                  # a small skull
            c.px(fx - 1, fy - 7, "horn", 1)
            c.px(fx + 1, fy - 7, "horn", 1)
            c.px(fx, fy - 5, "horn", 2)
            for k, (dx, dy) in enumerate(((-2, -4), (2, -4), (-2, -2), (2, -2))):   # dangling charms
                c.px(fx + dx, fy + dy, "horn" if k % 2 else "cloth2", 4 if dy < -3 else 3)
            for dx, dy, sh in ((0, -10, 5), (-1, -11, 4), (1, -12, 4), (0, -13, 3), (1, -10, 3)):   # the spirit
                c.px(fx + dx, fy + dy, "magic", sh)
        elif kind == "war_horn":
            c.polygon([(fx - 4, fy - 5), (fx - 1, fy - 6), (fx + 2, fy - 2), (fx + 1, fy)], "horn", 3)
            c.line(fx - 4, fy - 5, fx - 1, fy - 6, "horn", 5)
            c.rect(fx - 1, fy - 4, fx, fy - 4, "trim", 4)
            c.ellipse(fx - 3.5, fy - 5.5, 1.5, 1.5, "horn", 1)    # the bell's mouth
    return draw
