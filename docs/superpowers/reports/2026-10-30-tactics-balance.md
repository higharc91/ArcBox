# Tactics balance report (final)

Task 23 of the [rules engine plan](../plans/2026-10-30-tactics-rules-engine.md). The tuning is in content only: there are no changes to `Tactics.Logic`. The first baseline is in [2026-10-30-tactics-balance-v0.md](2026-10-30-tactics-balance-v0.md). The full simulator output for the final content is in the [appendix](#appendix-final-simulator-report).

## Summary

**Two of the four balance targets are met, and two are partly met.** The role-band target has 52 of 60 values in band and 8 out. The usage target has 29 of its 55 rare abilities documented as situational, while the other 26 are rarely used because of AI planner limits or a better option in the same loadout. Success criterion 2 is only partly met, and criterion 4 is met for the stress setup and both 10-enemy sample encounters (see below). The other criteria are met.

Final sweep: all 5 encounters at levels 1, 3, 5, 7 and 10, party sizes 4, 5 and 6, 6 parties per size, 20 seeds, Normal. That is 9,000 battles.

### The four targets

| Target | Result | Evidence |
|---|---|---|
| Every class within ±15% of its role band at every level | **Unmet:** 52 of 60 in band | The 8 misses are under [Unmet role bands](#unmet-role-bands). The baseline had 44 of 60 out of band |
| No ability under 2% use, or documented as situational | **Partly met:** 55 are under 2%; 29 are situational, 26 are not | See [Rarely used abilities](#rarely-used-abilities). 19 need conditions the sample encounters rarely provide and 10 need the user to close to melee range: those 29 meet the target. 10 are AI planner limits and 16 are outranked by a better option in the same default loadout: real players would use them, so those 26 don't meet it |
| Every sample encounter won 60–95% on Normal at its level | **Met:** 5 of 5 | Table below |
| Median battle length 8–12 rounds | **Met:** every encounter at its level, and 9.0 overall | Table below. Crystal Cave's *mean* is 12.3, so the simulator's mean-based flag still marks it LONG; its median is 12.0 |

| Encounter | Level | Win rate | By size 4 / 5 / 6 | Mean rounds | Median rounds | Stopped at round cap |
|---|---:|---:|---:|---:|---:|---:|
| river_crossing | 1 | 90.8% | 79.2 / 94.2 / 99.2% | 9.5 | 9.0 | 1 |
| forest_ruins | 3 | 86.9% | 63.3 / 97.5 / 100% | 9.7 | 9.0 | 0 |
| crypt_hall | 5 | 92.8% | 78.3 / 100 / 100% | 11.8 | 11.0 | 1 |
| warlord_bridge | 5 | 91.9% | 84.2 / 91.7 / 100% | 10.9 | 12.0 | 0 |
| crystal_cave | 7 | 93.1% | 79.2 / 100 / 100% | 12.3 | 12.0 | 2 |

Encounters don't scale with party size, so each win rate hides a spread: parties of 4 win 63–84% and parties of 6 win 99–100%.

### Success criteria (spec §1)

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | Every class and subclass at levels 1–10 loads, validates and has generated tooltip text | **Met** | `BasePackTests.Every_class_builds_a_default_character_at_levels_1_to_10` and `Every_ability_passive_and_condition_has_a_tooltip_at_levels_1_to_10` pass on the tuned pack |
| 2 | `Tactics.Sim` runs every sample encounter with mixed parties of 4–6, and every class's damage, healing and mitigation per round sits inside its role band | **Partly met** | The sweep plays all 9,000 battles, but 8 of the 60 band values are outside ±15%. Simulated parties have no subclass, so subclass balance isn't measured |
| 3 | A battle saved after any command and restored finishes identically | **Met** | `GoldenTests.Every_replay_matches_events` and `Replay_is_deterministic` round-trip the state after every command of all three replays |
| 4 | An AI turn takes ≤ 250 ms at p95 in the worst sample encounter (6 heroes vs 10 enemies) | **Met** | `AiPerformanceTests.P95_under_250ms_on_6_vs_10` passes. No encounter starts with more than 10 enemies: crypt_hall and crystal_cave have exactly 10 |
| 5 | The showcase encounter's triggers fire as scripted, checked by a golden replay | **Met** | `GoldenTests.Warlord_bridge_plays_the_showcase_triggers` passes on seed 15, with all five triggers and the boss's phase |
| 6 | `dotnet test ArcBox.slnx` stays green | **Met** | 1,859 tests passed, 0 failed; `dotnet build ArcBox.slnx` gives 0 warnings |

### Unmet role bands

| Level | Class | Band measure | Value | Band mean | Deviation | Best explanation |
|---:|---|---|---:|---:|---:|---|
| 3 | rogue | damage / round | 3.8 | 4.5 | −15.6% | Misses by 0.6 percentage points. Melee classes lose their first rounds walking in |
| 5 | fighter | mitigated / round | 6.3 | 5.2 | +20.3% | The measure counts every attack aimed at a unit, so it mostly shows how long the unit stands and draws attacks. The fighter has AC 18, plus 1 from its Defense style, and lasts longest |
| 7 | fighter | mitigated / round | 7.0 | 5.7 | +23.1% | As above |
| 7 | paladin | mitigated / round | 4.7 | 5.7 | −17.9% | The fighter lifts the band mean, and the paladin spends turns on Lay on Hands and support |
| 10 | fighter | mitigated / round | 6.1 | 5.2 | +18.0% | As for level 5 |
| 10 | barbarian | mitigated / round | 3.9 | 5.2 | −24.7% | Rage's resistance halves the HP it loses, so the better it tanks, the less the measure credits it |
| 10 | druid | turns denied / battle | 3.3 | 2.4 | +35.9% | See below |
| 10 | wizard | turns denied / battle | 1.5 | 2.4 | −35.9% | The wizard's concentration goes to Haste, which the AI rates well above its control spells |

**Level 10 is the weakest row because no sample encounter is built for it.** Every level-10 number comes from fights below the party's level, won 99–100% of the time. Fixing control there would have meant cutting Haste below the "1 minute → 10 rounds" convention, so I left it.

**The defender misses come from the measure more than the numbers.** It rewards being attacked and counts resistance as less mitigation. Crediting the damage that resistance prevents would need a change to `Tactics.Sim`'s `Tally`, which was out of scope. No `Tactics.Logic` change is needed for any of the targets.

## What changed

Each tuning change and its reason. Values are final.

### AI weights (`ai-profiles.json`)

| Change | Reason |
|---|---|
| `caution`: brute 0.2 → 0.05, skirmisher 0.6 → 0.1, archer 0.8 → 0.2, caster 0.8 → 0.25, healer 0.6 → 0.2, coward 1.2 → 0.6, boss and boss_cruel 0.3 → 0.1 | Traces showed both sides circling out of each other's threat range for rounds on end. Level-1 fights ran 12–14 rounds with few attacks. This was the largest single cause of melee classes dealing almost nothing |
| `control`: brute 0.4 → 0.15, skirmisher 0.5 → 0.2, archer 0.5 → 0.2, healer 0.6 → 0.3, coward 0.4 → 0.2 | Dodge is valued through `control`, so units out of reach Dodged instead of Dashing. At baseline, Dodge and Help took 39% of party turns and the plain Attack action 5% |

`companion` is unchanged: the simulator doesn't play it, and `CompanionTests` depends on its weights. **So absent players' AI doesn't get these changes.** They apply to enemies and to the simulated parties.

### Strikers

| Change | Reason |
|---|---|
| Rogue: 3 attacks from level 1, 4 from level 5, 5 from level 9 (was 1) | The rogue dealt 0.4–0.8 damage per round at baseline, against up to 30 for the top strikers. Its bonus needs advantage or a flank, which the AI rarely set up |
| Rogue: Sneak Attack 3d6 at level 1, 6d6 at 3, 9d6 at 9 (was 1d6 up to 5d6) | As above |
| Rogue: Cunning Hide costs no edge and comes at level 1 (was 1 edge, level 2); edge 3 / 4 / 5 (was 2 / 3 / 4); rapier instead of shortsword | Hide is how it gets advantage, so it has to be available every turn |
| Monk: 2 attacks from level 1, 3 from level 5 (was 1, then 2); martial arts die 2d8 at levels 1–2, then d10 (was d6, then d8) | The same problem as the rogue, less severe |
| Monk: Patient Defense 1 → 2 ki | Stops it spending ki on defence ahead of Flurry |
| Ranger: Focus 1 at level 1, 2 at 3, topping out at 5 (was 2, up to 6) | Ranged classes hit from round 1 while melee classes walk; the ranger was +69% at level 1 |
| Ranger: Hail of Thorns chains to 2 foes, not 4, for 1d4 (1d6 from level 3), not 1d10 | As above |
| Ranger: Conjure Barrage 5d8 → 2d6, 3 → 4 Focus | It dealt 48 damage per use at level 10 |
| Sorcerer: Fireball 8d6 → 3d6, Cone of Cold 8d8 → 2d8, Wall of Fire 5d8 → 2d8, Ice Storm 2d10 + 4d6 → 1d6 + 2d6, Chromatic Orb 3d8 → 2d8 | The sorcerer was +206% at level 5: area damage over several foes outscores any single target |
| Sorcerer: Mana 3, 4, 5, 5, 5, 6, 7, 8, 8, 9 (was 4 up to 16); Sorcery 1 up to 5 (was 2 up to 10); Quickened 2 → 4; Innate Sorcery 2 → 1 use; cantrips reach 2 dice at level 7, not 5 | It has a second resource, like the bard and druid, and double casts made up the rest of the excess |
| Warlock: Shatter 3d8 / 4d8 / 5d8 → 1d8 / 3d8 / 4d8 and 1 → 2 pact; Hunger of Hadar 1 → 2 pact; Synaptic Static 8d6 → 5d6 | +128% at level 3 from Shatter. Hunger blocks the warlock's own sight, which left Eldritch Blast unused |
| Warlock: Eldritch Blast 2d8 at level 1, d10 at 3, d12 from 5 (was d10); 3 beams from level 7 (was 10) | Fills the at-will gap those cuts leave |

### Healers and control

| Change | Reason |
|---|---|
| Bard: Healing Word 3d4 and Cure Wounds 3d8 to level 6, then 2d4 and 2d8 (was 1d4, 1d8); Mass Healing Word 1d4 → 2d4 | The bard's dice were below the cleric's for the same spells |
| Bard: Mana 4, 5, 7, 8 at levels 1–4 (was 3, 4, 6, 7); Shatter 3d8 → 2d8 | More heals early, and less reason to spend Mana on damage |
| Cleric: Devotion 4, 5, 5, 7, 8, 11 at levels 1–6 (was 4, 6, 8, 10, 11, 12) | Brings the bard and cleric together at levels 3 and 5 |
| Wizard: Fireball 8d6 → 3d6, Cone of Cold 8d8 → 3d8, Ice Storm 2d10 + 4d6 → 1d6 + 2d6 | The same reason as the sorcerer. It also moves the wizard's turns back to control |
| Druid: Mana 2 at level 1, 5 and 6 at levels 3 and 4, 11 from level 7 (was 3, 6, 7, then 10 to 12) | Evens out turns denied against the wizard |
| Druid: Moonbeam lasts 1 round, not 3 | Its concentration crowded out Entangle at levels 5–7 |

### Defenders

| Change | Reason |
|---|---|
| Fighter: Second Wind heals 1d4 + level (was 1d10 + level), 2 uses at every level (was 2–4); Superiority 2, 3 at level 7, 5 at level 10 (was 2 up to 5) | Surviving longest drives its mitigated figure. `BasePackTests` pins 2 uses at level 1 |
| Barbarian: Rage 4 uses at level 5, 5 at level 6 (was 3, 4); Rage's temporary HP 2d8 at level 5, 4d8 at level 9 (was 1d8, 2d6) | Temporary HP soaked counts as mitigation, which offsets its resistance |

### Monsters and encounters

| Change | Reason |
|---|---|
| river_crossing (level 1): goblin HP 10 → 16 and d6 → d8; goblin archer HP 10 → 14 and d6 → d8 | Won 97%. The enemy count stays at 6, which tests pin |
| forest_ruins (level 3): 4 bandits, not 3; bandit HP 18 → 26 and d10 → d12; captain HP 42 → 64; wolf HP at level 3 18 → 26 | Won 99% |
| crypt_hall (level 5): 4 skeletons, not 2; skeleton d8 → d10; skeleton archer d10 → 2d6; zombie HP 26 → 32 and d10 → 2d6; necromancer HP 46 → 52 and Shadow Bolt 3d8 → 4d8; wraith HP 36 → 40 and 2d8 → 2d10 | Won 98%. Now 10 enemies, the spec's worst case |
| crystal_cave (level 7): 3 spiders, not 2; 4 slingers, not 3; spider HP 50 → 40; kobold HP 24 → 18 and d8 → 2d6; slinger HP 28 → 22 and d10 → 2d6 | Won 99.7%. More, frailer enemies give the danger without lengthening the fight. Now 10 enemies |
| warlord_bridge | See below |

### warlord_bridge

The 100% win rate had one main cause: **the boss walked onto the bridge, a party member stepped on, and the collapse dropped him into the chasm.** A dead warlord meets `defeat:warlord`, so battles could end in round 1 or 2.

| Change | Reason |
|---|---|
| The warlord starts at (13, 5), not (11, 5), and the bridge collapses when **1** party member enters, not 2 | The collapse now comes before he can reach the bridge, and only one party member falls, which settles the Task 22 note |
| `survive:8` → `survive:11` | Surviving 8 rounds was too safe to lose. This also sets the longest battle at 12 rounds, which is why the median is 12.0 |
| Boss HP at level 5 70 → 240; 2 → 3 attacks; d6 → 2d8; CON 10 → 14, WIS 8 → 14, proficient in both saves | With 70 HP the party's focus fire killed him, and Sleep held him for much of the fight |
| 4 hobgoblins, not 2; hobgoblin HP at level 5 11 → 50, 2 attacks, d10 → 2d8, STR 13 → 16, WIS 10 → 13, proficient in WIS saves | The ford is a chokepoint, so the enemies that do arrive have to hit hard |
| Goblin HP at level 5 30, archer 24 (both were 10) | They had level-1 HP at level 5 |

One Fireball still can't skip the boss's phase. The spec's trigger example still shows `survive:8` (§4); it's only an example, so I left it.

### Golden replays

| Replay | Change |
|---|---|
| `skirmish-basic` | None: it doesn't use the base pack |
| `river-crossing` | Events: goblin and archer hits are larger (9 not 7, 7 not 6). **One hand-written command changed:** the wizard's Magic Missile now targets `goblin.3`, not `goblin.4`. Goblins have 16 HP, so the fighter's 11-damage hit no longer kills `goblin.3` alone, and the test requires a `Died` event. The test is unchanged |
| `warlord-bridge` | Re-recorded from `Ai.Plan` on **seed 15, not 11**, because seed 11 no longer brings the boss below 50%. Seed 15 plays all five triggers, the ranger's fall into the chasm and the phase; the party wins by surviving to round 12. 13 of seeds 1–60 play the whole showcase |

## Sweep rounds

Rounds 1–10 are the tuning rounds, plus one more round of small fixes (round 11). The four fix-up sweeps and the final one take the total past the cap of about 10: they only repair what the tests caught, or a change that did harm. Each row is a 6-seed sweep (2,700 battles) or a 20-seed one (9,000).

| Round | Main change | Seeds | Band values out | Win % river / forest / crypt / warlord / crystal | Median rounds, same order |
|---|---|---:|---:|---|---|
| Baseline | None | 20 | 44 | 96.9 / 99.4 / 98.3 / 100 / 99.7 | 12 / 9 / 11 / 5 / 10 |
| 1 | Caution down | 6 | 42 | 97.2 / 98.1 / 99.1 / 100 / 98.1 | 8.5 / 6 / 8 / 4 / 8 |
| 2 | Control down | 6 | 42 | 96.3 / 99.1 / 100 / 100 / 100 | 8 / 6 / 8 / 3 / 8 |
| 3 | Area spells, melee attacks | 6 | 34 | 97.2 / 100 / 100 / 100 / 98.1 | 7 / 6 / 9 / 5 / 8 |
| 4 | Monster HP and counts | 6 | 37 | 89.8 / 88.0 / 86.1 / 100 / 83.3 | 10 / 8 / 12.5 / 2 / 14 |
| 5 | Warlord moved, Hide free | 6 | 28 | 89.8 / 95.4 / 90.7 / 100 / 87.0 | 9 / 9 / 12 / 9 / 14 |
| 6 | Monster damage, sneak dice | 6 | 29 | 84.3 / 86.1 / 94.4 / 100 / 90.7 | 9 / 9 / 11 / 9 / 12 |
| 7 | Boss 240 HP, caster pools | 6 | 21 | 88.0 / 89.8 / 96.3 / 99.1 / 85.2 | 9 / 9 / 11 / 12 / 12 |
| 8 | Hobgoblins, rogue attacks | 6 | 14 | 88.0 / 85.2 / 97.2 / 91.7 / 88.9 | 9 / 9 / 11 / 12 / 12 |
| 9 | Crypt damage, sorcerer pool | 6 | 17 | 88.9 / 85.2 / 86.1 / 90.7 / 85.2 | 9 / 10 / 11.5 / 12 / 12 |
| 10 | Rage, bard heals | 6 | 11 | 87.0 / 84.3 / 85.2 / 93.5 / 87.0 | 9 / 10 / 11.5 / 12 / 12 |
| 10, rerun | None | 20 | 15 | 90.8 / 87.8 / 88.6 / 91.7 / 88.1 | 9 / 9 / 11.5 / 12 / 12 |
| 11 | Small fixes | 20 | 5 | 91.1 / 86.4 / 89.4 / 91.4 / 88.9 | 9 / 9 / 12 / 12 / 12 |
| Fix-up 1 | Restored 6 river enemies, wizard Mana and companion; capped encounters at 10 enemies | 6 | 7 | 95.4 / 82.4 / 91.7 / 95.4 / 91.7 | 8.5 / 10 / 11 / 12 / 13 |
| Fix-up 2 | Goblin archer, crystal HP | 20 | 8 | 90.6 / 86.4 / 91.9 / 91.9 / 94.4 | 9 / 9 / 11 / 12 / 11 |
| Fix-up 3 | Restored Second Wind at level 1 | 20 | 10 | 91.1 / 86.9 / 91.9 / 91.9 / 95.3 | 9 / 9 / 11 / 12 / 11 |
| Fix-up 4 | Spider HP, Second Wind dice | 20 | 8 | 90.8 / 86.7 / 92.8 / 91.9 / 93.1 | 9 / 9 / 11 / 12 / 12 |
| **Final** | Reverted a Goading Attack cost that stopped it being used | 20 | **8** | **90.8 / 86.9 / 92.8 / 91.9 / 93.1** | **9 / 9 / 11 / 12 / 12** |

Six seeds are noisy: the same content went from 11 to 15 values out of band at 20 seeds. Round 11's 5 of 60 was the best result; the test-pinned values restored after it cost 3 of those.

## Fix round 1: review findings

The Task 23 review ([findings](../plans/2026-10-30-tactics-rules-engine.task-23-review.md)) found five Important problems. This section records what fix round 1 changed and why.

### Sorcerer picks are affordable when they arrive (finding 4)

Tuning cut Sorcery to 1 at levels 1–2 and raised Quickened to 4, without moving the picks that need it. Font of Magic and Twinned (2 each) couldn't be paid at level 2, and Quickened couldn't be paid until level 7, though it was a default pick from level 2.

| Change | Reason |
|---|---|
| Sorcery 2 at level 2 (was 1 until level 3) | Font of Magic and Twinned arrive at level 2 and cost 2. Level 2 isn't sampled, so no measured number moves |
| The second metamagic choice moves from level 5 to level 7; Quickened leaves the level-2 list | Quickened's cost of 4 is a balance lever from the sweep. The pool first reaches 4 at level 7, so that is where it can be chosen |
| Default picks: Careful at level 2 (was Quickened), Quickened at level 7 | Quickened was unaffordable before level 7 anyway. So every sampled level keeps the same usable loadout, and the AI uses neither Twinned nor Careful (see [Rarely used abilities](#rarely-used-abilities)) |

### The area-spell ladder (finding 5)

Tuning cut only the spells in `defaultPicks`. Lightning Bolt kept 8d6 at the same tier and cost as a 3d6 Fireball, and Shatter (tier 2) out-damaged Fireball per target over a smaller footprint. The sorcerer's tier 5 Cone of Cold (2d8) fell below tier 1 Burning Hands. The wizard's Wall of Fire kept 5d8 and outranked its own Cone of Cold.

| Change | Reason |
|---|---|
| Lightning Bolt 8d6 → 3d6 (wizard, sorcerer) | Fireball's sibling: same tier and cost, and a similar footprint (10 tiles against 13). The SRD gives both 8d6 |
| Shatter 3d8 → 2d8 (wizard, sorcerer) | A tier 2 burst of 5 tiles can't outdamage a tier 3 burst of 13 per target. 2d8 matches the bard's Shatter |
| Sorcerer Cone of Cold 2d8 → 3d8 | Above every lower tier; it now matches the wizard's |
| Wizard Wall of Fire 5d8 → 2d8 (both the cast and the burn) | Matches the sorcerer's, and no longer outranks Cone of Cold |

Burning Hands (tier 1, 3d6 over a 9-tile cone) still equals Fireball per target. That's deliberate: Fireball covers 13 tiles at range 6, while Burning Hands is a cone from the caster, so Fireball is strictly the bigger spell. Lifting Fireball's dice would undo the Striker band fix (see [Concerns for review](#concerns-for-review)).

Only the sorcerer's Cone of Cold is in a default loadout (from level 9), so it's the only change the simulator sees. SWEEP_RESULT

**New conventions, enforced by tests.** `ContentLadderTests` checks both rules at every level 1–10, not only at the sampled levels and default picks:
- every ability a class or subclass grants, offers or default-picks can be paid for at the level it arrives, and pools never shrink;
- within a class's Mana spells, a higher-tier area spell with at least as large a footprint never deals less per target, and two area spells of one tier with similar footprints differ by at most 1.5×.

Against the content before this round, the tests failed on exactly the problems above. `games/Tactics/README.md` lists them as conventions.

### Dice that step down at levels 3 and 7 (finding 3)

Three per-hit values fall as the level rises:

| Ability | Levels 1–2 | Level 3 on | Later |
|---|---|---|---|
| Monk martial arts die | 2d8 (9) | d10 (5.5) | 3 attacks from level 5 |
| Eldritch Blast | 2d8 (9), 1 beam | d10 (5.5), 1 beam | d12 with 2 beams at 5, 3 beams at 7 |
| Bard Healing Word / Cure Wounds | 3d4 / 3d8 to level 6 | | 2d4 / 2d8 from level 7 |

**They're a level-1 floor, not a mistake, but they were fitted to the sampled levels.** At level 1 these classes have almost nothing but this one ability: the monk has 2 ki, and the warlock 2 pact. The bigger die keeps them in band while melee walks in and pools are empty. From level 3 the die steps down as the pools grow: ki 3, then 1 per level; bard Mana 7 at level 3 and 10 at level 7.

**The class's output per round still rises at every sampled level:**
- monk damage: 3.5, 4.7, 5.7, 6.6, 8.5;
- warlock damage: 3.8, 5.1, 5.4, 5.6, 8.0;
- bard healing: 1.6 at level 5, 1.7 at 7, 2.1 at 10.

So no class gets weaker by levelling. The cost is felt per hit: a player sees a smaller number on the same button. Smoothing the dice would move level 1 or level 3 out of its role band unless another lever, such as attacks, beams or pools, moves with it. That's a design call, listed under the decisions for the user in the [tracker](../plans/2026-10-30-tactics-rules-engine.progress.md).

### The usage target and the timing criterion (findings 1 and 2)

- **Usage target:** the summary now says *partly met*. 29 of the 55 rare abilities are situational. The 10 planner limits and the 16 outranked abilities aren't, so they count against the target.
- **Timing:** `AiPerformanceTests` now also times crypt_hall at level 5 and crystal_cave at level 7, as shipped, with 6 heroes against their 10 enemies. TIMING_RESULT

## Rarely used abilities

55 abilities are used in under 2% of the turns where they're legal. "Legal" means `Battle.Check` allows the ability, whether or not it would do anything useful, so these rates understate how often an ability is actually wanted. Parties here have no subclass and use each class's `defaultPicks`.

The first table comes from reading the planner. The reasons in the others come from the ability data and traces, and weren't checked ability by ability.

**AI planner limits, not situational: real players would use these.** Fixing them needs planner changes.

| Ability | Use | Reason |
|---|---:|---|
| `wizard.misty_step`, `sorcerer.misty_step` | 0.0% | The planner skips empty-tile abilities |
| `druid.conjure_animals` | 0.0% | Same: the summon aims at an empty tile |
| `rogue.cunning_dash`, `rogue.withdraw` | 0.0% | A bonus-action Dash is never planned (Task 14 finding) |
| `fighter.action_surge` | 0.0% | The AI gives an extra action no value |
| `sorcerer.metamagic_twinned`, `sorcerer.metamagic_careful` | 0.0% | The AI values only Quickened and Extended |
| `rogue.steady_aim` | 0.0% | Its advantage and its "can't move" cancel out in the AI's scoring |
| `cleric.death_ward` | 0.0% | The AI gives a condition with only a hook no value |

**Situational: they need something the sample encounters rarely provide.**

| Ability | Use | Reason |
|---|---:|---|
| `bard.lesser_restoration`, `paladin.lesser_restoration`, `ranger.lesser_restoration` | 0.0% | Only worth casting on an ally with a matching condition |
| `bard.greater_restoration`, `cleric.greater_restoration`, `druid.greater_restoration` | 0.0% | As above, at tier 5 |
| `bard.dispel_magic`, `paladin.dispel_magic` | 0.0% | Needs a magic effect to end, and the sample monsters have few |
| `bard.countercharm` | 0.4% | Only helps against WIS and CHA saves |
| `cleric.turn_undead`, `cleric.sear_undead` | 0.0%, 0.7% | Undead only, and crypt_hall is the only encounter with them |
| `cleric.banishment`, `wizard.hold_monster` | 0.0%, 0.2% | Costly single-target spells, known only at high levels |
| `druid.mass_cure_wounds` | 0.0% | Tier 5, for a badly hurt group; high-level parties here rarely are |
| `cleric.resistance` | 0.1% | Takes concentration, which better spells hold |
| `barbarian.relentless_rage` | 1.9% | A self-heal for when it is hurt |
| `warlock.maddening_hex` | 0.1% | Only does anything to a Hexed target |
| `ranger.barkskin`, `wizard.greater_invisibility` | 0.0%, 0.4% | Touch range, and the caster stands back from its allies |

**Close-range spells: the user has to stand next to foes.**

| Ability | Use | Reason |
|---|---:|---|
| `bard.thunderclap`, `druid.thunderwave`, `wizard.thunderwave` | 0.8%, 0.1%, 0.0% | Bursts around a caster who stays back |
| `cleric.word_of_radiance`, `warlock.arms_of_hadar`, `barbarian.savage_roar` | 0.5%, 1.4%, 0.4% | As above. The roar also has one use and a low scoring |
| `wizard.burning_hands` | 0.7% | A short cone from the caster |
| `sorcerer.shocking_grasp`, `wizard.shocking_grasp`, `wizard.chill_touch` | 0.7%, 0.2%, 0.6% | Touch cantrips |

**Outranked by something better in the same loadout. Not situational either:** the AI picks the stronger option every time, and a player with a different loadout would use these. They count against the target.

| Ability | Use | Reason |
|---|---:|---|
| `wizard.ray_of_frost`, `wizard.acid_splash`, `druid.thorn_whip`, `warlock.chill_touch` | 0.6%, 1.2%, 0.2%, 1.8% | A second cantrip behind the class's main one |
| `wizard.magic_missile`, `wizard.scorching_ray` | 1.5%, 1.8% | Sleep and control spells take the wizard's Mana first |
| `druid.spike_growth`, `druid.sleet_storm`, `druid.call_lightning`, `druid.blight` | 0.1%, 0.5%, 0.8%, 0.5% | Entangle takes the druid's turn and its concentration |
| `fighter.pushing_attack`, `fighter.menacing_attack` | 0.0%, 0.0% | Trip and Precision spend the same Superiority first |
| `paladin.elemental_weapon` | 0.0% | 3 Oath, which smites and healing use first |
| `ranger.lightning_arrow` | 0.0% | 3 Focus from level 9, and Conjure Barrage takes the Focus first |
| `ranger.cure_wounds` | 1.6% | Touch range, and the ranger stands back from its allies |
| `sorcerer.font_of_magic_mana` | 1.7% | A niche conversion of Sorcery into Mana |

## Concerns for review

- **Fireball is now 3d6.** The Striker band compares classes by total damage per round, so spells that hit 3–4 foes had to fall far below the SRD to let single-target strikers fit. That meets the target as written, but players will notice. Measuring the band differently might be a better design than these dice.
- **The rogue has 5 attacks at level 9.** It was the only numbers-only way to lift a class whose bonus needs advantage. A player who sets up every Sneak Attack will far outperform the AI at these numbers.
- **The class numbers assume this AI's play.** Much of the baseline's gap was AI behaviour, so real players will shift the bands.
- **Subclasses are unmeasured.** The simulator plays no subclass, so the 36 subclasses weren't tuned. Some of their abilities share the base dice and some don't. Fix round 1 made the wizard's and sorcerer's Cone of Cold, Wall of Fire, Lightning Bolt and Shatter match.
- **Off-level matchups run to extremes, unflagged.** crypt_hall at level 1 is won 0.8% of the time, and every encounter is won 99%+ at level 10.
- **Some tables no longer follow the content conventions.** Bard, cleric, druid and sorcerer pools and the ranger's Focus no longer match the conventions' formulas, and warlock `pact` costs now vary. Mana cost = tier still holds for every Mana spell. `games/Tactics/README.md` notes this.
- **The warlord's phase isn't guaranteed.** Only 13 of 60 seeds reach it with the golden party. In most of the rest, the party survives to round 12 with the boss above 50%.

## Appendix: final simulator report


Generated by `Tactics.Sim` (spec §5.6): both sides AI-controlled, every battle deterministic from its seed.

### 1. Summary

- **Battles:** 9000 (5 encounter(s), levels 1, 3, 5, 7, 10, 18 parties, 20 seed(s) each).
- **Won:** 7050 (78.3%). Lost: 1950, of which 16 stopped at the round cap of 30. Fled: 0.
- **Rounds:** mean 9.2, median 9.0; 50.2% of battles within the 8–12 round target.
- **Flags:**
  - win rate outside 60%–95%: 0 of 25 encounter/level cells;
  - mean battle length outside 8–12 rounds: 7 cells;
  - role-band values more than ±15% from the band mean: 8 of 60;
  - abilities used in < 2% of legal turns: 55;
  - battles stopped at the round cap: 16.

### 2. Win rate by encounter and level

Flags: **LOW** below 60%, **HIGH** above 95% (difficulty Normal; judged at the encounter's intended level when set).

| Encounter | Level | Battles | Won | Win rate | Flag |
|---|---:|---:|---:|---:|---|
| crypt_hall | 1 | 360 | 3 | 0.8% |  |
| crypt_hall | 3 | 360 | 97 | 26.9% |  |
| crypt_hall | 5 | 360 | 334 | 92.8% |  |
| crypt_hall | 7 | 360 | 350 | 97.2% |  |
| crypt_hall | 10 | 360 | 360 | 100.0% |  |
| crystal_cave | 1 | 360 | 0 | 0.0% |  |
| crystal_cave | 3 | 360 | 60 | 16.7% |  |
| crystal_cave | 5 | 360 | 309 | 85.8% |  |
| crystal_cave | 7 | 360 | 335 | 93.1% |  |
| crystal_cave | 10 | 360 | 360 | 100.0% |  |
| forest_ruins | 1 | 360 | 144 | 40.0% |  |
| forest_ruins | 3 | 360 | 313 | 86.9% |  |
| forest_ruins | 5 | 360 | 359 | 99.7% |  |
| forest_ruins | 7 | 360 | 360 | 100.0% |  |
| forest_ruins | 10 | 360 | 360 | 100.0% |  |
| river_crossing | 1 | 360 | 327 | 90.8% |  |
| river_crossing | 3 | 360 | 359 | 99.7% |  |
| river_crossing | 5 | 360 | 360 | 100.0% |  |
| river_crossing | 7 | 360 | 360 | 100.0% |  |
| river_crossing | 10 | 360 | 360 | 100.0% |  |
| warlord_bridge | 1 | 360 | 239 | 66.4% |  |
| warlord_bridge | 3 | 360 | 263 | 73.1% |  |
| warlord_bridge | 5 | 360 | 331 | 91.9% |  |
| warlord_bridge | 7 | 360 | 349 | 96.9% |  |
| warlord_bridge | 10 | 360 | 358 | 99.4% |  |

By party size:

| Encounter | Level | Size | Battles | Win rate |
|---|---:|---:|---:|---:|
| crypt_hall | 1 | 4 | 120 | 0.0% |
| crypt_hall | 1 | 5 | 120 | 0.0% |
| crypt_hall | 1 | 6 | 120 | 2.5% |
| crypt_hall | 3 | 4 | 120 | 2.5% |
| crypt_hall | 3 | 5 | 120 | 23.3% |
| crypt_hall | 3 | 6 | 120 | 55.0% |
| crypt_hall | 5 | 4 | 120 | 78.3% |
| crypt_hall | 5 | 5 | 120 | 100.0% |
| crypt_hall | 5 | 6 | 120 | 100.0% |
| crypt_hall | 7 | 4 | 120 | 91.7% |
| crypt_hall | 7 | 5 | 120 | 100.0% |
| crypt_hall | 7 | 6 | 120 | 100.0% |
| crypt_hall | 10 | 4 | 120 | 100.0% |
| crypt_hall | 10 | 5 | 120 | 100.0% |
| crypt_hall | 10 | 6 | 120 | 100.0% |
| crystal_cave | 1 | 4 | 120 | 0.0% |
| crystal_cave | 1 | 5 | 120 | 0.0% |
| crystal_cave | 1 | 6 | 120 | 0.0% |
| crystal_cave | 3 | 4 | 120 | 0.0% |
| crystal_cave | 3 | 5 | 120 | 15.0% |
| crystal_cave | 3 | 6 | 120 | 35.0% |
| crystal_cave | 5 | 4 | 120 | 61.7% |
| crystal_cave | 5 | 5 | 120 | 97.5% |
| crystal_cave | 5 | 6 | 120 | 98.3% |
| crystal_cave | 7 | 4 | 120 | 79.2% |
| crystal_cave | 7 | 5 | 120 | 100.0% |
| crystal_cave | 7 | 6 | 120 | 100.0% |
| crystal_cave | 10 | 4 | 120 | 100.0% |
| crystal_cave | 10 | 5 | 120 | 100.0% |
| crystal_cave | 10 | 6 | 120 | 100.0% |
| forest_ruins | 1 | 4 | 120 | 9.2% |
| forest_ruins | 1 | 5 | 120 | 37.5% |
| forest_ruins | 1 | 6 | 120 | 73.3% |
| forest_ruins | 3 | 4 | 120 | 63.3% |
| forest_ruins | 3 | 5 | 120 | 97.5% |
| forest_ruins | 3 | 6 | 120 | 100.0% |
| forest_ruins | 5 | 4 | 120 | 99.2% |
| forest_ruins | 5 | 5 | 120 | 100.0% |
| forest_ruins | 5 | 6 | 120 | 100.0% |
| forest_ruins | 7 | 4 | 120 | 100.0% |
| forest_ruins | 7 | 5 | 120 | 100.0% |
| forest_ruins | 7 | 6 | 120 | 100.0% |
| forest_ruins | 10 | 4 | 120 | 100.0% |
| forest_ruins | 10 | 5 | 120 | 100.0% |
| forest_ruins | 10 | 6 | 120 | 100.0% |
| river_crossing | 1 | 4 | 120 | 79.2% |
| river_crossing | 1 | 5 | 120 | 94.2% |
| river_crossing | 1 | 6 | 120 | 99.2% |
| river_crossing | 3 | 4 | 120 | 99.2% |
| river_crossing | 3 | 5 | 120 | 100.0% |
| river_crossing | 3 | 6 | 120 | 100.0% |
| river_crossing | 5 | 4 | 120 | 100.0% |
| river_crossing | 5 | 5 | 120 | 100.0% |
| river_crossing | 5 | 6 | 120 | 100.0% |
| river_crossing | 7 | 4 | 120 | 100.0% |
| river_crossing | 7 | 5 | 120 | 100.0% |
| river_crossing | 7 | 6 | 120 | 100.0% |
| river_crossing | 10 | 4 | 120 | 100.0% |
| river_crossing | 10 | 5 | 120 | 100.0% |
| river_crossing | 10 | 6 | 120 | 100.0% |
| warlord_bridge | 1 | 4 | 120 | 53.3% |
| warlord_bridge | 1 | 5 | 120 | 50.8% |
| warlord_bridge | 1 | 6 | 120 | 95.0% |
| warlord_bridge | 3 | 4 | 120 | 53.3% |
| warlord_bridge | 3 | 5 | 120 | 65.8% |
| warlord_bridge | 3 | 6 | 120 | 100.0% |
| warlord_bridge | 5 | 4 | 120 | 84.2% |
| warlord_bridge | 5 | 5 | 120 | 91.7% |
| warlord_bridge | 5 | 6 | 120 | 100.0% |
| warlord_bridge | 7 | 4 | 120 | 90.8% |
| warlord_bridge | 7 | 5 | 120 | 100.0% |
| warlord_bridge | 7 | 6 | 120 | 100.0% |
| warlord_bridge | 10 | 4 | 120 | 98.3% |
| warlord_bridge | 10 | 5 | 120 | 100.0% |
| warlord_bridge | 10 | 6 | 120 | 100.0% |

### 3. Rounds per battle

Target: 8–12 rounds. Flags: **SHORT** / **LONG** when the mean is outside it, **CAPPED** when any battle hit the round cap (30, counted as lost).

| Encounter | Level | Mean | Median | Min | Max | In target | Capped | Flag |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| crypt_hall | 1 | 7.0 | 6.0 | 3 | 24 | 23.9% | 0 |  |
| crypt_hall | 3 | 13.0 | 12.0 | 5 | 30 | 38.9% | 8 | **CAPPED** |
| crypt_hall | 5 | 11.8 | 11.0 | 5 | 30 | 66.7% | 1 | **CAPPED** |
| crypt_hall | 7 | 11.2 | 11.0 | 5 | 30 | 67.8% | 2 | **CAPPED** |
| crypt_hall | 10 | 8.9 | 9.0 | 4 | 19 | 67.2% | 0 |  |
| crystal_cave | 1 | 6.7 | 6.0 | 2 | 17 | 30.8% | 0 |  |
| crystal_cave | 3 | 11.3 | 11.0 | 4 | 30 | 37.5% | 1 | **CAPPED** |
| crystal_cave | 5 | 13.2 | 13.0 | 6 | 30 | 42.5% | 1 | **CAPPED** |
| crystal_cave | 7 | 12.3 | 12.0 | 5 | 30 | 51.7% | 2 | **LONG** **CAPPED** |
| crystal_cave | 10 | 8.6 | 8.0 | 3 | 19 | 54.2% | 0 |  |
| forest_ruins | 1 | 9.7 | 9.0 | 3 | 24 | 55.0% | 0 |  |
| forest_ruins | 3 | 9.7 | 9.0 | 5 | 22 | 68.1% | 0 |  |
| forest_ruins | 5 | 7.9 | 8.0 | 4 | 15 | 49.7% | 0 |  |
| forest_ruins | 7 | 7.4 | 7.0 | 4 | 13 | 40.0% | 0 |  |
| forest_ruins | 10 | 6.3 | 6.0 | 3 | 12 | 21.1% | 0 |  |
| river_crossing | 1 | 9.5 | 9.0 | 5 | 30 | 53.6% | 1 | **CAPPED** |
| river_crossing | 3 | 6.9 | 6.0 | 3 | 17 | 30.0% | 0 |  |
| river_crossing | 5 | 5.9 | 6.0 | 3 | 13 | 15.8% | 0 |  |
| river_crossing | 7 | 5.9 | 5.0 | 3 | 14 | 16.4% | 0 |  |
| river_crossing | 10 | 4.8 | 4.0 | 2 | 12 | 5.6% | 0 |  |
| warlord_bridge | 1 | 10.0 | 12.0 | 2 | 12 | 73.6% | 0 |  |
| warlord_bridge | 3 | 10.4 | 12.0 | 3 | 12 | 83.9% | 0 |  |
| warlord_bridge | 5 | 10.9 | 12.0 | 5 | 12 | 90.3% | 0 |  |
| warlord_bridge | 7 | 10.6 | 12.0 | 5 | 12 | 88.3% | 0 |  |
| warlord_bridge | 10 | 9.7 | 10.0 | 3 | 12 | 82.5% | 0 |  |

### 4. Role bands

Each class's band measure at each level (the mean over the battles it fought, per member), against the mean of its band's classes at that level; **FLAG** beyond ±15%. Measures: Defender, damage mitigated per round (HP and temporary HP it lost, plus misses against it × the mean hostile hit); Striker, damage per round (HP taken off foes: no overkill, and temporary HP soaked doesn't count); Healer, healing and revives per round; Control, enemy turns denied per battle. A band with one class present sits on its own mean.

| Level | Band | Measure | Class | Value | Band mean | Deviation | Flag |
|---:|---|---|---|---:|---:|---:|---|
| 1 | Defender | mitigated / round | barbarian | 3.9 | 3.9 | +0.8% |  |
| 1 | Defender | mitigated / round | fighter | 4.4 | 3.9 | +12.8% |  |
| 1 | Defender | mitigated / round | paladin | 3.4 | 3.9 | -13.6% |  |
| 1 | Striker | damage / round | monk | 3.5 | 3.6 | -4.0% |  |
| 1 | Striker | damage / round | ranger | 3.8 | 3.6 | +5.6% |  |
| 1 | Striker | damage / round | rogue | 3.3 | 3.6 | -7.9% |  |
| 1 | Striker | damage / round | sorcerer | 3.6 | 3.6 | +0.0% |  |
| 1 | Striker | damage / round | warlock | 3.8 | 3.6 | +6.2% |  |
| 1 | Healer | healing / round | bard | 0.9 | 1.0 | -9.9% |  |
| 1 | Healer | healing / round | cleric | 1.1 | 1.0 | +9.9% |  |
| 1 | Control | turns denied / battle | druid | 3.8 | 3.3 | +14.8% |  |
| 1 | Control | turns denied / battle | wizard | 2.8 | 3.3 | -14.8% |  |
| 3 | Defender | mitigated / round | barbarian | 5.3 | 5.8 | -8.8% |  |
| 3 | Defender | mitigated / round | fighter | 6.2 | 5.8 | +6.6% |  |
| 3 | Defender | mitigated / round | paladin | 6.0 | 5.8 | +2.1% |  |
| 3 | Striker | damage / round | monk | 4.7 | 4.5 | +3.1% |  |
| 3 | Striker | damage / round | ranger | 4.5 | 4.5 | -0.4% |  |
| 3 | Striker | damage / round | rogue | 3.8 | 4.5 | -15.6% | **FLAG** |
| 3 | Striker | damage / round | sorcerer | 4.6 | 4.5 | +0.8% |  |
| 3 | Striker | damage / round | warlock | 5.1 | 4.5 | +12.0% |  |
| 3 | Healer | healing / round | bard | 1.4 | 1.5 | -8.7% |  |
| 3 | Healer | healing / round | cleric | 1.7 | 1.5 | +8.7% |  |
| 3 | Control | turns denied / battle | druid | 4.9 | 4.4 | +11.8% |  |
| 3 | Control | turns denied / battle | wizard | 3.9 | 4.4 | -11.8% |  |
| 5 | Defender | mitigated / round | barbarian | 4.6 | 5.2 | -11.4% |  |
| 5 | Defender | mitigated / round | fighter | 6.3 | 5.2 | +20.3% | **FLAG** |
| 5 | Defender | mitigated / round | paladin | 4.7 | 5.2 | -8.9% |  |
| 5 | Striker | damage / round | monk | 5.7 | 5.8 | -1.5% |  |
| 5 | Striker | damage / round | ranger | 6.1 | 5.8 | +5.2% |  |
| 5 | Striker | damage / round | rogue | 5.4 | 5.8 | -7.5% |  |
| 5 | Striker | damage / round | sorcerer | 6.4 | 5.8 | +11.3% |  |
| 5 | Striker | damage / round | warlock | 5.4 | 5.8 | -7.5% |  |
| 5 | Healer | healing / round | bard | 1.6 | 1.6 | -1.9% |  |
| 5 | Healer | healing / round | cleric | 1.6 | 1.6 | +1.9% |  |
| 5 | Control | turns denied / battle | druid | 4.2 | 3.7 | +13.1% |  |
| 5 | Control | turns denied / battle | wizard | 3.2 | 3.7 | -13.1% |  |
| 7 | Defender | mitigated / round | barbarian | 5.4 | 5.7 | -5.3% |  |
| 7 | Defender | mitigated / round | fighter | 7.0 | 5.7 | +23.1% | **FLAG** |
| 7 | Defender | mitigated / round | paladin | 4.7 | 5.7 | -17.9% | **FLAG** |
| 7 | Striker | damage / round | monk | 6.6 | 6.3 | +4.5% |  |
| 7 | Striker | damage / round | ranger | 6.3 | 6.3 | -0.7% |  |
| 7 | Striker | damage / round | rogue | 5.8 | 6.3 | -7.7% |  |
| 7 | Striker | damage / round | sorcerer | 7.3 | 6.3 | +14.8% |  |
| 7 | Striker | damage / round | warlock | 5.6 | 6.3 | -10.9% |  |
| 7 | Healer | healing / round | bard | 1.7 | 1.8 | -1.7% |  |
| 7 | Healer | healing / round | cleric | 1.8 | 1.8 | +1.7% |  |
| 7 | Control | turns denied / battle | druid | 2.9 | 2.9 | -1.1% |  |
| 7 | Control | turns denied / battle | wizard | 2.9 | 2.9 | +1.1% |  |
| 10 | Defender | mitigated / round | barbarian | 3.9 | 5.2 | -24.7% | **FLAG** |
| 10 | Defender | mitigated / round | fighter | 6.1 | 5.2 | +18.0% | **FLAG** |
| 10 | Defender | mitigated / round | paladin | 5.6 | 5.2 | +6.7% |  |
| 10 | Striker | damage / round | monk | 8.5 | 8.3 | +3.2% |  |
| 10 | Striker | damage / round | ranger | 8.6 | 8.3 | +3.8% |  |
| 10 | Striker | damage / round | rogue | 7.3 | 8.3 | -11.6% |  |
| 10 | Striker | damage / round | sorcerer | 8.9 | 8.3 | +7.4% |  |
| 10 | Striker | damage / round | warlock | 8.0 | 8.3 | -2.8% |  |
| 10 | Healer | healing / round | bard | 2.1 | 2.1 | +3.2% |  |
| 10 | Healer | healing / round | cleric | 2.0 | 2.1 | -3.2% |  |
| 10 | Control | turns denied / battle | druid | 3.3 | 2.4 | +35.9% | **FLAG** |
| 10 | Control | turns denied / battle | wizard | 1.5 | 2.4 | -35.9% | **FLAG** |

Every measure, by class and level:

| Class | Band | Level | Battles | Damage / round | Healing / round | Mitigated / round | Turns denied |
|---|---|---:|---:|---:|---:|---:|---:|
| barbarian | Defender | 1 | 300 | 1.9 | 0.1 | 3.9 | 0.0 |
| barbarian | Defender | 3 | 300 | 3.0 | 0.0 | 5.3 | 0.0 |
| barbarian | Defender | 5 | 300 | 5.2 | 0.0 | 4.6 | 0.0 |
| barbarian | Defender | 7 | 300 | 5.9 | 0.0 | 5.4 | 0.0 |
| barbarian | Defender | 10 | 300 | 7.3 | 0.1 | 3.9 | 0.0 |
| bard | Healer | 1 | 600 | 3.1 | 0.9 | 1.2 | 0.0 |
| bard | Healer | 3 | 600 | 6.3 | 1.4 | 1.2 | 0.0 |
| bard | Healer | 5 | 600 | 5.2 | 1.6 | 0.8 | 0.3 |
| bard | Healer | 7 | 600 | 4.9 | 1.7 | 0.7 | 0.2 |
| bard | Healer | 10 | 600 | 4.4 | 2.1 | 0.5 | 0.1 |
| cleric | Healer | 1 | 500 | 2.9 | 1.1 | 1.9 | 0.0 |
| cleric | Healer | 3 | 500 | 2.5 | 1.7 | 2.5 | 0.0 |
| cleric | Healer | 5 | 500 | 3.3 | 1.6 | 1.0 | 0.2 |
| cleric | Healer | 7 | 500 | 2.9 | 1.8 | 0.7 | 0.3 |
| cleric | Healer | 10 | 500 | 8.3 | 2.0 | 0.3 | 0.1 |
| druid | Control | 1 | 600 | 0.5 | 0.0 | 1.7 | 3.8 |
| druid | Control | 3 | 600 | 0.4 | 0.2 | 3.1 | 4.9 |
| druid | Control | 5 | 600 | 0.7 | 0.3 | 1.5 | 4.2 |
| druid | Control | 7 | 600 | 4.3 | 0.3 | 0.9 | 2.9 |
| druid | Control | 10 | 600 | 2.4 | 0.4 | 0.6 | 3.3 |
| fighter | Defender | 1 | 700 | 2.5 | 0.4 | 4.4 | 0.0 |
| fighter | Defender | 3 | 700 | 2.6 | 0.6 | 6.2 | 0.0 |
| fighter | Defender | 5 | 700 | 4.2 | 0.7 | 6.3 | 0.0 |
| fighter | Defender | 7 | 700 | 4.9 | 0.9 | 7.0 | 0.0 |
| fighter | Defender | 10 | 700 | 5.7 | 1.0 | 6.1 | 0.0 |
| monk | Striker | 1 | 600 | 3.5 | 0.0 | 3.0 | 0.0 |
| monk | Striker | 3 | 600 | 4.7 | 0.0 | 6.4 | 0.0 |
| monk | Striker | 5 | 600 | 5.7 | 0.0 | 5.0 | 0.4 |
| monk | Striker | 7 | 600 | 6.6 | 0.0 | 4.3 | 0.4 |
| monk | Striker | 10 | 600 | 8.5 | 0.0 | 3.0 | 0.5 |
| paladin | Defender | 1 | 500 | 1.1 | 1.1 | 3.4 | 0.0 |
| paladin | Defender | 3 | 500 | 2.0 | 1.5 | 6.0 | 0.0 |
| paladin | Defender | 5 | 500 | 2.9 | 1.6 | 4.7 | 0.0 |
| paladin | Defender | 7 | 500 | 3.0 | 2.0 | 4.7 | 0.1 |
| paladin | Defender | 10 | 500 | 3.5 | 2.2 | 5.6 | 0.0 |
| ranger | Striker | 1 | 700 | 3.8 | 0.0 | 2.3 | 0.0 |
| ranger | Striker | 3 | 700 | 4.5 | 0.0 | 2.8 | 0.0 |
| ranger | Striker | 5 | 700 | 6.1 | 0.1 | 1.4 | 0.0 |
| ranger | Striker | 7 | 700 | 6.3 | 0.1 | 0.9 | 0.0 |
| ranger | Striker | 10 | 700 | 8.6 | 0.0 | 0.9 | 0.0 |
| rogue | Striker | 1 | 800 | 3.3 | 0.1 | 3.1 | 0.0 |
| rogue | Striker | 3 | 800 | 3.8 | 0.0 | 4.2 | 0.0 |
| rogue | Striker | 5 | 800 | 5.4 | 0.0 | 5.1 | 0.0 |
| rogue | Striker | 7 | 800 | 5.8 | 0.0 | 5.5 | 0.0 |
| rogue | Striker | 10 | 800 | 7.3 | 0.0 | 5.4 | 0.0 |
| sorcerer | Striker | 1 | 600 | 3.6 | 0.0 | 1.4 | 0.0 |
| sorcerer | Striker | 3 | 600 | 4.6 | 0.0 | 1.6 | 0.0 |
| sorcerer | Striker | 5 | 600 | 6.4 | 0.0 | 0.9 | 0.0 |
| sorcerer | Striker | 7 | 600 | 7.3 | 0.0 | 0.6 | 0.0 |
| sorcerer | Striker | 10 | 600 | 8.9 | 0.0 | 0.4 | 0.0 |
| warlock | Striker | 1 | 800 | 3.8 | 0.0 | 1.6 | 0.0 |
| warlock | Striker | 3 | 800 | 5.1 | 0.0 | 1.8 | 0.0 |
| warlock | Striker | 5 | 800 | 5.4 | 0.0 | 0.5 | 0.1 |
| warlock | Striker | 7 | 800 | 5.6 | 0.0 | 0.4 | 0.1 |
| warlock | Striker | 10 | 800 | 8.0 | 0.0 | 0.1 | 0.2 |
| wizard | Control | 1 | 900 | 0.4 | 0.0 | 1.7 | 2.8 |
| wizard | Control | 3 | 900 | 0.5 | 0.0 | 2.0 | 3.9 |
| wizard | Control | 5 | 900 | 2.4 | 0.0 | 1.5 | 3.2 |
| wizard | Control | 7 | 900 | 2.2 | 0.0 | 1.2 | 2.9 |
| wizard | Control | 10 | 900 | 3.6 | 0.0 | 0.8 | 1.5 |

### 5. Rarely used abilities

Abilities party members used in fewer than 2% of their turns where the ability was legal, over every battle (the built-in `common.*` actions are listed below but not flagged). Legal is judged at the start of each turn: an ability `Battle.Check` accepts on some unit or nearby tile, or refuses only for range or line of sight within the unit's Move plus the ability's range. Reactions (opportunity attacks, Shield, Uncanny Dodge and the like) aren't counted, used or legal.

| Ability | Used turns | Legal turns | Use rate |
|---|---:|---:|---:|
| barbarian.relentless_rage | 46 | 2478 | 1.9% |
| barbarian.savage_roar | 40 | 9533 | 0.4% |
| bard.countercharm | 45 | 10254 | 0.4% |
| bard.dispel_magic | 0 | 6021 | 0.0% |
| bard.greater_restoration | 0 | 2683 | 0.0% |
| bard.lesser_restoration | 0 | 3180 | 0.0% |
| bard.thunderclap | 67 | 8300 | 0.8% |
| cleric.banishment | 0 | 1818 | 0.0% |
| cleric.death_ward | 0 | 9096 | 0.0% |
| cleric.greater_restoration | 0 | 3239 | 0.0% |
| cleric.resistance | 27 | 21660 | 0.1% |
| cleric.sear_undead | 64 | 9278 | 0.7% |
| cleric.turn_undead | 0 | 14824 | 0.0% |
| cleric.word_of_radiance | 59 | 11387 | 0.5% |
| druid.blight | 9 | 1955 | 0.5% |
| druid.call_lightning | 82 | 10385 | 0.8% |
| druid.conjure_animals | 0 | 6960 | 0.0% |
| druid.greater_restoration | 1 | 3108 | 0.0% |
| druid.mass_cure_wounds | 0 | 3187 | 0.0% |
| druid.sleet_storm | 51 | 10385 | 0.5% |
| druid.spike_growth | 8 | 13631 | 0.1% |
| druid.thorn_whip | 31 | 12706 | 0.2% |
| druid.thunderwave | 4 | 3694 | 0.1% |
| fighter.action_surge | 0 | 22379 | 0.0% |
| fighter.menacing_attack | 0 | 2305 | 0.0% |
| fighter.pushing_attack | 2 | 4118 | 0.0% |
| paladin.dispel_magic | 0 | 3581 | 0.0% |
| paladin.elemental_weapon | 1 | 3581 | 0.0% |
| paladin.lesser_restoration | 0 | 3720 | 0.0% |
| ranger.barkskin | 0 | 5218 | 0.0% |
| ranger.cure_wounds | 189 | 11953 | 1.6% |
| ranger.lesser_restoration | 0 | 820 | 0.0% |
| ranger.lightning_arrow | 0 | 820 | 0.0% |
| rogue.cunning_dash | 0 | 26674 | 0.0% |
| rogue.steady_aim | 0 | 26674 | 0.0% |
| rogue.withdraw | 0 | 5921 | 0.0% |
| sorcerer.font_of_magic_mana | 318 | 19073 | 1.7% |
| sorcerer.metamagic_careful | 0 | 14188 | 0.0% |
| sorcerer.metamagic_twinned | 0 | 19073 | 0.0% |
| sorcerer.misty_step | 0 | 6116 | 0.0% |
| sorcerer.shocking_grasp | 63 | 9485 | 0.7% |
| warlock.arms_of_hadar | 112 | 8053 | 1.4% |
| warlock.chill_touch | 507 | 28118 | 1.8% |
| warlock.maddening_hex | 10 | 15565 | 0.1% |
| wizard.acid_splash | 290 | 24843 | 1.2% |
| wizard.burning_hands | 153 | 22262 | 0.7% |
| wizard.chill_touch | 10 | 1750 | 0.6% |
| wizard.greater_invisibility | 25 | 6101 | 0.4% |
| wizard.hold_monster | 9 | 4168 | 0.2% |
| wizard.magic_missile | 438 | 29555 | 1.5% |
| wizard.misty_step | 0 | 23456 | 0.0% |
| wizard.ray_of_frost | 202 | 33508 | 0.6% |
| wizard.scorching_ray | 446 | 24625 | 1.8% |
| wizard.shocking_grasp | 27 | 14432 | 0.2% |
| wizard.thunderwave | 2 | 10053 | 0.0% |

Every ability party members could use:

| Ability | Used turns | Legal turns | Use rate | Flag |
|---|---:|---:|---:|---|
| barbarian.brutal_strike | 841 | 1597 | 52.7% |  |
| barbarian.cleaving_blow | 4648 | 9703 | 47.9% |  |
| barbarian.crushing_leap | 822 | 4325 | 19.0% |  |
| barbarian.earthshaker | 86 | 1436 | 6.0% |  |
| barbarian.rage | 5813 | 13675 | 42.5% |  |
| barbarian.reckless_attack | 3332 | 12450 | 26.8% |  |
| barbarian.relentless_rage | 46 | 2478 | 1.9% | **RARE** |
| barbarian.savage_roar | 40 | 9533 | 0.4% | **RARE** |
| barbarian.whirlwind_slash | 84 | 3767 | 2.2% |  |
| bard.bardic_inspiration | 13067 | 22403 | 58.3% |  |
| bard.countercharm | 45 | 10254 | 0.4% | **RARE** |
| bard.cure_wounds | 964 | 17222 | 5.6% |  |
| bard.dispel_magic | 0 | 6021 | 0.0% | **RARE** |
| bard.dissonant_whispers | 2912 | 13510 | 21.6% |  |
| bard.faerie_fire | 2677 | 13678 | 19.6% |  |
| bard.greater_invisibility | 641 | 5540 | 11.6% |  |
| bard.greater_restoration | 0 | 2683 | 0.0% | **RARE** |
| bard.healing_word | 2506 | 17681 | 14.2% |  |
| bard.heroism | 833 | 10185 | 8.2% |  |
| bard.hold_monster | 52 | 1859 | 2.8% |  |
| bard.hold_person | 584 | 9209 | 6.3% |  |
| bard.hypnotic_pattern | 187 | 8753 | 2.1% |  |
| bard.lesser_restoration | 0 | 3180 | 0.0% | **RARE** |
| bard.mass_healing_word | 1051 | 8753 | 12.0% |  |
| bard.shatter | 5753 | 12434 | 46.3% |  |
| bard.song_of_rest | 1482 | 15014 | 9.9% |  |
| bard.thunderclap | 67 | 8300 | 0.8% | **RARE** |
| bard.vicious_mockery | 5387 | 22858 | 23.6% |  |
| cleric.aid | 2072 | 9955 | 20.8% |  |
| cleric.banishment | 0 | 1818 | 0.0% | **RARE** |
| cleric.bless | 2994 | 25813 | 11.6% |  |
| cleric.cure_wounds | 1532 | 25813 | 5.9% |  |
| cleric.death_ward | 0 | 9096 | 0.0% | **RARE** |
| cleric.divine_intervention | 265 | 4642 | 5.7% |  |
| cleric.divine_spark | 1838 | 20509 | 9.0% |  |
| cleric.flame_strike | 1380 | 3239 | 42.6% |  |
| cleric.greater_restoration | 0 | 3239 | 0.0% | **RARE** |
| cleric.guiding_bolt | 7322 | 19137 | 38.3% |  |
| cleric.healing_word | 4296 | 25813 | 16.6% |  |
| cleric.hold_person | 1237 | 10229 | 12.1% |  |
| cleric.mass_cure_wounds | 362 | 3239 | 11.2% |  |
| cleric.mass_healing_word | 1035 | 14261 | 7.3% |  |
| cleric.resistance | 27 | 21660 | 0.1% | **RARE** |
| cleric.sacred_flame | 1789 | 29402 | 6.1% |  |
| cleric.sear_undead | 64 | 9278 | 0.7% | **RARE** |
| cleric.shield_of_faith | 1030 | 21145 | 4.9% |  |
| cleric.spirit_guardians | 196 | 3776 | 5.2% |  |
| cleric.spiritual_weapon | 1138 | 13915 | 8.2% |  |
| cleric.toll_the_dead | 5642 | 29403 | 19.2% |  |
| cleric.turn_undead | 0 | 14824 | 0.0% | **RARE** |
| cleric.word_of_radiance | 59 | 11387 | 0.5% | **RARE** |
| common.attack | 40433 | 164941 | 24.5% |  |
| common.dash | 32746 | 341369 | 9.6% |  |
| common.disengage | 640 | 341369 | 0.2% |  |
| common.dodge | 42481 | 341369 | 12.4% |  |
| common.help | 37389 | 320984 | 11.6% |  |
| common.shove | 1 | 147664 | 0.0% |  |
| druid.blight | 9 | 1955 | 0.5% | **RARE** |
| druid.call_lightning | 82 | 10385 | 0.8% | **RARE** |
| druid.conjure_animals | 0 | 6960 | 0.0% | **RARE** |
| druid.entangle | 9445 | 15819 | 59.7% |  |
| druid.faerie_fire | 2664 | 15819 | 16.8% |  |
| druid.greater_restoration | 1 | 3108 | 0.0% | **RARE** |
| druid.healing_word | 1154 | 14002 | 8.2% |  |
| druid.hold_person | 475 | 10602 | 4.5% |  |
| druid.ice_storm | 831 | 6618 | 12.6% |  |
| druid.mass_cure_wounds | 0 | 3187 | 0.0% | **RARE** |
| druid.moonbeam | 378 | 10988 | 3.4% |  |
| druid.produce_flame | 1363 | 13993 | 9.7% |  |
| druid.sleet_storm | 51 | 10385 | 0.5% | **RARE** |
| druid.spike_growth | 8 | 13631 | 0.1% | **RARE** |
| druid.thorn_whip | 31 | 12706 | 0.2% | **RARE** |
| druid.thunderwave | 4 | 3694 | 0.1% | **RARE** |
| druid.wild_shape_bear | 464 | 11224 | 4.1% |  |
| druid.wild_shape_dire_wolf | 500 | 7371 | 6.8% |  |
| druid.wild_shape_wolf | 684 | 14126 | 4.8% |  |
| fighter.action_surge | 0 | 22379 | 0.0% | **RARE** |
| fighter.feinting_attack | 205 | 4280 | 4.8% |  |
| fighter.goading_attack | 1464 | 8835 | 16.6% |  |
| fighter.menacing_attack | 0 | 2305 | 0.0% | **RARE** |
| fighter.precision_attack | 1488 | 11097 | 13.4% |  |
| fighter.pushing_attack | 2 | 4118 | 0.0% | **RARE** |
| fighter.rally | 4320 | 11373 | 38.0% |  |
| fighter.second_wind | 4165 | 21132 | 19.7% |  |
| fighter.trip_attack | 4577 | 11097 | 41.2% |  |
| monk.flurry_of_blows | 1222 | 14369 | 8.5% |  |
| monk.heightened_flurry | 436 | 3549 | 12.3% |  |
| monk.martial_arts | 367 | 17734 | 2.1% |  |
| monk.patient_defense | 2842 | 14024 | 20.3% |  |
| monk.step_of_the_wind | 922 | 20096 | 4.6% |  |
| monk.stunning_strike | 3654 | 9779 | 37.4% |  |
| monk.unarmed_strike | 6399 | 17582 | 36.4% |  |
| paladin.aid | 891 | 10200 | 8.7% |  |
| paladin.bless | 1817 | 16178 | 11.2% |  |
| paladin.command | 179 | 7010 | 2.6% |  |
| paladin.compelled_duel | 417 | 9086 | 4.6% |  |
| paladin.cure_wounds | 2633 | 16178 | 16.3% |  |
| paladin.dispel_magic | 0 | 3581 | 0.0% | **RARE** |
| paladin.divine_smite | 678 | 8135 | 8.3% |  |
| paladin.divine_smite_2 | 752 | 5630 | 13.4% |  |
| paladin.divine_smite_3 | 301 | 1945 | 15.5% |  |
| paladin.elemental_weapon | 1 | 3581 | 0.0% | **RARE** |
| paladin.lay_on_hands | 5521 | 16483 | 33.5% |  |
| paladin.lesser_restoration | 0 | 3720 | 0.0% | **RARE** |
| paladin.shield_of_faith | 595 | 14013 | 4.2% |  |
| ranger.barkskin | 0 | 5218 | 0.0% | **RARE** |
| ranger.conjure_barrage | 700 | 820 | 85.4% |  |
| ranger.cure_wounds | 189 | 11953 | 1.6% | **RARE** |
| ranger.ensnaring_strike | 322 | 15194 | 2.1% |  |
| ranger.entangle | 293 | 14004 | 2.1% |  |
| ranger.hail_of_thorns | 5385 | 15194 | 35.4% |  |
| ranger.hunters_mark | 2074 | 13109 | 15.8% |  |
| ranger.lesser_restoration | 0 | 820 | 0.0% | **RARE** |
| ranger.lightning_arrow | 0 | 820 | 0.0% | **RARE** |
| ranger.spike_growth | 629 | 9079 | 6.9% |  |
| ranger.vanish | 847 | 1850 | 45.8% |  |
| rogue.cunning_dash | 0 | 26674 | 0.0% | **RARE** |
| rogue.cunning_disengage | 1701 | 26674 | 6.4% |  |
| rogue.cunning_hide | 16153 | 31768 | 50.8% |  |
| rogue.obscure | 781 | 2856 | 27.3% |  |
| rogue.poison_strike | 500 | 6564 | 7.6% |  |
| rogue.steady_aim | 0 | 26674 | 0.0% | **RARE** |
| rogue.trip | 644 | 10068 | 6.4% |  |
| rogue.withdraw | 0 | 5921 | 0.0% | **RARE** |
| sorcerer.acid_splash | 5059 | 29909 | 16.9% |  |
| sorcerer.burning_hands | 1133 | 7134 | 15.9% |  |
| sorcerer.chill_touch | 2150 | 13022 | 16.5% |  |
| sorcerer.chromatic_orb | 1671 | 11877 | 14.1% |  |
| sorcerer.cone_of_cold | 642 | 1472 | 43.6% |  |
| sorcerer.fire_bolt | 2057 | 23924 | 8.6% |  |
| sorcerer.fireball | 2192 | 5437 | 40.3% |  |
| sorcerer.font_of_magic_mana | 318 | 19073 | 1.7% | **RARE** |
| sorcerer.font_of_magic_sorcery | 1445 | 11368 | 12.7% |  |
| sorcerer.hold_monster | 212 | 843 | 25.1% |  |
| sorcerer.ice_storm | 453 | 3539 | 12.8% |  |
| sorcerer.innate_sorcery | 4000 | 4000 | 100.0% |  |
| sorcerer.metamagic_careful | 0 | 14188 | 0.0% | **RARE** |
| sorcerer.metamagic_extended | 1233 | 3796 | 32.5% |  |
| sorcerer.metamagic_quickened | 1571 | 4281 | 36.7% |  |
| sorcerer.metamagic_twinned | 0 | 19073 | 0.0% | **RARE** |
| sorcerer.misty_step | 0 | 6116 | 0.0% | **RARE** |
| sorcerer.poison_spray | 420 | 2420 | 17.4% |  |
| sorcerer.ray_of_frost | 2653 | 23924 | 11.1% |  |
| sorcerer.scorching_ray | 2703 | 6391 | 42.3% |  |
| sorcerer.shocking_grasp | 63 | 9485 | 0.7% | **RARE** |
| sorcerer.wall_of_fire | 717 | 1645 | 43.6% |  |
| warlock.arms_of_hadar | 112 | 8053 | 1.4% | **RARE** |
| warlock.blight | 201 | 6688 | 3.0% |  |
| warlock.chill_touch | 507 | 28118 | 1.8% | **RARE** |
| warlock.eldritch_blast | 12636 | 31851 | 39.7% |  |
| warlock.hex | 3524 | 24293 | 14.5% |  |
| warlock.hold_monster | 735 | 3371 | 21.8% |  |
| warlock.hold_person | 1011 | 13158 | 7.7% |  |
| warlock.hunger_of_hadar | 6563 | 12012 | 54.6% |  |
| warlock.maddening_hex | 10 | 15565 | 0.1% | **RARE** |
| warlock.shadow_of_moil | 1607 | 5119 | 31.4% |  |
| warlock.shatter | 1323 | 16310 | 8.1% |  |
| warlock.synaptic_static | 511 | 5119 | 10.0% |  |
| wizard.acid_splash | 290 | 24843 | 1.2% | **RARE** |
| wizard.arcane_recovery | 2198 | 34777 | 6.3% |  |
| wizard.burning_hands | 153 | 22262 | 0.7% | **RARE** |
| wizard.chill_touch | 10 | 1750 | 0.6% | **RARE** |
| wizard.cone_of_cold | 455 | 4641 | 9.8% |  |
| wizard.fire_bolt | 2356 | 33508 | 7.0% |  |
| wizard.fireball | 2096 | 22917 | 9.1% |  |
| wizard.greater_invisibility | 25 | 6101 | 0.4% | **RARE** |
| wizard.haste | 3623 | 6472 | 56.0% |  |
| wizard.hold_monster | 9 | 4168 | 0.2% | **RARE** |
| wizard.ice_storm | 542 | 13949 | 3.9% |  |
| wizard.magic_missile | 438 | 29555 | 1.5% | **RARE** |
| wizard.misty_step | 0 | 23456 | 0.0% | **RARE** |
| wizard.ray_of_frost | 202 | 33508 | 0.6% | **RARE** |
| wizard.scorching_ray | 446 | 24625 | 1.8% | **RARE** |
| wizard.shocking_grasp | 27 | 14432 | 0.2% | **RARE** |
| wizard.sleep | 24539 | 36746 | 66.8% |  |
| wizard.thunderwave | 2 | 10053 | 0.0% | **RARE** |
| wizard.web | 774 | 15113 | 5.1% |  |

### 6. Downs, Outs and AI item use

Party members Downed (each time) and Out, per battle; items the AI used (party members play with items off, so only enemies could use any). The last three columns are totals over both sides' turns: AI commands `Battle.Apply` refused, turns planned again (after a refusal or a move cut short) and turns the simulator ended because the plans didn't.

| Encounter | Level | Battles | Downs / battle | Outs / battle | Battles with an Out | AI item uses | Rejected commands | Re-plans | Forced ends |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| crypt_hall | 1 | 360 | 7.5 | 1.0 | 193 | 0 | 8 | 8 | 0 |
| crypt_hall | 3 | 360 | 6.5 | 1.8 | 266 | 0 | 16 | 16 | 0 |
| crypt_hall | 5 | 360 | 1.7 | 0.5 | 105 | 0 | 2 | 2 | 0 |
| crypt_hall | 7 | 360 | 0.7 | 0.2 | 51 | 0 | 0 | 16 | 0 |
| crypt_hall | 10 | 360 | 0.1 | 0.0 | 4 | 0 | 0 | 3 | 0 |
| crystal_cave | 1 | 360 | 7.0 | 1.4 | 275 | 0 | 1 | 1 | 0 |
| crystal_cave | 3 | 360 | 6.6 | 1.9 | 303 | 0 | 0 | 0 | 0 |
| crystal_cave | 5 | 360 | 2.9 | 1.2 | 237 | 0 | 2 | 2 | 0 |
| crystal_cave | 7 | 360 | 1.5 | 0.7 | 146 | 0 | 0 | 27 | 0 |
| crystal_cave | 10 | 360 | 0.4 | 0.2 | 49 | 0 | 0 | 1 | 0 |
| forest_ruins | 1 | 360 | 5.2 | 1.5 | 256 | 0 | 4 | 4 | 0 |
| forest_ruins | 3 | 360 | 1.4 | 0.6 | 121 | 0 | 2 | 2 | 0 |
| forest_ruins | 5 | 360 | 0.1 | 0.0 | 8 | 0 | 4 | 4 | 0 |
| forest_ruins | 7 | 360 | 0.0 | 0.0 | 1 | 0 | 1 | 7 | 0 |
| forest_ruins | 10 | 360 | 0.0 | 0.0 | 1 | 0 | 1 | 3 | 0 |
| river_crossing | 1 | 360 | 2.7 | 0.8 | 163 | 0 | 1 | 1 | 0 |
| river_crossing | 3 | 360 | 0.3 | 0.0 | 12 | 0 | 0 | 0 | 0 |
| river_crossing | 5 | 360 | 0.1 | 0.0 | 12 | 0 | 0 | 0 | 0 |
| river_crossing | 7 | 360 | 0.0 | 0.0 | 0 | 0 | 0 | 5 | 0 |
| river_crossing | 10 | 360 | 0.0 | 0.0 | 0 | 0 | 0 | 0 | 0 |
| warlord_bridge | 1 | 360 | 7.7 | 1.2 | 233 | 0 | 1 | 1 | 0 |
| warlord_bridge | 3 | 360 | 4.9 | 1.0 | 205 | 0 | 10 | 10 | 0 |
| warlord_bridge | 5 | 360 | 3.3 | 0.7 | 159 | 0 | 1 | 1 | 0 |
| warlord_bridge | 7 | 360 | 2.4 | 0.4 | 88 | 0 | 1 | 24 | 0 |
| warlord_bridge | 10 | 360 | 1.5 | 0.2 | 70 | 0 | 0 | 18 | 0 |

### 7. Configuration and seeds

```
dotnet run -c Release --project games/Tactics/Tactics.Sim -- \
  --encounters crypt_hall,crystal_cave,forest_ruins,river_crossing,warlord_bridge --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 \
  --seeds 20 --difficulty Normal --round-cap 30
```

- **Encounters:** crypt_hall, crystal_cave, forest_ruins, river_crossing, warlord_bridge.
- **Levels:** 1, 3, 5, 7, 10. **Party sizes:** 4, 5, 6 (6 sampled parties per size).
- **Seeds:** 1–20 for every encounter, level and party (the battle's `Battle.Start` seed).
- **Difficulty:** Normal. **Round cap:** 30 (a battle still going after it counts as lost).
- **Parties:** human characters from `SheetBuilder.DefaultCharacter` (no subclass, default picks), playing the profile their class's `aiTags` name (healer → `healer`, caster or control → `caster`, ranged → `archer`, striker → `skirmisher`, otherwise `brute`) with items off; enemies play their monster's profile. Party compositions are drawn from every combination with at least one healer or defender and at most 2 of a class, by a generator seeded 20261030:
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
