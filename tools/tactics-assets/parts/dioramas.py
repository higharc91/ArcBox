"""Preview dioramas: small maps built from the terrain and prop catalogues with characters standing on them. They
exist to judge every texture and prop together and in context, the way the game will use them.

Maps are written as two grids of text: one character per tile for the material, one digit per tile for the height
(in 16 px steps). Row = j (towards the viewer's lower-left), column = i (towards the lower-right).
"""
from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache

import numpy as np
from PIL import Image

from arcpix import iso
from . import characters as C
from . import props as PR
from . import terrain as TR
from .recipes import ALL
from .rig import ANIMS, ANCHOR


@lru_cache(maxsize=None)
def _frames(tex_id: str) -> list:
    return next(x for x in TR.CATALOGUE if x.id == tex_id).frames()


@lru_cache(maxsize=None)
def _tex(tex_id: str, variant: str | None, frame: int) -> np.ndarray:
    t = next(x for x in TR.CATALOGUE if x.id == tex_id)
    v = t.variants[variant] if variant else next(iter(t.variants.values()))
    fr = _frames(tex_id)
    return np.asarray(fr[frame % len(fr)].render(TR.ramps(v))).astype(np.float32)


def tex(tex_id: str, variant: str | None = None, frame: int = 0) -> np.ndarray:
    return _tex(tex_id, variant, frame)


@lru_cache(maxsize=None)
def _prop_frames(pid: str, variant: str | None) -> list[Image.Image]:
    p = next(x for x in PR.CATALOGUE if x.id == pid)
    v = p.variants[variant] if variant else next(iter(p.variants.values()))
    return [f.render(PR.ramps(v)) for f in p.frames()]


