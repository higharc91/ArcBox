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


BUILDS: dict[str, Build] = {
    "standard": Build("standard", head_top=8, head_w=11, head_h=10, shoulder_y=19, hip_y=28, foot_y=35,
                      half_w=5, hip_half=4, arm_len=8),
    "short": Build("short", head_top=13, head_w=11, head_h=10, shoulder_y=24, hip_y=31, foot_y=35,
                   half_w=5, hip_half=4, arm_len=6),
    "large": Build("large", head_top=4, head_w=13, head_h=11, shoulder_y=16, hip_y=27, foot_y=35,
                   half_w=7, hip_half=5, arm_len=9),
}


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


# Animations: name -> (fps, loop, poses). Built for readability at 32x40 and 3x-4x zoom on a TV.
ANIMS: dict[str, tuple[float, bool, list[Pose]]] = {
    "idle": (3.0, True, [
        Pose(),
        Pose(bob=1, hand_n=(1, 0), hand_f=(-1, 0)),
    ]),
    "walk": (8.0, True, [
        Pose(bob=1, foot_n=(3, 0), foot_f=(-3, 0), hand_n=(-1, -1), hand_f=(1, -1), weapon=15),
        Pose(bob=0, foot_n=(1, 0), foot_f=(-1, 2), hand_n=(0, 0), hand_f=(0, 0), weapon=22),
        Pose(bob=1, foot_n=(-1, 0), foot_f=(1, 0), hand_n=(2, -1), hand_f=(-2, -1), weapon=30),
        Pose(bob=0, foot_n=(1, 2), foot_f=(-1, 0), hand_n=(0, 0), hand_f=(0, 0), weapon=22),
    ]),
    # Melee swing (swords, axes, maces): overhead wind-up, chop forward-down, recover.
    "attack": (10.0, False, [
        Pose(lean=-1, head=(-1, 0), hand_n=(-2, -14), hand_f=(-1, -2), foot_n=(1, 0), foot_f=(-2, 0), weapon=-10),
        Pose(lean=1, head=(1, 0), bob=1, hand_n=(5, -2), hand_f=(-2, 0), foot_n=(4, 0), foot_f=(-2, 0), weapon=125),
        Pose(lean=1, head=(1, 0), hand_n=(4, 0), hand_f=(-1, 0), foot_n=(3, 0), foot_f=(-2, 0), weapon=100),
    ]),
    # Thrust (spears, daggers): draw back, lunge, recover.
    "thrust": (10.0, False, [
        Pose(lean=-1, hand_n=(-3, -4), hand_f=(-1, -2), foot_n=(1, 0), foot_f=(-2, 0), weapon=80),
        Pose(lean=1, bob=1, head=(1, 0), hand_n=(6, -4), hand_f=(-2, -1), foot_n=(4, 0), foot_f=(-2, 0), weapon=90),
        Pose(lean=1, hand_n=(3, -2), hand_f=(-1, 0), foot_n=(3, 0), foot_f=(-2, 0), weapon=85),
    ]),
    # Shoot (bows): raise, draw to the cheek, release.
    "shoot": (8.0, False, [
        Pose(hand_n=(4, -5), hand_f=(8, -6), foot_n=(2, 0), foot_f=(-2, 0), weapon=0, aim=True),
        Pose(lean=-1, hand_n=(5, -6), hand_f=(7, -8), foot_n=(2, 0), foot_f=(-2, 0), weapon=0, aim=True),
        Pose(hand_n=(5, -6), hand_f=(4, -6), foot_n=(2, 0), foot_f=(-2, 0), weapon=0),
    ]),
    "cast": (6.0, False, [
        Pose(hand_n=(-3, -4), hand_f=(3, -4), weapon=10),
        Pose(head=(0, -1), hand_n=(1, -12), hand_f=(-1, -12), weapon=0),
        Pose(hand_n=(2, -9), hand_f=(-2, -9), weapon=5),
    ]),
    "hurt": (6.0, False, [
        Pose(bob=1, lean=-2, head=(-1, 1), hand_n=(-1, -2), hand_f=(-2, -3), foot_n=(0, 0), foot_f=(-2, 0), weapon=50),
    ]),
    "down": (1.0, False, [
        Pose(bob=5, head=(1, 2), hand_n=(2, 3), hand_f=(-1, 3), foot_n=(3, 0), foot_f=(-2, 0), weapon=105, kneel=True),
    ]),
}

ANIM_ORDER = ["idle", "walk", "attack", "thrust", "shoot", "cast", "hurt", "down"]


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
    bob = pose.bob
    lean = pose.lean
    sy = b.shoulder_y + bob
    hip = b.hip_y + (bob if pose.kneel else 0)
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
    head_x0 = CX - b.head_w // 2 + lean + pose.head[0]
    head_y0 = b.head_top + (3 if pose.kneel else bob) + pose.head[1]
    if direction == "front":
        return Points("front", b, pose, head_x0, head_y0, sn, sf, hn, hf, hip_n, hip_f, fn, ff,
                      sy, hip, CX + lean, pose.weapon, +1)
    m = lambda p: ((FRAME_W - 1) - p[0], p[1])  # noqa: E731
    return Points("back", b, pose, (FRAME_W - 1) - (head_x0 + b.head_w - 1), head_y0,
                  m(sn), m(sf), m(hn), m(hf), m(hip_n), m(hip_f), m(fn), m(ff),
                  sy, hip, (FRAME_W - 1) - (CX + lean), -pose.weapon, -1)
