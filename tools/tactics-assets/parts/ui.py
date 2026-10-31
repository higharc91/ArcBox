"""The battle UI kit: 9-slice panels and buttons, bars, isometric tile markers and path arrows, turn-order pieces, the
floating combat-number font and the d20.

Everything is drawn at 1x in (slot, shade) like the characters, then baked to colour per *variant* palette (the
terrain set's trick), so one drawing ships as the gold, navy and red button, or as the HP, MP and XP fill. The UI is
not recoloured at runtime, so the pack holds plain RGBA atlases, and the manifest says where each piece sits and how
it stretches. The game draws the UI at whole multiples with nearest filtering.

The look follows the mockups (docs/superpowers/mockups/tactics): navy glass, a gold edge lit from the top-left, and
gold diamond studs on the top corners.
"""
from __future__ import annotations

import shutil
from dataclasses import dataclass

import numpy as np
from PIL import Image, ImageDraw

from arcpix.canvas import Canvas, SlotTable, FLAG_OUTLINE
from arcpix.palette import ramp, BASE
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.preview import contact_sheet, Tile, stack, strip, scale, font, BG, INK, MUTED

OUT = PACK / "ui"

SLOTS = SlotTable()
for _s in ("edge", "glass", "line", "stud", "fill", "text", "rim", "shadow", "glow", "ghost", "tint"):
    SLOTS.id(_s)

# The UI's ramps. Bases come from the mockups' CSS (STYLE.md, "UI palette"); the rest are picked to sit beside them.
RAMPS: dict[str, list[str]] = {
    "gold": ramp("#e6bd5c", shift=0.06, spread=1.2),
    "gold_dull": ramp("#8a7a5c", shift=0.04, spread=0.9),
    "glass": ramp("#1a1e3a", shift=0.04, spread=0.75),
    "glass_hi": ramp("#252a4e", shift=0.04, spread=0.8),
    "steel": ramp("#565f80", shift=0.04, spread=0.9),
    "umber": ramp("#2c2622", shift=0.04, spread=0.8),
    "ink": ramp("#f4ecd6", shift=0.05, spread=0.6),
    "muted": ramp("#bdb093", shift=0.05, spread=0.8),
    "shadow": ramp("#140f1a", shift=0.02),
    "hp": ramp("#63d36f", shift=0.06, spread=0.9),
    "enemy": ramp("#e2534c", shift=0.06, spread=0.9),
    "magic": ramp("#7fc8ff", shift=0.05, spread=0.9),
    "xp": ramp("#e6bd5c", shift=0.06, spread=1.0),
    "ct": ramp("#c9e4ff", shift=0.05, spread=0.9),
    "ghost": ramp("#f6dcc4", shift=0.04, spread=0.6),
    "red": ramp("#c4473f", shift=0.06, spread=1.0),
    "red_dull": ramp("#7a5a58", shift=0.04, spread=0.9),
    "slate": ramp("#4a5262", shift=0.04, spread=0.9),
    "move": ramp("#4aa6ff", shift=0.05, spread=0.9),
    "attack": ramp("#ff5446", shift=0.06, spread=0.9),
    "area": ramp("#ff7a2e", shift=0.06, spread=0.9),
    "ally": ramp("#5fe08a", shift=0.06, spread=0.9),
    "crit": ramp("#ffc04a", shift=0.06, spread=0.9),
    "rim_damage": ramp("#8a1f33", shift=0.05),
    "rim_heal": ramp("#1d5c3e", shift=0.05),
    "rim_crit": ramp("#8a3c12", shift=0.05),
    "rim_mana": ramp("#22387a", shift=0.05),
    "p1": ramp("#ff8a3d", shift=0.06, spread=0.9),
    "p2": ramp("#3ec7ff", shift=0.05, spread=0.9),
    "p3": ramp("#c08cff", shift=0.05, spread=0.9),
    "p4": ramp("#5fe08a", shift=0.06, spread=0.9),
    "p5": ramp("#ffd447", shift=0.06, spread=0.9),
    "p6": ramp("#ff7fb6", shift=0.05, spread=0.9),
}
PLAYERS = ("p1", "p2", "p3", "p4", "p5", "p6")

# Text colours the game should use on each piece (fonts are the game's; these keep the contrast the mockups have).
TEXT = {"ink": "#f4ecd6", "muted": "#bdb093", "dim": "#8a826d", "gold": "#e6bd5c", "on_gold": "#2a1b04"}

Pal = dict[str, str]                       # slot -> ramp name


def _bake(c: Canvas, pal: Pal, alpha: dict[str, float | list[float]] | None = None) -> Image.Image:
    """Renders a canvas in `pal`; `alpha` makes slots (or single shades of a slot) translucent: glass, glows and
    the tile markers that sit over terrain."""
    img = c.render({s: RAMPS[r] for s, r in pal.items()})
    if not alpha:
        return img
    a = np.asarray(img).copy()
    for slot, v in alpha.items():
        m = (c.slot == SLOTS.id(slot)) & ((c.flags & FLAG_OUTLINE) == 0)
        if isinstance(v, list):
            k = np.array(v, np.float32)[np.clip(c.shade, 0, 5)]
        else:
            k = np.full(c.slot.shape, v, np.float32)
        a[..., 3] = np.where(m, (a[..., 3] * k).round(), a[..., 3]).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


# ------------------------------------------------------------------------------------------------------ geometry
def _depth_rect(w: int, h: int, r: int) -> np.ndarray:
    """Distance (in rings) from the edge of a w x h box whose corners are chamfered by `r`. The chamfer shrinks as
    the rings go in, so an outline and a 1 px edge both round the corner while the interior stays square."""
    ys, xs = np.mgrid[0:h, 0:w]
    t, l, b, rt = ys, xs, h - 1 - ys, w - 1 - xs
    return np.minimum.reduce([t, l, b, rt, t + l - r, t + rt - r, b + l - r, b + rt - r])


def _depth_mask(mask: np.ndarray) -> np.ndarray:
    """Rings of an arbitrary shape by 4-neighbour erosion: a 45-degree edge stays one pixel per row."""
    d = np.full(mask.shape, -1, np.int32)
    cur, k = mask.copy(), 0
    while cur.any():
        er = cur.copy()
        er[1:] &= cur[:-1]
        er[:-1] &= cur[1:]
        er[:, 1:] &= cur[:, :-1]
        er[:, :-1] &= cur[:, 1:]
        er[0], er[-1], er[:, 0], er[:, -1] = False, False, False, False
        d[cur & ~er] = k
        cur, k = er, k + 1
    return d


Ring = tuple[str, "int | None", "int | None", "int | None"]
"""(slot, shade on the lit top/left, shade where the two meet, shade on the dark bottom/right). None lets the fill
show through on that side."""


def _paint(c: Canvas, x0: int, y0: int, depth: np.ndarray, rings: list[Ring], slot: str, *, mid: int = BASE,
           top: tuple[int, ...] = (), bottom: tuple[int, ...] = ()) -> None:
    """Fills a shape ring by ring. A ring pixel is lit when the shape ends above or left of it and dark when it ends
    below or right, so bevels follow the top-left light on any outline. Inside the rings, `top` and `bottom` give
    the first and last rows their own shades (a glass sheen, a button's lip) and everything else is `mid`."""
    h, w = depth.shape
    pad = np.pad(depth, 1, constant_values=-1)
    rows = np.where((depth >= len(rings)).any(1))[0]
    y_first, y_last = (rows[0], rows[-1]) if len(rows) else (0, h - 1)
    for y in range(h):
        i, j = max(0, y - y_first), max(0, y_last - y)
        fill = top[i] if i < len(top) else bottom[j] if j < len(bottom) else mid
        for x in range(w):
            k = depth[y, x]
            if k < 0:
                continue
            s, sh = slot, fill
            if k < len(rings):
                lit = pad[y + 1, x] < k or pad[y, x + 1] < k
                dark = pad[y + 1, x + 2] < k or pad[y + 2, x + 1] < k
                rs, a, m, b = rings[k]
                v = m if lit == dark else a if lit else b
                if v is not None:
                    s, sh = rs, v
            c.px(x0 + x, y0 + y, s, sh)


def _stud(c: Canvas, cx: int, cy: int, rad: int = 3, slot: str = "stud") -> None:
    """The gold diamond stud on a panel corner: four facets lit from the top-left, a bright centre."""
    for dy in range(-rad, rad + 1):
        for dx in range(-rad, rad + 1):
            if abs(dx) + abs(dy) > rad:
                continue
            if dx == 0 and dy == 0:
                sh = 5
            elif dx <= 0 and dy < 0:
                sh = 5 if dx == 0 or abs(dx) + abs(dy) < rad else 4
            elif dx < 0 and dy >= 0:
                sh = 4 if dy == 0 else 3
            elif dx > 0 and dy <= 0:
                sh = 4 if dy < 0 else 3
            else:
                sh = 2
            c.px(cx + dx, cy + dy, slot, sh)


def _dilate(m: np.ndarray, diagonal: bool = False) -> np.ndarray:
    out = m.copy()
    out[1:] |= m[:-1]
    out[:-1] |= m[1:]
    out[:, 1:] |= m[:, :-1]
    out[:, :-1] |= m[:, 1:]
    if diagonal:
        out[1:, 1:] |= m[:-1, :-1]
        out[1:, :-1] |= m[:-1, 1:]
        out[:-1, 1:] |= m[1:, :-1]
        out[:-1, :-1] |= m[1:, 1:]
    return out


def _halo(c: Canvas, slot: str = "glow", shade: int = 4) -> None:
    """One ring of `slot` around everything drawn (a hover glow or selection light; bake it translucent)."""
    m = c.slot > 0
    ring = _dilate(m, diagonal=True) & ~m
    c.slot[ring] = SLOTS.id(slot)
    c.shade[ring] = shade
    c.flags[ring] = 0


# Tiny 3x5-ish label font for the few words baked into pieces ("NOW", "AI").
MINI = {
    "N": ["#..#", "##.#", "#.##", "#..#", "#..#"],
    "O": [".##.", "#..#", "#..#", "#..#", ".##."],
    "W": ["#...#", "#...#", "#.#.#", "#.#.#", ".#.#."],
    "A": [".#.", "#.#", "###", "#.#", "#.#"],
    "I": ["###", ".#.", ".#.", ".#.", "###"],
}


def _mini_text(c: Canvas, x: int, y: int, word: str, slot: str, shade: int) -> int:
    for ch in word:
        g = MINI[ch]
        for dy, row in enumerate(g):
            for dx, v in enumerate(row):
                if v == "#":
                    c.px(x + dx, y + dy, slot, shade)
        x += len(g[0]) + 1
    return x - 1


def _mini_width(word: str) -> int:
    return sum(len(MINI[ch][0]) + 1 for ch in word) - 1


# --------------------------------------------------------------------------------------------------------- pieces
@dataclass
class Piece:
    """One atlas entry: the drawing, its palette, and the 9-slice data the manifest needs (all in source pixels)."""
    canvas: Canvas
    pal: Pal
    slice: tuple[int, int, int, int] | None = None      # left, top, right, bottom insets
    outset: tuple[int, int, int, int] = (0, 0, 0, 0)     # pixels outside the nominal box (studs, glow)
    padding: tuple[int, int, int, int] | None = None     # content padding inside the nominal box
    alpha: dict | None = None
    extra: dict | None = None

    def image(self) -> Image.Image:
        return _bake(self.canvas, self.pal, self.alpha)


GLASS_ALPHA = {"glass": 0.93}


