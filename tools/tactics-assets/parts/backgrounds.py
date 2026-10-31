"""Parallax backgrounds behind the battle map: sky gradients, distant mountain ranges, clouds and stars.

The HD-2D look floats the map in atmosphere: a banded sky, two or three hazy ranges that scroll slower than the map,
and clouds drifting across. Ranges tile horizontally (their noise is periodic), skies are 1 px wide gradients the
game stretches, and every layer is banded rather than smooth so it stays pixel art next to the sprites.
"""
from __future__ import annotations

import shutil

import numpy as np
from PIL import Image

from arcpix import tex
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import Tile, contact_sheet

RANGE_W, RANGE_H = 512, 160
SKY_W, SKY_H = 64, 256


def _hex(h: str) -> np.ndarray:
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


SKIES = {
    "day": ["#6f93cc", "#86a8d8", "#a3bde2", "#c2d2e6", "#ddd9d0", "#e9dcc4"],
    "dusk": ["#2e2c5c", "#5a3f72", "#9a5378", "#d6735e", "#f0a35a", "#f7cf86"],
    "night": ["#07081a", "#0c1030", "#141a44", "#1d2656", "#2a3266", "#3a3f72"],
    "overcast": ["#7c8496", "#8e95a4", "#a0a5b0", "#b1b3b8", "#c0bfbd", "#cbc7c0"],
    "ashfall": ["#2a1a1e", "#43242a", "#6a2e2c", "#93402e", "#b85a32", "#d9823e"],
    # biome skies
    "swamp": ["#2f3a3a", "#3f4d46", "#566452", "#76805e", "#9a9a6e", "#b9b282"],
    "desert": ["#5f8fd0", "#7ea7da", "#a6c0de", "#cfd3d0", "#ecd9b6", "#f4d9a4"],
    "frost": ["#7d97b8", "#97aecb", "#b3c5db", "#cfdbe8", "#e3ebf2", "#eef2f6"],
    "forest": ["#6f9ac0", "#8db0cc", "#b2c8cf", "#d6dcc6", "#ecdcb0", "#f3d39a"],
}

RANGES = {      # biome -> [(far colour, near colour), roughness, peak height, snow line]
    "hills": (("#8fa7c4", "#6f8aa8"), 0.45, 0.55, None),
    "peaks": (("#9db0cc", "#6f7f9e"), 0.8, 0.9, 0.62),
    "mesas": (("#d9b48a", "#b98c66"), 0.3, 0.6, None),
    "volcano": (("#5a3236", "#3a2228"), 0.7, 0.85, None),
    # biome ranges: the ridge line is shaped by `_ridge`, not plain noise
    "treeline": (("#7f9aa6", "#4f6e64"), 0.5, 0.42, None),
    "dunes": (("#e2c39a", "#c99f72"), 0.25, 0.3, None),
    "marsh": (("#6f7c6c", "#4c5a4a"), 0.4, 0.32, None),
    "castle": (("#8d98ad", "#68728a"), 0.3, 0.6, None),
    "glacier": (("#b4c6dc", "#8aa0bf"), 0.85, 0.95, 0.75),
}

HAZE = {"volcano": "#6a2e2c", "marsh": "#9a9a6e", "treeline": "#d6dcc6", "dunes": "#f0dcb4", "glacier": "#e3ebf2",
        "castle": "#ddd9d0"}


