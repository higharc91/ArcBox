# Meridian: endgame balance (M3 and M7 simulation)

- **Date:** 2026-11-18. M3's first five scenarios were tuned on 2026-11-14 ([#367](https://github.com/higharc91/ArcBox/issues/367)). M7's seven, and the table below for all twelve, come from plan Task 10, issue [#417](https://github.com/higharc91/ArcBox/issues/417).
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

| Scenario | Players | Games | Ended | Win rate | Round limit decided | Breach at (share of turns, median) | Endgame turns (median) | Deaths per game | Est. minutes (median) |
|---|---|---|---|---|---|---|---|---|---|
| captain_on_deck | 3 | 1000 | 100% | crew 49% | 51% | 45% | 29 | 0.0 | 37 |
| captain_on_deck | 4 | 1000 | 100% | crew 51% | 49% | 38% | 37 | 0.1 | 44 |
| captain_on_deck | 5 | 1000 | 100% | crew 57% | 43% | 36% | 36 | 0.1 | 50 |
| captain_on_deck | 6 | 1000 | 100% | crew 74% | 26% | 45% | 25 | 0.0 | 39 |
| cold_sleep | 3 | 1000 | 100% | crew 26% | 46% | 39% | 34 | 0.1 | 43 |
| cold_sleep | 4 | 1000 | 100% | crew 41% | 59% | 33% | 48 | 0.3 | 54 |
| cold_sleep | 5 | 1000 | 100% | crew 53% | 47% | 33% | 42 | 0.1 | 54 |
| cold_sleep | 6 | 1000 | 100% | crew 62% | 38% | 38% | 32 | 0.0 | 46 |
| company_asset | 3 | 1000 | 100% | crew 54% | 15% | 48% | 20 | 0.9 | 32 |
| company_asset | 4 | 1000 | 100% | crew 53% | 28% | 35% | 35 | 1.4 | 46 |
| company_asset | 5 | 1000 | 100% | crew 47% | 20% | 32% | 41 | 2.5 | 52 |
| company_asset | 6 | 1000 | 100% | crew 42% | 34% | 27% | 58 | 3.0 | 68 |
| decaying_orbit | 3 | 1000 | 100% | survive 65% | 0% | 49% | 20 | 1.0 | 32 |
| decaying_orbit | 4 | 1000 | 100% | survive 74% | 0% | 43% | 26 | 1.0 | 37 |
| decaying_orbit | 5 | 1000 | 100% | survive 81% | 0% | 40% | 31 | 0.9 | 42 |
| decaying_orbit | 6 | 1000 | 100% | survive 83% | 0% | 37% | 37 | 1.0 | 48 |
| hull_rot | 3 | 1000 | 100% | crew 35% | 29% | 43% | 25 | 0.2 | 37 |
| hull_rot | 4 | 1000 | 100% | crew 48% | 30% | 38% | 30 | 0.3 | 43 |
| hull_rot | 5 | 1000 | 100% | crew 76% | 17% | 42% | 26 | 0.2 | 39 |
| hull_rot | 6 | 1000 | 100% | crew 84% | 10% | 43% | 26 | 0.2 | 39 |
| last_clean_sample | 3 | 1000 | 100% | 52% claimed; top turn position 1.10x fair | 0% | 55% | 17 | 1.4 | 28 |
| last_clean_sample | 4 | 1000 | 100% | 64% claimed; top turn position 1.07x fair | 0% | 50% | 20 | 1.5 | 32 |
| last_clean_sample | 5 | 1000 | 100% | 77% claimed; top turn position 1.09x fair | 0% | 47% | 22 | 1.2 | 34 |
| last_clean_sample | 6 | 1000 | 100% | 68% claimed; top turn position 1.12x fair | 0% | 43% | 28 | 2.0 | 41 |
| one_of_us_isnt | 3 | 1000 | 100% | crew 42% | 58% | 42% | 29 | 0.0 | 38 |
| one_of_us_isnt | 4 | 1000 | 100% | crew 58% | 43% | 38% | 34 | 0.0 | 44 |
| one_of_us_isnt | 5 | 1000 | 100% | crew 74% | 26% | 37% | 34 | 0.0 | 46 |
| one_of_us_isnt | 6 | 1000 | 100% | crew 68% | 32% | 33% | 43 | 0.0 | 56 |
| salvage_rights | 3 | 1000 | 100% | 96% claimed; top turn position 1.15x fair | 4% | 63% | 10 | 0.0 | 24 |
| salvage_rights | 4 | 1000 | 100% | 98% claimed; top turn position 1.17x fair | 2% | 59% | 13 | 0.0 | 27 |
| salvage_rights | 5 | 1000 | 100% | 99% claimed; top turn position 1.15x fair | 2% | 56% | 15 | 0.0 | 29 |
| salvage_rights | 6 | 1000 | 100% | 99% claimed; top turn position 1.30x fair | 1% | 54% | 17 | 0.0 | 32 |
| scuttle_charge | 3 | 1000 | 100% | crew 30% | 30% | 47% | 19 | 0.0 | 33 |
| scuttle_charge | 4 | 1000 | 100% | crew 53% | 53% | 38% | 38 | 0.0 | 46 |
| scuttle_charge | 5 | 1000 | 100% | crew 45% | 45% | 37% | 33 | 0.0 | 46 |
| scuttle_charge | 6 | 1000 | 100% | crew 54% | 54% | 31% | 56 | 0.0 | 64 |
| the_passenger | 3 | 1000 | 100% | crew 33% | 8% | 52% | 17 | 1.4 | 30 |
| the_passenger | 4 | 1000 | 100% | crew 43% | 3% | 50% | 20 | 1.9 | 33 |
| the_passenger | 5 | 1000 | 100% | crew 57% | 1% | 49% | 20 | 2.2 | 33 |
| the_passenger | 6 | 1000 | 100% | crew 57% | 1% | 45% | 24 | 2.7 | 38 |
| tiller_says_no | 3 | 1000 | 100% | crew 58% | 42% | 49% | 21 | 0.1 | 32 |
| tiller_says_no | 4 | 1000 | 100% | crew 65% | 35% | 43% | 26 | 0.1 | 38 |
| tiller_says_no | 5 | 1000 | 100% | crew 84% | 16% | 42% | 27 | 0.0 | 40 |
| tiller_says_no | 6 | 1000 | 100% | crew 72% | 28% | 35% | 38 | 0.1 | 50 |
| vent_cycle | 3 | 1000 | 100% | survive 67% | 0% | 54% | 15 | 1.0 | 28 |
| vent_cycle | 4 | 1000 | 100% | survive 75% | 0% | 50% | 18 | 1.0 | 31 |
| vent_cycle | 5 | 1000 | 100% | survive 80% | 0% | 47% | 21 | 1.0 | 34 |
| vent_cycle | 6 | 1000 | 100% | survive 83% | 0% | 45% | 24 | 1.0 | 38 |

## What was tuned in M7

These are content numbers, plus two levers the content reads. Each "before" is the first 300-game run.

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
    - The kit is the target plus one spare, so at 3 players the saboteur can still win.
  - Then: five charges needed with 3–4 aboard, four with 5–6.
  - Before: the crew won 0–6%.
- **Vent Cycle, Hull Rot, The Last Clean Sample:** untouched. Their first runs met the targets that apply, or see below.

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
| Crew wins 40–60% | Company Asset at every count; Captain on Deck at 3–5; The Passenger and Scuttle Charge at 4–6; Cold Sleep at 4–5; One of Us Isn't at 3–4; Hull Rot at 4; Tiller at 3 | 3 players in most scenarios (Cold Sleep crew 26%, Scuttle Charge 30%, The Passenger 33%, Hull Rot 35%). Captain on Deck at 6 (74%); Hull Rot at 5–6 (76–84%); One of Us Isn't at 5–6; Tiller at 4–6 |
| Free-for-all turn position ≤ 1.3× fair | Salvage Rights (1.15–1.30×) and The Last Clean Sample (1.07–1.12×) at every count | — |
| Round limit decides < 10% | The Passenger, Salvage Rights, The Last Clean Sample, Decaying Orbit, Vent Cycle; Hull Rot at 6 | Every scenario whose other side wins at the limit by design: see below |
| Breach at 35–55% of turns | Most rows | Salvage Rights at 3–5 players and the shortest endgames (their endgames end fast) |
| Median game 45–60 minutes | 12 of 48 rows | 34 rows estimate 24–44 minutes (see below); two run over: Scuttle Charge (64) and Company Asset (68) at 6 players |

## Structural findings

**1. The round limit is a side's win condition in six scenarios.** In One of Us Isn't and Tiller Says No (owner's decision, below), Captain on Deck, Cold Sleep and Scuttle Charge, one side wins by holding out. Hull Rot's ship wins the same way when the rot doesn't reach the Airlock. The plan approved each of these, and the limit is their pressure. So the "under 10% decided by the limit" target can't hold for them at any balanced win rate. Scuttle Charge's crew win *only* at the limit, which mirrors One of Us Isn't. The same decision should cover all six: the target doesn't apply where the limit is a win condition.

**2. Three players is a thin crew.** At 3 players, several scenarios fall below 40% (Cold Sleep, Scuttle Charge, The Passenger, Hull Rot). With three seats the "crew" is two people against one opponent and the ship. Each scenario now has a 3-player lever in content. Pushing them further would trade away the 4–6 player rows, so I'd rather see a 3-player playtest first.

**3. Vent Cycle's race is thin.** Survival is exactly (n − 1)/n: the seat without a suit dies, and everyone else always escapes. If the Suit Lockers aren't placed, the suits fall back to the Docking Ring, which sits next to the Airlock, so getting out costs almost nothing. It meets the targets that apply (the round limit decides 0%). Whether it plays as a race needs a decision: scatter the suits on placed compartments far from the Airlock, or shorten the cycle (a one-round cycle gives 38–48% survival, but short games). **Owner's call.**

**4. Games run short,** by the length estimate: 34 of 48 rows estimate under 45 minutes against a 45–60-minute target. Spec §7.3 measures the *whole game*, and most scenarios already run 20–40 endgame turns. So the lever is the **Explore phase**: the Breach fires early (often at 35–45% of turns) and Explore turns are cheap. **Proposed, not applied:**
- raise the Breach threshold from 6 to 7, which lengthens Explore by roughly a round; or
- add a tile per deck.

Either moves every scenario at once, and the Breach-timing target (35–55%) bounds how far. Bots also play faster than people, so this is the number most likely to move in a playtest. **Owner's call:** apply one now, or wait for the first game night.

### M3's finding: One of Us Isn't and Tiller Says No can only be lost at the round limit

In **One of Us Isn't** and **Tiller Says No** the crew almost never lose any other way. Tiller's vents rarely kill, and the mimic's bot never attacks anyone. So the crew's losses *are* round-limit endings, and the "under 10% decided by the limit" target can't be met at any crew win rate near 50%. No number fixes that; each needs a second way for the crew to lose. Options, for the owner to decide:

1. **Tiller escalates:** each round it vents one more compartment, or a vent that catches someone already hurt kills.
2. **The mimic strikes:** its bot attacks a crew member it finds alone (the scenario already allows it), and a crew reduced to one survivor loses.

**Decision (owner, 2026-11-17): keep them as designed.** In both scenarios the round limit is the pressure: the crew finish in time or lose. Neither option is taken. The "under 10% decided by the limit" target doesn't apply to these two. Revisit after the first game night if they play as a race with no opponent.

## Other findings

- **The Last Clean Sample** is the fairest free-for-all: the top turn position takes 1.07–1.12× its fair share. Someone is cured in 52–77% of games. The rest end with everyone dead, which is the scenario's threat working.
- **Hull Rot gets easier with more hands:** 35% at 3 players, 84% at 6. Each extra crew member burns rot and holds the Airlock. A spread that scales with the players would flatten it. It's the obvious next lever if a playtest agrees.
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
