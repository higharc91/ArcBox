"""Register only the oak Mimic's complete figure, HUD crop and raised-jaw clearance."""
from pathlib import Path
import json, math, shutil

ROOT = Path(__file__).resolve().parent
BASE = ROOT.parents[3] / 'content/tactics/base'
path = BASE / 'creatures/manifest.json'
manifest = json.loads(path.read_text())
mob, = json.loads((ROOT / 'packing.json').read_text())
shutil.copyfile(ROOT / 'export/mimic/atlas.png', BASE / 'creatures/mob_refresh_mimic.png')
idle = [f for f in mob['frames'] if f['action'] == 'idle']
head_top = round(min(mob['anchor'][1] + (f['head'][1] - f['foot'][1]) * f['scale'] for f in idle))
checks = json.loads((ROOT / 'export/mimic/checks.json').read_text())
bounds = [f['bounds'] for f in checks if f['action'] == 'idle']
front = bounds[0]
# This creature has no head: the HUD shows its complete front-facing chest.
portrait_size = math.ceil(max(front[2] - front[0] + 1, front[3] - front[1] + 1) + 8)
cx, cy = (front[0] + front[2] + 1) / 2, (front[1] + front[3] + 1) / 2
portrait = [max(0, min(mob['size'] - portrait_size, round(cx - portrait_size / 2))),
            max(0, min(mob['size'] - portrait_size, round(cy - portrait_size / 2))), portrait_size, portrait_size]
top = min(f['bounds'][1] for f in checks if f['action'] == 'attack')
clearance = {'attack': round((head_top - top) * mob['pixelScale'] + 2, 2)} if top < head_top else {}
manifest['sprites']['mob_refresh_mimic'] = {
    'file': 'mob_refresh_mimic.png', 'family': 'mimic', 'variant': 'oak-ambush',
    'frame': {'width': mob['size'], 'height': mob['size'], 'anchor': mob['anchor']},
    'portrait': portrait, 'headTop': head_top, 'overheadClearance': clearance,
    'pixelScale': mob['pixelScale'], 'columns': 8, 'rowsPerDirection': 2,
    'directions': ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east'],
    'facingOffsets': {v: [0, 0] for v in ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east']},
    'footprintWidth': round(max(b[2] - b[0] + 1 for b in bounds) * mob['pixelScale'], 2),
    'footprintHeight': round(max(b[3] - b[1] + 1 for b in bounds) * mob['pixelScale'], 2),
    'keys': ['bosses.mimic.oak'],
    'anims': {
        'idle': {'frames': [0, 1, 2, 3], 'fps': 3, 'loop': True},
        'walk': {'frames': [4, 5, 6, 7], 'fps': 6, 'loop': True},
        'attack': {'frames': [8, 9, 9, 9, 10, 10, 11, 0], 'fps': 10, 'loop': False},
        'hurt': {'frames': [12], 'fps': 8, 'loop': False},
        'down': {'frames': [13], 'fps': 1, 'loop': False},
    },
    'provenance': 'Scenario oak Mimic concept; OpenAI complete pixel poses. Sources and export checks: tools/tactics-assets/imagegen/oak-ambush-2026-11-07/.',
}
path.write_text(json.dumps(manifest, indent=2) + '\n')
print('Registered oak Mimic: 112 poses; whole-chest HUD crop', portrait, '; raised-jaw clearance', clearance)
