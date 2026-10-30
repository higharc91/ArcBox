"""The character rig: builds, poses and the keypoints every layer draws from.

A frame is 32x40 px. The anchor (16, 37) is the ground point under the character's feet; the game places that pixel
on the tile. Two facings are drawn — "front" (towards the camera, turned slightly screen-right) and "back" (away,
turned screen-left). The game mirrors them for the other two isometric directions.

In the front view the *near* hand (weapon) is on screen-right and the *far* hand (shield/off-hand) on screen-left.
The back view uses the same skeleton mirrored, so the weapon hand stays on the side nearer the camera.
"""
from __future__ import annotations

from dataclasses import dataclass, replace

FRAME_W, FRAME_H = 32, 40
ANCHOR = (16, 37)
CX = 16

DIRECTIONS = ("front", "back")


@dataclass(frozen=True)
class Build:
    name: str
    head_top: int       # first row of the head
    head_w: int         # head width (odd, centred on CX)
    head_h: int
    shoulder_y: int     # top of the torso
    hip_y: int          # bottom of the torso / top of the legs
    foot_y: int         # ankle row; feet occupy foot_y..foot_y+1
    half_w: int         # torso half width at the shoulders
    hip_half: int       # torso half width at the hips
    arm_len: int        # shoulder to wrist, straight down
    feminine: bool = False   # body type: narrower shoulders, a waist, lashes and lips


BUILDS: dict[str, Build] = {
    "standard": Build("standard", head_top=8, head_w=11, head_h=10, shoulder_y=19, hip_y=28, foot_y=35,
                      half_w=5, hip_half=4, arm_len=8),
    "short": Build("short", head_top=13, head_w=11, head_h=10, shoulder_y=24, hip_y=31, foot_y=35,
                   half_w=5, hip_half=4, arm_len=6),
    "large": Build("large", head_top=4, head_w=13, head_h=11, shoulder_y=16, hip_y=27, foot_y=35,
                   half_w=7, hip_half=5, arm_len=9),
}
# Body types. Each race has a size (standard / short / large); the player picks the body type, and the "_f" build
# is that size with a narrower chest and a waist. Every layer draws from the build's keypoints, so all gear fits both.
for _k in list(BUILDS):
    _b = BUILDS[_k]
    BUILDS[f"{_k}_f"] = replace(_b, name=f"{_k}_f", half_w=_b.half_w - 1, hip_half=_b.hip_half + 1, feminine=True)


@dataclass(frozen=True)
class Pose:
    """Offsets from the build's rest skeleton. +y is down."""
    bob: int = 0                      # everything above the hips moves down by this
    lean: int = 0                     # torso top shifts by this in x (head follows)
    head: tuple[int, int] = (0, 0)    # extra head offset
    hand_n: tuple[int, int] = (1, 0)  # near hand, relative to the rest wrist position
    hand_f: tuple[int, int] = (-1, 0)
    foot_n: tuple[int, int] = (1, 0)  # (dx, lift) relative to the hip column
    foot_f: tuple[int, int] = (-1, 0)
    weapon: float = 25.0              # weapon angle in degrees, 0 = straight up, 90 = screen-right
    kneel: bool = False               # the downed pose: knees bent, body low
    aim: bool = False                 # bow drawn: the string runs to the far hand
    blink: bool = False               # eyes shut for a frame (idles blink, so characters look alive)
    smear: float | None = None        # a swing's previous weapon angle: the weapon draws a motion arc from it
    rise: int = 0                     # lifts the whole body (jumps, leaps); the ground anchor doesn't move
    expression: str | None = None     # dialogue faces: happy, angry, sad, surprised (None = neutral)
    sit: bool = False                 # seated on a bench or log (the game draws the seat): thighs forward


