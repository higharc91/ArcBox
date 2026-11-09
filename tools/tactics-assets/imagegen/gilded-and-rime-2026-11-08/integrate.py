"""Register complete figures and existing ability clips; preserve prior entries/rules."""
from pathlib import Path
import json
import shutil

ROOT = Path(__file__).resolve().parent
BASE = ROOT.parents[3] / 'content/tactics/base'
path = BASE / 'creatures/manifest.json'
manifest = json.loads(path.read_text())
roles = {r['id']: r for r in json.loads((ROOT / 'roles.json').read_text())}
for mob in json.loads((ROOT / 'packing.json').read_text()):
    role = roles[mob['id']]
    dragon = role['id'] == 'dragon_gold'
    name = 'mob_refresh_' + role['id']
    shutil.copyfile(ROOT / 'export' / role['id'] / 'atlas.png', BASE / 'creatures' / (name + '.png'))
    checks = json.loads((ROOT / 'export' / role['id'] / 'checks.json').read_text())
    bounds = [f['bounds'] for f in checks if f['action'] == 'idle']
    head_top = min(b[1] for b in bounds)
    front = next(f for f in mob['frames'] if f['action'] == 'idle')
    hx = round(mob['anchor'][0] + (front['head'][0] - front['foot'][0]) * front['scale'])
    hy = round(mob['anchor'][1] + (front['head'][1] - front['foot'][1]) * front['scale'])
    size = role['portraitSize']
    clearance = {}
    for action in ['walk', 'attack', 'special', 'cast', 'buff']:
        active = [f['bounds'][1] for f in checks if f['action'] == action]
        if active and min(active) < head_top:
            clearance[action] = round((head_top - min(active)) * mob['pixelScale'] + 2, 2)
    def clip(frames, fps=10, loop=False):
        return {'frames': frames, 'fps': fps, 'loop': loop}
    anims = {'idle': clip([0, 1, 2, 3], role['idleFps'], True),
             'walk': clip([4, 5, 6, 7], role['walkFps'], True),
             'attack': clip([8, 9, 9, 9, 9, 10, 10, 11, 0, 0] if dragon else [8, 9, 9, 9, 10, 10, 11, 0]),
             'hurt': clip([12], 8), 'down': clip([13], 1)}
    if dragon:
        anims.update(special=clip([14, 15, 15, 15, 15, 16, 16, 16, 17, 0, 0, 0]),
                     cast=clip([18, 19, 19, 19, 19, 20, 20, 20, 21, 0, 0, 0]),
                     buff=clip([22, 23, 23, 23, 23, 23, 23, 24, 24, 24, 25, 0, 0, 0]))
    else:
        anims.update(special=clip([14, 14, 15, 15, 16, 16, 17, 0]),
                     buff=clip([18, 18, 19, 19, 20, 20, 21, 0]))
        for alias in ['cast', 'point', 'pray', 'channel', 'summon']:
            anims[alias] = anims['buff']
            if 'buff' in clearance:
                clearance[alias] = clearance['buff']
    manifest['sprites'][name] = {
        'file': name + '.png', 'family': 'dragon' if dragon else 'troll', 'variant': 'gilded-and-rime',
        'frame': {'width': mob['size'], 'height': mob['size'], 'anchor': mob['anchor']},
        'portrait': [hx - size // 2, max(0, hy + 8), size, size], 'headTop': head_top,
        'overheadClearance': clearance, 'pixelScale': mob['pixelScale'], 'columns': 8,
        'rowsPerDirection': mob['rowsPerDirection'],
        'directions': ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east'],
        'facingOffsets': {d: [0, 0] for d in ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east']},
        'footprintWidth': round(max(b[2] - b[0] + 1 for b in bounds) * mob['pixelScale'], 2),
        'footprintHeight': round(max(b[3] - b[1] + 1 for b in bounds) * mob['pixelScale'], 2),
        'keys': [role['key']], 'anims': anims,
        'provenance': 'Scenario concept sheet; OpenAI complete pixel figures. Sources/packing: tools/tactics-assets/imagegen/gilded-and-rime-2026-11-08/.'}
path.write_text(json.dumps(manifest, indent=2) + '\n')
print('Registered Gold Dragon, Frost Troll and Ogre; prior art/rules unchanged.')
