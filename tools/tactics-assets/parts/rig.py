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
    """Offsets from the build's rest skeleton. +y is down; x offsets are in front-view terms (+x = forward, towards
    the weapon side) and are mirrored for the back view."""
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
    expression: str | None = None     # faces: happy, angry, sad, surprised, pain, shut (None = neutral)
    sit: bool = False                 # seated on a bench or log (the game draws the seat): thighs forward
    crouch: int = 0                   # the hips (and all above) drop by this and the knees bend to take it
    drag: tuple[int, int] = (0, 0)    # secondary motion: hair, capes, hems and tails trail the body by this
    shift: int = 0                    # the whole body slides this far along x (knockback, dodges); ends at 0
    tilt: float = 0.0                 # the whole body topples backwards by this many degrees (death)


# Animations: name -> (fps, loop, poses). Built for readability at 32x40 and 3x-4x zoom on a TV. Attacks follow
# anticipation -> a short action frame (a smear on swings, none on thrusts) -> a held impact -> recovery. Held
# frames earn their place through secondary motion: the body holds while hair, capes and hems catch up a frame later.
F0, F2 = (2, 0), (-2, 0)              # a planted stance
SIT_DROP = 5                          # how far the hips drop when seated
DOWNED = Pose(bob=5, head=(1, 2), hand_n=(2, 3), hand_f=(-1, 3), foot_n=(3, 0), foot_f=(-2, 0), weapon=105,
              kneel=True)             # the held "downed" pose: the last frame of `down`, the first of `rise`
STANCE = dict(lean=1, crouch=1, foot_n=(2, 0), foot_f=(-3, 0))   # battle stance: knees bent, weight forward


def _walk(stride: int, lift: int, *, lean: int = 0, crouch: int = 0, swing: int = 2, weapon: float = 22,
          hands: tuple[int, int] = (0, 0), drag: int = -1) -> list[Pose]:
    """A walk cycle: contact, down, passing and up for each foot, with the arms swinging against the legs. `hands`
    is the arms' carry offset (forward, up) for sneaks; `swing` how far they swing."""
    hx, hy = hands
    # (near foot, far foot, crouch, arm phase -1..1, cloth lag)
    beats = [((stride, 0), (-stride, 0), 0, 1, drag),                       # contact: near heel strikes
             ((stride - 1, 0), (-stride + 1, 1), 1, 1, drag - 1),          # down: weight onto it, knees give
             ((1, 0), (0, lift), 0, 0, drag - 1),                          # passing: the far foot swings through
             ((-1, 0), (stride - 1, lift - 1), -1, -1, drag)]              # up: push off, body at its highest
    out = []
    for flip in (False, True):
        for fn, ff, cr, arm, d in beats:
            if flip:
                fn, ff, arm = ff, fn, -arm
            n, f = fn, ff
            out.append(Pose(lean=lean, crouch=crouch + cr, foot_n=(n[0] + 1, n[1]), foot_f=(f[0] - 1, f[1]),
                            hand_n=(hx - arm * swing, hy - (1 if arm < 0 else 0)),
                            hand_f=(hx + arm * swing, hy - (1 if arm > 0 else 0)),
                            weapon=weapon + arm * -7, drag=(d, 0)))
    return out


