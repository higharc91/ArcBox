# Lore places · issue #81 part 2

Pixel-art review stage on `ashen-ford-secrets`. **The owner replaced PixelLab with local authoring. The funding request and PixelLab instructions below are historical and no longer block the work.** Four native assets and actual TV previews are ready for the second owner review. See [the current local-art report](local-art/README.md) for originals, screenshots, source, verification and the reviewed [39,71] granary layout. Part 1's live game content and the saved scene are unchanged pending art approval.

The two lore-linked briefs are in `tools/tactics-assets/concepts/scenario-briefs.json`; exact Scenario prompts, references, job/asset IDs and costs are in `tools/tactics-assets/concepts/ashen-ford-secrets/generation-records.json`.

The concepts reuse the existing Emberwick Scenario anchor, an actual Chapter 1 town screenshot, and the actual `cottage_thatch_green` sprite used for Maud's cottage. Scenario Nano Banana 2.1 is the same model recorded for the earlier approved village/animal concept series. All three jobs succeeded: **13 CU each, 39 CU total**, matching the dry-run estimates. The images were displayed inline for review. The owner approved both concepts with “Looks good” on 2026-11-05. PixelLab generation is authorized; no runtime assets or placement changed.

| Concept | Original image | Scenario asset |
| --- | --- | --- |
| Shared style/design anchor | [2752×1536 PNG](../../../../../tools/tactics-assets/concepts/ashen-ford-secrets/shared-anchor.png) | `asset_hbuwhcmNdF6WKw5jhMAJpeto` |
| Locked granary contact sheet | [2528×1696 PNG](../../../../../tools/tactics-assets/concepts/ashen-ford-secrets/granary-sheet.png) | `asset_YwU2o6XytMjAbX4nZBFTdUeG` |
| Maud's hut/dressing contact sheet | [2528×1696 PNG](../../../../../tools/tactics-assets/concepts/ashen-ford-secrets/maud-hut-sheet.png) | `asset_K2XK6wqLUJZu7jXdkCzUDZHX` |

Visual review: both designs share the town's camera, warm/cool material treatment and practical construction. The granary's closed central hasp/lock and grain-storage silhouette read clearly; Maud's cottage retains its compact shell with distinct herbs, simple charms, soot, pots and cold-ash planting. The garden detail inset contains a roof/chimney fragment: use the clean ground portion as a reference for a separate garden prop, never as a production cutout. Scale silhouettes, parchment, ground patches and sheet insets must not enter shipped art. PNG decoding, dimensions, provenance hashes, JSON records and document links were checked. This stage changes briefs/review artifacts only, so the already completed full game suite was not repeated.

## The granary

A compact raised stone-and-timber barn for a hungry town whose council keeps its grain locked away. The door, iron straps and central padlock must read clearly at the game camera. Narrow loft vents, a restrained hoist, modest tied grain sacks and practical repairs establish its use without turning it into a wealthy warehouse or a generic tavern.

The provisional footprint is at most 3×2 tiles, subject to the saved scene's route/placement checks. The existing `ch1_granary` marker at [42,70] and nearby people must remain reachable. Inspect/reuse the current grain-storage pocket and neighbouring cottage/clutter placements; do not place a building over the marker or fill the approach just because the concept has a decorative ground base.

## Old Maud's hut

Dress the existing cottage at [130,88] (`cottage_193`, `thatch_green`) rather than replacing its silhouette with an oversized fantasy hut. Maintain the 2×2 footprint, roof and door/chimney arrangement. Add a soot-dark chimney, hanging kitchen herbs, simple twig/cord charms, a worn stool and clay pots, and a modest herb bed in cold scorched-grey soil. The house is cared for by an old, practical woman; the look should be austere and humane.

Maud has never used ember-fire. Do not add magical windows, glowing crystals, burning ash, a witch altar or story-spoiling symbols. Keep dressing modular and leave her standing place, the story marker, visitor approach and new reed-bank shortcut clear.

## Next stages

Issue #81 explicitly requires owner approval after the Scenario concepts, then PixelLab art from those approved references, then TV shots and a second owner approval before final placement. Both contact sheets were shown inline and approved. Concept paintings are reference material, not shipped sprites.

## Pixel-stage preflight

The existing configured PixelLab MCP connection was successfully initialized through a local client (107 tools); no new key or connection is needed. The live account reports **0.75 subscription generations remaining, $0 credit, and a daily bonus of 5**. The owner was asked to add around 60 generations. Six planned requests total **40 generations before revisions**: one 256×224 Pro granary, a localized 188×155 edit of the existing cottage for chimney soot, and four Pro Flash dressing groups (herbs, twig/cord charms, cold-ash garden, stool/pots). No paid jobs have been started.

[The native-size plan](../../../../../tools/tactics-assets/concepts/ashen-ford-secrets/pixel-plan.json) records prompts, source files, costs, footprints and attachment policies. The [request preparer](../../../../../tools/tactics-assets/concepts/ashen-ford-secrets/prepare-pixels.py) resolves fresh upload references, applies explicit `ashen_ford` palette/style settings where the selected tool supports them, and validates against PixelLab's actual current schemas without submitting jobs. `edit_image` has no palette argument: the cottage edit retains the native kit colors. The shared small-prop style reference is the shipped 26×28 oak crate, which fits every target canvas. [Six-request validation result](pixel-request-preflight.json).

```sh
python tools/tactics-assets/concepts/ashen-ford-secrets/prepare-pixels.py \
  --uploads /path/to/fresh-upload-refs.json \
  --catalog /path/to/current-pixellab-tools.json \
  --out /path/to/scratch-requests
```

Reference upload URLs expire after 15 minutes; uploaded `upload:<id>` inputs last three days. Refresh them if needed. Generated request JSON contains inline small source sprites and belongs in scratch output; the checked-in plan records source paths and hashes rather than expiring URLs or credentials. The approved granary sheet has already been uploaded for the current preparation.

The actual Godot world demo captured [the granary area before](before-granary.png) and [Maud's hut before](before-maud.png); [TV output](before-tv.txt) completed successfully. These are unchanged live-map baseline views, not pixel-art candidates. A [read-only collision preflight](placement-preflight.json) adds a blocking 3×2 placeholder at [43,72], replacing the space of `cottage_39` and the overlapping `clutter_barrels_41` for a future preview. All six foundation cells are level height 3, and all 12 story legs and two recovery routes remain reachable with hidden shortcuts closed. The production map was not edited. Art anchors, door orientation, roof occlusion and final placement still require generated pixels and TV review.

Reproduce that collision check from the repository root:

```sh
dotnet run tools/tactics-assets/concepts/ashen-ford-secrets/placement-preflight.cs -- content/tactics/base
```

Once concepts are approved, use the town/props profiles and explicit `ashen_ford` palette. Preserve 2:1 isometric projection, top-left lighting, native pixel scale, ground anchors, footprint/collision contracts, and separate shadow/light masks. Keep small dressing pieces separate so their locations and depth order can be tuned. Do not add baked-in terrain or smoke effects to a building sprite; retain existing environmental effects where appropriate.

After pixel-art approval, add the town/props manifests and source records, paint the saved scene, export headless twice and compare bytes. Validate story and person routes, hidden-path access, camera scale and depth sorting, daytime/nighttime lighting, and Chapter 1 progression. No runtime placement or new paid pixel-art jobs occur before the relevant approval. Do not open a PR unless asked.
