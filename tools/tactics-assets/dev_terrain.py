"""Quick look at terrain textures, tiled 2x2 to show the seams: python3 dev_terrain.py [ids...]"""
import sys
from PIL import Image
from arcpix.preview import contact_sheet, Tile
from parts.terrain import CATALOGUE, ramps

ids = sys.argv[1:]
tiles = []
for t in CATALOGUE:
    if ids and t.id not in ids:
        continue
    fr = t.frames()[0]
    for vname, v in t.variants.items():
        img = fr.render(ramps(v))
        if t.kind in ("top", "side"):
            big = Image.new("RGBA", (img.width * 2, img.height * 2))
            for i in range(2):
                for j in range(2):
                    big.paste(img, (i * img.width, j * img.height))
            img = big
        tiles.append(Tile(img, f"{t.id}", vname))
img = contact_sheet("terrain dev", tiles, columns=4, zoom=2)
img.save("previews/dev.png"); print(img.size)
