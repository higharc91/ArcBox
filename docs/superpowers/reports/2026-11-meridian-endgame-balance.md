# Meridian: endgame balance (M3 simulation)

- **Date:** 2026-11-14
- **Build:** branch `ccr-de425e76-4vxzw2`, the commit that adds this report. Plan Task 10, issue [#367](https://github.com/higharc91/ArcBox/issues/367).
- **Players:** bots in every seat: the Explore bot until the Breach, then each scenario's role bots. **These are bot numbers, not people.** They catch a broken or lopsided scenario; they don't replace a playtest.
- **Targets** (spec §7.3): crew wins 40–60% at every player count (traitor and co-op scenarios); no free-for-all turn position wins more than 1.3× its fair share; under 10% of games decided by the round limit; the Breach 35–55% of the way through a game's turns; a median game of 45–60 minutes.

## How it was run

```sh
dotnet build games/Meridian/Meridian.Sim -c Release
dotnet run -c Release --no-build --project games/Meridian/Meridian.Sim -- endgame --games 1000 --players 3-6 --seed 1
```

Each row is 1,000 games (seeds 1–1000) with the scenario forced, about 2 minutes 40 seconds in all.

**Columns.** *Win rate* depends on the archetype: the crew's side for traitor and co-op scenarios; for Decaying Orbit, the share of seats that survive; for Salvage Rights, how often anyone claimed the Prize and how much the most successful **turn-order position** (counted from the endgame's first turn) beats a fair share. *Breach at* is the share of a game's turns played before the Breach. *Est. minutes* assumes 40 s per exploring turn, 55 s per endgame turn and 90 s of briefings; that's an assumption, not a measurement.

## Final results

| Scenario | Players | Games | Ended | Win rate | Round limit decided | Breach at (share of turns, median) | Endgame turns (median) | Deaths per game | Est. minutes (median) |
|---|---|---|---|---|---|---|---|---|---|
| decaying_orbit | 3 | 1000 | 100% | survive 65% | 0% | 49% | 20 | 1.0 | 32 |
| decaying_orbit | 4 | 1000 | 100% | survive 74% | 0% | 43% | 26 | 1.0 | 37 |
| decaying_orbit | 5 | 1000 | 100% | survive 81% | 0% | 40% | 31 | 0.9 | 42 |
| decaying_orbit | 6 | 1000 | 100% | survive 83% | 0% | 37% | 37 | 1.0 | 48 |
| one_of_us_isnt | 3 | 1000 | 100% | crew 42% | 58% | 42% | 29 | 0.0 | 38 |
| one_of_us_isnt | 4 | 1000 | 100% | crew 58% | 43% | 38% | 34 | 0.0 | 44 |
| one_of_us_isnt | 5 | 1000 | 100% | crew 74% | 26% | 37% | 34 | 0.0 | 46 |
| one_of_us_isnt | 6 | 1000 | 100% | crew 68% | 32% | 33% | 43 | 0.0 | 56 |
| salvage_rights | 3 | 1000 | 100% | 96% claimed; top turn position 1.15x fair | 4% | 63% | 10 | 0.0 | 24 |
| salvage_rights | 4 | 1000 | 100% | 98% claimed; top turn position 1.17x fair | 2% | 59% | 13 | 0.0 | 27 |
| salvage_rights | 5 | 1000 | 100% | 99% claimed; top turn position 1.15x fair | 2% | 56% | 15 | 0.0 | 29 |
| salvage_rights | 6 | 1000 | 100% | 99% claimed; top turn position 1.30x fair | 1% | 54% | 17 | 0.0 | 32 |
| the_passenger | 3 | 1000 | 100% | crew 33% | 8% | 52% | 17 | 1.4 | 30 |
| the_passenger | 4 | 1000 | 100% | crew 43% | 3% | 50% | 20 | 1.9 | 33 |
| the_passenger | 5 | 1000 | 100% | crew 57% | 1% | 49% | 20 | 2.2 | 33 |
| the_passenger | 6 | 1000 | 100% | crew 57% | 1% | 45% | 24 | 2.7 | 38 |
| tiller_says_no | 3 | 1000 | 100% | crew 58% | 42% | 49% | 21 | 0.1 | 32 |
| tiller_says_no | 4 | 1000 | 100% | crew 65% | 35% | 43% | 26 | 0.1 | 38 |
| tiller_says_no | 5 | 1000 | 100% | crew 84% | 16% | 42% | 27 | 0.0 | 40 |
| tiller_says_no | 6 | 1000 | 100% | crew 72% | 28% | 35% | 38 | 0.1 | 50 |

