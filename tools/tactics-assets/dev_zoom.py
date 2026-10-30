import sys
from arcpix.preview import contact_sheet, Tile
from parts.recipes import ALL
from parts.characters import recipe_sheet, frames_per_direction
ids = sys.argv[1].split(","); picks = [int(x) for x in sys.argv[2].split(",")]
n = frames_per_direction(); tiles = []
for r in [r for r in ALL if r.id in ids]:
    s = recipe_sheet(r)
    for k in picks:
        tiles.append(Tile(s.frames[k], f"{r.id} #{k}"))
img = contact_sheet("zoom", tiles, columns=len(picks), zoom=8)
img.save("previews/zoom.png"); print(img.size)
