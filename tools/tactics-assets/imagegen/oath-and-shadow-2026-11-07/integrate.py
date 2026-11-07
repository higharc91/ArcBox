"""Register only two new mob entries, preserving every older entry and combat rule."""
from pathlib import Path
import json, shutil

ROOT = Path(__file__).resolve().parent
BASE = ROOT.parents[3] / 'content/tactics/base'
manifest_path = BASE / 'creatures/manifest.json'
manifest = json.loads(manifest_path.read_text())
for mob in json.loads((ROOT / 'packing.json').read_text()):
    name = 'mob_refresh_' + mob['id']
    shutil.copyfile(ROOT / 'export' / mob['id'] / 'atlas.png', BASE / 'creatures' / (name + '.png'))
    idle = [f for f in mob['frames'] if f['action'] == 'idle']
    head_top = round(min(mob['anchor'][1] + (f['head'][1] - f['foot'][1]) * f['scale'] for f in idle))
    head_x = round(mob['anchor'][0] + (idle[0]['head'][0] - idle[0]['foot'][0]) * idle[0]['scale'])
    bounds = [f['bounds'] for f in json.loads((ROOT / 'export' / mob['id'] / 'checks.json').read_text()) if f['action'] == 'idle']
    manifest['sprites'][name] = {
        'file': name + '.png', 'family': mob['id'], 'variant': 'oath-and-shadow',
        'frame': {'width': mob['size'], 'height': mob['size'], 'anchor': mob['anchor']},
        'portrait': [head_x - 32, max(0, head_top - 3), 64, 64], 'headTop': head_top,
        'pixelScale': mob['pixelScale'], 'columns': 8, 'rowsPerDirection': mob['rowsPerDirection'],
        'directions': ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east'],
        'facingOffsets': {v: [0, 0] for v in ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east']},
        'footprintWidth': round(max(b[2] - b[0] + 1 for b in bounds) * mob['pixelScale'], 2),
        'footprintHeight': round(max(b[3] - b[1] + 1 for b in bounds) * mob['pixelScale'], 2),
        'keys': ['characters.' + mob['id']],
        'anims': {'idle': {'frames': [0, 1, 2, 3], 'fps': 3, 'loop': True},
                  'walk': {'frames': [4, 5, 6, 7], 'fps': 5 if mob['id'] == 'black_knight' else 6, 'loop': True},
                  'attack': {'frames': [8, 9, 9, 10, 10, 11, 0] if mob['id'] == 'black_knight' else [8, 9, 10, 10, 11, 0], 'fps': 10, 'loop': False},
                  'hurt': {'frames': [12], 'fps': 8, 'loop': False},
                  'down': {'frames': [13], 'fps': 1, 'loop': False},
                  **({'slam': {'frames': [8, 9, 9, 10, 10, 11, 0], 'fps': 10, 'loop': False}} if mob['id'] == 'black_knight' else {}),
                  **{key: {'frames': [14, 14, 15, 15, 16, 0], 'fps': 8, 'loop': False} for key in ['cast', 'point', 'summon', 'pray', 'channel', 'buff', 'special']}},
        'provenance': 'Scenario lore concepts; OpenAI pixel poses. Sources, rejected drafts and complete export checks: tools/tactics-assets/imagegen/oath-and-shadow-2026-11-07/.'
    }
manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
print('Registered shared Halvard / Black Knight and Drow / Ysolde; all prior entries and rules retained.')
