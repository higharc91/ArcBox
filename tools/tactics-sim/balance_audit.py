#!/usr/bin/env python3
"""Acceptance gate for #140. Read complete simulator exports, never rounded report percentages."""
import argparse
import gzip
import json
from collections import defaultdict
from pathlib import Path

MOBS = ('mob_pine_wolves', 'mob_river_slimes', 'mob_road_bandits', 'mob_rooting_boars',
        'mob_hollow_bats', 'mob_hollow_kobolds', 'mob_kobold_scouts')
HOLLOW = ('hollow_throat', 'hollow_nest', 'hollow_diggings', 'hollow_lair')


def audit(battles, seed_min=1, seed_max=20):
    cells = defaultdict(list)
    failures = []
    identities = set()
    for battle in battles:
        if not seed_min <= battle['Seed'] <= seed_max:
            continue
        encounter, level, size = battle['Encounter'], battle['Level'], len(battle['Classes'])
        key = (encounter, level, size)
        identity = (*key, tuple(battle['Classes']), battle['Seed'])
        if identity in identities:
            failures.append(f'Duplicate battle {identity}')
        identities.add(identity)
        cells[key].append(battle)
    expected = {(e, 1, s) for e in MOBS for s in (5, 6)}
    expected |= {(e, 2, s) for e in HOLLOW for s in (5, 6)}
    expected |= {('hollow_lair', 2, s) for s in (3, 4)}
    rows = []
    for key in sorted(expected):
        values = cells[key]
        if not values:
            failures.append(f'Missing cell {key}')
            continue
        by_party = defaultdict(set)
        for battle in values:
            by_party[tuple(battle['Classes'])].add(battle['Seed'])
        if len(by_party) < 6 or any(seeds != set(range(seed_min, seed_max + 1)) for seeds in by_party.values()):
            failures.append(f'Incomplete party/seed coverage {key}')
        win = sum(b['Outcome'] == 'Won' for b in values) / len(values)
        rounds = sum(b['Rounds'] for b in values) / len(values)
        mob = key[0] in MOBS
        low, high = (0.85, 0.95) if mob else (0.60, 0.95)
        round_low, round_high = (4, 8) if mob else (8, 12)
        # Sizes 3/4 of Ashmaw are measured for the adjacent-size guard; the owner's existing 4+ profile is retained.
        if key[2] >= 5:
            if not low <= win <= high:
                failures.append(f'Win rate outside {low:.0%}–{high:.0%}: {key}: {win:.6f}')
            if not round_low <= rounds <= round_high:
                failures.append(f'Mean rounds outside {round_low}–{round_high}: {key}: {rounds:.6f}')
        rows.append(dict(encounter=key[0], level=key[1], size=key[2], battles=len(values), winRate=win, meanRounds=rounds))
    boss = {r['size']: r['winRate'] for r in rows if r['encounter'] == 'hollow_lair'}
    for size in (3, 4, 5):
        if size in boss and size + 1 in boss and abs(boss[size] - boss[size + 1]) > 0.20 + 1e-12:
            failures.append(f'Ashmaw adjacent win-rate cliff: {size}→{size + 1}')
    return dict(seedMin=seed_min, seedMax=seed_max, rows=rows, failures=failures, passed=not failures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--samples', action='append', required=True, type=Path)
    parser.add_argument('--seed-min', type=int, default=1)
    parser.add_argument('--seed-max', type=int, default=20)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    battles = []
    for path in args.samples:
        with (gzip.open(path, 'rt') if path.suffix == '.gz' else path.open()) as stream:
            battles.extend(json.load(stream)['Battles'])
    result = audit(battles, args.seed_min, args.seed_max)
    args.out.write_text(json.dumps(result, indent=2) + '\n')
    lines = ['# Party-size acceptance audit', '', '| Encounter | Level | Size | Battles | Wins | Mean rounds |',
             '|---|---:|---:|---:|---:|---:|']
    for row in result['rows']:
        lines.append(f"| {row['encounter']} | {row['level']} | {row['size']} | {row['battles']} | {row['winRate']:.1%} | {row['meanRounds']:.3f} |")
    lines += ['', 'PASS' if result['passed'] else 'FAIL', *result['failures'], '']
    args.out.with_suffix('.md').write_text('\n'.join(lines))
    print(f"{'PASS' if result['passed'] else 'FAIL'}: {len(result['rows'])} cells, {len(result['failures'])} failures")
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
