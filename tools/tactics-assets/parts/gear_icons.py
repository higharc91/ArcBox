"""Inventory icons generated from the character layers: every weapon, off-hand, helm, armour and cloak variant gets a
24x24 icon drawn by the very part the character wears, so loot in the bag always matches what appears on the hero.
Each loot tier is just a different metal ramp, so the icon for a mithril sword is the same drawing in mithril.

A part is drawn alone (no body) in a pose that presents it well (a weapon held at 45 degrees, armour on an empty
frame), cropped to its pixels, centred on the cell and outlined.
"""
from __future__ import annotations

import shutil

from PIL import Image

from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import Tile, contact_sheet, stack
from . import characters as C
from .rig import Pose

CELL = 24
TIERS = ["iron", "steel", "silver", "gold", "mithril", "adamantine", "obsidian", "elven"]
DISPLAY = {   # layer -> pose that shows it off
    "weapon": Pose(weapon=45, hand_n=(-2, 0)),
    "offhand": Pose(hand_f=(3, -2)),
    "headgear": Pose(),
    "torso": Pose(hand_n=(2, 2), hand_f=(-2, 2)),
    "cape": Pose(),
}                                 # hands and feet read as two dots on their own: the game shows the torso icon
FACING = {"cape": "back"}
PALETTE = {"skin": "skin/tan", "eyes": "eyes/dark", "hair": "hair/brown", "cloth1": "cloth/crimson",
           "cloth2": "cloth/ivory", "trim": "metal/gold", "metal": "metal/steel", "leather": "leather/brown",
           "wood": "wood/oak", "magic": "magic/arcane", "horn": "horn/bone", "paint": "paint/woad",
           "blade": "metal/steel"}
SKIP = {"cape": {"quiver", "banner_back"}}          # worn items that make poor standalone icons


def gear_icon(layer: str, variant: str, metal: str = "steel") -> Image.Image:
    pal = dict(PALETTE, metal=f"metal/{metal}", blade=f"metal/{metal}")
    r = C.Recipe("icon", "icon", "standard", {layer: variant}, pal)
    cv = C.compose(r, FACING.get(layer, "front"), DISPLAY[layer])
    img = cv.render(r.ramps())
    box = img.getbbox()
    out = Image.new("RGBA", (CELL, CELL))
    if box is None:
        return out
    part = img.crop(box)
    if part.width > CELL or part.height > CELL:           # very long weapons: keep the business end
        part = part.crop((max(0, part.width - CELL), 0, part.width, min(part.height, CELL)))
    out.alpha_composite(part, ((CELL - part.width) // 2, (CELL - part.height) // 2))
    return out


def build() -> None:
    out = PACK / "gear_icons"
    shutil.rmtree(out, ignore_errors=True)
    layers = [lay for lay in DISPLAY if lay in C.PARTS]
    rects, sections = {}, []
    atlas_cells = []
    for layer in layers:
        tiles = []
        for v in sorted(C.PARTS[layer]):
            if v in SKIP.get(layer, set()):
                continue
            img = gear_icon(layer, v)
            rects[f"{layer}/{v}"] = len(atlas_cells)
            atlas_cells.append(img)
            tiles.append(Tile(img, v))
        sections.append(contact_sheet(layer.capitalize(), tiles, columns=12, zoom=3))
    cols = 16
    rows = (len(atlas_cells) + cols - 1) // cols
    atlas = Image.new("RGBA", (cols * CELL, rows * CELL))
    for k, im in enumerate(atlas_cells):
        atlas.paste(im, ((k % cols) * CELL, (k // cols) * CELL))
    save(atlas, out / "gear.png")
    # metal tiers for the metal-bearing pieces, as separate atlases (one per tier, same layout)
    for tier in TIERS:
        t_atlas = Image.new("RGBA", atlas.size)
        for key, k in rects.items():
            layer, v = key.split("/")
            t_atlas.paste(gear_icon(layer, v, tier), ((k % cols) * CELL, (k // cols) * CELL))
        save(t_atlas, out / f"gear_{tier}.png")
    write_json({"cell": [CELL, CELL], "columns": cols,
                "icons": {key: [(k % cols) * CELL, (k // cols) * CELL, CELL, CELL] for key, k in rects.items()},
                "tiers": {t: f"gear_{t}.png" for t in TIERS}, "default": "gear.png",
                "note": "key = layer/variant (the same ids as the character layers); frame with icons/frames.png"},
               out / "manifest.json")
    tier_tiles = [Tile(Image.merge("RGBA", gear_icon("weapon", "greatsword", t).split()), t) for t in TIERS]
    sections.append(contact_sheet("Loot tiers · one drawing, eight metals", tier_tiles, columns=8, zoom=3))
    save(stack("Gear icons", sections, "Generated from the character layers, so bag icons always match the hero."),
         PREVIEWS / "34-gear-icons.png")