def prop(pid: str, i: float, j: float, variant: str | None = None, frame: int = 0) -> iso.Sprite:
    fr = _prop_frames(pid, variant)
    img = fr[frame % len(fr)]
    return iso.Sprite(img, (img.width // 2, img.height - 1), i, j, shadow=False)


def unit(rid: str, i: float, j: float, facing: str = "front", mirror: bool = False, anim: str = "idle",
         k: int = 0) -> iso.Sprite:
    r = next(x for x in ALL if x.id == rid)
    img = C.compose(r, facing, ANIMS[anim][2][k]).render(r.ramps())
    if mirror:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
        return iso.Sprite(img, (img.width - 1 - ANCHOR[0], ANCHOR[1]), i, j)
    return iso.Sprite(img, ANCHOR, i, j)


def beast(bid: str, i: float, j: float, variant: str | None = None, facing: str = "front", mirror: bool = False,
          k: int = 0) -> iso.Sprite:
    from . import beasts as B
    b = next(x for x in B.CATALOGUE if x.id == bid)
    sh = B.sheet(b, variant or next(iter(b.variants)))
    img = sh.frames[k + (len(B.frame_poses()) if facing == "back" else 0)]
    if mirror:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
        return iso.Sprite(img, (img.width - 1 - B.ANCHOR[0], B.ANCHOR[1]), i, j)
    return iso.Sprite(img, B.ANCHOR, i, j)


@dataclass
class Mat:
    """One map legend entry: which textures a tile uses."""
    top: tuple[str, str | None]
    side: tuple[str, str | None]
    lip: tuple[str, str | None] | None = None
    sink: int = 0
    side_of_top: bool = False                 # liquids: the side is the top texture, darkened


def build_scene(mats: dict[str, Mat], terrain: list[str], heights: list[str], *, frame: int = 0,
                decals: dict[str, tuple[float, list[tuple[str, str | None]]]] | None = None,
                seed: int = 1) -> iso.Scene:
    rng = np.random.default_rng(seed)
    d, w = len(terrain), len(terrain[0])
    s = iso.Scene(w, d)
    for j in range(d):
        for i in range(w):
            ch = terrain[j][i]
            if ch == ".":
                continue
            m = mats[ch]
            top = tex(*m.top, frame=frame)
            side = top[:32] * np.array([0.78, 0.84, 0.95, 1.0], np.float32) if m.side_of_top else tex(*m.side)
            lip = tex(*m.lip) if m.lip else None
            decal = None
            if decals and ch in decals:
                p, pool = decals[ch]
                if rng.random() < p:
                    decal = tex(*pool[int(rng.integers(len(pool)))])
            s.cells[(i, j)] = iso.Cell(int(heights[j][i]), top, side, lip=lip, decal=decal, sink=m.sink)
    return s


# ------------------------------------------------------------------------------------------------------- scenes
FLOWERS = [(f"flowers_{k}", v) for k in range(3) for v in ("red", "yellow", "white", "blue")]


def ashen_ford(frame: int = 0) -> iso.Scene:
    """Grassland with a rocky bluff, a river with a plank bridge, a dirt road, wildflowers and a camp."""
    mats = {
        "g": Mat(("grass", "green"), ("dirt_side", "brown"), ("grass_lip", "green")),
        "G": Mat(("grass", "green"), ("cliff_side", "grey"), ("grass_lip", "green")),
        "R": Mat(("rock", "grey"), ("cliff_side", "grey")),
        "p": Mat(("path", None), ("dirt_side", "brown")),
        "s": Mat(("sand", None), ("dirt_side", "brown"), sink=2),
        "w": Mat(("water", "clear"), ("water", "clear"), sink=6, side_of_top=True),
        "b": Mat(("planks", "oak"), ("plank_side", None)),
    }
    terrain = ["ggggppgGGRRR",
               "ggggppgGGRRR",
               "ggggppgGGGGG",
               "ggggppgGGGGG",
               "ggggppgggggg",
               "sssgppggggss",
               "wwwwbbwwwwww",
               "wwwwbbwwwwww",
               "sssgppggggss",
               "ggggppgggggg"]
    heights = ["222222244666",
               "222222244666",
               "222222244444",
               "222222244444",
               "222222222222",
               "222222222222",
               "222222222222",
               "222222222222",
               "222222222222",
               "222222222222"]
    ik = (frame // 4) % 2                      # idle breathing, slower than the water
    s = build_scene(mats, terrain, heights, frame=frame, seed=7,
                    decals={"g": (0.2, FLOWERS), "p": (0.35, [("pebbles_0", None), ("pebbles_1", None)])})
    s.sprites += [
        prop("tree", 0, 0), prop("tree", 2, 0, "light"), prop("tree", 0, 2, "autumn"), prop("bush", 1, 1),
        prop("pine", 10, 0), prop("pine", 11, 1), prop("rock_mossy", 8, 3), prop("rock_medium", 7, 0),
        prop("tall_grass", 3, 1), prop("tall_grass", 9, 4), prop("campfire", 2, 3, frame=frame),
        prop("tent", 0, 4), prop("crate", 6, 5), prop("barrel", 3, 5), prop("signpost", 6, 4),
        prop("fence", 2, 9), prop("banner", 10, 9, "goblin", frame=frame), prop("rock_small", 11, 8),
        prop("bush", 0, 9, "autumn"), prop("reeds", 2, 5),
        unit("kara", 5, 4, k=ik), unit("aldric", 3, 3, k=1 - ik), unit("ilyra", 8, 2, k=ik),
        unit("tamsin", 10, 2, k=1 - ik), unit("wren", 6, 3, k=ik), unit("sefa", 1, 3, k=1 - ik),
        unit("goblin_cutthroat", 4, 9, "back", mirror=True, k=ik), unit("goblin_spear", 7, 9, "back", k=1 - ik),
        unit("orc_brute", 5, 8, "back", k=ik), unit("goblin_shaman", 9, 9, "back", mirror=True, k=1 - ik),
    ]
    return s


def frostpeak(frame: int = 0) -> iso.Scene:
    """Winter heights: snowfields over dark cliffs with snow cornices, a frozen tarn and snowy pines."""
    mats = {
        "n": Mat(("snow", None), ("cliff_side", "dark"), ("snow_lip", None)),
        "i": Mat(("ice", None), ("ice", None), sink=3, side_of_top=True),
        "r": Mat(("rock", "dark"), ("cliff_side", "dark"), ("snow_lip", None)),
        "c": Mat(("cobble", "grey"), ("cliff_side", "dark"), ("snow_lip", None)),
    }
    terrain = ["nnnnnnrrrrnn",
               "nnnnnnrrrrnn",
               "nniiiinnnnnn",
               "niiiiinnnccc",
               "nniiinnnnccc",
               "nnnnnnnnnccc",
               "nnnnnnnnnnnn",
               "nnnnnnnnnnnn"]
    heights = ["444446888866",
               "444446888866",
               "443333444444",
               "433333444444",
               "443334444444",
               "444444442222",
               "222222222222",
               "222222222222"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=3,
                    decals={"n": (0.08, [("pebbles_0", None)])})
    s.sprites += [
        prop("pine_snowy", 0, 0), prop("pine_snowy", 2, 0), prop("pine_snowy", 11, 0), prop("pine_snowy", 0, 6),
        prop("pine_snowy", 1, 7), prop("rock_large", 7, 1, "dark"), prop("dead_tree", 5, 5, "grey"),
        prop("tent", 10, 4, "red"), prop("campfire", 9, 5, frame=frame), prop("crate", 11, 5),
        unit("tamsin", 4, 5), unit("kara", 8, 6), unit("class_paladin", 6, 6), unit("ilyra", 3, 6),
        unit("skeleton", 8, 0, mirror=True), unit("skeleton", 9, 1),
        beast("wolf", 1, 3, "frost", mirror=True), beast("wolf", 2, 4, "frost", mirror=True, k=1),
        beast("bear", 10, 7, "polar", "back"),
    ]
    return s


def mirewood(frame: int = 0) -> iso.Scene:
    """A swamp: mud flats, murky channels, moss-lipped banks, dead trees, reeds and mushrooms."""
    mats = {
        "m": Mat(("mud", None), ("dirt_side", "dark"), ("moss_lip", None)),
        "w": Mat(("water", "swamp"), ("water", "swamp"), sink=5, side_of_top=True),
        "g": Mat(("grass", "lush"), ("dirt_side", "dark"), ("moss_lip", None)),
        "b": Mat(("planks", "dark"), ("plank_side", None)),
    }
    terrain = ["ggmmmwwwmmgg",
               "gmmmwwwmmmmg",
               "mmmwwwbbbmmm",
               "mmwwwmmmbmmm",
               "mwwwmmmmbwww",
               "mmwwmmmmmwwm",
               "gmmwwwmmwwmm",
               "ggmmmwwwwmmg"]
    heights = ["332222222233",
               "322222222223",
               "222222333222",
               "222222222222",
               "222222223222",
               "222222222222",
               "322222222222",
               "332222222223"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=11,
                    decals={"m": (0.1, [("puddle", "swamp"), ("bones", None)])})
    s.sprites += [
        prop("dead_tree", 0, 0, "dark"), prop("dead_tree", 11, 0, "dark"), prop("dead_tree", 0, 7, "dark"),
        prop("reeds", 3, 2), prop("reeds", 5, 4), prop("reeds", 9, 3), prop("reeds", 2, 6),
        prop("mushrooms", 1, 1, "violet"), prop("mushrooms", 10, 6), prop("rock_mossy", 11, 7),
        prop("torch", 6, 1, frame=frame), prop("torch", 10, 2, frame=frame), prop("gravestone", 1, 4, "dark"),
        unit("wren", 7, 2), unit("sefa", 6, 3), unit("cultist", 9, 6, mirror=True), unit("goblin_shaman", 7, 6),
        unit("goblin_cutthroat", 10, 5, mirror=True),
        beast("spider", 4, 0, "black"), beast("slime", 8, 7, "green", mirror=True), beast("bat", 3, 5, k=1),
    ]
    return s


def sunscar(frame: int = 0) -> iso.Scene:
    """Desert ruins: dunes, sandstone mesas, flagstone remains of a temple, broken pillars and an old well."""
    mats = {
        "s": Mat(("sand", None), ("sandstone_side", None)),
        "f": Mat(("flagstone", "warm"), ("ashlar_side", "warm")),
        "c": Mat(("cobble", "warm"), ("sandstone_side", None)),
    }
    terrain = ["ssssssssssss",
               "ssffffffssss",
               "ssffffffsscc",
               "ssffffffsscc",
               "ssssffsssscc",
               "ssssssssssss",
               "ssssssssssss",
               "ssssssssssss"]
    heights = ["664444222222",
               "663333332222",
               "443333332244",
               "443333332244",
               "222233222244",
               "222222222222",
               "222222222222",
               "222222222222"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=5,
                    decals={"s": (0.06, [("bones", None), ("pebbles_1", None)]),
                            "f": (0.15, [("cracks", None)])})
    s.sprites += [
        prop("pillar", 2, 1, "warm"), prop("pillar_broken", 7, 1, "warm"), prop("pillar", 2, 3, "warm"),
        prop("pillar_broken", 7, 3, "warm"), prop("rubble", 5, 1, "warm"), prop("well", 10, 6),
        prop("dead_tree", 0, 6, "grey"), prop("rock_medium", 11, 0, "warm"), prop("rock_small", 5, 7, "warm"),
        prop("altar", 4, 2, "holy"), prop("banner", 10, 2, "navy", frame=frame), prop("sack", 9, 7),
        unit("aldric", 5, 3), unit("class_cleric", 4, 4), unit("tamsin", 9, 5),
        unit("bandit", 3, 6, "back", mirror=True), unit("bandit", 6, 7, "back"), unit("bandit_captain", 4, 7, "back"),
        beast("boar", 1, 7, "brown"),
    ]
    return s


def ember_keep(frame: int = 0) -> iso.Scene:
    """A volcanic fortress hall: flagstone and cobble floors, brick and ashlar walls, lava channels, pillars, an
    unholy altar, torches and crystals."""
    mats = {
        "f": Mat(("flagstone", "dark"), ("ashlar_side", "grey")),
        "c": Mat(("cobble", "grey"), ("brick_side", None)),
        "W": Mat(("flagstone", "dark"), ("brick_side", None)),
        "l": Mat(("lava", None), ("lava", None), sink=6, side_of_top=True),
        "r": Mat(("rock", "dark"), ("cliff_side", "dark")),
    }
    terrain = ["WWWWWWWWWWrr",
               "WffffffffWrr",
               "WfffccfffWll",
               "lllfccflllll",
               "lllfccflllll",
               "fffffccfffff",
               "fffffccffffr",
               "rffffccfffrr"]
    heights = ["777777777788",
               "744444444788",
               "744444444722",
               "222444422222",
               "222444422222",
               "444444444444",
               "444444444444",
               "444444444444"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=9,
                    decals={"f": (0.12, [("scorch", None), ("cracks", None), ("blood", None)])})
    s.sprites += [
        prop("pillar", 2, 1, "dark"), prop("pillar", 7, 1, "dark"), prop("pillar_broken", 2, 5, "dark"),
        prop("altar", 5, 1, "dark"), prop("torch", 1, 1, frame=frame), prop("torch", 8, 1, frame=frame),
        prop("crystals", 10, 0, "violet"), prop("crystals", 11, 1, "violet"), prop("rubble", 9, 6, "dark"),
        prop("chest", 8, 5), prop("banner", 3, 1, "crimson", frame=frame),
        unit("black_knight", 5, 2, mirror=True), unit("cultist", 3, 2), unit("cultist", 7, 2, mirror=True),
        unit("kara", 5, 5, "back"), unit("sefa", 3, 6, "back"), unit("class_wizard", 7, 6, "back", mirror=True),
        beast("drake", 9, 5, "red", mirror=True), beast("slime", 1, 6, "magma"),
    ]
    return s


SCENES = {"ashen-ford": ashen_ford, "frostpeak": frostpeak, "mirewood": mirewood, "sunscar": sunscar,
          "ember-keep": ember_keep}
