"""Quick look at recipes while developing: python3 dev_preview.py [ids...]"""
import sys
from PIL import Image
from arcpix.preview import contact_sheet, Tile, strip
from parts.recipes import ALL
from parts.characters import recipe_sheet, frames_per_direction

ids = sys.argv[1:] or [r.id for r in ALL[:6]]
tiles = []
n = frames_per_direction()
for r in [r for r in ALL if r.id in ids]:
    s = recipe_sheet(r)
    tiles.append(Tile(strip(s.frames[:n], 1, gap=4), r.name, "front: idle×2 walk×4 attack×3 cast×3 hurt down"))
    tiles.append(Tile(strip(s.frames[n:], 1, gap=4), "", "back"))
img = contact_sheet("dev preview", tiles, columns=2, zoom=3)
img.save("previews/dev.png"); print(img.size)
