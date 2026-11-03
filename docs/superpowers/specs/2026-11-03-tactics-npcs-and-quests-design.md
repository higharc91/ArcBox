# NPC conversations and side quests

Implemented for [issue #73](https://github.com/higharc91/ArcBox/issues/73) on `npcs-and-quests`. The branch integrates `rest-and-recovery` and `overworld-issue-board`; these prerequisite merges do not update main. See the [NPC brief](2026-11-03-tactics-ashen-ford-npc-brief.md) for biographies and secret unlocks, and the [validation report](../reports/2026-11-03-npcs-quests/README.md) for evidence.

## USER DECISION defaults

These are the defaults authorized in issue #73, retained for owner review.

| ID | Default used |
|---|---|
| Q1 | 100 XP per quest. |
| Q2 | Gold range 20–40; the grain quest pays 30. Other quests have their own item/story reward mix. |
| Q3 | Items go to the conversation starter; first present character in member order is the fallback. |
| Q4 | Away members receive XP and the party level-up check. |
| Q5 | Failure requires an explicit authored choice. No time limits. The three initial quests do not force failure. |
| Q6 | Quest giver/turn-in markers appear only when a reachable action in the matching conversation entry is eligible. |
| Q7 | Everyone reads privately; only the current leader opens and controls the shared TV journal. |
| Q8 | First eligible bark, then rotation across eligible lines; persisted counters. |
| Q9 | One cast per townsfolk preset; people share that preset's cast. |
| Q10 | One saved pin. |
| Q11 | Starting a quest auto-pins only when no quest is pinned. |
| Q12 | Waypoint at the current stage's visible target, with an edge arrow; no route line. |

## People and barks

`RegionPerson` adds optional `id`, `character`, `requires`, `unless`, `bark` and `voice`. Godot person markers export these fields. Explicit IDs remain stable across edits; legacy entries retain deterministic fallback IDs. Null/default fields are omitted. Ashen Ford has 51 people, including the gated Rook at the docks. The scene was exported twice with identical bytes.

People gates apply on the server and TV. Goblin warriors leave after either **won** ford ending; Mother Skarrow leaves only after force. Main-story choice flags are already set before the battle: presence masks those flags until the corresponding beat is won, preserving the existing story save while keeping the people after a loss. Departures fade over 0.35 s; arrivals wait until off-screen or behind the party and never appear on the leader's tile.

Everyone without a currently eligible conversation uses an instant town bark. Ten unnamed presets each have at least three unconditional lines and conditional reactions. Named Mother Skarrow, Lark and Dagna have town barks reusing their existing recordings. The server persists rotation counters, sends one attributed caption and voice event, and leaves the exploration phase unchanged. The speaker faces the party and pauses their idle movement while speaking. A bark's timer clears only its own note.

## Conversations and story hooks

Scenes can omit `beat`. Beatless scenes are retrieved by ID and excluded from main-beat scheduling. `lore/conversations/*.json` adds a character and ordered `entries` (`node`, `requires`, `unless`) to the scene graph. The first matching entry starts. Halden, Bree, Aubrine, Tobin, Pip, Corvin, Celandine and Rook have conversations; other named people use barks.

The existing 5d story runner handles lines, votes, party-picked checks, presence holds, resuming, End session and restart. A saved person cursor retains the underlying main-story cursor and starter, so a conversation cannot consume the pending ford battle. A selected check and rolled result survive reload. Service options return to exploration and open the existing inn/temple service rather than a second rest engine. Options support quest actions, gold, item giving and service IDs. Interactable `scene` hooks use the same beatless plumbing and retain their selected hook through gates changing at completion.

## Quest state and rewards

`lore/quests.json` contains stage flags, short objectives, history, optional targets, endings and rewards. State is derived from story flags, choosing the furthest matching stage; endings take precedence. No duplicate quest progression is saved. The optional saved quest fields are `WorldSave.QuestsRewarded` and `PinnedQuest`. Existing saves without them still serialize unchanged.

Every save passes through the central quest reward preparation step. Completion flags, reward ledger, split present-player gold, starter bag items, all-member XP, level-ups and story rewards are written together. The ledger prevents a restart, duplicate action or re-entry paying again. A new quest auto-pins only when the pin is empty. Completion/failure keeps the result on the tracker for seven visible walking seconds, then unpins. Notices queued during a conversation start their display timer only after returning to walking.

| Quest | Route and lore protection | Reward |
|---|---|---|
| Where the Grain Went | Granary sack → Ash Market tally → quiet account with Halden or a Persuasion/Intimidation confrontation with Celandine. Halden deflects about his secret before his confession. Celandine's writer-only secret remains unrevealed. | 30 gold, 100 XP, accounted story flag |
| A Cold Forge | Covered charcoal note at docks or a referral to gated Rook. Corvin offers a check about the cold. This does not create a real ember before the main-story unlock. | Oil flask, 100 XP, story outcome |
| The Guard's Whistle | Annet's grave; leave the whistle or bring it to Pip. Reactive Pip barks, without her Chapter 4 memory reveal. | 100 XP and story outcome |

All three have tests after force, parley and a ford loss. Side content never sets main-story choice flags. Secret conditions may read main-story unlocks; authoring validators reject main-story assignments and missing entry nodes, scenes, targets, items, services and unattainable stage flags. Targets are checked against their region, including tile bounds. Python and runtime loading enforce the same side-content rules; cross-pack world checks are skipped only for isolated test lore with no rules manifest.

## Journal and phone contract

The server checks the leader for `questLog { action: open | close | select | pin | unpin, quest? }`. Opening stops movement. Selection mirrors to the TV; closing, lead changes and disconnection close the shared book. Open/selection are ephemeral and restart closed. The saved pin survives lead changes and restarts. Other phones can browse privately without changing the TV. Only active quests can be pinned. The banner supports a 600 ms leader long-press to pin; tapping opens details.

Optional exploration fields carry the tracker, eligible markers, speech, seven-second notices and the shared journal only when relevant. Private view history contains only reached stages and known endings. A waypoint resolves through currently visible people and discovered, eligible things; absent people and unfound things never acquire coordinates. No quest means no new optional fields. Legacy phone fixtures remain byte-identical.

The TV uses a procedural two-page parchment spread with leather framing, seals, bookmark, giver portrait, history and rewards. Cinzel/Alegreya type and the existing palette match the story UI. Book transitions take 0.28 s. The tracker hides in scenes/battles. The waypoint has a dark halo and an off-screen arrow. Phones use parchment cards under a leather header, explicit ink contrast and large controls. Walking keeps party information in a separate scroll region above fixed actions and movement controls. Battle and encounter lists likewise scroll separately from their action row, preventing the overlap found in the phone audit.

Art remains procedural: [prompt manifest](2026-11-03-tactics-npcs-quests-art.json). Sound keys resolve to existing assets: quest_start→secret_found, quest_update→page_turn, quest_complete→ui_level_up, talk_start→ui_menu_open, questlog_open/close→page_turn, questlog_pin→ui_select. Nothing new was generated for art or SFX.

## Voice and verification

All new spoken scene and bark lines have rendered Ogg clips, delivery directions and registered cast themes. Existing named casts are unchanged. The [voice plan](../../../tools/tactics-lore/VOICE-PLAN.md) records actual casting, rendering, fallback limits and drift checks. Paid Scenario renders, including superseded takes, are retained by job/asset ID in `tools/tactics-lore/voice-assets.json`; total 114 renders, 220 CU. Nine additional town barks reuse existing matching captions/casts and recordings at no generation cost.

Run the commands in [the report](../reports/2026-11-03-npcs-quests/README.md) for unit coverage, the 264-case phone audit, the Godot NPC demonstration and real TV/phone quest plus Chapter 1 playthroughs. The delivered slice excludes companions, romance, timed/multi-region quests and path lines.
