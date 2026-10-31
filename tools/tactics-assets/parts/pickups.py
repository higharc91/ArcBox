"""Loot on the battlefield: what a dropped item looks like until someone picks it up. Loot is personal and rolled with
need/greed, so drops have to read at a glance: a light beam in the rarity's colour, a glint, and the item's icon
bobbing above a small shadow. The coin pile and loot sack are drops in their own right.

The game composes a drop as: beam (under) + the item's icon from the icons set, bobbing by `bob` pixels per frame
+ glint (over). Everything loops in whole periods.
"""
from __future__ import annotations

import math
import shutil

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, strip
from .props import blob

SLOTS = SlotTable()
for _s in ("light", "gold", "cloth", "dark"):
    SLOTS.id(_s)

RARITY = {
    "common": ramp("#d9dce6", shift=0.04, spread=0.7),
    "uncommon": ramp("#5fe08a", shift=0.06, spread=0.85),
    "rare": ramp("#4f9cff", shift=0.05, spread=0.85),
    "epic": ramp("#b07bff", shift=0.06, spread=0.85),
    "legendary": ramp("#ffb43a", shift=0.07, spread=0.8),
}
BEAM_W, BEAM_H, BEAM_FRAMES = 32, 72, 12
BOB = [0, 0, -1, -1, -2, -2, -2, -2, -1, -1, 0, 0]  # pixels per beam frame, for the icon above the drop
RAYS = {"epic", "legendary"}                        # the top rarities also throw slow-turning rays


def beam(f: int, rays: bool = False) -> Canvas:
    """A column of light: a white-hot thread in a soft dithered column that fades out at the top, motes spiralling
    up round it, a ring pulsing out over the ground and a glow pooled at its foot. Rare drops add rays."""
    c = Canvas(BEAM_W, BEAM_H, SLOTS)
    cx, ground = BEAM_W // 2, BEAM_H - 8
    if rays:
        for k in range(6):                                   # rays fanning from the foot, turning slowly
            a = math.radians(200 + k * 28 + f * 28 / BEAM_FRAMES * 2)
            for d in range(4, 15):
                if d % 3 != f % 3:
                    c.px(cx + math.cos(a) * d * 1.1, ground + math.sin(a) * d * 0.45, "light", 2 + (d < 9))
    for y in range(4, ground):
        t = (ground - y) / (ground - 4)                     # 0 at the ground, 1 at the top
        half = 4.0 - t * 2.0
        for x in range(int(cx - half), int(cx + half) + 1):
            edge = abs(x - cx) > half - 1.2
            if edge and (x + y) % 2:                         # dithered edge: a soft column, not a slab
                continue
            if t > 0.65 and (x + y + f) % 2:
                continue                                     # the top fades out
            if t > 0.85 and (y + f) % 3 == 0:
                continue
            core = abs(x - cx) < 1
            streak = (y + f * 3) % 12 < 2 and not edge
            c.px(x, y, "light", 5 if core else (4 if streak or not edge else 3))
    for k in range(6):                                       # motes spiralling up
        tt = (f / BEAM_FRAMES + k / 6) % 1.0
        a = 2 * math.pi * (tt * 1.5 + k / 6)
        x = cx + math.cos(a) * 6
        y = ground - tt * (ground - 10) + math.sin(a) * 1.5
        c.px(x, y, "light", 5 if math.sin(a) > 0 else 3)
    r = 5 + (f % 6) * 1.6                                    # the ground ring pulses out
    for k in range(36):
        a = 2 * math.pi * k / 36
        if k % 2 == f % 2 or f % 6 < 2:
            c.px(cx + math.cos(a) * r, ground + 2 + math.sin(a) * r * 0.42, "light", 4 if f % 6 < 3 else 3)
    for k in range(-5, 6):                                    # glow pooled at the foot
        c.px(cx + k, ground + 2, "light", 5 if abs(k) < 2 else 4 if abs(k) < 4 else 3)
        if abs(k) < 4 and (k + f) % 2:
            c.px(cx + k, ground + 3, "light", 3)
    return c


def glint(f: int) -> Canvas:
    """A twinkle that flares and fades: four long arms, four short diagonal ones once it's big, a bright core.
    The 'there's something here' cue."""
    c = Canvas(16, 16, SLOTS)
    size = [0, 2, 4, 6, 5, 3, 1, 0][f]
    if size:
        for k in range(1, size + 1):
            shade = 5 if k < 3 else 4 if k < 5 else 3
            for dx, dy in ((k, 0), (-k, 0), (0, k), (0, -k)):
                c.px(8 + dx, 8 + dy, "light", shade)
        c.px(8, 8, "light", 5)
        if size > 2:
            for k in (1, 2):
                for dx, dy in ((1, 1), (-1, -1), (1, -1), (-1, 1)):
                    c.px(8 + dx * k, 8 + dy * k, "light", 4 if k == 1 else 3)
    return c