def _panel(w: int, h: int, *, rings: list[Ring], r: int = 2, studs: str = "top", stud_r: int = 3,
           top=(3, 3), mid=2, bottom=(1,), slot="glass", extra_pad: int = 3) -> tuple[Canvas, dict]:
    """A navy glass panel `w` x `h` (outline included) with gold rings and optional corner studs. Returns the canvas
    and its 9-slice numbers: studs sit on the outline's corner pixels and stick out by `stud_r`."""
    o = stud_r + 1 if studs != "none" else 0
    ob = o if studs == "all" else 0
    c = Canvas(w + 2 * o, h + o + ob, SLOTS)
    _paint(c, o + 1, o + 1, _depth_rect(w - 2, h - 2, r), rings, slot, mid=mid, top=top, bottom=bottom)
    c.outline()
    corners = [(o, o), (o + w - 1, o)]
    if studs == "all":
        corners += [(o, o + h - 1), (o + w - 1, o + h - 1)]
    if studs != "none":
        for cx, cy in corners:
            _stud(c, cx + 1, cy + 1, stud_r)
        # outline() only fills empty pixels; studs that overhang the panel need their own rim.
        m = c.mask("stud")
        ring = _dilate(m) & ~m & (c.slot == 0)
        c.slot[ring], c.shade[ring], c.flags[ring] = SLOTS.id("stud"), 0, FLAG_OUTLINE
    n = len(rings) + 1
    left = o + n + extra_pad
    meta = {"slice": (left, o + n + len(top), left, ob + n + len(bottom)),
            "outset": (o, o, o, ob), "padding": (n + extra_pad, n + 2, n + extra_pad, n + 2)}
    return c, meta


GOLD_EDGE: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0), ("glass", 4, 3, 3)]


def panels() -> dict[str, Piece]:
    out: dict[str, Piece] = {}
    pal = {"edge": "gold", "stud": "gold", "glass": "glass", "line": "glass"}

    c, m = _panel(34, 30, rings=GOLD_EDGE)
    out["main"] = Piece(c, pal, alpha=GLASS_ALPHA, **m)

    # Inner card: sits on a panel, so no studs and no gold; a quiet steel line and slightly lifted glass.
    card_rings: list[Ring] = [("edge", 4, 3, 2), ("glass", 4, None, None)]
    c, m = _panel(18, 18, rings=card_rings, r=1, studs="none", top=(3,), mid=3, bottom=(), extra_pad=2)
    out["card"] = Piece(c, {"edge": "steel", "glass": "glass_hi"}, **m)
    c, m = _panel(18, 18, rings=card_rings, r=1, studs="none", top=(3,), mid=3, bottom=(), extra_pad=2)
    out["card_selected"] = Piece(c, {"edge": "gold", "glass": "umber"}, **m)

    # Tooltip: compact, darker, with small studs, so it reads as a pop-over rather than a window.
    tip_rings: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0)]
    c, m = _panel(14, 12, rings=tip_rings, r=1, stud_r=2, top=(2,), mid=1, bottom=(), extra_pad=2)
    out["tooltip"] = Piece(c, {**pal}, alpha={"glass": 0.95}, **m)
    tail = Canvas(9, 6, SLOTS)
    for k in range(4):
        tail.rect(k + 1, k, 7 - k, k, "glass", 1)
        tail.px(k + 1, k, "edge", 3)
        tail.px(7 - k, k, "edge", 2)
    tail.outline()
    out["tooltip_tail"] = Piece(tail, pal, extra={"attach": "centred under the tooltip, its top row on the tooltip's "
                                                            "bottom rim; flip it vertically to point up"})

    # Dialogue: a double frame (gold, dark line, a dimmer inner rule) with studs on all four corners.
    dlg_rings: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0), ("glass", 3, 3, 3), ("edge", 2, 2, 1),
                             ("glass", 4, None, None)]
    c, m = _panel(40, 34, rings=dlg_rings, studs="all", top=(3, 3), mid=2, bottom=(1,))
    out["dialogue"] = Piece(c, pal, alpha=GLASS_ALPHA, **m)
    tab_rings: list[Ring] = [("edge", 5, 4, 2), ("line", 0, 0, 0)]
    c, m = _panel(16, 13, rings=tab_rings, r=1, studs="none", top=(3,), mid=3, bottom=(2,), extra_pad=3)
    out["dialogue_tab"] = Piece(c, {**pal, "glass": "glass_hi"}, extra={
        "attach": "x = dialogue box left + 6, y = dialogue box top - 7 (it straddles the top edge)"}, **m)

    out["banner"] = _banner()
    out["banner_crest"] = _crest()

    # Turn-order strip: a panel with a recessed tray the portraits sit in.
    tray: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0), ("glass", 4, 3, 3), ("glass", 2, 2, 2),
                        ("glass", 2, 2, 2), ("glass", 2, 2, 2), ("line", 0, 1, 2)]
    c, m = _panel(40, 34, rings=tray, top=(1,), mid=1, bottom=(), extra_pad=0)
    # header band above the tray: give the label a lighter strip
    for y in range(5, 12):
        for x in range(c.w):
            if c.slot[y, x] == SLOTS.id("glass") and c.shade[y, x] == 2:
                c.shade[y, x] = 3
    m["slice"] = (m["slice"][0], 15, m["slice"][2], m["slice"][3])
    m["padding"] = (m["padding"][0] + 1, 11, m["padding"][2] + 1, 4)
    out["turn_strip"] = Piece(c, pal, alpha=GLASS_ALPHA, extra={"header": "the label sits in the top 8 px"}, **m)
    return out


def _banner() -> Piece:
    """The ability banner ("SHIELD BASH"): a navy plaque with pointed ends, a gold edge and an inner gold rule."""
    w, h = 44, 23
    half = h // 2
    ys, xs = np.mgrid[0:h, 0:w]
    tip = half - np.abs(ys - half)                  # how far in from each end the edge is on this row
    mask = (xs >= half - tip) & (xs <= w - 1 - half + tip)
    rings: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0), ("glass", 3, 3, 3), ("edge", 3, 2, 1),
                         ("glass", 3, None, None)]
    c = Canvas(w + 2, h + 2, SLOTS)
    _paint(c, 1, 1, _depth_mask(mask), rings, "glass", mid=2, top=(3,), bottom=(1,))
    c.outline()
    s = half + 7
    return Piece(c, {"edge": "gold", "stud": "gold", "glass": "glass", "line": "glass"}, alpha=GLASS_ALPHA,
                 slice=(s, 8, s, 8), padding=(half + 3, 6, half + 3, 6),
                 extra={"stretch": "horizontal; keep the source height so the pointed ends stay sharp"})


def _crest() -> Piece:
    """A gem set in a gold diamond, centred on the banner's top edge."""
    c = Canvas(13, 9, SLOTS)
    for y in range(9):
        r = 4 - abs(y - 4)
        for x in range(6 - 2 * r - 1, 6 + 2 * r + 2):
            c.px(x, y, "stud", 4 if y < 4 else 2 if y > 4 else 3)
    for y in range(2, 7):
        r = 2 - abs(y - 4)
        for x in range(6 - 2 * r, 6 + 2 * r + 1):
            c.px(x, y, "glow", 4 if (y < 4 and x <= 6) else 2 if y > 4 or x > 6 else 3)
    c.px(5, 3, "glow", 5)
    c.outline()
    return Piece(c, {"stud": "gold", "glow": "magic"}, extra={"attach": "centred on the banner's top edge"})


# --------------------------------------------------------------------------------------------------------- buttons
BUTTONS = {
    "primary": {"normal": {"fill": "gold"}, "disabled": {"fill": "gold_dull"}, "text": "on_gold"},
    "secondary": {"normal": {"fill": "glass_hi"}, "disabled": {"fill": "slate"}, "text": "ink"},
    "danger": {"normal": {"fill": "red"}, "disabled": {"fill": "red_dull"}, "text": "ink"},
}
STATES = ("normal", "hover", "pressed", "disabled", "focused")
BW, BH = 18, 13


def _button(state: str) -> Canvas:
    """A raised button: lit rim on the top-left, a dark lip underneath. Pressed drops 1 px and loses the lip, so
    the face sits where the lip was; hover lifts every shade by one and adds a soft halo."""
    c = Canvas(BW + 4, BH + 4, SLOTS)
    if state == "focused":
        c = Canvas(BW + 6, BH + 6, SLOTS)
        c.blit(_button("normal"), 1, 1)
        _halo(c, "rim", 5)
        _halo(c, "rim", 2)
        return c
    if state == "pressed":
        rings: list[Ring] = [("fill", 1, 2, 3)]
        _paint(c, 2, 3, _depth_rect(BW, BH - 1, 2), rings, "fill", mid=3, top=(2,), bottom=())
    else:
        up = 1 if state == "hover" else 0
        rings = [("fill", 4 + up, 3 + up, 1)]
        _paint(c, 2, 2, _depth_rect(BW, BH, 2), rings, "fill", mid=3 + up, top=(4 + up, 4 + up), bottom=(1, 2))
        if state == "disabled":
            c.shade[c.mask("fill")] = np.clip(c.shade[c.mask("fill")], 1, 3)
    c.outline()
    if state == "hover":
        _halo(c)
    return c


def buttons() -> dict[str, dict[str, Piece]]:
    out: dict[str, dict[str, Piece]] = {}
    for kind, spec in BUTTONS.items():
        out[kind] = {}
        for st in STATES:
            pal = spec["disabled"] if st == "disabled" else spec["normal"]
            pal = {**pal, "glow": pal["fill"], "rim": "gold"}
            o = 2 if st == "focused" else 1
            out[kind][st] = Piece(_button(st), pal, slice=(6 + o - 1, 6 + o - 1, 6 + o - 1, 6 + o - 1),
                                  outset=(o, o, o, o),
                                  padding=(5, 4, 5, 4), alpha={"glow": 0.45},
                                  extra={"contentOffset": [0, 1] if st == "pressed" else [0, 0],
                                         "text": "dim" if st == "disabled" else spec["text"]})
    return out


# ------------------------------------------------------------------------------------------------------------ bars
@dataclass
class BarFrame:
    h: int          # outer height, outline included
    fill_h: int


BAR_FRAMES = {"standard": BarFrame(9, 5), "slim": BarFrame(7, 3)}
BAR_W = 16
# kind -> (frame, ramp, fill row shades top to bottom, segment every n px or 0)
BARS = {
    "hp": ("standard", "hp", (5, 4, 3, 3, 1), 0),
    "enemy_hp": ("standard", "enemy", (5, 4, 3, 3, 1), 0),
    "mp": ("standard", "magic", (5, 4, 3, 3, 1), 0),
    "xp": ("slim", "xp", (5, 3, 2), 0),
    "ct": ("slim", "ct", (5, 3, 2), 4),
}


def _bar_frame(kind: str) -> Canvas:
    """A dark trough with a lit rim; the fill sits in its inner rows."""
    f = BAR_FRAMES[kind]
    c = Canvas(BAR_W, f.h, SLOTS)
    rings: list[Ring] = [("edge", 4, 3, 3), ("glass", 0, 0, 1)]
    _paint(c, 1, 1, _depth_rect(BAR_W - 2, f.h - 2, 1), rings, "glass", mid=0, top=(0,), bottom=())
    c.outline()
    return c


