# REDLINE COTA Course Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the two driver/helmet model flaws, then make the COTA course broadcast-real: corner data, track surface and markings, per-corner kerbs and run-off, barriers and trackside furniture, and the surroundings.

**Architecture:** Corner data in `cota.json` drives pure placement logic in `Redline.Presentation` (tested); track-following geometry stays procedural C#; props are Blender models placed by that logic; Godot maps both to nodes and materials.

**Spec:** `docs/superpowers/specs/2026-10-28-redline-cota-upgrade-design.md` (read it first).

## Global Constraints
- Spec §3 budgets and rules; Blender pipeline rules from `docs/blender-models.md` (new modules and new lib files only).
- `.NET`: `dotnet test --solution ArcBox.slnx` (Microsoft.Testing.Platform: `--project`, `--filter-class`). Godot verified with `--redline-demo` screenshots and `tools/tv-e2e.sh`.
- Send screenshots to the coordinator ("main") as soon as each piece looks right; the user wants images as things finish.

## Review Focus
1. **Markings, kerbs, gravel or barriers float or sink on slopes** (Turn 1 climb, esses descent): Task 2/3 tests assert heights against the track surface.
2. **Barriers intruding into the run-off or track:** Task 3 test against track width + run-off width per corner.
3. **Frame cost on Low:** prop density halved, skid/marble detail off; Task 3/4 check draw calls with F3 numbers in the docs.
4. **Racing line nonsense (crossing the track edge):** Task 2 test keeps it inside the asphalt with a margin.
5. **Grid boxes not matching car grid slots:** Task 2 test.

---

### Task 1: Fix mouth corners and helmet lower-side line
**Files:** `tools/blender/{driver.py, helmet.py, lib/head.py, lib/hair.py …}` as needed, regenerated `models/drivers/*.glb`, `models/helmets/*.glb`, Godot import.
- [ ] Find the cause of each artefact (geometry, normals, UV seam, material slot or shading) before changing anything; fix at the source.
- [ ] Before/after close-ups of 4 faces and 4 helmets; contact sheets regenerated. Commit `fix(redline): clean mouth corners and helmet lower edge`.

### Task 2: Corner data, track surface, markings, kerbs and run-off
**Files:** `games/Redline/Redline.Presentation/{Data/cota.json, CircuitData.cs, Meshes/TrackMeshBuilder.cs or new Meshes/Track*.cs}`, new `Course/{Corners.cs, RacingLine.cs, Markings.cs}` with tests; Godot `World/TrackMaterials.cs` (new) and minimal hooks in `WorldBuilder.cs`.
- [ ] Corner data and straight-mode zones (spec §2.1) with tests.
- [ ] Racing line, skid marks, marbles, seams, asphalt shader; markings; per-corner kerbs; run-off types incl. gravel; mown grass bands (spec §2.2).
- [ ] Screenshots before/after: grid, Turn 1, esses, Turn 11, stadium section. Commit `feat(redline): COTA corner data, track surface, markings, kerbs and run-off`.

### Task 3: Barriers and trackside furniture
**Files:** `tools/blender/trackside.py` (new) + `lib/` additions, `models/trackside/*.glb`, `Redline.Presentation/Course/Trackside.cs` (placement, tests), Godot `World/Trackside.cs`.
- [ ] Props per spec §2.3 as instanced meshes; placement from the corner data; big screen with a viewport texture of the broadcast.
- [ ] Screenshots before/after. Commit `feat(redline): barriers, catch fence and trackside furniture`.

### Task 4: Surroundings
**Files:** `tools/blender/surroundings.py` (new), `models/surroundings/*.glb`, `Redline.Presentation/Course/Surroundings.cs` (placement, tests), Godot `World/Surroundings.cs`, terrain material in a new `World/TerrainMaterials.cs`.
- [ ] Spec §2.4 items; nothing overlapping the track, run-off, landmarks or tree clusters (test).
- [ ] Screenshots: wide heli views, tower/plaza, skyline. Commit `feat(redline): COTA surroundings — terrain, roads, plaza, tents, skyline`.

### Task 5: Integration
- [ ] Full e2e race passes; Ultra/High/Low shots; F3 numbers recorded in `docs/redline-tv.md`. Commit `test: COTA upgrade in the TV end-to-end run`.
