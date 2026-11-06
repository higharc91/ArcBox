# Combat #98 final balance and integration report (#112)

Task 11 starts from `4e90a904`. Main was merged normally at `9dd18828`. The independent review covered merge-base `9f8c2fd8` through `036141da`; one fix wave is included in the final measured revision below. CI remains manually disabled. This session pushes only to `wardenflame-combat-98`, opens no PR and closes no issues.

## Task 11 changes and retained departures

Striker bands now score the sum of each member-turn's mean HP damage per damaged hostile, divided by battle rounds. Repeated hits on one victim accumulate; off-turn damage joins the latest own turn, pre-first-turn damage joins the first turn, and a resumed Delay shares its original bucket. Overkill and temporary-HP damage are excluded. Raw damage remains alongside normalized damage in the full table. This removes victim-count multiplication as a reason to shrink area dice.

| Issue row | Final behavior / departure |
|---|---|
| Area ladder | Burning Hands 3d6; Fireball and Lightning Bolt 6d6; Cone of Cold 8d8. Expected per-target damage is 10.5 → 21 → 36, satisfying both 1.5× steps. Fireball remains below SRD 8d6 because SRD Fireball 28 and Cone 36 cannot satisfy the required second ratio together. |
| Other area spells | Ice Storm 2d8 + 4d6; Wall of Fire initial 5d8, ongoing 1d8; Cloudkill initial 6d8, ongoing 5d8. Wall ongoing and Cloudkill initial are retained ladder/balance departures. |
| Level-up dice | Flat d8 monk die with two main attacks; flat d12 Eldritch Blast beams; flat bard 2d4 Healing Word / 2d8 Cure Wounds. No primary direct-dice step decreases on level-up. Monk/beam schedules and die sizes remain game departures, explicit in README/tooltips. |
| Shove | Separate push-one and prone choices; defender uses higher total STR/DEX save bonus. Existing push-landing scoring remains, prone scoring includes the standing cost; both have AI tests. |
| Prone | Automatic standing requires available movement and spends half, rounded up to whole tiles. A restrained unit cannot stand. Tile rounding departs from continuous movement. |
| Charmed | Cannot target the charmer harmfully, including areas, opportunity attacks and Twinned's automatic extra victim. Other attacks have no charm-only disadvantage. |
| Bleeding | Ends when actual healing restores HP, including reviving a Downed unit; zero/full-HP healing does not fire onHealed. The hook receives the known healer. |
| Burning | Common Put out flames spends the Action to remove burning from self or an adjacent ally. Water behavior is retained. |
| Uncanny Dodge | Halves the triggering numbered hit, with aggregate rounding across its damage blocks after resistances; no temporary HP or extra RNG. Hit state clears with the drained queue. The outcome preference requires known Downing without Dodge and known survival with it; an unresolved secondary player decision is unknown. |

Other tuning uses flat 4d8 Conjure Barrage, early 1d4 Hunter's Mark, one Second Wind, documented class pool/default-pick adjustments and explicit controller/guardian AI priorities. Monk retains its original Ki curve, early movement and unrestricted Heightened Flurry; Stunning Strike costs 2 Ki. The finale scales Maren HP to 105/180/240 for party sizes 4/5/6 and uses one cult leader at size 4, with the ritual, dragon and phase triggers preserved. All five old tracker decisions are resolved with README references. R31's frightening Shadow capstone is unchanged.

Brutal Strike and Cleaving Blow can push a later attack's target out of reach: each attack rechecks its target, then retargets or is spent. Sweeping Cleave's extra rider applies once per use across targets, including a critical first hit; later attacks retain base weapon damage. Two rogues' Vex and two sources' other marks now coexist, move and expire independently; old sourced `marked` saves retain advantage.

## Final measurement protocol

The audit uses seeds **1–20**, Normal difficulty, human/default-kit characters with no subclass, six sampled compositions at each size 4–6, no party items, the existing party generator seed 20261030, and round cap 30. `--rotate-formation` cyclically rotates spawn seats across seeds without consuming RNG or changing ids/companion selection. Legacy fixed formation remains the default without that flag. Paired companion comparisons use the same size-4 compositions/seeds and the same rotation. Historical fixed-seat reports are not a direct A/B comparison.

Five benchmark encounters run at every level 1–10 (18,000 battles). Four additional story encounters run at their own levels (1,440); the five existing own-level rows are reused. All nine companion-filled size-4 comparisons run at twenty seeds (1,080). The default rapier scripted rogue runs at all ten levels over the same benchmark encounters and the sampled parties containing a rogue. Full-precision capture uses the public, unchanged `SimRunner.Run` and `Report.Markdown` APIs; a smoke report exactly matched the official CLI. Counts and cap decisions below use unrounded JSONL, not printed one-decimal values. Performance tests run only after simulation.

| Measured revision | Rules hash | Battles across all cohorts |
|---|---|---|
| 0fbd804c5af3ff49350a19cdaa81103070a3fea0 | a52dd6d512ec1c1b148e6888d2e2e072c4788b0cc1a9855716df7105156a47a4 | 28520 |

CLI-equivalent benchmark command (story/companion/scripted cohorts change only the displayed configuration options):

```sh
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --rules content/tactics/base/rules --encounters crypt_hall,crystal_cave,forest_ruins,river_crossing,warlord_bridge --levels 1,2,3,4,5,6,7,8,9,10 --sizes 4,5,6 --parties 6 --seeds 20 --rotate-formation
```

## Final judgment and every residual outlier

The full benchmark wins 14168/18000 (78.71%), averaging 8.47 rounds across intended and off-level matchups. **15/120 class/level role values remain outside ±15%.** 0/9 intended-level story cells miss the win or round target; 2/9 paired companion win-rate gaps exceed ten percentage points. Scripted default-rapier cap passes 10/10 levels. These are measured exceptions, not a claim that all balance targets passed.

Several candidates were retuned before this final pass: initial 47/120 flags fell to 32/120 and 31/120 in six-seed full-level screens; seat rotation removed additional placement bias. Guardian spell-heavy weights and a once-per-battle Heightened Flurry limit were rejected after they failed to improve behavior. Fighter/paladin mitigation differences, the remaining late controller gaps and the level-4 rogue outlier were retained for explicit review rather than shrinking restored area dice, exceeding rogue caps or distorting HP/hit dice merely to fit a proxy. Defender mitigation includes HP taken and missed attacks, so it measures who absorbed pressure as well as prevention. The final table, rather than the tuning numbers, is authoritative.

| Level | Band | Class | Value | Deviation |
|---|---|---|---|---|
| 1 | Defender | paladin | 3.71 | -19.10% |
| 2 | Defender | paladin | 4.69 | -18.08% |
| 4 | Striker | rogue | 5.22 | +18.12% |
| 5 | Defender | fighter | 6.70 | +16.98% |
| 6 | Defender | fighter | 6.91 | +17.46% |
| 7 | Defender | fighter | 6.68 | +17.59% |
| 8 | Defender | fighter | 7.12 | +17.25% |
| 9 | Control | druid | 1.93 | +30.57% |
| 9 | Control | wizard | 1.02 | -30.57% |
| 9 | Defender | fighter | 4.91 | +15.46% |
| 9 | Defender | paladin | 3.35 | -21.16% |
| 10 | Control | druid | 2.17 | +16.27% |
| 10 | Control | wizard | 1.56 | -16.27% |
| 10 | Defender | fighter | 5.85 | +38.26% |
| 10 | Defender | paladin | 2.91 | -31.20% |

All off-level win/round cells and capped counts, including every additional LONG/SHORT/CAPPED flag, are preserved in the full benchmark appendix. Intended-level exceptions are explicit in the next table.

## Story encounters at their own level

| Encounter | Level | N | Win | Rounds | Capped | Mean Downs | Mean Outs | Flag |
|---|---|---|---|---|---|---|---|---|
| river_crossing | 1 | 360 | 88.61% | 11.03 | 8 | 4.54 | 0.97 |  |
| ford_assault | 1 | 360 | 71.67% | 9.85 | 11 | 4.66 | 1.12 |  |
| forest_ruins | 3 | 360 | 78.06% | 9.66 | 2 | 2.13 | 0.79 |  |
| crypt_hall | 5 | 360 | 87.22% | 9.90 | 2 | 1.87 | 0.56 |  |
| warlord_bridge | 5 | 360 | 91.11% | 11.12 | 0 | 3.43 | 0.76 |  |
| crystal_cave | 7 | 360 | 86.94% | 10.49 | 0 | 2.87 | 1.00 |  |
| rime_tower | 7 | 360 | 92.50% | 10.20 | 1 | 1.78 | 0.51 |  |
| keep_gate | 9 | 360 | 85.56% | 9.52 | 3 | 1.88 | 0.89 |  |
| hall_of_the_flame | 10 | 360 | 68.61% | 9.48 | 0 | 2.51 | 1.23 |  |

### Intended-level results by party size

The primary encounter judgment above aggregates sizes 4–6, matching the standard simulator report. Individual size cells still miss absolute targets, especially size 4; every such miss is flagged below. These remain balance limitations even where the aggregate passes.

| Encounter | Size | N | Win | Rounds | Capped | Flag |
|---|---|---|---|---|---|---|
| river_crossing | 4 | 120 | 72.50% | 13.38 | 8 | LONG |
| river_crossing | 5 | 120 | 95.00% | 10.06 | 0 |  |
| river_crossing | 6 | 120 | 98.33% | 9.64 | 0 | HIGH |
| ford_assault | 4 | 120 | 53.33% | 10.46 | 4 | LOW |
| ford_assault | 5 | 120 | 71.67% | 10.49 | 6 |  |
| ford_assault | 6 | 120 | 90.00% | 8.60 | 1 |  |
| forest_ruins | 4 | 120 | 38.33% | 11.55 | 2 | LOW |
| forest_ruins | 5 | 120 | 95.83% | 10.00 | 0 | HIGH |
| forest_ruins | 6 | 120 | 100.00% | 7.42 | 0 | HIGH, SHORT |
| crypt_hall | 4 | 120 | 61.67% | 11.83 | 2 |  |
| crypt_hall | 5 | 120 | 100.00% | 9.83 | 0 | HIGH |
| crypt_hall | 6 | 120 | 100.00% | 8.04 | 0 | HIGH |
| warlord_bridge | 4 | 120 | 76.67% | 11.08 | 0 |  |
| warlord_bridge | 5 | 120 | 96.67% | 11.59 | 0 | HIGH |
| warlord_bridge | 6 | 120 | 100.00% | 10.68 | 0 | HIGH |
| crystal_cave | 4 | 120 | 62.50% | 13.47 | 0 | LONG |
| crystal_cave | 5 | 120 | 98.33% | 10.08 | 0 | HIGH |
| crystal_cave | 6 | 120 | 100.00% | 7.93 | 0 | HIGH, SHORT |
| rime_tower | 4 | 120 | 77.50% | 13.70 | 1 | LONG |
| rime_tower | 5 | 120 | 100.00% | 9.18 | 0 | HIGH |
| rime_tower | 6 | 120 | 100.00% | 7.72 | 0 | HIGH, SHORT |
| keep_gate | 4 | 120 | 58.33% | 12.55 | 3 | LOW, LONG |
| keep_gate | 5 | 120 | 98.33% | 9.90 | 0 | HIGH |
| keep_gate | 6 | 120 | 100.00% | 6.12 | 0 | HIGH, SHORT |
| hall_of_the_flame | 4 | 120 | 70.83% | 8.28 | 0 |  |
| hall_of_the_flame | 5 | 120 | 67.50% | 10.34 | 0 |  |
| hall_of_the_flame | 6 | 120 | 67.50% | 9.82 | 0 |  |

## Companion-filled parties: paired size 4

Target: win-rate difference within ten percentage points; the normal comparison here is size 4, not all sizes. Both columns use 120 battles per encounter. Absolute size-4 win/round misses remain visible alongside the paired-gap flags.

| Encounter | Normal win | Companion win | Gap pp | Normal rounds | Companion rounds | Caps normal/comp | Flag |
|---|---|---|---|---|---|---|---|
| river_crossing | 72.50% | 77.50% | +5.00 | 13.38 | 12.88 | 8/8 |  |
| ford_assault | 53.33% | 64.17% | +10.83 | 10.46 | 10.80 | 4/5 | OUTSIDE ±10 pp |
| forest_ruins | 38.33% | 39.17% | +0.83 | 11.55 | 11.28 | 2/2 |  |
| crypt_hall | 61.67% | 63.33% | +1.67 | 11.83 | 13.62 | 2/6 |  |
| warlord_bridge | 76.67% | 71.67% | -5.00 | 11.08 | 10.88 | 0/0 |  |
| crystal_cave | 62.50% | 65.00% | +2.50 | 13.47 | 13.42 | 0/1 |  |
| rime_tower | 77.50% | 79.17% | +1.67 | 13.70 | 13.30 | 1/1 |  |
| keep_gate | 58.33% | 53.33% | -5.00 | 12.55 | 12.68 | 3/1 |  |
| hall_of_the_flame | 70.83% | 55.83% | -15.00 | 8.28 | 8.93 | 0/0 | OUTSIDE ±10 pp |

## Default rogue and scripted ceiling

R27 is judged at all ten levels under the new measure. Default rogue bands and the scripted always-hide-when-legal rapier policy are measured separately. The cap is 1.5 times the benchmark Striker mean of class means at that level. Exact full-precision comparisons decide pass/fail; no shortbow exception is included in this default-kit claim.

| Level | Default rogue | Default deviation | Striker mean | Script normalized | Script raw | 1.5× cap | Script ratio | N | In cap |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 3.25 | +3.39% | 3.15 | 3.15 | 3.29 | 4.72 | 1.0024× | 800 | True |
| 2 | 4.02 | +7.59% | 3.73 | 4.03 | 4.24 | 5.60 | 1.0806× | 800 | True |
| 3 | 4.63 | +10.91% | 4.17 | 4.46 | 4.67 | 6.26 | 1.0695× | 800 | True |
| 4 | 5.22 | +18.12% | 4.42 | 4.97 | 5.17 | 6.63 | 1.1235× | 800 | True |
| 5 | 6.12 | +2.89% | 5.95 | 5.88 | 6.22 | 8.92 | 0.9890× | 800 | True |
| 6 | 6.80 | +8.54% | 6.26 | 6.59 | 6.99 | 9.39 | 1.0514× | 800 | True |
| 7 | 6.96 | +1.18% | 6.88 | 6.96 | 7.35 | 10.32 | 1.0115× | 800 | True |
| 8 | 7.47 | +0.46% | 7.43 | 7.34 | 7.76 | 11.15 | 0.9878× | 800 | True |
| 9 | 8.00 | -4.65% | 8.39 | 8.01 | 8.51 | 12.58 | 0.9555× | 800 | True |
| 10 | 7.59 | -6.43% | 8.11 | 7.51 | 8.13 | 12.17 | 0.9259× | 800 | True |

