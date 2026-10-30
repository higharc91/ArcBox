"""Quick looks at the town kit while drawing: python3 dev_town.py tex | decals | b id[:variant] ... | diorama"""
import sys
from pathlib import Path

from arcpix.preview import Tile, contact_sheet
from parts import town as TW

OUT = Path("/tmp/claude-0/-home-user-ArcBox/b1deb369-fed3-5fd5-b028-eeb6ff8ac6c2/scratchpad")
OUT.mkdir(parents=True, exist_ok=True)
what = sys.argv[1] if len(sys.argv) > 1 else "tex"
if what in ("tex", "decals"):
    kinds = ("wall", "roof", "floor") if what == "tex" else ("wall_decal", "floor_decal")
    tiles = [Tile(TW.tex_image(t.id, v), t.id, v) for t in TW.CATALOGUE if t.kind in kinds
             for v in list(t.variants)[:1 if what == "decals" else 2]]
    contact_sheet(what, tiles, columns=6 if what == "tex" else 10, zoom=3 if what == "tex" else 5,
                  checker=True).save(OUT / f"town-{what}.png")
elif what == "b":
    tiles = []
    for arg in [a for a in sys.argv[2:] if not a.isdigit()]:
        bid, _, var = arg.partition(":")
        r = TW.render_building(bid, var or None)
        tiles.append(Tile(r.image, arg))
    contact_sheet("buildings", tiles, columns=min(4, len(tiles)), zoom=int(sys.argv[-1]) if sys.argv[-1].isdigit()
                  else 3, checker=True).save(OUT / "town-b.png")
elif what == "diorama":
    TW.dev_diorama(OUT)
elif what == "dusk":
    from arcpix import iso
    iso.finish(TW.dusk_market(), bloom=0.3).save(OUT / "town-dusk.png")
elif what == "interior":
    from arcpix import iso
    iso.finish(TW._scene_img(TW.interior_scene(), "dusk", margin=110)).save(OUT / "town-interior.png")
elif what == "kit":
    from arcpix import iso
    iso.finish(TW._scene_img(TW.kit_scene(), "day", margin=110)).save(OUT / "town-kit.png")
