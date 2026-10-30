"""Quick look at icons while drawing them: python3 dev_icons.py [ids or categories...] [--zoom=N] [--out=PATH]"""
import sys
import tempfile
from pathlib import Path
from arcpix.preview import contact_sheet, Tile
from parts.icons import CATALOGUE, RARITIES, render_frame

args = [a for a in sys.argv[1:] if not a.startswith("--")]
zoom = int(next((a.split("=")[1] for a in sys.argv[1:] if a.startswith("--zoom=")), 8))
tiles = [Tile(ic.render(), ic.id, ic.category) for ic in CATALOGUE
         if not args or ic.id in args or ic.category in args]
if "frame" in args:
    tiles += [Tile(render_frame(r), r, "frame") for r in RARITIES]
img = contact_sheet("icons dev", tiles, columns=min(6, len(tiles)), zoom=zoom)
out = next((a.split("=", 1)[1] for a in sys.argv[1:] if a.startswith("--out=")),
           str(Path(tempfile.gettempdir()) / "arcbox-icons-dev.png"))
img.save(out); print(out, img.size, len(tiles))
