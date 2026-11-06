# Wardenflame combat rules and AI (#98): implementation plan

**Spec:** GitHub issue #98 and its 11 sub-issues (#105–#115) in higharc91/arcbox. Each task below embeds its issue's text verbatim under "Issue text", then adds "Rulings". The rulings are decisions made for this plan. Where a ruling and the issue text differ, the ruling wins.

The rules-engine design is `docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md`. The deferred findings and "Decisions for the user" are in `docs/superpowers/plans/2026-10-30-tactics-rules-engine.progress.md`.

**Owner decisions (2026-11-06):**
- **#105:** the owner's phone controls summons.
- **#106:** guard bosses only. Ordinary units still die in deep tiles.
- **#108 and #112:** apply the issues' suggestions, then retune with the simulator.
- **Pushing:** work is pushed to its own branch, `wardenflame-combat-98`. The controller pushes, never the implementer.

## Global Constraints

These bind every task.

### Workspace

- **Work only in the worktree** `/home/user/ArcBox/.claude/worktrees/wardenflame-combat-98`, on branch `worktree-wardenflame-combat-98`.
- **Never touch the main checkout** at `/home/user/ArcBox`. Another agent works there.
- **Never push.**
- **Never run bare `git stash`.** The stash is shared with other worktrees.

### Environment

- **.NET:** run `export DOTNET_ROOT=/root/.dotnet PATH=/root/.dotnet:$PATH` before any `dotnet` command.
- **Shared CPU.** The machine has 4 cores and another agent shares them.
  - While iterating, run focused tests. Tests use xunit v3 on Microsoft.Testing.Platform, for example: `dotnet test --project games/Tactics/Tactics.Logic.Tests -c Release -- --filter-class "*DelayTests*"`.
  - Run the full suite `dotnet test ArcBox.slnx -c Release` once, before your final commit.
- **Phone code** is in `web/play`.
  - Run `npm ci` there once if `node_modules` is missing.
  - Use `npm test` (vitest) and `npm run build` (type-check and build).

### Engine rules

- **`Tactics.Logic` stays deterministic and pure:**
  - no clock, `System.Random`, `Guid.NewGuid` or file IO in the rules;
  - sorted collections only;
  - all randomness through the battle's PCG32 `Rng`.
- **State stays immutable:** `Apply(state, command)` returns a new state plus events.
- **Old saves keep working.** New fields on persisted records (`TacticsDocument`, `BattleState`, content defs) need defaults.

### Content and balance

- **Follow the README's "Content conventions"** (`games/Tactics/README.md`), including the area ladder, `ContentLadderTests`, "Affordable on arrival" and "Content may add, never override".
- **When you move a rule away from SRD 5.2 or the spec, say so** in the README's content conventions or in the ability's tooltip text.
- **Golden replays.** If a change alters a golden replay:
  - re-record with `UPDATE_GOLDENS=1 dotnet test --project games/Tactics/Tactics.Logic.Tests -c Release`;
  - run again without the flag;
  - in your report, say which goldens changed and why.
- **Balance.** A task that changes balance runs `Tactics.Sim` (see the README), and writes its report under `docs/superpowers/reports/2026-11-06-combat-98/`.
  - The targets are the README's: 60–95% wins and 8–12 rounds at each encounter's level, and role bands within ±15%.
  - Iterate at `--seeds 6` and judge at `--seeds 20`.

### Tests and commits

