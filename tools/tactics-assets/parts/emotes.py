"""Emotes: the little bubbles that pop up over a unit's head during exploration and dialogue. "!" when a unit
spots something, "?" when confused, "..." while a player is choosing on their phone, a crown over the session's
leader, a check mark when someone has voted, a die before a skill check.

Each emote pops in (small bubble, overshoot, settle) and then bobs; the manifest's loopStart says where the loop
begins. Icons are hand-authored 7-9 px bitmaps: at this size a drawn glyph reads better than anything generated.
"""
from __future__ import annotations

import shutil

from PIL import Image

from arcpix.canvas import Canvas, SlotTable
from arcpix.out import PACK, PREVIEWS, save, write_json
from arcpix.palette import FAMILIES, ramp
from arcpix.preview import Tile, contact_sheet, strip

W, H = 20, 20
ANCHOR = (8, 19)                 # the tail's tip: put it just above the unit's head
FRAMES = 4                       # pop, overshoot, settle, bob
SLOTS = SlotTable()
for _s in ("bubble", "icon", "accent"):
    SLOTS.id(_s)

BUBBLE = ramp("#f4ecd6", shift=0.04, spread=0.7)

# Bitmap keys: '#' icon base, '+' icon highlight, '-' icon shadow, '*' accent
GLYPHS: dict[str, list[str]] = {
    "alert": ["..##..", "..##..", "..##..", "..##..", "..#-..", "......", "..##..", "..--.."],
    "question": [".####.", "##..##", "....##", "...##.", "..##..", "......", "..##..", "..--.."],
    "heart": [".##.##.", "#+#####", "#######", ".#####.", "..###..", "...#..."],
    "anger": ["#.....#", "##...##", ".#...#.", ".......", ".#...#.", "##...##", "#.....#"],
    "sweat": ["...#...", "..###..", ".##+##.", ".#+###.", ".#####.", "..###.."],
    "note": ["....##.", "....#+#", "....#..", "....#..", "..###..", ".####..", "..##..."],
    "sleep": ["####...", "..#....", ".#.....", "####...", "....###", ".....#.", "....###"],
    "idea": ["..###..", ".#+###.", ".#+###.", ".#####.", "..###..", "..*.*..", "..***.."],
    "skull": [".#####.", "#######", "#--#--#", "#######", ".##-##.", ".#.#.#."],
    "coin": ["..###..", ".##+##.", ".#+#-#.", ".##-##.", "..###.."],
    "vote": ["......#", ".....##", "#...##.", "##.##..", ".###...", "..#...."],
    "crown": ["*..#..*", "#.###.#", "#######", "#+#+#+#", "#######"],
    "dice": ["#######", "#-...-#", "#.....#", "#..-..#", "#.....#", "#-...-#", "#######"],
    "sword": ["......+", ".....+#", "....+#.", "*..+#..", ".*+#...", "..*....", ".*.*..."],
}
COLOURS: dict[str, tuple[str, str]] = {      # icon ramp, accent ramp
    "alert": ("magic/blood", "metal/gold"), "question": ("cloth/steel_blue", "metal/gold"),
    "heart": ("magic/blood", "cloth/ivory"), "anger": ("magic/blood", "cloth/ivory"),
    "sweat": ("magic/frost", "cloth/ivory"), "note": ("magic/shadow", "metal/gold"),
    "sleep": ("cloth/navy", "cloth/navy"), "idea": ("magic/holy", "metal/iron"),
    "skull": ("metal/iron", "cloth/charcoal"), "coin": ("metal/gold", "metal/gold"),
    "vote": ("magic/nature", "metal/gold"), "crown": ("metal/gold", "magic/blood"),
    "dice": ("metal/gold", "cloth/charcoal"), "sword": ("metal/steel", "leather/brown"),
}
THOUGHT = {"sleep", "idea", "question"}       # thought bubbles (puffs) instead of speech bubbles
ELLIPSIS_DOTS = 3


def _ref(ref: str) -> list[str]:
    fam, name = ref.split("/")
    return FAMILIES[fam][name]


