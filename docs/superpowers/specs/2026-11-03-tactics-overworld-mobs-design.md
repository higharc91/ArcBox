# Visible overworld mobs (#79)

Status: implemented on `overworld-mobs` from current main, including merged #74 and #77; implementation and verification complete. No PR until requested.

## USER DECISIONs

- Visible, avoidable roaming groups; contact with the leader on the same or four-adjacent tile starts an in-place fight. Followers do not trigger fights. Mobs never chase.
- Defeated groups remain absent until the next chapter. Losing grants no rewards and follows the existing recovery rule. No farming, default loot, or newly generated audio.
- Default XP is 25% of region story-fight XP (roughly 60–75 in Chapter I); gold is 5–15 per group, distributed among present players.
- Five groups in Ashen Ford: wolves, boars, kobold scouts, river slimes, and bandits gated by `ch1_ford_settled`. Two groups in Cinder Hollow: bats and kobolds. Preserve the safe route from the Nest to the campfire.
- Existing monsters first; lore-consistent variants are allowed if balance needs them. No new named characters.
- Sim each encounter at levels 1–3 and party sizes 1–4. Target 85–95% wins and 4–8 rounds. Report outcomes and full-sweep XP against chapter-end thresholds; do not silently change the owner's balance target.

## Implementation contracts

Group definitions live in manifest-listed `rules/mobs/<region>.json`: id, encounter, arena, patch, show, pace, flag gates and reward overrides. Arenas are explicitly painted stamps with matching 14×12 maps, exported twice with identical output. Dynamic map extraction is deferred.

Server-owned deterministic movement uses region/group/epoch seeds. Movement pauses for scenes, interaction panels, shops, quest logs and rests. Never step onto the party or its destination; proximity contact is checked after movement. Persist defeated groups through optional `WorldSave.MobsBeaten`, omitted when empty, and reset only on chapter transition.

Validation covers arena bounds, party spawn edges, patch connectivity and walkability; excludes roads, beats, spawns, recovery, people, interactables and required quest paths. Keep required routes traversable.

TV uses depth-sorted creature units, idle/walk clips, subtle hostile rings, existing entrance growls and a brief contact banner before the existing 5c walk-in. Send only visible mobs and movement legs through #77 deltas. Phone danger information is optional and absent without nearby mobs; add `walk-danger` and `walk-ambush` fixtures.

## Verification

Determinism/restart, both contact directions, follower exclusion, paused movement, victories/losses/chapter reset, invalid content, unchanged no-mob fixtures and packet budgets. Bot coverage includes defeating wolves, reconnect/restart, and Chapter I force/parley completion. Capture TV wandering, contact and battle. Complete all relevant suites and document existing environment limitations separately.

## Authored implementation

Seven groups and their stamped maps are listed in the [review report](../reports/2026-11-04-overworld-mobs/README.md). Each pays 75 XP per member under the existing battle convention, 10 gold divided among present members, and no loot. A full seven-group sweep adds 525 XP per member. This can raise an otherwise level-1 chapter-end party to level 2 when combined with all Chapter I side quests; the report gives both story branches against the current 1,000-XP threshold.

`MobActors` stores each actor's position, cursor and original seed. `MobAmbush` saves the one-second contact transition; reconnecting cannot cancel or duplicate it. `MobChapter` separates a genuine chapter advance from revisiting a region. These fields and `MobsBeaten` are optional. Empty mob/danger view fields remain absent. No fleeing action was added.

The route check reserves the whole roaming patch plus cardinal contact distance, including currently gated groups, then compares reachability with the unobstructed world. This deliberately preserves safe routes even at the worst possible roaming position. Named arrival spawns, ordinary spawns, beat markers, NPC home/walk tiles, interactables and road materials are protected. Arena party spawns must connect to a walkable edge. Existing map/region checks also validate each spawn and stamped tile.

The grey wolf coat shares the existing wolf sound family. TV rings use art-pixel radii, matching the normal unit renderer. The two cave arenas use Cinder Hollow's darkness and room tone. Dynamic extraction of battle maps and Chapter II content remain outside this issue.

Roaming stamps retain the region's natural terrain art, tint, decals and edge blending in both exploration and the in-place battle stage. They still use the same explicit map tiles, props, collision and stage bounds. This removes the rectangular arena-colour patches without repainting the world or changing the story ford's authored battle art. Renderer regression checks compare staged terrain faces with the surrounding world's faces.
