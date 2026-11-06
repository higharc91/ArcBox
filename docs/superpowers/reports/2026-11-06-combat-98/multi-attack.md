# Multi-attack targets and once-only riders (#107)

Task 6 of the combat-area fixes (#98). No class numbers were retuned; this records how the rule change moved the sampled encounters.

## What changed

- **Each attack picks its target.** A weapon attack (`isWeaponAttack`) makes the sheet's `attacks` attacks one by one (`StrikeStep`). A `Use` may list a target per attack (`targets`; the phone's "Attack 2 of N"). An attack whose target is Dead, Out or out of reach goes to the next listed target still in reach, then to the hostile in reach with the least current HP (ties by unit id). Before, the effect list was copied onto the one target, and attacks after a kill did nothing.
- **Once-only riders.** `"riderOnce": true` runs the whole list on the attacks up to and including the first hit; later attacks are plain hits. It is set on 25 abilities: every resource-costed (`resource` or `uses`) weapon attack that a unit with more than one attack can use (`RiderOnceTests`): `avenger.soul_of_vengeance`, `bandit.dirty_strike`, `crusader.searing_smite`, `duelist.master_flourish`, `duelist.riposte`, `fighter.goading_attack`, `fighter.menacing_attack`, `fighter.precision_attack`, `fighter.pushing_attack`, `fighter.trip_attack`, `halvard.wardens_judgement`, `iron_body.counter_palm`, `monk.stunning_strike`, `open_hand.palm_strike`, `open_hand.quivering_palm`, `rogue.daze`, `rogue.knock_out`, `rogue.obscure`, `rogue.poison_strike`, `rogue.trip`, `sharpshooter.deadeye`, `valor.battle_magic`, `weaponmaster.blade_dance`, `weaponmaster.cleave`, `weaponmaster.execution_strike`.
- **AI.** Weapon attacks are valued with the rider counted once (with the chance that any attack hits), and after a predicted kill (the first target at least 50% likely dropped) the planner may send the rest to another foe in reach.

## Sampled encounters

Each encounter at its own level, the Sim's default parties (sizes 4, 5 and 6, 6 parties each), Normal difficulty. "Before" is the branch at `39c3ef85`, "after" this change; each was a published copy of the Sim run against its own copy of the rules.

```
dotnet Tactics.Sim.dll --encounters E --levels L --seeds {6|20} --rules <copy>
```

### Seeds 1 to 20 (360 battles per cell)

| Encounter | Lvl | Win % before | after | Rounds before | after | Size 4 win % before | after | Capped before | after |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ford_assault | 1 | 78.6 | 78.3 | 9.2 | 9.2 | 63.3 | 63.3 | 6 | 6 |
| forest_ruins | 3 | 88.1 | 88.1 | 10.5 | 10.4 | 67.5 | 67.5 | 4 | 3 |
| warlord_bridge | 5 | 93.1 | 93.6 | 11.2 | 11.3 | 83.3 | 83.3 | 0 | 0 |
| crystal_cave | 7 | 93.3 | 94.4 | 12.0 | 11.8 | 80.8 | 83.3 | 0 | 0 |

### Seeds 1 to 6 (108 battles per cell)

| Encounter | Lvl | Win % before | after | Rounds before | after | Size 4 win % before | after |
|---|---:|---:|---:|---:|---:|---:|---:|
| ford_assault | 1 | 82.4 | 81.5 | 9.2 | 9.2 | 63.9 | 61.1 |
| forest_ruins | 3 | 86.1 | 87.0 | 10.8 | 10.6 | 58.3 | 61.1 |
| warlord_bridge | 5 | 91.7 | 92.6 | 11.3 | 11.4 | 80.6 | 80.6 |
| crystal_cave | 7 | 89.8 | 94.4 | 12.2 | 11.9 | 72.2 | 83.3 |

### Role bands that moved (seeds 1 to 20; deviation from the band mean, flagged beyond ±15%)

| Encounter | Band | Class | Before | After |
|---|---|---|---:|---:|
| warlord_bridge L5 | Defender | fighter | 11.7 (+59.1%) flagged | 12.0 (+54.1%) flagged |
| warlord_bridge L5 | Striker | ranger | 7.6 (−15.7%) flagged | 7.6 (−11.9%) |
| warlord_bridge L5 | Striker | rogue | 10.5 (+16.6%) flagged | 9.6 (+10.7%) |
| warlord_bridge L5 | Striker | monk | 4.5 (−50.0%) flagged | 4.2 (−51.8%) flagged |
| crystal_cave L7 | Striker | warlock | 8.2 (+27.0%) flagged | 7.9 (+22.4%) flagged |

Every other band value moved by less than 4 points.

### Rider abilities used (share of the turns they were legal, seeds 1 to 20)

| Ability | warlord_bridge L5 before | after | crystal_cave L7 before | after |
|---|---:|---:|---:|---:|
| `monk.stunning_strike` | 24.7% | 19.3% | 32.8% | 29.1% |
| `rogue.trip` | 19.4% | 25.5% | 4.7% | 10.2% |
| `rogue.poison_strike` | – | – | 13.3% | 5.7% |
| `fighter.trip_attack` | 15.6% | 14.6% | 10.5% | 12.1% |
| `fighter.precision_attack` | 4.1% | 2.7% | 8.5% | 4.3% |
| `fighter.goading_attack` | 23.1% | 26.2% | 5.1% | 5.4% |

## Reading it

- Every sampled cell stays inside the 60–95% and 8–12 round targets. The largest move is crystal_cave L7 (+1.1 points at 20 seeds, +4.6 at 6), still under the 95% ceiling; party size 4 there gains 2.5 points. Attacks no longer wasted after a kill outweigh the riders that now land once.
- The rogue's striker lead at level 5 narrows (+16.6% to +10.7%): its four attacks no longer stack Poison Strike or Trip saves, and the AI moves from Poison Strike to Trip. The monk's level-5 striker gap (−50%) was there before and widens slightly, as Stunning Strike now makes one save. Both classes are for #108 and later tuning; nothing was retuned here.
- Precision Attack loses half its use: its +4 now counts only until the first hit.
