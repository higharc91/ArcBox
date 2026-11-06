# Combat follow-ups #239, #240 and #241

These follow-ups start from merged combat main `057eecf3`. Hosted CI stays manually disabled at the owner’s request. The owner authorized merging the follow-up PR and closing completed issues after final local verification. This report records the measured results; the PR records the final local gate and merge state.

## Implementation and tradeoffs

| Issue | Behavior | Cost / limitation |
|---|---|---|
| #239 | Rogue gets two melee attacks or one ranged attack through optional `rangedAttacks` content, resolved when building the equipped sheet. Vex and the ally clause remain. The L3–4 Sneak Attack step is 2d6+2; L1–2/5–8/9–10 remain 2d6/5d6/7d6. The phone guide explains the budgets. | A bow gives up the second weapon hit in exchange for safer and more consistent access. The middle rider step loses 1.5 mean damage versus 3d6. Existing rider caps and monotonic level-up damage are preserved. |
| #240 | Standing heals include the estimated ally turn preserved by avoiding Downed, using 0.8 confidence in mean healing. A simultaneous ward covers only remaining risk. Guardian Control rises from 0.05 to 0.4; its other weights are unchanged. | Threat and mean healing are heuristics. Guardians may choose more control or buffs and deal less immediate damage. Healthy allies gain no survival credit, and the original fresh/spent Death Ward tests remain unchanged. |
| #241 | A refused non-reaction AI plan gets one fresh plan in the same turn, using remaining economy/resources and existing timeout options. A second refusal ends the turn. The retry guard resets between battles. Sim captures actual refusal reasons. | A second legitimate invalidation can still end the turn. Resources are not reserved or refunded, and the exception watchdog is retained. Sim refusals remain real counts rather than being hidden. |

## Measurement protocol

All cohorts use human default characters, no subclass, Normal difficulty, the same six sampled compositions per size 4–6, generator seed 20261030, round cap 30, seeds **1–20**, formation rotation, and no party items. The five benchmark encounters are crypt_hall, crystal_cave, forest_ruins, river_crossing and warlord_bridge. Each cohort is checkpointed by level; partitioning does not change the parties, seeds or gameplay. The original monolithic run was killed with signal 9; its incomplete results and six-seed tuning screens are excluded from the final tables. The audit runtime was limited to four processors and an 8 GiB managed heap; performance checks run later without these audit overrides.

| Measured gameplay snapshot | Rules hash |
|---|---|
| 158be1da | c1f25acf33a330bf000e20b631666d34cda4133f3f74c40536a9466422086216 |

| Cohort | Battles | Difference from default benchmark |
|---|---|---|
| Default benchmark | 18,000 | All sampled parties; starting rapier rogue |
| AI shortbow | 8,000 | Only the same sampled parties containing rogue; rogue weapon shortbow |
| Scripted shortbow | 8,000 | Same rogue parties and weapon; existing hidden-rogue policy |

CLI-equivalent benchmark command (checkpointing changes only `--levels`):

```sh
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --rules content/tactics/base/rules --encounters crypt_hall,crystal_cave,forest_ruins,river_crossing,warlord_bridge --levels 1,2,3,4,5,6,7,8,9,10 --sizes 4,5,6 --parties 6 --seeds 20 --rotate-formation
```

For AI shortbow add `--with-class rogue --weapons rogue:shortbow`; for scripted shortbow also add `--policy hidden-rogue`. Cap decisions use unrounded target-normalized damage per round (the same class/band calculation as the standard report), with raw damage retained in the appendix. Both bow cohorts compare against the paired default benchmark’s mean of the five Striker class means. Printed values are rounded only for reading.

## #239: final rogue targets

| Level | Striker mean | Rapier | Rapier deviation | AI bow / mean | Scripted bow / mean |
|---|---|---|---|---|---|
| 1 | 3.1794 | 3.3380 | +4.99% | 1.2553× | 1.1088× |
| 2 | 3.8111 | 4.1679 | +9.36% | 1.1102× | 1.0992× |
| 3 | 4.1508 | 4.4379 | +6.92% | 1.1135× | 1.0661× |
| 4 | 4.4624 | 5.0780 | +13.80% | 1.0257× | 1.0751× |
| 5 | 5.8803 | 5.9747 | +1.61% | 0.8924× | 0.9710× |
| 6 | 6.2893 | 6.7861 | +7.90% | 0.8317× | 0.9470× |
| 7 | 6.8676 | 6.9332 | +0.96% | 0.7886× | 0.8574× |
| 8 | 7.4253 | 7.3495 | -1.02% | 0.7540× | 0.8408× |
| 9 | 8.3551 | 7.8970 | -5.48% | 0.7335× | 0.7644× |
| 10 | 8.1569 | 7.6041 | -6.78% | 0.7328× | 0.7852× |

