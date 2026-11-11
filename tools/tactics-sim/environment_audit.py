#!/usr/bin/env python3
"""Audit authored terrain opt-ins and complete, real-engine reaction measurements (#138)."""
import argparse
import base64
import collections
import fnmatch
import gzip
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def terrain_counts(definition, patterns):
    raw = base64.b64decode(definition['tiles'], validate=True)
    if len(raw) != definition['size'][0] * definition['size'][1] * 3:
        raise ValueError(f"Invalid tile bytes: {definition['id']}")
    fuel = shallows = 0
    for i in range(0, len(raw), 3):
        material, flags = definition['materials'][raw[i + 1]], raw[i + 2]
        fuel += not flags & 1 and any(fnmatch.fnmatchcase(material, p) for p in patterns)
        shallows += flags & 17 == 1
    return {'fuel': fuel, 'shallows': shallows}


def audit(rules, policy, samples):
    patterns = json.loads((rules / 'environment.json').read_text())['fuel']
    maps = {p.stem: json.loads(p.read_text()) for p in (rules / 'maps').glob('*.json')}
    encounters = {p.stem: json.loads(p.read_text()) for p in (rules / 'encounters').glob('*.json')}
    observed = collections.defaultdict(list)
    failures = []
    seen = set()
    configurations = []
    for sample in samples:
        config = sample['Config']
        configurations.append(config)
        if config.get('WithClass') or config.get('FixedParty'):
            failures.append('Use the unfiltered sampled-party schedule for this terrain audit')
        expected = (len(config['Encounters']) * len(config['Levels']) * len(config['PartySizes'])
                    * config['PartiesPerSize'] * config['Seeds'])
        if len(sample['Battles']) != expected:
            failures.append(f"Incomplete sample: expected {expected} battles, got {len(sample['Battles'])}")
        cells = collections.Counter()
        for battle in sample['Battles']:
            identity = (battle['Encounter'], battle['Level'], tuple(battle['Classes']), battle['Seed'])
            if identity in seen:
                failures.append(f'Duplicate battle: {identity}')
            seen.add(identity)
            observed[battle['Encounter']].append(battle)
            cells[(battle['Encounter'], battle['Level'], len(battle['Classes']))] += 1
        for encounter in config['Encounters']:
            for level in config['Levels']:
                for size in config['PartySizes']:
                    if cells[(encounter, level, size)] != config['PartiesPerSize'] * config['Seeds']:
                        failures.append(f'Incomplete cell: {encounter}, L{level}, size {size}')
    rows = []
    exceptions = policy['exceptions']
    for name, encounter in sorted(encounters.items()):
        counts = terrain_counts(maps[encounter['map']], patterns)
        enabled = encounter.get('environment', False)
        exception = exceptions.get(name)
        if exception:
            if enabled or any(counts[k] != exception[k] for k in counts) or len(exception['reason']) < 40:
                failures.append(f'Stale exception: {name}')
        if (counts['fuel'] >= 10 or counts['shallows'] >= 10) and not enabled and not exception:
            failures.append(f'Reactive terrain disabled without a reason: {name}')
        battles = observed[name]
        mean = None
        if enabled:
            if not battles or any(b['Environmental'] is None for b in battles):
                failures.append(f'Missing reaction measurements: {name}')
            else:
                mean = sum(b['Environmental']['Reactions'] for b in battles) / len(battles)
                if mean < 1:
                    failures.append(f'Unproductive opt-in: {name} averages {mean:.6f} reactions/battle')
                if any(b['Level'] != encounter['level'] for b in battles):
                    failures.append(f'Measure reactions at the intended level: {name}')
        row = {'encounter': name, 'map': encounter['map'], 'level': encounter['level'], **counts,
               'enabled': enabled, 'reason': exception['reason'] if exception else
               ('Measured reactive combat terrain' if enabled else 'No substantial native fuel or shallow water'),
               'battles': len(battles), 'reactionsPerBattle': mean}
        if battles:
            row.update(winRate=sum(b['Outcome'] == 'Won' for b in battles) / len(battles),
                       meanRounds=sum(b['Rounds'] for b in battles) / len(battles),
                       capped=sum(b['Capped'] for b in battles),
                       rejected=sum(b['Rejected'] for b in battles),
                       forcedEnds=sum(b['ForcedEnds'] for b in battles))
        rows.append(row)
    for name in exceptions:
        if name not in encounters:
            failures.append(f'Unknown exception: {name}')
    for name in observed:
        if name not in encounters:
            failures.append(f'Unknown measured encounter: {name}')
    return {'configurations': configurations, 'rows': rows, 'lava': policy['lava'],
            'totalBattles': len(seen), 'failures': failures, 'passed': not failures}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rules', type=Path, default=ROOT / 'content/tactics/base/rules')
    parser.add_argument('--policy', type=Path, default=Path(__file__).with_name('environment-policy.json'))
    parser.add_argument('--samples', type=Path, action='append', required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    samples = []
    for path in args.samples:
        with (gzip.open(path, 'rt') if path.suffix == '.gz' else path.open()) as stream:
            samples.append(json.load(stream))
    result = audit(args.rules, json.loads(args.policy.read_text()), samples)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + '\n')
    lines = ['# Environmental encounter audit', '', f"Real-engine battles: {result['totalBattles']}; gate: {'PASS' if result['passed'] else 'FAIL'}.", '',
             '| Encounter | Fuel | Shallows | Enabled | Battles | Reactions/battle | Reason |', '|---|---:|---:|---|---:|---:|---|']
    for row in result['rows']:
        mean = '—' if row['reactionsPerBattle'] is None else f"{row['reactionsPerBattle']:.4f}"
        lines.append(f"| {row['encounter']} | {row['fuel']} | {row['shallows']} | {row['enabled']} | {row['battles']} | {mean} | {row['reason']} |")
    lines += ['', '## Lava decision', '', result['lava']['decision'], '', result['lava']['rationale']]
    if result['failures']:
        lines += ['', '## Failures', '', *['- ' + f for f in result['failures']]]
    args.out.with_suffix('.md').write_text('\n'.join(lines) + '\n')
    print(f"{'PASS' if result['passed'] else 'FAIL'}: {result['totalBattles']} battles; {len(result['failures'])} gate failures")
    return int(not result['passed'])


if __name__ == '__main__':
    raise SystemExit(main())
