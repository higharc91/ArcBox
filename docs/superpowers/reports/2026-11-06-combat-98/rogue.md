# Rebalancing the rogue for human play (#108)

Task 10 of the combat-area fixes (#98). The rogue's numbers had been raised (3–5 attacks per Attack, 3d6–9d6 Sneak Attack) to make up for an AI that rarely set up advantage, and Hide was a free bonus action anywhere. A human who hid every turn was several times stronger than any other striker. This task closes the free Hide, returns the numbers to SRD-like ones and tunes them with the simulator. The striker measure is unchanged: Task 11 changes it and re-checks every band (ruling R4).

## What changed

### Hide needs cover

- `rogue.cunning_hide` is refused in **plain view**: when a foe that can act (Up, not incapacitated) stands next to the rogue, or sees it with no cover. "Sees" is the engine's line of sight (`Sight.Trace`) from the foe's tile; cover is the engine's cover from walls, props and height, and smoke blocks sight. Standing units are no cover to hide behind. Half cover is enough (ruling 1: plain view is "line of sight and no cover").
- It applies the new `hiding` condition (invisible, as `hidden` is), which ends when the rogue attacks (hit or miss), when it **ends its turn in plain view**, or after its next turn. Walking through plain view mid-turn doesn't end it.
- The test is content: Hide has `"requires": { "seen": false }`. `requires` is a new ability field (a `when`, checked on the user); the new `seen` leaf is `Stealth.InPlainView`. The engine checks it in `Battle.ResolveTarget`, so `Battle.Check`, the phone's choices and the AI's targeting all see it. A refusal is `RejectCode.Unmet`, "Only when you aren't in plain view.", and the tooltip says the same. The loader checks the conditions a `requires` names, and refuses a leaf that reads an attack, a roll or a target (`weapon`, `advantage`, `success`, `flanked` and the like): `requires` is checked on the user alone, before anything is aimed or rolled.
- Invisibility, the ranger's Vanish and the warlock's One with Shadows keep the older `hidden`, which only an attack ends. The phone and the TV draw `hiding` with the Hidden art.
- Rules tests (`HideTests`): refused next to a foe, even one standing on a crate that gives the rogue half cover from it; refused in the open in sight; allowed behind a wall and behind half cover; refused when one of two foes sees it; a Downed foe sees nothing; hiding ends when the turn ends in plain view and lasts behind cover.

### Steady Aim is folded into Hide

Steady Aim (a bonus action: advantage, can't move) did what hiding does on the next attack, and was used 0% of the time. It is removed. Hide's tooltip says it covers Steady Aim. Cunning Action: Disengage moves from level 2 to level 3 (every class level needs a feature).

Old saves stay safe:
- A character record never named Steady Aim: it was a class feature, not a pick.
- A battle saved with `rogue.steady_aim` on a sheet reads back. The phone's choices and the AI skip ids the pack doesn't define, and using it is refused with `UnknownAbility` (`RogueContentTests.A_battle_saved_with_steady_aim_on_a_rogue_still_plays`).
- A battle saved under the older rules starts again from the records when it resumes (the content hash differs), and the rebuilt sheet has no Steady Aim (`DocumentTests.A_battle_saved_with_an_ability_since_removed_starts_again_without_it`, with the party at level 3).
- The icon atlas keeps its Steady Aim rect (art tooling input; nothing references it).

### The numbers

| | Before | After | SRD 5.2 | Cap (ruling) |
|---|---|---|---|---|
| Attacks per Attack action | 3 (L1), 4 (L5), 5 (L9) | 2 from L1 | 1 | ≤ 2 |
| Sneak Attack | 3d6 (L1), 6d6 (L3), 9d6 (L9) | 2d6 (L1), 3d6 (L3), 5d6 (L5), 7d6 (L9) | 1d6–5d6 | 2d6 / 3d6 / 5d6 / 7d6 at L1–2 / 3–4 / 5–8 / 9–10, every once-per-turn damage rider together (R29) |
| Shadow's Death from the Shadows (L10) | +2d6 necrotic on a hit with advantage | frightened for a round (Wis save against the rogue's Dex DC), no damage | | inside the cap above |
| Sneak Attack needs | advantage, or a flank | advantage, or an ally that can act next to the target and no disadvantage | the same as after | |
| Advantage from hits | none | Vex: a hit with a dart, rapier, shortbow or shortsword makes the target *Vexed*, the rogue's own mark; the rogue's attacks on it have advantage until the end of its next turn | Weapon Mastery (Vex is the rapier's, shortsword's, shortbow's and dart's) | |

`RogueContentTests` holds both caps at every level 1–10, for the rogue and each subclass: at most two attacks, and the once-per-turn damage riders summed (Sneak Attack plus any subclass passive like it) within the cap. The Sneak Attack dice are at the caps: the simulator showed the AI rogue below its band at every lower value (see Tuning). The first version of this report said the test covered every subclass; it checked Sneak Attack's own dice only, and missed that Death from the Shadows put a level-10 Shadow rogue at 9d6 (see "The cap covers every rider" below).

**Two SRD 5.2 rules beyond the issue's suggestions.** The caps alone left the AI rogue 10–36% under its band at most levels. Both rules raise how often the AI lands Sneak Attack, and neither raises a hiding human's ceiling much: a human who hides already has advantage.
- **Sneak Attack's ally clause.** Sneak Attack takes the SRD's own condition: an ally next to the target, rather than only a flank. This needed two new `when` leaves, `allyBesideTarget` and `disadvantage`.
- **Vex.** The rogue gets SRD 5.2's level-1 Weapon Mastery as a passive, `rogue.vex`, using the engine's `mark` with a condition of its own, `vexed`.

Both are departures in detail, stated in the README: a mark helps every attack until it ends, not only the next one, and no other mastery is modelled. A mark's tooltip now says what it gives ("your attacks on it have advantage"), and names its condition when it has its own ("marks target as Vexed for 1 round"). Vow of Enmity's flavour said the same advantage again; it now reads "You swear a vow of enmity against one foe."

### Vex is the rogue's own mark (ruling R28)

The first version of Vex marked with the shared `marked` condition. A condition from the same source replaces itself on a unit, but a mark from another unit replaced it too: a rogue's hit on a paladin's vowed foe ended the vow's advantage, and Vengeful Strikes (Avenger) and Ferocious Bond (Beastmaster), which read `marked`, fired on any foe the rogue had hit.

- `mark` takes an optional `condition` (`marked` unless given); Vex's is `vexed` (Vexed, tagged `mark`, drawn with the Marked art on the phone and the TV).
- The advantage comes from "a mark this attacker made": a condition the `mark` effect put on the target, flagged as a mark on the unit (`ActiveCondition.Mark`, a new field that defaults to false and is written only when true). `marked`, and every passive that reads it, is unchanged.
- Tests (`RogueContentTests`): a paladin's vow survives a rogue's hit on the same foe and still gives the paladin advantage; Vengeful Strikes and Ferocious Bond don't fire on a foe only vexed; Vex still gives the rogue advantage and puts `vexed`, not `marked`. The loader checks the condition a `mark` names.

### The cap covers every rider (ruling R29)

The cap is on Sneak Attack plus any once-per-turn subclass rider. Shadow's level-10 Death from the Shadows added 2d6 necrotic once per turn on a hit with advantage, so a level-10 Shadow rogue hiding as designed dealt 7d6 + 2d6 = 9d6, over the 7d6 cap.

- It now frightens the target for a round instead (Wisdom save against the rogue's Dexterity DC), on the same trigger. Why not keep damage: the engine has no shared once-per-turn budget for two passives, and no way to give one subclass a different Sneak Attack, so any extra dice there stack on Sneak Attack's at the cap. Frightened (disadvantage on the target's own attacks for the round) fits a shadow striking from the dark, and stays a level-10 payoff for fighting from hiding.
- `RogueContentTests.Once_per_turn_damage_riders_stay_within_the_rulings_caps` now sums, at every level 1–10 and for the rogue and each subclass, the average of every once-per-turn (or once-per-round) damage effect a hit can add, against the cap's d6s.
- The other subclasses: Trickster and Duelist have no damage rider on a hit. Duelist's Riposte and Master Flourish are separate attacks, each with its own cost (a reaction and Edge; one use a battle), not riders on the rogue's hits; Blade Mastery (+1 to hit, +2 damage) is a static bonus on every attack, not a once-per-turn rider. None of the three is in the default simulator's parties, which have no subclass, so R28 and R29 move no band number.

### Flanking (ruling 2)

The engine already had flanking for every class and monster: `Modifiers.Flanked`, an ally that can act on the tile directly opposite. It gave melee advantage (`AttackStep`), and Sneak Attack and the hobgoblin's Martial Advantage counted it. It has been in every role band since the rules engine was built, so "give flanking to every class" moves no band against BASE. No rogue-only path was needed and nothing changed. The README now states it as a departure (SRD 5.2 has no flanking rule). Sneak Attack now counts the advantage a flank gives, plus the SRD's ally clause, which includes every flank.

### The AI

- The planner aims every ability through `Battle.ResolveTarget`, so it never plans a Hide in plain view (`HideAiTests`; RED at BASE content, where it hid next to a goblin). It still plans a legal one: with its action and Move spent behind a wall, it hides.
- Hide is valued, as every self buff is, at the profile's `control` weight. That is 0.26 to a level-1 skirmisher, against several points for closing in. So the AI rogue hides on about 10% of its turns (it was 36–53% when Hide was free), mostly where a plan already ends out of sight. Ruling 6 asks for no new planner mode unless the AI never hides, and it does hide, so none was built. See Concerns.

### The simulator

- `--policy hidden-rogue`: the party's rogues play `HiddenRogue`, a scripted human who sets up Sneak Attack every turn it can. Each turn it weighs three plans:
  - walk, strike, and walk on to hide;
  - hide, then walk and strike from hiding;
  - with nothing to strike on foot and no foe within Cunning Dash reach, hide within a turn's reach of a foe.

  Striking scores 10, advantage 4 and ending hidden 5. It plays a plan that strikes with advantage or ends hidden. Anything else goes to the AI, which picks targets, riders and Cunning Dash at least as well: a plain strike, a foe only a dash away, or an approach from far off. Every command goes through `Battle.Apply`, and no dice are looked at. If the engine refuses a scripted command, the AI plays the rest of that turn (the first version asked the script again, which could repeat the refusal until the turn was force-ended); the report counts these refusals.
- Advantage, for the script, is the engine's own count (`Rolls.AttackEdge`): the attack roll, the AI's odds and the script all count edge with one pure function, which takes the sources and the situation (flank, mark, taunt, help). The first version of the script had its own copy, which left out help and taunts.
- The policy took three fixes to become a credible upper bound, each checked against its own runs:
  1. Scoring hiding above striking made a passive player: it hid on 80% of its turns and struck on 20%.
  2. Striking first, it still crept from cover to cover where the AI dashes in. On the BASE rules (free Hide) it hid on 85% of its turns, struck on only 35%, and scored below the BASE AI.
  3. It now leaves a foe a dash away to the AI.
- `--with-class rogue` plays only the sampled parties with a rogue (8 of 18). `--weapons rogue:shortbow` swaps a class's weapon.
- The report's "Sneak Attack set-up (rogues)" table gives, by level: the share of rogue turns with Hide, Sneak Attack and a weapon attack, the share of those attacks made from hiding and, in melee, flanking, and the scripted rogue's refused commands.

### AI planning time

Hide asks `Stealth.InPlainView`, which traces line of sight from each foe. The oil-coated stress test (`AiPerformanceTests`, 6 heroes against 10 level-10 monsters, 100 timed plans; Release, build servers stopped, nothing else running), five runs each:

| | p95 (ms) | Median (ms) | Max (ms) |
|---|---|---|---|
| BASE `4b7fd089` | 41.0, 44.9, 36.6, 52.2, 40.2 | 9.5–12.9 | 67–89 |
| This task | 54.3, 45.5, 52.4, 56.5, 55.3 | 9.2–10.9 | 113–211 |

p95 is about 13 ms higher, against a 250 ms budget, and the median is unchanged. It isn't the plain-view test: timed directly, `InPlainView` took 6–7 ms in all, over 183 calls in a whole run. The test plays two battles with the AI and times the start of each turn, so the rogue's new play gives it different states to plan: the slowest plan is the wizard's first turn in both, but at HEAD the field differs (the goblins can no longer hide in the open, and HP and positions follow), and the wizard weighs Misty Step and Sleep (124 ms) where at BASE it weighed Misty Step and Fireball (99 ms). `InPlainView` now finds the foes that can act once and answers from adjacency before it traces any line, which was free to do.

## How it was measured

Normal difficulty, party sizes 4–6, 6 sampled parties per size, the balance report's five encounters. "Before" is BASE `4b7fd089`, "after" is this task's final content. Each run is a published copy of the Sim with its own copy of the rules. The new Sim run on the BASE rules plays as BASE did: every engine change is gated by content (`requires`, `seen`, the new leaves). That gives BASE runs the new options and set-up table.

```
bands:     Tactics.Sim --encounters crypt_hall,crystal_cave,forest_ruins,river_crossing,warlord_bridge \
             --levels 1,2,3,4,5,6,7,8,9,10 --seeds 20                       (18,000 battles each)
story:     Tactics.Sim --encounters E --levels L --seeds 20                  (360 per cell; five cells come from the bands run)
tuning:    ... --levels 1,3,5,7,10 --seeds 6 --with-class rogue             (1,200 battles per candidate)
scripted:  ... --levels 1,2,3,5,7,10 --with-class rogue --policy hidden-rogue [--weapons rogue:shortbow] --seeds 20
```

## Tuning

The AI rogue's damage per round in the rogue parties (seeds 1–6), by candidate. **In band** is the range that keeps it within ±15% of the band mean, given the other four strikers' values in BASE's full sweep at seeds 20: the band mean includes the rogue, so the range is 0.205–0.299 × their sum.

| Candidate | L1 | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| In band (BASE others) | 3.3–4.8 | 4.1–5.9 | 4.2–6.2 | 4.1–5.9 | 6.5–9.5 | 6.9–10.0 | 7.4–10.7 | 7.7–11.3 | 9.2–13.4 | 9.7–14.2 |
| BASE: 3/4/5 attacks, 3d6/6d6/9d6, free Hide | 4.1 | | 6.9 | | 9.9 | | 10.3 | | | 13.1 |
| c1: 1 attack (2 from L3), caps, Hide needs cover | 1.7 | | 4.0 | | 5.0 | | 6.0 | | | 6.9 |
| c2: c1 with 2 attacks from L1 | 2.9 | | 4.0 | | | | | | | |
| c3: c2 + SRD Sneak Attack ally clause | 2.9 | | 4.3 | | 5.2 | | 6.3 | | | 7.2 |
| c4: c3 with 1 attack at L1–2 | 2.0 | | 4.3 | | 5.2 | | 6.3 | | | 7.2 |
| c5: c2 + Vex | 3.4 | | 5.1 | | 6.4 | | 7.5 | | | 8.2 |
| **c6: c3 + Vex** (final, with Disengage at L3) | 3.4 | 4.4 | 5.1 | 5.5 | 6.5 | 7.0 | 7.7 | 8.0 | 9.0 | 8.6 |

What the candidates show:
- The caps are binding. At the caps' maximum (c2/c3), the AI rogue is 10–36% under its band from L5 on. The cut from 4–5 attacks to 2 halves its weapon damage, and Hide behind cover leaves it Sneak Attack on about 15% of its turns. BASE's free Hide gave it 18–23%.
- The SRD ally clause alone adds little (c3). Vex lifts Sneak Attack to about 25% of turns (c5/c6), which brings L1–L8 into band.
- Lower Sneak Attack dice, or one attack at L1–2, move it further out (c1, c4). So the dice are at the caps and 2 attacks start at level 1.

## The rogue's band, levels 1–10 (seeds 20, every sampled party)

Striker measure: damage per round (HP taken off foes, no overkill), against the striker band's mean; flagged beyond ±15%.

| Level | Before | Band mean | Deviation | After | Band mean | Deviation | In band |
|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 4.2 | 4.0 | +5.1% | 3.7 | 4.0 | −6.7% | yes |
| 2 | 5.9 | 5.2 | +14.0% | 4.4 | 4.9 | −9.4% | yes |
| 3 | 7.2 | 5.6 | **+30.2%** | 5.2 | 5.1 | +2.4% | yes |
| 4 | 8.0 | 5.5 | **+44.4%** | 5.8 | 5.1 | **+15.1%** | no (0.1 over) |
| 5 | 10.1 | 8.4 | **+21.5%** | 7.0 | 7.8 | −10.0% | yes |
| 6 | 10.1 | 8.7 | **+16.2%** | 7.4 | 8.1 | −8.3% | yes |
| 7 | 10.6 | 9.3 | +13.9% | 7.7 | 8.5 | −9.3% | yes |
| 8 | 11.4 | 9.8 | **+16.3%** | 7.9 | 9.0 | −11.5% | yes |
| 9 | 13.1 | 11.6 | +12.8% | 9.1 | 10.9 | **−17.1%** | no |
| 10 | 13.6 | 12.2 | +11.2% | 8.9 | 11.3 | **−21.4%** | no |

The rogue was out of band at five levels and is out at three now:
- **L4** is 0.1 point over, which is within seed noise.
- **L9–10** are below. They are bound by the caps: 7d6 and two attacks are the ruling's maximum, and the AI lands Sneak Attack on about 24% of its turns there. The L9–10 band is lifted by the sorcerer's area damage (+11–13%) and the monk (+21–22%).

Every striker after, with its deviation:

| Level | monk | ranger | rogue | sorcerer | warlock | Band mean |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 4.0 (+1.2%) | 4.2 (+7.1%) | 3.7 (−6.7%) | 3.9 (−0.9%) | 3.9 (−0.7%) | 4.0 |
| 2 | 5.9 (+21.0%) | 4.5 (−7.9%) | 4.4 (−9.4%) | 4.3 (−12.3%) | 5.3 (+8.6%) | 4.9 |
| 3 | 5.8 (+13.5%) | 4.8 (−6.6%) | 5.2 (+2.4%) | 5.0 (−1.9%) | 4.7 (−7.4%) | 5.1 |
| 4 | 6.3 (+24.1%) | 4.7 (−6.8%) | 5.8 (+15.1%) | 5.0 (−1.4%) | 3.5 (−31.0%) | 5.1 |
| 5 | 9.6 (+23.3%) | 7.0 (−9.4%) | 7.0 (−10.0%) | 7.1 (−8.6%) | 8.1 (+4.7%) | 7.8 |
| 6 | 10.3 (+26.5%) | 7.7 (−5.1%) | 7.4 (−8.3%) | 7.5 (−7.1%) | 7.6 (−5.9%) | 8.1 |
| 7 | 10.7 (+26.3%) | 7.9 (−7.5%) | 7.7 (−9.3%) | 7.6 (−10.4%) | 8.6 (+1.0%) | 8.5 |
| 8 | 11.1 (+23.5%) | 8.2 (−8.7%) | 7.9 (−11.5%) | 7.8 (−13.2%) | 9.9 (+9.9%) | 9.0 |
| 9 | 13.3 (+21.4%) | 9.1 (−16.7%) | 9.1 (−17.1%) | 12.1 (+11.3%) | 11.0 (+1.0%) | 10.9 |
| 10 | 13.7 (+21.8%) | 10.3 (−8.3%) | 8.9 (−21.4%) | 12.7 (+12.5%) | 10.8 (−4.6%) | 11.3 |

**Other classes.** No other class's own value moves by more than a few percent; paladins in rogue parties gain 5–12%, as they land more of the hits. But the striker mean falls with the rogue, so the monk, unchanged itself, is now flagged at L2, L4, L5, L8, L9 and L10 (+21–26%; it was +11–14%). Flagged band values overall: 36 → 35 of 120. Flanking for everyone was already in these bands at BASE (ruling 2), so it moves nothing.

## The scripted always-hidden rogue against 1.5× the band mean

The rogue parties, with each rogue played by `HiddenRogue`, at seeds 20 (4,800 battles per weapon). The band mean is the regular after-sweep's at that level. These are the review round's runs: a refused scripted command now hands the rest of the turn to the AI, and the numbers barely moved (the first round's rapier run, at seeds 6, was 0.74–0.90×; its shortbow run, at seeds 20, matches this one to 0.1 except at level 10, 11.2).

| Level | Band mean | 1.5× | Scripted, rapier (default kit) | Scripted, shortbow | AI, shortbow (control; seeds 6) |
|---:|---:|---:|---:|---:|---:|
| 1 | 4.0 | 6.0 | 3.4 (0.85×) | 6.8 (**1.70×**) | 7.5 (1.88×) |
| 2 | 4.9 | 7.4 | 4.4 (0.90×) | 7.8 (**1.59×**) | 8.2 (1.67×) |
| 3 | 5.1 | 7.6 | 4.9 (0.96×) | 8.8 (**1.73×**) | 9.6 (1.88×) |
| 5 | 7.8 | 11.7 | 6.5 (0.83×) | 10.8 (1.38×) | 10.6 (1.36×) |
| 7 | 8.5 | 12.8 | 7.3 (0.86×) | 10.7 (1.26×) | 10.5 (1.24×) |
| 10 | 11.3 | 17.0 | 8.6 (0.76×) | 11.3 (1.00×) | 11.5 (1.02×) |

How the scripted runs' commands fared (both sides' AI included in the first three counts; 800 battles per level):

| Weapon | Level | Rejected commands | Re-plans | Forced ends | Scripted refusals |
|---|---:|---:|---:|---:|---:|
| rapier | 1 / 2 / 3 / 5 / 7 / 10 | 7 / 13 / 1 / 11 / 6 / 6 | 81 / 112 / 72 / 66 / 70 / 33 | 0 | 5 / 12 / 1 / 9 / 6 / 6 |
| shortbow | 1 / 2 / 3 / 5 / 7 / 10 | 31 / 39 / 33 / 27 / 29 / 18 | 66 / 83 / 57 / 40 / 83 / 69 | 0 | 30 / 28 / 32 / 27 / 29 / 17 |

No turn was force-ended. The script's refusals are 39 in 30,000 rapier turns and 163 in 36,500 shortbow turns, and almost all of the shortbow runs' rejected commands. In a probed sample every one was the closing Hide of a strike-then-hide plan: the script judges that Hide on the field before its strike resolves, and the strike's outcome can change who sees the rogue. The engine refuses it, and the AI plays the rest of the turn.

On the BASE rules, with Hide free, the same policy plays the issue's loop (attack from hiding, then Hide). Against BASE's band means:

| Level | Band mean | Scripted, rapier | Scripted, shortbow | AI, shortbow |
|---:|---:|---:|---:|---:|
| 1 | 4.0 | 3.6 (0.90×) | 6.9 (1.73×) | 8.0 (2.00×) |
| 2 | 5.2 | 6.2 (1.19×) | 9.0 (1.73×) | 10.3 (1.98×) |
| 3 | 5.6 | 6.9 (1.23×) | 10.3 (1.84×) | 11.3 (2.02×) |
| 5 | 8.4 | 9.4 (1.12×) | 12.6 (1.50×) | 13.6 (1.62×) |
| 7 | 9.3 | 9.7 (1.04×) | 12.8 (1.38×) | 13.4 (1.44×) |
| 10 | 12.2 | 12.0 (0.98×) | 15.6 (1.28×) | 17.2 (1.41×) |

Reading these:
- **The default rogue passes at every level.** With its starting rapier, the scripted always-hidden rogue stays under the band mean (0.76–0.96×). It plays about as well as the AI: in melee, hiding costs the bonus action Cunning Dash would use, and Move to reach cover, and gains little over Vex and the ally clause.
- **A shortbow is over 1.5× at levels 1–3 (1.59–1.73×), but not because of hiding.** The AI with the same shortbow, which hides on 10% of its turns, is higher still (1.67–1.88×). A ranged rogue attacks on 90–97% of its turns where the melee rogue manages 55–64%, and its allies are next to its targets, so Sneak Attack fires on 36–42% of its turns. It was like this before too (the AI shortbow rogue was 1.41–2.02× at BASE), and this task brings it down at every level from 3 on. The level 1–3 ranged rogue is a weapon-balance question; see Concerns.
- **The issue's estimate** of 31 damage a round at level 3 was a per-turn ceiling: three hits and Sneak Attack, every turn. Measured per round over whole battles (moving in, misses, foes down), even BASE's hide-every-turn rogue averaged 6.9 with a rapier and 10.3 with a shortbow at level 3. What the free Hide also gave was defence: disadvantage on every attack against it, even next to its foes. That is gone too. In melee the AI rogue now hides on 5–13% of its turns and the scripted one on 16–26%, never next to a foe.

## Story encounters at their own level (seeds 20)

| Encounter | Lvl | Win % before | after | Rounds before | after | Capped before | after | Flags after |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| river_crossing | 1 | 88.3% | 86.1% | 10.2 | 10.4 | 15 | 13 | CAPPED |
| ford_assault | 1 | 80.6% | 78.3% | 9.1 | 9.3 | 7 | 7 | CAPPED |
| forest_ruins | 3 | 90.8% | 85.8% | 9.1 | 9.3 | 1 | 2 | CAPPED |
| crypt_hall | 5 | 92.5% | 88.9% | 9.5 | 9.7 | 4 | 2 | CAPPED |
| warlord_bridge | 5 | 90.0% | 90.0% | 11.1 | 11.3 | 0 | 0 | |
| crystal_cave | 7 | 90.8% | 89.7% | 10.0 | 10.6 | 0 | 0 | |
| rime_tower | 7 | 92.2% | 90.8% | 10.0 | 10.2 | 0 | 0 | |
| keep_gate | 9 | 86.7% | 83.3% | 9.0 | 9.8 | 1 | 0 | |
| hall_of_the_flame | 10 | 75.6% | 68.9% | 6.9 | 8.0 | 0 | 0 | SHORT |

All nine stay inside 60–95%, and every one is a little harder (0 to −6.7 points) as the rogue's damage falls. The two shifts already flagged for Task 11:
- **hall_of_the_flame L10** loses 6.7 points and gains a round. It moves from 6.9 to 8.0 rounds, which narrows its SHORT flag; the mean is just under 8.
- **river_crossing L1** loses 2.2 more points, to 86.1%, with 13 capped battles (15 before).

## Hide, Sneak Attack and flanking

Share of the rogue's own turns with Hide, with Sneak Attack, and with a weapon attack. The last two columns are the share of those attacks made from hiding and, in melee, flanking. The BASE AI row comes from the tuning runs (seeds 6; the BASE band sweep was played by the older Sim, which has no set-up table).

| Run | Level | Hide | Sneak Attack | Attacking | From hiding | Flanking |
|---|---:|---:|---:|---:|---:|---:|
| Before, AI | 1 / 3 / 5 / 7 / 10 | 53 / 43 / 41 / 41 / 36% | 18 / 22 / 23 / 21 / 22% | 41 / 62 / 64 / 65 / 68% | 48 / 43 / 39 / 38 / 35% | 6 / 9 / 5 / 4 / 8% |
| After, AI | 1 / 3 / 5 / 7 / 10 | 13 / 8 / 11 / 12 / 10% | 21 / 24 / 26 / 26 / 24% | 41 / 55 / 61 / 63 / 64% | 13 / 11 / 14 / 15 / 12% | 5 / 8 / 5 / 6 / 8% |
| After, AI | 2 / 4 / 6 / 8 / 9 | 5 / 8 / 12 / 11 / 13% | 24 / 26 / 27 / 26 / 24% | 55 / 59 / 62 / 63 / 63% | 6 / 11 / 15 / 14 / 16% | 8 / 8 / 6 / 7 / 6% |
| Before, scripted rapier | 1 / 3 / 5 / 7 / 10 | 85 / 62 / 58 / 60 / 55% | 20 / 25 / 26 / 23 / 19% | 35 / 61 / 65 / 66 / 69% | 92 / 69 / 60 / 62 / 58% | 4 / 6 / 3 / 3 / 3% |
| After, scripted rapier (seeds 20) | 1 / 3 / 5 / 7 / 10 | 26 / 16 / 22 / 21 / 22% | 20 / 24 / 26 / 26 / 25% | 39 / 55 / 61 / 62 / 64% | 25 / 23 / 28 / 27 / 27% | 4 / 6 / 4 / 5 / 7% |
| After, scripted shortbow (seeds 20) | 1 / 3 / 5 / 7 / 10 | 65 / 51 / 47 / 46 / 41% | 35 / 42 / 39 / 39 / 36% | 75 / 97 / 90 / 90 / 89% | 56 / 46 / 41 / 41 / 36% | – |

- **Hide** (`rogue.cunning_hide`, the Sim's used / legal turns): 44.1% at BASE (20,594 of 46,674), 42.8% after (5,199 of 12,151). "Legal" is judged at the start of the turn, and Hide is now legal at the start of only about a quarter of the rogue's turns. Per turn, the AI rogue hides 4–13% of the time (36–53% before).
- **Sneak Attack** lands on 21–27% of the AI rogue's turns (18–23% before), now from Vex and the ally clause rather than from hiding.
- **Flanking** is unchanged: 5–8% of the AI rogue's melee attacks.
- **Steady Aim** was 0.0% of 37,478 legal turns at BASE. It is gone.

## For Task 11

- **Tracker "Decisions for the user" item 1** (Task 23's changes beyond numbers):
  - **Hide at level 1:** kept, but now refused in plain view, and hiding ends in plain view (#108).
  - **The rogue's attack counts:** now at most 2, from level 1 (were 3/4/5).
  - **The rogue's rapier:** kept, with Vex.
  - The rest of item 1 (monster `saves`, the monk's attacks, warlord_bridge, goblin_boss) isn't this task's.
- **Ruling R4.** The rogue is in band at L1–L3 and L5–L8 under the current measure. It is +15.1% at L4 and −17%/−21% at L9/L10, where the caps bind. Re-check it under the new measure: a per-target measure would lower the sorcerer's L9–10 lift. If L9–10 still fall short, the levers are a Sneak Attack above the 7d6 cap (a new ruling), or making the AI value hiding as the Sneak Attack it unlocks (Concerns 2).
- **The monk** is now flagged at six levels (+21–26%) only because the rogue no longer lifts the striker mean.
- **Encounters:** hall_of_the_flame L10 is 68.9% and 8.0 rounds (was 75.6%, 6.9 rounds), still flagged SHORT. river_crossing L1 is 86.1% (was 88.3%).
- **The simulator** has `--with-class`, `--weapons` and `--policy hidden-rogue` for re-checking the rogue cheaply, and the set-up table.
- **Review round (R28, R29):** Vex is now its own mark (`vexed`), and Death from the Shadows frightens instead of adding 2d6. Neither feeds the default simulator, whose parties have no subclass and no other marking class, so the band tables above stand. The scripted runs were re-run at seeds 20.

## Concerns

1. **Two SRD 5.2 rules beyond the issue's suggestions**, Sneak Attack's ally clause and Vex. Without them the caps leave the AI rogue 10–36% under its band (c2/c3). Both are SRD-faithful and content-only (they can be reverted in `abilities/rogue.json`), but they are design calls the owner hasn't made.
2. **The AI values Hide as control, not damage.** A self buff is weighed at the profile's `control` weight: 0.26 for a level-1 skirmisher, against several points for closing in. So the AI hides on about 10% of its turns, and never steps back into cover after striking. Ruling 6 asked for no new planner mode since the AI does hide. Valuing hiding as the Sneak Attack it unlocks, and letting move–act–move retreat to a tile where the bonus action can Hide, is the obvious next step if L9–10 stay short.
3. **A ranged rogue is strong at levels 1–3** (1.6–1.9× the band mean with a shortbow, whether hiding or not). The Sim's rogue carries a rapier, so the band doesn't see it. Options: the ally clause for melee only, Vex for melee weapons only, or accept it (it was higher before).
4. **The scripted policy is a heuristic, not an optimum.** It took three fixes before it played at least as well as the AI; see The simulator. Its numbers bound "a human who hides every turn it can", not every possible human. It plans a whole turn on the field as it stands, so a strike that changes who sees the rogue can leave its closing Hide refused (about 0.1–0.5% of its turns); the AI then finishes the turn.
5. **Death from the Shadows is now a control rider** (frightened), not damage, so a level-10 Shadow rogue gains no damage from its capstone. If the owner wants damage there, the engine needs either a shared once-per-turn budget or a per-subclass Sneak Attack that stays within 7d6.

