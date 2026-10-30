"""Icons for the phone's character sheet: the six ability scores, the eighteen skills and the damage types, in the
same symbol vocabulary as the crests (13 px symbols on a small rounded plate), so the sheet, the creator and the
TV callouts all speak one visual language.
"""
from __future__ import annotations

import math
import shutil

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES
from arcpix.preview import Tile, contact_sheet, stack
from . import crests as K

SIZE = 18
SLOTS = K.SLOTS


def _ln(c, x0, y0, x1, y1, x, y, slot="sym", shade=4):
    c.line(x + x0, y + y0, x + x1, y + y1, slot, shade)


# a few symbols the crests don't have
def boot(c, x, y):
    """A winged boot: speed and agility."""
    c.polygon([(x - 3, y - 6), (x + 1, y - 6), (x + 1, y + 1), (x + 5, y + 2), (x + 5, y + 5), (x - 3, y + 5)],
              "sym", 4)
    c.rect(x - 3, y - 6, x - 3, y + 5, "sym", 5)
    c.rect(x - 3, y + 5, x + 5, y + 5, "accent", 3)                     # sole
    for k in range(3):                                                    # the wing at the heel
        _ln(c, -4, -4 + k * 2, -7 + k, -6 + k * 2, x, y, "sym", 5)


def brain(c, x, y):
    c.ellipse(x + 0.5, y + 0.5, 5.5, 4.5, "sym", 4)
    _ln(c, 0, -4, 0, 4, x, y, "accent", 2)
    for k in (-3, 2):
        _ln(c, k, -2, k + 1, 1, x, y, "accent", 2)
def owl_eye(c, x, y):
    for s in (-1, 1):
        c.ellipse(x + s * 3 + 0.5, y + 0.5, 2.6, 2.6, "sym", 4)
        c.px(x + s * 3, y, "accent", 0)
    c.polygon([(x - 0.5, y + 2), (x + 1.5, y + 2), (x + 0.5, y + 4)], "accent", 4)
def crown_sym(c, x, y):
    c.polygon([(x - 5, y + 4), (x - 5, y - 3), (x - 2, y), (x, y - 5), (x + 2, y), (x + 5, y - 3), (x + 5, y + 4)],
              "sym", 4)
    c.rect(x - 5, y + 3, x + 5, y + 4, "accent", 4)
def heart_beat(c, x, y):
    K.heart(c, x, y)
    _ln(c, -5, 0, -2, 0, x, y, "accent", 5); _ln(c, -2, 0, 0, -3, x, y, "accent", 5); _ln(c, 0, -3, 2, 2, x, y, "accent", 5)
def footprints(c, x, y):
    """Stealth: a hood with only the eyes showing."""
    c.polygon([(x, y - 6), (x + 5, y - 1), (x + 5, y + 5), (x - 5, y + 5), (x - 5, y - 1)], "sym", 4)
    c.ellipse(x + 0.5, y + 1, 3, 2.6, "accent", 0)
    c.px(x - 1, y + 1, "sym", 5)
    c.px(x + 2, y + 1, "sym", 5)


def hand_open(c, x, y):
    K.palm(c, x, y)
def scroll_sym(c, x, y):
    c.rect(x - 4, y - 4, x + 4, y + 4, "sym", 4)
    c.rect(x - 5, y - 5, x + 5, y - 4, "accent", 3); c.rect(x - 5, y + 4, x + 5, y + 5, "accent", 3)
    for k in (-2, 0, 2):
        _ln(c, -3, k, 3, k, x, y, "accent", 2)
def magnifier(c, x, y):
    for a in range(16):
        ang = a / 16 * 2 * math.pi
        c.px(x - 1 + math.cos(ang) * 3.5, y - 1 + math.sin(ang) * 3.5, "sym", 4)
    _ln(c, 2, 2, 5, 5, x, y, "accent", 3)
def paw_sym(c, x, y):
    K.paw(c, x, y)
def mask_sym(c, x, y):
    K.mask(c, x, y)
def mask_happy(c, x, y):
    c.ellipse(x + 0.5, y + 0.5, 5, 5.5, "sym", 4)
    c.px(x - 2, y - 1, "accent", 0); c.px(x + 2, y - 1, "accent", 0)
    _ln(c, -2, 2, 0, 3, x, y, "accent", 0); _ln(c, 0, 3, 2, 2, x, y, "accent", 0)
def speech(c, x, y):
    c.ellipse(x + 0.5, y - 0.5, 5.5, 4, "sym", 4)
    c.polygon([(x - 3, y + 2), (x - 1, y + 2), (x - 4, y + 5)], "sym", 4)
def pouch(c, x, y):
    c.ellipse(x + 0.5, y + 1.5, 4.5, 4, "sym", 4); c.rect(x - 2, y - 4, x + 2, y - 2, "sym", 3)
    c.rect(x - 2, y - 2, x + 2, y - 2, "accent", 2)
def eye_open(c, x, y):
    K.eye(c, x, y)
def herb(c, x, y):
    K.leaf(c, x, y)
def fire_sym(c, x, y):
    K.flame(c, x, y)
def snowflake(c, x, y):
    for a in range(3):
        ang = a * math.pi / 3
        c.line(x - math.cos(ang) * 5, y - math.sin(ang) * 5, x + math.cos(ang) * 5, y + math.sin(ang) * 5, "sym", 4)
    c.px(x, y, "sym", 5)
