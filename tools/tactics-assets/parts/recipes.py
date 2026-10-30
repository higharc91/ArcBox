"""Ready-made characters assembled from the parts: the sample party, ten class starters and a set of enemies.
These double as tests that the parts compose, and as presets the character creator can start from."""
from __future__ import annotations

from .characters import Recipe
from .creator import Look, make_character

_BASE = {"eyes": "eyes/dark", "leather": "leather/brown", "wood": "wood/oak", "metal": "metal/steel",
         "trim": "metal/gold", "magic": "magic/arcane", "horn": "horn/ivory",
         "paint": "paint/woad"}
# Enemies are authored directly as recipes: they aren't player races, so they skip the creator's rules.


def _r(id: str, name: str, build: str, layers: dict[str, str], palette: dict[str, str], *tags: str) -> Recipe:
    return Recipe(id, name, build, layers, {**_BASE, **palette}, list(tags))


# The sample party: each is just a name, a race, a class and the player's look, exactly what the creator stores.
PARTY = [
    make_character("Kara", "human", "fighter", Look(
        body_type="feminine", skin="fair", hair="cropped", hair_colour="auburn", eyes="blue",
        colours={"cloth1": "cloth/crimson", "cloth2": "cloth/slate"},
        gear={"torso": "plate", "legs": "greaves", "feet": "sabatons", "hands": "gauntlets",
              "offhand": "kite_shield", "cape": "short_cape", "headgear": "open_helm"}), tags=("party",)),
    make_character("Aldric", "dwarf", "cleric", Look(
        skin="tan", hair="short", hair_colour="copper", beard="full",
        colours={"cloth1": "cloth/ivory", "cloth2": "cloth/wine"}), tags=("party",)),
    make_character("Wren", "halfling", "rogue", Look(
        skin="olive", hair="messy", hair_colour="brown", eyes="green",
        colours={"cloth1": "cloth/forest", "cloth2": "cloth/charcoal"}), tags=("party",)),
    make_character("Ilyra", "elf", "wizard", Look(
        body_type="feminine", skin="porcelain", hair="long", hair_colour="platinum", eyes="violet",
        colours={"cloth1": "cloth/royal_purple", "cloth2": "cloth/navy", "magic": "magic/frost"}), tags=("party",)),
    make_character("Tamsin", "half_elf", "ranger", Look(
        body_type="feminine", skin="fair", marks="freckles", hair="ponytail", hair_colour="auburn", eyes="green",
        colours={"cloth1": "cloth/moss", "cloth2": "cloth/sand"}), tags=("party",)),
    make_character("Sefa", "tiefling", "warlock", Look(
        body_type="feminine", skin="tiefling", marks="stripes", paint="kohl", hair="long", hair_colour="black", horns="curled", horn="ram", eyes="gold",
        gear={"legs": "skirt", "hands": "bare"}), tags=("party",)),
]

# One starter per class, each on a different race to show the combinations.
CLASSES = [
    make_character("Fighter", "half_orc", "fighter", Look(marks="warpaint", paint="woad", beard="goatee"),
                   id="class_fighter", tags=("class",)),
    make_character("Rogue", "tiefling", "rogue", Look(horns="small", hair_colour="violet"), id="class_rogue",
                   tags=("class",)),
    make_character("Wizard", "gnome", "wizard", Look(beard="full", hair_colour="white"), id="class_wizard",
                   tags=("class",)),
    make_character("Cleric", "human", "cleric", Look(body_type="feminine", skin="brown", hair="curly",
                                                      hair_colour="black"),
                   id="class_cleric", tags=("class",)),
    make_character("Ranger", "elf", "ranger", Look(body_type="feminine", skin="olive", hair="braids",
                                                    hair_colour="chestnut"),
                   id="class_ranger", tags=("class",)),
    make_character("Paladin", "dragonborn", "paladin", Look(skin="dragon_silver"), id="class_paladin",
                   tags=("class",)),
    make_character("Bard", "half_elf", "bard", Look(body_type="feminine", hair="twintails", hair_colour="copper"),
                   id="class_bard",
                   tags=("class",)),
    make_character("Druid", "halfling", "druid", Look(skin="deep", hair="braids", hair_colour="white"),
                   id="class_druid", tags=("class",)),
    make_character("Warlock", "human", "warlock", Look(body_type="feminine", skin="porcelain", hair="long",
                                                        hair_colour="crimson", eyes="red", marks="tattoo",
                                                        paint="crimson"), id="class_warlock", tags=("class",)),
    make_character("Monk", "human", "monk", Look(skin="olive", hair="topknot", hair_colour="black", beard="moustache"),
                   id="class_monk",
                   tags=("class",)),
]

