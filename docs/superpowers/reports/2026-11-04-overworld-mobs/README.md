# Visible overworld mobs — issue #79

Branch: `overworld-mobs`. Seven optional groups inhabit matching, explicitly authored 14×12 arena stamps. All art and sounds are existing approved assets; no generated art, audio or paid generations were needed.

| Region | Group | Arena origin | Representatives | Gate |
|---|---|---|---:|---|
| Ashen Ford | Wolves in the Pines | 8,32 | 1 grey wolf | Always |
| Ashen Ford | Rooting Boars | 35,27 | 1 boar | Always |
| Ashen Ford | Ash Market Scouts | 119,74 | Kobold and slinger | Always |
| Ashen Ford | River Slimes | 96,76 | 1 slime | Always |
| Ashen Ford | Road Bandits | 141,63 | 1 bandit | `ch1_ford_settled` |
| Cinder Hollow | Hollow Bats | 20,2 | 1 bat | Always |
| Cinder Hollow | Off-Shift Diggers | 20,14 | 1 kobold | Always |

One representative can stand for several combatants, as specified in the brief. Existing eight-facing sprites retain their world scales. Grey wolves use the ordinary animal coat, rather than the green wildshape coat. Roaming-only solo and group profiles reuse their species' approved art; original story and wildshape monsters retain their stats.

## Behavior and placement

The server moves groups within connected patches at 2.5-second intervals, without chasing. Region/group/epoch seeds and saved cursors reproduce movement after a restart. Only leader contact starts the fight. Contact halts walking, saves the pending ambush and presents the TV notice and phone ambush screen before the existing in-place walk-in. The leader sees a small optional danger chip within four tiles. Mobs are sent only within a 20-tile area and use movement legs and host patches.

Scenes, shops, rests and the journal pause movement. The journal sends a quiet keepalive while read, preventing the leader's silence lease from closing it. With no connected phones, roaming clocks stop. Existing creature sounds announce first appearance. Ordinary battle music and location-appropriate ambience accompany the seven fights; standalone arenas also retain their region's lighting.

Wins give 75 XP to each member under the existing battle-XP convention, 10 gold split among present members, and no loot. Defeated groups persist through reconnects, restarts, rests and region travel. Losses give no rewards and use existing injury/recovery rules; the group remains. Entering a later chapter clears defeated groups. Mob encounters cannot be selected directly from the developer battle list to bypass persistence.

Both scene exports are byte-identical on repeated exports. Validation excludes roads (including cobble, planks and flagstone), people and their walking tiles, things, beats and all spawns. It blocks every possible mob tile plus cardinal contact distance and proves the remaining world still reaches every previously reachable tile outside that danger area and every interactable. The boar patch was reduced to two safe tiles after the wider candidate obstructed access to the farms. The cave's optional side chamber leaves the set fights and route to the safe fire accessible. Map props own their stamped area to avoid duplicate drawing.

## XP impact

`rules/xp.json` uses thresholds 0 / 1,000 / 2,000 / … . XP is awarded in full to each party member, matching story battles; gold is divided.

| Sweep | Extra XP per member | Extra group gold |
|---|---:|---:|
| Five Ashen Ford groups | 375 | 50 |
| Two Cinder Hollow groups | 150 | 20 |
| All seven | 525 | 70 |

The main force fight gives 300 XP; parley gives 250. Story-only completion plus all seven groups therefore totals 825 / 775 XP respectively: both remain level 1. Completing the three 100-XP town side quests and the 300-XP Ashmaw bounty adds another 600. Those full Chapter I routes total 900 / 850 without roaming groups and 1,425 / 1,375 with all seven: **the full optional sweep can move an otherwise level-1 chapter-end party to level 2**. The five Ford groups alone also cross that threshold with all side quests (1,275 / 1,225). No XP thresholds or story rewards were changed. Groups do not respawn in the same chapter.

## Placement captures

[Open all seven placements](gallery.html). These are normal-renderer gallery views of the actual authored locations. The live wolf flow is recorded separately below.

| Group | TV capture |
|---|---|
| Wolves in the Pines | [View](wolves.png) |
| Rooting Boars | [View](boars.png) |
| Ash Market Scouts | [View](scouts.png) |
| River Slimes | [View](slimes.png) |
| Road Bandits | [View](bandits.png) |
| Hollow Bats | [View](cave-bats.png) |
| Off-Shift Diggers | [View](cave-kobolds.png) |

The final pass removed the mismatched arena terrain:

| Before | After |
|---|---|
| ![Arena terrain before](arena-before.png) | ![Natural terrain after](scouts.png) |

## Review and reproducibility

