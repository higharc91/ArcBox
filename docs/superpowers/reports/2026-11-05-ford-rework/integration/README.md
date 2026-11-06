# Installed Warden bridge and road ramps

Owner-approved native bridge art is installed on `ford-rework` for [#82](https://github.com/higharc91/ArcBox/issues/82).
The owner also requested ramps at every authored road elevation change and characters that follow their actual slopes.
The separate crisp-ground concept is approved; its [native pixel/TV review](../ground-art/README.md) awaits installation approval.

## What is installed

The shared `river_crossing` stamp has six raised timber deck cells, two-tile supported ramps on both banks,
blocked stone parapets, a gatehouse beside the lane, and continuous roads from Emberwick through the east exit.
The gatehouse/storage footprint is off the road and the shallow-water landing remains open. One crate and the
stormcaller move clear of that footprint; the camp spawn zone and ford dialogue zone follow the new geometry.
Enemy counts, stats, abilities, rewards and objectives are unchanged.

The original northern ruined bridge remains deep and unwalkable. The ford stamp is still the only river crossing.
Deck planks are fuel and the shallows still conduct lightning. All 138 new native PNGs pass the palette and binary-alpha checks.

The approved structure is split into **113 physical tile-owned state pieces**, plus invisible checkpoint collision.
No combined review sprite is shipped. Static world blocks exclude story-dependent pieces; a sorted overlay selects
fortified art until `ch1_ford_settled`, then force art with `ch1_skarrow_slain` or parley art with `ch1_skarrow_spared`.
Choosing a branch without winning leaves the checkpoint closed. Followers receive the same updated collision state.

## Character movement and road coverage

The audit covers both shipped regions and all walkable path, Ford path and cobble road cells outside blocked footprints.
Ashen Ford has **13 road elevation edges**, covered by **38 native ramp tiles** using 24 shared mesh profiles.
Cinder Hollow has no road cells or road elevation changes. See [the complete audit](road-audit.json).
Road heights and collision flags outside the ford are preserved.

Each ramp has an actual sloped mesh. Shared corner heights make adjoining surfaces meet continuously;
the character sampler uses the same two triangles. Party leaders, all followers, townsfolk, animals,
roaming enemies and battle movement sample that surface. Ramp travel suppresses the old synthetic step hop.
Wooden decking and masonry ramps also use the corresponding existing footstep sounds.

The real walking presenter was captured with four heroes going both directions on both bridge lanes:
[15-second movement recording](ramp-walk.mp4). Tests sample every animation frame for parties of one through four,
both directions, and actual battle movement. Picture tests require zero incorrect foot overlap pixels in the world
and fight; solid parapets correctly hide lower legs while leaving upper bodies visible. The original #78 quarter-frame
and wide-prop regressions remain as explicit legacy fixtures with their original thresholds.

## Live views

| State | West approach | East approach |
|---|---|---|
| Fortified | [World](fortified-west.png) | [World](fortified-east.png) |
| After force | [World](force-west.png) | [World](force-east.png) |
| After parley | [World](parley-west.png) | [World](parley-east.png) |

[Actual in-place battle](battle-fortified.png). Road views are named `world-road-<x>-<y>.png` in this folder.
These are native Godot captures of installed content, not assembly previews. Software OpenGL is a visual and lifecycle
check; it does not establish a hardware frame-time result. The four-member/roster-change texture check peaks at
446,461,823 bytes (425.78 MiB), below the existing 512 MiB gate.

## Saved scenes and content preservation

Both scenes export twice byte-identically after the final palette cleanup. Material source IDs stay stable.
All existing people, variants, buildings, stamps, markers, spawns and original region prop placements are preserved.
Ashen Ford still has 930 original active prop cells; Cinder Hollow's scene and world JSON are unchanged from main.
No townsfolk, hero, creature or voice asset is edited. Native source pixels reconstruct the approved whole structure
exactly before per-tile placement; see [asset hashes](installed-assets.json) and [content checks](content-checks.json).

## Balance and chapter checks

[Paired results and proposed follow-up](balance.md) retain every seed and roster: 480 battles per layout for real
session sizes, plus 480 per layout for explicit small-hero stress. Overall wins are nearly unchanged, but fights run
longer and capped cases rise. Encounter numbers remain unchanged for the owner's decision.

The canonical four-hero force bot campaign lost six attempts before being stopped. A separate supported six-hero
force campaign won its first fight and reached the chapter card, then walked four more steps. Its strict checker passes.
This proves that branch's story/bridge transition, and does not claim four-hero balance acceptance.
The four-hero parley campaign at normal 2.5-second dialogue timing won its first fight, said all 129 selected lines once,
reached the card and walked four steps afterward. Its strict checker passes. An earlier accelerated 0.5-second run
reached the card but missed one early mayor line on the TV, so it is not the final acceptance run.

Final verification: **6,779 .NET tests and 517 phone tests pass; build has zero warnings/errors.**
See [validation.json](validation.json), [force checker](chapter-force/check.txt), [parley checker](chapter-parley/check.txt),
[force card](chapter-force/card.png) and [parley card](chapter-parley/card.png).
The crisp ground repaint and its pixel/TV approval are not part of this installed bridge checkpoint. No PR or merge is requested.

## Reviewed test changes

Content expectations now account for mesh-owned surfaces, story-gated props and the moved enemy starts.
The two Ford replays use legal routes on the new map while retaining their Sleep, skipped turns, Bless, death,
downing, conduction, freeze and douse assertions. The controlled storm replay burns three adjacent grass cells
instead of one; exact reaction and friendly-conduction totals remain checked.

The deliberately flooded no-crossing fixture now floods the new deck too, so opening the story checkpoint cannot
invalidate its stranded-swimmer regression. The walking oracle follows saved story flags after a win, and the whole-region
walk routes around real interactable blockers, including the campfire beside the new approach. Seed/event budgets,
movement legality checks and painter thresholds are retained. Four phone fixtures reflect changed moves/targets and battle results;
no phone implementation changes are needed.

## Authoring and reproduction

`parts/ford_bridge.py`, `native/install.py` and `native/road-ramps.py` author the geometry and pixels.
The installer takes a pre-bridge `--baseline-rules` snapshot from commit `217a2a5f`; the road tool saves its own
post-bridge baseline in `--work`. After rerunning the installer, refresh that road baseline before regenerating ramps.
Use the editor's `--swatches`, `--tilesets` and `--paint-region=<json>` commands, preserve/remap existing prop identities,
then export both regions twice. The committed scenes are the editable authority; do not replace them from generated JSON.

`native/sim-impact.cs` reproduces the paired runs. `native/capture-review.py --party-walk --walk '102,61;111,61;102,61' --record`
uses the real walking presenter. Supply a live pack, name and explicit report directory; `--flags` selects an ending.
