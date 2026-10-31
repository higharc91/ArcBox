"""The second half of the UI kit (parts/ui.py builds it into the ui set): the pieces a 4K TV and a controller need on
top of the battle HUD. Panel shimmer and edge glints, toasts, a tooltip with a header band, the reaction prompt and its
countdown ring, the objective tracker, the minimap frame, bar animations (damage-ghost drain, heal sweep), and
keyboard and gamepad button glyphs.

Same rules as ui.py: drawn at 1x in (slot, shade), baked per palette, 9-slice numbers in the manifest. Animated pieces
are horizontal frame strips. Nothing here replaces a ui.py piece; the battle HUD keeps its keys.
"""
from __future__ import annotations

import math

import numpy as np
from PIL import Image

from arcpix.canvas import Canvas, FLAG_OUTLINE
from arcpix.palette import ramp
from parts.ui import (GOLD_EDGE, RAMPS, SLOTS, Atlas, Piece, Ring, _depth_rect, _dilate, _halo, _paint, _panel,
                      _sheet, _stud)

for _s in ("accent", "key", "mark"):
    SLOTS.id(_s)

RAMPS.update({
    "pad_a": ramp("#5cc24a", shift=0.06, spread=0.9),
    "pad_b": ramp("#e2534c", shift=0.06, spread=0.9),
    "pad_x": ramp("#3e8ef0", shift=0.05, spread=0.9),
    "pad_y": ramp("#f0c23a", shift=0.06, spread=0.9),
    "pad_body": ramp("#3a4058", shift=0.04, spread=0.9),
    "ps_cross": ramp("#7fb0ff", shift=0.05, spread=0.85),
    "ps_circle": ramp("#ff6f7a", shift=0.06, spread=0.85),
    "ps_square": ramp("#f08ad0", shift=0.06, spread=0.85),
    "ps_triangle": ramp("#4fd8b0", shift=0.06, spread=0.85),
    "keycap": ramp("#d8d0bc", shift=0.05, spread=0.8),
    "key_ink": ramp("#2a2230", shift=0.03),
    "urgent": ramp("#ff6a3d", shift=0.07, spread=0.9),
})

# A 3x5 pixel font for labels baked into glyphs (key caps, A/B/X/Y). Variable width: M, N and W need more room.
FONT3: dict[str, list[str]] = {
    "A": [".#.", "#.#", "###", "#.#", "#.#"], "B": ["##.", "#.#", "##.", "#.#", "##."],
    "C": [".##", "#..", "#..", "#..", ".##"], "D": ["##.", "#.#", "#.#", "#.#", "##."],
    "E": ["###", "#..", "##.", "#..", "###"], "F": ["###", "#..", "##.", "#..", "#.."],
    "G": [".##", "#..", "#.#", "#.#", ".##"], "H": ["#.#", "#.#", "###", "#.#", "#.#"],
    "I": ["###", ".#.", ".#.", ".#.", "###"], "J": ["..#", "..#", "..#", "#.#", ".#."],
    "K": ["#.#", "#.#", "##.", "#.#", "#.#"], "L": ["#..", "#..", "#..", "#..", "###"],
    "M": ["#...#", "##.##", "#.#.#", "#...#", "#...#"], "N": ["#..#", "##.#", "#.##", "#..#", "#..#"],
    "O": [".#.", "#.#", "#.#", "#.#", ".#."], "P": ["##.", "#.#", "##.", "#..", "#.."],
    "Q": [".#.", "#.#", "#.#", "##.", ".##"], "R": ["##.", "#.#", "##.", "#.#", "#.#"],
    "S": [".##", "#..", ".#.", "..#", "##."], "T": ["###", ".#.", ".#.", ".#.", ".#."],
    "U": ["#.#", "#.#", "#.#", "#.#", "###"], "V": ["#.#", "#.#", "#.#", "#.#", ".#."],
    "W": ["#...#", "#...#", "#.#.#", "#.#.#", ".#.#."], "X": ["#.#", "#.#", ".#.", "#.#", "#.#"],
    "Y": ["#.#", "#.#", ".#.", ".#.", ".#."], "Z": ["###", "..#", ".#.", "#..", "###"],
    "0": ["###", "#.#", "#.#", "#.#", "###"], "1": [".#.", "##.", ".#.", ".#.", "###"],
    "2": ["##.", "..#", ".#.", "#..", "###"], "3": ["##.", "..#", ".#.", "..#", "##."],
    "4": ["#.#", "#.#", "###", "..#", "..#"], "5": ["###", "#..", "##.", "..#", "##."],
    "6": [".##", "#..", "###", "#.#", "###"], "7": ["###", "..#", ".#.", ".#.", ".#."],
    "8": ["###", "#.#", "###", "#.#", "###"], "9": ["###", "#.#", "###", "..#", "##."],
    "^": [".#.", "###", ".#.", ".#.", ".#."], "v": [".#.", ".#.", ".#.", "###", ".#."],
    "<": [".....", ".#...", "#####", ".#...", "....."], ">": [".....", "...#.", "#####", "...#.", "....."],
    "!": [".#.", ".#.", ".#.", "...", ".#."], " ": ["..", "..", "..", "..", ".."],
}


