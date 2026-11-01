# Ashen Ford, rebuilt to match the lore: design

- **Replaces:** the Ashen Ford terrain draft and the scattered dressing in `AshenFordDraft`, and goes further than the [world dressing design](2026-11-01-tactics-world-dressing-design.md), whose props, fading and ambient code it keeps.
- **The bar:** [mockup 02](../mockups/tactics/02-tv-explore-vote.png): warm dusk, lit windows, lamp posts, cobbles, a real square, townsfolk. It should look like that.
- **The lore:** `content/tactics/base/lore/regions.json`: "Golden-hour autumn turning cold. Woodsmoke, wet ash, a town that is pretending to be fine."

## 1. Why

The user judged the region awful, and they're right. Four things are wrong:

1. **There's no town.** Buildings are terrain blocks. The pack's town kit (`content/tactics/base/town`: pre-lit tavern, stores, smithy, chapel, cottages, houses, stalls, windmills, walls, gates, towers, bridges, notice boards, with shadows, night lights, chimney smoke and animated shop signs) isn't used at all.
2. **There's nobody there.** The game shows only a story beat's figure. The pack has presets for the mayor, innkeeper, priest, blacksmith, guards, farmers, fishers, merchants and children.
3. **The ground is wrong.** It's bright green, when a mile around the ford burned grey and the farmland beyond is tired autumn.
4. **The light is wrong.** The region runs at flat noon. The lore and the mockup are dusk.

Props were scattered by seed, with no place in mind and none of the story's objects: no tents, fires, lanterns, haystacks or wells.

## 2. What each place must be

| Place | Must read as |
|---|---|
| **Emberwick** | A walled town of warm stone with gates and towers. A cobbled market square with the well, stalls, a notice board and lamp posts. The chapel, The Last Coal (tavern), the general store and Tobin's smithy all face the square, with signs swinging and chimneys smoking. Cottages and houses on streets behind. |
| **The fields** | Autumn farmland outside the walls: fields, fences, haystacks, a windmill, a granary, cooling and thin. Grey starts to show near the ford. |
| **The road east** | A worn road with verges, signposts and carts, autumn trees thinning to dead ones. |
| **The burned ground** | A ring around the ford, about a mile by the lore: grey ash and scorch, dead trees, rubble, nothing green. |
| **The Ford Bridge** | The stamp, unchanged. Goblin banners and fires around it, the river steaming, reeds on both banks. |
| **The River Docks** | Jetties, boats, crates, fishers, lanterns for the Ash Market. |
| **The Skarrow Hills** | Cold rock and frozen mud up to a small camp: hide tents, a fire, goblin banners, thin goblins. Smaller than anyone expects. |
| **Old Maud's Hut** | One hut alone on the burned bank, hung about with charms and dead trees. |

**Every place gets a short hand-written list of what stands where.** Nothing decorative comes from a random scatter, though a wood's trees may be jittered within a hand-drawn outline.

## 3. What's new

### 3.1 Buildings

A region gains `buildings`: `{ id, variant, at }`, where `at` is the footprint's back corner.

- **Data** comes from `town/manifest.json`: footprint, `blocks`, height, attach points, `walkable`.
- **Walking:** `WorldGround` blocks the tiles the manifest says block, so a gate's middle tile and a bridge's deck stay walkable. The checks see them as they see props.
- **Drawing** follows the manifest's notes: 1 px = 1 texel at the map's scale, drawn in 32 px screen columns each sorted at the frontmost footprint tile, so units pass behind and in front. The shadow goes on the ground, under units. Animated stores and stalls loop. `overlays/smoke` sits on `attach.smoke`, and the sign named by `sign` on `attach.sign`. `lights` goes over the building at dusk.
- **A building hides the party** as a tree does: use the tall props' fade on the columns in front of a walker. If that reads badly on walls, use a silhouette for buildings only.
- **The painting workspace** places them as markers, like stamps, with an outline of the footprint. Tile painting doesn't suit multi-tile sprites.
- **Checks:** unknown ids and variants fail; a footprint off the region, on a void or on a stamp fails; overlapping footprints fail; a door (`attach.door`) with no walkable tile in front warns.

### 3.2 People

A region gains `people`: `{ preset, at, face, walk? }`, where `walk` is an optional short loop of tiles.

- **The TV draws them,** as sprites from the character presets, with their idle or walk frames. It's decoration: no phone change, no server state, and they don't block, so nobody can be walled in.
- **Named people stand where they live:** Halden on the square, Bree at The Last Coal, Aubrine at the chapel, Tobin at the smithy, Dagna at the gate. When a beat's figure is the same person, it replaces theirs, so nobody appears twice.
- **Townsfolk** fill the square and streets: merchants at stalls, a guard at each gate, farmers in the fields, fishers on the jetties, children, goblins at camp. About 25 in all.
- **Talking to them is out of scope.** It belongs to the NPCs and side quests project, which can use the lore's barks.

### 3.3 Light

Ashen Ford gets a dusk grade: a warm, low, slightly purple ambient in `BoardLighting`, stated for the region, plus real light from lamp posts, lanterns, fires and the buildings' `lights`. Battles at the ford share it. Check units and HUD stay readable, and that an in-place fight still has no seam.

The river steams: the `vent` prop's or the ambient art's steam along the water, thickest at the ford. Chimneys smoke.

### 3.4 Terrain

Rewrite the draft by place, in rings from the ford: grey ash, then scorched and thin ground, then tired autumn fields. Keep the stamp, beat spots' meaning, the walk times, the ford as the only crossing and every story check. Reduce the plain green to almost nothing. Buildings' block terrain goes.

## 4. Build order

1. **Rebase** on `ccr-18ad4510-q68nad`, which has the story runtime and its fixes.
2. **Buildings:** format, loader, ground, drawing. One tavern on a test region first, because column sorting and the fade are the risk.
3. **Light:** the dusk grade, on what exists, so everything after is judged in the right light.
4. **Terrain:** rewrite by place.
5. **Emberwick,** then each other place in §2's order. Look at each on the real TV before moving on.
6. **People.**
7. **Painting workspace:** building and people markers, so the user can refine it all.
8. **The whole walk,** Emberwick to the ford and every beat, on the real TV. Compare with mockup 02.

The user's rules hold: no rule, balance or phone contract changes, and no generated art without asking.

## 5. Done when

The party steps out of the inn onto a dusk square with people on it, and it looks like mockup 02. Every place in §2 is recognisable without being told its name. The frame rate holds, all checks pass, and the story still walks on both branches.