## What was tuned

The first pass (300 games each, before tuning) is at the end. Changes, all in the scenarios' content files or their scaling rules:

- **The Passenger:** the starting brood scales with the players (two fewer than the players, at least one), and brood stop spawning at one fewer than the players. Before: the crew won 25% with 3 players and 63% with 6.
- **One of Us Isn't:** one repair point per player, plus two for each player above four (it was a flat four). Before: the crew won 35% with 3 players and 91% with 5.
- **Tiller Says No:** banks need Tech 5+ (was 4+), vents need Agility 4+ (was 3+), and the banks needed are one per player, at least three (was a flat three). Before: the crew won 98–100%.
- **Decaying Orbit:** engines need Tech 6+ (was 5+) and the orbit lasts 7 rounds (was 8). Before: 80–98% of seats survived.
- **The report itself:** the free-for-all's fairness is measured by turn-order position. The first version measured seat index, which in a bot game is the crew member (seat *n* always gets crew *n*), so it was measuring who has the best Agility.

## Where the targets hold

| Target | Holds for | Doesn't hold for |
|---|---|---|
| Crew wins 40–60% | The Passenger at 4–6 players; One of Us Isn't at 3–4; Tiller at 3 (58%) | The Passenger at 3 (33%); One of Us Isn't at 5–6 (68–74%); Tiller at 4–6 (65–84%) |
| Free-for-all turn position ≤ 1.3× fair | Salvage Rights at every count (1.15–1.30×) | — |
| Round limit decides < 10% | The Passenger, Salvage Rights, Decaying Orbit | One of Us Isn't (26–58%) and Tiller (16–42%): see below |
| Breach at 35–55% of turns | Most rows | Salvage Rights at 3–5 players (56–63%, its endgames are short); One of Us Isn't at 6 (33%) |
| Median game 45–60 minutes | One of Us Isn't at 5–6, Decaying Orbit and Tiller at 6 | Most rows estimate 24–44 minutes |

## A structural finding: two scenarios can only be lost at the round limit

In **One of Us Isn't** and **Tiller Says No** the crew almost never lose any other way. Tiller's vents rarely kill, and the mimic's bot never attacks anyone. So the crew's losses *are* round-limit endings, and the "under 10% decided by the limit" target can't be met at any crew win rate near 50%. No number fixes that; each needs a second way for the crew to lose. Options, for the owner to decide:

1. **Tiller escalates:** each round it vents one more compartment, or a vent that catches someone already hurt kills.
2. **The mimic strikes:** its bot attacks a crew member it finds alone (the scenario already allows it), and a crew reduced to one survivor loses.

Until one is chosen, these two scenarios work and end, but read as a race against the clock rather than against an opponent.

## Other findings

- **Games run short** by the length estimate, mostly 30–45 minutes. Bots move efficiently and never deliberate. People will be slower, so this is the number most likely to move in a playtest; it's not a reason to lengthen scenarios yet.
- **More players don't always mean easier.** The scaling makes One of Us Isn't and Tiller harder for 6 than for 5. That's the extra points and banks, and it's acceptable while they're within reach.
- **Decaying Orbit has no win-rate target** (every seat is for itself). Survival of 65–83% leaves the escape-or-help choice with real stakes.

## First pass, before tuning (300 games each)

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
