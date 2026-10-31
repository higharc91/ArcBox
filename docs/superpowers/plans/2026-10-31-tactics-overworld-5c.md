# Overworld 5c: Fights on the Spot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** River Crossing starts in the world, with no scene change, and returns to walking. A lost fight wakes the party at the recovery spawn and can be tried again.

**Architecture:** the engine still sees an ordinary battle map. A `BattleStage` converts between battle and world tiles for both server and TV. The server chooses the encounter from the arc, runs the battle exactly as today, and moves the party when it ends. On the TV, `BeatPlayer`, `UnitPlan` and `BoardCanvas` keep working in the battle's own pixels. `BoardCanvas` gets a plan built from world tiles, shifted into those pixels, and is placed by the world camera. A pure `StageDirector` runs the walk-in, the grid fade and the way out. If the world isn't ready, the plain board from 3b shows the fight.

**Tech Stack:** C# / .NET 10, Godot 4.7.2 .NET, xUnit v3.

**Spec:** [the overworld design](../specs/2026-10-31-tactics-overworld-design.md), §2, §4.3, §4.5, §4.6. Starts after [5b](2026-10-31-tactics-overworld-5b.md) is merged, with Ashen Ford's draft in the pack.

## Global Constraints

- The 5a and 5b constraints hold.
- Don't change game rules, balance numbers or existing battle screens. Phone fixtures don't change in this milestone.
- `Tactics.Logic` stays clock-free and takes no new dependencies.
- Anything worded, ordered or computed is pure C# with tests. Godot classes only draw.
- Animate from each beat's events, send `Signal("shown", {seq})`, and keep attacks under about 1 s. A slow or missing TV never stalls the game.
- A battle on an unstamped map, or in a pack with no region, behaves exactly as it does today.
- Godot windows open on the left monitor and never take focus.
- Stage files by name. Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- **A retry that plays a different fight, or none.** After a defeat, walk back in and check the same encounter starts. Check both `ford_assault` and `river_crossing` on the same map, on the same TV, with no stale board, plan or lighting.
- **The party lands somewhere illegal.** Win with the leader Downed, on a raised tile, or on a tile a spell sank. Walking must resume, and the party must not be stuck inside a re-armed stamp.
- **A seam at the stage's edge.** Look for a visible rectangle from lighting, water frames, lips, a skirt, or the board shaking apart from the world on a heavy hit.
- **Unlucky TV timing.** Kill the TV during the walk-in, mid-fight, during the last blow and at Results. Each time, the world must come back around the board with no replayed beats, and `shown` must still reach the game.
- **Leftover walking state.** Hold the pad into a trigger. No step, lease or lead timer survives into the battle, a late `walk` raises no toast, and the pad works cleanly after Results.

## Defaults chosen while the user is away

1. **A stamp triggers on entry, not on one marked tile.** A step from outside into the stamp of the armed battle beat starts the fight, and the party stops one tile short. Beat spots only place the "!" (5d).
2. **A stamp only arms for the available battle beat.** Before that, and after the fight is won, it's ordinary ground.
3. **Far from the stamp, the party fades in rather than running.** Over 14 steps from its slot, which also covers the battle list, the TV cuts through a fade.
4. **After a win, walking resumes on the leader's battle tile.** If the leader isn't Up, it's the first Up hero in member order. If that tile can't be stood on, it's the nearest legal tile, then `party_spawn[0]`.
5. **A fled battle doesn't wake the party.** The party stays at the stamp on the same rule as a win, and the beat stays open. Only a trigger's `endBattle` can flee today.
6. **Battle flags go into `WorldSave.Flags` only on a win.** A lost attempt leaves no story state.
7. **The battle list's fights don't touch the story.** A list win completes no beat.
8. **The battle list is out of the way of the party, not removed.** It still works from walking and from Results.
9. **A raised or sunk tile heals when the fight ends.** `WorldGround` never changes, so the world returns as the grid fades out.

## Where the spec or 5b falls short

