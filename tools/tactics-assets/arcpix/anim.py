"""Animated previews: plays sprite-sheet animations into a GIF so people can see the motion, not just the frames."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence

from PIL import Image, ImageDraw

from .preview import BG, PANEL, INK, MUTED, EDGE, font, scale


@dataclass
class Actor:
    """One character on the stage: its frames, and per beat which frame to show."""
    label: str
    sub: str
    frames: list[Image.Image]
    anchor: tuple[int, int]
    mirror: bool = False


def stage_gif(path, title: str, actors: Sequence[Actor], beats: Sequence[tuple[str, list[list[int]], int]], *,
              zoom: int = 5, columns: int | None = None, cell_w: int = 48, cell_h: int = 52) -> None:
    """Renders a GIF where every actor plays the same beat at once.

    beats: (caption, per-actor frame list, ms per frame). Each actor's frame list for a beat may differ in length
    (e.g. a swing for the fighter, a shot for the ranger); shorter lists hold their last frame.
    """
    cols = columns or len(actors)
    rows = -(-len(actors) // cols)
    cw, ch = cell_w * zoom, cell_h * zoom
    head, foot = 70, 46
    W = cols * (cw + 12) + 28
    H = head + rows * (ch + foot) + 10
    f_title, f_beat, f_label, f_sub = font(24, True), font(18, True), font(15, True), font(12)

    frames: list[Image.Image] = []
    durations: list[int] = []
    for caption, per_actor, ms in beats:
        n = max(len(x) for x in per_actor)
        for k in range(n):
            img = Image.new("RGBA", (W, H), BG)
            d = ImageDraw.Draw(img)
            d.text((20, 16), title, font=f_title, fill=INK)
            tw = d.textlength(caption, font=f_beat)
            d.rounded_rectangle((W - tw - 44, 14, W - 18, 44), 8, fill=(46, 38, 20, 255), outline=EDGE)
            d.text((W - tw - 31, 18), caption, font=f_beat, fill=(255, 231, 168, 255))
            for i, a in enumerate(actors):
                x = 14 + (i % cols) * (cw + 12)
                y = head + (i // cols) * (ch + foot)
                d.rounded_rectangle((x, y, x + cw, y + ch), 10, fill=PANEL)
                gx, gy = x + cw // 2, y + ch - 7 * zoom          # where the anchor (feet) lands
                d.ellipse((gx - 8 * zoom, gy - 2 * zoom, gx + 8 * zoom, gy + 2 * zoom), fill=(14, 15, 28, 255))
                seq = per_actor[i]
                fr = a.frames[seq[min(k, len(seq) - 1)]]
                if a.mirror:
                    fr = fr.transpose(Image.FLIP_LEFT_RIGHT)
                ax = a.anchor[0] if not a.mirror else fr.width - 1 - a.anchor[0]
                big = scale(fr, zoom)
                img.alpha_composite(big, (gx - ax * zoom, gy - a.anchor[1] * zoom))
                d.text((x + 4, y + ch + 6), a.label, font=f_label, fill=INK)
                d.text((x + 4, y + ch + 25), a.sub, font=f_sub, fill=MUTED)
            frames.append(img.convert("RGB").quantize(colors=255, method=Image.Quantize.MEDIANCUT))
            durations.append(ms)
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True,
                   disposal=1)
