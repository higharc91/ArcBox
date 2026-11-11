"""Prove which measured cells remain identical after the three validation corrections.

Run from the repository root. Source commit stores the original measured mob profiles;
Every selected enemy, effective party-size override and map is compared; all other rules must be unchanged.
"""
import hashlib
import json
import subprocess
from pathlib import Path

SOURCE = '8d716ae50f98d954775ebb5a8f2e561139d90d29'
RULES = 'content/tactics/base/rules/'
MOBS = ('mob_pine_wolves', 'mob_river_slimes', 'mob_road_bandits', 'mob_rooting_boars',
        'mob_hollow_bats', 'mob_hollow_kobolds', 'mob_kobold_scouts')


def read(path, source):
    return json.loads(subprocess.check_output(['git', 'show', f'{SOURCE}:{path}']) if source else Path(path).read_bytes())


def monsters(source):
    files = (subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', SOURCE, RULES + 'monsters']).decode().splitlines()
             if source else [str(p) for p in Path(RULES + 'monsters').glob('*.json')])
    return {m['id']: m for path in files if path.endswith('.json') for m in read(path, source)}


def fingerprint(encounter, size, source, catalog):
    data = read(RULES + f'encounters/{encounter}.json', source)
    enemies = []
    for original in data['enemies']:
        enemy = dict(original)
        enemy['stableUnitTag'] = original.get('tag', original['unit'])
        enemy['unit'] = enemy.pop('unitByParty', {}).get(str(size), original['unit'])
        enemy['count'] = enemy.pop('countByParty', {}).get(str(size), original.get('count', 1))
        enemy['hp'] = enemy.pop('hpByParty', {}).get(str(size))
        enemy['definition'] = catalog[enemy['unit']]
        enemies.append(enemy)
    data['enemies'] = enemies
    data['mapData'] = read(RULES + f"maps/{data['map']}.json", source)
    return hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()


old, new = monsters(True), monsters(False)
rows = []
for encounter in (*MOBS, 'hollow_throat', 'hollow_nest', 'hollow_diggings', 'hollow_lair'):
    for size in ((5, 6) if encounter in MOBS else (3, 4, 5, 6)):
        before = fingerprint(encounter, size, True, old)
        after = fingerprint(encounter, size, False, new)
        rows.append(dict(encounter=encounter, size=size, before=before, after=after, unchanged=before == after))
changed = {(r['encounter'], r['size']) for r in rows if not r['unchanged']}
assert changed == {('mob_river_slimes', 5), ('mob_hollow_bats', 5), ('hollow_diggings', 6)}, changed
files = subprocess.check_output(['git', 'diff', '--name-only', SOURCE, '--', RULES]).decode().splitlines()
assert set(files) == {RULES + p for p in ('encounters/mob_river_slimes.json', 'encounters/mob_hollow_bats.json', 'encounters/hollow_diggings.json', 'monsters/party-size.json')}, files
report = dict(sourceCommit=SOURCE, reusedCells=27, rerunCells=3, unchangedOtherRules=True, rows=rows)
Path(__file__).with_name('sample-reuse-proof.json').write_text(json.dumps(report, indent=2) + '\n')
print('PASS: 27 identical encounter/map/enemy cells; three corrected cells require new measurements; all other rules unchanged.')
