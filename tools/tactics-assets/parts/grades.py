"""Colour grades: time of day and mood as 3D lookup tables (LUTs), so one map can be played at dawn, noon, dusk,
night, in a storm or in a cave without repainting a pixel. The TV applies the LUT as a post-process over the whole
scene (a few lines of shader; see the manifest); UI is drawn after the grade so it stays true.

A grade is built from simple, art-directable operations: white balance, a shadow tint and a highlight tint (split
toning), saturation, contrast and a lift on the blacks. Night also crushes the reds and leans the shadows blue, which
is what makes moonlight read as moonlight rather than just "darker".
"""
from __future__ import annotations

import shutil
from dataclasses import dataclass

import numpy as np
from PIL import Image

from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import Tile, contact_sheet

N = 16       # LUT resolution per channel: the strip is N*N wide, N tall


@dataclass(frozen=True)
class Grade:
    name: str
    balance: tuple[float, float, float] = (1.0, 1.0, 1.0)   # per-channel gain
    shadows: tuple[float, float, float] = (0, 0, 0)         # tint added to darks (-1..1 per channel, small)
    highlights: tuple[float, float, float] = (0, 0, 0)      # tint added to lights
    saturation: float = 1.0
    contrast: float = 1.0
    exposure: float = 1.0
    lift: float = 0.0                                        # raises the blacks (haze)


GRADES = [
    Grade("day"),
    Grade("dawn", balance=(1.05, 0.97, 1.0), shadows=(0.02, -0.01, 0.05), highlights=(0.06, 0.03, -0.02),
          saturation=0.9, contrast=0.95, lift=0.04),
    Grade("golden_hour", balance=(1.1, 1.0, 0.85), shadows=(0.0, -0.02, 0.04), highlights=(0.08, 0.04, -0.04),
          saturation=1.1, contrast=1.05),
    Grade("dusk", balance=(1.0, 0.85, 0.95), shadows=(0.0, -0.03, 0.08), highlights=(0.1, 0.02, -0.02),
          saturation=0.95, contrast=1.05, exposure=0.85),
    Grade("night", balance=(0.55, 0.7, 1.0), shadows=(-0.02, 0.0, 0.07), highlights=(0.0, 0.03, 0.08),
          saturation=0.55, contrast=1.1, exposure=0.6, lift=0.03),
    Grade("storm", balance=(0.85, 0.92, 1.0), shadows=(0.0, 0.02, 0.05), highlights=(-0.02, 0.0, 0.03),
          saturation=0.6, contrast=0.9, exposure=0.8, lift=0.05),
    Grade("cave", balance=(0.8, 0.82, 0.9), shadows=(0.02, 0.0, 0.06), highlights=(0.06, 0.03, 0.0),
          saturation=0.75, contrast=1.15, exposure=0.6),
    Grade("infernal", balance=(1.15, 0.8, 0.7), shadows=(0.06, -0.02, 0.0), highlights=(0.08, 0.02, -0.05),
          saturation=1.15, contrast=1.15, exposure=0.85),
    Grade("fey", balance=(0.95, 1.0, 1.05), shadows=(0.03, -0.01, 0.07), highlights=(0.04, 0.06, 0.02),
          saturation=1.25, contrast=0.95, lift=0.03),
    Grade("memory", balance=(1.05, 1.0, 0.85), saturation=0.25, contrast=0.9, lift=0.08),   # flashbacks, sepia
]


def apply(g: Grade, rgb: np.ndarray) -> np.ndarray:
    """rgb in 0..1, any shape (..., 3)."""
    x = rgb * np.array(g.balance) * g.exposure
    lum = (x @ np.array([0.299, 0.587, 0.114]))[..., None]
    x = lum + (x - lum) * g.saturation
    x = (x - 0.5) * g.contrast + 0.5
    w = np.clip(lum, 0, 1)
    x = x + np.array(g.shadows) * (1 - w) + np.array(g.highlights) * w
    x = g.lift + x * (1 - g.lift)
    return np.clip(x, 0, 1)


def lut(g: Grade) -> Image.Image:
    """A standard 2D strip: N slices side by side, slice index = blue, x within slice = red, y = green."""
    out = np.zeros((N, N * N, 3))
    for k in range(N):
        cube = np.stack([np.linspace(0, 1, N)[None, :].repeat(N, 0), np.linspace(0, 1, N)[:, None].repeat(N, 1),
                         np.full((N, N), k / (N - 1))], -1)
        out[:, k * N:(k + 1) * N] = apply(g, cube)
    return Image.fromarray((out * 255 + 0.5).astype(np.uint8), "RGB")


def graded(img: Image.Image, g: Grade) -> Image.Image:
    a = np.asarray(img.convert("RGB")).astype(np.float32) / 255
    return Image.fromarray((apply(g, a) * 255 + 0.5).astype(np.uint8), "RGB")


SHADER = """shader_type canvas_item;   // on a full-screen ColorRect over the world, under the UI
uniform sampler2D screen : hint_screen_texture, filter_nearest;
uniform sampler2D lut : filter_linear;   // grades/<name>.png
uniform float strength = 1.0;
void fragment() {
    vec3 c = texture(screen, SCREEN_UV).rgb;
    float n = 16.0;
    float b = c.b * (n - 1.0);
    float b0 = floor(b), b1 = min(b0 + 1.0, n - 1.0);
    vec2 uv0 = vec2((b0 * n + c.r * (n - 1.0) + 0.5) / (n * n), (c.g * (n - 1.0) + 0.5) / n);
    vec2 uv1 = vec2((b1 * n + c.r * (n - 1.0) + 0.5) / (n * n), uv0.y);
    vec3 g = mix(texture(lut, uv0).rgb, texture(lut, uv1).rgb, b - b0);
    COLOR = vec4(mix(c, g, strength), 1.0);
}"""


def build() -> None:
    out = PACK / "grades"
    shutil.rmtree(out, ignore_errors=True)
    for g in GRADES:
        save(lut(g), out / f"{g.name}.png")
    write_json({"size": N, "layout": "N slices of N x N side by side; slice = blue, x = red, y = green",
                "grades": {g.name: f"{g.name}.png" for g in GRADES}, "godotShader": SHADER,
                "notes": "apply to the world before the UI; blend between two grades by strength for time passing"},
               out / "manifest.json")
    from arcpix import iso
    from parts import dioramas as D, backgrounds as BG
    base = iso.finish(iso.render(D.ashen_ford(), backdrop=lambda w, h: BG.compose("day", "hills", w, h,
                                                                                   horizon=int(h * 0.48))), zoom=1)
    tiles = [Tile(graded(base, g).convert("RGBA"), g.name) for g in GRADES]
    save(contact_sheet("Colour grades", tiles, columns=2, zoom=1,
                       subtitle="One map, ten moods: 16-cube LUTs the TV applies after the world and before the UI."),
         PREVIEWS / "35-grades.png")
