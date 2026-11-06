# Whole-branch review and resolution (#112)

The independent review below covers merge-base `9f8c2fd8` through `036141da`, with no inherited implementation context. Its five Important findings are addressed in one fix wave. The scoped re-review approves the fix wave; the final integrated-tree full local gate passes all five groups. The original 39-item triage is preserved in full.

## Fix-wave resolution

| Finding | Resolution | Evidence |
|---|---|---|
| I1 | Clear Dodge marker and accumulator when the queue drains and hit numbers reset; retain pending-hit save fields. | Consecutive real attack command reproduces free half damage before fix; pending fields roundtrip. |
| I2 | Marks coexist by source, refresh only their own copy, move and expire independently. | Two-source Vex/marked coexist, attack advantage, refresh, transfer and expiry regressions. |
| I3 | Legacy sourced `marked` still grants advantage without the new boolean; AI recognizes it too. | Deserialize old condition JSON and make a real attack with advantage. |
| I4 | Twinned skips charmers and chooses the next legal second target. | Guaranteed-failure save spell against three foes: charmer spared and alternate hit. |
| I5 | Downing cancels queued StrikeStep continuations while preserving revive hooks. | Retaliation downs a two-attack fighter; Death Ward revives it and no second strike follows. |
| Cheap Minors | Trap duplicate/exhaustion filtering; percentage-HP placement validation; strict difficulty names; current-round retreat cooldown; reaction timer cleanup; clear loss hint; report routing; documentation/test setup corrections; Cleave and retarget coverage. | Focused tests plus final gate below. |

Regressions first reproduced all five Important findings (three mark cases, Dodge lifetime, Twinned charm, interrupted multiattack), the stale reaction timer and cross-round retreat cooldown. Initial test setup/build/filter errors were corrected before counting valid failures. Focused combined rules verification: 176 passed. Game verification: 47 passed. Full gate pending.

---

# Independent whole-branch review: Wardenflame combat #98

Snapshot: `036141da70404b43df4433193de7f32b100af6ff` in detached worktree `/workspace/work/combat-review-112`.
Comparison: merge-base `9f8c2fd86ffbe8523e9f1a254574256a8a83e52f` through snapshot HEAD (259 changed files).

Disposition: **changes requested**. No Critical findings. Five Important findings, followed by cheap Minor fixes and an explicit disposition for every one of the ledger's 39 `minor (deferred)` entries. Findings describe the reviewed snapshot, not fixes being made concurrently in the implementation worktree.

Read the authoritative attached handoff, combat plan and progress ledger, engine design and old tracker, README content conventions, and companion/multi-attack/turn-wait/AI/rogue/final-draft reports. Reviewed rules/effect queue and serialized state, content and validation changes, planner and scorer, simulator metrics, game adapter and preferences/retreat/summon routing, phone targeting/preferences/retreat, presentation changes and relevant tests. The final balance report is explicitly a draft, so its missing final 20-seed tables are pending work, not a review finding. #239, #240 and #241 remain excluded. No repository edits, commits, GitHub posts, workflow changes, full-suite runs or performance runs were made.

## Important findings (single fix wave)

### I1 — Uncanny Dodge continues halving later commands when hit numbers are reused

Locations: `games/Tactics/Tactics.Logic/Effects/Blocks/Hp.cs:43`, `games/Tactics/Tactics.Logic/Rules/Battle.cs:491`, `games/Tactics/Tactics.Logic/State/UnitState.cs:58`.

`HalveHitBlock` saves the pending resolution number in `UnitState.HalvedHit`, and `Hp.Damage` applies the reduction whenever a later damage resolution has that number. Nothing clears that state. `Battle.Settled` resets `NextResolution` to zero whenever the queue drains. Subsequent independent commands therefore reuse the number of the dodged hit and receive a free half-damage reduction; the rounding accumulator also carries across unrelated hits. This directly changes rogue survivability and balance measurements.

