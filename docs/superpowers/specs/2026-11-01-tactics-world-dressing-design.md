# ArcBox Tactics world dressing: design

- **Sub-project:** the first of four that take the overworld towards Baldur's Gate. The others are inventory with looting and Perception (sub-project 6), NPCs and side quests, and reactive surfaces.
- **Builds on:** the [overworld design](2026-10-31-tactics-overworld-design.md) and its milestones 5a to 5d. It starts after the story runtime lands, because both touch the same world files.

## 1. Goal

Ashen Ford looks sparse because the world can't hold props: its woods are only floor. This work gives regions painted props, ground detail and ambient life, so a place feels inhabited and worth walking through. It also gives the later projects somewhere to put things.

**Success means:** walking from Emberwick to the ford passes through a town with clutter, real woods, farms and a burned bank, the party is never lost behind a tree, and the frame rate holds.

### Decisions

| Question | Decision |
|---|---|
| The direction | Dense, explorable places, people and side quests, and a world that reacts. The leader still steers the party |
| What comes first | Dressing, before any new game systems |
| How props are placed | Painted on a Props layer. Claude's draft script does the first pass and the user refines it. No scatter brush and no generation at load |
| A party behind a tall prop | The prop fades. If that costs too much, the party's silhouette shows through instead |

### Out of scope

- Containers, pickups, drops and gathering nodes, which need an inventory.
- NPCs, side quests, mobs and fog of war.
- Reactive surfaces and hazards doing harm.
- New art: this uses the pack's art, and gaps are listed for the user.
- Dressing other regions: only Ashen Ford exists.

## 2. What the player sees

- **Props:** trees, pines, bushes, rocks, stumps, logs, tall grass, reeds, ferns, mushrooms, fences, carts, wells, barrels, ruins. The pack has 68 prop types, most with variants. Props that block stop the party like any wall.
- **Ground:** decals such as flowers, pebbles and scorch marks break up the ground, and a slow tint stops ash and water repeating every 4 tiles. `BoardPlan` draws no decals today.
- **Ambient life:** birds, butterflies, fireflies, fish, falling leaves and chimney smoke. The pack has this art and nothing draws it yet.
- **Fading:** a tall prop between the camera and the party turns partly see-through as they pass, and comes back when they've gone.

Phones don't change.

## 3. How the world is made

### 3.1 Format

A region file gains `props`, grouped so a wood stays small:

```json
"props": [
  { "id": "tree", "variant": "autumn", "at": [[40, 12], [41, 14]] }
]
```

Groups sort by id and variant, and tiles by position, so an export stays byte-identical and diffs stay small. Whether a prop blocks, its cover and its height come from the props manifest, so nothing is entered twice. A prop fills one tile, as in battle maps.

The file is under `rules/world/`, so a repaint doesn't restart a saved battle. A deployed export still makes each TV download the whole pack again (handoff §8, decision 7).

### 3.2 The painting workspace

Each region's scene gains a fourth layer, Props, with one swatch per prop and variant. It paints like the other three, and the export reads it. Erasing a prop leaves the ground beneath it alone.

The layer shows the prop's own art at a small size, so a wood reads as a wood when zoomed out. Blocking props are marked, because a mistaken wall is the likeliest painting error.

Apply stamp doesn't place the map's props on this layer. They stay the stamp's, as they are now.

### 3.3 Claude's draft

A script fills the layer once for Ashen Ford, using a fixed seed: woods along the road, farm fences and carts around the fields, clutter in Emberwick's streets, and dead trees and rubble on the burned bank. It refuses to overwrite a scene that has props, so painted work can't be lost, and it's the only place randomness is used.

### 3.4 Checks

The pack loader fails on:

- a prop id or variant that isn't in the manifest;
- a prop outside the region, or in a void chunk;
- a prop on a battle stamp, because a stamp's tiles must match its map exactly.

Blocking props count wherever walking is checked, through `WorldGround`: reachability, walk-back and the story-order check. So a tree can cut off a beat, and the export says so.

