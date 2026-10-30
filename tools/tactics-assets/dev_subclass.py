"""Subclass grid: python3 dev_subclass.py [out]"""
import sys
from arcpix.preview import matrix, strip
from parts.creator import SUBCLASSES, CLASSES, make_character, Look
from parts.characters import compose
from parts.rig import ANIMS
RACE = {"fighter": "human", "rogue": "halfling", "wizard": "elf", "cleric": "dwarf", "ranger": "half_elf",
        "paladin": "dragonborn", "bard": "tiefling", "druid": "gnome", "warlock": "human", "monk": "half_orc"}
ATK = {"attack": "attack"}
cells, rows = [], []
for cls in CLASSES:
    row = []
    for s in [x for x in SUBCLASSES.values() if x.cls == cls]:
        r = make_character("x", RACE[cls], cls, Look(body_type="feminine" if len(row) == 1 else "masculine"),
                           subclass=s.id)
        frames = [compose(r, "front", ANIMS["idle"][2][0]), compose(r, "back", ANIMS["idle"][2][0]),
                  compose(r, "front", ANIMS["attack"][2][2])]
        row.append(strip([f.render(r.ramps()) for f in frames], 1, gap=2))
    cells.append(row)
    rows.append(CLASSES[cls].name)
img = matrix("subclasses", rows, ["I", "II", "III"], cells, zoom=3)
img.save(sys.argv[1] if len(sys.argv) > 1 else "previews/dev.png"); print(img.size)