# Animations: name -> (fps, loop, poses). Built for readability at 32x40 and 3x-4x zoom on a TV. Attacks follow
# anticipation -> strike (with a smear frame) -> impact -> recovery, so hits land with weight.
F0, F2 = (2, 0), (-2, 0)              # a planted stance
SIT_DROP = 5                          # how far the hips drop when seated
ANIMS: dict[str, tuple[float, bool, list[Pose]]] = {
    # Exploration idle: breathe in, out, hold, and blink on the way back up.
    "idle": (4.0, True, [
        Pose(),
        Pose(bob=1, hand_n=(1, 1), hand_f=(-1, 1)),
        Pose(bob=1, hand_n=(1, 1), hand_f=(-1, 1)),
        Pose(blink=True),
    ]),
    # Battle idle: weapon up, knees bent, weight forward, breathing.
    "ready": (5.0, True, [
        Pose(bob=1, lean=1, hand_n=(2, -3), hand_f=(2, -3), foot_n=(2, 0), foot_f=(-3, 0), weapon=40),
        Pose(bob=2, lean=1, hand_n=(2, -2), hand_f=(2, -2), foot_n=(2, 0), foot_f=(-3, 0), weapon=44),
        Pose(bob=2, lean=1, hand_n=(2, -2), hand_f=(2, -2), foot_n=(2, 0), foot_f=(-3, 0), weapon=44, blink=True),
        Pose(bob=1, lean=1, hand_n=(2, -3), hand_f=(2, -3), foot_n=(2, 0), foot_f=(-3, 0), weapon=40),
    ]),
    "walk": (8.0, True, [
        Pose(bob=1, foot_n=(3, 0), foot_f=(-3, 0), hand_n=(-1, -1), hand_f=(1, -1), weapon=15),
        Pose(bob=0, foot_n=(1, 0), foot_f=(-1, 2), hand_n=(0, 0), hand_f=(0, 0), weapon=22),
        Pose(bob=1, foot_n=(-1, 0), foot_f=(1, 0), hand_n=(2, -1), hand_f=(-2, -1), weapon=30),
        Pose(bob=0, foot_n=(1, 2), foot_f=(-1, 0), hand_n=(0, 0), hand_f=(0, 0), weapon=22),
    ]),
    # Overhead swing (swords, axes, maces): coil, wind up behind the head, smear down, follow through, recover.
    "attack": (12.0, False, [
        Pose(lean=-1, hand_n=(-1, -10), hand_f=(-1, -2), foot_n=F0, foot_f=F2, weapon=-30),
        Pose(lean=-1, head=(-1, 0), hand_n=(-3, -15), hand_f=(-1, -3), foot_n=F0, foot_f=F2, weapon=-55),
        Pose(lean=1, bob=1, head=(1, 0), hand_n=(5, -4), hand_f=(-2, 0), foot_n=(4, 0), foot_f=F2, weapon=110,
             smear=-55),
        Pose(lean=1, bob=1, head=(1, 0), hand_n=(5, 0), hand_f=(-2, 0), foot_n=(4, 0), foot_f=F2, weapon=140),
        Pose(hand_n=(3, -2), hand_f=(-1, 0), foot_n=(3, 0), foot_f=F2, weapon=70),
    ]),
    # Thrust (spears, daggers, rapiers): draw back, lunge, hold, recover.
    "thrust": (12.0, False, [
        Pose(lean=-1, hand_n=(-4, -4), hand_f=(-1, -2), foot_n=(1, 0), foot_f=F2, weapon=80),
        Pose(lean=2, bob=1, head=(1, 0), hand_n=(7, -4), hand_f=(-2, -1), foot_n=(5, 0), foot_f=F2, weapon=90),
        Pose(lean=2, bob=1, head=(1, 0), hand_n=(8, -4), hand_f=(-2, -1), foot_n=(5, 0), foot_f=F2, weapon=90),
        Pose(lean=1, hand_n=(3, -2), hand_f=(-1, 0), foot_n=(3, 0), foot_f=F2, weapon=85),
    ]),
    # Shoot (bows, crossbows): raise, draw to the cheek, hold the aim, release (the string snaps forward).
    "shoot": (8.0, False, [
        Pose(hand_n=(4, -5), hand_f=(6, -5), foot_n=F0, foot_f=F2, weapon=0, aim=True),
        Pose(lean=-1, hand_n=(5, -6), hand_f=(7, -8), foot_n=F0, foot_f=F2, weapon=0, aim=True),
        Pose(lean=-1, bob=1, hand_n=(5, -6), hand_f=(7, -8), foot_n=F0, foot_f=F2, weapon=0, aim=True),
        Pose(hand_n=(5, -6), hand_f=(2, -5), foot_n=F0, foot_f=F2, weapon=0),
    ]),
    # Cast: gather at the chest, raise, channel overhead, release forward.
    "cast": (8.0, False, [
        Pose(bob=1, hand_n=(-2, -2), hand_f=(3, -2), weapon=10),
        Pose(hand_n=(0, -8), hand_f=(0, -8), weapon=5),
        Pose(head=(0, -1), hand_n=(1, -13), hand_f=(-1, -13), weapon=0),
        Pose(lean=1, hand_n=(5, -7), hand_f=(3, -7), foot_n=(3, 0), foot_f=F2, weapon=30),
    ]),
    # Heavy slam (greatswords, hammers, axes): crouch, leap with the weapon high, fall into the strike, land hard.
    "slam": (12.0, False, [
        Pose(bob=2, hand_n=(-2, -6), hand_f=(-1, -4), foot_n=F0, foot_f=F2, weapon=-20),
        Pose(rise=4, lean=-1, hand_n=(-3, -16), hand_f=(0, -14), foot_n=(1, 2), foot_f=(-1, 2), weapon=-60),
        Pose(rise=2, lean=2, hand_n=(6, -6), hand_f=(3, -6), foot_n=(2, 1), foot_f=(-2, 1), weapon=100, smear=-60),
        Pose(bob=4, lean=2, hand_n=(6, 2), hand_f=(3, 2), foot_n=(4, 0), foot_f=(-3, 0), weapon=150),
        Pose(bob=1, hand_n=(3, -1), hand_f=(0, -1), foot_n=(3, 0), foot_f=F2, weapon=90),
    ]),
    # Whirlwind cleave: the weapon sweeps all the way round.
    "spin": (14.0, False, [
        Pose(lean=-1, hand_n=(-4, -4), hand_f=(-2, -2), foot_n=F0, foot_f=F2, weapon=-90),
        Pose(hand_n=(6, -3), hand_f=(-3, -2), foot_n=(3, 0), foot_f=F2, weapon=90, smear=-90),
        Pose(lean=-1, hand_n=(2, 0), hand_f=(-4, -2), foot_n=(1, 0), foot_f=(-3, 0), weapon=165, smear=90),
        Pose(hand_n=(3, -1), hand_f=(-1, 0), foot_n=(2, 0), foot_f=F2, weapon=60),
    ]),
    # Flurry (unarmed, dual wield): jab, cross, jab, then a kick.
    "flurry": (14.0, False, [
        Pose(lean=1, hand_n=(6, -6), hand_f=(0, -4), foot_n=(3, 0), foot_f=F2, weapon=80),
        Pose(lean=1, hand_n=(1, -4), hand_f=(9, -6), foot_n=(3, 0), foot_f=F2, weapon=60),
        Pose(lean=1, bob=1, hand_n=(7, -6), hand_f=(1, -4), foot_n=(3, 0), foot_f=F2, weapon=85),
        Pose(lean=-1, hand_n=(-1, -4), hand_f=(1, -5), foot_n=(5, 4), foot_f=(-2, 0), weapon=40),
        Pose(lean=-2, hand_n=(-2, -5), hand_f=(0, -6), foot_n=(8, 6), foot_f=(-2, 0), weapon=40),
        Pose(hand_n=(2, -3), hand_f=(2, -3), foot_n=(2, 0), foot_f=F2, weapon=40),
    ]),
    # Shield bash: brace, drive the off-hand forward, recover.
    "bash": (12.0, False, [
        Pose(lean=-1, hand_n=(-1, -1), hand_f=(2, -3), foot_n=F0, foot_f=F2, weapon=30),
        Pose(lean=2, bob=1, head=(1, 0), hand_n=(1, -2), hand_f=(12, -4), foot_n=(5, 0), foot_f=F2, weapon=40),
        Pose(lean=1, hand_n=(1, -1), hand_f=(5, -2), foot_n=(3, 0), foot_f=F2, weapon=35),
    ]),
    # Block: shield (or weapon) up across the body, braced.
    "block": (6.0, False, [
        Pose(bob=1, hand_n=(2, -5), hand_f=(7, -7), foot_n=(2, 0), foot_f=(-3, 0), weapon=45),
        Pose(bob=2, lean=-1, hand_n=(2, -5), hand_f=(7, -7), foot_n=(2, 0), foot_f=(-3, 0), weapon=45),
    ]),
    "dodge": (10.0, False, [
        Pose(lean=-2, head=(-2, 0), hand_n=(-2, -2), hand_f=(-3, -2), foot_n=(-2, 0), foot_f=(-4, 0), weapon=10),
        Pose(lean=-1, bob=1, head=(-1, 0), hand_n=(0, -1), hand_f=(-2, -1), foot_n=(0, 0), foot_f=(-3, 0)),
    ]),
    # Jump (climbing a ledge, a monk's leap): crouch, air, land.
    "jump": (10.0, False, [
        Pose(bob=2, hand_n=(-1, 0), hand_f=(1, 0), foot_n=F0, foot_f=F2),
        Pose(rise=7, hand_n=(2, -9), hand_f=(-2, -9), foot_n=(1, 3), foot_f=(-1, 3), weapon=10),
        Pose(bob=2, hand_n=(2, -1), hand_f=(-2, -1), foot_n=(2, 0), foot_f=(-3, 0)),
    ]),
    # Use an item (drink a potion, read a scroll): to the chest, tip back, lower.
    "item": (6.0, False, [
        Pose(hand_n=(-2, -6), hand_f=(-1, 0), weapon=40),
        Pose(head=(0, -1), lean=-1, hand_n=(-4, -11), hand_f=(-1, 0), weapon=10),
        Pose(hand_n=(0, -3), hand_f=(-1, 0), weapon=30),
    ]),
    # Victory: weapon raised, a little hop.
    "victory": (6.0, True, [
        Pose(hand_n=(0, -16), hand_f=(-1, -1), weapon=-5),
        Pose(rise=2, hand_n=(0, -17), hand_f=(-2, -3), weapon=0, foot_n=(1, 1), foot_f=(-1, 1)),
        Pose(hand_n=(0, -16), hand_f=(-1, -1), weapon=-5, blink=True),
    ]),
    "hurt": (8.0, False, [
        Pose(bob=1, lean=-2, head=(-1, 1), hand_n=(-1, -2), hand_f=(-2, -3), foot_n=(0, 0), foot_f=(-2, 0), weapon=50),
        Pose(bob=1, lean=-1, head=(-1, 0), hand_n=(0, -1), hand_f=(-1, -2), foot_n=(1, 0), foot_f=(-2, 0), weapon=40),
    ]),
    "down": (1.0, False, [
        Pose(bob=5, head=(1, 2), hand_n=(2, 3), hand_f=(-1, 3), foot_n=(3, 0), foot_f=(-2, 0), weapon=105, kneel=True),
    ]),
    # Downtime: seated at camp or in a tavern, chatting, waving, dozing by the fire.
    "sit": (3.0, True, [
        Pose(sit=True, hand_n=(3, -3), hand_f=(3, -3), weapon=70),
        Pose(sit=True, bob=1, hand_n=(3, -3), hand_f=(3, -3), weapon=70),
        Pose(sit=True, bob=1, hand_n=(3, -3), hand_f=(3, -3), weapon=70),
        Pose(sit=True, hand_n=(3, -3), hand_f=(3, -3), weapon=70, blink=True),
    ]),
    "doze": (2.0, True, [
        Pose(sit=True, head=(1, 1), hand_n=(2, -2), hand_f=(2, -2), weapon=80, blink=True),
        Pose(sit=True, bob=1, head=(1, 2), hand_n=(2, -2), hand_f=(2, -2), weapon=80, blink=True),
    ]),
    "talk": (5.0, True, [
        Pose(hand_n=(2, -5), hand_f=(-1, 0), weapon=50),
        Pose(head=(1, 0), hand_n=(4, -7), hand_f=(-1, 0), weapon=40),
        Pose(hand_n=(1, -4), hand_f=(1, -4), weapon=55),
        Pose(head=(0, 1), hand_n=(3, -6), hand_f=(-2, -1), weapon=45, blink=True),
    ]),
    "wave": (6.0, True, [
        Pose(hand_n=(0, -15), hand_f=(-1, 0), weapon=0),
        Pose(hand_n=(3, -14), hand_f=(-1, 0), weapon=15),
        Pose(hand_n=(0, -15), hand_f=(-1, 0), weapon=0),
        Pose(hand_n=(-2, -14), hand_f=(-1, 0), weapon=-15),
    ]),
    # Getting back up after a revive.
    "rise": (6.0, False, [
        Pose(bob=5, head=(1, 1), hand_n=(2, 2), hand_f=(-1, 2), foot_n=(3, 0), foot_f=(-2, 0), weapon=90, kneel=True),
        Pose(bob=3, hand_n=(2, 0), hand_f=(-1, 0), foot_n=(3, 0), foot_f=(-2, 0), weapon=50),
    ]),
}

