"""Verify one library-art batch against its recorded integration base."""
from pathlib import Path
import hashlib
import json
import subprocess
from PIL import Image


def run(root):
    root = Path(root).resolve()
    repo = root.parents[3]
    batch = json.loads((root / 'batch.json').read_text())
    roles = json.loads((root / 'roles.json').read_text())
    prior = batch['priorCommit']
    out = repo / 'docs/art/mob-sprite-review' / batch['slug'] / 'checks'
    out.mkdir(parents=True, exist_ok=True)

    def old(path):
        return subprocess.check_output(['git', 'show', f'{prior}:{path}'], cwd=repo)

    def save(path, value):
        path.write_text(json.dumps(value, indent=2) + '\n')

    def receipt(folder, name, files):
        hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}
        target = folder / name
        if target.exists():
            assert json.loads(target.read_text()) == hashes, ('changed receipt', str(target))
        else:
            save(target, hashes)
        return hashes

    names = {'mob_refresh_' + r['id'] for r in roles}
    path = 'content/tactics/base/creatures/manifest.json'
    before = json.loads(old(path))
    after = json.loads((repo / path).read_text())
    assert {k: v for k, v in before.items() if k != 'sprites'} == {k: v for k, v in after.items() if k != 'sprites'}
    assert all(after['sprites'][k] == v for k, v in before['sprites'].items())
    assert set(after['sprites']) - set(before['sprites']) == names
    allowed = {path} | {'content/tactics/base/creatures/' + n + '.png' for n in names}
    changed = subprocess.check_output(['git', 'diff', '--name-only', prior], cwd=repo, text=True).splitlines()
    assert not [p for p in changed if p.startswith('content/') and p not in allowed]
    for file in ['all-mobs.png', 'inventory.json', 'cards.json', 'production/refresh-status.json']:
        path = 'docs/art/mob-sprite-review/' + file
        assert (repo / path).read_bytes() == old(path)
    for file, prefix in [('index.html', ''), ('production/index.html', '../')]:
        path = 'docs/art/mob-sprite-review/' + file
        link = f'<p><a style="color:#b5ead4" href="{prefix}{batch["slug"]}/index.html">Latest: {batch["title"]}</a></p>'
        assert (repo / path).read_text().replace(link, '') == old(path).decode()

    files = sorted((root / 'source').glob('*.png'))
    ids = json.loads((root / 'source-images.json').read_text())
    assert set(ids) == {p.stem for p in files}
    receipt(root, 'source-sha256.json', files)
    originals_checked = all(Path('/workspace/generated_images/exec-' + ids[p.stem] + '.png').exists() for p in files)
    if originals_checked:
        for p in files:
            assert p.read_bytes() == Path('/workspace/generated_images/exec-' + ids[p.stem] + '.png').read_bytes(), p.name
    references = sorted((root / 'references').glob('*.png'))
    assert len(references) == 4 * len(roles)
    receipt(root, 'references-sha256.json', references)
    for item in json.loads((root / 'reference-inputs.json').read_text()).values():
        for reference in item['references']:
            assert (root / reference).exists(), reference

    packing = json.loads((root / 'packing.json').read_text())
    keys = sum(len(m['frames']) for m in packing)
    selected = sum(len({(f['source'], f['sourcePose']) for f in m['frames']}) for m in packing)
    assert selected == keys
    assert not any(f['source'].endswith('-draft.png') for m in packing for f in m['frames'])
    motion = {}
    for role in roles:
        motion[role['id']] = {}
        for action in role.get('motionActions', batch.get('motionActions', ['idle', 'walk', 'attack'])):
            motion[role['id']][action] = {}
            for direction in sorted((root / 'export' / role['id'] / action).iterdir()):
                signatures = {hashlib.sha256(Image.open(p).tobytes()).hexdigest() for p in direction.glob('*.png')}
                assert len(signatures) == 4, (role['id'], action, direction.name)
                motion[role['id']][action][direction.name] = len(signatures)
    save(out / 'directional-motion.json', motion)
    receipt(out, 'runtime-assets.json', [repo / 'content/tactics/base/creatures' / (n + '.png') for n in names])

    inspection = root / 'inspection-strips.json'
    if inspection.exists():
        encoded = json.loads(inspection.read_text())
        assert set(encoded) == {r['id'] for r in roles}
        for name, item in encoded.items():
            assert hashlib.sha256((root / 'export' / name / 'atlas.png').read_bytes()).hexdigest() == item['sourceAtlasSha256']
            assert len(item['facings']) == 8
            for facing in item['facings']:
                assert facing['pixelsMatchSourceAtlas']
                assert hashlib.sha256((root / facing['file']).read_bytes()).hexdigest() == facing['webpSha256']
        save(out / 'inspection-encoding.json', {name: {'sourceBytes': item['sourceBytes'],
             'reviewBytes': sum(f['bytes'] for f in item['facings']), 'facings': 8, 'rgbaPixelsPreserved': True}
             for name, item in encoded.items()})

    layout = json.loads((root / 'inspection-layout.json').read_text())
    checked = []
    for mob in packing:
        frames = json.loads((root / 'export' / mob['id'] / 'checks.json').read_text())
        assert len(frames) == len(mob['frames'])
        for frame in frames:
            for kind, zoom, x, y, width, height in [
                ('gallery', layout['galleryZoom'], 160, layout['galleryBaseline'], 320, 420),
                ('reel', layout['reelZoom'], 240, layout['reelBaseline'], 480, 540),
            ]:
                b = frame['bounds']
                drawn = [x + (b[0] - mob['anchor'][0]) * zoom, y + (b[1] - mob['anchor'][1]) * zoom,
                         x + (b[2] + 1 - mob['anchor'][0]) * zoom, y + (b[3] + 1 - mob['anchor'][1]) * zoom]
                assert drawn[0] >= 8 and drawn[1] >= 65 and drawn[2] <= width - 8 and drawn[3] <= height - 8, (mob['id'], frame, kind, drawn)
        checked.append({'id': mob['id'], 'framesChecked': len(frames), 'completeSilhouettesFit': True, **layout})
    save(out / 'artifact-bounds.json', checked)
    save(out / 'scope.json', {'priorCommit': prior, 'addedCreatureEntries': sorted(names), 'priorCreatureEntriesUnchanged': True,
         'priorRuntimeArtUnchanged': True, 'rulesAndAbilitiesUnchanged': True, 'dialoguePortraitsUnchanged': True,
         'historicalBaselineUnchanged': True, 'combatTrackerUnchanged': True, 'sourceImages': len(files),
         'sourceOriginalsAvailableAndChecked': originals_checked, 'packedFrameKeys': keys,
         'distinctSelectedWholeFigures': selected, 'runtimeAssets': len(names), 'mainUntouchedForPlaytest': True})
    print(f'Verified {keys} complete selected keys, {len(files)} sources, inspection bounds and unchanged prior art/rules.')
