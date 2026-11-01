# Overworld 5d: The Story Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** all of Chapter 1 plays on the TV and phones, from the mayor's request to the ember, on either branch.

**Architecture:** the graphs in `lore/scenes` are data. A pure `SceneRun` walks them, `VoteRules` and `SkillCheck` decide, and `TacticsGame.Story.cs` adds the timers, inputs and saves. The server owns every clock: line lengths come from the clips themselves, the TV sends nothing back, and it only shows and speaks what it's told. A cursor saved on every line means nothing is lost. `Story` is a new phase, so walking can't run during a scene.

**Tech Stack:** C# / .NET 10, Godot 4.7.2 .NET, Preact, xUnit v3, Vitest.

**Spec:** [the overworld design](../specs/2026-10-31-tactics-overworld-design.md), §2, §4.2, §4.5, §4.6, and [the lore design](../specs/2026-10-31-tactics-lore-design.md), §7. Starts after [5c](2026-10-31-tactics-overworld-5c.md) is merged.

## Global Constraints

- The 5a, 5b and 5c constraints hold.
- Don't change game rules or balance numbers. Skill checks use the existing dice rules with no new maths.
- The phone screens for dialogue, voting and skill checks are the contract change the spec approved in §4.5. Existing battle screens don't change. Fixtures are regenerated and their diffs checked.
- Dialogue and votes never wait on the TV. The story has no host signal. `--drop-signals` and no TV at all must both finish a scene.
- A line with no clip shows its caption, uses a reading timer, and never blocks. The TV doesn't send it to the speech chain, because that fallback is undecided.
- A phone's view doesn't change because of what someone else did, except when the question itself changes.
- No line text changes, so no clip hash changes.
- `Tactics.Logic` stays clock-free, and rolls take their die as an argument.
- Stage files by name. Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- **A vote that loses someone's tap.** Four people voting within a second: every vote must count. Check a view diff on each phone per vote.
- **Everyone's phone locks during a long scene.** The TV keeps talking, and the first vote holds until a phone returns. Test on real phones, not just the harness.
- **A choice made by nobody that can't be undone.** `ch1_force` on an unanswered vote, a dropped leader on a tie, a vote closing as the clip plays. Check the outcome, the TV's words, and that the clip isn't cut.
- **A restart in the worst spot.** Kill the server mid-line, mid-vote, on a tie-break, between roll and reveal, during an aftermath. No double rolls, no lost votes that were decided, and no repeated flags.
- **A dead end.** A node with nothing to offer, a beat with no scene to play, a missing clip, a scene id gone after a content update. Each must end cleanly with the party walking.

## Defaults chosen while the user is away

1. **One beat at a time, in arc order.** See flag 2. If the user wants side beats later, an `after` key in `arcs.json` can relax it.
2. **Lines advance by themselves.** Each runs for its clip plus 0.5 s. The leader can skip a line. Mockup 02's ▼ implies tap to continue, but a couch group shouldn't have to tap 30 times.
3. **Non-vote options are the leader's.** The schema's "the player who's talking" (`scenes.schema.json:98`) is the leader, who tapped to interact. An unanswered one takes the first offered option with no check, so a timer never spends a roll.
4. **Vote timer 45 s.** Tie-break 10 s, roller pick 20 s, non-vote choice 30 s.
5. **Votes can change until they close.** Once everyone present has voted, a 2 s settle allows one last change. It also waits for the line's clip.
6. **The party picks the roller by a vote,** on the same rules. An unanswered pick takes the best bonus, not the first card.
7. **Skill rolls have no natural 20 or 1,** as saves and Counterspell's check (`Rolls.cs:104`, `Counterspell.cs:42`).
8. **Tags look at the whole party,** absent characters included, matching checks.
9. **Options open as the node's last line starts,** as mockup 02 shows the vote beside its question.
10. **A battle beat's lead-in plays once.** A retry goes straight to the fight.
11. **The fight follows at once when the party is already there.** See §3. Otherwise it waits at the "!".
12. **NPC figures:** the scene's first non-narrator cast member stands at the spot, with the `alert` emote over them. Doesn't block the party. Purely dressing, and easy to overrule.
13. **After Chapter 1:** a chapter card for 6 s, then free walking, no markers, and "Chapter II isn't here yet".

## Where the spec, the lore or 5b fall short

