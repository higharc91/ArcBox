# Blender models (`tools/blender`)

REDLINE's 3D models are built by committed Python scripts running in **headless Blender** and exported as glTF
binaries into `src/ArcBox.Host/games/redline/models/`. The `.glb` files are committed, so the TV app never needs
Blender. Godot binds team colours and materials at runtime, and every model has a procedural C# fallback.
(Spec: [`superpowers/specs/2026-10-28-redline-blender-models-design.md`](superpowers/specs/2026-10-28-redline-blender-models-design.md).)

## Build and check

```sh
blender -b --factory-startup -P tools/blender/build.py -- [--only cars,drivers,podium,landmarks] [--list]
blender -b --factory-startup -P tools/blender/build.py -- --only cars --preview --preview-dir /tmp/pv \
    team=red-bull views=front34,side,rear34,top res=1280x720 samples=64      # Cycles previews
blender -b --factory-startup -P tools/blender/check.py -- [--only cars]       # validate the committed .glb files
GODOT=/path/to/godot tools/godot-import.sh                                   # re-import into Godot (see below)
```

- Blender 4.0.2 (Ubuntu package) with `python3-numpy` (the glTF exporter needs it). There is no OpenImageDenoise in
  this build: previews render with denoising off, so keep the sample count reasonable or judge the look in Godot.
- Builds are deterministic: the same scripts give byte-identical `.glb` files (no randomness without a fixed seed, no
  timestamps).
- `build.py` checks every export against its contracts right after writing it and exits with code 1 on any failure.
  `check.py` runs the same checks on the committed files and prints triangle counts, sizes and the 40 MiB total.
- The `.NET` tests (`Redline.Presentation.Tests/Models`) parse the committed `.glb` files too (no Blender, no Godot),
  so a broken or renamed export fails `dotnet test`.

## Conventions

- **Frame.** Geometry is authored in the **Godot frame**: metres, +Y up, +Z forward, +X to the model's left. `lib`
  converts to Blender's Z-up when it creates objects and the glTF exporter converts back, so a point authored at
  `(x, y, z)` is at `(x, y, z)` in Godot. The car's origin is on the ground midway between the axles, exactly as
  `CarMeshBuilder`, so all placement code is unchanged.
- **Named material slots.** A slot name is a contract with Godot, which rebinds every slot to its own material.
  Blender's materials only provide sensible glTF defaults (and the previews).
- **Colours.** Hex colours from the data (`grid2026.json`) are sRGB; glTF base colours are linear. `lib.materials`
  converts with `lib.colour.srgb_to_linear`, so `#ff8000` is exported as `(1.0, 0.216, 0.0)` and papaya stays papaya.
  In Godot, livery slots get `new Color("#rrggbb")` (sRGB) directly.
- **Empties** mark where Godot attaches things. An empty's local +Z is its outward normal and local +Y its "up"
  (`lib.blend.empty(name, pos, right, up, normal)`; `lib.frame.facing(normal, up_hint)` builds the triple).
