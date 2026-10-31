# Task 23 review: findings to resume from

Task 23's commit `98b06c5` was reviewed on 2026-10-30 and came back **Needs fixes**. No fix round has run yet. The session's git-ignored ledger stays on the local machine, so this file carries what the next session needs.

## How to resume

1. Read the [handoff guide](2026-10-30-tactics-rules-engine.handoff.md), then the [implementer's report](2026-10-30-tactics-rules-engine.task-23-report.md).
2. Run fix round 1 of 5 on the Important findings below. The first implementer can't be resumed, so dispatch a fresh one with the Task 23 brief, its report and these findings. Use `98b06c5` as the base of the task and the head the review saw.
3. Run a scoped re-review of the fix diff, using the handoff's re-review instructions.
4. Move the Minor findings to the tracker's deferred table, then mark Task 23 done.
5. Run the whole-branch final review, triage the deferred table and check `AiPerformanceTests` for flakiness. Then finish the branch.

Any tuning change means a new sweep and refreshed report tables. Re-record goldens with `UPDATE_GOLDENS=1` only after the tuning is final, and check each diff.

## Verified by the review

- The report's tables match its appendix: win rates, means, medians, cap counts, band rows and the 55 rare abilities.
- "What changed" lists every tuning change across the 30 content files.
- No `Tactics.Logic` or test code changed, and no target or flag threshold was weakened.
- Golden changes are explained by tuning and lose no verification.
- The README covers the Sim, chapter packs and the conventions, apart from the Minor points below.

**Couldn't be verified from the diff:** the p95 figure (none is recorded), "13 of seeds 1–60", the baseline "44 of 60" and the sweep-round rows (their output isn't stored), and the 1,859 passing tests, which the reviewer was told not to re-run.

## Important findings

1. **The 2% target is over-claimed** (report lines 7, 16, 169–171). The brief allows under-2% use only when documented as situational. Line 171 heads 10 abilities "not situational: real players would use these", such as `fighter.action_surge` at 0 of 22,379 turns. The 16 "Outranked" abilities aren't situational either, and line 169 says 45 reasons "weren't checked ability by ability". The honest headline is two of four targets met, with this one partly met. The tracker said "three of its four"; that has been corrected.
2. **Criterion 4 is marked Met on proxy evidence** (report line 37). `AiPerformanceTests.cs:42-46,60` runs river_crossing plus 4 added monsters, at level 10 with triple HP, not crypt_hall or crystal_cave. Task 23 made those two true 10-enemy samples, so the spec's "worst sample encounter" was never timed. Time the real encounters, or mark it met by proxy with the caveat.
3. **Levels 3 and 7 lose power, unexplained.**
   - Monk: 2d8 → d10 at level 3, so 9 → 5.5 per hit with the same attacks (`abilities/monk.json:6`).
   - Eldritch Blast: 2d8 → d10 at level 3 (`abilities/warlock.json:7`), against the spec's "scales by level" (spec line 348).
   - Bard: Healing Word 3d4 → 2d4 and Cure Wounds 3d8 → 2d8 at level 7 (`abilities/bard.json:45,50`).
   - Report lines 78, 86 and 92 give the values, but no reason explains the drop. They look fitted to the five sampled levels.
4. **Sorcerer picks can't be paid for at the levels that grant them, and this is undocumented.** Sorcery is 1 at levels 1–2 and reaches 4 only at level 7 (`classes/sorcerer.json:9`). Create Mana (2, `abilities/sorcerer.json:10`) and Twinned (2, line 23) can't be paid at level 2. Quickened (4, line 41) is a default pick from level 2 (`classes/sorcerer.json:55`) and can't be paid until level 7. Draw Sorcery can't bridge this, because `grantResource` caps at max without `overMax` (`Blocks/Combat.cs:39`). Report line 84 doesn't mention it.
5. **The spell ladder is inverted, and tuning only covered default picks.** Lightning Bolt stays 8d6 at the same tier and cost as Fireball at 3d6 (`abilities/wizard.json:144`, `abilities/sorcerer.json:162`). It's in both classes' choose lists but no one's `defaultPicks`, so it's absent from the appendix. Tier 1 Burning Hands at 3d6 equals Fireball, and sorcerer tier 5 Cone at 2d8 is below tier 1 Burning Hands. Tune these or document them.

## Minor findings

