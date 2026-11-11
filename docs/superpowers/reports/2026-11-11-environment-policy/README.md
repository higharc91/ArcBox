# Environmental encounter coverage — #138

On main `cf156c46`, six encounters with substantial fuel or shallows still had terrain reactions disabled. They now opt in: Pine Wolves, River Slimes, Roadside Toughs, Rooting Boars, Hollow Diggings and Rime Tower. Ten encounters are enabled in total. The Forest Ruins, Hollow Nest and Hollow Lair opt out explicitly: their sparse decorative ground yielded only 0.375 / 0.097 / 0.431 reactions per exploratory battle. Their policy reasons and exact terrain counts are checked in [the coverage audit](coverage.md).

The boar clearing adds 15 dry-grass tiles in its engagement lane; the slime bank adds 19 dry-grass tiles, leaving every water/depth flag intact; the cave's enemy landing adds six existing plank tiles. All heights, flags, props and spawn zones remain identical ([geometry audit](geometry.json)). Ashen Ford's world stamps and the Godot editor's Material cells match the two overworld patches. Native scene save/load/export still preserves original bytes and a second export is byte-identical ([editor check](editor-check.txt)). Existing sprites, sounds and voices are unchanged.

## Measured reactions

The final audit runs **3,600 real deterministic battles**: each enabled encounter at its intended level, six sampled parties per size 4/5/6, seeds 1–20, Normal difficulty and a 30-round cap counted as a loss. **All ten average at least one reaction per battle**; minimum **1.5056**, maximum **9.4583**. The complete per-battle measurements and configurations are in [raw/](raw/), with [machine-readable coverage](coverage.json) and balance diagnostics in the four level reports. The gate rejects missing/duplicate/incomplete samples, stale policy exceptions and unproductive opt-ins.

The quick 72-battle probe overestimated the original slime bank: its larger 360-battle cell averaged only 0.6194 reactions and was rejected. After the dry-bank patch, the same classes and seeds average 1.8306. [The rejected control](raw/rejected-slime-control.json.gz) is retained. The other 1,800 level-one battle measurements are identical across that rerun ([control check](control-check.json)). These overlapping controls are not additional independent samples. Balance tuning for large mob/Hollow parties remains #140; this change does not alter enemy stats.

To repeat:

```sh
# Run each level's documented simulator command from balance-level-{1,2,5,7}.md, adding --json /tmp/level-N.json.gz.
python3 tools/tactics-sim/environment_audit.py --samples /tmp/level-1.json.gz --samples /tmp/level-2.json.gz --samples /tmp/level-5.json.gz --samples /tmp/level-7.json.gz --out /tmp/environment-coverage.json
```

## Lava decision

Lava uses the existing **Deep** contract: it blocks walking; forced entry invokes deep falls, downing a party member or killing an enemy. It adds neither periodic fire damage nor neighbouring ignition. Hazard stays an authoring tag. This explicit choice is recorded in `tools/tactics-sim/environment-policy.json` and guarded against walkable or fuel-bearing molten tiles. Any later finale change in #141 must explicitly replace this contract.

## Native and browser review

Reviewed real Godot 1920×1080 captures of [Rooting Boars](mob_rooting_boars.png), [River Slimes](mob_river_slimes.png) and [Rime Tower](rime_tower.png), using the normal renderer and existing art. The grass and plank patches meet the active fighting lanes. Actual Chromium [deuteranopia](native-deuteranopia.png) and [protanopia](native-protanopia.png) simulations retain terrain texture and shape cues. These are software-rendered cloud captures, not physical-TV or human usability tests.

Real Chromium environmental-phone checks at [320×844](phone-320.png) and [390×844](phone-390.png), with reduced motion, pass contrast, text-size, touch-target, pixel-background and horizontal-overflow checks ([audit](phone-audit.json)). The active-turn preview also passes contrast/overflow checks ([active audit](active-phone-audit.json)).

Focused checks: **21 content/export tests**, **28 golden/environment/entry tests**, and **five adversarial audit tests**, all passing; fresh host and test builds have zero warnings. The full merge gate is recorded below when complete.

## Merge gate

The full check passes the zero-warning build, 601 phone tests and production build, 24 Node tests, and all Python/audio/lore groups. The .NET group is blocked by ten changed main-branch lore lines without recordings (see [audit](missing-main-voices.json)). Existing strict voice coverage is retained; no tests are skipped or weakened. Baseline scene-flag loading, caption length and stale authored-text fixtures are corrected separately. This branch must not merge until the complete gate passes.