**All ten levels pass** rapier ±15%, AI shortbow ≤1.5× and scripted shortbow ≤1.5×. No ranged-cap exception is needed. The inherited main L4 rapier exception was +18.12%; this follow-up removes it.

Candidate screening used six seeds and the same rotated compositions. Early AI-bow ratios on the merged rules were 2.31/2.10/2.09/2.11× at L1–4 under the current normalized measure. Removing ranged Vex alone still gave 1.97/1.66/1.76/1.61× and was rejected. One ranged attack gave 1.33/1.19/1.16/1.09× while keeping Vex and the ally clause. Replacing default Trip with Poison did not fix L4 rapier (+18.5%); the 2d6+2 middle rider step did. These screens explain the choice; the final twenty-seed table above is authoritative.

## #240: final legal-turn ability use

| Ability | Merged-main baseline | Final used / legal | Final rate | Disposition |
|---|---|---|---|---|
| paladin.command | 51 / 9,270 (0.55%) | 355 / 9293 | 3.82% | Pass ≥2% |
| ranger.cure_wounds | 520 / 26,881 (1.93%) | 538 / 27079 | 1.99% | Situational; see explanation below |

Legal-turn counts use the existing Sim definition: judged at turn start, including a reachable target within remaining movement plus ability range. They do not assert that the spell is useful or preferable on that turn. Numerators count used own turns, not reaction casts.

**Command is situational.** It is a Wisdom-save, single-target, one-round denial. Its useful window is a foe that survives ordinary attacks and has a dangerous next turn, or a paladin that cannot reach melee with its remaining movement but can still see a foe within range 6. Small foes that the party can finish, already-disabled foes, poor save odds and short remaining fights offer little denial value. Oath competition with Smite adds a cost but is not the sole reason. The scorer already counted the whole denied turn; the guardian’s old 0.05 control weight suppressed it. Regressions cover a lethal foe and a normal weapon foe beyond remaining melee reach, with accepted commands rather than a usage-frequency bonus.

**Ranger Cure Wounds is emergency, touch-range support.** It is useful when the ranger can walk to an injured ally and the restored HP materially lowers the chance that ally loses its next turn. A healthy ally, an ally that remains overwhelmingly exposed after the heal, or an ally outside touch reach gives little such value; the back-line ranger otherwise contributes by attacking. Regression tests require walking to heal an exposed ally at 1 HP and spare Focus for a healthy ally. The original Death Ward preference tests still pass. The final table states whether the aggregate clears 2%; situational behavior is also documented in `ai-abilities.md`.

## #241: remaining real refusals and recovery

| Cohort | Rejected commands | Replans | Forced ends |
|---|---|---|---|
| benchmark | 174 | 1248 | 0 |
| bow-ai | 185 | 661 | 0 |
| bow-script | 338 | 645 | 0 |

Replans also include moves cut short, so that column can exceed refused commands. Counts cover both sides. The simulator retains its existing four-plan budget; the real adapter’s new budget is one retry. The adapter regression triggers a real opportunity attack, spends the last Pact on Hellish Rebuke during a walk, observes stale Hex refused with CantAfford, and then observes Eldritch Blast before EndTurn. A permanently stale planner retries only once and ends. Resources and economy are not reset.

| Actual command / rejection | Default | AI bow | Scripted bow |
|---|---|---|---|
| Move / PathBlocked | 1 | 0 | 0 |
| ranger.hunters_mark / InvalidTarget | 0 | 1 | 2 |
| rogue.cunning_hide / Unmet | 101 | 146 | 305 |
| sorcerer.font_of_magic_sorcery / CantAfford | 3 | 3 | 0 |
| sorcerer.scorching_ray / CantAfford | 2 | 2 | 3 |
| warlock.arms_of_hadar / CantAfford | 2 | 1 | 0 |
| warlock.blight / CantAfford | 2 | 0 | 0 |
| warlock.eldritch_blast / InvalidTarget | 5 | 1 | 1 |
| warlock.hex / CantAfford | 34 | 16 | 17 |
| warlock.hex / InvalidTarget | 4 | 0 | 0 |
| warlock.hunger_of_hadar / CantAfford | 2 | 3 | 1 |
| warlock.maddening_hex / InvalidTarget | 1 | 1 | 1 |
| warlock.shadow_of_moil / CantAfford | 2 | 0 | 0 |
| warlock.shatter / CantAfford | 0 | 1 | 0 |
| wizard.fireball / CantAfford | 1 | 0 | 0 |
| wizard.misty_step / CantAfford | 0 | 1 | 2 |
| wizard.sleep / CantAfford | 14 | 9 | 6 |

