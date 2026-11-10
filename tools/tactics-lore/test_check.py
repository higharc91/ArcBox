"""Tests for check.py against the fixture packs: python3 -m unittest discover tools/tactics-lore"""

import json
import re
import tempfile
import unittest
from pathlib import Path

import check

FIXTURES = Path(__file__).parent / "fixtures"


class CheckTests(unittest.TestCase):
    def test_spoken_lore_uses_gold_as_its_only_named_coin(self):
        lore = check.CONTENT / "lore"
        currency = re.compile(r"\b(?:crowns?|coppers?|silvers?|electrum|platinum)\b", re.IGNORECASE)
        for file in json.loads((lore / "pack.json").read_text(encoding="utf-8"))["files"]:
            if not file.startswith(("scenes/", "conversations/", "barks/")):
                continue
            doc = json.loads((lore / file).read_text(encoding="utf-8"))
            for key, value, pointer in check._walk(doc):
                if key == "text" and isinstance(value, str):
                    self.assertIsNone(currency.search(value), f"{file}{pointer}: {value}")

    def test_story_gold_matches_the_runtime_int32_contract(self):
        schemas = check.load_schemas()
        for value in (0, 25, -20, 2147483647, -2147483647):
            errors = check.Errors()
            check.validate(value, schemas["scenes.schema.json"]["$defs"]["option"]["properties"]["gold"],
                           "scenes.schema.json", schemas, "gold-test", "/gold", errors)
            self.assertFalse(errors)
        for value in (True, 1.5, "25", None, 2147483648, -2147483648):
            errors = check.Errors()
            check.validate(value, schemas["scenes.schema.json"]["$defs"]["node"]["properties"]["gold"],
                           "scenes.schema.json", schemas, "gold-test", "/gold", errors)
            self.assertTrue(errors, repr(value))

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

    def test_camp_scenes_are_separate_from_battle_aftermaths_and_can_set_their_own_flags(self):
        def scene(id_, **extra):
            return dict(id=id_, beat="fight", cast=["narrator"], start="n",
                        nodes=[dict(id="n", lines=[dict(id=id_+"_line", speaker="narrator", text="Fire.")],
                                    sets=["camp_reflection"] if extra.get("camp") is True else [], end=True)], **extra)
        with tempfile.TemporaryDirectory() as tmp:
            pack = Path(tmp)
            (pack / "scenes").mkdir()
            (pack / "pack.json").write_text(json.dumps(dict(id="camp", loreVersion=1, requires=["story"], files=["scenes/camp.json"])))
            (pack / "scenes/camp.json").write_text(json.dumps(dict(scenes=[
                scene("won", after=True), scene("reflection", camp=True), scene("bad_both", camp=True, after=True)])) )
            errors = check.check([FIXTURES / "valid" / "base", FIXTURES / "story", pack])
        self.assertTrue(any("camp scene cannot also" in e for e in errors), errors)
        self.assertFalse(any("reflection" in e or "lead-in" in e for e in errors), errors)

    def test_side_content_rejects_bad_entries_targets_unset_stages_and_main_flag_actions(self):
        import shutil
        with tempfile.TemporaryDirectory() as tmp:
            content = Path(tmp) / "base"
            shutil.copytree(check.CONTENT / "lore", content / "lore", ignore=shutil.ignore_patterns("*.ogg"))
            shutil.copytree(check.CONTENT / "rules", content / "rules")
            # Keep real art lookup on the original content; cross references are the same stable IDs.
            lore = content / "lore"
            def edit(file, change):
                p = lore / file; d = json.loads(p.read_text()); change(d); p.write_text(json.dumps(d))
            edit("conversations/halden_varrow.json", lambda d: d["entries"][0].update(node="missing_entry"))
            edit("quests.json", lambda d: d["quests"][0]["stages"][0].update(when=["sq_unset_stage"], target={"tile":[9999,9999]}))
            edit("conversations/tobin_fell.json", lambda d: d["nodes"][0]["options"][0].update(sets=["ch1_skarrow_slain"]))
            # Removing the start action makes its former first-stage flag genuinely unreachable.
            edit("conversations/halden_varrow.json", lambda d: [o.pop("quest",None) for n in d["nodes"] for o in n.get("options",[]) if o.get("quest",{}).get("start")])
            edit("barks/mother_skarrow.json", lambda d: d["events"].pop("town"))
            errors = check.check([lore])
        self.assertTrue(any("mother_skarrow" in e and "neither conversation nor barks" in e for e in errors), errors)
        self.assertTrue(any("missing_entry" in e for e in errors), errors)
        self.assertTrue(any("stage 'asked'" in e and "nothing" in e for e in errors), errors)
        self.assertTrue(any("outside" in e for e in errors), errors)
        self.assertTrue(any("main-story" in e for e in errors), errors)

    def test_class_trinket_rewards_cover_every_class_with_its_own_known_item(self):
        import shutil
        with tempfile.TemporaryDirectory() as tmp:
            content = Path(tmp) / "base"
            shutil.copytree(check.CONTENT / "lore", content / "lore", ignore=shutil.ignore_patterns("*.ogg"))
            shutil.copytree(check.CONTENT / "rules", content / "rules")
            path = content / "lore/quests.json"
            data = json.loads(path.read_text())
            trinkets = json.loads((content / "rules/trinkets.json").read_text())
            valid = {t["class"]: t["id"] for t in trinkets}
            data["quests"][0]["rewards"]["trinkets"] = valid
            path.write_text(json.dumps(data))
            self.assertEqual(check.check([content / "lore"]), [])
            for kind in ["unknown_class", "unknown_trinket", "wrong_class", "missing_class"]:
                with self.subTest(kind=kind):
                    reward = dict(valid)
                    if kind == "unknown_class": reward["invented"] = "ember_claw_gauntlet"
                    elif kind == "unknown_trinket": reward["fighter"] = "missing"
                    elif kind == "wrong_class": reward["fighter"] = "hearthstone_pendant"
                    else: reward.pop("wizard")
                    data["quests"][0]["rewards"]["trinkets"] = reward
                    path.write_text(json.dumps(data))
                    self.assertTrue(any("trinket" in e for e in check.check([content / "lore"])))

    def test_quest_targets_accept_an_explicit_region_and_reject_unknown_regions(self):
        import shutil
        with tempfile.TemporaryDirectory() as tmp:
            content = Path(tmp) / "base"
            shutil.copytree(check.CONTENT / "lore", content / "lore", ignore=shutil.ignore_patterns("*.ogg"))
            shutil.copytree(check.CONTENT / "rules", content / "rules")
            path = content / "lore/quests.json"
            data = json.loads(path.read_text())
            target = {"region": "ashen_ford", "tile": [4, 3]}
            data["quests"][0]["stages"][0]["target"] = target
            path.write_text(json.dumps(data))
            self.assertEqual(check.check([content / "lore"]), [])
            target["region"] = "missing"
            path.write_text(json.dumps(data))
            self.assertTrue(any("unknown target region" in e for e in check.check([content / "lore"])))

    def test_optional_beat_hooks_cannot_set_main_flags_or_be_aftermaths(self):
        def scene(id_, **kw):
            return dict(id=id_, cast=["narrator"], start="n", nodes=[dict(id="n", lines=[
                dict(id=id_+"_1", speaker="narrator", text="Hello.")], end=True, sets=["sq_hook_seen"])], **kw)
        with tempfile.TemporaryDirectory() as tmp:
            pack = Path(tmp) / "hooks"
            (pack / "scenes").mkdir(parents=True)
            (pack / "pack.json").write_text(json.dumps(dict(id="hooks", loreVersion=1,
                requires=["story"], files=["scenes/hooks.json"])))
            good = scene("good")
            bad = scene("bad", after=True)
            main = scene("main")
            main["nodes"][0]["sets"] = ["mill_found"]
            (pack / "scenes/hooks.json").write_text(json.dumps(dict(scenes=[good, bad, main])))
            errors = check.check([FIXTURES / "valid" / "base", FIXTURES / "story", pack])
        mine = [e for e in errors if "scenes/hooks.json" in e]
        self.assertFalse(any("/scenes/0" in e for e in mine), mine)
        self.assertTrue(any("aftermath" in e for e in mine), mine)
        self.assertTrue(any("main-story" in e for e in mine), mine)

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