def text3(c: Canvas, x: int, y: int, word: str, slot: str, shade: int) -> int:
    for ch in word:
        g = FONT3[ch]
        for dy, row in enumerate(g):
            for dx, v in enumerate(row):
                if v == "#":
                    c.px(x + dx, y + dy, slot, shade)
        x += len(g[0]) + 1
    return x - 1


def width3(word: str) -> int:
    return sum(len(FONT3[ch][0]) + 1 for ch in word) - 1


def _rim(c: Canvas, slot: str) -> None:
    """Selective outline for pieces drawn after outline() ran (studs, chips that overhang the box)."""
    m = c.mask(slot) & (c.flags == 0)
    ring = _dilate(m) & (c.slot == 0)
    c.slot[ring], c.shade[ring], c.flags[ring] = SLOTS.id(slot), 0, FLAG_OUTLINE


GLASS = {"edge": "gold", "stud": "gold", "glass": "glass", "line": "glass"}


# ----------------------------------------------------------------------------------------------- shimmer + glint
def shimmer(main: Piece, frames: int = 8) -> list[Piece]:
    """The main panel with a glint running over its gold. The gleam stays inside the corner cells of the 9-slice
    (the parts that never stretch), so it reads the same on any panel size: it sweeps the top-left corner and its
    stud, then flares the top-right stud, then rests. Play it every few seconds, not continuously."""
    c0 = main.canvas
    l, t, r, b = main.slice
    h, w = c0.slot.shape
    ys, xs = np.mgrid[0:h, 0:w]
    corner_l = (xs < l) & (ys < t)
    corner_r = (xs >= w - r) & (ys < t)
    gold = np.isin(c0.slot, [SLOTS.id("edge"), SLOTS.id("stud")]) & (c0.flags == 0)
    out = []
    for f in range(frames):
        c = c0.copy()
        sh = c.shade.astype(np.int16)
        if f < 5:                                         # a diagonal band sweeping the top-left corner
            pos = -2 + f * (l + t) / 4
            band = np.abs((xs + ys) - pos)
            sh[gold & corner_l & (band < 1.5)] += 2
            sh[gold & corner_l & (band >= 1.5) & (band < 3)] += 1
        if 3 <= f < 7:                                    # then the far stud flares and settles
            k = {3: 1, 4: 2, 5: 2, 6: 1}[f]
            sh[gold & corner_r] += k if f in (4, 5) else 1
        c.shade = np.clip(sh, 0, 5).astype(np.uint8)
        out.append(Piece(c, main.pal, slice=main.slice, outset=main.outset, padding=main.padding,
                         alpha=main.alpha))
    return out


