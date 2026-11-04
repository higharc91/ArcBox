"""Recast planning must invalidate recordings without disturbing other speakers."""
import copy
import unittest
from unittest.mock import patch

import voices


class RecastPlanningTests(unittest.TestCase):
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
        self.assertEqual(66, affected)

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