- **No Draco** (Godot can't import it), no vertex colours, animations, cameras, lights or extras.
- **No logos or trademarks** anywhere (spec §1).

## The car (`tools/blender/car.py`, group `cars`)

| File | Budget | Now |
|---|---|---|
| `car_2026.glb` (LOD0) | ≤ 120k triangles | ~68.5k triangles, ~2.0 MB |
| `car_2026_lod1.glb` (LOD1) | ≤ 30k triangles | ~16.3k triangles, ~0.55 MB |

Nodes: `body` (every static part: monocoque, nose, sidepods, floor, wings, halo, mirrors, suspension, brake drums),
`wheel_fl`/`wheel_fr`/`wheel_rl`/`wheel_rr` (tyre + rim, origin at the wheel centre; Godot spins them about X;
`l` = +X), the empties `number_nose`, `number_endplate_l`, `number_endplate_r` (Label3D numbers) and `helmet`.
Material slots: `paint_top paint_side stripe nose wing endplate halo` (livery, matte or gloss per team) and
`carbon tyre tyre_stripe rim light glass tcam`. `tcam` is an addition to the spec's list: the T-camera is black on a
team's first car and yellow on its second. The C# side of the contract is `Redline.Presentation.Models.CarModel`.

What it models: a smooth subdivided monocoque with a short blunt 2026 nose, panel-line grooves and a boolean-cut
cockpit with a padded rim; roll hoop with its inlet and T-cam; downwash sidepods with deep undercuts and letterbox
inlets; a narrow coke-bottle engine cover and shark fin; floor with up-turned edges, a curled floor-edge wing,
in-wash boards, leading-edge fences, a diffuser with strakes and a gurney; a low front wing (main plane + two flaps,
the rear one the active-aero flap) with small endplates; a rear wing with main plane and active flap, slim leaning
endplates with a cut-away top and LED rain lights, swan-neck pylons, no beam wing; halo, wide flat mirrors on aero
stalks; wishbones, push/pull rods, track and toe links, uprights and brake drums; wheels with a brushed rim, ten
spokes over a dark cover, hub and hexagonal centre-lock nut; tyres with sidewall bulge, rounded shoulders, a crowned
tread and the compound stripe. LOD1 is the same build at lower resolution (no subdivision, fewer segments, no
small details).

### In Godot

- `World/ModelLibrary.cs` loads each model once, in this order (`ModelChoice`): the imported scene (started on a
  background thread by `RedlineAssets.Prewarm`), else the raw `.glb` through `GLTFDocument`, else nothing. A model
  that loads but misses part of its naming contract is refused too. Each outcome is logged once
  (`REDLINE model car_2026.glb: imported scene`). `REDLINE_NO_MODELS=1` refuses every model (comparison shots).
- `Cars/CarFactory.cs` instances LOD0 and LOD1 per car, binds every surface by slot name
  (`Cars/CarMaterials.cs`: flake gloss paint with clearcoat or matte paint, twill carbon, rubber, brushed rims,
  emissive lights, mirror glass, T-cam), registers the wheel nodes for rolling, draws the numbers at the empties
  (smaller on the endplates: `CarModel.NumberSizes`) and the helmet at the helmet empty. When the model is unusable
  it builds the procedural car exactly as before.
- LOD: `CarLod` — LOD0 up to 60 m, LOD1 beyond (each mesh's `visibility_range_*`, no hysteresis so no part is ever
  in neither LOD); the Low preset draws LOD1 at every distance (`CarNode.SetLowDetail`, re-applied on F2).

### Import into Godot

The `.glb` and its `.glb.import` are committed (like the fonts' `.ttf.import`); the imported artefacts in
`src/ArcBox.Host/.godot/imported/` are not. Godot only imports in the editor, so:

- **Fresh clone:** open the project in the editor once, or run `GODOT=… tools/godot-import.sh`.
- **After re-exporting a model:** run `tools/godot-import.sh` again (it imports only when an artefact is missing or
  its source's md5 changed; `--check` just reports). Commit the `.glb` (and the `.import` when it is new).
- `tools/tv-e2e.sh` runs it automatically before starting the TV.
- If nobody imported, the TV still shows the Blender car (parsed at runtime, logged), and if that fails, the
  procedural one.

## The `lib` API

All geometry helpers work in the Godot frame. Shared modules belong to Task 1; later work adds **new** modules (e.g.
`lib/figure.py` for drivers, `lib/arch.py` for buildings) instead of editing these, so parallel tasks never touch
the same file.

| Module | API |
|---|---|
| `lib.frame` | `to_blender(p)`, `from_blender(p)`, `node_matrix(pos, right, up, normal)`, `facing(normal, up_hint) -> (right, up, normal)` |
| `lib.colour` | `srgb_to_linear("#rrggbb") -> (r, g, b, a)`, `linear_to_srgb_hex(rgb)` |
| `lib.mesh` | `Mesh` (`vert`, `face(idx, slot, uv=None, smooth=True)`, `extend(other, matrix)`, `transformed(m)`, `mirrored_x()`, `paint(slot)`, `orient_closed()`, `orient_outward(fn)`, `bounds()`), `merge(*meshes)`; transforms `T`, `Rx`, `Ry`, `Rz`, `S`; `spline(points, per)`; `Section` + `densify`, `section_at`, `ring_rels`, `loft(stations, per, rels, mat_fn, z_breaks, z_grooves, rel_grooves, ...)`; `airfoil`, `Span` + `wing(spans, slot, n, per)`; `plate(outline, thickness, origin, u, v, slot)`, `rounded_outline(points, radius)`; `tube(points, ra, rb, sides, major, slot, caps, per, taper)`; `lathe(profile, segs, slot_or_fn, outward)`; `box`, `ellipsoid`, `cylinder` |
| `lib.blend` | `reset()`, `make(mesh, name) -> obj` (slots, smooth flags, UVs; faces without UVs get a planar projection), `subsurf(obj, levels)`, `bevel(obj, width, segments, angle)`, `boolean(obj, cutter)` (exact solver, cut faces take the cutter's slot), `weld`, `apply(obj)`, `mark_sharp(obj, angle)`, `finish(obj, angle)` (apply + sharp edges: the last step for every part), `join(objs, name)` (merges slots by name), `set_origin(obj, pos)`, `empty(name, pos, right, up, normal)`, `parent(child, parent)`, `delete(obj)`, `triangles(obj)`, `slot_names(obj)` |
| `lib.materials` | `define(name, colour, metallic, roughness, coat, coat_roughness, emission, emission_strength, alpha, anisotropic)`, presets `paint(name, hex, matte)`, `carbon`, `rubber`, `metal`, `emissive`, `glass`; `get(name)` |
| `lib.export` | `glb(path, objects)` (the objects and their children only, so one scene can hold several exports) |
| `lib.glb` | `read(path)`, `summary(path)` → nodes (world transforms), materials, used materials, base colours, triangles, bounds, bytes. Pure Python. |
| `lib.validate` | `Contract(path, max_triangles, nodes, materials, size, min_y, max_bytes, extra)`, `check_all(contracts, models_dir)`. Pure Python. |
| `lib.asset` | `Context` (`models_dir`, `model(rel)`, `preview`, `preview_dir`, `args`), `grid()`, `team(id)`, paths `REPO_ROOT`, `MODELS_DIR`, `GRID_JSON` |
| `lib.preview` | `stage()` (sky, sun, ground), `render(path, eye, target, lens, res, samples)` |

Typical part: build a `Mesh`, `blend.make` it, add `subsurf`/`bevel`/`boolean`, `blend.finish`, then `blend.join` the
parts and `export.glb` the result with its empties.

## Adding a model (how `build.py` dispatches)

`build.py` imports every `tools/blender/<name>.py` other than `build.py`, `check.py` and files starting with `_`, in
name order. A module takes part when it defines:

```python
GROUP = "drivers"                 # its --only group: cars, drivers, podium, landmarks
def build(ctx): ...               # blend.reset(), build, export.glb(ctx.model("drivers/NOR.glb"), [...])
def contracts(): return [...]     # lib.validate.Contract per export (budget, names, dimensions)
def preview(ctx): ...             # optional, for --preview
```

Nothing has to be registered, so new modules never edit `build.py`. Planned owners (each writes only its own files):

| Task | Modules | Exports | Godot side |
|---|---|---|---|
| 1 (done) | `car.py` | `car_2026.glb`, `car_2026_lod1.glb` | `ModelLibrary`, `CarFactory`, `CarMaterials`, `CarModel` |
| 2 | `driver.py`, `helmet.py`, `data/drivers.json` (group `drivers`) | `drivers/<CODE>.glb`, `helmets/<CODE>.glb` | podium figures; `CarFactory.Helmet` swaps to `helmets/<CODE>.glb` at the car's `helmet` empty |
| 3 | `podium.py`, `landmarks.py` (groups `podium`, `landmarks`) | `podium.glb`, `landmarks/*.glb` | `PodiumSet`, `Landmarks` |

For the Godot side, load through `ModelLibrary.Prewarm("drivers/NOR.glb")` and
`ModelLibrary.Instantiate("drivers/NOR.glb", node => missingNames)`; put the naming contract in
`Redline.Presentation/Models/<Thing>Model.cs` and its glb checks in `Redline.Presentation.Tests/Models/<Thing>ModelTests.cs`
(see `CarModel` and `BlenderModelTests`). After exporting, run `tools/godot-import.sh` and commit the new
`.glb.import` files with the models.

Budgets (spec §1): car ≤ 120k triangles (LOD1 ≤ 30k), driver ≤ 40k, helmet ≤ 12k, podium set ≤ 150k, landmarks
≤ 600k in total; all `.glb` ≤ 40 MiB.
