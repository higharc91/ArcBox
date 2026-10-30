"""Customisation check: python3 dev_custom.py"""
from arcpix.preview import contact_sheet, Tile, strip
from parts.creator import make_character, Look, HAIR_STYLES, ALL_BEARDS, MARKS, RACES
from parts.characters import compose
from parts.rig import ANIMS
pose = ANIMS["idle"][2][0]
def img(rec, d="front"):
    return compose(rec, d, pose).render(rec.ramps())
tiles = []
for race in RACES:
    a = make_character("x", race, "monk")
    b = make_character("x", race, "monk", Look(body_type="feminine"))
    tiles.append(Tile(strip([img(a), img(b)], 1, gap=4), race))
for h in HAIR_STYLES:
    r = make_character("x", "human", "bard", Look(body_type="feminine", hair=h, hair_colour="auburn"))
    tiles.append(Tile(strip([img(r), img(r, "back")], 1, gap=4), h))
for bd in ALL_BEARDS:
    r = make_character("x", "human", "fighter", Look(beard=bd, hair_colour="black"))
    tiles.append(Tile(img(r), bd))
for m, pt in zip(MARKS, ["woad", "woad", "ochre", "kohl", "jade"]):
    r = make_character("x", "half_orc", "monk", Look(marks=m, paint=pt, body_type="feminine"))
    tiles.append(Tile(img(r), m))
im = contact_sheet("custom", tiles, columns=8, zoom=4)
im.save("previews/dev.png"); print(im.size)