1. **`needs` doesn't exist.** The key is `requires` (`arcs.json`, for example line 32; spec §4.2).
2. **Flags don't order beats.** `ch1_old_maud` and `ch1_the_ember` have no `requires` (`arcs.json:44-49`, `:66-71`), so they're open at the start. `ch1_kindler.open` offers only `slain` and `spared` (`ch1.json:2228-2245`), so it would come up empty. `ch1_maud.open` could too. The lore warns of this (lore §7, Gated options). Also, `ch1_granary` opens with "the road is open" and `ch1_song` needs the grain that follows it, and neither `requires` says so.
3. **Lead-ins and aftermaths aren't marked.** The lore's rule (§7, Order) has no data behind it, and "first scene is the lead-in" fails for `ch4_rime_tower` and `ch5_gate_duel`, and both `ch3_the_bridge` scenes are lead-ins. Adding optional `after: true` needs the schema, because it has `additionalProperties: false` (`scenes.schema.json:69`), plus `check.py`.
4. **A scene can name another place.** Only `ch1_parley` does (`ch1.json:660`), and spec §3.1 has no spot for it. See §3.
5. **Line timers need real lengths.** Measured on all 208 Chapter 1 clips: `words × 0.36 + 1.2` is short for 30, by up to 1.9 s. The longest line is 11.0 s. A Question-priority line cuts off the one before it (`NarrationQueue.cs:41-54`), so a short timer would chop speech.
6. **"Nobody is connected: waits" would freeze the TV.** Phones sleep through scenes, and wake lock needs HTTPS (5b §7). So only decisions hold.
7. **A vote tally can't go on the phone.** `GameSession.cs:122` and `:219`, as above. Tallies are the TV's.
8. **5b's `A_step_doesnt_change_any_phone_view` needs narrowing.** The Interact button must change a view, but only when in-range flips. Its pad already handles a new viewId.
9. **5b's `ReconsiderLeader` shouldn't run mid-scene.** A returning session leader takes over when the scene ends, so a tie-break can't change hands.
10. **"Existing dice rules" exist only inside a battle.** `Rolls.D20` takes a `BattleWriter` (`Rolls.cs:48`). There's no skill-to-stat table anywhere, only `Skills` on the sheet. The table is 18 data lines of SRD fact, no new maths, and goes on the user's list.
11. **`Banner()` assumes a battle.** It falls through to `State`, which dereferences `_doc.Battle!` (`TacticsGame.Views.cs:282`), and says "Resuming the battle…" at `:280`.
12. **The mockups show things the data doesn't have.** "Talk the mayor into paying 200 gp up front", "+200 gp", and "Dusk, day 12" have no source. The scene says forty crowns and nothing pays it, since currency is sub-project 6. The check panel uses the option's label, and the result is the next scene's words.
13. **`ch1_kindler` happens at the chapel steps but has no `place`.** It plays at the docks. Adding `place` to it and to `ch1_four_more` would fix it with this runtime. Left alone: the words are unchanged, and it's the user's call.
14. **Spec §2's "read out by the narrator":** Chapter 1 has a clip per speaker. Clips are used.
15. **Lore §7's camp and town barks aren't in this slice.** Nothing in 5d needs them.

## Design

### 1. Layers

| Layer | Where | Does |
|---|---|---|
| Data | `Tactics.Logic/Story/LoreBook.cs` | Arcs, scenes, characters, clip lengths |
| Rules | `Tactics.Logic/Story/` | `StoryRules`, `SceneRun`, `VoteRules`, `SkillCheck`. Pure, no clock |
| Game | `Tactics.Game/TacticsGame.Story.cs` | Timers, inputs, saves, views |
| TV | `Tactics.Presentation/Story/`, `src/ArcBox.Host/games/tactics/` | `StoryTrack`, `StoryScreen`, then drawing |

### 2. Beats

- **Available:** the first beat in arc order that isn't done and whose `requires` hold.
- **Marker:** `ExploreView.Markers` holds that beat's spot, sprite and alert. It changes only when availability changes.
- **Interact:** the leader's walk view gains `interact: string | null` ("Talk", "Look around", "Sit by the fire", "Begin"), set within 2 legal steps of the spot. Steps matter, so a spot on top of a cliff isn't in reach from below it. Input `interact` re-checks on the server.
- **Objective:** "Go to {place}", from the place names in `regions.json`. Beat summaries are spoilers.
- **Sets:** node `sets` apply when the node plays, a choice's when it wins, option `sets` when picked. Beat `sets` apply at the end, or at the win for a battle beat. `Flags` is a set, so a replay is harmless.

### 3. Scenes in a beat

Scenes play in file order, each tested as it starts (lore §7). A 2 s gap with the title separates them.

**Battle beats:**

```
lead: unmarked scenes -> fight -> Results -> after: scenes with after: true -> beat done
```

- **Lost:** the cursor stays at stage `fight` with no scene. The "!" and the stamp re-arm, and the retry skips the lead-in.
- **Fight starts from a chain:** when a beat ends and the next is a battle whose `place` is where the party now is, the next beat starts at once. So `ch1_force`'s "Dawn, then" runs straight into `ch1_dawn`.
- **Another place:** a scene whose `place` differs moves the party to the spot of the arc's first beat in that place, behind a fade, with `Epoch`, `Arrived = "travel"` and a save. For `ch1_parley` that's `ch1_skarrow_song`'s spot. The narrator's words cover the walk. Then `ch1_river_crossing` isn't in the same place, so the party walks back to Dagna's "Ready when you are."