Repro: use the existing UncannyHitTests 3+4 flat-damage attack; react with Dodge and drain the command; then make a fresh attack command against that rogue with no Reaction remaining. The fresh attack should deal 7, but the reused resolution applies Dodge again. The existing test's direct `Hp.Damage(... resolution: 999)` does not exercise command-number reuse.

Fix: clear the hit marker and accumulator when the queue drains and numbering resets. Preserve them while a reaction/queued hit is still pending. Add consecutive-command and mid-hit save/resume regressions.

### I2 — Two sources cannot keep Vex on the same target

Locations: `games/Tactics/Tactics.Logic/Effects/Blocks/Combat.cs:242` and `:163`; related expiry cleanup `games/Tactics/Tactics.Logic/Rules/Conditions.cs:156`.

`Sourced.Put` removes every condition with the same id, irrespective of source. Rogue B hitting X replaces rogue A's Vex, so A loses the advantage R25/R28 grants it. Merely allowing insertion to coexist is insufficient: moving A's mark away from X uses source-blind `Conditions.Remove`, and duration expiry also removes by id. Either path would still erase B's mark after coexistence is fixed.

Repro/regressions: A marks X; B marks X; both attackers must retain advantage. A then marks Y; B's Vex must remain on X. Give their marks different expiry times and end A's turn; B's mark must survive A's expiry. Refreshing A's own mark should not stack it. Preserve the separation between `vexed` and other classes' `marked` passives.

Fix source identity consistently at insertion, move and expiry. Existing shared-mark semantics for two paladins should be handled deliberately, without making unrelated condition types globally stack.

### I3 — Old saves silently lose sourced `marked` advantage

Locations: `games/Tactics/Tactics.Logic/Effects/Blocks/Combat.cs:144` and `games/Tactics/Tactics.Logic/State/UnitState.cs:24`.

Before this branch, `MarkedBy` recognized a condition whose id was `marked` and whose source matched the attacker. It now requires the newly added `Mark` bool. That field defaults to false when old battle JSON omits it. An old save therefore loads but its existing Vow/curse/other sourced marks stop granting advantage until reapplied, violating behavioral save compatibility.

Repro: deserialize pre-branch ActiveCondition JSON with id `marked`, source A, and no `mark` property; target X carries it. `Rolls.AttackEdge(state, A, X, ...)` should still see advantage, but `MarkedBy` returns false.

Fix: retain recognition of legacy `marked` conditions, or migrate them explicitly. Test the resulting attack edge after JSON roundtrip, not only successful deserialization. Keep AI's already-marked valuation consistent too (`Ai/Scoring.Effects.cs:128`).

### I4 — Twinned's automatic second target bypasses charm protection

Locations: `games/Tactics/Tactics.Logic/Rules/Battle.cs:365` and `:415`–`:430`.

The charm refusal checks the initial affected set, then Twinned appends a second target later. A charmed caster may aim a harmful save/control spell at another foe and have its charmer selected automatically as target two. The extra guard in AttackStep protects attack-roll spells only; direct damage and save/control effects still resolve against the charmer.

Repro: a charmed sorcerer has Twinned active; two foes are within spell range, and its charmer is the nearest eligible second target to the first foe. Cast a known non-`common.*` tiered single-target save spell (Hold Person, or a test save with certain failure). The charmer appears in the accepted affected set and gets the harmful condition. `common.*` test abilities are excluded by Metamagic.Shapes and do not reproduce this path.

Fix: exclude forbidden charm sources when selecting Twinned's second target, preferably allowing another legal target to be selected; revalidate the final affected set as appropriate. Cover a save/control spell, since an attack-roll-only regression misses the actual harmful resolution.

### I5 — Dropping and immediately reviving lets a multiattacker continue cancelled strikes

Location: `games/Tactics/Tactics.Logic/Rules/Downed.cs:155`; new continuation `games/Tactics/Tactics.Logic/Rules/Strikes.cs:124`.

