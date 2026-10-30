"""Quick looks while drawing the dungeon set: python3 dev_dungeon.py tex|props|scene NAME [out.png].

Writes to the scratch path given (default /tmp/dungeon-dev.png), never into previews/."""
import sys

import numpy as np
from PIL import Image

from arcpix.preview import Tile, contact_sheet
from parts import dungeon as D

what = sys.argv[1] if len(sys.argv) > 1 else "tex"
out = sys.argv[3] if len(sys.argv) > 3 else (sys.argv[2] if len(sys.argv) > 2 and sys.argv[2].endswith(".png")
                                             else "/tmp/dungeon-dev.png")
arg = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].endswith(".png") else ""

if what == "tex":
    tiles = []
    for t in D.TEXTURES:
        if arg and arg not in (t.kind, t.id):
            continue
        for v in t.variants:
            im = Image.fromarray(D.tex_rgba(t.id, v).astype(np.uint8), "RGBA")
            if t.kind == "top":
                big = Image.new("RGBA", (im.width * 2, im.height * 2))
                for k in range(4):
                    big.paste(im, ((k % 2) * im.width, (k // 2) * im.height))
                im = big
            elif t.kind in ("side", "lip"):
                big = Image.new("RGBA", (im.width * 2, im.height * 2))
                for k in range(4):
                    big.paste(im, ((k % 2) * im.width, (k // 2) * im.height))
                im = big
            tiles.append(Tile(im, t.id, v))
    zoom = 2 if arg in ("top", "side", "") else 4
    contact_sheet("dungeon textures", tiles, columns=4 if zoom == 2 else 8, zoom=zoom, checker=True).save(out)
elif what == "props":
    tiles = []
    for p in D.PROPS:
        if arg and arg not in (p.id, *p.tags):
            continue
        fr = p.frames()
        for v in p.variants:
            pal = D.ramps(p.variants[v], D.PS)
            imgs = [f.render(pal) for f in fr]
            from arcpix.preview import strip
            tiles.append(Tile(strip(imgs, 1, gap=2) if len(imgs) > 1 else imgs[0], p.id, v))
    contact_sheet("dungeon props", tiles, columns=6, zoom=4, checker=True).save(out)
elif what == "scene":
    D.render_scene(arg or "crypt", 0).save(out)
