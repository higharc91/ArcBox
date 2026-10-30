"""Ground hazards: persistent surface effects that sit on a tile for several turns (burning ground, poison gas, ice
slick, webs, thorns, consecrated ground, oil, caltrops, magical darkness). They're tactical information, so each
reads at a glance and differs by pattern as well as colour.

Each hazard is drawn in tile space (U, V on the 32 px tile top) and projected to the same 64x32 iso diamond as the
UI tile markers (origin (32, 16) on the tile top's centre), so a hazard covers exactly its tile and neighbours join.
Tall parts (flames, gas, thorns) also get an 'over' sprite drawn after units, so they wrap around whoever stands
there. All loops are whole-period.
"""
from __future__ import annotations

import math
import shutil

import numpy as np
from PIL import Image

from arcpix import tex
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet

T = 32                     # tile size in texels
W, H = 64, 32              # projected diamond
OVER_H = 40                # the 'over' sprite: same width, taller, the tile centre at (32, 32)
FRAMES = 8


def _hex(h: str) -> np.ndarray:
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.uint8)


def project(field: np.ndarray, alpha: np.ndarray) -> Image.Image:
    """Tile-space RGB (T x T x 3) + alpha (T x T) -> the 64x32 iso diamond. Each screen pixel samples the texel under
    its centre, so edges are hard and neighbours tile exactly (like the UI markers)."""
    out = np.zeros((H, W, 4), np.uint8)
    for y in range(H):
        for x in range(W):
            a, b = x + 0.5 - 32, 2 * (y + 0.5 - 16)          # screen -> (U - V, U + V) about the tile centre
            u, v = (a + b) / 2 + T / 2, (b - a) / 2 + T / 2
            if 0 <= u < T and 0 <= v < T:
                ui, vi = int(u), int(v)
                out[y, x, :3] = field[vi, ui]
                out[y, x, 3] = alpha[vi, ui]
    return Image.fromarray(out, "RGBA")


def _shade(ramp_: list[str], idx: np.ndarray) -> np.ndarray:
    pal = np.stack([_hex(c) for c in ramp_])
    return pal[np.clip(idx, 0, 5)]


def _uv() -> tuple[np.ndarray, np.ndarray]:
    return np.mgrid[0:T, 0:T][::-1].astype(np.float64)        # (u, v)


def _edge_fade(alpha: np.ndarray) -> np.ndarray:
    """Soften the outermost texel ring so hazards on neighbouring tiles blend instead of drawing a grid."""
    u, v = _uv()
    d = np.minimum(np.minimum(u, T - 1 - u), np.minimum(v, T - 1 - v))
    return np.where(d < 1, (alpha.astype(np.float32) * 0.6).astype(np.uint8), alpha)


