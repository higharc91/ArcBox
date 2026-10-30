"""Beasts: monsters that don't fit the humanoid rig. Wolves, boars and bears share a quadruped rig; spiders, slimes,
bats and the drake have their own small drawings. All use 48x48 frames anchored at (24, 44), the same animation
names as characters (idle, walk, attack, hurt, down) and the same two facings (front, back; the game mirrors them),
so the battle code treats every unit alike.

Bodies are lit blobs (props.blob) with fur flecks, legs are posed per frame, and every species ships in palette
variants (grey wolf / timber wolf / dire wolf ...).
"""
from __future__ import annotations

import math
import shutil
from dataclasses import dataclass, field
from typing import Callable

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, strip
from arcpix.sheet import Sheet
from .props import blob, leaf_texture

W = H = 48
ANCHOR = (24, 44)
SLOTS = SlotTable()
for _s in ("fur", "fur2", "eyes", "horn", "mouth", "wing", "glow"):
    SLOTS.id(_s)

ANIMS: dict[str, tuple[float, bool, int]] = {   # name -> (fps, loop, frames)
    "idle": (3.0, True, 2), "walk": (8.0, True, 4), "attack": (10.0, False, 3), "hurt": (6.0, False, 1),
    "down": (1.0, False, 1),
}
ORDER = ["idle", "walk", "attack", "hurt", "down"]

