"""Recast planning must invalidate recordings without disturbing other speakers."""
import copy
import unittest
from unittest.mock import patch

import voices


class RecastPlanningTests(unittest.TestCase):
    def test_mauds_approved_take_keeps_its_exact_render_request(self):
        approved = voices.casting()["characters"]["old_maud"]["approvedRenderText"]["bark_old_maud_town_1"]
        job = next(j for j in voices.jobs() if j["line"] == "bark_old_maud_town_1")
        self.assertEqual(approved["renderText"], job["renderText"])

    def test_edited_caption_or_cast_direction_cannot_reuse_an_old_approved_request(self):
        cast = voices.casting()
        line = "bark_old_maud_town_1"
        old = next(j for j in voices.jobs() if j["line"] == line)
        with patch.object(voices, "lines", return_value=[("char:old_maud", line, "A different caption.")]):
            changed = voices.jobs()[0]
        self.assertTrue(changed["renderText"].endswith("A different caption."))
        self.assertNotEqual(old["raw"], changed["raw"])
        self.assertNotEqual(old["file"], changed["file"])
        updated = copy.deepcopy(cast)
        updated["characters"]["old_maud"]["direction"] += " More restrained."
        with patch.object(voices, "casting", return_value=updated):
            changed = next(j for j in voices.jobs() if j["line"] == line)
        self.assertNotEqual(old["raw"], changed["raw"])
        self.assertNotEqual(old["file"], changed["file"])

    def test_new_line_delivery_invalidates_an_approved_request(self):
        line = "bark_old_maud_town_1"
        old = next(j for j in voices.jobs() if j["line"] == line)
        directed = copy.deepcopy(voices.delivery())
        directed[line] = {"direction": "With sudden urgency.", "mood": "angry"}
        with patch.object(voices, "delivery", return_value=directed):
            changed = next(j for j in voices.jobs() if j["line"] == line)
        self.assertIn("With sudden urgency.", changed["renderText"])
        self.assertNotEqual(old["raw"], changed["raw"])
        self.assertNotEqual(old["file"], changed["file"])

    def test_delivery_change_invalidates_all_of_the_speakers_recordings(self):
        before = {j["id"]: j for j in voices.jobs()}
        cast = copy.deepcopy(voices.casting())
        cast["characters"]["dagna_holt"]["direction"] += " More restrained."
        with patch.object(voices, "casting", return_value=cast):
            after = {j["id"]: j for j in voices.jobs()}
        self.assertEqual(before.keys(), after.keys())
        affected = 0
        for key, old in before.items():
            new = after[key]
            self.assertEqual(old["text"], new["text"])
            if old["speaker"] == "dagna_holt":
                affected += 1
                self.assertNotEqual(old["raw"], new["raw"])
                self.assertNotEqual(old["file"], new["file"])
            else:
                self.assertEqual(old, new)
        self.assertEqual(96, affected)  # 78, plus four ford reactions (#207) and fourteen council and burial lines (#210)

    def test_existing_player_and_npc_recording_identities_are_preserved(self):
        jobs = {j["id"]: j for j in voices.jobs()}
        for key, raw, file in [
            ("voice_grim_battle_start_1.high", "Gacrux.28650f41", "voice_grim_battle_start_1.e8ccd9f5.ogg"),
            ("bark_halden_varrow_town_1.main", "Charon.f089eeb7", "bark_halden_varrow_town_1.b8325026.ogg"),
        ]:
            self.assertEqual(raw, jobs[key]["raw"])
            self.assertEqual(file, jobs[key]["file"])

    def test_character_direction_preserves_authored_line_delivery(self):
        jobs = {j["line"]: j for j in voices.jobs() if j["speaker"] == "dagna_holt"}
        for line, style in voices.delivery().items():
            if line in jobs:
                rendered = jobs[line]["renderText"]
                self.assertIn(style["direction"], rendered)
                self.assertIn(voices.casting()["characters"]["dagna_holt"]["direction"], rendered)
                self.assertTrue(rendered.endswith(jobs[line]["text"]))


if __name__ == "__main__":
    unittest.main()