1. **"Only the TV needs [the offset]" (§4.3) is wrong.** The server needs it to trigger on entry and to put the party back in the world afterwards.
2. **"Enemies step out" can't come from events.** `Battle.Start` emits only `RoundStarted`, `TurnStarted`, hooks and triggers (`Battle.cs:13-15`, `:38-41`), and units simply appear in `state.Units`. So the walk-in is a TV script read from the first view's units. Everything after it still plays from beats.
3. **`StartBattle` doesn't pace its opening beat.** It ignores `AddBeat`'s result (`TacticsGame.Battle.cs:52-54`), so the AI could act while the party is still walking in. In-place fights must call `Pace` with a `FormUp` wait. The `_awaitShown` return at `:142` gives this for free, and a player can still act.
4. **5b's `ZIndex = bx + by` needs its own canvas layer (my knowledge, unverified).** Z sorts across the whole canvas layer, so positive-Z blocks would paint over the HUD, table and caption. `WorldCanvas` goes in a `CanvasLayer` under the UI, and `BoardCanvas` joins it above the blocks.
5. **`Explore` must exist in every phase.** 5b only describes it while walking. A reconnecting TV in a fight has to learn the region from the view.
6. **`Timers.Lead` must stop when the pad leaves the screen.** No pad means no idle ping (5b §1), so the 8 s silence rule would take the lead away during any battle.
7. **Followers stack with no trail.** With empty `Legs` every follower sits on the leader's tile, which shows after any `Epoch`. `WalkTrack` needs `Seed(places)`.
8. **`BoardCanvas.SetEffects` shakes the board against the world.** It moves `Position` (`BoardCanvas.cs:142`). In place, the shake goes to the camera.
9. **5b's risk on fixed scale is closed.** All seven maps are 14×12, and at scale 2 the tallest is 960 px against the 992 px `BoardFit.Area` (`BoardFit.cs:23`). That's excluding sprites and skirts.
10. **The camera clamp must not apply in a fight.** A stamp near the region's edge would sit off-centre with 5b's 16-tile margin.
11. **`Battle.Result` is never used for flags.** `EndBattle` drops `FlagsSet` (`TacticsGame.cs:350-384`, `BattleResult.cs:21`).
12. **Losing pays half XP every time.** That's today's rule (`Objectives.cs`, `Battle.Result`). Retrying makes it farmable. It stays, because changing it would be a rules change, but it goes on the user's list.
13. **The region check needs one addition.** `start` and `recovery` must not be inside a stamp. Otherwise a defeat wakes the party on the battlefield.

## Design

### 1. Where a fight is

```csharp
public readonly record struct BattleStage(string Map, Tile Origin, int Height, int Width, int Depth)
{
    Tile ToWorld(Tile battle); Tile ToBattle(Tile world); bool Holds(Tile world);
    static BattleStage? Of(WorldRegion region, BattleMap map);
}
```

5a already guarantees one stamp per map (`Regions.cs`, `mapsSeen`), that tiles match, and that `party_spawn` can be reached.

### 2. Which fight

The arc's available beat decides. `ford_assault` and `river_crossing` share a map, and the beats' `requires` make them exclusive, so the stamp never has to name an encounter.

| Start | From | Encounter | `BattleSave.Beat` |
|---|---|---|---|
| Walking in | Any step from outside into the stamp | The available battle beat's, if its map is the stamp's | That beat |
| Battle list | The VIP's list | The one picked | null |
| "!" and story chains (5d) | Beat spot, or a scene ending | The beat's | That beat |

`StoryRules.Available` goes in now so 5d doesn't change the meaning. 5c can only reach it through a seeded document, so `TacticsSeed` is a Development-only setting read in `AddTactics`, applied once to a campaign with no `World`.

Starting always does the same thing: `LeaveExploration()` (stop, cancel `Step`, `Lead` and `Save`), `FillAbsentCharacters()`, `Companions(min)`, `StartBattle`. The party is everyone in `_players`, because every member has a seat and absent ones are told as lost phones (`Room.cs:401-408`).

### 3. What reaches the TV

