"""Portraits for the turn order, unit cards and the phone: a head-and-shoulders bust of any character.

A portrait isn't separate art. It's the character's own layers (minus weapon and off-hand, which would poke into the
frame), composed in the front idle pose and cropped around the head. So a custom hero gets a portrait that matches
their sprite exactly, including every later gear change. The manifest gives the crop per build, so the game can
make portraits at runtime the same way; baked portraits ship for every preset and enemy.

Two moods: "idle" and "hurt" (the hurt pose's face), which the UI swaps in at low HP.
"""
from __future__ import annotations

import shutil

import numpy as np
from PIL import Image

from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import ramp
from arcpix.preview import Tile, contact_sheet, strip
from . import characters as C
from .recipes import ALL, PARTY, CLASSES, ENEMIES
from .rig import ANIMS, BUILDS

CROP = 20                                # source window around the head
SCALE = 2                                # drawn at 2x: a portrait is a close-up
SIZE = CROP * SCALE
SKIP = ("weapon", "offhand")             # held items crowd the frame
MOODS = {"idle": ANIMS["idle"][2][0], "hurt": ANIMS["hurt"][2][0]}

BACKDROPS = {                              # backdrop ramps: allies warm-neutral, enemies red, neutral navy
    "ally": ramp("#3a4a7a", shift=0.05),
    "enemy": ramp("#7a2f36", shift=0.06),
    "neutral": ramp("#3f4157", shift=0.04),
}


def crop_box(build: str) -> tuple[int, int, int, int]:
    """The 20x20 window around a build's head: a little headroom, the face in the upper middle, shoulders below."""
    b = BUILDS[build]
    top = b.head_top - 4
    cx = 16
    return (cx - CROP // 2, top, cx + CROP // 2, top + CROP)


def bust(recipe, mood: str = "idle") -> Image.Image:
    layers = {k: v for k, v in recipe.layers.items() if k not in SKIP}
    r = C.Recipe(recipe.id, recipe.name, recipe.build, layers, recipe.palette, recipe.tags)
    img = C.compose(r, "front", MOODS[mood]).render(recipe.ramps())
    x0, y0, x1, y1 = crop_box(recipe.build)
    canvas = Image.new("RGBA", (CROP, CROP))
    canvas.alpha_composite(img.crop((x0, max(0, y0), x1, min(img.height, y1))), (0, max(0, -y0)))
    return canvas


def backdrop(kind: str) -> Image.Image:
    """A soft radial plate in the backdrop ramp, banded (not smooth) to stay pixel art."""
    rp = BACKDROPS[kind]
    ys, xs = np.mgrid[0:CROP, 0:CROP]
    d = np.sqrt((xs - 8.5) ** 2 + (ys - 7.5) ** 2) / 14.0
    idx = np.clip(4 - (d * 4.2).astype(int), 1, 4)
    # checker the band edges so the bands dissolve into each other
    edge = ((d * 4.2) % 1 > 0.8) & ((xs + ys) % 2 == 0)
    idx = np.where(edge, np.maximum(1, idx - 1), idx)
    arr = np.zeros((CROP, CROP, 4), np.uint8)
    for k in range(6):
        h = rp[k].lstrip("#")
        arr[idx == k, :3] = [int(h[i:i + 2], 16) for i in (0, 2, 4)]
    arr[..., 3] = 255
    return Image.fromarray(arr, "RGBA")


def portrait(recipe, mood: str = "idle", kind: str | None = None) -> Image.Image:
    kind = kind or ("enemy" if "enemy" in recipe.tags else "ally")
    out = backdrop(kind)
    out.alpha_composite(bust(recipe, mood))
    return out.resize((SIZE, SIZE), Image.NEAREST)


def build() -> None:
    out = PACK / "portraits"
    shutil.rmtree(out, ignore_errors=True)
    for kind in BACKDROPS:
        save(backdrop(kind).resize((SIZE, SIZE), Image.NEAREST), out / "backdrops" / f"{kind}.png")
    entries = {}
    for r in ALL:
        files = {}
        for mood in MOODS:
            rel = f"{r.id}_{mood}.png"
            save(portrait(r, mood), out / rel)
            files[mood] = rel
        entries[r.id] = files
    write_json({
        "size": SIZE,
        "scale": SCALE,
        "crop": {b: list(crop_box(b)) for b in BUILDS},
        "skipLayers": list(SKIP),
        "moods": {"idle": {"anim": "idle", "frame": 0}, "hurt": {"anim": "hurt", "frame": 0}},
        "backdrops": {k: f"backdrops/{k}.png" for k in BACKDROPS},
        "presets": entries,
        "howTo": "compose the character's layers (except skipLayers) for the front facing in the mood's pose, "
                 "crop to crop[build], draw over a backdrop, then scale by `scale` (nearest)",
    }, out / "manifest.json")

    tiles = []
    for group in (PARTY, CLASSES, ENEMIES):
        for r in group:
            tiles.append(Tile(strip([portrait(r, "idle"), portrait(r, "hurt")], 1, gap=3), r.name,
                              "idle · hurt"))
    save(contact_sheet("Portraits", tiles, columns=8, zoom=2,
                       subtitle="Made from each character's own layers, so custom heroes get matching portraits. "
                                "Idle and hurt moods; ally and enemy backdrops."),
         PREVIEWS / "14-portraits.png")