def glint(horizontal: bool) -> Piece:
    """A short gold highlight the game slides along a long panel edge (additive): bright centre, tapering ends."""
    n = 13
    c = Canvas(n if horizontal else 3, 3 if horizontal else n, SLOTS)
    for k in range(n):
        d = abs(k - n // 2)
        sh = 5 if d == 0 else 4 if d < 3 else 3 if d < 5 else 2
        x, y = (k, 1) if horizontal else (1, k)
        c.px(x, y, "glow", sh)
        if d < 2:
            c.px(x + (0 if horizontal else -1), y + (-1 if horizontal else 0), "glow", 3)
            c.px(x + (0 if horizontal else 1), y + (1 if horizontal else 0), "glow", 3)
    return Piece(c, {"glow": "gold"}, extra={"blend": "add", "use": "slide along a gold edge, centre on the edge's "
                                                                  "middle row, every few seconds"})


# ------------------------------------------------------------------------------------------------------- panels
def tooltip_header() -> Piece:
    """A tooltip with a lit header band for the name (ability, item, status), a gold rule under it, and the body
    below in darker glass. The header's height is fixed by the slice; the body stretches."""
    rings: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0)]
    c, m = _panel(30, 26, rings=rings, r=1, stud_r=2, top=(2,), mid=1, bottom=(), extra_pad=2)
    o = m["outset"][1]
    band0, band1 = o + 3, o + 11                          # header rows (inside the rings)
    for y in range(band0, band1):
        for x in range(c.w):
            if c.slot[y, x] == SLOTS.id("glass") and c.flags[y, x] == 0:
                c.shade[y, x] = 4 if y == band0 else 3
    for x in range(c.w):
        if c.slot[band1, x] == SLOTS.id("glass") and c.flags[band1, x] == 0:
            c.px(x, band1, "edge", 3)
        if c.slot[band1 + 1, x] == SLOTS.id("glass") and c.flags[band1 + 1, x] == 0:
            c.px(x, band1 + 1, "line", 0)
    sl = (m["slice"][0], band1 + 3, m["slice"][2], m["slice"][3])
    return Piece(c, GLASS, alpha={"glass": 0.95}, slice=sl, outset=m["outset"],
                 padding=(m["padding"][0], band1 + 3 - o, m["padding"][2], m["padding"][3]),
                 extra={"header": [3, band1 - o - 3], "headerNote": "the header text sits in rows header[0].."
                                                                    "header[0]+header[1] of the box; body below"})


TOASTS = {"info": "magic", "success": "hp", "warning": "gold", "danger": "enemy"}


def toast(kind: str) -> Piece:
    """A notification: a compact dark panel with an accent bar in the message's colour down the left and a matching
    gem, so a glance says good news from bad before the text is read."""
    rings: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0)]
    c, m = _panel(30, 16, rings=rings, r=1, studs="none", top=(2,), mid=1, bottom=(), extra_pad=8)
    for y in range(3, c.h - 3):
        for x in (2, 3, 4):
            c.px(x, y, "accent", 4 if x == 2 else 3 if x == 3 else 2)
    c.px(2, 3, "accent", 5)
    cy = c.h // 2
    for dy in range(-2, 3):
        for dx in range(-2 + abs(dy), 3 - abs(dy)):
            c.px(9 + dx, cy + dy, "accent", 5 if (dx < 0 and dy < 0) else 4 if dx <= 0 and dy <= 0 else 2)
    sl = (13, 4, 4, 4)
    return Piece(c, {**GLASS, "accent": TOASTS[kind]}, alpha={"glass": 0.94}, slice=sl, padding=(14, 3, 4, 3),
                 extra={"text": "ink", "enter": "slide in from the right edge over 0.2 s, hold ~3 s, fade 0.3 s"})


def objective() -> Piece:
    """The objective tracker: a quiet strip (no studs, so it never competes with the main panels) with a gold rule
    down its left edge and a lit title row."""
    rings: list[Ring] = [("edge", 3, 3, 1), ("line", 0, 0, 0)]
    c, m = _panel(26, 24, rings=rings, r=1, studs="none", top=(3, 3), mid=1, bottom=(), extra_pad=4)
    for y in range(2, c.h - 2):
        c.px(2, y, "edge", 5 if y < 6 else 4)
        c.px(3, y, "edge", 3)
    for y in range(3, 10):
        for x in range(4, c.w - 3):
            if c.slot[y, x] == SLOTS.id("glass"):
                c.shade[y, x] = 3 if y > 3 else 4
    return Piece(c, GLASS, alpha={"glass": 0.9}, slice=(7, 11, 4, 4), padding=(7, 3, 4, 3),
                 extra={"title": "the first 8 px of the box is the title row (OBJECTIVE / round counter)"})