def coin_pile(f: int) -> Canvas:
    """A heap of coins: lit blobs stacked back to front, coin rims picked out, a couple of coins standing on edge,
    and a glint hopping across the pile."""
    c = Canvas(24, 16, SLOTS)
    for x, y, r in ((7, 12, 4), (16, 12, 4.5), (12, 9, 4.2), (11, 13, 5)):
        blob(c, "gold", x, y, r, r * 0.55, k=2.0)
    for x, y in ((6, 11), (13, 8), (17, 11), (10, 13), (14, 13), (9, 9)):     # coin rims
        c.px(x, y, "gold", 5)
        c.px(x + 1, y, "gold", 4)
        c.px(x + 2, y, "gold", 2)
    for x in (19, 4):                                        # coins on edge
        c.rect(x, 9, x, 12, "gold", 4)
        c.px(x + 1, 10, "gold", 2)
        c.px(x + 1, 11, "gold", 2)
    gx = [9, 14, 12, 17, 7, 11][f % 6]
    gy = [9, 8, 7, 10, 11, 9][f % 6]
    for dx, dy, sh in ((0, 0, 5), (0, -1, 4), (1, 0, 4), (-1, 0, 4), (0, 1, 4)):
        c.px(gx + dx, gy + dy, "light", sh)
    c.outline()
    return c


def loot_sack(f: int) -> Canvas:
    c = Canvas(24, 24, SLOTS)
    blob(c, "cloth", 12, 15, 7, 6.5, k=1.8)
    c.polygon([(9, 6), (15, 6), (14, 10), (10, 10)], "cloth", 3)
    c.rect(9, 9, 15, 9, "dark", 2)                           # drawstring
    c.px(16, 10, "dark", 2)
    for k in range(3):                                       # patches of coins peeking out
        c.px(11 + k * 2, 6, "gold", 5 if (k + f) % 3 == 0 else 3)
    c.outline()
    return c


def build() -> None:
    out = PACK / "pickups"
    shutil.rmtree(out, ignore_errors=True)
    tiles = []
    beams = {}
    for name, rp in RARITY.items():
        pal = {"light": rp, "gold": FAMILIES["metal"]["gold"], "cloth": rp, "dark": rp}
        imgs = [beam(f, name in RAYS).render(pal) for f in range(BEAM_FRAMES)]
        sheet = Image.new("RGBA", (BEAM_W * BEAM_FRAMES, BEAM_H))
        for k, im in enumerate(imgs):
            sheet.paste(im, (k * BEAM_W, 0))
        save(sheet, out / f"beam_{name}.png")
        beams[name] = f"beam_{name}.png"
        tiles.append(Tile(strip(imgs[:6], 1, gap=2), f"beam · {name}", f"{BEAM_FRAMES} frames · additive"))
    light = {"light": FAMILIES["metal"]["silver"], "gold": FAMILIES["metal"]["gold"],
             "cloth": ramp("#a07448", shift=0.06), "dark": FAMILIES["leather"]["dark"]}
    extras = {}
    for name, fn, n, size in (("glint", glint, 8, (16, 16)), ("coin_pile", coin_pile, 6, (24, 16)),
                              ("loot_sack", loot_sack, 6, (24, 24))):
        imgs = [fn(f).render(light) for f in range(n)]
        sheet = Image.new("RGBA", (size[0] * n, size[1]))
        for k, im in enumerate(imgs):
            sheet.paste(im, (k * size[0], 0))
        save(sheet, out / f"{name}.png")
        extras[name] = {"file": f"{name}.png", "frame": list(size), "frames": n, "fps": 8,
                        "anchor": [size[0] // 2, size[1] - 1]}
        tiles.append(Tile(strip(imgs, 1, gap=2), name, f"{n} frames"))
    write_json({"beam": {"frame": [BEAM_W, BEAM_H], "frames": BEAM_FRAMES, "fps": 10,
                         "anchor": [BEAM_W // 2, BEAM_H - 6], "blend": "add", "files": beams},
                "iconBob": BOB, "extras": extras,
                "compose": "beam (under, additive) + the item's icon (icons set) drawn 10 px above the anchor, offset "
                           "by iconBob[frame] + glint (over, every few seconds)"}, out / "manifest.json")
    save(contact_sheet("Pickups · loot on the ground", tiles, columns=4, zoom=4,
                       subtitle="Rarity beams (common to legendary), a glint, a coin pile and a loot sack."),
         PREVIEWS / "26-pickups.png")
