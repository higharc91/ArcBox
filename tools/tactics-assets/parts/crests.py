"""Crests: a heraldic shield per class and a round medallion per subclass. The phone creator shows them on the class
and subclass pickers, character cards carry them, and the TV can flash a subclass medallion when a signature
ability goes off.

Symbols are drawn with primitives on a 13x13 grid, centred, so the same drawing sits in a 32 px class shield or a
22 px subclass medallion. The field takes the class kit's own colour; the symbol is gold or ivory.
"""
from __future__ import annotations

import math
import shutil
from typing import Callable

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES
from arcpix.preview import Tile, contact_sheet, stack
from . import creator

SLOTS = SlotTable()
for _s in ("field", "rim", "sym", "accent"):
    SLOTS.id(_s)
Sym = Callable[[Canvas, int, int], None]      # draws a symbol centred on (cx, cy) within ±6 px


def _ln(c, x0, y0, x1, y1, ox, oy, slot="sym", shade=4):
    c.line(ox + x0, oy + y0, ox + x1, oy + y1, slot, shade)


# ---------------------------------------------------------------------------------------------------- symbols
def swords(c, x, y):
    for s in (-1, 1):
        _ln(c, -5 * s, 5, 5 * s, -5, x, y, shade=5)
        _ln(c, -5 * s + s, 3, -3 * s + s, 5, x, y, "accent", 3)       # crossguards
def dagger(c, x, y):
    c.rect(x, y - 6, x, y + 2, "sym", 5); c.rect(x - 2, y + 2, x + 2, y + 2, "accent", 4)
    c.rect(x, y + 3, x, y + 5, "accent", 2); c.px(x, y - 6, "sym", 5)
def star(c, x, y):
    for a in range(8):
        r = 6 if a % 2 == 0 else 3
        ang = a * math.pi / 4
        c.line(x, y, x + math.cos(ang) * r, y + math.sin(ang) * r, "sym", 5 if r == 6 else 4)
def sun(c, x, y, rays=6):
    c.ellipse(x + 0.5, y + 0.5, 3, 3, "sym", 4); c.px(x - 1, y - 1, "sym", 5)
    for a in range(8):
        ang = a * math.pi / 4
        c.line(x + math.cos(ang) * 4.5, y + math.sin(ang) * 4.5, x + math.cos(ang) * rays,
               y + math.sin(ang) * rays, "sym", 4)
def bow(c, x, y):
    for k in range(-6, 7):
        c.px(x - 3 + abs(k) * abs(k) / 14, y + k, "accent", 3)
    _ln(c, -2, -6, -2, 6, x, y, "sym", 3); _ln(c, -5, 0, 6, 0, x, y, "sym", 5); _ln(c, 4, -2, 6, 0, x, y, "sym", 5)
    _ln(c, 4, 2, 6, 0, x, y, "sym", 5)
def cross(c, x, y):
    c.rect(x - 1, y - 6, x + 1, y + 6, "sym", 4); c.rect(x - 5, y - 2, x + 5, y, "sym", 4)
    c.rect(x - 1, y - 6, x - 1, y + 6, "sym", 5)
def lute(c, x, y):
    c.ellipse(x - 1.5, y + 2.5, 3.5, 3, "accent", 3); c.px(x - 2, y + 2, "sym", 1)
    _ln(c, 0, 0, 5, -5, x, y, "accent", 2); c.rect(x + 4, y - 6, x + 6, y - 5, "sym", 4)
def leaf(c, x, y):
    c.polygon([(x, y - 6), (x + 4, y - 2), (x + 3, y + 3), (x, y + 6), (x - 3, y + 3), (x - 4, y - 2)], "sym", 4)
    _ln(c, 0, -5, 0, 6, x, y, "accent", 2)
def eye(c, x, y):
    c.polygon([(x - 6, y), (x, y - 4), (x + 6, y), (x, y + 4)], "sym", 4)
    c.ellipse(x + 0.5, y + 0.5, 2, 2, "accent", 2); c.px(x, y, "accent", 0)
