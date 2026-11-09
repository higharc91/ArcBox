"""Locate and calibrate whole authored figures; no pixel repainting or deformation."""
from pathlib import Path
import json
import numpy as np
from PIL import Image
from scipy.ndimage import label, find_objects, binary_erosion, binary_closing

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
        neighbor = bool(np.any((labels[yy, xx] != 0) & (labels[yy, xx] != lab) & (sizes[labels[yy, xx]] > 100)))
        h = yy.stop - yy.start
        strip = alpha[max(yy.start, yy.stop - 10):yy.stop, xx]
        contact = np.where(strip >= 128)[1]
        foot = [xx.start + (int(contact.min()) + int(contact.max()) + 1) / 2, yy.stop]
        head_mask = alpha[yy.start:yy.start + max(1, int(h * .23)), xx] >= 128
        # Isolate the head from a weapon tip in the same horizontal band.
        head_labels, _ = label(head_mask, np.ones((3, 3)))
        head_sizes = np.bincount(head_labels.ravel()); head_sizes[0] = 0
        head_mask = head_labels == int(head_sizes.argmax())
        head_x = xx.start + (int(np.where(head_mask)[1].min()) + int(np.where(head_mask)[1].max()) + 1) / 2
        head_y = yy.start + int(np.where(head_mask)[0].min())
        x, y = max(0, xx.start - 2), max(0, yy.start - 2)
        x2, y2 = min(alpha.shape[1], xx.stop + 2), min(alpha.shape[0], yy.stop + 2)
        frames.append({'head': [head_x, head_y], 'source': 'source/' + name + '.png', 'sourcePose': index,
                       'cell': [x, y, x2 - x, y2 - y], 'bounds': [xx.start, yy.start, xx.stop, yy.stop],
                       'foot': foot,
                       'sourceNeighbor': neighbor, 'sourceEdge': xx.start == 0 or yy.start == 0 or xx.stop == alpha.shape[1] or yy.stop == alpha.shape[0]})
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

items = []
roles=json.loads((ROOT/'roles.json').read_text())
corrections=json.loads((ROOT/'corrections.json').read_text())
row_maps=json.loads((ROOT/'facings.json').read_text())
def facing_order(mob,action,frames,stride):
    rows=row_maps.get(mob,{}).get(action,list(range(8)))
    assert sorted(rows)==list(range(8))
    return [frames[row*stride+i] for row in rows for i in range(stride)]
for role in roles:
    mob,height=role['id'],role['height']
    idle=facing_order(mob,'idle',calibrated(mob+'-idle',32,height),4)
    walk=facing_order(mob,'walk',calibrated(mob+'-walk',32,height),4)
    melee=facing_order(mob,'attack',calibrated(mob+'-attack',32,height),4)
    breath=facing_order(mob,'breath',calibrated(mob+'-breath',32,height),4)
    roar=facing_order(mob,'roar',calibrated(mob+'-roar',32,height),4)
    wingbeat=facing_order(mob,'wingbeat',calibrated(mob+'-wingbeat',32,height),4)
    damage=facing_order(mob,'damage',calibrated(mob+'-damage',16,height-8),2)
    order=json.loads((ROOT/'damage-order.json').read_text()).get(mob,list(range(16)))
    damage=[damage[i] for i in order]
    groups={}
    for row in range(8):
        groups[(row,'idle')]=idle[row*4:row*4+4]
        groups[(row,'walk')]=walk[row*4:row*4+4]
        groups[(row,'attack')]=melee[row*4:row*4+4]
        groups[(row,'special')]=breath[row*4:row*4+4]
        groups[(row,'cast')]=roar[row*4:row*4+4]
        groups[(row,'buff')]=wingbeat[row*4:row*4+4]
        groups[(row,'hurt')]=[damage[row*2]]
        fallen=dict(damage[row*2+1]);fallen['foot']=[(fallen['bounds'][0]+fallen['bounds'][2])/2,fallen['foot'][1]]
        groups[(row,'down')]=[fallen]
    for fix in [v for v in corrections if v['mob']==mob]:
        fixed=calibrated(fix['source'],fix['count'],height*fix.get('heightFactor',1),fix.get('columns',4),fix.get('referencePose',0))
        for target in fix['targets']:
            key=(target['row'],fix['action'])
            if 'frames' in target:
                replacement=list(groups[key])
                for destination,source_index in zip(target['frames'],target['poses'],strict=True): replacement[destination]=fixed[source_index]
                groups[key]=replacement
            else:groups[key]=[fixed[i] for i in target['poses']]
    for row in range(8):
        for pose in groups[(row,'down')]:
            pose['foot'] = [(pose['bounds'][0]+pose['bounds'][2])/2, pose['foot'][1]]
    frames=[]
    for row,direction in enumerate(DIRECTIONS):
        for action in ['idle','walk','attack','hurt','down','special','cast','buff']:
            for index,pose in enumerate(groups[(row,action)]):
                assert not pose['sourceNeighbor'],(mob,direction,action,index,'selected crop contains a neighbor')
                assert not pose['sourceEdge'],(mob,direction,action,index,'selected clipped source')
                frames.append({**pose,'direction':direction,'row':row,'action':action,'frame':index})
    items.append({'id':mob,'size':role['size'],'anchor':role['anchor'],'pixelScale':.32,'headRegistration':False,'rowsPerDirection':4,
        'actionOffsets':{'idle':0,'walk':4,'attack':8,'hurt':12,'down':13,'special':14,'cast':18,'buff':22},'frames':frames})
    print(mob,len(frames),'selected complete frame keys')
(ROOT/'packing.json').write_text(json.dumps(items,indent=2)+'\n')
