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
def _prop_anchor(pid: str) -> tuple[int, int]:
    p = next(x for x in PR.CATALOGUE if x.id == pid)
    return p.anchor_of(_prop_canvases(pid)[0])


@lru_cache(maxsize=None)
def _prop_canvases(pid: str) -> list:
    return next(x for x in PR.CATALOGUE if x.id == pid).frames()


@lru_cache(maxsize=None)
def _prop_frames(pid: str, variant: str | None) -> list[Image.Image]:
    p = next(x for x in PR.CATALOGUE if x.id == pid)
    v = p.variants[variant] if variant else next(iter(p.variants.values()))
    return [f.render(PR.ramps(v)) for f in _prop_canvases(pid)]


def prop(pid: str, i: float, j: float, variant: str | None = None, frame: int = 0, mirror: bool = False) -> iso.Sprite:
    fr = _prop_frames(pid, variant)
    img = fr[frame % len(fr)]
    ax, ay = _prop_anchor(pid)
    if mirror:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
        ax = img.width - 1 - ax
    return iso.Sprite(img, (ax, ay), i, j, shadow=False)


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
    foam: str | None = None                   # liquids: the ambient shore_foam variant to lap at the banks


@lru_cache(maxsize=None)
def _foam(variant: str, frame: int) -> np.ndarray:
    """The ambient set's shoreline foam (a water decal, bank on its v = 0 edge) as float RGBA."""
    from . import ambient as AM
    a = next(x for x in AM.CATALOGUE if x.id == "shore_foam")
    fr = a.make()
    return np.asarray(fr[frame % len(fr)].render(AM.palette(AM.SLOTS, a.variants[variant]))).astype(np.float32)


def _over(dst: np.ndarray | None, src: np.ndarray) -> np.ndarray:
    if dst is None:
        return src.copy()
    al = src[..., 3:4] / 255
    out = dst * (1 - al) + src * al
    out[..., 3] = np.maximum(dst[..., 3], src[..., 3])
    return out


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
            side = (top[:32] * np.array([0.78, 0.84, 0.95, 1.0], np.float32) if m.side_of_top
                    else tex(*m.side, frame=frame))
            lip = tex(*m.lip) if m.lip else None
            decal = None
            if decals and ch in decals:
                p, pool = decals[ch]
                if rng.random() < p:
                    decal = tex(*pool[int(rng.integers(len(pool)))])
            s.cells[(i, j)] = iso.Cell(int(heights[j][i]), top, side, lip=lip, decal=decal, sink=m.sink)
    # foam laps wherever a liquid meets a bank: the decal's v = 0 edge, turned to face each banked side
    for (i, j), c in s.cells.items():
        m = mats[terrain[j][i]]
        if not m.foam:
            continue
        f = _foam(m.foam, frame)
        for (di, dj), turn in (((0, -1), lambda a: a), ((-1, 0), lambda a: a.transpose(1, 0, 2)),
                               ((0, 1), lambda a: a[::-1]), ((1, 0), lambda a: a.transpose(1, 0, 2)[:, ::-1])):
            n = (i + di, j + dj)
            if n in s.cells and not mats[terrain[n[1]][n[0]]].foam and s.top_z(*n) > s.top_z(i, j) - 4:
                c.decal = _over(c.decal, turn(f))
    return s


# ------------------------------------------------------------------------------------------------------- scenes
FLOWERS = [(f"flowers_{k}", v) for k in range(3) for v in ("red", "yellow", "white", "blue")]


def ashen_ford(frame: int = 0, units: bool = True) -> iso.Scene:
    """Grassland with a rocky bluff, a river with a plank bridge, a dirt road, wildflowers and a camp."""
    mats = {
        "g": Mat(("grass", "green"), ("dirt_side", "brown"), ("grass_lip", "green")),
        "G": Mat(("grass", "green"), ("cliff_side", "grey"), ("grass_lip", "green")),
        "R": Mat(("rock", "grey"), ("cliff_side", "grey")),
        "p": Mat(("path", None), ("dirt_side", "brown")),
        "s": Mat(("sand", None), ("dirt_side", "brown"), sink=2),
        "w": Mat(("water", "clear"), ("water", "clear"), sink=6, side_of_top=True, foam="clear"),
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
    ]
    if not units:
        return s
    s.sprites += [
        unit("kara", 5, 4, k=ik), unit("aldric", 3, 3, k=1 - ik), unit("ilyra", 8, 2, k=ik),
        unit("tamsin", 10, 2, k=1 - ik), unit("wren", 6, 3, k=ik), unit("sefa", 1, 3, k=1 - ik),
        unit("goblin_cutthroat", 4, 9, "back", mirror=True, k=ik), unit("goblin_spear", 7, 9, "back", k=1 - ik),
        unit("orc_brute", 5, 8, "back", k=ik), unit("goblin_shaman", 9, 9, "back", mirror=True, k=1 - ik),
    ]
    return s


