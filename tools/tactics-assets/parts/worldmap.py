"""The campaign world map: where the party has been and where the next session could go. It's drawn as an
illustrated parchment map (ink lines, muted washes, hatched mountains, stamped trees) rather than as pixel terrain,
so it reads as a different space from the battlefield: the story's map, not the fight's.

Pieces the game composes: a tileable parchment, location icons, a party token, dotted route pieces and fog of war.
`sample_map()` builds a whole map from them, the way a content pack would.
"""
from __future__ import annotations

import math
import shutil

import numpy as np
from PIL import Image

from arcpix import tex
from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import ramp
from arcpix.preview import Tile, contact_sheet

INK = ramp("#4a3226", shift=0.05, spread=1.2)
PAPER = ramp("#e6d3a8", shift=0.05, spread=0.6)
WASH = {"sea": ramp("#7fa7b8", shift=0.04, spread=0.7), "forest": ramp("#7f9a5c", shift=0.06, spread=0.8),
        "hills": ramp("#b8b07a", shift=0.06, spread=0.8), "snow": ramp("#e8eef2", shift=0.04, spread=0.5),
        "desert": ramp("#e0bf7c", shift=0.05, spread=0.7), "swamp": ramp("#7f8a5c", shift=0.06, spread=0.8),
        "red": ramp("#b8483c", shift=0.07), "gold": ramp("#d9a93a", shift=0.06), "blue": ramp("#4f6fa8", shift=0.05),
        "stone": ramp("#9a9488", shift=0.04), "roof": ramp("#a8543c", shift=0.06)}

SLOTS = SlotTable()
for _s in ("ink", "paper", "a", "b", "c"):
    SLOTS.id(_s)
ICON = 24


# --------------------------------------------------------------------------------------------------- parchment
def parchment(seed: int = 3, size: int = 128) -> Image.Image:
    """Tileable aged paper: soft mottling, a few fibres and foxing spots, all from periodic fields."""
    r = tex.rng(seed)
    n = tex.fbm(size, size, 4, r, octaves=4)
    shade = tex.quantize(n, [(0.3, 2), (0.72, 4)], 3, dither=0.05)
    spots = tex.fbm(size, size, 16, r, octaves=1)
    shade = np.where(spots < 0.08, 1, shade)
    fib = r.random((size, size)) < 0.004
    shade = np.where(fib, 4, shade)
    arr = np.zeros((size, size, 4), np.uint8)
    for k in range(6):
        h = PAPER[k].lstrip("#")
        arr[shade == k, :3] = [int(h[i:i + 2], 16) for i in (0, 2, 4)]
    arr[..., 3] = 255
    return Image.fromarray(arr, "RGBA")


# ------------------------------------------------------------------------------------------------------ icons
def _icon() -> Canvas:
    return Canvas(ICON, ICON, SLOTS)


def _ground(c: Canvas, w: int = 16) -> None:
    """The little ink shadow every icon sits on, so they settle onto the paper."""
    c.ellipse(12, 21, w / 2, 1.6, "ink", 2)