def check(state: str) -> Piece:
    """An objective's box: open (to do), done (gold tick) or failed (red cross)."""
    c = Canvas(11, 11, SLOTS)
    _paint(c, 1, 1, _depth_rect(9, 9, 1), [("edge", 4, 3, 2), ("glass", 0, 0, 1)], "glass", mid=1)
    if state == "done":
        for (x, y) in ((3, 5), (4, 6), (5, 7), (6, 6), (7, 5), (8, 4), (9, 3)):
            c.px(x, y, "mark", 5)
            c.px(x, y + 1, "mark", 3)
    elif state == "failed":
        for k in range(5):
            c.px(3 + k, 3 + k, "mark", 4)
            c.px(7 - k, 3 + k, "mark", 4)
    c.outline()
    return Piece(c, {"edge": "gold" if state != "failed" else "red", "glass": "glass",
                     "mark": "gold" if state == "done" else "enemy"})


def reaction_panel() -> Piece:
    """The reaction prompt ("Kara can block: SHIELD?"): the dialogue's double frame, but its inner rule glows in an
    urgent ember colour and all four studs are set, so it can't be mistaken for chatter."""
    rings: list[Ring] = [("edge", 4, 3, 2), ("line", 0, 0, 0), ("accent", 4, 3, 2), ("glass", 3, 3, 3),
                         ("glass", 4, None, None)]
    c, m = _panel(40, 32, rings=rings, studs="all", top=(3, 3), mid=2, bottom=(1,))
    return Piece(c, {**GLASS, "accent": "urgent"}, alpha={"glass": 0.95}, **m,
                 extra={"ring": "the countdown ring sits in the left padding, vertically centred"})


RING_FRAMES = 16


def countdown(f: int) -> Piece:
    """A 25 px ring that empties clockwise from twelve o'clock over RING_FRAMES frames (the game picks the frame
    from time left). Gold while there's time, ember in the last quarter, with a bright head on the moving edge."""
    n = 25
    c = Canvas(n, n, SLOTS)
    cx = cy = n / 2
    left = 1 - f / RING_FRAMES
    late = left <= 0.25
    for y in range(n):
        for x in range(n):
            d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
            if not 8.0 < d <= 11.5:
                continue
            a = (math.degrees(math.atan2(x + 0.5 - cx, -(y + 0.5 - cy))) % 360) / 360
            lit = (x + 0.5 - cx) + (y + 0.5 - cy) < 0
            if a <= left:
                head = left - a < 0.035
                sh = 5 if head else (4 if lit else 3) if d > 9.5 else (3 if lit else 2)
                c.px(x, y, "accent" if late else "edge", sh)
            else:
                c.px(x, y, "line", 2 if lit else 1)                 # the spent track stays visible, dim steel
    for y in range(n):
        for x in range(n):
            if math.hypot(x + 0.5 - cx, y + 0.5 - cy) <= 8.0:
                c.px(x, y, "glass", 3 if (x + y) < n - 6 else 2)
    c.outline()
    return Piece(c, {"edge": "gold", "accent": "urgent", "glass": "glass", "line": "steel"},
                 alpha={"glass": 0.9})


