# Environmental tiles — completion review

This completes the remaining work for #69 after the [earlier G checkpoint](milestone-g.md). The user authorized the outstanding balance tuning, final art review and PR on 2026-11-03. A–C are already in `main`; this branch finishes D–G and integrates the shops/gold changes from PR #70.

## Final content

Five encounters enable reactions: **River Crossing, Ford Assault, Forest Ruins, Warlord Bridge and Crystal Cave**. Other encounters retain their previous behavior. The ford map and its overworld stamp remain unchanged; the forest's nine-cell material patch and the bridge's existing collapse trigger are retained.

Ford Assault now has Mother Skarrow, **two goblins, one archer and one stormcaller**. Mother has **66 HP**, up from 44. Mother and this encounter's shaman use a scoped **1d6 fire Cinder Bolt**, replacing their previous 2d6 version. The ordinary goblin shaman keeps its original ability. These changes reduce burst pressure on four characters while keeping the boss fight long enough. The stormcaller remains at **[9,7]**, **18 HP**, with **1d8 lightning Storm Hex**: one Action, tier 0, range 6, line of sight, no new scaling or resource cost. Rewards retain **300 XP, 120 gold and one oil flask**. Values live in `encounters/ford_assault.json`, `monsters/villains.json`, `monsters/goblinoids.json` and `abilities/monsters.json`.

Crystal Cave replaces one melee kobold with a fourth nest spider: **four spiders, four slingers and two kobolds**. The cave-only `rime_spider` copies the ordinary giant spider's stats and art, with **38 HP at level 7** (base level-1 HP remains 26). Ordinary giant spiders elsewhere are unchanged. Explicit `giant_spider.1`–`.4` identifiers preserve the first three identities. Four permanent, difficult web tiles occupy the existing `spider_nest` zone at round 1. Their existing **Dexterity DC 13, restrained until save** passive excludes the tagged nest spiders; difficult movement still applies. This uses the existing trigger and passive-condition rules. Values live in `monsters/rime-nest.json` and `encounters/crystal_cave.json`; the new monster file is listed in `manifest.json`.

The oil flask's price is **10**, now that shops/gold has landed. Its two approved loot placements remain the only placements, and starting kits have none. Wenna's merchant stock is unchanged. Merging the economy work preserves encounter/chest gold and the existing once-only loot behavior.

## Balance evidence

| Encounter | Level | Off wins | On wins | Mean rounds off / on | On capped losses |
|---|---:|---:|---:|---:|---:|
| river_crossing | 1 | 325/360 (90.3%) | 329/360 (91.4%) | 9.5 / 9.4 | 0 |
| ford_assault | 1 | 332/360 (92.2%) | 287/360 (79.7%) | 8.9 / 8.1 | 2 |
| forest_ruins | 3 | 317/360 (88.1%) | 318/360 (88.3%) | 10.6 / 10.6 | 4 |
| warlord_bridge | 5 | 323/360 (89.7%) | 328/360 (91.1%) | 10.9 / 10.9 | 0 |
| crystal_cave | 7 | 334/360 (92.8%) | 334/360 (92.8%) | 12.0 / 11.6 | 0 |

The off/on pair keeps each final encounter's enemies, starting webs and fuel definitions identical; only the reaction opt-in changes. Ford's final off/on delta is −12.5 percentage points. Cave wins are unchanged at level 7, while reactions shorten its mean by 0.4 rounds.

| Stress comparison | Party level | Off / on wins | Mean rounds off / on | Caps off / on |
|---|---:|---:|---:|---:|
| ford_assault | 1 | 92.2% / 79.7% | 8.9 / 8.1 | 2 / 2 |
| ford_assault | 3 | 100.0% / 100.0% | 5.6 / 5.2 | 0 / 0 |
| ford_assault | 5 | 100.0% / 100.0% | 5.2 / 5.2 | 0 / 0 |
| crystal_cave | 5 | 83.6% / 85.8% | 13.2 / 13.0 | 0 / 2 |
| crystal_cave | 7 | 92.8% / 92.8% | 12.0 / 11.6 | 0 / 0 |
| crystal_cave | 9 | 98.9% / 98.1% | 8.3 / 8.3 | 1 / 0 |

Targets apply at each encounter's intended level: **60–95% pooled wins and 8–12 mean rounds**. Each 20-seed cell contains six sampled parties at each size 4/5/6, Normal difficulty, no party item use, and a 30-round cap counted as a loss. Adjacent levels are stress comparisons. The 40-seed runs include the initial seeds 1–20 plus 21–40; they are an extension, not an independent additional 40 seeds.