```csharp
public sealed record StageView(string Region, Tile Origin, int Height, ImmutableArray<string> Heroes);
// BattleView gains: StageView? Stage = null
// ExploreView gains: string? Arrived = null   // "fellIn" | "woke" | null
```

`Heroes` gives each `hero<n>`'s walker id, because `UnitView.Player` is null for an AI-played hero. `Arrived` tells the TV how to show the new `Epoch`.

### 4. Drawing in place

The projection is linear (`BoardProjection.cs:24-25`), so a stamp's offset is one constant: `Corner(Origin.X, Origin.Y, Height)`. So the TV doesn't convert tiles at all:

- `BeatPlayer`, `DisplayedBattle`, `BeatScript` and `UnitPlan` keep local tiles and local pixels, on the map's own `BoardTerrain`.
- `BoardFit.Stage(stage, cameraOrigin)` gives `Origin = worldOrigin + offset × 2`, so every local pixel lands correctly.
- `StagePlan.Build` makes the plan from `WorldGround`. It covers the stamp plus 10 tiles along +x and +y for whatever can stand in front. It keeps only items overlapping the stage's rect, marks `Grid` only inside the stamp, and shifts points into local pixels with `BoardPlan.Shifted`.
- Keys stay in world tiles, so `UnitPlan.Build` gets a `Tile worldOffset` used only for `OrderOf`.
- `BoardCanvas` then draws over identical world pixels with no skirt, because neighbours are real tiles. Occlusion from a cliff in front is correct, and lighting works unchanged because plan and units share a frame.

Three changes to `BoardCanvas`, all additive:
- `SetFit(fit)`, which moves it without rebuilding.
- `GridAlpha`, applied at `BoardCanvas.cs:206`.
- `Clock`, shared with `FrameTextures` so water doesn't step at the seam.

Zones draw on the stage alone. If `Heights` or `Sunk` change, the affected world blocks are also rebuilt from the same changed terrain, or the old higher tile would show around a lowered one.

### 5. The timeline

| Stage | What happens | Length | Source |
|---|---|---|---|
| `Forming` | Camera glides to the stage. Heroes walk from `WalkTrack.Places` to their slots by BFS. Far party: fade | at most 1.2 s | First view |
| `Forming` | Grid `GridAlpha` 0 to 1 | 0.5 s | |
| `Forming` | Enemies and companions step in from one tile out, staggered 0.2 s | at most 1.4 s | First view's units |
| `Fighting` | Beats play as they do today, in place. Shake goes to the camera | | Beat events |
| `Fading` | Last beat plays through `BeatLoop.Held`, then grid fades | 0.4 s | `LastBattle` |
| `Results` | World and final positions stay. Results panel on top | until "next" | View |
| `FallingIn` / `Waking` | Tween to the follow slots, or fade to the recovery spawn | 0.6 s / 0.4 + 0.6 s | `Arrived` |

The walk-in is at most 2.6 s. `Timers.Pace` falls back after 3 s (`TacticsTiming.FormUp`). Beat 1's `shown` comes after the walk-in, and only ever shortens that wait.

### 6. When it ends

`EndBattle` does this all before its existing `Checkpoint`, so a restart at Results is safe:

| Outcome | Tile | Story | `Arrived` |
|---|---|---|---|
| Won | Default 4 | Merge `FlagsSet`, add the beat to `Beats`, apply its `sets` | `fellIn` |
| Lost | `Spawns.Recovery` | No change: the stamp re-arms | `woke` |
| Fled | Default 4 | No change | `fellIn` |
| Unstamped | Unchanged | Unchanged | null, no new `Epoch` |

`Results` "next" goes to Exploration. `_doc.Completed`, XP and injuries are exactly as they were. A retry gets a new seed through the same path.

**Stuck inside a stamp:** after Fled, or a chain, the party can stand in an armed stamp. Entry is a step from outside to inside, so they walk out and back.

### 7. A TV coming back

