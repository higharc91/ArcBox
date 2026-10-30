"""The character creator's model: a player names their hero and picks any race with any class.

Race and class are independent axes:
  * a Race decides the *body*: build (standard / short / large), body part (ears, tusks, a snout), tail, which
    skins, hair, beards and horns are on offer, and sensible defaults for each;
  * a ClassKit decides the *gear*: torso, legs, feet, hands, weapon, off-hand, cape, headgear, and the starting
    cloth and metal colours.
A Look holds the player's own picks from what the race offers (and any colour changes to the kit).

make_character(name, race, class, look) merges the three into a Recipe, which composes like any other preset. Every
combination is valid because every part is drawn from the same rig keypoints on every build. build.py renders the
full race x class matrix to prove it, and exports races.json / classes.json so the phone creator offers exactly
these options.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from .characters import Recipe

HAIR_STYLES = ["short", "messy", "long", "ponytail", "bun", "mohawk", "braids", "cropped"]
HAIR_COLOURS = ["black", "brown", "chestnut", "auburn", "copper", "blonde", "platinum", "silver", "white", "teal",
                "violet", "crimson"]
HUMAN_SKINS = ["porcelain", "fair", "tan", "olive", "brown", "deep"]
EYES = ["dark", "blue", "green", "amber", "violet", "red", "gold"]


@dataclass(frozen=True)
class Race:
    id: str
    name: str
    build: str
    body: str
    skins: list[str]
    hair: list[str]                       # styles on offer; [] means no hair (dragonborn)
    beards: list[str]                     # [] means none on offer
    horns: list[str] = field(default_factory=list)
    horns_required: bool = False
    tail: str | None = None
    horn_colours: list[str] = field(default_factory=lambda: ["ivory", "bone", "obsidian", "ram", "crimson"])
    default: dict[str, str] = field(default_factory=dict)   # skin, hair, hair_colour, beard, horns, horn, eyes
    traits: list[str] = field(default_factory=list)         # shown as chips in the creator


@dataclass(frozen=True)
class ClassKit:
    id: str
    name: str
    gear: dict[str, str]                  # layer -> variant
    colours: dict[str, str]               # slot -> "family/ramp"
    role: str = ""


RACES: dict[str, Race] = {r.id: r for r in [
    Race("human", "Human", "standard", "human", HUMAN_SKINS, HAIR_STYLES, ["short", "full", "braided"],
         default={"skin": "tan", "hair": "short", "hair_colour": "brown", "eyes": "dark"},
         traits=["Versatile", "+1 to every score"]),
    Race("elf", "Elf", "standard", "elf", HUMAN_SKINS, HAIR_STYLES, [],
         default={"skin": "porcelain", "hair": "long", "hair_colour": "platinum", "eyes": "green"},
         traits=["Darkvision", "Fey ancestry", "Keen senses"]),
    Race("half_elf", "Half-Elf", "standard", "half_elf", HUMAN_SKINS, HAIR_STYLES, ["short"],
         default={"skin": "fair", "hair": "ponytail", "hair_colour": "auburn", "eyes": "green"},
         traits=["Darkvision", "Fey ancestry", "Two extra skills"]),
    Race("dwarf", "Dwarf", "short", "human", ["fair", "tan", "olive", "brown", "deep"], HAIR_STYLES,
         ["full", "braided", "short"],
         default={"skin": "tan", "hair": "short", "hair_colour": "copper", "beard": "full", "eyes": "dark"},
         traits=["Darkvision", "Poison resilience", "Stone sense"]),
    Race("halfling", "Halfling", "short", "human", HUMAN_SKINS, HAIR_STYLES, [],
         default={"skin": "olive", "hair": "messy", "hair_colour": "brown", "eyes": "green"},
         traits=["Lucky", "Brave", "Nimble"]),
    Race("gnome", "Gnome", "short", "gnome", HUMAN_SKINS, HAIR_STYLES, ["short", "full"],
         default={"skin": "fair", "hair": "messy", "hair_colour": "teal", "eyes": "blue"},
         traits=["Darkvision", "Gnome cunning"]),
    Race("half_orc", "Half-Orc", "large", "orc", ["orc", "orc_grey", "orc_olive", "goblin"], HAIR_STYLES,
         ["short"],
         default={"skin": "orc", "hair": "mohawk", "hair_colour": "black", "eyes": "amber", "horn": "ivory"},
         traits=["Darkvision", "Relentless endurance", "Savage attacks"]),
    Race("tiefling", "Tiefling", "standard", "human", ["tiefling", "infernal", "lilac", "ash"], HAIR_STYLES,
         [], horns=["curled", "straight", "ram", "small"], horns_required=True, tail="devil",
         default={"skin": "tiefling", "hair": "long", "hair_colour": "black", "horns": "curled",
                  "horn": "ram", "eyes": "gold"},
         traits=["Darkvision", "Hellish resistance", "Infernal legacy"]),
    Race("dragonborn", "Dragonborn", "standard", "dragon",
         ["dragon_red", "dragon_blue", "dragon_green", "dragon_black", "dragon_white", "dragon_gold",
          "dragon_bronze", "dragon_silver", "dragon_copper", "dragon_brass"], [], [],
         horns=["swept", "straight", "small"], tail="dragon",
         default={"skin": "dragon_bronze", "horns": "swept", "horn": "bone", "eyes": "gold"},
         traits=["Breath weapon", "Damage resistance"]),
]}

CLASSES: dict[str, ClassKit] = {k.id: k for k in [
    ClassKit("fighter", "Fighter", {"torso": "chain", "legs": "pants", "feet": "boots", "hands": "gloves",
                                    "weapon": "sword", "offhand": "round_shield"},
             {"cloth1": "cloth/steel_blue", "cloth2": "cloth/slate"}, "Front line"),
    ClassKit("rogue", "Rogue", {"torso": "vest", "legs": "pants", "feet": "boots", "hands": "gloves",
                                "weapon": "dagger", "cape": "short_cape", "headgear": "hood"},
             {"cloth1": "cloth/charcoal", "cloth2": "cloth/slate", "leather": "leather/dark"}, "Striker"),
    ClassKit("wizard", "Wizard", {"torso": "robe", "legs": "skirt", "feet": "sandals", "hands": "bare",
                                  "weapon": "staff", "headgear": "wizard_hat"},
             {"cloth1": "cloth/navy", "cloth2": "cloth/sky", "magic": "magic/arcane"}, "Control"),
    ClassKit("cleric", "Cleric", {"torso": "vestments", "legs": "pants", "feet": "boots", "hands": "bare",
                                  "weapon": "mace", "offhand": "round_shield"},
             {"cloth1": "cloth/ivory", "cloth2": "cloth/ochre", "magic": "magic/holy"}, "Healer"),
    ClassKit("ranger", "Ranger", {"torso": "coat", "legs": "pants", "feet": "boots", "hands": "gloves",
                                  "weapon": "bow", "cape": "cloak"},
             {"cloth1": "cloth/forest", "cloth2": "cloth/sand"}, "Ranged"),
    ClassKit("paladin", "Paladin", {"torso": "plate", "legs": "greaves", "feet": "sabatons", "hands": "gauntlets",
                                    "weapon": "greatsword", "cape": "cloak"},
             {"cloth1": "cloth/ivory", "cloth2": "cloth/sky", "metal": "metal/silver"}, "Defender"),
    ClassKit("bard", "Bard", {"torso": "tunic", "legs": "pants", "feet": "boots", "hands": "bare",
                              "weapon": "lute", "cape": "short_cape"},
             {"cloth1": "cloth/teal", "cloth2": "cloth/ochre", "wood": "wood/pale"}, "Support"),
    ClassKit("druid", "Druid", {"torso": "robe", "legs": "skirt", "feet": "sandals", "hands": "bare",
                                "weapon": "staff", "headgear": "circlet"},
             {"cloth1": "cloth/moss", "cloth2": "cloth/forest", "magic": "magic/nature", "wood": "wood/dark"},
             "Shapeshifter"),
    ClassKit("warlock", "Warlock", {"torso": "coat", "legs": "pants", "feet": "boots", "hands": "gloves",
                                    "weapon": "tome", "cape": "cloak"},
             {"cloth1": "cloth/plum", "cloth2": "cloth/charcoal", "trim": "metal/dark", "magic": "magic/shadow"},
             "Blaster"),
    ClassKit("monk", "Monk", {"torso": "tunic", "legs": "pants", "feet": "sandals", "hands": "bare"},
             {"cloth1": "cloth/ochre", "cloth2": "cloth/rust"}, "Skirmisher"),
]}

_BASE_COLOURS = {"eyes": "eyes/dark", "leather": "leather/brown", "wood": "wood/oak", "metal": "metal/steel",
                 "trim": "metal/gold", "magic": "magic/arcane", "horn": "horn/ivory", "hair": "hair/brown"}


@dataclass
class Look:
    """The player's picks. Anything left as None falls back to the race default; `colours` recolours any slot
    (e.g. {"cloth1": "cloth/crimson"}), `gear` swaps or removes kit pieces (None removes, e.g. a helmet)."""
    skin: str | None = None
    hair: str | None = None
    hair_colour: str | None = None
    beard: str | None = None
    horns: str | None = None
    horn: str | None = None
    eyes: str | None = None
    colours: dict[str, str] = field(default_factory=dict)
    gear: dict[str, str | None] = field(default_factory=dict)


class CreatorError(ValueError):
    pass


def make_character(name: str, race: str, cls: str, look: Look | None = None, *, id: str | None = None,
                   tags: tuple[str, ...] = ()) -> Recipe:
    """Name + race + class (+ look) -> a Recipe. Rejects picks the race doesn't offer, so the phone creator and the
    server can share this validation."""
    if not name.strip():
        raise CreatorError("a character needs a name")
    r, k = RACES[race], CLASSES[cls]
    lk = look or Look()

    def pick(attr: str, offered: list[str], *, required: bool = False) -> str | None:
        v = getattr(lk, attr)
        if v is None:
            v = r.default.get(attr)
        if v in (None, "", "none"):
            if required:
                raise CreatorError(f"{r.name} needs a {attr}")
            return None
        if v not in offered:
            raise CreatorError(f"{r.name} can't pick {attr}={v!r}; offered: {offered}")
        return v

    skin = pick("skin", r.skins, required=True)
    hair = pick("hair", r.hair)
    beard = pick("beard", r.beards)
    horns = pick("horns", r.horns, required=r.horns_required)

    layers: dict[str, str] = {"body": r.body, **k.gear}
    for layer, variant in lk.gear.items():
        if variant is None:
            layers.pop(layer, None)
        else:
            layers[layer] = variant
    for layer, variant in (("hair", hair), ("beard", beard), ("horns", horns), ("tail", r.tail)):
        if variant:
            layers[layer] = variant

    palette = dict(_BASE_COLOURS)
    palette.update(k.colours)
    palette["skin"] = f"skin/{skin}"
    palette["hair"] = f"hair/{lk.hair_colour or r.default.get('hair_colour', 'brown')}"
    palette["eyes"] = f"eyes/{lk.eyes or r.default.get('eyes', 'dark')}"
    palette["horn"] = f"horn/{lk.horn or r.default.get('horn', 'ivory')}"
    palette.update(lk.colours)

    rid = id or "".join(ch for ch in name.lower().replace(" ", "_") if ch.isalnum() or ch == "_")
    return Recipe(rid, name, r.build, layers, palette, [race, cls, *tags], race=race, cls=cls)


def options() -> dict:
    """Everything the phone creator needs to offer choices (exported as races.json / classes.json)."""
    return {
        "races": {r.id: {"name": r.name, "build": r.build, "body": r.body, "skins": r.skins, "hair": r.hair,
                         "hairColours": HAIR_COLOURS if r.hair else [], "beards": r.beards, "horns": r.horns,
                         "hornsRequired": r.horns_required, "hornColours": r.horn_colours if r.horns else [],
                         "tail": r.tail, "eyes": EYES, "default": r.default, "traits": r.traits}
                  for r in RACES.values()},
        "classes": {k.id: {"name": k.name, "role": k.role, "gear": k.gear, "colours": k.colours}
                    for k in CLASSES.values()},
    }