ANIMS: dict[str, tuple[float, bool, list[Pose]]] = {
    # Exploration idle: settle on the out-breath with the hands and hair a beat behind, rise back, blink, and let
    # the cloak drift in the breeze before the loop starts again.
    "idle": (5.0, True, [
        Pose(),
        Pose(bob=1, drag=(0, -1)),
        Pose(bob=1, hand_n=(1, 1), hand_f=(-1, 1)),
        Pose(hand_n=(1, 1), hand_f=(-1, 1), drag=(0, 1)),
        Pose(blink=True),
        Pose(drag=(1, 0)),
    ]),
    # Battle idle: weapon up, knees bent, weight forward; the weapon bobs a beat behind the breath.
    "ready": (6.0, True, [
        Pose(**STANCE, hand_n=(2, -3), hand_f=(2, -3), weapon=40),
        Pose(**STANCE, bob=1, hand_n=(2, -3), hand_f=(2, -3), weapon=40, drag=(0, -1)),
        Pose(**STANCE, bob=1, hand_n=(2, -2), hand_f=(2, -2), weapon=44),
        Pose(**STANCE, bob=1, hand_n=(2, -2), hand_f=(2, -2), weapon=47, blink=True),
        Pose(**STANCE, hand_n=(2, -2), hand_f=(2, -2), weapon=45, drag=(0, 1)),
        Pose(**STANCE, hand_n=(2, -3), hand_f=(2, -3), weapon=42),
    ]),
    "walk": (12.0, True, _walk(3, 3)),
    # Overhead swing (swords, axes, maces): coil, wind up behind the head, smear down, bite, hold, recover.
    "attack": (14.0, False, [
        Pose(lean=-1, crouch=1, hand_n=(-1, -9), hand_f=(-1, -2), foot_n=F0, foot_f=F2, weapon=-25, drag=(1, 0)),
        Pose(lean=-2, head=(-1, 0), hand_n=(-3, -15), hand_f=(-1, -4), foot_n=F0, foot_f=F2, weapon=-60,
             drag=(2, -1), expression="angry"),
        Pose(lean=1, crouch=1, head=(1, 0), hand_n=(5, -4), hand_f=(-2, 0), foot_n=(4, 0), foot_f=F2, weapon=110,
             smear=-60, drag=(-1, 0), expression="angry"),
        Pose(lean=2, crouch=2, head=(1, 1), hand_n=(5, 1), hand_f=(-2, 1), foot_n=(5, 0), foot_f=F2, weapon=145,
             drag=(-2, 1), expression="angry"),
        Pose(lean=2, crouch=2, head=(1, 1), hand_n=(5, 1), hand_f=(-2, 1), foot_n=(5, 0), foot_f=F2, weapon=145,
             drag=(-1, 0), expression="angry"),
        Pose(lean=1, crouch=1, hand_n=(4, -1), hand_f=(-1, 0), foot_n=(4, 0), foot_f=F2, weapon=100, drag=(1, 0)),
        Pose(lean=1, crouch=1, hand_n=(3, -2), hand_f=(0, -1), foot_n=(3, 0), foot_f=(-3, 0), weapon=60),
    ]),
    # Thrust (spears, daggers, rapiers): draw back, coil, lunge (no smear: a thrust reads by its reach), hold, recover.
    "thrust": (14.0, False, [
        Pose(lean=-1, crouch=1, hand_n=(-3, -4), hand_f=(-1, -2), foot_n=(1, 0), foot_f=F2, weapon=80, drag=(1, 0)),
        Pose(lean=-2, crouch=2, hand_n=(-5, -4), hand_f=(-1, -3), foot_n=(1, 0), foot_f=(-3, 0), weapon=82,
             drag=(1, -1), expression="angry"),
        Pose(lean=2, crouch=1, head=(1, 0), hand_n=(7, -4), hand_f=(-2, -1), foot_n=(5, 0), foot_f=(-3, 0), weapon=90,
             drag=(-2, 0), expression="angry"),
        Pose(lean=2, crouch=1, head=(1, 0), hand_n=(8, -4), hand_f=(-2, -1), foot_n=(5, 0), foot_f=(-3, 0), weapon=90,
             drag=(-1, 0), expression="angry"),
        Pose(lean=1, crouch=1, hand_n=(4, -3), hand_f=(-1, -1), foot_n=(4, 0), foot_f=(-3, 0), weapon=86, drag=(1, 0)),
        Pose(lean=1, crouch=1, hand_n=(2, -3), hand_f=(1, -2), foot_n=(2, 0), foot_f=(-3, 0), weapon=75),
    ]),
    # Shoot (bows, crossbows): raise, nock, draw to the cheek, hold the aim, release (the string snaps), recover.
    "shoot": (10.0, False, [
        Pose(hand_n=(4, -5), hand_f=(3, -4), foot_n=F0, foot_f=F2, weapon=0),
        Pose(hand_n=(4, -5), hand_f=(6, -5), foot_n=F0, foot_f=F2, weapon=0, aim=True),
        Pose(lean=-1, hand_n=(5, -6), hand_f=(7, -8), foot_n=F0, foot_f=F2, weapon=0, aim=True, drag=(1, 0)),
        Pose(lean=-1, crouch=1, hand_n=(5, -6), hand_f=(7, -8), foot_n=F0, foot_f=F2, weapon=0, aim=True,
             expression="angry"),
        Pose(lean=-1, hand_n=(6, -6), hand_f=(1, -5), foot_n=F0, foot_f=F2, weapon=0, drag=(1, 0)),
        Pose(hand_n=(4, -4), hand_f=(0, -3), foot_n=F0, foot_f=F2, weapon=0),
    ]),
    # Cast: gather low with the eyes shut, draw it up, channel overhead while the robes lift, release, recover.
    "cast": (10.0, False, [
        Pose(crouch=1, bob=1, hand_n=(-2, -2), hand_f=(3, -2), weapon=10),
        Pose(crouch=1, bob=1, hand_n=(-3, -3), hand_f=(4, -3), weapon=8, expression="shut", drag=(0, -1)),
        Pose(hand_n=(0, -8), hand_f=(0, -8), weapon=5, expression="shut", drag=(0, 1)),
        Pose(head=(0, -1), hand_n=(1, -13), hand_f=(-1, -13), weapon=0, drag=(0, -1)),
        Pose(head=(0, -1), hand_n=(1, -14), hand_f=(-1, -14), weapon=0, drag=(1, -1)),
        Pose(lean=2, crouch=1, hand_n=(6, -7), hand_f=(4, -7), foot_n=(3, 0), foot_f=F2, weapon=35, drag=(-2, 0),
             expression="angry"),
        Pose(lean=1, hand_n=(3, -4), hand_f=(1, -3), foot_n=(2, 0), foot_f=F2, weapon=25, drag=(-1, 0)),
    ]),
    # Heavy slam (greatswords, hammers, axes): crouch, leap with the weapon high, smear down, land hard, hold, rise.
    "slam": (12.0, False, [
        Pose(crouch=3, hand_n=(-2, -6), hand_f=(-1, -4), foot_n=F0, foot_f=F2, weapon=-20, drag=(0, -1)),
        Pose(rise=3, lean=-1, hand_n=(-3, -16), hand_f=(0, -14), foot_n=(1, 2), foot_f=(-1, 2), weapon=-60,
             drag=(0, 1), expression="angry"),
        Pose(rise=4, lean=-1, hand_n=(-3, -17), hand_f=(0, -15), foot_n=(1, 3), foot_f=(-1, 3), weapon=-70,
             expression="angry"),
        Pose(rise=2, lean=2, hand_n=(6, -6), hand_f=(3, -6), foot_n=(2, 1), foot_f=(-2, 1), weapon=100, smear=-70,
             drag=(0, -2), expression="angry"),
        Pose(crouch=4, lean=2, hand_n=(6, 2), hand_f=(3, 2), foot_n=(4, 0), foot_f=(-3, 0), weapon=150,
             drag=(-1, 1), expression="angry"),
        Pose(crouch=4, lean=2, hand_n=(6, 2), hand_f=(3, 2), foot_n=(4, 0), foot_f=(-3, 0), weapon=150,
             drag=(0, 2), expression="angry"),
        Pose(crouch=1, lean=1, hand_n=(3, -1), hand_f=(0, -1), foot_n=(3, 0), foot_f=F2, weapon=90),
    ]),
    # Whirlwind cleave: coil, then the weapon sweeps round in two smeared frames, holds, and comes back to guard.
    "spin": (14.0, False, [
        Pose(lean=-1, crouch=1, hand_n=(-4, -4), hand_f=(-2, -2), foot_n=F0, foot_f=F2, weapon=-90, drag=(1, 0)),
        Pose(crouch=1, hand_n=(6, -3), hand_f=(-3, -2), foot_n=(3, 0), foot_f=F2, weapon=90, smear=-90,
             drag=(-2, 0), expression="angry"),
        Pose(lean=-1, crouch=2, hand_n=(2, 0), hand_f=(-4, -2), foot_n=(1, 0), foot_f=(-3, 0), weapon=165, smear=90,
             drag=(-2, 1), expression="angry"),
        Pose(lean=-1, crouch=2, hand_n=(2, 0), hand_f=(-4, -2), foot_n=(1, 0), foot_f=(-3, 0), weapon=165,
             drag=(1, 0), expression="angry"),
        Pose(crouch=1, hand_n=(3, -1), hand_f=(-1, 0), foot_n=(2, 0), foot_f=F2, weapon=60),
    ]),
    # Flurry (unarmed, dual wield): guard, jab, cross, jab, chamber the knee, kick, recover.
    "flurry": (14.0, False, [
        Pose(crouch=1, hand_n=(2, -5), hand_f=(4, -5), foot_n=(2, 0), foot_f=F2, weapon=50),
        Pose(lean=1, crouch=1, hand_n=(6, -6), hand_f=(0, -4), foot_n=(3, 0), foot_f=F2, weapon=80, drag=(-1, 0)),
        Pose(lean=1, crouch=1, hand_n=(1, -4), hand_f=(9, -6), foot_n=(3, 0), foot_f=F2, weapon=60, drag=(-1, 0)),
        Pose(lean=1, crouch=2, hand_n=(7, -6), hand_f=(1, -4), foot_n=(3, 0), foot_f=F2, weapon=85, drag=(-2, 0),
             expression="angry"),
        Pose(lean=-1, hand_n=(-1, -4), hand_f=(1, -5), foot_n=(5, 4), foot_f=(-2, 0), weapon=40, drag=(1, 0)),
        Pose(lean=-2, hand_n=(-2, -5), hand_f=(0, -6), foot_n=(8, 6), foot_f=(-2, 0), weapon=40, drag=(1, 0),
             expression="angry"),
        Pose(crouch=1, hand_n=(2, -3), hand_f=(2, -3), foot_n=(2, 0), foot_f=F2, weapon=40),
    ]),
    # Shield bash: brace, coil behind the shield, drive it forward, hold the impact, recover.
    "bash": (12.0, False, [
        Pose(lean=-1, crouch=1, hand_n=(-1, -1), hand_f=(2, -3), foot_n=F0, foot_f=F2, weapon=30),
        Pose(lean=-2, crouch=2, hand_n=(-2, -1), hand_f=(1, -4), foot_n=F0, foot_f=(-3, 0), weapon=28, drag=(1, 0)),
        Pose(lean=2, crouch=1, head=(1, 0), hand_n=(1, -2), hand_f=(12, -4), foot_n=(5, 0), foot_f=F2, weapon=40,
             drag=(-2, 0), expression="angry"),
        Pose(lean=2, crouch=1, head=(1, 0), hand_n=(1, -2), hand_f=(11, -4), foot_n=(5, 0), foot_f=F2, weapon=40,
             drag=(-1, 0), expression="angry"),
        Pose(lean=1, crouch=1, hand_n=(1, -1), hand_f=(5, -2), foot_n=(3, 0), foot_f=F2, weapon=35),
    ]),
    # Block: shield (or weapon) up across the body, braced; the blow jolts it back a pixel, and it holds.
    "block": (8.0, False, [
        Pose(crouch=1, hand_n=(2, -5), hand_f=(7, -7), foot_n=(2, 0), foot_f=(-3, 0), weapon=45),
        Pose(crouch=2, lean=-1, hand_n=(1, -5), hand_f=(6, -7), foot_n=(2, 0), foot_f=(-3, 0), weapon=48,
             drag=(1, 0), expression="angry"),
        Pose(crouch=2, hand_n=(2, -5), hand_f=(7, -7), foot_n=(2, 0), foot_f=(-3, 0), weapon=45),
    ]),
    # Dodge: snap the upper body away, hop back a step, land low, come back to guard.
    "dodge": (12.0, False, [
        Pose(lean=-2, head=(-2, 0), hand_n=(-2, -2), hand_f=(-3, -2), foot_n=(-1, 0), foot_f=(-3, 0), weapon=10,
             drag=(2, 0), expression="surprised"),
        Pose(shift=-2, rise=1, lean=-2, head=(-1, 0), hand_n=(-1, -3), hand_f=(-3, -3), foot_n=(-1, 1),
             foot_f=(-3, 1), weapon=15, drag=(2, 1)),
        Pose(shift=-2, crouch=2, lean=-1, hand_n=(0, -1), hand_f=(-2, -1), foot_n=(0, 0), foot_f=(-3, 0), weapon=20,
             drag=(1, -1)),
        Pose(shift=-1, crouch=1, hand_n=(1, -2), hand_f=(0, -2), foot_n=(1, 0), foot_f=(-3, 0), weapon=30),
    ]),
    # Jump (a ledge, a monk's leap): crouch, launch, tuck at the top, reach down, land. The game moves the sprite
    # along the arc; `rise` keeps the body inside the frame.
    "jump": (10.0, False, [
        Pose(crouch=3, hand_n=(-2, 1), hand_f=(1, 1), foot_n=F0, foot_f=F2, drag=(0, -1)),
        Pose(rise=3, hand_n=(2, -9), hand_f=(-2, -9), foot_n=(1, 2), foot_f=(-1, 1), weapon=10, drag=(0, 1)),
        Pose(rise=4, hand_n=(2, -6), hand_f=(-2, -6), foot_n=(2, 4), foot_f=(0, 4), weapon=15, drag=(0, 1)),
        Pose(rise=2, hand_n=(3, -4), hand_f=(-3, -4), foot_n=(1, 1), foot_f=(-1, 1), weapon=20, drag=(0, -1)),
        Pose(crouch=3, hand_n=(2, -1), hand_f=(-2, -1), foot_n=(2, 0), foot_f=(-3, 0), drag=(0, 1)),
    ]),
    # Use an item (drink a potion, read a scroll): reach to the belt, bring it up, tip back, savour, lower.
    "item": (8.0, False, [
        Pose(hand_n=(0, -1), hand_f=(-1, 0), weapon=40),
        Pose(hand_n=(-2, -6), hand_f=(-1, 0), weapon=40),
        Pose(head=(0, -1), lean=-1, hand_n=(-4, -11), hand_f=(-1, 0), weapon=10, drag=(1, 0)),
        Pose(head=(-1, -1), lean=-1, hand_n=(-4, -11), hand_f=(-1, 0), weapon=10, blink=True, drag=(1, 1)),
        Pose(hand_n=(0, -3), hand_f=(-1, 0), weapon=30),
    ]),
    # Victory: weapon raised, a hop, land, and a pleased blink.
    "victory": (6.0, True, [
        Pose(hand_n=(0, -16), hand_f=(-1, -1), weapon=-5, expression="happy"),
        Pose(rise=2, hand_n=(0, -17), hand_f=(-2, -3), weapon=0, foot_n=(1, 1), foot_f=(-1, 1), drag=(0, 1),
             expression="happy"),
        Pose(crouch=1, hand_n=(0, -16), hand_f=(-1, -2), weapon=-5, drag=(0, -1), expression="happy"),
        Pose(hand_n=(0, -16), hand_f=(-1, -1), weapon=-5, blink=True),
    ]),
    # Hurt: the flash-ready recoil (the game tints this frame), pushed back a pixel, then settle. Short on purpose.
    "hurt": (10.0, False, [
        Pose(bob=1, lean=-2, head=(-1, 1), hand_n=(-1, -2), hand_f=(-2, -3), foot_n=(0, 0), foot_f=(-2, 0),
             weapon=50, drag=(2, 0), expression="pain"),
        Pose(shift=-1, crouch=1, lean=-1, head=(-1, 0), hand_n=(0, -1), hand_f=(-1, -2), foot_n=(1, 0),
             foot_f=(-2, 0), weapon=40, drag=(1, 1), expression="pain"),
        Pose(crouch=1, hand_n=(1, -2), hand_f=(0, -2), foot_n=(2, 0), foot_f=(-3, 0), weapon=40, drag=(-1, 0)),
    ]),
    # Down: hit, the knees give, drop onto one knee (overshooting a pixel), then the held downed slump.
    "down": (8.0, False, [
        Pose(bob=1, lean=-2, head=(-1, 1), hand_n=(-1, -2), hand_f=(-2, -3), foot_n=(0, 0), foot_f=(-2, 0),
             weapon=55, drag=(2, 0), expression="pain"),
        Pose(crouch=3, lean=1, head=(1, 1), hand_n=(2, 0), hand_f=(0, 0), foot_n=(3, 0), foot_f=(-2, 0), weapon=80,
             drag=(1, -1), expression="pain"),
        Pose(bob=5, head=(1, 3), hand_n=(2, 4), hand_f=(-1, 4), foot_n=(3, 0), foot_f=(-2, 0), weapon=110, kneel=True,
             drag=(0, 1)),
        DOWNED,
    ]),
    # Getting back up after a revive: from the slump, look up, push off the knee, stand into guard.
    "rise": (8.0, False, [
        DOWNED,
        Pose(bob=5, head=(0, 0), hand_n=(1, 1), hand_f=(-1, 2), foot_n=(3, 0), foot_f=(-2, 0), weapon=90, kneel=True),
        Pose(crouch=3, lean=1, hand_n=(2, 0), hand_f=(0, 0), foot_n=(3, 0), foot_f=(-2, 0), weapon=60, drag=(0, 1)),
        Pose(crouch=1, hand_n=(2, -1), hand_f=(0, -1), foot_n=(2, 0), foot_f=(-2, 0), weapon=45, drag=(0, -1)),
    ]),
    # Downtime: seated at camp or in a tavern, chatting, waving, dozing by the fire.
    "sit": (3.0, True, [
        Pose(sit=True, hand_n=(3, -3), hand_f=(3, -3), weapon=70),
        Pose(sit=True, bob=1, hand_n=(3, -3), hand_f=(3, -3), weapon=70, drag=(0, -1)),
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
        Pose(hand_n=(0, -15), hand_f=(-1, 0), weapon=0, expression="happy"),
        Pose(hand_n=(3, -14), hand_f=(-1, 0), weapon=15, expression="happy"),
        Pose(hand_n=(0, -15), hand_f=(-1, 0), weapon=0, expression="happy"),
        Pose(hand_n=(-2, -14), hand_f=(-1, 0), weapon=-15, expression="happy"),
    ]),
    # ---- added in the polish pass
    # Run: a forward lean, long strides with a flight frame, arms pumping, cloth streaming behind.
    "run": (14.0, True, [
        Pose(lean=2, crouch=1, foot_n=(6, 0), foot_f=(-5, 2), hand_n=(-3, -3), hand_f=(4, -5), weapon=10,
             drag=(-2, 0)),
        Pose(lean=2, crouch=2, foot_n=(3, 0), foot_f=(-3, 4), hand_n=(-1, -3), hand_f=(2, -4), weapon=20,
             drag=(-3, 0)),
        Pose(lean=2, rise=1, foot_n=(-2, 2), foot_f=(3, 3), hand_n=(2, -4), hand_f=(-2, -3), weapon=40,
             drag=(-3, -1)),
        Pose(lean=2, crouch=1, foot_n=(-5, 2), foot_f=(6, 0), hand_n=(4, -5), hand_f=(-3, -3), weapon=50,
             drag=(-2, 0)),
        Pose(lean=2, crouch=2, foot_n=(-3, 4), foot_f=(3, 0), hand_n=(2, -4), hand_f=(-1, -3), weapon=40,
             drag=(-3, 0)),
        Pose(lean=2, rise=1, foot_n=(3, 3), foot_f=(-2, 2), hand_n=(-2, -3), hand_f=(2, -4), weapon=20,
             drag=(-3, -1)),
    ]),
    # Knockback: shoved off the feet, slide on planted heels, stumble a step back, catch, recover. Ends at shift 0,
    # so the game can slide the unit to its new tile underneath.
    "knockback": (12.0, False, [
        Pose(shift=-2, lean=-3, head=(-2, 1), hand_n=(-2, -4), hand_f=(-3, -5), foot_n=(1, 1), foot_f=(-2, 0),
             weapon=60, drag=(3, 0), expression="pain"),
        Pose(shift=-3, crouch=2, lean=-2, head=(-1, 0), hand_n=(2, -6), hand_f=(-4, -6), foot_n=(3, 0),
             foot_f=(-3, 0), weapon=50, drag=(2, 0), expression="pain"),
        Pose(shift=-2, crouch=1, lean=-1, hand_n=(1, -4), hand_f=(-4, -4), foot_n=(2, 0), foot_f=(-4, 2), weapon=45,
             drag=(1, -1)),
        Pose(shift=-1, crouch=2, lean=1, hand_n=(2, -3), hand_f=(1, -3), foot_n=(3, 0), foot_f=(-4, 0), weapon=45,
             drag=(-1, 1)),
        Pose(**STANCE, hand_n=(2, -3), hand_f=(2, -3), weapon=40),
    ]),
    # Stagger: reeling from a heavy blow or a stun: lurch back, sway forward, back, wobble, steady.
    "stagger": (8.0, False, [
        Pose(shift=-1, lean=-2, crouch=1, head=(-1, 0), hand_n=(0, -3), hand_f=(-3, -3), foot_n=(1, 0),
             foot_f=(-3, 1), weapon=55, drag=(2, 0), expression="pain"),
        Pose(shift=1, lean=2, crouch=2, head=(1, 1), hand_n=(2, 0), hand_f=(1, -1), foot_n=(4, 1), foot_f=(-2, 0),
             weapon=70, drag=(-2, 0), expression="pain"),
        Pose(lean=-1, crouch=1, head=(-1, 0), hand_n=(0, -2), hand_f=(-2, -2), foot_n=(3, 0), foot_f=(-3, 0),
             weapon=55, drag=(1, 0), blink=True),
        Pose(lean=1, crouch=1, head=(1, 0), hand_n=(1, -1), hand_f=(0, -1), foot_n=(3, 0), foot_f=(-3, 0),
             weapon=50, drag=(-1, 0)),
        Pose(**STANCE, hand_n=(2, -2), hand_f=(1, -2), weapon=45),
    ]),
    # Death: hit, buckle, drop to the knees, topple backwards, land, and settle flat: the game fades the last frame.
    "death": (8.0, False, [
        Pose(bob=1, lean=-2, head=(-1, 1), hand_n=(-1, -2), hand_f=(-2, -3), foot_n=(0, 0), foot_f=(-2, 0),
             weapon=55, drag=(2, 0), expression="pain"),
        Pose(crouch=3, lean=-1, head=(-1, 2), hand_n=(0, 2), hand_f=(-1, 2), foot_n=(3, 0), foot_f=(-2, 0),
             weapon=110, drag=(1, -1), expression="pain"),
        Pose(head=(1, 2), hand_n=(2, 3), hand_f=(-1, 3), foot_n=(3, 0), foot_f=(-2, 0), weapon=120, kneel=True,
             drag=(0, 1), expression="pain"),
        Pose(head=(0, 1), hand_n=(1, 1), hand_f=(-2, 1), foot_n=(3, 0), foot_f=(-2, 0), weapon=130, kneel=True,
             tilt=45, drag=(2, 0)),
        Pose(head=(0, 1), hand_n=(1, 2), hand_f=(-2, 2), foot_n=(3, 0), foot_f=(-2, 0), weapon=150, kneel=True,
             tilt=84, drag=(1, 0)),
        Pose(head=(0, 0), hand_n=(2, 3), hand_f=(-2, 3), foot_n=(3, 0), foot_f=(-2, 0), weapon=160, kneel=True,
             tilt=90),
    ]),
    # Pray (a divine cast: clerics, paladins): hands together and head bowed, then lifted and opened to the sky.
    "pray": (8.0, False, [
        Pose(head=(0, 1), hand_n=(-5, -4), hand_f=(6, -4), weapon=10, expression="shut"),
        Pose(crouch=1, head=(0, 1), hand_n=(-5, -5), hand_f=(6, -5), weapon=10, expression="shut", drag=(0, 1)),
        Pose(head=(0, -1), hand_n=(-2, -12), hand_f=(3, -12), weapon=5, expression="shut", drag=(0, -1)),
        Pose(head=(0, -1), hand_n=(3, -14), hand_f=(-3, -14), weapon=0, drag=(0, -1)),
        Pose(head=(0, -1), hand_n=(4, -15), hand_f=(-4, -15), weapon=0, drag=(1, -1)),
        Pose(hand_n=(2, -4), hand_f=(-1, -3), weapon=20, drag=(0, 1)),
    ]),
    # Channel: a sustained concentration loop, palms pushed forward, trembling, robes stirring in the flow.
    "channel": (8.0, True, [
        Pose(crouch=1, lean=1, hand_n=(4, -6), hand_f=(6, -6), foot_n=F0, foot_f=F2, weapon=30, drag=(-1, -1),
             expression="angry"),
        Pose(crouch=1, lean=1, bob=1, hand_n=(5, -6), hand_f=(7, -6), foot_n=F0, foot_f=F2, weapon=32,
             drag=(-2, -1), expression="angry"),
        Pose(crouch=1, lean=1, hand_n=(4, -7), hand_f=(6, -7), foot_n=F0, foot_f=F2, weapon=30, drag=(-1, 0),
             expression="angry"),
        Pose(crouch=1, lean=1, bob=1, hand_n=(5, -6), hand_f=(6, -5), foot_n=F0, foot_f=F2, weapon=33,
             drag=(-2, 0), expression="angry"),
    ]),
    # Summon (big area spells): gather low, sweep the arms up, hold them high as the robes lift, bring it down.
    "summon": (9.0, False, [
        Pose(crouch=2, head=(0, 1), hand_n=(-2, 2), hand_f=(3, 2), foot_n=F0, foot_f=F2, weapon=15,
             expression="shut"),
        Pose(crouch=1, hand_n=(1, -6), hand_f=(-1, -6), foot_n=F0, foot_f=F2, weapon=8, expression="shut",
             drag=(0, 1)),
        Pose(rise=1, head=(0, -1), hand_n=(4, -15), hand_f=(-4, -15), foot_n=F0, foot_f=F2, weapon=-5, drag=(0, 1)),
        Pose(rise=2, head=(0, -1), hand_n=(4, -16), hand_f=(-4, -16), foot_n=F0, foot_f=F2, weapon=-5,
             drag=(0, -1)),
        Pose(rise=2, head=(0, -1), hand_n=(5, -16), hand_f=(-5, -16), foot_n=F0, foot_f=F2, weapon=-8,
             drag=(1, -1)),
        Pose(crouch=2, lean=1, hand_n=(5, -2), hand_f=(-5, -2), foot_n=(3, 0), foot_f=F2, weapon=60, drag=(0, -2),
             expression="angry"),
        Pose(crouch=1, hand_n=(3, -2), hand_f=(-1, -2), foot_n=(2, 0), foot_f=F2, weapon=40, drag=(0, 1)),
    ]),
    # Point: a quick single-target bolt: cock the hand back by the shoulder, snap it out, recoil, lower.
    "point": (12.0, False, [
        Pose(lean=-1, hand_n=(-3, -7), hand_f=(-1, -1), foot_n=F0, foot_f=F2, weapon=30),
        Pose(lean=1, hand_n=(8, -7), hand_f=(-2, 0), foot_n=(3, 0), foot_f=F2, weapon=85, drag=(-1, 0),
             expression="angry"),
        Pose(hand_n=(7, -7), hand_f=(-2, 0), foot_n=(3, 0), foot_f=F2, weapon=80, drag=(1, 0)),
        Pose(hand_n=(3, -3), hand_f=(-1, 0), foot_n=(2, 0), foot_f=F2, weapon=50),
    ]),
    # Buff (a self or ally boost): a fist to the chest, then the weapon thrust up in a rallying cry.
    "buff": (9.0, False, [
        Pose(crouch=1, head=(0, 1), hand_n=(-1, -1), hand_f=(1, -2), weapon=30),
        Pose(crouch=1, hand_n=(-1, -2), hand_f=(6, -6), weapon=30, expression="angry"),
        Pose(crouch=1, bob=1, hand_n=(-1, -2), hand_f=(6, -6), weapon=30, drag=(0, -1), expression="angry"),
        Pose(head=(0, -1), hand_n=(0, -16), hand_f=(5, -6), weapon=-5, drag=(0, 1), expression="angry"),
        Pose(rise=1, head=(0, -1), hand_n=(0, -17), hand_f=(5, -6), weapon=0, expression="happy"),
        Pose(hand_n=(2, -3), hand_f=(0, -2), weapon=30, drag=(0, -1)),
    ]),
    # Kneel (oaths, rest, a respectful bow): lower onto one knee, upright, then bow the head. The game holds the last.
    "kneel": (6.0, False, [
        Pose(crouch=3, lean=1, head=(0, 1), hand_n=(2, 0), hand_f=(1, 0), foot_n=(3, 0), foot_f=(-2, 0), weapon=40),
        Pose(head=(0, -2), hand_n=(1, -1), hand_f=(1, -1), foot_n=(3, 0), foot_f=(-2, 0), weapon=35, kneel=True,
             drag=(0, 1)),
        Pose(head=(0, -1), hand_n=(1, -1), hand_f=(1, -1), foot_n=(3, 0), foot_f=(-2, 0), weapon=35, kneel=True,
             expression="shut"),
    ]),
    # Cheer: both arms up, bouncing on the toes.
    "cheer": (8.0, True, [
        Pose(crouch=1, hand_n=(1, -15), hand_f=(-1, -14), weapon=-10, expression="happy"),
        Pose(rise=2, hand_n=(2, -17), hand_f=(-2, -16), weapon=-5, foot_n=(1, 1), foot_f=(-1, 1), drag=(0, 1),
             expression="happy"),
        Pose(rise=1, hand_n=(1, -16), hand_f=(-1, -15), weapon=-10, expression="happy"),
        Pose(crouch=1, hand_n=(2, -13), hand_f=(-2, -12), weapon=-12, drag=(0, -1), expression="happy"),
    ]),
    # Crouch: low and still behind cover, breathing.
    "crouch": (3.0, True, [
        Pose(crouch=4, lean=2, head=(1, 0), hand_n=(2, -2), hand_f=(3, -2), foot_n=(3, 0), foot_f=(-3, 0),
             weapon=60),
        Pose(crouch=4, lean=2, bob=1, head=(1, 0), hand_n=(2, -2), hand_f=(3, -2), foot_n=(3, 0), foot_f=(-3, 0),
             weapon=60, drag=(0, -1)),
    ]),
    # Sneak: the walk cycle low and short, hands carried forward.
    "sneak": (9.0, True, [p for i, p in enumerate(_walk(2, 2, lean=2, crouch=3, swing=1, weapon=60, hands=(2, -2),
                                                           drag=0)) if i % 4 != 1]),
    # Hop: a quick step up or down one height level.
    "hop": (12.0, False, [
        Pose(crouch=2, hand_n=(-2, 0), hand_f=(1, 0), foot_n=F0, foot_f=F2, drag=(0, -1)),
        Pose(rise=4, lean=1, hand_n=(2, -6), hand_f=(-1, -6), foot_n=(2, 3), foot_f=(-1, 2), weapon=15, drag=(0, 1)),
        Pose(crouch=2, lean=1, hand_n=(2, -2), hand_f=(-1, -2), foot_n=(2, 0), foot_f=(-2, 0), drag=(0, -1)),
        Pose(hand_n=(1, -1), hand_f=(-1, -1), foot_n=(2, 0), foot_f=(-2, 0), weapon=25, drag=(0, 1)),
    ]),
    # Climb: reach up to the ledge, haul up, knee onto it, stand. The game moves the sprite up the height step.
    "climb": (8.0, False, [
        Pose(hand_n=(1, -15), hand_f=(0, -14), head=(0, -1), weapon=-10),
        Pose(rise=3, hand_n=(1, -12), hand_f=(0, -11), foot_n=(1, 3), foot_f=(-1, 0), weapon=-5, drag=(0, 1),
             expression="angry"),
        Pose(rise=4, lean=1, hand_n=(2, -8), hand_f=(1, -8), foot_n=(3, 5), foot_f=(-1, 1), weapon=10, drag=(0, 1),
             expression="angry"),
        Pose(crouch=2, lean=1, hand_n=(2, -2), hand_f=(1, -2), foot_n=(2, 0), foot_f=(-2, 0), weapon=30,
             drag=(0, -1)),
        Pose(hand_n=(1, -1), hand_f=(-1, 0), foot_n=(2, 0), foot_f=(-2, 0), weapon=25),
    ]),
}

ANIM_ORDER = ["idle", "ready", "walk", "attack", "thrust", "shoot", "cast", "slam", "spin", "flurry", "bash",
              "block", "dodge", "jump", "item", "victory", "hurt", "down", "rise", "sit", "doze", "talk", "wave",
              "run", "knockback", "stagger", "death", "pray", "channel", "summon", "point", "buff", "kneel", "cheer",
              "crouch", "sneak", "hop", "climb"]
assert sorted(ANIM_ORDER) == sorted(ANIMS), "every animation needs a place in the sheet"


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
    crouch = 0 if pose.kneel or pose.sit else pose.crouch
    bob = pose.bob + (SIT_DROP if pose.sit else 0) + crouch
    lean = pose.lean
    sy = b.shoulder_y + bob
    hip = b.hip_y + (bob if pose.kneel or pose.sit else crouch)
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
