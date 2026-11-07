# Oak Mimic production sources

Scenario concept and receipts: `../../concepts/oak-ambush-2026-11-07/`. Four generated transparent pixel sheets, exact prompts and generation IDs are preserved here. PixelLab was not used. Scripts select complete authored figures, crop, scale uniformly with nearest-neighbour sampling and pack them. No mirrored, independently moved or repainted parts.

112 packed keys select 96 distinct source poses across eight directions: four idle, four walk, four bite, one hurt and one down per direction. Bite ready/recovery reuse two idle poses. Hurt/down are single poses; down is a low slumped chest with an ajar lid, not an animated fall. Basic and adhesive bite share their existing bosses.attack binding; no separate special/buff/cast sheet.

facings.json selects whole idle/walk rows to correct front-oblique/profile source ordering. Active bite figures already face correctly; attack keys 0/3 reuse corrected idle ready/recovery. Rear hinges remain rear and tongue remains rooted. Scale targets per sheet: idle/damage 64px, walk 70px with feet extended, attack 68px. Runtime: 256px cells, [128,224] anchors, two rows per facing, 0.32 scale. Whole-chest HUD crop [84,148,89,89]; attack overhead clearance 6.8 world pixels. Idle 3fps, walk 6fps, bite [8,9,9,9,10,10,11,0] at 10fps. Melee lasts 0.8s; existing impact stays at 0.4s. Rules, ability bindings, effects, audio, spawns and other library variants remain unchanged.

Reproduce from repository root with Pillow/NumPy/SciPy and Node Canvas/Playwright:

```sh
python tools/tactics-assets/imagegen/oak-ambush-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/oak-ambush-2026-11-07
python tools/tactics-assets/imagegen/oak-ambush-2026-11-07/integrate.py
```

Then run compare.cjs, review.cjs, reel.cjs, verify-review.cjs and verify-scope.py from this folder, keeping repository root as cwd. Set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH for your installed runtime. Capture collection arguments are in the review README. Scope compares against 83cbed32.