### 4. The states of a scene

`TacticsPhase.Story` goes at the end of the enum. `walk` is ignored there, and `pickClass` still works.

| State | Enters | Leaves by |
|---|---|---|
| `Lines` | A node | `Timers.Line` or the leader's skip |
| `Asking` | Last line of a node with options | All present voted + settle, or `Timers.Vote` |
| `TieBreak` | A tie the decider's vote can't settle | The decider, or 10 s: first tied in node order |
| `PickRoller` | A check won | All voted + settle, or 20 s: best bonus |
| `Rolled` | The roll, saved with the next node | `Reveal` timer, 4.5 s |
| `Gap` | Between scenes, and after travel | 2 s |
| `ChapterCard` | Last beat done | 6 s |

**Line time:** clip length + 0.5 s, or `words × 0.36 + 1.2` with no clip. A vote's close never beats `_lineDue`, so its question is always heard.

**Nobody connected:** lines run on. `Asking`, `TieBreak` and `PickRoller` cancel their timers and hold. The first phone back restarts the timer in full.

### 5. Votes

```csharp
public sealed record VoteTally(ImmutableArray<string> Options, ImmutableDictionary<string, string> Votes);
public abstract record VoteResult;
public sealed record Won(string Option) : VoteResult;
public sealed record Tied(string Decider, ImmutableArray<string> Options) : VoteResult;
public static VoteResult VoteRules.Close(VoteTally, string? leader, string? firstConnected, Func<ImmutableArray<string>, string> unanswered);
```

| Case | Result |
|---|---|
| A clear winner | It wins |
| Tie, leader voted for a tied option | That option |
| Tie, leader voted elsewhere or not at all | Leader gets a 10 s tie-break of the tied options only |
| Tie, no leader, someone connected | The first connected player, the same way |
| Tie-break unanswered | First tied in node order |
| No votes | `unanswered`: first offered option, as the spec says |
| A voter drops | Their vote stands. They no longer count towards "everyone" |
| Someone joins | They can vote. "Everyone" grows |
| A player with no character | Can't vote, and sees `pickClass` |

Away players (`_players` but not `_online`, `Room.cs:401-408`) aren't voters. The TV says "Dave is away and doesn't vote". Votes aren't saved, so a restart reopens the question.

### 6. Skill checks

- **Eligible:** every member with a character, here or not. Cards show who's away.
- **Bonus:** `Stats.Mod(sheet.Score(stat)) + (sheet.Skills.Contains(skill) ? Stats.Proficiency(level) : 0)`, from `SheetBuilder.ForCharacter`.
- **Roll:** `Rolls.Flat(die, modifier, vs)` shares `D20`'s success rule with `critOn20: false, autoMissOn1: false`. The die is `_ctx.Random.Next(1, 21)` from the game.
- **Saved at once:** the node after the roll goes in the cursor with the roll, so a restart can't re-roll.

### 7. Saved state

```csharp
public sealed record StorySave(string Beat, string Stage, string? Scene, string? Node, int Line, int Seq);
// WorldSave gains: StorySave? Story = null, ignored when null
```

**No version bump.** 5b added `Flags` and `Beats`. This is one more optional field, and `ArcJson.Options` rejects nothing (`ArcJson.cs:13-16`). `Read` needs no change except a test.

| Moment | Saved | How |
|---|---|---|
| Beat starts | Cursor | `Save` |
| Each line | `Line`, `Seq` | `Save` |
| Option, vote or roll decided | Flags and next node | `Save`, in the same call |
| Node `sets` | Flags | `Save` |
| Travel | Tile and cursor | `Save` |
| Battle won | Beat stage, flags | `Checkpoint`, as 5c |
| Beat done | `Beats`, cursor null | `Checkpoint` `After <beat title>` |
| Chapter end | | `Checkpoint` `End of Chapter I` |

About one save every 3 to 8 s in a scene, far below a battle's save per command (`TacticsGame.Battle.cs:345`). A cursor naming a scene or node that's gone restarts the beat from its first scene with a `_notice`.

### 8. When things go wrong

| Case | What happens |
|---|---|
| Leader disconnects | Next connected leads. An open tie-break or choice moves to them, with its timer unchanged |
| Everyone's phones sleep | Lines run on, the next decision holds |
| Server restarts mid-scene | Resume sees the cursor: phase `Resuming`, the same `_awaited` and "Continue without them" as a battle. Then the saved line replays |
| New session, same cursor | `Start` carries on at the line after Muster |
| TV reconnects | Newest line as a caption at once, with no voice. The next line speaks. Vote panel from the view |
| TV slow | Newest `Seq` wins. A skipped line is skipped |
| TV missing | Same timers. Phones carry the line's text |
| Clip missing | Caption, reading timer |
| No option offered | Log, end the scene, finish the beat. Base pack test proves it can't happen in Chapter 1 |
| Battle mid-scene | Can't happen: a fight only starts between scenes |