ANIM_ORDER = ["idle", "ready", "walk", "attack", "thrust", "shoot", "cast", "slam", "spin", "flurry", "bash",
              "block", "dodge", "jump", "item", "victory", "hurt", "down", "rise", "sit", "doze", "talk", "wave"]


def frame_list() -> list[tuple[str, int, Pose]]:
    """(anim, index, pose) in sheet order for one direction."""
    out = []
    for name in ANIM_ORDER:
        for i, p in enumerate(ANIMS[name][2]):
            out.append((name, i, p))
    return out


@dataclass(frozen=True)
class Points:
    """Absolute keypoints for one frame, already mirrored for the back view."""
    direction: str
    build: Build
    pose: Pose
    head_x0: int
    head_y0: int
    shoulder_n: tuple[int, int]
    shoulder_f: tuple[int, int]
    hand_n: tuple[int, int]
    hand_f: tuple[int, int]
    hip_n: tuple[int, int]
    hip_f: tuple[int, int]
    foot_n: tuple[int, int]
    foot_f: tuple[int, int]
    torso_top: int
    torso_bot: int
    torso_cx: int
    weapon: float                      # degrees, 0 = up; mirrored for the back view
    near_sign: int                     # +1 when the near side is screen-right (front), -1 for back

    @property
    def head_cx(self) -> int:
        return self.head_x0 + self.build.head_w // 2

    def mx(self, x: float) -> float:
        """Maps an x drawn for the front view into this direction (identity for front)."""
        return x if self.direction == "front" else (FRAME_W - 1) - x