def _bubble(c: Canvas, cx: int, top: int, w: int, h: int, thought: bool) -> None:
    """A rounded bubble with a tail (speech) or trailing puffs (thought), lit from the top-left."""
    x0, x1 = cx - w // 2, cx - w // 2 + w - 1
    for y in range(top, top + h):
        inset = 2 if y in (top, top + h - 1) else (1 if y in (top + 1, top + h - 2) else 0)
        c.rect(x0 + inset, y, x1 - inset, y, "bubble", 4)
    c.rect(x0 + 2, top + 1, x1 - 3, top + 1, "bubble", 5)            # top highlight
    c.rect(x0 + 2, top + h - 2, x1 - 2, top + h - 2, "bubble", 3)    # underside
    if thought:
        c.ellipse(ANCHOR[0] - 2, top + h + 1.5, 1.5, 1.2, "bubble", 4)
        c.px(ANCHOR[0] - 3, top + h + 3, "bubble", 4)
    else:
        for i in range(3):                                           # tail down to the anchor
            c.rect(ANCHOR[0] - 1 + i // 2, top + h + i, ANCHOR[0] + 1 - i, top + h + i, "bubble", 4 if i < 2 else 3)


def _glyph(c: Canvas, name: str, cx: int, cy: int) -> None:
    rows = GLYPHS[name]
    gw, gh = len(rows[0]), len(rows)
    x0, y0 = cx - gw // 2, cy - gh // 2
    for dy, row in enumerate(rows):
        for dx, ch in enumerate(row):
            if ch == "#":
                c.px(x0 + dx, y0 + dy, "icon", 3)
            elif ch == "+":
                c.px(x0 + dx, y0 + dy, "icon", 5)
            elif ch == "-":
                c.px(x0 + dx, y0 + dy, "icon", 1)
            elif ch == "*":
                c.px(x0 + dx, y0 + dy, "accent", 4)


def frames(name: str) -> list[Canvas]:
    """pop (small, empty), overshoot (up a pixel), settle, bob (up a pixel). Ellipsis types its dots instead."""
    out = []
    thought = name in THOUGHT
    for f in range(FRAMES):
        c = Canvas(W, H, SLOTS)
        if f == 0:
            _bubble(c, W // 2, 9, 10, 7, thought)
        else:
            lift = 1 if f in (1, 3) else 0
            _bubble(c, W // 2, 3 - lift, 16, 13, thought)
            cy = 3 - lift + 6
            if name == "ellipsis":
                for k in range(min(f, ELLIPSIS_DOTS)):
                    c.rect(W // 2 - 4 + k * 3, cy, W // 2 - 3 + k * 3, cy + 1, "icon", 2)
            else:
                _glyph(c, name, W // 2, cy)
        c.outline()
        out.append(c)
    return out


NAMES = list(GLYPHS) + ["ellipsis"]


def ramps(name: str) -> dict[str, list[str]]:
    icon, accent = COLOURS.get(name, ("cloth/charcoal", "cloth/charcoal"))
    return {"bubble": BUBBLE, "icon": _ref(icon), "accent": _ref(accent)}


def build() -> None:
    out = PACK / "emotes"
    shutil.rmtree(out, ignore_errors=True)
    sheet = Image.new("RGBA", (W * FRAMES, H * len(NAMES)))
    entries, tiles = {}, []
    for row, name in enumerate(NAMES):
        imgs = [f.render(ramps(name)) for f in frames(name)]
        for k, im in enumerate(imgs):
            sheet.paste(im, (k * W, row * H))
        entries[name] = {"row": row, "thought": name in THOUGHT}
        tiles.append(Tile(strip(imgs, 1, gap=3), name, "thought" if name in THOUGHT else "speech"))
    save(sheet, out / "emotes.png")
    write_json({"frame": [W, H], "anchor": list(ANCHOR), "frames": FRAMES, "fps": 8, "loopStart": 2,
                "emotes": entries,
                "notes": {"anchor": "the tail tip; place it a pixel above the unit's head",
                          "loop": "play 0-3 once, then loop 2-3 while the emote is shown",
                          "ellipsis": "'...': a player is choosing on their phone"}}, out / "manifest.json")
    save(contact_sheet("Emotes", tiles, columns=5, zoom=4,
                       subtitle="Pop in, overshoot, settle, then bob. '...' types out while a player is choosing on "
                                "their phone; the crown marks the session's leader."), PREVIEWS / "23-emotes.png")
