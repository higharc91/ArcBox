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


def title_text(text: str, style: str = "gold", scale: int = 3, spacing: int = 1, rim: int = 2) -> Image.Image:
    """Big cast lettering: glyphs scaled up, shaded top-to-bottom through a ramp, a lit top bevel and a dark lower
    edge per stroke, a rim in the outline colour and a soft drop shadow."""
    bm = _bitmap(text, spacing)
    big = np.kron(bm, np.ones((scale, scale), np.uint8))
    h, w = big.shape
    pad = rim + 3
    W, H = w + pad * 2, h + pad * 2
    mask = np.zeros((H, W), bool)
    mask[pad:pad + h, pad:pad + w] = big.astype(bool)
    rp = [_hex(c) for c in STYLES[style]]
    rgb = np.zeros((H, W, 3), np.float32)
    ys = np.arange(H)[:, None].repeat(W, 1)
    t = np.clip((ys - pad) / max(1, h - 1), 0, 1)
    shade = np.where(t < 0.25, 5, np.where(t < 0.5, 4, np.where(t < 0.8, 3, 2)))
    up = np.roll(mask, 1, 0)
    down = np.roll(mask, -1, 0)
    shade = np.where(mask & ~up, 5, shade)                  # lit top edge of every stroke
    shade = np.where(mask & ~down, np.maximum(shade - 1, 1), shade)
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
    """The logo's crest: a sword and a staff crossed behind a d20."""
    from arcpix.canvas import Canvas, SlotTable
    st = SlotTable()
    for s_ in ("blade", "wood", "die", "magic", "trim"):
        st.id(s_)
    c = Canvas(48, 48, st)
    c.thick_line(9, 40, 38, 8, 3, "blade", 4)                 # sword
    c.line(9, 40, 38, 8, "blade", 5)
    c.thick_line(14, 30, 20, 37, 2, "trim", 4)                # crossguard
    c.thick_line(39, 40, 12, 9, 2, "wood", 3)                 # staff
    c.ellipse(11.5, 7.5, 3.2, 3.2, "magic", 4)
    c.px(10, 6, "magic", 5)
    pts = [(24, 12), (35, 18), (35, 31), (24, 37), (13, 31), (13, 18)]   # d20 hexagon
    c.polygon(pts, "die", 3)
    c.polygon([(24, 15), (32, 30), (16, 30)], "die", 4)       # front face
    c.polygon([(24, 12), (35, 18), (24, 15)], "die", 5)
    c.polygon([(13, 31), (24, 37), (16, 30)], "die", 2)
    c.polygon([(35, 31), (24, 37), (32, 30)], "die", 2)
    c.outline()
    pal = {"blade": FAMILIES["metal"]["silver"], "wood": FAMILIES["wood"]["dark"], "die": FAMILIES["metal"]["gold"],
           "magic": FAMILIES["magic"]["arcane"], "trim": FAMILIES["metal"]["gold"]}
    img = c.render(pal)
    num = title_text("20", "red", scale=1, rim=1)              # the face value
    img.alpha_composite(num, (24 - num.width // 2, 25 - num.height // 2))
    return img


def logo() -> Image.Image:
    top = title_text("ARCBOX", "gold", scale=4, spacing=1)
    sub = title_text("TACTICS", "silver", scale=2, spacing=3)
    em = emblem()
    W = max(top.width, sub.width) + 8
    H = em.height + top.height + sub.height - 18
    out = Image.new("RGBA", (W, H))
    out.alpha_composite(em, ((W - em.width) // 2, 0))
    out.alpha_composite(top, ((W - top.width) // 2, em.height - 14))
    out.alpha_composite(sub, ((W - sub.width) // 2, em.height - 14 + top.height - 6))
    return out


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
    write_json({"font": {"file": "font5x7.png", "glyph": [GW, GH], "lineHeight": GH + 2, "glyphs": meta,
                         "note": "white glyphs: tint, or use title styles via the logo pipeline"},
                "logo": "logo.png", "emblem": "emblem.png", "ribbon": {"file": "ribbon.png", "slice": [14, 4, 14, 4]},
                "banners": files}, out / "manifest.json")
    from arcpix import iso
    from parts import dioramas as D, backgrounds as BG
    scene = iso.render(D.ashen_ford(), margin=24,
                       backdrop=lambda w, h: BG.compose("dusk", "hills", w, h + 150,
                                                        horizon=int(h * 0.62)).crop((0, 150, w, h + 150)))
    sky = BG.compose("dusk", "hills", scene.width, scene.height + 150, horizon=int(scene.height * 0.62))
    sky.alpha_composite(scene, (0, 150))                    # headroom above the map for the logo
    key_art = iso.finish(sky, zoom=1)
    key_art = key_art.convert("RGBA")
    lg = logo()
    lg = lg.resize((lg.width * 2, lg.height * 2), Image.NEAREST)
    key_art.alpha_composite(lg, ((key_art.width - lg.width) // 2, 8))
    press = title_text("PRESS START", "silver", scale=2, spacing=2)
    key_art.alpha_composite(press, ((key_art.width - press.width) // 2, key_art.height - press.height - 16))
    save(key_art, out / "title_screen.png")
    sample = title_text("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 0123456789!?", "silver", scale=1, rim=1)
    save(stack("Titles and banners", [
        contact_sheet("Moments", tiles, columns=3, zoom=2),
        contact_sheet("Font · 5x7 capitals", [Tile(sample, "", "")], columns=1, zoom=3),
        contact_sheet("Title screen mock", [Tile(key_art, "", "")], columns=1, zoom=2)],
        "The logo, the big moment banners, the chapter ribbon and the pixel font they're set in."),
        PREVIEWS / "29-title.png")
