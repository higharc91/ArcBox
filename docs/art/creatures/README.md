# Creature and overworld animal integration

The game reads `content/tactics/base/creatures/manifest.json`: 14 bestiary families and seven everyday animal families. There are 40 runtime atlases including coat variants. Eight distinct structural variants retain their original art so riders, exposed skeletons and crystals are not lost.

Open `index.html` directly for a portable review. All images are embedded. Automatic GIF previews work without JavaScript; the optional inspector selects a facing and action. Each card places a human fighter beside its subject at the same 3× game magnification. `scale-review.png` and `all-walk.gif` compare every primary specimen at a shared scale.

## Motion and registration

Compass order is south, south-west, west, north-west, north, north-east, east, south-east. New animals select authored views from their projected movement vector; they are not mirrored side views. The directionless wisp intentionally reuses its motion around the compass.

The pack contains 344 idle, movement and grounded crow-flap entries. Bestiary creatures also have basic attacks and defeated poses in every facing. Special attacks currently share the family basic attack; hurt uses the rest pose. These are visual replacements, with no new battle stats or encounters. Everyday fauna has no new combat behavior.

Each species uses one canvas and anchor across its frames. Ground contact is registered once per facing, and that same translation applies to all live animation frames. Individual frames are never recentered. Opening and closing rest poses are exact; duplicate closing frames are omitted from loop playback. Retain this policy when replacing a limb, weapon, pannier, wing or tail.

Pixels use fixed family palettes and transparent backgrounds. Atlas PNGs are losslessly indexed when their colors permit it; the encoder checks exact RGBA round trips. High resolution sources remain available for future edits.

## Physical sizes

`tools/tactics-assets/creature_scale.py` defines bestiary world scale independently of provider canvas size. The human fighter's head-to-ground height is 36 world pixels. Wolves and boars stay below the player; bears and giant creatures have species-specific limits. Every facing is checked against player-relative size budgets, including rear views. Dire wolves, black bears and polar bears preserve their size differences.

Everyday animals render at half their native pixel dimensions, with the crow reduced to 0.4 for its smaller physical size. The duck was separately redrawn at a small native size and uses an authored one-native-pixel buoyancy cycle after provider swim attempts unfolded its wings; its front and rear swimming views both have hidden feet and no painted water. The mule retains its attached grain panniers. Crow rear views are grounded with folded wings; an occasional grounded wing stretch uses a separate animation.

## Placement and scene rendering

Ashen Ford has 11 animal placements covering all seven everyday species. The dog lives near the pack area; two sheep accompany the farm pens. One goat home moved from a blocked tile to nearby free ground. Habitat checks require connected walkable land for mammals/crows and connected water for ducks. Existing cliff and fence restrictions remain in force.

Animals receive the same scene lighting as party figures, including local light sources. The wisp remains self-lit. Unit culling, ground rings, overhead marks and prop fading use world measurements rather than unscaled transparent atlas padding. HUD portraits use bounded authored rest-pose crops.

`scenes/` contains captures from the actual Godot renderer: a battle review and farm, pack area, woodland and river locations. These were rendered with software OpenGL in a virtual display and do not establish hardware performance.

## Rebuild and review

Python dependencies: Pillow, NumPy, SciPy. Accepted canonical sprites and combat inputs are committed; re-exporting does not call a provider or spend generation credits.

Run from the repository root:

```sh
python tools/tactics-assets/pixellab/animal-concepts/directional/export-creatures.py
python tools/tactics-assets/pixellab/overworld-animals/export-overworld.py
python tools/tactics-assets/creature_scale.py content/tactics/base/creatures/manifest.json
python tools/tactics-assets/review_creature_assets.py
```

The first exporter checks edge contact and required combat poses before replacing the runtime output. The fauna exporter requires all expected validated loops. `canonical/` contains accepted normalized frames; provider request manifests and job identifiers document their origins. Concept references are from Scenario; native poses and most motion are from PixelLab. The final duck buoyancy loop is locally authored. Local scripts perform palette, stray-pixel, anchor, scale and atlas cleanup.

For a battle inspection, launch the Mono Godot host with `--tactics-demo=forest_ruins:creatures`. World review uses `--world-demo=ashen_ford` and `--world-looks=name@lookX,lookY@standX,standY` (semicolon-separated for several locations).
