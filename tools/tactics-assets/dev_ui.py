"""Quick look at the UI atlases at high zoom: python3 dev_ui.py [atlas ...] [--zoom N]"""
import sys

from PIL import Image

from arcpix.out import PACK, PREVIEWS
from arcpix.preview import scale

args = [a for a in sys.argv[1:] if not a.startswith("--")]
zoom = next((int(a.split("=")[1]) for a in sys.argv[1:] if a.startswith("--zoom=")), 8)
names = args or ["panels"]
ims = [Image.open(PACK / "ui" / f"{n}.png").convert("RGBA") for n in names]
W = max(i.width for i in ims) * zoom
H = sum(i.height * zoom + 8 for i in ims)
out = Image.new("RGBA", (W, H), (60, 64, 90, 255))
y = 0
for im in ims:
    out.alpha_composite(scale(im, zoom), (0, y))
    y += im.height * zoom + 8
out.save(PREVIEWS / "dev.png")
print(out.size)
