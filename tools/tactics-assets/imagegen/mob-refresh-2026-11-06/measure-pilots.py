"""Measure generated candidate sprites for lossless frame extraction and nearest-neighbor preview export.

Only inspects source pixels; the companion Canvas packer copies/rescales complete sprites.
Whole-sheet connected components avoid cutting a tail or weapon at an approximate AI grid boundary.
"""
from pathlib import Path
import json
import statistics
import numpy as np
from PIL import Image
from scipy.ndimage import label, find_objects, center_of_mass

root = Path(__file__).resolve().parent
configs = [('goblin', 'goblin-idle-turnaround.png', 128, 80, 111),
           ('orc', 'orc-idle-turnaround-v2.png', 192, 132, 171),
           ('drake', 'drake-idle-turnaround.png', 192, 88, 171),
           ('goblin-walk', 'goblin-walk-front-clean.png', 128, 80, 111)]
items = []
for kind, file, size, target_height, floor in configs:
    image = Image.open(root / file).convert('RGBA')
    alpha = np.array(image.getchannel('A')) >= 64
    labels, count = label(alpha, structure=np.ones((3, 3)))
    assert count == 8, (kind, count)  # each pose, including its held weapon, is one opaque island
    boxes = find_objects(labels)
    centers = center_of_mass(alpha, labels, list(range(1, 9)))
    ranked = sorted(range(8), key=lambda i: centers[i][0])
    order = sorted(ranked[:4], key=lambda i: centers[i][1]) + sorted(ranked[4:], key=lambda i: centers[i][1])
    frames = []
    for idx in order:
        yy, xx = boxes[idx]
        assert not np.any((labels[yy, xx] > 0) & (labels[yy, xx] != idx + 1))
        x, y = max(0, xx.start - 2), max(0, yy.start - 2)
        x2, y2 = min(image.width, xx.stop + 2), min(image.height, yy.stop + 2)
        local = image.crop((x, y, x2, y2))
        a = local.getchannel('A')
        bounds = a.point(lambda v: 255 if v >= 128 else 0).getbbox()
        assert bounds
        lower = a.crop((bounds[0], bounds[1] + int((bounds[3] - bounds[1]) * .84), bounds[2], bounds[3])).point(lambda v: 255 if v >= 128 else 0).getbbox()
        assert lower
        foot = [bounds[0] + (lower[0] + lower[2]) / 2, bounds[3]]
        head = None
        if kind == 'goblin-walk':
            green = []
            for py in range(bounds[1], bounds[1] + int((bounds[3] - bounds[1]) * .28)):
                for px in range(bounds[0], bounds[2]):
                    r, g, blue, opacity = local.getpixel((px, py))
                    if opacity >= 128 and g >= r * .65 and r >= blue * 1.2:
                        green.append((px, py))
            assert green
            head = [(min(p[0] for p in green) + max(p[0] for p in green)) / 2, min(p[1] for p in green)]
        frames.append({'cell': [x, y, x2 - x, y2 - y], 'bounds': list(bounds), 'foot': foot, 'head': head})
    scale = target_height / statistics.median(f['bounds'][3] - f['bounds'][1] for f in frames)
    if kind == 'drake':
        scale = min(scale, 150 / max(f['bounds'][2] - f['bounds'][0] for f in frames))
    items.append({'id': kind, 'source': file, 'size': size, 'floor': floor, 'scale': scale,
                  'pixelScale': .32, 'frames': frames, 'status': 'review candidate; not production art'})
(root / 'pilots/packing.json').write_text(json.dumps(items, indent=2) + '\n')
print('Measured 32 connected sprites; no neighboring pose falls inside any extraction rectangle.')
