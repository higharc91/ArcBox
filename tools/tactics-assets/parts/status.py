"""Status overlays: looping effects drawn on a unit while a condition lasts (stunned stars, sleep Zs, poison
bubbles, flames, frost, an ice block, a hex sigil ...). They pair with the 16x16 status icons in the HUD; these are
what the TV shows on the battlefield.

Frames are the character frame size (32x40, anchor (16, 37)), drawn for the standard build, so the game places an
overlay exactly where the unit's sprite goes. Short and large builds carry their heads lower or higher: the manifest
gives a per-build vertical offset for the overhead effects. Every loop is whole-period, so it can run forever.

Each overlay combines a big readable sign (the thing you'd draw on the icon: stars, a heart, a Z, a reticle) with a
little secondary motion round the body (motes, a ring at the feet, drips), so a status reads at TV distance and still
feels alive up close. Things behind the head are dimmer than things in front of it, a cheap depth cue.
"""
from __future__ import annotations

import math
import shutil
from typing import Callable

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES
from arcpix.preview import Tile, contact_sheet, strip
from .rig import BUILDS, FRAME_W, FRAME_H, ANCHOR

SLOTS = SlotTable()
for _s in ("fx", "fx2", "fx3"):
    SLOTS.id(_s)
HEAD = (16, 13)        # head centre, standard build
BODY = (16, 26)
FEET = (16, 37)
TWO_PI = 2 * math.pi

STAR = [(0, -2), (0, -1), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (0, 1), (0, 2)]
STAR_S = [(0, -1), (-1, 0), (0, 0), (1, 0), (0, 1)]
HEART = [(-1, -1), (1, -1), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (-1, 1), (0, 1), (1, 1), (0, 2)]
HEART_S = [(-1, 0), (1, 0), (0, 0), (0, 1)]
NOTE = [(1, -3), (2, -3), (3, -2), (1, -2), (1, -1), (0, 0), (1, 0), (-1, 1), (0, 1)]
PLUS = [(0, -1), (-1, 0), (0, 0), (1, 0), (0, 1)]
PLUS_L = [(0, -2), (0, -1), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (0, 1), (0, 2)]
ZED = [(-1, -1), (0, -1), (1, -1), (0, 0), (-1, 1), (0, 1), (1, 1)]
ZED_L = [(-2, -2), (-1, -2), (0, -2), (1, -2), (2, -2), (1, -1), (0, 0), (-1, 1), (-2, 2), (-1, 2), (0, 2),
         (1, 2), (2, 2)]


def _stamp(c: Canvas, pts, x: float, y: float, slot: str, shade: int, hi: tuple[int, int] | None = None) -> None:
    """A small shape; `hi` is an offset in it painted one shade brighter (its lit pixel)."""
    for dx, dy in pts:
        c.px(round(x + dx), round(y + dy), slot, shade + (1 if hi == (dx, dy) else 0))


def _orbit(c: Canvas, f: int, n: int, count: int, shape, rx: float, ry: float, cy: float, *, slot="fx",
           small=None) -> None:
    """Things circling the head: behind it they're dimmer and smaller, in front brighter."""
    for k in range(count):
        a = TWO_PI * (k / count + f / n)
        x, y = HEAD[0] + math.cos(a) * rx, cy + math.sin(a) * ry
        front = math.sin(a) > 0
        _stamp(c, shape if front or small is None else small, x, y, slot, 5 if front else 3)


def _rising(c: Canvas, f: int, n: int, shape, xs: list[float], *, top: float, bottom: float, slot="fx",
            phase_step: float = 0.37, sway: float = 1.2) -> None:
    """Particles rising and fading on a loop: each starts at its own phase so the stream never pulses."""
    for k, x in enumerate(xs):
        t = (f / n + k * phase_step) % 1.0
        y = bottom - (bottom - top) * t
        shade = 5 if t < 0.4 else (4 if t < 0.7 else 2)
        _stamp(c, shape, x + math.sin(TWO_PI * (t + k * 0.2)) * sway, y, slot, shade)


