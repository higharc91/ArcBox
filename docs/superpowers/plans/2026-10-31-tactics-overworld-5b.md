# Overworld 5b: Walking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the user walks all of Ashen Ford on the TV from a phone.

**Architecture:** input is level-triggered with a server lease, so any lost message heals itself. The server moves the party in whole tiles, and the first step fires inside the press's own input call. The TV plays a forward-only path buffer, with followers as offsets along that path. Phone views never hold position, so phones don't re-render while walking. The world draws from cached static blocks, and each frame repaints only the items in front of the party.

**Tech Stack:** C# / .NET 10, Godot 4.7.2 .NET, Preact, xUnit v3, Vitest.

**Spec:** [the overworld design](../specs/2026-10-31-tactics-overworld-design.md), §2, §3.4, §4.1, §4.4 to §4.6. Starts after [5a](2026-10-31-tactics-overworld.md) is merged.

## Global Constraints

- The 5a plan's constraints hold.
- Don't change game rules or balance numbers. The walk screen and exploration TV data are the contract change the spec approved in §4.5. Existing battle screens don't change.
- `Tactics.Logic` stays clock-free: durations are computed in `Tactics.Game`.
- A walk view never holds position, a deadline or a toast.
- No timer runs while nobody is holding a direction, except `Timers.Lead` and the save timer.
- A slow or missing TV never holds up the game. Walking has no `shown` signal.
- Godot windows open on the left monitor and never take focus.
- Stage files by name. Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- A release lost in each way listed in §1 stops the party within the lease.
- A phone view doesn't change on a step.
- A stale `send` in the pad keeps the old viewId, and everything it sends is dropped.
- Save count over a 60 s walk.
- A saved tile that a repaint made illegal.
- The party behind a cliff, and behind a stamped prop, on screen.
- A view stream with dropped views doesn't move anyone backward.
- The world keeps the board's lighting, if Task 8 of the TV plan has landed: check against `BoardLighting`.

## Defaults chosen while the user is away

These are decided, and go on the list for the user to overrule.

1. **An 8-way pad, not a d-pad.** Screen up is tile `(-1,-1)`, so a grid-aligned road runs at about 26.6° on screen and a 4-way pad couldn't follow it. `DPad` in `turn.tsx` also uses click events and moves a cursor, so it can't be reused.
2. **The session leader takes the lead back** the next time the party is stopped. Otherwise a 2-second blip loses the lead for the night.
3. **Hazard tiles stay walkable and do no harm.** Making them harmful would be a rules change.
4. **A walk-back content check.** Drops are free at any height, so one-way ground could soft-lock the party. Painted cliffs may fail this until there's a way round.

## Where the spec or 5a falls short

1. **Blocking props don't exist in the world.** `RegionSource.Stamp` drops them, `WorldRegion.Walkable` ignores them, and the stamp check compares flags exactly, so a prop can't be painted as Impassable either. Without Task 1 the party walks through River Crossing's four blocking props, and the TV doesn't draw them. The reachability flood treats those tiles as walkable too.
2. **"A direction on press and a stop on release" (§4.1) isn't safe.** `GameSession.Input` drops a stale viewId, `RateLimitHubFilter` silently drops excess `SendInput`, and `session.send` swallows failures. Any of these can lose a stop, so a lease is required.
3. **The 5a plan's "reuses milestone 3b's board" is only partly true.** `BoardFit.Of` would shrink a 160×120 region to about 0.2×, `BoardCanvas` re-records its list at 20 Hz, water rewrites UVs at draw time, and units are merged into the list. `BoardPlan` and `BoardTerrain` reuse well. `BoardCanvas`'s frame loop doesn't.
4. **The version bump would crash without a code change.** `TacticsDocument.Read` throws on any other version, and Start and `CardFor` both call it uncaught.
5. **The spec's walk time needs a slower step than battle.** Battle walks at 0.2 s a tile, so 160 tiles takes 32 s, not one to two minutes. This plan uses 0.30 s.
6. **"The party can stop anywhere" (§2) has no way out and no end hook.** A session ends only from Results, and `IGame` has no end hook. So 5b adds a VIP "End session" on the walk screen, and it saves whenever the party stops.
7. **The leader's Jump.** Rogue, monk and barbarian have Jump 3, so a per-leader Jump would change the map with whoever leads. Use `ClassDef.DefaultJump` (2), as the region check does.
8. **"Exploration waits" (§4.6) needs no phase.** With no input, nothing runs.