Sneak Attack remains capped at 2d6/3d6/5d6/7d6 at levels 1/3–4/5–8/9–10, and the attack caps are unchanged. The level-9/10 default rogue is now within its band under the normalized measure, so no additional late dice or hiding lever was needed. Level 4 remains an explicit outlier. No new Sneak Attack dice lever was used. Historical #239 shortbow ratios (1.6–1.9× early Striker mean) used the old total-damage measure; they are carried as an existing follow-up, not misrepresented as a fresh measurement. The script is a heuristic, not a proof of the optimal human turn.

## Rare uses and existing follow-ups (R20)

| Carried #110 finding | Earlier usage | Disposition |
|---|---|---|
| rogue.steady_aim | 0.0% | Removed from playable content by #108; legacy saves load; stale art remains under R30. |
| sorcerer.misty_step in elf subclass sweep | 0.3% | Free Fey Step competes; human usage was 2.6%. |
| paladin.command | 1.5% | Smite competes for Oath; existing #240, excluded from this fix wave. |
| ranger.cure_wounds | 1.7% | Touch range and Hunter's Mark compete for Focus; existing #240. |

The complete current benchmark rare-use table is in appendix section 5, including every ability below 2% of legal own turns. Reactions are excluded from that denominator and common actions are listed without a rarity flag. Earlier #110 reported 43 rare abilities; its four focused carry-forward rows above retain their original context rather than replacing them with new rates silently. #239, #240 and #241 remain open follow-ups and are not fixed here. #241 may appear in the refusal/replan counts below.

| Cohort | Battles | Rejected commands | Replans | Forced ends | Capped |
|---|---|---|---|---|---|
| benchmark | 18000 | 173 | 1263 | 0 | 72 |
| companion-crypt_hall | 120 | 2 | 2 | 0 | 6 |
| companion-crystal_cave | 120 | 4 | 47 | 0 | 1 |
| companion-ford_assault | 120 | 1 | 1 | 0 | 5 |
| companion-forest_ruins | 120 | 5 | 5 | 0 | 2 |
| companion-hall_of_the_flame | 120 | 0 | 0 | 0 | 0 |
| companion-keep_gate | 120 | 7 | 7 | 0 | 1 |
| companion-rime_tower | 120 | 1 | 1 | 0 | 1 |
| companion-river_crossing | 120 | 0 | 0 | 0 | 8 |
| companion-warlord_bridge | 120 | 1 | 1 | 0 | 0 |
| scripted-rogue | 8000 | 156 | 731 | 0 | 35 |
| story-ford_assault | 360 | 1 | 1 | 0 | 11 |
| story-hall_of_the_flame | 360 | 3 | 3 | 0 | 0 |
| story-keep_gate | 360 | 1 | 1 | 0 | 3 |
| story-rime_tower | 360 | 3 | 3 | 0 | 1 |

## Integration, independent review and checks

The [whole-branch review and complete deferred-minor triage](review-final.md) records five Important findings, no Critical, and all 39 deferred entries as fix-now or leave with reasons. One fix wave addresses every confirmed Important and cheap Minor. Focused verification passes 176 rules checks and 47 game checks. The approved scoped re-review independently passes 46 focused checks with no remaining Critical/Important findings. Full local check-all is recorded below. CI remains disabled.

FINAL_GATE_PENDING

The first full gate exposed two obsolete test assumptions, three slow real-area sequences, missing deterministic passive coverage after AI tuning, and two missing icon-fallback records. The test expectations now follow the reviewed legacy-mark/controller behavior. Countdown, transformation and zone cleanup cues overlap within an area instead of pausing later victims; the 1.1-second bound and original seeds remain. Direct RED: 1.51 seconds. Focused GREEN: 100 presentation, 14 rules and 5 manifest checks. Added real engine fixtures preserve both Indomitable and Martial Advantage coverage. These changes affect presentation/tests/tool bookkeeping only, so the measured rules/AI/simulator revision is unchanged.

## Goldens and phone fixtures

Goldens were regenerated with `UPDATE_GOLDENS=1` and then verified normally (7/7). The changed golden files across the whole branch are:

| File | Why it changed |
|---|---|
| `river-crossing.events.json` | Task #115 changed Downed countdown to own-turn starts and persistent Delay order; the handwritten replay commands remain unchanged. |
| `warlord-bridge.events.json` | Whole-branch rules/AI changes alter the fight: phase clamping/guard, multiattack targeting, class priorities and final content. Task #112 refreshed events from the updated legal command recording. |
| `warlord-bridge.replay.json` | The earlier branch selected AI seed 2 for full showcase coverage. Task #112 retained that seed and re-recorded commands because the old script tried a second Second Wind after the pool changed from two to one. The taunt, ridge archers, collapse, phase, Deep fall and single battle ending assertions remain intact. |

The new `common.extinguish` and `common.shove_prone` actions explicitly use the existing generic phone ability-icon fallback; their labels identify the distinct choices. No new bespoke icon art is claimed.

No other golden files changed in Task #112. The initial refusal was corrected by recording legal current AI play, with no assertion weakened or test skipped.

The branch changes **58 phone fixture files** versus main; **41** differ from the handoff after Task #112 and main integration. Fixtures were regenerated with `UPDATE_FIXTURES=1`, then normally verified (17/17). Changes reflect difficulty/reaction/retreat/multiattack view data from the whole branch; new push/prone and extinguish choices; actual class pools/default picks; corrected condition and once-rider tooltips; and main's GiftTrades data preserved in integration. Focused phone hint/retreat tests pass 13/13. The full phone gate is recorded with check-all above.

Every changed fixture versus main:

- `camp-job.json`
- `camp-rolled.json`
- `camp-target.json`
- `camp-vote.json`
- `check-group-rolled.json`
- `check-group.json`
- `check-help-rolled.json`
- `check-help.json`
- `check-pick.json`
- `check-rolled.json`
- `check-search-rolled.json`
- `check-search.json`
- `choice-follower.json`
- `choice-leader.json`
- `dialogue-leader.json`
- `dialogue.json`
- `environment-status.json`
- `environment-turn.json`
- `inn-rooms.json`
- `portal-character.json`
- `portal-new.json`
- `portal-trinket-dormant.json`
- `portal-trinket.json`
- `react.json`
- `results-fled.json`
- `results-noRegion.json`
- `results.json`
- `retreat-status.json`
- `retreat-turn.json`
- `retreat-vote-proposer.json`
- `retreat-vote.json`
- `shop-open.json`
- `shop-poor.json`
- `shop-sell.json`
- `shop-sold-out.json`
- `shop-spectator.json`
- `status.json`
- `talk-check.json`
- `talk-vote.json`
- `temple.json`
- `turn-attacks.json`
- `turn.json`
- `vote-loot.json`
- `vote-tie.json`
- `vote.json`
- `walk-ambush.json`
- `walk-danger.json`
- `walk-follower.json`
- `walk-give-away.json`
- `walk-interact.json`
- `walk-leader.json`
- `walk-pinned-done.json`
- `walk-pinned.json`
- `walk-ready-to-rest.json`
- `walk-things-follower.json`
- `walk-things.json`
- `walk-trinket-presented.json`
- `walk-with-gold.json`



## Rulings I made

R1–R31 were made in earlier tasks; this session adds R32 onward. Every ruling is carried in order with its cost if wrong. The progress ledger remains authoritative for the original evidence.

