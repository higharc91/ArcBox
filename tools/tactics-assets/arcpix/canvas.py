"""An indexed pixel canvas: every pixel is a (slot, shade) pair, not a colour.

Drawing in slots keeps every part recolourable. A canvas renders two ways:
  * render(palette)  -> an RGBA image in that palette (previews, pre-baked sprites);
  * ramp_map()       -> an RGBA "ramp map": R = slot id, G = shade, B = flags, A = coverage.
                        The game recolours it at runtime with a palette lookup texture
                        (see tools/tactics-assets/README.md, "Recolouring in Godot").

Coordinates are pixels, origin top-left, y down. Slot 0 means empty.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
from PIL import Image

from .palette import OUTLINE, RAMP_LEN, BASE

FLAG_OUTLINE = 1
"""B-channel flag: this pixel is a selective outline of its neighbour's slot."""

MAX_SLOTS = 15


@dataclass
class SlotTable:
    """Names <-> ids for the material slots a drawing uses. Shared by every part of one asset family so their ramp
    maps agree, e.g. "cloth1" is slot 3 on the torso layer *and* on the cape layer."""
    names: list[str] = field(default_factory=lambda: ["<empty>"])

    def id(self, name: str) -> int:
        if name not in self.names:
            if len(self.names) > MAX_SLOTS:
                raise ValueError(f"too many slots (max {MAX_SLOTS}); adding {name}")
            self.names.append(name)
        return self.names.index(name)

    def name(self, slot_id: int) -> str:
        return self.names[slot_id]


def _hex_rgb(h: str) -> tuple[int, int, int]:
    h = h.lstrip("#")
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16)