CantAfford remains possible when reactions spend the resource a cached spell expected; the fresh plan recovers from the current state. Unmet hiding and InvalidTarget cases are separate stale-plan causes and remain included rather than relabeled as successful commands. The engine still rejects illegal commands, and bounded recovery prevents an endless retry loop. This satisfies the issue’s documented-residual alternative; it is not a claim of zero refusals.

## Wider benchmark effects

- Merged-main baseline: 18,000 battles; 14,168 wins (78.71%); 8.47 mean rounds; 72 capped.
- Final follow-up: 18,000 battles; 14,156 wins (78.64%); 8.47 mean rounds; 62 capped.

The inherited broader balance limitations are kept visible below. These follow-ups target the rogue and the two spell choices; passing those targets does not assert that every other class/level or party-size cell fits its band. The four non-benchmark story encounters and paired companion gaps from the prior audit are not remeasured here.

| Encounter | Level | Size | N | Wins | Rounds | Capped | Flags |
|---|---|---|---|---|---|---|---|
| river_crossing | 1 | all | 360 | 86.94% | 10.86 | 9 | CAPPED |
| river_crossing | 1 | 4 | 120 | 70.00% | 13.12 | 8 | LONG, CAPPED |
| river_crossing | 1 | 5 | 120 | 94.17% | 9.95 | 0 |  |
| river_crossing | 1 | 6 | 120 | 96.67% | 9.50 | 1 | HIGH, CAPPED |
| forest_ruins | 3 | all | 360 | 78.89% | 9.88 | 1 | CAPPED |
| forest_ruins | 3 | 4 | 120 | 40.83% | 11.65 | 1 | LOW, CAPPED |
| forest_ruins | 3 | 5 | 120 | 95.83% | 10.11 | 0 | HIGH |
| forest_ruins | 3 | 6 | 120 | 100.00% | 7.88 | 0 | HIGH, SHORT |
| crypt_hall | 5 | all | 360 | 85.56% | 10.08 | 2 | CAPPED |
| crypt_hall | 5 | 4 | 120 | 57.50% | 12.10 | 2 | LOW, LONG, CAPPED |
| crypt_hall | 5 | 5 | 120 | 99.17% | 9.86 | 0 | HIGH |
| crypt_hall | 5 | 6 | 120 | 100.00% | 8.28 | 0 | HIGH |
| warlord_bridge | 5 | all | 360 | 89.72% | 11.19 | 0 |  |
| warlord_bridge | 5 | 4 | 120 | 71.67% | 11.07 | 0 |  |
| warlord_bridge | 5 | 5 | 120 | 97.50% | 11.67 | 0 | HIGH |
| warlord_bridge | 5 | 6 | 120 | 100.00% | 10.85 | 0 | HIGH |
| crystal_cave | 7 | all | 360 | 85.28% | 10.41 | 2 | CAPPED |
| crystal_cave | 7 | 4 | 120 | 57.50% | 13.03 | 2 | LOW, LONG, CAPPED |
| crystal_cave | 7 | 5 | 120 | 98.33% | 10.17 | 0 | HIGH |
| crystal_cave | 7 | 6 | 120 | 100.00% | 8.03 | 0 | HIGH |

### All class and level measures