- **Numbers-only scope:** Hide moved to level 1 (`classes/rogue.json:11-12`); a rapier swap (`classes/rogue.json:6`) whose reason at report line 77 covers only Hide; new monster `saves` lists (`monsters/goblinoids.json:18,31`); rogue 3–5 attacks and monk 2–3, while other classes get Extra Attack at 5 (spec lines 340–350). **Needs a decision from the user:** accept or reject these.
- **`warlord_bridge` design:** the implementer changed the boss's start tile, the collapse count (to 1) and the win (`survive:11`) because the collapse was killing the boss. **Needs a decision from the user** with the item above.
- **Spec example drift:** report line 128 notes `survive:8`, but not that the `rogue.sneak_attack` example at spec line 320 no longer matches.
- **Imprecise "What changed":** report line 84 says cantrips reach 2 dice at level 7. Only Fire Bolt, Acid Splash and Chill Touch moved; Ray of Frost, Shocking Grasp and Poison Spray stay at 5.
- **Fix-ups described too narrowly:** the sweep-rounds intro says they "only repair", but fix-ups 2 and 4 also retuned crystal and spider HP and Second Wind dice.
- **Deviation lists are incomplete:** report line 228 and `games/Tactics/README.md:49` omit rogue Edge, fighter Superiority, Second Wind and barbarian Rage.
- **README pack steps:** step 5 can't be run with a dependent pack, because the Sim loads one `--rules` folder (`Tactics.Sim/Program.cs:69`). The steps never say where the pack folder goes, and the intro's Sim command duplicates the new section's.
- **Unsampled levels:** levels 2, 4, 6, 8 and 9 aren't simulated, and the report doesn't warn about that.
- **Warlord targets are held by the timer:** `survive:11` makes round 12 the ceiling, so median 12 is max 12. Win rate by level is 66.4, 73.1, 91.9, 96.9 and 99.4, but report line 227 only describes off-level extremes. Give the split of timer wins against boss kills.
- **Common actions:** `common.shove` (0.0%) and `common.disengage` (0.2%) are under 2% and undocumented, because the sim excludes `common.*`.
- **Showcase test:** `GoldenTests.cs:111-123` doesn't assert `ford_jeer`, and the comment at line 124 still says "survive 8 rounds".
- **Monster stats leak across levels:** hobgoblin and goblin_boss `attacks` and dice aren't level-gated, and `cr` is unchanged, so a level-1 CR 1/2 hobgoblin deals 2 × (2d8+3). No current encounter does this.
- **Subclass dice lag:** `open_hand.sweeping_kick` costs 1 ki for 1d6 (`abilities/monk.json:69`), below the free Martial Arts die.

## Rulings made on the user's behalf

- Task 23 tunes numbers in content only; deferred approximations that need engine changes stay for the final review. Cost if wrong: a target may stay unmet.
- Tuning was capped at about 10 sweep rounds, with unmet targets reported as unmet. Cost if wrong: another pass. The implementer ran 11 rounds, 4 fix-up sweeps and a final sweep.
- `warlord_bridge` keeps a showcase test requiring every trigger, and may change seed. Cost if wrong: one seed change. It moved from seed 11 to 15.

Task 22's fixes also carry one ruling for the user to confirm: `goblin_boss` was raised to 70 HP at level 5 so one Fireball couldn't skip its phase. Task 23 has since raised it to 240.

## Resolution (fix round 1, 2026-10-31)

All five Important findings are resolved. The tracker's "Task 23 fix round 1 and the whole-branch review" section has the details.

1. **The usage target** is reported as partly met: 29 situational, 26 not.
2. **The timing criterion** is timed on the real 10-enemy encounters: p95 23.5 and 24.9 ms.
3. **The step-down dice** are explained with each class's output per round, which rises at every level. Smoothing them is a decision for the user.
4. **Sorcerer affordability** is fixed (Sorcery 2 at level 2; Quickened from level 7), and `ContentLadderTests` enforces it for every class.
5. **The area-spell ladder** is fixed and enforced by `ContentLadderTests`.

The Minor findings that need the user's decision (the scope beyond numbers, the warlord_bridge redesign) are listed in the tracker. The rest are in the deferred table, which the whole-branch review triaged.

No re-review agent was run on this round. The whole-branch review covered the engine, AI, content loading and Sim code after these changes.
