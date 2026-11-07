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
        # Isolate the head from a staff tip in the same horizontal band.
        head_labels, _ = label(head_mask, np.ones((3, 3)))
        head_sizes = np.bincount(head_labels.ravel()); head_sizes[0] = 0
        head_mask = head_labels == int(head_sizes.argmax())
        head_x = xx.start + (int(np.where(head_mask)[1].min()) + int(np.where(head_mask)[1].max()) + 1) / 2
        head_y = yy.start + int(np.where(head_mask)[0].min())
        x, y = max(0, xx.start - 2), max(0, yy.start - 2)
        x2, y2 = min(alpha.shape[1], xx.stop + 2), min(alpha.shape[0], yy.stop + 2)
        frames.append({'head': [head_x, head_y], 'source': 'source/' + name + '.png', 'sourcePose': index,
                       'cell': [x, y, x2 - x, y2 - y], 'bounds': [xx.start, yy.start, xx.stop, yy.stop],
                       'foot': foot, 'sourceEdge': xx.start == 0 or yy.start == 0 or xx.stop == alpha.shape[1] or yy.stop == alpha.shape[0]})
    CACHE[name] = frames
    return frames

def calibrated(name, count, height, columns=4):
    frames = [dict(f) for f in source(name, count, columns)]
    scale = height / (frames[0]['foot'][1] - frames[0]['head'][1])
    for f in frames:
        f['scale'] = scale
    return frames

items = []
for mob, height in [('cultist', 120), ('cult_leader', 116)]:
    idle = calibrated(mob + '-idle', 32, height)
    walk = calibrated(mob + '-walk', 32, height)
    attack = calibrated(mob + '-attack', 32, height * .9)
    damage = calibrated(mob + '-damage', 16, height)
    cast = calibrated(mob + '-cast', 32, height)
    if mob == 'cult_leader':
        for action, poses in [('idle', idle), ('walk', walk), ('cast', cast)]:
            fixed = calibrated(mob + '-' + action + '-rear', 12, height)
            poses[12:24] = fixed
        fixed = calibrated(mob + '-cast-profile', 8, height)
        cast[24:28] = fixed[:4]
        fixed = calibrated(mob + '-hurt-rear', 12, height)
        for row, index in [(3, 0), (4, 4), (5, 8)]:
            di = (row // 2) * 4 + (row % 2) * 2
            damage[di] = fixed[index]
    frames = []
    for row, direction in enumerate(DIRECTIONS):
        di = (row // 2) * 4 + (row % 2) * 2
        groups = [('idle', idle[row * 4:row * 4 + 4]), ('walk', walk[row * 4:row * 4 + 4]),
                  ('attack', attack[row * 4:row * 4 + 4]), ('hurt', [damage[di]]), ('down', [damage[di + 1]]),
                  ('cast', [cast[row * 4 + i] for i in [0, 2, 3]])]
        for action, poses in groups:
            for index, pose in enumerate(poses):
                assert not pose['sourceEdge'], (mob, direction, action, index, 'selected clipped source')
                frames.append({**pose, 'direction': direction, 'row': row, 'action': action, 'frame': index})
    size, floor = 224, 198
    items.append({'id': mob, 'size': size, 'anchor': [size // 2, floor], 'pixelScale': .32,
                  'rowsPerDirection': 3, 'actionOffsets': {'idle': 0, 'walk': 4, 'attack': 8, 'hurt': 12, 'down': 13, 'cast': 14}, 'frames': frames})
    print(mob, len(frames), 'selected complete poses; target body', height)
(ROOT / 'packing.json').write_text(json.dumps(items, indent=2) + '\n')
