# Native bridge pixel-art review (approved checkpoint)

**Approved by the owner: “That looks much better”.** The bridge is now installed with per-tile sorting, real road ramps and character slope sampling; see [production integration](../integration/README.md). The isolated preview boundary below describes this earlier checkpoint.

The owner approved revision 2 concepts (“that looks much better”) and chose
“Use native art tools” on 2026-11-05. PixelLab is superseded for this bridge.
No additional generation credits were spent: Scenario remains **52 CU total**,
PixelLab **0** for this batch.

Review the [native state sheet](native-states.png) and
[TV comparison](tv-comparison.jpg). The TV comparison crops the same camera area
from the three full-resolution screenshots, which are also saved here.

The stone-and-timber bridge has three actual arch openings, a continuous deck,
stone parapets and a compact gatehouse. Following owner feedback about the
unsupported raised platform, both ends now have two-tile stone-supported ramps,
sloping curbs and road aprons. The west approach joins the existing town cobbles;
the east approach turns onto the existing east-bank exit road. The gatehouse sits
at bank level, and force wreckage stays beside the approach rather than on it. Fortified has high hide screens and a
closed spiked checkpoint; force has smashed defenses, collapsed awning and
localized soot; parley has open gates, rolled hides, a pale shelter and grain.
The geometry and shared frames remain consistent across the states.

The native authoring source is `tools/tactics-assets/parts/ford_bridge.py`.
It uses the existing town material renderer and geometry, with final RGB colours
restricted to `wardenflame-style.json`'s Ashen Ford palette, no dithering or
resampling, binary alpha and transparent margins. There are 26 separate module
and variant images, including deck tiles, arch support, parapets, screens,
barricade, open gates, wreckage, rolls, stakes, shelter and gatehouse states, plus both ramps and the road apron.
Original pixel images and hashes are in the concept batch's `native/` folder.

## Validation

- All 26 module/variant PNGs pass palette, binary alpha and margin checks.
- All three disposable packs load through the real rules/art loaders, build
  80 world blocks and produce nonempty battle plans.
- Each retains the ford as the only river crossing in both directions when its
  stamp is blocked; the northern ruined crossing remains deep and unwalkable.
- Six staged deck tiles are walkable planks with map/world height parity;
  the shallow-water row beside them remains water. Road material and reachable
  connections are verified from the town side through the approaches to the
  existing east exit road.
- All three native Godot captures complete with no errors, four actual party
  members and the current townsfolk. Software OpenGL timings are not a hardware
  performance acceptance result. Existing V-sync/AA renderer warnings remain.
- Hashes confirm all 3,253 live content PNG/JSON files were untouched.

See `validation.json`, `runtime-validation.json`, `assets.json` and the TV logs.
No runtime library code changed; full .NET/npm suites are due after installation.

## Preview boundary and work remaining

**This is a pixel-art/scale approval checkpoint, not an installed bridge.**
The isolated preview maps have proposed raised deck geometry, and the TV displays
the modular art assembled into one clearly marked review-only sprite. This avoids
pretending the current low-prop sorter can already handle a two-dimensional bridge
with tall parapets and a gatehouse. The review cameras keep the party clear of that
large sprite, which otherwise uses the normal automatic prop fading.
The assembly sprite must never be installed as the production crossing. The
preview removes the old crate from the new landing footprint; production cover
placements and their impact remain part of the encounter review.

Production requires per-tile component sorting, blocked parapets, story-gated
dressing, environmental fuel/conductivity checks, and regression pictures for units
on and behind the bridge in world and fight views. The town content format also
currently forbids a building inside a battle stamp; bridge/gatehouse production
placement must resolve that contract rather than bypass validation. Standalone
battle planning is checked here, but final in-place fight occlusion is not.

After owner pixel-art approval: integrate the approved modules in both encounters,
export the editable region twice identically, simulate both fights over the
requested seeds/levels/party sizes without changing balance numbers, and exercise
force/parley Chapter 1 completion. Part 2 ground repaint and its approval remain.

## Reproduce

From the repository root, with the existing .NET/Godot environment active:

```bash
python tools/tactics-assets/concepts/ford-rework/native/render-review.py --out /workspace/work/ford82-native-stage
dotnet run tools/tactics-assets/concepts/ford-rework/native/verify-review.cs -- /workspace/work/ford82-native-stage
python tools/tactics-assets/concepts/ford-rework/native/capture-review.py --pack /workspace/work/ford82-native-stage/fortified/content/tactics/base --name fortified-art
```

Repeat capture for `force` and `parley`. Review packs and raw captures stay outside
the repository; only selected evidence and source pixels are retained here.