def fist(c, x, y):
    c.rect(x - 4, y - 3, x + 4, y + 4, "sym", 4)
    for k in (-2, 0, 2):
        c.rect(x + k, y - 3, x + k, y, "sym", 2)
    c.rect(x - 4, y + 1, x - 2, y + 3, "sym", 5); c.rect(x - 3, y + 5, x + 3, y + 6, "accent", 3)
def tower_shield(c, x, y):
    c.rect(x - 4, y - 6, x + 4, y + 5, "sym", 4); c.rect(x - 1, y - 5, x + 1, y + 4, "accent", 3)
    c.rect(x - 3, y - 2, x + 3, y - 1, "accent", 3)
def halberd(c, x, y):
    _ln(c, -4, 6, 4, -6, x, y, "accent", 3)
    c.polygon([(x + 1, y - 5), (x + 6, y - 6), (x + 5, y - 1)], "sym", 4); c.px(x + 4, y - 6, "sym", 5)
def banner(c, x, y):
    _ln(c, -4, -6, -4, 6, x, y, "accent", 3)
    c.polygon([(x - 3, y - 6), (x + 5, y - 5), (x + 3, y - 2), (x + 5, y + 1), (x - 3, y + 1)], "sym", 4)
def mask(c, x, y):
    c.polygon([(x - 6, y - 2), (x + 6, y - 2), (x + 5, y + 2), (x, y + 4), (x - 5, y + 2)], "sym", 4)
    c.rect(x - 4, y - 1, x - 2, y - 1, "accent", 0); c.rect(x + 2, y - 1, x + 4, y - 1, "accent", 0)
def bomb(c, x, y):
    c.ellipse(x - 0.5, y + 1.5, 4, 4, "accent", 1); c.px(x - 2, y, "sym", 5)
    _ln(c, 2, -2, 4, -5, x, y, "sym", 3); c.px(x + 5, y - 6, "sym", 5)
def rapier(c, x, y):
    _ln(c, -4, 5, 5, -6, x, y, "sym", 5)
    for dx, dy in ((-3, 2), (-2, 4), (-4, 4), (-5, 3)):
        c.px(x + dx, y + dy, "accent", 4)
def flame(c, x, y):
    c.polygon([(x, y - 6), (x + 4, y + 1), (x + 3, y + 5), (x - 3, y + 5), (x - 4, y + 1)], "sym", 4)
    c.polygon([(x, y - 1), (x + 2, y + 3), (x - 2, y + 3)], "accent", 5)
def hourglass(c, x, y):
    c.rect(x - 4, y - 6, x + 4, y - 6, "accent", 4); c.rect(x - 4, y + 6, x + 4, y + 6, "accent", 4)
    c.polygon([(x - 3, y - 5), (x + 3, y - 5), (x, y)], "sym", 4); c.polygon([(x, y), (x + 3, y + 5), (x - 3, y + 5)], "sym", 3)
def mountain(c, x, y):
    c.polygon([(x - 6, y + 5), (x - 1, y - 5), (x + 6, y + 5)], "accent", 3)
    c.polygon([(x - 3, y - 1), (x - 1, y - 5), (x + 1, y - 1)], "sym", 5)
def heart(c, x, y):
    c.ellipse(x - 2, y - 2, 2.6, 2.4, "sym", 4); c.ellipse(x + 3, y - 2, 2.6, 2.4, "sym", 4)
    c.polygon([(x - 5, y - 1), (x + 6, y - 1), (x + 0.5, y + 5)], "sym", 4); c.px(x - 3, y - 3, "sym", 5)
def hammer(c, x, y):
    _ln(c, -3, 6, 2, -2, x, y, "accent", 3); c.rect(x - 1, y - 6, x + 6, y - 3, "sym", 4)
    c.rect(x - 1, y - 6, x + 6, y - 6, "sym", 5)