def frostpeak(frame: int = 0) -> iso.Scene:
    """Winter heights: soft snow drifts over dark cliffs with snow cornices, a frozen tarn walled in glacier ice,
    frosted grass on the lower shelf, ice spikes, snowed-on boulders and a crag, snowy pines."""
    mats = {
        "n": Mat(("snow", None), ("cliff_side", "dark"), ("snow_lip", None)),
        "i": Mat(("ice", None), ("ice_side", "glacier"), sink=3),
        "r": Mat(("rock", "dark"), ("cliff_side", "dark"), ("snow_lip", None)),
        "c": Mat(("cobble", "grey"), ("cliff_side", "dark"), ("snow_lip", None)),
        "g": Mat(("grass", "frost"), ("ice_side", "glacier"), ("snow_lip", None)),
    }
    terrain = ["nnnnnnrrrrnn",
               "nnnnnnrrrrnn",
               "nniiiinnnnnn",
               "niiiiinnnccc",
               "nniiinnnnccc",
               "nnnnnnnnnccc",
               "gggggggggggg",
               "gggggggggggg"]
    heights = ["444446888866",
               "444446888866",
               "443333444444",
               "433333444444",
               "443334444444",
               "444444442222",
               "222222222222",
               "222222222222"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=3,
                    decals={"n": (0.06, [("pebbles_0", None)]), "g": (0.08, [("pebbles_1", None)])})
    s.sprites += [
        prop("pine_snowy", 0, 0), prop("pine_snowy", 2, 0), prop("pine_snowy", 11, 0), prop("pine_snowy", 0, 6),
        prop("crag", 7, 0, "snowy"), prop("boulder", 9, 1, "snowy"), prop("ice_spikes", 4, 2),
        prop("ice_spikes", 0, 3), prop("rock_cluster", 6, 4, "snowy"), prop("dead_tree", 7, 5, "grey"),
        prop("tent", 10, 4, "red"), prop("campfire", 9, 5, frame=frame), prop("crate", 11, 5),
        prop("fence_stone", 2, 7, "snowy"), prop("fence_stone", 3, 7, "snowy"),
        unit("tamsin", 4, 5), unit("kara", 8, 6), unit("class_paladin", 6, 6), unit("ilyra", 3, 6),
        unit("skeleton", 8, 2, mirror=True), unit("skeleton", 10, 2),
        beast("wolf", 1, 3, "frost", mirror=True), beast("wolf", 2, 5, "frost", mirror=True, k=1),
        beast("bear", 10, 7, "polar", "back"),
    ]
    return s


def mirewood(frame: int = 0) -> iso.Scene:
    """A swamp: still bog water under duckweed, mossy forest-floor hummocks, cypresses hung with moss, lily pads,
    reeds, glowing toadstools and will-o'-wisps over the channels."""
    mats = {
        "m": Mat(("mud", None), ("dirt_side", "dark"), ("moss_lip", None)),
        "w": Mat(("bog", "duckweed"), ("water", "bog"), sink=5, side_of_top=True, foam="swamp"),
        "g": Mat(("forest_floor", "mossy"), ("dirt_side", "dark"), ("moss_lip", None)),
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
        prop("swamp_tree", 0, 1), prop("swamp_tree", 11, 0), prop("dead_tree", 0, 7, "dark"),
        prop("swamp_tree", 11, 7), prop("reeds", 3, 2), prop("reeds", 5, 4), prop("reeds", 9, 3),
        prop("reeds", 2, 6), prop("lily_pads", 5, 1, "pink"), prop("lily_pads", 3, 4, "white"),
        prop("lily_pads", 9, 6, "pink"), prop("mushroom_cluster", 1, 1, "glow"),
        prop("mushroom_cluster", 10, 6, "brown"), prop("rock_mossy", 10, 1),
        prop("will_o_wisp", 6, 0, "blue", frame=frame), prop("will_o_wisp", 2, 5, "green", frame=frame + 3),
        prop("torch", 6, 1, frame=frame), prop("torch", 10, 2, frame=frame), prop("gravestone", 1, 4, "dark"),
        prop("skull_pike", 7, 7), prop("fern", 0, 2), prop("fern", 11, 5),
        unit("wren", 7, 2), unit("sefa", 6, 3), unit("cultist", 9, 5, mirror=True), unit("goblin_shaman", 7, 5),
        unit("goblin_cutthroat", 10, 4, mirror=True),
        beast("spider", 4, 0, "black"), beast("slime", 8, 7, "green", mirror=True), beast("bat", 4, 6, k=1),
    ]
    return s