## Needed by 5c and 5d

Those plans found these, and it's cheaper to build them in now:

- `ExploreView` is present in every phase once a region exists, so a TV reconnecting mid-fight can learn the region (Task 3).
- `Timers.Lead` stops when the pad leaves the screen. No pad means no idle ping, so the 8 s rule would take the lead away during any battle (Task 4).
- A `walk` input outside Exploration is ignored before `BattleInput`, with no toast (Task 4).
- `WalkTrack.Seed(places)`: with empty `Legs` every follower would stack on the leader's tile after any `Epoch` (Task 9).
- `WorldCanvas` sits in its own `CanvasLayer` under the UI, so positive-Z blocks can't paint over the HUD, and `BoardCanvas` can join it above the blocks (Task 8).

## Design

### 1. Input

One action through the existing `CustomInput`: `{action:"walk", data:{dir:"ne"}}`, with `dir` one of n, ne, e, se, s, sw, w, nw, or null for stopped. The phone sends screen directions and the server maps them:

| dir | tile delta | dir | tile delta |
|---|---|---|---|
| n | (-1,-1) | s | (1,1) |
| ne | (0,-1) | sw | (0,1) |
| e | (1,-1) | w | (-1,1) |
| se | (1,0) | nw | (-1,0) |

**Cadence:**

- At once on press, change or release.
- Every 250 ms while held.
- Every 3 s while idle on the leader's phone.
- A second stop 150 ms after release. `dir: null` is idempotent, so this is only a retry.
- Direction changes are coalesced to one per 66 ms. The stop bypasses this.

That's about 4 a second, against 20 tokens a second per connection in `RoomOptions.InvocationsPerSecond`.

**A repeated `dir` only extends the lease. It never steps.** The dev keyboard path ignores `e.repeat`.

**Deadman:** `_heldUntil = Now + HoldLease` (750 ms). The step tick checks it, and an expired lease stops the party. The worst case is about 3 tiles of run-on, only when both stops are lost.

| Case | What stops it |
|---|---|
| Normal release | `pointerup` stop |
| Android long-press, browser gesture | `pointercancel`, `lostpointercapture`; `contextmenu` prevented |
| Tab loses focus | `blur`, `visibilitychange`, `pagehide` send stop and clear the ping timer |
| Phone locks and JS freezes | Lease |
| Network blip | Lease |
| View changed under a stop | Lease; the pad also re-sends its state on any new `viewId` |
| Lost to the rate limiter | Retry stop, then lease |

**Stale `send`:** `App.tsx` closes over the viewId each render, so the pad reads `send` from a ref updated every render.

**The phone stays responsive:** `GameSession` mints a new view only when the JSON changes, so the walk view holds no position, deadline or toast. `Toast` changes `Header`, so blocked steps never toast. Pad state lives in refs, and pressed state is written straight to the DOM, as `useWobble` does.

**Tap sound:** `ControllerScreen` wraps every send with `feedback.onSend()`, so each ping would click. Add `stream(action, data)` to `CustomScreenProps`, which skips the cue. The pad vibrates for 8 ms on press.

**Latency target:** thumb to first TV motion ≤ 100 ms on a LAN, excluding the panel. Measured in Task 11.

### 2. Server step

**Rate:** 0.30 s per orthogonal tile, ×√2 on a diagonal. Duration uses the rules:

`Ms = Step × Paths.StepCost × (diagonal ? √2 : 1) + DropMs × max(0, drop − 2)`

**Timers:** schedule against an absolute time so one-shot timers don't drift: `_stepDue` advances by each leg, then `SetTimer(Step, max(_stepDue − Now, 1 ms))`. If a stall has passed a whole step, restart from Now. Never burst.

**No tick storm:** a step timer exists only while `_dir != null`, and each tick re-arms only if the lease is live. A leader change, a phase change or `EndGame` also cancels it.

**The rule:** `WorldWalk.Next(Terrain, Tile from, WalkDir, int jump, bool? lastAlongX)`, using `Paths.StepCost` and `Terrain.Walkable` directly.

- **Climb:** ≤ 2. Beyond that is blocked.
- **Drop:** free at any height. A step's duration grows for long ones.
- **Deep water, void, Impassable, blocking props:** blocked through `Terrain.Walkable`.
- **Diagonal:** legal only if both corner tiles are walkable and at least one L-path is legal leg by leg. No cutting past wall corners, which keeps 4-way reachability valid.
- **Sliding:** a blocked diagonal tries its two axes, preferring the last axis moved. A blocked straight step doesn't slide: the party turns to face that way and stays.

