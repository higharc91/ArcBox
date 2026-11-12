"""Cross-check shipped Tactics art/audio against manifests and rules.

Run with python -m unittest tools/tactics-assets/test_manifest_crosscheck.py.
"""
import json
import hashlib
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

    def test_retained_mob_sources_are_unchanged_and_have_live_replacements(self):
        inventory = json.loads((Path(__file__).parent / "baseline/retained-mob-sources.json").read_text())
        assets = inventory["assets"]
        self.assertTrue(assets, "Historical sources must be individually inventoried")
        allowed = {path for path, reason in ALLOW["sourceAssets"].items()
                   if reason.startswith("Historical mob-refresh source:")}
        self.assertEqual(set(assets), allowed, "Retained-source exceptions must match the inventory exactly")
        runtime = set()
        for manifest in BASE.rglob("manifest.json"):
            runtime.update((manifest.parent / path).relative_to(BASE).as_posix()
                           for path in manifest_files(json.loads(manifest.read_text())))
        for path, entry in assets.items():
            with self.subTest(path=path):
                source = BASE / path
                replacement = entry["replacement"]
                self.assertTrue(source.is_file(), "Retained source missing")
                self.assertEqual(entry["sha256"], hashlib.sha256(source.read_bytes()).hexdigest(),
                                 "Historical source changed; refreshes must use their replacement path")
                self.assertNotEqual(path, replacement)
                self.assertIn(replacement, runtime, "Replacement must remain referenced by a runtime manifest")
                self.assertTrue((BASE / replacement).is_file(), "Replacement missing")

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
        # Old saved sprite keys are optional compatibility aliases of the actual accepted atlases.
        for entry in creatures["sprites"].values():
            keys.update(entry.get("keys", []))
        self.assertFalse((BASE / "monsters").exists(), "Legacy stand-in art must not return")
        missing = set()
        for path in (BASE / "rules/monsters").glob("*.json"):
            for monster in json.loads(path.read_text()):
                sprite = monster.get("sprite")
                if sprite and sprite not in keys:
                    missing.add(sprite)
        self.assertEqual(set(), missing, "Unresolved rules monster sprites")

    def test_character_layer_aliases_are_resolvable_and_no_duplicate_bytes_ship(self):
        manifest = json.loads((BASE / "characters/manifest.json").read_text())
        aliases = manifest["layerAliases"]
        self.assertEqual(165, len(aliases))
        for source, target in aliases.items():
            self.assertNotIn(target, aliases, "Aliases must resolve in one hop")
            self.assertFalse((BASE / "characters" / source).exists())
            self.assertTrue((BASE / "characters" / target).is_file())
        hashes = {}
        for path in (BASE / "characters").rglob("*.png"):
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertNotIn(digest, hashes, f"Duplicate character art: {path} / {hashes.get(digest)}")
            hashes[digest] = path

    def test_creator_traits_and_subclass_names_match_rules(self):
        creator = json.loads((BASE / "characters/creator.json").read_text())

        # 1. Subclass names match rules exactly (36 total, 3 per class).
        subclasses = creator.get("subclasses", {})
        self.assertEqual(36, len(subclasses), "Expected 36 subclasses in creator.json")
        for sub_id, sub_data in subclasses.items():
            cls_id = sub_data["class"]
            cls_rule = json.loads((BASE / f"rules/classes/{cls_id}.json").read_text())
            rule_sub = next((s for s in cls_rule.get("subclasses", [])
                             if s["id"] == f"{cls_id}.{sub_id}" or s["id"].endswith(f".{sub_id}")), None)
            self.assertIsNotNone(rule_sub, f"Subclass {cls_id}.{sub_id} not found in rules")
            self.assertEqual(sub_data["name"], rule_sub["name"],
                             f"Subclass name mismatch for {cls_id}.{sub_id}: creator='{sub_data['name']}', rules='{rule_sub['name']}'")

        # 2. Race traits match rules and have no unimplemented drift traits.
        drift_traits = {
            "+1 to every score", "Two extra skills", "Brave", "Nimble",
            "Savage attacks", "Powerful build", "Mountain born",
            "Radiant soul", "Keen senses", "Stone sense", "Versatile"
        }
        for race_id, race_data in creator["races"].items():
            traits = set(race_data.get("traits", []))
            bad = traits & drift_traits
            self.assertEqual(set(), bad, f"Drift traits in {race_id}: {bad}")

        self.assertEqual(creator["races"]["human"]["traits"], ["Resourceful", "+1 skill"])
        self.assertEqual(creator["races"]["half_elf"]["traits"], ["Darkvision", "Fey ancestry", "+1 skill"])
        self.assertEqual(creator["races"]["halfling"]["traits"], ["Lucky"])
        self.assertEqual(creator["races"]["half_orc"]["traits"], ["Darkvision", "Relentless endurance"])
        self.assertEqual(creator["races"]["goliath"]["traits"], ["Stone's endurance"])
        self.assertEqual(creator["races"]["aasimar"]["traits"], ["Darkvision", "Healing hands"])
        self.assertEqual(creator["races"]["dwarf"]["traits"], ["Darkvision", "Poison resilience", "Stoneskin"])
        self.assertEqual(creator["races"]["gnome"]["traits"], ["Darkvision", "Gnome cunning"])
        self.assertEqual(creator["races"]["tiefling"]["traits"], ["Darkvision", "Hellish resistance", "Hellish rebuke"])
        self.assertEqual(creator["races"]["elf"]["traits"], ["Darkvision", "Fey ancestry", "Fey step"])
        self.assertEqual(creator["races"]["dragonborn"]["traits"], ["Breath weapon", "Damage resistance"])

        # 3. Subclass role text does not promise non-existent mechanics.
        shadow_role = creator["subclasses"]["shadow"]["role"].lower()
        self.assertNotIn("backstab", shadow_role, "Shadow role should not promise backstabs (no facing)")
        self.assertNotIn("behind", shadow_role, "Shadow role should not promise positioning behind")
        lore_role = creator["subclasses"]["lore"]["role"].lower()
        self.assertNotIn("cop", lore_role, "Lore role should not promise copying abilities")

    def test_creator_drawn_weapon_and_shield_match_starting_kit(self):
        creator = json.loads((BASE / "characters/creator.json").read_text())
        shields = {"round_shield", "kite_shield", "tower_shield", "buckler"}
        for cls_id, cls_data in creator["classes"].items():
            cls_rule = json.loads((BASE / f"rules/classes/{cls_id}.json").read_text())
            sk = cls_rule["startingKit"]
            gear = cls_data["gear"]

            has_shield = sk.get("shield", False)
            drawn_offhand = gear.get("offhand")
            if has_shield:
                self.assertIn(drawn_offhand, shields,
                              f"{cls_id} startingKit has shield, but drawn offhand is {drawn_offhand}")
            else:
                self.assertNotIn(drawn_offhand, shields,
                                 f"{cls_id} startingKit has no shield, but drawn offhand is {drawn_offhand}")

            drawn_weapon = gear.get("weapon")
            self.assertIsNotNone(drawn_weapon, f"{cls_id} has no drawn weapon")

            if cls_id == "paladin":
                self.assertEqual("sword", drawn_weapon, "Paladin drawn weapon should match startingKit longsword ('sword')")
                self.assertEqual("chain", gear.get("torso"), "Paladin drawn torso should match startingKit chain_mail ('chain')")
            elif cls_id == "monk":
                self.assertEqual("spear", drawn_weapon, "Monk drawn weapon should match startingKit spear ('spear')")
            elif cls_id == "druid":
                self.assertEqual("staff", drawn_weapon, "Druid drawn weapon should match startingKit quarterstaff ('staff')")


if __name__ == "__main__":
    unittest.main()

