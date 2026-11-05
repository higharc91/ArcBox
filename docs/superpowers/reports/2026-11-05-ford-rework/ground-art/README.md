# Crisp ground: native pixel and TV approval

The owner approved the ground concept: **“Approve ground concept”**. This checkpoint authors that direction with the
native terrain canvas and palette. Installed terrain and rules remain unchanged until the pixel/TV approval required by
[#82](https://github.com/higharc91/ArcBox/issues/82).

Review [before and after](comparison.jpg), then the full-resolution pairs below. Both sides use the same real Godot
renderer, camera positions, bridge geometry, party and scene state. The battle pair uses the real River Crossing
encounter setup with five heroes, not the mixed-creature demo. These are static visual captures, not new balance runs.

| View | Installed ground | Native candidate |
|---|---|---|
| World crossing | [Before](before/world-bridge-west.png) | [After](after/world-bridge-west.png) |
| Battle board | [Before](before/battle.png) | [After](after/battle.png) |
| TV overview | [Before](before/world-overview.png) | [After](after/world-overview.png) |
| Burned bank | [Before](before/world-ash-bank.png) | [After](after/world-ash-bank.png) |
| Autumn fields | [Before](before/world-field-edge.png) | [After](after/world-field-edge.png) |

## Art direction and diagnosis

Nine top textures and one cliff side use discrete indexed pixel shapes: cool charcoal ash and root debris, sparse
autumn grass, restrained earth roads, faceted bank stones and quiet grey water ripples. All source colours come from
the established Ashen Ford palette, with opaque source pixels and no antialiasing or diffusion dither.

The old 256-pixel tops span eight tiles; the new 128-pixel tops span four. **Both are 32 source pixels per tile.**
The old softness was not a density or nearest-filter failure: it came from painted tonal gradients and continuously
feathered boundaries. The candidate uses limited colour clusters and an explicit `edges: clustered` profile with
opaque, whole-pixel boundaries. Legacy masks remain unchanged, and installed materials do not enable the new profile.

The west battle bank previously retained a conspicuous grass rectangle against the burned world. In the isolated
candidate, 56 cells use charred root ground that matches the bank. The remaining dry roots retain the original fuel
classification through one explicit material alias. Every cell keeps its height, flags, collision and fuel behaviour.
Encounter counts, stats, rewards, objectives, props and story conditions are unchanged.

## Validation

- All **2,852 presentation tests pass**, including the existing legacy masks and new opaque cluster-boundary checks.
- Host build: **zero warnings and errors**.
- The candidate loads through the real rules and art loaders, builds both encounter plans and all 80 world blocks,
  and draws all three bridge states.
- Per-cell comparison covers all 19,200 world tiles in fortified, force and parley states: walkability, height, flags
  and fuel remain equivalent. Water conductivity flags are preserved.
- Matched native TV captures complete without errors. Existing compatibility-driver and standalone-demo exit warnings
  occur in the baseline too; software rendering is not a hardware frame-time acceptance result.
- The committed installed content tree remains unchanged. This review does not claim a production installation,
  scene export or balance retuning.

See `validation.json`, `presentation-tests.txt`, `build.txt` and the before/after TV logs. The preceding bridge
integration's full 6,779-test and 517-phone-test receipts remain in [integration](../integration/README.md).

## Reproduce and next gate

The source is `tools/tactics-assets/parts/ford_terrain.py`, using the existing `parts/terrain.py` canvas, slots,
cliff authoring and palette renderer. Original installed images remain available and are not overwritten.

```bash
python tools/tactics-assets/concepts/ford-rework/ground/render-preview.py --out /workspace/work/ford82-ground-preview
dotnet run tools/tactics-assets/concepts/ford-rework/ground/verify-preview.cs -- content/tactics/base /workspace/work/ford82-ground-preview/content/tactics/base
```

The capture tool now supports `--battle encounter:river_crossing` with an isolated `--pack` for the same renderer used
by the world preview. Authoring outputs, seeds, dimensions and hashes are retained under the concept batch's `ground/native-art/`.

**Awaiting owner pixel/TV approval.** After approval, install the selected textures and material aliases, update the
editable scene, export twice, run the final checks and refresh the place-shot set. The separate four-hero force balance
decision remains open; this art pass preserves existing mechanics.

No additional generation spend: **native authoring 0, PixelLab 0, Scenario total still 65 CU**.
