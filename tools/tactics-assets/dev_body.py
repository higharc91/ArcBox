from arcpix.preview import contact_sheet, Tile
from parts.creator import make_character, Look
from parts.characters import compose
from parts.rig import ANIMS
pose = ANIMS["idle"][2][0]
tiles = []
for race, cls, look in [("human", "monk", {}), ("human", "fighter", {}), ("elf", "wizard", {}), ("dwarf", "cleric", {"beard": "none"})]:
    for bt in ("masculine", "feminine"):
        r = make_character("x", race, cls, Look(body_type=bt, **look))
        tiles.append(Tile(compose(r, "front", pose).render(r.ramps()), f"{race} {cls} {bt[0]}"))
im = contact_sheet("body", tiles, columns=8, zoom=6); im.save("previews/zoom.png")
