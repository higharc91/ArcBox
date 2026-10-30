"""Quick look at one boss while drawing: python3 dev_bosses.py <id> [variant] [zoom] [out.png]
Writes the front and back rows (every frame) as one strip; defaults to the scratch file given on the command line."""
import sys

from arcpix.preview import Tile, contact_sheet, strip
from parts import bosses as BS

mid = sys.argv[1]
m = next(x for x in BS.CATALOGUE if x.id == mid)
v = sys.argv[2] if len(sys.argv) > 2 else next(iter(m.variants))
zoom = int(sys.argv[3]) if len(sys.argv) > 3 else 3
path = sys.argv[4] if len(sys.argv) > 4 else "/tmp/boss.png"
fr = BS.frames(m, v)
n = len(BS.frame_poses())
img = contact_sheet(f"{m.name} · {v}", [Tile(strip(fr[:n], 1, gap=3), "front"), Tile(strip(fr[n:], 1, gap=3), "back")],
                    columns=1, zoom=zoom)
img.save(path)
print(img.size)
