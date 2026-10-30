"""Zoom on race/class combos: python3 dev_combo.py race:class,... frames"""
import sys
from arcpix.preview import contact_sheet, Tile
from parts.creator import make_character
from parts.characters import recipe_sheet
picks = [int(x) for x in sys.argv[2].split(",")]
tiles = []
for combo in sys.argv[1].split(","):
    r, c = combo.split(":")
    s = recipe_sheet(make_character("X", r, c))
    for k in picks:
        tiles.append(Tile(s.frames[k], f"{r} {c} #{k}"))
img = contact_sheet("combos", tiles, columns=len(picks), zoom=7)
img.save("previews/zoom.png"); print(img.size)