### 9. What reaches the TV and phones

```csharp
public sealed record StoryLineView(string Id, string Speaker, string? Name, string? Portrait, string Mood, string Text);
public sealed record StoryOptionView(string Id, string Label, string? Skill, int? Dc, ImmutableArray<string> Voters);
public sealed record StoryAskView(bool Party, int Voted, int Of, ImmutableArray<string> Away, string? Tie, string? Decider, DateTimeOffset? Deadline, ImmutableArray<StoryOptionView> Options);
public sealed record StoryCardView(string Member, string Name, string Player, string? Sprite, int Bonus, string Note, bool Here, int Votes);
public sealed record StoryCheckView(string Skill, int Dc, string Title, ImmutableArray<StoryCardView> Cards, string? Chosen, RollResult? Roll);
public sealed record StoryView(int Seq, string Scene, string Title, string State, StoryLineView? Line, StoryAskView? Ask, StoryCheckView? Check, DateTimeOffset? Until);
// TacticsHostView gains: StoryView? Story = null, string? Chapter = null
```

Phones get `Screens.Dialogue`, `Screens.Vote`, `Screens.Check`:

```ts
dialogue: { speaker, portrait, text, canSkip }
vote:     { party, canChoose, options: [{id, label, skill, dc}], mine, tie, decider, away }
check:    { skill, dc, title, cards: [{id, name, player, cls, bonus, note, here}], mine, roll }
```

No tally and no `Seq` in any of them. `DeadlineUtc` carries the timer, so `Countdown` shows it (`ControllerScreen.tsx:76`).

**Inputs:** `interact`, `skip`, `vote {option}`, `pickRoller {member}`. `skip` needs no guard against a double tap: the view changes with the line, so a second tap has a stale viewId.

### 10. On the TV

`StoryTrack.Receive(StoryView?)` and `Tick(delta)` give a `StoryTick(Say, Cut, Panel)`:

- New `Seq` on a running track: say it, cutting the last clip.
- First view on a fresh track: caption only.
- A line with no clip: `Cut`, and caption only. `StoryVoice.Fallback` is the single switch for when the user decides.
- Story gone: `Cut`.

Clips come from `VoiceClips.ForLine(id)` with `Cast` `main`, at Question priority, so a skip takes effect at once. Commentary would queue instead (`NarrationQueue.cs:44-48`).

**Portrait:** `portraits/<preset>_<mood>.png`, on a backdrop by role. All 11 Chapter 1 speakers have all six moods, and every line has a mood. The narrator has no portrait, as in mockup 02.

**Layout from the mockups:** dialogue at the bottom left, vote at the bottom right, the skill check full over a dimmed world.

**Vote clock:** reads the deadline against the local clock once, then counts on frame time, as `HudClock` does for REDLINE (`docs/tv-host.md:129-131`).

### Tunables only a person can judge

| Number | Default | Lives in |
|---|---|---|
| Gap after a clip | 0.5 s | `StoryTiming.LineGap` |
| Reading pace, no clip | 0.36 s a word + 1.2 s | `StoryTiming` |
| Vote / choice | 45 / 30 s | `StoryTiming` |
| Tie-break / roller | 10 / 20 s | `StoryTiming` |
| Settle | 2 s | `StoryTiming.Settle` |
| Reveal | 4.5 s | `StoryTiming.Reveal` |
| Scene gap | 2 s | `StoryTiming.SceneGap` |
| Chapter card | 6 s | `StoryTiming.Card` |
| Interact reach | 2 steps | `StoryRules.Reach` |

`StoryTiming` joins `TacticsTiming` as an optional parameter, so existing constructors still compile.

---

### Task 1: The scenes, loaded and checked

**Files:**
- Create: `games/Tactics/Tactics.Logic/Story/SceneDefs.cs`, `games/Tactics/Tactics.Logic/Story/ClipLength.cs`
- Modify: `games/Tactics/Tactics.Logic/Story/LoreBook.cs`, `content/tactics/base/lore/schema/scenes.schema.json`, `content/tactics/base/lore/scenes/ch1.json` to `ch5.json` (`after: true` on `ch1_bridge_cost`, `ch1_road_east`, `ch2_journal`, `ch3_crypt_records`, `ch4_caves_after`, `ch4_rime_tower_after`, `ch5_gate_duel_after`, `ch5_ritual_after`), `tools/tactics-lore/check.py`, `tools/tactics-lore/test_check.py`
- Test: `games/Tactics/Tactics.Logic.Tests/Story/BaseLoreTests.cs`, `games/Tactics/Tactics.Logic.Tests/Story/ClipLengthTests.cs`