def sunscar(frame: int = 0) -> iso.Scene:
    """Desert ruins: wind-carved dunes, ochre sandstone mesas and pavement, the walls and arch of a fallen temple,
    a tomb, cacti, a great beast's ribs bleaching in the sun and an old well."""
    mats = {
        "s": Mat(("dunes", "gold"), ("sandstone_side", "ochre")),
        "f": Mat(("sandstone", "ochre"), ("sandstone_side", "ochre")),
        "c": Mat(("sandstone", "red"), ("sandstone_side", "red")),
        "p": Mat(("flagstone", "warm"), ("ashlar_side", "warm")),
    }
    terrain = ["ssssssssssss",
               "ssppppppssss",
               "ssppppppsscc",
               "ssppppppsscc",
               "ssssppssffcc",
               "ssssssssffss",
               "ssssssssssss",
               "ssssssssssss"]
    heights = ["664444222222",
               "663333332266",
               "443333332266",
               "443333332266",
               "222233224422",
               "222222224422",
               "222222222222",
               "222222222222"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=5,
                    decals={"s": (0.05, [("bones", None), ("pebbles_1", None)]),
                            "p": (0.15, [("cracks", None)])})
    s.sprites += [
        prop("ruin_arch", 4, 1, "sandstone"), prop("ruin_wall", 2, 1, "sandstone"),
        prop("ruin_wall", 7, 1, "sandstone"), prop("pillar_broken", 7, 3, "warm"), prop("pillar", 2, 3, "warm"),
        prop("tomb", 6, 2, "sandstone"), prop("rubble", 5, 3, "warm"), prop("well", 10, 6),
        prop("cactus", 0, 5), prop("cactus", 11, 0), prop("cactus_pear", 3, 6), prop("cactus_pear", 9, 7),
        prop("ribcage", 1, 7), prop("rock_cluster", 11, 4, "sandstone"), prop("crag", 0, 2, "sandstone"),
        prop("banner", 10, 2, "navy", frame=frame), prop("sack", 9, 6),
        unit("aldric", 5, 4), unit("class_cleric", 4, 4), unit("tamsin", 9, 5),
        unit("bandit", 3, 5, "back", mirror=True), unit("bandit", 6, 7, "back"), unit("bandit_captain", 4, 7, "back"),
        beast("boar", 2, 7, "brown"),
    ]
    return s


