# Rebalancing the rogue for human play (#108)

Task 10 of the combat-area fixes (#98). The rogue's numbers had been raised (3–5 attacks per Attack, 3d6–9d6 Sneak Attack) to make up for an AI that rarely set up advantage, and Hide was a free bonus action anywhere. A human who hid every turn was several times stronger than any other striker. This task closes the free Hide, returns the numbers to SRD-like ones and tunes them with the simulator. The striker measure is unchanged: Task 11 changes it and re-checks every band (ruling R4).

## What changed

### Hide needs cover

- `rogue.cunning_hide` is refused in **plain view**: when a foe that can act (Up, not incapacitated) stands next to the rogue, or sees it with no cover. "Sees" is the engine's line of sight (`Sight.Trace`) from the foe's tile; cover is the engine's cover from walls, props and height, and smoke blocks sight. Standing units are no cover to hide behind. Half cover is enough (ruling 1: plain view is "line of sight and no cover").
- It applies the new `hiding` condition (invisible, as `hidden` is), which ends when the rogue attacks (hit or miss), when it **ends its turn in plain view**, or after its next turn. Walking through plain view mid-turn doesn't end it.
- The test is content: Hide has `"requires": { "seen": false }`. `requires` is a new ability field (a `when`, checked on the user); the new `seen` leaf is `Stealth.InPlainView`. The engine checks it in `Battle.ResolveTarget`, so `Battle.Check`, the phone's choices and the AI's targeting all see it. A refusal is `RejectCode.Unmet`, "Only when you aren't in plain view.", and the tooltip says the same. The loader checks the conditions a `requires` names.
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
| Sneak Attack | 3d6 (L1), 6d6 (L3), 9d6 (L9) | 2d6 (L1), 3d6 (L3), 5d6 (L5), 7d6 (L9) | 1d6–5d6 | 2d6 / 3d6 / 5d6 / 7d6 |
| Sneak Attack needs | advantage, or a flank | advantage, or an ally that can act next to the target and no disadvantage | the same as after | |
| Advantage from hits | none | Vex: a hit with a dart, rapier, shortbow or shortsword marks the target; the rogue's attacks on it have advantage until the end of its next turn | Weapon Mastery (Vex is the rapier's, shortsword's, shortbow's and dart's) | |

`RogueContentTests` holds both caps at every level 1–10 (and every subclass). The Sneak Attack dice are at the caps: the simulator showed the AI rogue below its band at every lower value (see Tuning).

**Two SRD 5.2 rules beyond the issue's suggestions.** The caps alone left the AI rogue 10–36% under its band at most levels. Both rules raise how often the AI lands Sneak Attack, and neither raises a hiding human's ceiling much: a human who hides already has advantage.
- **Sneak Attack's ally clause.** Sneak Attack takes the SRD's own condition: an ally next to the target, rather than only a flank. This needed two new `when` leaves, `allyBesideTarget` and `disadvantage`.
- **Vex.** The rogue gets SRD 5.2's level-1 Weapon Mastery as a passive, `rogue.vex`, using the engine's `mark`.

Both are departures in detail, stated in the README: a mark helps every attack until it ends, not only the next one, and no other mastery is modelled. A mark's tooltip now says what it gives ("your attacks on it have advantage"); this also reads better on Vow of Enmity and the other marks.

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

  Striking scores 10, advantage 4 and ending hidden 5. It plays a plan that strikes with advantage or ends hidden. Anything else goes to the AI, which picks targets, riders and Cunning Dash at least as well: a plain strike, a foe only a dash away, or an approach from far off. Every command goes through `Battle.Apply` (a refused one is planned again), and no dice are looked at.
- The policy took three fixes to become a credible upper bound, each checked against its own runs:
  1. Scoring hiding above striking made a passive player: it hid on 80% of its turns and struck on 20%.
  2. Striking first, it still crept from cover to cover where the AI dashes in. On the BASE rules (free Hide) it hid on 85% of its turns, struck on only 35%, and scored below the BASE AI.
  3. It now leaves a foe a dash away to the AI.
- `--with-class rogue` plays only the sampled parties with a rogue (8 of 18). `--weapons rogue:shortbow` swaps a class's weapon.
- The report's "Sneak Attack set-up (rogues)" table gives, by level: the share of rogue turns with Hide, Sneak Attack and a weapon attack, and the share of those attacks made from hiding and, in melee, flanking.

## How it was measured

Normal difficulty, party sizes 4–6, 6 sampled parties per size, the balance report's five encounters. "Before" is BASE `4b7fd089`, "after" is this task's final content. Each run is a published copy of the Sim with its own copy of the rules. The new Sim run on the BASE rules plays as BASE did: every engine change is gated by content (`requires`, `seen`, the new leaves). That gives BASE runs the new options and set-up table.

```
bands:     Tactics.Sim --encounters crypt_hall,crystal_cave,forest_ruins,river_crossing,warlord_bridge \
             --levels 1,2,3,4,5,6,7,8,9,10 --seeds 20                       (18,000 battles each)
story:     Tactics.Sim --encounters E --levels L --seeds 20                  (360 per cell; five cells come from the bands run)
tuning:    ... --levels 1,3,5,7,10 --seeds 6 --with-class rogue             (1,200 battles per candidate)
scripted:  ... --levels 1,2,3,5,7,10 --with-class rogue --policy hidden-rogue [--weapons rogue:shortbow] --seeds 6|20
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

The rogue parties, with each rogue played by `HiddenRogue`. The band mean is the regular after-sweep's at that level.

| Level | Band mean | 1.5× | Scripted, rapier (default kit; seeds 6) | Scripted, shortbow (seeds 20) | AI, shortbow (control; seeds 6) |
|---:|---:|---:|---:|---:|---:|
| 1 | 4.0 | 6.0 | 3.3 (0.82×) | 6.8 (**1.70×**) | 7.5 (1.88×) |
| 2 | 4.9 | 7.4 | 4.2 (0.86×) | 7.8 (**1.59×**) | 8.2 (1.67×) |
| 3 | 5.1 | 7.6 | 4.6 (0.90×) | 8.8 (**1.73×**) | 9.6 (1.88×) |
| 5 | 7.8 | 11.7 | 6.3 (0.81×) | 10.8 (1.38×) | 10.6 (1.36×) |
| 7 | 8.5 | 12.8 | 7.1 (0.84×) | 10.7 (1.26×) | 10.5 (1.24×) |
| 10 | 11.3 | 17.0 | 8.4 (0.74×) | 11.2 (0.99×) | 11.5 (1.02×) |

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
- **The default rogue passes at every level.** With its starting rapier, the scripted always-hidden rogue stays under the band mean (0.74–0.90×). It plays about as well as the AI: in melee, hiding costs the bonus action Cunning Dash would use, and Move to reach cover, and gains little over Vex and the ally clause.
- **A shortbow is over 1.5× at levels 1–3 (1.59–1.73×), but not because of hiding.** The AI with the same shortbow, which hides on 10% of its turns, is higher still (1.67–1.88×). A ranged rogue attacks on 90–97% of its turns where the melee rogue manages 55–64%, and its allies are next to its targets, so Sneak Attack fires on 36–42% of its turns. It was like this before too (the AI shortbow rogue was 1.41–2.02× at BASE), and this task brings it down at every level from 3 on. The level 1–3 ranged rogue is a weapon-balance question; see Concerns.
- **The issue's estimate** of 31 damage a round at level 3 was a per-turn ceiling: three hits and Sneak Attack, every turn. Measured per round over whole battles (moving in, misses, foes down), even BASE's hide-every-turn rogue averaged 6.9 with a rapier and 10.3 with a shortbow at level 3. What the free Hide also gave was defence: disadvantage on every attack against it, even next to its foes. That is gone too. In melee the AI rogue now hides on 5–13% of its turns and the scripted one on 16–25%, never next to a foe.

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
| After, scripted rapier | 1 / 3 / 5 / 7 / 10 | 25 / 16 / 23 / 22 / 22% | 20 / 23 / 25 / 26 / 24% | 39 / 55 / 61 / 63 / 65% | 25 / 23 / 29 / 27 / 28% | 3 / 6 / 4 / 5 / 8% |
| After, scripted shortbow | 1 / 3 / 5 / 7 / 10 | 65 / 51 / 47 / 45 / 42% | 35 / 42 / 39 / 39 / 36% | 75 / 97 / 90 / 90 / 89% | 56 / 46 / 41 / 40 / 37% | – |

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

## Concerns

1. **Two SRD 5.2 rules beyond the issue's suggestions**, Sneak Attack's ally clause and Vex. Without them the caps leave the AI rogue 10–36% under its band (c2/c3). Both are SRD-faithful and content-only (they can be reverted in `abilities/rogue.json`), but they are design calls the owner hasn't made.
2. **The AI values Hide as control, not damage.** A self buff is weighed at the profile's `control` weight: 0.26 for a level-1 skirmisher, against several points for closing in. So the AI hides on about 10% of its turns, and never steps back into cover after striking. Ruling 6 asked for no new planner mode since the AI does hide. Valuing hiding as the Sneak Attack it unlocks, and letting move–act–move retreat to a tile where the bonus action can Hide, is the obvious next step if L9–10 stay short.
3. **A ranged rogue is strong at levels 1–3** (1.6–1.9× the band mean with a shortbow, whether hiding or not). The Sim's rogue carries a rapier, so the band doesn't see it. Options: the ally clause for melee only, Vex for melee weapons only, or accept it (it was higher before).
4. **The scripted policy is a heuristic, not an optimum.** It took three fixes before it played at least as well as the AI; see The simulator. Its numbers bound "a human who hides every turn it can", not every possible human.

