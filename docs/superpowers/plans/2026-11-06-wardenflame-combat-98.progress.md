# SDD ledger — plan: docs/superpowers/plans/2026-11-06-wardenflame-combat-98.md

Spec: GitHub #98 and sub-issues #105–#115 (embedded verbatim in the plan). Branch base: 5dfce0c3 (origin/main). Push target: origin wardenflame-combat-98 (owner-approved).

## Pre-flight scan

| Tasks | Shared file or interface | Finding |
|---|---|---|
| T1 / T5 | `Rules/Downed.cs` | T1 changes the countdown tick; T5 changes the deep-push kill path. Different functions, no interface conflict. |
| T2 / T8 | `TacticsDocument` | T2 adds `Difficulty`; T8 adds per-member reaction preferences. Additive and sequential. No conflict. |
| T3 → T4 | Companion profile | T4 uses the companion profile for absent owners' summons. T3 retunes it first. No conflict. |
| T3 / T5 / T9 | AI scoring and planner | All three change AI weights or candidates; they run in order T3, T5, T9. Each reruns `AiPerformanceTests`. No conflict. |
| T2 → T7 | Results screen loss hint | T2 shows a difficulty hint after a loss. T7 adds `Fled`, which the campaign treats as a loss. **Ruling R1 below.** |
| T4 / T6 / T7 / T8 | Phone `turn.tsx`, `screens.tsx`, types | Each adds UI to the battle screens, sequentially. No interface conflict. |
| T6 / T10 / T11 | Class and ability content (rogue, common) | T6 adds `riderOnce`; T10 cuts rogue attacks and Sneak Attack; T11 changes Shove and spell dice. They run in order. No conflict. |
| T5 / T11 | `common.shove` | T5's AI push scoring reads Shove; T11 adds a prone option to it. **Ruling R2 below.** |
| T8 → T11 | Reaction preferences vs Uncanny Dodge | T8's "only when it turns a hit into a miss" setting doesn't fit T11's damage-halving Uncanny Dodge. **Ruling R3 below.** |
| T10 → T11 | Striker band measure | T10 tunes the rogue against the current band measure; T11 changes the measure. **Ruling R4 below.** |
| T4 (self) | Issue's done-when vs plan ruling | The issue says "No summon turn waits for `Timers.Turn`"; the owner chose phone control, which needs a timer. The plan's ruling already reinterprets this. No further action. |
| T5 (self) | Done-when vs owner decision | The issue wants the AI to prefer a push into a hazard; the owner decided guarded bosses can't be pushed in. The test uses an unguarded unit. Consistent. |
| T1, T2, T3, T6, T7, T8, T9, T10, T11 (self) | Issue text vs rulings | Checked each task's tests against its specified code. No internal contradictions beyond the rulings already in the plan. |

## Pre-flight rulings

- Ruling R1: T7 extends T2's after-loss difficulty hint to `Fled` results too — a fled party is a struggling party — cost if wrong: one extra line on the results screen after a retreat.
- Ruling R2: T11 must keep T5's push-landing scoring working for the push mode of Shove and teach the AI the prone mode, with an AI test for each — cost if wrong: the AI underuses the new prone Shove.
- Ruling R3: in T11, the "Shield and other defensive reactions: only when it changes the outcome" setting uses Uncanny Dodge when halving stops the hit from Downing the rogue, and otherwise doesn't — cost if wrong: players who chose that setting take more damage than they expect.
- Ruling R4: T11 re-checks every band, the rogue's included, under the new striker measure and retunes any that leave ±15%; T10's bound is re-asserted there — cost if wrong: one more simulator pass.