`StopActing` cancels the dropped unit's queued RunEffectsStep and AttackStep at depth zero but omits the new StrikeStep continuation. With Death Ward (or an on-ally-downed revive), the attacker can return Up before that queued StrikeStep runs and then execute its remaining attacks. Before this branch the remaining copied ability-effect steps were removed. Drop's documented rule is to stop the rest of the unit's action regardless of immediate revival.

Repro: a two-attack party fighter with Death Ward attacks an enemy with an on-damaged retaliation that downs the fighter after its first hit. Ward revives the fighter. The continuation produces a second attack although the action was interrupted. Use a retaliation passive owned by the damaged enemy and targeted at Other, so its source is correct.

Fix: remove own-source StrikeSteps in StopActing. Regression should assert the fighter is revived but no second strike occurs; do not suppress the revival hook.

## Other cheap Minor findings

- **M1: retreat failure cooldown uses proposal round.** `games/Tactics/Tactics.Game/TacticsGame.Retreat.cs:121` records `vote.Round`. Propose near the end of round 3, let play reach round 4, then fail/timeout: another vote is immediately permitted in round 4. Record the round when failure happens; test a vote crossing a round boundary. This is outside the deferred ledger but follows the stated cooldown contract.
- **M2: simulator configuration text omits new specialist routing.** `games/Tactics/Tactics.Sim/Report.cs:252` still describes wizard/controller as caster and guardian as fallback brute, while `AiProfile.IdForRoles` now routes explicit controller/guardian tags first. Update the printed mapping so the final report describes its actual experiment.
- The remaining cheap fixes are identified individually in the complete triage below. Documentation-only fixes do not need new implementation-mirroring tests.

## Complete deferred-minor triage

Line numbers below refer to `docs/superpowers/plans/2026-11-06-wardenflame-combat-98.progress.md` at the reviewed snapshot. “Leave” means no change required in this branch, with the stated reason; it does not silently drop the item.

