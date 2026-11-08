"""Register two complete giant figures and their distinct existing ability presentations."""
from pathlib import Path
import json, shutil

ROOT = Path(__file__).resolve().parent
BASE = ROOT.parents[3] / 'content/tactics/base'
path = BASE / 'creatures/manifest.json'
manifest = json.loads(path.read_text())
for mob in json.loads((ROOT / 'packing.json').read_text()):
    name = 'mob_refresh_' + mob['id']
    shutil.copyfile(ROOT / 'export' / mob['id'] / 'atlas.png', BASE / 'creatures' / (name + '.png'))
    idle = [f for f in mob['frames'] if f['action'] == 'idle']
    head_top = round(min(mob['anchor'][1] + (f['head'][1] - f['foot'][1]) * f['scale'] for f in idle))
    head_x = round(mob['anchor'][0] + (idle[0]['head'][0] - idle[0]['foot'][0]) * idle[0]['scale'])
    # HUD portraits show the front pose; global headTop still clears the tallest facing's effects.
    front_head_top = round(mob['anchor'][1] + (idle[0]['head'][1] - idle[0]['foot'][1]) * idle[0]['scale'])
    checks = json.loads((ROOT / 'export' / mob['id'] / 'checks.json').read_text())
    bounds = [f['bounds'] for f in checks if f['action'] == 'idle']
    clearance = {}
    for action in ['attack', 'special', 'buff']:
        top = min(f['bounds'][1] for f in checks if f['action'] == action)
        if top < head_top:
            clearance[action] = round((head_top - top) * mob['pixelScale'] + 2, 2)
    special = {'frames': [14, 14, 15, 15, 16, 16, 17, 0], 'fps': 10 if mob['id'] == 'troll' else 8, 'loop': False}
    buff = {'frames': [18, 18, 19, 19, 20, 20, 21, 0], 'fps': 10, 'loop': False}
    manifest['sprites'][name] = {
        'file': name + '.png', 'family': mob['id'], 'variant': 'stone-and-mire',
        'frame': {'width': mob['size'], 'height': mob['size'], 'anchor': mob['anchor']},
        'portrait': [head_x - 32, max(0, front_head_top - 3), 64, 64], 'headTop': head_top,
        'overheadClearance': clearance,
        'pixelScale': mob['pixelScale'], 'columns': 8, 'rowsPerDirection': mob['rowsPerDirection'],
        'directions': ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east'],
        'facingOffsets': {v: [0, 0] for v in ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east']},
        'footprintWidth': round(max(b[2] - b[0] + 1 for b in bounds) * mob['pixelScale'], 2),
        'footprintHeight': round(max(b[3] - b[1] + 1 for b in bounds) * mob['pixelScale'], 2),
        'keys': ['bosses.troll.troll' if mob['id'] == 'troll' else 'bosses.golem.granite'],
        'anims': {
            'idle': {'frames': [0, 1, 2, 3], 'fps': 3, 'loop': True},
            'walk': {'frames': [4, 5, 6, 7], 'fps': 5 if mob['id'] == 'troll' else 4, 'loop': True},
            'attack': {'frames': [8, 9, 9, 9, 10, 10, 11, 0], 'fps': 10, 'loop': False},
            'hurt': {'frames': [12], 'fps': 8, 'loop': False},
            'down': {'frames': [13], 'fps': 1, 'loop': False},
            'special': special,
            **{key: buff for key in ['buff', 'cast', 'point', 'pray', 'channel', 'summon']},
        },
        'provenance': 'Scenario creature concepts; OpenAI complete pixel poses. Sources and export checks: tools/tactics-assets/imagegen/stone-and-mire-2026-11-07/.',
    }
path.write_text(json.dumps(manifest, indent=2) + '\n')

# Register the visual key for validation and preserve a matching legacy-sheet fallback.
path = BASE / 'bosses/manifest.json'
bosses = json.loads(path.read_text())
bosses['anims'].setdefault('buff', dict(bosses['anims']['special']))
path.write_text(json.dumps(bosses, indent=2) + '\n')

# Only two visual bindings change; legacy boss sheets also resolve buff to special.
path = BASE / 'rules/abilities/monsters.json'
text = path.read_text()
for ability in ['troll.frenzy_roar', 'golem.stone_aegis']:
    start = text.index('"id": "' + ability + '"')
    end = text.index('"ai":', start)
    block = text[start:end]
    if '"anim": "bosses.special"' in block:
        text = text[:start] + block.replace('"anim": "bosses.special"', '"anim": "bosses.buff"') + text[end:]
path.write_text(text)
print('Registered Troll and granite Golem; assigned distinct buff gestures without changing combat mechanics.')
