"""Projectiles drawn natively in 16 directions. Rotating pixel art at arbitrary angles blurs and breaks lines; drawing
each heading with pixel primitives keeps every arrow crisp. The game picks the frame whose heading is nearest the
screen-space flight direction (heading 0 = screen-right, counter-clockwise in 22.5 degree steps).

Frames are 32 px so a spell bolt has room for its glow and a trail that streams behind the head along the heading.
Every projectile animates: arrows and bolts shed a flickering wind trail, thrown weapons spin through the air (their
spin is independent of the heading, with a smear on the leading edge), and spell bolts flicker, their trails
wavering and shedding motes. Spell bolts are layered like the fx set: a dim halo, the coloured body and trail, a
white-hot core. Additive ones glow; solid ones (arrows, rocks, axes, acid) blend normally and get an outline.
"""
from __future__ import annotations

import math
import shutil

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet

SIZE = 32
HEADINGS = 16
C = SIZE / 2
SLOTS = SlotTable()
for _s in ("shaft", "head", "fletch", "glow", "core", "trail", "halo"):
    SLOTS.id(_s)


def _dir(h: int) -> tuple[float, float]:
    a = 2 * math.pi * h / HEADINGS
    return math.cos(a), -math.sin(a)          # screen y is down


def _soft(c: Canvas, x: float, y: float, r: float, slot: str, shade: int) -> None:
    """A dithered disc: an additive halo that reads as soft light without filling the frame."""
    for yy in range(int(y - r - 1), int(y + r + 2)):
        for xx in range(int(x - r - 1), int(x + r + 2)):
            if (xx + 0.5 - x) ** 2 + (yy + 0.5 - y) ** 2 <= r * r and (xx + yy) % 2 == 0:
                if 0 <= xx < c.w and 0 <= yy < c.h and c.slot[yy, xx] == 0:
                    c.px(xx, yy, slot, shade)