def greenhollow(frame: int = 0) -> iso.Scene:
    """Deep forest: leaf-litter and needle floors under layered broadleaf crowns and pines, mossy boulders, ferns
    and toadstools, a brook with foam at its banks, and a ruined shrine on a rise."""
    mats = {
        "f": Mat(("forest_floor", "leafy"), ("dirt_side", "brown"), ("grass_lip", "autumn")),
        "n": Mat(("forest_needles", "pine"), ("dirt_side", "dark"), ("moss_lip", None)),
        "g": Mat(("grass", "lush"), ("dirt_side", "brown"), ("grass_lip", "lush")),
        "r": Mat(("flagstone", "grey"), ("cliff_side", "grey"), ("moss_lip", None)),
        "w": Mat(("water", "clear"), ("water", "clear"), sink=6, side_of_top=True, foam="clear"),
        "b": Mat(("planks", "oak"), ("plank_side", None)),
    }
    terrain = ["nnnnnffffrrr",
               "nnnnfffffrrr",
               "nnnffffggggg",
               "wwwwwbbwwwww",
               "ffffgggggggf",
               "fffggggggfff",
               "ffffgggfffff",
               "ffffffffffff"]
    heights = ["444443333555",
               "444433333555",
               "444333333333",
               "222222222222",
               "222222222222",
               "222222222222",
               "222222222222",
               "222222222222"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=13,
                    decals={"g": (0.2, FLOWERS), "f": (0.1, [("leaves_0", "autumn"), ("leaves_1", "red")])})
    s.sprites += [
        prop("pine", 0, 0), prop("pine", 2, 0), prop("pine", 1, 2), prop("tree", 5, 0), prop("tree", 7, 1, "autumn"),
        prop("tree", 0, 7, "light"), prop("tree", 11, 6, "autumn"), prop("tree_fruit", 3, 7),
        prop("ruin_arch", 10, 0, "grey"), prop("shrine", 11, 1), prop("ruin_wall", 9, 1, "grey"),
        prop("boulder", 4, 2, "grey"), prop("rock_mossy", 8, 5), prop("fern", 3, 1), prop("fern", 6, 2),
        prop("fern", 1, 5, "autumn"), prop("mushroom_cluster", 2, 4, "red"), prop("mushroom_cluster", 9, 7),
        prop("stump", 10, 4), prop("log", 6, 6), prop("bush", 5, 7, "autumn"), prop("lantern", 7, 2),
        unit("wren", 5, 4), unit("tamsin", 4, 5), unit("class_druid", 7, 4),
        unit("goblin_spear", 3, 1, mirror=True), unit("goblin_cutthroat", 8, 2, mirror=True),
        beast("wolf", 6, 1, mirror=True), beast("boar", 9, 5, "brown", mirror=True),
    ]
    return s


def highward(frame: int = 0) -> iso.Scene:
    """A castle courtyard: worn pavers inside ashlar walls, a gatehouse step, iron railings round a raised garden,
    a training ground (dummies, an archery butt) and a siege line of barricades and mantlets at the breach."""
    mats = {
        "p": Mat(("pavers", "grey"), ("ashlar_side", "grey")),
        "W": Mat(("pavers", "dark"), ("ashlar_side", "grey")),
        "g": Mat(("grass", "green"), ("ashlar_side", "grey"), ("grass_lip", "green")),
        "c": Mat(("cobble", "grey"), ("ashlar_side", "grey")),
        "d": Mat(("dirt", "brown"), ("dirt_side", "brown")),
    }
    terrain = ["WWWWWWWWWWWW",
               "WggggppcccWW",
               "WggggppcccWW",
               "WpppppppcccW",
               "WpppppppdddW",
               "WpppppppdddW",
               "WpppppppdddW",
               "Wppppppppppp",
               "cccccccccccc"]
    heights = ["777777777777",
               "733332222277",
               "733332222277",
               "722222222224",
               "722222222224",
               "722222222224",
               "722222222224",
               "722222222222",
               "111111111111"]
    s = build_scene(mats, terrain, heights, frame=frame, seed=17,
                    decals={"g": (0.25, FLOWERS), "p": (0.06, [("cracks", None)]), "d": (0.15, [("pebbles_0", None)])})
    s.sprites += [
        prop("tree", 2, 1, "light"), prop("fence_iron", 1, 2, "iron"), prop("fence_iron", 2, 2, "iron"),
        prop("fence_iron", 3, 2, "iron"), prop("well", 6, 1), prop("banner", 4, 0, "crimson", frame=frame),
        prop("banner", 8, 0, "crimson", frame=frame + 2), prop("lantern", 1, 6), prop("lantern", 6, 3),
        prop("training_dummy", 9, 5), prop("training_dummy", 10, 4), prop("archery_target", 10, 6),
        prop("cart", 8, 2, "hay"), prop("crate", 9, 1), prop("barrel", 9, 2),
        prop("barricade", 3, 7), prop("barricade", 6, 7), prop("mantlet", 9, 7, "crimson"), prop("ladder", 1, 4),
        prop("fence_stone", 11, 7, "grey"),
        unit("aldric", 4, 4), unit("kara", 6, 5), unit("class_paladin", 3, 5), unit("ilyra", 2, 4),
        unit("orc_brute", 4, 8, "back"), unit("goblin_spear", 7, 8, "back", mirror=True),
        unit("goblin_cutthroat", 10, 8, "back"),
    ]
    return s


