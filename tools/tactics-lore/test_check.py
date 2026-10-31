"""Tests for check.py against the fixture packs: python3 -m unittest discover tools/tactics-lore"""

import unittest
from pathlib import Path

import check

FIXTURES = Path(__file__).parent / "fixtures"


class CheckTests(unittest.TestCase):
    def test_base_lore_pack_is_valid(self):
        self.assertEqual(check.check([check.CONTENT / "lore"]), [])

    def test_every_file_kind_and_a_chapter_replace_pass(self):
        self.assertEqual(check.check([FIXTURES / "valid" / "base", FIXTURES / "valid" / "chapter-02"]), [])

    def test_a_chapter_without_its_base_is_refused(self):
        errors = check.check([FIXTURES / "valid" / "chapter-02"])
        self.assertIn("chapter-02/pack.json#/requires/0: 'base' must be loaded before this pack", errors)
        self.assertIn("chapter_02/regions.json#/regions/0/replaces: no earlier pack defines the region 'mirewood'", errors)

    def test_each_kind_of_mistake_is_reported_once(self):
        errors = check.check([FIXTURES / "invalid" / "base"])
        expected = [
            "characters/3/id: the character 'tamsin' is already defined",
            "characters/2/relationships/0/with: no character has the id 'missing_friend'",
            "characters/1/id: the art key 'characters.nobody' isn't in any art manifest",
            "factions/0: 'colour' isn't a known key",
            "line id 'dup_line' is already used",
            "events/sneeze: \"sneeze\" isn't one of",
            "sneeze/0/mood: \"bored\" isn't one of",
            "the file 'codex/missing.json' doesn't exist",
            "speaker: no character has the id 'ghost'",
        ]
        for fragment in expected:
            self.assertEqual(sum(fragment in e for e in errors), 1, f"{fragment!r} in {errors}")
        self.assertEqual(len(errors), len(expected), errors)

    def test_art_keys_follow_the_rules_engine_harvest(self):
        keys = check.art_keys(check.CONTENT)
        self.assertIn("chapters.mirewood", keys)
        self.assertIn("characters.aldric", keys)
        self.assertNotIn("rules.manifest", keys)


if __name__ == "__main__":
    unittest.main()