def drop(c, x, y):
    c.polygon([(x, y - 6), (x + 4, y + 1), (x + 3, y + 4), (x - 3, y + 4), (x - 4, y + 1)], "sym", 4)
    c.px(x - 2, y, "sym", 5)
def skull(c, x, y):
    c.ellipse(x + 0.5, y - 0.5, 4.5, 4.5, "sym", 4); c.rect(x - 2, y + 3, x + 2, y + 5, "sym", 4)
    c.px(x - 2, y - 1, "accent", 0); c.px(x + 2, y - 1, "accent", 0)
def wave(c, x, y):
    for k in range(-5, 6):
        c.px(x + k, y + math.sin(k * 0.9) * 2, "sym", 4)
        c.px(x + k, y + 3 + math.sin(k * 0.9 + 1) * 2, "sym", 3)
def mind(c, x, y):
    K.spiral(c, x, y)


ABILITIES = [("str", "Strength", K.fist, "red"), ("dex", "Dexterity", boot, "green"),
             ("con", "Constitution", heart_beat, "rust"), ("int", "Intelligence", brain, "blue"),
             ("wis", "Wisdom", owl_eye, "teal"), ("cha", "Charisma", crown_sym, "plum")]
SKILLS = [("acrobatics", "dex", K.wind), ("animal_handling", "wis", paw_sym), ("arcana", "int", K.star),
          ("athletics", "str", K.fist), ("deception", "cha", mask_sym), ("history", "int", scroll_sym),
          ("insight", "wis", eye_open), ("intimidation", "cha", skull),
          ("investigation", "int", magnifier), ("medicine", "wis", herb), ("nature", "int", K.leaf),
          ("perception", "wis", owl_eye), ("performance", "cha", K.note), ("persuasion", "cha", speech),
          ("religion", "int", K.sun), ("sleight_of_hand", "dex", pouch), ("stealth", "dex", footprints),
          ("survival", "wis", K.mountain)]
DAMAGE = [("slashing", K.swords, "steel"), ("piercing", K.target, "steel"), ("bludgeoning", K.hammer, "steel"),
          ("fire", fire_sym, "fire"), ("cold", snowflake, "frost"), ("lightning", K.bolt, "storm"),
          ("thunder", wave, "arcane"), ("poison", drop, "nature"), ("acid", drop, "fel"),
          ("necrotic", skull, "shadow"), ("radiant", K.rays, "holy"), ("force", K.star, "arcane"),
          ("psychic", mind, "void")]
FIELD = {"red": "cloth/crimson", "green": "cloth/forest", "rust": "cloth/rust", "blue": "cloth/navy",
         "teal": "cloth/teal", "plum": "cloth/plum", "steel": "cloth/slate"}
_ABIL = {a[0]: a[3] for a in ABILITIES}


def plate(sym, field_ref: str, sym_ref: str = "metal/gold") -> Image.Image:
    c = Canvas(SIZE, SIZE, SLOTS)
    for y in range(1, SIZE - 1):
        inset = 2 if y in (1, SIZE - 2) else (1 if y in (2, SIZE - 3) else 0)
        c.rect(1 + inset, y, SIZE - 2 - inset, y, "field", 3 if y > SIZE // 2 else 4)
    sym(c, SIZE // 2, SIZE // 2)
    c.outline()
    fam, name = field_ref.split("/")
    sf, sn = sym_ref.split("/")
    return c.render({"field": FAMILIES[fam][name], "rim": FAMILIES["metal"]["gold"], "sym": FAMILIES[sf][sn],
                     "accent": FAMILIES["cloth"]["charcoal"]})


def build() -> None:
    out = PACK / "sheet_icons"
    shutil.rmtree(out, ignore_errors=True)
    items: list[tuple[str, str, Image.Image]] = []
    for aid, name, sym, col in ABILITIES:
        items.append(("ability", aid, plate(sym, FIELD[col])))
    for sid, abil, sym in SKILLS:
        items.append(("skill", sid, plate(sym, FIELD[_ABIL[abil]])))
    for did, sym, mag in DAMAGE:
        items.append(("damage", did, plate(sym, "cloth/charcoal", f"magic/{mag}" if mag != "steel"
                                                else "metal/steel")))
    cols = 12
    atlas = Image.new("RGBA", (cols * SIZE, ((len(items) + cols - 1) // cols) * SIZE))
    rects: dict[str, dict] = {}
    for k, (group, key, img) in enumerate(items):
        x, y = (k % cols) * SIZE, (k // cols) * SIZE
        atlas.paste(img, (x, y))
        rects.setdefault(group, {})[key] = [x, y, SIZE, SIZE]
    save(atlas, out / "sheet_icons.png")
    write_json({"size": [SIZE, SIZE], "file": "sheet_icons.png", "icons": rects,
                "skillAbility": {s: a for s, a, _ in SKILLS},
                "note": "skill plates take their ability's colour, so the sheet groups by colour at a glance"},
               out / "manifest.json")
    secs = []
    for group, title in (("ability", "Abilities"), ("skill", "Skills (coloured by ability)"),
                         ("damage", "Damage types")):
        secs.append(contact_sheet(title, [Tile(img, key.replace("_", " ")) for g, key, img in items if g == group],
                                  columns=9, zoom=4))
    save(stack("Character sheet icons", secs, "For the phone's full D&D sheet; same symbols as the crests."),
         PREVIEWS / "39-sheet-icons.png")
