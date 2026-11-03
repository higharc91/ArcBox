# Environmental tiles: build plan

**2026-11-03 completion authorization:** The user asked to finish all remaining steps, review the art again and make a PR: “If there are remaining steps, do all of them.” This authorizes the remaining Ford/cave balance tuning and final polish, superseding the earlier ask-before-tuning and no-PR gates below. Record every final value and validation result in the completion report. Target the PR at `main`, which now contains the overworld and shops/gold work; do not merge the PR without a further instruction.

- **Design:** docs/superpowers/specs/2026-11-01-tactics-environmental-tiles-draft.md. The user answered all 11 questions on 2026-11-02 (the
  table at the end of that doc), so the draft is now the agreed design. **Where this plan and the draft differ, this plan wins:** it
  folds in those answers.
- **Branch:** `environmental-tiles`, cut from `ccr-18ad4510-q68nad`. Work and push there; don't push to other branches.
- **Who:** an implementer who hasn't seen this codebase. Read the design doc fully first (§2 "Today" maps the code you'll touch), then
  `games/Tactics/README.md` (conventions, goldens, the sim) and the rules-engine spec it points to.
- **Rules of the road:**
  - TDD.
  - After every task: `dotnet test` (the solution) and `cd web/play && npm test` are green, with **0 warnings**.
  - Small commits, staged by name.
  - **Balance numbers are the user's.** Use the ones below, and put anything new in the report as a question rather than deciding it.
  - **No art and no generated audio:** reuse the pack's hazard, fx and sfx keys (design §5.1).
- **Known flake:** `ThingShowTests.Fifty_things_cost_a_frame_little_and_allocate_little` fails sometimes under full-suite load. Re-run
  its class alone before counting it.

Environment notes (cloud containers): `export PATH=/root/.dotnet:$PATH`; Godot 4.7.2 .NET may be at
`/root/godot/Godot_v4.7.2-stable_mono_linux_x86_64/`, and `tools/tv-e2e.sh` runs the TV under Xvfb and lavapipe (docs/tv-host.md).
If Godot isn't available, say plainly what wasn't seen.

## The agreed rules (the user's answers applied to design §3)