def rays(c, x, y):
    sun(c, x, y, rays=7)
def target(c, x, y):
    for r, sh in ((6, 4), (4, 2), (2, 4)):
        for a in range(32):
            ang = a / 32 * 2 * math.pi
            c.px(x + math.cos(ang) * r, y + math.sin(ang) * r, "sym" if sh == 4 else "accent", sh)
    c.px(x, y, "accent", 5)
def paw(c, x, y):
    c.ellipse(x + 0.5, y + 2.5, 3, 2.5, "sym", 4)
    for dx, dy in ((-4, -2), (-1, -5), (2, -5), (5, -2)):
        c.ellipse(x + dx + 0.5, y + dy + 0.5, 1.2, 1.4, "sym", 4)
def vine(c, x, y):
    for k in range(-6, 7):
        c.px(x + math.sin(k / 2) * 3, y + k, "accent", 3)
    for k in (-4, 0, 4):
        c.polygon([(x + math.sin(k / 2) * 3, y + k), (x + math.sin(k / 2) * 3 + 3, y + k - 2),
                   (x + math.sin(k / 2) * 3 + 2, y + k + 1)], "sym", 4)
def heater(c, x, y):
    c.polygon([(x - 5, y - 6), (x + 5, y - 6), (x + 5, y), (x, y + 6), (x - 5, y)], "sym", 4)
    c.rect(x - 4, y - 5, x - 4, y, "sym", 5); c.rect(x, y - 5, x, y + 3, "accent", 3)
def holy_sword(c, x, y):
    c.rect(x, y - 6, x, y + 3, "sym", 5); c.rect(x - 3, y + 1, x + 3, y + 1, "accent", 4)
    c.rect(x, y + 2, x, y + 5, "accent", 3)
    for dx, dy in ((-3, -4), (3, -4), (-2, -6), (2, -6)):
        c.px(x + dx, y + dy, "sym", 5)
def brand(c, x, y):
    eye(c, x, y + 2)
    c.polygon([(x - 2, y - 2), (x, y - 6), (x + 2, y - 2)], "accent", 5)
def horn(c, x, y):
    c.polygon([(x - 6, y + 3), (x - 3, y - 1), (x + 3, y - 4), (x + 6, y - 6), (x + 5, y - 1), (x - 3, y + 5)],
              "sym", 4)
    c.rect(x - 1, y - 3, x, y + 2, "accent", 3)
def book(c, x, y):
    c.rect(x - 5, y - 5, x + 5, y + 5, "sym", 4); c.rect(x - 5, y - 5, x - 4, y + 5, "accent", 3)
    c.rect(x - 2, y - 2, x + 3, y - 2, "accent", 2); c.rect(x - 2, y + 1, x + 3, y + 1, "accent", 2)
def note(c, x, y):
    c.rect(x + 1, y - 6, x + 1, y + 3, "sym", 4); c.rect(x + 1, y - 6, x + 5, y - 5, "sym", 4)
    c.ellipse(x - 1, y + 4, 2.5, 2, "sym", 4)
def antlers(c, x, y):
    for s in (-1, 1):
        _ln(c, 0, 5, 3 * s, -1, x, y, "sym", 4); _ln(c, 3 * s, -1, 5 * s, -6, x, y, "sym", 4)
        _ln(c, 3 * s, -1, 6 * s, -2, x, y, "sym", 4); _ln(c, 2 * s, 2, 5 * s, 2, x, y, "sym", 3)
def bolt(c, x, y):
    c.polygon([(x + 1, y - 6), (x - 3, y + 1), (x, y + 1), (x - 2, y + 6), (x + 4, y - 2), (x + 1, y - 2)],
              "sym", 5)
def flower(c, x, y):
    for a in range(5):
        ang = a * 2 * math.pi / 5 - math.pi / 2
        c.ellipse(x + math.cos(ang) * 3 + 0.5, y + math.sin(ang) * 3 + 0.5, 1.8, 1.8, "sym", 4)
    c.ellipse(x + 0.5, y + 0.5, 1.4, 1.4, "accent", 5)
