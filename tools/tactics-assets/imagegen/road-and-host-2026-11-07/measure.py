"""Locate and calibrate whole authored figures; no pixel repainting or deformation."""
from pathlib import Path
import json
import numpy as np
from PIL import Image
from scipy.ndimage import label, find_objects

ROOT = Path(__file__).resolve().parent
DIRECTIONS = ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east']
CACHE = {}

def source(name, count, columns=4):
    if name in CACHE:
        return CACHE[name]
    rgba = np.array(Image.open(ROOT / 'source' / (name + '.png')).convert('RGBA'))
    alpha = rgba[:, :, 3]
    labels, _ = label(alpha >= 64, np.ones((3, 3)))
    sizes = np.bincount(labels.ravel())
    boxes = [(i, b) for i, b in enumerate(find_objects(labels), 1) if sizes[i] > 100]
    assert len(boxes) == count, (name, len(boxes), count)
    boxes.sort(key=lambda v: v[1][0].stop)
    ordered = []
    for start in range(0, count, columns):
        ordered += sorted(boxes[start:start + columns], key=lambda v: v[1][1].start)
    frames = []
    for index, (lab, (yy, xx)) in enumerate(ordered):
        assert not np.any((labels[yy, xx] != 0) & (labels[yy, xx] != lab) & (sizes[labels[yy, xx]] > 100)), (name, index, 'neighbor')
        h = yy.stop - yy.start
        strip = alpha[max(yy.start, yy.stop - 10):yy.stop, xx]
        contact = np.where(strip >= 128)[1]
        foot = [xx.start + (int(contact.min()) + int(contact.max()) + 1) / 2, yy.stop]
        head_mask = alpha[yy.start:yy.start + max(1, int(h * .23)), xx] >= 128
        head_x = xx.start + (int(np.where(head_mask)[1].min()) + int(np.where(head_mask)[1].max()) + 1) / 2
        x, y = max(0, xx.start - 2), max(0, yy.start - 2)
        x2, y2 = min(alpha.shape[1], xx.stop + 2), min(alpha.shape[0], yy.stop + 2)
        frames.append({'head': [head_x, yy.start], 'source': 'source/' + name + '.png', 'sourcePose': index,
                       'cell': [x, y, x2 - x, y2 - y], 'bounds': [xx.start, yy.start, xx.stop, yy.stop],
                       'foot': foot, 'sourceEdge': xx.start == 0 or yy.start == 0 or xx.stop == alpha.shape[1] or yy.stop == alpha.shape[0]})
    CACHE[name] = frames
    return frames

def calibrated(name, count, height, columns=4):
    frames = [dict(f) for f in source(name, count, columns)]
    scale = height / (frames[0]['foot'][1] - frames[0]['bounds'][1])
    for f in frames:
        f['scale'] = scale
    return frames

items = []
for mob, height in [('hobgoblin', 132), ('bandit', 120)]:
    idle = calibrated(mob + '-idle', 32, height)
    walk = calibrated(mob + '-walk', 32, height)
    attack = calibrated(mob + '-attack', 32, height * .9)
    damage = calibrated(mob + '-damage', 16, height)
    if mob == 'hobgoblin':
        attack[:8] = calibrated('hobgoblin-attack-front', 8, height * .9)
        # Narrow image-generation cleanup preserves the original pose's silhouette and proportions.
        reference = attack[17]
        extent = (reference['bounds'][3] - reference['bounds'][1]) * reference['scale']
        attack[17] = calibrated('hobgoblin-overhead-clean', 1, extent)[0]
    else:
        # Reject mirrored rear proposals. Two authored attack keys between quiet ready/recovery
        # keep the right axe arm and left pouch fixed in the difficult rear diagonals.
        fixed = calibrated('bandit-rear-keys', 4, height * 1.15, 2)
        for row, offset in [(3, 0), (5, 2)]:
            attack[row * 4:row * 4 + 4] = [dict(idle[row * 4]), fixed[offset], fixed[offset + 1], dict(idle[row * 4 + 1])]
        fixed = calibrated('bandit-attack-rear', 12, height)
        attack[16:20] = fixed[4:8]
    frames = []
    for row, direction in enumerate(DIRECTIONS):
        di = (row // 2) * 4 + (row % 2) * 2
        for action, poses in [('idle', idle[row * 4:row * 4 + 4]), ('walk', walk[row * 4:row * 4 + 4]),
                              ('attack', attack[row * 4:row * 4 + 4]), ('hurt', [damage[di]]), ('down', [damage[di + 1]])]:
            for index, pose in enumerate(poses):
                assert not pose['sourceEdge'], (mob, direction, action, index, 'selected clipped source')
                frames.append({**pose, 'direction': direction, 'row': row, 'action': action, 'frame': index})
    size, floor = (224, 198) if mob == 'hobgoblin' else (208, 184)
    items.append({'id': mob, 'size': size, 'anchor': [size // 2, floor], 'pixelScale': .32, 'rowsPerDirection': 2, 'frames': frames})
    print(mob, len(frames), 'selected complete poses; target body', height)
(ROOT / 'packing.json').write_text(json.dumps(items, indent=2) + '\n')
