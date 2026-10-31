"""Titles and banners: the game's logo, the big moment banners (VICTORY, DEFEAT, LEVEL UP, YOUR TURN, BOSS), the
chapter ribbon, and the pixel font they're set in.

The font is a hand-authored 5x7 capital set. The game can use it directly (the atlas ships with advances), and the
title renderer scales it up and dresses it: a vertical ramp, a lit top bevel, a dark rim and a drop shadow, which is
what makes big pixel lettering feel cast rather than typed.
"""
from __future__ import annotations

import shutil

import numpy as np
from PIL import Image

from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, OUTLINE, ramp
from arcpix.preview import Tile, contact_sheet, stack

GAME_NAME = "Wardenflame"
"""The game's display name, the only place it lives: the logo, the title screen and anything else that shows the name
are generated from it, so a rename is this one edit and a rebuild (`build.py title chapters`). Any 1-3 words of A-Z,
0-9 and simple punctuation work; see `logo()` for how the words are laid out."""
LOGO_SPLIT = 0
"""How many leading words of GAME_NAME form the big top line; the rest become the spaced-out subtitle. 0 puts the whole
name on one line (best for a single word, or two short ones)."""
CAMPAIGN = "Embers of the Vale"
"""The current campaign, set small under the logo between gold rules ("" for none)."""