class Canvas:
    def __init__(self, w: int, h: int, slots: SlotTable):
        self.w, self.h = w, h
        self.slots = slots
        self.slot = np.zeros((h, w), np.uint8)
        self.shade = np.zeros((h, w), np.uint8)
        self.flags = np.zeros((h, w), np.uint8)

    # ----------------------------------------------------------------------------------------------- primitives
    def _ok(self, x: int, y: int) -> bool:
        return 0 <= x < self.w and 0 <= y < self.h

    def px(self, x: int, y: int, slot: str, shade: int = BASE) -> None:
        x, y = int(round(x)), int(round(y))
        if self._ok(x, y):
            self.slot[y, x] = self.slots.id(slot)
            self.shade[y, x] = max(0, min(RAMP_LEN - 1, shade))
            self.flags[y, x] = 0

    def clear(self, x: int, y: int) -> None:
        if self._ok(x, y):
            self.slot[y, x] = 0
            self.flags[y, x] = 0

    def rect(self, x0: float, y0: float, x1: float, y1: float, slot: str, shade: int = BASE) -> None:
        """Filled rectangle, inclusive corners (coordinates round to the nearest pixel)."""
        x0, y0, x1, y1 = (int(round(v)) for v in (x0, y0, x1, y1))
        for y in range(min(y0, y1), max(y0, y1) + 1):
            for x in range(min(x0, x1), max(x0, x1) + 1):
                self.px(x, y, slot, shade)

    def line(self, x0: float, y0: float, x1: float, y1: float, slot: str, shade: int = BASE) -> None:
        """Bresenham line, both ends inclusive."""
        x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
        dx, dy = abs(x1 - x0), -abs(y1 - y0)
        sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
        err = dx + dy
        while True:
            self.px(x0, y0, slot, shade)
            if x0 == x1 and y0 == y1:
                return
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def thick_line(self, x0, y0, x1, y1, width: int, slot: str, shade: int = BASE) -> None:
        """A line `width` pixels wide (centred), drawn as stacked parallel lines."""
        if width <= 1:
            self.line(x0, y0, x1, y1, slot, shade)
            return
        horiz = abs(x1 - x0) >= abs(y1 - y0)
        for o in range(width):
            d = o - (width - 1) / 2
            if horiz:
                self.line(x0, y0 + d, x1, y1 + d, slot, shade)
            else:
                self.line(x0 + d, y0, x1 + d, y1, slot, shade)

    def ellipse(self, cx: float, cy: float, rx: float, ry: float, slot: str, shade: int = BASE) -> None:
        """Filled ellipse with a pixel-friendly boundary (no single-pixel spurs)."""
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                nx = (x + 0.5 - cx) / max(rx, 0.5)
                ny = (y + 0.5 - cy) / max(ry, 0.5)
                if nx * nx + ny * ny <= 1.0:
                    self.px(x, y, slot, shade)

    def polygon(self, pts: list[tuple[float, float]], slot: str, shade: int = BASE) -> None:
        """Filled polygon (even-odd rule, pixel centres)."""
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        for y in range(int(min(ys)) - 1, int(max(ys)) + 2):
            cy = y + 0.5
            nodes: list[float] = []
            j = len(pts) - 1
            for i in range(len(pts)):
                (xi, yi), (xj, yj) = pts[i], pts[j]
                if (yi < cy <= yj) or (yj < cy <= yi):
                    nodes.append(xi + (cy - yi) / (yj - yi) * (xj - xi))
                j = i
            nodes.sort()
            for k in range(0, len(nodes) - 1, 2):
                for x in range(int(np.ceil(nodes[k] - 0.5)), int(np.floor(nodes[k + 1] - 0.5)) + 1):
                    self.px(x, y, slot, shade)
        _ = xs

    # ------------------------------------------------------------------------------------------------- shading
    def mask(self, slot: str | None = None) -> np.ndarray:
        if slot is None:
            return self.slot > 0
        return self.slot == self.slots.id(slot)

    def shade_volume(self, slot: str, *, light=(-1, -1), hi: int = 1, lo: int = 1, base: int = BASE) -> None:
        """Gives a flat-filled slot volume: pixels whose neighbour *towards* the light is not this slot get +hi,
        pixels whose neighbour *away* from it is not this slot get -lo. Light comes from the top-left by default."""
        m = self.mask(slot)
        lx, ly = light
        towards = np.zeros_like(m)
        away = np.zeros_like(m)
        h, w = m.shape
        for y in range(h):
            for x in range(w):
                if not m[y, x]:
                    continue
                tx, ty = x + lx, y + ly
                ax, ay = x - lx, y - ly
                if not (0 <= tx < w and 0 <= ty < h) or not m[ty, tx]:
                    towards[y, x] = True
                if not (0 <= ax < w and 0 <= ay < h) or not m[ay, ax]:
                    away[y, x] = True
        # Only pixels still at the base shade are touched, so hand-placed details (highlights, seams) survive.
        flat = m & (self.shade == base) & (self.flags == 0)
        self.shade[flat & towards & ~away] = min(RAMP_LEN - 1, base + hi)
        self.shade[flat & away & ~towards] = max(0, base - lo)

    def shade_form(self, slot: str, *, base: int = BASE, light_left: bool = True) -> None:
        """Cylindrical form shading for a flat-filled slot: across each horizontal run the lit side (left) gets +1
        and the far side -1; down each vertical run the top rows get +1 and the underside -1. Hand-placed shades
        (anything not at `base`) are left alone."""
        m = self.mask(slot)
        flat = m & (self.shade == base) & (self.flags == 0)
        if not flat.any():
            return
        h, w = m.shape
        dx = np.zeros((h, w), np.int8)
        dy = np.zeros((h, w), np.int8)
        for y in range(h):
            x = 0
            while x < w:
                if not m[y, x]:
                    x += 1
                    continue
                a = x
                while x < w and m[y, x]:
                    x += 1
                n = x - a
                for k in range(n):
                    t = (k + 0.5) / n
                    if not light_left:
                        t = 1 - t
                    if n >= 2 and t < 0.3:
                        dx[y, a + k] = 1
                    elif n >= 2 and t > 0.68:
                        dx[y, a + k] = -1
        for x in range(w):
            y = 0
            while y < h:
                if not m[y, x]:
                    y += 1
                    continue
                a = y
                while y < h and m[y, x]:
                    y += 1
                n = y - a
                if n < 4:
                    continue
                for k in range(n):
                    t = (k + 0.5) / n
                    if t < 0.16:
                        dy[a + k, x] = 1
                    elif t > 0.82:
                        dy[a + k, x] = -1
        delta = np.clip(dx.astype(np.int16) + dy, -2, 1)
        sh = self.shade.astype(np.int16)
        sh = np.where(flat, np.clip(base + delta, 0, RAMP_LEN - 1), sh)
        self.shade = sh.astype(np.uint8)

    def dither_band(self, slot: str, shade_a: int, shade_b: int, y0: int, y1: int) -> None:
        """Checkerboard between two shades over rows y0..y1 of a slot (soft transitions on cloth and terrain)."""
        sid = self.slots.id(slot)
        for y in range(max(0, y0), min(self.h, y1 + 1)):
            for x in range(self.w):
                if self.slot[y, x] == sid:
                    self.shade[y, x] = shade_a if (x + y) % 2 == 0 else shade_b

    # ------------------------------------------------------------------------------------------------- compose
    def blit(self, other: "Canvas", dx: int = 0, dy: int = 0) -> None:
        """Draws `other` over this canvas (non-empty pixels win). Slot tables must be the same object."""
        assert other.slots is self.slots, "blit needs a shared SlotTable"
        for y in range(other.h):
            ty = y + dy
            if not 0 <= ty < self.h:
                continue
            for x in range(other.w):
                tx = x + dx
                if 0 <= tx < self.w and other.slot[y, x]:
                    self.slot[ty, tx] = other.slot[y, x]
                    self.shade[ty, tx] = other.shade[y, x]
                    self.flags[ty, tx] = other.flags[y, x]

    def mirrored(self) -> "Canvas":
        c = Canvas(self.w, self.h, self.slots)
        c.slot = self.slot[:, ::-1].copy()
        c.shade = self.shade[:, ::-1].copy()
        c.flags = self.flags[:, ::-1].copy()
        return c

    def copy(self) -> "Canvas":
        c = Canvas(self.w, self.h, self.slots)
        c.slot, c.shade, c.flags = self.slot.copy(), self.shade.copy(), self.flags.copy()
        return c

    def outline(self, *, diagonal: bool = False, skip: set[str] | None = None) -> None:
        """Selective outline: every empty pixel touching a filled one becomes an outline pixel of that neighbour's
        slot (flagged), so it renders as a darkened version of the neighbour rather than flat black."""
        skip_ids = {self.slots.id(s) for s in (skip or set())}
        filled = (self.slot > 0) & (self.flags == 0)
        out_slot = np.zeros_like(self.slot)
        dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)]
        if diagonal:
            dirs += [(1, 1), (-1, -1), (1, -1), (-1, 1)]
        h, w = self.slot.shape
        for y in range(h):
            for x in range(w):
                if self.slot[y, x]:
                    continue
                for ddx, ddy in dirs:
                    nx, ny = x + ddx, y + ddy
                    if 0 <= nx < w and 0 <= ny < h and filled[ny, nx] and self.slot[ny, nx] not in skip_ids:
                        out_slot[y, x] = self.slot[ny, nx]
                        break
        sel = out_slot > 0
        self.slot[sel] = out_slot[sel]
        self.shade[sel] = 0
        self.flags[sel] = FLAG_OUTLINE

    # -------------------------------------------------------------------------------------------------- render
    def render(self, palette: dict[str, list[str]], *, outline_mix: float = 0.6) -> Image.Image:
        """RGBA in `palette` (slot name -> ramp). Outline pixels mix the slot's shade 0 towards OUTLINE."""
        img = np.zeros((self.h, self.w, 4), np.uint8)
        ol = np.array(_hex_rgb(OUTLINE), np.float32)
        cache: dict[tuple[int, int, int], tuple[int, int, int]] = {}
        for y in range(self.h):
            for x in range(self.w):
                sid = int(self.slot[y, x])
                if not sid:
                    continue
                key = (sid, int(self.shade[y, x]), int(self.flags[y, x]))
                if key not in cache:
                    name = self.slots.name(sid)
                    if name not in palette:
                        raise KeyError(f"palette has no ramp for slot '{name}'")
                    rgb = np.array(_hex_rgb(palette[name][key[1]]), np.float32)
                    if key[2] & FLAG_OUTLINE:
                        rgb = rgb * (1 - outline_mix) + ol * outline_mix
                    cache[key] = tuple(int(c) for c in rgb)  # type: ignore[assignment]
                img[y, x, :3] = cache[key]
                img[y, x, 3] = 255
        return Image.fromarray(img, "RGBA")

    def ramp_map(self) -> Image.Image:
        """R = slot id * 16, G = shade * 32, B = flags * 255, A = 255 where drawn. Lossless, palette-independent."""
        img = np.zeros((self.h, self.w, 4), np.uint8)
        drawn = self.slot > 0
        img[..., 0] = self.slot * 16
        img[..., 1] = self.shade * 32
        img[..., 2] = np.where(self.flags & FLAG_OUTLINE, 255, 0)
        img[..., 3] = np.where(drawn, 255, 0)
        return Image.fromarray(img, "RGBA")
