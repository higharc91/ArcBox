# Task 23 report: balance pass and final report

**Status:** DONE_WITH_CONCERNS. One target is unmet (role bands).
**Commit:** `98b06c5` chore(tactics): balance pass and final report. Local only, not pushed.

The balance report has the full detail: `docs/superpowers/reports/2026-10-30-tactics-balance.md` (What changed, every sweep round, the 55 rare abilities, criteria 1–6, and the raw simulator output).

## Final numbers against the four targets

Full sweep: all encounters, levels 1, 3, 5, 7, 10, sizes 4, 5, 6, 6 parties per size, 20 seeds, Normal. 9,000 battles.

| Target | Result |
|---|---|
| Every class within ±15% of its band at every level | **UNMET:** 52 of 60 in band (baseline: 16 of 60). Out: rogue L3 −15.6%; fighter L5 +20.3%, L7 +23.1%, L10 +18.0%; paladin L7 −17.9%; barbarian L10 −24.7%; druid L10 +35.9%; wizard L10 −35.9% |
| No ability under 2%, or documented | **Met by documentation:** 55 under 2%, all 55 listed with a reason (cross-checked against the report's ids). 10 are AI planner limits, not situational |
| Every encounter won 60–95% at its level | **MET:** river 90.8, forest 86.9, crypt 92.8, warlord 91.9, crystal 93.1 |
| Median battle 8–12 rounds | **MET:** river 9, forest 9, crypt 11, warlord 12, crystal 12; 9.0 overall. Crystal's mean is 12.3, so the sim's mean-based flag still says LONG |

Best explanation for the misses: mitigated counts attacks drawn, so it rewards staying up (fighter) and counts resistance as less mitigation (barbarian). No encounter is built for level 10, so those rows come only from easy fights; there, the AI puts the wizard's concentration on Haste. Fixing either needs `Tactics.Sim` or planner work, not more numbers. No `Tactics.Logic` change is needed for any target.

## What changed and why

Summary only; each change and reason is in the report.

- **AI weights:** cut `caution` (units circled out of threat range) and `control` on martial profiles (Dodge beat Dash). `companion` is unchanged.
- **Strikers:** more attacks and sneak dice for the rogue (Hide free, from level 1), more attacks and a bigger die for the monk, large cuts to area spells and pools for the sorcerer, cuts for ranger and warlock burst, Eldritch Blast up.
- **Healers and control:** bard heal dice and early Mana up, cleric early Devotion down, wizard area spells down, druid Mana reshaped, Moonbeam 1 round.
- **Defenders:** weaker Second Wind, less Superiority, more Rage and Rage temporary HP.
- **Monsters and encounters:** HP, damage and counts for all five. No encounter starts with more than 10 enemies.
- **warlord_bridge:** the real cause of 100% wins was the collapse dropping the boss into the chasm, which met `defeat:warlord`. He now starts at (13, 5), the bridge collapses at 1 party member, the win is `survive:11`, he has 240 HP and saves, and there are 4 tougher hobgoblins.

## Sweep results per round

The full table is in the report's "Sweep rounds". Values out of band by round: baseline 44, then 42, 42, 34, 37, 28, 29, 21, 14, 17, 11 (15 at 20 seeds), round 11: 5. Then fix-ups: 7, 8, 10, 8, final 8.

I went past the cap of about 10: 11 tuning rounds, 4 fix-up sweeps and the final one. Tuning stopped at round 11. The later sweeps only repaired what tests caught or a change that did harm. That's why the final 8 is worse than round 11's 5.

## Tests

Commands, with `export PATH="/c/Users/fatma/AppData/Local/Microsoft/dotnet:$PATH"`:

```
UPDATE_GOLDENS=1 dotnet test games/Tactics/Tactics.Logic.Tests
  total 494, failed 1, succeeded 493
  (the expected failure: Warlord_bridge_plays_the_showcase_triggers ran before the replay was written)

dotnet test ArcBox.slnx
  Test run summary: Passed!  total: 1859  failed: 0  succeeded: 1859

dotnet build ArcBox.slnx
  0 Warning(s)  0 Error(s)
```

No test was weakened and no test file changed, except the replay data below.

Golden diffs, each reviewed:
- `river-crossing.events.json`: bigger goblin and archer hits, and the re-aimed Magic Missile.
- `river-crossing.replay.json`: **one hand-written command changed.** Magic Missile targets `goblin.3`, not `goblin.4`. With 16 HP the fighter's 11 damage no longer kills it alone, and the test requires `Died`. Not covered by your golden instructions, so please check you're happy with it.
- `warlord-bridge.replay.json` and `.events.json`: re-recorded on seed 15. Seed 11 no longer brings the boss below 50%. 13 of seeds 1–60 play the whole showcase.
- `skirmish-basic`: no change.

## Files changed

- 30 files in `content/tactics/base/rules/`: `ai-profiles.json`, 11 in `abilities/` (barbarian, bard, druid, fighter, monk, monsters, ranger, rogue, sorcerer, warlock, wizard), 9 in `classes/` (all but paladin, warlock and wizard), 4 in `encounters/` (all but river_crossing, whose changes are in the monster file), 5 in `monsters/` (beasts-1, goblinoids, humanoids, undead, wildshape).
- `docs/superpowers/reports/2026-10-30-tactics-balance.md` (new)
- `docs/superpowers/plans/2026-10-30-tactics-rules-engine.progress.md`: task 23 done, Status refreshed, Task 22 showcase note marked as changed, new deferred row for task 23.
- `games/Tactics/README.md`: running the Sim, adding a chapter pack, content conventions.
- `games/Tactics/Tactics.Logic.Tests/Golden/`: the four files above.

## Self-review findings

- Tests caught four overreaches, which I undid rather than loosening the tests: 5 river goblins (pinned at 6), `companion` caution (the sim doesn't use it), wizard L1 Mana, fighter L1 Second Wind.
- I took crypt and crystal down to 10 enemies. Both had reached 11, past the spec's "6 vs 10" worst case, though no test caught that (the perf test uses its own setup).
- Goading Attack at 2 Superiority took it to 0.0% use for no gain, so I reverted it and reran the final sweep.
- Numbers quoted in flavour text were updated for monk and Hail of Thorns.
- `attacks` counts and `startingKit` weapon are not strictly "dice, tables, costs, weights". Rogue and monk needed them. Hide moving to level 1 is a feature-table change, and the warlord's start tile and win expression are encounter data. All are content numbers, with no new mechanics, but they stretch the brief a little.
- The rare-ability reasons in the first report table come from reading the planner. The others are from data and traces, and the report says so.
- The perf criterion is evidenced by the passing test. I couldn't get the measured p95 printed, so the report gives no figure.

## Concerns

1. **The role-band target is unmet** (8 of 60), so spec criterion 2 is only partly met. The fix for most of them is changing the measure or the planner, not more tuning.
2. **Fireball is 3d6 and the rogue has 5 attacks at level 9.** Those are what the band target needed, and they're far from the SRD. The Striker band compares total damage per round, which punishes area spells. The user may prefer a different measure to these numbers.
3. **The numbers fit this AI's play.** A human setting up every Sneak Attack will far outperform the AI at them. `companion` didn't get the caution change, so absent players' AI still plays cautiously.
4. **Subclasses are unmeasured**, because the simulator plays none. Spells now differ by class too: Cone of Cold is 2d8 for the sorcerer and 3d8 for the wizard.
5. **Parties of 4 win 63–84%, and parties of 6 win 99–100%.** Encounters don't scale with party size.
6. **Some pools no longer match the content conventions** (bard, cleric, druid, sorcerer, ranger Focus, warlock costs). The README notes it. Mana cost = tier still holds.
7. **The warlord's phase is reached in only 13 of 60 seeds** with the golden party.
8. **The spec's §4 example still shows `survive:8`.** It's only an example, so I left it.
