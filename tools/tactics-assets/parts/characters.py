"""Builds the layered character sprites: every part x every build, as recolourable ramp-map sheets, plus recipes
(complete characters = a build + one variant per layer + a palette) rendered as ready-to-use previews.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.palette import FAMILIES, RAMP_LEN
from arcpix.sheet import Sheet, Anim
from . import character_parts as P
from .rig import BUILDS, DIRECTIONS, FRAME_W, FRAME_H, ANCHOR, ANIMS, ANIM_ORDER, frame_list, points

SLOTS = SlotTable()
for _s in ("skin", "eyes", "hair", "cloth1", "cloth2", "trim", "metal", "leather", "wood", "magic", "horn", "paint", "blade"):
    SLOTS.id(_s)

# layer -> variant -> part factory
PARTS: dict[str, dict[str, P.Part]] = {
    "cape": {k: P.cape(k) for k in ("short_cape", "cloak", "pelt", "quiver", "banner_back")},
    "body": {k: P.body(k) for k in ("human", "elf", "half_elf", "gnome", "orc", "dragon", "goblin", "skeleton")},
    "tail": {k: P.tail(k) for k in ("devil", "dragon")},
    "legs": {k: P.legs(k) for k in ("pants", "greaves", "shorts", "skirt")},
    "feet": {k: P.feet(k) for k in ("boots", "sabatons", "sandals")},
    "torso": {k: P.torso(k) for k in ("tunic", "vest", "chain", "plate", "robe", "vestments", "coat", "rags",
                                         "leather", "scale", "tabard", "gi")},
    "beard": {k: P.beard(k) for k in ("short", "full", "braided", "goatee", "moustache", "stubble")},
    "hair": {k: P.hair(k) for k in ("short", "messy", "long", "ponytail", "bun", "mohawk", "braids", "cropped",
                                    "curly", "pixie", "topknot", "twintails")},
    "marks": {k: P.marks(k) for k in ("freckles", "scar", "warpaint", "tattoo", "stripes")},
    "horns": {k: P.horns(k) for k in ("curled", "straight", "ram", "small", "swept")},
    "headgear": {k: P.headgear(k) for k in ("open_helm", "great_helm", "hood", "wizard_hat", "circlet", "bandana", "crown",
                                               "horned_helm", "feathered_cap", "mask_hood", "winged_helm", "halo",
                                               "beret", "antlers", "flower_crown", "headband")},
    "weapon": {k: P.weapon(k) for k in ("sword", "greatsword", "axe", "mace", "dagger", "spear", "staff", "bow", "tome", "wand", "rapier", "lute", "warhammer",
                                           "halberd", "flail", "scimitar", "quarterstaff", "crossbow",
                                           "hourglass_staff", "crystal_staff", "holy_staff", "storm_staff",
                                           "living_staff", "lyre")},
    "hands": {k: P.hands(k) for k in ("bare", "gloves", "gauntlets", "wraps")},
    "offhand": {k: P.offhand(k) for k in ("round_shield", "kite_shield", "buckler", "lantern", "orb",
                                              "tower_shield", "blade", "dagger", "main_gauche", "bomb",
                                              "war_horn")},
}

# Draw order per facing (first = bottom). In the front view the cloak hangs behind the body and the off-hand
# (shield) is held in front; from behind it is the other way round.
ORDER: dict[str, list[str]] = {
    "front": ["tail", "cape", "body", "marks", "legs", "feet", "torso", "beard", "hair", "headgear", "horns", "weapon", "hands",
              "offhand"],
    "back": ["offhand", "body", "marks", "legs", "feet", "torso", "tail", "weapon", "hands", "cape", "beard", "hair", "headgear",
             "horns"],
}

# Slots that get automatic volume shading per layer (others are drawn with explicit shades).
VOLUME_SLOTS = ("skin", "hair", "cloth1", "cloth2", "metal", "leather", "wood", "trim", "blade")
# Horns are excluded on purpose: they're small, and drawn with explicit shades.


def render_layer(layer: str, variant: str, build: str, direction: str, pose) -> Canvas:
    """One layer, shaded, *without* an outline: outlines are applied once to the composed character, otherwise
    every upper layer's outline would paint over the layer beneath it (eyes under a fringe, hands on a torso)."""
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    pts = points(BUILDS[build], pose, direction)
    PARTS[layer][variant](c, pts)
    for s in VOLUME_SLOTS:
        if c.mask(s).any():
            c.shade_form(s)
    return c


def layer_frames(layer: str, variant: str, build: str) -> list[Canvas]:
    """All frames for both facings: front row then back row, ANIM_ORDER within a row."""
    out: list[Canvas] = []
    for d in DIRECTIONS:
        for _anim, _i, pose in frame_list():
            out.append(render_layer(layer, variant, build, d, pose))
    return out


def frames_per_direction() -> int:
    return len(frame_list())


def anim_table() -> list[Anim]:
    """Animations in sheet terms (front row; the back row is the same indices + frames_per_direction())."""
    anims, i = [], 0
    for name in ANIM_ORDER:
        fps, loop, poses = ANIMS[name]
        anims.append(Anim(name, list(range(i, i + len(poses))), fps, loop))
        i += len(poses)
    return anims


@dataclass
class Recipe:
    id: str
    name: str
    build: str
    layers: dict[str, str]                         # layer -> variant (omit a layer to leave it empty)
    palette: dict[str, str]                        # slot -> "family/ramp"
    tags: list[str] = field(default_factory=list)
    race: str | None = None                        # creator picks (None for enemies authored directly)
    cls: str | None = None

    def __post_init__(self) -> None:
        # Weapons have their own metal; unless a palette says otherwise it matches the armour.
        self.palette.setdefault("blade", self.palette.get("metal", "metal/steel"))

    def ramps(self) -> dict[str, list[str]]:
        out = {}
        for slot, ref in self.palette.items():
            fam, name = ref.split("/")
            out[slot] = FAMILIES[fam][name]
        return out


def compose(recipe: Recipe, direction: str, pose) -> Canvas:
    """Stacks the recipe's layers in draw order, then outlines the silhouette once. This is exactly what the game
    does when a character is created or changes gear (see README, "Composing a character")."""
    c = Canvas(FRAME_W, FRAME_H, SLOTS)
    for layer in ORDER[direction]:
        v = recipe.layers.get(layer)
        if v:
            c.blit(render_layer(layer, v, recipe.build, direction, pose))
    c.outline()
    return c


def recipe_sheet(recipe: Recipe) -> Sheet:
    ramps = recipe.ramps()
    s = Sheet(FRAME_W, FRAME_H, frames_per_direction(), ANCHOR, anims=anim_table())
    for d in DIRECTIONS:
        for _a, _i, pose in frame_list():
            s.frames.append(compose(recipe, d, pose).render(ramps))
    return s


def lut(palettes: dict[str, dict[str, str]]) -> tuple[Image.Image, list[str]]:
    """The palette lookup texture for the runtime recolour shader: one row per palette, 16 slots x 8 shades
    (128 px). Pixel (slot*8 + shade, row) is that slot's colour; unused cells stay transparent."""
    names = list(palettes)
    img = Image.new("RGBA", (16 * 8, len(names)), (0, 0, 0, 0))
    for row, name in enumerate(names):
        for slot, ref in palettes[name].items():
            fam, rname = ref.split("/")
            ramp = FAMILIES[fam][rname]
            sid = SLOTS.id(slot)
            for shade in range(RAMP_LEN):
                h = ramp[shade].lstrip("#")
                img.putpixel((sid * 8 + shade, row), (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255))
    return img, names