# ------------------------------------------------------------------------------------------------- hazards
def burning(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    u, v = _uv()
    n = tex.fbm(T, T, 4, r, octaves=3)
    rp = FAMILIES["magic"]["fire"]
    glow = (n + 0.25 * np.sin(2 * np.pi * (f / FRAMES + n * 2))) > 0.55
    idx = np.where(glow, 4, 1)
    base = _shade(FAMILIES["terrain"]["crust"], np.full((T, T), 1))
    col = np.where(glow[..., None], _shade(rp, idx), base)
    alpha = np.where(glow, 230, 140).astype(np.uint8)
    ground = project(col, _edge_fade(alpha))
    over = Image.new("RGBA", (W, OVER_H))
    from arcpix.canvas import Canvas, SlotTable
    st = SlotTable()
    st.id("fire")
    c = Canvas(W, OVER_H, st)
    ph = 2 * math.pi * f / FRAMES
    for k, (x, y) in enumerate(((20, 33), (32, 37), (44, 33), (26, 29), (38, 29), (32, 25))):
        h = 7 + 3 * math.sin(ph + k * 1.7)
        c.polygon([(x - 2.5, y), (x + 2.5, y), (x + math.sin(ph * 2 + k), y - h)], "fire", 3)
        c.polygon([(x - 1.2, y), (x + 1.2, y), (x, y - h * 0.55)], "fire", 5)
    over = c.render({"fire": rp})
    return ground, over


def poison(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    n1 = tex.fbm(T, T, 4, r, octaves=3)
    n = tex.wrap_roll(n1, f * T // FRAMES, 0)
    rp = FAMILIES["magic"]["nature"]
    idx = np.where(n > 0.62, 4, np.where(n > 0.4, 3, 2))
    alpha = np.where(n > 0.4, 170, 90).astype(np.uint8)
    ground = project(_shade(rp, idx), _edge_fade(alpha))
    from arcpix.canvas import Canvas, SlotTable
    st = SlotTable()
    st.id("gas")
    c = Canvas(W, OVER_H, st)
    for k in range(6):                                  # rising bubbles of gas
        t = (f / FRAMES + k / 6) % 1.0
        x = 16 + (k * 11) % 34
        y = 34 - t * 22
        rr = 1.5 + t * 2
        c.ellipse(x + 0.5, y, rr, rr * 0.8, "gas", 4 if t < 0.5 else 3)
    over = c.render({"gas": rp})
    return ground, over


def ice(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    u, v = _uv()
    n = tex.fbm(T, T, 4, r, octaves=3)
    rp = FAMILIES["terrain"]["ice"]
    idx = np.where(n > 0.6, 4, 3)
    cracks = (np.abs(np.sin(u * 0.7 + v * 0.3 + n * 4)) < 0.08)
    idx = np.where(cracks, 1, idx)
    g = (f / FRAMES) * (T + 16) - 8                     # a glint sweeping across
    glint = np.abs((u + v) / 2 - g) < 1.2
    idx = np.where(glint & ~cracks, 5, idx)
    alpha = np.full((T, T), 210, np.uint8)
    return project(_shade(rp, idx), _edge_fade(alpha)), None


def web(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    u, v = _uv()
    cu, cv = T / 2, T / 2
    ang = np.arctan2(v - cv, u - cu)
    rad = np.hypot(u - cu, v - cv)
    spokes = np.abs(((ang / (2 * np.pi) * 8) % 1) - 0.5) > 0.44
    rings = np.abs(((rad / 4.2) % 1) - 0.5) > 0.4
    strand = (spokes | rings) & (rad < 15)
    shimmer = ((rad / 4.2 - f / FRAMES * 2) % 2) < 0.25
    rp = ramp("#e6e8ee", shift=0.04, spread=0.6)
    idx = np.where(shimmer & strand, 5, 3)
    alpha = np.where(strand, 220, 0).astype(np.uint8)
    return project(_shade(rp, idx), alpha), None


def thorns(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    u, v = _uv()
    n = tex.fbm(T, T, 8, r, octaves=2)
    vines = np.abs(np.sin(u * 0.5 + n * 6) * np.cos(v * 0.4 - n * 4)) > 0.82
    rp = FAMILIES["foliage"]["leaf"]
    alpha = np.where(vines, 230, 0).astype(np.uint8)
    ground = project(_shade(rp, np.where(vines, 2, 0)), alpha)
    from arcpix.canvas import Canvas, SlotTable
    st = SlotTable()
    st.id("vine")
    st.id("thorn")
    c = Canvas(W, OVER_H, st)
    sway = [0, 1, 1, 0, 0, -1, -1, 0][f]
    for k, (x, y) in enumerate(((18, 34), (30, 38), (44, 34), (24, 28), (40, 28))):
        h = 9 + (k % 3) * 2
        tip = (x + sway + (k % 2) * 2 - 1, y - h)
        c.thick_line(x, y, *tip, 2, "vine", 3)
        for t in (0.35, 0.7):
            px_, py_ = x + (tip[0] - x) * t, y + (tip[1] - y) * t
            c.px(px_ + (1 if k % 2 else -2), py_, "thorn", 4)
    over = c.render({"vine": rp, "thorn": FAMILIES["horn"]["bone"]})
    return ground, over


def consecrated(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    u, v = _uv()
    rad = np.hypot(u - T / 2, v - T / 2)
    ang = np.arctan2(v - T / 2, u - T / 2)
    ring = np.abs(rad - 12) < 1.0
    inner = np.abs(rad - 8 + 0.6 * np.sin(2 * np.pi * f / FRAMES)) < 0.7
    runes = (np.abs(rad - 10) < 1.2) & (((ang / (2 * np.pi) * 12 + f / FRAMES) % 1) < 0.3)
    cross = ((np.abs(u - T / 2) < 1) | (np.abs(v - T / 2) < 1)) & (rad < 6)
    rp = FAMILIES["magic"]["holy"]
    idx = np.where(cross | runes, 5, 4)
    mask = ring | inner | runes | cross
    alpha = np.where(mask, 220, 50).astype(np.uint8)
    return project(_shade(rp, idx), _edge_fade(alpha)), None


def oil(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    n = tex.fbm(T, T, 4, r, octaves=3)
    rp = ramp("#2a2530", shift=0.04, spread=1.3)
    sheen = tex.wrap_roll(tex.fbm(T, T, 4, r, octaves=2), f * T // FRAMES, f * T // FRAMES)
    idx = np.where(sheen > 0.75, 4, np.where(n > 0.55, 2, 1))
    rainbow = (sheen > 0.7) & (sheen < 0.75)
    col = _shade(rp, idx)
    col[rainbow] = _hex("#6a8ad0")
    alpha = np.where(n > 0.3, 210, 0).astype(np.uint8)
    return project(col, alpha), None


def caltrops(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    col = np.zeros((T, T, 3), np.uint8)
    alpha = np.zeros((T, T), np.uint8)
    rp = FAMILIES["metal"]["iron"]
    for x, y in tex.poisson(T, T, 4.2, r, tries=600):
        if 2 < x < T - 3 and 2 < y < T - 3:
            for dx, dy, s in ((0, 0, 4), (-1, 0, 2), (1, 0, 2), (0, -1, 5), (0, 1, 1), (-1, 1, 1), (1, 1, 1)):
                col[y + dy, x + dx] = _hex(rp[s])
                alpha[y + dy, x + dx] = 255
            if (x + y + f) % FRAMES == 0:              # a glint hops between spikes
                col[y - 1, x] = _hex(rp[5])
    return project(col, alpha), None


def darkness(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    n = tex.wrap_roll(tex.fbm(T, T, 4, r, octaves=3), 0, f * T // FRAMES)
    rp = FAMILIES["magic"]["shadow"]
    idx = np.where(n > 0.6, 1, 0)
    alpha = np.where(n > 0.25, 225, 180).astype(np.uint8)
    ground = project(_shade(rp, idx), alpha)
    from arcpix.canvas import Canvas, SlotTable
    st = SlotTable()
    st.id("dark")
    c = Canvas(W, OVER_H, st)
    for k in range(5):                                   # wisps curling up
        t = (f / FRAMES + k / 5) % 1.0
        x0 = 18 + k * 7
        for i in range(4):
            c.px(x0 + math.sin(t * 6 + i) * 1.5, 34 - t * 18 - i * 2, "dark", 1 if i else 2)
    return ground, c.render({"dark": rp})


def water_shallow(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    """Flooded tile (Geomancer sinks, Stormcaller rain): a thin film of water with travelling ripples."""
    u, v = _uv()
    rad = np.hypot(u - T / 2, v - T / 2)
    ripple = np.abs(((rad / 5 - f / FRAMES) % 1) - 0.5) > 0.42
    rp = FAMILIES["terrain"]["water"]
    idx = np.where(ripple, 4, 2)
    alpha = np.full((T, T), 140, np.uint8)
    return project(_shade(rp, idx), _edge_fade(alpha)), None


HAZARDS = {   # id: (draw, blocks movement, difficult terrain, damage type, note)
    "burning": (burning, False, False, "fire", "damage on entering and at turn start"),
    "poison": (poison, False, False, "poison", "poisons those who end a turn inside"),
    "ice": (ice, False, True, None, "slippery: movement may slide one extra tile"),
    "web": (web, False, True, None, "restrains; burns away under fire"),
    "thorns": (thorns, False, True, "piercing", "Warden snare ground"),
    "consecrated": (consecrated, False, False, "radiant", "allies heal, undead burn"),
    "oil": (oil, False, True, None, "ignites into burning ground"),
    "caltrops": (caltrops, False, True, "piercing", "damage on entering"),
    "darkness": (darkness, False, False, None, "heavily obscured: blocks line of sight"),
    "flooded": (water_shallow, False, True, None, "conducts lightning"),
}


def build() -> None:
    out = PACK / "hazards"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles = {}, []
    for hid, (draw, blocks, difficult, dmg, note) in HAZARDS.items():
        r_ground, r_over = [], []
        for f in range(FRAMES):
            g, o = draw(f, tex.rng(sum(map(ord, hid))))
            r_ground.append(g)
            r_over.append(o)
        sheet = Image.new("RGBA", (W * FRAMES, H))
        for k, im in enumerate(r_ground):
            sheet.paste(im, (k * W, 0))
        save(sheet, out / f"{hid}.png")
        entry = {"ground": f"{hid}.png", "frames": FRAMES, "fps": 8, "blocks": blocks, "difficult": difficult,
                 "damage": dmg, "note": note}
        if r_over[0] is not None:
            osheet = Image.new("RGBA", (W * FRAMES, OVER_H))
            for k, im in enumerate(r_over):
                osheet.paste(im, (k * W, 0))
            save(osheet, out / f"{hid}_over.png")
            entry["over"] = f"{hid}_over.png"
        entries[hid] = entry
        # preview: a 3x2 patch of the hazard on grass, with the over layer, so the tiling shows
        patch = Image.new("RGBA", (W * 2 + 32, H + 32 + OVER_H))
        for i, j in ((0, 0), (1, 0), (0, 1), (1, 1)):
            x, y = 32 + (i - j) * 32 - 32 + 32, (i + j) * 16 + OVER_H - 8
            patch.alpha_composite(r_ground[0], (x, y))
        if r_over[0] is not None:
            for i, j in ((0, 0), (1, 0), (0, 1), (1, 1)):
                x, y = 32 + (i - j) * 32, (i + j) * 16 + OVER_H - 8 + 16 - 32
                patch.alpha_composite(r_over[0], (x, y))
        tiles.append(Tile(patch, hid, ("difficult · " if difficult else "") + (dmg or "no damage")))
    write_json({"frame": [W, H], "origin": [32, 16], "overFrame": [W, OVER_H], "overOrigin": [32, 32],
                "hazards": entries,
                "notes": {"ground": "draw on the tile top after the terrain, before units (like the UI markers)",
                          "over": "draw after units on that tile, origin on the tile centre",
                          "tiling": "same projection as ui markers: neighbouring hazard tiles join"}},
               out / "manifest.json")
    save(contact_sheet("Ground hazards", tiles, columns=5, zoom=3, checker=True,
                       subtitle="Persistent tile effects: ground layer + an 'over' layer that wraps whoever stands "
                                "there. Same diamond as the UI tile markers."), PREVIEWS / "37-hazards.png")
