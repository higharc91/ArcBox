# ArcBox Tactics Rules Engine: handoff guide

This guide is for an agent or person picking up the rules-engine plan partway through. Read these in order:

1. **This guide:** how the work is run, and what to know before the next task.
2. **[`2026-10-30-tactics-rules-engine.progress.md`](2026-10-30-tactics-rules-engine.progress.md):** the status of every task, the binding additions to the plan, and the deferred findings.
3. **[`2026-10-30-tactics-rules-engine.md`](2026-10-30-tactics-rules-engine.md):** the plan. Each task's section is its brief.
4. **[`../specs/2026-10-30-tactics-rules-engine-design.md`](../specs/2026-10-30-tactics-rules-engine-design.md):** the spec. Where the plan and spec disagree, the spec wins.

**Branch:** `ccr-472d8660-sxpjlj`. Everything is committed and pushed there.

**Before starting work:**
- **Toolchain:** run `export PATH=$HOME/.dotnet:$PATH`. On the Windows checkout the SDK is at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`; the `dotnet` first on PATH there has no SDKs.
- **Tests:** `dotnet test ArcBox.slnx` must pass with no warnings, because warnings are treated as errors.

## How the work has been run

The plan is run with **subagent-driven development** (the `superpowers:subagent-driven-development` skill), one task at a time. For each task:

1. **Brief.** Take the task's section from the plan, either with the skill's `scripts/task-brief PLAN N` or by copying it by hand.
2. **Implement.** Hand the brief to a fresh implementer, together with three things:
   - the **implementer instructions** (below);
   - the **global constraints**: the plan's "Global Constraints" section plus the controller rulings (below);
   - the tracker's **binding additions**.

   The implementer works test-first, writes failing tests before the fix, runs the full suite, commits with the attribution trailer, and writes a report.
3. **Review.** A fresh reviewer checks the diff from the commit before the task to its last commit, using the **reviewer instructions** (below). It gives two verdicts: whether the work matches the brief, and its code quality. It reads the diff from a file.
4. **Fix rounds.** Any Important or Critical finding goes back to the same implementer as a fix round. A fresh reviewer then re-reviews only the fix diff, using the **re-review instructions** (below). The cap is 5 rounds. No task has needed more than 1.
5. **Finish the task.**
   - Minor findings go to the tracker's deferred table.
   - Each decision you make on the plan's behalf is recorded as a "Ruling".
   - Mark the task done in the tracker, commit, and push.

**Models used so far:**
- **Implementers:** opus for engine and content tasks, sonnet for small ones.
- **Reviewers:** opus for large diffs, sonnet for the rest.
- **Re-reviews:** sonnet or haiku.

**Two runs touched assets outside the plan:** the Barbarian/Sorcerer sprites and the Sorcerer signature pieces.
- Both changed `tools/tactics-assets`, the asset library, rather than the rules engine.
- Both ran in an isolated git worktree and were merged afterwards.
- A full asset build takes about 11 minutes and is deterministic.

**Commit trailer:** every commit message ends with:
```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UZNTTVCBKLVWRDHcmTGbSi
```

## What's next

The tracker's status table is authoritative; check it first. Tasks 1–22 are done, and Task 23 (balance pass) is implemented but needs fixes. **Start from [the Task 23 review](2026-10-30-tactics-rules-engine.task-23-review.md)**, which has the findings and the steps to resume; the [implementer's report](2026-10-30-tactics-rules-engine.task-23-report.md) is beside it. Then run the whole-branch review.

**Remaining tasks, with notes earlier work left for them:**

| Task | Notes for whoever picks it up |
|---|---|
| 18: Bard, druid, warlock | Before writing Hex, add a recursion guard to the AI's `QuarryDamage` (`Ai/Scoring.Effects.cs`): a passive that re-applies its own required condition would recurse until the stack overflows. Warlock Pact regain uses the passive route (`onRoundStart` + `if` even round + `grantResource`), not the `resourceRegen` feature. Set `primaryResource` and `"subclassesPending": true`. Wild Shape uses the `transform` hook with beasts in `monsters/wildshape.json`. |
| 18A: Barbarian, sorcerer (added mid-plan) | Add an `unarmoredDefense` stat to `ClassDef` and `SheetBuilder`, with a test. Add a `metamagic` named hook for Twinned, Extended, Careful, Quickened and Distant. Rage is a `statMod` with resistance and bonus damage. Spec §4.5 has the class tables. |
| 19: Martial subclasses (16) and the hound | Avenger needs the same `QuarryDamage` guard (see Task 18). Includes Berserker, Earthshaker and Spiritwarden. Avenger needs source-specific `marked` advantage (an engine addition). Duelist and Uncanny-Dodge-style effects need damage-halving reactions (an engine addition). Remove `subclassesPending` from these classes. The hound is summoned `onBattleStart`. |
| 20: Caster subclasses (16), sapling and imp | Includes Draconic, Chaos (the `random` block) and Runeborn (rune zones with `onEnterTile` passives). Remove the `subclassesPending` support from validation. Chronomancer uses `turnOrder`; Geomancer uses `alterTerrain`. |
| 21: Monster roster, maps, encounters | Every enemy, beast and boss in the art manifests needs a stat block. Add an optional intended `level` to encounters and make the simulator's win-rate flag use it. The simulator's damage upper-bound test assumes enemies don't heal, so revisit it if healers are added. |
| 22: Objectives, triggers, showcase | Trigger `do` effect blocks are only key-checked today; fully deserialise and resolve them. Objective scoring in the AI covers only protected units, so extend it for exits and the like. |
| 23: Balance pass | Use the tracker's deferred table (the SRD approximations row) and the simulator reports. Fighter manoeuvres with Extra Attack and Feint are too strong. The AI's caution-fade constant is a tuning knob. |
| Final review | Run one whole-branch review on the most capable model, triage the deferred table, and fix everything in one wave. Check the AI performance test for flakiness: one unnamed test failure was seen once. |

**Notes for the game adapter** (`TacticsGame`, sub-project 3), recorded here so they aren't lost:
- **Absent members:** pass `AllowItems: true` for them.
- **Reactions:** answer an unanswered player reaction prompt with `Ai.React(state, state.Pending offer, rules)`.
- **Timeouts:** apply timed-out turns with `aiFinished: true`.
- **Art layers:** read the art layer order from the pack. The `float` and `shoulders` layers are new.
- **Content hash:** a battle save carries it; restart the battle if it differs.

## Every ruling made so far

These are the decisions the controller made on the plan's behalf, in order. Each one says what it costs if wrong.

- | T4 ↔ T8 | T4 `static class Battle` (Start/Apply); T8 adds `Battle.Result` | Two declarations → Ruling below |
- | T4 ↔ T10 | T4 `BattleState.Zones: ImmutableArray<Zone>`; T10 defines `Zone` | Type used before defined → Ruling |
- | T6 ↔ T8 | T6 deep tile "Downed (countdown 3)"; T8 owns Downed rules | Order conflict → Ruling |
- | T9 ↔ T12 | T12 uses `ClassDef.AiTags.Priority`; T9 lists `AiTags` untyped | Underspecified → Ruling |
- | T16 ↔ tests | Tests reference Tactics.Sim (Exe) | Allowed in .NET; see Ruling |
- `Battle` is a `static partial class` across Rules/Battle.cs and later files — two tasks add members — cost if wrong: none (rename only).
- Task 4 declares `Zone` with the exact shape listed in Task 10 (Serial, Owner, Kind, Tiles, RoundsLeft, Concentration, Difficult, Passives) so BattleState compiles; Task 10 implements behaviour — cost if wrong: small record edit.
- Task 6 sets `Life = Downed, Countdown = 3` directly for deep-tile party members (LifeState exists from Task 4); Task 8 adds ticking/revive/Out — cost if wrong: Task 8 re-routes through Downed.Down().
- `ClassDef.AiTags` is `sealed record ClassAiTags(ImmutableArray<string> Tags, ImmutableArray<Stat> Priority)` — Priority orders the standard array assignment — cost if wrong: content field rename.
- Tests may reference the Tactics.Sim Exe project; if the SDK warns (warnings are errors), move SimRunner/Report into a `Tactics.Sim` class library + thin console is NOT allowed — instead set `<ReferenceOutputAssembly>`-free reference per SDK guidance or put SimRunner/Report in Logic under `Sim/` namespace — cost if wrong: file move.
- Task 1: minor (deferred): InjuryId/ItemId/LootTableId lack IComparable — Task 4 adds ordinal IComparable to them (state uses sorted collections) — cost if wrong: none
- Task 1: minor (deferred): id converters accept null/"" and throw InvalidOperationException on wrong token types; TileJsonConverter same — Task 9 loader must reject empty ids and wrap non-JsonException parse errors as ContentError — cost if wrong: poorer error messages
- Task 2: path tie-break is (cost, ordinal Tile) via queue order; plan's "expand N,E,S,W" has no observable effect — plan intent was determinism, which holds — cost if wrong: different (equally short) path choice, golden replays re-recorded
- Task 2: minor (deferred): FromBytes doesn't guard default arrays / prop & zone bounds / flag bits — Task 9 loader validates props and zones in bounds and flag bits — cost if wrong: crash on bad map content
- Task 3: prop-adjacency cover can yield Total while HasLineOfSight (rays) is true; content props may only declare none/half/three_quarters (spec §4.8), so Task 9 validation rejects cover "total" on props; Total only arises from rays — cost if wrong: a test-only prop could make an untargetable-but-visible unit
- Task 4: Delay only before spending anything, once per round; new rejection CantDelay "You can only delay at the start of your turn, once per round."; resumed turn skips turn-start hooks (StartedThisRound) — spec §3.2 says Delay uses none of the turn's economy — cost if wrong: Delay feels restrictive (rule change only)
- Task 4: BattleWriter public — later public APIs take it — cost if wrong: API surface wider than needed
- Task 4: BattleState gains Xp, Loot, Difficulty, Triggers now — avoids Task 8/22 churn — cost if wrong: none
- Task 4: ordinal JSON converter approach accepted and added to global constraints for later tasks
- Task 4: resumed (delayed) turn resets Move/Action/Bonus but keeps Reaction as spent — prevents refunding an OA reaction — cost if wrong: minor rules tweak
- Task 5: AreaDef JSON uses a single "size" key (spec examples' radius/length/arm become size) — one key keeps schema simple — cost if wrong: content key rename
- Task 5: tile-kind abilities whose area holds no units still need a run — Task 10 adds a once-per-ability "RunAt aim" path for tile-semantic blocks (zone, alterTerrain, hazard, summon, teleport/dash/jumpTo self) — cost if wrong: Task 10 scope grows slightly
- Task 5: until-save apply outside a save block is a load-time error (Task 9 validation)
- Task 5: static modifiers in attack totals are Task 7's (Modifiers.For) — plan assigns them there — cost if wrong: none
- Task 6: beforeHit/spellCast reaction offers are raised in Task 7 (attack block after d20, before outcome; Use of a tiered ability) — Task 6 only wires leavesReach — cost if wrong: Task 7 slightly larger
- Task 6: walking down a ledge is free; falling damage only from pushes/pulls (spec §3.5 "Dropping down any height is allowed") — cost if wrong: rule tweak
- Task 6: a halted move on a shared tile returns the mover to its last solely-held path tile; halted moves drop their remaining MoveSteps — keeps one-unit-per-tile invariant — cost if wrong: mover ends a tile back
- Ruling note: Out units are removed from the map's occupancy (Task 8 must make Out/Dead units not occupy tiles; occupancy = Up or Downed everywhere)
- Task 7: a condition does not tick down at the end of the source turn in which it was applied ("rounds: 1" = until the end of the source's next turn, 5e semantics; matches spec §4.2 Guiding Bolt) — cost if wrong: content durations off by one
- Task 7: Downed/incapacitated casters lose concentration — Task 8 implements (with Downed) — cost if wrong: none
- Task 7: "rounds: 0" = until the end of the source's current turn (for Disengage-style self conditions); rounds ≥1 skip the applying turn's tick — cost if wrong: content duration tweak
- Task 7: PassiveDef gains Target (Other|Self, default Other); Other skips when no other unit or other == owner; condition passives act as the condition's Source — prevents self-retaliation recursion — cost if wrong: passive content field
- Task 7: new event AttackResolved(Source, Target, Hit, Crit) emitted after Resolve — the TV needs the final outcome after Shield/crit re-judging — cost if wrong: one extra event type
- Task 8: XP is full on Won and half (rounded down) on Lost/Fled — spec silent on losses; a losing week should still progress but less — cost if wrong: one constant
- Task 8: accepted: Up unit may stand in deep (revived there, can swim out); ally/enemy/any_unit targeting includes Downed units (hits tick countdown, heals revive); summons die rather than Down; protected NPCs Down but get no XP; Settle step cap 100,000 → invariant
- Task 8: dropped mover's MoveSteps are NOT removed — MoveStep's own Halt handles Downed movers and returns them off shared tiles — cost if wrong: none
- Task 9: content JSON rejects unknown properties (loader uses a copy of ArcJson.Options with UnmappedMemberHandling.Disallow) — typos in ~300 abilities must not pass silently — cost if wrong: stricter authoring
- Task 9: split Validation.cs (~1050 lines) by concern into Content/Validation/*.cs — cost if wrong: none
- Task 9: built-in common.attack/common.help presentation keys become real art keys in Task 13 (e.g. characters.<anim>) — cost if wrong: none
- Task 9: monster aiProfile refs validated only once a pack defines AI profiles (Task 13 precedes Task 14) — Task 14 makes it strict
- Task 9: ContentHash stays hash-of-digests (per-file SHA-256 over path+NUL+bytes, then SHA-256 over sorted digests), documented — deterministic, nothing external recomputes it — cost if wrong: hash value changes
- Task 10: monsters referenced by named hooks / summon / animate_dead / polymorph must be checked at load (Validation) — runtime throw breaks a battle — cost if wrong: none
- Task 10: effect lists keep authored order — contiguous tile-block runs execute once at aim, unit-block runs per unit, in order — content authors expect [attack, teleport] to attack first — cost if wrong: none
- Task 10: block-level concentration:true without cost.concentration is a load error
- Task 10: accepted: extra events TempHpGained and Countered; turnOrder only moves units still waiting this round; chain stays on first target's side, each bounce a new hit; dispel/reveal run at aim
- Task 11: accepted: '1 tile' singular; basic weapon attack header 'Weapon attack, weapon range'; monster names derived from ids; 'target' wording on self abilities — polish deferred
- Task 11: minor (deferred): statMod tooltip grammar ("target gets can't act") — Task 13 fixes it since its content hits it; HookPhrase not exhaustive-tested; save "half on a save" clause misses nested damage; Group bracketing ambiguity; zone passive "your" wording; number formatting style
- Task 12: weapons.json/armor.json are content kinds (WeaponDef existing; ArmorDef(Id, Name, Kind, BaseAc)); shield is a flag; MonsterSheet lookup delegates to SheetBuilder.ForMonster; injuries applied via a route Modifiers.For honours — plan had no source for weapon/armour data — cost if wrong: content file layout
- Task 12: warlock pact regen uses the plan's passive route (onRoundStart + if roundEven + grantResource), so FeatureRef `resourceRegen` needs no engine support — Task 18 must not rely on it — cost if wrong: none
- Task 12: accepted: StaticMods.MaxHp (sheet-only; rejected on conditions); injuries surface as `injury:<id>` passives via RulesPack.PassiveFor; EncounterUnitDef optional Id; heavy-armour −2 stays in TurnOrder (not baked); defaultPicks = whole loadout at highest level ≤ character level; placement tag added to sheet Tags; score cap 20
- Task 12: engine trusts CharacterRecord scores; point-buy budget is validated by the portal (sub-project 4, spec §9)
- Task 12: Task 13 rider — CheckPicks enforces counts per choose feature, and class defaultPicks are validated at load with CheckPicks; Encounters place `at` units before zone fills — Task 13 content depends on both — cost if wrong: none
- Task 13: items need a real use path now (Task 15 companion potion and the Use Item action depend on it): Use command gains optional ItemId Item; common.use_item runs the item's Use ability, consumes one from the Bag, records ItemUse(ByAi) — goes into Task 13 fix round — cost if wrong: command shape
- Task 13: StaticMods gains save edges (advantage/disadvantage per stat) so Dodge/Danger Sense/Lucky-style effects stop using ±2 approximations — Task 13 fix round — cost if wrong: small
- Task 13: deferred: source-specific `marked` advantage (Avenger) and damage-halving reactions (Uncanny Dodge) — engine additions land with the subclasses that need them
- Task 13: smoke bomb gets a real LOS-blocking zone flag `blocksSight` honoured by Sight.Trace — brief requires LOS blocking — cost if wrong: small engine addition
- Task 13: new rejection NoItem "You don't have that item."; ClassDef.primaryResource for grantResource "primary"
- Task 14: split Ai/Planner.cs (808) and Ai/Scoring.cs (645) by concern in the fix round — plan's one-responsibility rule — cost if wrong: none
- Task 14: accepted: caution fades after round 5 to 30% (prevents stalemates; balance knob for Tasks 16/23); planner never Delays (AllowDelay inert); objective scoring limited to protected units until Task 22; Battle.Check public legality helper; PathMap.Reachable indexing bug fixed (Task 2 regression test added)
- Task 15: Ai.React must keep the plan's signature (state, offer, rules) — the adapter only holds the pending offer; store the ReactionTrigger on ReactionOffer (serialisable) if React needs it — cost if wrong: offer JSON shape
- Task 15: accepted: sheets carry `class:<id>` tag; EV>0 rule applies to every AI reaction incl. counterspell; defensive = self-targeted reactions turning hit→miss or temp HP ≥25%; AllowItems stays master switch (adapter passes true for absent members); AiFinished init-only on BattleEvent base, omitted when false; planner: bonus actions drive movement once Action spent
- Task 16: EncounterDef gains optional intended `level` in Task 21 (sample encounters), and the Sim win-rate flag uses it; until then it judges every level — cost if wrong: none
- Task 16: an intermittent unnamed Tactics test failure was seen once; attributed to AiPerformanceTests p95 under CPU contention; SimSmokeTests moved to a non-parallel collection — final review must check perf-test flakiness (consider a looser CI budget or Perf trait filter) — cost if wrong: flaky CI

## Controller rulings given to implementers

These are kept verbatim, as appended to the global constraints.

### Controller rulings (binding)
- `Battle` is a `static partial class`; later tasks add members in their own files.
- Task 4 declares `Zone(int Serial, UnitId? Owner, string Kind, ImmutableArray<Tile> Tiles, int? RoundsLeft, bool Concentration, bool Difficult, ImmutableArray<PassiveDef> Passives)`; if PassiveDef doesn't exist yet in Task 4, declare a minimal placeholder `PassiveDef` record in Content/Defs.cs that Task 7 fills in.
- Task 6 sets `Life = Downed, Countdown = 3` directly for party members pushed into deep tiles; Task 8 owns the rest of the Downed rules.
- `ClassDef.AiTags` is `sealed record ClassAiTags(ImmutableArray<string> Tags, ImmutableArray<Stat> Priority)`.
- Build/test: `export PATH=$HOME/.dotnet:$PATH`; `dotnet test ArcBox.slnx` (full) or `dotnet test games/Tactics/Tactics.Logic.Tests` (focused).
- Commit trailer (exact, as the last lines of every commit message):
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01UZNTTVCBKLVWRDHcmTGbSi
- Commit locally only; do NOT push (the controller pushes).
- Ordinal string collections: System.Text.Json rebuilds ImmutableSorted{Set,Dictionary}<string,…> with a culture comparer. Use `Core/Ordinal.cs` (`Ordinal.Set`/`Ordinal.Map`, `[property: JsonConverter(typeof(OrdinalJsonConverter))]`) on every string-keyed sorted property; never `.Empty`/collection expressions for these.
- `BattleWriter` is public (documented as the engine's working copy for effect blocks), so Tasks 5–7 public APIs may take it.
- Delay: allowed only at the start of the unit's turn (nothing spent) and once per round; otherwise rejected with code `CantDelay`, text "You can only delay at the start of your turn, once per round." (also for no later slot / Slots < 1). A resumed (delayed) turn emits TurnStarted again but turn-start hooks and condition ticks run only on the first start of that unit's turn in the round (state tracks `StartedThisRound`).
- BattleState stores the setup's `Xp`, `Loot`, `Difficulty` and `Triggers` (Task 8 Result and Task 22 need them).
- AreaDef JSON key is "size" for every shape. Stat and AreaShape serialise lowercase/snake_case. Effect-list properties must be typed `EffectDef` (the converter keys off it).
- Tile-kind abilities: Task 10 adds a once-per-ability run at the aim tile for tile-semantic blocks, even when the area holds no units.
- Reaction abilities are tagged `"reactsTo:<trigger>"` in AbilityDef.Ai.Tags (triggers: leavesReach, beforeHit, spellCast). Task 7 raises `beforeHit` offers in the attack block (after the d20 is rolled, before hit/miss is applied, so Shield-like reactions can change the outcome) and `spellCast` offers when a tiered ability is used.
- Walking down a ledge is free; only pushes/pulls cause fall damage. A push stops at a wall, a unit, a tile ≥2 levels higher, a fall > 3 levels, or a deep tile.
- Occupancy: a tile is occupied only by Up or Downed units, everywhere (Out and Dead units occupy nothing).
- Condition rounds: a condition does not tick at the end of the source's turn in which it was applied; "rounds: 1" lasts until the end of the source's next turn.
- Task 8: becoming Downed, Out, Dead or Incapacitated ends that unit's concentration (linked conditions/zones/summons removed).
- Condition rounds 0 = until the end of the source's current turn. PassiveDef.Target (Other|Self, default Other). Event AttackResolved(Source, Target, Hit, Crit) follows every attack's Resolve.
- Task 8: a skipped turn (Downed/Out/Incapacitated) still runs that unit's end-of-turn condition ticks/saves, so conditions it sourced still expire.
- Content: passives whose effects affect their owner (turn/round/battle start, move hooks) must set "target": "self" (default Other is skipped when there is no other unit).
- Weapons and armour are content (`weapons.json`, `armor.json`); `ArmorDef(Id, Name, Kind, BaseAc)`; a shield is `CharacterRecord.Shield` (+2 AC).


## Implementer instructions (verbatim)

### Implementer instructions (read fully)

You are implementing one task of the ArcBox Tactics rules engine: a deterministic C# battle-engine library in /home/user/ArcBox (branch ccr-472d8660-sxpjlj), under games/Tactics/. Your task brief (path in your dispatch) is your requirements, with the exact values to use verbatim. The binding project-wide constraints are in global-constraints.md beside it — read it; the "Controller rulings" at its end are binding.

Before you begin: if anything about requirements, approach or dependencies is unclear, report NEEDS_CONTEXT with your questions instead of guessing.

Your job:
1. Implement exactly what the task specifies. TDD: write the failing tests first and capture RED evidence, then implement and capture GREEN.
2. While iterating, run the focused test project (`export PATH=$HOME/.dotnet:$PATH; dotnet test games/Tactics/Tactics.Logic.Tests`). Run `dotnet test ArcBox.slnx` once before committing — it must be green with no warnings (warnings are errors).
3. Commit locally with the brief's commit subject plus the exact trailer from global-constraints.md. Do NOT push.
4. Self-review your diff: completeness, edge cases, naming, YAGNI, existing patterns, tests that verify real behaviour, pristine test output. Fix what you find before reporting.

Code organisation: follow the plan's file structure; one clear responsibility per file. If a file grows beyond the plan's intent, report DONE_WITH_CONCERNS rather than splitting on your own. Match the style of the existing Tactics.Logic code (XML doc comments on public types, sorted immutable collections, no System.Random/DateTime).

You do not dispatch subagents — never spawn helpers or reviewers. Review is the controller's job.

When in over your head (architectural choices the brief doesn't settle, uncertainty about correctness), stop and report BLOCKED or NEEDS_CONTEXT with specifics. Bad work is worse than no work.

After review findings: you may be resumed with findings. Fix them, re-run the covering tests, append a fix report (what changed, tests run, command, output) to your report file, and reply with the same short contract.

Report: write the full report to the report path in your dispatch — what you implemented, tests and results, TDD evidence (RED command + relevant failing output and why expected; GREEN command + passing output), files changed, self-review findings, concerns. Then reply with ONLY (<15 lines): Status (DONE | DONE_WITH_CONCERNS | BLOCKED | NEEDS_CONTEXT), commits (short SHA + subject), one-line test summary, concerns, report path. If BLOCKED/NEEDS_CONTEXT, put the specifics in the reply.


## Reviewer instructions (verbatim)

### Task reviewer instructions (read fully)

You are reviewing one task's implementation: first whether it matches its requirements, then whether it is well-built. This is a task-scoped gate, not a merge review — a broad whole-branch review happens after all tasks. Repo: /home/user/ArcBox.

Inputs (paths given in your dispatch): the task brief (what was requested), global-constraints.md (binding project-wide values and "Controller rulings" — read the bullets relevant to this task), the implementer's report, and the diff file.

Read the diff file once — it has the commit list, stat summary and full diff with context; it is your view of the change. Do not Read changed files separately unless a hunk you must judge is cut off (say so). Do not re-run git commands. Inspect code outside the diff only to evaluate a concrete risk you can name — one focused check per named risk; name the risk and what you checked. Read-only: never mutate the working tree, index, HEAD or branches.

You do not dispatch subagents. Do the whole review yourself.

Do not trust the report: verify its claims against the diff. Stated rationales ("kept it simple", "YAGNI") never downgrade a finding.

Tests: the implementer ran them with TDD evidence. Do not re-run the suite. Run a focused test only when a specific named doubt needs it. Warnings/noise in the reported test output are findings.

Part 1 — Spec compliance: Missing / Extra / Misunderstood vs the brief, with file:line. Requirements you cannot verify from the diff alone → ⚠️ items.
Part 2 — Code quality: separation of concerns, error handling, DRY, edge cases, tests verifying real behaviour (not asserting nothing), file structure per the plan, new files already large.

Calibration: Important = the task cannot be trusted until fixed (incorrect/fragile behaviour, missed requirement, merge-blocking maintainability damage, tests that assert nothing). Coverage/polish = Minor. If the brief mandates a defect, report it Important labelled plan-mandated.

Output (your final message IS the report; start directly with the verdict; every line a verdict, a finding with file:line, or a check you ran):
### Spec Compliance
- ✅ Spec compliant | ❌ Issues found: …
- ⚠️ Cannot verify from diff: …
### Strengths
### Issues
#### Critical (Must Fix)
#### Important (Should Fix)
#### Minor (Nice to Have)
### Assessment
**Task quality:** Approved | Needs fixes
**Reasoning:** 1-2 sentences


## Re-review instructions (verbatim)

### Scoped re-review instructions (read fully)

You are re-reviewing one task's fix round in /home/user/ArcBox. A previous review produced findings; the implementer attempted fixes. Verdict each finding and inspect the fix diff — nothing else.

Inputs (paths in your dispatch): the task brief, the findings list (in your dispatch), the implementer's report (fix reports appended at the end — treat as unverified claims; confirm it names the covering tests and shows their output), and the fix diff file (fix commits + stat + diff with context). Read the diff once; do not re-run git commands. Read-only: never mutate the working tree, index, HEAD or branches. Do not dispatch subagents.

Scope: verdict every finding; inspect the fix diff for new problems the fix introduced. Do NOT re-review code the fix did not touch — issues outside it go under Out-of-Scope Observations (non-blocking). Do not re-run the suite; run a focused test only for a specific named doubt.

Output (your final message IS the report; begin with the first verdict):
### Finding Verdicts
- **[finding one-liner]** — ADDRESSED | NOT ADDRESSED, with file:line evidence ("attempted" is not addressed).
### New Breakage in the Fix Diff
(severity Critical/Important/Minor, file:line; "None" if clean)
### Out-of-Scope Observations
("None" if none)
### Verdict
**Fix round:** All findings addressed, no new Critical/Important breakage | Findings remain open — list them.