The export also warns, without blocking the file, on:

- a blocking prop on a road tile;
- a prop on a beat spot or a spawn;
- a tall prop within 4 tiles in front of a stamp, along +x or +y, where it would hide a fight.

## 4. How the game runs it

### 4.1 The server

`WorldGround` merges the region's props with the stamps' props, and blocking ones stop the party through `Terrain.Walkable`, as stamps' props already do. Nothing else on the server changes, and there's no new view data: the TV reads props from the pack.

### 4.2 Drawing

Low props draw in the static blocks, as stamps' props do now. The stress region ran 3,840 props and 960 lights at 144 fps on an RTX 5080. Props with light data light up as they do on the board. Decals and the ground tint go into the same bake.

`StagePlan` draws props near a fight from the same data, so a fight on the spot keeps its surroundings.

### 4.3 Fading

A tall prop can't stay in a static block, because a recorded block can't change one item's alpha. So props of height 2 or more leave the blocks and draw in a thin layer of their own, sorted with the rest of the picture, and only that layer changes when something fades.

A pure `PropFade` in Tactics.Presentation decides each prop's alpha from where the party stands, easing over about 0.2 s. Only props in front of a walker fade, and only those overlapping a walker's rect. The occluder overlay has to keep working: it can't repaint a faded prop solid over a walker.

During a fight, props never fade for units, and the warning in §3.4 keeps tall props clear of a stamp's front.

### 4.4 Ambient life

A small pure scheduler picks what's on screen from the materials near the camera: butterflies over grass, fish in deep water, smoke from chimneys, fireflies at dusk once there's a time of day. It's seeded per region and runs on the TV only, so it can't affect the game, and it stops where the camera isn't. A world with none of it still plays.

## 5. Testing

- **Content:** each check in §3.4 has a failing case.
- **Export:** a round trip of a small Props layer gives the expected file, and a second export is byte-identical.
- **Walking:** a tree cuts a road and the story-order check says so. A blocking prop stops the party on the harness.
- **Picture:** `OverlayPictureTests` sweeps a walk through a wood against true painter's order. `PropFade` gets its own tests, including a walker stopping behind a tree and a party split across one.
- **By eye:** a real walk through the woods on the real TV, at the same spots as 5b's checks plus a tree at a block corner.
- **Performance:** measured with no `--shots`, which stall frames.

## 6. Build order

| # | Task | Done when |
|---|---|---|
| 1 | **The fading spike** | A wood of real props on the TV fades correctly and costs a known amount. Decides fade or silhouette |
| 2 | Format, loader checks, `WorldGround` | A hand-written region with props loads, and props block the party |
| 3 | The Props layer, export and warnings | A prop painted in the workspace reaches the file and the game |
| 4 | Drawing props, decals and tint | Props draw in the world and in a fight, with no regression in a battle's cost |
| 5 | Fading, if the spike passes | The party is never lost behind a tree |
| 6 | Ambient life | The world moves, and turning it off changes nothing else |
| 7 | Ashen Ford's draft | Woods, farms, town and burned bank, reviewed by eye |

Task 1 runs first because it's the only thing that could change the design.

**The user's part:** refining the draft in the editor, and judging the look from the sofa.

## 7. Risks

- **Fading costs draw calls.** Tall props in their own layer break batching, and a wood is mostly tall props. Task 1 measures it on the real GPU. A modest TV box is still unmeasured. If it's too slow, the silhouette replaces it.
- **Painting 19,200 tiles by hand is slow.** The draft does the bulk, and later chapters will show whether a scatter brush is worth adding. This is the decision to revisit first.
- **Props at block corners.** Painter's order held with the pack's art because the widest prop reaches 28 px each way. Tall props in their own layer change that, so it needs re-proving.
- **Scene file size.** A fourth layer adds to a scene that's already 660 KB.
- **The ground still has hard edges between materials.** Decals soften this but don't fix it, and fixing it properly would need new art.
