# Milestone C — AI uses environmental reactions

Issue #69. Runtime and tests: `ba68f50f`. On 2026-11-03 the user authorized the A-only merge and continuation. `ccr-18ad4510-q68nad` was fast-forwarded
from `340e1de5` through `dccfab9f` and pushed before C began. B remains in separate commits on `environmental-tiles`.
This fulfills the plan's A review/landing gate without merging the default-off engine into the overworld branch.

## Behavior

- Lightning's conducted damage is scored for every standing secondary target through the existing `Worth` function.
  That includes the caster and allies, honors saves, immunity/resistance and Evasion, and excludes downed and direct targets.
  The secondary save uses the caster's DC and does not inherit spell shaping. The full elemental dice are carried by water;
  a direct critical hit does not double them again.
- Single-target attacks use the existing attack, cover, advantage and critical odds. The shared attack-odds calculation was
  extracted without changing direct attack scoring. Repeated attacks and selected conditional/random element branches are
  considered analytically. Zero-weight random entries do not create a risk. Planning does not roll or mutate battle state.
- Fire values igniting native fuel, oil and grease near foes. Scorched ground is not fuel. Ice values the actual fixed-DC
  slip odds and difficult terrain, including the value of foes near a new sheet.
- **Story forbids any positively probable reaction damage to an ally or the caster**, including a random lightning branch.
  It also follows the deterministic connected fuel patch to avoid fire spreading onto a distant ally's present tile.
  Normal and Hard continue to weigh friend against foe through `Worth`. Immune and downed allies do not block conduction.
- Empty water can be an aim when the conducted foes are outside direct spell range. Empty fuel/ice aims near foes are also
  considered. Geometry is cached by footprint, reach, and the immutable zone/sunk state, independently of caster position.
- The existing direct-area score cache remains in place, but reactions and Story safety are recomputed at each hypothetical
  caster position. Spells that can change surfaces are not followed by a walk planned against the pre-spell terrain.

Surface value uses the existing zone heuristics: a **3-round maximum** horizon, **0.5** entry/adjacency weight, and **0.3 ×
control** for difficult terrain. Actual lifetimes, dice, DCs and conduction reach come from `environment.json`. Damage,
kill, friendly-fire, objective and control weights are the existing profile/`Worth` values. No new balance weights or content
numbers were chosen; no profiles, encounters, maps, art, phone contracts or item placements changed. Nothing opts in yet.
The fire horizon is a heuristic over current positions, not a simulation of future unit movement.

## Validation

- Full Release solution: **5,806 passed, 0 failed, 0 skipped**. Web: **478 passed**, no warnings. Release build: **0 warnings**.
- **21 new scenarios** plus existing planner/golden checks: **59 passed**. **11 of the 21** fail against the isolated pre-C
  B assembly. The distant-ally spread case was also observed failing before its additional Story guard was implemented.
- Existing six golden replay/event files are byte-identical to A/B; nothing was regenerated.
- All **five performance cases** pass **≤ 250 ms p95**, with 5 warmups and 100 measured plans over 64 deterministic turn states:

| Case | Median ms | p95 ms | Max ms |
|---|---:|---:|---:|
| Environment enabled, 6 vs 10, lightning/fire/cold choices, oil and spell hazards | 6.0 | 56.4 | 72.7 |
| Existing spell-hazard stress | 4.1 | 20.9 | 40.4 |
| Ordinary river stress | 4.5 | 16.9 | 27.3 |
| Crypt Hall level 5 | 3.6 | 6.0 | 10.1 |
| Crystal Cave level 7 | 3.6 | 6.6 | 10.2 |

These measurements validate the budget on this executor; they are not a speed comparison with A's runs under concurrent
simulation load. [Performance output](c-performance.xml) preserves the measured results. [Validation metadata](c-validation.json)
records the tested source hashes, counts and unchanged golden hashes.

The next milestone is D: the approved loot-only oil flask. TV/phone integration and encounter opt-ins remain E–G; their
visual checks and opt-in balance simulations are still pending those milestones. No PR was opened.