**The party:** the server holds one leader tile plus a trail of recent legs. Followers are drawn along that trail, so they can't stand on anything illegal.

### 3. What reaches the TV

Added to `TacticsHostView` as `Explore`, under about 1 KB, sent only when a step commits or `Held` changes:

```csharp
public sealed record WalkLeg(int Seq, Tile To, int Ms);
public sealed record WalkerView(string Id, string Name, string Sprite, bool Here, bool Leads);
public sealed record ExploreView(string Region, string Name, int Epoch, int Seq, Tile At, Tile Facing, bool Held,
    ImmutableArray<WalkLeg> Legs, ImmutableArray<WalkerView> Party, string? Leader);
```

`Legs` holds the last 12, because `MainThreadQueue` keeps only the newest view per frame. `Epoch` changes only on a teleport.

**`WalkTrack`:** a forward-only playhead on a polyline, so there's no rubber-banding. `rate = clamp(1 + 2 × (Lag − 0.08 s), 0.75, 2)`, an 80 ms jitter buffer built up by running slightly slow after a start, so the first step still shows at once. `Held` with an empty queue keeps the walk animation and holds position.

**Stall or reconnect:** lag over 1.2 s jumps the playhead along the path to the target lag. A seq gap beyond `Legs`, a new `Epoch` or a fresh scene snaps, and rebuilds the trail from `Legs`.

**`WorldCamera`:** fixed scale 2. Exponential follow, τ ≈ 0.12 s. Look-ahead up to 1.5 tiles along the heading, τ ≈ 0.5 s. Height low-passed at 0.3 s. Clamp in map coordinates: U ∈ [16, W−16], V ∈ [16, H−16]. A region too small for the margin is centred. Origin rounds to whole layout pixels, as `BoardFit.Of` does.

### 4. Leader

- **Who:** the next member after `WorldSave.LastLeader`, in `_doc.Members` order (ordinal by profile), because seat order changes between nights. Only connected players with a character count.
- **When it rotates:** once at each `Start`, saved at once. `Resume` doesn't rotate.
- **Handover:** the party stops first and any step timer is cancelled. It happens on `OnPlayerConnectionChanged(false)`, and on `Timers.Lead` after 8 s with nothing from the leader's phone, because stateful reconnect can delay the disconnect event.
- **Coming back:** the session leader takes the lead back the next time the party is stopped.

Both phone views are `Screens.Walk`, with no position:

```ts
{ leader: boolean, leaderName, region, objective, me: {name, cls, level},
  party: [{name, cls, here, leads}], vip: boolean, canBattle: boolean, portal }
```

The leader sees the pad. Everyone else sees the objective, their character, and the party with the crown. The VIP also gets "Choose a battle" and "End session". A member with no character gets `pickClass` during Exploration, without stopping the party.

### 5. Phases and saves

Add `Exploration` at the end of `TacticsPhase`, so existing values keep their order.

```
Muster → Exploration ⇄ EncounterPick (VIP's dev shortcut) → Battle → Results → Exploration
```

- `MusterDone` goes to Exploration if a region exists, else EncounterPick.
- `Results` "next" returns to Exploration, at the same tile.
- The list stays reachable by `{action:"battles"}`, with "Back to walking".
- `OnTimer` and `OnHostSignal` early-return outside Battle, so `OnTimer` becomes a phase switch.
- `Resume` with no battle reaches Exploration through `ToMusterOrPick`, with nobody online, and nothing runs until a phone connects.

**Document v1:**

```csharp
public sealed record WorldSave(string Region, Tile At, Tile Facing, string? LastLeader,
    ImmutableArray<string> Flags, ImmutableArray<string> Beats);
// TacticsDocument gains: WorldSave? World = null, CurrentVersion = 1
```

`Flags` and `Beats` go in now, to avoid a second bump in 5d. `Read` accepts 0 and 1 and writes 1. A missing `World` means the start spawn. Default `ImmutableArray`s are normalised in `Read`. A saved tile the repainted region no longer allows falls back to the start spawn, with a `_notice`.

**Saves:** 1 s after the party stops, if it moved. Every 15 s while walking. At once on phase change, leader change, disconnect and End session.

