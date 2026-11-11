import base64
import json
from pathlib import Path
import tempfile
import unittest
from environment_audit import audit


class EnvironmentAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'maps').mkdir()
        (self.root / 'encounters').mkdir()
        (self.root / 'environment.json').write_text(json.dumps({'fuel': ['grass/dry']}))
        (self.root / 'maps/test.json').write_text(json.dumps({'id': 'test', 'size': [5, 3], 'materials': ['grass/dry'],
            'tiles': base64.b64encode(bytes([0, 0, 0]) * 15).decode()}))
        self.encounter = {'id': 'test', 'map': 'test', 'level': 1, 'environment': True}
        self.policy = {'exceptions': {}, 'lava': {}}
        self.sample = {'Config': {'Encounters': ['test'], 'Levels': [1], 'PartySizes': [4], 'PartiesPerSize': 1, 'Seeds': 1},
            'Battles': [{'Encounter': 'test', 'Level': 1, 'Classes': ['fighter', 'cleric', 'rogue', 'wizard'], 'Seed': 1,
                'Environmental': {'Reactions': 2}, 'Outcome': 'Won', 'Rounds': 5, 'Capped': False, 'Rejected': 0, 'ForcedEnds': 0}]}

    def run_audit(self, samples=None):
        (self.root / 'encounters/test.json').write_text(json.dumps(self.encounter))
        return audit(self.root, self.policy, [self.sample] if samples is None else samples)

    def test_complete_productive_sample_passes(self):
        result = self.run_audit()
        self.assertTrue(result['passed'], result['failures'])
        self.assertEqual(2, result['rows'][0]['reactionsPerBattle'])

    def test_reactive_terrain_cannot_be_silently_disabled(self):
        self.encounter['environment'] = False
        self.assertIn('Reactive terrain disabled without a reason: test', self.run_audit()['failures'])

    def test_stale_exception_cannot_hide_new_terrain(self):
        self.encounter['environment'] = False
        self.policy['exceptions']['test'] = {'fuel': 0, 'shallows': 0, 'reason': 'This is a sufficiently long explanation for an old zero-fuel map.'}
        self.assertIn('Stale exception: test', self.run_audit()['failures'])

    def test_empty_or_low_reaction_samples_fail(self):
        self.assertIn('Missing reaction measurements: test', self.run_audit([])['failures'])
        self.sample['Battles'][0]['Environmental']['Reactions'] = 0
        self.assertTrue(any('Unproductive opt-in' in f for f in self.run_audit()['failures']))

    def test_incomplete_and_duplicate_battles_cannot_pass(self):
        self.sample['Config']['Seeds'] = 2
        self.assertTrue(any('Incomplete sample' in f for f in self.run_audit()['failures']))
        self.sample['Battles'] *= 2
        self.assertTrue(any('Duplicate battle' in f for f in self.run_audit()['failures']))
