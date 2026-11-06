"""Install only the explicitly approved bounty art, using the exact reviewed normalization."""
import importlib.util
import json
import shutil
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
BASE = ROOT / 'content/tactics/base'


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def install():
    approval = json.loads((ROOT / 'tools/tactics-assets/concepts/ashmaw/approvals.json').read_text())
    if approval.get('inGameMotionReview', {}).get('status') != 'approved':
        raise ValueError('Issue #74 requires owner approval of actual TV world/battle motion before installation.')
    spec = importlib.util.spec_from_file_location('bounty_preview', HERE / 'build-preview.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    with tempfile.TemporaryDirectory(prefix='ashmaw-approved-') as temp:
        preview = Path(temp)
        module.build(preview)
        sprites = json.loads((preview / 'creatures/manifest.json').read_text())['sprites']
        manifest = json.loads((BASE / 'creatures/manifest.json').read_text())
        for key, filename in [('ashmaw', 'ashmaw_bounty'), ('kobold_foreman', 'grisk_bounty')]:
            entry = sprites['review_' + key]
            shutil.copy2(preview / 'creatures' / entry['file'], BASE / 'creatures' / (filename + '.png'))
            entry.update(file=filename + '.png', variant='bounty',
                         provenance='Owner-approved concepts, pixel direction and actual TV world/battle motion, 2026-11-03. Curated paid sources under tools/tactics-assets/pixellab/ashmaw.')
            manifest['sprites'][filename] = entry
        write(BASE / 'creatures/manifest.json', manifest)
        portraits = json.loads((BASE / 'portraits/manifest.json').read_text())
        for name, aliases in [('ashmaw', ['ashmaw']), ('grisk', ['grisk', 'kobold_foreman'])]:
            shutil.copy2(preview / 'portraits' / (name + '_review.png'), BASE / 'portraits' / (name + '.png'))
            for alias in aliases:
                portraits['presets'][alias] = {'idle': name + '.png'}
        write(BASE / 'portraits/manifest.json', portraits)
        characters = json.loads((BASE / 'lore/characters.json').read_text())
        for character in characters['characters']:
            if character['id'] == 'grisk':
                character.update(sprite='monsters.kobold_foreman', portrait='grisk')
        write(BASE / 'lore/characters.json', characters)
        aliases = json.loads((BASE / 'monsters/manifest.json').read_text())
        aliases['about'] = 'Owner-approved named creature art is registered by creatures/manifest.json. These aliases are legacy fallbacks only; production resolves the dedicated atlases first.'
        write(BASE / 'monsters/manifest.json', aliases)
    print('Installed approved bounty atlases and portraits.')


if __name__ == '__main__':
    install()
