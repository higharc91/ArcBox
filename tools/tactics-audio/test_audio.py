"""Tests for audio.py's lists and plan. Run: python3 -m unittest discover -s tools/tactics-audio"""
from __future__ import annotations

import json
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

import audio


def kinds(tmp: Path, music: dict, done: dict | None = None) -> dict:
    """A KINDS table over a throwaway music list (and an empty sfx list)."""
    (tmp / "music-list.json").write_text(json.dumps(music), encoding="utf-8")
    (tmp / "music-assets.json").write_text(json.dumps({"clips": done or {}}), encoding="utf-8")
    (tmp / "sfx-list.json").write_text("{}", encoding="utf-8")
    return {
        "sfx": {**audio.KINDS["sfx"], "list": tmp / "sfx-list.json", "assets": tmp / "sfx-assets.json"},
        "music": {**audio.KINDS["music"], "list": tmp / "music-list.json", "assets": tmp / "music-assets.json"},
    }


def cue(**over) -> dict:
    return {"prompt": "a tune", "duration": 9, "variants": 1, "group": "sting", "loop": False, "use": "x", "seed": 1,
            "instrumental": True, **over}


class MusicList(unittest.TestCase):
    def test_every_cue_says_whether_it_is_instrumental(self):
        for sid, spec in audio.sounds("music").items():
            with self.subTest(sid):
                self.assertIsInstance(spec.get("instrumental"), bool)
                self.assertNotIn("vocals", spec, "one field says it: instrumental")

    def test_the_boss_victory_is_generated_with_its_choir(self):
        self.assertIs(audio.sounds("music")["victory_boss"]["instrumental"], False)

    def test_the_voiced_cues_are_not_instrumental(self):
        music = audio.sounds("music")
        for sid in ("title", "finale", "court_mirewood", "town_greyhold", "camp_scarwalker"):
            with self.subTest(sid):
                self.assertIs(music[sid]["instrumental"], False)
        for sid in ("battle", "explore", "victory", "defeat", "town_emberwick"):
            with self.subTest(sid):
                self.assertIs(music[sid]["instrumental"], True)


class Plan(unittest.TestCase):
    def test_a_music_job_carries_instrumental(self):
        with tempfile.TemporaryDirectory() as d:
            jobs = audio.plan_jobs(kinds(Path(d), {"choir": cue(instrumental=False), "brass": cue()}))
        by = {j["id"]: j for j in jobs}
        self.assertIs(by["choir"]["instrumental"], False)
        self.assertIs(by["brass"]["instrumental"], True)

    def test_a_generated_cue_is_not_planned(self):
        with tempfile.TemporaryDirectory() as d:
            jobs = audio.plan_jobs(kinds(Path(d), {"brass": cue()}, done={"brass": {"asset": "a"}}))
        self.assertEqual(jobs, [])

    def test_a_music_cue_without_instrumental_is_refused(self):
        with tempfile.TemporaryDirectory() as d:
            spec = cue()
            del spec["instrumental"]
            with self.assertRaises(SystemExit) as e:
                audio.plan_jobs(kinds(Path(d), {"brass": spec}))
        self.assertIn("brass", str(e.exception))


class Manifests(unittest.TestCase):
    def test_import_keeps_registered_external_animal_calls_alongside_generated_clips(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            table = kinds(root, {})["sfx"]
            table["out"] = root / "sfx"
            table["out"].mkdir()
            table["external"] = root / "external-sfx.json"
            external = {"files": ["crow.wav"], "loop": False, "group": "ambience", "use": "A nearby crow"}
            table["external"].write_text(json.dumps({"sounds": {"animal_crow": external}}))
            (table["out"] / "crow.wav").write_bytes(b"existing")
            table["list"].write_text(json.dumps({"chime": cue()}))
            (table["out"] / "chime.ogg").write_bytes(b"generated")
            with patch.object(audio, "KINDS", {"sfx": table}):
                self.assertEqual(audio.write_manifests(), {"sfx": 2})
            result = json.loads((table["out"] / "manifest.json").read_text())
            self.assertEqual(result["sounds"]["animal_crow"], external)
            self.assertEqual(result["sounds"]["chime"]["files"], ["chime.ogg"])
            # A later unrelated import must leave an unchanged manifest byte-identical, including its authored ordering.
            manifest_path = table["out"] / "manifest.json"
            authored = json.dumps(result, sort_keys=True, separators=(",", ":"))
            manifest_path.write_text(authored)
            with patch.object(audio, "KINDS", {"sfx": table}):
                audio.write_manifests()
            self.assertEqual(manifest_path.read_text(), authored)


class ShippedLoudness(unittest.TestCase):
    def test_every_manifest_clip_meets_its_group_target_and_true_peak_ceiling(self):
        measurements = audio.audit_shipped()
        self.assertTrue(measurements)
        failures = [f"{row['file']}: {row['level']:.1f} {row['metric']} vs {row['target']:.1f}, "
                    f"true peak {row['true_peak']:.1f} dBTP"
                    for row in measurements
                    if abs(row["level"] - row["target"]) > audio.LOUDNESS_TOLERANCE
                    or row["true_peak"] > audio.TRUE_PEAK_CEILING]
        self.assertEqual([], failures)
        dungeon = next(row for row in measurements if row["file"] == "music/dungeon.ogg")
        self.assertLessEqual(dungeon["true_peak"], -5.0)
        self.assertEqual("RMS dBFS", next(row for row in measurements if row["file"] == "sfx/ui_cancel.ogg")["metric"])


if __name__ == "__main__":
    unittest.main()
