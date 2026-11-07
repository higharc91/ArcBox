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
        # The rejected draft contacts overlap a neighbouring row; none are exported.
        if not (name == 'sapling-attack' and index % 2 == 1):
            assert not np.any((labels[yy, xx] != 0) & (labels[yy, xx] != lab) & (sizes[labels[yy, xx]] > 100)), (name, index, 'neighbor')
        h = yy.stop - yy.start
        strip = alpha[max(yy.start, yy.stop - 10):yy.stop, xx]
        contact = np.where(strip >= 128)[1]
        foot = [xx.start + (int(contact.min()) + int(contact.max()) + 1) / 2, yy.stop]
        if name == 'sapling-contact':
            # A forward lash may extend below the feet. Anchor the root feet, not its tip.
            region = rgba[yy, xx]
            rgb = region[:, :, :3].astype(int)
            wood = (region[:, :, 3] >= 128) & (rgb[:,:,0] > rgb[:,:,1] + 8) & (rgb[:,:,1] > rgb[:,:,2] + 5)
            wood[:int(h*.4)] = False
            assert wood.any(), (name,index,'missing woody root feet')
            bottom = int(np.where(wood)[0].max()) + 1
            root_x = np.where(wood[max(0,bottom-10):bottom])[1]
            foot = [xx.start + (int(root_x.min()) + int(root_x.max()) + 1) / 2, yy.start + bottom]
        # Register both leaf masses together, not whichever crown cluster is largest.
        region = rgba[yy, xx]
        rgb = region[:, :, :3].astype(int)
        leaves = (region[:, :, 3] >= 128) & (rgb[:,:,1] > rgb[:,:,0] + 12) & (rgb[:,:,1] > rgb[:,:,2] + 20)
        leaves[max(1, int(h * .4)):] = False
        assert leaves.any(), (name, index, 'missing leaf crown anchor')
        head_x = xx.start + float(np.where(leaves)[1].mean())
        head_y = yy.start + int(np.where(leaves)[0].min())
        x, y = max(0, xx.start - 2), max(0, yy.start - 2)
        x2, y2 = min(alpha.shape[1], xx.stop + 2), min(alpha.shape[0], yy.stop + 2)
        frames.append({'head': [head_x, head_y], 'source': 'source/' + name + '.png', 'sourcePose': index,
                       'cell': [x, y, x2 - x, y2 - y], 'bounds': [xx.start, yy.start, xx.stop, yy.stop],
                       'foot': foot,
                       'sourceEdge': xx.start == 0 or yy.start == 0 or xx.stop == alpha.shape[1] or yy.stop == alpha.shape[0]})
    CACHE[name] = frames
    return frames

def calibrated(name, count, height, columns=4, reference=0):
    frames = [dict(f) for f in source(name, count, columns)]
    scale = height / (frames[reference]['foot'][1] - frames[reference]['head'][1])
    for f in frames:
        f['scale'] = scale
    return frames

SOURCE_SETS = json.loads((ROOT / 'source-sets.json').read_text())
def authored_action(mob, action, count, height):
    parts = SOURCE_SETS.get(mob, {}).get(action, [{'source': mob + '-' + action, 'count': count}])
    frames = []
    for part in parts:
        frames += calibrated(part['source'], part['count'], height, part.get('columns', 4), part.get('referencePose', 0))
    assert len(frames) == count, (mob, action, len(frames), count)
    return frames

ROW_MAP = json.loads((ROOT / 'facings.json').read_text())
def facing_order(action, frames, stride):
    rows = ROW_MAP.get(action, list(range(8)))
    assert sorted(rows) == list(range(8)), (action, rows)
    return [frames[row * stride + i] for row in rows for i in range(stride)]

items = []
corrections_path = ROOT / 'corrections.json'
corrections = json.loads(corrections_path.read_text()) if corrections_path.exists() else []
for mob, height in [('sapling', 64)]:
    idle = facing_order('idle', calibrated(mob + '-idle', 32, height), 4)
    walk = facing_order('walk', calibrated(mob + '-walk', 32, 64), 4)
    attack_phases = facing_order('attack', calibrated(mob + '-attack', 16, 64, columns=2), 2)
    attack = [pose for row in range(8) for pose in [idle[row*4], attack_phases[row*2], attack_phases[row*2+1], idle[row*4+3]]]
    damage = facing_order('damage', calibrated(mob + '-damage', 16, 60), 2)
    groups = {}
    for row in range(8):
        di = (row // 2) * 4 + (row % 2) * 2
        for action, poses in [('idle', idle[row * 4:row * 4 + 4]), ('walk', walk[row * 4:row * 4 + 4]),
                              ('attack', attack[row * 4:row * 4 + 4]), ('hurt', [damage[di]]), ('down', [damage[di + 1]])]:
            groups[(row, action)] = poses
        # Centre the complete fallen sapling on its tile rather than anchoring it to one slack foot.
        fallen = dict(groups[(row, 'down')][0])
        fallen['foot'] = [(fallen['bounds'][0] + fallen['bounds'][2]) / 2, fallen['foot'][1]]
        groups[(row, 'down')] = [fallen]
    for fix in [c for c in corrections if c['mob'] == mob]:
        fixed = calibrated(fix['source'], fix['count'], height * fix.get('heightFactor', 1), fix.get('columns', 4), fix.get('referencePose', 0))
        for target in fix['targets']:
            key = (target['row'], fix['action'])
            if 'frames' in target:
                replacement = list(groups[key])
                for destination, source_index in zip(target['frames'], target['poses'], strict=True):
                    replacement[destination] = fixed[source_index]
                groups[key] = replacement
            else:
                groups[key] = [fixed[i] for i in target['poses']]
    frames = []
    for row, direction in enumerate(DIRECTIONS):
        for action in ['idle', 'walk', 'attack', 'hurt', 'down']:
            for index, pose in enumerate(groups[(row, action)]):
                assert not pose['sourceEdge'], (mob, direction, action, index, 'selected clipped source')
                frames.append({**pose, 'direction': direction, 'row': row, 'action': action, 'frame': index})
    size, floor = 320, 192
    items.append({'id': mob, 'size': size, 'anchor': [size // 2, floor], 'pixelScale': .32,
                  'rowsPerDirection': 2,
                  'actionOffsets': {'idle': 0, 'walk': 4, 'attack': 8, 'hurt': 12, 'down': 13}, 'frames': frames})
    print(mob, len(frames), 'selected complete poses; target body', height)
(ROOT / 'packing.json').write_text(json.dumps(items, indent=2) + '\n')
