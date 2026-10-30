"""Preview dioramas: small maps built from the terrain catalogue, with characters standing on them. They exist to
judge the textures together and in context, the way the game will use them."""
from __future__ import annotations

from functools import lru_cache

import numpy as np

from arcpix import iso
from . import terrain as TR
from . import characters as C
from .recipes import ALL
from .rig import ANIMS, ANCHOR


@lru_cache(maxsize=None)
def _frames(tex_id: str) -> list:
    t = next(x for x in TR.CATALOGUE if x.id == tex_id)
    return t.frames()


def tex(tex_id: str, variant: str | None = None, frame: int = 0) -> np.ndarray:
    t = next(x for x in TR.CATALOGUE if x.id == tex_id)
    v = t.variants[variant] if variant else next(iter(t.variants.values()))
    fr = _frames(tex_id)
    return np.asarray(fr[frame % len(fr)].render(TR.ramps(v))).astype(np.float32)


def _sprite(rid: str, i: float, j: float, facing: str = "front", frame: int = 0, mirror: bool = False) -> iso.Sprite:
    r = next(x for x in ALL if x.id == rid)
    img = C.compose(r, facing, ANIMS["idle"][2][0] if frame == 0 else C.frame_list()[frame][2]).render(r.ramps())
    if mirror:
        from PIL import Image
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
        return iso.Sprite(img, (img.width - 1 - ANCHOR[0], ANCHOR[1]), i, j)
    return iso.Sprite(img, ANCHOR, i, j)


def ashen_ford(frame: int = 0) -> iso.Scene:
    """Grassland with a raised rocky bluff, a river with a plank bridge, a dirt road and wildflowers."""
    s = iso.Scene(12, 10)
    rng = np.random.default_rng(7)
    grass, grass_lip = tex("grass", "green"), tex("grass_lip", "green")
    dirt_side, cliff = tex("dirt_side", "brown"), tex("cliff_side", "grey")
    path, rock = tex("path"), tex("rock", "grey")
    water, water_side = tex("water", "clear", frame), tex("water", "clear", frame)[:32] * np.array([0.8, 0.85, 0.95, 1])
    planks, plank_side = tex("planks", "oak"), tex("plank_side")
    sand = tex("sand")
    flowers = [tex(f"flowers_{k}", v) for k in range(3) for v in ("red", "yellow", "white", "blue")]
    pebbles = [tex(f"pebbles_{k}") for k in range(2)]
    for i in range(12):
        for j in range(10):
            river = j in (6, 7)
            road = i in (4, 5)
            if river and road:
                s.cells[(i, j)] = iso.Cell(2, planks, plank_side)
            elif river:
                s.cells[(i, j)] = iso.Cell(2, water, water_side, sink=6)
            elif j in (5, 8) and i in (0, 1, 2, 9, 10, 11):
                s.cells[(i, j)] = iso.Cell(2, sand, dirt_side, sink=2)
            elif road:
                s.cells[(i, j)] = iso.Cell(2, path, dirt_side,
                                           decal=pebbles[(i + j) % 2] if rng.random() < 0.4 else None)
            else:
                h = 2
                if i >= 7 and j <= 3:
                    h = 4
                if i >= 9 and j <= 1:
                    h = 6
                top = rock if h == 6 else grass
                side = cliff if h >= 4 else dirt_side
                lip = None if h == 6 else grass_lip
                decal = flowers[int(rng.integers(len(flowers)))] if (h == 2 and rng.random() < 0.22) else None
                s.cells[(i, j)] = iso.Cell(h, top, side, lip=lip, decal=decal)
    s.sprites += [
        _sprite("kara", 5, 4), _sprite("aldric", 3, 3), _sprite("ilyra", 8, 2), _sprite("tamsin", 10, 0),
        _sprite("wren", 6, 5), _sprite("sefa", 2, 4),
        _sprite("goblin_cutthroat", 4, 9, "back", mirror=True), _sprite("goblin_spear", 7, 9, "back"),
        _sprite("orc_brute", 5, 9, "back"), _sprite("goblin_shaman", 9, 9, "back", mirror=True),
    ]
    return s


SCENES = {"ashen-ford": ashen_ford}
