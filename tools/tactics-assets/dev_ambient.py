"""Quick look while drawing ambient animations: renders one asset (or a few) as zoomed frame strips plus a GIF.

    python3 dev_ambient.py tree_anim crystals_anim      # -> scratch previews in previews/dev-ambient*.png
"""
import sys
from pathlib import Path

from PIL import Image

from arcpix.out import PREVIEWS, save
from arcpix.preview import Tile, contact_sheet
from parts import ambient as A

ids = sys.argv[1:] or [a.id for a in A.CATALOGUE]
tiles, gif = [], []
for i in ids:
    a = next(x for x in A.CATALOGUE if x.id == i)
    for v in list(a.variants)[:3]:
        imgs = A.frames_of(a, v)
        tiles.append(Tile(A._strip_preview(a, imgs), f"{a.id} {v}", f"{len(imgs)}f"))
        gif.append((a, imgs))
out = Path(PREVIEWS / "dev-ambient.png")
save(contact_sheet("dev", tiles, columns=1, zoom=6, checker=True), out)
# a looping GIF of the first asset, 4 loops, 8x
a, imgs = gif[0]
z = 8
fr = [A._strip_preview(a, [im]).resize((im.width * z, im.height * z), Image.NEAREST).convert("RGB") for im in imgs]
fr = fr * 2
fr[0].save(PREVIEWS / "dev-ambient.gif", save_all=True, append_images=fr[1:], duration=int(1000 / a.fps), loop=0)
print("ok", out)