def cursed(c, x, y):
    c.rect(x, y - 6, x, y + 3, "accent", 2); c.rect(x - 2, y + 2, x + 2, y + 2, "sym", 3)
    for k in range(6):
        c.px(x + 2 + math.sin(k) * 2, y - 5 + k * 2, "sym", 5)
def imp(c, x, y):
    c.ellipse(x + 0.5, y + 1.5, 4, 4, "sym", 4)
    c.polygon([(x - 4, y - 1), (x - 6, y - 6), (x - 2, y - 2)], "accent", 4)
    c.polygon([(x + 5, y - 1), (x + 7, y - 6), (x + 3, y - 2)], "accent", 4)
    c.px(x - 1, y + 1, "accent", 0); c.px(x + 2, y + 1, "accent", 0)
def spiral(c, x, y):
    for k in range(40):
        ang = k * 0.45
        r = k * 0.15
        c.px(x + math.cos(ang) * r, y + math.sin(ang) * r, "sym", 5 if k > 30 else 4)
def palm(c, x, y):
    c.rect(x - 3, y - 1, x + 3, y + 5, "sym", 4)
    for k, h in zip((-3, -1, 1, 3), (4, 6, 6, 4)):
        c.rect(x + k, y - h, x + k, y - 1, "sym", 4)
    _ln(c, -3, 2, -6, -1, x, y, "sym", 4)
def wind(c, x, y):
    for dy, ln in ((-4, 9), (0, 11), (4, 7)):
        _ln(c, -6, dy, -6 + ln, dy, x, y, "sym", 4)
        c.px(x - 6 + ln + 1, y + dy - 1, "sym", 5)
def gauntlet(c, x, y):
    fist(c, x, y)
    c.rect(x - 4, y - 3, x + 4, y - 3, "accent", 5)
def labrys(c, x, y):
    """A double-bitted war axe: the barbarian."""
    c.rect(x, y - 6, x, y + 6, "accent", 3)
    for s in (-1, 1):
        c.polygon([(x + s * 1, y - 5), (x + s * 5, y - 7), (x + s * 6.5, y - 3), (x + s * 5, y + 1),
                   (x + s * 1, y - 1)], "sym", 4)
        c.line(x + s * 5, y - 6, x + s * 6, y - 3, "sym", 5)
    c.px(x, y - 7, "accent", 4)
def cupped_flame(c, x, y):
    """Magic held in a bare hand, no focus: the sorcerer."""
    c.polygon([(x - 5.5, y + 1), (x + 5.5, y + 1), (x + 3.5, y + 6), (x - 3.5, y + 6)], "accent", 3)
    for k in (-3, 0, 3):
        c.px(x + k, y + 1, "accent", 5)
    c.polygon([(x, y - 7), (x + 3.5, y - 2), (x + 2.5, y + 1), (x - 2.5, y + 1), (x - 3.5, y - 2)], "sym", 4)
    c.polygon([(x, y - 3), (x + 1.5, y), (x - 1.5, y)], "sym", 5)
def wolf(c, x, y):
    c.polygon([(x - 5, y - 7), (x - 2, y - 3), (x + 2, y - 3), (x + 5, y - 7), (x + 5.5, y), (x + 1, y + 6),
               (x - 1, y + 6), (x - 5.5, y)], "sym", 4)
    c.px(x - 2, y, "accent", 0); c.px(x + 2, y, "accent", 0)
    c.rect(x - 1, y + 4, x + 1, y + 5, "accent", 0)
    c.px(x - 4, y - 5, "sym", 5)
