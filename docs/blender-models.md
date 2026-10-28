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

## The podium set and landmarks (`podium.py`, `landmarks.py`)

Each model is authored in the local frame of the procedural set piece it replaces, so placement, footprints, cameras
and the crowd are unchanged; `lib/arch.py` holds their helpers (prisms, stair flights, beams, box trusses, lumpy
blobs, and extruded lettering in the brand face, Titillium Web Black slanted).

| File | Budget | Now | Nodes |
|---|---|---|---|
| `podium.glb` | ≤ 150k | ~26k triangles, ~1.6 MB | `podium`, `confetti_l`/`confetti_r` (cannon muzzles, +Z = aim), `step_1..3` (= `PodiumLayout.StepTopCentre`) |
| `landmarks/grandstand_main.glb` (300 m × 22 rows) | ≤ 90k | ~48k, ~2.8 MB | `grandstand`, `tier_first`, `tier_last` |
| `landmarks/grandstand_t1.glb` (130 m × 16) | ≤ 40k | ~16k, ~1.0 MB | same |
| `landmarks/grandstand_corner.glb` (90 m × 10, used 3×) | ≤ 25k | ~8k, ~0.5 MB | same |
| `landmarks/pit_building.glb` | ≤ 80k | ~12k, ~0.8 MB | `pit_building` |
| `landmarks/pit_station.glb` (used 11×) | ≤ 8k | ~1.1k, ~0.1 MB | `pit_station` |
| `landmarks/start_gantry.glb` | ≤ 30k | ~4k, ~0.3 MB | `gantry`, `light_1..5` |
| `landmarks/tower.glb` | ≤ 40k | ~18k, ~0.5 MB | `tower` |
| `landmarks/trees.glb` | ≤ 6k | 3 × ~1.5k, ~0.1 MB | `oak_a`, `oak_b`, `oak_c` |

Landmarks total ~113k triangles (budget 600k, counting each file once; ~140k drawn with the corner stand three
times and the pit station eleven times).