| Rule | Final |
|---|---|
| **R1 conduction** | Lightning damage on any water tile in the footprint floods over connected, un-iced water, up to 6 steps. **Everyone standing** in it takes the **full** lightning dice, with a DEX save for half: allies and the caster included. **Downed units in the water aren't hit.** |
| **R2 fire** | Fire on fuel makes burning ground. **1d6 fire to a unit that starts its turn in it, or enters it**, at most once per turn. It spreads one tile a round to 4-adjacent fuel, burns 2 rounds, then is scorched for good. Fuel: `planks/*`, `mine_boards/*`, `grass/dry`, `grass/autumn`, `forest_floor/leafy`, oil, **and grease** (user: Grease is flammable, against the SRD). |
| **R3 freeze** | Cold on **shallow water only** (Water, not Deep): an ice sheet for 3 rounds, difficult, and DEX save DC 10 or prone the first time a unit enters it in a turn. Ice insulates (R1 doesn't flow through). Fire thaws it. **Deep water never freezes.** |
| **R4 oil flask** | As designed (thrown, range 4, burst 1 of oil for 10 rounds, DEX save or `oiled`). **Source: loot only** (interactable chests and encounter loot), and later shops (the `shops-and-gold` branch adds prices). **Not in starting kits.** |
| **R5 webs** | As designed: fire burns web tiles away for 1 round, 2d4 to a unit starting its turn there, and frees anyone restrained only by them. |
| **R6 douse** | As designed: a burning unit entering water stops burning; no burning surface on water. |
| **Grease** | A `grease` zone is fuel: fire on it becomes burning ground (and the grease zone's tiles leave it, like R5). |
| **Lava (`hazard` flag)** | Stays harmless until Chapter 5's Hall of the Flame is tuned with it. |
| **Where it's on** | **Per encounter** (`"environment": true`). Nothing else moves. |
| **Phone** | The contract addition is approved: move warnings (`MoveChoice.Risk`/`Warn`) and a spread preview (`TargetChoice.Spread`), all optional fields. |
| **AI friendly fire** | Allowed on **Normal and Hard** when it nets out; **never on Story** difficulty (`Difficulty.Story` in `Tactics.Logic/State/BattleSetup.cs`). |
| **AI hazard avoidance** | Ships **first and on its own**, before any surfaces exist (milestone A). It changes the AI in every encounter, so the sim and bot win rates move: report them. |
| **First content** | River Crossing and Ford Assault opt in. **Ford Assault gains a goblin shaman who casts a cantrip-level lightning spell (1d8).** Then Warlord Bridge, Rime Tower and Forest Ruins (§G.6). |
| **Oil flasks placed** | One in Ford Assault's loot, one in an Ashen Ford chest. |
| **Hazard avoidance lands** | On `ccr-18ad4510-q68nad` as soon as it's reviewed, ahead of the rest. |

## Milestones

Each milestone is its own reviewable chunk: report at the end of each, with commit shas, test totals and the numbers asked for.
**Order matters:** A must land, and its sim report must be read, before C.

### A. AI hazard avoidance (ships alone)

**User's answer:** this lands on the main overworld branch, `ccr-18ad4510-q68nad`, **as soon as it's reviewed and its sim report is in**,
ahead of the rest. Build it first on `environmental-tiles`, in commits that touch nothing else, and report when it's ready. The owner's
session merges it across.

Today the planner values laying zones but never avoids standing in them (design §2.6, §4.7 "Avoid").

1. Add a **tile danger** term to `Planner.Position` (`Tactics.Logic/Ai/`): the expected damage of the zone passives a unit meets by
   **ending its turn** on a tile (`onTurnEnd`, plus `onTurnStart` for next turn), weighed by the profile's caution and the unit's HP,
   like the threat term. Add the **entering** cost along the path for the tiles the planner already keeps (it prunes to 12).
2. Scenario tests in `Ai/PlannerTests.cs`:
   - it doesn't end its turn in Wall of Fire, Spike Growth or Moonbeam when a near-equal safe tile exists;
   - it still steps in when the only way to reach a must-hit target is through them, and the profile is aggressive enough.
3. `AiPerformanceTests` stays at **≤ 250 ms p95**.
4. **Sim:** `Tactics.Sim`, every encounter, levels per the README, 20 seeds, before and after. Report win rate and mean rounds per
   encounter, and flag any encounter leaving 60–95% wins or 8–12 rounds. Then run the sweep harness
   (`docs/superpowers/reports/2026-10-31-tactics-overworld/battle-sweep-harness.cs.txt`, `SWEEP_SEEDS=100`) for crashes.
5. **Goldens:** AI changes may alter golden replays. Regenerate only the goldens whose AI turns changed, review each diff, and explain
   it in the report.

### B. Engine core (nothing opted in, so the goldens are unchanged)

Design §4.1–4.6.
1. `rules/environment.json`, its schema in `rules/schema/`, the loader (`RulesPack.Environment`), and validation in
   `Content/Validation/`. Unknown material families, surfaces or damage types are load errors with a JSON pointer. Warn when a map has a
   connected fuel patch over 12 tiles. List the file in `rules/manifest.json`, which changes the rules hash; that's expected.
2. `Zone.Surface` and `Zone.Source`, both optional with `WhenWritingDefault`. Surfaces tick once a round at round start and can't be
   dispelled.
3. `EncounterDef.environment` (default false), carried into `BattleState`.
4. `ReactStep`: queued last by `Battle.PlanUse`, and serialisable. It runs R1 (as `RunEffectsStep`s with a save for half, in `UnitId`
   order), R2 ignite (grease included), R3 freeze and thaw, and R5. Area footprint: `Areas.Tiles`. Single target: only on a hit.
5. R2 spread in a new `Effects/Blocks/SurfaceRules.cs`, `Surfaces.RoundStart`, called right after `ZoneRules.TickOrphans` in
   `Battle.cs`. R6 in `Motion.Entered`.
6. The `TileReacted(Rule, Source, Tiles)` event, emitted before the ordinary events a reaction causes. Tiles are in animation order
   (breadth first from the source).
7. The invariants in design §4.6.
8. Tooltips generated from content ("Lightning: runs through connected water (6 tiles)").
9. Tests (design §7.2):
   - unit tests per rule on `AsciiMap` pictures;
   - `SurfaceModelTests`: random maps and commands, invariants after each, every fire bounded, no tile burns twice, and save/load equals
     direct apply;
   - mutation checks: write down which test catches each mutation;
   - a test that **every existing golden is byte-identical** while nothing opts in.

### C. AI uses reactions (after A's report)

Design §4.7 "Exploit":
- R1's expected damage over every conducted unit, through `Worth` (friend against foe);
- R2 and R4 per fuel tile lit next to foes;
- R3's prone odds.
- **On Story difficulty, never pick a use whose reaction would damage an ally**; on Normal and Hard, follow `Worth`.
- Scenarios: it casts lightning into the ford with two foes and no ally there; it doesn't with an ally there on Story; on Normal it
  may if the net value is positive.
- Performance ≤ 250 ms p95, with the flood fill cached per (footprint, water state).

### D. Oil flask

- The `oil_flask` item (`item.oil_flask`), the `oiled` condition, and R4 surfaces.
- **Not in any starting kit.** **User's answer: a few placed.**
  - One goes in Ford Assault's encounter `loot`.
  - One goes in an Ashen Ford chest: add it to the loot of `af_reeves_chest` in `rules/interactables/ashen_ford.json`, or a new
    container that passes validation.
  - Add no more without asking.
- If the `shops-and-gold` branch has landed by then, give it a price of 10 in `items.json`, its §3.1 number.

### E. TV

Design §5.1.
- `TileReacted` beats in `Beats/BeatScript.cs`, with timings in `BeatTiming`: a conduct ripple at about 40 ms a tile, ignite and spread
  flames, freeze, thaw and douse puffs, burning webs. Each finishes in under 1 s.
- `ZoneArt` keys for `burning`, `ice_sheet`, `oil` and `scorched`, and the `scorch/burnt` decal drawn by `BoardPlan.AddZones`.
- Sounds come from the existing sfx keys only.
- Tests: `BeatScriptTests` and `BoardPlanTests`.
- On a real TV run (`tools/tv-e2e.sh` or a dev battle): shots of a lightning ripple through the ford, a burning bridge deck and a frozen
  ford, judged in motion.

### F. Phone

Design §5.2. **Approved by the user.**
- `MoveChoice.Risk` (expected damage) and `Warn` (a short string). `TargetChoice.Spread` (the tiles a reaction would touch). All
  optional, so old phones ignore them.
- Conducted units appear in `TargetChoice.Affected`.
- In `web/play/src/games/tactics/`:
  - the move button reads "Move here · 3 · burns ~4";
  - risky reach tiles get an amber tint;
  - spread tiles get a dashed second aim colour.
- Regenerate fixtures with `UPDATE_FIXTURES=1` via `Tactics.Game.Tests/PhoneFixtureTests.cs`. **Fixtures for encounters without
  `environment` must stay byte-identical**: only emit the new fields when they're non-empty. Add web tests.

### G. Content and balance

1. Opt in `river_crossing` and `ford_assault` (`rules/encounters/*.json`, `"environment": true`). No map edits, so the world stamp
   still matches.
2. **Ford Assault's goblin shaman casting lightning** (user's choice):
   - The `goblin_shaman` in `rules/monsters/goblinoids.json` has Cinder Bolt (fire). **User's answer: cantrip-level, in addition to
     Cinder Bolt.** Add a new shaman ability, such as "Storm Hex": a ranged lightning spell attack, **1d8 lightning**, at Shocking
     Grasp's strength. Use a variant of the goblin shaman for Ford Assault, or give the ability to the base shaman, whichever the
     content conventions prefer. Its danger is conduction through the ford, not its dice.
   - Add the shaman to `ford_assault.json`'s enemies on the far bank.
   - Both are balance-visible: report the sim delta and ask before raising anything.
3. Sim at 20 seeds, levels 1, 3 and 5, environment on and off for both encounters. The win rate must stay in 60–95% and mean rounds in
   8–12. Report the new tallies in `Report.cs`: reactions per battle, friendly damage, burning tile-rounds, and deaths to surfaces.
4. A new golden, `river-ford-storm.replay.json`: lightning into the ford, a freeze, a douse.
5. Update the README's conventions (the area ladder doesn't count conduction) and add a balance report section.
6. **User's answer: all three showcases too** (design §6.2–6.4), each opted in only after the sim for it is in band:
   - **Warlord Bridge** (level 5): the oak deck is fuel. Whether a burnt deck drops on its own stays a later trigger question; don't
     add that.
   - **Rime Tower** (`crystal_cave`, level 7): a web zone on `spider_nest` at battle start, using the existing `effects` trigger
     action.
   - **Forest Ruins** (level 3): repaint a patch of `grass/lush` to `grass/dry` or `forest_floor/leafy`.
   - Each gets a sim run (20 seeds, at its level ±2) with environment on and off. Report them, and ask before tuning anything that
     falls out of band.
7. Run the Chapter 1 e2e (`ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force tools/tv-e2e.sh`) and confirm Ford Assault still plays through to the
   card.

## What can be cut, in order

Design §9's list. The smallest version worth shipping is **A + R1 + R2 (decks and grease) + R6** on River Crossing and Ford Assault.

## Done means

All milestones are reported, with:
- the sim reports for A and G;
- the goldens explained;
- every new number listed for the user, with the file it lives in;
- the TV shots, or a plain note of what wasn't seen;
- the open questions at the end.

Everything is pushed to `environmental-tiles`, with **no PR** unless the user asks.
