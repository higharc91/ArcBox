# Locally authored pixel art · owner review

**Approved by the owner (“I approve”) and integrated on 2026-11-05.** This page archives the reviewed candidates and preview checks. See [final integration](../../integration/README.md) for the live pack/scene and final verification.

The owner replaced the PixelLab stage with “We don't have access to pixal lab, you make it.” These sprites are authored locally with the production town renderer and its existing materials. No PixelLab access, balance top-up or paid generation is needed. This stage used **zero new generation credits**; the earlier approved Scenario concepts remain the design references.

## Art and TV captures

- [Native pixel-art sheet](pixel-art-sheet.png): granary, Maud's cottage, cold-ash herb bed, stool and pots.
- [Granary in the actual TV renderer](granary-tv.png).
- [Granary story-marker clearance / normal building fade](granary-marker-tv.png).
- [Maud's hut in the actual TV renderer](maud-tv.png).
- [Native PNGs and metadata](../../../../../../tools/tactics-assets/concepts/ashen-ford-secrets/local-pixels/assets.json).

The granary has a raised fieldstone ground storey, framed grain loft, narrow louvres, grey shakes, a small hoist and a single working padlock/hasp bridging its double doors. Its 3×2 footprint replaces one generic cottage in the preview storage yard. Existing sacks and an empty cart reinforce the purpose while leaving the doorway clear. It has no new magical or emissive lighting.

Maud's hut keeps the original cottage's **188×155 frame, origin, ground anchor and 2×2 footprint**. Local surface soot, a stone footing, hanging sage/dried herbs and plain twig/cord charms are authored relative to the house. The hanging pieces are part of its geometry, so they stay attached and share its occlusion and lighting. The cold-grey garden and knee-high stool/pots are separate prop assets. Four nearby skull-pike decorations are removed in the preview: her home is practical and cared for, not an occult camp. Ordinary woodsmoke and the existing exterior torch remain; no ember-fire or glowing ash is added.

## Validation

The [art checks](art-validation.json) confirm four deterministic image/shadow renders, transparent frame borders, matching shadow dimensions and Maud's unchanged cottage frame/anchors. The preview pack loads through the real `RulesPack`/art manifests. [Route verification](routes.json) passes all 12 story legs, both recovery legs, the two review standing tiles and both story markers. [The TV capture](tv-output.txt) completed with no TV errors under Godot 4.7.2/OpenGL compatibility at 1280×720; software-renderer V-Sync/AA notices are environment limitations, not a hardware frame-pacing measurement.

An initial northern granary position would have covered the existing `af_granary_sack`. The content validator caught it, and the reviewed candidate instead uses **[39,71]**. The quest sack stays at [40,70], the granary story marker stays at [42,70], and Maud's marker stays at [132,93]. No interactable coordinates, flags, rewards or voiced text changed. The granary's earlier [43,72] collision-only preflight is historical, not this final review candidate.

These review captures used a disposable cloned pack. The owner subsequently approved them, and the same PNG bytes and layout are now integrated into the live pack and saved scene. Current full-suite results and fresh live-pack captures are in the [integration report](../../integration/README.md).

## Source and reproduction

The authored geometry/material functions are in [lore_places.py](../../../../../../tools/tactics-assets/parts/lore_places.py). Candidate originals, hashes, source hashes and placement details are in [provenance.json](provenance.json). From the repository root, with Python/Pillow/numpy, .NET 10, a built Godot .NET host and its imported assets:

```sh
python tools/tactics-assets/concepts/ashen-ford-secrets/render-local.py --out /path/to/preview
dotnet run tools/tactics-assets/concepts/ashen-ford-secrets/verify-local.cs -- /path/to/preview/content/tactics/base
python tools/tactics-assets/concepts/ashen-ford-secrets/preview-local.py --stage /path/to/preview --godot /path/to/godot
```

The capture helper uses Xvfb on Linux when no display is selected, or the existing display/window otherwise. The preview builder copies the pack, stages the candidate layout and clones the already-built host beside it. It checks the live manifests/world file hashes remain unchanged. It does not silently install the art into the live game.

## Approval and integration

The owner approved this art and the TV previews with “I approve” on 2026-11-05. The native build hooks, manifests, palette and scene are integrated. Two headless exports are byte-identical; final checks are recorded in the [integration report](../../integration/README.md). No PR is to be opened.
