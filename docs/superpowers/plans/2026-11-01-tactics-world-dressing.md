# World Dressing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** regions hold painted props, ground detail and ambient life, so Ashen Ford feels inhabited and the party is never lost behind a tree.

**Architecture:** a region file gains a grouped `props` list. `WorldGround` merges those props with the stamps', so walking and every check see them with no new code path. On the TV, low props bake into the static blocks, and tall props draw in a thin layer of their own so they can fade. Ambient life is TV-only.

**Tech Stack:** C# / .NET 10, Godot 4.7.2 .NET, xUnit v3.

**Spec:** [world dressing](../specs/2026-11-01-tactics-world-dressing-design.md). Starts after the story runtime (overworld 5d) has landed on `ccr-18ad4510-q68nad`. Start by rebasing on it.

## Global Constraints

- Don't change game rules, balance numbers or the server-to-phone contract. Nothing here changes a phone view. The user reserved those decisions.
- Anything worded, ordered or computed is pure C# with tests. Godot classes only draw.
- Read frames, sizes, variants, `blocks`, `cover` and `height` from manifests. Never hard-code them.
- No generated art without asking. The pack's art only. List gaps for the user.
- A battle's picture and cost must not regress: `hall_of_the_flame:fight` is 521 draw calls, `river_crossing` 420.
- Godot windows open on the user's left monitor and never take focus: `--position 160,100`, plus an uncommitted `src/ArcBox.Host/override.cfg` with `[display]` then `window/size/no_focus=true`, deleted afterwards. Use `--audio-driver Dummy` for automated runs.
- Measure performance with no `--shots`. Captures stall frames: a real fight start of 2 to 5 ms logged 216 ms with them on. 144 fps is the cap, so 6.94 ms is the floor.
- `dotnet test --solution ArcBox.slnx` stays green with 0 warnings. The only known flake is `A_rebuild_on_the_largest_region_is_quick` (`StagePlanTests.cs:378`), a timing ratio.
- Don't open or merge PRs unless the user asks. Pull with rebase before each push. Stage files by name. Keep `.superpowers/` and `.claude/` out of commits. Commit trailer, last line: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- A party walking through a wood: nothing pops, no walker is painted over a tree in front of them, and no tree paints over a walker it's behind. Only motion shows this: sweep a route.
- A party split across a tree, one walker in front and one behind, and a party stopping behind a trunk.
- A tree painted across the only road: the export says so and names the tile.
- A fight beside tall props: units stay visible, and the stage and the world meet with no seam.
- Leaving a world and coming back with hundreds of faded and unfaded props: no leak, no second layer, nothing re-recorded.

---

### Task 1: The fading spike

Decides fade or silhouette. Throwaway drawing is fine, but keep what's good.