- **Mid-fight:** `StageDirector` jumps straight to `Fighting` with the grid on and the camera on the stage. `BeatQueue.Late` does the rest, as in 3b.
- **World still loading:** the plain board plays the fight. When the world is ready, the stage takes over with a 0.25 s fade.
- **World can't be drawn:** the plain board stays. A TV that can't draw the world must still show the fight.
- **At Results:** no beats remain, so the world draws at the new tile with the panel on top.
- **Mid walk-in:** a first view with `Beats[^1].Seq > 2` skips the walk-in, the same test as `BeatQueue.OpeningBeats`.

### 8. The battle list

| Case | What happens |
|---|---|
| Stamped map, party far away | Fade to the stamp, then a normal in-place fight. Won: party stays there. Lost: recovery spawn |
| Unstamped map | Today's plain board. Party returns to its tile, no `Epoch` |
| A pack with no region | Exactly today |
| Beat pending | Beat stays open. `BattleSave.Beat` is null, so nothing completes |
| Encounter named in a beat, won from the list | `Completed` gets it, `Beats` doesn't |

### 9. Saved state

`BattleSave` gains `string? Beat = null`, ignored when default. **No version bump**: `ArcJson.Options` doesn't reject unknown or missing members (`ArcJson.cs:13-16`), and an optional constructor parameter reads as its default. `Epoch` stays in memory, since a restart changes it anyway and a different value snaps.

### Tunables only a person can judge

| Number | Default | Lives in |
|---|---|---|
| Walk-in max | 1.2 s | `StageTuning` |
| Far form-up | 14 steps | `StageTuning` |
| Grid in / out | 0.5 / 0.4 s | `StageTuning` |
| Enemy stagger | 0.2 s | `StageTuning` |
| Fall-in | 0.6 s | `StageTuning` |
| Fade down / up | 0.4 / 0.6 s | `StageTuning` |
| Stage plan reach | 10 tiles | `StagePlan` |
| Fallback wait | 3 s | `TacticsTiming.FormUp` |

---

### Task 1: The stage

**Files:**
- Create: `games/Tactics/Tactics.Logic/Map/BattleStage.cs`
- Modify: `games/Tactics/Tactics.Logic/Map/WorldRegion.cs` (`StampFor(string map)`), `games/Tactics/Tactics.Logic/Content/Validation/Regions.cs`
- Test: `games/Tactics/Tactics.Logic.Tests/Map/BattleStageTests.cs`, `games/Tactics/Tactics.Logic.Tests/Content/RegionCheckTests.cs`

**Interfaces:** `BattleStage` as in §1. `static Tile? BattleStage.Standable(Terrain, Tile from, ImmutableArray<Tile> fallback)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Tiles_convert_both_ways_with_the_offset()
[Fact] public void Holds_covers_exactly_the_stamp()
[Fact] public void A_map_with_no_stamp_has_no_stage()
[Fact] public void Two_encounters_on_one_map_have_one_stage()
[Fact] public void Standable_keeps_a_legal_tile()
[Fact] public void Standable_finds_the_nearest_legal_tile()
[Fact] public void Standable_falls_back_when_nothing_near_is_legal()
[Fact] public void A_recovery_spawn_inside_a_stamp_is_an_error()
[Fact] public void A_start_spawn_inside_a_stamp_is_an_error()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Logic.Tests`. Expected: fails to compile.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-world): where a fight stands`

### Task 2: Arcs, and the beat that's available

**Files:**
- Create: `games/Tactics/Tactics.Logic/Story/LoreBook.cs`, `games/Tactics/Tactics.Logic/Story/StoryRules.cs`, `games/Tactics/Tactics.Game/TacticsSeed.cs`
- Modify: `games/Tactics/Tactics.Game/TacticsGame.cs` (optional `Lazy<LoreBook>? lore`, `TacticsSeed? seed`), `src/ArcBox.Server/Games/Tactics/TacticsServiceCollectionExtensions.cs`
- Test: `games/Tactics/Tactics.Logic.Tests/Story/StoryRulesTests.cs`, `games/Tactics/Tactics.Logic.Tests/Story/BaseLoreTests.cs`

**Interfaces:**
- `sealed class LoreBook`: `static Load(string basePack)`, `static Empty`, `Arc? ArcFor(string region)`
- `sealed record ArcBeat(string Id, string Kind, string? Place, string? Encounter, ImmutableArray<string> Requires, ImmutableArray<string> Sets, ImmutableArray<ArcChoice> Choices)`
- `static ArcBeat? StoryRules.Available(Arc, IReadOnlySet<string> flags, IReadOnlySet<string> beats)`: the first beat in order that isn't done and whose `Requires` hold
- `static bool StoryRules.ChapterDone(Arc, IReadOnlySet<string> beats)`: its last beat is done

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_new_campaign_starts_at_the_first_beat()
[Fact] public void A_beat_waits_for_the_beats_before_it()
[Fact] public void Unmet_requires_are_passed_over()
[Fact] public void Force_makes_the_assault_available_and_never_the_crossing()
[Fact] public void Parley_makes_the_crossing_available_and_never_the_assault()
[Fact] public void A_done_beat_isnt_available()
[Fact] public void The_chapter_ends_with_its_last_beat()
[Fact] public void Every_arc_battle_takes_a_full_party()     // Party.Max >= the manifest's 6
[Fact] public void Every_battle_beat_in_chapter_1_has_a_stamp()
[Fact] public void An_empty_book_has_no_beats()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Logic.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Read `lore/pack.json`, then `arcs.json`. Real JSON key: `requires`.
- [ ] **Step 4: Run them, and** `dotnet build ArcBox.slnx`. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat(tactics-story): the beat that's available`