| # | Ledger line / item | Disposition | Reason / concrete action |
|---:|---|---|---|
| 1 | 35: trap minor injuries can duplicate | **Fix now — Minor** | Task 1's ruling says a hero cannot hold two copies. `TacticsGame.Interact.cs:329` appends blindly. Suppress an explicit trap injury already held; random trap selection should exclude held injuries and return none when exhausted. Add one duplicate/exhaustion behavior regression. |
| 2 | 38: HitMark XML summary attached to DelayMark | **Fix now — Minor** | `State/BattleState.cs:6` contains consecutive summaries before DelayMark. Move the HitMark summary onto HitMark; trivial correction. |
| 3 | 39: countdown/Delay documentation stale | **Fix now — Minor** | `State/Events.cs:153` still says a new round; design §3.8 still says round start; design §3.2 says Delay only affects the current round. Update those and add persistent Delay/countdown conventions in README. DownedRules.TickTurn's own summary is already correct. |
| 4 | 40: Delay chains and never-pruned marks | **Leave** | Chronological replay of delay moves is deterministic; stale entries whose unit/anchor has left are ignored. Battle-local data is bounded by participants, and no shipped malfunction was confirmed. More edge tests can follow without changing the now-established ordering policy. |
| 5 | 41: maxHp read inside its own assignment | **Fix now — Minor** | `Content/SheetBuilder.cs:144` is correct but obscures the common pre-injury basis. Use a named uninjured/base HP local so all injury percentages visibly use that basis. |
| 6 | 42: MaxHpPercent outside injuries silently ignored | **Fix now — Minor** | Reject unsupported percentage modifiers at validation boundaries (conditions, passives and effect/zone statics; other non-injury static sources if accepted). Its schema/API currently allows an ineffective value. Prefer validation to expanding runtime semantics. |
| 7 | 49: difficulty Enum.TryParse accepts numeric/composite names | **Fix now — Minor** | Match exactly one allowed enum name, ignoring case; `Story,Hard` can otherwise parse to an accepted defined value. This is a small input-contract correction. |
| 8 | 50: loss hint points to unavailable party menu | **Fix now — Minor** | Clarify that VIP should select Carry on and then open the party menu; update matching README text if kept verbatim there. |
| 9 | 57: companion “seven of nine” | **Fix now — Minor** | Three exceptions are listed, so `companion.md:41` must say six of nine. |
| 10 | 58: timing seeds hide 1.15s beat / unexplained sampling | **Fix now — Minor (documentation/provenance)** | Forest seed 7 is already restored by R8 with a direct Eldritch/Repelling test. `RealBattleTimingTests.cs:29` still changes river seed 3→4 and expands hand-picked cases without explaining coverage. Add a seed-selection comment and explicitly record the historical 1.15s case's disposition; do not claim it was fixed solely because the sampled test is green, and do not weaken the 1.1s bound. No new gameplay defect is confirmed by the historical note alone. |
| 11 | 59: GuardWeight pass-through | **Fix now — Minor** | Inline `Companion.GuardWeight` at its single consumer and remove the redundant private property. |
| 12 | 60: companions inherit Cruel/FleeBelowHp | **Leave** | All profiles reachable from the shipped class-role mapping (including new controller/guardian) have Cruel=false and FleeBelowHp=0. `coward` and `boss_cruel` are not reachable through that mapping. Thus absent heroes do not acquire this behavior; speculative profile-extension policy need not block. |
| 13 | 66: no cross-player summon command test | **Leave** | `Plays(state, hero, unit)` checks exact hero identity or direct SummonedBy; both command and reaction paths use it. No authorization bug confirmed. A coverage-only omission does not require another gameplay change. |
| 14 | 67: no summon reaction phone test | **Leave** | Reaction screen and input both route with Plays; PrefsOf uses the summon's controlling owner. Existing architecture is consistent. A dedicated integration test is useful later but no defect was found. |
| 15 | 68: nested summon Hand rejection | **Leave** | Shipped summon templates cannot summon another creature. Battle.Hand deliberately restricts handoff to a hero's direct summon, consistent with adapter Plays. Supporting recursive ownership would be a separate feature. |
| 16 | 69: SummonControlTests connection expression | **Fix now — Minor** | Replace `here.DefaultIfEmpty(0).Concat(...)` with a named effective seat list (`here.Length == 0 ? [0,1,2,3] : here`). Same test behavior, clearer setup. |
| 17 | 70: missing hero causes SeatChanged early return | **Leave** | Battle state retains Out/Dead hero records; normal removal is from order, not Units. The missing-roster case is malformed/unreachable in this branch's valid saves, and no live summon ownership failure was found. |
| 18 | 80: EdgeRisk counts universal Shove/action already spent | **Leave; audit balance** | It predicts the foe's next turn, where Action refreshes; every foe truly knows common.shove. The approximation is conservative geometry/probability, not a present-action legality bug. Final intended-level and companion sweeps must judge the weight. |
| 19 | 83: Brace triggers bank traps/surfaces | **Leave** | Landing enters the tile, matching push/teleport conventions and R7. Suppressing enter hooks would create an inconsistent exception. |
| 20 | 84: narrow concentration-check presentation pattern | **Leave** | The specific collateral collision/save fault has its direct regression. A generic event-provenance redesign would exceed a cheap fix; no additional concrete misclassification was confirmed. |
| 21 | 85: Vorga test calls Battle.Start twice | **Fix now — Minor** | `SteadfastTests.cs:311` should deconstruct one Start result. Duplicate deterministic setup is unnecessary. |
| 22 | 93: Cleave riderOnce semantics undocumented/untested | **Fix now — Minor** | README describes area strikes and riders separately but does not explicitly say the 1d8 rider is once across all Cleave targets. Add that sentence and a focused multi-target behavior test. This protects the branch's deliberate rule, rather than changing it. |
| 23 | 94: first-hit tooltip wrong for attack bonuses | **Fix now — Minor** | `Content/Tooltips.cs:56` should state full bonuses/effects apply until the first hit, then attacks are plain. Precision/Deadeye's bonus also helps earlier misses; existing wording obscures that. |
| 24 | 95: wrap-around retarget not tested | **Fix now — Minor** | Small high-value regression around `Strikes.Pick`: later listed target becomes invalid, selection wraps to an earlier living listed target before lowest-HP fallback. No implementation change expected. |
| 25 | 96: AI split candidates include Downed foes | **Leave** | R9 explicitly permits deliberately listed Downed targets; Worth gives non-cruel targeting a penalty and cruel targeting positive value. Candidate enumeration alone does not violate fair play. |
| 26 | 97: vitest ECONNREFUSED localhost noise | **Leave** | This is a test-server/asset fetch observation, not a confirmed branch failure. No branch code starts a new localhost request path solely from this change. Preserve real phone test failures if the final gate finds one; historical stderr alone does not justify code edits. |
| 27 | 100: pushes move later attack target out of reach | **Fix now — Minor (report only)** | Rules intentionally re-resolve each attack; retarget/spent attacks match the agreed semantics. Note this consequence for Brutal Strike/Cleaving Blow in the final report; no gameplay fix. |
| 28 | 108: no story beat through retreat-vote test | **Leave** | Vote success uses the same BattleResult completion path; story progression remains Won-only and Fled sets RestReady. Existing retreat and story behavior establish the path; no concrete progression regression found. |
| 29 | 109: Retreat=true always serialized | **Leave** | Adding WhenWritingDefault for bool would omit false (the meaningful opt-out) and still write true. Current explicit true is correct and harmless; special default-true serialization machinery is unnecessary. |
| 30 | 110: TV retreat countdown hardcodes default duration | **Leave** | Deadline is authoritative; the default length only controls VoteClock's display cap. Production uses default timing; custom harness timings can show a capped display but voting still expires correctly. A duration field/UI contract expansion is not a cheap necessary fix. |
| 31 | 121: auto-answer leaves reaction timer stale | **Fix now — Minor** | Cancel Timers.React and clear _reactTimerFor before answering an already-visible preference-covered prompt in Drive. Assert the obsolete timer is gone. |
| 32 | 122: reaction settings unavailable in prompt/aiming loses sheet state | **Leave** | Existing UI deliberately offers settings while waiting or on the turn menu. Preserving sheets across screen changes is UX expansion; settings themselves remain saved. No loss of reaction preferences. |
| 33 | 123: wait test only checks not-worse | **Leave** | R14 accepts measured idle-bot cuts, and the test documents a noisy battle-dependent harness. New strict historical percentage thresholds would overfit old content/AI trajectories. Report current measurement honestly during the final gate. |
| 34 | 124: no summon 15s-clock test | **Leave** | Clock logic operates on active UnitId and shares TurnSpent regardless of hero/summon; summon turns already exercise the timer path. Coverage-only omission, no special-case defect found. |
| 35 | 125: no auto-answer while retreat pending test | **Leave** | Votes live separately, neither replace Pending nor suspend Drive; reaction preference evaluation runs normally. No conflicting state mutation confirmed. |
| 36 | 144: reaction spends planned spell resource | **Leave — explicit #241** | User excluded this known race from this branch. Keep the final audit's refusal/replan counts visible. |
| 37 | 145: duplicate Twinned cost check | **Leave** | One check discards an unprofitable shaped variant early, the later score ranks full plans including cost. Both are cheap and protect different stages; the test requiring both removed is not a behavioral defect. |
| 38 | 148: PushOnHit longest push / AwayFromSource=false | **Leave** | No shipped passive combines pushes or uses the unsupported origin. Keep this documented approximation; generalized hypothetical effects require a separate planner extension. |
| 39 | 174: two-source Vex | **Fix now — Important I2** | Confirmed shipped two-rogue party behavior. Include insertion, transfer and expiry, not just coexistence. |

