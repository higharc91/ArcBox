# Ashen Ford texture and bridge pass

Date: 2026-11-01
Branch: `world-dressing-design`

## What changed

- Repainted Ashen Ford's ash, dry and wet earth, autumn and dry grass, packed path, river water, rock tops, and the Ford cliff face with larger, quieter painterly forms. The new tops cover an 8×8 tile span to reduce repetition; the cliff face is a separate long, muted slate texture.
- Added organic high-resolution ground masks for Ford materials. They blend softly into neighbors and skip extra pebble/leaf decals so the surface detail stays controlled.
- Repainted the River Crossing timber bridge at twice its source resolution with cleaner, broader planks and less grain. After visual review, softened its grain again and reduced micro-contrast while keeping the same sprite dimensions, placement, and anchor. The updated in-game view is in `texture-art-pass/battle-river-crossing.png`.
- Repointed Ashen Ford's existing material indices to Ford-specific variants. The region's material bytes, tile heights, and material slots remain unchanged.
- Fixed the overworld bridge path: the four deck cells at (105–108, 48) were marked deep water. They are now shallow water, leaving them visibly part of the river while making them walkable. Updated the editable region scene and exported chunk flags. Added a reachability test across the deck.
- Kept the battle map's bridge tile unchanged; it is already walkable. The phone protocol and game rules are unchanged.

## Screenshots

- [Ashen Ford materials in the world](texture-art-pass/world-ashen_ford.png)
- [Ford cliff and rock bank in context](texture-art-pass/world-rock-bank.png)
- [Party standing on the overworld bridge deck](texture-art-pass/world-bridge-walkable.png)
- [Burned ash](texture-art-pass/world-ash.png)
- [Dry and wet earth](texture-art-pass/world-dirt.png)
- [Autumn grass](texture-art-pass/world-grass.png)
- [River water](texture-art-pass/world-water.png)
- [Battle River Crossing with the cleaner timber bridge](texture-art-pass/battle-river-crossing.png)

## Verification

- Host Debug build: 0 warnings, 0 errors.
- Focused presentation tests: both the Ford cliff span UV test and bridge bank-to-bank reachability test pass. The full focused presentation run before the final art iteration had 249 passing tests.
- Content validation tests: 140 passed across base-pack, validation, and region checks.
- Full presentation test run: 2,012 passed, 7 failed. Failures were `BoardPlanTests.The_plan_says_whether_it_needs_a_clock`, `BoardMapTests.The_real_river_crossing_has_its_river_ford_and_ridge` (expects the old `water/clear` name), `WorldDrawingTests.The_plan_lists_each_image_once_and_says_which_lie_flat`, `WorldPlanTests.Block_order_agrees_with_painters_order_where_items_overlap(name: "ashen_ford")`, and three `OverlayPictureTests` for Ford painter ordering/array bounds. The full suite was not green; these need a separate review.
- Godot 4.7.2 on the RTX 5080, 1920×1080, left monitor, no focus: the short six-tile route rendered with a 6.94ms median frame and no frame above 16ms. World setup was 510ms. The party screenshot shows the walker on the deck.
- New square textures are 256×256; Ford cliff art is 256×32 and declares an eight-tile sampling span so the UVs use the full side texture. Opposite edges are blended for repetition.

## Remaining

1. Review the seven failures in the full presentation suite.
2. Recheck the cliff silhouette and palette during the next full-world art pass; the textures are calmer, but the tile-based landform outline is still visibly stepped.
3. Review character models as a consistent set later, as requested.

No commit or push was made.

## Follow-up visual review

- Replaced the Ford's broad cracked-stone top with calmer, larger irregular slate planes and rebuilt the cliff strip so it reads as grey rock rather than dark timber bands.
- Replaced the dock's old hull-shaped plank patches with deep water that blends into the river; the water stays non-walkable. Added three small fishing skiffs beside the piers and a test that keeps them on deep water.
- The dock walk now reads as a working riverfront: stone quay, three timber piers, market stalls, fishers, lanterns, cargo, and moored boats. Screenshot: `ashen-ford-polish/world-docks.png`.
- Rechecked the ford/cliff route and Maud's hut on the RTX 5080 at 1920×1080. The Ford scene loaded in about 0.55 s; the short route had a 6.94 ms median frame and no frame above 16 ms. Godot ran on the left monitor with focus disabled.
- The 3 focused dock tests and 7 Ford material-resolution tests pass. The full 380-test world-tools run had one remaining failure: `BasePackRegionTests.A_region_file_is_what_its_scene_exports(ashen_ford)` because the hand-painted scene and exported region already contain mismatched props. The changed stamp-material expectation was updated to the current `grass_ford/autumn` map data.