def cracked_earth(c, x, y):
    c.rect(x - 6, y + 1, x + 6, y + 5, "sym", 4)
    c.rect(x - 6, y + 1, x + 6, y + 1, "sym", 5)
    for dx, dy in ((0, 1), (-1, 2), (-1, 3), (0, 4), (1, 5)):
        c.px(x + dx, y + dy, "accent", 0)
    c.rect(x - 5, y - 3, x - 4, y - 2, "sym", 5); c.rect(x + 3, y - 5, x + 4, y - 4, "sym", 5)
    c.px(x, y - 6, "sym", 4)
    _ln(c, -2, -1, -2, -2, x, y, "accent", 3); _ln(c, 2, -1, 2, -3, x, y, "accent", 3)
def ghost(c, x, y):
    c.ellipse(x + 0.5, y - 1.5, 4.2, 4.2, "sym", 4)
    c.rect(x - 4, y - 1, x + 4, y + 4, "sym", 4)
    for k in (-3, 0, 3):
        c.rect(x + k, y + 5, x + k + 1, y + 6, "sym", 3)
    c.rect(x - 2, y - 2, x - 1, y - 1, "accent", 0); c.rect(x + 2, y - 2, x + 3, y - 1, "accent", 0)
    c.px(x - 3, y - 4, "sym", 5)
def wing(c, x, y):
    c.polygon([(x - 6, y + 5), (x - 4, y - 6), (x + 6, y - 5), (x + 4, y - 2), (x + 6, y + 1), (x + 2, y + 1),
               (x + 3, y + 4), (x - 1, y + 3)], "sym", 4)
    for tx, ty in ((6, -5), (6, 1), (3, 4)):                              # the spars
        _ln(c, -4, -5, tx - 1, ty, x, y, "accent", 3)
    _ln(c, -4, -6, -6, 5, x, y, "sym", 5)
def chaos_star(c, x, y):
    for a in range(8):
        ang = a * math.pi / 4
        ex, ey = x + math.cos(ang) * 6, y + math.sin(ang) * 6
        c.line(x + math.cos(ang) * 2, y + math.sin(ang) * 2, ex, ey, "sym", 5 if a % 2 else 4)
        for da in (-0.6, 0.6):
            c.px(ex - math.cos(ang + da) * 1.6, ey - math.sin(ang + da) * 1.6, "sym", 4)
    c.ellipse(x + 0.5, y + 0.5, 1.6, 1.6, "accent", 3)
def rune(c, x, y):
    c.rect(x, y - 6, x, y + 6, "sym", 5)
    _ln(c, 0, -1, -4, -5, x, y, "sym", 4); _ln(c, 0, -1, 4, -5, x, y, "sym", 4)
    _ln(c, 0, 3, -3, 6, x, y, "sym", 4); _ln(c, 0, 3, 3, 6, x, y, "sym", 4)
    for dx, dy in ((-4, 1), (4, 1)):
        c.px(x + dx, y + dy, "accent", 4)


CLASS_SYMBOL: dict[str, Sym] = {"fighter": swords, "rogue": dagger, "wizard": star, "cleric": sun, "ranger": bow,
                                "paladin": cross, "bard": lute, "druid": leaf, "warlock": eye, "monk": fist,
                                "barbarian": labrys, "sorcerer": cupped_flame}
SUB_SYMBOL: dict[str, Sym] = {
    "vanguard": tower_shield, "weaponmaster": halberd, "warlord": banner, "shadow": mask, "trickster": bomb,
    "duelist": rapier, "evoker": flame, "chronomancer": hourglass, "geomancer": mountain, "life": heart,
    "war": hammer, "light": rays, "sharpshooter": target, "beastmaster": paw, "warden": vine, "guardian": heater,
    "crusader": holy_sword, "avenger": brand, "valor": horn, "lore": book, "siren": note, "shapeshifter": antlers,
    "stormcaller": bolt, "grovekeeper": flower, "hexblade": cursed, "fiendbound": imp, "void": spiral,
    "open_hand": palm, "windstep": wind, "iron_body": gauntlet, "berserker": wolf, "earthshaker": cracked_earth,
    "spiritwarden": ghost, "draconic": wing, "chaos": chaos_star, "runeborn": rune,
}


