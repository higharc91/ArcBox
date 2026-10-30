"""Zoom on props: python3 dev_props.py id[:variant] ..."""
import sys
from arcpix.preview import contact_sheet, Tile
from parts.props import CATALOGUE, ramps
tiles = []
for arg in sys.argv[1:]:
    pid, _, var = arg.partition(":")
    p = next(x for x in CATALOGUE if x.id == pid)
    v = p.variants[var] if var else next(iter(p.variants.values()))
    tiles.append(Tile(p.frames()[0].render(ramps(v)), arg))
contact_sheet("props", tiles, columns=len(tiles), zoom=6, checker=True).save("previews/zoom.png")