GLYPHS: dict[str, list[str]] = {
    "A": [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "B": ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
    "C": [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
    "D": ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
    "E": ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
    "F": ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
    "G": [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####"],
    "H": ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "I": [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
    "J": ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
    "K": ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
    "L": ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
    "M": ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
    "N": ["#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#"],
    "O": [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "P": ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
    "Q": [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
    "R": ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
    "S": [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
    "T": ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
    "U": ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "V": ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
    "W": ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#."],
    "X": ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
    "Y": ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
    "Z": ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
    "0": [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
    "1": ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
    "2": [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
    "3": ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
    "4": ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
    "5": ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
    "6": ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
    "7": ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
    "8": [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
    "9": [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
    "!": ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
    "?": [".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.."],
    ".": [".....", ".....", ".....", ".....", ".....", ".....", "..#.."],
    ",": [".....", ".....", ".....", ".....", ".....", "..#..", ".#..."],
    ":": [".....", "..#..", ".....", ".....", ".....", "..#..", "....."],
    "-": [".....", ".....", ".....", ".###.", ".....", ".....", "....."],
    "'": ["..#..", "..#..", ".....", ".....", ".....", ".....", "....."],
    "+": [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
    "/": ["....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...."],
    "·": [".....", ".....", ".....", "..#..", ".....", ".....", "....."],
    " ": [".....", ".....", ".....", ".....", ".....", ".....", "....."],
}
GW, GH = 5, 7
NARROW = {"I": 3, "1": 3, "!": 1, ".": 1, ",": 2, ":": 1, "'": 1, " ": 3, "·": 1}   # advance overrides

STYLES = {
    "gold": FAMILIES["metal"]["gold"], "silver": FAMILIES["metal"]["silver"],
    "red": ramp("#d8423a", shift=0.07), "blue": ramp("#5aa8f0", shift=0.05),
    "green": ramp("#5fd06a", shift=0.06), "violet": ramp("#9a6ae0", shift=0.06),
}


def _hex(h: str) -> np.ndarray:
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


def _advance(ch: str) -> int:
    if ch in NARROW:
        return NARROW[ch]
    rows = GLYPHS[ch]
    cols = [x for x in range(GW) if any(r[x] == "#" for r in rows)]
    return (max(cols) + 1) if cols else 3


def _bitmap(text: str, spacing: int = 1) -> np.ndarray:
    """The text as a 0/1 array at 1x, with proportional advances."""
    width = sum(_advance(ch) + spacing for ch in text) - spacing
    out = np.zeros((GH, max(1, width)), np.uint8)
    x = 0
    for ch in text:
        rows = GLYPHS[ch]
        left = 1 if ch in "I1" else (2 if ch in "!.:'" else (1 if ch == "," else 0))
        for y, row in enumerate(rows):
            for gx, v in enumerate(row):
                if v == "#" and 0 <= x + gx - left < width:
                    out[y, x + gx - left] = 1
        x += _advance(ch) + spacing
    return out


def title_text(text: str, style: str = "gold", scale: int = 3, spacing: int = 1, rim: int = 2,
               depth: int = 0) -> Image.Image:
    """Big cast lettering: glyphs scaled up, shaded top-to-bottom through a ramp, a lit top bevel and a dark lower
    edge per stroke, a rim in the outline colour and a soft drop shadow. `depth` extrudes the letters downwards in
    the ramp's deep shades, so a logo reads as a solid cast block rather than a flat cut-out."""
    bm = _bitmap(text, spacing)
    big = np.kron(bm, np.ones((scale, scale), np.uint8))
    h, w = big.shape
    pad = rim + 3
    W, H = w + pad * 2, h + pad * 2 + depth
    mask = np.zeros((H, W), bool)
    mask[pad:pad + h, pad:pad + w] = big.astype(bool)
    face = mask.copy()
    side = np.zeros_like(mask)
    for k in range(1, depth + 1):
        side |= np.roll(face, k, 0)
    side &= ~face
    mask = face | side
    rp = [_hex(c) for c in STYLES[style]]
    rgb = np.zeros((H, W, 3), np.float32)
    ys = np.arange(H)[:, None].repeat(W, 1)
    t = np.clip((ys - pad) / max(1, h - 1), 0, 1)
    shade = np.where(t < 0.25, 5, np.where(t < 0.5, 4, np.where(t < 0.8, 3, 2)))
    up = np.roll(mask, 1, 0)
    down = np.roll(mask, -1, 0)
    shade = np.where(mask & ~up, 5, shade)                  # lit top edge of every stroke
    shade = np.where(mask & ~down, np.maximum(shade - 1, 1), shade)
    if depth:
        below = np.roll(side, -1, 0) & side
        shade = np.where(side, np.where(below, 1, 0), shade)
        shade = np.where(face & np.roll(side, -1, 0), np.maximum(shade, 2), shade)
    for k in range(6):
        rgb[shade == k] = rp[k]
    # rim and shadow
    rim_mask = mask.copy()
    for _ in range(rim):
        rim_mask = rim_mask | np.roll(rim_mask, 1, 0) | np.roll(rim_mask, -1, 0) | np.roll(rim_mask, 1, 1) | \
            np.roll(rim_mask, -1, 1)
    shadow = np.roll(np.roll(rim_mask, 2, 0), 2, 1) & ~rim_mask
    out = np.zeros((H, W, 4), np.uint8)
    ol = _hex(OUTLINE)
    out[shadow, :3] = ol
    out[shadow, 3] = 150
    out[rim_mask & ~mask, :3] = ol
    out[rim_mask & ~mask, 3] = 255
    out[mask, :3] = rgb[mask].astype(np.uint8)
    out[mask, 3] = 255
    return Image.fromarray(out, "RGBA")


def ribbon(width: int, height: int = 22, colour: str = "#2f3f72") -> Image.Image:
    """A banner ribbon with forked tails and gold edges, for chapter titles and moment banners."""
    base = ramp(colour, shift=0.05)
    gold = FAMILIES["metal"]["gold"]
    W = width + 24
    img = np.zeros((height + 6, W, 4), np.uint8)
    for y in range(height):
        for x in range(W):
            tail = x < 12 or x >= W - 12
            if tail:
                d = x if x < 12 else W - 1 - x
                notch = abs(y - height / 2) < (12 - d) * 0.45
                if notch or y < 3 or y > height - 4:
                    continue
                y_ = y + 3                                    # tails sit lower: the band folds behind
                img[y_, x, :3] = _hex(base[2])
                img[y_, x, 3] = 255
            else:
                k = 4 if y < 2 else (3 if y < height - 3 else 2)
                c = gold[k] if y in (1, height - 2) else base[k]
                img[y, x, :3] = _hex(c)
                img[y, x, 3] = 255
    return Image.fromarray(img, "RGBA")


def laurel(side: int, size: int = 30) -> Image.Image:
    """Half a victory wreath: leaf pairs along an arc."""
    leaf = FAMILIES["foliage"]["leaf"]
    gold = FAMILIES["metal"]["gold"]
    img = np.zeros((size, size // 2 + 4, 4), np.uint8)
    import math
    for k in range(9):
        a = math.radians(200 - k * 18)
        cx, cy = size // 2 - 2 + math.cos(a) * (size / 2 - 4), size / 2 + math.sin(a) * (size / 2 - 4)
        cx = cx if side < 0 else img.shape[1] - 1 - cx
        for dx, dy in ((0, 0), (1, 0), (0, 1), (-1, 1)):
            x, y = int(cx + dx * side), int(cy + dy)
            if 0 <= x < img.shape[1] and 0 <= y < img.shape[0]:
                img[y, x, :3] = _hex(leaf[4 if dy == 0 else 2])
                img[y, x, 3] = 255
        if k % 3 == 1:
            x, y = int(cx - side * 2), int(cy)
            if 0 <= x < img.shape[1]:
                img[y, x, :3] = _hex(gold[5])
                img[y, x, 3] = 255
    return Image.fromarray(img, "RGBA")


def emblem() -> Image.Image:
    """The Wardens' crest: a gold-rimmed heater shield in the UI's night navy cradling the Hearthflame, with the five
    embers it shattered into hanging in an arc above. Warm ember gold against cool navy, like the UI kit."""
    import math
    from arcpix.canvas import Canvas, SlotTable
    st = SlotTable()
    for s_ in ("rim", "field", "flame", "ember"):
        st.id(s_)
    c = Canvas(48, 54, st)
    oy = 8
    outer = [(7, oy + 4), (41, oy + 4), (41, oy + 22), (36, oy + 33), (24, oy + 44), (12, oy + 33), (7, oy + 22)]
    inner = [(10, oy + 7), (38, oy + 7), (38, oy + 22), (33.5, oy + 31.5), (24, oy + 40.5), (14.5, oy + 31.5),
             (10, oy + 22)]
    c.polygon(outer, "rim", 4)
    c.polygon(inner, "field", 2)
    c.polygon([(10, oy + 7), (24, oy + 7), (24, oy + 40.5), (14.5, oy + 31.5), (10, oy + 22)], "field", 3)
    for y in range(c.h):                                           # bevel the rim from the top-left
        for x in range(c.w):
            if c.slot[y, x] == st.id("rim"):
                up = y == 0 or c.slot[y - 1, x] == 0
                left = x == 0 or c.slot[y, x - 1] == 0
                down = y == c.h - 1 or c.slot[y + 1, x] == 0
                right = x == c.w - 1 or c.slot[y, x + 1] == 0
                if up or left:
                    c.px(x, y, "rim", 5)
                elif down or right:
                    c.px(x, y, "rim", 2)
                elif c.slot[y + 1, x] == st.id("field") or c.slot[y, x + 1] == st.id("field"):
                    c.px(x, y, "rim", 3)
    for (sc, sh) in ((1.0, 2), (0.72, 3), (0.48, 4), (0.26, 5)):   # the Hearthflame, nested teardrops
        w, h, by, cx = 8.5 * sc, 27 * sc, oy + 37, 24
        b = by - (1 - sc) * 5
        pts = [(cx + w * math.cos(a), b - w + w * math.sin(a)) for a in [k * math.pi / 10 for k in range(11)]]
        pts += [(cx - w, b - w - (h - w) * 0.3), (cx - w * 0.45, b - w - (h - w) * 0.72), (cx + 1.2 * sc, b - h),
                (cx + w * 0.6, b - w - (h - w) * 0.5), (cx + w, b - w)]
        c.polygon(pts, "flame", sh)
        if sc == 1.0:
            c.polygon([(cx - w * 0.8, b - w), (cx - w * 1.3, b - h * 0.6), (cx - w * 0.2, b - w * 1.5)], "flame", sh)
            c.polygon([(cx + w * 0.85, b - w * 0.9), (cx + w * 1.25, b - h * 0.55), (cx + w * 0.3, b - w * 1.5)],
                      "flame", sh)
    for k in range(5):                                             # the five embers, arced over the shield
        ex, ey = 12 + k * 6, oy - 2 - (0, 3, 4, 3, 0)[k]
        c.rect(ex - 1, ey - 1, ex, ey, "ember", 4)
        c.px(ex - 1, ey - 1, "ember", 5)
        c.px(ex, ey + 1, "ember", 2)
    c.outline()
    pal = {"rim": FAMILIES["metal"]["gold"], "field": ramp("#1e2550", shift=0.04, spread=0.8),
           "flame": ramp("#ff9a2e", shift=0.08, spread=0.9), "ember": ramp("#ffb347", shift=0.08, spread=0.85)}
    return c.render(pal)

def _logo_lines(name: str = GAME_NAME, split: int = LOGO_SPLIT) -> tuple[str, str]:
    words = [w for w in name.upper().split() if w]
    words = ["".join(ch for ch in w if ch in GLYPHS) for w in words]
    if split <= 0 or len(words) <= split:
        return " ".join(words), ""
    return " ".join(words[:split]), " ".join(words[split:])


def logo(name: str = GAME_NAME, split: int = LOGO_SPLIT, max_width: int = 300) -> Image.Image:
    """The emblem over the name in cast gold, with an optional spaced subtitle in silver. Nothing is tied to the
    current letters: the top line takes the largest scale (4 down to 2) that fits `max_width`, and the subtitle
    is set at half that, so any 1-3 word name lays out."""
    head, tail = _logo_lines(name, split)
    for sc in (4, 3, 2):
        top = title_text(head, "gold", scale=sc, spacing=1, depth=max(1, sc - 1))
        if top.width <= max_width:
            break
    sub = title_text(tail, "silver", scale=max(2, sc // 2), spacing=3, depth=1) if tail else None
    camp = _campaign_line(CAMPAIGN) if CAMPAIGN else None
    em = emblem()
    W = max(top.width, sub.width if sub else 0, em.width) + 8
    H = em.height + top.height - 9 + (sub.height - 6 if sub else 0)
    out = Image.new("RGBA", (W, H))
    out.alpha_composite(em, ((W - em.width) // 2, 0))
    out.alpha_composite(top, ((W - top.width) // 2, em.height - 9))
    if sub:
        out.alpha_composite(sub, ((W - sub.width) // 2, em.height - 9 + top.height - 6))
    if camp:
        grown = Image.new("RGBA", (max(W, camp.width + 8), out.height + camp.height))
        grown.alpha_composite(out, ((grown.width - W) // 2, 0))
        grown.alpha_composite(camp, ((grown.width - camp.width) // 2, out.height - 3))
        out = grown
    return out


def _campaign_line(text: str) -> Image.Image:
    """The campaign's name in small silver capitals between two short gold rules with diamond ends."""
    t = title_text(text.upper(), "silver", scale=2, spacing=1, rim=1)
    gold = [_hex(c_) for c_ in FAMILIES["metal"]["gold"]]
    rule = 14
    out = np.zeros((t.height, t.width + 2 * (rule + 4), 4), np.uint8)
    cy = t.height // 2
    for side in (0, 1):
        x0 = 0 if side == 0 else t.width + rule + 8
        for x in range(rule):
            out[cy, x0 + x, :3], out[cy, x0 + x, 3] = gold[4], 255
            out[cy + 1, x0 + x, :3], out[cy + 1, x0 + x, 3] = gold[2], 255
        dx = x0 if side == 0 else x0 + rule - 1
        for (ddx, ddy) in ((0, -1), (-1, 0), (1, 0), (0, 2), (-1, 1), (1, 1)):
            if 0 <= dx + ddx < out.shape[1]:
                out[cy + ddy, dx + ddx, :3], out[cy + ddy, dx + ddx, 3] = gold[5], 255
    img = Image.fromarray(out, "RGBA")
    img.alpha_composite(t, (rule + 4, 0))
    return img


def gleam(img: Image.Image, frames: int = 12, width: float = 5.0) -> list[Image.Image]:
    """A light gleam sweeping across a cast title (logo, banner): a slanted band that lifts the lettering's lit
    pixels towards warm white. The outline and shadow stay put, so only the metal catches the light."""
    a = np.asarray(img).astype(np.float32)
    h, w = a.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w]
    lum = a[..., :3].mean(-1)
    metal = (a[..., 3] == 255) & (lum > 70)
    warm = np.array([255, 246, 216], np.float32)
    out = []
    for f in range(frames):
        pos = -h + f * (w + 2 * h) / (frames - 1)
        d = np.abs(xs + ys * 0.6 - pos)
        k = np.where(d < width * 0.4, 0.75, np.where(d < width, 0.4, 0.0)) * metal
        b = a.copy()
        b[..., :3] = b[..., :3] * (1 - k[..., None]) + warm * k[..., None]
        out.append(Image.fromarray(b.astype(np.uint8), "RGBA"))
    return out


def _sparkle(img: Image.Image, x: int, y: int, r: int, col) -> None:
    for k in range(-r, r + 1):
        for (px, py) in ((x + k, y), (x, y + k)):
            if 0 <= px < img.width and 0 <= py < img.height:
                a = 255 if abs(k) < 2 else 170
                img.putpixel((px, py), (*col, a))


def banner_anim(key: str, frames: int = 16) -> list[Image.Image]:
    """A moment banner played in: the ribbon unrolls from the centre, the title drops in with a one-frame
    overshoot, then a gleam crosses it. Each moment adds its own beat: VICTORY throws gold sparkles, LEVEL UP
    sends motes rising, DEFEAT sags and darkens. Seeded, so it's the same every build."""
    import random
    text, style, rib, wreath = BANNERS[key]
    full = moment(text, style, rib, wreath)
    t = title_text(text, style, scale=3)
    rb = ribbon(t.width + 4, t.height - 4, rib) if rib else None
    W, H = full.width, full.height + 16
    shine = gleam(t, 6)
    rng = random.Random(key)
    motes = [(rng.randrange(8, W - 8), rng.randrange(4, H - 4), rng.random()) for _ in range(14)]
    out = []
    for f in range(frames):
        im = Image.new("RGBA", (W, H))
        if rb is not None:
            reveal = min(1.0, (f + 1) / 4)
            cw = max(2, int(rb.width * reveal))
            part = rb.crop(((rb.width - cw) // 2, 0, (rb.width + cw) // 2, rb.height))
            sag = (f - 8) // 3 if key == "defeat" and f > 8 else 0
            im.alpha_composite(part, ((W - cw) // 2, 12 + sag))
        if wreath and f >= 3:
            lw, rw = laurel(-1, t.height + 4), laurel(1, t.height + 4)
            im.alpha_composite(lw, (6, 10))
            im.alpha_composite(rw, (W - rw.width - 6, 10))
        if f >= 2:
            drop = {2: -8, 3: -3, 4: 1}.get(f, 0)
            if key == "defeat" and f > 8:
                drop = (f - 8) // 2
            tt = shine[f - 8] if 8 <= f < 14 and key != "defeat" else t
            if key == "defeat" and f > 9:
                arr = np.asarray(tt).astype(np.float32)
                arr[..., :3] *= max(0.55, 1 - (f - 9) * 0.07)
                tt = Image.fromarray(arr.astype(np.uint8), "RGBA")
            im.alpha_composite(tt, ((W - t.width) // 2, 9 + drop))
        if key == "victory" and f >= 5:
            for (x, y, ph) in motes:
                if (f + int(ph * 6)) % 6 < 3:
                    _sparkle(im, x, y, 3 if (f + int(ph * 6)) % 6 == 1 else 2, (255, 230, 140))
        if key == "level_up" and f >= 4:
            for (x, y, ph) in motes:
                yy = int((y - (f - 4) * 2 - ph * 10) % H)
                im.putpixel((x, yy), (140, 220, 255, 230))
                if yy + 1 < H:
                    im.putpixel((x, yy + 1), (90, 160, 255, 140))
        if key == "defeat" and f >= 10:
            for (x, y, ph) in motes[:8]:
                yy = int(H * 0.55 + (f - 10) * 2 + ph * 6)
                if yy < H:
                    im.putpixel((x, yy), (120, 40, 50, 200))
        out.append(im)
    return out


ANIMATED = ("victory", "defeat", "level_up")


def moment(text: str, style: str, ribbon_colour: str | None = None, wreath: bool = False) -> Image.Image:
    t = title_text(text, style, scale=3)
    W = t.width + (40 if wreath else 0) + 8
    H = t.height + 10
    out = Image.new("RGBA", (W + 24, H))
    if ribbon_colour:
        rb = ribbon(t.width + 4, t.height - 4, ribbon_colour)
        out.alpha_composite(rb, ((out.width - rb.width) // 2, 4))
    if wreath:
        lw, rw = laurel(-1, t.height + 4), laurel(1, t.height + 4)
        out.alpha_composite(lw, (6, 2))
        out.alpha_composite(rw, (out.width - rw.width - 6, 2))
    out.alpha_composite(t, ((out.width - t.width) // 2, 1))
    return out


BANNERS = {
    "victory": ("VICTORY!", "gold", "#7a2f36", True),
    "defeat": ("DEFEAT", "red", "#2a2230", False),
    "level_up": ("LEVEL UP!", "gold", "#2f3f72", False),
    "your_turn": ("YOUR TURN", "blue", "#1a1e3a", False),
    "enemy_turn": ("ENEMY TURN", "red", "#1a1e3a", False),
    "boss": ("BOSS", "red", "#3a1a22", False),
    "round": ("ROUND", "silver", "#1a1e3a", False),
    "chapter": ("CHAPTER", "silver", "#2f3f72", False),
    "critical": ("CRITICAL!", "gold", None, False),
    "party_wiped": ("ALL DOWN", "red", "#2a2230", False),
}


def _strip(frames: list[Image.Image], gap: int = 0) -> Image.Image:
    w, h = frames[0].size
    out = Image.new("RGBA", (len(frames) * (w + gap) - gap, h))
    for i, im in enumerate(frames):
        out.alpha_composite(im, (i * (w + gap), 0))
    return out


def _gif(frames: list[Image.Image], path) -> None:
    """A review GIF on the UI navy at 3x."""
    from arcpix.preview import BG
    imgs = []
    for im in frames:
        bg = Image.new("RGBA", im.size, BG)
        bg.alpha_composite(im)
        imgs.append(bg.resize((im.width * 3, im.height * 3), Image.NEAREST).convert("RGB"))
    imgs[0].save(path, save_all=True, append_images=imgs[1:], duration=1000 // 12, loop=0, optimize=True)


def font_atlas() -> tuple[Image.Image, dict]:
    chars = list(GLYPHS)
    cols = 16
    img = Image.new("RGBA", (cols * (GW + 1), ((len(chars) + cols - 1) // cols) * (GH + 1)))
    meta = {}
    px = img.load()
    for i, ch in enumerate(chars):
        x0, y0 = (i % cols) * (GW + 1), (i // cols) * (GH + 1)
        for y, row in enumerate(GLYPHS[ch]):
            for x, v in enumerate(row):
                if v == "#":
                    px[x0 + x, y0 + y] = (255, 255, 255, 255)
        meta[ch] = {"rect": [x0, y0, GW, GH], "advance": _advance(ch) + 1}
    return img, meta


def build() -> None:
    out = PACK / "title"
    shutil.rmtree(out, ignore_errors=True)
    atlas, meta = font_atlas()
    save(atlas, out / "font5x7.png")
    save(logo(), out / "logo.png")
    save(emblem(), out / "emblem.png")
    tiles = [Tile(logo(), "logo", "")]
    files = {}
    for key, (text, style, rib, wreath) in BANNERS.items():
        img = moment(text, style, rib, wreath)
        save(img, out / f"{key}.png")
        files[key] = f"{key}.png"
        tiles.append(Tile(img, key, text))
    save(ribbon(160, 22), out / "ribbon.png")               # stretch the middle for any chapter title
    anims = {}
    lg_frames = gleam(logo(), 14, width=7.0)
    save(_strip(lg_frames), out / "logo_gleam.png")
    anims["logo_gleam"] = {"file": "logo_gleam.png", "frame": list(lg_frames[0].size), "frames": len(lg_frames),
                           "fps": 14, "loop": False, "every": "5-8 s on the title screen",
                           "note": "frames of logo.png with the gleam; rest on logo.png between plays"}
    anim_tiles = [Tile(_strip(lg_frames[::2], gap=4), "logo_gleam", f"{len(lg_frames)} frames (every 2nd shown)")]
    for key in ANIMATED:
        fr = banner_anim(key)
        save(_strip(fr), out / f"{key}_anim.png")
        anims[f"{key}_anim"] = {"file": f"{key}_anim.png", "frame": list(fr[0].size), "frames": len(fr), "fps": 12,
                                "loop": False, "hold": "the last frame until dismissed",
                                "note": f"plays {key}.png in: ribbon unroll, title drop, then its flourish"}
        anim_tiles.append(Tile(_strip(fr[::2], gap=4), f"{key}_anim", f"{len(fr)} frames at 12 fps (every 2nd)"))
        _gif(fr, PREVIEWS / f"29-title-{key.replace('_', '-')}.gif")
    _gif(lg_frames + [lg_frames[-1]] * 10, PREVIEWS / "29-title-logo.gif")
    write_json({"font": {"file": "font5x7.png", "glyph": [GW, GH], "lineHeight": GH + 2, "glyphs": meta,
                         "note": "white glyphs: tint, or use title styles via the logo pipeline"},
                "logo": "logo.png", "emblem": "emblem.png", "ribbon": {"file": "ribbon.png", "slice": [14, 4, 14, 4]},
                "banners": files, "anims": anims, "name": {"display": GAME_NAME}}, out / "manifest.json")
    from arcpix import iso
    from parts import dioramas as D, backgrounds as BG
    headroom = 190                                          # sky above the map: room for the logo and campaign
    scene = iso.render(D.ashen_ford(), margin=24,
                       backdrop=lambda w, h: BG.compose("dusk", "hills", w, h + headroom,
                                                        horizon=int(h * 0.62)).crop((0, headroom, w, h + headroom)))
    sky = BG.compose("dusk", "hills", scene.width, scene.height + headroom, horizon=int(scene.height * 0.62))
    sky.alpha_composite(scene, (0, headroom))
    key_art = iso.finish(sky, zoom=1)
    key_art = key_art.convert("RGBA")
    lg = logo()
    lg = lg.resize((lg.width * 2, lg.height * 2), Image.NEAREST)
    key_art.alpha_composite(lg, ((key_art.width - lg.width) // 2, 8))
    press = title_text("PRESS START", "silver", scale=2, spacing=2)
    key_art.alpha_composite(press, ((key_art.width - press.width) // 2, key_art.height - press.height - 16))
    save(key_art, out / "title_screen.png")
    save(key_art, PREVIEWS / "29-title-screen.png")
    sample = title_text("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 0123456789!?", "silver", scale=1, rim=1)
    save(stack("Titles and banners", [
        contact_sheet("Moments", tiles, columns=3, zoom=2),
        contact_sheet("Animated", anim_tiles, columns=1, zoom=2, subtitle="Logo gleam; VICTORY sparkles, DEFEAT "
                      "sags and darkens, LEVEL UP sends motes up. GIFs: previews/29-title-*.gif"),
        contact_sheet("Font · 5x7 capitals", [Tile(sample, "", "")], columns=1, zoom=3),
        contact_sheet("Title screen mock", [Tile(key_art, "", "")], columns=1, zoom=2)],
        "The logo, the big moment banners, the chapter ribbon and the pixel font they're set in."),
        PREVIEWS / "29-title.png")