- **Podium set:** graphite stage deck skirted into the verge, carpet top, aluminium nosing, red and white LED edge
  strips, gloss fascia panels with REDLINE/ARCBOX lettering, diamond-plate stairs with rails at both ends; bevelled
  white blocks (2 left, 1 centre and highest, 3 right) with inset red numerals (boolean-cut), gold/silver/bronze
  collars and glowing base lines; the LED media wall (slot `screen`, UVs 0..1: Godot puts
  `PodiumBackdrop`'s artwork on it) in a black frame with an LED surround; aluminium box-truss towers with light bars,
  par lamps and vertical ARCBOX panels, a header truss with a light bar; the front lighting truss on posts with five
  hanging lamps (where `PodiumSet`'s spotlights are) and a REDLINE banner; confetti cannons on the header truss
  (Godot's confetti fires from their muzzles) and on the deck. The driver figures and bottles are not part of it.
- **Grandstands** keep the tier contract the instanced crowd sits on (`GrandstandModel`): row r's tread at
  y = 1 + 0.9 r over 1.2 r ≤ z ≤ 1.2 (r + 1), a seat per crowd place every 0.85 m (bucket seats in a dark slate
  plastic, so empty seats read and every crowd colour stands out), aisles with half steps every 25 m, yellow tread
  nosings, a glass balustrade, a back wall with fins, end stair flights, and a
  cantilevered roof on trusses, a back girder and columns. `roof`, `trim` and `brand` (the fascia lettering) take the
  stand's colour: white main stand (red lettering), red Turn 1 stand, blue/white corner stands. On a Blender stand
  the crowd skips the aisles and its figures are slimmer and seated on the seat pans, about a seat-back high (the
  procedural stand keeps the old standing capsules).
- **Pit building:** 27 garages behind corrugated roller shutters (every fourth half open, lit, with epoxy floors and
  white walls), team boards with an LED line, a leaning glass hospitality floor with mullions, a roof with a red edge,
  lettering, a glass terrace rail and rooftop plant.
- **Pit wall and stations** (`PitLaneModel`): the TV draws the painted concrete pit wall under the catch fence
  (19.0–19.9 m from the centre line, 1.1 m high) and the pit lane's white lines (fast-lane line, fast/working lane
  divider, a box line at every garage divider) as ribbons that follow the track, like the pit lane surface; the
  straight rises ~1.4 m along the building, so they are not baked into the flat building. `pit_station.glb` is one
  low, long (16 m) covered station on the wall's pit-lane side (grated deck, console, ten screens, stools, a canopy
  reaching over the wall with a red edge and lights, posts, glass ends), framed eleven times at its own points along
  the wall.
- **Start gantry:** concrete plinths and dark steel columns, a clad girder with a red cap, REDLINE/ARCBOX lettering
  and LED bars, a dot-matrix board on top (slot `board`, UVs in decimetres: Godot's dot texture) showing
  ARCBOX/REDLINE in lit LEDs, and the light board with five pods of two lenses under visors. Each pod's lenses are
  the node `light_k` (origin at the pod, where the procedural lamp was); `StartGantry.SetLit` swaps their material.
- **Observation Tower:** a tapering white shaft with glass lift strips, soffit cone, the flared glass deck with
  mullions, roof, crown and mast, and the red veil: two counter-twisted families of 18 curved tubes from the crown to
  a 26 m ring, the hyperboloid lattice.
- **Trees:** three live oaks at unit scale (crown radius ~1): a short thick trunk forking into low, nearly level
  limbs under a broad crown of lumpy, flattened clusters. `Trees` draws one MultiMesh per variant (tree i is variant
  i mod 3, so a preset's prefix still mixes all three) with the leaves tinted per instance.

**In Godot:** `World/SetPieces` instances each model through `ModelLibrary` against its contract
(`Redline.Presentation.Models.LandmarkModels`, `PodiumModel`, both built on `ModelContract`) and binds every slot to a
Godot material (`World/SetMaterials`: triplanar concrete, painted and dark steel, brushed metal, tinted and clear
glass, seat plastic, shutter aluminium, LEDs, the dot-matrix board, bark and leaves). `Landmarks`, `StartGantry`,
`Trees` and `Podium/PodiumStage` (called from `PodiumSet._Ready`) fall back to the procedural pieces when a model is
missing or incomplete; `RedlineAssets.Prewarm` starts loading them all in the background. Tests:
`Redline.Presentation.Tests/Models/LandmarkModelTests.cs`.

## Drivers and helmets (`driver.py`, `helmet.py`, `data/drivers.json`, group `drivers`)

| File | Budget | Now | Nodes |
|---|---|---|---|
| `drivers/<CODE>.glb` (22) | ≤ 40k | 26.5k–33k triangles (Hamilton 39.1k: locs), 0.53–0.76 MB (~13.6 MB for all 22) | `legs`, `spine` › `torso`, `number_suit`, `name_suit`, `head` › (`face`, `hair`, `hair_top`, `locs`, `cap` › `number_cap`), `arm_r` › `forearm_r` › `hand_r` › `bottle`, `arm_l` › `forearm_l` › `hand_l` |
| `helmets/<CODE>.glb` (22) | ≤ 12k | 8.6k–9.4k triangles, ~0.17 MB (~3.7 MB for all 22) | `helmet` |

**Likeness data.** The user chose face likenesses for this private build. `data/drivers.json` holds, per driver code,
height, build, skin tone, eye colour, hair (colour, texture, style, length, shape: fringe, sweep, part, volume,
recession, sides, tied), facial hair, face shape (width, length, jaw width/squareness/definition, chin projection/
width/height/cleft, cheekbones, cheek fullness, brow ridge, forehead slope, age lines), nose (length, width,
projection, bridge, bridge width, tip, nostril flare), eyes (spacing, size, openness, tilt, depth, hooding), lips
(fullness, width, smile), brows, ears, optional jewellery and the helmet scheme. **Every trait carries a
`confidence` (high, medium, low)**: values are approximations of widely known public appearance, and low means a best
guess for the user to correct. Shape numbers are small values round 0 (an average adult man), usually -1..1; the
head applies them with a fixed gain (`lib.head.GAIN`). Edit the file and rebuild with `--only drivers`. Represent
everyone respectfully: no exaggeration for comedy, no stereotyping; the numbers stay moderate.

**The figure** (`lib.body`, `lib.head`, `lib.hair`, `lib.cap`, `lib.flags`): a stylised-realistic driver at their
height (head about 1/7.2 of it) in a fitted two-tone race suit (main colour, side panels from hip to armpit, a chest
band, epaulettes, arm and leg stripes, cuffs and the collar in the second colour, a raised belt), gloves, race boots,
the team cap, and the nationality flag patch on the left chest by the shoulder (simplified flags, no emblems). The
head is a parametric surface (`lib.head.Head`): two superellipse halves per height (front and back) interpolated over
the height, with smooth displacements for the brow, nose, lips, chin, cheekbones, jaw and folds, and the orbit set
outright: a bowl pressed into the face meeting lids that wrap a sphere round the eyeball (a smooth max), with an
almond aperture whose margin is an exact edge loop and rolls in behind the eyeball; the upper lid covers the top of
the iris and a dark lash line runs along it. Eyeballs carry sclera, iris (the driver's colour) and pupil; brows are
tapered shells; ears have a helix, antihelix, concha and lobe. Hair is a shell over the scalp cut by a hairline
polygon, thick by style and textured (combed clumps, curls, coils); locs are tubes over the scalp, tied at the back
when `tied`. Facial hair is painted (light stubble and stubble with a soft two-step edge) or a shell (beard,
moustache, goatee). Under the cap only `hair` shows; `hair_top` is the part the cap covers.

Surfaces are triangulated adaptively (`lib.figure`): a quadtree sample of the (u, v) domain, fine round the eyelids
(~1.5 mm), nose and lips and coarse on the back of the head, then Blender's constrained Delaunay triangulation with
the paint regions (lips, lash lines, stubble, helmet motifs) as exact polygons and creases (lid margins) as edges,
mapped onto the surface with analytic normals written as custom split normals. Parts with no textures drop their UV
layer, which keeps the files small.

**The rig.** Rest rotations are all identity: Godot poses the arms by rotating `arm_r`/`forearm_r`/`hand_r` (and the
left chain) about the shoulder, elbow and wrist. The rest pose holds the bottle (a podium magnum, 0.48 m, the
`BottleMesh` profile) upright in front of the stomach in both hands: the right round its body, the left under its
base. `bottle` hangs off `hand_r` with its origin at its base; its mouth is at (0, 0.48, 0).

**Helmets** (`helmet.py`): a 2026-style closed-face shell (chin bar, a proud dark visor with a rubber gasket and side
pivots, a visor strip, a rear spoiler, a neck seal) centred on the head and facing +Z. The design is the driver's
scheme from `drivers.json` (`base` + accents `a` and `b` in one motif: stripes, chevrons, halves, flag_bands, swirls)
plus a small nationality flag on the back: colour schemes only, no copied artwork and no sponsor marks. The shell is
parametrised round the lateral axis, so stripes over the crown are regular. Slots: `helmet_base`, `helmet_a`,
`helmet_b`, `visor`, `helmet_trim`, `flag_1..4`.

**Previews.** `--preview` renders every driver (cap off, front and three-quarter: `drivers_sheet.png`; cap on, full
figure: `drivers_figures.png`, skipped with `figure=0`) and every helmet (`helmets_sheet.png`) with Cycles and studio
lights (`lib.studio`), subset with `drivers=NOR,HAM`.

**In Godot:** `Podium/PodiumDriver` puts each podium figure on its step: the driver's glb checked against
`DriverModel` (`Redline.Presentation.Models`), team slots bound to the team's colours and the likeness slots to
proper surfaces (`World/FigureMaterials`: skin with subsurface scattering and a faint triplanar roughness variation,
wet eyes, fabric suits), `hair_top` hidden under the cap, the number and surname as `Label3D`s on `number_suit`,
`name_suit` and `number_cap`. `PodiumSet` decides where the bottle goes (shaken at rest, raised and aimed with
`SprayLook.Aim` while spraying); `PodiumDriver.Pose` reaches it with a two-bone IK through `arm_r` (the left hand
stays under the base until the spray lifts it away), and the spray frame follows the figure's `bottle` node. Without
a usable glb it draws the procedural figure as before. `CarFactory.Helmet` puts `helmets/<CODE>.glb` at the car's
`helmet` empty (gloss clearcoat paint, iridium visor), else the procedural helmet. `CarFactory.Prewarm` starts
loading the helmets and figures in the background. Tests: `Redline.Presentation.Tests/Models/DriverModelTests.cs`.

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
| `lib.arch` | `frame(origin, right, up, normal)`, `prism(profile_zy, x0, x1, slot)`, `stair_profile(n, run, rise, base)`, `beam(a, b, w, h, slot, up)`, `truss(a, b, w, h, bay, chord, brace, slot, up)`, `ring(points, r, sides, slot)`, `blob(centre, radii, seed, slot, ...)`, `text(body, height, depth, slot, align, shear)` (brand-face lettering, reads from +Z), `repaint(mesh, slot, where)`, `quad(a, b, c, d, slot, uv)` (podium and landmarks) |
| `lib.figure` | `adaptive_points(u0, u1, v0, v1, spacing, base)` (quadtree samples), `surface(pos, domain, points, base_slot, regions, creases, normal, keep)` (constrained Delaunay with exact paint regions, analytic normals), `grid_surface(pos, us, vs, slot_fn, normal, wrap_u)`, `make_smooth(part, name)` (custom split normals, no UVs), `concat`, `transform`, `weld_mesh`, `flat_normals`; `Curve` (monotone cubic), `smoothstep`, `bump`, `clamp`, `lerp`, `seg_dist`; deterministic `fbm`, `cells`, `hash01` (drivers and helmets) |
| `lib.head` | `Head(traits)` (`pos(u, y)`, `normal(u, y)`, `point(x, y)`, landmarks, `aperture`, `lip_regions`, `facial_hair_regions`), `skin`, `eyes`, `brows`, `ears`, `studs`; head-local frame: eye line y = 0, ear canal z = 0 |
| `lib.hair` | `Style(traits)`, `shell(head, style, split)` (`hair` / `hair_top` at `cap_band`), `locs`, `beard` |
| `lib.body` | `Rig(height, build)` (joints, `ik_elbow`), `torso`, `legs`, `boot`, `arm`, `right_hand`, `left_hand`, `bottle`, `flag_patch`, `surface_point`, `limb` |
| `lib.cap` | `cap(head, style)` (crown over the hair, brim, button, number frame) |
| `lib.flags` | `FLAGS[iso]`: simplified national flags as coloured polygons (no emblems or text) |
| `lib.studio` | `stage(target, scale)` (key, fill, rim, grey world), `render(path, eye, target, lens, res, samples)` (portrait previews) |
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
| 2 (done) | `driver.py`, `helmet.py`, `data/drivers.json`, `lib/{figure,head,hair,body,cap,flags,studio}.py` (group `drivers`) | `drivers/<CODE>.glb`, `helmets/<CODE>.glb` | `PodiumDriver` (from `PodiumSet`), `FigureMaterials`, `DriverModel`, `HelmetModel`; `CarFactory.Helmet` puts `helmets/<CODE>.glb` at the car's `helmet` empty |
| 3 (done) | `podium.py`, `landmarks.py`, `lib/arch.py` (groups `podium`, `landmarks`) | `podium.glb`, `landmarks/*.glb` | `PodiumStage` (from `PodiumSet`), `Landmarks`, `StartGantry`, `Trees`, `SetPieces`, `SetMaterials` |

For the Godot side, load through `ModelLibrary.Prewarm("drivers/NOR.glb")` and
`ModelLibrary.Instantiate("drivers/NOR.glb", node => missingNames)`; put the naming contract in
`Redline.Presentation/Models/<Thing>Model.cs` and its glb checks in `Redline.Presentation.Tests/Models/<Thing>ModelTests.cs`
(see `CarModel` and `BlenderModelTests`). After exporting, run `tools/godot-import.sh` and commit the new
`.glb.import` files with the models.

Budgets (spec §1): car ≤ 120k triangles (LOD1 ≤ 30k), driver ≤ 40k, helmet ≤ 12k, podium set ≤ 150k, landmarks
≤ 600k in total; all `.glb` ≤ 40 MiB.