BEAST_RAMPS = {
    "wolf_grey": ramp("#8a8f9c", shift=0.05), "wolf_timber": ramp("#8a6a4a", shift=0.06),
    "wolf_dire": ramp("#3f3a48", shift=0.05, spread=1.2), "wolf_frost": ramp("#cfd9e6", shift=0.05, spread=0.7),
    "boar": ramp("#6a4a36", shift=0.06), "boar_dark": ramp("#3f3036", shift=0.05, spread=1.2),
    "bear_brown": ramp("#7a5232", shift=0.06), "bear_black": ramp("#36323c", shift=0.05, spread=1.3),
    "bear_polar": ramp("#e6e2d6", shift=0.05, spread=0.6),
    "belly_light": ramp("#d9cbb0", shift=0.05, spread=0.8), "belly_grey": ramp("#b5b8c2", shift=0.05),
    "spider": ramp("#3a3340", shift=0.05, spread=1.3), "spider_red": ramp("#7a2a30", shift=0.07),
    "slime_green": ramp("#6fd36a", shift=0.06, spread=0.9), "slime_blue": ramp("#5fa8e8", shift=0.05, spread=0.9),
    "slime_magma": ramp("#ff7b2b", shift=0.08, spread=0.8),
    "bat": ramp("#4a3a52", shift=0.06, spread=1.1), "bat_wing": ramp("#6a4a62", shift=0.06),
    "ivory": ramp("#e3d8bd", shift=0.05, spread=0.85), "mouth": ramp("#8a2a36", shift=0.06),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    return BEAST_RAMPS[name] if fam == "beast" else FAMILIES[fam][name]


@dataclass
class Pose:
    bob: float = 0
    lunge: float = 0                    # body shift forward (screen-right in front view)
    legs: tuple[float, float, float, float] = (0, 0, 0, 0)   # near-front, near-back, far-front, far-back: x swing
    lift: tuple[float, float, float, float] = (0, 0, 0, 0)   # foot lift
    head: tuple[float, float] = (0, 0)
    jaw: bool = False                   # mouth open (attack)
    down: bool = False
    hurt: bool = False


POSES: dict[str, list[Pose]] = {
    "idle": [Pose(), Pose(bob=1, head=(0, 1))],
    "walk": [Pose(legs=(3, -3, -3, 3), lift=(0, 2, 2, 0)), Pose(bob=1, legs=(1, -1, -1, 1)),
             Pose(legs=(-3, 3, 3, -3), lift=(2, 0, 0, 2)), Pose(bob=1, legs=(-1, 1, 1, -1))],
    "attack": [Pose(lunge=-2, bob=1, head=(-1, 1), legs=(-2, -2, -2, -2)),
               Pose(lunge=4, bob=-1, head=(3, -1), jaw=True, legs=(4, 1, 3, 0), lift=(2, 0, 2, 0)),
               Pose(lunge=2, head=(1, 0), jaw=True, legs=(2, 0, 1, 0))],
    "hurt": [Pose(lunge=-3, bob=1, head=(-2, -1), hurt=True)],
    "down": [Pose(bob=6, head=(1, 4), down=True)],
}


def frame_poses() -> list[tuple[str, int, Pose]]:
    return [(a, i, p) for a in ORDER for i, p in enumerate(POSES[a])]


# --------------------------------------------------------------------------------------------- quadruped rig
@dataclass(frozen=True)
class Quad:
    body_rx: float
    body_ry: float
    body_y: float                       # body centre height above the ground
    leg_len: float
    leg_w: int
    head_r: float
    snout: float                        # snout length
    ears: str                           # pointed | round | small
    tail: str                           # bushy | stub | none
    tusks: bool = False
    ridge: bool = False                 # bristle ridge along the back


SPECIES: dict[str, Quad] = {
    "wolf": Quad(11, 6, 16, 10, 2, 5, 5, "pointed", "bushy"),
    "boar": Quad(11, 7, 12, 6, 3, 5.5, 4, "small", "stub", tusks=True, ridge=True),
    "bear": Quad(14, 9, 16, 8, 4, 6.5, 3, "round", "stub"),
    "drake": Quad(12, 7, 16, 9, 3, 5.5, 5, "none", "long", ridge=True),
}


def quad(species: str) -> Callable[[str, Pose], Canvas]:
    q = SPECIES[species]

    def draw(direction: str, p: Pose) -> Canvas:
        c = Canvas(W, H, SLOTS)
        gy = ANCHOR[1]
        cx = ANCHOR[0] + p.lunge * 0.5
        by = gy - q.body_y + p.bob
        hx_off = q.body_rx * 0.95                              # shoulders / head at +x (front view faces right)
        shoulder = (cx + q.body_rx * 0.55, by + q.body_ry * 0.4)
        hip = (cx - q.body_rx * 0.6, by + q.body_ry * 0.4)

        def leg(base, swing, lift, far):
            bx, byy = base
            fx = bx + swing + (-1.5 if far else 1.5)
            fy = gy - lift - (2 if far else 0)
            if p.down:
                fx, fy = bx + (4 if not far else -2), byy + q.leg_len * 0.5
            kx, ky = (bx + fx) / 2 + swing * 0.2, (byy + fy) / 2                  # knee
            shade = 2 if far else 3
            c.thick_line(bx, byy - 1, kx, ky, q.leg_w + 2, "fur", shade)          # thigh
            c.thick_line(kx, ky, fx, fy, q.leg_w, "fur", shade - 1 if far else shade)
            c.rect(fx - 1, fy, fx + q.leg_w, fy, "fur", 1 if far else 2)         # paw

        # far legs, then tail, body, near legs, head
        leg(shoulder, p.legs[2], p.lift[2], True)
        leg(hip, p.legs[3], p.lift[3], True)
        tx, ty = cx - q.body_rx - 1, by - 1
        if q.tail == "bushy":
            c.thick_line(tx + 2, ty, tx - 2, ty + 2 + p.bob, 3, "fur", 3)
            blob(c, "fur", tx - 4, ty + 4 + p.bob, 5, 3, base=2.8)
            c.px(tx - 8, ty + 5 + p.bob, "fur2", 4)                                 # pale tail tip
        elif q.tail == "stub":
            c.thick_line(tx + 1, ty, tx - 2, ty - 2, 2, "fur", 3)
        elif q.tail == "long":                                                      # tapering, to the ground
            pts = [(tx + 3, ty), (tx - 3, ty + 4), (tx - 7, ty + 9), (tx - 9, ty + 13 - p.bob), (tx - 12, ty + 13)]
            for (ax, ay), (bx2, by2), wdt in zip(pts, pts[1:], (4, 3, 2, 1)):
                c.thick_line(ax, ay, bx2, by2, wdt, "fur", 3)
            c.polygon([(tx - 12, ty + 11), (tx - 15, ty + 13), (tx - 12, ty + 15)], "horn", 3)
        blob(c, "fur", cx, by, q.body_rx, q.body_ry, k=2.0)
        blob(c, "fur2", cx + 1, by + q.body_ry * 0.55, q.body_rx * 0.7, q.body_ry * 0.4, base=3.2, k=1.2)   # belly
        if q.ridge:
            for k in range(-int(q.body_rx * 0.7), int(q.body_rx * 0.6), 2):
                c.px(cx + k, by - q.body_ry + 1, "fur", 1)
                c.px(cx + k + 1, by - q.body_ry, "fur", 2)
        leg(shoulder, p.legs[0], p.lift[0], False)
        leg(hip, p.legs[1], p.lift[1], False)
        # head, joined to the shoulders by a neck
        hx = cx + hx_off + p.head[0]
        hy = by - q.body_ry * 0.5 + p.head[1] + (3 if p.down else 0)
        c.thick_line(cx + q.body_rx * 0.5, by - q.body_ry * 0.2, hx - 1, hy + 1, int(q.head_r * 1.3), "fur", 3)
        if q.ears == "pointed":
            for ex in (hx - 2, hx + 1):
                c.polygon([(ex - 1.5, hy - q.head_r + 2), (ex + 1.5, hy - q.head_r + 2), (ex, hy - q.head_r - 3)],
                          "fur", 3)
        elif q.ears == "round":
            blob(c, "fur", hx - 3, hy - q.head_r + 1, 2, 2)
            blob(c, "fur", hx + 3, hy - q.head_r + 1, 2, 2)
        elif q.ears == "small":
            c.polygon([(hx - 3, hy - q.head_r + 2), (hx - 1, hy - q.head_r + 2), (hx - 2, hy - q.head_r)], "fur", 2)
        blob(c, "fur", hx, hy, q.head_r, q.head_r * 0.85, k=2.0)
        sx = hx + q.head_r * 0.6
        blob(c, "fur2", sx + q.snout * 0.5, hy + 1.5, q.snout * 0.7 + 1, 2.2, base=3.2, k=1.4)   # muzzle
        c.px(sx + q.snout + 0.5, hy + 0.5, "mouth", 0)                                           # nose
        if p.jaw:
            c.rect(sx, hy + 3, sx + q.snout, hy + 4, "mouth", 2)
            c.px(sx + 1, hy + 3, "horn", 5)
            c.px(sx + q.snout - 1, hy + 3, "horn", 5)
        else:
            c.line(sx, hy + 3, sx + q.snout - 1, hy + 3, "fur2", 1)
        if q.tusks:
            c.px(sx + q.snout - 1, hy + 2, "horn", 5)
            c.px(sx + q.snout - 1, hy + 1, "horn", 4)
        if p.down or p.hurt:
            c.line(hx, hy - 1, hx + 2, hy - 1, "eyes", 0)
        else:
            c.px(hx + 1, hy - 1, "eyes", 4)
            c.px(hx + 2, hy - 1, "eyes", 1)
        leaf_texture(c, "fur", 41, density=0.12)
        if direction == "back":
            return _back(c)
        return c
    return draw


def _back(c: Canvas) -> Canvas:
    """The back facing for beasts: the front drawing mirrored. At 48 px the silhouette carries the facing; the
    heads point away from the camera, towards the upper-left once mirrored."""
    return c.mirrored()


# ----------------------------------------------------------------------------------------------- other beasts
def spider(direction: str, p: Pose) -> Canvas:
    """A giant spider: a bulbous abdomen, a cephalothorax with a cluster of glowing eyes, eight jointed legs."""
    c = Canvas(W, H, SLOTS)
    gy = ANCHOR[1]
    cx, by = ANCHOR[0] + p.lunge * 0.6, gy - 11 + p.bob
    phase = sum(p.legs) * 0.15
    for side in (-1, 1):                                     # far legs first (side -1 = far)
        for k in range(4):
            spread = (-1.0, -0.35, 0.35, 1.0)[k]
            reach = 19 if k in (0, 3) else 15
            sw = math.sin(phase + k * 1.3 + (0 if side > 0 else 1.6)) * 2 if not p.down else 0
            kx = cx + 2 + spread * reach * 0.55 + sw
            ky = by - 9 - (2 if side < 0 else 0) + (4 if p.down else 0)        # knees arch above the body
            fx = cx + 2 + spread * reach + sw * 1.4
            fy = gy - (2 if side < 0 else 0) - (3 if p.down else 0)
            shade = 2 if side < 0 else 4
            c.thick_line(cx + 2 + spread * 2, by, kx, ky, 2, "fur", shade)
            c.line(kx, ky, fx, fy, "fur", shade - 1)
    blob(c, "fur", cx - 6, by - 2, 10, 8, k=2.4)             # abdomen
    for k in range(4):                                       # hourglass markings
        c.px(cx - 9 + k * 2, by - 6 + k, "fur2", 4)
        c.px(cx - 8 + k * 2, by - 6 + k, "fur2", 3)
    blob(c, "fur", cx + 4, by + 1, 6, 4.5, k=2.0)            # cephalothorax
    for ex, ey in ((cx + 7, by), (cx + 8, by + 1), (cx + 6, by - 1), (cx + 9, by), (cx + 7, by - 2)):
        c.px(ex, ey, "glow", 4 if not p.hurt else 2)
    c.line(cx + 9, by + 3, cx + 11, by + (6 if p.jaw else 4), "horn", 4)          # fangs
    c.line(cx + 7, by + 4, cx + 8, by + (7 if p.jaw else 5), "horn", 4)
    return _back(c) if direction == "back" else c


def slime(direction: str, p: Pose) -> Canvas:
    """A slime: squash and stretch carries all of its motion; a glossy highlight and two beady eyes."""
    c = Canvas(W, H, SLOTS)
    gy = ANCHOR[1]
    squash = {0: 0}.get(0, 0)
    stretch = p.bob * 0.8 + (2.5 if p.jaw else 0) - (2 if p.down else 0) + (1.5 if p.lunge > 2 else 0)
    rx = 10 - stretch * 0.6 + (3 if p.down else 0)
    ry = 8 + stretch
    cx = ANCHOR[0] + p.lunge * 0.4
    cy = gy - ry - (3 if p.jaw else 0) + (0.5 if p.down else 0)
    _ = squash
    blob(c, "fur", cx, cy, rx, ry, k=1.8)
    blob(c, "fur", cx, gy - 1.5, rx + 1.5, 2.2, base=2.2, k=0.8)      # the puddle foot
    c.rect(cx - rx * 0.5, cy - ry * 0.55, cx - rx * 0.5 + 2, cy - ry * 0.55, "fur", 5)   # gloss
    c.px(cx - rx * 0.5, cy - ry * 0.55 + 1, "fur", 5)
    ex = cx + 2
    if p.down or p.hurt:
        c.line(ex - 3, cy, ex - 1, cy, "eyes", 0)
        c.line(ex + 2, cy, ex + 4, cy, "eyes", 0)
    else:
        for x in (ex - 2, ex + 3):
            c.rect(x, cy - 1, x, cy + 1, "eyes", 0)
            c.px(x, cy - 1, "eyes", 4)
    if p.jaw:
        c.rect(ex - 1, cy + 3, ex + 2, cy + 4, "mouth", 1)
    return _back(c) if direction == "back" else c


def bat(direction: str, p: Pose) -> Canvas:
    """A giant bat hovering a body-height above its tile; the wings carry the motion (flap on every frame)."""
    c = Canvas(W, H, SLOTS)
    flap = (p.bob + sum(p.lift)) % 3 if not p.down else 2
    cx, cy = ANCHOR[0] + p.lunge * 0.5, 20 + p.bob * 2 + p.head[1]
    if p.down:
        cy = 38
    span = (19, 15, 10)[int(flap)] if not p.down else 12
    lift = (-8, 0, 6)[int(flap)] if not p.down else 3
    for side in (-1, 1):
        tip = (cx + side * span, cy + lift)
        c.polygon([(cx + side * 2, cy - 2), tip, (cx + side * span * 0.7, cy + 5 + lift * 0.3),
                   (cx + side * span * 0.35, cy + 3), (cx + side * 2, cy + 4)], "wing", 2 if side < 0 else 3)
        c.line(cx + side * 2, cy - 2, *tip, "fur", 2)
    blob(c, "fur", cx, cy + 1, 5.5, 6.5, k=1.8)
    for ex in (cx - 3, cx + 2):                              # ears
        c.polygon([(ex - 1.5, cy - 4), (ex + 1.5, cy - 4), (ex, cy - 9)], "fur", 3)
    if p.down:
        c.line(cx - 2, cy, cx - 1, cy, "glow", 1)
        c.line(cx + 2, cy, cx + 3, cy, "glow", 1)
    else:
        c.px(cx - 2, cy, "glow", 4)
        c.px(cx + 2, cy, "glow", 4)
    if p.jaw:
        c.rect(cx - 1, cy + 3, cx + 1, cy + 3, "horn", 5)
    return _back(c) if direction == "back" else c


def drake(direction: str, p: Pose) -> Canvas:
    """A young dragon: the quadruped rig with a long tail, horns, a dorsal ridge and wings that half-fold at rest
    and spread on the attack."""
    c = Canvas(W, H, SLOTS)
    gy = ANCHOR[1]
    q = SPECIES["drake"]
    cx = ANCHOR[0] + p.lunge * 0.5
    by = gy - q.body_y + p.bob
    open_ = p.jaw
    # far wing behind everything
    wx, wy = cx - 1, by - 4
    far = [(wx + 2, wy), (wx - 6, wy - (16 if open_ else 10)), (wx - 4, wy - (8 if open_ else 5)),
           (wx - 13, wy - (10 if open_ else 5)), (wx - 6, wy + 1)]
    c.polygon(far, "wing", 2)
    body = quad("drake")("front", p)
    c.blit(body)
    near = [(wx, wy + 1), (wx - 9, wy - (18 if open_ else 11)), (wx - 6, wy - (9 if open_ else 6)),
            (wx - 17, wy - (12 if open_ else 6)), (wx - 11, wy - 1), (wx - 4, wy + 3)]
    c.polygon(near, "wing", 3)
    c.line(wx, wy + 1, near[1][0], near[1][1], "fur", 2)                     # wing arm
    c.line(near[1][0], near[1][1], near[3][0], near[3][1], "fur", 2)
    for k in range(1, 3):                                                       # membrane folds
        c.line(near[1][0] + 1, near[1][1] + 2, wx - 5 - 4 * k, wy + 1 - k, "wing", 2)
    hx, hy = cx + q.body_rx * 0.95 + p.head[0], by - q.body_ry * 0.5 + p.head[1] + (3 if p.down else 0)
    for dx in (-3, -1):                                                         # swept horns
        c.line(hx + dx, hy - q.head_r + 1, hx + dx - 4, hy - q.head_r - 3, "horn", 4)
    return _back(c) if direction == "back" else c


# --------------------------------------------------------------------------------------------------- catalogue
@dataclass
class Beast:
    id: str
    name: str
    draw: Callable[[str, Pose], Canvas]
    variants: dict[str, dict[str, str]]
    tags: list[str] = field(default_factory=list)
    flying: bool = False


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
                    mouth="beast/mouth")}, ["beast"]),
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
                    mouth="beast/mouth")}, ["beast", "large"]),
    Beast("spider", "Giant Spider", spider, {
        "black": _v(fur="beast/spider", fur2="beast/spider_red", glow="magic/blood", horn="beast/ivory"),
        "red": _v(fur="beast/spider_red", fur2="beast/ivory", glow="magic/fire", horn="beast/ivory")}, ["beast"]),
    Beast("slime", "Slime", slime, {
        "green": _v(fur="beast/slime_green", eyes="eyes/dark", mouth="beast/mouth"),
        "blue": _v(fur="beast/slime_blue", eyes="eyes/dark", mouth="beast/mouth"),
        "magma": _v(fur="beast/slime_magma", eyes="eyes/dark", mouth="beast/mouth")}, ["ooze"]),
    Beast("bat", "Giant Bat", bat, {
        "dusk": _v(fur="beast/bat", wing="beast/bat_wing", glow="magic/blood", horn="beast/ivory")},
          ["beast"], flying=True),
    Beast("drake", "Drake", drake, {
        "red": _v(fur="skin/dragon_red", fur2="skin/dragon_gold", wing="skin/dragon_copper", eyes="eyes/gold",
                  horn="beast/ivory", mouth="beast/mouth"),
        "green": _v(fur="skin/dragon_green", fur2="skin/dragon_brass", wing="skin/dragon_green", eyes="eyes/red",
                    horn="beast/ivory", mouth="beast/mouth"),
        "blue": _v(fur="skin/dragon_blue", fur2="skin/dragon_silver", wing="skin/dragon_blue", eyes="eyes/gold",
                   horn="beast/ivory", mouth="beast/mouth")}, ["dragon", "boss"]),
]