### Task 3: A fight starts where the party is

**Files:**
- Modify: `games/Tactics/Tactics.Game/TacticsGame.Explore.cs`, `TacticsGame.Battle.cs`, `TacticsGame.cs`, `TacticsGame.Views.cs`, `games/Tactics/Tactics.Game/TacticsTiming.cs`, `games/Tactics/Tactics.Game/Campaign/TacticsDocument.cs`
- Test: `games/Tactics/Tactics.Game.Tests/StageBattleTests.cs`

**Interfaces:** `BattleSave.Beat`, `StageView`, `BattleView.Stage`, `TacticsTiming.FormUp`. Private: `StartFight(string encounter, string? beat)`, `LeaveExploration()`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Stepping_into_an_armed_stamp_starts_its_fight()
[Fact] public void The_party_stops_one_tile_short()
[Fact] public void The_arc_beat_decides_between_two_encounters_on_a_map()
[Fact] public void An_unarmed_stamp_is_ordinary_ground()
[Fact] public void Walking_inside_an_armed_stamp_doesnt_start_it()
[Fact] public void No_walking_timer_survives_the_start()         // Harness.ActiveTimers
[Fact] public void The_lead_doesnt_change_during_a_battle()
[Fact] public void A_late_walk_in_battle_is_ignored_with_no_toast()
[Fact] public void The_host_view_has_the_stage_and_the_region()
[Fact] public void Heroes_map_to_walkers_even_when_ai_played()
[Fact] public void An_absent_member_is_in_the_fight_with_a_character()
[Fact] public void A_small_party_gets_its_companions()
[Fact] public void The_opening_beat_holds_the_ai_until_shown()
[Fact] public void The_opening_beat_holds_only_as_long_as_the_fallback()
[Fact] public void A_player_can_act_during_the_walk_in()
[Fact] public void A_battle_on_an_unstamped_map_has_no_stage()
[Fact] public void A_seed_applies_once_to_a_new_campaign()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Game.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.** `Explore` is present in every phase once a region exists. `OnInput` ignores `walk` in Battle before `BattleInput`, or `ParseCommand` could toast (`TacticsGame.Battle.cs:237-241`).
- [ ] **Step 4: Run them.** Expected: PASS. `PhoneFixtureTests` has no diff.
- [ ] **Step 5: Commit** `feat(tactics-game): fights start where the party stands`

### Task 4: What happens after

**Files:**
- Modify: `games/Tactics/Tactics.Game/TacticsGame.cs` (`EndBattle`), `TacticsGame.Explore.cs`, `TacticsGame.Views.cs`
- Test: `games/Tactics/Tactics.Game.Tests/StageEndTests.cs`, `DocumentTests.cs`