## Validation and limits

Focused existing tests at the snapshot: **56 passed**, zero failed/skipped, using the Logic test project in Release and repeated `--filter-class` for `*UncannyHitTests`, `*SrdConditionTests`, `*TallyTests`, `*RogueContentTests`. Runtime activated with `/workspace/.arcbox/activate.sh`; `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0`. An initial pipe-separated class filter matched zero tests; it was corrected to repeated filter arguments before reporting the 56-pass result.

The Important findings were confirmed by deterministic code-path inspection; their missing cases are outside the 56 existing tests. I sent them immediately to the implementation agent so RED regressions can run in the one authorized fix wave. No claim is made that the whole suite or final 20-seed balance audit passed. The isolated pending-hit preview otherwise preserves immutable input/RNG and stops at an unresolved secondary decision; its lifetime marker must be fixed by I1. Target-normalized tallies retain raw damage and put pre-first-turn/off-turn damage and delayed starts into the documented buckets; no additional normalization defect was confirmed. Final audit must run after gameplay fixes, particularly Dodge and multi-source Vex.

One scoped re-review should inspect the fix diff and its regression evidence, plus ensure the final report records the agreed departures and pending balance evidence. Do not restart an unrelated whole-branch review loop.


---

# Scoped re-review: one fix wave

Reviewed `036141da70404b43df4433193de7f32b100af6ff..0fbd804c5af3ff49350a19cdaa81103070a3fea0` in detached worktree `/workspace/work/combat-review-112`. Scope: the 42-file fix diff and the original five Important findings plus cheap-Minor dispositions. This is the one requested scoped re-review, not another whole-branch review.

