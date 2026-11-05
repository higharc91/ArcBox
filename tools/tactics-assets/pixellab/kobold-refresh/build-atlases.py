"""Build kobold atlases from complete, downloaded PixelLab directional states."""
import json
import math
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[4]
BASE = ROOT / 'content/tactics/base/creatures'
SOURCE = Path(__file__).resolve().parent / 'source'
DIRECTIONS = ['south', 'south-west', 'west', 'north-west', 'north', 'north-east', 'east', 'south-east']
ROLES = [('worker', 'characters.kobold', 'dagger_attack'), ('slinger', 'characters.kobold_slinger', 'sling_attack')]
GROUPS = [('idle', 'idle', 4, 3, True), ('walk', 'walk', 6, 7.5, True), ('attack', None, 7, 8, False), ('hurt', 'hurt', 6, 9, False), ('down', 'down', 7, 7, False)]


def build():
    manifest = json.loads((BASE / 'manifest.json').read_text())
    for role, key, attack in ROLES:
        source = SOURCE / role / 'Idle'
        groups = [(a, attack if name is None else name, n, fps, loop) for a, name, n, fps, loop in GROUPS]
        if role == 'slinger': groups.append(('throw', 'firepot', 7, 8, False))
        count = sum(g[2] for g in groups)
        rows = math.ceil(count / 8)
        size, anchor = 128, (64, 110)
        atlas = Image.new('RGBA', (size * 8, size * rows * 8))
        anims, bounds, offset = {}, [], 0
        for action, group, n, fps, loop in groups:
            anims[action] = {'frames': list(range(offset, offset + n)), 'fps': fps, 'loop': loop}
            for index, direction in enumerate(DIRECTIONS):
                rot = Image.open(source / 'rotations' / (direction + '.png')).convert('RGBA')
                floor = rot.getbbox()[3] - 1
                x0, y0 = (size - rot.width) // 2, anchor[1] - floor
                chosen = 'hurt_west_fix' if role == 'slinger' and action == 'hurt' and direction == 'west' else group
                frames = sorted((source / 'animations' / chosen / direction).glob('*.png'))
                if len(frames) != n:
                    raise ValueError(f'{role}/{group}/{direction}: expected {n}, found {len(frames)}')
                for frame, path in enumerate(frames):
                    im = Image.open(path).convert('RGBA')
                    x = x0 - (im.width - rot.width) // 2
                    y = y0 - (im.height - rot.height) // 2
                    box = im.getbbox()
                    if box and not (0 <= box[0] + x < box[2] + x <= size and 0 <= box[1] + y < box[3] + y <= size):
                        raise ValueError(f'Clipped {role}/{group}/{direction}/{frame}')
                    cell = Image.new('RGBA', (size, size)); cell.alpha_composite(im, (x, y))
                    if cell.getbbox(): bounds.append(cell.getbbox())
                    atlas.paste(cell, (((offset + frame) % 8) * size, (index * rows + (offset + frame) // 8) * size))
            offset += n
        # The ranged basic attack asks for the shoot pose.
        if role == 'slinger':
            anims['shoot'] = anims['attack']
            anims['cast'] = anims['throw']
        filename = 'kobold_refresh_' + role + '.png'
        atlas.save(BASE / filename)
        manifest['sprites']['kobold_refresh_' + role] = {'file': filename, 'family': 'kobold', 'variant': role, 'frame': {'width': size, 'height': size, 'anchor': list(anchor)}, 'headTop': min(b[1] for b in bounds), 'pixelScale': 0.32, 'columns': 8, 'rowsPerDirection': rows, 'directions': DIRECTIONS, 'facingOffsets': {d: [0, 0] for d in DIRECTIONS}, 'footprintWidth': 14, 'footprintHeight': 12, 'keys': [key], 'anims': anims, 'provenance': 'PixelLab eight-direction sprites based on matching Grisk concept; source in tools/tactics-assets/pixellab/kobold-refresh/source.'}
    portraits = json.loads((BASE.parent / 'portraits/manifest.json').read_text())
    for role, preset in [('worker', 'kobold'), ('slinger', 'kobold_slinger')]:
        image = Image.open(SOURCE / role / 'Idle/rotations/south.png').convert('RGBA')
        filename = preset + '_refresh.png'
        image.crop((28, 4, 68, 44)).save(BASE.parent / 'portraits' / filename)
        portraits['presets'][preset] = {mood: filename for mood in portraits['presets'][preset]}
    (BASE.parent / 'portraits/manifest.json').write_text(json.dumps(portraits, indent=2) + '\n')
    (BASE / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__': build()