### 6. Chunks on the TV

Estimates, measured by Task 7:

- About 506 visible tile tops at scale 2, about 720 candidates allowing for height 9, across 4 to 6 chunks and 9 at worst.
- 19,200 tiles at 1.2 to 2 items each, so 23,000 to 38,000 items. A battle board is 168 tiles.

**Structure:**

- `WorldGround.Of(region, maps)` in `Tactics.Logic` builds one `BattleMap` of the whole region through `BattleMap.FromBytes`, with stamp props moved to world tiles and `void/deep` for missing chunks. The server's `Terrain` and the TV's `BoardTerrain` both use it. `AddSide` and `Beside` then read real neighbours, so seams are correct. A per-chunk `BattleMap` would draw skirts at every border.
- `BoardPlan.BuildBlock(terrain, art, TileRect, grid: false)`: the same code, for a 16×16 block. The draw unit is a block, and the data unit stays the 32×32 chunk.
- `WorldBlock : Node2D` runs `_Draw` once. Moving the camera changes only a transform.
- Blocks use `ZIndex = bx + by`. A prop more than about 2 tiles wide could mis-sort across a block border, so check by eye.
- **Units:** each frame, an overlay draws the units merged with only the items that sort later and overlap a unit's rect, using `BoardOrder.Merge`. Painting those twice gives the same picture, except that semi-transparent pixels blend twice, so check lips by eye.
- **Water and lava:** `FrameTextures` holds one single-frame `ImageTexture` per animated image and calls `Update` with the current frame. A separate water layer would break painter's order. Lit lava needs a second `FrameTextures` for its glow.
- **Lights:** one lighting object for the whole region, built off-thread with its probe grid, so a pool doesn't stop at a block edge. Blocks bake each light at its mean, and flicker stays on halos and units. Floors don't breathe in the world, which costs little in a daylit region and needs no shader. The overlay reads the same frozen colours, or painting twice wouldn't match. If floors must flicker later, a world-space light-map texture with one canvas shader is the upgrade; `BoardLight` and the falloff carry over. Three things to do here: add a mean variant of `LitBoard.ColourAt` and `LightNow`, which read "now" and would freeze each light at the last `Step` (Task 7). Cull `BoardLighting.Halos()` by view, or a region records every halo at 20 Hz (Task 8). Extend `StatedLight`'s test to region ids, so a region named with a room word can't go dark unchecked (Task 7).

**Loading:**

- Off the main thread: parse, build `WorldGround`, `BoardTerrain` and every block's plan, and `Prepare` arrays once `Load()` is split out.
- On the main thread, behind the opening fade: every texture in the plans, one per frame, because `Load` decodes PNGs on the spot. Then the blocks around the party.
- After reveal: remaining blocks at 4 ms a frame, nearest to `leader + velocity × 2 s` first. An unrecorded block shows clear colour and logs `WORLD late block`.

Eviction isn't needed in this slice. If Task 7's numbers are bad, drop the block size first and re-check item counts.

### 7. Risks

- **Occluder repaint on tall terrain:** the overlap query must allow for height 9 sides. Pin it with a screenshot behind a cliff.
- **`_Draw` cost in C#:** each `DrawPrimitive` marshals arrays. Unmeasured.
- **Two-tap direction lag:** a turn takes effect at the next tile, up to 0.42 s away. If this feels bad, shorten the step, not the protocol.
- **SignalR buffered replay:** a late `walk` can arrive. The stop follows it in order, and the lease bounds it.
- **Wake lock needs HTTPS:** on plain LAN HTTP a leader's phone may dim on a long walk.
- **5c:** a `BoardFit` at fixed scale must be able to host the battle board.

### Tunables only a person can judge

| Number | Default | Lives in |
|---|---|---|
| Step per tile | 300 ms | `WalkTiming.Step` |
| Extra per drop level | 60 ms | `WalkTiming.DropStep` |
| Hold lease / ping | 750 / 250 ms | `WalkTiming.HoldLease`, `PING_MS` |
| Lead silence / idle ping | 8 s / 3 s | `WalkTiming.LeadSilence`, `IDLE_PING_MS` |
| Save after stop / while walking | 1 s / 15 s | `WalkTiming` |
| TV jitter buffer | 80 ms | `WorldTuning.TargetLag` |
| Rate clamp, gain | 0.75 to 2, 2 | `WorldTuning` |
| Snap lag | 1.2 s | `WorldTuning` |
| Follower spacing | 0.9 tile | `WorldTuning` |
| Camera τ, look-ahead, height τ | 0.12 s, 1.5 tiles, 0.3 s | `WorldTuning` |
| Camera edge margin | 16 tiles | `WorldTuning` |
| Block size, per-frame budget | 16 tiles, 4 ms | `WorldTuning` |
| Pad dead zone, sectors | 18% of radius, equal 45° | `walkModel.ts` |
| Scale | 2 | `WorldTuning` |