- **Test-first.** Every behaviour change gets a test that fails before the change and passes after. Rules go in `Tactics.Logic.Tests`, the adapter in `Tactics.Game.Tests`, and the phone in `web/play` vitest.
- **Fixtures follow the views.** When a phone view type changes, update the TypeScript types and the phone fixtures that render it.
- **Commits** use the repo's conventional style, for example `fix(tactics): …` or `feat(tactics): …`, naming the issue, for example `(#115)`.
- **Every commit message ends with these two lines:**
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01UZNTTVCBKLVWRDHcmTGbSi
  ```
- **Never put a model name** in code, comments or commit messages.


## Task 1: Small combat rules fixes (#115)

**Issue #115:** Wardenflame [P3]: small combat rules fixes (Delay double turn, Downed countdown window, Cracked Ribs scaling, RNG resume check)

### Issue text

**P3 · combat** · four small, independent fixes. Each can be its own PR.

**1. Delay gives back-to-back double turns.**
- **Why:** Delay is free and can move a turn to the last slot (`Rules/Battle.cs:157-165`), the order is re-sorted every round (`:479`, `Rules/TurnOrder.cs:260-283`), and players win ties. A fast party can act at the end of round R and again first in R+1, before any enemy responds. The AI never delays, so it's a one-sided exploit, and it breaks the exact "you're up in 3" promise (spec §3.2).
- **Fix:** a unit that delayed acts at its delayed position next round too, or Delay costs the Reaction.
- **Done when:** a rules test shows a unit that delayed to the end of a round can't take the next round's first turn ahead of the enemies it waited past.

**2. The Downed countdown window varies between about 2 and 3 rounds.**
- **Why:** countdowns tick for every Downed unit at the start of each round (`Rules/Downed.cs:93-97`, `Rules/Battle.cs:488`). A hero Downed on the last turn of a round loses a tick almost at once, but the TV and phone always show "3". Each attack is a separate hit (`Effects/Blocks/Rolls.cs:141`), so a cruel multi-attacker (Vorga in phase 2) can take a hero from Downed to Out in one turn.
- **Fix:** tick at the start of the Downed unit's own (skipped) turn, as SRD death saves do.
- **Done when:** a rules test shows a hero Downed at any point in a round goes Out at the start of their 3rd own turn if nobody hits them.

**3. Cracked Ribs can nearly halve a level-1 hero's HP.**
- **Why:** `injuries.json:2` sets max HP −5, with weight 3 of 20. A level-1 wizard drops to about 2–3 HP, and injuries stack, clamped at 1 HP (`Content/SheetBuilder.cs:141-153,170`). At level 10 the same injury is about 8%.
- **Fix:** a percentage or `byLevel` value, and cap stacking of the same injury.
- **Done when:** no single battle injury removes more than about 20% of a level-1 character's max HP, with a content test at every level.

**4. A zeroed RNG in a resumed save hangs the room.**
- **Why:** with `State = Inc = 0`, `Rng.Below`'s rejection loop never exits (`Core/Rng.cs:9,32-49`). Resume runs `Invariants.Check` (`TacticsGame.cs:183`), but it has no RNG check. Tracked as progress.md deferred T1.
- **Fix:** add an invariant that `Inc` is odd, and use the existing invalid-save path on failure.
- **Done when:** a test that resumes a save with a zeroed `Rng` gets a called-off battle, not a hang.

### Rulings

- **Ruling (Delay):** a unit that uses Delay keeps its new place in the turn order for later rounds. Its initiative becomes the slot it delayed to, so the issue's first fix option applies, and "you're up in N" stays exact. Delay stays free.
- **Ruling (Downed countdown):** a Downed unit's countdown ticks at the start of its own skipped turn, not at the start of the round. The TV and phone keep showing the remaining count.
- **Ruling (Cracked Ribs and stacking):**
  - Switch max-HP injuries to a percentage: Cracked Ribs becomes −15% max HP, rounded down, at least 1.
  - A hero can't hold two copies of the same injury. If the roll picks one they already have, re-roll deterministically among the injuries they don't have. If they have them all, they gain none.
  - Add a content test checking that no single injury removes more than 20% of max HP at any level from 1 to 10.
- **Ruling (RNG):**
  - Add an `Invariants.Check` rule that `Rng.Inc` is odd.
  - A resumed battle that fails it goes through the existing invalid-save path: the battle is called off.
- Implement the four fixes as four separate commits.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#115)` in the subject.
- Report as instructed by the controller.

## Task 2: Expose campaign difficulty (#114)

**Issue #114:** Wardenflame [P2]: expose the campaign difficulty setting (Story/Normal/Hard) the engine already supports

### Issue text

**P2 · design · combat**

**Problem.** A casual group that keeps losing coin-toss fights has no lever except grinding XP. A strong group can't make fights harder. The feature is already built, so exposing it is cheap.

**Evidence:**
- **Spec §5.4** makes Difficulty a campaign setting.
- **The engine has it:**
  - `AiProfile.ForDifficulty` implements the difficulty levels.
  - Story mode also blocks friendly-fire environmental hits (`Ai/Planner.Actions.cs:164`).
- **Players can't reach it:**
  - The server reads `Tactics:Difficulty` only from Development config (`src/ArcBox.Server/Games/Tactics/TacticsServiceCollectionExtensions.cs:49`).
  - Real sessions keep Normal (`TacticsGame.Battle.cs:50-51`).
  - No phone, portal or lobby UI mentions difficulty.

**Suggestion:**
- Store the difficulty in the campaign document.
- Let the VIP or leader set it from the lobby or portal, and perhaps offer it after a loss.
- Pass it into `Encounters.Setup` and show it on the TV battle HUD.

**Done when:**
- A campaign can switch between Story, Normal and Hard outside Development.
- The setting persists and reaches `BattleState.Difficulty`, with a Game test.

### Rulings

