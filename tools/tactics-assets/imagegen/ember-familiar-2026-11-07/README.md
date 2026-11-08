# Bound Imp sources and integration

Scenario concept/receipt: `../../concepts/ember-familiar-2026-11-07/`. Five original generated transparent pixel sheets, exact prompts and generation IDs are preserved here. PixelLab was not used. Scripts select complete authored figures, crop and uniformly scale with nearest-neighbour sampling, then pack them. No mirrored anatomy, independently moved limbs or pixel repainting.

112 selected complete frame keys across eight directions: four idle, four wing-assisted walk, four hand-cast attack, one hurt and one down per direction. Hurt/down are single poses, not an animated fall. The original attack sheet's four full-back keys are excluded: imp-attack-fullback.png supplies four corrected whole figures. Its right arm gathers on the far side and extends away beside the right shoulder, retaining the same hand and two upper-back wing roots. Other facings use the original sheet. There are 116 authored source figures and 112 selected source-pose pairs. The wingless rear anatomy panel in the concept is not used as a production pose.

Neutral idle/walk/cast target 56px horn-to-floor height; hurt targets 52px. Scale is fixed within each source sheet. Ivory horn pixels register the head, avoiding an incorrect anchor on a moving wing tip. Idle/walk registration follows that stable head reference, with only a one-pixel walk bob. Every wing membrane remains connected to its back root/finger struts; horns/ears stay on the head and the tail stays joined to the rump. Down bodies rest low with folded wings and joined arms/legs/tail. The sprite is grounded or low-hover stepping, not high aerial flight.

Runtime: 256px cells, [128,224] anchors, two rows per facing, 0.32 scale. Front head/horns/ears use HUD crop [112,165,40,40]. Idle 3fps, walk 6fps, cast [8,9,9,9,10,10,11,0] at 10fps. Release is at 0.4s and recovery finishes through 0.8s. The renderer now recognizes the crimson Imp's generic common.attack as natural hellfire, using the existing projectiles.fireball and fx.flames.hellfire rather than the generic arrow fallback. It preserves explicit visual overrides. No new ability, active special, balance, damage, status, encounter, audio asset or FX asset is added; the existing fire projectile also selects the normal spell-cast sound instead of bow release. The violet library variant retains its own art/behavior.

From repository root with Pillow/NumPy/SciPy and Node Canvas/Playwright available:

```sh
python tools/tactics-assets/imagegen/ember-familiar-2026-11-07/measure.py
node tools/tactics-assets/imagegen/mob-refresh-2026-11-06/production/export.cjs tools/tactics-assets/imagegen/ember-familiar-2026-11-07
python tools/tactics-assets/imagegen/ember-familiar-2026-11-07/integrate.py
node tools/tactics-assets/imagegen/ember-familiar-2026-11-07/compare.cjs
node tools/tactics-assets/imagegen/ember-familiar-2026-11-07/review.cjs
node tools/tactics-assets/imagegen/ember-familiar-2026-11-07/reel.cjs
node tools/tactics-assets/imagegen/ember-familiar-2026-11-07/verify-review.cjs
python tools/tactics-assets/imagegen/ember-familiar-2026-11-07/verify-scope.py
```

Set NODE_PATH and PLAYWRIGHT_BROWSERS_PATH to installed runtimes as needed. Capture collection arguments are in the review README. Scope compares against 6b3794ef, protecting all rules, ability JSON, prior art, portraits and the historical baseline. Source hashes and the exact rear correction selection are recorded by verify-scope.py.