**Outcome: gameplay/code fixes approved. No remaining Critical or Important findings in this diff.** All five Important findings are fixed. Two small documentation corrections noticed during this pass have been made by the implementation agent and read back; one final-report consequence remains part of the already pending report assembly. None requires another gameplay change or restarting measurement.

## Important fix verification

| Finding | Scoped result and evidence |
|---|---|
| I1: Dodge lifetime | Fixed in `Rules/Battle.cs:493`. Settlement clears both `HalvedHit` and `HalvedHitDamage` at the same drained-queue boundary where resolution numbering resets. Pending queued work retains these fields. The regression issues a second actual Use command after restoring Action, exercising reused resolution IDs; HP goes from 97 to 90. Serialization retains non-default marker/accumulator with queued work and NextResolution. The prior exact pending-hit simulation and unresolved-secondary-decision behavior remain intact. |
| I2: multiple mark sources | Fixed in `Effects/Blocks/Combat.cs:163,242` and `Rules/Conditions.cs:44,135`. Insertion/refresh replaces only the same source for marks; moving a mark removes only that source; duration expiry carries the expiring condition's source through removal. Ordinary non-mark replacement behavior remains unchanged. Behavioral tests cover Vex and marked coexistence, actual independent attack advantage, own refresh, transfer, and independent expiry. Until-save/concentration paths do not create another source-blind mark issue: MarkBlock creates neither kind. |
| I3: old marked saves | Fixed in `Effects/Blocks/Combat.cs:144` and `Ai/Scoring.Effects.cs:128`: the literal legacy `marked` condition still counts when its new boolean is absent/default false, with source identity required. AI also avoids incorrectly revaluing its already-present old mark. Old-JSON regression checks a real advantaged attack. |
| I4: Twinned charm protection | Fixed in `Rules/Battle.cs:364,420`. A harmful ability computes the active charm sources once, rejects explicit protected victims, and excludes those sources before selecting the automatic second victim. Helpful spells retain their existing behavior. Regression uses a non-common known save spell, guaranteed failed saves, and three enemies; it spares the charmer and damages the next legal alternative. |
| I5: interrupted multiattack | Fixed in `Rules/Downed.cs:156`: own-source StrikeStep continuations are removed on stopping action, alongside the existing top-level attack/effect removal. Hook work is preserved. Regression makes retaliation down a two-attack fighter, Death Ward revive it, and verifies exactly one attack rather than resuming the interrupted action. |

## Cheap-Minor verification and complete triage reconciliation

