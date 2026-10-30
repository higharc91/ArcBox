"""Close crops of terrain at zoom 5: python3 dev_crop.py id[:variant] ..."""
import sys
from arcpix.preview import contact_sheet, Tile
from parts.terrain import CATALOGUE, ramps
tiles = []
for arg in sys.argv[1:]:
    tid, _, var = arg.partition(":")
    t = next(x for x in CATALOGUE if x.id == tid)
    v = t.variants[var] if var else next(iter(t.variants.values()))
    img = t.frames()[0].render(ramps(v))
    tiles.append(Tile(img.crop((0, 0, min(64, img.width), min(64, img.height))), arg))
img = contact_sheet("crop", tiles, columns=4, zoom=5)
img.save("previews/zoom.png"); print(img.size)
