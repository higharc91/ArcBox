# ArcBox Tactics overworld, milestone 5a: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a region format the game loads and checks, a Godot workspace where the user paints it, and Claude's draft of Ashen Ford to paint over.

**Architecture:** regions are content files in the rules pack, in the battle map's tile format, split into chunks. `games/Tactics/Tactics.WorldTools` holds the pure code that builds and draws regions. A small Godot project at `tools/tactics-world` is the painting workspace, and only calls that library.

**Tech Stack:** C# / .NET 10, Godot 4.7.2 .NET, xUnit v3.

**Spec:** [`../specs/2026-10-31-tactics-overworld-design.md`](../specs/2026-10-31-tactics-overworld-design.md)

**Starts after:** milestone 3b. **Later milestones:** see [the outline](#later-milestones). Each gets its own plan once the work before it is merged, because 5b to 5e build on TV code that doesn't exist yet.

## Global Constraints

- Build has `TreatWarningsAsErrors`, nullable and implicit usings. Public types get XML doc comments.
- `Tactics.Logic` references only `ArcBox.Sdk`. No new NuGet packages, and no `System.Random` or `DateTime` in it.
- JSON uses `ArcJson.Options`; content is read with unknown properties rejected. String-keyed sorted collections use `Core/Ordinal.cs`.
- Region ids and battle maps are data. A stamped battle map's tiles must match exactly.
- Chunks are 32×32 tiles. A missing chunk is void: impassable.
- Heights are 0 to 9, as on battle maps.
- Painted work is never overwritten: the draft refuses to replace an existing scene.
- Commit trailer, as the last line: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Stage files by name.

## Review Focus

- A stamp placed at a height offset that pushes a tile above 9: the export reports it, rather than clamping and silently changing the battle.
- A chunk file with the wrong byte length or a tile outside its chunk: a content error, never a crash in the loader.
- Painting that disconnects a beat spot from the start spawn, such as a river with no crossing: a clear content error naming the spot and tile.
- Re-running the draft after painting: it stops and says the scene exists, and nothing changes.
- A material name in the scene that isn't in the terrain manifest: the export names the tile, rather than writing a material the TV can't draw.

---

## File Structure

| File | Responsibility |
|---|---|
| `games/Tactics/Tactics.Logic/Content/RegionDefs.cs` | `RegionDef` and its JSON records |
| `games/Tactics/Tactics.Logic/Map/WorldRegion.cs` | A loaded region: tile lookups, walkability, chunks |
| `games/Tactics/Tactics.Logic/Content/Validation/Regions.cs` | The region content checks |
| `content/tactics/base/rules/schema/region.schema.json` | Documents the file format |
| `games/Tactics/Tactics.WorldTools/RegionSource.cs` | The editable grid a scene or draft fills in |
| `games/Tactics/Tactics.WorldTools/RegionWriter.cs` | Turns a `RegionSource` into `RegionDef` JSON |
| `games/Tactics/Tactics.WorldTools/AshenFordDraft.cs` | Claude's first pass of the region |
| `games/Tactics/Tactics.WorldTools.Tests/` | Tests for the above |
| `tools/tactics-world/` | The Godot painting workspace |

## Task 1: Region format and loading

**Files:**
- Create: `games/Tactics/Tactics.Logic/Content/RegionDefs.cs`, `games/Tactics/Tactics.Logic/Map/WorldRegion.cs`, `content/tactics/base/rules/schema/region.schema.json`
- Modify: `games/Tactics/Tactics.Logic/Content/PackLoader.cs` (add `FileKind.Region` for `world/<id>.json`, and `Regions` on the loaded pack), `games/Tactics/Tactics.Logic/Content/RulesPack.cs`, `games/Tactics/Tactics.Logic/Content/Validation/ContentShape.cs`
- Test: `games/Tactics/Tactics.Logic.Tests/Content/RegionTests.cs`

**Interfaces:**
- Produces:
  - `record RegionDef(string Id, string Name, int Chapter, ImmutableArray<int> Size, ImmutableArray<string> Materials, ImmutableArray<RegionChunk> Chunks, ImmutableArray<RegionStamp> Stamps, ImmutableArray<BeatSpot> Beats, RegionSpawns Spawns, ImmutableArray<RegionEdge> Edges)`
  - `record RegionChunk(int X, int Y, string Tiles)`: chunk coordinates, base64 of 3 bytes a tile for 32×32 tiles
  - `record RegionStamp(string Map, string Encounter, Tile At, int Height)`: the map's tile (0, 0) is world tile `At`, and `Height` is added to its heights
  - `record BeatSpot(string Beat, Tile At)`, `record RegionSpawns(Tile Start, Tile Recovery)`, `record RegionEdge(string Neighbour, string Side, int Offset)`
  - `sealed class WorldRegion`: `static WorldRegion From(RegionDef def)`, `TileInfo? TileAt(Tile t)` (null for void), `bool Walkable(Tile t)`, `RegionDef Def`
  - `RulesPack.Regions: ImmutableSortedDictionary<string, WorldRegion>`, and `RulesPack.RegionFor(string id)`

- [ ] **Step 1: Write the failing tests in `RegionTests.cs`**

```csharp
[Fact] public void Tile_lookups_cross_chunks()          // tile (33, 0) reads from chunk (1, 0), byte offset (1 + 0 * 32) * 3
[Fact] public void A_missing_chunk_is_void()            // TileAt is null, Walkable is false
[Fact] public void Impassable_and_deep_tiles_cant_be_walked()
[Fact] public void A_chunk_with_the_wrong_length_is_a_content_error()  // ContentError at /chunks/0/tiles
[Fact] public void A_chunk_outside_the_region_is_a_content_error()
[Fact] public void A_height_above_9_is_a_content_error()
[Fact] public void An_unknown_key_in_a_region_is_a_content_error()
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project games/Tactics/Tactics.Logic.Tests`
Expected: FAIL to compile, `RegionDef` doesn't exist.

- [ ] **Step 3: Implement the records, `WorldRegion` and the loader's region kind**

`WorldRegion.From` decodes chunks once into a dictionary keyed by chunk coordinates. Use the same `TileInfo` as `BattleMap`, so the rules' terrain code can read either later. `Walkable` matches `Terrain.Walkable`: not `Impassable`, not `Deep`.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test --project games/Tactics/Tactics.Logic.Tests`
Expected: PASS. No base pack region exists yet, so `BasePackTests` is unchanged.

- [ ] **Step 5: Commit** `feat(tactics-content): the region format`

## Task 2: Region content checks

**Files:**
- Create: `games/Tactics/Tactics.Logic/Content/Validation/Regions.cs`
- Modify: `games/Tactics/Tactics.Logic/Content/PackLoader.cs` (call the checks after maps load)
- Test: `games/Tactics/Tactics.Logic.Tests/Content/RegionCheckTests.cs`

**Interfaces:**
- Consumes: `RegionDef`, `WorldRegion`, `BattleMap`.
- Produces: `Validation.CheckRegions(IEnumerable<Loaded<RegionDef>> regions, IReadOnlyDictionary<string, BattleMap> maps)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void A_stamp_that_differs_from_its_map_is_an_error()
// a tile's height, material NAME or flags differ. Compare materials by name, since the two lists differ.
[Fact] public void A_stamp_on_an_unknown_map_or_encounter_is_an_error()
[Fact] public void A_stamp_outside_the_region_is_an_error()
[Fact] public void A_spot_or_spawn_that_cant_be_reached_is_an_error()
// message names the marker and its tile: "The beat 'ch1_old_maud' at (4, 9) can't be reached from the start."
[Fact] public void Two_stamps_that_overlap_are_an_error()
[Fact] public void Edges_that_disagree_with_a_neighbour_are_an_error()
[Fact] public void A_region_with_no_neighbour_loaded_skips_its_edge_check()
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project games/Tactics/Tactics.Logic.Tests`
Expected: FAIL, the checks don't exist.

- [ ] **Step 3: Implement `CheckRegions`**

Reachability is a flood fill from `Spawns.Start` over `Walkable` tiles, stepping up or down at most 2 levels (the rules' jump). Report each unreachable marker once.

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test --project games/Tactics/Tactics.Logic.Tests`
Expected: PASS.

- [ ] **Step 5: Commit** `feat(tactics-content): region checks`

## Task 3: Building a region from a source grid

**Files:**
- Create: `games/Tactics/Tactics.WorldTools/Tactics.WorldTools.csproj`, `RegionSource.cs`, `RegionWriter.cs`, and the test project `games/Tactics/Tactics.WorldTools.Tests/`
- Modify: `ArcBox.slnx`
- Test: `games/Tactics/Tactics.WorldTools.Tests/RegionWriterTests.cs`

**Interfaces:**
- Consumes: `RegionDef`, `BattleMap`, `ArtManifest`.
- Produces:
  - `sealed class RegionSource(string id, string name, int chapter, int width, int height)`: `void Set(Tile t, int height, string material, TileFlags flags)`, `TileInfoNamed? Get(Tile t)`, `void Stamp(BattleMap map, string encounter, Tile at, int heightOffset)`, `List<BeatSpot> Beats`, `Tile Start`, `Tile Recovery`
  - `static class RegionWriter`: `WriteResult Write(RegionSource source, IReadOnlySet<string> knownMaterials)`, where `WriteResult(RegionDef? Region, ImmutableArray<string> Problems)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Tiles_are_split_into_32_by_32_chunks()
[Fact] public void A_chunk_with_no_tiles_is_left_out()
[Fact] public void Materials_are_listed_once_in_first_use_order()
[Fact] public void Stamp_copies_the_map_with_its_height_offset()
[Fact] public void A_stamp_pushing_a_tile_above_9_is_a_problem_not_a_clamp()
[Fact] public void An_unknown_material_is_a_problem_naming_the_tile()
[Fact] public void Writing_twice_gives_identical_output()
[Fact] public void The_written_region_loads_and_passes_the_checks()   // through RulesPack.Load on a temp pack
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project games/Tactics/Tactics.WorldTools.Tests`
Expected: FAIL to compile.

- [ ] **Step 3: Implement `RegionSource` and `RegionWriter`**

- [ ] **Step 4: Run them to see them pass**

Run: `dotnet test --project games/Tactics/Tactics.WorldTools.Tests`
Expected: PASS.

- [ ] **Step 5: Commit** `feat(tactics-world): build regions from a source grid`

## Task 4: The painting workspace

**Files:**
- Create: `tools/tactics-world/project.godot`, `tools/tactics-world/Tactics.World.Editor.csproj`, `tools/tactics-world/addons/region_tools/` (the editor plugin), `tools/tactics-world/palette/` (generated tile swatches), `tools/tactics-world/README.md`
- Test: `games/Tactics/Tactics.WorldTools.Tests/PaletteTests.cs`

**Interfaces:**
- Consumes: `RegionSource`, `RegionWriter`.
- Produces: the scene convention every later task and the user rely on. A region scene has a root `Region` node with `Id`, `Name` and `Chapter` properties, and children:
  - `Material`, `Height` and `Flags`, three `TileMapLayer`s on the same square grid;
  - `Beats`, `Stamps` and `Spawns`, holding `Marker2D`-based nodes with typed properties.

The grid is **flat, not isometric**, because the terrain art is sampled across several tiles rather than drawn per tile: a painter picks a material, a height or a flag and paints that layer. Each swatch shows the material's real texture, and heights show as a numbered shade.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void The_palette_lists_every_material_in_the_terrain_manifest()
// PaletteBuilder.Materials(manifestPath) contains each "<texture>/<variant>" of kind "top", such as "grass/lush".
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test --project games/Tactics/Tactics.WorldTools.Tests`
Expected: FAIL, `PaletteBuilder` doesn't exist.

- [ ] **Step 3: Implement `PaletteBuilder`, the project and the plugin**

The plugin adds an "Export region" button to the editor. It reads the three layers and the markers into a `RegionSource`, calls `RegionWriter.Write`, shows any problems in a list, and only on success writes `content/tactics/base/rules/world/<id>.json`. Add the file to `rules/manifest.json` in Task 6, once a region exists.

- [ ] **Step 4: Run the test, and open the project headless to check it imports**

Run: `dotnet test --project games/Tactics/Tactics.WorldTools.Tests`, then `"$GODOT" --headless --path tools/tactics-world --import`
Expected: PASS, and Godot exits with no errors.

- [ ] **Step 5: Commit** `feat(tactics-world): the painting workspace`

## Task 5: Claude's draft of Ashen Ford

**Files:**
- Create: `games/Tactics/Tactics.WorldTools/AshenFordDraft.cs`, `tools/tactics-world/addons/region_tools/DraftScene.cs`
- Test: `games/Tactics/Tactics.WorldTools.Tests/AshenFordDraftTests.cs`

**Interfaces:**
- Consumes: `RegionSource`, the scene convention, `lore/regions.json`, `lore/arcs.json`.
- Produces: `static RegionSource AshenFordDraft.Build(RulesPack rules)` at 160×120 tiles, and `tools/tactics-world/regions/ashen_ford.tscn`.

Lay it out to fit the lore: Emberwick at the west, the road east to the Ford Bridge, the river running north to south through it, the River Docks on the river, the Skarrow Hills to the north-east and Old Maud's Hut on the burned far bank. River Crossing is stamped at the ford.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void The_draft_is_160_by_120()
[Fact] public void River_crossing_is_stamped_exactly()
[Fact] public void Every_beat_in_chapter_1_of_the_lore_has_a_spot()   // reads content/tactics/base/lore/arcs.json
[Fact] public void The_draft_writes_with_no_problems()
[Fact] public void Every_marker_is_reachable_from_the_start()
[Fact] public void The_river_can_only_be_crossed_at_the_ford()
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project games/Tactics/Tactics.WorldTools.Tests`
Expected: FAIL, `AshenFordDraft` doesn't exist.

- [ ] **Step 3: Implement `AshenFordDraft.Build` and the `DraftScene` command**

`DraftScene` runs headless (`"$GODOT" --headless --path tools/tactics-world -- --draft=ashen_ford`), fills the layers and markers, and saves with `ResourceSaver`. **If the scene exists, it prints that and exits non-zero, changing nothing.**

- [ ] **Step 4: Run the tests, run the draft, then run it again**

Run the tests, then the command twice.
Expected: tests PASS, the first run writes the scene, and the second refuses and leaves its modified time unchanged.

- [ ] **Step 5: Commit** `feat(tactics-world): a draft of Ashen Ford`

## Task 6: Export the region and hand it over

**Files:**
- Create: `content/tactics/base/rules/world/ashen_ford.json`, `games/Tactics/Tactics.Logic.Tests/Content/RegionLoreTests.cs`
- Modify: `content/tactics/base/rules/manifest.json`, `games/Tactics/Tactics.Logic.Tests/Content/BasePackTests.cs`, `tools/tactics-world/README.md`, `docs/tv-host.md`

**Interfaces:**
- Consumes: everything above.
- Produces: `RulesPack.Regions["ashen_ford"]`, which milestone 5b walks on.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Base_pack_has_ashen_ford()                         // chapter 1, 160×120, one stamp: river_crossing
[Fact] public void Every_beat_of_a_regions_chapter_has_a_spot()       // arcs.json against each region; lore isn't in the rules pack
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project games/Tactics/Tactics.Logic.Tests`
Expected: FAIL, the pack has no region.

- [ ] **Step 3: Export from the editor and add the file to the manifest**

Use the plugin's own export, headless, so the path the user takes is the one tested.

- [ ] **Step 4: Write the painting guide in `tools/tactics-world/README.md`**

It covers opening the project, painting each layer, moving markers, exporting, and what each check's message means. Add a screenshot of the workspace with the draft open.

- [ ] **Step 5: Run everything**

Run: `dotnet build ArcBox.slnx`, then `dotnet test ArcBox.slnx`
Expected: 0 warnings, and all tests pass.

- [ ] **Step 6: Commit** `feat(tactics-content): Ashen Ford, ready to paint`

---

## Later milestones

Each gets its own plan once the work before it is merged.

- **5b, walking:** the Exploration phase, the direction input, the fixed-rate step on the game's timers, the phone's walk screen, saved position, chunk drawing on the TV, and the bots' walking mode. The drawing reuses milestone 3b's board.
- **5c, fights on the spot:** starting an encounter from a stamp, the party forming up, the battle board drawn in the world, return to walking, and recovery after a defeat.
- **5d, story:** the dialogue format, beats, votes, skill checks, their phone and TV screens, and Chapter 1's scripts.
- **5e, polish and playtest:** tuning, then a real session on phones.
