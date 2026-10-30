"""Sprite sheets: frames packed in a grid, plus the metadata the game needs to cut them back out."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Sequence

from PIL import Image


@dataclass
class Anim:
    name: str
    frames: list[int]                 # indices into the sheet's frame list
    fps: float = 8.0
    loop: bool = True


@dataclass
class Sheet:
    frame_w: int
    frame_h: int
    columns: int
    anchor: tuple[int, int]           # pixel inside a frame that sits on the ground / pivot
    frames: list[Image.Image] = field(default_factory=list)
    anims: list[Anim] = field(default_factory=list)

    def image(self) -> Image.Image:
        rows = max(1, -(-len(self.frames) // self.columns))
        out = Image.new("RGBA", (self.frame_w * self.columns, self.frame_h * rows), (0, 0, 0, 0))
        for i, f in enumerate(self.frames):
            out.paste(f, ((i % self.columns) * self.frame_w, (i // self.columns) * self.frame_h))
        return out

    def meta(self) -> dict:
        return {
            "frameWidth": self.frame_w,
            "frameHeight": self.frame_h,
            "columns": self.columns,
            "frameCount": len(self.frames),
            "anchor": list(self.anchor),
            "anims": {a.name: {"frames": a.frames, "fps": a.fps, "loop": a.loop} for a in self.anims},
        }


def grid(images: Sequence[Image.Image], columns: int, *, pad: int = 0, bg=(0, 0, 0, 0)) -> Image.Image:
    """Packs same-size images into a grid (no metadata) — for atlases of independent tiles or icons."""
    w, h = images[0].size
    rows = -(-len(images) // columns)
    out = Image.new("RGBA", (columns * (w + pad) + pad, rows * (h + pad) + pad), bg)
    for i, im in enumerate(images):
        out.paste(im, (pad + (i % columns) * (w + pad), pad + (i // columns) * (h + pad)), im)
    return out
