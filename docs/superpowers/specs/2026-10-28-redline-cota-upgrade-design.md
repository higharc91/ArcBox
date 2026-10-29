# REDLINE COTA Course Upgrade — Design Spec

- **Date:** 2026-10-28
- **Status:** Requested by the user ("upgrade the COTA course"); scope chosen by Claude, open to redirection
- **Builds on:** [REDLINE design](2026-10-25-redline-design.md) §4.8, [Blender models](2026-10-28-redline-blender-models-design.md)

## 1. Purpose
The cars, drivers, podium and landmarks are now detailed; the circuit itself (flat asphalt ribbon, simple curbs, plain walls and fence, uniform grass) is the weakest part of the picture. Make the course read like a real Grade 1 circuit on a TV broadcast, while keeping the layout data, placement, cameras and performance budgets.

Also fixes two model flaws from the driver pass: faint dark marks at the mouth corners and a thin dark line on the lower sides of the helmets.

## 2. Scope
### 2.1 Corner data
`Data/cota.json` gains a `corners` list: the 20 official turns with `turn`, `s` (apex, metres along the lap), `direction` (left/right), `apexSide`, `kerb` (`flat`, `sawtooth`, `sausage`), `runoff` (`tarmac-striped`, `tarmac`, `gravel`, `grass`), `barrier` (`tecpro`, `tyres`, `armco`, `wall`) and `confidence`. Derived from curvature peaks matched to COTA's published turn order, with COTA's real character where widely known: the Turn 1 hairpin at the top of the hill, the Maggotts–Becketts-style esses (T3–T6), the Turn 11 hairpin onto the back straight, the stadium section (T12–T16), the T16–T18 multi-apex right, and T19/T20. Pure data; `CircuitData` exposes it. 2026 straight-mode (active aero) zones on the main and back straights are also recorded.

### 2.2 Track surface and markings
- **Asphalt:** aggregate detail (procedural normal and roughness), a rubbered racing line computed from the geometry (apex-to-apex smoothed line, darker and smoother), braking-zone skid marks before the heavy-braking corners, marbles just off line at corner exits, and a few repair seams.
- **Markings:**
  - start/finish line (existing chequer kept);
  - 22 staggered grid boxes with position numbers, matching `LapMath.Grid`;
  - pit entry and exit lines;
  - straight-mode activation lines;
  - white track-limit edges (existing, refined).
- **Kerbs:** per-corner profiles from `kerb`: flat painted, raised sawtooth with bevels, and yellow sausage kerbs behind apexes where `sausage`. Red/white alternate at the real stripe length.
- **Run-off:**
  - COTA's painted red/white/blue stripes stay, on corners marked `tarmac-striped`;
  - plain tarmac;
  - gravel traps with a pebble material on corners marked `gravel`;
  - mown grass stripes near the track.

### 2.3 Barriers and trackside furniture (Blender props placed along the track)
- **Barriers per corner type:**
  - TecPro block barriers (red/white/blue);
  - tyre walls with a conveyor-belt face;
  - armco double rails on posts along the straights;
  - concrete walls with REDLINE/ARCBOX ad boards.
- **Catch fence:** real posts, cables and a mesh look, with overhang at corner outsides.
- **Furniture:**
  - marshal posts every ~300 m with numbered huts and light panels;
  - corner number boards (T1–T20);
  - brake marker boards (300/200/100) before the heavy-braking corners;
  - trackside TV camera platforms;
  - one big screen at the main grandstand showing the broadcast picture (viewport texture).

### 2.4 Surroundings
- **Terrain:** a dry Texas grass material with colour variation, dirt patches and mown bands near the track.
- **Ground features:** service roads in the infield, car parks with low-poly parked-car instances, and the Grand Plaza and amphitheatre beside the Observation Tower.
- **Additions:** hospitality tents, scattered shrubs, and a hazy distant Austin skyline silhouette on the horizon.
- **Branding:** REDLINE/ARCBOX only; no real-world logos.

## 3. Constraints
- Layout, `LapMath`, camera framing and placement stay unchanged; everything new follows the track via `TrackGeometry` samples (the straight rises ~1.4 m along the pit building, so nothing track-following is baked flat).
- Budgets (RTX 5080, 1440p @ 144 fps): course additions ≤ 1.5M triangles total; trackside props instanced (MultiMesh or shared meshes). The Low preset drops skid marks/marbles detail, halves props density and disables the big screen's live feed.
- Blender props go through the existing pipeline (`tools/blender`, `ModelLibrary`, named contracts, procedural fallback where a prop is missing). Models total stays ≤ 40 MB (currently 28 MB).
- Pure placement and data logic lives in `Redline.Presentation` with tests; Godot only maps it to nodes and materials.

## 4. Testing
- Corner data: 20 turns in order, apexes within the curvature peaks, every field present with a confidence.
- Track features: grid boxes equal `LapMath.Grid` slots; markings and kerbs never float or sink (height = track height ± a few cm); barriers never intrude into the track width or run-off; props respect budgets.
- Screenshots (`--redline-demo`, Ultra 1080p) before/after at the grid, Turn 1, the esses, Turn 11, the stadium section and the podium; e2e full race still passes.
