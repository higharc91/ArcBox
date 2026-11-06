"""Cross-check shipped Tactics art/audio against manifests and rules.

Run with python -m unittest tools/tactics-assets/test_manifest_crosscheck.py.
"""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / "content/tactics/base"
ALLOW = json.loads((Path(__file__).with_name("manifest-allowlist.json")).read_text())


def manifest_files(node):
    """Only path-shaped leaf strings are asset references; prose is not."""
    if isinstance(node, dict):
        for value in node.values():
            yield from manifest_files(value)
    elif isinstance(node, list):
        for value in node:
            yield from manifest_files(value)
    elif isinstance(node, str) and re.fullmatch(r"[\w./@-]+\.(?:png|ogg|wav)", node, re.I):
        yield node


class ManifestCrosscheckTests(unittest.TestCase):
    def test_manifest_paths_exist_and_every_shipped_file_is_accounted_for(self):
        referenced = set()
        missing = set()
        for manifest in BASE.rglob("manifest.json"):
            if manifest.parent == BASE / "rules":
                continue
            for name in manifest_files(json.loads(manifest.read_text())):
                path = manifest.parent / name
                relative = path.relative_to(BASE).as_posix()
                referenced.add(relative)
                if not path.is_file():
                    missing.add(relative)

        # These manifests name presets/layers, with the path assembled by the loader.
        characters = json.loads((BASE / "characters/manifest.json").read_text())
        for file in (BASE / "characters").rglob("*.png"):
            rel = file.relative_to(BASE / "characters").parts
            if len(rel) == 3 and rel[0] in characters["builds"] and rel[1] in characters["layers"] and rel[2][:-4] in characters["layers"][rel[1]]:
                referenced.add(file.relative_to(BASE).as_posix())
        referenced.update(f"characters/presets/{name}.png" for name in characters["presets"])
        tactical = json.loads((BASE / "tactical/manifest.json").read_text())
        referenced.update(f"tactical/{name}.png" for name in tactical["presets"])
        # Camp frames and the emote atlas have conventional names used by the loaders.
        referenced.update(f"camp/{kind}_{frame}.png" for kind in ("fire", "pot") for frame in range(5))
        referenced.update(("camp/fire_unlit.png", "camp/hearth.png", "emotes/emotes.png"))

        source = ALLOW["sourceAssets"]
        shipped = {p.relative_to(BASE).as_posix() for p in BASE.rglob("*") if p.suffix.lower() in {".png", ".ogg", ".wav"}}
        self.assertEqual(set(), missing, f"Missing manifest files: {sorted(missing)}")
        self.assertEqual(set(), set(source) - shipped, "Stale source-asset allowlist")
        self.assertEqual(set(), shipped - referenced - set(source), "Unreferenced shipped assets")

    def test_rules_ids_have_art_or_a_documented_fallback(self):
        icons = json.loads((BASE / "icons/manifest.json").read_text())
        ability_icons = set(icons["abilityIcons"])
        abilities = {entry["id"] for path in (BASE / "rules/abilities").glob("*.json") for entry in json.loads(path.read_text())["abilities"]}
        fallback = set(ALLOW["abilityIconFallbacks"])
        self.assertEqual(set(), abilities - ability_icons - fallback, "Abilities without icons")
        self.assertEqual(set(), fallback - abilities, "Stale ability fallback entries")

        conditions = {entry["id"] for entry in json.loads((BASE / "rules/conditions.json").read_text())}
        status = json.loads((BASE / "status/manifest.json").read_text())["statuses"]
        icon_keys = set(icons["icons"])
        fallback = set(ALLOW["conditionIconFallbacks"])
        self.assertEqual(set(), conditions - set(status) - icon_keys - fallback, "Conditions without icons/overlays")
        self.assertEqual(set(), fallback - conditions, "Stale condition fallback entries")

        injuries = {entry["id"] for entry in json.loads((BASE / "rules/injuries.json").read_text())}
        injury_icons = json.loads((BASE / "injury_icons/manifest.json").read_text())["icons"]
        self.assertEqual(set(), injuries - set(injury_icons), "Injuries without icons")

        items = {entry["id"] for entry in json.loads((BASE / "rules/items.json").read_text())}
        fallback = set(ALLOW["itemIconFallbacks"])
        self.assertEqual(set(), {f"item.{x}" for x in items} - ability_icons - fallback, "Items without icons")
        self.assertEqual(set(), fallback - {f"item.{x}" for x in items}, "Stale item fallback entries")

    def test_status_overlays_have_a_rules_condition_or_live_alias(self):
        conditions = {entry["id"] for entry in json.loads((BASE / "rules/conditions.json").read_text())}
        status = set(json.loads((BASE / "status/manifest.json").read_text())["statuses"])
        # UnitArt.Effect maps these conditions to reusable overlay artwork.
        aliases = {"unconscious": "sleeping", "mage_armor": "shielded", "barkskin": "shielded", "death_ward": "shielded", "aura_of_protection": "blessed"}
        self.assertEqual(set(), status - conditions - set(aliases.values()), "Dead status artwork")
        self.assertEqual(set(), set(aliases) - conditions, "Stale condition aliases")
        self.assertEqual(set(), set(aliases.values()) - status, "Missing alias artwork")

    def test_direct_sfx_ids_used_by_presentation_exist(self):
        sounds = set(json.loads((BASE / "sfx/manifest.json").read_text())["sounds"])
        presentation = ROOT / "games/Tactics/Tactics.Presentation"
        beat = (presentation / "Beats/BeatScript.cs").read_text()
        music = (presentation / "Audio/MusicChoice.cs").read_text()
        story = (presentation / "Story/StoryTrack.cs").read_text()
        ids = set(re.findall(r'Sound\("([a-z0-9_]+)"\)', beat))
        ids.update(re.findall(r'"(amb_[a-z0-9_]+)"', music))
        ids.update(re.findall(r'\bSound\s*=\s*"([a-z0-9_]+)"', story))
        # ThingSounds candidates are optional by design: its For() checks bank.Has.
        self.assertEqual(set(), ids - sounds, "Presentation plays missing sfx ids")

    def test_rules_monster_sprites_resolve(self):
        keys = set()
        for group, property_name in (("beasts", "beasts"), ("bosses", "monsters"), ("bestiary", "creatures")):
            data = json.loads((BASE / group / "manifest.json").read_text())[property_name]
            for monster, value in data.items():
                keys.add(f"{group}.{monster}")
                keys.update(f"{group}.{monster}.{variant}" for variant in value["variants"])
        creatures = json.loads((BASE / "creatures/manifest.json").read_text())
        keys.update(f"creatures.{key}" for key in creatures["sprites"])
        characters = json.loads((BASE / "characters/manifest.json").read_text())
        keys.update(f"characters.{name}" for name in characters["presets"])
        tactical = json.loads((BASE / "tactical/manifest.json").read_text())
        keys.update(f"tactical.{name}" for name in tactical["presets"])
        aliases = json.loads((BASE / "monsters/manifest.json").read_text())["sprites"]
        for key, entry in aliases.items():
            self.assertIn(entry["standIn"], keys, f"Broken legacy sprite alias: {key}")
            keys.add(f"monsters.{key}")
        missing = set()
        for path in (BASE / "rules/monsters").glob("*.json"):
            for monster in json.loads(path.read_text()):
                sprite = monster.get("sprite")
                if sprite and sprite not in keys:
                    missing.add(sprite)
        self.assertEqual(set(), missing, "Unresolved rules monster sprites")


if __name__ == "__main__":
    unittest.main()