Ford's 40-seed check gives **576/720 wins (80.0%)**, **8.0 mean rounds**, with **three capped losses**. Four-character parties win **62.5%**, compared with **46.7%** before tuning and **64.2%** in the pre-stormcaller original. Size five wins **84.2%**, size six **93.3%**. The unrounded mean passes the length band; CAPPED remains a separate reported diagnostic.

The first cave candidate that passed 20 seeds narrowly failed the larger sample: **95.1%** wins. It was rejected. The selected 38-HP variant wins **674/720 (93.6%)**, with **11.7 mean rounds and one capped loss**; four-character parties win **80.8%**. The complete merged-code 40-seed report is byte-identical to the exploratory report, confirming the economy merge is combat-neutral for this variant. Larger cave parties remain easier than four-character parties; the pooled gate does not imply equal difficulty at every party size. Raw reports preserve the class-band, rare-ability and capped-battle diagnostics rather than treating them as resolved by this content work.

All exploratory results, including rejected variants, are in [the tuning ledger](completion-data/experiments.md), with exact parameters in its JSON companion. [The earlier report](milestone-g.md#measurement) retains the original/off baselines for River, Forest and Bridge; those combat definitions were not retuned in this completion pass.

Raw final reports: [Ford on, levels 1/3/5](completion-data/ford-final-20.md), [Ford off](completion-data/ford-final-off.md), [Ford 40 seeds](completion-data/ford-final-40.md), [cave on, levels 5/7/9](completion-data/cave-final-on.md), [cave off](completion-data/cave-final-off.md), [cave 40 seeds](completion-data/cave-final-40.md). The final Ford/cave comparisons contain **5,760 battles** (three on/off level cells and one 40-seed intended-level extension per encounter); the seed overlap is described above.


### Final reaction tallies

| Encounter | Reactions / battle | Friendly conduction damage / battle | Burning tile-rounds / battle | Surface deaths |
|---|---:|---:|---:|---:|
| river_crossing | 2.7 | 0.0 | 17.3 | 44 |
| ford_assault | 8.2 | 0.0 | 56.2 | 43 |
| forest_ruins | 0.5 | 0.0 | 2.7 | 3 |
| warlord_bridge | 1.4 | 0.0 | 5.0 | 4 |
| crystal_cave | 5.5 | 0.0 | 23.2 | 11 |

These are the 360-battle intended-level cells. Conduction counts actual same-side/self HP and temporary HP lost, with no overkill; burning counts unique tile-rounds; deaths count terminal surface deaths, not Downs.

## Visual and interaction review

The existing approved sprites, effects and sounds already suit the stormcaller and cave brood, so no generated replacement was needed. The final review covered:

- **All nine encounter boards**, rendered from their real enemy definitions, intended party level and starting zones via `--tactics-demo=encounter:<id>`. Checked unit scale, portraits, ground contact, the dry forest patch and the cave's native webs. [Encounter sheet](completion-art/encounters.jpg), [full cave view](completion-art/cave-final.png).
- **89 overworld review positions:** one placement for each of 20 NPC presets and 69 prop families in Ashen Ford. Checked buildings, occlusion/fading, foliage, props and animal scale in their actual surroundings. The 91 named captures include two later views; moving fauna can leave a camera crop. Contact sheets: [1](completion-art/world-1.jpg), [2](completion-art/world-2.jpg), [3](completion-art/world-3.jpg), [4](completion-art/world-4.jpg), [5](completion-art/world-5.jpg), [6](completion-art/world-6.jpg).
- **TV reaction progression:** conduction, freezing, thawing, ignition, spreading, dousing, oil and burning webs through the ordinary beat renderer. The [33-second captured reel](completion-art/environment.mp4) and [timed frames](completion-art/motion.jpg) retain the approved slow **2.4-second steam dissipation**. Steam continues fading beyond the short action beat.
- **All six phone terrain treatments:** anchored silk webs, flame embers, ice, water glare, oil sheen and scorched ground. [Surface sheet](completion-art/surfaces.jpg). Browser checks cover risk confirmation, spread/cursor visibility, taps through decorative layers, clearing effects, reduced motion, 320px portrait and 844px landscape layouts; [results](completion-data/phone-browser-check.json).

The review found overly tall spell cards. Browsing now limits previews to three lines; selecting a spell displays its full description before the separate Use confirmation. [Selected description](completion-art/phone-selected-detail.png), [narrow phone](completion-art/phone-320.png), [landscape](completion-art/phone-844.png), [risk](completion-art/phone-risk.png), [spread](completion-art/phone-spread.png). No tooltip information or combat behavior was removed.

The VM uses the existing OpenGL/Dummy-audio wrapper for Godot. Its unsupported VSync/AA notices and eight-object shutdown warning also occur in the earlier E capture; they are not new game errors. MovieMaker recording speed is not a runtime FPS benchmark. The web build retains the existing 537.65 kB flyover-chunk advisory.

## Four-player chapter and final checks

`ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force ARCBOX_PHONES=4 ARCBOX_LINE=1 tools/tv-e2e.sh --skip-build` **passed**, using the rebuilt server/host and four bot phones over the real hub. The fresh campaign used the default **fighter, cleric, rogue and wizard** party. It lost twice, recovered, then won Ford Assault on the third attempt. Each character received **300 XP**, and the party received exactly **one oil flask**; neither loss duplicated the rewards. The saved purses contain 80 gold per character, including the chapter's other funding ([reward snapshot](completion-data/chapter-four-rewards.json)).

The aftermath continued through Old Maud, the Ember, docks, Kindler and Stranger to **Chapter I · The Bridge at the Ford**. All **94 story lines** were said once; **29 shots** were saved, and the bots walked **four steps after the card**. The route harness replanned once after the post-battle world placement. The script exited **0**, with no TV errors. [Check summary](completion-data/chapter-four-check.txt), [phones](completion-data/chapter-four-phones.log), [TV log](completion-data/chapter-four-tv.log).

The actual TV run also captured the stormcaller's lightning conducting through the ford, alongside ignition in the stamped battle. These are runtime captures, not concept renders: [battle placement](completion-art/chapter-ford.png), [Storm Hex conduction](completion-art/chapter-conduction.png), [fire](completion-art/chapter-fire.png), [chapter card](completion-art/chapter-card.png), [walking afterward](completion-art/chapter-walking-after.png).

An additional fresh four-player run with **barbarian, cleric, fighter and monk** also **passed** through the card and four subsequent walking steps, winning its first assault. All **94 lines** were said once, with **25 shots**, no TV errors and exit **0**. Its VM capture used a 15 FPS cap while the default-party run was also active. [Additional check](completion-data/chapter-melee-check.txt), [phone commands](completion-data/chapter-melee-phones.log), [TV log](completion-data/chapter-melee-tv.log).

The original phone bots lost three complete assaults and were stopped before a fourth finished ([commands](completion-data/chapter-unaware-phones.log)). A warning-aware version also lost three ([commands](completion-data/chapter-camping-phones.log)): its cheap safe escapes scattered the party away from its objective. The final harness keeps safe escapes directed toward the shared target and pursues explicit defeat objectives immediately. It uses phone-visible forecasts/positions and the same objective IDs it already used for target priority; no hidden state or combat bonus is supplied. A separate check of the exact default fighter/cleric/rogue/wizard party under the game AI wins **15/20 (75%)**, with 7.6 mean rounds and no caps ([seed results](completion-data/classic-party.json), [probe source](completion-data/classic-party-probe.cs.txt)). This is a diagnostic party check, not a replacement for the larger sampled-party gate or the real TV run.

The final C# and content tree passes **5,927 .NET tests**, with zero failures or skips ([log](completion-data/completion-tests.log)). The web suite passes **489 tests**, and the production web build and Debug server/host builds pass. The final phone-bot policy passes **six Node regression checks** plus a syntax check; its JavaScript-only refinement does not change the tested C# or content. [Bot checks](completion-data/bot-hazard-tests.log), [web tests](completion-data/final-web-test.log), [web build](completion-data/final-web-build.log), [server build](completion-data/final-server-build.log), [host build](completion-data/final-host-build.log).

All six legacy replay/event JSON files remain byte-identical to `357df1c0`; the additional river/ford/storm replay covers real Storm Hex conduction, freezing and dousing, including save/load determinism. [Legacy comparison](completion-data/legacy-goldens.json). Existing non-environment phone fixtures remain unchanged.

The final source fingerprints and validation summary are in [completion-validation.json](completion-validation.json). No additional #69 implementation step remains. The deliberately excluded lava tuning and burn-triggered bridge collapse remain outside the agreed issue scope.
