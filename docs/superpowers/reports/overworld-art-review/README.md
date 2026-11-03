# Overworld art review

Reviewed against the approved tactical characters and the *Embers of the Vale*
lore in `content/tactics/base/lore/world.json`, `regions.json` and the Chapter I
arc/scenes. The audit covers **600 shipped environmental PNGs**, including
terrain, props, town pieces/textures, animated effects, map icons and their
shadow/light masks. An animation strip counts as one asset, not one per frame.

Open [the searchable illustrated review](index.html) for an individual card for
every image. [assets.json](assets.json) records its dimensions, frame count,
anchor, hash, decision, recommended context and current integration status.
The actual PNG inventory must match the reviewed inventory; new families need
an explicit decision in `tools/tactics-assets/parts/overworld_review.py`.

## Findings and changes

- **563 images kept.** Town architecture, layered foliage, faceted rocks,
  weather, water animation and most utility props already match the art direction.
  Keeping them is an explicit review outcome, not a reason to add detail everywhere.
- **26 existing images refined.** Grass has fewer, broader tufts and fewer bright
  tips, leaving room for small characters. Ordinary ash no longer glows or casts
  firelight: the Fall happened a century ago. Tents gain folds and repairs; wells,
  chests and altars gain material depth. The chapel carries a gilded flame-shaped
  finial rather than a generic cross. Cottage roof variants now share one projection
  frame with the family's manifest, shadows, light overlays, origins and anchors.
  Derived shadow/light masks and the animated chest are included in this count.
- **11 images added.** A dark plank edge, a mended hide tent for the hungry
  Skarrow camp, a translucent river-steam animation, and eight literal parchment
  landmarks: stone bridge, rope bridge, hut, inn, crater, heartpool, flame hall and ice cave entrance.
- **Runtime material edges fixed.** Paths, ash, woodland floors, bog/mud, sand,
  dunes, sandstone, snow, ice, planks and pavers use appropriate sides rather than
  falling back to grey cliff rock. Warm paving has warm foundations. Wooden
  walkways identify as decks. Frost/snowy needles keep a snowy rim.
- **Lore placement recorded individually.** Autumn vegetation belongs outside
  Emberwick's warm fields; snow belongs to Frostpeak, large bog pools and wisps
  to Mirewood, cacti/dunes to Sunscar, and active volcanic features to an actual
  hot site. Skull pikes, cages and blood need an authored reason, particularly
  around the starving Skarrow clan. Glowing crystals, portals and ritual objects
  should identify a magical place rather than fill otherwise empty scenery.

## Current integration boundary

Ashen Ford's saved region and its Godot painting scene currently contain terrain,
battle stamps, story spots and spawns. Buildings, boats and tents are represented
by terrain blocks; there is no authored region prop/building/effect placement
layer, and the walking exploration renderer is not implemented in this checkout.
The live TV renderer consumes the shared terrain/prop art in **battles**.

Consequently the material fixes improve those live maps immediately, but the town
building, steam and landmark work is **asset-library ready**. The new `placeIcons`
metadata is an art override for existing lore place ids; a future map consumer
must honor each place's existing unlock conditions. It does not change lore kinds,
story state or visibility by itself. The generic parchment sample is a style
specimen, not canonical Vale geography.

The [context comparison](../../../../tools/tactics-assets/previews/overworld-context-comparison.png)
shows proposed dressing at the game's pixel density. It is a software composition,
not a screenshot of a playable walking overworld. Terrain layout, heights, flags,
battle stamps, beat positions, navigation and narrative are preserved.

## Validation

- 600 PNGs accounted for: image sizes, horizontal frame strips, in-frame anchors,
  static/animated replacement alignment and animation-state frame indices checked.
- Python overworld contracts: 5 passed, including cottage projection/attachments,
  non-emissive old ash, restrained steam opacity/loop seam, and lore icon references.
- Presentation: 914 tests passed, including the new material-edge cases.
- World tools: 293 tests passed, including painted scene/export agreement,
  reachability, story routes and battle-stamp preservation.
- Host and world editor builds: passed with no warnings or errors.
- Editor terrain swatches regenerated through Godot; painted scene/source IDs preserved.
- Live Godot + server + four bot phones: River Crossing reached results through
  170 beats; screenshots, effects and music checks passed with no renderer errors.
- Lore reference checker: passed.
- Standalone gallery: 600 cards and set/search/change filters checked in Chromium; no JavaScript errors.

Review decisions and the gallery can be rebuilt with:

```bash
python3 tools/tactics-assets/build.py terrain props ambient town worldmap
python3 tools/tactics-assets/audit_overworld.py --before /path/to/previous/sets
python3 tools/tactics-assets/review_overworld_context.py --before /path/to/previous/sets
python3 tools/tactics-assets/test_overworld.py
```

The optional baseline contains `terrain/`, `props/`, `town/`, `ambient/` and
`worldmap/`. Without it the audit still validates coverage and metadata, but
reports every item as reviewed instead of claiming a historical change.