`WalkTiming` joins `TacticsTiming` as an optional parameter, so existing constructors still compile.

---

### Task 1: The ground the party walks on

**Files:**
- Create: `games/Tactics/Tactics.Logic/Map/WorldGround.cs`, `games/Tactics/Tactics.Logic/Map/WorldWalk.cs`
- Modify: `games/Tactics/Tactics.Logic/Content/Validation/Regions.cs`, `games/Tactics/Tactics.Logic/Content/RulesPack.cs` (`GroundFor(string id)`, one shared cached instance per region)
- Test: `games/Tactics/Tactics.Logic.Tests/Map/WorldWalkTests.cs`, `games/Tactics/Tactics.Logic.Tests/Content/RegionCheckTests.cs`

**Interfaces:**
- `sealed class WorldGround`: `static Of(WorldRegion, IReadOnlyDictionary<string, BattleMap>)`, `BattleMap Map`, `Terrain Terrain`
- `enum WalkDir { N, NE, E, SE, S, SW, W, NW }`
- `readonly record struct WalkMove(Tile To, int Cost, bool Diagonal, bool Slid)`
- `static WalkMove? WorldWalk.Next(Terrain, Tile from, WalkDir, int jump, bool? lastAlongX)`
- `static (int Dx, int Dy) WorldWalk.Delta(WalkDir)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Stamped_props_are_on_the_ground_at_world_tiles()
[Fact] public void A_blocking_prop_stops_the_party()
[Fact] public void A_missing_chunk_is_impassable_void()
[Fact] public void Screen_directions_map_to_tiles()             // the table in §1
[Fact] public void A_climb_over_the_default_jump_is_blocked()
[Fact] public void A_drop_of_any_height_is_allowed()
[Fact] public void Deep_water_blocks()
[Fact] public void A_ford_costs_what_the_rules_say()
[Fact] public void A_diagonal_cant_cut_a_corner()
[Fact] public void A_blocked_diagonal_slides_along_the_wall()
[Fact] public void A_slide_prefers_the_axis_it_was_on()
[Fact] public void A_blocked_straight_step_doesnt_slide()
[Fact] public void A_marker_only_reachable_through_a_prop_is_an_error()
[Fact] public void A_pocket_you_cant_walk_back_from_is_an_error()  // names a tile
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Logic.Tests`. Expected: fails to compile.
- [ ] **Step 3: Implement.** The region flood uses `Paths.StepCost` on `WorldGround.Terrain`. Add a reverse flood from the start spawn. Check the Ashen Ford draft still passes.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-world): the ground the party walks on`

### Task 2: Saved position

**Files:**
- Modify: `games/Tactics/Tactics.Game/Campaign/TacticsDocument.cs`, `games/Tactics/Tactics.Game/TacticsTiming.cs`
- Test: `games/Tactics/Tactics.Game.Tests/DocumentTests.cs`

**Interfaces:** `WorldSave`, `CurrentVersion = 1`, `WalkTiming`, `TacticsTiming.Walk`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_version_0_document_reads_with_no_world()
[Fact] public void A_version_0_document_keeps_its_battle()
[Fact] public void A_saved_world_round_trips()
[Fact] public void An_unknown_version_is_still_refused()
[Fact] public void The_portal_reads_a_version_0_document()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Game.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement,** as §5 describes.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): the campaign keeps the party's place`

### Task 3: The Exploration phase, without steps

**Files:**
- Create: `games/Tactics/Tactics.Game/TacticsGame.Explore.cs`
- Modify: `games/Tactics/Tactics.Game/TacticsGame.cs`, `TacticsGame.Views.cs`, `games/Tactics/Tactics.Presentation/PhaseScreen.cs`, `games/Tactics/Tactics.Presentation/Audio/MusicChoice.cs`
- Test: `games/Tactics/Tactics.Game.Tests/ExplorationTests.cs`, `HostViewContractTests.cs`, `PhaseScreenTests.cs`, `MusicChoiceTests.cs`. Update the `ToBattle` helper and the fixture capture to send `battles`.

