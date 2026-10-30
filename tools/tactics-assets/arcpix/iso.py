"""A small isometric renderer for previewing terrain as the game will show it: block tiles with textured tops and
sides, overhanging lips, ground decals and sprites standing on tiles.

It mirrors the 3D map's conventions: textures are world-aligned (a 128 px texture spans 4 tiles), tops are sampled
by world (U, V), sides by distance along the edge and depth below the top. It's a preview tool, not the game's
renderer: the game draws the same textures on real 3D blocks with real lighting.

Projection, in texel units (one tile = 32 texels, one height step = 16 texels):
    screen x = (U - V) + ox
    screen y = (U + V) / 2 - z + oy
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
from PIL import Image, ImageFilter

TILE = 32
STEP = 16
TEX = 128


@dataclass
class Cell:
    h: int                                   # height in steps (16 texels each); 0 = ground level
    top: np.ndarray                          # RGBA texture, TEX x TEX
    side: np.ndarray                         # RGBA texture, TEX wide x 32 tall (one tile of depth)
    lip: np.ndarray | None = None            # RGBA overlay at the top of the sides (TEX wide)
    decal: np.ndarray | None = None          # RGBA 32x32, drawn on the top in tile-local UV
    sink: int = 0                            # texels to lower the top (water sits below the bank)


@dataclass
class Sprite:
    image: Image.Image
    anchor: tuple[int, int]
    i: float
    j: float
    scale: int = 2
    shadow: bool = True


@dataclass
class Scene:
    w: int
    d: int
    cells: dict[tuple[int, int], Cell] = field(default_factory=dict)
    sprites: list[Sprite] = field(default_factory=list)

    def top_z(self, i: int, j: int) -> int:
        c = self.cells.get((i, j))
        return 0 if c is None else c.h * STEP - c.sink


def _arr(img: Image.Image) -> np.ndarray:
    return np.asarray(img.convert("RGBA")).astype(np.float32)


def render(scene: Scene, *, margin: int = 24, sky=((150, 178, 214), (226, 214, 196))) -> Image.Image:
    max_h = max(c.h for c in scene.cells.values()) * STEP
    W = (scene.w + scene.d) * TILE + margin * 2
    H = (scene.w + scene.d) * TILE // 2 + max_h + margin * 2 + 60
    ox = scene.d * TILE + margin
    oy = max_h + margin + 50
    # sky: a soft vertical gradient (HD-2D scenes float in haze)
    t = np.linspace(0, 1, H)[:, None, None]
    top_c, bot_c = np.array(sky[0], np.float32), np.array(sky[1], np.float32)
    canvas = np.zeros((H, W, 4), np.float32)
    canvas[..., :3] = top_c * (1 - t) + bot_c * t
    canvas[..., 3] = 255

    def blend(ys, xs, rgba, mul=1.0, tint=(1.0, 1.0, 1.0)):
        a = rgba[:, 3:4] / 255.0
        col = rgba[:, :3] * mul * np.array(tint, np.float32)
        canvas[ys, xs, :3] = canvas[ys, xs, :3] * (1 - a) + np.clip(col, 0, 255) * a

    order = []
    for (i, j), c in scene.cells.items():
        order.append((i + j, 0, ("cell", i, j, c)))
    for s in scene.sprites:
        order.append((int(s.i) + int(s.j), 1, ("sprite", s)))
    order.sort(key=lambda o: (o[0], o[1]))

    for _k, _p, item in order:
        if item[0] == "sprite":
            _draw_sprite(canvas, scene, item[1], ox, oy)
            continue
        _, i, j, c = item
        U0, V0 = i * TILE, j * TILE
        Z = c.h * STEP - c.sink
        # ----- left face (V = V0 + TILE), visible when the neighbour in +V is lower
        for face in ("left", "right"):
            if face == "left":
                nz = scene.top_z(i, j + 1) if (i, j + 1) in scene.cells else -STEP * 2
                x0, x1 = U0 - (V0 + TILE) + ox, U0 + TILE - (V0 + TILE) + ox
            else:
                nz = scene.top_z(i + 1, j) if (i + 1, j) in scene.cells else -STEP * 2
                x0, x1 = (U0 + TILE) - (V0 + TILE) + ox, (U0 + TILE) - V0 + ox
            if nz >= Z:
                continue
            xs = np.arange(int(x0), int(x1))
            if not len(xs):
                continue
            if face == "left":
                U = xs - ox + V0 + TILE
                base_sy = (U + V0 + TILE) / 2 + oy
                along = U
            else:
                V = U0 + TILE + ox - xs
                base_sy = (U0 + TILE + V) / 2 + oy
                along = V
            for zz in range(int(nz), int(Z)):
                sy = (base_sy - zz - 1).astype(int)
                depth = (Z - 1 - zz)
                rgba = c.side[depth % c.side.shape[0], along.astype(int) % TEX]
                mul = 0.80 if face == "left" else 0.62
                tint = (1.0, 1.0, 1.04) if face == "left" else (0.95, 0.97, 1.08)
                # ambient occlusion towards the bottom of a tall wall
                mul *= 1.0 - 0.18 * min(1.0, (Z - zz) / (STEP * 4))
                blend(sy, xs, rgba, mul, tint)
                if c.lip is not None and depth < c.lip.shape[0]:
                    blend(sy, xs, c.lip[depth, along.astype(int) % TEX], mul * 1.05, tint)
        # ----- top face
        ys = np.arange(int((U0 + V0) / 2 - Z + oy), int((U0 + V0 + 2 * TILE) / 2 - Z + oy) + 1)
        xs = np.arange(int(U0 - V0 - TILE + ox), int(U0 - V0 + TILE + ox) + 1)
        YY, XX = np.meshgrid(ys, xs, indexing="ij")
        a = XX - ox
        b = 2 * (YY - oy + Z)
        U = (a + b) / 2
        V = (b - a) / 2
        inside = (U >= U0) & (U < U0 + TILE) & (V >= V0) & (V < V0 + TILE)
        if inside.any():
            uu, vv = U[inside].astype(int), V[inside].astype(int)
            rgba = c.top[vv % TEX, uu % TEX]
            # rim light on front edges that are real ledges (sells the block's thickness without gridding flat ground)
            ledge_v = scene.top_z(i, j + 1) < Z if (i, j + 1) in scene.cells else True
            ledge_u = scene.top_z(i + 1, j) < Z if (i + 1, j) in scene.cells else True
            rim = (((vv - V0) >= TILE - 1) & ledge_v) | (((uu - U0) >= TILE - 1) & ledge_u)
            mul = np.where(rim, 1.14, 1.0)[:, None]
            col = np.clip(rgba[:, :3] * mul, 0, 255)
            canvas[YY[inside], XX[inside], :3] = col
            if c.decal is not None:
                d = c.decal[vv - V0, uu - U0]
                blend(YY[inside], XX[inside], d)
    out = Image.fromarray(canvas.clip(0, 255).astype(np.uint8), "RGBA")
    return out


def _draw_sprite(canvas: np.ndarray, scene: Scene, s: Sprite, ox: int, oy: int) -> None:
    i, j = int(s.i), int(s.j)
    U = s.i * TILE + TILE / 2 if s.i == int(s.i) else s.i * TILE
    V = s.j * TILE + TILE / 2 if s.j == int(s.j) else s.j * TILE
    Z = scene.top_z(i, j)
    gx = U - V + ox
    gy = (U + V) / 2 - Z + oy
    img = s.image.resize((s.image.width * s.scale, s.image.height * s.scale), Image.NEAREST)
    arr = np.asarray(img).astype(np.float32)
    if s.shadow:
        ys, xs = np.mgrid[int(gy - 5):int(gy + 6), int(gx - 14):int(gx + 15)]
        e = ((xs - gx) / 13.0) ** 2 + ((ys - gy) / 4.5) ** 2
        m = e < 1
        canvas[ys[m], xs[m], :3] *= (0.62 + 0.3 * e[m])[:, None]
    x0 = int(gx - s.anchor[0] * s.scale)
    y0 = int(gy - s.anchor[1] * s.scale)
    h, w = arr.shape[:2]
    sub = canvas[y0:y0 + h, x0:x0 + w]
    a = arr[:sub.shape[0], :sub.shape[1], 3:4] / 255.0
    sub[..., :3] = sub[..., :3] * (1 - a) + arr[:sub.shape[0], :sub.shape[1], :3] * a


def finish(img: Image.Image, *, zoom: int = 2, bloom: float = 0.18, vignette: float = 0.35) -> Image.Image:
    """HD-2D-ish post: soft bloom on bright areas and a vignette, then a nearest-neighbour upscale."""
    base = img.convert("RGB")
    if bloom:
        bright = base.point(lambda v: max(0, v - 205) * 4)
        glow = bright.filter(ImageFilter.GaussianBlur(6))
        arr = np.asarray(base).astype(np.float32) + np.asarray(glow).astype(np.float32) * bloom
        base = Image.fromarray(arr.clip(0, 255).astype(np.uint8))
    if vignette:
        w, h = base.size
        ys, xs = np.mgrid[0:h, 0:w]
        d = np.sqrt(((xs - w / 2) / (w / 2)) ** 2 + ((ys - h / 2) / (h / 2)) ** 2)
        m = 1 - vignette * np.clip(d - 0.55, 0, 1) ** 1.5
        base = Image.fromarray((np.asarray(base) * m[..., None]).clip(0, 255).astype(np.uint8))
    return base.resize((base.width * zoom, base.height * zoom), Image.NEAREST)