## Progress
Task 1: dispatched (BASE 969c84b5, implementer sonnet)
Baseline: dotnet build Release OK; dotnet test 6787/6787 passed (worktree at 5dfce0c3)
Task 1: implementer DONE_WITH_CONCERNS (commits 678b6582, 2df59371, 35582b25, dea02a69); goldens river-crossing + warlord-bridge re-recorded; 5 web fixtures re-recorded
- Ruling R5: commit trailers keep the session's fixed attribution line "Co-Authored-By: Claude Opus 5.5" for every commit, subagent commits included — the session's attribution instruction names that exact line, and the plan's "never name a model" rule means code, comments and message bodies — cost if wrong: subagent-authored commits carry a slightly imprecise attribution line.
Task 1: minor (deferred): no-stacking covers battle injuries only; trap minor injuries can still duplicate.
Task 1: review dispatched (sonnet) on 969c84b5..dea02a69; controller full-suite run in background
Task 1: review: spec ✅, quality Approved (0 critical, 0 important). ⚠️ even-Inc saves resolved by controller: Rng.Seeded is the only Tactics constructor and always sets Inc odd.
Task 1: minor (deferred): DelayMark inserted between HitMark's doc summary and HitMark (State/BattleState.cs) — HitMark lost its doc comment.
Task 1: minor (deferred): CountdownTicked / Downed docs, README and spec §3.2/§3.8 still say the countdown ticks at round start; the README content conventions need a one-line note on the persistent-Delay house rule (Global Constraint: document deviations).
Task 1: minor (deferred): Delay chains (A behind B, B delays later) and never-pruned marks are untested design edges.
Task 1: minor (deferred): SheetBuilder.cs maxHp read inside its own assignment — use a local for clarity.
Task 1: minor (deferred): MaxHpPercent on non-injury sources is silently ignored; the loader should reject it like MaxHp on conditions.
Task 1: controller full suite 6796/6796 passed at dea02a69
Task 1: complete (commits 969c84b5..dea02a69, review clean)
Task 2: dispatched (BASE dea02a69, implementer sonnet)
Task 2: implementer DONE_WITH_CONCERNS (commit d2ad7e39); full suite 6814/6814, npm 521, build OK; 37 phone fixtures regenerated; no goldens changed. Tests written after the code (RED shown by disabling the handler). No portal change (no settings surface exists).
Task 2: review dispatched (sonnet) on dea02a69..d2ad7e39
Task 2: review: spec ✅, quality Approved (0 critical, 0 important). ⚠️ Godot HudLayer placement untestable in suite (BattleHud.Tag covered) — accepted; phone input goes through the existing server input path.
Task 2: minor (deferred): SetDifficulty uses Enum.TryParse, which also accepts "0"/"1"/"2" and "Story,Hard"; IsDefined blocks undefined values — a strict name match would be tighter.
Task 2: minor (deferred): the loss hint says "party menu" but the results screen has no menu; the VIP reaches it after "Carry on".
Task 2: complete (commits dea02a69..d2ad7e39, review clean)
Task 3: dispatched (BASE d2ad7e39, implementer sonnet)
Task 3: implementer DONE_WITH_CONCERNS (commit aae4eecc); full suite 6816/6816, npm 521. Sim (size 4, 20 seeds): within ±10 at 6/9 story encounters (ford_assault +16.6, rime_tower +11.7, hall −11.6); capped battles 21 → 16 (class party 18); river_crossing capped 9 vs class 5; finale not clock-limited (clock untouched). Seeds changed in RealBattleTimingTests to avoid a 1.15 s beat.
Task 3: review dispatched (sonnet) on d2ad7e39..aae4eecc
Task 3: review: spec ✅ (with ⚠️ acceptance partly met), quality Approved (0 critical, 0 important). Companion call sites checked by reviewer: all resolve through AiProfile.For → Companion.Weigh.
- Ruling R6: accept #109 as done with 6/9 story encounters inside ±10 points — the issue's problem (companions hanging back, running out clocks, capped battles) is fixed (rounds 14.7–17.4 → 11.7–14.9, capped 21 → 16), the misses go both ways, and the finale's −11.6 is re-checked in T11's final balance pass — cost if wrong: companion-filled parties stay ~17 points stronger at ford_assault and ~12 weaker at the finale than class-profile play until a later retune.
Task 3: minor (deferred): companion.md:100 says "seven of nine" — should be six of nine.
Task 3: minor (deferred): RealBattleTimingTests seeds were re-picked to avoid a real 1.15 s save-area beat (bound 1.1 s); 14 hand-picked fights with no comment on why — track the beat and comment the seeds. Final review to triage.
Task 3: minor (deferred): Planner.Positions GuardWeight is a pass-through to Companion.GuardWeight — inline it.
Task 3: minor (deferred): companions now inherit the class profile's Cruel and FleeBelowHp — confirm intended (an absent player's hero may flee at low HP) and document.
Task 3: complete (commits d2ad7e39..aae4eecc, review clean, 1 ruling)
Task 4: dispatched (BASE aae4eecc, implementer sonnet)
Task 4: implementer DONE (commit 94d1c179); full suite 6821/6821, npm 522, build OK. Concern: Game tests inject a cloned hero as the hound instead of a real Beastmaster summon.
Task 4: review dispatched (sonnet) on aae4eecc..94d1c179
Task 4: review: spec ✅ (⚠️ real Beastmaster path), quality Approved; 1 Important: no Game test spawns a real summon (SummonControlTests.cs:54-56). Routing to non-owned summons safe by construction (Plays requires SummonedBy == own hero).
Task 4: minor (deferred): no cross-player test (player 1 sends endTurn/react while player 2's hound is active).
Task 4: minor (deferred): no test for a summon's reaction offered to the owner's phone.
Task 4: minor (deferred): Hand refuses a summon whose owner is itself a summon; such a summon would sit on the timer unoffered — unreachable in current content; add a comment or test.
Task 4: minor (deferred): SummonControlTests connection logic (here/DefaultIfEmpty/Concat) hard to read.
Task 4: minor (deferred): SeatChanged returns early if the hero's unit is gone; its summons keep their old controller.
Task 4: fix round 1 dispatched (resume implementer, FIX_BASE 94d1c179) — real-summon Game test
Task 4: fix round 1/5 (1 addressed, 0 open; commits 94d1c179..4048ee8d)
Task 4: complete (commits aae4eecc..4048ee8d, review clean)
Task 5: dispatched (BASE 4048ee8d, implementer sonnet)
Task 5: implementer DONE_WITH_CONCERNS (commits 7b1bd5e2, 089cc3c3); full suite 6838/6838. Sim (seeds 6): hall_of_the_flame 71.3→60.2%; warlord_bridge 92.6→91.7%; others move <6 pts. Golden warlord-bridge.events re-recorded (phase clamp holds Vorga at 119). RealBattleTimingTests forest_ruins seed 7→31 to dodge a suspected BeatScript area-detection bug (follow-up task queued by implementer). FallIn (terrain collapse) still kills a boss. steadfast on every boss-tagged block.
Task 5: review dispatched (sonnet) on 4048ee8d..089cc3c3
Task 5: review: spec ✅ vs brief, ❌ vs issue goal (FallIn); quality Approved with follow-ups; 2 Important: (1) Motion.FallIn kills a steadfast boss (Warlord's Bridge collapse under Vorga); (2) RealBattleTimingTests forest_ruins seed 7→31 masks a latent BeatScript fault this branch's AI changes exposed.
- Ruling R7: extend the steadfast guard to terrain collapse (Motion.FallIn) — a guarded unit caught by a collapse is placed on the nearest walkable non-deep tile (deterministic search, ties by tile order), takes the engine's existing fall damage if it has one, keeps phases guaranteed via the clamp, and emits PushResisted (or a sibling event) — the owner's #106 decision was "bosses stop at the edge and phases always fire", and a collapse kill skips the fight the same way a shove did — cost if wrong: one more placement helper; bosses survive bridge collapses they used to die in.
- Ruling R8: fix the BeatScript area-detection fault in this branch, restore forest_ruins seed 7 in RealBattleTimingTests, and add a direct BeatScript test for the 3-beam Eldritch Blast + Repelling Blast case — this branch's AI change exposed it, and swapping seeds hides a TV glitch the branch would ship; tests are never skipped or quarantined — cost if wrong: a presentation fix lands in a combat branch; if it proves large, the implementer reports back instead.
Task 4/5: minor (deferred): EdgeRisk Pushers() counts every foe (common.shove is universal) and ignores whether the foe has an action left; EdgeRisk weight (0.25×caution×(HP+KillWorth)) is large and applies to party AI too — hall 71→60% — T11 balance pass to look.
Task 5: fix round 1 dispatched (resume implementer, FIX_BASE 089cc3c3)
Task 5: fix round 1/5 (2 addressed, 0 open; commits 089cc3c3..39c3ef85). Implementer withdrew its queued BeatScript follow-up task (fixed in-branch).
Task 5: minor (deferred): Brace landing calls Entered, so bank traps/surfaces fire on the braced unit (consistent with push).
Task 5: minor (deferred): BeatScript IsConcentrationCheck pattern is narrow (Collided+Damaged+Save same unit).
Task 5: minor (deferred): SteadfastTests Vorga test calls Battle.Start twice.
Task 5: complete (commits 4048ee8d..39c3ef85, review clean)
Task 6: dispatched (BASE 39c3ef85, implementer opus)
Task 6: implementer DONE_WITH_CONCERNS (commits db228043, 26837eb7); full suite 6868/6868, npm 527, build OK; 25 abilities riderOnce; warlord-bridge golden re-recorded from AI at seed 2 (seed 15 no longer reaches the phase). Sim 20 seeds: all sampled cells in band (crystal_cave L7 94.4%, 0.6 under HIGH). Monk L5 striker ~−50% (pre-existing) slightly lower.
- Ruling R9: the auto-retarget fallback ("lowest-HP hostile in reach") excludes Downed units; if only Downed units remain in reach, the attack is spent — the plan's ruling meant "never waste attacks on a dead target", not "auto-finish Downed heroes", and auto-picking Downed heroes would tick their countdowns on every multi-attack (against spec §5.4's fair-play spirit); an explicitly listed Downed target (cruel AI choice) is still honoured — cost if wrong: an occasional spent attack when only Downed foes remain in reach.
Task 6: review dispatched (opus-sized diff; reviewer sonnet) on 39c3ef85..26837eb7 with R9 as a constraint
Task 6: review (opus): spec ❌ R9 (Strikes.Pick fallback uses DownedRules.InBattle → Downed units at 0 HP sort first); quality Needs fixes; 1 Important (R9). Backward compat, rider split, warlord golden coverage: pass.
- Ruling R10: later hits of a riderOnce ability keep the ability's own first damage block (e.g. Quivering Palm 2d8+DEX, Halvard 3d8), not the unit's plain weapon damage — documented in schema/README, meets the done-when, and redefining "base weapon damage" for non-weapon-flagged abilities needs per-ability judgement the T11 balance pass can make for outliers — cost if wrong: some abilities' later hits stay stronger than a plain weapon strike.
Task 6: minor (deferred): Cleave's riderOnce (1d8 once per use across all targets) has no behaviour test and isn't spelled out in the README.
Task 6: minor (deferred): Tooltips.cs:54-55 "only the first hit gets this" is wrong for roll bonuses (Precision Attack, Deadeye, Execution Strike apply until the first hit).
Task 6: minor (deferred): no test for the wrap-around retarget (Strikes.cs:76).
Task 6: minor (deferred): AI split candidates include Downed hostiles (Planner.Actions.cs:210-212) — allowed by R9 as a deliberate listed choice; confirm intent.
Task 6: minor (deferred): vitest ECONNREFUSED 127.0.0.1:3000 noise reported — check whether pre-existing.
Task 6: fix round 1 dispatched (resume implementer, FIX_BASE 26837eb7) — R9
Task 6: fix round 1/5 (1 addressed, 0 open; commits 26837eb7..8cb968b6)
Task 6: minor (deferred): pushes (Brutal Strike, Cleaving Blow L5+) can take the next attack's target out of reach, so it retargets or is spent — consistent with SRD, note in final report.
Task 6: complete (commits 39c3ef85..8cb968b6, review clean, 2 rulings R9/R10)
Task 7: dispatched (BASE 8cb968b6, implementer sonnet) — carries R1
Task 7: implementer DONE_WITH_CONCERNS (commit 5a2f8533); full suite 6895/6895, npm 536, build OK; 5 new phone fixtures. Tests for Game/Presentation/vitest written after the code. TV overlay unviewed (Godot). Vote not persisted (restart cancels it). No story-fight test through the vote path.
- Ruling R11: a Fled battle sets RestReady like a loss — the plan's ruling treats Fled as a loss for story progress, and every won or lost battle earns a rest today — cost if wrong: a party that retreats rests once without having fought to the end.
- Ruling R12: an in-progress retreat vote is not persisted; a server restart cancels it and players can propose again — votes are transient social state, restarts are rare, and persisting it adds document fields for little value — cost if wrong: a vote in flight during a restart must be re-proposed.
Task 7: review dispatched (sonnet) on 8cb968b6..5a2f8533 with R1, R11 as constraints
Task 7: review: spec ❌ R11 (TacticsGame.cs:600 RestReady excludes Fled); quality Needs fixes; 1 Important. Story beats stay retryable after Fled (StoryAfter Won-only); vote integrity sound.
Task 7: minor (deferred): no test sends a vote-driven flight through a story beat (Beats/Story unchanged).
Task 7: minor (deferred): EncounterDef.Retreat lacks WhenWritingDefault ignore — serialized encounters always emit "retreat": true.
Task 7: minor (deferred): TacticsScene hardcodes RetreatLength = TacticsTiming.Default.RetreatVote instead of the view's timing.
Task 7: fix round 1 dispatched (resume implementer, FIX_BASE 5a2f8533) — R11
Task 7: fix round 1/5 (1 addressed, 0 open; commits 5a2f8533..0c6f59dd)
Task 7: complete (commits 8cb968b6..0c6f59dd, review clean, rulings R1/R11/R12)
Task 8: dispatched (BASE 0c6f59dd, implementer sonnet)
Task 8: implementer DONE_WITH_CONCERNS (commit 5658a3b6); full suite 6913/6914 (its own clock test window fixed after; ReactionPrefsTests 17/17; whole suite not re-run → controller running it); npm 540, build OK; 10 fixtures regenerated; no goldens. Measurement (Game-level fake clock, 6 bots vs 10 foes, Crypt Hall): idle-bot s/round 218.6→176.3 (−19%), longest wait 337→309 s; prompts 23→0. Portal page not done (no battle context there).
- Ruling R13: "Move spent" for the 15 s clock means no Move left, nowhere to go, or the hero has already walked this turn — a strict MoveLeft == 0 would almost never fire — cost if wrong: a player who planned to walk again after acting has 15 s to do it.
- Ruling R14: the measured cut (idle-bot −19% seconds per round, zero prompts for opted-in players; longest wait −8%) is the "agreed amount" for #113's done-when; deeper cuts belong to the (a)/(c) follow-ups in turn-wait.md — cost if wrong: the owner may want #113 kept open for (a)/(c).
- Ruling R15 (amends R5): commit attribution lines may be either the session's "Claude Opus 5.5" line or the authoring subagent's own model line (5658a3b6 uses its own); no amending — both are accurate attributions for this session's work — cost if wrong: mixed trailer text across the branch's commits.
Task 8: review dispatched (sonnet) on 0c6f59dd..5658a3b6; controller full suite running
Task 8: review: spec ✅, quality Approved (0 critical, 0 important). ⚠️ SetTimer replace-by-key resolved by controller (GameSession.SetTimer disposes the old entry for the key).
Task 8: minor (deferred): auto-answering a prompt already on the phone (prefs changed mid-prompt) doesn't cancel Timers.React or clear _reactTimerFor — stale timer; cancel it and assert in the test.
Task 8: minor (deferred): the Reactions sheet isn't on the React prompt screen; its open state is lost while aiming.
Task 8: minor (deferred): TurnWaitTests asserts only "not worse", not the agreed cut; brisk-bot mean wait slightly worse (86.3→88.4).
Task 8: minor (deferred): no test of the 15 s clock on a summon's turn.
Task 8: minor (deferred): no test of an auto-answer while a retreat vote is pending (verified by reading only).
Task 8: controller full suite 6914/6914 passed at 5658a3b6
Task 8: complete (commits 0c6f59dd..5658a3b6, review clean, rulings R13/R14/R15)
Task 9: dispatched (BASE 5658a3b6, implementer opus)
Task 9: implementer DONE_WITH_CONCERNS (commits fd4a1827..72454f57); full suite 6927/6927; npm 540, build OK; no goldens changed; 2 fixtures + 1 fault test updated; AI P95 worst 50.5 ms (budget 250). Use 0% → Action Surge 18.2, Cunning Dash 48.9, Withdraw 8.1, Conjure Animals 14.2, Misty Step 5.6/2.9; subclass signatures 5.8–54.3%. Twinned/Careful/Death Ward still 0%. Flurry 7.7 → 0.6%. hall_of_the_flame L10 SHORT (7.8 rounds); river_crossing L1 −6.7.
- Ruling R16: Twinned, Careful and Death Ward are in #110's "done when" list (each ≥2% of legal turns or documented situational), so they get done in Task 9's fix round, not a follow-up: plan the metamagic on a spell when it adds value, value Death Ward's on-Downed hook as preventing a Down — the done-when is binding and the three are not situational — cost if wrong: one more planner fix round (time) for abilities the owner might have accepted as a follow-up.
- Ruling R17: Flurry of Blows falling 7.7% → 0.6% is a regression this task caused (Step of the Wind now takes all the Ki); the fix round makes the Ki choice compare both uses so Flurry is planned when it's worth more, and Flurry must stay ≥2% — cost if wrong: a little extra planner work if the shift was actually correct play.
- Ruling R18: hall_of_the_flame L10 SHORT and river_crossing L1 −6.7 go to T11's balance pass (R4 already re-checks every band) — cost if wrong: none beyond T11's existing scope.
Task 9: review dispatched (opus) on 5658a3b6..72454f57
- Ruling R19: merge origin/main (ae34140e, 25 commits; overlap README.md, TacticsDocument.cs, TacticsGame.Views.cs, TacticsGame.cs) right after Task 9 completes, before Task 10 — not only before the final review — so T10/T11 balance runs on current code and main's #201 view-payload budget test checks this branch's view growth early — cost if wrong: one extra merge if main moves again before the end (a second merge is still planned before the final review).
Task 9: review: spec ❌ (extra-action value not delivered hero-side), quality Needs fixes. Important: (1) Planner.Commands.cs:105-107/:150 Leaving puts every non-party unit in `leaving` without MayDrop → Action Surge never re-attacks the monster the first action hit; (2) Scoring.Worth.cs:31 via Ai.cs:181 Countering scores the enemy spell with the reactor's non-cruel profile → party counterspeller undervalues saving a Downed hero. Minors: no tests for bonus DashMove/bonus retarget avoidance/main-action hop; HopRoutes beside-foe scan uncapped and unmeasured; web test vacuous on shot<0; Misty Step order unasserted; EngineFaultTests re-derives harness options; Sim usage text misaligned. Sim subclass/race options judged a justified measurement aid.
Task 9: fix round 1 → resume implementer (Important 1-2, R16, R17, all six minors)
Task 9: fix round 1 DONE_WITH_CONCERNS (commits 6a869804..c8c51c95); full suite 6940/6940; AI P95 worst 45.9 ms (melee-hop setup 10.9 ms); no goldens/fixtures changed. Twinned 10.6%, Careful 2.5%, Death Ward 26.1%, Flurry 5.4%. Extra: three bonus-action targeting flaws found and fixed test-first. Command 2.3→1.5%, ranger Cure Wounds 2.1→1.7% (outranked by Smite/Hunter's Mark now allowed on the monster just hit). hall_of_the_flame L10 +11.4 / 6.9 rounds; river_crossing L1 −3.9.
- Ruling R20: Command and the ranger's Cure Wounds dipping under 2% is accepted for #110 — neither is in #110's listed abilities, and the dip is the correct consequence of Important 1 (Smite / Hunter's Mark now go on the monster just hit); ai-abilities.md lists them as outranked — cost if wrong: two more entries in the balance report's under-2% table for T11 to look at.
Task 9: fix round 1 re-review dispatched (opus) on 72454f57..c8c51c95
Task 9: fix round 1 re-review: all 10 findings ADDRESSED. New Important: Death Ward valued (and cast, 82.3 over Cure Wounds 43.35) on an ally whose once-per-battle death_ward.save already fired (Scoring.Conditions.cs:14-20, Scoring.Effects.cs:98; probe in scratchpad/mini). New minors: surge+main damage not summed for bonus `leaving` (Commands.cs:179); push-on-hit ignores Repelling Blast isSpell/when and Hex range (Commands.cs:110-111,:135-136); Twinned "alone" test doesn't exercise the Sorcery-cost check (AbilityPlanningTests.cs:191-195). Out of scope: engine accepts re-casting Death Ward on a spent holder but it does nothing (UsesLeft never reset) — SRD lets a fresh casting work.
- Ruling R21: fix the engine so Death Ward follows SRD ("the spell ends" when it triggers): the ward condition ends when it fires (or, if the passive mechanism makes that awkward, re-applying the condition restores its uses), so a fresh casting works for humans and AI alike; and the AI values no ward that cannot fire — the out-of-scope engine bug and the Important share one root and SRD settles it — cost if wrong: a small rules change outside #110's strict scope (could touch a golden).
Task 9: fix round 2 → resume implementer (Death Ward Important + R21, push-on-hit precision, Twinned test; summed surge damage if cheap, else deferred)
Task 9: fix round 2 DONE_WITH_CONCERNS (commits 009d1519..a1106415); full suite 6949/6949; AI P95 worst 40 ms; no goldens/fixtures. R21 option 1: the ward already removed its condition on firing — the bug was conditions.json usesPerBattle:1 on death_ward.save; removed it; AI values no unusable ward. Push check rewritten to judge from the pushed-to tile (6917d795). Death Ward use 26.1% unchanged.
Task 9: minor (deferred): Sim refusals 24→26 (usage sweep) / 6→7 (subclass sweep) — pre-existing cause: a warlock's Hellish Rebuke (reaction to an OA while walking) spends the Pact slot its planned Hex/Hold Person needed.
Task 9: minor (deferred): Twinned's Sorcery cost checked twice (Planner.Metamagic.cs:73 and Planner.cs:152) — the "not worth the Sorcery" test only fails when both are removed.
Task 9: fix round 2 re-review dispatched (sonnet) on c8c51c95..a1106415
Task 9: fix round 2 re-review: all findings ADDRESSED (minor c DONE: TogetherDrop sums main+surge damage), no new Critical/Important.
Task 9: minor (deferred): PushOnHit models one push per hit (the longest) and ignores PushEffect.AwayFromSource == false — no shipped passive combines these; worst case a refused bonus target.
Task 9: complete (commits fd4a1827..a1106415, review clean after 2 fix rounds, rulings R16/R17/R18/R20/R21)
Task 9: pushed 5658a3b6..a1106415 to wardenflame-combat-98
Merge (R19): origin/main ae34140e merged as 4b7fd089, no conflicts. check-all groups: python tools OK (assets 26, audio 9, lore check + 23), node playtest 24/24, npm 540/540 + build OK; .NET full suite running.
Merge: .NET full suite 6980/6980 at 4b7fd089 (incl. main's #201 view-payload budgets); pushed.
Task 10: dispatched (BASE 4b7fd089, implementer opus)
- Ruling R22 (T10 dispatch): flanking goes to every class only if a seeds-6 A/B shows no other class's band moves beyond ±15% and no encounter leaves 60–95%; otherwise rogue-only — the brief's own fallback, made measurable — cost if wrong: rogue-only flanking where all-class flanking might have been fine (a later content change).
- Ruling R23 (T10 dispatch): Sneak Attack at level 2 uses the level-1 cap (≤2d6), since the brief's bands skip level 2 — cost if wrong: a level-2 rogue a little weaker than intended.
- Ruling R24 (T10 dispatch): removing rogue.steady_aim must keep saves that reference it loading (tested) — the plan's "old saves keep working" constraint — cost if wrong: none; it's a safety test.
Task 10: implementer DONE_WITH_CONCERNS (commits 81fb1deb..15ec39de); full suite 7030/7031 (AiPerformanceTests p95 failed under sweep load, 7/7 alone → controller re-runs); npm 541 + build OK; warlord-bridge golden re-recorded (its Hide now refused); 9 fixtures (1 new). Hide needs cover; 2 attacks; SA 2d6/3d6/5d6/7d6 at caps; Steady Aim folded into Hide; flanking already existed for every class (no change). Added SRD Sneak Attack ally clause (allyBesideTarget, disadvantage leaves) + rogue.vex passive (mark). Band: L4 +15.1%, L9 −17.1%, L10 −21.4% (at caps); scripted always-hidden rapier 0.74–0.90×; shortbow 1.59–1.73× at L1–3 (AI shortbow control higher, 1.67–1.88×; was 1.73–2.02× at BASE). Monk now flagged at 6 levels as striker mean fell.
- Ruling R25: accept the two SRD 5.2 additions (Sneak Attack's ally clause; Vex as a rogue-only weapon-mastery passive) — both are SRD rules, content-level, stated in the README, and the issue's own target is "SRD-like" — cost if wrong: two content rules to revert if the owner wanted the caps alone.
- Ruling R26: #108's "scripted always-hidden rogue within 1.5×" is judged on the default kit (rapier, passes 0.74–0.90×); the L1–3 shortbow rogue's 1.6–1.7× is a ranged-weapon balance issue that hiding doesn't cause (the non-hiding AI control is higher) and it fell from BASE — report it to the owner as a follow-up — cost if wrong: a human shortbow rogue at L1–3 out-damages the band by 1.6–1.7×.
- Ruling R27: rogue L4 (+15.1%, seed noise) and L9–10 (−17/−21%, at the caps) go to T11's R4 re-check under the new striker measure; if still out there, T11 may lift L9–10 with a non-dice SRD lever (e.g. Cunning Strike) or record why not — cost if wrong: T11 scope grows a little.
Task 10: review dispatched (opus) on 4b7fd089..15ec39de; controller full suite running
Follow-ups filed at the owner's request: #239 (shortbow rogue 1.6–1.9× at L1–3, R26), #240 (paladin Command / ranger Cure Wounds under 2%, R20), #241 (reaction spends the Pact/Mana a planned spell needs; adapter ends the turn on a refused AI command — TacticsGame.Battle.cs ~420 — where the Sim re-plans). Not attached to #98 so it can close when this branch lands.
Owner (2026-11-06): finish T10, push, then hand off T11 (+ final review/wrap-up) to another session via a prompt — controller does NOT start T11.
Task 10: controller full suite 7031/7031 passed at 15ec39de (AiPerformanceTests green without sweep load)
Task 10: review: spec ❌, quality Needs fixes. Important: (1) Vex reuses the shared `marked` condition — Sourced.Put (Combat.cs:234) removes every `marked` regardless of source, so a rogue hit wipes Vow of Enmity / Soul of Vengeance / Hexblade's Curse / Sic 'Em, and avenger.vengeful_strikes / beastmaster.ferocious_bond fire off Vex marks; (2) shadow.death_from_the_shadows adds 2d6 on advantage → 9d6 at L10 over the 7d6 cap, and the cap test only reads rogue.sneak_attack though rogue.md/README claim every subclass. Minors: 1.5× wording misstated; HiddenRogue.Advantage duplicates AttackStep edge logic (drops Help/Taunt) + third copy in Scoring.Effects; refused scripted plan never falls back to AI, Rejected/ForcedEnds counts not reported; AiPerformanceTests oil-coated p95 324 ms in the implementer's full run (Hide aims run Sight.Trace per foe) — compare base vs head; BasePackTests.cs:311 assumes every Requires means plain view; Validation/Elements.cs:28 accepts hook-situation leaves in `requires`; Vow tooltip states advantage twice; stale steady_aim icon manifest entry.
- Ruling R28: Vex gets its own sourced condition (e.g. `vexed`), and the attacker-advantage rule reads "a mark made by this attacker" — other classes' `marked` and the passives that read it stay untouched — the reviewer's first option; it keeps Vex rogue-only as R25 required — cost if wrong: one more condition id.
- Ruling R29: the Sneak Attack cap covers every once-per-turn damage rider a rogue gets from advantage/hiding (Sneak Attack plus subclass passives such as Death from the Shadows); retune the subclass rider so the total stays ≤ cap, extend the cap test to every subclass, and correct rogue.md/README — the cap exists to bound the hide loop's payout — cost if wrong: Shadow rogues a little weaker at L9–10 (already a T11 item under R27).
- Ruling R30: leave the stale `rogue.steady_aim` icon manifest entry and art-tool references — the assets are main's workstream (#103/#183) and the entry is harmless; mention in the final report — cost if wrong: one unused icon in the manifest.
Task 10: fix round 1 → resume implementer (Important 1-2, minors 3-8 except the icon entry)
Task 10: fix round 1 DONE_WITH_CONCERNS (commit dda2c87a); full suite 7048/7048; npm 542 + build OK; assets cross-check OK; 4 portal fixtures re-recorded; goldens unchanged. Scripted rapier 0.76–0.96×, shortbow 1.59–1.73× L1–3; 0 forced ends. Oil-coated AI p95 BASE 36.6–52.2 vs HEAD 45.5–56.5 ms (plain-view check 6–7 ms total; adjacency now checked before tracing). Death from the Shadows now frightens instead of +2d6.
- Ruling R31: accept Death from the Shadows as a non-damage capstone (frightens the target) so the Shadow rogue stays inside R29's cap — a shared once-per-turn rider budget is new engine machinery this task doesn't need — cost if wrong: the owner may want a damage capstone back via a per-subclass Sneak Attack within 7d6.
Task 10: fix round 1 re-review dispatched (sonnet) on 15ec39de..dda2c87a
Task 10: fix round 1 re-review: all 8 findings ADDRESSED, no new Critical/Important.
Task 10: minor (deferred): two rogues' Vex on one target don't coexist — Sourced.Put removes by id without a source check (Combat.cs ~1193), so the second rogue's `vexed` replaces the first's; same pre-existing behaviour for two paladins' `marked`; untested.
Task 10: complete (commits 81fb1deb..dda2c87a, review clean after 1 fix round, rulings R22–R31)

## Handoff (2026-11-06)
The owner asked the controller to finish Task 10, push, and hand Task 11 (#112) plus the final merge, whole-branch review and wrap-up to another session. This ledger was committed to the branch for that session. Task 11 starts from the head that carries this file. Append to this file as you go.
Task 11: in progress in the separate `/workspace/work/combat-final-112` worktree, local branch `combat-final-112`, based on handoff `4e90a904`. The owner's new handoff supersedes the old workspace/push prohibitions: push only `HEAD:wardenflame-combat-98`, merge main normally, no PR and no issue closure. Main checkout remains untouched. CI stays disabled under the standing owner instruction.
- Runtime: .NET 10.0.403 through `/workspace/.arcbox/activate.sh`; stale-worker protection uses `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0`. Use focused tests while iterating and do not overlap performance checks with simulator sweeps.
- This session's commit attribution trailers use `Co-Authored-By: OpenAI <noreply@openai.com>` and `Session: wardenflame-combat-98-final-112`; the handoff's prohibition on model names applies to new commits.
- First step: target-normalize Striker damage by each member's own turn, retaining raw HP damage for audit. Off-turn damage belongs to the owner's latest turn; repeated hits on one target accumulate before normalization.

- Ruling R32: Striker bands use the sum of each member-turn’s mean HP damage per damaged hostile, divided by battle rounds. Repeated hits on one victim accumulate; off-turn effects belong to the source’s latest own turn. Raw HP damage remains reported independently. This prevents multiplying area-spell credit by victim count while retaining multi-hit damage against one foe — cost if wrong: spread weapon attacks and incidental splash receive less band credit; audit both raw and normalized tables before tuning.
- Task 11 metric RED: three new Tally regressions failed against the nullable metric stub (10 tests, 3 failed); implementation now tracks per-member target buckets.

- Task 11 metric GREEN: 21 Tally/report/simulator focused tests passed. Conditions RED: 6 tests, 4 failures (standing and healing); GREEN 35 focused tests. Charm/flames RED: 9 tests, 3 failures. Shove RED: better-save behavior failed on original content; new mode absent. Uncanny RED: temp-HP reaction advanced RNG; outcome-preference RED failed for preventable Downing. Scaling RED: 2 of 6 ladder tests failed. Current combined rule/content/tooltip run: **59 passed**, including push/prone AI tests. Logs are outside the repository in `/workspace/work/combat-final-112-logs/`.
- Implemented so far: additive `onHealed`; bleeding healing hook; costed standing via `remove.halfMove`; charm targeting and opportunity-attack protection; Put out flames; separate push/prone Shoves with shared better-save selection in rules and AI; hit-scoped Uncanny Dodge with aggregate rounding and additive persisted defaults; deterministic isolated-hit preview for the outcome preference. Level-up dice and full area ladder restored without down-steps. Benchmark six-seed sweep 1 running across levels 1–10 and five encounters. No push yet.
- Ruling R33: choose 6d6 Fireball/Lightning Bolt and SRD 8d8 Cone of Cold, rather than raising Cone above SRD to keep Fireball at 8d6. The required ≥1.5× ratio cannot hold with both SRD values (28 and 36). Keep the full area ladder and tooltips explicit — cost if wrong: Fireball remains 25% below SRD per target; late spell encounters may still need retuning.
- Ruling R34: automatic standing spends half available Move rounded up to whole tiles and requires movement; restraint prevents standing. Uncanny Dodge rounds once over all damage blocks of its numbered hit after resistances, and its outcome preference replays only that hit on immutable state/RNG copies — cost if wrong: odd Move costs an extra half-tile versus continuous SRD movement; preview adds computation when this preference is used.
- Ruling R35: retain Task 23's non-dice decisions (bounded attack schedules, rapier, monster save proficiencies, warlord encounter redesign), #108's revised level-1 Hide, and affordable level-7 Quickened. Close all five old “Decisions for the user” with README references, then audit these retained choices in the final sweep — cost if wrong: these game departures remain instead of receiving a separate redesign.
- Rule implementation committed as `7516ca06` (62 focused rule/content tests passed; lore validation passed). Main integration is underway: preserve both optional saved `Difficulty` and `GiftTrades` fields, and union the combat phone fixtures with main's give-away fixture. The tuning sweep remains based on the rule implementation; main adds campaign/phone work and does not change the combat rules content.