| Ruling | Decision | Cost if wrong |
|---|---|---|
| R1 | T7 extends T2's after-loss difficulty hint to `Fled` results too — a fled party is a struggling party | one extra line on the results screen after a retreat. |
| R2 | T11 must keep T5's push-landing scoring working for the push mode of Shove and teach the AI the prone mode, with an AI test for each | the AI underuses the new prone Shove. |
| R3 | in T11, the "Shield and other defensive reactions: only when it changes the outcome" setting uses Uncanny Dodge when halving stops the hit from Downing the rogue, and otherwise doesn't | players who chose that setting take more damage than they expect. |
| R4 | T11 re-checks every band, the rogue's included, under the new striker measure and retunes any that leave ±15%; T10's bound is re-asserted there | one more simulator pass. |
| R5 | Historical task-session attribution policy, superseded by R15 and the current handoff. | subagent-authored commits carry a slightly imprecise attribution line. |
| R6 | accept #109 as done with 6/9 story encounters inside ±10 points — the issue's problem (companions hanging back, running out clocks, capped battles) is fixed (rounds 14.7–17.4 → 11.7–14.9, capped 21 → 16), the misses go both ways, and the finale's −11.6 is re-checked in T11's final balance pass | companion-filled parties stay ~17 points stronger at ford_assault and ~12 weaker at the finale than class-profile play until a later retune. |
| R7 | extend the steadfast guard to terrain collapse (Motion.FallIn) — a guarded unit caught by a collapse is placed on the nearest walkable non-deep tile (deterministic search, ties by tile order), takes the engine's existing fall damage if it has one, keeps phases guaranteed via the clamp, and emits PushResisted (or a sibling event) — the owner's #106 decision was "bosses stop at the edge and phases always fire", and a collapse kill skips the fight the same way a shove did | one more placement helper; bosses survive bridge collapses they used to die in. |
| R8 | fix the BeatScript area-detection fault in this branch, restore forest_ruins seed 7 in RealBattleTimingTests, and add a direct BeatScript test for the 3-beam Eldritch Blast + Repelling Blast case — this branch's AI change exposed it, and swapping seeds hides a TV glitch the branch would ship; tests are never skipped or quarantined | a presentation fix lands in a combat branch; if it proves large, the implementer reports back instead. |
| R9 | the auto-retarget fallback ("lowest-HP hostile in reach") excludes Downed units; if only Downed units remain in reach, the attack is spent — the plan's ruling meant "never waste attacks on a dead target", not "auto-finish Downed heroes", and auto-picking Downed heroes would tick their countdowns on every multi-attack (against spec §5.4's fair-play spirit); an explicitly listed Downed target (cruel AI choice) is still honoured | an occasional spent attack when only Downed foes remain in reach. |
| R10 | later hits of a riderOnce ability keep the ability's own first damage block (e.g. Quivering Palm 2d8+DEX, Halvard 3d8), not the unit's plain weapon damage — documented in schema/README, meets the done-when, and redefining "base weapon damage" for non-weapon-flagged abilities needs per-ability judgement the T11 balance pass can make for outliers | some abilities' later hits stay stronger than a plain weapon strike. |
| R11 | a Fled battle sets RestReady like a loss — the plan's ruling treats Fled as a loss for story progress, and every won or lost battle earns a rest today | a party that retreats rests once without having fought to the end. |
| R12 | an in-progress retreat vote is not persisted; a server restart cancels it and players can propose again — votes are transient social state, restarts are rare, and persisting it adds document fields for little value | a vote in flight during a restart must be re-proposed. |
| R13 | "Move spent" for the 15 s clock means no Move left, nowhere to go, or the hero has already walked this turn — a strict MoveLeft == 0 would almost never fire | a player who planned to walk again after acting has 15 s to do it. |
| R14 | the measured cut (idle-bot −19% seconds per round, zero prompts for opted-in players; longest wait −8%) is the "agreed amount" for #113's done-when; deeper cuts belong to the (a)/(c) follow-ups in turn-wait.md | the owner may want #113 kept open for (a)/(c). |
| R15 | Accept historical attribution without rewriting published commits; this session uses its own attribution and no model names in new commit text. | mixed trailer text across the branch's commits. |
| R16 | Twinned, Careful and Death Ward are in #110's "done when" list (each ≥2% of legal turns or documented situational), so they get done in Task 9's fix round, not a follow-up: plan the metamagic on a spell when it adds value, value Death Ward's on-Downed hook as preventing a Down — the done-when is binding and the three are not situational | one more planner fix round (time) for abilities the owner might have accepted as a follow-up. |
| R17 | Flurry of Blows falling 7.7% → 0.6% is a regression this task caused (Step of the Wind now takes all the Ki); the fix round makes the Ki choice compare both uses so Flurry is planned when it's worth more, and Flurry must stay ≥2% | a little extra planner work if the shift was actually correct play. |
| R18 | hall_of_the_flame L10 SHORT and river_crossing L1 −6.7 go to T11's balance pass (R4 already re-checks every band) | none beyond T11's existing scope. |
| R19 | merge origin/main (ae34140e, 25 commits; overlap README.md, TacticsDocument.cs, TacticsGame.Views.cs, TacticsGame.cs) right after Task 9 completes, before Task 10 — not only before the final review — so T10/T11 balance runs on current code and main's #201 view-payload budget test checks this branch's view growth early | one extra merge if main moves again before the end (a second merge is still planned before the final review). |
| R20 | Command and the ranger's Cure Wounds dipping under 2% is accepted for #110 — neither is in #110's listed abilities, and the dip is the correct consequence of Important 1 (Smite / Hunter's Mark now go on the monster just hit); ai-abilities.md lists them as outranked | two more entries in the balance report's under-2% table for T11 to look at. |
| R21 | fix the engine so Death Ward follows SRD ("the spell ends" when it triggers): the ward condition ends when it fires (or, if the passive mechanism makes that awkward, re-applying the condition restores its uses), so a fresh casting works for humans and AI alike; and the AI values no ward that cannot fire — the out-of-scope engine bug and the Important share one root and SRD settles it | a small rules change outside #110's strict scope (could touch a golden). |
| R22 | flanking goes to every class only if a seeds-6 A/B shows no other class's band moves beyond ±15% and no encounter leaves 60–95%; otherwise rogue-only — the brief's own fallback, made measurable | rogue-only flanking where all-class flanking might have been fine (a later content change). |
| R23 | Sneak Attack at level 2 uses the level-1 cap (≤2d6), since the brief's bands skip level 2 | a level-2 rogue a little weaker than intended. |
| R24 | removing rogue.steady_aim must keep saves that reference it loading (tested) — the plan's "old saves keep working" constraint | none; it's a safety test. |
| R25 | accept the two SRD 5.2 additions (Sneak Attack's ally clause; Vex as a rogue-only weapon-mastery passive) — both are SRD rules, content-level, stated in the README, and the issue's own target is "SRD-like" | two content rules to revert if the owner wanted the caps alone. |
| R26 | #108's "scripted always-hidden rogue within 1.5×" is judged on the default kit (rapier, passes 0.74–0.90×); the L1–3 shortbow rogue's 1.6–1.7× is a ranged-weapon balance issue that hiding doesn't cause (the non-hiding AI control is higher) and it fell from BASE — report it to the owner as a follow-up | a human shortbow rogue at L1–3 out-damages the band by 1.6–1.7×. |
| R27 | rogue L4 (+15.1%, seed noise) and L9–10 (−17/−21%, at the caps) go to T11's R4 re-check under the new striker measure; if still out there, T11 may lift L9–10 with a non-dice SRD lever (e.g. Cunning Strike) or record why not | T11 scope grows a little. |
| R28 | Vex gets its own sourced condition (e.g. `vexed`), and the attacker-advantage rule reads "a mark made by this attacker" — other classes' `marked` and the passives that read it stay untouched — the reviewer's first option; it keeps Vex rogue-only as R25 required | one more condition id. |
| R29 | the Sneak Attack cap covers every once-per-turn damage rider a rogue gets from advantage/hiding (Sneak Attack plus subclass passives such as Death from the Shadows); retune the subclass rider so the total stays ≤ cap, extend the cap test to every subclass, and correct rogue.md/README — the cap exists to bound the hide loop's payout | Shadow rogues a little weaker at L9–10 (already a T11 item under R27). |
| R30 | leave the stale `rogue.steady_aim` icon manifest entry and art-tool references — the assets are main's workstream (#103/#183) and the entry is harmless; mention in the final report | one unused icon in the manifest. |
| R31 | accept Death from the Shadows as a non-damage capstone (frightens the target) so the Shadow rogue stays inside R29's cap — a shared once-per-turn rider budget is new engine machinery this task doesn't need | the owner may want a damage capstone back via a per-subclass Sneak Attack within 7d6. |
| R32 | Striker bands use the sum of each member-turn’s mean HP damage per damaged hostile, divided by battle rounds. Repeated hits on one victim accumulate; off-turn effects belong to the source’s latest own turn. Raw HP damage remains reported independently. This prevents multiplying area-spell credit by victim count while retaining multi-hit damage against one foe | spread weapon attacks and incidental splash receive less band credit; audit both raw and normalized tables before tuning. |
| R33 | choose 6d6 Fireball/Lightning Bolt and SRD 8d8 Cone of Cold, rather than raising Cone above SRD to keep Fireball at 8d6. The required ≥1.5× ratio cannot hold with both SRD values (28 and 36). Keep the full area ladder and tooltips explicit | Fireball remains 25% below SRD per target; late spell encounters may still need retuning. |
| R34 | automatic standing spends half available Move rounded up to whole tiles and requires movement; restraint prevents standing. Uncanny Dodge rounds once over all damage blocks of its numbered hit after resistances, and its outcome preference replays only that hit on immutable state/RNG copies | odd Move costs an extra half-tile versus continuous SRD movement; preview adds computation when this preference is used. |
| R35 | retain Task 23's non-dice decisions (bounded attack schedules, rapier, monster save proficiencies, warlord encounter redesign), #108's revised level-1 Hide, and affordable level-7 Quickened. Close all five old “Decisions for the user” with README references, then audit these retained choices in the final sweep | these game departures remain instead of receiving a separate redesign. |
| R36 | specialist class AI priorities may be explicit `controller` or `guardian` profiles rather than sharing the general caster/brute weights, while existing role routing remains unchanged for other classes. This targets actual control and tank behavior rather than reducing restored spell dice | balance gains depend more on AI priorities; final companion and all-class sweeps must verify the result. |
| R37 | an optional `controlByLevel` content table may override a profile's scalar control weight, with the same unit level used by normal and companion AI; legacy scalar profiles are unchanged and the loader requires coverage from level 1. This lets a controller retain control when high-tier damage arrives without inflating its early priorities | level-specific AI tuning can overfit the benchmark; judge all levels at twenty seeds. |
| R38 | final balance samples rotate spawn seats cyclically across seeds, without consuming PCG RNG, while retaining roster ids and companion selection. Legacy runs remain fixed unless `--rotate-formation` is supplied. Fixed class-name placement was a measurable bias, especially for early melee and healing | historical fixed-seat comparisons are not direct A/B comparisons; use the new paired class/companion cohorts and publish the option. |
| R39 | damage before a member's first turn joins that first turn, and resuming a delayed turn does not create a new normalized-damage bucket. These are parts of the same own turn under R32 | some first-round reaction damage receives less normalized credit; raw damage remains unchanged for audit. |
| R40 | retain a flat d8 monk die, two main attacks, the original Ki curve, early Unarmored Movement and 2-Ki Stunning Strike, with unrestricted 1-Ki Heightened Flurry. Keep flat d12 Eldritch Blast beams and flat SRD-sized bard heals. These are documented balance departures with no level-up dice loss | the monk's late die and stun cost are below SRD power, and warlock beams above it; final all-level bands and the scripted rogue cap are mandatory checks. |
| R41 | scale only the finale's authored Maren HP to 105/180/240 for sizes 4/5/6 and use one cult leader at size 4 (two otherwise), retaining the ritual, dragon and phase triggers. This extends large-party fights and reduces small-party opening pressure | party size changes the boss budget more explicitly; final intended-level and companion comparisons must show the remaining exceptions honestly. |

## Appendix: complete final 20-seed benchmark report

### Tactics balance report

Generated by `Tactics.Sim` (spec §5.6): both sides AI-controlled, every battle deterministic from its seed.

## 1. Summary

- **Battles:** 18000 (5 encounter(s), levels 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 18 parties, 20 seed(s) each).
- **Won:** 14168 (78.7%). Lost: 3832, of which 72 stopped at the round cap of 30. Fled: 0.
- **Rounds:** mean 8.5, median 8.0; 43.4% of battles within the 8–12 round target.
- **Flags:**
  - win rate outside 60%–95%: 0 of 50 encounter/level cells;
  - mean battle length outside 8–12 rounds: 19 cells;
  - role-band values more than ±15% from the band mean: 15 of 120;
  - abilities used in < 2% of legal turns: 46;
  - battles stopped at the round cap: 72.

## 2. Win rate by encounter and level

Flags: **LOW** below 60%, **HIGH** above 95% (difficulty Normal; judged at the encounter's intended level when set).

| Encounter | Level | Battles | Won | Win rate | Flag |
|---|---:|---:|---:|---:|---|
| crypt_hall | 1 | 360 | 0 | 0.0% |  |
| crypt_hall | 2 | 360 | 12 | 3.3% |  |
| crypt_hall | 3 | 360 | 97 | 26.9% |  |
| crypt_hall | 4 | 360 | 157 | 43.6% |  |
| crypt_hall | 5 | 360 | 314 | 87.2% |  |
| crypt_hall | 6 | 360 | 329 | 91.4% |  |
| crypt_hall | 7 | 360 | 355 | 98.6% |  |
| crypt_hall | 8 | 360 | 351 | 97.5% |  |
| crypt_hall | 9 | 360 | 360 | 100.0% |  |
| crypt_hall | 10 | 360 | 359 | 99.7% |  |
| crystal_cave | 1 | 360 | 0 | 0.0% |  |
| crystal_cave | 2 | 360 | 10 | 2.8% |  |
| crystal_cave | 3 | 360 | 53 | 14.7% |  |
| crystal_cave | 4 | 360 | 104 | 28.9% |  |
| crystal_cave | 5 | 360 | 260 | 72.2% |  |
| crystal_cave | 6 | 360 | 293 | 81.4% |  |
| crystal_cave | 7 | 360 | 313 | 86.9% |  |
| crystal_cave | 8 | 360 | 325 | 90.3% |  |
| crystal_cave | 9 | 360 | 358 | 99.4% |  |
| crystal_cave | 10 | 360 | 341 | 94.7% |  |
| forest_ruins | 1 | 360 | 100 | 27.8% |  |
| forest_ruins | 2 | 360 | 220 | 61.1% |  |
| forest_ruins | 3 | 360 | 281 | 78.1% |  |
| forest_ruins | 4 | 360 | 315 | 87.5% |  |
| forest_ruins | 5 | 360 | 359 | 99.7% |  |
| forest_ruins | 6 | 360 | 360 | 100.0% |  |
| forest_ruins | 7 | 360 | 360 | 100.0% |  |
| forest_ruins | 8 | 360 | 360 | 100.0% |  |
| forest_ruins | 9 | 360 | 360 | 100.0% |  |
| forest_ruins | 10 | 360 | 360 | 100.0% |  |
| river_crossing | 1 | 360 | 319 | 88.6% |  |
| river_crossing | 2 | 360 | 346 | 96.1% |  |
| river_crossing | 3 | 360 | 359 | 99.7% |  |
| river_crossing | 4 | 360 | 360 | 100.0% |  |
| river_crossing | 5 | 360 | 360 | 100.0% |  |
| river_crossing | 6 | 360 | 360 | 100.0% |  |
| river_crossing | 7 | 360 | 360 | 100.0% |  |
| river_crossing | 8 | 360 | 360 | 100.0% |  |
| river_crossing | 9 | 360 | 360 | 100.0% |  |
| river_crossing | 10 | 360 | 360 | 100.0% |  |
| warlord_bridge | 1 | 360 | 236 | 65.6% |  |
| warlord_bridge | 2 | 360 | 275 | 76.4% |  |
| warlord_bridge | 3 | 360 | 279 | 77.5% |  |
| warlord_bridge | 4 | 360 | 281 | 78.1% |  |
| warlord_bridge | 5 | 360 | 328 | 91.1% |  |
| warlord_bridge | 6 | 360 | 344 | 95.6% |  |
| warlord_bridge | 7 | 360 | 348 | 96.7% |  |
| warlord_bridge | 8 | 360 | 351 | 97.5% |  |
| warlord_bridge | 9 | 360 | 358 | 99.4% |  |
| warlord_bridge | 10 | 360 | 358 | 99.4% |  |

By party size:

| Encounter | Level | Size | Battles | Win rate |
|---|---:|---:|---:|---:|
| crypt_hall | 1 | 4 | 120 | 0.0% |
| crypt_hall | 1 | 5 | 120 | 0.0% |
| crypt_hall | 1 | 6 | 120 | 0.0% |
| crypt_hall | 2 | 4 | 120 | 0.0% |
| crypt_hall | 2 | 5 | 120 | 2.5% |
| crypt_hall | 2 | 6 | 120 | 7.5% |
| crypt_hall | 3 | 4 | 120 | 1.7% |
| crypt_hall | 3 | 5 | 120 | 25.0% |
| crypt_hall | 3 | 6 | 120 | 54.2% |
| crypt_hall | 4 | 4 | 120 | 4.2% |
| crypt_hall | 4 | 5 | 120 | 53.3% |
| crypt_hall | 4 | 6 | 120 | 73.3% |
| crypt_hall | 5 | 4 | 120 | 61.7% |
| crypt_hall | 5 | 5 | 120 | 100.0% |
| crypt_hall | 5 | 6 | 120 | 100.0% |
| crypt_hall | 6 | 4 | 120 | 75.0% |
| crypt_hall | 6 | 5 | 120 | 99.2% |
| crypt_hall | 6 | 6 | 120 | 100.0% |
| crypt_hall | 7 | 4 | 120 | 95.8% |
| crypt_hall | 7 | 5 | 120 | 100.0% |
| crypt_hall | 7 | 6 | 120 | 100.0% |
| crypt_hall | 8 | 4 | 120 | 92.5% |
| crypt_hall | 8 | 5 | 120 | 100.0% |
| crypt_hall | 8 | 6 | 120 | 100.0% |
| crypt_hall | 9 | 4 | 120 | 100.0% |
| crypt_hall | 9 | 5 | 120 | 100.0% |
| crypt_hall | 9 | 6 | 120 | 100.0% |
| crypt_hall | 10 | 4 | 120 | 99.2% |
| crypt_hall | 10 | 5 | 120 | 100.0% |
| crypt_hall | 10 | 6 | 120 | 100.0% |
| crystal_cave | 1 | 4 | 120 | 0.0% |
| crystal_cave | 1 | 5 | 120 | 0.0% |
| crystal_cave | 1 | 6 | 120 | 0.0% |
| crystal_cave | 2 | 4 | 120 | 0.0% |
| crystal_cave | 2 | 5 | 120 | 2.5% |
| crystal_cave | 2 | 6 | 120 | 5.8% |
| crystal_cave | 3 | 4 | 120 | 2.5% |
| crystal_cave | 3 | 5 | 120 | 14.2% |
| crystal_cave | 3 | 6 | 120 | 27.5% |
| crystal_cave | 4 | 4 | 120 | 4.2% |
| crystal_cave | 4 | 5 | 120 | 35.0% |
| crystal_cave | 4 | 6 | 120 | 47.5% |
| crystal_cave | 5 | 4 | 120 | 30.8% |
| crystal_cave | 5 | 5 | 120 | 90.0% |
| crystal_cave | 5 | 6 | 120 | 95.8% |
| crystal_cave | 6 | 4 | 120 | 47.5% |
| crystal_cave | 6 | 5 | 120 | 96.7% |
| crystal_cave | 6 | 6 | 120 | 100.0% |
| crystal_cave | 7 | 4 | 120 | 62.5% |
| crystal_cave | 7 | 5 | 120 | 98.3% |
| crystal_cave | 7 | 6 | 120 | 100.0% |
| crystal_cave | 8 | 4 | 120 | 70.8% |
| crystal_cave | 8 | 5 | 120 | 100.0% |
| crystal_cave | 8 | 6 | 120 | 100.0% |
| crystal_cave | 9 | 4 | 120 | 98.3% |
| crystal_cave | 9 | 5 | 120 | 100.0% |
| crystal_cave | 9 | 6 | 120 | 100.0% |
| crystal_cave | 10 | 4 | 120 | 84.2% |
| crystal_cave | 10 | 5 | 120 | 100.0% |
| crystal_cave | 10 | 6 | 120 | 100.0% |
| forest_ruins | 1 | 4 | 120 | 10.0% |
| forest_ruins | 1 | 5 | 120 | 18.3% |
| forest_ruins | 1 | 6 | 120 | 55.0% |
| forest_ruins | 2 | 4 | 120 | 24.2% |
| forest_ruins | 2 | 5 | 120 | 65.8% |
| forest_ruins | 2 | 6 | 120 | 93.3% |
| forest_ruins | 3 | 4 | 120 | 38.3% |
| forest_ruins | 3 | 5 | 120 | 95.8% |
| forest_ruins | 3 | 6 | 120 | 100.0% |
| forest_ruins | 4 | 4 | 120 | 64.2% |
| forest_ruins | 4 | 5 | 120 | 98.3% |
| forest_ruins | 4 | 6 | 120 | 100.0% |
| forest_ruins | 5 | 4 | 120 | 99.2% |
| forest_ruins | 5 | 5 | 120 | 100.0% |
| forest_ruins | 5 | 6 | 120 | 100.0% |
| forest_ruins | 6 | 4 | 120 | 100.0% |
| forest_ruins | 6 | 5 | 120 | 100.0% |
| forest_ruins | 6 | 6 | 120 | 100.0% |
| forest_ruins | 7 | 4 | 120 | 100.0% |
| forest_ruins | 7 | 5 | 120 | 100.0% |
| forest_ruins | 7 | 6 | 120 | 100.0% |
| forest_ruins | 8 | 4 | 120 | 100.0% |
| forest_ruins | 8 | 5 | 120 | 100.0% |
| forest_ruins | 8 | 6 | 120 | 100.0% |
| forest_ruins | 9 | 4 | 120 | 100.0% |
| forest_ruins | 9 | 5 | 120 | 100.0% |
| forest_ruins | 9 | 6 | 120 | 100.0% |
| forest_ruins | 10 | 4 | 120 | 100.0% |
| forest_ruins | 10 | 5 | 120 | 100.0% |
| forest_ruins | 10 | 6 | 120 | 100.0% |
| river_crossing | 1 | 4 | 120 | 72.5% |
| river_crossing | 1 | 5 | 120 | 95.0% |
| river_crossing | 1 | 6 | 120 | 98.3% |
| river_crossing | 2 | 4 | 120 | 88.3% |
| river_crossing | 2 | 5 | 120 | 100.0% |
| river_crossing | 2 | 6 | 120 | 100.0% |
| river_crossing | 3 | 4 | 120 | 99.2% |
| river_crossing | 3 | 5 | 120 | 100.0% |
| river_crossing | 3 | 6 | 120 | 100.0% |
| river_crossing | 4 | 4 | 120 | 100.0% |
| river_crossing | 4 | 5 | 120 | 100.0% |
| river_crossing | 4 | 6 | 120 | 100.0% |
| river_crossing | 5 | 4 | 120 | 100.0% |
| river_crossing | 5 | 5 | 120 | 100.0% |
| river_crossing | 5 | 6 | 120 | 100.0% |
| river_crossing | 6 | 4 | 120 | 100.0% |
| river_crossing | 6 | 5 | 120 | 100.0% |
| river_crossing | 6 | 6 | 120 | 100.0% |
| river_crossing | 7 | 4 | 120 | 100.0% |
| river_crossing | 7 | 5 | 120 | 100.0% |
| river_crossing | 7 | 6 | 120 | 100.0% |
| river_crossing | 8 | 4 | 120 | 100.0% |
| river_crossing | 8 | 5 | 120 | 100.0% |
| river_crossing | 8 | 6 | 120 | 100.0% |
| river_crossing | 9 | 4 | 120 | 100.0% |
| river_crossing | 9 | 5 | 120 | 100.0% |
| river_crossing | 9 | 6 | 120 | 100.0% |
| river_crossing | 10 | 4 | 120 | 100.0% |
| river_crossing | 10 | 5 | 120 | 100.0% |
| river_crossing | 10 | 6 | 120 | 100.0% |
| warlord_bridge | 1 | 4 | 120 | 51.7% |
| warlord_bridge | 1 | 5 | 120 | 65.8% |
| warlord_bridge | 1 | 6 | 120 | 79.2% |
| warlord_bridge | 2 | 4 | 120 | 55.8% |
| warlord_bridge | 2 | 5 | 120 | 74.2% |
| warlord_bridge | 2 | 6 | 120 | 99.2% |
| warlord_bridge | 3 | 4 | 120 | 56.7% |
| warlord_bridge | 3 | 5 | 120 | 75.8% |
| warlord_bridge | 3 | 6 | 120 | 100.0% |
| warlord_bridge | 4 | 4 | 120 | 59.2% |
| warlord_bridge | 4 | 5 | 120 | 75.0% |
| warlord_bridge | 4 | 6 | 120 | 100.0% |
| warlord_bridge | 5 | 4 | 120 | 76.7% |
| warlord_bridge | 5 | 5 | 120 | 96.7% |
| warlord_bridge | 5 | 6 | 120 | 100.0% |
| warlord_bridge | 6 | 4 | 120 | 88.3% |
| warlord_bridge | 6 | 5 | 120 | 98.3% |
| warlord_bridge | 6 | 6 | 120 | 100.0% |
| warlord_bridge | 7 | 4 | 120 | 90.0% |
| warlord_bridge | 7 | 5 | 120 | 100.0% |
| warlord_bridge | 7 | 6 | 120 | 100.0% |
| warlord_bridge | 8 | 4 | 120 | 92.5% |
| warlord_bridge | 8 | 5 | 120 | 100.0% |
| warlord_bridge | 8 | 6 | 120 | 100.0% |
| warlord_bridge | 9 | 4 | 120 | 98.3% |
| warlord_bridge | 9 | 5 | 120 | 100.0% |
| warlord_bridge | 9 | 6 | 120 | 100.0% |
| warlord_bridge | 10 | 4 | 120 | 98.3% |
| warlord_bridge | 10 | 5 | 120 | 100.0% |
| warlord_bridge | 10 | 6 | 120 | 100.0% |

## 3. Rounds per battle

Target: 8–12 rounds. Flags: **SHORT** / **LONG** when the mean is outside it, **CAPPED** when any battle hit the round cap (30, counted as lost).

| Encounter | Level | Mean | Median | Min | Max | In target | Capped | Flag |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| crypt_hall | 1 | 6.1 | 5.0 | 3 | 30 | 15.6% | 1 | **CAPPED** |
| crypt_hall | 2 | 8.6 | 7.0 | 3 | 30 | 35.8% | 6 | **CAPPED** |
| crypt_hall | 3 | 11.8 | 10.0 | 4 | 30 | 33.9% | 15 | **CAPPED** |
| crypt_hall | 4 | 12.2 | 11.0 | 5 | 30 | 50.6% | 10 | **CAPPED** |
| crypt_hall | 5 | 9.9 | 9.0 | 4 | 30 | 59.7% | 2 | **CAPPED** |
| crypt_hall | 6 | 9.9 | 9.5 | 5 | 30 | 60.0% | 3 | **CAPPED** |
| crypt_hall | 7 | 9.0 | 8.0 | 4 | 26 | 52.2% | 0 |  |
| crypt_hall | 8 | 9.1 | 8.0 | 4 | 30 | 43.9% | 4 | **CAPPED** |
| crypt_hall | 9 | 6.8 | 6.0 | 2 | 26 | 28.1% | 0 |  |
| crypt_hall | 10 | 7.2 | 7.0 | 3 | 23 | 36.9% | 0 |  |
| crystal_cave | 1 | 5.4 | 5.0 | 2 | 14 | 12.8% | 0 |  |
| crystal_cave | 2 | 7.2 | 6.0 | 3 | 30 | 27.5% | 1 | **CAPPED** |
| crystal_cave | 3 | 10.1 | 9.0 | 3 | 30 | 35.8% | 1 | **CAPPED** |
| crystal_cave | 4 | 11.8 | 11.0 | 4 | 30 | 38.1% | 2 | **CAPPED** |
| crystal_cave | 5 | 11.1 | 11.0 | 4 | 24 | 53.9% | 0 |  |
| crystal_cave | 6 | 11.1 | 11.0 | 5 | 25 | 55.8% | 0 |  |
| crystal_cave | 7 | 10.5 | 10.0 | 4 | 30 | 52.5% | 0 |  |
| crystal_cave | 8 | 10.2 | 9.0 | 4 | 30 | 49.7% | 1 | **CAPPED** |
| crystal_cave | 9 | 6.1 | 5.0 | 2 | 30 | 18.3% | 1 | **CAPPED** |
| crystal_cave | 10 | 7.7 | 7.0 | 2 | 30 | 34.2% | 3 | **CAPPED** |
| forest_ruins | 1 | 9.2 | 8.0 | 3 | 30 | 40.3% | 1 | **CAPPED** |
| forest_ruins | 2 | 10.7 | 10.0 | 4 | 30 | 53.6% | 5 | **CAPPED** |
| forest_ruins | 3 | 9.7 | 9.0 | 5 | 30 | 48.1% | 2 | **CAPPED** |
| forest_ruins | 4 | 9.9 | 9.0 | 4 | 30 | 48.3% | 1 | **CAPPED** |
| forest_ruins | 5 | 7.0 | 7.0 | 2 | 17 | 34.2% | 0 |  |
| forest_ruins | 6 | 6.7 | 7.0 | 2 | 18 | 28.6% | 0 |  |
| forest_ruins | 7 | 6.3 | 6.0 | 2 | 14 | 17.2% | 0 |  |
| forest_ruins | 8 | 6.3 | 6.0 | 3 | 14 | 19.4% | 0 |  |
| forest_ruins | 9 | 4.7 | 4.0 | 1 | 10 | 7.2% | 0 |  |
| forest_ruins | 10 | 4.9 | 5.0 | 2 | 9 | 8.1% | 0 |  |
| river_crossing | 1 | 11.0 | 10.0 | 5 | 30 | 62.2% | 8 | **CAPPED** |
| river_crossing | 2 | 8.5 | 8.0 | 4 | 30 | 44.2% | 5 | **CAPPED** |
| river_crossing | 3 | 6.8 | 6.0 | 3 | 15 | 27.2% | 0 |  |
| river_crossing | 4 | 6.3 | 6.0 | 3 | 13 | 24.2% | 0 |  |
| river_crossing | 5 | 5.0 | 5.0 | 2 | 16 | 7.2% | 0 |  |
| river_crossing | 6 | 4.8 | 5.0 | 2 | 11 | 5.6% | 0 |  |
| river_crossing | 7 | 4.6 | 4.0 | 2 | 11 | 3.3% | 0 |  |
| river_crossing | 8 | 4.8 | 5.0 | 3 | 14 | 5.6% | 0 |  |
| river_crossing | 9 | 4.3 | 4.0 | 1 | 10 | 3.3% | 0 |  |
| river_crossing | 10 | 4.1 | 4.0 | 1 | 9 | 0.6% | 0 |  |
| warlord_bridge | 1 | 10.3 | 12.0 | 3 | 12 | 76.7% | 0 |  |
| warlord_bridge | 2 | 10.9 | 12.0 | 3 | 12 | 87.2% | 0 |  |
| warlord_bridge | 3 | 11.1 | 12.0 | 4 | 12 | 90.6% | 0 |  |
| warlord_bridge | 4 | 11.2 | 12.0 | 4 | 12 | 93.9% | 0 |  |
| warlord_bridge | 5 | 11.1 | 12.0 | 6 | 12 | 94.2% | 0 |  |
| warlord_bridge | 6 | 11.2 | 12.0 | 5 | 12 | 96.1% | 0 |  |
| warlord_bridge | 7 | 10.9 | 12.0 | 5 | 12 | 93.1% | 0 |  |
| warlord_bridge | 8 | 10.5 | 11.0 | 5 | 12 | 91.9% | 0 |  |
| warlord_bridge | 9 | 9.7 | 10.0 | 5 | 12 | 81.1% | 0 |  |
| warlord_bridge | 10 | 9.7 | 10.0 | 4 | 12 | 80.0% | 0 |  |

## 4. Role bands

Each class's band measure at each level (the mean over the battles it fought, per member), against the mean of its band's classes at that level; **FLAG** beyond ±15%. Measures: Defender, damage mitigated per round (HP and temporary HP it lost, plus misses against it × the mean hostile hit); Striker, target-normalized damage per round (mean HP damage per victim within each member turn, summed over turns; off-turn effects belong to the owner’s latest turn; no overkill or temporary HP); Healer, healing and revives per round; Control, enemy turns denied per battle. A band with one class present sits on its own mean.

| Level | Band | Measure | Class | Value | Band mean | Deviation | Flag |
|---:|---|---|---|---:|---:|---:|---|
| 1 | Defender | mitigated / round | barbarian | 5.2 | 4.6 | +13.1% |  |
| 1 | Defender | mitigated / round | fighter | 4.9 | 4.6 | +6.0% |  |
| 1 | Defender | mitigated / round | paladin | 3.7 | 4.6 | -19.1% | **FLAG** |
| 1 | Striker | damage / target-turn | monk | 2.8 | 3.1 | -11.3% |  |
| 1 | Striker | damage / target-turn | ranger | 3.5 | 3.1 | +10.0% |  |
| 1 | Striker | damage / target-turn | rogue | 3.3 | 3.1 | +3.4% |  |
| 1 | Striker | damage / target-turn | sorcerer | 2.8 | 3.1 | -12.0% |  |
| 1 | Striker | damage / target-turn | warlock | 3.5 | 3.1 | +9.9% |  |
| 1 | Healer | healing / round | bard | 2.3 | 2.1 | +9.1% |  |
| 1 | Healer | healing / round | cleric | 1.9 | 2.1 | -9.1% |  |
| 1 | Control | turns denied / battle | druid | 3.9 | 3.4 | +13.3% |  |
| 1 | Control | turns denied / battle | wizard | 3.0 | 3.4 | -13.3% |  |
| 2 | Defender | mitigated / round | barbarian | 6.1 | 5.7 | +6.4% |  |
| 2 | Defender | mitigated / round | fighter | 6.4 | 5.7 | +11.7% |  |
| 2 | Defender | mitigated / round | paladin | 4.7 | 5.7 | -18.1% | **FLAG** |
| 2 | Striker | damage / target-turn | monk | 3.2 | 3.7 | -14.7% |  |
| 2 | Striker | damage / target-turn | ranger | 4.0 | 3.7 | +7.3% |  |
| 2 | Striker | damage / target-turn | rogue | 4.0 | 3.7 | +7.6% |  |
| 2 | Striker | damage / target-turn | sorcerer | 3.3 | 3.7 | -10.3% |  |
| 2 | Striker | damage / target-turn | warlock | 4.1 | 3.7 | +10.0% |  |
| 2 | Healer | healing / round | bard | 2.5 | 2.5 | -2.5% |  |
| 2 | Healer | healing / round | cleric | 2.6 | 2.5 | +2.5% |  |
| 2 | Control | turns denied / battle | druid | 4.2 | 3.7 | +14.0% |  |
| 2 | Control | turns denied / battle | wizard | 3.2 | 3.7 | -14.0% |  |
| 3 | Defender | mitigated / round | barbarian | 6.4 | 6.0 | +6.7% |  |
| 3 | Defender | mitigated / round | fighter | 6.5 | 6.0 | +8.0% |  |
| 3 | Defender | mitigated / round | paladin | 5.1 | 6.0 | -14.6% |  |
| 3 | Striker | damage / target-turn | monk | 4.3 | 4.2 | +2.9% |  |
| 3 | Striker | damage / target-turn | ranger | 3.9 | 4.2 | -5.5% |  |
| 3 | Striker | damage / target-turn | rogue | 4.6 | 4.2 | +10.9% |  |
| 3 | Striker | damage / target-turn | sorcerer | 4.1 | 4.2 | -2.0% |  |
| 3 | Striker | damage / target-turn | warlock | 3.9 | 4.2 | -6.3% |  |
| 3 | Healer | healing / round | bard | 1.7 | 1.8 | -4.9% |  |
| 3 | Healer | healing / round | cleric | 1.9 | 1.8 | +4.9% |  |
| 3 | Control | turns denied / battle | druid | 4.1 | 3.8 | +7.7% |  |
| 3 | Control | turns denied / battle | wizard | 3.5 | 3.8 | -7.7% |  |
| 4 | Defender | mitigated / round | barbarian | 6.1 | 6.5 | -6.8% |  |
| 4 | Defender | mitigated / round | fighter | 7.3 | 6.5 | +12.4% |  |
| 4 | Defender | mitigated / round | paladin | 6.2 | 6.5 | -5.6% |  |
| 4 | Striker | damage / target-turn | monk | 4.7 | 4.4 | +6.4% |  |
| 4 | Striker | damage / target-turn | ranger | 4.0 | 4.4 | -9.0% |  |
| 4 | Striker | damage / target-turn | rogue | 5.2 | 4.4 | +18.1% | **FLAG** |
| 4 | Striker | damage / target-turn | sorcerer | 4.3 | 4.4 | -1.9% |  |
| 4 | Striker | damage / target-turn | warlock | 3.8 | 4.4 | -13.6% |  |
| 4 | Healer | healing / round | bard | 2.1 | 2.3 | -5.6% |  |
| 4 | Healer | healing / round | cleric | 2.4 | 2.3 | +5.6% |  |
| 4 | Control | turns denied / battle | druid | 4.3 | 4.1 | +7.0% |  |
| 4 | Control | turns denied / battle | wizard | 3.8 | 4.1 | -7.0% |  |
| 5 | Defender | mitigated / round | barbarian | 5.1 | 5.7 | -11.5% |  |
| 5 | Defender | mitigated / round | fighter | 6.7 | 5.7 | +17.0% | **FLAG** |
| 5 | Defender | mitigated / round | paladin | 5.4 | 5.7 | -5.5% |  |
| 5 | Striker | damage / target-turn | monk | 5.7 | 5.9 | -3.9% |  |
| 5 | Striker | damage / target-turn | ranger | 6.7 | 5.9 | +12.1% |  |
| 5 | Striker | damage / target-turn | rogue | 6.1 | 5.9 | +2.9% |  |
| 5 | Striker | damage / target-turn | sorcerer | 5.8 | 5.9 | -2.3% |  |
| 5 | Striker | damage / target-turn | warlock | 5.4 | 5.9 | -8.8% |  |
| 5 | Healer | healing / round | bard | 2.0 | 2.2 | -7.2% |  |
| 5 | Healer | healing / round | cleric | 2.4 | 2.2 | +7.2% |  |
| 5 | Control | turns denied / battle | druid | 3.2 | 2.9 | +11.3% |  |
| 5 | Control | turns denied / battle | wizard | 2.6 | 2.9 | -11.3% |  |
| 6 | Defender | mitigated / round | barbarian | 5.5 | 5.9 | -6.4% |  |
| 6 | Defender | mitigated / round | fighter | 6.9 | 5.9 | +17.5% | **FLAG** |
| 6 | Defender | mitigated / round | paladin | 5.2 | 5.9 | -11.0% |  |
| 6 | Striker | damage / target-turn | monk | 6.3 | 6.3 | +0.3% |  |
| 6 | Striker | damage / target-turn | ranger | 7.1 | 6.3 | +13.4% |  |
| 6 | Striker | damage / target-turn | rogue | 6.8 | 6.3 | +8.5% |  |
| 6 | Striker | damage / target-turn | sorcerer | 5.8 | 6.3 | -8.0% |  |
| 6 | Striker | damage / target-turn | warlock | 5.4 | 6.3 | -14.2% |  |
| 6 | Healer | healing / round | bard | 2.5 | 2.4 | +2.7% |  |
| 6 | Healer | healing / round | cleric | 2.4 | 2.4 | -2.7% |  |
| 6 | Control | turns denied / battle | druid | 2.6 | 2.6 | +0.2% |  |
| 6 | Control | turns denied / battle | wizard | 2.6 | 2.6 | -0.2% |  |
| 7 | Defender | mitigated / round | barbarian | 5.3 | 5.7 | -6.3% |  |
| 7 | Defender | mitigated / round | fighter | 6.7 | 5.7 | +17.6% | **FLAG** |
| 7 | Defender | mitigated / round | paladin | 5.0 | 5.7 | -11.3% |  |
| 7 | Striker | damage / target-turn | monk | 7.0 | 6.9 | +1.3% |  |
| 7 | Striker | damage / target-turn | ranger | 7.3 | 6.9 | +5.9% |  |
| 7 | Striker | damage / target-turn | rogue | 7.0 | 6.9 | +1.2% |  |
| 7 | Striker | damage / target-turn | sorcerer | 6.7 | 6.9 | -3.1% |  |
| 7 | Striker | damage / target-turn | warlock | 6.5 | 6.9 | -5.2% |  |
| 7 | Healer | healing / round | bard | 2.8 | 2.6 | +7.8% |  |
| 7 | Healer | healing / round | cleric | 2.4 | 2.6 | -7.8% |  |
| 7 | Control | turns denied / battle | druid | 2.1 | 2.1 | -1.5% |  |
| 7 | Control | turns denied / battle | wizard | 2.2 | 2.1 | +1.5% |  |
| 8 | Defender | mitigated / round | barbarian | 5.7 | 6.1 | -6.7% |  |
| 8 | Defender | mitigated / round | fighter | 7.1 | 6.1 | +17.2% | **FLAG** |
| 8 | Defender | mitigated / round | paladin | 5.4 | 6.1 | -10.6% |  |
| 8 | Striker | damage / target-turn | monk | 7.1 | 7.4 | -4.5% |  |
| 8 | Striker | damage / target-turn | ranger | 7.6 | 7.4 | +1.7% |  |
| 8 | Striker | damage / target-turn | rogue | 7.5 | 7.4 | +0.5% |  |
| 8 | Striker | damage / target-turn | sorcerer | 6.8 | 7.4 | -9.0% |  |
| 8 | Striker | damage / target-turn | warlock | 8.3 | 7.4 | +11.5% |  |
| 8 | Healer | healing / round | bard | 2.8 | 2.6 | +6.0% |  |
| 8 | Healer | healing / round | cleric | 2.5 | 2.6 | -6.0% |  |
| 8 | Control | turns denied / battle | druid | 2.7 | 2.4 | +12.6% |  |
| 8 | Control | turns denied / battle | wizard | 2.1 | 2.4 | -12.6% |  |
| 9 | Defender | mitigated / round | barbarian | 4.5 | 4.2 | +5.7% |  |
| 9 | Defender | mitigated / round | fighter | 4.9 | 4.2 | +15.5% | **FLAG** |
| 9 | Defender | mitigated / round | paladin | 3.4 | 4.2 | -21.2% | **FLAG** |
| 9 | Striker | damage / target-turn | monk | 8.9 | 8.4 | +5.6% |  |
| 9 | Striker | damage / target-turn | ranger | 8.7 | 8.4 | +3.4% |  |
| 9 | Striker | damage / target-turn | rogue | 8.0 | 8.4 | -4.6% |  |
| 9 | Striker | damage / target-turn | sorcerer | 8.6 | 8.4 | +3.1% |  |
| 9 | Striker | damage / target-turn | warlock | 7.8 | 8.4 | -7.4% |  |
| 9 | Healer | healing / round | bard | 3.2 | 2.9 | +10.6% |  |
| 9 | Healer | healing / round | cleric | 2.6 | 2.9 | -10.6% |  |
| 9 | Control | turns denied / battle | druid | 1.9 | 1.5 | +30.6% | **FLAG** |
| 9 | Control | turns denied / battle | wizard | 1.0 | 1.5 | -30.6% | **FLAG** |
| 10 | Defender | mitigated / round | barbarian | 3.9 | 4.2 | -7.1% |  |
| 10 | Defender | mitigated / round | fighter | 5.9 | 4.2 | +38.3% | **FLAG** |
| 10 | Defender | mitigated / round | paladin | 2.9 | 4.2 | -31.2% | **FLAG** |
| 10 | Striker | damage / target-turn | monk | 9.2 | 8.1 | +13.2% |  |
| 10 | Striker | damage / target-turn | ranger | 7.5 | 8.1 | -7.2% |  |
| 10 | Striker | damage / target-turn | rogue | 7.6 | 8.1 | -6.4% |  |
| 10 | Striker | damage / target-turn | sorcerer | 8.5 | 8.1 | +4.9% |  |
| 10 | Striker | damage / target-turn | warlock | 7.7 | 8.1 | -4.5% |  |
| 10 | Healer | healing / round | bard | 3.3 | 3.2 | +3.1% |  |
| 10 | Healer | healing / round | cleric | 3.1 | 3.2 | -3.1% |  |
| 10 | Control | turns denied / battle | druid | 2.2 | 1.9 | +16.3% | **FLAG** |
| 10 | Control | turns denied / battle | wizard | 1.6 | 1.9 | -16.3% | **FLAG** |

Every measure, by class and level:

| Class | Band | Level | Battles | Damage / round | Target damage / round | Healing / round | Mitigated / round | Turns denied |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| barbarian | Defender | 1 | 300 | 2.0 | 1.7 | 0.1 | 5.2 | 0.0 |
| barbarian | Defender | 2 | 300 | 2.4 | 2.2 | 0.1 | 6.1 | 0.0 |
| barbarian | Defender | 3 | 300 | 2.8 | 2.5 | 0.0 | 6.4 | 0.0 |
| barbarian | Defender | 4 | 300 | 3.1 | 2.9 | 0.0 | 6.1 | 0.0 |
| barbarian | Defender | 5 | 300 | 4.7 | 4.2 | 0.0 | 5.1 | 0.0 |
| barbarian | Defender | 6 | 300 | 5.1 | 4.4 | 0.0 | 5.5 | 0.0 |
| barbarian | Defender | 7 | 300 | 5.8 | 4.9 | 0.0 | 5.3 | 0.0 |
| barbarian | Defender | 8 | 300 | 6.0 | 5.0 | 0.0 | 5.7 | 0.0 |
| barbarian | Defender | 9 | 300 | 6.5 | 5.7 | 0.2 | 4.5 | 0.0 |
| barbarian | Defender | 10 | 300 | 7.3 | 6.4 | 0.2 | 3.9 | 0.0 |
| bard | Healer | 1 | 600 | 1.3 | 1.2 | 2.3 | 1.6 | 0.0 |
| bard | Healer | 2 | 600 | 0.9 | 0.8 | 2.5 | 1.7 | 0.0 |
| bard | Healer | 3 | 600 | 5.1 | 2.1 | 1.7 | 1.5 | 0.0 |
| bard | Healer | 4 | 600 | 4.8 | 2.0 | 2.1 | 1.4 | 0.0 |
| bard | Healer | 5 | 600 | 4.4 | 1.9 | 2.0 | 0.7 | 0.1 |
| bard | Healer | 6 | 600 | 4.5 | 2.0 | 2.5 | 0.7 | 0.1 |
| bard | Healer | 7 | 600 | 4.2 | 1.9 | 2.8 | 0.7 | 0.1 |
| bard | Healer | 8 | 600 | 4.4 | 1.9 | 2.8 | 0.7 | 0.1 |
| bard | Healer | 9 | 600 | 3.4 | 1.5 | 3.2 | 0.5 | 0.1 |
| bard | Healer | 10 | 600 | 3.4 | 1.6 | 3.3 | 0.6 | 0.1 |
| cleric | Healer | 1 | 500 | 2.8 | 2.8 | 1.9 | 2.3 | 0.0 |
| cleric | Healer | 2 | 500 | 2.4 | 2.4 | 2.6 | 2.6 | 0.0 |
| cleric | Healer | 3 | 500 | 2.5 | 2.4 | 1.9 | 2.5 | 0.0 |
| cleric | Healer | 4 | 500 | 2.4 | 2.3 | 2.4 | 2.7 | 0.2 |
| cleric | Healer | 5 | 500 | 3.2 | 3.1 | 2.4 | 1.3 | 0.1 |
| cleric | Healer | 6 | 500 | 2.8 | 2.7 | 2.4 | 1.2 | 0.1 |
| cleric | Healer | 7 | 500 | 2.3 | 2.3 | 2.4 | 1.1 | 0.1 |
| cleric | Healer | 8 | 500 | 2.5 | 2.4 | 2.5 | 1.2 | 0.2 |
| cleric | Healer | 9 | 500 | 3.9 | 3.3 | 2.6 | 0.7 | 0.0 |
| cleric | Healer | 10 | 500 | 4.8 | 3.6 | 3.1 | 0.5 | 0.0 |
| druid | Control | 1 | 600 | 0.5 | 0.5 | 0.1 | 2.1 | 3.9 |
| druid | Control | 2 | 600 | 0.4 | 0.4 | 0.3 | 3.8 | 4.2 |
| druid | Control | 3 | 600 | 0.4 | 0.4 | 0.4 | 3.0 | 4.1 |
| druid | Control | 4 | 600 | 1.7 | 1.1 | 0.4 | 3.5 | 4.3 |
| druid | Control | 5 | 600 | 1.0 | 0.8 | 0.6 | 1.6 | 3.2 |
| druid | Control | 6 | 600 | 1.3 | 1.1 | 0.4 | 1.8 | 2.6 |
| druid | Control | 7 | 600 | 3.6 | 2.1 | 0.3 | 1.4 | 2.1 |
| druid | Control | 8 | 600 | 3.6 | 1.9 | 0.4 | 1.1 | 2.7 |
| druid | Control | 9 | 600 | 1.6 | 1.1 | 0.4 | 0.6 | 1.9 |
| druid | Control | 10 | 600 | 1.4 | 0.9 | 0.4 | 0.6 | 2.2 |
| fighter | Defender | 1 | 700 | 2.3 | 2.2 | 0.2 | 4.9 | 0.0 |
| fighter | Defender | 2 | 700 | 3.1 | 2.9 | 0.3 | 6.4 | 0.0 |
| fighter | Defender | 3 | 700 | 3.1 | 3.0 | 0.3 | 6.5 | 0.0 |
| fighter | Defender | 4 | 700 | 3.2 | 3.1 | 0.3 | 7.3 | 0.0 |
| fighter | Defender | 5 | 700 | 4.8 | 4.6 | 0.4 | 6.7 | 0.0 |
| fighter | Defender | 6 | 700 | 4.9 | 4.5 | 0.5 | 6.9 | 0.0 |
| fighter | Defender | 7 | 700 | 5.4 | 5.0 | 0.5 | 6.7 | 0.0 |
| fighter | Defender | 8 | 700 | 5.5 | 5.0 | 0.5 | 7.1 | 0.0 |
| fighter | Defender | 9 | 700 | 5.3 | 4.7 | 0.5 | 4.9 | 0.0 |
| fighter | Defender | 10 | 700 | 5.9 | 5.3 | 0.5 | 5.9 | 0.0 |
| monk | Striker | 1 | 600 | 3.0 | 2.8 | 0.1 | 3.9 | 0.0 |
| monk | Striker | 2 | 600 | 3.4 | 3.2 | 0.0 | 4.4 | 0.0 |
| monk | Striker | 3 | 600 | 4.5 | 4.3 | 0.0 | 7.6 | 0.0 |
| monk | Striker | 4 | 600 | 4.9 | 4.7 | 0.0 | 8.4 | 0.0 |
| monk | Striker | 5 | 600 | 6.0 | 5.7 | 0.0 | 9.3 | 0.2 |
| monk | Striker | 6 | 600 | 6.8 | 6.3 | 0.0 | 9.9 | 0.3 |
| monk | Striker | 7 | 600 | 7.4 | 7.0 | 0.0 | 9.6 | 0.4 |
| monk | Striker | 8 | 600 | 7.5 | 7.1 | 0.0 | 9.0 | 0.4 |
| monk | Striker | 9 | 600 | 9.3 | 8.9 | 0.0 | 9.6 | 0.5 |
| monk | Striker | 10 | 600 | 9.8 | 9.2 | 0.0 | 8.7 | 0.6 |
| paladin | Defender | 1 | 500 | 1.0 | 1.0 | 0.5 | 3.7 | 0.0 |
| paladin | Defender | 2 | 500 | 1.7 | 1.6 | 0.6 | 4.7 | 0.0 |
| paladin | Defender | 3 | 500 | 1.8 | 1.8 | 0.7 | 5.1 | 0.0 |
| paladin | Defender | 4 | 500 | 1.8 | 1.8 | 2.0 | 6.2 | 0.0 |
| paladin | Defender | 5 | 500 | 2.9 | 2.8 | 1.7 | 5.4 | 0.0 |
| paladin | Defender | 6 | 500 | 2.8 | 2.7 | 2.0 | 5.2 | 0.0 |
| paladin | Defender | 7 | 500 | 3.0 | 2.9 | 2.1 | 5.0 | 0.0 |
| paladin | Defender | 8 | 500 | 3.0 | 2.9 | 2.1 | 5.4 | 0.0 |
| paladin | Defender | 9 | 500 | 2.4 | 2.4 | 1.6 | 3.4 | 0.0 |
| paladin | Defender | 10 | 500 | 2.2 | 2.1 | 1.7 | 2.9 | 0.0 |
| ranger | Striker | 1 | 700 | 4.0 | 3.5 | 0.0 | 2.4 | 0.0 |
| ranger | Striker | 2 | 700 | 4.6 | 4.0 | 0.0 | 3.1 | 0.0 |
| ranger | Striker | 3 | 700 | 4.8 | 3.9 | 0.0 | 3.0 | 0.0 |
| ranger | Striker | 4 | 700 | 4.9 | 4.0 | 0.0 | 3.1 | 0.0 |
| ranger | Striker | 5 | 700 | 7.0 | 6.7 | 0.1 | 2.2 | 0.0 |
| ranger | Striker | 6 | 700 | 7.4 | 7.1 | 0.2 | 1.9 | 0.0 |
| ranger | Striker | 7 | 700 | 7.6 | 7.3 | 0.1 | 1.4 | 0.0 |
| ranger | Striker | 8 | 700 | 7.9 | 7.6 | 0.1 | 1.5 | 0.0 |
| ranger | Striker | 9 | 700 | 8.9 | 8.7 | 0.1 | 0.8 | 0.0 |
| ranger | Striker | 10 | 700 | 18.2 | 7.5 | 0.0 | 0.7 | 0.0 |
| rogue | Striker | 1 | 800 | 3.4 | 3.3 | 0.0 | 3.5 | 0.0 |
| rogue | Striker | 2 | 800 | 4.2 | 4.0 | 0.0 | 5.0 | 0.0 |
| rogue | Striker | 3 | 800 | 4.9 | 4.6 | 0.0 | 5.2 | 0.0 |
| rogue | Striker | 4 | 800 | 5.5 | 5.2 | 0.0 | 6.2 | 0.0 |
| rogue | Striker | 5 | 800 | 6.6 | 6.1 | 0.0 | 6.9 | 0.0 |
| rogue | Striker | 6 | 800 | 7.3 | 6.8 | 0.0 | 7.9 | 0.0 |
| rogue | Striker | 7 | 800 | 7.5 | 7.0 | 0.0 | 8.1 | 0.0 |
| rogue | Striker | 8 | 800 | 8.0 | 7.5 | 0.0 | 8.6 | 0.0 |
| rogue | Striker | 9 | 800 | 8.5 | 8.0 | 0.0 | 8.8 | 0.0 |
| rogue | Striker | 10 | 800 | 8.4 | 7.6 | 0.0 | 8.1 | 0.0 |
| sorcerer | Striker | 1 | 600 | 3.9 | 2.8 | 0.0 | 1.6 | 0.0 |
| sorcerer | Striker | 2 | 600 | 4.8 | 3.3 | 0.0 | 2.1 | 0.0 |
| sorcerer | Striker | 3 | 600 | 5.4 | 4.1 | 0.0 | 1.5 | 0.0 |
| sorcerer | Striker | 4 | 600 | 5.6 | 4.3 | 0.0 | 1.5 | 0.0 |
| sorcerer | Striker | 5 | 600 | 11.0 | 5.8 | 0.0 | 0.9 | 0.0 |
| sorcerer | Striker | 6 | 600 | 11.0 | 5.8 | 0.0 | 0.8 | 0.0 |
| sorcerer | Striker | 7 | 600 | 12.5 | 6.7 | 0.0 | 0.6 | 0.0 |
| sorcerer | Striker | 8 | 600 | 12.4 | 6.8 | 0.0 | 0.6 | 0.0 |
| sorcerer | Striker | 9 | 600 | 27.7 | 8.6 | 0.0 | 0.4 | 0.0 |
| sorcerer | Striker | 10 | 600 | 26.8 | 8.5 | 0.0 | 0.4 | 0.0 |
| warlock | Striker | 1 | 800 | 3.8 | 3.5 | 0.0 | 1.9 | 0.0 |
| warlock | Striker | 2 | 800 | 5.2 | 4.1 | 0.0 | 2.1 | 0.0 |
| warlock | Striker | 3 | 800 | 5.3 | 3.9 | 0.0 | 1.9 | 0.0 |
| warlock | Striker | 4 | 800 | 5.0 | 3.8 | 0.0 | 2.0 | 0.0 |
| warlock | Striker | 5 | 800 | 8.5 | 5.4 | 0.0 | 0.6 | 0.0 |
| warlock | Striker | 6 | 800 | 8.5 | 5.4 | 0.0 | 0.6 | 0.0 |
| warlock | Striker | 7 | 800 | 9.5 | 6.5 | 0.0 | 0.4 | 0.0 |
| warlock | Striker | 8 | 800 | 11.0 | 8.3 | 0.0 | 0.4 | 0.0 |
| warlock | Striker | 9 | 800 | 11.4 | 7.8 | 0.0 | 0.2 | 0.0 |
| warlock | Striker | 10 | 800 | 11.8 | 7.7 | 0.0 | 0.2 | 0.0 |
| wizard | Control | 1 | 900 | 0.3 | 0.2 | 0.0 | 2.3 | 3.0 |
| wizard | Control | 2 | 900 | 0.3 | 0.3 | 0.0 | 2.5 | 3.2 |
| wizard | Control | 3 | 900 | 0.2 | 0.2 | 0.0 | 2.4 | 3.5 |
| wizard | Control | 4 | 900 | 0.3 | 0.3 | 0.0 | 2.2 | 3.8 |
| wizard | Control | 5 | 900 | 4.0 | 2.0 | 0.0 | 1.4 | 2.6 |
| wizard | Control | 6 | 900 | 4.1 | 2.0 | 0.0 | 1.2 | 2.6 |
| wizard | Control | 7 | 900 | 5.3 | 2.7 | 0.0 | 0.9 | 2.2 |
| wizard | Control | 8 | 900 | 3.6 | 1.4 | 0.0 | 1.0 | 2.1 |
| wizard | Control | 9 | 900 | 14.6 | 3.4 | 0.0 | 0.3 | 1.0 |
| wizard | Control | 10 | 900 | 0.5 | 0.2 | 0.0 | 0.7 | 1.6 |

### Sneak Attack set-up (rogues)

How the party's rogues set up Sneak Attack (#108), over their own turns: the share of turns in which they used Hide, landed Sneak Attack and made a weapon attack; and of those weapon attacks, the share made from hiding and the share made in melee flanking the target (an ally that can act on the far side of it). Scripted refusals are the commands of a scripted rogue (`--policy hidden-rogue`) the engine refused; each hands the rest of that turn to the AI.

| Level | Rogue turns | Hide | Sneak Attack | Attacking | From hiding | Flanking | Scripted refusals |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 4694 | 13.9% | 18.7% | 38.7% | 14.5% | 4.8% | 0 |
| 2 | 4576 | 3.6% | 24.2% | 54.8% | 5.1% | 7.2% | 0 |
| 3 | 4836 | 6.3% | 24.9% | 58.1% | 8.9% | 7.7% | 0 |
| 4 | 5197 | 7.0% | 26.6% | 61.0% | 9.4% | 8.4% | 0 |
| 5 | 4750 | 11.7% | 25.4% | 61.0% | 12.7% | 5.8% | 0 |
| 6 | 4886 | 13.2% | 28.0% | 63.4% | 15.4% | 6.1% | 0 |
| 7 | 4900 | 13.8% | 26.2% | 63.2% | 17.0% | 5.6% | 0 |
| 8 | 4996 | 12.3% | 28.6% | 65.6% | 15.6% | 6.1% | 0 |
| 9 | 4243 | 15.6% | 25.1% | 63.0% | 19.4% | 7.0% | 0 |
| 10 | 4369 | 11.8% | 26.0% | 63.7% | 14.2% | 7.6% | 0 |

## 5. Rarely used abilities

Abilities party members used in fewer than 2% of their turns where the ability was legal, over every battle (the built-in `common.*` actions are listed below but not flagged). Legal is judged at the start of each turn: an ability `Battle.Check` accepts on some unit or nearby tile, or refuses only for range or line of sight within the unit's Move plus the ability's range. Reactions (opportunity attacks, Shield, Uncanny Dodge and the like) aren't counted, used or legal.

| Ability | Used turns | Legal turns | Use rate |
|---|---:|---:|---:|
| barbarian.savage_roar | 160 | 17337 | 0.9% |
| bard.countercharm | 134 | 23297 | 0.6% |
| bard.dispel_magic | 0 | 13510 | 0.0% |
| bard.greater_restoration | 0 | 2443 | 0.0% |
| bard.hypnotic_pattern | 85 | 16073 | 0.5% |
| bard.lesser_restoration | 0 | 8483 | 0.0% |
| bard.thunderclap | 115 | 14907 | 0.8% |
| cleric.banishment | 5 | 5179 | 0.1% |
| cleric.greater_restoration | 0 | 2850 | 0.0% |
| cleric.resistance | 30 | 47548 | 0.1% |
| cleric.sear_undead | 139 | 17370 | 0.8% |
| cleric.turn_undead | 0 | 34388 | 0.0% |
| cleric.word_of_radiance | 158 | 22858 | 0.7% |
| druid.blight | 23 | 4021 | 0.6% |
| druid.call_lightning | 288 | 16994 | 1.7% |
| druid.greater_restoration | 1 | 2289 | 0.0% |
| druid.mass_cure_wounds | 0 | 4701 | 0.0% |
| druid.sleet_storm | 330 | 16994 | 1.9% |
| druid.spike_growth | 5 | 23188 | 0.0% |
| druid.thunderwave | 22 | 6141 | 0.4% |
| fighter.feinting_attack | 45 | 4125 | 1.1% |
| fighter.menacing_attack | 0 | 5119 | 0.0% |
| paladin.bless | 215 | 27074 | 0.8% |
| paladin.command | 51 | 9270 | 0.6% |
| paladin.compelled_duel | 378 | 21526 | 1.8% |
| paladin.dispel_magic | 0 | 2358 | 0.0% |
| paladin.elemental_weapon | 0 | 4589 | 0.0% |
| paladin.lesser_restoration | 0 | 7791 | 0.0% |
| ranger.barkskin | 9 | 12969 | 0.1% |
| ranger.cure_wounds | 520 | 26881 | 1.9% |
| ranger.ensnaring_strike | 53 | 30087 | 0.2% |
| ranger.entangle | 336 | 28501 | 1.2% |
| ranger.lesser_restoration | 0 | 8223 | 0.0% |
| sorcerer.font_of_magic_mana | 686 | 35045 | 2.0% |
| sorcerer.shocking_grasp | 93 | 15794 | 0.6% |
| warlock.arms_of_hadar | 226 | 13223 | 1.7% |
| warlock.hold_monster | 0 | 2785 | 0.0% |
| warlock.hold_person | 3 | 28312 | 0.0% |
| wizard.acid_splash | 674 | 51772 | 1.3% |
| wizard.burning_hands | 209 | 38108 | 0.5% |
| wizard.chill_touch | 10 | 1614 | 0.6% |
| wizard.magic_missile | 219 | 53073 | 0.4% |
| wizard.ray_of_frost | 644 | 60035 | 1.1% |
| wizard.scorching_ray | 220 | 41557 | 0.5% |
| wizard.shocking_grasp | 48 | 24006 | 0.2% |
| wizard.thunderwave | 0 | 18246 | 0.0% |

Every ability party members could use:

| Ability | Used turns | Legal turns | Use rate | Flag |
|---|---:|---:|---:|---|
| barbarian.brutal_strike | 1631 | 2921 | 55.8% |  |
| barbarian.cleaving_blow | 8442 | 17921 | 47.1% |  |
| barbarian.crushing_leap | 2191 | 8119 | 27.0% |  |
| barbarian.earthshaker | 291 | 4287 | 6.8% |  |
| barbarian.rage | 11263 | 25710 | 43.8% |  |
| barbarian.reckless_attack | 6799 | 25595 | 26.6% |  |
| barbarian.relentless_rage | 143 | 4477 | 3.2% |  |
| barbarian.savage_roar | 160 | 17337 | 0.9% | **RARE** |
| barbarian.whirlwind_slash | 175 | 8725 | 2.0% |  |
| bard.bardic_inspiration | 25449 | 39512 | 64.4% |  |
| bard.countercharm | 134 | 23297 | 0.6% | **RARE** |
| bard.cure_wounds | 2279 | 32656 | 7.0% |  |
| bard.dispel_magic | 0 | 13510 | 0.0% | **RARE** |
| bard.dissonant_whispers | 1983 | 25411 | 7.8% |  |
| bard.faerie_fire | 6068 | 29509 | 20.6% |  |
| bard.greater_invisibility | 1388 | 9984 | 13.9% |  |
| bard.greater_restoration | 0 | 2443 | 0.0% | **RARE** |
| bard.healing_word | 8997 | 33371 | 27.0% |  |
| bard.heroism | 2484 | 21855 | 11.4% |  |
| bard.hold_monster | 98 | 2828 | 3.5% |  |
| bard.hold_person | 1099 | 16808 | 6.5% |  |
| bard.hypnotic_pattern | 85 | 16073 | 0.5% | **RARE** |
| bard.lesser_restoration | 0 | 8483 | 0.0% | **RARE** |
| bard.mass_healing_word | 2998 | 16073 | 18.7% |  |
| bard.shatter | 9639 | 23387 | 41.2% |  |
| bard.song_of_rest | 3535 | 32022 | 11.0% |  |
| bard.thunderclap | 115 | 14907 | 0.8% | **RARE** |
| bard.vicious_mockery | 11342 | 42864 | 26.5% |  |
| cleric.aid | 4078 | 18703 | 21.8% |  |
| cleric.banishment | 5 | 5179 | 0.1% | **RARE** |
| cleric.bless | 4599 | 42846 | 10.7% |  |
| cleric.cure_wounds | 2612 | 42846 | 6.1% |  |
| cleric.death_ward | 3222 | 12714 | 25.3% |  |
| cleric.divine_intervention | 343 | 4222 | 8.1% |  |
| cleric.divine_spark | 4865 | 40121 | 12.1% |  |
| cleric.flame_strike | 1344 | 5476 | 24.5% |  |
| cleric.greater_restoration | 0 | 2850 | 0.0% | **RARE** |
| cleric.guiding_bolt | 10695 | 32190 | 33.2% |  |
| cleric.healing_word | 11709 | 42846 | 27.3% |  |
| cleric.hold_person | 1813 | 18467 | 9.8% |  |
| cleric.mass_cure_wounds | 848 | 5476 | 15.5% |  |
| cleric.mass_healing_word | 2415 | 21263 | 11.4% |  |
| cleric.resistance | 30 | 47548 | 0.1% | **RARE** |
| cleric.sacred_flame | 4325 | 56790 | 7.6% |  |
| cleric.sear_undead | 139 | 17370 | 0.8% | **RARE** |
| cleric.shield_of_faith | 2764 | 38369 | 7.2% |  |
| cleric.spirit_guardians | 206 | 4952 | 4.2% |  |
| cleric.spiritual_weapon | 4717 | 22201 | 21.2% |  |
| cleric.toll_the_dead | 13488 | 56790 | 23.8% |  |
| cleric.turn_undead | 0 | 34388 | 0.0% | **RARE** |
| cleric.word_of_radiance | 158 | 22858 | 0.7% | **RARE** |
| common.attack | 86428 | 291841 | 29.6% |  |
| common.dash | 57726 | 603509 | 9.6% |  |
| common.disengage | 1009 | 603509 | 0.2% |  |
| common.dodge | 69215 | 603509 | 11.5% |  |
| common.extinguish | 211 | 603509 | 0.0% |  |
| common.help | 33870 | 564694 | 6.0% |  |
| common.shove | 416 | 254271 | 0.2% |  |
| common.shove_prone | 42 | 254249 | 0.0% |  |
| druid.blight | 23 | 4021 | 0.6% | **RARE** |
| druid.call_lightning | 288 | 16994 | 1.7% | **RARE** |
| druid.conjure_animals | 2497 | 13844 | 18.0% |  |
| druid.entangle | 16925 | 28078 | 60.3% |  |
| druid.faerie_fire | 4051 | 28078 | 14.4% |  |
| druid.greater_restoration | 1 | 2289 | 0.0% | **RARE** |
| druid.healing_word | 3038 | 26251 | 11.6% |  |
| druid.hold_person | 497 | 17292 | 2.9% |  |
| druid.ice_storm | 1134 | 10456 | 10.8% |  |
| druid.mass_cure_wounds | 0 | 4701 | 0.0% | **RARE** |
| druid.moonbeam | 802 | 20596 | 3.9% |  |
| druid.produce_flame | 895 | 22491 | 4.0% |  |
| druid.sleet_storm | 330 | 16994 | 1.9% | **RARE** |
| druid.spike_growth | 5 | 23188 | 0.0% | **RARE** |
| druid.thorn_whip | 618 | 19838 | 3.1% |  |
| druid.thunderwave | 22 | 6141 | 0.4% | **RARE** |
| druid.wild_shape_bear | 1093 | 21285 | 5.1% |  |
| druid.wild_shape_dire_wolf | 1552 | 14761 | 10.5% |  |
| druid.wild_shape_wolf | 1373 | 26678 | 5.1% |  |
| fighter.action_surge | 5443 | 27306 | 19.9% |  |
| fighter.feinting_attack | 45 | 4125 | 1.1% | **RARE** |
| fighter.goading_attack | 756 | 14437 | 5.2% |  |
| fighter.menacing_attack | 0 | 5119 | 0.0% | **RARE** |
| fighter.precision_attack | 2077 | 19054 | 10.9% |  |
| fighter.pushing_attack | 4534 | 9259 | 49.0% |  |
| fighter.rally | 8448 | 22890 | 36.9% |  |
| fighter.second_wind | 3817 | 38362 | 9.9% |  |
| fighter.trip_attack | 7101 | 19464 | 36.5% |  |
| monk.flurry_of_blows | 1669 | 22494 | 7.4% |  |
| monk.heightened_flurry | 440 | 2643 | 16.6% |  |
| monk.martial_arts | 1642 | 28880 | 5.7% |  |
| monk.patient_defense | 4122 | 21283 | 19.4% |  |
| monk.step_of_the_wind | 7825 | 31334 | 25.0% |  |
| monk.stunning_strike | 6512 | 14067 | 46.3% |  |
| monk.unarmed_strike | 19202 | 30710 | 62.5% |  |
| paladin.aid | 2558 | 15452 | 16.6% |  |
| paladin.bless | 215 | 27074 | 0.8% | **RARE** |
| paladin.command | 51 | 9270 | 0.6% | **RARE** |
| paladin.compelled_duel | 378 | 21526 | 1.8% | **RARE** |
| paladin.cure_wounds | 2813 | 19536 | 14.4% |  |
| paladin.dispel_magic | 0 | 2358 | 0.0% | **RARE** |
| paladin.divine_smite | 3512 | 14946 | 23.5% |  |
| paladin.divine_smite_2 | 2233 | 8433 | 26.5% |  |
| paladin.divine_smite_3 | 888 | 2317 | 38.3% |  |
| paladin.elemental_weapon | 0 | 4589 | 0.0% | **RARE** |
| paladin.lay_on_hands | 9784 | 27717 | 35.3% |  |
| paladin.lesser_restoration | 0 | 7791 | 0.0% | **RARE** |
| paladin.shield_of_faith | 576 | 22316 | 2.6% |  |
| ranger.barkskin | 9 | 12969 | 0.1% | **RARE** |
| ranger.conjure_barrage | 688 | 786 | 87.5% |  |
| ranger.cure_wounds | 520 | 26881 | 1.9% | **RARE** |
| ranger.ensnaring_strike | 53 | 30087 | 0.2% | **RARE** |
| ranger.entangle | 336 | 28501 | 1.2% | **RARE** |
| ranger.hail_of_thorns | 4414 | 30087 | 14.7% |  |
| ranger.hunters_mark | 9259 | 27683 | 33.4% |  |
| ranger.lesser_restoration | 0 | 8223 | 0.0% | **RARE** |
| ranger.lightning_arrow | 1017 | 2451 | 41.5% |  |
| ranger.spike_growth | 926 | 22113 | 4.2% |  |
| ranger.vanish | 746 | 1791 | 41.7% |  |
| rogue.cunning_dash | 16593 | 40811 | 40.7% |  |
| rogue.cunning_disengage | 3702 | 36347 | 10.2% |  |
| rogue.cunning_hide | 5149 | 11788 | 43.7% |  |
| rogue.obscure | 1146 | 5985 | 19.1% |  |
| rogue.poison_strike | 1459 | 11428 | 12.8% |  |
| rogue.trip | 5204 | 17793 | 29.2% |  |
| rogue.withdraw | 206 | 3958 | 5.2% |  |
| sorcerer.acid_splash | 8881 | 52183 | 17.0% |  |
| sorcerer.burning_hands | 2221 | 13937 | 15.9% |  |
| sorcerer.chill_touch | 4241 | 25430 | 16.7% |  |
| sorcerer.chromatic_orb | 3915 | 23812 | 16.4% |  |
| sorcerer.cone_of_cold | 1449 | 1588 | 91.2% |  |
| sorcerer.fire_bolt | 2698 | 41674 | 6.5% |  |
| sorcerer.fireball | 3398 | 10514 | 32.3% |  |
| sorcerer.font_of_magic_mana | 686 | 35045 | 2.0% | **RARE** |
| sorcerer.font_of_magic_sorcery | 4239 | 27127 | 15.6% |  |
| sorcerer.hold_monster | 9 | 341 | 2.6% |  |
| sorcerer.ice_storm | 1612 | 6827 | 23.6% |  |
| sorcerer.innate_sorcery | 7977 | 8020 | 99.5% |  |
| sorcerer.metamagic_careful | 888 | 39085 | 2.3% |  |
| sorcerer.metamagic_extended | 1275 | 4579 | 27.8% |  |
| sorcerer.metamagic_quickened | 2692 | 6665 | 40.4% |  |
| sorcerer.metamagic_twinned | 3494 | 35045 | 10.0% |  |
| sorcerer.misty_step | 429 | 14459 | 3.0% |  |
| sorcerer.poison_spray | 288 | 1180 | 24.4% |  |
| sorcerer.ray_of_frost | 3612 | 41674 | 8.7% |  |
| sorcerer.scorching_ray | 4159 | 11730 | 35.5% |  |
| sorcerer.shocking_grasp | 93 | 15794 | 0.6% | **RARE** |
| sorcerer.wall_of_fire | 2393 | 5086 | 47.1% |  |
| warlock.arms_of_hadar | 226 | 13223 | 1.7% | **RARE** |
| warlock.blight | 969 | 10607 | 9.1% |  |
| warlock.chill_touch | 1824 | 52066 | 3.5% |  |
| warlock.eldritch_blast | 30788 | 58736 | 52.4% |  |
| warlock.hex | 13205 | 46651 | 28.3% |  |
| warlock.hold_monster | 0 | 2785 | 0.0% | **RARE** |
| warlock.hold_person | 3 | 28312 | 0.0% | **RARE** |
| warlock.hunger_of_hadar | 10292 | 22069 | 46.6% |  |
| warlock.maddening_hex | 545 | 27121 | 2.0% |  |
| warlock.shadow_of_moil | 4416 | 13697 | 32.2% |  |
| warlock.shatter | 2505 | 29200 | 8.6% |  |
| warlock.synaptic_static | 1299 | 8790 | 14.8% |  |
| wizard.acid_splash | 674 | 51772 | 1.3% | **RARE** |
| wizard.arcane_recovery | 4901 | 60377 | 8.1% |  |
| wizard.burning_hands | 209 | 38108 | 0.5% | **RARE** |
| wizard.chill_touch | 10 | 1614 | 0.6% | **RARE** |
| wizard.cone_of_cold | 639 | 7576 | 8.4% |  |
| wizard.fire_bolt | 2499 | 60034 | 4.2% |  |
| wizard.fireball | 2467 | 37223 | 6.6% |  |
| wizard.greater_invisibility | 3045 | 16628 | 18.3% |  |
| wizard.haste | 3449 | 5977 | 57.7% |  |
| wizard.hold_monster | 141 | 6653 | 2.1% |  |
| wizard.hypnotic_pattern | 810 | 37223 | 2.2% |  |
| wizard.ice_storm | 1466 | 22821 | 6.4% |  |
| wizard.magic_missile | 219 | 53073 | 0.4% | **RARE** |
| wizard.misty_step | 6917 | 45980 | 15.0% |  |
| wizard.ray_of_frost | 644 | 60035 | 1.1% | **RARE** |
| wizard.scorching_ray | 220 | 41557 | 0.5% | **RARE** |
| wizard.shocking_grasp | 48 | 24006 | 0.2% | **RARE** |
| wizard.sleep | 48239 | 66453 | 72.6% |  |
| wizard.thunderwave | 0 | 18246 | 0.0% | **RARE** |

## 6. Downs, Outs and AI item use

Party members Downed (each time) and Out, per battle; items the AI used (party members play with items off, so only enemies could use any). The last three columns are totals over both sides' turns: AI commands `Battle.Apply` refused, turns planned again (after a refusal or a move cut short) and turns the simulator ended because the plans didn't.

| Encounter | Level | Battles | Downs / battle | Outs / battle | Battles with an Out | AI item uses | Rejected commands | Re-plans | Forced ends |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| crypt_hall | 1 | 360 | 7.5 | 0.9 | 173 | 0 | 2 | 2 | 0 |
| crypt_hall | 2 | 360 | 7.3 | 1.8 | 293 | 0 | 5 | 5 | 0 |
| crypt_hall | 3 | 360 | 6.4 | 1.9 | 295 | 0 | 7 | 7 | 0 |
| crypt_hall | 4 | 360 | 5.1 | 1.6 | 261 | 0 | 7 | 7 | 0 |
| crypt_hall | 5 | 360 | 1.9 | 0.6 | 117 | 0 | 1 | 1 | 0 |
| crypt_hall | 6 | 360 | 1.5 | 0.5 | 95 | 0 | 2 | 2 | 0 |
| crypt_hall | 7 | 360 | 1.0 | 0.2 | 54 | 0 | 6 | 6 | 0 |
| crypt_hall | 8 | 360 | 0.7 | 0.2 | 53 | 0 | 1 | 1 | 0 |
| crypt_hall | 9 | 360 | 0.3 | 0.1 | 21 | 0 | 3 | 3 | 0 |
| crypt_hall | 10 | 360 | 0.2 | 0.1 | 22 | 0 | 0 | 0 | 0 |
| crystal_cave | 1 | 360 | 7.6 | 1.1 | 229 | 0 | 0 | 196 | 0 |
| crystal_cave | 2 | 360 | 7.6 | 1.7 | 286 | 0 | 2 | 197 | 0 |
| crystal_cave | 3 | 360 | 6.8 | 2.1 | 315 | 0 | 1 | 152 | 0 |
| crystal_cave | 4 | 360 | 6.5 | 2.1 | 315 | 0 | 4 | 174 | 0 |
| crystal_cave | 5 | 360 | 3.5 | 1.4 | 244 | 0 | 0 | 79 | 0 |
| crystal_cave | 6 | 360 | 3.1 | 1.3 | 225 | 0 | 1 | 63 | 0 |
| crystal_cave | 7 | 360 | 2.9 | 1.0 | 197 | 0 | 1 | 75 | 0 |
| crystal_cave | 8 | 360 | 2.5 | 0.9 | 175 | 0 | 0 | 66 | 0 |
| crystal_cave | 9 | 360 | 0.8 | 0.2 | 47 | 0 | 0 | 30 | 0 |
| crystal_cave | 10 | 360 | 0.9 | 0.4 | 81 | 0 | 0 | 67 | 0 |
| forest_ruins | 1 | 360 | 6.0 | 1.7 | 275 | 0 | 7 | 7 | 0 |
| forest_ruins | 2 | 360 | 4.0 | 1.4 | 224 | 0 | 10 | 10 | 0 |
| forest_ruins | 3 | 360 | 2.1 | 0.8 | 147 | 0 | 8 | 8 | 0 |
| forest_ruins | 4 | 360 | 1.4 | 0.6 | 100 | 0 | 7 | 7 | 0 |
| forest_ruins | 5 | 360 | 0.5 | 0.1 | 38 | 0 | 3 | 3 | 0 |
| forest_ruins | 6 | 360 | 0.3 | 0.1 | 27 | 0 | 6 | 6 | 0 |
| forest_ruins | 7 | 360 | 0.2 | 0.0 | 12 | 0 | 4 | 4 | 0 |
| forest_ruins | 8 | 360 | 0.1 | 0.0 | 8 | 0 | 1 | 1 | 0 |
| forest_ruins | 9 | 360 | 0.0 | 0.0 | 4 | 0 | 1 | 1 | 0 |
| forest_ruins | 10 | 360 | 0.0 | 0.0 | 0 | 0 | 4 | 4 | 0 |
| river_crossing | 1 | 360 | 4.5 | 1.0 | 167 | 0 | 0 | 0 | 0 |
| river_crossing | 2 | 360 | 2.1 | 0.3 | 70 | 0 | 0 | 0 | 0 |
| river_crossing | 3 | 360 | 0.7 | 0.1 | 27 | 0 | 1 | 1 | 0 |
| river_crossing | 4 | 360 | 0.5 | 0.1 | 18 | 0 | 0 | 0 | 0 |
| river_crossing | 5 | 360 | 0.1 | 0.0 | 2 | 0 | 0 | 0 | 0 |
| river_crossing | 6 | 360 | 0.1 | 0.0 | 3 | 0 | 0 | 0 | 0 |
| river_crossing | 7 | 360 | 0.0 | 0.0 | 0 | 0 | 0 | 0 | 0 |
| river_crossing | 8 | 360 | 0.0 | 0.0 | 0 | 0 | 0 | 0 | 0 |
| river_crossing | 9 | 360 | 0.0 | 0.0 | 0 | 0 | 0 | 0 | 0 |
| river_crossing | 10 | 360 | 0.0 | 0.0 | 0 | 0 | 3 | 3 | 0 |
| warlord_bridge | 1 | 360 | 8.2 | 1.1 | 209 | 0 | 2 | 2 | 0 |
| warlord_bridge | 2 | 360 | 7.2 | 0.8 | 194 | 0 | 4 | 4 | 0 |
| warlord_bridge | 3 | 360 | 5.5 | 0.8 | 187 | 0 | 2 | 2 | 0 |
| warlord_bridge | 4 | 360 | 5.0 | 0.9 | 213 | 0 | 2 | 2 | 0 |
| warlord_bridge | 5 | 360 | 3.4 | 0.8 | 167 | 0 | 12 | 12 | 0 |
| warlord_bridge | 6 | 360 | 3.1 | 0.6 | 159 | 0 | 21 | 21 | 0 |
| warlord_bridge | 7 | 360 | 2.7 | 0.6 | 144 | 0 | 14 | 14 | 0 |
| warlord_bridge | 8 | 360 | 2.4 | 0.6 | 131 | 0 | 7 | 7 | 0 |
| warlord_bridge | 9 | 360 | 2.0 | 0.4 | 117 | 0 | 7 | 7 | 0 |
| warlord_bridge | 10 | 360 | 1.9 | 0.4 | 115 | 0 | 4 | 4 | 0 |

### Environmental reactions and surfaces

Friendly conduction damage is same-side/self HP and temporary HP actually lost, without overkill. Burning tile-rounds count unique tiles present at any point in each round; surface deaths count terminal Died/Out events directly caused by surface packets, not Downs.

| Encounter | Level | Reactions / battle | Friendly conduction damage / battle | Burning tile-rounds / battle | Surface deaths | Reaction rules |
|---|---:|---:|---:|---:|---:|---|
| crypt_hall | 1 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 2 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 3 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 4 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 5 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 6 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 7 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 8 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 9 | 0.0 | 0.0 | 0.0 | 0 |  |
| crypt_hall | 10 | 0.0 | 0.0 | 0.0 | 0 |  |
| crystal_cave | 1 | 2.0 | 0.0 | 8.5 | 17 | burn_web: 3, ignite: 243, spread: 481 |
| crystal_cave | 2 | 2.9 | 0.0 | 12.2 | 25 | burn_web: 31, ignite: 313, spread: 701 |
| crystal_cave | 3 | 4.2 | 0.0 | 17.5 | 29 | burn_web: 80, ignite: 433, spread: 1000 |
| crystal_cave | 4 | 4.4 | 0.0 | 18.3 | 22 | burn_web: 98, ignite: 450, spread: 1021 |
| crystal_cave | 5 | 5.2 | 0.0 | 21.5 | 15 | burn_web: 225, ignite: 511, spread: 1118 |
| crystal_cave | 6 | 5.1 | 0.0 | 20.5 | 24 | burn_web: 244, ignite: 504, spread: 1075 |
| crystal_cave | 7 | 4.3 | 0.0 | 18.6 | 8 | burn_web: 184, ignite: 415, spread: 965 |
| crystal_cave | 8 | 4.5 | 0.0 | 18.4 | 9 | burn_web: 223, ignite: 425, spread: 966 |
| crystal_cave | 9 | 2.1 | 0.0 | 7.8 | 2 | burn_web: 146, ignite: 166, spread: 431 |
| crystal_cave | 10 | 3.3 | 0.0 | 12.9 | 0 | burn_web: 194, ignite: 254, spread: 735 |
| forest_ruins | 1 | 0.6 | 0.0 | 2.4 | 6 | ignite: 55, spread: 144 |
| forest_ruins | 2 | 0.5 | 0.0 | 2.3 | 7 | ignite: 50, spread: 142 |
| forest_ruins | 3 | 0.4 | 0.0 | 2.1 | 4 | ignite: 43, spread: 104 |
| forest_ruins | 4 | 0.4 | 0.0 | 1.7 | 2 | ignite: 40, spread: 86 |
| forest_ruins | 5 | 1.0 | 0.0 | 5.0 | 2 | ignite: 124, spread: 223 |
| forest_ruins | 6 | 1.0 | 0.0 | 5.2 | 2 | ignite: 127, spread: 241 |
| forest_ruins | 7 | 0.2 | 0.0 | 1.1 | 0 | ignite: 25, spread: 34 |
| forest_ruins | 8 | 0.7 | 0.0 | 3.2 | 0 | ignite: 77, spread: 168 |
| forest_ruins | 9 | 0.3 | 0.0 | 1.3 | 0 | ignite: 40, spread: 64 |
| forest_ruins | 10 | 0.3 | 0.0 | 1.2 | 0 | ignite: 38, spread: 63 |
| river_crossing | 1 | 2.6 | 0.0 | 12.8 | 32 | conduct: 5, freeze: 6, ignite: 240, spread: 699 |
| river_crossing | 2 | 1.3 | 0.0 | 5.8 | 13 | conduct: 3, freeze: 1, ignite: 133, spread: 335, thaw: 1 |
| river_crossing | 3 | 0.9 | 0.0 | 3.6 | 5 | ignite: 83, spread: 230 |
| river_crossing | 4 | 0.9 | 0.0 | 3.5 | 4 | ignite: 89, spread: 228 |
| river_crossing | 5 | 3.4 | 0.0 | 19.9 | 49 | freeze: 9, ignite: 496, spread: 713 |
| river_crossing | 6 | 3.3 | 0.0 | 19.5 | 46 | conduct: 1, freeze: 10, ignite: 494, spread: 682 |
| river_crossing | 7 | 2.7 | 0.0 | 16.9 | 54 | freeze: 18, ignite: 410, spread: 546 |
| river_crossing | 8 | 2.4 | 0.0 | 14.4 | 36 | freeze: 3, ignite: 351, spread: 521 |
| river_crossing | 9 | 1.4 | 0.0 | 4.5 | 4 | freeze: 109, ignite: 175, spread: 203 |
| river_crossing | 10 | 1.2 | 0.0 | 3.7 | 0 | freeze: 68, ignite: 156, spread: 190 |
| warlord_bridge | 1 | 1.0 | 0.0 | 3.6 | 0 | ignite: 86, spread: 265 |
| warlord_bridge | 2 | 0.7 | 0.0 | 2.5 | 0 | ignite: 65, spread: 182 |
| warlord_bridge | 3 | 0.4 | 0.0 | 1.3 | 0 | ignite: 42, spread: 105 |
| warlord_bridge | 4 | 0.4 | 0.0 | 1.4 | 0 | ignite: 38, spread: 112 |
| warlord_bridge | 5 | 1.6 | 0.0 | 5.3 | 0 | ignite: 166, spread: 426 |
| warlord_bridge | 6 | 1.6 | 0.0 | 5.4 | 0 | ignite: 165, spread: 425 |
| warlord_bridge | 7 | 0.2 | 0.0 | 0.7 | 0 | ignite: 22, spread: 36 |
| warlord_bridge | 8 | 0.9 | 0.0 | 3.2 | 1 | ignite: 84, spread: 249 |
| warlord_bridge | 9 | 0.3 | 0.0 | 1.1 | 0 | ignite: 27, spread: 81 |
| warlord_bridge | 10 | 0.3 | 0.0 | 1.1 | 0 | ignite: 25, spread: 74 |

## 7. Configuration and seeds

```
dotnet run -c Release --project games/Tactics/Tactics.Sim -- \
  --encounters crypt_hall,crystal_cave,forest_ruins,river_crossing,warlord_bridge --levels 1,2,3,4,5,6,7,8,9,10 --sizes 4,5,6 --parties 6 \
  --seeds 20 --difficulty Normal --round-cap 30 --rotate-formation
```

- **Encounters:** crypt_hall, crystal_cave, forest_ruins, river_crossing, warlord_bridge.
- **Levels:** 1, 2, 3, 4, 5, 6, 7, 8, 9, 10. **Party sizes:** 4, 5, 6 (6 sampled parties per size).
- **Seeds:** 1–20 for every encounter, level and party (the battle's `Battle.Start` seed).
- **Difficulty:** Normal. **Round cap:** 30 (a battle still going after it counts as lost).
- **Trinkets:** none.
- **Formation:** spawn seats rotate cyclically across seeds; roster ids and companion selection stay fixed. No RNG is consumed.
- **Companions:** none.
- **Parties:** human characters from `SheetBuilder.DefaultCharacter` (no subclass, default picks), playing the profile their class's `aiTags` name (controller → `controller`, guardian → `guardian`, healer → `healer`, caster or control → `caster`, ranged → `archer`, striker → `skirmisher`, otherwise `brute`) with items off; enemies play their monster's profile. Party compositions are drawn from every combination with at least one healer or defender and at most 2 of a class, by a generator seeded 20261030:
  - 4: barbarian, cleric, fighter, monk
  - 4: bard, ranger, rogue, wizard
  - 4: cleric, cleric, ranger, warlock
  - 4: fighter, fighter, rogue, wizard
  - 4: fighter, ranger, rogue, wizard
  - 4: paladin, paladin, ranger, wizard
  - 5: barbarian, barbarian, rogue, warlock, warlock
  - 5: barbarian, cleric, cleric, sorcerer, wizard
  - 5: bard, druid, fighter, monk, warlock
  - 5: bard, druid, sorcerer, sorcerer, warlock
  - 5: druid, fighter, monk, monk, warlock
  - 5: monk, paladin, ranger, sorcerer, warlock
  - 6: bard, bard, cleric, cleric, fighter, wizard
  - 6: bard, druid, monk, paladin, rogue, sorcerer
  - 6: bard, paladin, ranger, rogue, warlock, wizard
  - 6: cleric, cleric, rogue, rogue, sorcerer, wizard
  - 6: druid, fighter, monk, warlock, wizard, wizard
  - 6: druid, paladin, ranger, rogue, sorcerer, sorcerer