def town(walls: bool = True) -> Canvas:
    c = _icon()
    _ground(c, 20)
    for x, y, w, h in ((4, 11, 6, 7), (10, 8, 6, 10), (15, 12, 5, 6)):
        c.rect(x, y + 3, x + w - 1, y + h, "paper", 4)               # walls
        c.polygon([(x - 1, y + 3), (x + w, y + 3), (x + w / 2 - 0.5, y - 1)], "a", 3)   # roof
        c.px(x + w // 2, y + h - 2, "ink", 1)                        # door
    c.rect(12, 3, 12, 7, "ink", 2)                                   # a spire
    c.px(12, 2, "b", 4)
    if walls:
        c.rect(3, 18, 20, 19, "c", 3)
        for x in range(3, 21, 3):
            c.px(x, 17, "c", 4)
    c.outline()
    return c


def castle() -> Canvas:
    c = _icon()
    _ground(c, 20)
    c.rect(5, 9, 18, 19, "c", 3)
    for x in (4, 16):                                               # towers
        c.rect(x, 5, x + 3, 19, "c", 4)
        c.polygon([(x - 1, 5), (x + 4, 5), (x + 1.5, 1)], "a", 3)
    for x in range(6, 16, 2):
        c.px(x, 8, "c", 4)
    c.rect(10, 14, 12, 19, "ink", 1)                                 # gate
    c.line(13, 1, 13, 5, "ink", 2)                                   # banner
    c.rect(14, 1, 16, 2, "b", 4)
    c.outline()
    return c


def dungeon() -> Canvas:
    c = _icon()
    _ground(c, 20)
    c.polygon([(2, 20), (8, 6), (13, 3), (19, 8), (22, 20)], "c", 3)  # a crag
    c.line(8, 6, 4, 18, "c", 4)
    c.ellipse(12, 16.5, 4, 4, "ink", 0)                              # cave mouth
    c.rect(8, 17, 16, 20, "ink", 0)
    for x in (10, 12, 14):                                           # teeth
        c.px(x, 13, "paper", 5)
    c.outline()
    return c


def ruins() -> Canvas:
    c = _icon()
    _ground(c, 20)
    for x, top in ((5, 7), (11, 12), (17, 5)):                       # broken columns
        c.rect(x, top, x + 2, 19, "c", 4)
        c.rect(x + 2, top, x + 2, 19, "c", 2)
        c.px(x + 1, top - 1, "c", 3)
    c.rect(4, 6, 10, 6, "c", 3)                                      # a fallen lintel
    c.rect(9, 18, 15, 19, "c", 2)
    c.outline()
    return c


def tower() -> Canvas:
    c = _icon()
    _ground(c, 12)
    c.polygon([(8, 20), (16, 20), (15, 6), (9, 6)], "c", 3)
    c.rect(13, 6, 15, 20, "c", 2)
    c.polygon([(7, 6), (17, 6), (12, 0)], "a", 3)
    c.rect(11, 10, 12, 12, "b", 5)                                   # a lit window
    c.outline()
    return c


def camp() -> Canvas:
    c = _icon()
    _ground(c, 18)
    c.polygon([(3, 19), (9, 8), (15, 19)], "paper", 4)               # tent
    c.polygon([(9, 8), (15, 19), (11, 19)], "paper", 3)
    c.line(9, 8, 9, 19, "ink", 1)
    c.polygon([(17, 19), (21, 19), (19, 13)], "b", 4)                # campfire
    c.px(19, 15, "b", 5)
    c.outline()
    return c


def port() -> Canvas:
    c = _icon()
    _ground(c, 20)
    c.rect(2, 16, 21, 17, "a", 2)                                    # pier
    for x in (4, 10, 16):
        c.rect(x, 17, x, 20, "a", 1)
    c.polygon([(8, 14), (20, 14), (18, 16), (10, 16)], "a", 3)       # boat
    c.line(14, 3, 14, 14, "ink", 2)
    c.polygon([(14, 4), (14, 12), (20, 12)], "paper", 5)             # sail
    c.outline()
    return c


def shrine() -> Canvas:
    c = _icon()
    _ground(c, 14)
    c.rect(7, 13, 16, 19, "c", 4)
    c.polygon([(5, 13), (18, 13), (11.5, 7)], "c", 3)
    c.ellipse(11.5, 4, 2.4, 2.4, "b", 5)                             # a glowing orb
    c.rect(11, 15, 12, 19, "ink", 1)
    c.outline()
    return c


def lair() -> Canvas:
    c = _icon()
    _ground(c, 16)
    c.ellipse(12, 11, 7, 6, "paper", 5)                              # a skull
    c.rect(8, 15, 16, 19, "paper", 4)
    for x in (9, 14):
        c.rect(x, 9, x + 1, 11, "ink", 0)
    c.px(12, 13, "ink", 0)
    for x in (9, 11, 13, 15):
        c.px(x, 17, "ink", 1)
    c.polygon([(4, 9), (7, 4), (8, 8)], "a", 3)                      # horns
    c.polygon([(20, 9), (17, 4), (16, 8)], "a", 3)
    c.outline()
    return c


def forest_icon() -> Canvas:
    c = _icon()
    _ground(c, 20)
    for x, y in ((6, 9), (13, 6), (18, 10), (10, 12)):
        c.ellipse(x, y + 3, 4, 4.5, "a", 3)
        c.px(x - 1, y + 1, "a", 5)
        c.rect(x, y + 7, x, y + 9, "ink", 2)
    c.outline()
    return c


def mountain_icon() -> Canvas:
    c = _icon()
    _ground(c, 20)
    c.polygon([(2, 20), (10, 4), (17, 20)], "c", 4)
    c.polygon([(10, 4), (17, 20), (12, 20)], "c", 2)
    c.polygon([(9, 20), (16, 9), (22, 20)], "c", 3)
    c.polygon([(8, 7), (10, 4), (12, 8), (10, 7)], "paper", 5)       # snowcap
    c.outline()
    return c


def swamp_icon() -> Canvas:
    """A bog: a pool of murky wash, a bare snag leaning over it and reeds at its edge."""
    c = _icon()
    c.ellipse(12, 19, 9, 3, "a", 2)
    c.ellipse(11, 18.5, 6, 1.6, "a", 4)
    c.line(15, 19, 15, 7, "ink", 2)
    c.line(15, 11, 19, 7, "ink", 2)
    c.line(15, 9, 12, 6, "ink", 2)
    for x, h in ((5, 6), (7, 8), (8, 5), (20, 6)):
        c.line(x, 20, x, 20 - h, "b", 3)
        c.px(x, 20 - h, "c", 2)
    c.outline()
    return c


def oasis_icon() -> Canvas:
    """An oasis: a pool ringed with sand and a palm bending over it."""
    c = _icon()
    _ground(c, 20)
    c.ellipse(12, 19, 8, 2.6, "c", 3)
    c.ellipse(12, 18.6, 5.5, 1.6, "b", 4)
    c.line(9, 19, 11, 6, "ink", 2)
    c.line(10, 19, 12, 6, "ink", 3)
    for dx, dy in ((-6, 3), (-4, -2), (4, -2), (6, 3), (0, -4)):
        c.line(12, 6, 12 + dx, 6 + dy, "a", 4 if dx < 0 else 2)
        c.line(12, 7, 12 + dx, 7 + dy, "a", 3)
    c.outline()
    return c


def volcano_icon() -> Canvas:
    """A volcano: a dark cone with lava down its flank and a plume of smoke."""
    c = _icon()
    _ground(c, 20)
    c.polygon([(2, 20), (9, 7), (15, 7), (22, 20)], "c", 2)
    c.polygon([(2, 20), (9, 7), (12, 7), (8, 20)], "c", 3)
    c.line(13, 8, 14, 13, "a", 5)
    c.line(14, 13, 16, 19, "a", 4)
    c.rect(9, 6, 15, 7, "a", 4)
    for k, (x, y, rr) in enumerate(((12, 4, 2.2), (14, 2, 1.8), (17, 1, 1.4))):
        c.ellipse(x, y, rr, rr * 0.8, "paper", 3 if k else 4)
    c.outline()
    return c


def glacier_icon() -> Canvas:
    """A glacier: jagged blue-white ice peaks with a crevasse."""
    c = _icon()
    _ground(c, 20)
    c.polygon([(2, 20), (8, 6), (13, 20)], "a", 4)
    c.polygon([(8, 6), (13, 20), (10, 20)], "a", 2)
    c.polygon([(9, 20), (15, 3), (22, 20)], "a", 4)
    c.polygon([(15, 3), (22, 20), (17, 20)], "a", 2)
    c.line(15, 8, 14, 14, "b", 2)
    c.outline()
    return c


def party_token(f: int = 0) -> Canvas:
    """The party's pin: a pole with a pennant that waves, and a ground ring."""
    c = _icon()
    c.ellipse(12, 21, 6, 2, "b", 3)
    c.ellipse(12, 21, 4, 1.2, "paper", 5)
    c.line(11, 21, 11, 3, "ink", 1)
    wave = [0, 1, 0, -1][f % 4]
    for y in range(3, 10):
        for x in range(12, 20):
            if x - 12 < 8 - abs(y - 6) * 1.2:
                c.px(x, y + (wave if x > 15 else 0), "b", 4 if y < 6 else 3)
    c.px(11, 2, "a", 5)
    c.outline()
    return c


def quest(f: int = 0) -> Canvas:
    c = _icon()
    lift = [0, -1, -2, -1][f % 4]
    c.ellipse(12, 21, 3 + (f % 2), 1, "ink", 2)
    c.rect(11, 5 + lift, 13, 14 + lift, "b", 4)
    c.rect(11, 16 + lift, 13, 18 + lift, "b", 4)
    c.px(11, 5 + lift, "b", 5)
    c.outline()
    return c


ICONS = {
    "town": (lambda: town(True), {"a": "roof", "b": "gold", "c": "stone"}),
    "village": (lambda: town(False), {"a": "roof", "b": "gold", "c": "stone"}),
    "castle": (castle, {"a": "blue", "b": "red", "c": "stone"}),
    "dungeon": (dungeon, {"a": "stone", "b": "red", "c": "stone"}),
    "ruins": (ruins, {"a": "stone", "b": "gold", "c": "stone"}),
    "tower": (tower, {"a": "blue", "b": "gold", "c": "stone"}),
    "camp": (camp, {"a": "roof", "b": "gold", "c": "stone"}),
    "port": (port, {"a": "roof", "b": "gold", "c": "stone"}),
    "shrine": (shrine, {"a": "stone", "b": "gold", "c": "stone"}),
    "lair": (lair, {"a": "stone", "b": "red", "c": "stone"}),
    "forest": (forest_icon, {"a": "forest", "b": "gold", "c": "stone"}),
    "mountain": (mountain_icon, {"a": "stone", "b": "gold", "c": "stone"}),
    "swamp": (swamp_icon, {"a": "swamp", "b": "forest", "c": "roof"}),
    "oasis": (oasis_icon, {"a": "forest", "b": "sea", "c": "desert"}),
    "volcano": (volcano_icon, {"a": "red", "b": "gold", "c": "stone"}),
    "glacier": (glacier_icon, {"a": "snow", "b": "blue", "c": "stone"}),
}


def _pal(slots: dict[str, str]) -> dict[str, list[str]]:
    return {"ink": INK, "paper": PAPER, **{k: WASH[v] for k, v in slots.items()}}


def render_icon(name: str) -> Image.Image:
    make, slots = ICONS[name]
    return make().render(_pal(slots), outline_mix=0.85)


# --------------------------------------------------------------------------------------------------- sample map
def sample_map(w: int = 384, h: int = 216, seed: int = 21) -> Image.Image:
    """A whole campaign map from the pieces: coast and sea wash, regions, hatched mountains, stamped forests,
    dotted roads between sites, a fog of war over the unexplored east, and the party's pin."""
    r = tex.rng(seed)
    out = Image.new("RGBA", (w, h))
    paper = parchment()
    for y in range(0, h, paper.height):
        for x in range(0, w, paper.width):
            out.alpha_composite(paper, (x, y))
    arr = np.asarray(out).astype(np.float32)
    ys, xs = np.mgrid[0:h, 0:w]
    land = tex.fbm(w, h, 4, r, octaves=4)
    edge = np.minimum(np.minimum(xs, w - 1 - xs) / 40, np.minimum(ys, h - 1 - ys) / 30)
    land = land * 0.75 + np.clip(edge, 0, 1) * 0.35 - 0.1
    sea = land < 0.42
    temp = (1 - ys / h) * 0.6 + tex.fbm(w, h, 2, r, octaves=2) * 0.4      # north is cold
    wet = tex.fbm(w, h, 4, r, octaves=3)

    def wash(mask, colour, strength=0.45):
        c = np.array([int(colour[3][i:i + 2], 16) for i in (1, 3, 5)], np.float32)
        arr[mask, :3] = arr[mask, :3] * (1 - strength) + c * strength

    wash(sea, WASH["sea"], 0.55)
    coast = sea & ~np.roll(sea, 1, 0) | sea & ~np.roll(sea, 1, 1) | sea & ~np.roll(sea, -1, 0)
    for k in (2, 4):                                                    # ripple lines echoing the coast
        ring = sea & (np.roll(coast, k, 0) | np.roll(coast, -k, 1)) & ((xs + ys) % 3 == 0)
        wash(ring, WASH["sea"], 0.9)
    snow = ~sea & (temp > 0.62)
    desert = ~sea & (temp < 0.32) & (wet < 0.5)
    swamp = ~sea & (wet > 0.72) & ~snow
    forest = ~sea & (wet > 0.5) & ~swamp & ~snow
    wash(snow, WASH["snow"], 0.6)
    wash(desert, WASH["desert"], 0.45)
    wash(swamp, WASH["swamp"], 0.4)
    wash(forest, WASH["forest"], 0.3)
    ink = np.array([int(INK[1][i:i + 2], 16) for i in (1, 3, 5)], np.float32)
    arr[coast & ~np.roll(coast, 1, 1), :3] = ink                       # coastline in ink
    out = Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGBA")
    # stamp mountains along the high ground and trees in the forests
    hi = tex.fbm(w, h, 8, r, octaves=2)
    mt = render_icon("mountain").resize((14, 14), Image.NEAREST)
    tr = render_icon("forest").resize((12, 12), Image.NEAREST)
    for pts, stamp, mask in ((tex.poisson(w, h, 15, r, tries=900), mt, (~sea) & (hi > 0.62)),
                             (tex.poisson(w, h, 11, r, tries=1400), tr, forest)):
        for x, y in sorted(pts, key=lambda p: p[1]):
            if mask[y, x] and 8 < x < w - 8 and 8 < y < h - 8:
                out.alpha_composite(stamp, (x - stamp.width // 2, y - stamp.height + 2))
    # a landmark in each biome region: the point deepest inside it (no randomness, so the sites don't move)
    for name, mask in (("swamp", swamp), ("oasis", desert), ("glacier", snow)):
        inner = mask.copy()
        for _ in range(6):
            shrunk = inner & np.roll(inner, 2, 0) & np.roll(inner, -2, 0) & np.roll(inner, 2, 1) & np.roll(inner, -2, 1)
            if not shrunk[16:h - 16, 16:w - 16].any():
                break
            inner = shrunk
        cand = np.argwhere(inner[16:h - 16, 16:w - 16])
        if len(cand):
            y, x = cand[len(cand) // 2] + 16
            ic = render_icon(name)
            out.alpha_composite(ic, (int(x) - 12, int(y) - 21))
    # sites and the roads between them
    land_pts = [(x, y) for x, y in tex.poisson(w, h, 48, r, tries=2000) if not sea[y, x] and 20 < x < w - 20
                and 20 < y < h - 20]
    kinds = ["town", "castle", "village", "dungeon", "ruins", "tower", "shrine", "camp", "lair", "port", "village"]
    sites = list(zip(land_pts, kinds))
    from PIL import ImageDraw
    d = ImageDraw.Draw(out)
    order = sorted(sites, key=lambda s: s[0][0])
    for (a, _), (b, _) in zip(order, order[1:]):                        # a road west to east
        steps = int(math.dist(a, b) / 4)
        for k in range(steps):
            if k % 2 == 0:
                t = k / steps
                x = a[0] + (b[0] - a[0]) * t + math.sin(t * 6) * 3
                y = a[1] + (b[1] - a[1]) * t
                d.rectangle((x, y, x + 1, y), fill=INK[1])
    for (x, y), kind in sites:
        icon = render_icon(kind)
        out.alpha_composite(icon, (x - 12, y - 20))
    # fog of war over the far east
    fog = tex.fbm(w, h, 4, r, octaves=3)
    farr = np.asarray(out).astype(np.float32)
    fogmask = (xs > w * 0.72 + fog * 40)
    dither = fogmask & (((xs + ys) % 2 == 0) | (xs > w * 0.8 + fog * 40))
    farr[dither, :3] = farr[dither, :3] * 0.35 + np.array([214, 206, 188], np.float32) * 0.65
    out = Image.fromarray(farr.clip(0, 255).astype(np.uint8), "RGBA")
    token = party_token().render(_pal({"a": "gold", "b": "red", "c": "stone"}), outline_mix=0.85)
    x, y = order[1][0]
    out.alpha_composite(token, (x - 4, y - 30))
    return out


def build() -> None:
    out = PACK / "worldmap"
    shutil.rmtree(out, ignore_errors=True)
    save(parchment(), out / "parchment.png")
    tiles = []
    for name in ICONS:
        img = render_icon(name)
        save(img, out / "icons" / f"{name}.png")
        tiles.append(Tile(img, name, "location"))
    for name, fn, slots in (("party", party_token, {"a": "gold", "b": "red", "c": "stone"}),
                            ("quest", quest, {"a": "gold", "b": "gold", "c": "stone"})):
        frames = [fn(f).render(_pal(slots), outline_mix=0.85) for f in range(4)]
        sheet = Image.new("RGBA", (ICON * 4, ICON))
        for k, im in enumerate(frames):
            sheet.paste(im, (k * ICON, 0))
        save(sheet, out / "icons" / f"{name}.png")
        tiles.append(Tile(frames[0], name, "4 frames"))
    mp = sample_map()
    save(mp, out / "sample_map.png")
    write_json({"parchment": {"file": "parchment.png", "tile": True},
                "icon": {"size": [ICON, ICON], "anchor": [12, 21],
                         "files": {n: f"icons/{n}.png" for n in list(ICONS) + ["party", "quest"]},
                         "animated": {"party": {"frames": 4, "fps": 6}, "quest": {"frames": 4, "fps": 6}}},
                "sample": "sample_map.png",
                "notes": "Compose a campaign map from parchment + region washes + icons; the sample shows the style"},
               out / "manifest.json")
    on_paper = []
    paper = parchment()
    for t in tiles:
        bg = paper.crop((0, 0, ICON + 8, ICON + 8)).copy()
        bg.alpha_composite(t.image, (4, 4))
        on_paper.append(Tile(bg, t.label, t.sub))
    from arcpix.preview import stack
    save(stack("World map", [contact_sheet("Locations on parchment", on_paper, columns=9, zoom=4),
                             contact_sheet("A sample campaign map", [Tile(mp, "", "")], columns=1, zoom=3)],
               "The campaign's map: where the party has been (dotted roads), where they are (the pin) and the fog "
               "over what they haven't explored yet."), PREVIEWS / "27-worldmap.png")