def _feet_ring(c: Canvas, f: int, n: int, slot: str, rx: float = 10, shade: int = 3, dashes: int = 0) -> None:
    """An iso ring round the feet with a bright crest running round it once per loop."""
    for k in range(40):
        a = TWO_PI * k / 40
        if dashes and (k // 2) % dashes == 0:
            continue
        crest = math.cos(a - TWO_PI * f / n)
        front = math.sin(a) > 0
        c.px(FEET[0] + math.cos(a) * rx, FEET[1] - 0.5 + math.sin(a) * rx * 0.4, slot,
             shade + (crest > 0.8) - (not front))


def _canvas() -> Canvas:
    return Canvas(FRAME_W, FRAME_H, SLOTS)


# ------------------------------------------------------------------------------------------------------ statuses
def stunned(f: int, n: int) -> Canvas:
    """Three stars wheeling round the head on a faint dotted orbit, twinkling as they pass in front."""
    c = _canvas()
    for k in range(20):
        a = TWO_PI * k / 20
        if k % 2 == f % 2:
            c.px(HEAD[0] + math.cos(a) * 9, 4 + math.sin(a) * 2.5, "fx2", 2 if math.sin(a) < 0 else 3)
    _orbit(c, f, n, 3, STAR, 9, 2.5, 4, small=STAR_S)
    return c


def charmed(f: int, n: int) -> Canvas:
    """Two hearts circling the head, and small hearts floating up from it."""
    c = _canvas()
    _orbit(c, f, n, 2, HEART, 9, 2.4, 4, small=HEART_S)
    _rising(c, f, n, HEART_S, [10, 22], top=-2, bottom=10, slot="fx2", phase_step=0.5)
    return c


def sleeping(f: int, n: int) -> Canvas:
    """Zs drifting up and away from the head, growing as they rise, and a slow breath bubble."""
    c = _canvas()
    for k in range(3):
        t = (f / n + k / 3) % 1.0
        x, y = HEAD[0] + 4 + t * 7 + math.sin(t * TWO_PI) * 1.5, HEAD[1] - 2 - t * 13
        if t < 0.2:
            c.px(x, y, "fx", 4)
        else:
            _stamp(c, ZED if t < 0.55 else ZED_L, x, y, "fx", 5 if t < 0.75 else 3)
    r = 1 + 1.2 * (0.5 - 0.5 * math.cos(TWO_PI * f / n))
    c.ellipse(HEAD[0] + 3.5, HEAD[1] + 3, r, r, "fx2", 3)
    c.px(HEAD[0] + 3, HEAD[1] + 2, "fx2", 5)
    return c


def poisoned(f: int, n: int) -> Canvas:
    """Green bubbles rising off the body and popping into rings, a sickly haze breathing round the feet."""
    c = _canvas()
    _feet_ring(c, f, n, "fx2", rx=10, shade=2, dashes=3)
    for k, x in enumerate((10, 15, 20, 13, 22, 17)):
        t = (f / n + k * 0.29) % 1.0
        y = 34 - t * 28
        if t > 0.86:                                   # pop: a ring
            for dx, dy in ((-2, 0), (2, 0), (0, -2), (0, 2), (-1, -1), (1, 1)):
                c.px(x + dx, y + dy, "fx", 3)
        else:
            r = 0.6 if t < 0.25 else 1.3 if t < 0.6 else 1.8
            c.ellipse(x + 0.5, y + 0.5, r, r, "fx", 3)
            c.px(x - int(r * 0.6), y - int(r * 0.6), "fx", 5)
    return c


def burning(f: int, n: int) -> Canvas:
    """Tongues of flame licking up the body (each on its own phase), embers spiralling up and a wisp of smoke."""
    c = _canvas()
    ph = TWO_PI * f / n
    for k, (x, base, hgt) in enumerate(((9, 37, 9), (13, 35, 14), (19, 36, 13), (23, 37, 9), (16, 31, 10),
                                        (11, 26, 7), (21, 27, 7))):
        h = hgt * (0.75 + 0.25 * math.sin(ph + k * 1.9))
        sway = math.sin(ph * 2 + k) * 1.4
        c.polygon([(x - 2.5, base), (x + 2.5, base), (x + sway, base - h)], "fx", 2)
        c.polygon([(x - 1.6, base), (x + 1.6, base), (x + sway * 0.8, base - h * 0.75)], "fx", 3)
        c.polygon([(x - 0.8, base), (x + 0.8, base), (x + sway * 0.6, base - h * 0.45)], "fx", 5)
    _rising(c, f, n, [(0, 0)], [11, 20, 15, 24], top=0, bottom=22, slot="fx2", phase_step=0.27, sway=2)
    t = (f / n) % 1.0
    for d in range(3):
        c.px(17 + math.sin(t * TWO_PI + d) * 1.5, 6 - t * 6 - d, "fx3", 2)
    return c


def chilled(f: int, n: int) -> Canvas:
    """Frost crystals glinting over the body, snowflakes drifting down past it and rime breathing at the feet."""
    c = _canvas()
    spots = [(9, 14), (23, 18), (11, 28), (22, 31), (16, 8), (8, 22), (19, 24)]
    for k, (x, y) in enumerate(spots):
        t = ((f + k * 2) % n) / n
        if t < 0.4:
            _stamp(c, STAR if t < 0.15 else STAR_S, x, y, "fx", 5 if t < 0.2 else 4)
    for k, x in enumerate((7, 14, 21, 26)):
        t = (f / n + k * 0.27) % 1.0
        c.px(x + math.sin(t * TWO_PI * 2) * 1.5, -2 + t * 38, "fx2", 4)
    for x in range(8, 25):                              # rime at the feet, breathing
        if (x + f) % 3:
            c.px(x, 37, "fx", 4)
        if (x + f // 2) % 4 == 0:
            c.px(x, 36, "fx", 3)
    return c


def frozen(f: int, n: int) -> Canvas:
    """An ice block round the unit: lit and shadowed edges, facet lines, icicles at the base and a frosted top, a
    glint sweeping across. The middle stays open so the unit shows through."""
    c = _canvas()
    x0, x1, y0, y1 = 6, 26, 5, 38
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if x in (x0, x1) or y in (y0, y1):
                c.px(x, y, "fx", 5 if (x == x0 or y == y0) else 2)
            elif x == x0 + 1 or y == y0 + 1:
                c.px(x, y, "fx", 4)
            elif x == x1 - 1:
                c.px(x, y, "fx", 3)
            elif (x - x0 + y) % 9 == 0 and y < y0 + 8:          # frosted top
                c.px(x, y, "fx", 4)
    for k in range(6):                                          # facet cracks
        c.px(x0 + 3 + k, y0 + 10 + k * 2, "fx", 3)
        c.px(x1 - 4 - k // 2, y1 - 6 - k * 2, "fx", 3)
    g = x0 + (f / n) * (x1 - x0 + 14) - 7
    for k in range(12):
        x, y = int(g + k * 0.6), y0 + 2 + k * 3
        if x0 + 1 < x < x1 - 1:
            c.px(x, y, "fx2", 5)
            c.px(x + 1, y, "fx2", 4)
    for x in range(x0 + 1, x1):
        c.px(x, y1 - 1, "fx", 3)
    return c


def bleeding(f: int, n: int) -> Canvas:
    """Drops running down and falling from the body, splashing in a small pool at the feet."""
    c = _canvas()
    for k, x in enumerate((12, 19, 15)):
        t = (f / n + k * 0.37) % 1.0
        y = 22 + t * 15
        if t < 0.82:
            c.px(x, y, "fx", 3)
            c.px(x, y - 1, "fx", 4)
            c.px(x, y - 2, "fx", 2)
        else:
            c.px(x - 1, 36, "fx", 3)
            c.px(x + 1, 36, "fx", 3)
            c.px(x, 35, "fx", 4)
    for x in range(12, 21):
        c.px(x, 37, "fx", 2 if x in (12, 20) else 3)
    return c


def blessed(f: int, n: int) -> Canvas:
    """A golden halo over the head with a glint running round it, and motes of light rising."""
    c = _canvas()
    for k in range(28):
        a = TWO_PI * k / 28
        crest = math.cos(a - TWO_PI * f / n)
        c.px(HEAD[0] + math.cos(a) * 6, 2 + math.sin(a) * 1.6, "fx", 4 + (crest > 0.85) - (math.sin(a) < 0))
    _rising(c, f, n, [(0, 0), (0, -1)], [9, 13, 18, 22, 16], top=4, bottom=34)
    return c


def hasted(f: int, n: int) -> Canvas:
    """Speed streaks trailing behind the unit and an afterimage arc at its heels."""
    c = _canvas()
    for k, y in enumerate((14, 19, 24, 29, 34)):
        ln = 5 + ((f + k * 2) % n) * 2
        c.line(10 - ln, y, 9, y, "fx", 4 if k % 2 else 5)
        c.px(10 - ln - 2, y, "fx", 2)
    for k in range(5):
        c.px(22 + k, 36 - (k - 2) ** 2 // 3 - (f % 2), "fx2", 3)
    return c


def slowed(f: int, n: int) -> Canvas:
    """An hourglass overhead with sand trickling, and a clock hand sweeping round a ring at the feet."""
    c = _canvas()
    x, y = HEAD[0], 0
    c.rect(x - 3, y, x + 3, y, "fx2", 4)
    c.rect(x - 3, y + 8, x + 3, y + 8, "fx2", 4)
    full = 1 - f / n
    c.polygon([(x - 2.5, y + 1), (x + 3.5, y + 1), (x + 0.5, y + 4)], "fx", 2)
    c.polygon([(x - 2.5 + 2 * (1 - full), y + 1 + 2 * (1 - full)), (x + 3.5 - 2 * (1 - full), y + 1 + 2 * (1 - full)),
               (x + 0.5, y + 4)], "fx", 4)
    c.polygon([(x + 0.5, y + 4), (x + 3.5, y + 7.5), (x - 2.5, y + 7.5)], "fx", 2)
    c.polygon([(x + 0.5, y + 7.5 - 3 * (1 - full)), (x + 3.5, y + 7.5), (x - 2.5, y + 7.5)], "fx", 4)
    c.px(x, y + 4 + (f % 2), "fx", 5)
    _feet_ring(c, 0, n, "fx", rx=9, shade=2, dashes=4)
    a = TWO_PI * f / n
    c.line(FEET[0], FEET[1], FEET[0] + math.cos(a) * 8, FEET[1] + math.sin(a) * 3.2, "fx", 4)
    return c


def shielded(f: int, n: int) -> Canvas:
    """A bubble round the unit: rim lit top-left, a glint orbiting it and a band of hex cells drifting up."""
    c = _canvas()
    for k in range(56):
        a = TWO_PI * k / 56
        x, y = BODY[0] + math.cos(a) * 12, BODY[1] - 4 + math.sin(a) * 16
        hl = abs(((a / TWO_PI - f / n) % 1.0) - 0.5) < 0.06
        lit = -(math.cos(a) + math.sin(a)) > 0.6
        if k % 2 == 0 or hl or lit:
            c.px(x, y, "fx", 5 if hl else 4 if lit else 3)
    band = 38 - (f / n) * 34
    for y in range(int(band) - 2, int(band) + 2):
        for x in range(6, 27):
            if ((x - BODY[0]) / 11) ** 2 + ((y - BODY[1] + 4) / 15) ** 2 < 0.85 and (x + (y // 2) * 2) % 4 == 0 \
                    and y % 2 == 0:
                c.px(x, y, "fx", 2)
    return c


def marked(f: int, n: int) -> Canvas:
    """A reticle over the head, turning, with a chevron pointing down at the unit and pulsing."""
    c = _canvas()
    x, y = HEAD[0], 3
    rot = TWO_PI * f / n / 4                              # a quarter turn per loop: the reticle's own symmetry
    for k in range(32):
        a = TWO_PI * k / 32
        gap = min(abs(((a - rot) % (TWO_PI / 4)) - TWO_PI / 8), 9) < 0.35
        if not gap:
            c.px(x + math.cos(a) * 5.5, y + math.sin(a) * 3, "fx", 4 if math.sin(a) < 0 else 3)
    for k in range(4):
        a = rot + TWO_PI * k / 4 + TWO_PI / 8
        for d in (5, 6, 7):
            c.px(x + math.cos(a) * d, y + math.sin(a) * d * 0.5, "fx", 5 if d == 7 else 4)
    c.px(x, y, "fx", 5)
    bob = 1 if (f % n) < n // 2 else 0
    for k in range(3):
        c.px(x - 2 + k, y + 4 + bob + (k == 1), "fx2", 5)
    return c


def inspired(f: int, n: int) -> Canvas:
    """Notes floating up round the unit, with golden sparkles."""
    c = _canvas()
    _rising(c, f, n, NOTE, [8, 23, 15], top=0, bottom=26, phase_step=0.33)
    _rising(c, f, n, STAR_S, [11, 20], top=2, bottom=20, slot="fx2", phase_step=0.5, sway=2)
    return c


def regenerating(f: int, n: int) -> Canvas:
    """Green crosses rising through the body and a ring of light pulsing at the feet."""
    c = _canvas()
    _feet_ring(c, f, n, "fx", rx=9 + (f % 4 == 0), shade=3)
    _rising(c, f, n, PLUS, [10, 21, 15], top=4, bottom=32, phase_step=0.33)
    _rising(c, f, n, PLUS_L, [17], top=8, bottom=28, phase_step=0.5)
    return c


def concentrating(f: int, n: int) -> Canvas:
    """A ring of runes circling the head, a bright arc sweeping round it."""
    c = _canvas()
    for k in range(30):
        a = TWO_PI * k / 30
        x, y = HEAD[0] + math.cos(a) * 10, HEAD[1] - 6 + math.sin(a) * 2.8
        bright = abs(((k / 30 - f / n) % 1.0) - 0.5) > 0.4
        if k % 3 != 2 or bright:
            c.px(x, y, "fx", 5 if bright else (3 if math.sin(a) > 0 else 2))
    for k in range(4):
        a = TWO_PI * (k / 4 - f / n / 4)
        x, y = HEAD[0] + math.cos(a) * 10, HEAD[1] - 7 + math.sin(a) * 2.8
        if math.sin(a) > -0.3:
            c.px(x, y - 1, "fx2", 5)
            c.px(x, y + 1, "fx2", 4)
    return c


def frightened(f: int, n: int) -> Canvas:
    """Wavering wisps of dread round the head, cold sweat sliding off it and a shiver of lines at the shoulders."""
    c = _canvas()
    for k in range(3):
        ph = TWO_PI * (f / n + k / 3)
        x0 = HEAD[0] - 10 + k * 10
        for i in range(6):
            c.px(x0 + math.sin(ph + i * 0.9) * 1.6, HEAD[1] - 10 + i * 2, "fx", 4 if i < 2 else 2)
    for k, dx in enumerate((6, -6)):
        t = (f / n + k * 0.5) % 1.0
        c.px(HEAD[0] + dx, HEAD[1] - 2 + t * 6, "fx2", 4)
        c.px(HEAD[0] + dx, HEAD[1] - 1 + t * 6, "fx2", 3)
    if f % 2:
        for dx in (-9, 9):
            c.line(HEAD[0] + dx, 18, HEAD[0] + dx + (1 if dx > 0 else -1), 20, "fx", 3)
    return c


def hidden(f: int, n: int) -> Canvas:
    """Smoke curling up round the unit's feet and legs (drawn under the unit)."""
    c = _canvas()
    for k, x in enumerate((7, 11, 16, 21, 25)):
        t = (f / n + k * 0.23) % 1.0
        y = 37 - t * 16
        r = 1.8 + t * 2.4
        if t < 0.9:
            c.ellipse(x + 0.5, y, r, r * 0.75, "fx", 4 if t < 0.4 else 3)
            c.px(x - r * 0.4, y - r * 0.4, "fx", 5)
    return c


def raging(f: int, n: int) -> Canvas:
    """A red flame shell flaring up round the unit's outline (hollow, so the unit shows), and steam rising."""
    c = _canvas()
    ph = TWO_PI * f / n
    for k, (x, base, hgt) in enumerate(((6, 37, 14), (8, 30, 10), (26, 37, 14), (24, 30, 10), (5, 22, 8),
                                        (27, 22, 8))):
        h = hgt * (0.7 + 0.3 * math.sin(ph + k * 2.1))
        lean = -1.5 if x < 16 else 1.5
        c.polygon([(x - 2, base), (x + 2, base), (x + lean + math.sin(ph * 2 + k), base - h)], "fx", 3)
        c.polygon([(x - 1, base), (x + 1, base), (x + lean * 0.6, base - h * 0.55)], "fx", 5)
    _rising(c, f, n, [(0, 0), (1, -1)], [10, 22, 16], top=-2, bottom=10, slot="fx2", phase_step=0.33)
    return c


def paralysed(f: int, n: int) -> Canvas:
    """Rings of runes locked round the unit and sparks crackling between them: held fast."""
    c = _canvas()
    for j, y in enumerate((14, 24, 33)):
        for k in range(32):
            a = TWO_PI * k / 32
            front = math.sin(a) > 0
            c.px(BODY[0] + math.cos(a) * 9, y + math.sin(a) * 2.6, "fx", (4 if front else 2) + (k % 8 == f % 8))
    for k in range(2):
        a = TWO_PI * ((f * 3 + k * 5) % n) / n
        x, y = BODY[0] + math.cos(a) * 9, 19 + k * 9 + math.sin(a) * 2.5
        c.line(x, y, x + 1, y + 2, "fx2", 5)
        c.line(x + 1, y + 2, x, y + 4, "fx2", 5)
    return c


def restrained(f: int, n: int) -> Canvas:
    """Coils of rope or vine wrapped round the legs and body, straining (they shift a pixel as the unit
    struggles)."""
    c = _canvas()
    strain = 1 if f % n in (2, 3) else 0
    for j, (y, rx) in enumerate(((20, 9), (27, 8), (34, 7))):
        for k in range(36):
            a = TWO_PI * k / 36
            front = math.sin(a) > 0
            if not front and k % 2:
                continue
            x = BODY[0] + math.cos(a) * (rx + strain * (j == 1))
            c.px(x, y + math.sin(a) * 2.2 + (a / TWO_PI) * 2, "fx", (4 if front else 2) - (k % 6 == 0))
    for k in range(3):
        c.px(8 + k * 7, 22 + k * 5, "fx2", 4)
    return c


def blinded(f: int, n: int) -> Canvas:
    """A dark smoky band drifting across the eyes, with a confused spark circling."""
    c = _canvas()
    for x in range(7, 26):
        w = 1 + (math.sin(x * 0.7 + TWO_PI * f / n) > 0.2)
        for d in range(-1, w):
            c.px(x, HEAD[1] - 1 + d, "fx", 1 if d < w - 1 else 2)
    a = TWO_PI * f / n
    c.px(HEAD[0] + math.cos(a) * 8, HEAD[1] - 6 + math.sin(a) * 2, "fx2", 5)
    c.px(HEAD[0] + math.cos(a - 0.4) * 8, HEAD[1] - 6 + math.sin(a - 0.4) * 2, "fx2", 3)
    return c


def lit(f: int, n: int) -> Canvas:
    """Faerie fire: motes of coloured light clinging round the unit's outline, twinkling on and off."""
    c = _canvas()
    pts = [(8, 12), (24, 12), (6, 20), (26, 21), (7, 29), (25, 30), (10, 36), (22, 36), (16, 4), (11, 6), (21, 6)]
    for k, (x, y) in enumerate(pts):
        t = ((f + k * 5) % n) / n
        if t < 0.35:
            _stamp(c, STAR_S, x, y, "fx", 5 if t < 0.15 else 4)
        elif t < 0.7:
            c.px(x, y, "fx2", 4)
    return c


STATUSES: dict[str, tuple[Callable[[int, int], Canvas], int, str, str, str]] = {
    # id: (draw, frames, fx ramp, fx2 ramp, layer); fx3 is always a dim smoke
    "stunned": (stunned, 12, "metal/gold", "metal/gold", "over"),
    "charmed": (charmed, 12, "foliage/blossom", "magic/blood", "over"),
    "sleeping": (sleeping, 12, "magic/frost", "cloth/sky", "over"),
    "poisoned": (poisoned, 12, "magic/nature", "magic/nature", "over"),
    "burning": (burning, 8, "magic/fire", "magic/holy", "over"),
    "chilled": (chilled, 12, "magic/frost", "cloth/ivory", "over"),
    "frozen": (frozen, 8, "terrain/ice", "magic/frost", "over"),
    "bleeding": (bleeding, 8, "magic/blood", "magic/blood", "over"),
    "blessed": (blessed, 12, "magic/holy", "magic/holy", "over"),
    "hasted": (hasted, 6, "magic/arcane", "magic/frost", "under"),
    "slowed": (slowed, 12, "magic/frost", "metal/gold", "over"),
    "shielded": (shielded, 12, "magic/arcane", "magic/arcane", "over"),
    "marked": (marked, 8, "magic/blood", "magic/fire", "over"),
    "inspired": (inspired, 12, "metal/gold", "magic/holy", "over"),
    "regenerating": (regenerating, 12, "magic/nature", "magic/nature", "over"),
    "concentrating": (concentrating, 12, "magic/arcane", "magic/frost", "over"),
    "frightened": (frightened, 12, "magic/shadow", "magic/frost", "over"),
    "hidden": (hidden, 8, "cloth/charcoal", "cloth/charcoal", "under"),
    "raging": (raging, 8, "magic/blood", "cloth/ivory", "under"),
    "paralysed": (paralysed, 8, "metal/gold", "magic/holy", "over"),
    "restrained": (restrained, 8, "foliage/bark", "foliage/leaf", "over"),
    "blinded": (blinded, 12, "cloth/charcoal", "metal/gold", "over"),
    "lit": (lit, 12, "foliage/blossom", "magic/arcane", "over"),
}


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    return FAMILIES[fam][name]


def _pal(r1: str, r2: str) -> dict[str, list[str]]:
    return {"fx": _ref(r1), "fx2": _ref(r2), "fx3": _ref("cloth/slate")}


def build() -> None:
    out = PACK / "status"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles = {}, []
    for sid, (draw, n, r1, r2, layer) in STATUSES.items():
        pal = _pal(r1, r2)
        imgs = []
        for f in range(n):
            cv = draw(f, n)
            cv.outline()
            imgs.append(cv.render(pal, outline_mix=0.45))
        sheet = Image.new("RGBA", (FRAME_W * n, FRAME_H))
        for k, im in enumerate(imgs):
            sheet.paste(im, (k * FRAME_W, 0))
        save(sheet, out / f"{sid}.png")
        entries[sid] = {"file": f"{sid}.png", "frames": n, "fps": 10 if n >= 12 else 8, "loop": True, "layer": layer}
        tiles.append(Tile(strip(imgs[: min(n, 8)], 1, gap=2), sid, f"{n} frames · {layer}"))
    write_json({"frame": [FRAME_W, FRAME_H], "anchor": list(ANCHOR),
                "headOffset": {b: BUILDS[b].head_top - BUILDS["standard"].head_top for b in BUILDS},
                "statuses": entries,
                "notes": {"placement": "draw at the unit's anchor; shift overhead effects by headOffset[build]",
                          "layer": "over: after the unit; under: before it"}}, out / "manifest.json")
    save(contact_sheet("Status overlays", tiles, columns=3, zoom=3,
                       subtitle="Looping on-unit effects that pair with the HUD status icons. Same frame and anchor "
                                "as the characters."), PREVIEWS / "25-status.png")
    _gif()


def _gif() -> None:
    """Every status looping on a sample hero, so the motion can be judged in place."""
    from arcpix.anim import Actor, stage_gif
    from . import characters as C
    from .recipes import ALL
    hero = next(r for r in ALL if r.id == "tamsin")
    base = C.recipe_sheet(hero).frames
    idle = C.anim_table()[0].frames
    actors, seqs = [], []
    for sid, (draw, n, r1, r2, layer) in STATUSES.items():
        pal = _pal(r1, r2)
        frames = []
        for k in range(24):
            ov = draw(k % n, n)
            ov.outline()
            o = ov.render(pal, outline_mix=0.45)
            unit = base[idle[(k // 6) % len(idle)]]
            img = Image.new("RGBA", unit.size)
            if layer == "under":
                img.alpha_composite(o)
                img.alpha_composite(unit)
            else:
                img.alpha_composite(unit)
                img.alpha_composite(o)
            frames.append(img)
        actors.append(Actor(sid, layer, frames, ANCHOR))
        seqs.append(list(range(24)))
    stage_gif(PREVIEWS / "anim-status.gif", "Status overlays", actors, [("Statuses", seqs, 100)], zoom=3, columns=6,
              cell_w=40, cell_h=46)
