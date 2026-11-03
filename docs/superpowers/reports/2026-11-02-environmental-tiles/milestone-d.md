# Milestone D — loot-only oil flask

Runtime, content and tests: `08fd4050`.

User-facing date: **2026-11-02 (America/New_York)**. The user authorized merging B/C and proceeding with D.
`ccr-18ad4510-q68nad` was fast-forwarded through **`ea90c2e2`** and pushed before this work. D remains on `environmental-tiles`.

## Result

`oil_flask` (Flask of Oil) uses `item.oil_flask`: **one Action**, **range 4**, a **burst of radius 1** (aim and four orthogonal
neighbors, clipped to the map). It can aim at a tile or unit. The splash includes allies and the thrower when they are in it;
each affected unit saves against the thrower's **DEX-based DC**, as Alchemist's Fire does.

The unlit oil is a **10-round**, non-difficult, nondispellable surface. A failed save applies `oiled` for **10 rounds** using
the existing source-turn condition clock. Oil surfaces use the separate round-start clock already implemented in B.
Their different expiry points are tested explicitly; no general duration convention changed.

The next positive fire packet on an oiled creature adds **5 fire before immunity, resistance and vulnerability**, then consumes the coating. Attack/save reductions determine the base packet first. This is one
packet, so resistance/vulnerability apply once, temporary HP behaves normally, lethal damage works, and kill hooks credit the
fire attacker. Zero fire and fire immunity do not consume the coating. Cold does not consume it. No secondary damage hook
or reaction cascade is introduced. The existing R2 surface reaction consumes oil tiles and ignites burning ground.

## Integration

- A `zone.surface` flag reuses the zone effect block and the existing surface rules. Surface kinds are validated against
  `environment.json`; surface traits and clocks come from that definition. Surface-only uses are refused before paying costs
  in a disabled encounter, and the ordinary choices filter omits unavailable uses. Existing zone JSON stays unchanged.
- The content-defined `ConditionDef.DamageTaken` describes the bonus and consumption without hardcoding the oil's strength
  into HP rules. Conditions, tooltips, the effect schema and loader validation describe the new fields.
- AI and phone previews account for the one-use coating through attacks, saves, repeats, conditionals and random branches.
  The scorer retains separate consumed/unconsumed paths, including their damage distributions and on-hit passive state.
  This avoids an impossible double-bonus kill that a marginal remaining-charge estimate could invent. It draws no RNG and
  does not mutate battle state. Ordinary targets retain the existing scoring path.

## Exactly two placements

1. **Ford Assault:** `encounters/ford_assault.json` has one fixed flask reward. A victory awards **one flask to the party**,
   stacked into the first participating hero's bag; losses and flights award none. It is checkpointed with the other rewards,
   so restoring does not award it again. The encounter's existing `goblin_camp` loot-table id remains intact.
2. **Ashen Ford:** one flask was added alongside the existing potion in **`af_reeves_chest`**.

There are no flasks in any starting kit or any other encounter/chest. Encounter loot previously carried only unresolved
loot-table identifiers, so `EncounterDef.itemLoot` adds optional, validated fixed item rewards using the existing `LootDef`
format and shared bag-stacking code. It introduces no new campaign-save field.

The `shops-and-gold` branch exists but **has not landed on the overworld branch**. Its approved **price 10** therefore remains
deferred; this change does not add a price field or merge that separate feature.

## Validation

- Full Release solution: **5,835 passed, 0 failed, 0 skipped**. Web: **478 passed**, no warnings. Builds: **0 warnings**.
- Focused logic/content/AI/golden/catalog checks: **62 passed**. Reward plus existing interaction checks: **36 passed**.
- **18 oil mechanic/preview/AI cases**, **6 content-validation/compatibility cases**, **4 reward/placement cases** and a new
  oil-coated performance case were added. Tests cover both tile/unit aims, all sides' saves, cost/consumption, default-off
  refusal, the four defense cases, lethal damage/kill credit, expiry clocks and save/load, zero damage, multi-hit previews,
  impossible-kill prevention, exact placement counts, starting kits and reward persistence.
- The initial 10 oil tests failed before content/runtime implementation; the Ford victory test failed before reward wiring;
  the four initial preview cases failed before coating-aware forecasts; the impossible-kill test failed before correlated
  consumption paths replaced the marginal estimate. The catalog smoke tests were updated to include the fifth item and
  explicitly enable environmental surfaces for the new surface-only ability. The existing victory-bag assertion now includes Ford’s approved flask reward while retaining the injury/spent-item checks.
- Existing six golden replay/event files are byte-identical; nothing was regenerated. All encounters remain default-off.
- All **six** AI performance cases meet **≤ 250 ms p95** (5 warmups, 100 timed plans, 64 deterministic turn states):

| Case | p95 ms |
|---|---:|
| Oil-coated targets, fire/cold/lightning, oil and spell hazards | 22.2 |
| Environment enabled with surface choices | 40.6 |
| Existing spell-hazard stress | 14.6 |
| Ordinary river stress | 18.5 |
| Crypt Hall | 6.5 |
| Crystal Cave | 7.1 |

[Performance artifact](d-performance.xml) · [Validation and source/golden fingerprints](d-validation.json).
These timings establish the budget on this executor, not a speed comparison across differently loaded runs.

## Approved gameplay values and remaining scope

All new gameplay values are the approved ones: range **4**, burst **1**, one Action and one consumed flask, oil/coating
**10 rounds**, extra fire **5**, and **one** flask at each of the two named placements. They live in `abilities/common.json`,
`conditions.json`, `environment.json`, `encounters/ford_assault.json` and `interactables/ashen_ford.json`. No profile weights
or additional placements were chosen. Existing FX/projectile keys are reused; no art or audio was generated.

E–G remain: TV rendering/animation, phone risk/spread additions, and encounter opt-ins with their balance runs. D does not opt
in encounters or claim those visual/e2e checks. No PR was opened.