ENEMIES = [
    _r("goblin_cutthroat", "Goblin Cutthroat", "short",
       {"body": "goblin", "headgear": "bandana", "torso": "rags", "legs": "shorts", "feet": "sandals",
        "weapon": "dagger", "hands": "bare"},
       {"skin": "skin/goblin", "hair": "hair/black", "cloth1": "cloth/rust", "cloth2": "cloth/charcoal",
        "eyes": "eyes/gold"}, "enemy"),
    _r("goblin_spear", "Goblin Spear", "short",
       {"body": "goblin", "hair": "messy", "torso": "vest", "legs": "shorts", "feet": "sandals", "weapon": "spear",
        "hands": "bare", "offhand": "buckler"},
       {"skin": "skin/goblin", "hair": "hair/brown", "cloth1": "cloth/moss", "cloth2": "cloth/charcoal",
        "eyes": "eyes/gold", "metal": "metal/iron"}, "enemy"),
    _r("goblin_shaman", "Goblin Shaman", "short",
       {"body": "goblin", "hair": "long", "torso": "robe", "legs": "skirt", "feet": "sandals", "weapon": "staff",
        "hands": "bare"},
       {"skin": "skin/goblin", "hair": "hair/white", "cloth1": "cloth/wine", "cloth2": "cloth/ochre",
        "eyes": "eyes/red", "magic": "magic/fire"}, "enemy"),
    _r("orc_brute", "Orc Brute", "large",
       {"body": "goblin", "hair": "mohawk", "torso": "rags", "legs": "pants", "feet": "boots", "weapon": "axe",
        "hands": "gloves"},
       {"skin": "skin/orc", "hair": "hair/black", "cloth1": "cloth/rust", "cloth2": "cloth/charcoal",
        "eyes": "eyes/red", "metal": "metal/iron"}, "enemy"),
    _r("skeleton", "Skeleton Soldier", "standard",
       {"body": "skeleton", "torso": "rags", "legs": "shorts", "weapon": "sword", "hands": "bare", "offhand": "buckler",
        "headgear": "open_helm"},
       {"skin": "skin/bone", "hair": "hair/black", "cloth1": "cloth/charcoal", "cloth2": "cloth/slate",
        "eyes": "eyes/red", "metal": "metal/iron"}, "enemy"),
    _r("bandit", "Bandit", "standard",
       {"body": "human", "hair": "short", "beard": "short", "headgear": "bandana", "torso": "vest", "legs": "pants",
        "feet": "boots", "weapon": "axe", "hands": "gloves"},
       {"skin": "skin/tan", "hair": "hair/black", "cloth1": "cloth/crimson", "cloth2": "cloth/charcoal"}, "enemy"),
    _r("black_knight", "Black Knight", "large",
       {"body": "human", "hair": "cropped", "headgear": "great_helm", "torso": "plate", "legs": "greaves",
        "feet": "sabatons", "weapon": "greatsword", "hands": "gauntlets", "cape": "cloak"},
       {"skin": "skin/fair", "hair": "hair/black", "cloth1": "cloth/wine", "cloth2": "cloth/charcoal",
        "metal": "metal/dark", "trim": "metal/bronze"}, "enemy"),
    _r("cultist", "Cultist", "standard",
       {"body": "human", "hair": "short", "headgear": "hood", "torso": "robe", "legs": "skirt", "feet": "sandals",
        "weapon": "wand", "hands": "bare", "offhand": "orb"},
       {"skin": "skin/ash", "hair": "hair/black", "cloth1": "cloth/charcoal", "cloth2": "cloth/wine",
        "magic": "magic/blood", "eyes": "eyes/red"}, "enemy"),
]

ALL = PARTY + CLASSES + ENEMIES