def _wind(c: Canvas, h: int, f: int, x: float, y: float, length: int) -> None:
    """Three dashed wind lines streaming back from (x, y); the dashes scroll back a step a frame."""
    dx, dy = _dir(h)
    px_, py_ = -dy, dx
    for off, ln in ((-1.5, length - 3), (0, length), (1.5, length - 4)):
        for k in range(2, ln):
            if (k + f * 2 + int(off * 3)) % 5 < 2:
                continue
            c.px(x - dx * k + px_ * off, y - dy * k + py_ * off, "trail", 4 if k < ln // 2 else 3)


# ------------------------------------------------------------------------------------------------------ physical
def arrow(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    tail, tip = (C - dx * 9, C - dy * 9), (C + dx * 8, C + dy * 8)
    _wind(c, h, f, *tail, 9)
    c.line(*tail, *tip, "shaft", 3)
    px_, py_ = -dy, dx                        # perpendicular
    hx, hy = tip
    c.polygon([(hx + dx * 2.5, hy + dy * 2.5), (hx - dx * 2 + px_ * 1.8, hy - dy * 2 + py_ * 1.8),
               (hx - dx * 2 - px_ * 1.8, hy - dy * 2 - py_ * 1.8)], "head", 4)
    c.px(hx + dx * 2, hy + dy * 2, "head", 5)
    tx, ty = tail
    for s in (-1, 1):                          # fletching
        c.line(tx + dx * 3.5, ty + dy * 3.5, tx + px_ * 2.2 * s, ty + py_ * 2.2 * s, "fletch", 4 if s < 0 else 3)
    return c


def bolt(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    _wind(c, h, f, C - dx * 6, C - dy * 6, 8)
    c.thick_line(C - dx * 6, C - dy * 6, C + dx * 4, C + dy * 4, 2, "shaft", 3)
    hx, hy = C + dx * 5, C + dy * 5
    c.polygon([(hx + dx * 2.5, hy + dy * 2.5), (hx - dy * 2, hy + dx * 2), (hx + dy * 2, hy - dx * 2)], "head", 4)
    c.px(hx + dx * 2, hy + dy * 2, "head", 5)
    return c


def _spinner(c: Canvas, h: int, f: int, n: int) -> tuple[float, float, float]:
    """The spin angle for frame f of n (one turn per n frames, independent of heading) and its unit vector."""
    a = 2 * math.pi * (h / HEADINGS) + f * 2 * math.pi / n
    return a, math.cos(a), -math.sin(a)


def _smear(c: Canvas, a: float, r: float, span: float = 0.9) -> None:
    """A thin arc trailing the spinning blade's tip, so the spin reads as motion rather than a pinwheel."""
    for k in range(1, 7):
        aa = a - span * k / 6
        c.px(C + math.cos(aa) * r, C - math.sin(aa) * r, "trail", 5 - k // 2)


def dagger(h: int, f: int) -> Canvas:
    """A thrown dagger tumbles (8 frames a turn), its point smearing an arc behind it."""
    c = Canvas(SIZE, SIZE, SLOTS)
    a, dx, dy = _spinner(c, h, f, 8)
    _smear(c, a, 7)
    c.line(C - dx * 2, C - dy * 2, C + dx * 7, C + dy * 7, "head", 5)
    c.line(C - dx * 6, C - dy * 6, C - dx * 3, C - dy * 3, "shaft", 2)
    c.line(C - dx * 2.5 - dy * 2.5, C - dy * 2.5 + dx * 2.5, C - dx * 2.5 + dy * 2.5, C - dy * 2.5 - dx * 2.5,
           "fletch", 4)
    return c


def axe(h: int, f: int) -> Canvas:
    """A thrown hand axe, end over end (8 frames a turn): the haft, a bearded head and a smear off its edge."""
    c = Canvas(SIZE, SIZE, SLOTS)
    a, dx, dy = _spinner(c, h, f, 8)
    _smear(c, a, 9, 1.2)
    c.thick_line(C - dx * 7, C - dy * 7, C + dx * 6, C + dy * 6, 2, "shaft", 3)
    px_, py_ = -dy, dx
    hx, hy = C + dx * 5, C + dy * 5
    c.polygon([(hx - dx * 2, hy - dy * 2), (hx + dx * 2, hy + dy * 2), (hx + dx * 3 + px_ * 5, hy + dy * 3 + py_ * 5),
               (hx - dx * 4 + px_ * 5, hy - dy * 4 + py_ * 5)], "head", 3)
    c.line(hx + dx * 3 + px_ * 5, hy + dy * 3 + py_ * 5, hx - dx * 4 + px_ * 5, hy - dy * 4 + py_ * 5, "head", 5)
    return c


def javelin(h: int, f: int) -> Canvas:
    """A javelin: a long shaft with a leaf head, flexing slightly and trailing wind."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    px_, py_ = -dy, dx
    flex = (0.6 if f % 2 else -0.6)
    tail, tip = (C - dx * 13, C - dy * 13), (C + dx * 10, C + dy * 10)
    _wind(c, h, f, *tail, 6)
    mx, my = C + px_ * flex, C + py_ * flex
    c.line(*tail, mx, my, "shaft", 3)
    c.line(mx, my, *tip, "shaft", 3)
    hx, hy = tip
    c.polygon([(hx + dx * 4, hy + dy * 4), (hx + px_ * 1.6, hy + py_ * 1.6), (hx - dx * 2, hy - dy * 2),
               (hx - px_ * 1.6, hy - py_ * 1.6)], "head", 4)
    c.px(hx + dx * 3, hy + dy * 3, "head", 5)
    c.line(*tail, tail[0] + dx * 2, tail[1] + dy * 2, "fletch", 4)
    return c


def rock(h: int, f: int) -> Canvas:
    """A thrown rock tumbling (its highlight walks round it) and kicking a puff of grit behind."""
    c = Canvas(SIZE, SIZE, SLOTS)
    from .props import blob
    a = f * math.pi / 2
    blob(c, "shaft", C, C, 4.2, 3.6, k=2.2, light=(-0.55 * math.cos(a) - 0.3, -0.85))
    c.px(C - 1 + round(math.cos(a)), C - 1 + round(math.sin(a)), "shaft", 5)
    dx, dy = _dir(h)
    for k, s in ((6, 3), (8, 2), (10, 2)):
        wob = (1 if (k + f) % 2 else -1)
        c.px(C - dx * k - dy * wob, C - dy * k + dx * wob, "fletch", s)
    return c


def acid(h: int, f: int) -> Canvas:
    """A glob of acid: a wobbling, glossy drop with drips shed behind it."""
    c = Canvas(SIZE, SIZE, SLOTS)
    from .props import blob
    dx, dy = _dir(h)
    sq = 1 + 0.15 * math.sin(f * math.pi / 2)
    cx, cy = C + dx * 3, C + dy * 3
    for k, r in ((6, 1.6), (10, 1.0), (13, 0.7)):
        j = (k + f * 3) % 4 - 1.5
        c.ellipse(cx - dx * k - dy * j * 0.6, cy - dy * k + dx * j * 0.6, r, r, "glow", 3)
    blob(c, "glow", cx, cy, 4 * sq, 4 / sq, k=2.0)
    c.px(cx - 1.5, cy - 1.5, "core", 5)
    c.px(cx - 0.5, cy - 1.5, "core", 4)
    return c


# ---------------------------------------------------------------------------------------------------- spell bolts
def _comet(c: Canvas, h: int, f: int, r: float, tail: int, *, flicker: float, ahead: float = 4.0,
           halo: float = 2.5) -> tuple[float, float]:
    """A glowing head with a tapering, wavering trail: a dithered halo, the body ramp cooling along the trail and a
    white-hot core pushed to the front. Returns the head's centre."""
    dx, dy = _dir(h)
    cx, cy = C + dx * ahead, C + dy * ahead
    for k in range(tail, 0, -1):               # the trail, thinning and cooling behind the head
        t = k / tail
        wob = math.sin(f * 1.7 + k * 0.9) * flicker * t
        x, y = cx - dx * k * 1.3 - dy * wob, cy - dy * k * 1.3 + dx * wob
        rr = r * (1 - t * 0.8)
        c.ellipse(x + 0.5, y + 0.5, rr, rr, "glow", 2 if t > 0.7 else 3 if t > 0.4 else 4)
    c.ellipse(cx + 0.5, cy + 0.5, r, r, "glow", 4)
    c.ellipse(cx + 0.5 + dx * 0.8, cy + 0.5 + dy * 0.8, r * 0.55, r * 0.55, "core", 5)
    _soft(c, cx + 0.5, cy + 0.5, r + halo, "halo", 1 + (f % 2))
    return cx, cy


def _motes(c: Canvas, h: int, f: int, cx: float, cy: float, n: int, spread: float, slot: str = "core") -> None:
    """Motes shed from the trail, drifting back and outward a little each frame."""
    dx, dy = _dir(h)
    for k in range(n):
        d = 6 + ((k * 5 + f * 3) % 12)
        s = ((k * 7) % 5 - 2) / 2 * spread * (d / 12)
        c.px(cx - dx * d - dy * s, cy - dy * d + dx * s, slot, 5 if d < 10 else 3)


def fireball(h: int, f: int) -> Canvas:
    c = Canvas(SIZE, SIZE, SLOTS)
    cx, cy = _comet(c, h, f, 3.6, 9, flicker=1.4)
    _motes(c, h, f, cx, cy, 4, 3.0, "glow")
    return c


def eldritch(h: int, f: int) -> Canvas:
    """An eldritch orb: a dark heart in a violet rim, three crackles orbiting it, a short writhing tail."""
    c = Canvas(SIZE, SIZE, SLOTS)
    cx, cy = _comet(c, h, f, 3.0, 5, flicker=0.6, halo=3)
    c.ellipse(cx + 0.5, cy + 0.5, 1.4, 1.4, "shaft", 1)
    for k in range(3):                         # crackling arcs orbiting the orb
        a = f * math.pi / 3 + k * 2.1
        x, y = cx + math.cos(a) * 5, cy + math.sin(a) * 5
        c.px(x, y, "core", 5)
        c.px(x + math.cos(a + 1.6), y + math.sin(a + 1.6), "glow", 4)
    return c


def missile(h: int, f: int) -> Canvas:
    """Magic missile: a dart of force with a sparkling, wavering trail and a flicker of light round its point."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = C + dx * 6, C + dy * 6
    for k in range(1, 14):
        wob = math.sin(k * 0.9 + f * 1.6) * 1.8
        x, y = cx - dx * k * 1.3 - dy * wob, cy - dy * k * 1.3 + dx * wob
        if k % 2 or k < 4:
            c.px(x, y, "glow", 5 if k < 4 else 4 if k < 8 else 2)
    _soft(c, cx, cy, 4.5, "halo", 1 + f % 2)
    c.polygon([(cx + dx * 4, cy + dy * 4), (cx - dy * 2, cy + dx * 2), (cx - dx * 1.5, cy - dy * 1.5),
               (cx + dy * 2, cy - dx * 2)], "glow", 4)
    c.line(cx - dx, cy - dy, cx + dx * 3, cy + dy * 3, "core", 5)
    if f % 2 == 0:
        c.px(cx + dx * 5, cy + dy * 5, "core", 5)
    return c


def ice(h: int, f: int) -> Canvas:
    """A frost shard: a faceted spike (lit face, shadow face, bright edge) shedding frost motes, a glint running
    along it."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    tip = (C + dx * 9, C + dy * 9)
    back = (C - dx * 7, C - dy * 7)
    side1 = (C - dx * 4 - dy * 3, C - dy * 4 + dx * 3)
    side2 = (C - dx * 4 + dy * 3, C - dy * 4 - dx * 3)
    _soft(c, C + dx * 2, C + dy * 2, 6, "halo", 1)
    c.polygon([tip, side1, back], "glow", 4)
    c.polygon([tip, back, side2], "glow", 2)
    c.line(*back, *tip, "core", 5)
    g = 8 - (f % 4) * 4
    c.px(C + dx * g, C + dy * g, "core", 5)
    _motes(c, h, f, C, C, 5, 4.0, "glow")
    return c


def lightning(h: int, f: int) -> Canvas:
    """A ball of lightning: a white core in a jittering cage of tiny forks, a zigzag tail behind (chromatic orb,
    witch bolt)."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = C + dx * 4, C + dy * 4
    _soft(c, cx, cy, 6, "halo", 1 + f % 2)
    x, y = cx, cy
    for k in range(5):                          # zigzag tail
        s = 2.2 if (k + f) % 2 else -2.2
        nx, ny = cx - dx * (k + 1) * 3 - dy * s, cy - dy * (k + 1) * 3 + dx * s
        c.line(x, y, nx, ny, "glow", 4 if k < 2 else 3)
        x, y = nx, ny
    for k in range(4):                          # forks round the core, different every frame
        a = (f * 1.9 + k * 1.6)
        x0, y0 = cx + math.cos(a) * 2, cy + math.sin(a) * 2
        x1, y1 = cx + math.cos(a + 0.4) * 5, cy + math.sin(a + 0.4) * 5
        c.line(x0, y0, x1, y1, "glow", 4)
        c.px(x1 + math.cos(a - 0.6), y1 + math.sin(a - 0.6), "glow", 3)
    c.ellipse(cx + 0.5, cy + 0.5, 2, 2, "core", 5)
    return c


def radiant(h: int, f: int) -> Canvas:
    """A bolt of radiance (guiding bolt): a star-pointed core with rays that pulse and turn, and a straight streak
    of light behind."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = C + dx * 4, C + dy * 4
    for k in range(1, 13):
        c.px(cx - dx * k, cy - dy * k, "glow", 5 if k < 4 else 4 if k < 8 else 3)
        if k < 7:
            c.px(cx - dx * k - dy, cy - dy * k + dx, "glow", 2)
    _soft(c, cx, cy, 6, "halo", 1 + f % 2)
    L = 5 if f % 2 == 0 else 4
    for k in range(4):
        a = math.radians(45 * (f % 2) + 90 * k)
        for d in range(1, L + 1):
            c.px(cx + math.cos(a) * d, cy + math.sin(a) * d, "glow", 5 - d // 2)
    c.ellipse(cx + 0.5, cy + 0.5, 1.6, 1.6, "core", 5)
    return c


def necrotic(h: int, f: int) -> Canvas:
    """A bolt of rot (shadow bolt, dark bolt): a black-green orb with a sickly rim and two eye-like sparks, twin
    wisps coiling round each other behind it. Normal blending, so it darkens."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    px_, py_ = -dy, dx
    cx, cy = C + dx * 4, C + dy * 4
    for s in (1, -1):
        for k in range(2, 14):
            w = math.sin(k * 0.8 + f * math.pi / 2) * 2.4 * s
            c.px(cx - dx * k * 1.1 + px_ * w, cy - dy * k * 1.1 + py_ * w, "trail" if k > 7 else "glow",
                 2 if k > 7 else 3)
    c.ellipse(cx + 0.5, cy + 0.5, 4, 4, "glow", 3)
    c.ellipse(cx + 0.5, cy + 0.5, 3, 3, "trail", 1)
    c.px(cx - 1 + px_, cy + py_ * 1, "core", 5)
    c.px(cx - 1 - px_, cy - py_ * 1, "core", 5 if f % 2 else 4)
    return c


def poison(h: int, f: int) -> Canvas:
    """A bolt of sickness (ray of sickness, poison spit): a bubbling green blob trailing a little cloud of puffs.
    Normal blending."""
    c = Canvas(SIZE, SIZE, SLOTS)
    from .props import blob
    dx, dy = _dir(h)
    cx, cy = C + dx * 4, C + dy * 4
    for k, r in ((5, 2.4), (9, 2.0), (12, 1.5)):
        j = math.sin(k + f * math.pi / 2) * 1.2
        blob(c, "trail", cx - dx * k - dy * j, cy - dy * k + dx * j, r, r, k=1.6)
    blob(c, "glow", cx, cy, 3.6, 3.6, k=2.0)
    c.px(cx - 1 + (f % 2), cy - 2, "core", 5)
    return c


def force(h: int, f: int) -> Canvas:
    """A dart of force made of hexagons: a bright hex head and three smaller hexes trailing, each spinning."""
    c = Canvas(SIZE, SIZE, SLOTS)
    dx, dy = _dir(h)
    cx, cy = C + dx * 5, C + dy * 5
    _soft(c, cx, cy, 5, "halo", 1 + f % 2)
    for k, r in ((14, 1.2), (10, 1.6), (6, 2.0), (0, 3.2)):
        x, y = cx - dx * k, cy - dy * k
        rot = f * 15 + k * 10
        pts = [(x + r * math.cos(math.radians(rot + 60 * j)), y + r * math.sin(math.radians(rot + 60 * j)))
               for j in range(6)]
        if r > 2:
            c.polygon(pts, "glow", 3)
        for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
            c.line(x0, y0, x1, y1, "glow", 5 if k == 0 else 4 - k // 6)
    c.px(cx, cy, "core", 5)
    return c


# id: (draw, frames, palette, blend, fps)
KINDS = {
    "arrow": (arrow, 4, {"shaft": "wood/oak", "head": "metal/steel", "fletch": "cloth/ivory", "trail": "fx/wind"},
              "normal", 16),
    "bolt": (bolt, 4, {"shaft": "wood/dark", "head": "metal/iron", "trail": "fx/wind"}, "normal", 16),
    "dagger": (dagger, 8, {"shaft": "leather/dark", "head": "metal/steel", "fletch": "metal/gold",
                           "trail": "fx/wind"}, "normal", 20),
    "rock": (rock, 4, {"shaft": "terrain/stone", "fletch": "terrain/dirt"}, "normal", 12),
    "fireball": (fireball, 6, {"glow": "magic/fire", "core": "magic/holy", "halo": "magic/fire"}, "add", 16),
    "eldritch": (eldritch, 6, {"glow": "magic/shadow", "core": "magic/void", "shaft": "magic/void",
                               "halo": "magic/shadow"}, "add", 14),
    "missile": (missile, 6, {"glow": "magic/arcane", "core": "magic/frost", "halo": "magic/arcane"}, "add", 16),
    "ice": (ice, 4, {"glow": "magic/frost", "core": "cloth/ivory", "halo": "magic/frost"}, "add", 12),
    "axe": (axe, 8, {"shaft": "wood/oak", "head": "metal/steel", "trail": "fx/wind"}, "normal", 20),
    "javelin": (javelin, 2, {"shaft": "wood/pale", "head": "metal/steel", "fletch": "leather/red",
                             "trail": "fx/wind"}, "normal", 12),
    "acid": (acid, 4, {"glow": "fx/acid", "core": "fx/bile"}, "normal", 12),
    "lightning": (lightning, 6, {"glow": "magic/storm", "core": "cloth/ivory", "halo": "magic/storm"}, "add", 18),
    "radiant": (radiant, 4, {"glow": "magic/holy", "core": "cloth/ivory", "halo": "metal/gold"}, "add", 14),
    "necrotic": (necrotic, 4, {"glow": "fx/grave", "core": "fx/grave", "trail": "fx/necro"}, "normal", 12),
    "poison": (poison, 4, {"glow": "fx/poison", "core": "fx/bile", "trail": "fx/fel_dark"}, "normal", 12),
    "force": (force, 4, {"glow": "fx/force", "core": "magic/frost", "halo": "fx/force"}, "add", 16),
}


def _ramps(slots: dict[str, str]) -> dict[str, list[str]]:
    from .fx import FX_RAMPS
    out = {k: FX_RAMPS[v[3:]] if v.startswith("fx/") else FAMILIES[v.split("/")[0]][v.split("/")[1]]
           for k, v in slots.items()}
    for name in SLOTS.names[1:]:
        out.setdefault(name, ramp("#808080"))
    return out


def build() -> None:
    out = PACK / "projectiles"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles, anim = {}, [], []
    for pid, (draw, n, slots, blend, fps) in KINDS.items():
        pal = _ramps(slots)
        sheet = Image.new("RGBA", (SIZE * n, SIZE * HEADINGS))
        for h in range(HEADINGS):
            for f in range(n):
                cv = draw(h, f)
                if blend == "normal":
                    cv.outline(skip={"trail", "halo"})
                sheet.paste(cv.render(pal, outline_mix=0.5), (f * SIZE, h * SIZE))
        save(sheet, out / f"{pid}.png")
        entries[pid] = {"file": f"{pid}.png", "frames": n, "fps": fps, "blend": blend}
        row = Image.new("RGBA", (SIZE * HEADINGS, SIZE))
        for h in range(HEADINGS):
            row.alpha_composite(sheet.crop((0, h * SIZE, SIZE, (h + 1) * SIZE)), (h * SIZE, 0))
        tiles.append(Tile(row, pid, f"16 headings × {n} frame{'s' if n > 1 else ''} · {fps} fps · {blend}"))
        frow = Image.new("RGBA", (SIZE * max(8, n), SIZE))
        for f in range(n):
            frow.alpha_composite(sheet.crop((f * SIZE, 2 * SIZE, (f + 1) * SIZE, 3 * SIZE)), (f * SIZE, 0))
        anim.append(Tile(frow, pid, f"heading 2 · frames 0-{n - 1}"))
    write_json({"frame": [SIZE, SIZE], "anchor": [SIZE // 2, SIZE // 2], "headings": HEADINGS,
                "layout": "row = heading (0 = screen-right, counter-clockwise, 22.5° steps), column = frame",
                "projectiles": entries}, out / "manifest.json")
    save(contact_sheet("Projectiles · 16 native headings", tiles, columns=1, zoom=3,
                       subtitle="Drawn per heading instead of rotated, so lines stay crisp. Pick the row nearest the "
                                "screen-space flight direction."), PREVIEWS / "33-projectiles.png")
    save(contact_sheet("Projectiles · animation frames", anim, columns=2, zoom=3,
                       subtitle="Every projectile animates: trails flicker, thrown weapons spin, bolts waver."),
         PREVIEWS / "33b-projectiles-frames.png")