**Files:**
- Modify: `games/Tactics/Tactics.Presentation/World/DemoRegion.cs` (a wood: at least 1,500 tall props in one view's reach)
- Create: `games/Tactics/Tactics.Presentation/World/PropFade.cs`
- Modify: `src/ArcBox.Host/games/tactics/WorldDemo.cs`, `WorldCanvas.cs`

- [ ] **Step 1: Split tall props out of `WorldPlan` blocks** for the demo: `height >= 2` in the manifest goes to a per-block list, kept in `Blocks` order.
- [ ] **Step 2: Draw them in their own layer,** with an alpha per prop. Find the cheapest Godot way that keeps them sorted between blocks: one canvas item per tall prop, or per block with a re-record when a fade changes.
- [ ] **Step 3: Measure** `--world-demo` at 1920×1080 with a walker crossing the wood: frame times, draw calls, render CPU, against the same wood baked. Record it in `docs/tv-host.md`.
- [ ] **Step 4: Decide.** Fading passes if no frame is regularly over 16 ms on the RTX 5080, draw calls stay under about 6,000, and a walking median of 6.94 ms holds. Otherwise stop, say so, and swap Task 5 for the silhouette in the spec's §7.
- [ ] **Step 5: Commit** `feat(tactics-tv): tall props in a layer of their own`

### Task 2: Props in the format, the loader and the ground

**Files:**
- Modify: `games/Tactics/Tactics.Logic/Map/WorldRegion.cs`, `Map/WorldGround.cs`, `Content/Validation/Regions.cs`, `content/tactics/base/rules/schema/region.schema.json`, `games/Tactics/Tactics.WorldTools/RegionSource.cs`, `RegionWriter.cs`
- Test: `games/Tactics/Tactics.Logic.Tests/Content/RegionCheckTests.cs`, `games/Tactics/Tactics.Logic.Tests/Map/WorldWalkTests.cs`, `games/Tactics/Tactics.WorldTools.Tests/RegionWriterTests.cs`

**Interfaces:**
- Produces: `sealed record RegionProps(string Id, string? Variant, ImmutableArray<Tile> At)`, `WorldRegionDef.Props`. `PropPlacement` needs the variant carried through to drawing: add an optional `string? Variant = null` and check battle maps are unchanged.

`WorldGround.Of` adds region props to the props it already builds from stamps (`WorldGround.cs:54`), with `Blocks` and `Cover` from the props manifest.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_region_with_no_props_loads_as_before()
[Fact] public void A_props_group_round_trips_byte_identical()
[Fact] public void Props_sort_by_id_variant_and_tile()
[Fact] public void An_unknown_prop_is_an_error()
[Fact] public void An_unknown_variant_is_an_error()
[Fact] public void A_prop_outside_the_region_is_an_error()
[Fact] public void A_prop_in_a_void_chunk_is_an_error()
[Fact] public void A_prop_on_a_stamp_is_an_error()
[Fact] public void A_blocking_prop_stops_the_party()
[Fact] public void A_soft_prop_doesnt()
[Fact] public void A_beat_walled_off_by_props_is_an_error()
[Fact] public void Region_props_stay_out_of_the_battle_hash()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Logic.Tests`. Expected: fails to compile.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them, and** `dotnet test --solution ArcBox.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(tactics-world): regions hold props`

### Task 3: The Props layer

**Files:**
- Modify: `games/Tactics/Tactics.WorldTools/PaletteBuilder.cs`, `PaintedRegion.cs`, `RegionExport.cs`, `StoryOrder.cs` if it needs nothing more than `WorldGround`, `tools/tactics-world/addons/region_tools/RegionScene.cs`, `RegionToolsPlugin.cs`, `tools/tactics-world/README.md`
- Test: `games/Tactics/Tactics.WorldTools.Tests/PaintedRegionTests.cs`, `RegionExportTests.cs`

**Interfaces:**
- `PaletteBuilder` gains a prop atlas: one tile per prop and variant, from the manifest. Blocking swatches are marked.
- The test-only `SceneText` reader (`BasePackRegionTests`' `.tscn` reader) must read the fourth layer.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_painted_prop_reaches_the_region_file()
[Fact] public void Erasing_a_prop_leaves_the_ground()
[Fact] public void Apply_stamp_puts_nothing_on_the_props_layer()
[Fact] public void A_second_export_is_byte_identical()
[Fact] public void A_blocking_prop_on_a_road_warns_and_goes_to_it()
[Fact] public void A_prop_on_a_beat_spot_warns()
[Fact] public void A_prop_on_a_spawn_warns()
[Fact] public void A_tall_prop_in_front_of_a_stamp_warns()
[Fact] public void A_tree_across_the_road_fails_the_story_order()
```

"Road" means the materials the draft uses for roads. Read them from one list, and don't guess by name.

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.WorldTools.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement,** then a headless `--press-export=ashen_ford`. Expected: the scene and JSON unchanged, as Ashen Ford has no props yet.
- [ ] **Step 4: Update the painting guide:** the layer, swatches, blocking marks, what each warning means. Say the editor steps are unproven by hand.
- [ ] **Step 5: Run everything.** Expected: PASS.
- [ ] **Step 6: Commit** `feat(tactics-world): a layer to paint props on`

### Task 4: Drawing props, decals and tint

**Files:**
- Modify: `games/Tactics/Tactics.Presentation/Board/BoardPlan.cs`, `ArtIndex.cs`, `World/WorldPlan.cs`, `World/StagePlan.cs`, `src/ArcBox.Host/games/tactics/WorldCanvas.cs`, `docs/tv-host.md`
- Test: `games/Tactics/Tactics.Presentation.Tests/World/WorldPlanTests.cs`, `OverlayPictureTests.cs`, `StagePlanTests.cs`

Decals exist as `decal` textures: `flowers`, `leaves`, `pebbles`, `scorch_burnt`, `bones_bone`, `cracks_dark`, `puddle_water`, `puddle_swamp`, `blood_red`. Choose by a hash of the tile and the material, at about 1 tile in 6. Skip blood and bones on ordinary ground. The tint is a slow drift through the vertex colours already baked: no new art and no draw calls. Don't try a random cell of a texture or mirroring: both cut the dither at tile edges.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_region_prop_is_drawn_on_its_tile_with_its_variant()
[Fact] public void A_prop_with_light_data_lights_the_ground()
[Fact] public void Decals_are_the_same_on_every_build()
[Fact] public void A_stamped_tile_gets_no_decal()     // a stamp must draw as its battle map does
[Fact] public void Tint_is_continuous_across_a_block_edge()
[Fact] public void A_battle_board_is_unchanged()      // no decals, no tint on BoardPlan.Build
[Fact] public void The_stage_draws_props_near_the_fight()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them.** Expected: PASS.
- [ ] **Step 5: Check** `hall_of_the_flame:fight` still costs 521 draw calls. Run a real walk and a seeded fight on the TV: `ARCBOX_SERVER_ENV="Tactics__Seed__Region=ashen_ford Tactics__Seed__At__0=96 Tactics__Seed__At__1=61"`.
- [ ] **Step 6: Commit** `feat(tactics-tv): props and ground detail in the world`

### Task 5: Fading

Skip if Task 1 chose the silhouette. Then draw the party's silhouette over tall props, and adjust the tests.

**Files:**
- Create: `games/Tactics/Tactics.Presentation/World/PropFade.cs`
- Modify: `World/WorldPlan.cs` (`Overlay` skips what's faded), `World/WorldTuning.cs`, `games/Tactics/Tactics.Presentation/World/PropFade.cs`, `WorldCanvas.cs`
- Test: `games/Tactics/Tactics.Presentation.Tests/World/PropFadeTests.cs`, `OverlayPictureTests.cs`

**Interfaces:**
- `sealed class PropFade`: `Update(IEnumerable<BoardUnit> walkers, double dt)`, `AlphaOf(int prop)`.
- `WorldTuning`: `PropFadeAlpha` 0.35, `PropFadeSeconds` 0.2.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_tree_in_front_of_a_walker_fades()
[Fact] public void A_tree_behind_a_walker_doesnt()
[Fact] public void Only_trees_that_cover_a_walker_fade()
[Fact] public void It_comes_back_when_the_party_has_gone()
[Fact] public void A_party_split_across_a_tree_fades_it_once()
[Fact] public void The_fade_eases()
[Fact] public void A_snap_fades_at_once()
[Fact] public void Nothing_fades_in_a_fight()
[Fact] public void A_faded_tree_isnt_repainted_solid_over_a_walker()
```

- [ ] **Step 2: Run** `dotnet test --project games/Tactics/Tactics.Presentation.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them, plus** a walk through a wood on the TV. Look at shots of a walker behind a trunk, a block corner and a stopped party.
- [ ] **Step 5: Commit** `feat(tactics-tv): trees fade in front of the party`

### Task 6: Ambient life

**Files:**
- Create: `games/Tactics/Tactics.Presentation/World/AmbientPlan.cs`, `src/ArcBox.Host/games/tactics/WorldAmbient.cs`
- Modify: `src/ArcBox.Host/games/tactics/WorldCanvas.cs`, `docs/tv-host.md`
- Test: `games/Tactics/Tactics.Presentation.Tests/World/AmbientPlanTests.cs`

The art is in `content/tactics/base/ambient/particles`: bird, butterfly, chimney smoke, embers, fireflies, fish, flock, leaf, motes. Read frames and rates from `ambient/manifest.json`. Fireflies wait for a time of day, so leave them out. Rain and snow are weather, also out.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Butterflies_stay_over_grass()
[Fact] public void Fish_stay_in_deep_water()
[Fact] public void The_same_region_and_seed_give_the_same_life()
[Fact] public void Nothing_runs_away_from_the_camera()
[Fact] public void A_world_with_no_ambient_art_still_plays()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement.** The TV only: no view data and no input.
- [ ] **Step 4: Measure** a walk with it on and off. Expected: no change in the walking median.
- [ ] **Step 5: Commit** `feat(tactics-tv): the world moves`

### Task 7: Dress Ashen Ford

**Files:**
- Modify: `games/Tactics/Tactics.WorldTools/AshenFordDraft.cs`, `tools/tactics-world/addons/region_tools/DraftScene.cs`, `tools/tactics-world/regions/ashen_ford.tscn`, `content/tactics/base/rules/world/ashen_ford.json`
- Test: `games/Tactics/Tactics.WorldTools.Tests/AshenFordDraftTests.cs`, `BasePackRegionTests.cs`

Add a `--dress=ashen_ford` command that adds props to the existing scene. **It refuses a scene that already has props,** and it must never rewrite the terrain: the user may have painted it. Check `git log` on the scene first. Woods along the road in the middle lands, fences and carts by the fields, clutter in Emberwick, reeds at the banks, dead trees and rubble on the burned bank, ruins near Maud's hut. Use a fixed seed. Keep roads and spots clear.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void The_dressing_passes_with_no_warnings()
[Fact] public void The_story_still_walks_on_both_branches()
[Fact] public void The_road_stays_clear()
[Fact] public void Dressing_twice_is_refused_and_changes_nothing()
[Fact] public void Dressing_leaves_the_terrain_alone()
```

- [ ] **Step 2: Run** them. Expected: FAIL.
- [ ] **Step 3: Implement, dress and export** with the plugin's own export, headless.
- [ ] **Step 4: Look at it:** a walk from Emberwick to the ford on the real TV, with shots of the town, woods, farms and burned bank. Tune until it reads as a place. Measure the walk with no `--shots`.
- [ ] **Step 5: Run everything:** `dotnet build ArcBox.slnx`, `dotnet test --solution ArcBox.slnx`, and `tools/tv-e2e.sh` with `ARCBOX_GAME=tactics` and `ARCBOX_WALK=1`. Expected: 0 warnings, all pass.
- [ ] **Step 6: Commit** `feat(tactics-content): Ashen Ford, dressed`

**For the user:** refine the props in the editor, judge the look from the sofa, and note how long painting took (overworld spec §7).