**Interfaces:** `ExploreView.Arrived`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_win_resumes_on_the_leaders_battle_tile()
[Fact] public void A_win_with_the_leader_down_uses_the_first_hero_up()
[Fact] public void A_win_on_a_tile_that_cant_be_stood_on_finds_one_that_can()
[Fact] public void A_win_marks_the_beat_done_and_merges_its_flags()
[Fact] public void A_loss_wakes_the_party_at_the_recovery_spawn_with_a_new_epoch()
[Fact] public void A_loss_keeps_xp_and_injuries_as_they_are_today()
[Fact] public void A_loss_sets_no_flags_and_completes_no_beat()
[Fact] public void A_lost_fight_can_be_retried_by_walking_back()
[Fact] public void A_retry_has_a_new_seed()
[Fact] public void A_flight_doesnt_wake_the_party()
[Fact] public void A_restart_at_results_resumes_on_the_new_tile()
[Fact] public void A_restart_mid_fight_keeps_the_beat()
[Fact] public void A_stamp_won_is_ordinary_ground()
[Fact] public void A_list_fight_completes_no_beat()
[Fact] public void A_list_fight_far_away_stands_at_its_stamp()
[Fact] public void A_list_fight_on_an_unstamped_map_keeps_the_tile_and_epoch()
[Fact] public void A_version_1_battle_with_no_beat_still_reads()
[Fact] public void The_party_can_walk_again_after_results()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.** Work out the tile before `Battle = null`.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): after the fight, waking and trying again`

### Task 5: The stage's plan

**Files:**
- Create: `games/Tactics/Tactics.Presentation/World/StagePlan.cs`
- Modify: `games/Tactics/Tactics.Presentation/Board/BoardPlan.cs` (`Shifted`), `BoardFit.cs` (`Stage`), `UnitPlan.cs` (`worldOffset`)
- Test: `games/Tactics/Tactics.Presentation.Tests/World/StagePlanTests.cs`

**Interfaces:**
- `BoardPlan.Shifted(Vector2 delta)`
- `BoardFit.Stage(BattleStage, Vector2 worldOrigin, float scale)`
- `StagePlan.Build(WorldGround, BattleStage, BoardChanges?, ArtIndex)`
- `BoardChangesOnWorld.Of(BoardChanges, BattleStage)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_local_pixel_lands_on_its_world_pixel()
[Fact] public void A_stamp_at_height_offsets_by_its_height()
[Fact] public void Stage_tiles_match_the_board_plans_tiles()
[Fact] public void No_skirt_is_drawn_at_the_stamp_edge()
[Fact] public void A_cliff_in_front_of_the_stamp_hides_a_unit()
[Fact] public void A_stamped_prop_sorts_with_a_unit_as_on_the_board()
[Fact] public void Only_stamp_tiles_have_a_grid()
[Fact] public void A_zone_shows_on_the_stage()
[Fact] public void A_lowered_tile_changes_the_blocks_too()
[Fact] public void Every_map_fits_the_area_at_scale_2()
[Fact] public void Lights_and_figures_read_the_same_frame()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Measure** plan size and `BoardTerrain` rebuild on 160×120. **Stop and report if a rebuild is over 8 ms.** Then crop to a window of the ground.
- [ ] **Step 6: Commit** `feat(tactics-tv): a stage built from the world's tiles`

### Task 6: The director

**Files:**
- Create: `games/Tactics/Tactics.Presentation/World/StageDirector.cs`, `BattleIntro.cs`, `FallIn.cs`, `StageTuning.cs`
- Modify: `games/Tactics/Tactics.Presentation/World/WalkTrack.cs` (`Seed`), `WorldCamera.cs` (`Hold`), `games/Tactics/Tactics.Presentation/PhaseScreen.cs`
- Test: `games/Tactics/Tactics.Presentation.Tests/World/StageDirectorTests.cs`, `BattleIntroTests.cs`, `PhaseScreenTests.cs`

