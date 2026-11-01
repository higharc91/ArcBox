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

    def test_scenes_are_checked_as_graphs_tied_to_their_beats(self):
        errors = check.check([FIXTURES / "valid" / "base", FIXTURES / "story", FIXTURES / "scenes"])
        mine = sorted(e.split("#")[1] for e in errors if "scenes/mill.json" in e.split("#")[0] or "beats/1:" in e)
        self.assertEqual(mine, [
            "/arcs/0/beats/1: its scenes never offer the choice(s) b",
            "/scenes/1/nodes/0/lines/0/id: line id 'mill_open' is already used in scenes/scenes/mill.json",
            "/scenes/1/nodes/0/lines/0/speaker: 'stranger' isn't in the scene's cast",
            "/scenes/1/nodes/0/options/1/choice: the beat 'gated' has no choice 'z'",
            "/scenes/1/nodes/0/options/1/tags/background/0: no background has the id 'pirate'",
            "/scenes/1/nodes/0/sets/0: no beat, choice, ending or encounter sets 'made_up_flag'",
            "/scenes/1/nodes/0: leads to 'gone', which isn't a node in this scene",
            "/scenes/1/nodes/1: a node needs exactly one of 'options', 'next' or 'end'",
            "/scenes/1/nodes/2: the node 'orphan' can't be reached from the start",
            "/scenes/2/nodes/0: every option is gated on flags, and no earlier vote guarantees one of them is set: "
            "the party could be left with nothing to pick",
        ])
        # The well-formed scene (a check, a tagged option, a flag the beat sets) raises nothing.
        self.assertFalse([e for e in errors if "/scenes/0/" in e], errors)

    def test_after_is_only_for_a_battle_beat(self):
        def scene(id_: str, beat: str, after: bool | None) -> dict:
            s = {"id": id_, "beat": beat, "cast": ["narrator"], "start": "n",
                 "nodes": [{"id": "n", "lines": [{"id": f"{id_}_1", "speaker": "narrator", "text": "t"}], "end": True}]}
            if after is not None:
                s["after"] = after
            return s

        with tempfile.TemporaryDirectory() as tmp:
            pack = Path(tmp) / "after"
            (pack / "scenes").mkdir(parents=True)
            (pack / "pack.json").write_text(json.dumps(
                {"id": "after", "loreVersion": 1, "requires": ["story"], "files": ["scenes/after.json"]}))
            (pack / "scenes" / "after.json").write_text(json.dumps({"scenes": [
                scene("lead", "fight", None),          # a battle's lead-in, then its aftermath: fine
                scene("won", "fight", True),
                scene("late_lead", "fight", False),    # a lead-in after the aftermath
                scene("found", "find_mill", True),     # a discovery has no fight to come after
            ]}))
            errors = check.check([FIXTURES / "valid" / "base", FIXTURES / "story", pack])
        mine = sorted(e.split("#")[1] for e in errors if "scenes/after.json" in e.split("#")[0])
        self.assertEqual(mine, [
            "/scenes/2: a lead-in of 'fight' comes after one of its aftermath scenes; move it before them",
            "/scenes/3/after: only a battle beat has an aftermath, and 'find_mill' is a discovery beat",
        ])

    def test_an_option_back_to_its_own_node_is_refused(self):
        """The game never offers an option leading to a node already played, its own included, so it could never show.
        Tactics.Logic's LoreBook warns of the same thing (LoreBook.Warnings)."""
        def line(id_: str) -> dict:
            return {"id": id_, "speaker": "narrator", "text": "t"}

        with tempfile.TemporaryDirectory() as tmp:
            pack = Path(tmp) / "loops"
            (pack / "scenes").mkdir(parents=True)
            (pack / "pack.json").write_text(json.dumps(
                {"id": "loops", "loreVersion": 1, "requires": ["story"], "files": ["scenes/loops.json"]}))
            (pack / "scenes" / "loops.json").write_text(json.dumps({"scenes": [{
                "id": "loops", "beat": "find_mill", "cast": ["narrator"], "start": "hub", "nodes": [
                    {"id": "hub", "lines": [line("loops_1")], "options": [
                        {"id": "again", "label": "Again.", "to": "hub"},
                        {"id": "try", "label": "Try.", "check": {"skill": "athletics", "dc": 10, "pass": "out", "fail": "hub"}},
                        # A 'to' beside a check counts as a target, as C#'s SceneOption.Targets has it (review A6).
                        {"id": "both", "label": "Both.", "to": "hub",
                         "check": {"skill": "athletics", "dc": 10, "pass": "out", "fail": "out"}},
                        {"id": "go", "label": "Go.", "to": "out"}]},
                    {"id": "out", "lines": [line("loops_2")], "end": True}]}]}))
            errors = check.check([FIXTURES / "valid" / "base", FIXTURES / "story", pack])
        mine = sorted(e.split("#")[1] for e in errors if "scenes/loops.json" in e.split("#")[0])
        self.assertEqual(mine, [
            "/scenes/0/nodes/0/options/0: leads back to its own node 'hub', so it's never offered",
            "/scenes/0/nodes/0/options/1: leads back to its own node 'hub', so it's never offered",
            "/scenes/0/nodes/0/options/2: leads back to its own node 'hub', so it's never offered",
        ])

    def test_art_keys_follow_the_rules_engine_harvest(self):
        keys = check.art_keys(check.CONTENT)
        self.assertIn("chapters.mirewood", keys)
        self.assertIn("characters.aldric", keys)
        self.assertNotIn("rules.manifest", keys)


if __name__ == "__main__":
    unittest.main()