The full original 39-row triage and its reasons remain preserved in `docs/superpowers/reports/2026-11-06-combat-98/review-final.md` and the independent-review artifact. The following explicitly reconciles every row without reopening the previously justified leave decisions.

| Original row | Scoped disposition |
|---:|---|
| 1 | Fixed: trap random selection filters held injuries consistently in Table/Total/Pick and adapter; fixed duplicates return none, exhausted table returns none. Focused behavior regression passes. |
| 2 | Fixed: HitMark XML summary restored to HitMark. |
| 3 | Fixed: countdown and persistent Delay documentation updated. New README issue-reference typo #105 corrected to #115 by implementation agent; read back after notification. |
| 4 | Leave unchanged: prior deterministic/bounded Delay-chain reasoning stands. |
| 5 | Fixed: named uninjuredMaxHp makes the common percentage basis explicit without changing math. |
| 6 | Fixed: unsupported nonzero MaxHpPercent rejected on passives, conditions, stat-mod effects, trinkets, and recursively validated zone/condition passives. Injury percentages remain supported. |
| 7 | Fixed: difficulty input must match one enum name case-insensitively; numeric/composite inputs rejected. |
| 8 | Fixed: loss hint and matching tests/docs direct the VIP through Carry on before party settings. |
| 9 | Fixed: companion report now says six of nine. |
| 10 | Fixed in documentation follow-up: timing sample comment explains curated coverage. Initial fix text overclaimed linkage of the historical Task3 1.15s beat to R8; implementation agent corrected it to explicitly unproven linkage, read back. The 1.1s bound remains unchanged. |
| 11 | Fixed: single-use GuardWeight pass-through removed/inlined. |
| 12 | Leave unchanged: no shipped companion-mapped profile enables Cruel/Flee. |
| 13 | Leave unchanged: direct ownership checks cover cross-player summon authorization. |
| 14 | Leave unchanged: summon reaction routing consistent; coverage-only omission. |
| 15 | Leave unchanged: nested summons absent from shipped templates. |
| 16 | Fixed: readable named effective connected-seat list. |
| 17 | Leave unchanged: valid battle states retain hero records; malformed missing-roster path not a confirmed shipped failure. |
| 18 | Leave unchanged; final balance evidence pending: next-turn Shove risk approximation remains as previously justified. |
| 19 | Leave unchanged: Brace landing enter hooks are intentional. |
| 20 | Leave unchanged: no additional confirmed presentation misclassification; direct collateral regression remains. |
| 21 | Fixed: one Battle.Start in the Steadfast setup. |
| 22 | Fixed: explicit once-per-use Cleave rider convention and multi-target behavioral regression. |
| 23 | Fixed: tooltip explains bonuses/riders apply until first hit, including preceding misses. |
| 24 | Fixed: wrap-around listed-target regression distinguishes the earlier surviving listed target from fallback. |
| 25 | Leave unchanged: deliberate listed Downed targets allowed; scorer handles non-cruel penalty. |
| 26 | Leave unchanged: historical localhost stderr is not itself a confirmed code failure. |
| 27 | Pending final-report assembly only: name the Brutal Strike/Cleaving Blow consequence when a push moves a later target out of reach (retarget or spend). Implementation agent confirms the final report generator already includes this exact consequence; draft replacement awaits audit. No rule change needed. |
| 28 | Leave unchanged: retreat uses shared battle-result path and Won-only story progression. |
| 29 | Leave unchanged: explicit default-true Retreat serialization is correct. |
| 30 | Leave unchanged: custom retreat-clock display cap does not alter authoritative deadline. |
| 31 | Fixed: preference auto-answer cancels reaction timer and clears tracking before ApplyAi; game regression asserts timer absent. |
| 32 | Leave unchanged: settings-screen persistence is UX expansion, not lost preferences. |
| 33 | Leave unchanged: do not overfit noisy historical wait reduction percentages; current report evidence remains required. |
| 34 | Leave unchanged: active-unit clock path is shared by heroes and summons. |
| 35 | Leave unchanged: retreat vote and reaction state do not conflict. |
| 36 | Leave unchanged: explicitly excluded #241 planned-spell resource race. |
| 37 | Leave unchanged: Twinned checks protect distinct planning stages. |
| 38 | Leave unchanged: unsupported push combinations/origins are not shipped. |
| 39 | Fixed as I2, including source-aware expiry and transfer. |

