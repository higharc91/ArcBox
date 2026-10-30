"""The character creator's model: a player names their hero and picks any race with any class.

Race and class are independent axes:
  * a Race decides the *body*: build (standard / short / large), body part (ears, tusks, a snout), tail, which
    skins, hair, beards and horns are on offer, and sensible defaults for each;
  * a ClassKit decides the *gear*: torso, legs, feet, hands, weapon, off-hand, cape, headgear, and the starting
    cloth and metal colours. A kit may also carry face marks (the barbarian's war paint) and a paint colour; the
    player's or the race's own marks and paint win over them.
A Look holds the player's own picks from what the race offers (and any colour changes to the kit).

make_character(name, race, class, look) merges the three into a Recipe, which composes like any other preset. Every
combination is valid because every part is drawn from the same rig keypoints on every build. build.py renders the
full race x class matrix to prove it, and exports races.json / classes.json so the phone creator offers exactly
these options.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from .characters import Recipe

HAIR_STYLES = ["short", "messy", "long", "ponytail", "bun", "mohawk", "braids", "cropped", "curly", "pixie", "topknot",
               "twintails"]
ALL_BEARDS = ["short", "full", "braided", "goatee", "moustache", "stubble"]
MARKS = ["freckles", "scar", "warpaint", "tattoo", "stripes"]
PAINTS = ["woad", "ochre", "ash", "kohl", "gold", "crimson", "jade"]
BODY_TYPES = ["masculine", "feminine"]
HAIR_COLOURS = ["black", "brown", "chestnut", "auburn", "copper", "blonde", "platinum", "silver", "white", "teal",
                "violet", "crimson"]
HUMAN_SKINS = ["porcelain", "fair", "tan", "olive", "brown", "deep"]
EYES = ["dark", "blue", "green", "amber", "violet", "red", "gold", "radiant"]


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
    Race("human", "Human", "standard", "human", HUMAN_SKINS, HAIR_STYLES, ALL_BEARDS,
         default={"skin": "tan", "hair": "short", "hair_colour": "brown", "eyes": "dark"},
         traits=["Versatile", "+1 to every score"]),
    Race("elf", "Elf", "standard", "elf", HUMAN_SKINS, HAIR_STYLES, [],
         default={"skin": "porcelain", "hair": "long", "hair_colour": "platinum", "eyes": "green"},
         traits=["Darkvision", "Fey ancestry", "Keen senses"]),
    Race("half_elf", "Half-Elf", "standard", "half_elf", HUMAN_SKINS, HAIR_STYLES, ["short", "goatee", "stubble"],
         default={"skin": "fair", "hair": "ponytail", "hair_colour": "auburn", "eyes": "green"},
         traits=["Darkvision", "Fey ancestry", "Two extra skills"]),
    Race("dwarf", "Dwarf", "short", "human", ["fair", "tan", "olive", "brown", "deep"], HAIR_STYLES,
         ["full", "braided", "short", "goatee", "moustache", "stubble"],
         default={"skin": "tan", "hair": "short", "hair_colour": "copper", "beard": "full", "eyes": "dark"},
         traits=["Darkvision", "Poison resilience", "Stone sense"]),
    Race("halfling", "Halfling", "short", "human", HUMAN_SKINS, HAIR_STYLES, [],
         default={"skin": "olive", "hair": "messy", "hair_colour": "brown", "eyes": "green"},
         traits=["Lucky", "Brave", "Nimble"]),
    Race("gnome", "Gnome", "short", "gnome", HUMAN_SKINS, HAIR_STYLES, ["short", "full", "moustache", "goatee"],
         default={"skin": "fair", "hair": "messy", "hair_colour": "teal", "eyes": "blue"},
         traits=["Darkvision", "Gnome cunning"]),
    Race("half_orc", "Half-Orc", "large", "orc", ["orc", "orc_grey", "orc_olive", "goblin"], HAIR_STYLES,
         ["short", "goatee", "moustache", "stubble"],
         default={"skin": "orc", "hair": "mohawk", "hair_colour": "black", "eyes": "amber", "horn": "ivory"},
         traits=["Darkvision", "Relentless endurance", "Savage attacks"]),
    Race("tiefling", "Tiefling", "standard", "human", ["tiefling", "infernal", "lilac", "ash"], HAIR_STYLES,
         [], horns=["curled", "straight", "ram", "small"], horns_required=True, tail="devil",
         default={"skin": "tiefling", "hair": "long", "hair_colour": "black", "horns": "curled",
                  "horn": "ram", "eyes": "gold"},
         traits=["Darkvision", "Hellish resistance", "Infernal legacy"]),
    Race("aasimar", "Aasimar", "standard", "human", ["celestial_gold", "celestial_silver", "celestial_bronze",
                                                     "porcelain", "brown"], HAIR_STYLES, ["short", "full"],
         default={"skin": "celestial_gold", "hair": "long", "hair_colour": "chestnut", "eyes": "radiant"},
         traits=["Darkvision", "Healing hands", "Radiant soul"]),
    Race("goliath", "Goliath", "large", "human", ["stone_grey", "stone_blue", "stone_umber"], HAIR_STYLES,
         ["short", "braided"],
         default={"skin": "stone_grey", "hair": "none", "eyes": "blue", "marks": "tattoo", "paint": "kohl"},
         traits=["Stone's endurance", "Powerful build", "Mountain born"]),
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
    ClassKit("barbarian", "Barbarian", {"torso": "harness", "legs": "kilt", "feet": "boots", "hands": "wraps",
                                        "weapon": "greataxe", "cape": "pelt", "marks": "warpaint"},
             {"cloth1": "leather/tan", "cloth2": "cloth/rust", "leather": "leather/dark", "paint": "paint/woad"},
             "Defender"),
    ClassKit("sorcerer", "Sorcerer", {"torso": "silk_robe", "legs": "skirt", "feet": "sandals", "hands": "bare",
                                      "weapon": "spellfire"},
             {"cloth1": "cloth/royal_purple", "cloth2": "cloth/ochre", "trim": "metal/gold", "magic": "magic/wild"},
             "Blaster"),
]}

@dataclass(frozen=True)
class Subclass:
    """Picked at level 3 and locked. It reshapes the class kit so the choice shows on the battlefield: different
    gear (a tower shield, dual blades, antlers), an accent colour scheme, and for some, a companion unit."""
    id: str
    name: str
    cls: str
    role: str
    gear: dict[str, str | None]            # layer -> variant (None removes the kit's piece)
    colours: dict[str, str]
    companion: str | None = None           # a bosses-set summon id the game spawns beside the hero


def _sub(cls: str, id: str, name: str, role: str, gear: dict, colours: dict, companion: str | None = None
         ) -> Subclass:
    return Subclass(id, name, cls, role, gear, colours, companion)


SUBCLASSES: dict[str, Subclass] = {s.id: s for s in [
    # Fighter
    _sub("fighter", "vanguard", "Vanguard", "Shield wall, taunt, zone control",
         {"torso": "plate", "legs": "greaves", "feet": "sabatons", "hands": "gauntlets", "offhand": "tower_shield",
          "headgear": "open_helm"}, {"cloth1": "cloth/crimson", "cloth2": "metal/gold"}),
    _sub("fighter", "weaponmaster", "Weaponmaster", "Stances, multi-hit",
         {"torso": "scale", "weapon": "halberd", "offhand": None, "cape": "short_cape", "headgear": "headband"},
         {"cloth1": "cloth/charcoal", "cloth2": "cloth/crimson", "blade": "metal/steel"}),
    _sub("fighter", "warlord", "Warlord", "Orders that give allies extra moves",
         {"torso": "plate", "legs": "greaves", "feet": "sabatons", "hands": "gauntlets", "cape": "banner_back",
          "headgear": "feathered_cap", "offhand": "war_horn"},
         {"cloth1": "cloth/navy", "cloth2": "metal/gold", "metal": "metal/bronze"}),
    # Rogue
    _sub("rogue", "shadow", "Shadow", "Stealth, backstabs from behind",
         {"headgear": "mask_hood", "offhand": "dagger", "torso": "leather", "cape": "cloak"},
         {"cloth1": "cloth/charcoal", "cloth2": "cloth/charcoal", "leather": "leather/dark", "blade": "metal/dark"}),
    _sub("rogue", "trickster", "Trickster", "Traps, swaps positions",
         {"headgear": "bandana", "offhand": "bomb", "torso": "vest", "cape": None},
         {"cloth1": "cloth/plum", "cloth2": "cloth/ochre", "magic": "magic/fire"}),
    _sub("rogue", "duelist", "Duelist", "Riposte, counters",
         {"headgear": "feathered_cap", "weapon": "rapier", "offhand": "main_gauche", "torso": "coat",
          "cape": "short_cape"}, {"cloth1": "cloth/wine", "cloth2": "cloth/ivory", "trim": "metal/silver"}),
    # Wizard
    _sub("wizard", "evoker", "Evoker", "Large area spells shaped to miss allies",
         {"weapon": "staff", "headgear": "wizard_hat"},
         {"cloth1": "cloth/crimson", "cloth2": "cloth/ochre", "magic": "magic/fire"}),
    _sub("wizard", "chronomancer", "Chronomancer", "Haste and slow, changes turn order",
         {"weapon": "hourglass_staff", "headgear": "wizard_hat", "offhand": "orb"},
         {"cloth1": "cloth/royal_purple", "cloth2": "cloth/navy", "trim": "metal/gold", "magic": "magic/frost"}),
    _sub("wizard", "geomancer", "Geomancer", "Raises and lowers tile height",
         {"weapon": "crystal_staff", "headgear": "hood", "hands": "wraps"},
         {"cloth1": "cloth/rust", "cloth2": "cloth/sand", "magic": "magic/earth"}),
    # Cleric
    _sub("cleric", "life", "Life Domain", "Big heals, revives",
         {"weapon": "holy_staff", "offhand": None, "headgear": "circlet", "legs": "skirt", "feet": "sandals"},
         {"cloth1": "cloth/ivory", "cloth2": "metal/gold", "magic": "magic/holy"}),
    _sub("cleric", "war", "War Domain", "Melee buffer",
         {"torso": "chain", "weapon": "warhammer", "offhand": "kite_shield", "headgear": "winged_helm",
          "hands": "gauntlets"}, {"cloth1": "cloth/crimson", "cloth2": "cloth/ivory", "magic": "magic/holy"}),
    _sub("cleric", "light", "Light Domain", "Radiant area damage, reveals stealth",
         {"headgear": "halo", "offhand": "lantern", "cape": "cloak"},
         {"cloth1": "cloth/ivory", "cloth2": "cloth/sky", "magic": "magic/holy", "trim": "metal/gold"}),
    # Ranger
    _sub("ranger", "sharpshooter", "Sharpshooter", "Gains range from height",
         {"cape": "quiver", "headgear": "feathered_cap", "torso": "coat"},
         {"cloth1": "cloth/forest", "cloth2": "cloth/ivory"}),
    _sub("ranger", "beastmaster", "Beastmaster", "Has a companion unit",
         {"cape": "pelt", "weapon": "spear", "headgear": None},
         {"cloth1": "leather/brown", "cloth2": "cloth/sand"}, companion="hound"),
    _sub("ranger", "warden", "Warden", "Snares, difficult terrain",
         {"weapon": "crossbow", "headgear": "hood", "torso": "leather", "cape": "cloak"},
         {"cloth1": "cloth/moss", "cloth2": "cloth/forest", "leather": "leather/dark"}),
    # Paladin
    _sub("paladin", "guardian", "Guardian", "Takes hits meant for allies",
         {"weapon": "mace", "offhand": "tower_shield", "headgear": "great_helm"},
         {"cloth1": "cloth/sky", "cloth2": "cloth/ivory", "metal": "metal/silver"}),
    _sub("paladin", "crusader", "Crusader", "Burst smites",
         {"torso": "tabard", "headgear": "winged_helm", "weapon": "greatsword"},
         {"cloth1": "cloth/ivory", "cloth2": "cloth/crimson", "metal": "metal/silver", "magic": "magic/holy"}),
    _sub("paladin", "avenger", "Avenger", "Punishes whoever hit an ally",
         {"headgear": "horned_helm", "weapon": "flail", "offhand": "buckler"},
         {"cloth1": "cloth/charcoal", "cloth2": "cloth/crimson", "metal": "metal/dark", "blade": "metal/obsidian"}),
    # Bard
    _sub("bard", "valor", "College of Valor", "Buffs, grants extra actions",
         {"torso": "chain", "weapon": "sword", "offhand": "war_horn", "headgear": "feathered_cap"},
         {"cloth1": "cloth/crimson", "cloth2": "metal/gold"}),
    _sub("bard", "lore", "College of Lore", "Debuffs, copies an enemy ability",
         {"torso": "coat", "weapon": "tome", "headgear": "beret", "cape": "cloak"},
         {"cloth1": "cloth/navy", "cloth2": "cloth/ivory", "magic": "magic/arcane"}),
    _sub("bard", "siren", "College of the Siren", "Charm, moves enemies",
         {"weapon": "lyre", "headgear": "circlet", "torso": "robe", "legs": "skirt", "feet": "sandals"},
         {"cloth1": "cloth/teal", "cloth2": "cloth/sky", "magic": "magic/frost"}),
    # Druid
    _sub("druid", "shapeshifter", "Shapeshifter", "Beast forms",
         {"headgear": "antlers", "cape": "pelt", "weapon": "quarterstaff"},
         {"cloth1": "leather/tan", "cloth2": "cloth/moss", "horn": "horn/bone"}),
    _sub("druid", "stormcaller", "Stormcaller", "Weather zones",
         {"weapon": "storm_staff", "headgear": "hood", "cape": "cloak"},
         {"cloth1": "cloth/navy", "cloth2": "cloth/slate", "magic": "magic/storm"}),
    _sub("druid", "grovekeeper", "Grovekeeper", "Summons, healing over time",
         {"weapon": "living_staff", "headgear": "flower_crown"},
         {"cloth1": "cloth/moss", "cloth2": "foliage/blossom", "magic": "magic/nature"}, companion="sapling"),
    # Warlock
    _sub("warlock", "hexblade", "Hexblade", "Cursed melee",
         {"weapon": "sword", "offhand": None, "torso": "leather", "headgear": "hood"},
         {"cloth1": "cloth/charcoal", "cloth2": "cloth/plum", "blade": "metal/obsidian", "magic": "magic/shadow"}),
    _sub("warlock", "fiendbound", "Fiendbound", "Summons an imp, burning damage",
         {"weapon": "tome", "offhand": "orb"},
         {"cloth1": "cloth/crimson", "cloth2": "cloth/charcoal", "magic": "magic/fire", "trim": "metal/gold"},
         companion="imp"),
    _sub("warlock", "void", "Void", "Teleport, pull",
         {"weapon": "wand", "offhand": "orb", "torso": "robe", "legs": "skirt", "headgear": "hood"},
         {"cloth1": "cloth/navy", "cloth2": "cloth/charcoal", "magic": "magic/void"}),
    # Monk
    _sub("monk", "open_hand", "Way of the Open Hand", "Knockback combos, push enemies off ledges",
         {"torso": "gi", "hands": "wraps", "headgear": "headband"},
         {"cloth1": "cloth/ivory", "cloth2": "cloth/crimson"}),
    _sub("monk", "windstep", "Way of the Windstep", "Mobility, jump height",
         {"torso": "gi", "hands": "wraps", "headgear": "headband", "cape": "short_cape"},
         {"cloth1": "cloth/sky", "cloth2": "cloth/ivory"}),
    _sub("monk", "iron_body", "Way of Iron Body", "Counters, self-healing",
         {"torso": "gi", "hands": "gauntlets", "legs": "greaves"},
         {"cloth1": "cloth/charcoal", "cloth2": "cloth/rust", "metal": "metal/iron"}),
    # Barbarian
    _sub("barbarian", "berserker", "Path of the Berserker", "Frenzy: reckless extra attacks",
         {"weapon": "axe", "offhand": "hand_axe", "headgear": "wolf_pelt"},
         {"cloth1": "leather/dark", "cloth2": "cloth/crimson", "leather": "leather/red", "blade": "metal/dark",
          "paint": "paint/crimson"}),
    _sub("barbarian", "earthshaker", "Earthshaker", "Ground slams that crack the terrain",
         {"weapon": "maul", "headgear": "horned_helm", "hands": "gauntlets"},
         {"cloth1": "leather/brown", "cloth2": "cloth/sand", "metal": "metal/iron", "blade": "metal/iron",
          "magic": "magic/earth", "paint": "paint/ochre"}),
    _sub("barbarian", "spiritwarden", "Spiritwarden", "Ancestral spirits shield allies",
         {"weapon": "axe", "offhand": "spirit_totem", "headgear": "antlers"},
         {"cloth1": "hair/silver", "cloth2": "cloth/teal", "magic": "magic/spirit", "paint": "paint/jade"}),
    # Sorcerer
    _sub("sorcerer", "draconic", "Draconic Bloodline", "Dragon scales, elemental damage",
         {"torso": "drake_robe", "headgear": "circlet", "marks": "scales"},
         {"cloth1": "cloth/crimson", "cloth2": "metal/bronze", "metal": "metal/gold", "magic": "magic/fire"}),
    _sub("sorcerer", "chaos", "Chaos Bloodline", "Wild magic surges",
         {"torso": "motley_robe", "weapon": "wild_motes"},
         {"cloth1": "cloth/teal", "cloth2": "cloth/crimson", "trim": "metal/gold", "magic": "magic/wild"}),
    _sub("sorcerer", "runeborn", "Runeborn", "Lays glowing runes on the ground",
         {"weapon": "rune_staff", "marks": "runes"},
         {"cloth1": "cloth/slate", "cloth2": "metal/gold", "wood": "wood/dark", "magic": "magic/rune"}),
]}

_BASE_COLOURS = {"eyes": "eyes/dark", "leather": "leather/brown", "wood": "wood/oak", "metal": "metal/steel",
                 "trim": "metal/gold", "magic": "magic/arcane", "horn": "horn/ivory", "hair": "hair/brown"}


@dataclass
class Look:
    """The player's picks. Anything left as None falls back to the race default; `colours` recolours any slot
    (e.g. {"cloth1": "cloth/crimson"}), `gear` swaps or removes kit pieces (None removes, e.g. a helmet)."""
    body_type: str = "masculine"           # masculine | feminine: the silhouette, lashes and lips
    skin: str | None = None
    hair: str | None = None
    hair_colour: str | None = None
    beard: str | None = None
    horns: str | None = None
    horn: str | None = None
    eyes: str | None = None
    marks: str | None = None               # freckles | scar | warpaint | tattoo | stripes
    paint: str | None = None               # colour for painted marks
    colours: dict[str, str] = field(default_factory=dict)
    gear: dict[str, str | None] = field(default_factory=dict)


class CreatorError(ValueError):
    pass


def make_character(name: str, race: str, cls: str, look: Look | None = None, *, id: str | None = None,
                   tags: tuple[str, ...] = (), subclass: str | None = None) -> Recipe:
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
    if lk.body_type not in BODY_TYPES:
        raise CreatorError(f"body type must be one of {BODY_TYPES}")
    if lk.marks is not None and lk.marks not in MARKS:
        raise CreatorError(f"marks must be one of {MARKS}")
    if lk.paint is not None and lk.paint not in PAINTS:
        raise CreatorError(f"paint must be one of {PAINTS}")
    build = r.build + ("_f" if lk.body_type == "feminine" else "")

    sub = SUBCLASSES.get(subclass) if subclass else None
    if subclass and (sub is None or sub.cls != cls):
        raise CreatorError(f"{subclass!r} is not a {k.name} subclass")
    layers: dict[str, str] = {"body": r.body, **k.gear}
    for layer, variant in (sub.gear.items() if sub else ()):
        if variant is None:
            layers.pop(layer, None)
        else:
            layers[layer] = variant
    for layer, variant in lk.gear.items():
        if variant is None:
            layers.pop(layer, None)
        else:
            layers[layer] = variant
    marks = lk.marks if lk.marks is not None else r.default.get("marks")
    for layer, variant in (("hair", hair), ("beard", beard), ("horns", horns), ("tail", r.tail),
                           ("marks", marks)):
        if variant:
            layers[layer] = variant

    palette = dict(_BASE_COLOURS)
    palette.update(k.colours)
    if sub:
        palette.update(sub.colours)
    palette["skin"] = f"skin/{skin}"
    palette["hair"] = f"hair/{lk.hair_colour or r.default.get('hair_colour', 'brown')}"
    palette["eyes"] = f"eyes/{lk.eyes or r.default.get('eyes', 'dark')}"
    palette["horn"] = f"horn/{lk.horn or r.default.get('horn', 'ivory')}"
    paint = lk.paint or r.default.get("paint")             # the player's paint, else the race's, else the kit's
    kit_paint = palette.pop("paint", "paint/woad")
    palette["paint"] = f"paint/{paint}" if paint else kit_paint
    palette.update(lk.colours)

    rid = id or "".join(ch for ch in name.lower().replace(" ", "_") if ch.isalnum() or ch == "_")
    tag_list = [race, cls, *([subclass] if subclass else []), *tags]
    return Recipe(rid, name, build, layers, palette, tag_list, race=race, cls=cls)


def options() -> dict:
    """Everything the phone creator needs to offer choices (exported as races.json / classes.json)."""
    return {
        "bodyTypes": BODY_TYPES,
        "marks": MARKS,
        "paints": PAINTS,
        "races": {r.id: {"name": r.name, "build": r.build, "body": r.body, "skins": r.skins, "hair": r.hair,
                         "hairColours": HAIR_COLOURS if r.hair else [], "beards": r.beards, "horns": r.horns,
                         "hornsRequired": r.horns_required, "hornColours": r.horn_colours if r.horns else [],
                         "tail": r.tail, "eyes": EYES, "default": r.default, "traits": r.traits}
                  for r in RACES.values()},
        "classes": {k.id: {"name": k.name, "role": k.role, "gear": k.gear, "colours": k.colours,
                           "subclasses": [s.id for s in SUBCLASSES.values() if s.cls == k.id]}
                    for k in CLASSES.values()},
        "subclasses": {s.id: {"name": s.name, "class": s.cls, "role": s.role, "gear": s.gear, "colours": s.colours,
                              "companion": s.companion} for s in SUBCLASSES.values()},
    }