**Interfaces:**
- `TacticsPhase.Exploration`, `Screens.Walk`, `Timers.Step/Lead/Save`
- `ExploreView`, `WalkerView`, `WalkLeg`, `TacticsHostView.Explore`
- Inputs: `battles`, `backToWalk`, `endSession`
- `Player? Leader`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void After_muster_the_party_is_at_the_start_spawn()
[Fact] public void A_pack_with_no_region_goes_to_the_battle_list()
[Fact] public void The_party_resumes_where_it_stopped()
[Fact] public void A_saved_tile_that_cant_be_stood_on_goes_to_the_start_with_a_notice()
[Fact] public void The_lead_rotates_each_session_by_profile_not_seat()
[Fact] public void Only_the_vip_opens_the_battle_list()
[Fact] public void Next_after_results_goes_back_to_walking_on_the_same_tile()
[Fact] public void End_session_saves_and_ends_the_game()
[Fact] public void A_member_with_no_character_picks_one_without_stopping_the_party()
[Fact] public void A_resume_with_nobody_here_runs_no_timers()
[Fact] public void A_battle_saved_mid_fight_still_continues()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Game.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** that and `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): the exploration phase`

### Task 4: The step and the deadman

**Files:**
- Modify: `games/Tactics/Tactics.Game/TacticsGame.Explore.cs`
- Test: `games/Tactics/Tactics.Game.Tests/WalkingTests.cs`

**Interfaces:** input `walk {dir: string|null}`. Private: `WalkInput`, `Step`, `StopWalking`, `HandLead`, `ReconsiderLeader`, `SaveSoon`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_press_steps_at_once()                       // same call, before any timer
[Fact] public void Holding_steps_at_the_rate()
[Fact] public void Diagonals_and_difficult_ground_take_longer()
[Fact] public void Pings_dont_change_the_speed()                 // 15 pings/s
[Fact] public void A_tap_moves_one_tile()
[Fact] public void A_stop_ends_the_timer()
[Fact] public void A_lost_release_stops_within_the_lease()
[Fact] public void No_timer_is_left_after_the_lease()
[Fact] public void Someone_else_cant_steer()
[Fact] public void A_bad_direction_is_ignored()
[Fact] public void A_late_timer_doesnt_drift()
[Fact] public void A_long_stall_gives_one_step_not_a_burst()
[Fact] public void A_step_doesnt_change_any_phone_view()         // PlayerView JSON identical across 20 steps
[Fact] public void Legs_keep_the_last_12()
[Fact] public void The_leader_leaving_stops_the_party_and_hands_on()
[Fact] public void Silence_hands_the_lead_on()
[Fact] public void The_session_leader_takes_it_back_once_stopped()
[Fact] public void Nobody_here_means_no_leader_and_no_timers()
[Fact] public void A_60_second_walk_saves_at_most_6_times()
[Fact] public void Stopping_saves_within_a_second()
[Fact] public void A_battle_input_doesnt_reach_walking()
[Fact] public void The_whole_region_can_be_walked()              // BFS route over Ashen Ford, every beat spot, through OnInput and Advance
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-game): the party walks`

### Task 5: The phone's walk screen

**Files:**
- Create: `web/play/src/games/tactics/walk.tsx`, `web/play/src/games/tactics/walkModel.ts`
- Modify: `web/play/src/games/module.ts` (`stream`), `web/play/src/widgets/Custom.tsx`, `web/play/src/widgets/ControllerScreen.tsx`, `web/play/src/widgets/common.tsx`, `web/play/src/games/tactics/index.ts`, `types.ts`, `screens.tsx`, `tactics.css`
- Regenerate: fixtures, adding `walk-leader` and `walk-follower`. Update the screen list in `PhoneFixtureTests.Every_phone_screen_matches_its_fixture`.
- Test: `web/play/test/walk.test.tsx`, `web/play/test/walkModel.test.ts`

**Interfaces:**
- `dirAt(dx, dy, deadZone): Dir | null`
- `class WalkSender(send, clock)`: `press(dir)`, `release()`, `newView()`, `dispose()`

**Pad:** `touch-action: none`, `setPointerCapture`, first `pointerId` only, sliding the thumb changes direction without lifting, at least 200 px, no `setState` while held, arrow keys for dev.

- [ ] **Step 1: Write the failing tests**

```ts
it("sends a direction at once on press")
it("pings every 250 ms while held")
it("sends a stop at once, and again 150 ms later")
it("a slid thumb changes direction without a stop")
it("coalesces fast direction changes but never a stop")
it("stops on pointercancel, blur, visibility hidden and pagehide")
it("uses the newest send after a new view")          // the stale closure
it("resends its state on a new view")
it("ignores a second finger")
it("doesn't re-render while held")
it("doesn't play the tap cue on a ping")
it("shows the pad only to the leader")
it("key repeat sends nothing extra")
```

- [ ] **Step 2: Run** `npm test` in `web/play`. Expected: FAIL.
- [ ] **Step 3: Implement,** then run `UPDATE_FIXTURES=1 dotnet test --project games/Tactics/Tactics.Game.Tests` and check the fixture diff.
- [ ] **Step 4: Run** `npm test` and `npm run build`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-phone): the walk screen`