def _ridge(biome: str, layer: str, n: np.ndarray, r: np.random.Generator, h_scale: float) -> np.ndarray:
    """The skyline for a biome, in pixel rows from the top. Plain ranges are noise; a tree line is noise crowned by
    conifer spikes; a marsh is low and flat with the odd bare snag; a castle skyline rises in walls and towers with
    crenellations; dunes are long smooth swells. Everything is periodic in x so the layer tiles."""
    base = RANGE_H * (1 - h_scale * (0.35 + 0.65 * n))
    xs = np.arange(RANGE_W)
    if biome == "treeline":
        k = 9 if layer == "far" else 13
        add = np.zeros(RANGE_W)
        for x0 in tex.poisson(RANGE_W, 1, k * 0.6, r, tries=400):
            hgt, half = r.uniform(k * 0.9, k * 2.0), r.uniform(k * 0.35, k * 0.55)
            d = np.abs(((xs - x0[0] + RANGE_W / 2) % RANGE_W) - RANGE_W / 2)
            add = np.maximum(add, np.clip(hgt * (1 - d / half), 0, None))
        return (base - add).astype(int)
    if biome == "marsh":
        add = np.zeros(RANGE_W)
        for x0 in tex.poisson(RANGE_W, 1, 40, r, tries=60):
            d = np.abs(((xs - x0[0] + RANGE_W / 2) % RANGE_W) - RANGE_W / 2)
            add = np.maximum(add, np.where(d < 1.5, r.uniform(14, 30), 0))
        return (base - add).astype(int)
    if biome == "dunes":
        sw = np.sin(2 * np.pi * xs / RANGE_W * 3 + n * 3) * 0.5 + 0.5
        return (RANGE_H * (1 - h_scale * (0.4 + 0.6 * sw))).astype(int)
    if biome == "castle":
        top = np.full(RANGE_W, RANGE_H * (1 - h_scale * 0.45))
        x = 0
        while x < RANGE_W - 30:
            w = int(r.integers(14, 26))
            tall = r.uniform(0.6, 1.0) if r.random() < 0.6 else 0.5
            top[x:x + w] = RANGE_H * (1 - h_scale * tall)
            x += w + int(r.integers(12, 40))
        crenel = (xs // 3) % 2 == 0
        return (top - np.where(crenel, 3, 0)).astype(int)
    return base.astype(int)


def sky(name: str) -> Image.Image:
    """A banded vertical gradient, SKY_W x SKY_H, tiled (not stretched) horizontally. Each band edge is a two-row
    checkerboard so bands dissolve into each other at 1:1."""
    stops = [_hex(c) for c in SKIES[name]]
    bands = 30
    t = np.linspace(0, 1, SKY_H)
    q = np.floor(t * bands) / bands                     # the band each row belongs to
    nxt = np.minimum(1.0, q + 1 / bands)
    frac = t * bands - np.floor(t * bands)

    def colour(v: float) -> np.ndarray:
        x = v * (len(stops) - 1)
        i = min(int(x), len(stops) - 2)
        f = x - i
        return stops[i] * (1 - f) + stops[i + 1] * f

    arr = np.zeros((SKY_H, SKY_W, 4), np.uint8)
    for y in range(SKY_H):
        a, b = colour(q[y]), colour(nxt[y])
        for x in range(SKY_W):
            use_b = frac[y] > 0.8 and (x + y) % 2 == 0
            arr[y, x, :3] = (b if use_b else a).astype(np.uint8)
    arr[..., 3] = 255
    return Image.fromarray(arr, "RGBA")


def mountain_range(biome: str, layer: str, seed: int) -> Image.Image:
    """A tileable silhouette: periodic 1D noise for the ridge line; near the ridge, flanks facing the light (left)
    are lit and the others shaded, judged from a smoothed slope so faces read as broad planes, not stripes; a snow
    cap on the peaks; and a banded fade towards a pale haze at the base (atmospheric perspective)."""
    (far_c, near_c), rough, peak, snow = RANGES[biome]
    base = _hex(far_c if layer == "far" else near_c)
    r = tex.rng(seed)
    n = tex.fbm(RANGE_W, 16, 3, r, octaves=4, gain=rough)[0]
    if biome == "mesas":                                       # flat-topped buttes: terrace the ridge
        n = np.round(n * 4) / 4
    h_scale = (0.55 if layer == "far" else 0.75) * peak
    ridge = _ridge(biome, layer, n, r, h_scale)
    slope = (np.roll(ridge, -4) - np.roll(ridge, 4)).astype(float)   # >0: falls away to the right (shadow side)
    face = np.where(slope < -2, 1, np.where(slope > 2, -1, 0))
    haze = _hex(HAZE.get(biome, "#e8e2d6"))
    arr = np.zeros((RANGE_H, RANGE_W, 4), np.uint8)
    for x in range(RANGE_W):
        top = ridge[x]
        for y in range(top, RANGE_H):
            d = y - top
            k = 1.0
            if d < 26:                                          # facets only near the ridge
                k = {1: 1.12, 0: 1.0, -1: 0.85}[int(face[x])]
                if d > 18 and (x + y) % 2:                      # dither the facet's lower edge away
                    k = 1.0
            col = base * k
            if snow is not None and y < RANGE_H * snow and d < 8 + (x * 7 % 5):
                col = np.array([236, 240, 248], np.float32) * (0.9 if face[x] < 0 else 1.0)
            band = min(4, int((y / RANGE_H) ** 1.6 * 6))       # 0..4 towards the base
            if (y * 6 / RANGE_H) % 1 > 0.8 and (x + y) % 2:
                band = min(4, band + 1)
            amt = band * (0.05 if layer == "near" else 0.08)
            col = col * (1 - amt) + haze * amt
            arr[y, x, :3] = np.clip(col, 0, 255)
            arr[y, x, 3] = 255
    return Image.fromarray(arr, "RGBA")


def cloud(seed: int, w: int = 64, h: int = 24) -> Image.Image:
    """A cumulus puff: stacked ellipses, lit tops, a flat shaded base."""
    r = tex.rng(seed)
    a = np.zeros((h, w), np.int8) - 1
    ys, xs = np.mgrid[0:h, 0:w]
    for _ in range(6):
        cx, cy = r.uniform(12, w - 12), r.uniform(h * 0.4, h * 0.7)
        rx, ry = r.uniform(7, 14), r.uniform(5, 8)
        d = ((xs - cx) / rx) ** 2 + ((ys - cy) / ry) ** 2
        a = np.where(d < 1, np.maximum(a, np.where(ys < cy - ry * 0.3, 2, 1)), a)
    a[ys > h * 0.72] = -1                                      # flat base
    a = np.where((a >= 0) & (ys > h * 0.62), 0, a)
    colours = {0: (184, 192, 214), 1: (222, 226, 236), 2: (246, 246, 250)}
    arr = np.zeros((h, w, 4), np.uint8)
    for k, c in colours.items():
        arr[a == k, :3] = c
        arr[a == k, 3] = 255
    return Image.fromarray(arr, "RGBA")


def stars(seed: int = 9, w: int = 256, h: int = 128) -> Image.Image:
    r = tex.rng(seed)
    arr = np.zeros((h, w, 4), np.uint8)
    for _ in range(140):
        x, y = int(r.integers(w)), int(r.integers(h))
        b = int(r.choice([150, 190, 240]))
        arr[y, x] = (b, b, min(255, b + 10), 255)
        if b == 240 and r.random() < 0.3:                      # a few twinkle crosses
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                arr[(y + dy) % h, (x + dx) % w] = (150, 150, 170, 255)
    return Image.fromarray(arr, "RGBA")


def compose(sky_name: str, biome: str, w: int = 640, h: int = 300, horizon: int | None = None) -> Image.Image:
    """A preview of how the layers stack (the game does this with parallax). `horizon` is where the near range's
    base meets the ground; below it the near range's base colour continues."""
    out = Image.new("RGBA", (w, h))
    sk = sky(sky_name)
    if h > SKY_H:                                       # extend the horizon colour below the gradient
        tall = Image.new("RGBA", (SKY_W, h))
        tall.paste(sk, (0, 0))
        tall.paste(sk.crop((0, SKY_H - 2, SKY_W, SKY_H)).resize((SKY_W, h - SKY_H), Image.NEAREST), (0, SKY_H))
        sk = tall
    for x in range(0, w, SKY_W):
        out.alpha_composite(sk.crop((0, 0, SKY_W, h)), (x, 0))
    if sky_name == "night":
        st = stars()
        for x in range(0, w, st.width):
            out.alpha_composite(st, (x, 0))
    base_y = (horizon if horizon is not None else h) - RANGE_H
    for layer, seed, y in (("far", 3, base_y - 40), ("near", 7, base_y + 10)):
        rg = mountain_range(biome, layer, seed + sum(map(ord, biome)))
        for x in range(0, w, RANGE_W):
            out.alpha_composite(rg, (x, y))
        if layer == "near" and y + RANGE_H < h:                     # continue the base below the range
            fill = rg.crop((0, RANGE_H - 1, RANGE_W, RANGE_H)).resize((RANGE_W, h - y - RANGE_H), Image.NEAREST)
            for x in range(0, w, RANGE_W):
                out.alpha_composite(fill, (x, y + RANGE_H))
    if sky_name in ("day", "dusk", "overcast", "desert", "forest", "frost"):
        for k, (x, y) in enumerate(((60, 30), (300, 55), (480, 20))):
            out.alpha_composite(cloud(k + 1), (x, y))
    return out


def build() -> None:
    out = PACK / "backgrounds"
    shutil.rmtree(out, ignore_errors=True)
    for name in SKIES:
        save(sky(name), out / "sky" / f"{name}.png")
    seeds = {"hills": 3, "peaks": 11, "mesas": 21, "volcano": 31, "treeline": 41, "dunes": 51, "marsh": 61,
             "castle": 71, "glacier": 81}
    for biome, sd in seeds.items():
        for layer, k in (("far", 0), ("near", 1)):
            save(mountain_range(biome, layer, sd + k), out / "ranges" / f"{biome}_{layer}.png")
    for k in range(6):
        save(cloud(k + 1, 48 + 8 * (k % 3), 20 + 2 * (k % 2)), out / "clouds" / f"cloud_{k}.png")
    save(stars(), out / "stars.png")
    write_json({
        "sky": {"files": {n: f"sky/{n}.png" for n in SKIES},
                "note": f"{SKY_W} x {SKY_H} banded gradient: tile horizontally, stretch vertically by whole multiples"},
        "ranges": {"size": [RANGE_W, RANGE_H], "tileX": True,
                   "files": {f"{b}_{l}": f"ranges/{b}_{l}.png" for b in seeds for l in ("far", "near")},
                   "parallax": {"far": 0.1, "near": 0.25}},
        "clouds": [f"clouds/cloud_{k}.png" for k in range(6)],
        "stars": {"file": "stars.png", "tileX": True, "tileY": True},
        "pairings": {"ashen-ford": ["day", "hills"], "frostpeak": ["overcast", "peaks"],
                     "mirewood": ["swamp", "marsh"], "sunscar": ["desert", "dunes"],
                     "ember-keep": ["ashfall", "volcano"], "night": ["night", "peaks"],
                     "greenhollow": ["forest", "treeline"], "highward": ["day", "castle"],
                     "rimeholt": ["frost", "glacier"]},
        "biomes": {"grassland": ["day", "hills"], "snow": ["frost", "glacier"], "swamp": ["swamp", "marsh"],
                   "desert": ["desert", "dunes"], "volcanic": ["ashfall", "volcano"], "forest": ["forest", "treeline"],
                   "courtyard": ["day", "castle"], "night": ["night", "peaks"]},
    }, out / "manifest.json")
    tiles = [Tile(compose(s, b), f"{s} · {b}") for s, b in
             (("day", "hills"), ("overcast", "peaks"), ("dusk", "hills"), ("day", "mesas"), ("ashfall", "volcano"),
              ("night", "peaks"), ("forest", "treeline"), ("desert", "dunes"), ("swamp", "marsh"),
              ("day", "castle"), ("frost", "glacier"), ("dusk", "treeline"))]
    save(contact_sheet("Backgrounds", tiles, columns=2, zoom=1,
                       subtitle="Sky gradients, tileable mountain ranges (far and near, for parallax), clouds and "
                                "stars."), PREVIEWS / "20-backgrounds.png")