def class_crest(cls: str) -> Canvas:
    """A heater shield: the class colour as the field, a gold rim, the class symbol."""
    c = Canvas(32, 32, SLOTS)
    pts = [(4, 3), (27, 3), (27, 15), (15.5, 29), (4, 15)]
    c.polygon(pts, "rim", 4)
    c.polygon([(6, 5), (25, 5), (25, 15), (15.5, 26.5), (6, 15)], "field", 3)
    c.polygon([(6, 5), (15, 5), (15, 26), (6, 15)], "field", 4)           # the lit half of the field
    c.rect(4, 3, 27, 3, "rim", 5)
    CLASS_SYMBOL[cls](c, 15, 14)
    c.outline()
    return c


def subclass_badge(sub: str) -> Canvas:
    c = Canvas(22, 22, SLOTS)
    c.ellipse(11, 11, 10, 10, "rim", 4)
    c.ellipse(11, 11, 8.3, 8.3, "field", 3)
    for k in range(20):                                                    # beaded rim highlight, top-left
        a = k / 20 * 2 * math.pi
        if math.cos(a) + math.sin(a) < -0.4:
            c.px(11 + math.cos(a) * 9.4, 11 + math.sin(a) * 9.4, "rim", 5)
    SUB_SYMBOL[sub](c, 11, 11)
    c.outline()
    return c


# Fields that are too pale (or too gold) for a gold symbol get a deeper field or an ivory symbol instead.
FIELD_OVERRIDE = {"cleric": "cloth/wine", "paladin": "cloth/sky", "monk": "cloth/rust", "barbarian": "cloth/crimson"}
IVORY_SYMBOL = {"monk", "cleric", "paladin"}


def _pal(cls: str) -> dict[str, list[str]]:
    ref = FIELD_OVERRIDE.get(cls, creator.CLASSES[cls].colours["cloth1"])
    fam, name = ref.split("/")
    field = FAMILIES[fam][name]
    sym = FAMILIES["cloth"]["ivory"] if cls in IVORY_SYMBOL else FAMILIES["metal"]["gold"]
    return {"field": field, "rim": FAMILIES["metal"]["gold"], "sym": sym, "accent": FAMILIES["metal"]["silver"]}


def build() -> None:
    out = PACK / "crests"
    shutil.rmtree(out, ignore_errors=True)
    ctiles, stiles = [], []
    classes = list(creator.CLASSES)
    sheet = Image.new("RGBA", (32 * len(classes), 32))
    for k, cls in enumerate(classes):
        img = class_crest(cls).render(_pal(cls))
        sheet.paste(img, (k * 32, 0))
        ctiles.append(Tile(img, creator.CLASSES[cls].name))
    save(sheet, out / "classes.png")
    subs = list(creator.SUBCLASSES.values())
    bsheet = Image.new("RGBA", (22 * 3, 22 * len(classes)))
    index = {}
    for s in subs:
        col = [x.id for x in subs if x.cls == s.cls].index(s.id)
        row = classes.index(s.cls)
        img = subclass_badge(s.id).render(_pal(s.cls))
        bsheet.paste(img, (col * 22, row * 22))
        index[s.id] = [col * 22, row * 22, 22, 22]
        stiles.append(Tile(img, s.name, creator.CLASSES[s.cls].name))
    save(bsheet, out / "subclasses.png")
    write_json({"classes": {"file": "classes.png", "size": [32, 32], "order": classes},
                "subclasses": {"file": "subclasses.png", "size": [22, 22], "rects": index}}, out / "manifest.json")
    save(stack("Crests", [contact_sheet("Classes", ctiles, columns=12, zoom=4),
                          contact_sheet("Subclasses", stiles, columns=6, zoom=4)],
               "Heraldry for the creator, character cards and signature-ability callouts."),
         PREVIEWS / "31-crests.png")