def ramps(variant: dict[str, str]) -> dict[str, list[str]]:
    out = {s: _ref(r) for s, r in variant.items()}
    for name in SLOTS.names[1:]:
        out.setdefault(name, FAMILIES["cloth"]["slate"])
    return out


def sheet(b: Beast, variant: str) -> Sheet:
    pal = ramps(b.variants[variant])
    s = Sheet(W, H, len(frame_poses()), ANCHOR)
    for d in ("front", "back"):
        for _a, _i, pose in frame_poses():
            cv = b.draw(d, pose)
            cv.outline()
            s.frames.append(cv.render(pal))
    return s


def build() -> None:
    out = PACK / "beasts"
    shutil.rmtree(out, ignore_errors=True)
    n = len(frame_poses())
    anims, i = {}, 0
    for a in ORDER:
        fps, loop, count = ANIMS[a]
        anims[a] = {"frames": list(range(i, i + count)), "fps": fps, "loop": loop}
        i += count
    entries, tiles = {}, []
    for b in CATALOGUE:
        files = {}
        for v in b.variants:
            sh = sheet(b, v)
            rel = f"{b.id}_{v}.png"
            save(sh.image(), out / rel)
            files[v] = rel
            tiles.append(Tile(strip([sh.frames[0], sh.frames[n]], 1, gap=4), b.name, v))
        entries[b.id] = {"name": b.name, "variants": files, "tags": b.tags, "flying": b.flying}
    write_json({"frame": {"width": W, "height": H, "anchor": list(ANCHOR)}, "framesPerDirection": n,
                "directions": {"front": {"row": 0}, "back": {"row": 1}}, "anims": anims, "beasts": entries},
               out / "manifest.json")
    save(contact_sheet("Beasts", tiles, columns=6, zoom=3,
                       subtitle="Monsters off the humanoid rig: same animation names, facings and anchor rules as "
                                "characters, in palette variants."), PREVIEWS / "18-beasts.png")
    # one row of each beast's full animation set
    rows = []
    for b in CATALOGUE:
        sh = sheet(b, next(iter(b.variants)))
        rows.append(Tile(strip(sh.frames[:n], 1, gap=2), b.name, "idle · walk · attack · hurt · down"))
    save(contact_sheet("Beasts · animations", rows, columns=1, zoom=3), PREVIEWS / "18-beasts-anims.png")

    from arcpix.anim import Actor, stage_gif
    actors = [Actor(b.name, next(iter(b.variants)), sheet(b, next(iter(b.variants))).frames, ANCHOR)
              for b in CATALOGUE]
    k = len(actors)
    atk = anims["attack"]["frames"]
    beats = [("Idle", [anims["idle"]["frames"] * 3] * k, 330), ("Walk", [anims["walk"]["frames"] * 3] * k, 125),
             ("Walk · away", [[f + n for f in anims["walk"]["frames"]] * 3] * k, 125),
             ("Attack", [[0, 0] + [f for f in atk for _ in range(2)] + [atk[-1]] * 3] * k, 90),
             ("Hurt", [anims["hurt"]["frames"] * 6 + [0, 0, 0]] * k, 90), ("Down", [anims["down"]["frames"] * 10] * k, 90)]
    stage_gif(PREVIEWS / "anim-beasts.gif", "Beasts", actors, beats, zoom=3, columns=4, cell_w=56, cell_h=56)