**Interfaces:**
- `StageDirector.Receive(TacticsHostView)`, `Tick(double)`, `Stage`, `GridAlpha`, `Hidden`, `Places`, `Poses`, `BeatsRun`, `Fade`, `CameraTarget`
- `PhaseScreen.InPlace`, so Results and Resuming keep the world
- `WalkTrack.Seed(places)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Heroes_walk_from_where_they_stand_to_their_slots()
[Fact] public void A_path_goes_round_a_blocking_prop()
[Fact] public void A_far_party_fades_in()
[Fact] public void The_walk_in_is_under_3_seconds()
[Fact] public void Enemies_are_hidden_until_they_step_out()
[Fact] public void Beats_wait_for_the_walk_in()
[Fact] public void A_late_join_skips_the_walk_in()
[Fact] public void The_camera_isnt_clamped_in_a_fight()
[Fact] public void The_last_blow_plays_before_the_grid_fades()
[Fact] public void Results_keep_the_world_on_screen()
[Fact] public void A_win_falls_in_from_where_they_stood()
[Fact] public void A_loss_fades_to_the_recovery_spawn()
[Fact] public void The_epoch_waits_until_results_are_over()
[Fact] public void Followers_seeded_after_an_epoch_arent_stacked()
[Fact] public void A_second_fight_on_the_same_map_starts_clean()
[Fact] public void A_world_not_ready_means_the_plain_board()
[Fact] public void The_stage_takes_over_when_the_world_is_ready()
[Fact] public void Plain_battles_are_untouched()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-tv): into the fight and out of it`

### Task 7: The fight on the TV

**Files:**
- Modify: `src/ArcBox.Host/games/tactics/BoardCanvas.cs` (`SetFit`, `GridAlpha`, `Clock`), `WorldCanvas.cs` (own `CanvasLayer`, `Repaint`, shake), `TacticsScene.cs`, `HudLayer.cs`, `src/ArcBox.Host/Shell/AppArgs.cs` (`--world-demo=fight`), `docs/tv-host.md`

- [ ] **Step 1: Wire it,** with `--world-demo=fight` driving a scripted battle at the stamp.
- [ ] **Step 2: Check by eye,** with `--shots`:
  - the stamp's edge at rest, in water and at a lip
  - a heavy hit
  - a unit behind a cliff and behind a stamped crate
  - the grid behind a prop
  - `:changed` with a sunk tile
  - the region's edge
  - the HUD over the world
  - the world over nothing it shouldn't cover
- [ ] **Step 3: Measure** frame time with `--perf` at 1920x1080. **Stop and report if it's over 16 ms.**
- [ ] **Step 4: Run** `dotnet build ArcBox.slnx` and `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: 0 warnings, PASS.
- [ ] **Step 5: Commit** `feat(tactics-tv): the fight in place`

### Task 8: Bots, end to end, and a person

**Files:**
- Modify: `tools/tactics-playtest/bots.cjs` (`--walk-to-stamp`, `--lose`), `tools/tv-e2e.sh`, `games/Tactics/README.md`
- Test: `src/ArcBox.Server.Tests/`, a new room test

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void A_stage_battle_runs_with_no_host_signals()
```

- [ ] **Step 2: Add** `ARCBOX_STAGE=1` to `tools/tv-e2e.sh`, seeded with `Tactics:Seed`. It fails unless:
  - walking in starts the fight
  - `TACTICS screen` shows a stage
  - `ARCBOX_RECONNECT=1` mid-fight logs a catch-up and no replay
  - `ARCBOX_RESTART=1` resumes
  - `--lose` ends at the recovery spawn
  - a second walk-in plays the same encounter
  - `--drop-signals` still finishes
- [ ] **Step 3: Run everything:** `dotnet build ArcBox.slnx`, `dotnet test ArcBox.slnx`, `npm test` and `npm run build` in `web/play`. Expected: 0 warnings, all pass.
- [ ] **Step 4: For the user:** River Crossing on the TV, won once and lost once. The user judges the walk-in and the grid fade here and at 5e.
- [ ] **Step 5: Commit** `feat(tactics-playtest): fights on the spot, end to end`
