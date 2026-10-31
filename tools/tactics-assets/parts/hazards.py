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
from arcpix.canvas import Canvas, SlotTable
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
def _over() -> tuple[Canvas, SlotTable]:
    st = SlotTable()
    for name in ("a", "b", "c"):
        st.id(name)
    return Canvas(W, OVER_H, st), st


def _tile_pts(n: int, r) -> list[tuple[float, float]]:
    """`n` points spread over the tile's diamond in the over sprite's coordinates (tile centre at (32, 32))."""
    out = []
    for _ in range(n):
        u, v = r.uniform(-0.8, 0.8), r.uniform(-0.8, 0.8)
        out.append((32 + (u - v) * 16, 32 + (u + v) * 8))
    return sorted(out, key=lambda p: p[1])


def burning(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    """Burning ground: black crust split by cracks that glow and pulse, and tongues of flame licking up all over the
    tile with embers rising off them."""
    u, v = _uv()
    n = tex.fbm(T, T, 4, r, octaves=3)
    rp = FAMILIES["magic"]["fire"]
    ph = 2 * np.pi * f / FRAMES
    crack = np.abs(np.sin(n * 13)) < 0.22
    pulse = 0.5 + 0.5 * np.sin(ph + n * 6)
    idx = np.where(crack, np.where(pulse > 0.5, 5, 4), np.where(n > 0.62, 2, 1))
    crust = _shade(FAMILIES["terrain"]["crust"], np.where(n > 0.5, 2, 1))
    col = np.where(crack[..., None] | (n > 0.62)[..., None], _shade(rp, idx), crust)
    alpha = np.where(crack, 240, 200).astype(np.uint8)
    ground = project(col, _edge_fade(alpha))
    c, _ = _over()
    pts = _tile_pts(9, tex.rng(77))
    for k, (x, y) in enumerate(pts):
        h = 6 + 3 * (k % 3) + 3 * math.sin(ph + k * 1.7)
        sway = math.sin(ph * 2 + k) * 1.2
        c.polygon([(x - 2.5, y), (x + 2.5, y), (x + sway, y - h)], "a", 2)
        c.polygon([(x - 1.6, y), (x + 1.6, y), (x + sway, y - h * 0.8)], "a", 3)
        c.polygon([(x - 0.8, y), (x + 0.8, y), (x + sway * 0.6, y - h * 0.5)], "a", 5)
    for k in range(6):
        t = (f / FRAMES + k / 6) % 1.0
        x, y = pts[k * 3 % len(pts)]
        c.px(x + math.sin(t * 6 + k) * 2, y - 6 - t * 22, "b", 5 if t < 0.5 else 3)
    over = c.render({"a": rp, "b": FAMILIES["magic"]["holy"], "c": rp})
    return ground, over


def poison(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    n1 = tex.fbm(T, T, 4, r, octaves=3)
    n = tex.wrap_roll(n1, f * T // FRAMES, 0)
    rp = FAMILIES["magic"]["nature"]
    idx = np.where(n > 0.62, 4, np.where(n > 0.4, 3, 2))
    alpha = np.where(n > 0.4, 170, 90).astype(np.uint8)
    ground = project(_shade(rp, idx), _edge_fade(alpha))
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


def acid(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    """An acid pool: a glossy yellow-green film with a bright rim, its sheen sliding, and bubbles swelling and popping
    with thin fumes."""
    u, v = _uv()
    n = tex.fbm(T, T, 4, r, octaves=3)
    rp = ramp("#b4dc34", shift=0.09, spread=0.95)
    sheen = tex.wrap_roll(tex.fbm(T, T, 4, r, octaves=2), f * T // FRAMES, 0)
    inside = n > 0.2
    rim = inside & (n < 0.26)
    idx = np.where(rim, 4, np.where(sheen > 0.72, 5, np.where(n > 0.6, 3, 2)))
    alpha = np.where(inside, 215, 0).astype(np.uint8)
    ground = project(_shade(rp, idx), _edge_fade(alpha))
    c, _ = _over()
    for k, (x, y) in enumerate(_tile_pts(7, tex.rng(78))):
        t = (f / FRAMES + k * 0.37) % 1.0
        if t < 0.6:
            rr = 0.6 + t * 2.2
            c.ellipse(x + 0.5, y - rr, rr, rr, "a", 3)
            c.px(x - rr * 0.5, y - rr * 1.5, "a", 5)
        elif t < 0.75:
            for dx, dy in ((-2, -2), (2, -2), (0, -4)):
                c.px(x + dx, y + dy, "a", 4)
        else:
            c.px(x, y - 4 - (t - 0.75) * 24, "b", 2)
    return ground, c.render({"a": rp, "b": FAMILIES["cloth"]["ivory"], "c": rp})


def spikes(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    """Spike growth: a mat of brambles over the ground and hooked thorns standing up out of it, glinting in turn.
    Reads as 'don't walk here' from its spikes, not its colour."""
    u, v = _uv()
    n = tex.fbm(T, T, 8, r, octaves=2)
    vines = np.abs(np.sin(u * 0.7 + n * 7)) < 0.25
    vines |= np.abs(np.sin(v * 0.6 - n * 5)) < 0.2
    rp = FAMILIES["foliage"]["bark"]
    idx = np.where(vines, np.where(n > 0.5, 3, 2), 1)
    alpha = np.where(vines, 235, 70).astype(np.uint8)
    ground = project(_shade(rp, idx), _edge_fade(alpha))
    c, _ = _over()
    for k, (x, y) in enumerate(_tile_pts(14, tex.rng(79))):
        h = 4 + (k % 3) * 2
        lean = (k % 5 - 2) * 0.6
        c.polygon([(x - 1.5, y), (x + 1.5, y), (x + lean, y - h)], "a", 3)
        c.px(x + lean, y - h, "b", 5 if (k + f) % FRAMES == 0 else 4)
        c.px(x - 1, y - 1, "a", 2)
    return ground, c.render({"a": FAMILIES["foliage"]["dead"], "b": FAMILIES["horn"]["bone"], "c": rp})


def fog(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    """Fog cloud: a pale ground haze and banks of fog in the over layer, rolling sideways a whole noise period per
    loop. Heavily obscured: it should hide the unit's feet and legs."""
    rp = ramp("#c8d0dc", shift=0.04, spread=0.6)
    n = tex.wrap_roll(tex.fbm(T, T, 4, r, octaves=3), f * T // FRAMES, 0)
    ground = project(_shade(rp, np.where(n > 0.5, 3, 2)), _edge_fade(np.full((T, T), 150, np.uint8)))
    big = tex.fbm(64, 40, 4, tex.rng(80), octaves=3)
    big = np.roll(big, f * 64 // FRAMES, 1)
    ys, xs = np.mgrid[0:OVER_H, 0:W]
    e = ((xs - 32) / 30) ** 2 + ((ys - 29) / 10) ** 2
    body = (big * 0.8 + (1 - e) * 0.7) > 0.8
    out = np.zeros((OVER_H, W, 4), np.uint8)
    sh = np.where(big > 0.6, 4, 3)
    out[..., :3] = _shade(rp, sh)
    out[..., 3] = np.where(body & (ys < 38), np.where((xs + ys) % 2 == 0, 200, 150), 0)
    return ground, Image.fromarray(out, "RGBA")


def sleet(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    """Sleet storm: slush and rime over the tile and sleet streaking down across it (difficult, puts out fire)."""
    u, v = _uv()
    n = tex.fbm(T, T, 4, r, octaves=3)
    rp = FAMILIES["terrain"]["snow"]
    idx = np.where(n > 0.65, 5, np.where(n > 0.45, 4, 3))
    alpha = np.where(n > 0.3, 200, 110).astype(np.uint8)
    ground = project(_shade(rp, idx), _edge_fade(alpha))
    c, _ = _over()
    for k in range(14):
        t = (f / FRAMES + k * 0.29) % 1.0
        x = 8 + (k * 13) % 48 + t * 6
        y = -4 + t * 40
        c.line(x, y, x + 1, y + 3, "a", 5 if k % 3 else 4)
    return ground, c.render({"a": FAMILIES["magic"]["frost"], "b": rp, "c": rp})


def electrified(f: int, r) -> tuple[Image.Image, Image.Image | None]:
    """Electrified ground (call lightning, storm sphere, a flooded tile struck): scorched ground with arcs that
    jump across it, a different set every frame."""
    u, v = _uv()
    n = tex.fbm(T, T, 4, r, octaves=3)
    rp = FAMILIES["magic"]["storm"]
    scorch = _shade(FAMILIES["terrain"]["crust"], np.where(n > 0.55, 2, 1))
    col = scorch.copy()
    alpha = np.full((T, T), 120, np.uint8)
    rr = tex.rng(900 + f)
    for _ in range(3):
        x, y = rr.uniform(4, T - 4), rr.uniform(4, T - 4)
        for _ in range(10):
            x2, y2 = np.clip(x + rr.uniform(-4, 4), 1, T - 2), np.clip(y + rr.uniform(-4, 4), 1, T - 2)
            for t in np.linspace(0, 1, 6):
                xi, yi = int(x + (x2 - x) * t), int(y + (y2 - y) * t)
                col[yi, xi] = _hex(rp[5])
                alpha[yi, xi] = 255
            x, y = x2, y2
    ground = project(col, _edge_fade(alpha))
    c, _ = _over()
    for k in range(2):
        x = rr.uniform(16, 48)
        y = 32 + rr.uniform(-6, 6)
        px_, py_ = x, y
        for d in range(5):
            nx, ny = px_ + rr.uniform(-3, 3), py_ - rr.uniform(2, 4)
            c.line(px_, py_, nx, ny, "a", 5 if d < 3 else 4)
            px_, py_ = nx, ny
    return ground, c.render({"a": rp, "b": rp, "c": rp})


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
    "acid": (acid, False, False, "acid", "acid pool: damage on entering and at turn start"),
    "spikes": (spikes, False, True, "piercing", "spike growth: damage for every tile moved inside"),
    "fog": (fog, False, False, None, "fog cloud: heavily obscured, blocks line of sight; wind disperses it"),
    "sleet": (sleet, False, True, None, "sleet storm: difficult, may knock prone, puts out fire"),
    "electrified": (electrified, False, False, "lightning", "lightning damage at turn start"),
}


def build() -> None:
    out = PACK / "hazards"
    shutil.rmtree(out, ignore_errors=True)
    entries, tiles, anim = {}, [], {}
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
        anim[hid] = (r_ground, r_over)
    write_json({"frame": [W, H], "origin": [32, 16], "overFrame": [W, OVER_H], "overOrigin": [32, 32],
                "hazards": entries,
                "notes": {"ground": "draw on the tile top after the terrain, before units (like the UI markers)",
                          "over": "draw after units on that tile, origin on the tile centre",
                          "tiling": "same projection as ui markers: neighbouring hazard tiles join"}},
               out / "manifest.json")
    save(contact_sheet("Ground hazards", tiles, columns=5, zoom=3, checker=True,
                       subtitle="Persistent tile effects: ground layer + an 'over' layer that wraps whoever stands "
                                "there. Same diamond as the UI tile markers."), PREVIEWS / "37-hazards.png")
    _gif(anim)


def _patch(ground: Image.Image, over: Image.Image | None) -> Image.Image:
    """A 2x2 patch of one hazard frame: grounds first, then overs back to front, as the game draws them."""
    patch = Image.new("RGBA", (W * 2 + 32, H + 32 + OVER_H))
    cells = ((0, 0), (1, 0), (0, 1), (1, 1))
    for i, j in cells:
        patch.alpha_composite(ground, (32 + (i - j) * 32, (i + j) * 16 + OVER_H - 8))
    if over is not None:
        for i, j in cells:
            patch.alpha_composite(over, (32 + (i - j) * 32, (i + j) * 16 + OVER_H - 8 + 16 - 32))
    return patch


def _gif(anim: dict) -> None:
    """Every hazard animating as a 2x2 patch at 3x, so the loops and the tiling can be judged."""
    from arcpix.preview import BG, PANEL, INK, font
    from PIL import ImageDraw
    cols, z = 5, 3
    pw, ph = (W * 2 + 32) * z, (H + 32 + OVER_H) * z
    rows = -(-len(anim) // cols)
    frames = []
    for f in range(FRAMES * 2):
        img = Image.new("RGBA", (cols * (pw + 8) + 8, rows * (ph + 24) + 40), BG)
        d = ImageDraw.Draw(img)
        d.text((10, 10), "Ground hazards (animated)", font=font(18, True), fill=INK)
        for n, (hid, (g, o)) in enumerate(anim.items()):
            x, y = 8 + (n % cols) * (pw + 8), 40 + (n // cols) * (ph + 24)
            d.rounded_rectangle((x, y, x + pw, y + ph), 6, fill=PANEL)
            p = _patch(g[f % FRAMES], o[f % FRAMES])
            img.alpha_composite(p.resize((p.width * z, p.height * z), Image.NEAREST), (x, y))
            d.text((x + 4, y + ph + 4), hid, font=font(12, True), fill=INK)
        frames.append(img.convert("RGB").quantize(colors=255, method=Image.Quantize.MEDIANCUT))
    frames[0].save(PREVIEWS / "anim-hazards.gif", save_all=True, append_images=frames[1:], duration=125, loop=0,
                   optimize=True, disposal=1)