def ember_keep(frame: int = 0):
    """A volcanic fortress hall at night, lit by its own fires: flagstone and cobble floors, brick and ashlar walls,
    crusted lava cliffs with glowing seams, basalt outcrops and ash, a fumarole, torches, lanterns and a fire
    crystal. Rendered with the dungeon set's darkness recipe, so every light source throws real light."""
    from .dungeon import Mat as DM, Room
    mats = {
        "f": DM(("flagstone", "dark"), ("ashlar_side", "grey")),
        "c": DM(("cobble", "grey"), ("brick_side", None)),
        "W": DM(("flagstone", "dark"), ("brick_side", None)),
        "l": DM(("lava", None), ("lava_side", None), sink=6),
        "r": DM(("basalt", None), ("basalt_side", None)),
        "a": DM(("ash", None), ("lava_side", "cooling")),
    }
    terrain = ["WWWWWWWWWWrr",
               "WffffffffWrr",
               "WfffccfffWll",
               "lllfccflllll",
               "lllfccflllll",
               "fffffccfffaa",
               "fffffccffaar",
               "rffffccfaarr"]
    heights = ["777777777788",
               "744444444788",
               "744444444722",
               "222444422222",
               "222444422222",
               "444444444444",
               "444444444445",
               "544444444455"]
    R = Room(mats, terrain, heights, frame=frame, ambient=(0.42, 0.33, 0.36), seed=9,
             decals={"f": (0.12, [("scorch", None), ("cracks", None), ("blood", None)])})
    for pid, i, j, v, kw in (("pillar", 2, 1, "dark", {}), ("pillar", 7, 1, "dark", {}),
                             ("pillar_broken", 2, 5, "dark", {}), ("altar", 5, 1, "dark", {}),
                             ("torch", 1, 1, None, {"phase": 0}), ("torch", 8, 1, None, {"phase": 2}),
                             ("crystal_large", 10, 0, "fire", {}), ("crag", 11, 1, "obsidian", {}),
                             ("rubble", 9, 6, "dark", {}), ("chest", 8, 5, None, {}),
                             ("banner", 3, 1, "crimson", {}), ("vent", 10, 5, None, {}), ("vent", 11, 6, None,
                                                                                            {"phase": 3}),
                             ("boulder", 0, 7, "basalt", {}), ("lantern", 4, 6, "moss", {"phase": 1}),
                             ("cage", 1, 5, "rust", {})):
        R.kit(pid, i, j, v, **kw)
    R.unit("black_knight", 5, 2, mirror=True)
    R.unit("cultist", 3, 2)
    R.unit("cultist", 7, 2, mirror=True)
    R.unit("kara", 5, 5, "back")
    R.unit("sefa", 3, 6, "back")
    R.unit("class_wizard", 7, 6, "back", mirror=True)
    R.beast("drake", 8, 6, "red", mirror=True)
    R.beast("slime", 1, 6, "magma")
    for i in range(12):                                              # the lava itself glows
        for j in range(8):
            if terrain[j][i] == "l" and (i + j) % 3 == 0:
                R.light(i, j, "#ff7b2b", 2.2, lift=4, flicker="pulse", power=0.7)
    return R


SCENES = {"ashen-ford": ashen_ford, "frostpeak": frostpeak, "mirewood": mirewood, "sunscar": sunscar,
          "ember-keep": ember_keep, "greenhollow": greenhollow, "highward": highward}

BACKDROPS = {"ashen-ford": ("day", "hills"), "frostpeak": ("frost", "glacier"), "mirewood": ("swamp", "marsh"),
             "sunscar": ("desert", "dunes"), "ember-keep": ("ashfall", "volcano"),
             "greenhollow": ("forest", "treeline"), "highward": ("day", "castle")}
"""Which sky and skyline each diorama floats in (the same pairings the backgrounds manifest gives)."""


def backdrop(name: str):
    from . import backgrounds as BG
    sk, biome = BACKDROPS[name]
    return lambda w, h: BG.compose(sk, biome, w, h, horizon=int(h * 0.48))


def render_scene(name: str, frame: int = 0, *, zoom: int = 2) -> Image.Image:
    """A finished diorama: lit scenes (a dungeon Room) take the darkness recipe and a stronger bloom, daylight ones
    the plain renderer."""
    sc = SCENES[name](frame)
    if isinstance(sc, iso.Scene):
        return iso.finish(iso.render(sc, backdrop=backdrop(name), margin=40), zoom=zoom)
    return iso.finish(sc.render(backdrop=backdrop(name), margin=40), zoom=zoom, bloom=0.32, vignette=0.45)