**Interfaces:** `SceneDef`, `SceneNode`, `SceneLine`, `SceneOption`, `SceneCheck`. `LoreBook.ScenesOf(string beat)`, `LoreBook.Character(string id)`, `LoreBook.ClipSeconds(string line)`. `static double? ClipLength.Of(Stream ogg)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Chapter_1_has_8_beats_and_13_scenes()
[Fact] public void Every_scene_line_loads_with_its_speaker()
[Fact] public void Every_chapter_1_line_has_a_clip_length()
[Fact] public void A_clip_length_is_read_from_its_last_page()
[Fact] public void A_bad_clip_has_no_length()
[Fact] public void Every_speaker_has_a_portrait_in_every_mood()
[Fact] public void Every_battle_beat_marks_its_aftermath()
[Fact] public void Every_beat_has_a_scene_on_each_branch()
```

Add to `test_check.py`: `test_after_is_only_for_a_battle_beat`.

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Logic.Tests` and `python3 -m unittest discover -s tools/tactics-lore`. Expected: FAIL. On this machine `python3` is the Store stub, so the implementer needs a real Python, or must list this as unverified.
- [ ] **Step 3: Implement.** Check every `after` line's diff adds one key and no text. Then `python3 tools/tactics-lore/check.py`.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-story): the scenes, as the game reads them`

### Task 2: Walking a scene

**Files:**
- Create: `games/Tactics/Tactics.Logic/Story/SceneRun.cs`
- Modify: `games/Tactics/Tactics.Logic/Story/StoryRules.cs`
- Test: `games/Tactics/Tactics.Logic.Tests/Story/SceneRunTests.cs`, `BaseLoreTests.cs`

**Interfaces:**
- `SceneDef? SceneRun.Next(ImmutableArray<SceneDef>, IReadOnlySet<string> flags, string? after, bool aftermath)`
- `ImmutableArray<SceneOption> SceneRun.Offered(SceneNode, IReadOnlySet<string> flags, PartyTags)`
- `SceneOption SceneRun.Unanswered(ImmutableArray<SceneOption>, bool vote)`
- `string? StoryRules.PlaceOf(...)`, `Tile? StoryRules.SpotFor(Arc, RegionDef, string place)`, `bool StoryRules.Chains(ArcBeat next, string? here)`
- `bool StoryRules.InReach(Terrain, Tile at, Tile spot)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_scene_is_tested_as_it_starts()
[Fact] public void A_vote_chooses_which_scene_plays_next()
[Fact] public void Lead_and_aftermath_are_told_apart()
[Fact] public void Options_filter_on_flags_and_tags()
[Fact] public void A_tag_counts_an_absent_character()
[Fact] public void An_unanswered_vote_takes_the_first_option()
[Fact] public void An_unanswered_choice_skips_a_check()
[Fact] public void Parley_travels_to_the_hills()
[Fact] public void Force_chains_to_the_assault()
[Fact] public void Parley_doesnt_chain_to_the_crossing()
[Fact] public void Reach_counts_steps_not_tiles_across_a_cliff()
[Fact] public void Every_reachable_node_offers_something_on_both_branches()   // no tags, the whole of chapter 1
[Fact] public void Every_path_through_every_chapter_1_scene_ends()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Logic.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-story): walking a scene`

### Task 3: Votes and rolls

