# Milestone B — default-off environmental engine

Issue #69. Definitions `a1980c4d`; runtime `f7bacd80`.
[Validation counts and source/golden SHA256 values](core-validation.json). The published 35 changed files match the isolated, validated checkout byte for byte.

**A-only merge boundary: `dccfab9f`.** Status update 2026-11-03: after user review and authorization, A was merged through that commit into `ccr-18ad4510-q68nad`. B remains on the feature branch; [C is now implemented](milestone-c.md). This is separate from the AI-only milestone A. No encounter is opted in, and there are no map, art, audio,
starting-kit, enemy, loot or balance edits. The oil surface is supported; the oil-flask item is milestone D.

## What changed

- Loaded and validated `rules/environment.json`, its schema and manifest entry. Unknown material families, surfaces,
  damage types and passive references report precise JSON pointers. Connected fuel patches larger than 12 tiles produce
  content warnings. Spreading definitions must leave permanent, nonflammable spent ground.
- Optional, omitted-by-default encounter/battle opt-in and zone surface/source metadata. Surfaces age once per round,
  independently of their owner, and cannot be dispelled. Adding the definition changes the content hash as designed.
- A serializable final `ReactStep` handles conduction, ignition, freezing/thawing and burning webs. Its hit and selected
  damage metadata survive pending reactions and save/load. Counterspell drops it with the canceled effects. The battle
  waits for this final step before checking victory, including when direct damage killed the last enemy.
- Conduction fills connected, uniced water in breadth-first layers and queues ordinary save/damage effects in UnitId
  order. Direct targets are not hit twice; standing allies and the caster are included, downed units excluded. Save DC,
  resistance and damage attribution use the source and existing engine rules.
- Fire spreads one tile per round from a snapshot, grants newly lit tiles their full lifetime, and leaves permanent
  scorch. It consumes only lit oil/grease tiles. Fire on empty fuel works; ticks and secondary effects cannot start
  reaction cascades. Web burning removes only the touched web tiles and their own restraints; surviving web-condition
  origins are rebound to the remaining zone.
- Cold freezes only shallow water. Ice insulates conduction and applies its first-entry save once per turn. Terrain
  sinking removes invalid ice. Walking, dashing, forced movement, teleportation and falling into water douse burning.
- Content-derived tooltips explain reactions only when the battle opts in. Existing ability, phone and character
  tooltips remain unchanged elsewhere. Fixed save DCs, durations and conduction reach come from the definition.

The design's static damage-tree lookup needed one refinement: unselected `if`/`random` branches must not trigger reactions.
Resolved direct damage is recorded without extra RNG and without multiplying dice by the number of direct targets. The
unconditional tree still covers an empty area or a successful save. An interrupted caster who never produced damage does
not create a surface; damage already landed still reacts even if its source subsequently fell.

## Validation

- Full Release solution: **5,784 passed, 0 failed**. Web: **478 passed**. Build: **0 warnings**. The base pack also loads with **0 content warnings**; a deliberately enlarged fuel patch is covered by the warning test.
- Focused surface/content/tooltip/golden checks: **91 passed**.
- Random model: **12 seeds**, default 128 commands per seed, then **2,048 per seed** (24,576 random commands plus pending
  responses and expiry checks). Every command checks invariants, byte-equal direct versus saved/reloaded state and events,
  no tile burning twice, burning tile-rounds ≤ twice initial fuel tiles, and extinction within remaining-patch diameter + 2.
- Every existing golden replay and event file remains byte-identical to milestone A's recorded SHA256 values; all replay
  and round-trip tests pass. Nothing was regenerated. Encounters remain default-off.
- Regression tests were observed failing before fixes for selected-element branches, web origin, invalid surfaces,
  renewable spent fuel, ice surviving a collapse, and interrupted uses.

Actual mutation checks were run one at a time, and source restored and rebuilt afterward:

| Mutation | Test that failed |
|---|---|
| Permit scorched tiles as fuel again | `Fire_on_empty_fuel_spreads_one_tile_per_round_then_scorches_and_cannot_relit` |
| Remove ice insulation | `Conduction_is_bounded_to_six_steps_and_ice_blocks_the_fill` |
| Reverse tile order within conduction layers | `Conduction_event_uses_breadth_first_layers_with_tile_order_within_a_layer` |
| Let condition fire damage ignite fuel | `Fire_condition_ticks_do_not_ignite_fuel_or_create_cascades` |

## Numbers and scope

All gameplay values below are the approved values, in `content/tactics/base/rules/environment.json`:

| Rule | Values |
|---|---|
| Conduction | 6 steps; Dexterity save for half; full lightning dice |
| Burning ground | 1d6 fire, enter or turn start once per turn; 2 rounds; spreads one orthogonal tile each round |
| Ice | 3 rounds; difficult; Dexterity DC 10 on first entry each turn |
| Oil surface | 10 rounds; fuel, not difficult |
| Burning web | 1 round; 2d4 fire on turn start |
| Scorch | Permanent for the battle; never fuel |
| Content warning | Connected fuel patch over 12 tiles |

No new balance values were chosen. There are no TV shots in this milestone: animation and board rendering are milestone E,
phone risk/spread fields are F, and encounter opt-ins and balance simulations are G. Milestone C was gated on A review and merge when B was published. That gate is now fulfilled; see the [C report](milestone-c.md).