Other original cheap Minors: M1 is fixed by recording the failure's current round in `TacticsGame.Retreat.cs:121`, with a vote-crossing-round regression. M2 is fixed by specialist controller/guardian routing in the simulator report text and updated SimRunner summary. Reaction timer cleanup is ordered before applying the preferred answer, avoiding stale timer identity even when another prompt follows.

## Verification and limits

Independent focused execution at exact `0fbd804c`: **46 passed, 0 failed, 0 skipped**, covering CombatBlockTests, UncannyHitTests, DeathWardTests, SrdConditionTests, SerialisationTests and TrapInjuriesTests. Log: `/workspace/work/combat-final-112-logs/scoped-focused-tests.txt`. Activated `/workspace/.arcbox/activate.sh`; disabled MSBuild node reuse and CLI MSBuild server as requested. An initial `dotnet run` invocation rejected the MTP filter option before running tests; corrected to `dotnet test --project ... --no-build` with repeated `--filter-class` arguments. Only the successful 46-test run is counted. `git diff --check 036141da..0fbd804c` is clean; detached worktree remains unmodified.

Also inspected the implementation agent's valid GREEN logs: `review-runtime-green.txt` records 176 logic tests passing; `review-game-green.txt` records 47 game tests passing. The zero-test retry/repeat logs are not regression evidence and are not counted. The independently read regression setups correspond to the five reported defects; supplied RED setup/filter errors are not counted as failures proving a defect.

Final 20-seed balance audit, final full local gate, and final report/results remain pending external evidence. No claim of their success is made here. Golden replay/fixture/phone results reported by the implementation agent are separate validation, outside this fix-source review. Source snapshot for gameplay measurement remains `0fbd804c`; documentation-only corrections and replay data regeneration do not change reviewed combat semantics. #239, #240 and #241 stay excluded.

No repository edits, commits, GitHub posts, workflow changes, delegation, full-suite runs, or performance tests were made by this reviewer. No further whole-branch or iterative review wave is requested.

## Full-gate follow-through

After scoped approval, the complete gate exposed two stale test expectations and a presentation timing edge outside the earlier focused sample. Legacy sourced marks correctly retain advantage, and wizard companions correctly inherit the specialist controller profile; their old assertions are updated without changing runtime rules. Three real area sequences took 1.12–1.15 seconds because countdown, transformation and zone cleanup each held the shared timeline. A direct regression reproduced 1.51 seconds; those side effects now overlap within the area, retaining every application/visual cue. The 1.1-second bound and all original battle seeds are unchanged. Real engine fixtures guarantee both required passive samples even when the tuned AI kills guards before they flank. Focused verification: 100 presentation + 14 rules passed. Five manifest checks also pass after documenting generic icon fallbacks for the two new common actions. No combat rules, AI or simulator implementation changed after the measured/reviewed `0fbd804c`. The fresh full gate result follows in balance-final.md.

## Completion evidence

Final integrated-tree `tools/check-all.sh --full`: PASS, exit 0; 7,161 .NET tests, 547 phone tests/build, 24 Node tests, all Python/lore/docs checks, no skipped tests. The twenty-seed audit completes 28,520 battles: 9/9 aggregate intended-level story targets, 10/10 scripted default-rapier caps; 15/120 role values and two paired companion gaps remain exceptions, with individual size misses also recorded. Full measurements and every ruling/cost are in [balance-final.md](balance-final.md). Main refreshed cleanly to `cb488e23` at `b221c479`, with no measured Tactics implementation changes. CI stays disabled; only the combat branch is delivered.
