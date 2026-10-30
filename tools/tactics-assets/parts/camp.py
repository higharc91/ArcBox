"""The camp between sessions, at night: the party around a fire on logs, one dozing, two talking, the leader waving
someone over. It exercises the downtime animations, the ambient set's fire, smoke, embers and fireflies, and the
night grade, with the fire's light pool punched back through the grade the way the game's lighting would.

Writes previews only (anim-camp.gif and a still).
"""
from __future__ import annotations

import json

import numpy as np
from PIL import Image

from arcpix import iso
from arcpix.out import PACK, PREVIEWS, save
from . import backgrounds as BG
from . import characters as C
from . import dioramas as D
from . import grades as G
from .recipes import ALL
from .rig import ANCHOR, ANIMS

AMB = json.loads((PACK / "ambient" / "manifest.json").read_text())["assets"]
TICKS = 48


def _amb(asset: str, variant: str, k: int) -> tuple[Image.Image, tuple[int, int]]:
    e = AMB[asset]
    sheet = Image.open(PACK / "ambient" / e["variants"][variant]).convert("RGBA")
    w, h = e["frame"]
    n = e["frames"]
    return sheet.crop(((k % n) * w, 0, (k % n + 1) * w, h)), tuple(e["anchor"])


def _unit(rid: str, anim: str, k: int, i: float, j: float, facing="front", mirror=False) -> iso.Sprite:
    r = next(x for x in ALL if x.id == rid)
    poses = ANIMS[anim][2]
    img = C.compose(r, facing, poses[k % len(poses)]).render(r.ramps())
    anchor = ANCHOR
    if mirror:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
        anchor = (img.width - 1 - ANCHOR[0], ANCHOR[1])
    return iso.Sprite(img, anchor, i, j)


def scene(t: int) -> iso.Scene:
    mats = {"g": D.Mat(("grass", "lush"), ("dirt_side", "dark"), ("grass_lip", "lush")),
            "d": D.Mat(("dirt", "dark"), ("dirt_side", "dark"))}
    terrain = ["ggggggg", "ggggggg", "ggdddgg", "ggdddgg", "ggdddgg", "ggggggg", "ggggggg"]
    heights = ["4433333", "4333333", "3333333", "3333333", "3333333", "3333333", "3333333"]
    s = D.build_scene(mats, terrain, heights, seed=4, decals={"g": (0.15, D.FLOWERS)})
    beat = t // 3
    s.sprites += [
        D.prop("pine", 0, 0), D.prop("pine", 1, 0), D.prop("tree", 0, 2, "autumn"), D.prop("pine", 6, 0),
        D.prop("tent", 2, 0), D.prop("tent", 5, 1, "red"), D.prop("crate", 6, 2), D.prop("barrel", 0, 4),
        D.prop("log", 2, 2), D.prop("log", 4, 4), D.prop("campfire", 3, 3, frame=t),
        D.prop("bush", 6, 6), D.prop("tall_grass", 0, 6), D.prop("rock_small", 5, 6),
        _unit("aldric", "doze", beat, 1.6, 2.4),
        _unit("ilyra", "sit", beat, 3.2, 1.6),
        _unit("kara", "talk", beat, 4.6, 2.8, mirror=True),
        _unit("tamsin", "sit", beat + 1, 1.8, 4.2),
        _unit("sefa", "talk", beat + 2, 4.2, 4.6, mirror=True),
        _unit("wren", "wave", beat, 5.6, 5.4, mirror=True),
    ]
    smoke, sa = _amb("campfire_smoke", "grey", t)
    s.sprites.append(iso.Sprite(smoke, (sa[0], sa[1] + 14), 3.02, 3.02, shadow=False, scale=2))
    emb, ea = _amb("embers", "fire", t)
    s.sprites.append(iso.Sprite(emb, (ea[0], ea[1] + 6), 3.03, 3.03, shadow=False, scale=2))
    for k, (i, j) in enumerate(((0.6, 3.2), (5.8, 1.4), (1.2, 5.6), (6.4, 5.2))):
        ff, fa = _amb("fireflies", "gold", t + k * 4)
        s.sprites.append(iso.Sprite(ff, (fa[0], fa[1] + 14), i, j, shadow=False, scale=2))
    return s


def frame(t: int) -> Image.Image:
    img = iso.render(scene(t), backdrop=lambda w, h: BG.compose("night", "peaks", w, h, horizon=int(h * 0.5)))
    base = iso.finish(img, zoom=1).convert("RGB")
    night = next(g for g in G.GRADES if g.name == "night")
    graded = np.asarray(G.graded(base, night)).astype(np.float32)
    # the fire's light: a warm radial pool (flickering) that lerps the grade back to the ungraded colour
    w, h = base.size
    ys, xs = np.mgrid[0:h, 0:w]
    cx, cy = w * 0.5, h * 0.5
    flick = 1.0 + 0.06 * np.sin(t * 1.7) + 0.04 * np.sin(t * 3.1)
    d = np.hypot((xs - cx) / 1.0, (ys - cy) / 0.6) / (115 * flick)
    light = np.clip(1.0 - d, 0, 1) ** 1.4
    light = np.round(light * 5) / 5                                  # banded falloff: pixel-art light
    warm = np.asarray(base).astype(np.float32) * np.array([1.12, 0.98, 0.8])
    out = graded * (1 - light[..., None]) + np.clip(warm, 0, 255) * light[..., None]
    return Image.fromarray(out.clip(0, 255).astype(np.uint8), "RGB")


def build() -> None:
    frames = [frame(t) for t in range(TICKS)]
    big = [f.resize((f.width * 2, f.height * 2), Image.NEAREST) for f in frames]
    save(big[0].convert("RGBA"), PREVIEWS / "41-camp.png")
    pal = [f.quantize(colors=255, method=Image.Quantize.MEDIANCUT) for f in big]
    pal[0].save(PREVIEWS / "anim-camp.gif", save_all=True, append_images=pal[1:], duration=110, loop=0,
                optimize=True)