def points(build: Build, pose: Pose, direction: str) -> Points:
    b = build
    bob = pose.bob + (SIT_DROP if pose.sit else 0)
    lean = pose.lean
    sy = b.shoulder_y + bob
    hip = b.hip_y + (bob if pose.kneel or pose.sit else 0)
    if pose.kneel:
        hip = b.hip_y + 3
        sy = b.shoulder_y + 3
    sn = (CX + b.half_w - 1 + lean, sy + 1)
    sf = (CX - b.half_w + lean, sy + 1)
    rest_wrist_dy = b.arm_len
    hn = (sn[0] + 1 + pose.hand_n[0], sn[1] + rest_wrist_dy + pose.hand_n[1])
    hf = (sf[0] - 1 + pose.hand_f[0], sf[1] + rest_wrist_dy + pose.hand_f[1])
    hip_n = (CX + b.hip_half - 2, hip)
    hip_f = (CX - b.hip_half + 1, hip)
    fn = (hip_n[0] + pose.foot_n[0], b.foot_y - pose.foot_n[1])
    ff = (hip_f[0] + pose.foot_f[0], b.foot_y - pose.foot_f[1])
    if pose.sit:                       # feet forward under the knees
        fn = (hip_n[0] + 5 + pose.foot_n[0], b.foot_y - pose.foot_n[1])
        ff = (hip_f[0] + 5 + pose.foot_f[0], b.foot_y - pose.foot_f[1])
    head_x0 = CX - b.head_w // 2 + lean + pose.head[0]
    head_y0 = b.head_top + (3 if pose.kneel else bob) + pose.head[1]
    if pose.rise:                      # lift everything, feet included
        r = pose.rise
        up = lambda p: (p[0], p[1] - r)  # noqa: E731
        sn, sf, hn, hf, hip_n, hip_f, fn, ff = (up(q) for q in (sn, sf, hn, hf, hip_n, hip_f, fn, ff))
        head_y0, sy, hip = head_y0 - r, sy - r, hip - r
    if direction == "front":
        return Points("front", b, pose, head_x0, head_y0, sn, sf, hn, hf, hip_n, hip_f, fn, ff,
                      sy, hip, CX + lean, pose.weapon, +1)
    m = lambda p: ((FRAME_W - 1) - p[0], p[1])  # noqa: E731
    return Points("back", b, pose, (FRAME_W - 1) - (head_x0 + b.head_w - 1), head_y0,
                  m(sn), m(sf), m(hn), m(hf), m(hip_n), m(hip_f), m(fn), m(ff),
                  sy, hip, (FRAME_W - 1) - (CX + lean), -pose.weapon, -1)
