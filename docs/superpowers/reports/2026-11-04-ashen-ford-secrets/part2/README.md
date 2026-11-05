# Lore places · issue #81 part 2

Concept stage on `ashen-ford-secrets`. Part 1's verified game content is unchanged. The two new lore-linked briefs are in `tools/tactics-assets/concepts/scenario-briefs.json`; exact prompts, references, job/asset IDs and costs are in `tools/tactics-assets/concepts/ashen-ford-secrets/generation-records.json`.

The concepts reuse the existing Emberwick Scenario anchor, an actual Chapter 1 town screenshot, and the actual `cottage_thatch_green` sprite used for Maud's cottage. Scenario Nano Banana 2.1 is the same model recorded for the earlier approved village/animal concept series. All three jobs succeeded: **13 CU each, 39 CU total**, matching the dry-run estimates. The images were displayed inline for review. Owner approval is pending before PixelLab; no runtime assets or placement changed.

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

Issue #81 explicitly requires owner approval after the Scenario concepts, then PixelLab art from those approved references, then TV shots and a second owner approval before final placement. Both contact sheets have been shown inline with the approval question; no approval has been assumed. Concept paintings are reference material, not shipped sprites.

Once concepts are approved, use the town/props profiles and explicit `ashen_ford` palette. Preserve 2:1 isometric projection, top-left lighting, native pixel scale, ground anchors, footprint/collision contracts, and separate shadow/light masks. Keep small dressing pieces separate so their locations and depth order can be tuned. Do not add baked-in terrain or smoke effects to a building sprite; retain existing environmental effects where appropriate.

After pixel-art approval, add the town/props manifests and source records, paint the saved scene, export headless twice and compare bytes. Validate story and person routes, hidden-path access, camera scale and depth sorting, daytime/nighttime lighting, and Chapter 1 progression. No runtime placement or new paid pixel-art jobs occur before the relevant approval. Do not open a PR unless asked.
