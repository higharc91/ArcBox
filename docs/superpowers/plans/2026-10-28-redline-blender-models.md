# REDLINE Blender Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Blender-built `.glb` models for the cars, drivers (with likenesses), helmets, podium and circuit landmarks, wired into the Godot TV app with the procedural meshes as fallback.

**Architecture:** Committed `bpy` scripts under `tools/blender/` generate every model from data and export `.glb` into `src/ArcBox.Host/games/redline/models/`. Godot loads them through a `ModelLibrary`, binds team colours to named materials and keeps placement code unchanged.

**Tech Stack:** Blender 4.0.2 (headless, Ubuntu package, `python3-numpy` for the glTF exporter), glTF 2.0 binary, Godot 4.7.2 .NET.

**Spec:** `docs/superpowers/specs/2026-10-28-redline-blender-models-design.md` (read it first).

## Global Constraints
- Run Blender as `blender -b --factory-startup -P tools/blender/build.py -- <args>`. Cycles has no OpenImageDenoise in this build: set `scene.cycles.use_denoising = False` for any preview render (or preview in Godot instead).
- Names, budgets, coordinate conventions and the no-logo rule exactly as the spec (§1 constraints, §2).
- Builds are deterministic (fixed seeds); re-running `build.py` produces byte-identical or at least geometry-identical exports.
- `.glb` total ≤ 40 MB; use `export_draco_mesh_compression_enable=False` (Godot doesn't import Draco) and keep triangle budgets instead.
- .NET tests: `dotnet test --solution ArcBox.slnx` (Microsoft.Testing.Platform: `--project`, `--filter-class`).

## Review Focus
1. **A `.glb` is missing or fails to import:** TV falls back to the procedural mesh and logs once. Test in Task 1 (`ModelLibrary_falls_back_when_missing`).
2. **Team colours wrong on Blender cars (sRGB vs linear):** papaya must stay papaya; compare against the procedural render. Task 1 screenshot check.
3. **Low preset performance:** LOD1 used beyond 60 m and in Low. Task 1.
4. **Likeness data errors:** every trait has a confidence; contact sheet of all 22 for the user. Task 2.
5. **Godot import of new assets in a fresh checkout:** `.import` files committed or a `--headless --import` step documented. Task 1.

---

### Task 1: Pipeline, car and Godot integration
**Files:** `tools/blender/{build.py, lib/*.py, car.py, check.py}`, `src/ArcBox.Host/games/redline/models/car_2026*.glb` (+ `.import`), `src/ArcBox.Host/games/redline/{Models/ModelLibrary.cs, Cars/CarFactory.cs}`, C# glb name-check test (new small test project or in `ArcBox.Host.Core.Tests`), `docs/blender-models.md`.
- [ ] Shared `lib`: deterministic helpers, material factory (paint gloss/matte with clearcoat, carbon, rubber, metal, emissive light, glass), bevel/subdiv helpers, glTF export with the naming contract, `check.py`.
- [ ] Car per spec §3 with named material slots and number empties; LOD1.
- [ ] Godot `ModelLibrary` + `CarFactory` binding team colours (from grid2026.json) to slots, matte/gloss per team, numbers on empties, LOD by `visibility_range`, fallback to procedural.
- [ ] Tests: glb names/budgets; fallback; LOD selection logic.
- [ ] Demo screenshots (grid, trackside close-up, question heli) vs the procedural ones. Commit `feat(redline): Blender model pipeline and 2026 car`.

### Task 2: Drivers (likenesses) and helmets
**Files:** `tools/blender/{driver.py, helmet.py, data/drivers.json}`, `src/ArcBox.Host/games/redline/models/{drivers,helmets}/*.glb`, Godot podium figures + in-car helmets.
- [ ] `drivers.json`: for each of the 22 drivers — height, build, skin tone, hair (colour, texture, style, length), facial hair, face-shape parameters, eye colour, helmet scheme (base + accents + motif), each with `confidence`.
- [ ] Parametric head + body + suit + cap + bottle per spec §3; helmets per scheme.
- [ ] Godot: podium uses driver glbs (spray pose via `arm_r`); cars carry the driver's helmet.
- [ ] Contact sheet: 22 driver close-ups (front and three-quarter) and 22 helmets, rendered in Godot. Commit `feat(redline): driver figures with likenesses and helmet designs`.

### Task 3: Podium set and circuit landmarks
**Files:** `tools/blender/{podium.py, landmarks.py}`, `src/ArcBox.Host/games/redline/models/{podium.glb, landmarks/*.glb}`, `World/Landmarks.cs`, `Podium/PodiumSet.cs` (swap to glbs, keep procedural fallback).
- [ ] Models per spec §3 with budgets; placement unchanged.
- [ ] Demo screenshots: grid (gantry, grandstands, pit building), Turn 1 grandstand, Observation Tower, podium. Commit `feat(redline): Blender podium set and circuit landmarks`.

### Task 4: Integration check
- [ ] `tools/tv-e2e.sh` full race passes; Low/High/Ultra demo shots; repo size of models ≤ 40 MB; docs updated. Commit `test: Blender models in the TV end-to-end run`.