**Files:**
- Create: `games/Tactics/Tactics.Logic/Story/VoteRules.cs`, `games/Tactics/Tactics.Logic/Story/SkillCheck.cs`
- Modify: `games/Tactics/Tactics.Logic/Rules/Rolls.cs` (`Flat`, sharing `D20`'s rule)
- Test: `games/Tactics/Tactics.Logic.Tests/Story/VoteRulesTests.cs`, `SkillCheckTests.cs`, `RollsTests.cs`

**Interfaces:** `VoteRules.Close` as in §5. `SkillCheck.StatOf(string skill)`, `SkillCheck.Bonus(CharacterRecord, string skill, RulesPack)`, `SkillCheck.Best(...)`, `Rolls.Flat(int die, int modifier, int vs)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_clear_winner_wins()
[Fact] public void The_leaders_vote_breaks_a_tie()
[Fact] public void A_leader_outside_the_tie_gets_a_tie_break()
[Fact] public void A_leader_who_didnt_vote_gets_a_tie_break()
[Fact] public void With_no_leader_the_first_connected_decides()
[Fact] public void No_votes_takes_the_unanswered_rule()
[Fact] public void A_three_way_tie_offers_only_the_tied()
[Fact] public void A_stale_option_isnt_counted()
[Fact] public void Every_skill_in_the_schema_has_a_stat()
[Fact] public void A_proficient_bonus_adds_proficiency_once()
[Fact] public void The_mockups_bonuses_add_up()
[Fact] public void Flat_succeeds_at_the_dc_and_has_no_natural_rules()
[Fact] public void Flat_agrees_with_D20()
[Fact] public void Best_breaks_a_tie_by_member_order()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-story): votes and rolls`

### Task 4: Beats start, and lines run

**Files:**
- Create: `games/Tactics/Tactics.Game/TacticsGame.Story.cs`
- Modify: `TacticsGame.cs`, `TacticsGame.Views.cs`, `TacticsGame.Explore.cs`, `TacticsTiming.cs`, `Campaign/TacticsDocument.cs`, `games/Tactics/Tactics.Presentation/PhaseScreen.cs`, `Audio/MusicChoice.cs`
- Test: `games/Tactics/Tactics.Game.Tests/StoryTests.cs`, `DocumentTests.cs`, `WalkingTests.cs`

**Interfaces:** `TacticsPhase.Story`, `StoryTiming`, `StorySave`, `WorldSave.Story`, `Timers.Line/Vote`, `Screens.Dialogue`, `StoryView`, `StoryLineView`, `ExploreView.Markers`. Walk data gains `interact`. Inputs `interact`, `skip`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_new_campaign_shows_the_mayors_marker()
[Fact] public void Interact_appears_only_in_reach()
[Fact] public void A_step_with_no_change_of_reach_changes_no_view()
[Fact] public void Only_the_leader_can_interact()
[Fact] public void Interact_out_of_reach_is_ignored()
[Fact] public void Interact_stops_the_party()
[Fact] public void The_party_cant_walk_during_a_scene()
[Fact] public void A_line_runs_for_its_clip_and_a_gap()
[Fact] public void A_line_with_no_clip_runs_on_a_reading_timer()
[Fact] public void Lines_advance_with_nobody_responding()
[Fact] public void Lines_advance_with_no_tv()
[Fact] public void Only_the_leader_can_skip()
[Fact] public void A_skip_doesnt_skip_two_lines()
[Fact] public void A_skip_cancels_the_line_timer()
[Fact] public void Node_sets_apply_when_it_plays()
[Fact] public void The_beat_ends_and_the_next_marker_shows()
[Fact] public void A_member_with_no_character_can_pick_during_a_scene()
[Fact] public void Leader_silence_doesnt_change_the_lead_in_a_scene()
[Fact] public void The_session_leader_waits_for_the_scene_to_end()
[Fact] public void The_banner_works_in_a_scene()
[Fact] public void A_version_1_document_with_no_story_reads()
[Fact] public void A_saved_story_round_trips()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Game.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Narrow 5b's `A_step_doesnt_change_any_phone_view` as flag 8 says.
- [ ] **Step 4: Run** that and `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): beats start, and lines run`

### Task 5: Asking, voting and rolling

**Files:**
- Modify: `games/Tactics/Tactics.Game/TacticsGame.Story.cs`, `TacticsGame.Views.cs`
- Test: `games/Tactics/Tactics.Game.Tests/StoryVoteTests.cs`, `StoryCheckTests.cs`

**Interfaces:** `Screens.Vote/Check`, inputs `vote`, `pickRoller`, `StoryAskView`, `StoryCheckView`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Options_open_as_the_last_line_starts()
[Fact] public void Only_present_players_vote()
[Fact] public void Away_players_are_named()
[Fact] public void A_vote_changes_only_the_voters_phone()      // the fix for dropped taps
[Fact] public void Four_votes_in_one_tick_all_count()
[Fact] public void A_vote_can_change_before_it_closes()
[Fact] public void Everyone_voting_closes_after_the_settle()
[Fact] public void A_close_waits_for_the_question_to_be_heard()
[Fact] public void An_unanswered_vote_takes_the_first_option()
[Fact] public void The_winning_choice_sets_its_flags()
[Fact] public void A_tied_leader_decides_at_once()
[Fact] public void A_tie_break_goes_to_the_leader()
[Fact] public void A_tie_break_unanswered_takes_the_first_tied()
[Fact] public void A_leader_lost_on_a_tie_hands_it_on()
[Fact] public void With_no_leader_the_first_connected_decides()
[Fact] public void A_voter_who_drops_keeps_their_vote()
[Fact] public void A_dropped_voter_no_longer_holds_up_the_close()
[Fact] public void Nobody_connected_holds_the_question_and_runs_no_timer()
[Fact] public void A_phone_coming_back_restarts_the_timer()
[Fact] public void Lines_run_on_with_nobody_connected()
[Fact] public void A_choice_is_the_leaders_alone()
[Fact] public void An_unanswered_choice_doesnt_spend_a_roll()
[Fact] public void Pay_goes_to_a_check_and_then_vote2()
[Fact] public void Check_cards_include_an_away_character()
[Fact] public void The_party_picks_who_rolls()
[Fact] public void An_unanswered_pick_takes_the_best_bonus()
[Fact] public void A_roll_goes_to_pass_or_fail()
[Fact] public void A_roll_doesnt_wait_for_the_tv()
[Fact] public void A_decided_vote_is_saved_with_its_flags()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): votes and skill checks`

### Task 6: Battles, travel and the chapter's end

**Files:**
- Modify: `games/Tactics/Tactics.Game/TacticsGame.Story.cs`, `TacticsGame.cs`, `TacticsGame.Battle.cs`, `TacticsGame.Views.cs`
- Test: `games/Tactics/Tactics.Game.Tests/StoryBattleTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_battle_beat_plays_its_lead_in_first()
[Fact] public void The_battle_starts_when_the_lead_in_ends()
[Fact] public void Force_runs_straight_into_the_assault()
[Fact] public void Parley_travels_with_a_new_epoch_and_a_save()
[Fact] public void After_the_parley_the_crossing_waits_at_its_marker()
[Fact] public void Tapping_the_marker_starts_the_battle_beat()
[Fact] public void Walking_into_the_stamp_starts_the_lead_in()
[Fact] public void A_win_plays_the_aftermath_after_results()
[Fact] public void The_beat_is_done_when_the_aftermath_ends()
[Fact] public void A_loss_keeps_the_beat_at_the_fight()
[Fact] public void A_retry_skips_the_lead_in()
[Fact] public void A_list_battle_leaves_a_pending_beat_alone()
[Fact] public void The_last_beat_ends_the_chapter()
[Fact] public void After_the_chapter_there_are_no_markers_and_walking_works()
[Fact] public void The_chapter_card_only_shows_once()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): beats that fight, and the end of a chapter`

### Task 7: Coming back

**Files:**
- Modify: `games/Tactics/Tactics.Game/TacticsGame.cs` (`Start`, `Resume`, `ContinueBattle` into `CarryOn`), `TacticsGame.Views.cs` (`Banner`), `games/Tactics/Tactics.Presentation/PhaseScreen.cs`
- Test: `games/Tactics/Tactics.Game.Tests/StoryResumeTests.cs`, `src/ArcBox.Server.Tests/`, a new room test

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_restart_mid_line_waits_for_the_party()
[Fact] public void The_vip_can_continue_without_them()
[Fact] public void It_resumes_on_the_saved_line()
[Fact] public void A_restart_mid_vote_reopens_the_question()
[Fact] public void A_restart_on_a_tie_break_reopens_the_vote()
[Fact] public void A_restart_after_a_roll_doesnt_roll_again()
[Fact] public void A_restart_in_an_aftermath_carries_on()
[Fact] public void A_restart_at_results_goes_to_the_aftermath()
[Fact] public void A_new_session_carries_on_the_scene()
[Fact] public void A_cursor_on_a_scene_thats_gone_restarts_the_beat_with_a_notice()
[Fact] public void Flags_arent_lost_or_doubled_by_a_replay()
[Fact] public void A_stale_vote_after_a_new_line_is_dropped()              // server test
[Fact] public void A_vote_storm_through_a_real_room_counts_everyone()      // server test
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Game.Tests` and `dotnet test --project src/ArcBox.Server.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): a story survives a restart`