| Level | Band | Class | N | Raw DPR | Target DPR | Band value | Deviation | Flag |
|---|---|---|---|---|---|---|---|---|
| 1 | Defender | barbarian | 300 | 2.018 | 1.765 | 5.094 | +12.67% |  |
| 1 | Defender | fighter | 700 | 2.305 | 2.256 | 4.828 | +6.78% |  |
| 1 | Defender | paladin | 500 | 1.041 | 0.990 | 3.642 | -19.45% | FLAG |
| 1 | Striker | monk | 600 | 3.012 | 2.801 | 2.801 | -11.90% |  |
| 1 | Striker | ranger | 700 | 4.074 | 3.524 | 3.524 | +10.83% |  |
| 1 | Striker | rogue | 800 | 3.513 | 3.338 | 3.338 | +4.99% |  |
| 1 | Striker | sorcerer | 600 | 3.948 | 2.783 | 2.783 | -12.46% |  |
| 1 | Striker | warlock | 800 | 3.807 | 3.451 | 3.451 | +8.54% |  |
| 1 | Healer | bard | 600 | 1.201 | 1.139 | 2.394 | +10.75% |  |
| 1 | Healer | cleric | 500 | 2.789 | 2.774 | 1.929 | -10.75% |  |
| 1 | Control | druid | 600 | 0.498 | 0.472 | 3.918 | +14.25% |  |
| 1 | Control | wizard | 900 | 0.257 | 0.245 | 2.941 | -14.25% |  |
| 2 | Defender | barbarian | 300 | 2.504 | 2.212 | 6.161 | +6.06% |  |
| 2 | Defender | fighter | 700 | 3.139 | 2.926 | 6.420 | +10.52% |  |
| 2 | Defender | paladin | 500 | 1.550 | 1.517 | 4.846 | -16.57% | FLAG |
| 2 | Striker | monk | 600 | 3.604 | 3.418 | 3.418 | -10.31% |  |
| 2 | Striker | ranger | 700 | 4.636 | 4.048 | 4.048 | +6.22% |  |
| 2 | Striker | rogue | 800 | 4.386 | 4.168 | 4.168 | +9.36% |  |
| 2 | Striker | sorcerer | 600 | 4.857 | 3.319 | 3.319 | -12.91% |  |
| 2 | Striker | warlock | 800 | 5.201 | 4.102 | 4.102 | +7.64% |  |
| 2 | Healer | bard | 600 | 0.858 | 0.808 | 2.416 | -2.47% |  |
| 2 | Healer | cleric | 500 | 2.390 | 2.379 | 2.539 | +2.47% |  |
| 2 | Control | druid | 600 | 0.445 | 0.441 | 4.112 | +14.36% |  |
| 2 | Control | wizard | 900 | 0.259 | 0.253 | 3.079 | -14.36% |  |
| 3 | Defender | barbarian | 300 | 2.773 | 2.438 | 6.453 | +7.54% |  |
| 3 | Defender | fighter | 700 | 3.181 | 3.055 | 6.599 | +9.98% |  |
| 3 | Defender | paladin | 500 | 1.641 | 1.614 | 4.949 | -17.52% | FLAG |
| 3 | Striker | monk | 600 | 4.539 | 4.299 | 4.299 | +3.57% |  |
| 3 | Striker | ranger | 700 | 4.974 | 4.078 | 4.078 | -1.76% |  |
| 3 | Striker | rogue | 800 | 4.648 | 4.438 | 4.438 | +6.92% |  |
| 3 | Striker | sorcerer | 600 | 5.320 | 3.994 | 3.994 | -3.77% |  |
| 3 | Striker | warlock | 800 | 5.292 | 3.945 | 3.945 | -4.96% |  |
| 3 | Healer | bard | 600 | 5.122 | 2.130 | 1.720 | -5.19% |  |
| 3 | Healer | cleric | 500 | 2.492 | 2.434 | 1.908 | +5.19% |  |
| 3 | Control | druid | 600 | 0.428 | 0.425 | 4.090 | +7.40% |  |
| 3 | Control | wizard | 900 | 0.178 | 0.166 | 3.527 | -7.40% |  |
| 4 | Defender | barbarian | 300 | 3.078 | 2.823 | 6.026 | -4.06% |  |
| 4 | Defender | fighter | 700 | 3.175 | 3.031 | 7.180 | +14.31% |  |
| 4 | Defender | paladin | 500 | 1.652 | 1.637 | 5.637 | -10.26% |  |
| 4 | Striker | monk | 600 | 5.265 | 5.018 | 5.018 | +12.44% |  |
| 4 | Striker | ranger | 700 | 5.044 | 4.133 | 4.133 | -7.38% |  |
| 4 | Striker | rogue | 800 | 5.336 | 5.078 | 5.078 | +13.80% |  |
| 4 | Striker | sorcerer | 600 | 5.412 | 4.248 | 4.248 | -4.82% |  |
| 4 | Striker | warlock | 800 | 5.046 | 3.836 | 3.836 | -14.04% |  |
| 4 | Healer | bard | 600 | 4.935 | 2.047 | 1.991 | -9.20% |  |
| 4 | Healer | cleric | 500 | 2.339 | 2.275 | 2.395 | +9.20% |  |
| 4 | Control | druid | 600 | 1.626 | 1.024 | 4.387 | +6.26% |  |
| 4 | Control | wizard | 900 | 0.257 | 0.246 | 3.869 | -6.26% |  |
| 5 | Defender | barbarian | 300 | 4.605 | 4.078 | 5.142 | -9.41% |  |
| 5 | Defender | fighter | 700 | 4.749 | 4.561 | 6.693 | +17.90% | FLAG |
| 5 | Defender | paladin | 500 | 2.675 | 2.601 | 5.195 | -8.49% |  |
| 5 | Striker | monk | 600 | 6.064 | 5.743 | 5.743 | -2.34% |  |
| 5 | Striker | ranger | 700 | 6.904 | 6.561 | 6.561 | +11.58% |  |
| 5 | Striker | rogue | 800 | 6.424 | 5.975 | 5.975 | +1.61% |  |
| 5 | Striker | sorcerer | 600 | 10.832 | 5.713 | 5.713 | -2.84% |  |
| 5 | Striker | warlock | 800 | 8.464 | 5.410 | 5.410 | -8.00% |  |
| 5 | Healer | bard | 600 | 4.307 | 1.903 | 1.932 | -9.66% |  |
| 5 | Healer | cleric | 500 | 3.130 | 3.038 | 2.345 | +9.66% |  |
| 5 | Control | druid | 600 | 0.984 | 0.747 | 3.287 | +11.76% |  |
| 5 | Control | wizard | 900 | 4.065 | 2.077 | 2.595 | -11.76% |  |
| 6 | Defender | barbarian | 300 | 5.119 | 4.387 | 5.536 | -3.81% |  |
| 6 | Defender | fighter | 700 | 4.876 | 4.435 | 6.877 | +19.48% | FLAG |
| 6 | Defender | paladin | 500 | 2.580 | 2.496 | 4.853 | -15.68% | FLAG |
| 6 | Striker | monk | 600 | 6.788 | 6.327 | 6.327 | +0.59% |  |
| 6 | Striker | ranger | 700 | 7.524 | 7.193 | 7.193 | +14.36% |  |
| 6 | Striker | rogue | 800 | 7.330 | 6.786 | 6.786 | +7.90% |  |
| 6 | Striker | sorcerer | 600 | 10.994 | 5.737 | 5.737 | -8.79% |  |
| 6 | Striker | warlock | 800 | 8.561 | 5.405 | 5.405 | -14.07% |  |
| 6 | Healer | bard | 600 | 4.410 | 1.921 | 2.476 | +1.27% |  |
| 6 | Healer | cleric | 500 | 2.815 | 2.755 | 2.414 | -1.27% |  |
| 6 | Control | druid | 600 | 1.177 | 0.989 | 2.675 | +2.41% |  |
| 6 | Control | wizard | 900 | 4.031 | 2.000 | 2.549 | -2.41% |  |
| 7 | Defender | barbarian | 300 | 5.854 | 4.961 | 5.526 | -3.32% |  |
| 7 | Defender | fighter | 700 | 5.375 | 4.915 | 6.602 | +15.49% | FLAG |
| 7 | Defender | paladin | 500 | 2.645 | 2.572 | 5.021 | -12.17% |  |
| 7 | Striker | monk | 600 | 7.271 | 6.842 | 6.842 | -0.37% |  |
| 7 | Striker | ranger | 700 | 7.687 | 7.336 | 7.336 | +6.82% |  |
| 7 | Striker | rogue | 800 | 7.446 | 6.933 | 6.933 | +0.96% |  |
| 7 | Striker | sorcerer | 600 | 12.520 | 6.671 | 6.671 | -2.86% |  |
| 7 | Striker | warlock | 800 | 9.515 | 6.555 | 6.555 | -4.54% |  |
| 7 | Healer | bard | 600 | 4.222 | 1.859 | 2.771 | +7.59% |  |
| 7 | Healer | cleric | 500 | 2.381 | 2.319 | 2.380 | -7.59% |  |
| 7 | Control | druid | 600 | 3.554 | 2.081 | 2.048 | -2.41% |  |
| 7 | Control | wizard | 900 | 5.364 | 2.696 | 2.149 | +2.41% |  |
| 8 | Defender | barbarian | 300 | 6.071 | 5.065 | 5.666 | -4.34% |  |
| 8 | Defender | fighter | 700 | 5.540 | 5.025 | 7.113 | +20.08% | FLAG |
| 8 | Defender | paladin | 500 | 2.836 | 2.757 | 4.991 | -15.74% | FLAG |
| 8 | Striker | monk | 600 | 7.549 | 7.084 | 7.084 | -4.60% |  |
| 8 | Striker | ranger | 700 | 8.042 | 7.692 | 7.692 | +3.59% |  |
| 8 | Striker | rogue | 800 | 7.894 | 7.349 | 7.349 | -1.02% |  |
| 8 | Striker | sorcerer | 600 | 12.249 | 6.704 | 6.704 | -9.71% |  |
| 8 | Striker | warlock | 800 | 10.983 | 8.297 | 8.297 | +11.74% |  |
| 8 | Healer | bard | 600 | 4.415 | 1.869 | 2.710 | +4.62% |  |
| 8 | Healer | cleric | 500 | 2.451 | 2.402 | 2.471 | -4.62% |  |
| 8 | Control | druid | 600 | 3.663 | 1.919 | 2.650 | +12.30% |  |
| 8 | Control | wizard | 900 | 3.592 | 1.436 | 2.069 | -12.30% |  |
| 9 | Defender | barbarian | 300 | 6.467 | 5.750 | 4.398 | +5.86% |  |
| 9 | Defender | fighter | 700 | 5.313 | 4.751 | 4.951 | +19.17% | FLAG |
| 9 | Defender | paladin | 500 | 2.090 | 2.049 | 3.115 | -25.03% | FLAG |
| 9 | Striker | monk | 600 | 9.300 | 8.823 | 8.823 | +5.61% |  |
| 9 | Striker | ranger | 700 | 8.943 | 8.715 | 8.715 | +4.30% |  |
| 9 | Striker | rogue | 800 | 8.449 | 7.897 | 7.897 | -5.48% |  |
| 9 | Striker | sorcerer | 600 | 27.565 | 8.598 | 8.598 | +2.90% |  |
| 9 | Striker | warlock | 800 | 11.382 | 7.743 | 7.743 | -7.33% |  |
| 9 | Healer | bard | 600 | 3.326 | 1.474 | 3.188 | +10.23% |  |
| 9 | Healer | cleric | 500 | 3.872 | 3.232 | 2.596 | -10.23% |  |
| 9 | Control | druid | 600 | 1.623 | 1.097 | 1.962 | +32.20% | FLAG |
| 9 | Control | wizard | 900 | 14.712 | 3.450 | 1.006 | -32.20% | FLAG |
| 10 | Defender | barbarian | 300 | 7.239 | 6.325 | 3.792 | -9.56% |  |
| 10 | Defender | fighter | 700 | 5.920 | 5.372 | 5.952 | +41.97% | FLAG |
| 10 | Defender | paladin | 500 | 1.925 | 1.892 | 2.834 | -32.41% | FLAG |
| 10 | Striker | monk | 600 | 9.897 | 9.252 | 9.252 | +13.42% |  |
| 10 | Striker | ranger | 700 | 18.343 | 7.661 | 7.661 | -6.08% |  |
| 10 | Striker | rogue | 800 | 8.405 | 7.604 | 7.604 | -6.78% |  |
| 10 | Striker | sorcerer | 600 | 26.919 | 8.562 | 8.562 | +4.97% |  |
| 10 | Striker | warlock | 800 | 11.673 | 7.706 | 7.706 | -5.53% |  |
| 10 | Healer | bard | 600 | 3.401 | 1.602 | 3.299 | +3.07% |  |
| 10 | Healer | cleric | 500 | 4.780 | 3.604 | 3.103 | -3.07% |  |
| 10 | Control | druid | 600 | 1.439 | 0.908 | 2.167 | +16.49% | FLAG |
| 10 | Control | wizard | 900 | 0.471 | 0.201 | 1.553 | -16.49% | FLAG |

## Validation

Regression-first checks: #241 reproduced both lost-action and unbounded-stale-plan cases on main, then 37 adapter/reaction/watchdog and 13 Sim/report checks passed. #239 reproduced ten missing ranged-budget cases; selected content, rider-cap and monotonic-level checks passed (122), and character guide checks passed (45). #240 reproduced the emergency-heal and lethal-foe decisions; the original Death Ward tests remain unchanged. Four phone fixtures were regenerated and then verified normally; seven golden replays passed without rerecording. No hosted workflow was enabled or dispatched.

Full integrated local gate passes all five groups after simulation: 7182 .NET tests, 547 phone tests plus production build, 24 Node tests, Python tool suites (26/9/23 tests), and lore/docs checks. The build passes. Hosted CI stayed disabled.