### Task 6: Hold and release through a real room

**Files:**
- Test: `src/ArcBox.Server.Tests/`, a new room test on a fake clock, following that project's fixtures

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_walk_step_sends_the_host_a_view_and_no_phone_anything()
[Fact] public void A_stale_view_id_stop_is_dropped_and_the_lease_ends_it()
[Fact] public void Inputs_over_the_rate_limit_dont_break_a_walk()
```

- [ ] **Step 2: Run** `dotnet test --project src/ArcBox.Server.Tests`.
- [ ] **Step 3: Fix** anything they find.
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `test(tactics-server): walking through a real room`

### Task 7: World plan and the cost spike

**Files:**
- Create: `games/Tactics/Tactics.Presentation/World/WorldPlan.cs`, `WorldTuning.cs`, `BlockQueue.cs`, `DemoRegion.cs`, `src/ArcBox.Host/games/tactics/WorldBlock.cs`
- Modify: `games/Tactics/Tactics.Presentation/Board/BoardPlan.cs` (`BuildBlock`, keeping `Build` as it is), `BoardTerrain.cs` (`TilesIn(TileRect)`), `src/ArcBox.Host/games/tactics/BoardCanvas.cs` (extract `BoardPainter`), `src/ArcBox.Host/Shell/AppArgs.cs` (`--world-demo`, `--walk-log`)
- Test: `games/Tactics/Tactics.Presentation.Tests/World/WorldPlanTests.cs`

**Interfaces:**
- `readonly record struct TileRect(int X, int Y, int W, int H)`
- `BoardPlan.BuildBlock(BoardTerrain, ArtIndex, TileRect, bool grid)`
- `sealed class WorldPlan`: `static Build(...)`, `Blocks`, `ItemsOver(rect, long afterOrder)`, `Paths`
- `DemoRegion.Hilly(160, 120)`: a generated region with a river and hills

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Blocks_add_up_to_the_whole_board()     // the union equals BoardPlan.Build's items: no seams, no skirts
[Fact] public void A_cliff_across_a_block_edge_has_its_side()
[Fact] public void A_stamped_prop_is_drawn_on_its_tile()
[Fact] public void Block_order_agrees_with_painters_order_where_items_overlap()
[Fact] public void The_view_window_touches_at_most_9_chunks()
[Fact] public void Blocks_are_queued_nearest_the_party_first()
[Fact] public void Items_over_a_rect_are_only_those_in_front()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Measure.** `"$GODOT" --path src/ArcBox.Host -- --world-demo --perf` at 1920x1080. Record total items, off-thread build time, the worst `_Draw` per block, memory, and frame time while panning. **Stop and report if a block's `_Draw` is over 4 ms.**
- [ ] **Step 6: Commit** `feat(tactics-tv): the world's plan`

### Task 8: Drawing the world

**Files:**
- Create: `src/ArcBox.Host/games/tactics/WorldCanvas.cs`, `FrameTextures.cs`
- Modify: `src/ArcBox.Host/games/tactics/BoardCanvas.cs` (`SetDrawn`)
- Test: pure parts in `games/Tactics/Tactics.Presentation.Tests/World/`