def minimap() -> Piece:
    """The minimap's frame: a heavy bevelled gold rim with four studs; the window inside is left empty for the
    game's own map render. The compass tab is a separate piece, so stretching the top edge never repeats it."""
    w = h = 40
    rings: list[Ring] = [("edge", 5, 4, 2), ("edge", 3, 3, 1), ("line", 0, 0, 0)]
    o = 4
    c = Canvas(w + 2 * o, h + 2 * o, SLOTS)
    d = _depth_rect(w, h, 3)
    d[d >= len(rings)] = -1
    _paint(c, o, o, d, rings, "edge")
    c.outline()
    for (x, y) in ((o, o), (o + w - 1, o), (o, o + h - 1), (o + w - 1, o + h - 1)):
        _stud(c, x, y, 3)
    _rim(c, "stud")
    inset = len(rings) + 2
    return Piece(c, {"edge": "gold", "stud": "gold", "glass": "glass", "line": "glass"},
                 slice=(o + inset, o + inset, o + inset, o + inset), outset=(o, o, o, o),
                 extra={"window": "the box minus 3 px all round is transparent: draw the map first, then the frame",
                        "compass": "draw minimap_compass centred on the box's top edge, its tick pointing in"})


def compass() -> Piece:
    """The minimap's N tab: a gold plaque with a tick pointing into the map."""
    c = Canvas(13, 14, SLOTS)
    _paint(c, 1, 1, _depth_rect(11, 9, 2), [("edge", 5, 4, 2)], "glass", mid=1, top=(2,))
    text3(c, 4, 3, "N", "text", 5)
    for k in range(3):
        c.px(6, 10 + k, "edge", 4 - k)
    c.outline()
    return Piece(c, {"edge": "gold", "glass": "glass", "text": "ink"},
                 extra={"attach": "centre x on the minimap box, y = box top - 6"})


def map_pip(kind: str) -> Piece:
    """Minimap markers, 5x5: a diamond for you, dots for allies and enemies, a star for the objective."""
    c = Canvas(7, 7, SLOTS)
    if kind == "objective":
        for (x, y) in ((3, 1), (2, 2), (3, 2), (4, 2), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (2, 4), (4, 4), (2, 5),
                       (4, 5)):
            c.px(x, y, "fill", 4 if y < 3 else 3)
    elif kind == "you":
        for dy in range(-2, 3):
            for dx in range(-2 + abs(dy), 3 - abs(dy)):
                c.px(3 + dx, 3 + dy, "fill", 5 if dx + dy < 0 else 3)
    else:
        for (x, y) in ((2, 1), (3, 1), (4, 1), (1, 2), (2, 2), (3, 2), (4, 2), (5, 2), (1, 3), (2, 3), (3, 3), (4, 3),
                       (5, 3), (1, 4), (2, 4), (3, 4), (4, 4), (5, 4), (2, 5), (3, 5), (4, 5)):
            c.px(x, y, "fill", 5 if (x, y) == (2, 2) else 4 if x + y < 6 else 3)
    c.outline()
    return Piece(c, {"fill": {"you": "gold", "ally": "hp", "enemy": "enemy", "objective": "gold"}[kind]})


# ------------------------------------------------------------------------------------------------- bar animations
SWEEP_FRAMES = 6


def heal_sweep(f: int, h: int = 5) -> Piece:
    """The heal sweep: a bright slanted band, 14 px wide, that travels left to right across the newly healed span
    (draw additively over the fill, clipped to the healed part; stretch the travel to that span's width)."""
    w = 14
    c = Canvas(w, h, SLOTS)
    pos = -4 + f * (w + 6) / (SWEEP_FRAMES - 1)
    for y in range(h):
        for x in range(w):
            d = abs(x + (h - 1 - y) * 0.6 - pos)
            if d < 1.0:
                c.px(x, y, "glow", 5)
            elif d < 2.5:
                c.px(x, y, "glow", 4)
            elif d < 4:
                c.px(x, y, "glow", 3)
    return Piece(c, {"glow": "ghost"}, alpha={"glow": [0, 0, 0, 0.45, 0.7, 0.95]})


DRAIN_FRAMES = 5


