"""Tests for check.py against the fixture packs: python3 -m unittest discover tools/tactics-lore"""

import json
import tempfile
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

    @staticmethod
    def _content_with_rules(tmp: str, encounter: dict) -> Path:
        """A content folder with the real art sets and a one-of-each rules pack."""
        content = Path(tmp)
        for art_set in ("chapters", "characters", "icons", "portraits"):
            (content / art_set).symlink_to(check.CONTENT / art_set)
        rules = content / "rules"
        for folder in ("monsters", "encounters"):
            (rules / folder).mkdir(parents=True)
        files = ["backgrounds.json", "monsters/m.json", "encounters/e.json"]
        (rules / "manifest.json").write_text(json.dumps({"files": files}))
        (rules / "backgrounds.json").write_text(json.dumps([{"id": "acolyte"}]))
        (rules / "monsters" / "m.json").write_text(json.dumps([{"id": "goblin"}]))
        (rules / "encounters" / "e.json").write_text(json.dumps(encounter))
        return content

    def test_bestiary_and_encounters_are_checked_against_a_rules_pack(self):
        with tempfile.TemporaryDirectory() as tmp:
            errors = check.check([FIXTURES / "valid" / "base"], self._content_with_rules(tmp, {"id": "river_crossing"}))
        self.assertEqual(sorted(e.split("#")[1] for e in errors),
                         ["/entries/1/subject: the rules pack has no monster 'wolf'",
                          "/regions/0/encounters/0: the rules pack has no encounter 'forest_ruins'"])

    def test_once_there_are_arcs_every_awaited_flag_must_be_set(self):
        errors = check.check([FIXTURES / "valid" / "base", FIXTURES / "story"])
        self.assertEqual(sorted(e.split("#")[1] for e in errors), [
            "/arcs/0/beats/1/place: no place has the id 'nowhere'",
            "/arcs/0/beats/1/requires/0: nothing sets the story flag 'never_set' (no beat, choice, ending or encounter)",
            "/arcs/0/chapter: the region 'mirewood' is chapter 2",
            "/characters/0/secret/unlock/flag: nothing sets the story flag 'aldric_secret' (no beat, choice, ending or "
            "encounter)",
        ])

    def test_an_encounter_trigger_counts_as_setting_a_flag(self):
        encounter = {"id": "forest_ruins", "triggers": [{"do": [{"setFlag": "aldric_secret"}, {"setFlag": "never_set"}]}]}
        with tempfile.TemporaryDirectory() as tmp:
            errors = check.check([FIXTURES / "valid" / "base", FIXTURES / "story"], self._content_with_rules(tmp, encounter))
        self.assertFalse([e for e in errors if "story flag" in e], errors)
        self.assertIn("story/arcs.json#/arcs/0/chapter: the region 'mirewood' is chapter 2", errors)

    def test_art_keys_follow_the_rules_engine_harvest(self):
        keys = check.art_keys(check.CONTENT)
        self.assertIn("chapters.mirewood", keys)
        self.assertIn("characters.aldric", keys)
        self.assertNotIn("rules.manifest", keys)


if __name__ == "__main__":
    unittest.main()