- [Live wolves clip](wolves-live.mp4): actual TV driven by bot phones; wandering, ambush, stamped fight and victory. [Battle](wolves-battle.png), [victory](wolves-victory.png).
- [Phone danger](phone-danger.png) and [ambush](phone-ambush.png). Both screens were rendered at 320×568, 390×844, 412×915, 844×390, 768×1024 and 1024×768; [bounds audit](phone-audit.json).
- `--world-demo=mobs` renders all seven placements through normal host views without writing a campaign.
- `ARCBOX_MOBS=1 ARCBOX_RESTART=1 tools/tv-e2e.sh --skip-build` uses Story difficulty and seed 76 for a reproducible UI flow, as the existing chapter harness does. `ARCBOX_DIFFICULTY` can override it; gameplay defaults and the Normal simulation are unchanged. It checks wandering, phone reconnect, server restart with living mobs, ambush, winning rewards and removal.
- `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force|parley tools/tv-e2e.sh --skip-build` verifies the existing chapter routes still reach the card while avoiding mob danger.

The [original all-level report](balance.md) is retained as a historical baseline. The owner subsequently confirmed **fixed enemies so XP gains help**. The follow-up balance pass therefore targets **85–95% wins and 4–8 rounds at level 1**, sizes 1–4, and measures levels 2–3 as progression checks. It uses separate solo/group profiles, party-size HP, and a roaming-slinger combat profile; original story monsters, rewards and XP thresholds are unchanged.

The [combined confirmation](balance-fixed-enemies.md) covers **24,150 battles**: all seven groups, levels 1–3, party sizes 1–4, six sampled compositions per size (five eligible solo classes), and fifty seeds. **All 28 level-1 cells meet both targets:** 87.3–95.0% wins and mean durations of 4.10–7.61 rounds. The original level-1 baseline met both targets in only 6 of 28 cells.

| Party level | Battles | Player wins | Win rate | Mean rounds |
|---|---:|---:|---:|---:|
| 1 — arrival target | 8,050 | 7,389 | 91.79% | 5.83 |
| 2 — progression | 8,050 | 8,015 | 99.57% | 5.17 |
| 3 — progression | 8,050 | 8,046 | 99.95% | 4.85 |

Enemies retain identical stats as the party levels up; a regression check verifies this for every group and all six supported party sizes. Lower solo burst damage and greater endurance replace short, swingy fights. Group profiles keep larger parties challenged without changing the original story/wildshape monsters. Roaming slingers now reposition out of blocked lines of sight; a deterministic regression case covers the former standoff.

Seven level-1 autoplay fights (0.087% of the level-1 sample) reached the 30-round cap, primarily repeated AI revives; all count as losses. Levels 2–3 had no caps. The report flags them separately, so they are not hidden by the averages. These results describe sampled AI parties with no items, rather than a guarantee for every human party or class composition.

Candle stubs in the kobold side chamber preserve walkability and cover. [Export hashes](exports.json) record the repeated scene exports. The simulation's applicable assessment is the 85–95% / 4–8-round mob table; the standard simulator's story bands remain afterward for diagnostics. The level-1 target and fixed-enemy progression follow the owner’s explicit confirmation.

The cloud TV uses Mesa software rendering and a dummy audio device. Captures prove appearance and flow, not physical-TV frame rate or speaker output. The existing phone build's REDLINE flyover chunk advisory is unrelated to these changes.

Roaming stamps retain the region's natural terrain art, tint, decals and edge blending in both exploration and the in-place battle stage. They still use the same explicit map tiles, props, collision and stage bounds. This removes the rectangular arena-colour patches without repainting the world or changing the story ford's authored battle art. Renderer regression checks compare staged terrain faces with the surrounding world's faces.

## Final verification

- Solution builds with **0 warnings and 0 errors**; the final clean renderer build also has zero warnings.
- The solution run exercised 6,357 tests. All modules passed except one server-fixture route assumption; after correcting that fixture, the **entire 425-test server suite passed**. The final **65 stage-renderer tests passed**, including the three new natural-terrain comparisons. These reruns close the failures from the earlier runs; [verification record](verification.json).
- **517 phone tests pass**; the production phone build succeeds. Both new screens were visually checked at six viewport sizes with no horizontal overflow or out-of-bounds controls.
- Both authored scenes export twice identically with zero warnings.
- Both complete Chapter I branches reach their card and return to walking: [force](chapter-force.png), [parley](chapter-parley.png).
- The final live wolf run verifies wandering, phone reconnect, server restart with living wolves on screen, saved ambush, victory, 75 XP/member, 10 total gold, and the defeated sprites staying absent. The clip uses that final run and its corrected terrain/rings.
- The original Normal sweep remains available as historical evidence; the fixed-enemy follow-up report records the new level-1 balance and level-2/3 progression results.

## Balance follow-up validation

The balanced wolf flow passed again through the real server, TV, phone reconnect and server restart, including rewards and removal. The clip and battle/victory captures now show this balanced run. All **1,287 logic tests, 725 game tests and 135 sound checks pass**. This includes the report assessments, fixed-stat invariant and former slinger-cover standoff. Solution and clean test builds have zero warnings and zero errors; see the [verification record](verification.json). No phone, terrain, sprite, reward or XP-threshold changes were made in this follow-up.