def ghost_drain(f: int, h: int = 5) -> Piece:
    """The damage ghost as it drains: pale, then blushing towards the bar's red, then fading, one 4 px tile per
    frame. The game shrinks the ghost's width over the same frames (see compose)."""
    c = Canvas(4, h, SLOTS)
    for y in range(h):
        for x in range(4):
            rows = (5, 4, 4, 3, 3) if f < 2 else (4, 4, 3, 3, 2)
            c.px(x, y, "ghost" if f < 2 else "fill", rows[min(y, 4)])
    a = (1.0, 1.0, 0.85, 0.6, 0.35)[f]
    return Piece(c, {"ghost": "ghost", "fill": "rim_damage" if f >= 2 else "ghost"},
                 alpha={"ghost": a, "fill": a})


# ---------------------------------------------------------------------------------------------- input glyphs
def _disc(c: Canvas, cx: float, cy: float, r: float, slot: str) -> None:
    for y in range(c.h):
        for x in range(c.w):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = math.hypot(dx, dy)
            if d <= r:
                lit = (dx + dy) / max(d, 0.01)
                sh = 5 if d < r * 0.4 and dx < 0 and dy < 0 else 4 if lit < -0.6 else 2 if lit > 0.75 else 3
                if d > r - 1.0:
                    sh = 4 if lit < -0.2 else 1
                c.px(x, y, slot, sh)