- [ ] **Step 1: Show the plan** with the `--world-demo` camera on a slow pan: off-thread build, one-texture-per-frame loading, block budget, opening fade.
- [ ] **Step 2: Add** `FrameTextures` and the occluder overlay, with a stand-in unit walking a scripted route, and carry the board's lights across.
- [ ] **Step 3: Check by eye,** with `--shots`: behind a cliff, behind a stamped crate, water at a block edge, a block corner, the edge of the region, and no visible hitch on a full crossing.
- [ ] **Step 4: Commit** `feat(tactics-tv): draw the world in blocks`

### Task 9: Following the party

**Files:**
- Create: `games/Tactics/Tactics.Presentation/World/WalkTrack.cs`, `WorldCamera.cs`
- Modify: `games/Tactics/Tactics.Presentation/Board/UnitPlan.cs` (a `Walkers` build with no bars, plus a crown mark)
- Test: `games/Tactics/Tactics.Presentation.Tests/World/WalkTrackTests.cs`, `WorldCameraTests.cs`

**Interfaces:**
- `WalkTrack`: `Receive(ExploreView)`, `Advance(double)`, `Places(BoardTerrain)`, `Poses`
- `WorldCamera`: `Follow(Vector2 map, float height, Vector2 heading, double dt)`, `BoardFit Fit`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void The_first_step_shows_at_once()
[Fact] public void The_leader_never_moves_backward()          // a random stream with dropped and bunched views
[Fact] public void Missed_views_are_filled_in_from_the_legs()
[Fact] public void Late_views_dont_stop_the_walk_animation_while_held()
[Fact] public void A_long_stall_catches_up_along_the_path()
[Fact] public void A_reconnect_snaps_with_followers_on_the_trail()
[Fact] public void A_new_epoch_snaps()
[Fact] public void Followers_trail_at_the_spacing_and_stop_when_the_leader_does()
[Fact] public void Heights_step_as_the_battle_walk_does()
[Fact] public void The_camera_stops_short_of_the_region_edge()
[Fact] public void A_small_region_is_centred()
[Fact] public void The_camera_origin_is_on_whole_pixels()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-tv): the party and the camera`

### Task 10: The exploration screen on the TV

**Files:**
- Create: `src/ArcBox.Host/games/tactics/WorldHud.cs`
- Modify: `src/ArcBox.Host/games/tactics/TacticsScene.cs`, `games/Tactics/Tactics.Presentation/PhaseScreen.cs`, `Audio/MusicChoice.cs`, `docs/tv-host.md`

Add the two HUD panels from mockup 02: chapter and region at top left, party leader at top right. Add footsteps through `SoundBank.Step`, and print `WALK <seq> <x> <y> <ms>` under `--walk-log`.

- [ ] **Step 1: Add** `PhaseScreenTests` for the Exploration screen, then a real view through `HostViewContractTests`.
- [ ] **Step 2: Implement.** `Render` runs 3 to 5 times a second, so `Show` must do no node churn when `Rows` is empty.
- [ ] **Step 3: Run** a server, the TV and a dev phone. Confirm walking, a leader change, and a move to a battle and back.
- [ ] **Step 4: Commit** `feat(tactics-tv): exploration on the TV`

### Task 11: Bots, end to end, and a person

**Files:**
- Modify: `tools/tactics-playtest/bots.cjs`, `tools/tv-e2e.sh`, `games/Tactics/README.md`

Phone views have no position, and bots answer each view once, so walking is its own scripted loop:

- `--walk="e:4,ne:2,stop:1"`
- `--walk-drop`: hold, then cut the connection with no stop
- `--walk-latency`: time a press against the TV's `WALK` line

Existing runs send `battles` by default, so they keep working.

- [ ] **Step 1: Add the modes, and `ARCBOX_WALK=1` to `tools/tv-e2e.sh`.** It fails unless the TV's Seq climbs, `--walk-drop` stops within the lease, the party is on the saved tile after a server restart, and a killed TV comes back with a snap.
- [ ] **Step 2: Run everything:** `dotnet build ArcBox.slnx`, `dotnet test ArcBox.slnx`, `npm test` and `npm run build` in `web/play`. Expected: 0 warnings, all pass.
- [ ] **Step 3: Measure** latency (target ≤ 100 ms) and how long a killed phone takes to raise the disconnect event, and note both in the report.
- [ ] **Step 4: For the user:** a real phone on the LAN. Lock it mid-walk, switch apps, and walk the whole region. The user judges feel here and at 5e.
- [ ] **Step 5: Commit** `feat(tactics-playtest): the bots walk`
