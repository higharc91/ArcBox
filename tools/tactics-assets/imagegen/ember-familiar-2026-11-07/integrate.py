"""Register only the Bound Imp's complete figure and head HUD crop."""
from pathlib import Path
import json, shutil

ROOT = Path(__file__).resolve().parent
BASE = ROOT.parents[3] / 'content/tactics/base'
path = BASE / 'creatures/manifest.json'
manifest = json.loads(path.read_text())
mob, = json.loads((ROOT / 'packing.json').read_text())
shutil.copyfile(ROOT / 'export/imp/atlas.png', BASE / 'creatures/mob_refresh_imp.png')
idle = [f for f in mob['frames'] if f['action'] == 'idle']
head_top = round(min(mob['anchor'][1] + (f['head'][1] - f['foot'][1]) * f['scale'] for f in idle))
checks = json.loads((ROOT / 'export/imp/checks.json').read_text())
bounds = [f['bounds'] for f in checks if f['action'] == 'idle']
front = bounds[0]
# Front-facing head crop includes the ivory horns, pointed ears and face.
front_pose = next(f for f in mob['frames'] if f['action'] == 'idle' and f['row'] == 0)
head_x = mob['anchor'][0] + (front_pose['head'][0] - front_pose['foot'][0]) * front_pose['scale']
head_y = mob['anchor'][1] + (front_pose['head'][1] - front_pose['foot'][1]) * front_pose['scale']
portrait_size = 40
portrait = [round(head_x - portrait_size / 2), round(head_y - 3), portrait_size, portrait_size]
manifest['sprites']['mob_refresh_imp'] = {
    'file': 'mob_refresh_imp.png', 'family': 'imp', 'variant': 'ember-familiar',
    'frame': {'width': mob['size'], 'height': mob['size'], 'anchor': mob['anchor']},
    'portrait': portrait, 'headTop': head_top,
    'pixelScale': mob['pixelScale'], 'columns': 8, 'rowsPerDirection': 2,
    'directions': ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east'],
    'facingOffsets': {v: [0, 0] for v in ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east']},
    'footprintWidth': round(max(b[2] - b[0] + 1 for b in bounds) * mob['pixelScale'], 2),
    'footprintHeight': round(max(b[3] - b[1] + 1 for b in bounds) * mob['pixelScale'], 2),
    'keys': ['bosses.imp.crimson'],
    'anims': {
        'idle': {'frames': [0, 1, 2, 3], 'fps': 3, 'loop': True},
        'walk': {'frames': [4, 5, 6, 7], 'fps': 6, 'loop': True},
        'attack': {'frames': [8, 9, 9, 9, 10, 10, 11, 0], 'fps': 10, 'loop': False},
        'hurt': {'frames': [12], 'fps': 8, 'loop': False},
        'down': {'frames': [13], 'fps': 1, 'loop': False},
    },
    'provenance': 'Scenario Bound Imp concept; OpenAI complete pixel poses. Sources and export checks: tools/tactics-assets/imagegen/ember-familiar-2026-11-07/.',
}
path.write_text(json.dumps(manifest, indent=2) + '\n')
print('Registered Bound Imp: 112 poses; head HUD crop', portrait)