def face_button(letter: str, ramp_name: str) -> Piece:
    """An A/B/X/Y face button: a dark disc with its letter in the button's own colour (the colours players know)."""
    c = Canvas(15, 15, SLOTS)
    _disc(c, 7.5, 7.5, 7.0, "key")
    for y in range(c.h):
        for x in range(c.w):
            if c.slot[y, x] and math.hypot(x + 0.5 - 7.5, y + 0.5 - 7.5) < 5.6:
                c.shade[y, x] = 1 if (x + y) > 12 else 2
    text3(c, 7 - width3(letter) // 2, 5, letter, "text", 5)
    c.outline()
    return Piece(c, {"key": "pad_body", "text": ramp_name})


def ps_button(shape: str) -> Piece:
    c = Canvas(15, 15, SLOTS)
    _disc(c, 7.5, 7.5, 7.0, "key")
    for y in range(c.h):
        for x in range(c.w):
            if c.slot[y, x] and math.hypot(x + 0.5 - 7.5, y + 0.5 - 7.5) < 5.6:
                c.shade[y, x] = 1 if (x + y) > 12 else 2
    if shape == "cross":
        for k in range(5):
            c.px(5 + k, 5 + k, "text", 4)
            c.px(9 - k, 5 + k, "text", 4)
    elif shape == "circle":
        for y in range(15):
            for x in range(15):
                if 1.6 < math.hypot(x + 0.5 - 7.5, y + 0.5 - 7.5) <= 3.0:
                    c.px(x, y, "text", 4)
    elif shape == "square":
        for k in range(5):
            for (x, y) in ((5 + k, 5), (5 + k, 9), (5, 5 + k), (9, 5 + k)):
                c.px(x, y, "text", 4)
    else:
        for (x, y) in ((7, 4), (6, 5), (8, 5), (6, 6), (8, 6), (5, 7), (9, 7), (5, 8), (9, 8), (4, 9), (5, 9), (6, 9),
                       (7, 9), (8, 9), (9, 9), (10, 9)):
            c.px(x, y, "text", 4)
    c.outline()
    return Piece(c, {"key": "pad_body", "text": f"ps_{shape}"})


def shoulder(label: str, trigger: bool) -> Piece:
    """LB/RB bumpers (a wide pill) and LT/RT triggers (a taller cap, rounded on top)."""
    w, h = (17, 9) if not trigger else (15, 12)
    c = Canvas(w + 2, h + 2, SLOTS)
    _paint(c, 1, 1, _depth_rect(w, h, 3 if trigger else 2), [("key", 4, 3, 1)], "key", mid=2, top=(3,), bottom=(1,))
    if trigger:
        for y in range(1, 4):
            for x in range(1, w + 1):
                if c.slot[y, x] and (x < 4 - y + 2 or x > w - 3 + y - 2):
                    c.clear(x, y)
    tw = width3(label)
    text3(c, 1 + (w - tw) // 2, 1 + (h - 5) // 2 + (1 if trigger else 0), label, "text", 5)
    c.outline()
    return Piece(c, {"key": "pad_body", "text": "ink"})


def dpad(lit: str | None) -> Piece:
    """The d-pad, with one arm lit (or none): up/down/left/right in hints like 'move cursor'."""
    c = Canvas(17, 17, SLOTS)
    arms = {"up": (6, 1, 10, 6), "down": (6, 10, 10, 15), "left": (1, 6, 6, 10), "right": (10, 6, 15, 10)}
    for name, (x0, y0, x1, y1) in arms.items():
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                c.px(x, y, "fill" if name == lit else "key", 3)
    for y in range(6, 11):
        for x in range(6, 11):
            c.px(x, y, "key", 2)
    for slot in ("key", "fill"):
        m = c.mask(slot)
        up = np.zeros_like(m)
        up[1:] = m[:-1]
        left = np.zeros_like(m)
        left[:, 1:] = m[:, :-1]
        c.shade[m & (~up | ~left)] = 4
        dn = np.zeros_like(m)
        dn[:-1] = m[1:]
        rt = np.zeros_like(m)
        rt[:, :-1] = m[:, 1:]
        c.shade[m & (~dn | ~rt) & (up & left)] = 1
    c.outline()
    return Piece(c, {"key": "pad_body", "fill": "gold"})


def stick(label: str) -> Piece:
    c = Canvas(15, 15, SLOTS)
    _disc(c, 7.5, 7.5, 7.0, "key")
    for y in range(15):
        for x in range(15):
            d = math.hypot(x + 0.5 - 7.5, y + 0.5 - 7.5)
            if 4.0 < d <= 5.0:
                c.px(x, y, "key", 1 if (x + y) > 14 else 4)
    text3(c, 7 - width3(label) // 2, 5, label, "text", 5)
    c.outline()
    return Piece(c, {"key": "pad_body", "text": "ink"})


def menu_button(kind: str) -> Piece:
    """Start ('menu', three lines) and select ('view', two overlapping windows)."""
    c = Canvas(13, 11, SLOTS)
    _paint(c, 1, 1, _depth_rect(11, 9, 3), [("key", 4, 3, 1)], "key", mid=2)
    if kind == "menu":
        for y in (3, 5, 7):
            for x in range(4, 9):
                c.px(x, y, "text", 5)
    else:
        for (x0, y0) in ((3, 3), (5, 5)):
            for k in range(4):
                for (x, y) in ((x0 + k, y0), (x0 + k, y0 + 3), (x0, y0 + k), (x0 + 4, y0 + k)):
                    c.px(x, y, "text", 5)
    c.outline()
    return Piece(c, {"key": "pad_body", "text": "ink"})


def keycap(label: str = "") -> Piece:
    """A keyboard key: an ivory cap with a lit top face and a dark front lip, the legend in near-black. Blank caps
    9-slice so the game can set its own legend; labelled caps size to their text."""
    tw = width3(label) if label else 3
    w, h = max(11, tw + 6), 12
    c = Canvas(w + 2, h + 2, SLOTS)
    _paint(c, 1, 1, _depth_rect(w, h, 2), [("key", 5, 4, 2)], "key", mid=4, top=(5,), bottom=(2, 2))
    if label:
        text3(c, 1 + (w - tw) // 2, 3, label, "text", 1)
    c.outline()
    return Piece(c, {"key": "keycap", "text": "key_ink"}, slice=(4, 4, 4, 5) if not label else None,
                 padding=(3, 2, 3, 4) if not label else None)


def mouse(button: str) -> Piece:
    c = Canvas(13, 17, SLOTS)
    for y in range(1, 16):
        for x in range(1, 12):
            dx, dy = (x + 0.5 - 6.5) / 5.5, (y + 0.5 - 8.5) / 7.5
            if dx * dx + dy * dy <= 1.0:
                c.px(x, y, "key", 4 if dx + dy < -0.5 else 2 if dx + dy > 0.6 else 3)
    for y in range(1, 8):
        for x in range(1, 12):
            if c.slot[y, x] and ((button == "left" and x < 6) or (button == "right" and x > 6)):
                c.px(x, y, "fill", 4 if y < 4 else 3)
    for y in range(1, 8):
        c.px(6, y, "key", 1)
    for x in range(1, 12):
        if c.slot[7, x]:
            c.px(x, 7, "key", 1)
    c.outline()
    return Piece(c, {"key": "keycap", "fill": "gold"})


KEYS = [*"ABCDEFGHIJKLMNOPQRSTUVWXYZ", *"0123456789", "ESC", "TAB", "SHIFT", "CTRL", "ALT", "ENTER", "SPACE", "^",
        "v", "<", ">"]
_KEY_IDS = {"^": "up", "v": "down", "<": "left", ">": "right"}


def glyphs() -> dict[str, Piece]:
    out: dict[str, Piece] = {}
    for letter, rp in (("A", "pad_a"), ("B", "pad_b"), ("X", "pad_x"), ("Y", "pad_y")):
        out[f"pad_{letter.lower()}"] = face_button(letter, rp)
    for shape in ("cross", "circle", "square", "triangle"):
        out[f"ps_{shape}"] = ps_button(shape)
    for lab in ("LB", "RB"):
        out[f"pad_{lab.lower()}"] = shoulder(lab, False)
    for lab in ("LT", "RT"):
        out[f"pad_{lab.lower()}"] = shoulder(lab, True)
    out["pad_dpad"] = dpad(None)
    for d in ("up", "down", "left", "right"):
        out[f"pad_dpad_{d}"] = dpad(d)
    out["pad_ls"], out["pad_rs"] = stick("L"), stick("R")
    out["pad_menu"], out["pad_view"] = menu_button("menu"), menu_button("view")
    out["key_blank"] = keycap()
    for k in KEYS:
        out[f"key_{_KEY_IDS.get(k, k.lower())}"] = keycap(k)
    out["mouse_left"], out["mouse_right"] = mouse("left"), mouse("right")
    return out


# ------------------------------------------------------------------------------------------------------- build
def hud_pieces(panels: dict[str, Piece]) -> dict[str, Piece]:
    out = {"tooltip_header": tooltip_header(), **{f"toast_{k}": toast(k) for k in TOASTS},
           "objective": objective(), **{f"check_{s}": check(s) for s in ("open", "done", "failed")},
           "reaction": reaction_panel(), "minimap": minimap(), "minimap_compass": compass(),
           **{f"pip_{k}": map_pip(k) for k in ("you", "ally", "enemy", "objective")},
           "glint_h": glint(True), "glint_v": glint(False)}
    return out


def strips(panels: dict[str, Piece]) -> dict[str, tuple[list[Piece], dict]]:
    """Animated pieces: frames, plus the manifest's playback data."""
    return {
        "panel_shimmer": (shimmer(panels["main"]), {"fps": 12, "loop": False, "every": "4-6 s",
                                                    "note": "frames of the 'main' panel source with the same slice: "
                                                            "swap it in for the main panel while it plays"}),
        "countdown_ring": ([countdown(f) for f in range(RING_FRAMES + 1)],
                           {"fps": 0, "loop": False,
                            "note": f"frame = round({RING_FRAMES} * elapsed / limit); the last frame is the "
                                    "empty ring"}),
        "heal_sweep": ([heal_sweep(f) for f in range(SWEEP_FRAMES)], {"fps": 15, "loop": False, "blend": "add"}),
        "ghost_drain": ([ghost_drain(f) for f in range(DRAIN_FRAMES)],
                        {"fps": 10, "loop": False,
                         "note": "tile frame f as the ghost fill while its width eases to the new value"}),
    }


def pack(pieces: dict[str, Piece], width: int = 256) -> tuple[Image.Image, dict[str, list[int]]]:
    atlas = Atlas(width)
    for k, p in pieces.items():
        atlas.add(k, p.image())
    return atlas.pack()


def strip_image(frames: list[Piece]) -> Image.Image:
    return _sheet([p.image() for p in frames])


__all__ = ["hud_pieces", "strips", "glyphs", "pack", "strip_image", "text3", "width3", "_halo"]
