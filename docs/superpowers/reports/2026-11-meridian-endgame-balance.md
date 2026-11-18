# Meridian: endgame balance (M3 and M7 simulation)

- **Date:** 2026-11-18, after the owner's decisions on the first M7 run. M3's first five scenarios were tuned on 2026-11-14 ([#367](https://github.com/higharc91/ArcBox/issues/367)). M7's seven, and the table below for all twelve, come from plan Task 10, issue [#417](https://github.com/higharc91/ArcBox/issues/417).
- **Build:** branch `ccr-de425e76-4vxzw2`, the commit that adds this report.
- **Players:** bots in every seat: the Explore bot until the Breach, then each scenario's role bots. **These are bot numbers, not people.** They catch a broken or lopsided scenario; they don't replace a playtest.
- **Targets** (spec §7.3):
  - the crew win 40–60% at every player count (traitor and co-op scenarios);
  - no free-for-all turn position wins more than 1.3× its fair share;
  - under 10% of games are decided by the round limit;
  - the Breach fires 35–55% of the way through a game's turns;
  - a median game runs 45–60 minutes.

## How it was run

```sh
dotnet build games/Meridian/Meridian.Sim -c Release
dotnet run -c Release --no-build --project games/Meridian/Meridian.Sim -- endgame --games 1000 --players 3-6 --seed 1
```

Each row is 1,000 games (seeds 1–1000) with the scenario forced.

**Columns:**
- *Win rate* depends on the scenario's declared archetype:
  - for traitor and co-op scenarios, the crew's side;
  - for timed escapes, the share of seats that survive;
  - for free-for-alls, how often anyone won, and how much the most successful turn-order position (counted from the endgame's first turn) beats a fair share.
- *Breach at* is the share of a game's turns played before the Breach.
- *Est. minutes* assumes 40 s per exploring turn, 55 s per endgame turn and 90 s of briefings. That's an assumption, not a measurement.

## Final results

After the owner's decisions (below): the Breach threshold is 7, and each holding side has a second way to win.

| Scenario | Players | Games | Ended | Win rate | Round limit decided | Breach at (share of turns, median) | Endgame turns (median) | Deaths per game | Est. minutes (median) |
|---|---|---|---|---|---|---|---|---|---|
| captain_on_deck | 3 | 1000 | 100% | crew 49% | 47% | 49% | 26 | 0.0 | 38 |
| captain_on_deck | 4 | 1000 | 100% | crew 53% | 47% | 43% | 34 | 0.1 | 46 |
| captain_on_deck | 5 | 1000 | 100% | crew 57% | 43% | 40% | 36 | 0.1 | 51 |
| captain_on_deck | 6 | 1000 | 100% | crew 72% | 28% | 47% | 27 | 0.1 | 43 |
| cold_sleep | 3 | 1000 | 100% | crew 25% | 44% | 44% | 33 | 0.1 | 45 |
| cold_sleep | 4 | 1000 | 100% | crew 42% | 58% | 36% | 48 | 0.3 | 57 |
| cold_sleep | 5 | 1000 | 100% | crew 54% | 46% | 35% | 39 | 0.1 | 55 |
| cold_sleep | 6 | 1000 | 100% | crew 63% | 37% | 40% | 33 | 0.0 | 49 |
| company_asset | 3 | 1000 | 100% | crew 63% | 10% | 58% | 16 | 0.8 | 32 |
| company_asset | 4 | 1000 | 100% | crew 59% | 23% | 42% | 31 | 1.3 | 44 |
| company_asset | 5 | 1000 | 100% | crew 51% | 17% | 38% | 38 | 2.4 | 52 |
| company_asset | 6 | 1000 | 100% | crew 49% | 30% | 32% | 53 | 2.8 | 66 |
| decaying_orbit | 3 | 1000 | 100% | survive 64% | 0% | 53% | 20 | 1.1 | 34 |
| decaying_orbit | 4 | 1000 | 100% | survive 72% | 0% | 47% | 26 | 1.1 | 40 |
| decaying_orbit | 5 | 1000 | 100% | survive 79% | 0% | 44% | 31 | 1.0 | 44 |
| decaying_orbit | 6 | 1000 | 100% | survive 81% | 0% | 39% | 38 | 1.1 | 51 |
| hull_rot | 3 | 1000 | 100% | crew 34% | 32% | 46% | 25 | 0.2 | 39 |
| hull_rot | 4 | 1000 | 100% | crew 49% | 31% | 41% | 30 | 0.2 | 45 |
| hull_rot | 5 | 1000 | 100% | crew 77% | 18% | 44% | 28 | 0.2 | 43 |
| hull_rot | 6 | 1000 | 100% | crew 83% | 12% | 45% | 28 | 0.2 | 44 |
| last_clean_sample | 3 | 1000 | 100% | 53% claimed; top turn position 1.15x fair | 0% | 59% | 17 | 1.4 | 30 |
| last_clean_sample | 4 | 1000 | 100% | 65% claimed; top turn position 1.10x fair | 0% | 55% | 20 | 1.4 | 34 |
| last_clean_sample | 5 | 1000 | 100% | 75% claimed; top turn position 1.28x fair | 0% | 51% | 22 | 1.2 | 37 |
| last_clean_sample | 6 | 1000 | 100% | 67% claimed; top turn position 1.04x fair | 0% | 46% | 28 | 2.0 | 43 |
| one_of_us_isnt | 3 | 1000 | 100% | crew 46% | 44% | 48% | 26 | 0.1 | 38 |
| one_of_us_isnt | 4 | 1000 | 100% | crew 59% | 40% | 43% | 31 | 0.1 | 44 |
| one_of_us_isnt | 5 | 1000 | 100% | crew 73% | 27% | 41% | 34 | 0.1 | 48 |
| one_of_us_isnt | 6 | 1000 | 100% | crew 69% | 32% | 36% | 42 | 0.1 | 56 |
| salvage_rights | 3 | 1000 | 100% | 96% claimed; top turn position 1.20x fair | 4% | 68% | 10 | 0.0 | 26 |
| salvage_rights | 4 | 1000 | 100% | 97% claimed; top turn position 1.18x fair | 3% | 62% | 13 | 0.0 | 29 |
| salvage_rights | 5 | 1000 | 100% | 98% claimed; top turn position 1.23x fair | 2% | 59% | 15 | 0.0 | 31 |
| salvage_rights | 6 | 1000 | 100% | 99% claimed; top turn position 1.22x fair | 1% | 57% | 17 | 0.0 | 34 |
| scuttle_charge | 3 | 1000 | 100% | crew 32% | 2% | 56% | 17 | 0.0 | 32 |
| scuttle_charge | 4 | 1000 | 100% | crew 51% | 2% | 50% | 22 | 0.0 | 37 |
| scuttle_charge | 5 | 1000 | 100% | crew 47% | 1% | 49% | 23 | 0.0 | 38 |
| scuttle_charge | 6 | 1000 | 100% | crew 53% | 0% | 46% | 27 | 0.0 | 43 |
| the_passenger | 3 | 1000 | 100% | crew 38% | 6% | 57% | 15 | 1.3 | 31 |
| the_passenger | 4 | 1000 | 100% | crew 42% | 3% | 53% | 20 | 1.9 | 35 |
| the_passenger | 5 | 1000 | 100% | crew 57% | 1% | 52% | 20 | 2.2 | 36 |
| the_passenger | 6 | 1000 | 100% | crew 59% | 1% | 49% | 24 | 2.6 | 39 |
| tiller_says_no | 3 | 1000 | 100% | crew 51% | 48% | 53% | 23 | 0.7 | 34 |
| tiller_says_no | 4 | 1000 | 100% | crew 51% | 49% | 46% | 30 | 1.2 | 42 |
| tiller_says_no | 5 | 1000 | 100% | crew 69% | 31% | 44% | 30 | 1.4 | 45 |
| tiller_says_no | 6 | 1000 | 100% | crew 51% | 49% | 37% | 44 | 2.2 | 55 |
| vent_cycle | 3 | 1000 | 100% | survive 67% | 0% | 59% | 15 | 1.0 | 30 |
| vent_cycle | 4 | 1000 | 100% | survive 75% | 0% | 55% | 18 | 1.0 | 33 |
| vent_cycle | 5 | 1000 | 100% | survive 80% | 0% | 53% | 20 | 1.0 | 36 |
| vent_cycle | 6 | 1000 | 100% | survive 83% | 0% | 51% | 23 | 1.0 | 38 |

## What was tuned in M7

These are content numbers, plus two levers the content reads. Each "before" is that scenario's first 300-game run.

- **Company Asset:**
  - The transmit target is one lower with three aboard (`easierAtThree`).
  - Before: the crew won 26% with 3 players.
- **Captain on Deck:**
  - A fourth command core with five or more aboard (`extraCoresAtFive`).
  - Before: the crew won 67–77% with 5–6 players.
- **Cold Sleep:**
  - Two relays (was three), reroute 3+ (was 4+), and sleepers with Agility 1 (was 2).
  - At most two sleepers awake at once (was six).
  - Before: the crew won 2–36%.
  - A rule fix came out of tuning. With no sleeper awake at the start, the crew could reroute both relays in the first round and win before the waker acted. So one sleeper starts awake, and the crew's win is judged at a round's end, as the approved rule says.
- **Scuttle Charge:**
  - Search reach and bot fixes:
    - A search sweeps the compartment and its neighbours.
    - The crew bots stay in company: whoever is alone is who can plant.
    - The kit is the target plus one spare.
  - Then: five charges needed with 3–4 aboard, four with 5–6.
  - Before: the crew won 0–6%.
- **Tiller Says No:**
  - The vent save is 3+ (was 4+). Lethal vents (below) had dropped the crew to 35–51%.

## The owner's decisions (2026-11-18), and what they did

1. **A second way to win for each holding side**:
   - One of Us Isn't: the mimic strikes, and wins when one crew member is left.
   - Tiller Says No and Captain on Deck: vents kill someone already hurt who fails the save.
   - Captain on Deck: the captain also wins when one crew member is left.
   - Scuttle Charge: the crew win once the saboteur can't reach the target.
   - Cold Sleep and Hull Rot already had one.

   Effect on the share decided by the round limit:
   - **Scuttle Charge fell from 30–54% to 0–2%.** Its crew now win by finding and disarming charges.
   - **One of Us Isn't went from 26–58% to 27–44%, and Captain on Deck from 26–51% to 28–47%.** The new paths fire only sometimes: striking is public, and deaths stay rare.
   - **Tiller Says No rose, from 16–42% to 31–49%.** Lethal vents thin the crew, and fewer hands pull fewer banks before time runs out. It's still a race against Tiller, now with real danger in it.
2. **Vent Cycle's suits are scattered,** each at least three steps from the Airlock. **Survival is unchanged at (n − 1)/n.** The bots still fetch a suit in time, whatever the cycle (two rounds or three) or the distance (three steps or four). With one suit fewer than the players, exactly one seat dies. That's the scenario's design, not a number: who goes without. A race needs fewer suits or a hazard in the way. That's a design change to make only if the game night finds it flat.
3. **The Breach threshold is 7.** A game gains about 3–5 minutes, and 13 of 48 rows now estimate 45–60 minutes (was 12). The cost is Breach timing: ten rows now fall outside the 35–55% target. Nine fire late (up to 68%): Salvage Rights at every count, and the 3-player rows of four more. Company Asset at 6 players fires early (32%). Most games still estimate 30–45 minutes, so the length lever is in the endgames, not Explore. People play slower than bots, so the game night should settle it.

## What was tuned in M3

The first pass (300 games each, before tuning) is at the end. Changes, all in the scenarios' content files or their scaling rules:

- **The Passenger:** the starting brood scales with the players (two fewer than the players, at least one), and brood stop spawning at one fewer than the players. Before: the crew won 25% with 3 players and 63% with 6.
- **One of Us Isn't:** one repair point per player, plus two for each player above four (it was a flat four). Before: the crew won 35% with 3 players and 91% with 5.
- **Tiller Says No:** banks need Tech 5+ (was 4+), vents need Agility 4+ (was 3+), and the banks needed are one per player, at least three (was a flat three). Before: the crew won 98–100%.
- **Decaying Orbit:** engines need Tech 6+ (was 5+) and the orbit lasts 7 rounds (was 8). Before: 80–98% of seats survived.
- **The report itself:** the free-for-all's fairness is measured by turn-order position. The first version measured seat index, which in a bot game is the crew member (seat *n* always gets crew *n*), so it was measuring who has the best Agility.

## Where the targets hold

| Target | Holds for | Doesn't hold for |
|---|---|---|
| Crew wins 40–60% | Company Asset at 4–6; Captain on Deck at 3–5; Cold Sleep at 4–5; Hull Rot at 4; One of Us Isn't at 3–4; Scuttle Charge, The Passenger at 4–6; Tiller at 3, 4 and 6 | 3 players in five scenarios (Cold Sleep 25%, Scuttle Charge 32%, Hull Rot 34%, The Passenger 38%; Company Asset 63%). Captain on Deck at 6 (72%); Cold Sleep at 6 (63%); Hull Rot at 5–6 (77–83%); One of Us Isn't at 5–6 (69–73%); Tiller at 5 (69%) |
| Free-for-all turn position ≤ 1.3× fair | Salvage Rights (1.18–1.23×) and The Last Clean Sample (1.04–1.28×) at every count | — |
| Round limit decides < 10% | The Passenger, Salvage Rights, The Last Clean Sample, Decaying Orbit, Vent Cycle, Scuttle Charge | Captain on Deck, Cold Sleep, One of Us Isn't and Tiller (27–58%); Company Asset (10–30%); Hull Rot (12–32%): see below |
| Breach at 35–55% of turns | 38 of 48 rows | 10 rows: Salvage Rights at every count; Company Asset, The Last Clean Sample, Scuttle Charge, The Passenger and Vent Cycle at 3 (56–59%); Company Asset at 6 (32%) |
| Median game 45–60 minutes | 13 of 48 rows | 34 rows estimate 26–44 minutes; Company Asset at 6 runs 66 |

## Structural findings

**1. The round limit still decides most games in four scenarios,** after the second ways to win: Captain on Deck, Cold Sleep, One of Us Isn't and Tiller Says No. The new paths fire, but the bots rarely press them. In One of Us Isn't, striking unmasks the mimic. In Cold Sleep, the waker's overrun is real but the crew's clearing is slow. People may press harder than bots, so the game night should judge whether these play as races with real danger. If they still drift to the limit, the next lever is per scenario. Raise the hazard rate:
- Tiller vents two compartments late in the game;
- the waker wakes faster near the limit;
- the captain gains Authority faster.

**2. Three players is a thin crew.** At 3 players, five scenarios sit outside 40–60%: four low (Cold Sleep 25%, Scuttle Charge 32%, Hull Rot 34%, The Passenger 38%) and Company Asset high (63%). With three seats, the "crew" is two people against one opponent and the ship. Each scenario has a 3-player content lever, but pushing them further trades away the 4–6 player rows, so a 3-player playtest should come first.

**3. More hands make some scenarios easy:** Hull Rot reaches 83% at 6 players, and Captain on Deck 72%. A spread or Authority that scales with the players is the obvious next lever if a playtest agrees.

### M3's finding: One of Us Isn't and Tiller Says No can only be lost at the round limit

In **One of Us Isn't** and **Tiller Says No** the crew almost never lose any other way. Tiller's vents rarely kill, and the mimic's bot never attacks anyone. So the crew's losses *are* round-limit endings, and the "under 10% decided by the limit" target can't be met at any crew win rate near 50%. No number fixes that; each needs a second way for the crew to lose. Options, for the owner to decide:

1. **Tiller escalates:** each round it vents one more compartment, or a vent that catches someone already hurt kills.
2. **The mimic strikes:** its bot attacks a crew member it finds alone (the scenario already allows it), and a crew reduced to one survivor loses.

**Decision (owner, 2026-11-17): keep them as designed.** *Superseded on 2026-11-18: after the M7 run, the owner chose a second way to win for every holding side (see "The owner's decisions" above). Options 1 and 2 were taken in that form: lethal vents, and the mimic striking.* In both scenarios the round limit is the pressure: the crew finish in time or lose. Neither option is taken. The "under 10% decided by the limit" target doesn't apply to these two. Revisit after the first game night if they play as a race with no opponent.

## Other findings

- **The Last Clean Sample** is a fair free-for-all: the top turn position takes 1.04–1.28× its fair share. Someone is cured in 53–75% of games. The rest end with everyone dead, which is the scenario's threat working.
- **Hidden-traitor bots only have to be adequate** (spec §6), and the simulator shows why that matters. Until the crew bots stayed in company, Scuttle Charge's saboteur won almost every game. People do that naturally; the numbers here assume they will.
- **Decaying Orbit has no win-rate target** (every seat is for itself). Survival of 65–83% leaves the escape-or-help choice with real stakes.

## M3's first pass, before tuning (300 games each)

| Scenario | Players | Games | Ended | Win rate | Round limit decided | Breach at (share of turns, median) | Endgame turns (median) | Deaths per game | Est. minutes (median) |
|---|---|---|---|---|---|---|---|---|---|
| decaying_orbit | 3 | 300 | 100% | survive 80% | 0% | 51% | 21 | 0.6 | 31 |
| decaying_orbit | 4 | 300 | 100% | survive 89% | 0% | 48% | 20 | 0.4 | 33 |
| decaying_orbit | 5 | 300 | 100% | survive 98% | 0% | 54% | 16 | 0.1 | 30 |
| decaying_orbit | 6 | 300 | 100% | survive 98% | 0% | 50% | 20 | 0.1 | 34 |
| one_of_us_isnt | 3 | 300 | 100% | crew 35% | 65% | 42% | 29 | 0.0 | 40 |
| one_of_us_isnt | 4 | 300 | 100% | crew 58% | 42% | 40% | 33 | 0.0 | 44 |
| one_of_us_isnt | 5 | 300 | 100% | crew 91% | 9% | 46% | 23 | 0.0 | 36 |
| one_of_us_isnt | 6 | 300 | 100% | crew 90% | 10% | 44% | 25 | 0.0 | 39 |
| salvage_rights | 3 | 300 | 100% | 95% claimed; top seat 1.34x fair | 5% | 64% | 10 | 0.0 | 24 |
| salvage_rights | 4 | 300 | 100% | 99% claimed; top seat 2.02x fair | 1% | 59% | 13 | 0.0 | 27 |
| salvage_rights | 5 | 300 | 100% | 99% claimed; top seat 1.87x fair | 1% | 57% | 15 | 0.0 | 29 |
| salvage_rights | 6 | 300 | 100% | 100% claimed; top seat 1.83x fair | 0% | 53% | 17 | 0.0 | 32 |
| the_passenger | 3 | 300 | 100% | crew 25% | 4% | 55% | 15 | 1.5 | 29 |
| the_passenger | 4 | 300 | 100% | crew 35% | 1% | 50% | 20 | 2.2 | 32 |
| the_passenger | 5 | 300 | 100% | crew 56% | 0% | 51% | 20 | 2.1 | 33 |
| the_passenger | 6 | 300 | 100% | crew 63% | 0% | 48% | 22 | 2.4 | 36 |
| tiller_says_no | 3 | 300 | 100% | crew 98% | 2% | 65% | 10 | 0.0 | 24 |
| tiller_says_no | 4 | 300 | 100% | crew 100% | 0% | 64% | 11 | 0.0 | 25 |
| tiller_says_no | 5 | 300 | 100% | crew 100% | 0% | 66% | 11 | 0.0 | 25 |
| tiller_says_no | 6 | 300 | 100% | crew 100% | 0% | 63% | 12 | 0.0 | 27 |