### Task 8: The phone's story screens

**Files:**
- Create: `web/play/src/games/tactics/story.tsx`
- Modify: `web/play/src/games/tactics/index.ts`, `types.ts`, `walk.tsx` (Interact), `tactics.css`
- Regenerate: fixtures, adding `dialogue`, `dialogue-leader`, `vote`, `vote-tie`, `choice-leader`, `check-pick`, `check-rolled`, `walk-interact`. Update the screen list in `PhoneFixtureTests.Every_phone_screen_matches_its_fixture`.
- Test: `web/play/test/story.test.tsx`

- [ ] **Step 1: Write the failing tests**

```ts
it("shows the line and its speaker")
it("shows skip only to the leader")
it("shows the options and marks my vote")
it("lets me change my vote")
it("shows a check's skill and dc on its option")
it("shows a non-vote choice read-only to everyone else")
it("offers only the tied options on a tie-break")
it("names who's away")
it("shows cards with bonuses and who's away")
it("shows the roll")
it("shows Interact on the walk screen and sends it")
it("keeps the pad working when Interact appears")
```

- [ ] **Step 2: Run** `npm test` in `web/play`. Expected: FAIL.
- [ ] **Step 3: Implement,** then run `UPDATE_FIXTURES=1 dotnet test --project games/Tactics/Tactics.Game.Tests` and check the fixture diff. No battle fixture may change.
- [ ] **Step 4: Run** `npm test` and `npm run build`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-phone): dialogue, votes and skill checks`

### Task 9: The story on the TV, pure parts

**Files:**
- Create: `games/Tactics/Tactics.Presentation/Story/StoryTrack.cs`, `StoryScreen.cs`, `StoryVoice.cs`, `VoteClock.cs`
- Modify: `games/Tactics/Tactics.Presentation/Audio/VoiceClips.cs` (`ForLine`), `games/Tactics/Tactics.Presentation/Hud/HudArt.cs` (`Speaker`)
- Test: `games/Tactics/Tactics.Presentation.Tests/Story/StoryTrackTests.cs`, `StoryScreenTests.cs`, `HostViewContractTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_new_line_speaks_its_clip()
[Fact] public void Every_chapter_1_line_finds_its_clip()
[Fact] public void A_line_with_no_clip_shows_a_caption_and_cuts()
[Fact] public void A_fresh_track_shows_the_line_and_doesnt_speak()
[Fact] public void A_jump_in_seq_speaks_only_the_newest()
[Fact] public void A_lower_seq_after_a_restart_is_a_new_track()
[Fact] public void The_story_ending_cuts_the_voice()
[Fact] public void The_narrator_has_no_portrait()
[Fact] public void A_speakers_mood_picks_their_portrait()
[Fact] public void The_vote_panel_words_the_tally()              // "4 of 4 voted · 2–2 tie, Sam decides"
[Fact] public void The_vote_panel_names_who_is_away()
[Fact] public void A_check_option_shows_its_dc()
[Fact] public void The_roll_reads_as_in_mockup_03()              // "14 + 5 = 19 vs DC 15"
[Fact] public void The_vote_clock_survives_a_skewed_tv_clock()
[Fact] public void A_real_scene_reads_as_a_story_view()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-tv): what the story says`

### Task 10: The story on the TV, drawn

**Files:**
- Create: `src/ArcBox.Host/games/tactics/StoryPanel.cs`, `VotePanel.cs`, `CheckPanel.cs`, `ChapterCard.cs`, `WorldMarkers.cs`
- Modify: `src/ArcBox.Host/games/tactics/TacticsScene.cs`, `WorldHud.cs`, `src/ArcBox.Host/Shell/AppArgs.cs` (`--story-demo`, `--story-log`), `docs/tv-host.md`

- [ ] **Step 1: Add** `--story-demo[=<scene>]`, which plays a scene with no server, and `STORY <seq> <lineId>` lines under `--story-log`.
- [ ] **Step 2: Build** the panels from `HudLayer`'s art.
- [ ] **Step 3: Check by eye** against mockups 02 and 03, with `--shots`: a long line, the narrator, a tie, a four-option vote, six cards, a pass and a fail, the marker over an NPC, the marker behind a house, a chapter card.
- [ ] **Step 4: Run** `dotnet build ArcBox.slnx`. Expected: 0 warnings.
- [ ] **Step 5: Commit** `feat(tactics-tv): dialogue, votes and checks on screen`

### Task 11: All of Chapter 1

**Files:**
- Create: `games/Tactics/Tactics.Game.Tests/ChapterOneTests.cs`, `tools/tactics-playtest/route.cjs`
- Modify: `tools/tactics-playtest/bots.cjs` (`--story`, `--branch=force|parley`, `--silent`), `tools/tv-e2e.sh`, `games/Tactics/README.md`

The bots find their way with `route.cjs`: BFS over the region file, by the 5b rules, from tiles they know. They check the Interact button as they arrive.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Chapter_1_plays_by_force()
[Fact] public void Chapter_1_plays_by_parley()
[Fact] public void Chapter_1_plays_with_every_vote_unanswered()
[Fact] public void Chapter_1_plays_with_one_player_and_companions()
[Fact] public void Chapter_1_plays_with_a_restart_at_every_line()
[Fact] public void Chapter_1_plays_with_no_clips()
[Fact] public void Chapter_1_plays_with_a_defeat_in_its_battle()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Game.Tests`. Expected: FAIL.
- [ ] **Step 3: Fix** anything they find.
- [ ] **Step 4: Add** `ARCBOX_STORY=1` to `tools/tv-e2e.sh`, for each branch. It fails unless the TV's `STORY` lines climb to the last line, `--silent` finishes on timers, `--drop-signals` finishes, a killed TV comes back with a caption and no replayed voice, and a restart resumes on the saved line.
- [ ] **Step 5: Run everything:** `dotnet build ArcBox.slnx`, `dotnet test ArcBox.slnx`, `npm test` and `npm run build` in `web/play`. Expected: 0 warnings, all pass.
- [ ] **Step 6: For the user:** a real group on phones. Let the phones sleep through a scene, vote out loud for a full timer, and pull a phone mid-vote. Pacing, voices and the 45 s are judged here and at 5e.
- [ ] **Step 7: Commit** `feat(tactics-playtest): all of Chapter 1`