- **Ruling (storage):** store `Difficulty` (Story, Normal or Hard) in `TacticsDocument`, defaulting to Normal, so old documents read as Normal. It reaches `BattleState.Difficulty` through `Encounters.Setup` for every battle, including roaming and Hollow fights.
- **Ruling (who and when):**
  - Only the VIP can change it.
  - It can be changed from the in-session party menu (the VIP's session options) at any time outside a battle.
  - It can also be changed from the portal, if the portal has a campaign-settings surface. If it doesn't, the session menu alone is enough. Don't build a new portal page.
- **Ruling (after a loss):** after a lost battle, the VIP's results screen shows a one-line hint, "Too hard? Change difficulty in the party menu". It is shown only when difficulty isn't already Story.
- **Ruling (display):** the TV battle HUD shows a small "Story" or "Hard" tag when difficulty isn't Normal.
- **Ruling (development override):** the `Tactics:Difficulty` Development config stays as an override for testing. When it's set, it wins over the campaign value. Document this in the README.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#114)` in the subject.
- Report as instructed by the controller.

## Task 3: Retune the companion AI and simulate it (#109)

**Issue #109:** Wardenflame [P1]: retune the companion AI (absent players, fillers, timeouts) and add it to the simulator

### Issue text

**P1 · balance · combat + characters** · tracked in `2026-10-30-tactics-rules-engine.task-23-report.md` Concern 3, and the balance report's "AI weights"

**Problem.** The `companion` profile still has the cautious weights that Task 23 removed from every other profile. It plays:
- absent members;
- timed-out turns;
- every AI filler that pads a small group to 4.

It is never simulated, so it hangs back, runs out clocks and lowers win rates, and none of that shows in any balance report. One profile also plays every class.

**Evidence:**
- **Too cautious:** `ai-profiles.json:101-112` gives companion caution 0.6, and the ranged role multiplies it by 1.5 to 0.9 (`Ai/Companion.cs:40`). Enemy profiles were cut to 0.05–0.25 because "units circled out of threat range".
- **Where it's used:** `TacticsGame.Battle.cs:240,242` and `TacticsGame.cs:502-512`.
- **Never simulated:** the Sim plays parties with class-mapped enemy profiles, never `companion` (`Tactics.Sim/SimRunner.cs:45-46,83`).
- **Measured cost:**
  - In the finale the companion won 1 of 10 because "more cautious (0.6) ... ran out the clock". After the clock was extended it won 6 of 20 (`reports/2026-10-30-tactics-balance.md:946-954`).
  - With companion fillers, the ford rework's capped battles rose from 2 to 11 (`ford-rework/integration/balance.md:49`).
- **Slow decay:** caution only fades after round 5 (`Ai/Planner.cs:77`).

**Suggestion:**
- Derive companion weights from the class's tuned profile, keeping the companion-only item and revive rules. Or at least lower caution to about 0.15–0.25.
- Add a Sim option to play N party members with `companion`, and report it beside the class profiles.
- Revisit the finale clock afterwards.

**Done when:**
- The Sim can run a companion-played party.
- At each story encounter's level, its win rate is within about 10 points of the class-profile party, with no rise in capped battles.

### Rulings

- **Ruling (weights):** take the issue's first option. A companion's scoring weights come from the class's tuned profile. Keep the companion-only behaviour on top: item use, reviving Downed allies, and not wasting resources an absent player would want kept. Remove the 0.6 caution and the ranged ×1.5 multiplier.
- **Ruling (Sim option):** add a `Tactics.Sim` option, `--companions N` (N = 0..party size), that plays N party members with the companion profile. Report the result beside the class-profile result.
- **Ruling (finale clock):** look at the finale clock only if the simulator shows the companion party is clock-limited there. Record what you find either way.
- Write the comparison report under `docs/superpowers/reports/2026-11-06-combat-98/companion.md`.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#109)` in the subject.
- Report as instructed by the controller.

## Task 4: Owner's phone controls their summons (#105)

**Issue #105:** Wardenflame [P1]: present players' summons wait 60 s every turn; let the owner control them

### Issue text

**P1 · bug · combat** · related to #95 (that issue covers only naming companions)

**Problem.** A present player's summon waits out the full 60 s turn timer every turn, then the AI plays it. That covers the Beastmaster hound, Grovekeeper sapling, Fiendbound imp and conjured wolves. The phone never offers the summon a turn screen. Spec §4.5 says "their owner's phone controls them". From level 3 (level 5 for a druid), each round gains a dead minute per summon, which reads as "the game froze".

**Evidence:**
- **Summons inherit the summoner's controller:** `Tactics.Logic/Effects/Blocks/Units.cs:38`, `new UnitState(id, sheet, summoner.Controller, ...)`. So they are `PlayerControlled(owner)`.
- **The adapter waits on them:** it treats any `PlayerControlled` unit with an online owner as played (`Tactics.Game/TacticsGame.Battle.cs:45-46`) and waits on it with the 60 s timer (`:216`).
- **The phone can't act for them:**
  - `UnitOf(profile)` maps a profile only to `heroN` (`TacticsGame.Battle.cs:40-41`).
  - The turn screen needs `state.Active == unit` (`TacticsGame.Views.cs:601`).
  - `BattleInput` drops commands when `state.Active != unit` (`TacticsGame.Battle.cs:341`).
  - `TurnTimedOut` (`:435`) then hands the turn to the companion AI.
- **Nothing in Tactics.Game handles summons:** `SummonedBy` is never read.
- **Affected content:** `ranger.json:154` (hound), `druid.json:255` (sapling), `warlock.json:252` (imp), `druid.json:151` (conjure_animals).
- **Controller isn't updated:** a summon made while its owner is absent stays `AiControlled` after they return. `Battle.Hand` refuses to re-hand summons (`Rules/Battle.Control.cs:16`).

**Suggestion:** pick one.
- **(a) Phone control:** map each summon to its owner's profile through `SummonedBy`. The owner's turn screen and input then accept the summon while it is active, with a "Hound's turn" header.
- **(b) AI control:** make summons always `AiControlled` with a pet profile, so they act at once.

Either way, let `Battle.Hand` re-hand a summon when its owner's controller changes.

**Done when:**
- In a Game test with a present Beastmaster, the hound's turn either shows on the owner's phone and accepts a command, or plays without the turn timer starting.
- A summon's controller follows its owner on disconnect and reconnect.
- No summon turn waits for `Timers.Turn`.

### Rulings

- **Owner decision:** fix option (a). The owner's phone controls summons, as spec §4.5 says.
- **Ruling (mapping):** while a summon is active, the adapter maps it to its owner's profile through `SummonedBy`.
  - The owner's turn screen and input accept commands for the active summon.
  - The header reads "<Summon name>'s turn", for example "Hound's turn".
  - The phone shows the summon's own abilities and economy.
- **Ruling (timer):** the summon's turn now belongs to a present player, so it uses the normal turn timer. On timeout the AI plays it, as for heroes.
  - This reinterprets the issue's "No summon turn waits for `Timers.Turn`" as: no summon turn waits on the timer without being offered to its owner.
- **Ruling (absent owners):**
  - When the owner is absent or disconnected, the summon is AI-controlled with the companion profile.
  - `Battle.Hand` must re-hand summons whenever their owner's controller changes, in both directions.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#105)` in the subject.
- Report as instructed by the controller.

## Task 5: Guard bosses against deep-tile pushes; edge-aware AI (#106)

**Issue #106:** Wardenflame [P1]: one Shove into a deep tile kills a boss outright; make the AI edge-aware

### Issue text

**P1 · design · combat**

**Problem.** Pushing any non-party unit onto a deep tile kills it whatever its HP, and skips its boss phases. In the level-10 finale, Maren Sorrel can be shoved into lava with one Action and fails a fighter's shove 75% of the time. The AI neither uses this nor defends against it, and AI pushes can drop heroes into chasms by accident.

**Evidence:**
- **The kill:** a push onto deep is treated as dropping to 0 HP (`Effects/Blocks/Motion.cs:77-81`), and non-party units die (`Rules/Downed.cs:45-55`).
- **No protection:** nothing gives size or forced-movement immunity (`Content/Defs.cs:132-157,361`).
- **Shove is open to everyone:** `common.shove` is one Action, a STR save against DC 8 + proficiency + STR (`abilities/common.json:28-33`). Content has 53 more push and pull effects.
- **Hall of the Flame:**
  - Maren starts at (9,4), height 3, and (9,3) is lava flagged Hazard+Deep. The plateau is ringed by lava.
  - The win condition is `defeat:maren_sorrel`.
  - She has STR 10 and no save proficiency (`monsters/villains.json`).
- **Warlord Bridge:** Vorga (240 HP, STR 10) fights on a bridge and ford 1–2 tiles wide.
- **Phases are skipped:** `hpBelow` triggers need the unit to still be in battle (`Rules/Triggers.cs:68-69`).
- **The AI ignores where a push lands:**
  - It scores a push as 0.3 × control × tiles (`Ai/Scoring.Effects.cs:81-84`).
  - Its position score has no edge term (`Ai/Planner.Positions.cs:37-98`).
  - `common.shove` is used in 0.0% of legal turns (task-23 review).

**Suggestion:**
- Give bosses and Large units a forced-movement guard: an `immovable` or size flag, or a DEX save to catch the edge.
- Make falling in hurt rather than kill: fall damage, then climb out or be placed on the nearest bank. Lava damage can come later.
- Guarantee phases: stop a phased unit at its threshold and fire the phase.
- Teach the AI to score a push by where it lands, and to treat edge tiles as risky.

**Done when:**
- A rules test shows a boss pushed toward deep isn't killed outright.
- An AI scenario test shows the AI preferring a push into a hazard and avoiding ending a turn on an edge next to a hero who can shove.
- Hall of the Flame can't be won by a round-1 shove.

### Rulings

- **Owner decision:** guard bosses only. Ordinary enemies and heroes keep today's behaviour: pushed into a deep tile, a non-party unit dies.
- **Ruling (who is guarded):** a unit is guarded if either:
  - its monster definition sets a new flag, `"steadfast": true`; or
  - it has any `hpBelow` phase trigger in the encounter.

  Set `steadfast` on the named villains in `rules/monsters/villains.json` (Maren Sorrel, Vorga, Halvard, Ivo Rime, Mother Skarrow and any other boss stat blocks) and on Ashmaw.
- **Ruling (guarded push):** a forced move that would end a guarded unit on a Deep tile instead stops on the last non-Deep tile of its path. If the path has no non-Deep tile, the unit doesn't move. Emit an event so the TV can show it, such as "Maren braces at the edge".
- **Ruling (phases):** damage that would take a unit with an untriggered `hpBelow` phase past that threshold leaves it at the threshold. The phase then fires. One hit can trigger at most one phase.
- **Ruling (AI):**
  - Score a push by where it lands. Landing in Deep or a Hazard is worth the target's remaining value, which is zero for guarded units.
  - Add a position-risk term: ending a turn on an edge tile next to a Deep tile, adjacent to an enemy who can push, costs value.
- **Done when** (in addition to the issue's list): an encounter-level test plays Hall of the Flame round 1 with a shove on Maren, and she survives.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#106)` in the subject.
- Report as instructed by the controller.

## Task 6: Multi-attack targets and once-per-turn riders (#107)

**Issue #107:** Wardenflame [P1]: multi-attacks waste attacks after a kill, and per-attack riders multiply for one cost

### Issue text

**P1 · design/balance · combat + characters** · tracked in `docs/superpowers/plans/2026-10-30-tactics-rules-engine.progress.md`, deferred T13 ("manoeuvres apply to each attack with Extra Attack")

**Problem.** `Battle.PlanUse` copies a weapon ability's whole effect list `Attacks` times onto the single target picked at plan time (`Rules/Battle.cs:200-201`). That causes two problems.

1. **Wasted attacks.** When the first attack kills the target, the remaining attacks do nothing: `RunEffectsStep` and `AttackStep` return for a Dead target (`Rules/Steps.cs:213`, `Effects/Blocks/Rolls.cs:84`). A level-1 rogue makes 3 attacks (4 at level 5, 5 at level 9), and level-1 goblins have 16 HP. SRD Extra Attack lets each attack pick a target; here it feels like a bug. The AI hides this because it caps damage at the target's HP; a person can't.
2. **Multiplied riders.** Riders repeat on every copy for one cost, so control and capstones stack far past their SRD cost. Nineteen `isWeaponAttack` rider abilities do this:
   - `monk.stunning_strike`: 3 stun saves for 1 ki at level 5 (SRD 5.2: once per turn).
   - `rogue.knock_out`: 4 "unconscious" saves for 2 Edge.
   - Trip, poison and fighter manoeuvres: one superiority die pays for every attack.
   - Crusader `searing_smite`: 2d6 plus burning on every hit for 1 oath.
   - `open_hand.quivering_palm`: 3 × (2d8+DEX plus a 4d10 save) for 3 ki, about 60 expected damage against a striker mean of 9.6.
   - Also `weaponmaster.execution_strike`, `avenger.soul_of_vengeance` and `halvard.wardens_judgement` (3 × 3d8, push and prone).

**Suggestion:**
- Let `Use` carry an ordered target list for weapon attacks, chosen on the phone as "Attack 2 of 3", or offer the remaining attacks against any foe in reach after a kill.
- Add `riderOnce` / `firstHitOnly` to abilities. Set it on every manoeuvre, Cunning and Devious Strike, Stunning Strike, Quivering Palm and smite-style ability, so the extra attacks are plain hits.
- Update the AI's `UnitEffects` to match.

**Done when:**
- A 3-attack unit that kills its first target can spend its remaining attacks on a second foe in reach.
- Trip Attack with 2 attacks makes at most one prone save, and Quivering Palm applies its 4d10 once.
- A content test fails on a resource-costed `isWeaponAttack` rider without the flag.
- The phone lets the player choose each attack's target.

### Rulings

- **Ruling (targets in `Use`):**
  - A `Use` of an `isWeaponAttack` ability may carry an ordered `Targets` list, one entry per attack. The existing single target is still accepted and means "all attacks at this target".
  - At resolution, attack i goes to `Targets[i]`.
  - If that unit is Dead, Out or no longer targetable, the attack goes to the next living listed target that's still in reach.
  - If no listed target is left, it goes to the hostile in reach with the lowest current HP (ties broken by unit id), so attacks are never wasted.
  - Remain deterministic.
- **Ruling (riders):**
  - Add `riderOnce: true` to ability definitions. When set, everything except the base weapon damage applies only on the first attack that hits.
  - Set it on every resource-costed `isWeaponAttack` rider ability the issue lists, and on any others the content test finds.
  - The content test fails on any resource-costed `isWeaponAttack` ability with `Attacks > 1` and no `riderOnce`.
- **Ruling (phone):** for a multi-attack ability, after the first target is picked, the aim screen offers "Attack 2 of N", and so on. Each defaults to the previous target and can be changed. Players who tap Use at once get all attacks at one target, with auto-retarget after a kill.
- **Ruling (AI):** `UnitEffects` and scoring must reflect `riderOnce` and spread attacks after a predicted kill.
- **Golden replays:** expect some to change. Re-record and explain them.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#107)` in the subject.
- Report as instructed by the controller.

## Task 7: Retreat from a losing battle (#111)

**Issue #111:** Wardenflame [P2]: add a way to retreat from a losing battle (BattleOutcome.Fled is coded but unreachable)

### Issue text

**P2 · design · combat + encounters**

**Problem.** A losing party has to play on until everyone is Downed. Fights that four heroes win about half the time then cost 10 or more rounds of a known loss: wasted session time and morale. A planned fallback is a tactics staple and fits the no-permadeath tone.

**Evidence:**
- **Fled exists but nothing reaches it:**
  - `BattleOutcome.Fled` exists (`State/BattleResult.cs:6`) and pays half XP (`Rules/Objectives.cs:157`).
  - No encounter uses an `endBattle` trigger, and neither the adapter nor the phone has a retreat command.
- **The exit zones are already drawn:** nine maps define an `exit` zone that no encounter references: crypt_hall, crystal_cave, forest_ruins, keep_gate, river_crossing and the four hollow_* maps.

**Suggestion:**
- Add a "Fall back" option: a party vote once k members stand in the `exit` zone, or a leader vote after round N.
- It ends the battle with `Fled`: half XP, injuries kept, no loot.
- Content can disable it for scripted fights.

**Done when:**
- A party can flee through a supported path.
- `Fled` reaches `BattleResult` and the campaign, with tests for the vote and the result.

### Rulings

- **Ruling (proposing and voting):**
  - From round 3 on, any present player can propose "Fall back" from their phone, during any player's turn.
  - That starts a party vote among present players: a strict majority of present players ends the battle. The vote times out after 20 s.
  - A failed vote can't be proposed again until the next round.
- **Ruling (exit zones):** there is no exit-zone requirement. Not every map has an `exit` zone, including the ford maps.
- **Ruling (outcome):** the battle ends with `BattleOutcome.Fled`:
  - half XP, as the existing rule says;
  - injuries kept;
  - no loot;
  - for story progress, the battle counts as not won, so the campaign treats Fled like a loss. Follow how losses are handled today.
- **Ruling (opting out):** an encounter can set `"retreat": false` to disable it. Don't disable it on any shipped encounter in this task.
- **Ruling (display):** show the proposal and the vote on the TV, as an overlay in the existing vote style, and on every phone.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#111)` in the subject.
- Report as instructed by the controller.

## Task 8: Cut the wait between turns (#113)

**Issue #113:** Wardenflame [P2]: cut the wait between a player's turns in big parties

### Issue text

**P2 · design · combat**

**Problem.** With 6 friends, most of a fight is waiting. Reaction prompts and late-round turns make it worse, and fights already run long.

**Evidence:**
- **Timers:** turns are 60 s and reaction prompts 10 s (`Tactics.Game/TacticsTiming.cs:88-90`).
- **Reactions pause everyone:** every reaction offered to a present player pauses the whole battle, including every opportunity attack, and there is no auto-answer setting (`Rules/Reactions.cs` `OfferReactionStep`; `TacticsGame.Battle.cs:196-202`).
- **AI turns are quick:** about 1 s per attack plus 0.2 s per tile (`Presentation/Beats/BeatScript.cs:13-31`).
- **Worst case:** 6 players against 10 enemies gives about 5 × 60 s + 10 × 3 s ≈ 5.5 min between one player's turns, before prompts.
- **Playtest:** the first playtest took 6+ min to reach round 3 when turns ran their timers (`reports/2026-10-31-tactics-playtest.md:21`).
- **Round counts:** River Crossing at level 1 averages 12.61 rounds, above the 8–12 target (`ford-rework/integration/balance.md:45-49`).
- **Spec budget:** 25–40 min per battle for 5 players (§5.6).

**Suggestion:** pick from these.
- (a) Let party members who are consecutive in the turn order act simultaneously. They already win tie-breaks (`Rules/TurnOrder.cs:266`).
- (b) Per-player reaction preferences that answer without a prompt: "always take opportunity attacks", "Shield only when it turns a hit".
- (c) A waiting phone can pre-pick an ability and target that runs when the turn starts, if still legal.
- (d) A shorter timer once a player has moved and acted.

**Done when:**
- A 6-bot TV run reports seconds per round and the longest wait between one player's turns.
- That wait falls by an agreed amount.
- No prompt pauses the game for a player who opted into auto-reactions.

### Rulings

- **Ruling (scope):** implement suggestions (b) and (d) only. (a) simultaneous turns and (c) pre-picked actions are out of scope; record them as follow-ups in the report.
- **Ruling (b), auto-reactions.** Add per-character reaction preferences, saved on the member record in the campaign document. They are set from the phone's battle screen, with a "Reactions" toggle sheet, and from the portal character page if that's simple. The settings are:
  - "Opportunity attacks": Ask, Always or Never. The default is Ask.
  - "Shield and other defensive reactions": Ask, "Only when it turns a hit into a miss", or Never. The default is Ask.

  A reaction the preference answers resolves at once, with no prompt and no pause.
- **Ruling (d), shorter clock.** Once a player has spent both their Move and their Action, the turn clock drops to `min(remaining, 15 s)`.
- **Ruling (measurement):** add measurement as a Game-level test or harness. It runs a 6-player, 10-enemy battle with bots and reports seconds per round and the longest wait between one player's turns, before and after. Use the bot-phone harness in `tools/tactics-playtest` if it runs headless; otherwise build a Game-level simulation with a fake clock. Put the numbers in the report.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#113)` in the subject.
- Report as instructed by the controller.

## Task 9: Let the AI use the abilities it never plans (#110)

**Issue #110:** Wardenflame [P2]: let the AI use the abilities it never plans (teleports, summons, Action Surge, bonus Dash, hit-and-run)

### Issue text

**P2 · design · combat + characters** · tracked in progress.md deferred T23 ("10 abilities are unused because of planner limits") and T14

**Problem.** These abilities are never used by absent players' characters, AI fillers or timed-out turns. The class balance was also tuned without them, so humans who use them outperform the bands.

**Evidence:**
- **0.0% use** in the balance report's "AI planner limits" table: `misty_step`, `conjure_animals`, `rogue.cunning_dash`/`withdraw`, `fighter.action_surge`, sorcerer twinned and careful, `rogue.steady_aim`, `cleric.death_ward`.
- **Empty-tile abilities are skipped** (`Ai/Planner.Actions.cs:133-134`). That includes subclass signatures: `shadow.shadowstep`, `windstep.gale_leap`, `void.rift_step`, `avenger.relentless_step`, `draconic.dragon_wings` and the elf's Fey Step.
- **Dash is main-action only:** bonus actions exclude it (`Ai/Planner.cs:154`, `Ai/Planner.Commands.cs:89`).
- **Extra actions are worthless to the AI:** `ExtraActionEffect` has no value (`Ai/Scoring.Effects.cs:137`).
- **No move–act–move:** the planner only does "act from here, then move" (`Ai/Planner.cs:130-151`).
- **Fair play:** enemy area attacks that catch a Downed hero are valued at 0 rather than avoided (`Ai/Scoring.Worth.cs:29-30`). That ticks the hero's countdown, against the spirit of spec §5.4.

**Suggestion:**
- Plan empty-tile aims near the best position tiles, scored by the post-move threat map.
- Value an extra action as the best second action, and plan a bonus-action Dash.
- Add a move–act–move candidate for the ranged and skirmisher profiles.
- Subtract value for Downed foes caught in an area unless the profile is cruel.

**Done when:**
- Each listed ability is used in at least 2% of its legal turns, or is documented as situational.
- An AI test shows an archer stepping out, shooting and stepping back.
- An enemy fireball avoids a Downed hero when a near-equal aim exists.

### Rulings

- **Ruling (scope):** implement all four suggestions in the issue:
  - empty-tile aims, scored by the post-move threat map;
  - extra-action value plus a bonus-action Dash;
  - a move–act–move candidate for the ranged and skirmisher profiles;
  - a penalty for catching Downed foes in an area, unless the profile is cruel.
- **Ruling (performance):** keep the AI inside its existing P95 budget (`AiPerformanceTests`). If the new candidates cost too much, cap how many empty-tile aims are generated, for example the top K position tiles.
- **Ruling (measurement):** measure use with the simulator's "abilities used in under 2% of legal turns" table. An ability that stays under 2% because it's situational gets a line explaining why in the report.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#110)` in the subject.
- Report as instructed by the controller.

## Task 10: Rebalance the rogue (#108)

**Issue #108:** Wardenflame [P1]: rebalance the rogue for human play (free Hide every turn, 3–5 attacks, oversized Sneak Attack)

### Issue text

**P1 · balance · combat + characters** · tracked in `2026-10-30-tactics-rules-engine.task-23-report.md` Concerns 2–3, the balance report's "Concerns for review", and progress.md "Decisions for the user" 1

**Problem.** The rogue's numbers were raised to make up for the AI rarely setting up advantage. A human who does set it up every turn is several times stronger than any other striker. The rogue also makes more attacks per action at level 1 than a level-10 fighter, which erases the fighter's identity. The Hide loop below is dominant and almost free.

**Evidence:**
- **Free Hide:** `rogue.cunning_hide` is a bonus action from level 1 with no line-of-sight, cover, distance or Stealth check (`abilities/rogue.json:13-17`). It applies `hidden`, which counts as invisible (`conditions.json:53`).
- **What hidden gives:** attacks against the rogue have disadvantage (`Effects/Blocks/Rolls.cs:125`) and its first attack has advantage (`:120`), which unlocks Sneak Attack. It ends only when the rogue itself attacks.
- **The loop:** each turn, attack with advantage plus Sneak Attack, then Hide as the bonus action. Enemies are at disadvantage against the rogue all round, even when adjacent. This strictly beats `common.dodge`, which costs the Action. The AI simply targets someone else (`Ai/Scoring.Effects.cs:181-186`).
- **Attack count:** 3, 4 and 5 attacks per Attack at levels 1, 5 and 9 (`classes/rogue.json:11,15,19`).
- **Sneak Attack dice:** 3d6, 6d6 and 9d6 (`rogue.json:142-144`). SRD is 1d6 to 5d6.
- **Human estimate:** a level-3 rogue hiding every turn deals about 31 damage per round. The measured striker mean is 4.6.
- **Steady Aim** is dominated by the free Hide and used 0% of the time.

**Suggestion:**
- Make Hide need no clear line of sight from a hostile (or half cover, or no adjacent enemy), or a Stealth roll against passive Perception, and end it when the rogue finishes its turn in plain view.
- Return to SRD-like numbers: at most 2 attacks, and Sneak Attack within 1.5× of SRD.
- Merge Steady Aim into Hide, and make advantage reachable by flanking instead.
- Measure with a scripted "sets up Sneak Attack every turn" policy, not only the AI.

**Done when:**
- Hide is refused or fails in the open next to an enemy, and works behind cover (rules test).
- The rogue makes at most 2 attacks per action.
- The simulated rogue is within ±15% of its band at levels 1–10.
- A scripted always-hidden rogue stays within 1.5× the band mean.

### Rulings

- **Owner decision:** apply the issue's suggestions, then retune with the simulator.
- **Ruling (Hide):** Hide is refused when any hostile is adjacent to the rogue, or when the rogue stands in plain view. Plain view means at least one hostile has line of sight to it and it has no cover.
  - Use the engine's existing line-of-sight and cover helpers. If there's no cover concept, use: no hostile has line of sight, or the rogue is adjacent to blocking terrain.
  - Being hidden ends when the rogue ends its turn in plain view, and when it attacks (as now).
- **Ruling (numbers):**
  - At most 2 attacks per Attack action, at every level.
  - Sneak Attack dice within 1.5× SRD per level band, at most 2d6/3d6/5d6/7d6 at levels 1/3–4/5–8/9–10. Pick the final values from the simulator.
- **Ruling (Steady Aim):** merge Steady Aim into Hide (remove the ability, or make Hide grant its effect), and add flanking advantage if the engine lacks it. Flanking means an ally on the opposite side of the target.
  - If adding flanking would change every class's balance, record that and give flanking to the rogue only, as a rogue feature.
- **Ruling (scripted check):** add a scripted "sets up Sneak Attack every turn" policy to the simulator, or as a test harness. The issue's ±15% band and 1.5× scripted bound are the acceptance bar.
- **Context:** the rules-engine progress file (`docs/superpowers/plans/2026-10-30-tactics-rules-engine.progress.md`) has "Decisions for the user" 1 and the Task 23 concerns.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#108)` in the subject.
- Report as instructed by the controller.

## Task 11: Rule on the SRD 5.2 approximations (#112)

**Issue #112:** Wardenflame [P2]: rule on the SRD 5.2 approximations players will notice (Fireball 3d6, attacks that weaken on level-up, Shove, prone…)

### Issue text

**P2 · design · combat + characters** · tracked in progress.md "Decisions for the user" 1–5 and deferred T13

**Problem.** A group that knows D&D will notice that Fireball barely hurts, that their monk got weaker at level 3, and that tooltips don't match the table. These were parked during balancing and are still open.

**Evidence:**

| Rule | Here | SRD 5.2 / spec | Where |
|---|---|---|---|
| Fireball | 3 mana, 3d6 | 8d6 | `abilities/wizard.json:139` |
| Burning Hands | 1 mana, 3d6 (same as Fireball) | | |
| Cone of Cold (sorcerer) | 5 mana, 3d6 (same again) | | |
| Monk unarmed die | 2d8, dropping to 1d10 at level 3 | grows with level | `abilities/monk.json:6` |
| Eldritch Blast and bard heals | step down on level-up | grow with level | task-23 review finding 3 |
| Shove | pushes only, STR save only | "push or knock prone" (spec §3.3); target picks STR or DEX | `abilities/common.json:28-33` |
| Standing from prone | free at turn start | costs half your Move | `conditions.json:19` |
| Charmed | only disadvantage on attacks | can't attack the charmer | `conditions.json:4` |
| Bleeding | not ended by healing | healing ends it (spec §3.7) | no healed hook (`Content/Defs.cs:76-95`) |
| Burning | no action puts it out | an action puts it out (spec §3.7) | water only puts it out in environment-enabled fights (`Effects/Blocks/SurfaceRules.cs:127`) |
| Uncanny Dodge | gives temporary HP | halves the damage | `abilities/rogue.json:69-71` |

The Striker measure counts total damage, which pushed area spells' dice down (decisions 4 and 5).

**Suggestion:**
- Put the list to the owner as one decision batch. For each item, either change the content or engine (an `onHealed` hook, prone on Shove, a stand-up cost, an "extinguish" action), or document the deviation in the README's content conventions and the tooltip.
- Score strikers by damage per target-turn, or cap the targets credited per cast, so area dice can move back toward SRD.

**Done when:**
- Each item is marked accepted-and-documented or fixed.
- No class's primary attack loses dice on level-up.
- Per-target damage: Fireball ≥ 1.5× Burning Hands, and Cone of Cold ≥ 1.5× Fireball.
- The tracker's "Decisions for the user" section is closed.

### Rulings

- **Owner decision:** apply and retune.
- **Ruling (each row of the issue's table):** fix toward SRD or the spec:
  - **Area spells:**
    - Fireball, Burning Hands and Cone of Cold dice satisfy the issue's per-target rule: Fireball ≥ 1.5× Burning Hands, and Cone of Cold ≥ 1.5× Fireball.
    - Change the striker measure first, scoring by damage per target-turn or capping the targets credited per cast, so the bands don't push area dice back down.
    - The README's area ladder must still pass.
  - **Monk unarmed die and level-up scaling:** never decreases with level. No class's primary attack, Eldritch Blast or bard heal loses dice on level-up.
  - **Shove:** the attacker chooses push 1 or knock prone. The target saves with the better of STR or DEX.
  - **Standing from prone:** costs half the unit's Move at the start of its turn.
  - **Charmed:** the charmed unit can't target the charmer with harmful abilities. Keep the existing disadvantage on other attacks only if the spec says so; otherwise drop it.
  - **Bleeding:** ends when the unit is healed. Add an `onHealed` hook in `Defs`.
  - **Burning:** add a "Put out flames" action (common ability, costs the Action) that ends burning on self or an adjacent ally. Water stays as is.
  - **Uncanny Dodge:** halves the triggering hit's damage, as a reaction, instead of giving temporary HP.
- **Ruling (deviations):** any row that can't reach SRD without breaking balance bands is kept, with the deviation written into the README's content conventions and the tooltip. The report lists each such row.
- **Ruling (tracker):** close the "Decisions for the user" section in `docs/superpowers/plans/2026-10-30-tactics-rules-engine.progress.md`. Mark each item resolved, with a link to the commit or README line.
- **Ruling (run last):** this task runs after #108, so its simulator sweep is the final balance pass for the branch. Write the full 20-seed report to `docs/superpowers/reports/2026-11-06-combat-98/balance-final.md`.

### Finish

- Run the full .NET suite once, plus `npm test` and `npm run build` if `web/play` changed.
- Commit (or commit in steps) with `(#112)` in the subject.
- Report as instructed by the controller.
