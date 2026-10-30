"""Screen transitions as threshold maps: battle starts, scene changes, chapter cards. Each map is a grayscale image;
the game reveals (or covers) every pixel whose value is below a progress value going 0 -> 1, so one tiny shader plays
any wipe. Maps are made at a low resolution and scaled up with nearest-neighbour, so the edge stays chunky and
pixel-art, whatever the screen size.
"""
from __future__ import annotations

import math
import shutil

import numpy as np
from PIL import Image

from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import Tile, contact_sheet

W, H = 160, 90      # 16:9; one map pixel covers 12x12 screen pixels at 1920x1080
BAYER4 = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0


def _grid():
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float64)
    return xs, ys


def _norm(a):
    return (a - a.min()) / (a.max() - a.min() + 1e-9)


def diamonds() -> np.ndarray:
    """FFT-style: a wave of diamonds sweeping left to right, each diamond growing from its centre."""
    xs, ys = _grid()
    cell = 10
    cx, cy = (xs // cell) * cell + cell / 2, (ys // cell) * cell + cell / 2
    local = (np.abs(xs - cx) + np.abs(ys - cy)) / cell
    return _norm(cx / W * 0.75 + local * 0.25)


def iris() -> np.ndarray:
    xs, ys = _grid()
    return _norm(np.hypot((xs - W / 2) / (W / 2), (ys - H / 2) / (H / 2) * 0.5625 / 0.5625))


def swirl() -> np.ndarray:
    xs, ys = _grid()
    a = np.arctan2(ys - H / 2, xs - W / 2) / (2 * math.pi) + 0.5
    r = np.hypot(xs - W / 2, ys - H / 2) / (W / 2)
    return _norm((a + r * 0.9) % 1.0 * 0.7 + r * 0.3)


def slash() -> np.ndarray:
    """A diagonal sword-cut: the screen splits along a slanted line, opening from it outwards."""
    xs, ys = _grid()
    d = (xs - W / 2) * 0.35 - (ys - H / 2)
    return _norm(np.abs(d) + np.sin(xs * 0.3) * 0.8)


def curtain() -> np.ndarray:
    xs, ys = _grid()
    return _norm(np.abs(xs - W / 2) + np.sin(ys * 0.5) * 1.5)


def dissolve() -> np.ndarray:
    """Ordered-dither dissolve: a retro fade that stays crisp."""
    xs, ys = _grid()
    return np.tile(BAYER4, (H // 4 + 1, W // 4 + 1))[:H, :W] * 0.85 + _norm(ys) * 0.15


def blinds() -> np.ndarray:
    xs, ys = _grid()
    return _norm((ys % 10) / 10 * 0.7 + xs / W * 0.3)


TRANSITIONS = {"diamonds": diamonds, "iris": iris, "swirl": swirl, "slash": slash, "curtain": curtain,
               "dissolve": dissolve, "blinds": blinds}

SHADER = """shader_type canvas_item;   // a full-screen rect drawn over everything
uniform sampler2D map : filter_nearest;  // transitions/<name>.png
uniform float progress;                  // 0 = clear, 1 = covered
uniform vec4 colour : source_color = vec4(0.05, 0.06, 0.1, 1.0);
void fragment() { COLOR = texture(map, UV).r < progress ? colour : vec4(0.0); }"""


def build() -> None:
    out = PACK / "transitions"
    shutil.rmtree(out, ignore_errors=True)
    tiles = []
    from parts import dioramas as D, backgrounds as BG
    from arcpix import iso
    scene = iso.finish(iso.render(D.ashen_ford(), backdrop=lambda w, h: BG.compose("day", "hills", w, h,
                                                                                     horizon=int(h * 0.48))), zoom=1)
    scene = scene.convert("RGBA").resize((W * 2, H * 2))
    for name, fn in TRANSITIONS.items():
        m = fn()
        img = Image.fromarray((m * 255).astype(np.uint8), "L")
        save(img, out / f"{name}.png")
        strip = Image.new("RGBA", (W * 2 * 4 + 12, H * 2), (0, 0, 0, 0))
        for k, p in enumerate((0.2, 0.45, 0.7, 0.9)):
            mask = (np.asarray(img.resize((W * 2, H * 2), Image.NEAREST)) / 255.0) < p
            frame = scene.copy()
            arr = np.asarray(frame).copy()
            arr[mask] = (13, 15, 26, 255)
            strip.paste(Image.fromarray(arr, "RGBA"), (k * (W * 2 + 4), 0))
        tiles.append(Tile(strip, name, "progress 0.2 · 0.45 · 0.7 · 0.9"))
    write_json({"size": [W, H], "transitions": {n: f"{n}.png" for n in TRANSITIONS}, "godotShader": SHADER,
                "notes": "sample with nearest filtering; animate progress 0->1 to cover, 1->0 to reveal"},
               out / "manifest.json")
    save(contact_sheet("Screen transitions", tiles, columns=1, zoom=1,
                       subtitle="Threshold maps: one shader, any wipe. Low-res maps keep the edge chunky."),
         PREVIEWS / "38-transitions.png")
