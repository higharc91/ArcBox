# The abilities the AI never planned (#110)

Task 9 of the combat-area fixes (#98). The planner skipped empty-tile aims, never planned a bonus-action Dash, gave an extra action no value and only ever acted then moved, so absent players' heroes, AI fillers and timed-out turns never used teleports, summons, Action Surge, Cunning Action Dash or hit-and-run. No class numbers were retuned here (the final balance task does that); this records what the planner change does to ability use and to the sampled encounters.

## What changed

- **Empty-tile aims.** A teleport or leap (`teleport` on the user, `jumpTo`) aims at the 5 best landing tiles in reach, ranked by the position score (the threat map with the unit's AC and cover, edge risk, closing in, high ground…, plus the zone damage of landing and staying), and is worth what landing there gains over staying. It is planned three ways: as the action ("walk, then teleport", Dimension Door); as the bonus action at the end of the turn (out of reach after acting); and as "walk, teleport, act": from the unit's tile to its 5 best landings, and for a unit that fights in melee to the empty tiles beside each foe (the 5 best, each from the reachable tile whose walk costs least). A summon (Conjure Animals) aims at the empty tiles beside its foes and next to the caster, the ones nearer a foe first.
- **Action Surge.** An extra action is worth the best second action it buys: once the plan has spent its action, the surge is planned with the best second action from where the plan leaves the unit when that action, less its own cost, is worth more than the surge costs.
- **What a later action may count on.** The surged second action and the bonus action keep off a unit the first action may take out of reach:
  - one it may drop (a monster only when its damage can reach the HP left, so a hero surges at a lone boss);
  - the foe a multi-attack moves on to after a kill;
  - the targets of an attack the unit's passives turn into a push (Repelling Blast);
  - the unit's own concentration summons, when the action starts a new concentration.

  After an action that moves units or changes the ground (a push, a teleport, a summon, a zone), only an action on the unit itself counts. Holding a foe in place (Hold Person) no longer counts as such an action.
- **Twinned and Careful Spell** lead the spell they improve ("metamagic, then the spell"). Twinned is used when a single-target spell finds a second target in range (the engine's own pick); Careful when an area spell with a save catches the sorcerer's allies, who then pass the save (the scorer now knows Careful). Each is planned only when the shaped spell beats the plain one by more than the Sorcery costs.
- **Death Ward.** A condition whose passive stands its holder back up when it is Downed is worth what getting that ally up is worth, times the chance it drops next round (the threat on its tile over its HP). So a cleric wards the endangered front-liner.
- **Ranking with the bonus action.** The best 16 plans are ranked again with the bonus action they leave for the end (its best use where the plan ends). A plan that spends the bonus action early (a disengage, a teleport, a dash) gives that up, so Step of the Wind is taken over Flurry of Blows only when escaping is worth more.
- **Bonus-action Dash.** Cunning Action Dash, Withdraw and Step of the Wind are planned as "dash, walk further, act" to the 12 best tiles only the dash brings in reach (Withdraw and Step of the Wind also disengage, so that walk draws no opportunity attack), and lengthen the move of a turn whose action is already spent.
- **Move–act–move** for the `archer` and `skirmisher` profiles (and companions whose class plays one): the 4 best tiles the unit can strike from are kept as well as the 12 best positions, and after acting there it walks on to the best tile its Move left reaches, scored as the whole walk (a foe's opportunity attack counts once; a disengage at the start covers both walks). It never plans the second walk after walking into a zone that may hold it (a web).
- **Fair play with Downed heroes.** Hitting a Downed hero is worth to a profile that isn't cruel minus what it is worth to a cruel one (the chance of a hit × the hero's worth ÷ the countdown left), where it was 0. An area spell that would also catch a Downed hero takes an equal aim that spares them. This departs from spec §5.4 ("zero value"); the README's content conventions say so. A counterspell still values the spell it stops by what the spell does: a hit on a Downed hero counts as harm (a cruel caster's gain), never as the reactor's own fair-play cost.
- **Simulator.** `--subclasses cls:id,...` and `--race id`, so the subclass signatures and the elf's Fey Step can be measured.
- **Teleport safety.** A teleport used on its own only escapes: it lands on a tile with less threat than where it is used from (closing in is the walk's job). Before an action it never lands where the threat map says the foes would Down the unit next round (the companion's existing `WouldDrop` test). The first measurement below shows why.

## How it was measured

"Before" is `fd4a1827` (the planner as it was, with the new Sim options). "First version" is `a8bf0778`, the version first reviewed. "After" is `03ca509f`, after fix round 1. Each is a published copy of the Sim run against the same copy of the rules (this task changes no content). Normal difficulty, party sizes 4, 5 and 6, 6 sampled parties per size.

```
usage:      dotnet Tactics.Sim.dll --encounters crypt_hall,crystal_cave,forest_ruins,river_crossing,warlord_bridge \
              --levels 1,3,5,7,10 --seeds {6 before | 20 after}                      (2,700 / 9,000 battles)
subclasses: dotnet Tactics.Sim.dll --encounters forest_ruins,warlord_bridge,crystal_cave --levels 3,7,10 --seeds 6 \
              --subclasses rogue:rogue.shadow,monk:monk.windstep,warlock:warlock.void,paladin:paladin.avenger,sorcerer:sorcerer.draconic \
              --race elf                                                              (972 battles each)
story:      dotnet Tactics.Sim.dll --encounters E --levels L --seeds 20               (360 battles per cell)
```

The usage sweep is the balance report's (its five encounters at every level). The "after" story cells for river_crossing, forest_ruins, warlord_bridge, crypt_hall and crystal_cave come from it; the other four story encounters ran on their own. "Legal" is the Sim's: `Battle.Check` allows the ability at the start of the turn, whether or not it would do anything useful.

## Ability use

### The listed abilities (usage sweep; before at seeds 1–6, after at seeds 1–20)

Rates are used turns over legal turns.

| Ability | Before | First version | After |
|---|---:|---:|---:|
| `wizard.misty_step` | 0.0% (0 of 6,933) | 5.6% | **13.1%** (2,536 of 19,374) |
| `sorcerer.misty_step` | 0.0% (0 of 1,840) | 2.9% | **2.6%** (149 of 5,838) |
| `druid.conjure_animals` | 0.0% (0 of 2,077) | 14.2% | **15.6%** (903 of 5,787) |
| `rogue.cunning_dash` | 0.0% (0 of 7,873) | 48.9% | **35.8%** (6,610 of 18,486) |
| `rogue.withdraw` | 0.0% (0 of 1,707) | 8.1% | **7.9%** (334 of 4,238) |
| `fighter.action_surge` | 0.0% (0 of 6,856) | 18.2% | **20.3%** (2,380 of 11,702) |
| `sorcerer.metamagic_twinned` | 0.0% (0 of 5,854) | 0.0% | **10.6%** (1,515 of 14,228) |
| `sorcerer.metamagic_careful` | 0.0% (0 of 6,539) | 0.0% | **2.5%** (430 of 17,049) |
| `cleric.death_ward` | 0.0% (0 of 2,615) | 0.0% | **26.1%** (1,639 of 6,287) |
| `rogue.steady_aim` | 0.0% (0 of 7,873) | 0.0% | 0.0% (0 of 18,486) |
| `warlock.misty_step` | not in any default loadout | – | – |
| `monk.step_of_the_wind` (bonus Dash and Disengage) | 5.1% | 47.1% | 26.0% |
| `monk.flurry_of_blows` | 7.7% | 0.6% | **5.4%** (585 of 10,924) |
| `monk.martial_arts` | 1.9% | 2.1% | 5.0% |
| `rogue.cunning_disengage` | 5.9% | 9.1% | 4.8% |
| `common.dash` | 10.6% | 8.1% | 9.8% |
| `bard.hold_monster` | 4.7% | 5.7% | 6.6% |
| `bard.hypnotic_pattern` | 2.0% | 2.5% | 2.2% |
| `paladin.command` | 2.3% | 2.0% | 1.5% |
| `ranger.cure_wounds` | 2.1% | 2.3% | 1.7% |

Action Surge is legal only until it is spent (one use a battle), so its legal turns are the turns before the surge.

### Subclass signatures and Fey Step (subclass sweep, seeds 1–6, elf parties)

| Ability | Before | First version | After |
|---|---:|---:|---:|
| `shadow.shadowstep` | 0.0% (0 of 3,224) | 24.9% | **31.6%** (628 of 1,989) |
| `windstep.gale_leap` | 0.0% (0 of 2,884) | 30.8% | **37.5%** (592 of 1,578) |
| `void.rift_step` | 0.0% (0 of 4,100) | 12.1% | **12.4%** (282 of 2,266) |
| `avenger.relentless_step` | 0.0% (0 of 1,093) | 54.3% | **35.8%** (311 of 869) |
| `draconic.dragon_wings` | 0.0% (0 of 907) | 15.2% | **5.8%** (38 of 658) |
| `elf.fey_step` | 0.0% (0 of 38,761) | 5.8% | **6.1%** (1,814 of 29,866) |
| `sorcerer.metamagic_twinned` | 0.0% | 0.0% | 8.4% |
| `sorcerer.metamagic_careful` | 0.0% | 0.0% | 2.6% |
| `cleric.death_ward` | 0.0% | 0.0% | 20.7% |
| `sorcerer.misty_step` | 0.0% | 0.4% | 0.3% |

### Still under 2%

| Ability | Use | Why |
|---|---:|---|
| `rogue.steady_aim` | 0.0% | To be merged into Hide (#108): no planner work spent on it here. |
| `sorcerer.misty_step` in the subclass sweep | 0.3% | **Situational there.** Every sorcerer in that sweep is an elf, and Fey Step does the same job with free uses, so the AI keeps its Mana. In the usage sweep (human sorcerers) it is 2.6%. |
| `paladin.command` | 1.5% (was 2.3% at seeds 1–6) | **Outranked by the bonus action the fix round freed.** Divine Smite (the same Oath) can now land on the monster the paladin's attack just hit, where before any monster the attack touched was off limits to a later action. Its use doubled (7.4% to 15.2%), and the Oath goes there before Command. Command was at the 2% line before. |
| `ranger.cure_wounds` | 1.7% (was 2.1%) | **Outranked the same way, and touch range.** Hunter's Mark (the same Focus) can now go on the monster the ranger's attack just hit (11.5% to 37.6%), and the ranger stands back from its allies (the balance report's reason when it was 1.6%). |

`warlock.misty_step` is planned as the wizard's and sorcerer's are; no warlock default loadout takes it, so the Sim never sees it.

Over the whole usage sweep, 43 abilities are under 2% (54 before, at seeds 1–6). Leaving the list: Action Surge, Cunning Dash, Withdraw, Conjure Animals, both Misty Steps, Twinned, Careful, Death Ward, Martial Arts, Lightning Arrow, Font of Magic and Acid Splash. Joining it: Command and the ranger's Cure Wounds.

## Story encounters at their own level

| Encounter | Lvl | Win % before | first version | after | Δ | Rounds before | after | Capped before | after | Flag after |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| river_crossing | 1 | 92.2 | 85.6 | 88.3 | −3.9 | 10.4 | 10.2 | 11 | 15 | |
| ford_assault | 1 | 78.3 | 75.6 | 80.6 | +2.2 | 9.2 | 9.1 | 6 | 7 | |
| forest_ruins | 3 | 88.1 | 88.3 | 91.1 | +3.1 | 10.4 | 9.1 | 3 | 1 | |
| warlord_bridge | 5 | 93.6 | 94.2 | 90.0 | −3.6 | 11.3 | 11.1 | 0 | 0 | |
| crypt_hall | 5 | 91.7 | 91.7 | 92.5 | +0.8 | 12.1 | 9.5 | 4 | 4 | |
| crystal_cave | 7 | 94.4 | 92.8 | 90.8 | −3.6 | 11.8 | 10.0 | 0 | 0 | |
| rime_tower | 7 | 91.1 | 92.2 | 92.2 | +1.1 | 11.9 | 10.0 | 3 | 0 | |
| keep_gate | 9 | 84.7 | 83.6 | 86.7 | +1.9 | 10.5 | 9.0 | 2 | 1 | |
| hall_of_the_flame | 10 | 64.2 | 71.9 | 75.6 | +11.4 | 9.5 | 6.9 | 0 | 0 | **SHORT** (6.9 rounds) |

Every cell stays inside 60–95% wins. One leaves 8–12 rounds: **hall_of_the_flame at level 10, 6.9 rounds** (it was 9.5). crypt_hall comes back inside (12.1 to 9.5). Nothing was retuned here; the final balance task (T11) retunes, and owns this cell and river_crossing below.

- **Battles are shorter.** Mean rounds fall in all 9 cells (0.1 to 2.6 rounds). Action Surge, bonus-action dashes, teleports and twinned spells bring damage in sooner; across the usage sweep the mean is 8.3 rounds (9.3 before).
- **river_crossing, level 1: −3.9 points** (size 4: 80.0% to 74.2%), 15 capped battles (11 before). A level-1 party has none of the new abilities (Action Surge and Cunning Action come at level 2), but its foes gain: the goblins play `skirmisher` and the goblin archers `archer`, so both now step out across the river, strike and step back.
- **hall_of_the_flame, level 10: +11.4 points** (size 6: 70.8% to 93.3%; size 4: 46.7% to 56.7%), at 6.9 rounds. A level-10 party gains the most: Action Surge, Withdraw, Conjure Animals, Misty Step, Twinned Spell and Death Ward all arrive by then.
- **Subclass and elf parties (subclass sweep): 86.9% to 84.9% won** (84.0% for the first version), 9.7 to 8.4 rounds. At warlord_bridge the win rate is back near or above baseline (level 7: 94.4% to 97.2%; level 10: 100% to 94.4%). Outs per battle are still higher there (level 7: 0.4 to 1.1; level 10: 0.1 to 0.7). The off-level crystal_cave at level 3 falls from 37.0% to 25.0%. The Sim's default parties have no subclass, so T11's bands won't see this.

## Teleports that ran ahead of the party

The first version of the planner had no teleport safety rules. In the subclass sweep its elf parties won 79.3% (86.9% before), and Warlord Bridge at level 7 fell from 94.4% to 84.3% with 1.6 Outs a battle (0.4). Traced battles showed melee heroes spending Fey Step, Shadowstep and the like to close in at the end of a turn (dash, walk, then teleport further) and to strike deep in the enemy line in round 2, a round or two before the rest of the party. They went Down there, out of the healer's reach, and their countdowns ran out. The brute and skirmisher profiles weigh the threat map lightly (caution 0.05 and 0.1), which suits a walk that keeps pace with the party but not a jump past it.

Warlord Bridge at level 7, seeds 1–6, win rate and Outs per battle:

| Planner | Elf parties | Subclass parties (human) |
|---|---:|---:|
| Before | 97.2%, 0.3 | 96.3%, 0.4 |
| First version | 88.0%, 1.3 | 90.7%, 1.1 |
| A teleport on its own only escapes | 93.5%, 1.2 | 90.7%, 1.2 |
| …and never lands where the unit would be Downed | 96.3%, 0.9 | 93.5%, 0.9 |

Both rules are kept, with an AI test each. The default parties were barely touched by either: their only teleport is the wizard's Misty Step, which escapes anyway.

## Planner timing

`AiPerformanceTests` p95 (ms), 100 timed plans each, on the 4-core container with nothing else running. The battles behind the timed turn states are played by the AI, so they differ before and after.

| Setup | Before | First version | After |
|---|---:|---:|---:|
| oil-coated river stress | 46.7 | 50.5 | 45.9 |
| river stress with spell hazards | 15.7 | 22.7 | 18.5 |
| river stress, level 10 | 15.4 | 24.1 | 15.4 |
| environment-enabled river stress | 14.3 | 18.8 | 16.3 |
| crystal_cave, level 7 | 8.2 | 10.8 | 7.6 |
| crypt_hall, level 5 | 6.6 | 7.6 | 8.4 |
| crystal_cave, level 7, elves with a Shadow rogue (new) | – | – | 10.9 |

All are far inside the 250 ms budget. Each new candidate source is capped:
- 5 landing tiles a teleport;
- for a melee unit, 5 more beside foes, chosen from the 10 best and each tried from at most 4 walks;
- 4 extra strike tiles for move–act–move;
- the 12 best tiles a bonus Dash brings in reach;
- Twinned and Careful only on actions they change;
- the bonus action's estimate for the best 16 plans, cached by where each ends.

The new setup times the melee teleport routes; the old ones only build human parties with no subclass.

## Plans the engine refused

In a narrow run (warlord_bridge and crystal_cave, levels 5 and 7, seeds 1–6), the Sim's refused AI commands went from 1 to 13, then back to 2 after three fixes (each with an AI test): no second walk after walking into a web (Restrained), no surged second action aimed at a foe a Shove has just moved, and neither a surged action nor the bonus action aimed at the foe a multi-attack moves on to after a kill. The 2 left are older: a reaction (Shield, Hellish Rebuke) spends the Mana or Pact the planned spell needed.

Fix round 1's ranking with the bonus action made two other blind spots show more often: in the subclass sweep refusals rose from 6 (before) to 16–21. They were Hex after an Eldritch Blast whose Repelling Blast had pushed the foe out of range, and Healing Word on a wolf a new concentration had sent away. With both covered (each with an AI test) it is back to 6. Across the final usage sweep, 24 commands of 9,000 battles were refused (37 for the first version).

## Goldens and fixtures

- **No golden replay changed.** The goldens replay recorded commands; the AI only records one whose commands are empty.
- **Phone fixtures** recorded from AI-played battles were re-recorded (`UPDATE_FIXTURES=1`): `react.json` (the first reaction offer now comes later in the recorded battle, so its deadline moved) and `results.json` (that party ends the battle with no injuries). The view types are unchanged.
- **`EngineFaultTests.A_save_whose_next_ai_step_fails_…`** took its save after a fixed six beats, which now lands mid-turn where the AI's remaining plan is only "end turn" (which can't fail there). It now goes on beat by beat (at most 20) to a save where the AI's next step is a real command, which is what the test means. It asks the adapter for that plan (`TacticsGame.PlanAi`, which the adapter's own AI step now uses), so the two can't drift apart.
- **Fix round 1** changed no golden and no phone fixture.
