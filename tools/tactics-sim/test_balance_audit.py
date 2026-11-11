import unittest
from balance_audit import MOBS, HOLLOW, audit


def complete():
    rows = []
    keys = [(e, 1, s) for e in MOBS for s in (5, 6)]
    keys += [(e, 2, s) for e in HOLLOW for s in (5, 6)]
    keys += [('hollow_lair', 2, s) for s in (3, 4)]
    for encounter, level, size in keys:
        for party in range(6):
            for seed in range(1, 21):
                rows.append(dict(Encounter=encounter, Level=level, Classes=[f'{party}-{i}' for i in range(size)],
                                 Seed=seed, Outcome='Won' if seed <= 18 else 'Lost', Rounds=6 if level == 1 else 10))
    return rows


class BalanceAuditTests(unittest.TestCase):
    def test_complete_schedule_passes(self):
        self.assertTrue(audit(complete())['passed'])

    def test_missing_duplicate_and_incomplete_rows_fail(self):
        rows = complete()
        self.assertFalse(audit(rows[120:])['passed'])
        self.assertFalse(audit(rows + [rows[0]])['passed'])
        self.assertFalse(audit(rows[1:])['passed'])

    def test_rounds_and_win_rates_are_checked_per_size(self):
        rows = complete()
        for row in rows[:120]:
            row['Outcome'] = 'Won'
        self.assertFalse(audit(rows)['passed'])
        rows = complete()
        for row in rows[:120]:
            row['Rounds'] = 9
        self.assertFalse(audit(rows)['passed'])

    def test_adjacent_boss_cliff_is_rejected(self):
        rows = complete()
        for row in rows:
            if row['Encounter'] == 'hollow_lair' and len(row['Classes']) == 3:
                row['Outcome'] = 'Lost'
        self.assertIn('Ashmaw adjacent win-rate cliff: 3→4', audit(rows)['failures'])

    def test_holdout_rejects_missing_seeds(self):
        self.assertFalse(audit(complete(), 21, 40)['passed'])


if __name__ == '__main__':
    unittest.main()