def _bar_fill(rows: tuple[int, ...], part: str, segment: int, w: int) -> Canvas:
    """A strip of fill: `left` cap rounds the start, `mid` tiles, `right` is the bright leading edge."""
    h = len(rows)
    c = Canvas(w, h, SLOTS)
    for y, sh in enumerate(rows):
        for x in range(w):
            c.px(x, y, "fill", sh)
    if part == "left":
        c.clear(0, 0)
        c.clear(0, h - 1)
        for y in range(1, h - 1):
            c.px(0, y, "fill", min(5, rows[y] + 1) if y < h // 2 + 1 else rows[y])
    elif part == "right":
        for y in range(h):
            c.px(w - 1, y, "fill", 5 if 0 < y < h - 1 else 4)
    elif part == "mid" and segment:
        for y in range(h):
            c.px(w - 1, y, "fill", max(0, rows[y] - 2))
    return c


def bars() -> tuple[dict[str, Piece], dict[str, dict[str, Piece]]]:
    frames = {k: Piece(_bar_frame(k), {"edge": "steel", "glass": "glass"}, slice=(3, 3, 3, 3))
              for k in BAR_FRAMES}
    fills: dict[str, dict[str, Piece]] = {}
    for kind, (frame, rp, rows, seg) in BARS.items():
        mid_w = seg or 4
        ghost = tuple(min(5, max(3, r + 1)) for r in rows)
        fills[kind] = {
            "left": Piece(_bar_fill(rows, "left", seg, 1), {"fill": rp}),
            "mid": Piece(_bar_fill(rows, "mid", seg, mid_w), {"fill": rp}),
            "right": Piece(_bar_fill(rows, "right", seg, 2), {"fill": rp}),
            # damage-taken ghost: pale, flat, drawn under the fill from the new value to the old one
            "ghost_mid": Piece(_bar_fill(ghost, "flat", 0, 4), {"fill": "ghost"}),
            "ghost_right": Piece(_bar_fill(ghost, "flat", 0, 1), {"fill": "ghost"}),
        }
    return frames, fills


# --------------------------------------------------------------------------------------------------- tile markers
TW, TH = 64, 32          # one 32 px tile, projected 2:1
TILE = 32
MARK_FRAMES = 4
MARK_FPS = 6


def _uv(w: int, h: int, cx: float, cy: float) -> tuple[np.ndarray, np.ndarray]:
    """Ground-plane coordinates (U, V in tile texels, 0..32 across the tile) at each pixel centre, for a tile whose
    centre lands on (cx, cy). The same projection as arcpix.iso: x = U - V, y = (U + V) / 2. Sampling at pixel
    centres makes neighbouring tiles partition the screen exactly, so pieces meet without gaps or overlaps."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    a = xs + 0.5 - cx
    b = 2 * (ys + 0.5 - (cy - TILE / 2))
    return (a + b) / 2, (b - a) / 2


def _edge_dist(U: np.ndarray, V: np.ndarray, lo: float = 0, hi: float = TILE) -> np.ndarray:
    return np.minimum.reduce([U - lo, V - lo, hi - U, hi - V])


@dataclass
class MarkerKind:
    ramp: str
    pattern: str            # plain | hatch | ripple | core | select
    fill_alpha: tuple[float, ...] = (0.40, 0.50, 0.40, 0.32)


MARKERS = {
    "move": MarkerKind("move", "plain"),
    "attack": MarkerKind("attack", "hatch"),
    "area": MarkerKind("area", "ripple"),
    "ally": MarkerKind("ally", "core"),
    "selected": MarkerKind("gold", "select", (0.40, 0.50, 0.40, 0.32)),
}


def _marker(kind: MarkerKind, f: int) -> tuple[Canvas, dict]:
    """A range marker: a 1-texel gap (so neighbouring marked tiles read as a grid), a bright rim and a translucent
    fill. Each kind adds an inner pattern so the ranges differ by shape too, not only by colour. The 4 frames pulse:
    the rim brightens and the pattern moves."""
    U, V = _uv(TW, TH, TW / 2, TH / 2)
    e = _edge_dist(U, V)
    c = Canvas(TW, TH, SLOTS)
    rim_shade = (4, 5, 4, 3)[f]
    rim = (e >= 1) & (e < 2)
    fill = e >= 2
    inner = fill & (e < 3)
    pat = np.zeros_like(fill)
    if kind.pattern == "hatch":
        pat = fill & ((np.floor(U + V + 2 * f) % 8) < 2) & (e >= 3)
    elif kind.pattern == "ripple":
        k = 3 + 2 * ((3 - f) % 4)                                    # a ring that travels inwards
        pat = fill & (e >= k) & (e < k + 1)
    elif kind.pattern == "core":
        pat = fill & (e >= (11, 10, 11, 12)[f])
    elif kind.pattern == "select":
        pat = fill & (e >= 3) & (e < 4)
    c.slot[fill] = SLOTS.id("tint")
    c.shade[fill] = 3
    c.shade[inner] = 4
    c.slot[pat] = SLOTS.id("glow")
    c.shade[pat] = 4 if kind.pattern != "select" else 5
    c.slot[rim] = SLOTS.id("edge")
    c.shade[rim] = rim_shade
    fa = kind.fill_alpha[f]
    alpha = {"tint": [fa] * 4 + [min(1, fa + 0.18)] * 2, "glow": min(1, fa + 0.3), "edge": 0.95}
    if kind.pattern == "select":
        alpha["edge"] = 1.0
    return c, alpha


def markers() -> dict[str, list[Piece]]:
    out = {}
    for name, k in MARKERS.items():
        pal = {"tint": k.ramp, "glow": k.ramp if name != "selected" else "ink", "edge": k.ramp}
        out[name] = []
        for f in range(MARK_FRAMES):
            cv, al = _marker(k, f)
            out[name].append(Piece(cv, pal, alpha=al))
    return out


# Path arrows. Directions are grid steps: -U is the tile's upper-left edge on screen, +U lower-right, -V upper-right,
# +V lower-left. A piece is named by the edges the path crosses.
DIRS = {"nw": (-1, 0), "se": (1, 0), "ne": (0, -1), "sw": (0, 1)}
PATH_HW = 2.5


def _ray(U, V, d: str, grow: float, stop: float | None = None) -> np.ndarray:
    """A ribbon from the tile centre out through edge `d` (and on into the neighbour, so pieces join)."""
    du, dv = DIRS[d]
    hw = PATH_HW + grow
    if du:
        along, across = (U - 16) * du, V - 16
    else:
        along, across = (V - 16) * dv, U - 16
    m = (along >= -hw) & (np.abs(across) <= hw)
    if stop is not None:
        m &= along <= stop + grow
    return m


def _head(U, V, d: str, grow: float) -> np.ndarray:
    """An arrowhead at the centre pointing out through edge `d`."""
    du, dv = DIRS[d]
    if du:
        along, across = (U - 16) * du, V - 16
    else:
        along, across = (V - 16) * dv, U - 16
    tip, base, half = 10.0, -5.0, 7.5
    return (along >= base - grow) & (np.abs(across) <= (tip - along) * half / (tip - base) + grow * 1.4)


OPPOSITE = {"nw": "se", "se": "nw", "ne": "sw", "sw": "ne"}


def _path_shape(name: str, U, V, grow: float) -> np.ndarray:
    kind, *ds = name.split("_")
    if kind in ("straight", "corner"):
        return _ray(U, V, ds[0], grow) | _ray(U, V, ds[1], grow)
    if kind == "start":                                      # a round nub on the unit's tile
        d = (U - 16) ** 2 + (V - 16) ** 2
        return _ray(U, V, ds[0], grow) | (d <= (PATH_HW + 2 + grow) ** 2)
    # end_<from>: enters through edge <from>, the head points the other way
    return _ray(U, V, ds[0], grow, stop=-2) | _head(U, V, OPPOSITE[ds[0]], grow)


PATH_PIECES = (["straight_nw_se", "straight_ne_sw", "corner_nw_ne", "corner_ne_se", "corner_se_sw", "corner_sw_nw"]
               + [f"end_{d}" for d in DIRS] + [f"start_{d}" for d in DIRS])


def _path(name: str, f: int) -> Canvas:
    """A path piece: a gold ribbon with a dark rim, lit along its upper side. Shapes are defined on the ground plane
    and run past the tile edge, so the rim and the shading continue seamlessly into the next piece."""
    U, V = _uv(TW, TH, TW / 2, TH / 2)
    inside = _edge_dist(U, V) > 0
    body = _path_shape(name, U, V, 0) & inside
    rim = _path_shape(name, U, V, 1.0) & inside & ~body
    up = _path_shape(name, U - 1, V - 1, 0)                 # one pixel up on screen
    down = _path_shape(name, U + 1, V + 1, 0)
    c = Canvas(TW, TH, SLOTS)
    base = (4, 5, 4, 4)[f]
    c.slot[body] = SLOTS.id("fill")
    c.shade[body] = base
    c.shade[body & ~up] = 5
    c.shade[body & ~down] = base - 2
    c.slot[rim] = SLOTS.id("rim")
    c.shade[rim] = 0
    c.shade[rim & ~_path_shape(name, U - 1, V - 1, 1.0)] = 1
    return c


def paths() -> dict[str, list[Piece]]:
    return {n: [Piece(_path(n, f), {"fill": "ink", "rim": "gold"}) for f in range(MARK_FRAMES)]
            for n in PATH_PIECES}


# Cursor: corner brackets that breathe outwards, and an arrow that bobs above the unit or tile.
CW, CH = TW + 8, TH + 4


def _brackets(f: int) -> Canvas:
    grow = (0, 1, 2, 1)[f]
    U, V = _uv(CW, CH, CW / 2, CH / 2)
    lo, hi = -grow, TILE + grow
    e = _edge_dist(U, V, lo, hi)
    band = (e >= 0) & (e < 1.5)
    near = np.zeros_like(band)
    for cu, cv in ((lo, lo), (hi, lo), (hi, hi), (lo, hi)):
        near |= (np.abs(U - cu) + np.abs(V - cv)) < 10
    m = band & near
    c = Canvas(CW, CH, SLOTS)
    c.slot[m] = SLOTS.id("fill")
    c.shade[m] = 5
    up = _edge_dist(U - 1, V - 1, lo, hi)
    c.shade[m & (up >= 1.5)] = 4                            # the inner side of the top brackets sits in shadow
    c.outline()
    return c


AW, AH = 13, 16
ARROW_BOB = (0, 1, 2, 1)


def _arrow(f: int) -> Canvas:
    """A faceted gold gem pointing down: left facet lit, right facet in shadow, bright top bevel."""
    c = Canvas(AW, AH, SLOTS)
    y0 = 1 + ARROW_BOB[f]
    for k in range(6):
        y = y0 + 2 + k
        for x in range(1 + k, AW - 1 - k):
            c.px(x, y, "fill", 4 if x < AW // 2 else 2 if x > AW // 2 else 3)
    for x in range(1, AW - 1):
        c.px(x, y0, "fill", 5)
        c.px(x, y0 + 1, "fill", 4 if x < AW // 2 else 3)
    c.px(AW // 2, y0 + 8, "fill", 2)
    c.px(3, y0 + 2, "fill", 5)
    c.outline()
    return c


def cursor() -> dict[str, list[Piece]]:
    return {"brackets": [Piece(_brackets(f), {"fill": "gold"}) for f in range(MARK_FRAMES)],
            "arrow": [Piece(_arrow(f), {"fill": "gold"}) for f in range(MARK_FRAMES)]}


# ---------------------------------------------------------------------------------------------------- turn order
from .portraits import SIZE as PORTRAIT     # the portraits set's bust size (content/tactics/base/portraits)


def _portrait_frame(kind: str) -> tuple[Canvas, dict]:
    """A frame around a PORTRAIT-sized window. Active ("NOW") gets a heavy gold rim and a glow; inactive a quiet
    steel line; enemies a red rim with spiked corners, so they read as enemies without colour."""
    border = 2
    w = PORTRAIT + 2 * border
    o = 3
    c = Canvas(w + 2 + 2 * o, w + 2 + 2 * o, SLOTS)
    if kind == "active":
        rings: list[Ring] = [("edge", 5, 4, 2), ("edge", 3, 3, 1)]
    elif kind == "enemy":
        rings = [("edge", 4, 3, 2), ("line", 0, 0, 0)]
    else:
        rings = [("edge", 4, 3, 2), ("line", 0, 0, 0)]
    d = _depth_rect(w, w, 0 if kind == "enemy" else 2)
    d[d >= len(rings)] = -1                                   # the window stays empty
    _paint(c, o + 1, o + 1, d, rings, "edge")
    c.outline()
    if kind == "enemy":
        n = w + 1 + o
        spikes = np.zeros((c.h, c.w), bool)
        for cx, cy, sx, sy in ((o, o, -1, -1), (n, o, 1, -1), (o, n, -1, 1), (n, n, 1, 1)):
            lit = 4 if sx < 0 and sy < 0 else 2 if sx > 0 and sy > 0 else 3
            for k in range(3):
                c.px(cx + sx * k, cy + sy * k, "edge", lit if k else 3)
                spikes[cy + sy * k, cx + sx * k] = True
        ring = _dilate(spikes) & (c.slot == 0)
        c.slot[ring], c.shade[ring], c.flags[ring] = SLOTS.id("edge"), 0, FLAG_OUTLINE
    if kind == "active":
        _halo(c, "glow", 5)
    inset = o + 1 + border
    return c, {"window": [inset, inset, PORTRAIT, PORTRAIT], "slice": (inset, inset, inset, inset),
               "outset": (o, o, o, o)}


def _now_tab() -> Canvas:
    tw = _mini_width("NOW") + 6
    c = Canvas(tw + 2, 11, SLOTS)
    _paint(c, 1, 1, _depth_rect(tw, 9, 1), [("fill", 5, 4, 2)], "fill", mid=4, top=(), bottom=(3,))
    _mini_text(c, 4, 3, "NOW", "text", 1)
    c.outline()
    return c


def _ai_badge() -> Canvas:
    tw = _mini_width("AI") + 6
    c = Canvas(tw + 2, 11, SLOTS)
    _paint(c, 1, 1, _depth_rect(tw, 9, 2), [("fill", 5, 4, 2)], "fill", mid=3, top=(4,), bottom=(2,))
    _mini_text(c, 4, 3, "AI", "text", 0)
    c.outline()
    return c


def _stripe() -> Canvas:
    c = Canvas(10, 5, SLOTS)
    _paint(c, 1, 1, _depth_rect(8, 3, 1), [], "fill", mid=3, top=(4,), bottom=(2,))
    c.outline()
    return c


def turn_order() -> dict[str, Piece]:
    out: dict[str, Piece] = {}
    for kind, pal in (("active", {"edge": "gold", "glow": "gold"}),
                      ("inactive", {"edge": "steel", "line": "glass"}),
                      ("enemy", {"edge": "enemy", "line": "glass"})):
        c, m = _portrait_frame(kind)
        out[f"frame_{kind}"] = Piece(c, pal, slice=m["slice"], outset=m["outset"], alpha={"glow": 0.4},
                                     extra={"window": m["window"]})
    # Player-coloured frames: the TV shows whose hero is whose (p1-p6 from STYLE.md). Inactive heroes get a rim in
    # their player's colour; the active one keeps the gold rim with an inner line in that colour.
    for p in PLAYERS:
        c, m = _portrait_frame("inactive")
        out[f"frame_{p}"] = Piece(c, {"edge": p, "line": "glass"}, slice=m["slice"], outset=m["outset"],
                                  extra={"window": m["window"], "player": p})
        c, m = _portrait_frame("active")
        inner = (c.slot == SLOTS.id("edge")) & (c.flags == 0)
        d = _depth_rect(c.w, c.h, 0)
        ring2 = inner & (d == d[inner].max())
        c.slot[ring2] = SLOTS.id("tint")
        out[f"frame_active_{p}"] = Piece(c, {"edge": "gold", "glow": "gold", "tint": p}, slice=m["slice"],
                                         outset=m["outset"], alpha={"glow": 0.4},
                                         extra={"window": m["window"], "player": p})
    out["now_tab"] = Piece(_now_tab(), {"fill": "gold", "text": "rim_crit"},
                           extra={"attach": "x = frame box left + 2, y = frame box top - 7"})
    out["ai_badge"] = Piece(_ai_badge(), {"fill": "magic", "text": "shadow"},
                            extra={"attach": "right edge 2 px past the frame box's right, y = box top - 6"})
    stripe_at = "x = frame box left + 2, y = box bottom + 1, width = box width - 4"
    for p in PLAYERS + ("enemy", "neutral"):
        out[f"stripe_{p}"] = Piece(_stripe(), {"fill": p if p != "neutral" else "steel"}, slice=(2, 0, 2, 0),
                                   extra={"attach": stripe_at})
    return out


# ---------------------------------------------------------------------------------------------------- number font
# Chunky 2-px strokes, 9 rows. The words are single glyphs so the game can pop them as one unit.
GLYPHS: dict[str, list[str]] = {
    "0": [" #### ", "##  ##", "##  ##", "##  ##", "##  ##", "##  ##", "##  ##", "##  ##", " #### "],
    "1": ["  ##  ", " ###  ", "####  ", "  ##  ", "  ##  ", "  ##  ", "  ##  ", "  ##  ", "######"],
    "2": [" #### ", "##  ##", "    ##", "    ##", "   ## ", "  ##  ", " ##   ", "##    ", "######"],
    "3": [" #### ", "##  ##", "    ##", "    ##", "  ### ", "    ##", "    ##", "##  ##", " #### "],
    "4": ["   ###", "  ####", " ## ##", "##  ##", "##  ##", "######", "    ##", "    ##", "    ##"],
    "5": ["######", "##    ", "##    ", "##### ", "    ##", "    ##", "    ##", "##  ##", " #### "],
    "6": [" #### ", "##  ##", "##    ", "##### ", "##  ##", "##  ##", "##  ##", "##  ##", " #### "],
    "7": ["######", "    ##", "    ##", "   ## ", "   ## ", "  ##  ", "  ##  ", "  ##  ", "  ##  "],
    "8": [" #### ", "##  ##", "##  ##", "##  ##", " #### ", "##  ##", "##  ##", "##  ##", " #### "],
    "9": [" #### ", "##  ##", "##  ##", "##  ##", " #####", "    ##", "    ##", "##  ##", " #### "],
    "+": ["      ", "      ", "  ##  ", "  ##  ", "######", "######", "  ##  ", "  ##  ", "      "],
    "-": ["     ", "     ", "     ", "     ", "#####", "#####", "     ", "     ", "     "],
}
LETTERS: dict[str, list[str]] = {
    "M": ["##   ##", "### ###", "#######", "## # ##", "##   ##", "##   ##", "##   ##", "##   ##", "##   ##"],
    "I": ["####", " ## ", " ## ", " ## ", " ## ", " ## ", " ## ", " ## ", "####"],
    "S": [" #####", "##    ", "##    ", "###   ", " #### ", "   ###", "    ##", "    ##", "##### "],
    "C": [" #####", "##    ", "##    ", "##    ", "##    ", "##    ", "##    ", "##    ", " #####"],
    "R": ["##### ", "##  ##", "##  ##", "##  ##", "##### ", "## ## ", "##  ##", "##  ##", "##  ##"],
    "T": ["######", "  ##  ", "  ##  ", "  ##  ", "  ##  ", "  ##  ", "  ##  ", "  ##  ", "  ##  "],
}
for _word in ("MISS", "CRIT"):
    GLYPHS[_word] = ["".join(LETTERS[ch][y] + " " for ch in _word)[:-1] for y in range(9)]

# style -> (fill ramp, rim ramp). Damage is warm white with a blood-dark rim; the rest are tinted.
STYLES = {"damage": ("ink", "rim_damage"), "heal": ("hp", "rim_heal"), "crit": ("crit", "rim_crit"),
          "mana": ("magic", "rim_mana")}
GLYPH_H = 9
FILL_ROWS = (5, 5, 4, 4, 4, 3, 3, 3, 2)                       # top to bottom: lit crown, darker foot


def _glyph(bits: list[str]) -> Canvas:
    """Fill, a 1 px rim all round (diagonals too, so strokes stay solid at small sizes) and a 1 px drop shadow
    down-right, which lifts the number off busy terrain."""
    gw = len(bits[0])
    c = Canvas(gw + 3, GLYPH_H + 3, SLOTS)
    f = np.zeros((c.h, c.w), bool)
    for y, row in enumerate(bits):
        for x, ch in enumerate(row):
            if ch == "#":
                f[y + 1, x + 1] = True
    rim = _dilate(f, diagonal=True) & ~f
    body = f | rim
    sh = np.zeros_like(body)
    sh[1:, 1:] = body[:-1, :-1]
    sh &= ~body
    c.slot[sh], c.shade[sh] = SLOTS.id("shadow"), 3
    c.slot[rim], c.shade[rim] = SLOTS.id("rim"), 3
    for y in range(c.h):
        for x in range(c.w):
            if f[y, x]:
                c.px(x, y, "text", FILL_ROWS[y - 1])
                if x > 0 and not f[y, x - 1] and y - 1 < 5:
                    c.px(x, y, "text", 5)                     # lit left edge of each stroke
    return c


def number_font() -> dict[str, dict[str, Piece]]:
    out: dict[str, dict[str, Piece]] = {}
    for style, (fill, rim) in STYLES.items():
        out[style] = {}
        for g, bits in GLYPHS.items():
            out[style][g] = Piece(_glyph(bits), {"text": fill, "rim": rim, "shadow": "shadow"},
                                  extra={"advance": len(bits[0]) + 2})
    return out


# ---------------------------------------------------------------------------------------------------------- d20
def _d20() -> Canvas:
    """A d20 seen face-on: a hexagon outline, the front triangle (left blank for the game to print the roll) and
    nine facets around it, each shaded by which way it faces relative to the top-left light."""
    c = Canvas(32, 32, SLOTS)
    H = [(16, 0.5), (30, 8.5), (30, 23.5), (16, 31.5), (2, 23.5), (2, 8.5)]
    T = [(16, 7.5), (25.5, 23), (6.5, 23)]
    facets = [
        ((H[5], H[0], T[0]), 5), ((H[0], H[1], T[0]), 4),
        ((H[5], T[0], T[2]), 4), ((H[1], T[1], T[0]), 2),
        ((H[4], H[5], T[2]), 3), ((H[1], H[2], T[1]), 1),
        ((H[3], H[4], T[2]), 2), ((H[2], H[3], T[1]), 1),
        ((T[2], T[1], H[3]), 2),
        ((T[0], T[1], T[2]), 4),
    ]
    for pts, sh in facets:
        c.polygon(list(pts), "fill", sh)
    # facet ridges: lit ridges catch the light, the rest are dark creases
    for a, b, sh in ((T[0], T[1], 2), (T[1], T[2], 2), (T[2], T[0], 5), (H[0], T[0], 5), (H[5], T[2], 5),
                     (H[1], T[0], 3), (H[1], T[1], 1), (H[4], T[2], 3), (H[3], T[2], 1), (H[3], T[1], 0),
                     (H[2], T[1], 0)):
        c.line(a[0], a[1], b[0], b[1], "fill", sh)
    c.outline()
    return c


def _starburst() -> Canvas:
    """The crit flash behind a CRIT number: an eight-point star with a hot core."""
    c = Canvas(24, 24, SLOTS)

    def star(r_out: float, r_in: float, slot: str, sh: int, rot: float = 0.0) -> None:
        pts = []
        for k in range(16):
            r = r_out if k % 2 == 0 else r_in
            a = -np.pi / 2 + rot + k * np.pi / 8
            pts.append((12 + r * np.cos(a), 12 + r * np.sin(a)))
        c.polygon(pts, slot, sh)

    star(11.5, 5.0, "fill", 3)
    star(8.0, 3.5, "fill", 4, np.pi / 8)
    c.ellipse(12, 12, 3.2, 3.2, "fill", 5)
    c.shade_volume("fill", hi=0, lo=1, base=3)
    c.outline()
    return c


def dice() -> dict[str, Piece]:
    d = _d20()
    return {"d20_gold": Piece(d, {"fill": "gold"}, extra={"numberCentre": [16, 17], "numberBox": [10, 12, 12, 9]}),
            "d20_dark": Piece(d, {"fill": "slate"}, extra={"numberCentre": [16, 17], "numberBox": [10, 12, 12, 9]}),
            "crit_burst": Piece(_starburst(), {"fill": "crit"})}


# ------------------------------------------------------------------------------------------------------ packing
class Atlas:
    """Shelf-packs images in insertion order (so the layout, like everything else, is deterministic)."""

    def __init__(self, width: int = 256, pad: int = 1):
        self.width, self.pad = width, pad
        self.items: list[tuple[str, Image.Image, bool]] = []

    def add(self, key: str, img: Image.Image, *, new_row: bool = False) -> str:
        self.items.append((key, img, new_row))
        return key

    def pack(self) -> tuple[Image.Image, dict[str, list[int]]]:
        x = y = row_h = 0
        rects: dict[str, list[int]] = {}
        for key, im, new_row in self.items:
            if x and (new_row or x + im.width > self.width):
                x, y, row_h = 0, y + row_h + self.pad, 0
            rects[key] = [x, y, im.width, im.height]
            x += im.width + self.pad
            row_h = max(row_h, im.height)
        w = max(r[0] + r[2] for r in rects.values())
        h = max(r[1] + r[3] for r in rects.values())
        out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        for key, im, _nr in self.items:
            out.paste(im, tuple(rects[key][:2]))
        return out, rects


def _sheet(frames: list[Image.Image]) -> Image.Image:
    out = Image.new("RGBA", (frames[0].width * len(frames), frames[0].height))
    for k, im in enumerate(frames):
        out.paste(im, (k * im.width, 0))
    return out


def nine(img: Image.Image, sl, w: int, h: int, mode: str = "tile") -> Image.Image:
    """Draws a 9-slice source at w x h the way the game does (edges and centre tiled or stretched)."""
    l, t, r, b = sl
    W, H = img.size
    out = Image.new("RGBA", (w, h))
    cols = [(0, l, 0, l), (l, W - r, l, w - r), (W - r, W, w - r, w)]
    rows = [(0, t, 0, t), (t, H - b, t, h - b), (H - b, H, h - b, h)]
    for sx0, sx1, dx0, dx1 in cols:
        for sy0, sy1, dy0, dy1 in rows:
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            patch = img.crop((sx0, sy0, sx1, sy1))
            if mode == "stretch":
                out.paste(patch.resize((dx1 - dx0, dy1 - dy0), Image.NEAREST), (dx0, dy0))
                continue
            for yy in range(dy0, dy1, patch.height):
                for xx in range(dx0, dx1, patch.width):
                    p = patch.crop((0, 0, min(patch.width, dx1 - xx), min(patch.height, dy1 - yy)))
                    out.paste(p, (xx, yy))
    return out


def bar(frame: Image.Image, fill: dict[str, Image.Image], w: int, value: float, ghost: float = 0.0,
        fill_at: tuple[int, int] = (2, 2)) -> Image.Image:
    """Composes a bar exactly as the manifest describes (used by the previews)."""
    out = nine(frame, (3, 3, 3, 3), w, frame.height)
    inner = w - 2 * fill_at[0]
    fx, fy = fill_at

    def run(left, mid, right, n):
        if n <= 0:
            return
        img = Image.new("RGBA", (n, mid.height))
        for xx in range(0, n, mid.width):
            img.paste(mid, (xx, 0))
        if left is not None:
            img.paste(left, (0, 0))
        img.paste(right, (n - right.width, 0))
        out.alpha_composite(img, (fx, fy))

    if ghost > value:
        run(None, fill["ghost_mid"], fill["ghost_right"], round(inner * ghost))
    run(fill["left"], fill["mid"], fill["right"], max(3, round(inner * value)) if value > 0 else 0)
    return out


# --------------------------------------------------------------------------------------------------------- build
def build() -> None:
    shutil.rmtree(OUT, ignore_errors=True)
    man: dict = {
        "scale": "1x sources: draw at whole multiples (3x-4x on the TV, 2x-3x on phones) with nearest filtering",
        "palette": {k: RAMPS[k] for k in ("gold", "glass", "ink", "hp", "enemy", "magic", *PLAYERS)},
        "text": TEXT,
        "nineSlice": "slice = [left, top, right, bottom] insets of the source rect. outset = how far the source "
                     "reaches outside the element's box (studs, glows): draw the source at box.x - outset.left, "
                     "box.y - outset.top, sized box + outset. padding = content inset inside the box. The centre "
                     "and edges are flat, so tile or stretch both work; tile keeps pixels square.",
    }

    # panels
    ps = panels()
    atlas = Atlas(256)
    for k, p in ps.items():
        atlas.add(k, p.image())
    img, rects = atlas.pack()
    save(img, OUT / "panels.png")
    man["panels"] = {"file": "panels.png", "pieces": {k: _entry(p, rects[k]) for k, p in ps.items()}}

    # buttons
    bs = buttons()
    BUTTONS_CACHE.update(bs)
    atlas = Atlas(256)
    for kind, states in bs.items():
        for st, p in states.items():
            atlas.add(f"{kind}/{st}", p.image())
    img, rects = atlas.pack()
    save(img, OUT / "buttons.png")
    man["buttons"] = {"file": "buttons.png",
                      "pieces": {kind: {st: _entry(p, rects[f"{kind}/{st}"]) for st, p in states.items()}
                                 for kind, states in bs.items()}}

    # bars
    frames, fills = bars()
    atlas = Atlas(128)
    for k, p in frames.items():
        atlas.add(f"frame/{k}", p.image())
    for kind, parts in fills.items():
        for part, p in parts.items():
            atlas.add(f"{kind}/{part}", p.image())
    img, rects = atlas.pack()
    save(img, OUT / "bars.png")
    man["bars"] = {
        "file": "bars.png",
        "frames": {k: {**_entry(p, rects[f"frame/{k}"]), "fill": [2, 2, BAR_FRAMES[k].fill_h]}
                   for k, p in frames.items()},
        "kinds": {kind: {"frame": BARS[kind][0], **{part: rects[f"{kind}/{part}"] for part in parts}}
                  for kind, parts in fills.items()},
        "compose": [
            "Draw the frame 9-slice at the bar's width (its height is fixed). frame.fill = [x, y, height]: the fill "
            "area starts at (x, y) and is (width - 2x) wide.",
            "fillW = round(innerW * value / max), at least 3 when value > 0. Tile `mid` across fillW, then draw "
            "`left` at the start and `right` ending at fillW (the bright leading edge).",
            "Damage ghost: when HP drops, keep the old width for ~0.4 s, then shrink it to the new one. Draw it "
            "first: tile `ghost_mid` across ghostW and end with `ghost_right`; the real fill goes on top.",
            "ct tiles in 4 px segments, so snap its fillW to a multiple of 4 for whole notches.",
        ],
    }

    # tile markers, path, cursor
    mk, ph, cu = markers(), paths(), cursor()
    rows = [(k, fr) for k, fr in mk.items()] + [(f"path/{k}", fr) for k, fr in ph.items()]
    sheet = Image.new("RGBA", (TW * MARK_FRAMES, TH * len(rows)))
    for i, (_k, fr) in enumerate(rows):
        sheet.paste(_sheet([p.image() for p in fr]), (0, i * TH))
    save(sheet, OUT / "markers.png")
    save(_sheet([p.image() for p in cu["brackets"]]), OUT / "cursor_brackets.png")
    save(_sheet([p.image() for p in cu["arrow"]]), OUT / "cursor_arrow.png")
    man["markers"] = {
        "file": "markers.png", "frame": [TW, TH], "frames": MARK_FRAMES, "fps": MARK_FPS, "loop": True,
        "origin": [TW // 2, TH // 2],
        "projection": "a 32 px tile projected 2:1 (x = U - V, y = (U + V) / 2). Put frame pixel (32, 16) on the "
                      "tile top's centre. A pixel belongs to a tile when its centre falls inside it, so markers "
                      "and path pieces on neighbouring tiles meet exactly.",
        "rows": {k: i for i, (k, _fr) in enumerate(rows)},
        "kinds": {k: {"row": i, "pattern": MARKERS[k].pattern} for i, k in enumerate(mk)},
        "path": {
            "pieces": {k: i + len(mk) for i, k in enumerate(ph)},
            "edges": {"nw": "-U (upper-left edge)", "se": "+U (lower-right)", "ne": "-V (upper-right)",
                      "sw": "+V (lower-left)"},
            "naming": "straight_<a>_<b> and corner_<a>_<b> join the two edges; start_<d> sits on the unit's tile and "
                      "leaves through <d>; end_<d> is entered through <d> and points the other way.",
        },
    }
    man["cursor"] = {
        "brackets": {"file": "cursor_brackets.png", "frame": [CW, CH], "origin": [CW // 2, CH // 2],
                     "frames": MARK_FRAMES, "fps": MARK_FPS, "loop": True},
        "arrow": {"file": "cursor_arrow.png", "frame": [AW, AH], "anchor": [AW // 2, AH - 1 - 2],
                  "frames": MARK_FRAMES, "fps": MARK_FPS, "loop": True,
                  "place": "anchor ~6 px above the unit's head, or 20 px above an empty tile's centre"},
    }

    # turn order
    to = turn_order()
    atlas = Atlas(160)
    for k, p in to.items():
        atlas.add(k, p.image())
    img, rects = atlas.pack()
    save(img, OUT / "turn_order.png")
    man["turnOrder"] = {"file": "turn_order.png", "portrait": PORTRAIT,
                        "pieces": {k: _entry(p, rects[k]) for k, p in to.items()},
                        "players": list(PLAYERS)}

    # font
    nf = number_font()
    atlas = Atlas(512)
    for style, gl in nf.items():
        for i, (g, p) in enumerate(gl.items()):
            atlas.add(f"{style}/{g}", p.image(), new_row=i == 0)
    img, rects = atlas.pack()
    save(img, OUT / "numbers.png")
    man["numbers"] = {
        "file": "numbers.png", "lineHeight": GLYPH_H + 3, "baseline": GLYPH_H + 1,
        "note": "Each rect includes a 1 px rim and a 1 px drop shadow (right and below). Pen x advances by "
                "`advance`; neighbouring glyphs' rims touch but their fills keep 2 px apart.",
        "styles": {style: {g: {"rect": rects[f"{style}/{g}"], "advance": p.extra["advance"]} for g, p in gl.items()}
                   for style, gl in nf.items()},
    }

    # dice
    dc = dice()
    atlas = Atlas(128)
    for k, p in dc.items():
        atlas.add(k, p.image())
    img, rects = atlas.pack()
    save(img, OUT / "dice.png")
    man["dice"] = {"file": "dice.png", "pieces": {k: _entry(p, rects[k]) for k, p in dc.items()}}

    # the second half of the kit (parts/ui_hud.py): HUD extras, animated strips, input glyphs
    from parts import ui_hud as H
    hud = H.hud_pieces(ps)
    img, rects = H.pack(hud)
    save(img, OUT / "hud.png")
    man["hud"] = {"file": "hud.png", "pieces": {k: _entry(p, rects[k]) for k, p in hud.items()}}
    anims = H.strips(ps)
    man["anims"] = {}
    for k, (fr, meta) in anims.items():
        save(H.strip_image(fr), OUT / f"{k}.png")
        man["anims"][k] = {"file": f"{k}.png", "frame": [fr[0].canvas.w, fr[0].canvas.h], "frames": len(fr),
                           **meta}
        if fr[0].slice:
            man["anims"][k].update({"slice": list(fr[0].slice), "outset": list(fr[0].outset)})
    gl = H.glyphs()
    img, rects = H.pack(gl, 192)
    save(img, OUT / "glyphs.png")
    man["glyphs"] = {"file": "glyphs.png", "pieces": {k: _entry(p, rects[k]) for k, p in gl.items()},
                     "note": "Gamepad (Xbox letters and colours, PlayStation shapes), keyboard and mouse prompts at 1x."
                             " "
                             "key_blank is a 9-slice cap for any legend the game sets itself. Show glyphs at the "
                             "UI scale beside the action's name, e.g. [A] Confirm  [B] Back  [LB][RB] Cycle."}
    man["bars"]["compose"] += [
        "Damage ghost drain (anims.ghost_drain): after the hit, hold the ghost 0.3 s, then over the strip's 5 "
        "frames tile frame f as the ghost and ease its width to the new value.",
        "Heal sweep (anims.heal_sweep): grow the fill to the new value over 0.3 s; meanwhile play the sweep "
        "additively across the healed span (scale its travel to that span, clip it to the fill).",
    ]
    write_json(man, OUT / "manifest.json")
    _previews(ps, bs, frames, fills, mk, ph, cu, to, nf, dc)
    _extras_preview(ps, hud, anims, gl, to, frames, fills)


def _entry(p: Piece, rect: list[int]) -> dict:
    e: dict = {"rect": rect}
    if p.slice:
        e["slice"] = list(p.slice)
    if any(p.outset):
        e["outset"] = list(p.outset)
    if p.padding:
        e["padding"] = list(p.padding)
    if p.extra:
        e.update(p.extra)
    return e


# ------------------------------------------------------------------------------------------------------- previews
def _label(img: Image.Image, k: int, text: str, xy, size: float = 7, fill=INK, bold: bool = True, anchor="la"):
    """Preview-only text, drawn on an image already scaled by k; `xy` and `size` are in 1x pixels. The assets carry
    no text: the game sets its own fonts on top."""
    d = ImageDraw.Draw(img)
    d.text((xy[0] * k, xy[1] * k), text, font=font(round(size * k), bold), fill=fill, anchor=anchor)


def _row(images, gap: int = 4) -> Image.Image:
    """Images side by side, each at its own size (strip() assumes equal frames)."""
    out = Image.new("RGBA", (sum(i.width for i in images) + gap * (len(images) - 1), max(i.height for i in images)))
    x = 0
    for im in images:
        out.alpha_composite(im, (x, (out.height - im.height) // 2))
        x += im.width + gap
    return out


def _previews(ps, bs, frames, fills, mk, ph, cu, to, nf, dc) -> None:
    sections = []
    tiles = []
    for k, p in ps.items():
        im = p.image()
        tiles.append(Tile(im, k, f"{im.width}x{im.height} source"))
    sections.append(contact_sheet("Panels · sources", tiles, columns=6, zoom=4, checker=True,
                                  subtitle="9-slice sources at 1x: navy glass, gold rim lit from the top-left, gold "
                                           "diamond studs on the top corners."))
    tiles = []
    for k, (w, h) in (("main", (120, 44)), ("main", (60, 70)), ("card", (80, 22)), ("card_selected", (80, 22)),
                      ("tooltip", (90, 14)), ("dialogue", (140, 44)), ("banner", (110, 25)),
                      ("turn_strip", (150, 50))):
        p = ps[k]
        ol, ot, orr, ob = p.outset
        tiles.append(Tile(nine(p.image(), p.slice, w + ol + orr, h + ot + ob), k, f"stretched to {w}x{h}"))
    # the composed dialogue: box + name tab
    dlg, tab = ps["dialogue"], ps["dialogue_tab"]
    box = nine(dlg.image(), dlg.slice, 140 + 8, 44 + 8)
    comp = Image.new("RGBA", (box.width, box.height + 4))
    comp.alpha_composite(box, (0, 4))
    comp.alpha_composite(nine(tab.image(), tab.slice, 44, 13), (4 + 6, 4 + 4 - 7))
    tiles.append(Tile(comp, "dialogue + dialogue_tab", "the tab straddles the top edge"))
    tip = ps["tooltip"]
    tb = nine(tip.image(), tip.slice, 60 + 4, 14 + 2)
    tc = Image.new("RGBA", (tb.width, tb.height + 4))
    tc.alpha_composite(tb)
    tail = ps["tooltip_tail"].image()
    tc.alpha_composite(tail, ((tb.width - tail.width) // 2, tb.height - 2))
    tiles.append(Tile(tc, "tooltip + tooltip_tail"))
    sections.append(contact_sheet("Panels · stretched", tiles, columns=4, zoom=3, checker=True,
                                  subtitle="Every piece drawn through the same 9-slice the game uses (edges and centre "
                                           "tiled)."))
    tiles = []
    for kind, states in bs.items():
        for st, p in states.items():
            im = p.image()
            tiles.append(Tile(_row([im, nine(im, p.slice, 52, im.height)], 6), f"{kind} · {st}",
                              f"text: {p.extra['text']}" + (" · content +1 px" if st == "pressed" else "")))
    sections.append(contact_sheet("Buttons", tiles, columns=4, zoom=4, checker=True,
                                  subtitle="normal · hover (lifted + halo) · pressed (drops 1 px, lip gone) · "
                                           "disabled. Source, then stretched to 50 px."))
    save(stack("UI kit · panels and buttons", sections), PREVIEWS / "15-ui-panels.png")

    sections = []
    tiles = []
    for kind, parts in fills.items():
        fr = frames[BARS[kind][0]].image()
        pi = {n: p.image() for n, p in parts.items()}
        rows = [bar(fr, pi, 64, v, g) for v, g in ((1.0, 0), (0.62, 0), (0.62, 0.85), (0.2, 0.45), (0.05, 0))]
        tall = Image.new("RGBA", (64, sum(r.height + 3 for r in rows)))
        y = 0
        for r in rows:
            tall.paste(r, (0, y))
            y += r.height + 3
        tiles.append(Tile(tall, kind, "100 · 62 · 62+ghost · 20+ghost · 5%"))
    sections.append(contact_sheet("Bars", tiles, columns=5, zoom=4,
                                  subtitle="Frame 9-slice + tiled fill with end caps. The pale ghost shows damage "
                                           "just taken."))
    sections.append(_turn_preview(to))
    sections.append(_font_preview(nf, dc))
    save(stack("UI kit · HUD pieces", sections), PREVIEWS / "15-ui-hud.png")

    sections = []
    tiles = [Tile(strip([p.image() for p in fr], 1, gap=4), k, f"{MARKERS[k].pattern} · 4-frame pulse")
             for k, fr in mk.items()]
    tiles += [Tile(strip([p.image() for p in cu["brackets"]], 1, gap=4), "cursor brackets", "breathe outwards"),
              Tile(strip([p.image() for p in cu["arrow"]], 1, gap=4), "cursor arrow", "bobs 2 px")]
    sections.append(contact_sheet("Tile markers", tiles, columns=2, zoom=3, checker=True,
                                  subtitle="64x32 = one 32 px tile projected 2:1. Ranges differ by pattern as well as "
                                           "colour: plain, hatched, ripple, core."))
    tiles = [Tile(ph[k][0].image(), k) for k in PATH_PIECES]
    sections.append(contact_sheet("Path pieces", tiles, columns=7, zoom=3, checker=True,
                                  subtitle="Straight, corner, start and end for the four iso edges; shapes run "
                                           "past the tile edge so neighbours join seamlessly."))
    board = _scene(mk, ph, cu, 0, margin=24)[0]
    board = board.crop((40, 100, board.width - 40, board.height - 30))
    sections.append(contact_sheet("In context", [Tile(board, "", "move range under a planned path, attack targets "
                                                                 "(hatched), an area (ripple), an ally target (core) "
                                                                 "and the cursor")], columns=1, zoom=3))
    save(stack("UI kit · tile markers and cursor", sections), PREVIEWS / "15-ui-markers.png")

    _mock(ps, bs, frames, fills, mk, ph, cu, to, nf, dc)


def _extras_preview(ps, hud, anims, gl, to, frames, fills) -> None:
    """previews/15-ui-extras.png (the ui_hud pieces, stretched as the game uses them) and 15-ui-bars.gif."""
    from parts import ui_hud as H
    sections = []
    tiles = []
    for k in ("tooltip_header", "toast_info", "toast_success", "toast_warning", "toast_danger", "objective",
              "reaction", "minimap"):
        p = hud[k]
        ol, ot, orr, ob = p.outset
        w, h = {"tooltip_header": (96, 40), "objective": (110, 40), "reaction": (130, 44), "minimap": (64, 64)}.get(
            k, (100, 16))
        tiles.append(Tile(_row([p.image(), nine(p.image(), p.slice, w + ol + orr, h + ot + ob)], 8), k,
                          f"source, then stretched to {w}x{h}"))
    tiles.append(Tile(_row([hud[k].image() for k in hud if k.startswith(("check_", "pip_", "glint_", "minimap_"))],
                           4), "checks · map pips · glints · compass"))
    sections.append(contact_sheet("HUD extras", tiles, columns=3, zoom=3, checker=True,
                                  subtitle="Tooltip with a header band, toasts by mood, objective tracker and its "
                                           "boxes, the reaction prompt, the minimap frame and its pips."))
    tiles = []
    for k, (fr, meta) in anims.items():
        tiles.append(Tile(strip([p.image() for p in fr], 1, gap=3), k, f"{len(fr)} frames"))
    sections.append(contact_sheet("Animated strips", tiles, columns=1, zoom=3, checker=True,
                                  subtitle="Panel shimmer (corner cells only, so any size works), the reaction "
                                           "countdown, the heal sweep and the damage-ghost drain."))
    tiles = [Tile(p.image(), k.replace("pad_", "").replace("key_", "")) for k, p in gl.items()]
    sections.append(contact_sheet("Input glyphs", tiles, columns=12, zoom=4, checker=True, label_h=22,
                                  subtitle="Xbox face buttons in their own colours, PlayStation shapes, bumpers, "
                                           "triggers, d-pad, sticks, menu/view, keycaps and the mouse."))
    tiles = [Tile(to[f"frame_{p}"].image(), f"frame_{p}") for p in PLAYERS]
    tiles += [Tile(to[f"frame_active_{p}"].image(), f"frame_active_{p}") for p in PLAYERS]
    tiles += [Tile(_row([bs.image() for bs in (BUTTONS_CACHE[k]["focused"] for k in BUTTONS_CACHE)], 6),
                   "buttons · focused", "the controller's cursor")] if BUTTONS_CACHE else []
    sections.append(contact_sheet("Turn order · player colours", tiles, columns=6, zoom=3, checker=True))
    save(stack("UI kit · extras for TV and controller", sections), PREVIEWS / "15-ui-extras.png")

    # bars in motion: a hit (ghost holds, then drains), then a heal (fill grows under the sweep)
    fr = frames["standard"].image()
    pi = {n: p.image() for n, p in fills["hp"].items()}
    drain = [p.image() for p in anims["ghost_drain"][0]]
    sweep = [p.image() for p in anims["heal_sweep"][0]]
    gif = []
    W = 96
    inner = W - 4
    seq = [(0.9, 0.9, None, None)] * 3 + [(0.45, 0.9, 0, None)] * 3
    seq += [(0.45, 0.9 - (0.45 * t / 4), min(t, 4), None) for t in range(5)] + [(0.45, 0, None, None)] * 3
    seq += [(0.45 + 0.35 * min(1, t / 3), 0, None, t) for t in range(6)] + [(0.8, 0, None, None)] * 4
    for value, ghost, df, sf in seq:
        f = dict(pi)
        if df is not None:
            f["ghost_mid"] = drain[df]
            f["ghost_right"] = drain[df].crop((0, 0, 1, drain[df].height))
        im = bar(fr, f, W, value, ghost)
        if sf is not None:
            lo, hi = round(inner * 0.45), round(inner * value)
            sw = sweep[sf]
            span = Image.new("RGBA", (max(1, hi - lo), sw.height))
            span.alpha_composite(sw.resize((max(1, hi - lo), sw.height), Image.NEAREST))
            layer = Image.new("RGBA", im.size)
            layer.alpha_composite(span, (2 + lo, 2))
            im = Image.alpha_composite(im, layer)
        canvas = Image.new("RGBA", (W + 16, fr.height + 16), BG)
        canvas.alpha_composite(im, (8, 8))
        gif.append(scale(canvas, 5).convert("RGB"))
    gif[0].save(PREVIEWS / "15-ui-bars.gif", save_all=True, append_images=gif[1:], duration=80, loop=0, optimize=True)


BUTTONS_CACHE: dict = {}


def _portraits() -> dict[str, Image.Image]:
    """Real portraits from the portraits set when it is built; otherwise a head crop of the composed character."""
    from parts import characters as C
    from parts.recipes import ALL
    from parts.rig import ANIMS, BUILDS
    out = {}
    for rid in ("kara", "goblin_cutthroat", "ilyra", "orc_brute", "aldric", "wren", "goblin_shaman", "tamsin"):
        path = PACK / "portraits" / f"{rid}_idle.png"
        if path.exists():
            out[rid] = Image.open(path).convert("RGBA")
            continue
        r = next(x for x in ALL if x.id == rid)
        top = BUILDS[r.build].head_top
        img = C.compose(r, "front", ANIMS["idle"][2][0]).render(r.ramps()).crop((6, top - 4, 26, top + 16))
        img = img.resize((PORTRAIT, PORTRAIT), Image.NEAREST)
        bg = Image.new("RGBA", (PORTRAIT, PORTRAIT), (40, 44, 84, 255))
        bg.alpha_composite(img)
        out[rid] = bg
    return out


ENEMY_IDS = ("goblin_cutthroat", "orc_brute", "goblin_shaman")
OWNERS = {"kara": "p1", "ilyra": "p3", "aldric": "p2", "wren": "p4", "tamsin": "p5"}


def _framed(to, rid: str, kind: str, por: Image.Image) -> Image.Image:
    p = to[f"frame_{kind}"]
    f = p.image()
    out = Image.new("RGBA", f.size)
    win = p.extra["window"]
    out.alpha_composite(por, (win[0], win[1]))
    out.alpha_composite(f)
    return out


def _turn_strip_image(to, n: int = 8) -> Image.Image:
    """A turn-order row the way the TV composes it: frame, portrait, stripe, NOW tab and AI badge."""
    pieces = {k: p.image() for k, p in to.items()}
    fw = pieces["frame_inactive"].width
    step = fw - 3
    out = Image.new("RGBA", (n * step + 3, fw + 10), (0, 0, 0, 0))
    for i, (rid, por) in enumerate(list(_portraits().items())[:n]):
        x, y = i * step, 4
        own = OWNERS.get(rid)
        kind = "enemy" if rid in ENEMY_IDS else (f"active_{own}" if i == 0 else own) if own else \
            "active" if i == 0 else "inactive"
        out.alpha_composite(_framed(to, rid, kind, por), (x, y))
        stripe = pieces[f"stripe_{OWNERS.get(rid, 'enemy')}"]
        out.alpha_composite(nine(stripe, (2, 0, 2, 0), fw - 10, stripe.height), (x + 5, y + fw - 2))
        if i == 0:
            out.alpha_composite(pieces["now_tab"], (x + 5, y - 4))
        if rid == "aldric":
            ai = pieces["ai_badge"]
            out.alpha_composite(ai, (x + fw - ai.width - 1, y - 3))
    return out


def _turn_preview(to) -> Image.Image:
    tiles = [Tile(p.image(), k) for k, p in to.items() if not k.startswith("stripe")]
    tiles.append(Tile(strip([to[f"stripe_{p}"].image() for p in PLAYERS + ("enemy", "neutral")], 1, gap=2),
                      "stripes", "p1-p6 · enemy · neutral"))
    a = contact_sheet("Turn order · pieces", tiles, columns=6, zoom=4, checker=True,
                      subtitle="Frames fit the portraits set's busts. Enemy frames have spiked corners, so they read "
                               "without colour.")
    b = contact_sheet("Turn order · composed", [Tile(_turn_strip_image(to), "", "")], columns=1, zoom=4)
    out = Image.new("RGBA", (max(a.width, b.width), a.height + b.height), BG)
    out.paste(a, (0, 0))
    out.paste(b, (0, a.height))
    return out


def _word(nf, style: str, text: str) -> Image.Image:
    """Lays glyphs out with their advances, as the game will."""
    gl = nf[style]
    keys = [text] if text in gl else list(text)
    w = sum(gl[k].extra["advance"] for k in keys) + 1
    out = Image.new("RGBA", (w, GLYPH_H + 3))
    x = 0
    for k in keys:
        out.alpha_composite(gl[k].image(), (x, 0))
        x += gl[k].extra["advance"]
    return out


def _crit_pop(nf, dc, text: str) -> Image.Image:
    burst = dc["crit_burst"].image()
    w = _word(nf, "crit", text)
    out = Image.new("RGBA", (w.width + burst.width // 2 + 2, burst.height))
    out.alpha_composite(burst, (0, 0))
    out.alpha_composite(w, (burst.width // 2 + 2, (burst.height - w.height) // 2))
    return out


def _font_preview(nf, dc) -> Image.Image:
    tiles = []
    for style in STYLES:
        tiles.append(Tile(_row([_word(nf, style, s) for s in ("0123456789", "+-")], 6), style, "digits · + -"))
    for style, sample in (("damage", "-9"), ("damage", "-128"), ("heal", "+12"), ("mana", "-4"), ("crit", "-27"),
                          ("damage", "MISS"), ("crit", "CRIT")):
        tiles.append(Tile(_word(nf, style, sample), style, sample))
    tiles.append(Tile(_crit_pop(nf, dc, "-27"), "crit pop", "burst behind the number"))
    for k in ("d20_gold", "d20_dark", "crit_burst"):
        tiles.append(Tile(dc[k].image(), k, "number area left blank" if k.startswith("d20") else ""))
    return contact_sheet("Combat numbers and dice", tiles, columns=4, zoom=4, checker=True,
                         subtitle="Chunky 2 px strokes, 1 px rim, drop shadow. damage · heal · crit · mana.")


def _tex(tex_id: str, variant: str | None = None) -> np.ndarray:
    from parts import terrain as TR
    t = next(x for x in TR.CATALOGUE if x.id == tex_id)
    v = t.variants[variant] if variant else next(iter(t.variants.values()))
    return np.asarray(t.frames()[0].render(TR.ramps(v))).astype(np.float32)


def _unit(rid: str, i: float, j: float, mirror: bool = False):
    from arcpix import iso
    from parts import characters as C
    from parts.recipes import ALL
    from parts.rig import ANIMS, ANCHOR
    r = next(x for x in ALL if x.id == rid)
    img = C.compose(r, "front", ANIMS["idle"][2][0]).render(r.ramps())
    if mirror:
        return iso.Sprite(img.transpose(Image.FLIP_LEFT_RIGHT), (img.width - 1 - ANCHOR[0], ANCHOR[1]), i, j)
    return iso.Sprite(img, ANCHOR, i, j)


def _grid_scene():
    """A small grass-and-road plateau from the terrain set, to judge the markers where they will be used."""
    from arcpix import iso
    s = iso.Scene(9, 7)
    grass, lip, side, path = _tex("grass", "green"), _tex("grass_lip", "green"), _tex("dirt_side"), _tex("path")
    for i in range(9):
        for j in range(7):
            road = j == 3
            h = 3 if (i >= 6 and j <= 1) else 2
            s.cells[(i, j)] = iso.Cell(h, path if road else grass, side, lip=None if road else lip)
    return s


HERO, TARGET = (2, 3), (5, 2)
ROUTE = [(2, 3), (3, 3), (4, 3), (4, 2), (5, 2)]
FOE = (6, 2)


def _route_pieces() -> list[tuple[str, tuple[int, int]]]:
    """Picks the path piece for each tile of ROUTE from the edges the path crosses there."""
    def edge(p, q):
        return {(1, 0): "se", (-1, 0): "nw", (0, 1): "sw", (0, -1): "ne"}[(q[0] - p[0], q[1] - p[1])]

    out = []
    for a, b, c in zip([None] + ROUTE[:-1], ROUTE, ROUTE[1:] + [None]):
        if a is None:
            out.append((f"start_{edge(b, c)}", b))
        elif c is None:
            out.append((f"end_{edge(b, a)}", b))
        else:
            e = {edge(b, a), edge(b, c)}
            out.append((next(n for n in PATH_PIECES if not n.startswith(("start", "end"))
                             and set(n.split("_")[1:]) == e), b))
    return out


def _scene(mk, ph, cu, frame: int, *, margin: int = 24, units: bool = True):
    """The plateau with this kit's markers on it, units drawn over them. Returns (image, tile -> screen centre)."""
    from arcpix import iso
    scene = _grid_scene()
    base = iso.render(scene, margin=margin)
    max_h = max(c.h for c in scene.cells.values()) * iso.STEP
    ox, oy = scene.d * iso.TILE + margin, max_h + margin + 50
    arr = np.asarray(base).astype(np.float32).copy()

    def centre(t) -> tuple[float, float]:
        i, j = t
        return (i - j) * TILE + ox, (i + j + 1) * TILE / 2 - scene.top_z(i, j) + oy

    def place(img: Image.Image, t, origin, dy: int = 0) -> None:
        cx, cy = centre(t)
        x0, y0 = int(round(cx - origin[0])), int(round(cy - origin[1])) + dy
        a = np.asarray(img).astype(np.float32)
        sub = arr[y0:y0 + a.shape[0], x0:x0 + a.shape[1]]
        al = a[:sub.shape[0], :sub.shape[1], 3:4] / 255
        sub[..., :3] = sub[..., :3] * (1 - al) + a[:sub.shape[0], :sub.shape[1], :3] * al

    moves = [(i, j) for i in range(9) for j in range(7) if 0 < abs(i - HERO[0]) + abs(j - HERO[1]) <= 3]
    overlays = [("move", t) for t in moves if t != TARGET]
    overlays += [("selected", TARGET), ("attack", FOE), ("attack", (5, 1)), ("ally", (1, 1))]
    overlays += [("area", t) for t in ((6, 5), (7, 5), (6, 6), (7, 4), (5, 5))]
    for k, t in overlays:
        place(mk[k][frame].image(), t, (TW // 2, TH // 2))
    for n, t in _route_pieces():
        place(ph[n][frame].image(), t, (TW // 2, TH // 2))
    place(cu["brackets"][frame].image(), TARGET, (CW // 2, CH // 2))
    if units:
        sprites = [_unit("kara", *HERO), _unit("goblin_cutthroat", *FOE), _unit("tamsin", 1, 1),
                   _unit("orc_brute", 5, 1, mirror=True)]
        for s in sorted(sprites, key=lambda s: s.i + s.j):
            iso._draw_sprite(arr, scene, s, ox, oy)
    place(cu["arrow"][frame].image(), TARGET, (AW // 2, AH - 3), dy=-22)
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGBA"), centre


MOCK_W, MOCK_H = 640, 360            # 1920x1080 at 3x, the TV mockup's frame


def _mock_frame(ps, bs, frames, fills, mk, ph, cu, to, nf, dc, f: int):
    """One 1x frame of the battle mock: the scene (with HD-2D post) and every UI piece placed at 1x, plus the
    text to draw on top once scaled, as (x, y, text, size, colour, bold, anchor)."""
    from arcpix import iso
    board, centre = _scene(mk, ph, cu, f % MARK_FRAMES, margin=64)
    y0 = 112
    img = iso.finish(board.crop((0, y0, MOCK_W, y0 + MOCK_H)), zoom=1).convert("RGBA")
    texts: list[tuple] = []
    por = _portraits()

    def tile(t):
        x, y = centre(t)
        return x, y - y0

    def put(p: Piece, x: int, y: int, w: int, h: int) -> None:
        ol, ot, orr, ob = p.outset
        img.alpha_composite(nine(p.image(), p.slice, w + ol + orr, h + ot + ob), (x - ol, y - ot))

    def text(x, y, s, size=7, fill=INK, bold=True, anchor="la"):
        texts.append((x, y, s, size, fill, bold, anchor))

    gold, muted, red, good = (230, 189, 92), MUTED, (232, 120, 110), (116, 224, 138)
    # floating number over the foe: rises and holds
    fx, fy = tile(FOE)
    word = _word(nf, "damage", "-9") if f < MARK_FRAMES else _crit_pop(nf, dc, "-27")
    word = word.resize((word.width * 2, word.height * 2), Image.NEAREST)     # numbers pop at 2x the UI scale
    img.alpha_composite(word, (int(fx - 22 - word.width // 2), int(fy - 62 - min(f % MARK_FRAMES, 2) * 3)))
    # tooltip under the foe
    tw = 128
    put(ps["tooltip"], int(fx - tw // 2), int(fy + 14), tw, 12)
    tail = ps["tooltip_tail"].image()
    img.alpha_composite(tail.transpose(Image.FLIP_TOP_BOTTOM), (int(fx - tail.width // 2), int(fy + 14 - 4)))
    text(fx, fy + 20.5, "Pushed 1 tile into the river · Prone", 5.2, INK, True, "mm")
    # title
    put(ps["main"], 10, 8, 196, 34)
    text(20, 13, "CHAPTER III · THE ASHEN FORD", 9.5, (244, 236, 214))
    text(20, 28, "Battle · Round 2 · Objective: Take the bridge", 5.5, muted, False)
    # ability banner
    bx, bw = 220, 118
    put(ps["banner"], bx, 10, bw, 25)
    crest = ps["banner_crest"].image()
    img.alpha_composite(crest, (bx + bw // 2 - crest.width // 2 + 1, 6))
    text(bx + bw / 2 + 1, 19, "KARA · VANGUARD", 4.2, muted, True, "mm")
    text(bx + bw / 2 + 1, 27, "SHIELD BASH", 8.5, (255, 226, 150), True, "mm")
    # turn order
    row = _turn_strip_image(to, 5)
    sw = row.width + 8
    sx = MOCK_W - 10 - sw
    put(ps["turn_strip"], sx, 8, sw, row.height + 16)
    text(sx + 7, 13, "TURN ORDER", 4.5, gold)
    img.alpha_composite(row, (sx + 4, 20))
    # the roll
    rx, ry = 452, 98
    put(ps["main"], rx, ry, 150, 44)
    img.alpha_composite(dc["d20_gold"].image(), (rx + 6, ry + 6))
    img.alpha_composite(dc["d20_dark"].image(), (rx + 40, ry + 6))
    text(rx + 22, ry + 23.5, "17", 7, (60, 38, 8), True, "mm")
    text(rx + 56, ry + 23.5, "6", 7, (150, 156, 170), True, "mm")
    text(rx + 80, ry + 9, "17 +5 = 22 vs AC 13", 6)
    text(rx + 80, ry + 19, "HIT", 9, good)
    text(rx + 80, ry + 33, "Advantage: its back is turned", 4.6, muted, False)
    # the active unit
    ux, uy = 10, MOCK_H - 72
    put(ps["main"], ux, uy, 206, 62)
    img.alpha_composite(_framed(to, "kara", "active", por["kara"]), (ux + 4, uy + 6))
    text(ux + 60, uy + 7, "KARA", 10)
    text(ux + 146, uy + 9, "● Jess", 5, (255, 138, 61))
    text(ux + 60, uy + 20, "Human Fighter · Vanguard · Lv 3", 5, muted, False)
    fill = {n: p.image() for n, p in fills["hp"].items()}
    hp = bar(frames["standard"].image(), fill, 104, 31 / 34, 0.97 if f % 4 < 2 else 0)
    img.alpha_composite(hp, (ux + 60, uy + 29))
    text(ux + 170, uy + 29, "31/34", 5.5)
    fill = {n: p.image() for n, p in fills["mp"].items()}
    img.alpha_composite(bar(frames["standard"].image(), fill, 104, 0.5), (ux + 60, uy + 39))
    text(ux + 170, uy + 39, "6/12", 5.5)
    for i, (kind, st, label) in enumerate((("secondary", "disabled", "MOVE"), ("primary", "normal", "ACTION"),
                                           ("secondary", "hover", "BONUS"), ("secondary", "normal", "REACT"))):
        p = bs[kind][st]
        x, y = ux + 60 + i * 36, uy + 49
        put(p, x, y, 34, 13)
        col = {"on_gold": (42, 27, 4), "ink": INK, "dim": (138, 130, 109)}[p.extra["text"]]
        text(x + 17, y + 6.2, label, 4.4, col, True, "mm")
    # the target
    ex, ey = MOCK_W - 216, MOCK_H - 72
    put(ps["main"], ex, ey, 206, 62)
    img.alpha_composite(_framed(to, "goblin_cutthroat", "enemy", por["goblin_cutthroat"]), (ex + 4, ey + 6))
    text(ex + 60, ey + 7, "GOBLIN CUTTHROAT", 8.5, red)
    text(ex + 60, ey + 19, "Enemy · Skirmisher", 5, muted, False)
    fill = {n: p.image() for n, p in fills["enemy_hp"].items()}
    img.alpha_composite(bar(frames["standard"].image(), fill, 104, 9 / 16, 12 / 16 if f % 4 < 3 else 0),
                        (ex + 60, ey + 29))
    text(ex + 170, ey + 29, "9/16", 5.5)
    fill = {n: p.image() for n, p in fills["ct"].items()}
    img.alpha_composite(bar(frames["slim"].image(), fill, 104, 0.6), (ex + 60, ey + 40))
    text(ex + 170, ey + 39, "CT", 5, muted)
    for i, (lab, w) in enumerate((("Back turned", 50), ("Bloodied", 40))):
        x = ex + 60 + i * 56
        put(ps["card"], x, ey + 49, w, 11)
        text(x + w / 2, ey + 54.5, lab, 4.4, INK, True, "mm")
    # extras: objective tracker, minimap, a toast and the controller hints (parts/ui_hud.py)
    from parts import ui_hud as H
    hud = H.hud_pieces(ps)
    put(hud["objective"], 10, 47, 132, 34)
    text(18, 50.5, "OBJECTIVE", 4.3, gold)
    text(118, 50.5, "R2", 4.3, muted)
    for i, (st, lab) in enumerate((("done", "Reach the ford"), ("open", "Take the bridge"))):
        bx_, by_ = 17, 60 + i * 10
        img.alpha_composite(hud[f"check_{st}"].image(), (bx_, by_ - 1))
        text(bx_ + 14, by_ + 0.5, lab, 5, muted if st == "done" else INK, False)
    mm = hud["minimap"]
    mmap = Image.new("RGBA", (50, 50), (34, 52, 40, 230))
    md = ImageDraw.Draw(mmap)
    md.polygon([(25, 4), (46, 25), (25, 46), (4, 25)], fill=(74, 118, 64, 255))
    md.line([(14, 30), (36, 14)], fill=(76, 140, 196, 255), width=3)
    img.alpha_composite(mmap, (17, 99))
    put(mm, 14, 96, 56, 56)
    mc = hud["minimap_compass"].image()
    img.alpha_composite(mc, (14 + 28 - mc.width // 2, 96 - 6))
    for kind, (px_, py_) in (("you", (34, 128)), ("ally", (40, 132)), ("ally", (30, 120)), ("enemy", (50, 112)),
                             ("enemy", (54, 118)), ("objective", (56, 104))):
        img.alpha_composite(hud[f"pip_{kind}"].image(), (px_, py_))
    put(hud["toast_success"], 452, 150, 150, 16)
    text(469, 155, "Ilyra reached level 4", 5, INK, False)
    hx, hy = 236, MOCK_H - 76
    gl = H.glyphs()
    for g, lab in (("pad_a", "Confirm"), ("pad_b", "Back"), ("pad_lb", None), ("pad_rb", "Target")):
        im = gl[g].image()
        img.alpha_composite(im, (hx, hy + (15 - im.height) // 2))
        hx += im.width + 4
        if lab:
            text(hx, hy + 4.5, lab, 5, INK)
            hx += len(lab) * 4 + 8
    # a line of dialogue
    dx, dy, dw = 232, MOCK_H - 56, 176
    put(ps["dialogue"], dx, dy, dw, 46)
    put(ps["dialogue_tab"], dx + 6, dy - 7, 36, 11)
    text(dx + 24, dy - 1.5, "Ilyra", 5.5, gold, True, "mm")
    text(dx + 10, dy + 12, "Stay behind me, Kara. The river", 5.4, INK, False)
    text(dx + 10, dy + 21, "is too fast here to wade.", 5.4, INK, False)
    return img, texts


def _mock(ps, bs, frames, fills, mk, ph, cu, to, nf, dc) -> None:
    """A battle screen built from this kit (plus the terrain and characters) to judge it as a whole, and the same
    screen animated: markers pulse, the cursor bobs, the ghost bar drains, a hit then a crit pop."""
    def draw(img: Image.Image, texts, k: int) -> Image.Image:
        out = scale(img, k)
        for x, y, s, size, fill, bold, anchor in texts:
            _label(out, k, s, (x, y), size, fill, bold, anchor)
        return out

    gif = []
    for f in range(MARK_FRAMES * 2):
        img, texts = _mock_frame(ps, bs, frames, fills, mk, ph, cu, to, nf, dc, f)
        if f == 0:
            save(draw(img, texts, 3).convert("RGB"), PREVIEWS / "15-ui-mock.png")
        gif.append(draw(img, texts, 2).convert("RGB").quantize(colors=255, method=Image.Quantize.MEDIANCUT))
    gif[0].save(PREVIEWS / "15-ui-anim.gif", save_all=True, append_images=gif[1:], duration=1000 // MARK_FPS,
                loop=0, optimize=True, disposal=1)