## Dock and ground-transition follow-up

- The screenshot review exposed that the piers began two tiles inside the stone quay. Their roots now begin at the first water tile (x=101), with the quay retained through x=100; the hand-authored world JSON was patched at those shoreline cells to match. Skiffs remain moored beside each pier.
- Ford's large painted ground tops now use a wider, calmer alpha fade between materials (0.12–0.94 tile reach, reduced edge wobble), while standard battle-map masks retain their original edge. The wet-dirt top's contrast is reduced while its exact repeat edges remain intact, making grass-to-earth transitions less abrupt. Visual capture: `ashen-ford-polish/world-shore-blend.png`.
- Verification: host build succeeded with 0 warnings; the focused Ford blend-mask and dock/mooring tests pass. Real-GPU world demo ran on the NVIDIA RTX 5080, 487 ms initial setup, 6.94 ms median frame, and no frames over 16 ms. Dock preview: `ashen-ford-polish/world-dock-fit.png`.
- The terrain still has tile-stepped cliff silhouettes; this pass improves the surface read, not the underlying height contour geometry.

## Hill rock surface follow-up

- Repainted the terrace's rock top as broader, softer slate planes and made the cliff strip brighter and less banded. Both keep their manifest dimensions; opposite edges were blended for repetition. The world screenshot is `ashen-ford-polish/world-cliff-face.png`.
- The terrace silhouette remains stepped because changing it means changing world heights and walkability; this art pass leaves those intact.
- Reworked the dark hill crag, boulder, and scree cluster sprites to match the slate palette, plus the snowy spruce and river reeds. They retain their existing sprite frames, anchors, placements, and blocking settings. See `ashen-ford-polish/world-hill-rocks.png` and `ashen-ford-polish/world-snowy-pine.png`.

## Farm and field follow-up

- Replaced the two training-target-looking field props with a readable burlap-and-straw scarecrow, sized for the overworld, and placed a third in Mayor's Forty. Their existing blocking behavior is unchanged.
- Previewed Mayor's Forty, Long Acre, Hearthfield and the reeve's farm in Godot 4.7.2 on the RTX 5080 at 1920×1080. The new scarecrow reads as a farm prop at world scale; the dirt and autumn-grass blend is calmer than the earlier stippled field pass. Screenshots: `ashen-ford-polish/world-mayor-acre.png`, `world-long-acre.png`, `world-hearthfield.png`, and `world-reeve-farm.png`.
- Host Debug build succeeded with 0 warnings and 0 errors. World and prop JSON parse successfully. Godot ran at `(160,100)` on the left monitor with no focus; its short demo route had a 6.94 ms median frame and no frames above 16 ms.
- Kept tile heights and material IDs as they were. The additional scarecrow uses the already-blocking training-dummy prop type, so its single occupied tile is blocked as with the existing two placements.

## Farm fences and crops

- Replaced the old partial fence fragments with continuous field borders around all eight named farm plots. Added a mirrored fence variant for the opposite isometric edge; shared boundaries are deduplicated, and each plot has an opening. The Southfield opening also leaves room beside the reeve's cottage, and the Wardens' Strip/Low Field share a passage.
- Added a small transparent wheat-stubble crop sprite and 21 placements in spaced rows, concentrated in Ten Rods so the harvested grain reads clearly without filling every field with clutter.
- Godot previewed Mayor's Forty, Gate Close, Southfield and Ten Rods on the RTX 5080. Build: 0 warnings, 0 errors. The 2-tile demo walk had 8.33 ms median frame time, 10.22 ms p95, and no frames above 16 ms. Screenshots: `ashen-ford-polish/world-mayor-field.png`, `world-gate-close.png`, `world-southfield.png`, and `world-ten-rods.png`.
- Heights and terrain materials were not changed. Fence openings are visible in the captures; a dedicated player walk through every gate still needs a play check.
