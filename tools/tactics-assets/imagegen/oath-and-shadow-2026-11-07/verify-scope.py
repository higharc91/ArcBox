"""Record source/runtime hashes and protect previously integrated art and the review baseline."""
from pathlib import Path
import hashlib, json, subprocess

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[3]
OUT = REPO / 'docs/art/mob-sprite-review/oath-and-shadow/checks'
OUT.mkdir(parents=True, exist_ok=True)
PRIOR = '481f2410'

def prior(file):
    return json.loads(subprocess.check_output(['git', 'show', f'{PRIOR}:{file}'], cwd=REPO))

def current(file):
    return json.loads((REPO / file).read_text())

def save(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n')

creatures = 'content/tactics/base/creatures/manifest.json'
old, new = prior(creatures), current(creatures)
assert {k: v for k, v in new.items() if k != 'sprites'} == {k: v for k, v in old.items() if k != 'sprites'}
assert all(new['sprites'][k] == v for k, v in old['sprites'].items())
added = sorted(set(new['sprites']) - set(old['sprites']))
assert added == ['mob_refresh_black_knight', 'mob_refresh_dark_elf'], added
portraits = 'content/tactics/base/portraits/manifest.json'
a, b = prior(portraits), current(portraits)
# Work with the existing manifest structure, finding the exact changed leaf paths.
def changed_leaves(a, b, prefix=''):
    if isinstance(a, dict) and isinstance(b, dict):
        assert set(a) == set(b)
        return sum((changed_leaves(a[k], b[k], prefix + '/' + k) for k in a), [])
    return [] if a == b else [prefix]
leaves = changed_leaves(a, b)
assert len(leaves) == 12, leaves
assert all(any('/' + preset + '/' in leaf for preset in ['black_knight', 'dark_elf']) for leaf in leaves), leaves
changed = subprocess.check_output(['git', 'diff', '--name-only', PRIOR], cwd=REPO, text=True).splitlines()
assert not any(p.startswith('content/tactics/base/rules/') or p.startswith('content/tactics/base/lore/') for p in changed)
allowed_art = {creatures, portraits}
for mob in ['black_knight', 'dark_elf']:
    allowed_art.add(f'content/tactics/base/creatures/mob_refresh_{mob}.png')
    allowed_art.update(f'content/tactics/base/portraits/{mob}_refresh_{mood}.png' for mood in ['idle', 'hurt', 'happy', 'angry', 'sad', 'surprised'])
unexpected_art = [p for p in changed if p.startswith(('content/tactics/base/creatures/', 'content/tactics/base/portraits/')) and p not in allowed_art]
assert not unexpected_art, unexpected_art
protected = ['docs/art/mob-sprite-review/all-mobs.png', 'docs/art/mob-sprite-review/inventory.json', 'docs/art/mob-sprite-review/cards.json']
assert all((REPO / p).read_bytes() == subprocess.check_output(['git', 'show', f'{PRIOR}:{p}'], cwd=REPO) for p in protected)
source_files = sorted((ROOT / 'source').glob('*.png'))
save(ROOT / 'source-sha256.json', {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source_files})
assets = sorted([*(REPO / 'content/tactics/base/creatures').glob('mob_refresh_black_knight.png'), *(REPO / 'content/tactics/base/creatures').glob('mob_refresh_dark_elf.png'), *(REPO / 'content/tactics/base/portraits').glob('black_knight_refresh_*.png'), *(REPO / 'content/tactics/base/portraits').glob('dark_elf_refresh_*.png')])
assert len(assets) == 14
save(OUT / 'runtime-assets.json', {str(p.relative_to(REPO)): hashlib.sha256(p.read_bytes()).hexdigest() for p in assets})
packing = current(str((ROOT / 'packing.json').relative_to(REPO)))
assert sum(len(p['frames']) for p in packing) == 272
assert all('rejected' not in f['source'] for p in packing for f in p['frames'])
save(OUT / 'scope.json', {'priorCommit': subprocess.check_output(['git', 'rev-parse', PRIOR], cwd=REPO, text=True).strip(), 'addedCreatureEntries': added, 'changedPortraitPresets': ['black_knight', 'dark_elf'], 'priorCreatureEntriesUnchanged': True, 'unrelatedPortraitsUnchanged': True, 'historicalBaselineUnchanged': True, 'runtimeAssets': len(assets), 'sourceImages': len(source_files), 'selectedDistinctPoses': 272, 'rejectedSourcesExcluded': True})
print('Verified two creature overrides, two portrait presets, fourteen runtime assets, 272 selected poses and unchanged prior art/rules/baseline.')
