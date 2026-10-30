"""Race x class matrix (front idle): python3 dev_matrix.py [frame]"""
import sys
from arcpix.preview import matrix
from parts.creator import RACES, CLASSES, make_character
from parts.characters import compose, frame_list
from parts.rig import ANIMS
k = int(sys.argv[1]) if len(sys.argv) > 1 else 0
d = "back" if k >= 20 else "front"
pose = frame_list()[k % 20][2]
cells = []
for r in RACES:
    row = []
    for c in CLASSES:
        rec = make_character("X", r, c)
        row.append(compose(rec, d, pose).render(rec.ramps()))
    cells.append(row)
img = matrix("race x class", [RACES[r].name for r in RACES], [CLASSES[c].name for c in CLASSES], cells, zoom=3)
img.save("previews/dev.png"); print(img.size)
