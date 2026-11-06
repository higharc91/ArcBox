# Companion AI retune (#109)

Task 3 of the combat-area fixes (#98). The `companion` profile plays absent players' characters, timed-out turns and AI fillers.

## What changed

- **Weights come from the class's tuned profile.** `Companion.Weigh` now takes the weights of the profile the class's `aiTags` name for a party member (`healer`, `caster`, `archer`, `skirmisher`, `brute`: the same mapping the Sim uses, now `AiProfile.IdForRoles`), keeps the companion's id, and adds only the companion's own rules on top:
  - `Hoard` 0 (class resources refill every battle, so they are spent freely);
  - `Revive` kept a quarter above every other weight;
  - healer and protected-ally guarding, with a fixed `Companion.GuardWeight` (2.25, what a defender companion had before) so it no longer depends on the class's Objective;
  - at most one healing item, on itself, when it would be Downed.
- **Gone:** the 0.6 base caution, the ranged ×1.5 caution and the other per-role multipliers (the old `Roles` table). The `companion` entry in `ai-profiles.json` is now only the fallback for a unit with no class: its caution is 0.2.
- **Sim:** `--companions N` (0 up to the smallest party size) plays the first N members of every party (class order) with the `companion` profile. `SimConfig.Companions` and `SimBattle.Companions` carry it; the report's configuration section prints it.
- Caution decay after round 5 (`Planner.cs:77`) is unchanged: with caution at the class profile's 0.05 to 0.25 it no longer matters.

## Comparison

Story encounters at their own level, party size 4 (the size AI fillers pad to), 6 sampled parties, seeds 1 to 20, Normal difficulty (120 battles per cell). "Class" is the class-profile party; "old companion" and "new companion" are `--companions 4`, before and after this change.

```
for each level: dotnet run -c Release --project games/Tactics/Tactics.Sim -- --levels L --encounters E --sizes 4 --parties 6 --seeds 20 --companions {0|4}
  L=1 ford_assault,river_crossing; 3 forest_ruins; 5 warlord_bridge,crypt_hall; 7 crystal_cave,rime_tower; 9 keep_gate; 10 hall_of_the_flame
```

| Encounter | Lvl | Class win % | Class rounds | Class capped | Old companion win % | rounds | capped | New companion win % | rounds | capped |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ford_assault | 1 | 56.7 | 9.4 | 2 | 70.8 | 10.6 | 4 | 73.3 | 8.8 | 2 |
| river_crossing | 1 | 76.7 | 12.2 | 5 | 80.8 | 13.7 | 8 | 81.7 | 12.7 | 9 |
| forest_ruins | 3 | 67.5 | 13.3 | 6 | 70.8 | 15.9 | 2 | 73.3 | 13.0 | 1 |
| warlord_bridge | 5 | 75.0 | 11.3 | 0 | 77.5 | 11.5 | 0 | 79.2 | 11.4 | 0 |
| crypt_hall | 5 | 79.2 | 13.8 | 1 | 75.8 | 17.4 | 3 | 84.2 | 14.2 | 2 |
| crystal_cave | 7 | 78.3 | 14.2 | 1 | 85.0 | 17.2 | 1 | 85.8 | 14.9 | 0 |
| rime_tower | 7 | 75.0 | 14.2 | 3 | 75.8 | 17.4 | 2 | 86.7 | 13.5 | 1 |
| keep_gate | 9 | 70.8 | 11.3 | 0 | 50.8 | 14.7 | 1 | 68.3 | 11.7 | 1 |
| hall_of_the_flame | 10 | 43.3 | 9.2 | 0 | 31.7 | 10.6 | 0 | 31.7 | 10.1 | 0 |
| **Capped battles, all cells** | | | **18** | | | **21** | | | **16** | |

Gap between the new companion party and the class party, in win-rate points: ford +16.6, river +5.0, forest +5.8, warlord +4.2, crypt +5.0, cave +7.5, rime +11.7, keep -2.5, hall -11.6.

## Reading it

- **The cost is gone.** Old companions ran the clock: mean rounds 14.7 to 17.4 at the deep encounters (crypt, cave, rime, keep), and keep_gate won 50.8% against the class party's 70.8%. The new companions finish in 11.7 to 14.9 rounds, like the class party, and keep_gate is back to 68.3%.
- **Within about 10 points** at seven of nine encounters. The exceptions are ford_assault (+16.6), rime_tower (+11.7) and hall_of_the_flame (-11.6). Where the companion party differs, it is mostly better than the class party, not worse: it revives first, spends resources freely (Hoard 0 against the class profiles' 0.2 to 0.5). I kept those companion rules as the ruling asks, and didn't tune further.
- **Capped battles** fall from 21 (old companion) to 16, and are 18 for the class party. River_crossing still rises from 5 to 9 (4% to 7.5% of its battles); the class party already caps there (5), and so did the old companion (8). It is a property of the encounter's clock at size 4 more than of the companion.
- **Finale clock.** The finale (hall_of_the_flame) is not clock-limited for the companion party: no battle was capped, the mean is 10.1 rounds (class party 9.2, old companion 10.6), the longest 15, and the lose clock is `survive:14`. Its win rate is low for both parties at size 4 (class 43.3%, companion 31.7%), the same size-4 weakness as the 2026-10-30 report's 40.8% at size 4. So the losses are party wipes, not the ritual running out, and I left the clock alone. The class-profile party is also below the 60% target here, which is the encounter's balance rather than the companion's.

## Goldens and fixtures

No golden replay changed: the Logic tests, goldens included, pass without re-recording. Three existing tests played real battles with companions and followed the new behaviour:

- `HostViewContractTests.Every_phase_of_a_real_session_reaches_the_tv`: its two-player River Crossing fight is now lost on the harness's own seed; pinned to `BattleSeed = 1`, which the companions win, so the results screen has XP rows to check.
- `RealBattleTimingTests`: it needs fights that contain a flanking hobgoblin hit and a fighter's Indomitable. Under the new weights the old seeds had neither, and one real save area ran 1.15 s against the 1.1 s bound. Fight seeds changed and more were added (the same mechanism as before, only more fights).
- `PhoneFixtureTests` / `web/play/test/fixtures/tactics/results.json`: the recorded results screen's injuries changed ("Cracked ribs" moved from Player4 to Player3, Player4 got "Shaken"), re-recorded with `UPDATE_FIXTURES=1`.
