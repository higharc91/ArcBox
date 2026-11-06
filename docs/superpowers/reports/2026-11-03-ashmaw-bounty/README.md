# Ashmaw bounty verification

Branch: `ashmaw-bounty`, based on merged #73. The quest and cave are implemented. The owner approved both actual TV motion previews, and the dedicated characters and portraits are installed in production. The owner retained level 2, requested about 60% four-player wins and clarified that only four-to-six-member fights should get harder. That group-only tuning is installed. No PR has been opened.

## Implemented behavior

The post-ford board leads to Dagna's bounty, a voted passage into Cinder Hollow, four forced encounters, a safe fire, proof collection and the return to Dagna. Both winning ford branches unlock the bounty. Losing or fleeing does not. The side adventure preserves the main chapter, regional cursor and quest state. The fisher's boy is alive at Maud's; the sliver crumbles without entering the shard economy.

The party receives 60 gold split among present members, 300 XP and a class-specific trinket for every campaign member, including away members. Replacement is explicit. A remade character retains the trinket, which becomes dormant for the wrong class. Private phones receive only their own gift; the TV presents the party's gifts. Reward sheets stop held walking, open once and stay dismissed across repeated snapshots.

The 56×44 cave has natural rock shelves, four exact battle stamps, two clearings reserved for #79, and no wandering arena between the Nest and the safe fire. Plain candles mark the mine. Old animal remains and muted dried blood collect near Ashmaw. These props neither block movement nor grant cover. Ambient light is darker while figure readability retains the existing floor. Small warm candle pools use the renderer's flicker. The existing eerie dungeon music accompanies cave ambience; a separate breathing layer plays near the living bear.

## Visual and audio evidence

Both concepts, the pixel direction and actual TV world/battle motion are owner-approved (“Approve both”). Every required action has eight real facings, with no mirrored weapons or static missing-direction substitution. Fixed canvas-padding normalization preserves ground anchors and rejects clipped frames. Bad anatomy, a long tail and a dropped pick were excluded through explicit curation; paid rejected takes remain archived. The generated candle animation added white smoke and was rejected in favor of the static candle plus light flicker. Production was captured without the art-review override. Repeating the approved installation was byte-identical.

- [Directional review and curation](directional-art-review.json)
- [Ashmaw's burning arena](ashmaw-burning-arena.png), [shake motion](ash-shake.gif)
- [Grisk's pick](grisk-pick.png), [attack motion](grisk-pick.gif)
- [Small phone reward](320x568-reward.png), [landscape reward](568x320-reward.png)
- [Stock goblin → Grisk opening → approved candle bark](grisk-stock-goblin-comparison.mp3)

All 1,443 voice clips are present. The [voice drift report](voice-check.json) has zero flags. Existing casts remain unchanged. Grisk's approved candle take is `bark_grisk_candle_1.f31d8679.ogg`, preserved exactly. It has weight 1 beside the weight-6 ordinary hit line; low-health and defeat barks take priority. All 15 new sound clips are imported with source/job provenance and peak/loudness processing recorded.

Scenario cost: concepts 39 CU, voices 52 CU and SFX 174 CU, **265 CU total**. PixelLab's observed account decrease is **338 generations** (554 → 216 remaining), including revisions, portraits, failed prop attempts and the rejected candle animation. The [ledger](../../../../tools/tactics-assets/pixellab/ashmaw/credit-ledger.json) separates observed usage from older partial receipt totals.

## Verification

- Final .NET suite after production art and group-only tuning: **6,191 passed, zero failed**, with a zero-warning solution build. [Log](hollow-final-idle-suite.log)
- Later presentation/data checks: **358 presentation and 20 pack/encounter checks passed**. [Log](hollow-final-demo-build-checks.log)
- After production art installation: **287 art/presentation and 71 content checks passed**, including three new checks for dedicated atlas/portrait resolution, all action facings and Grisk's lore art. [Log](hollow-production-art-checks.log)
- Web: **508 passed**; TypeScript and Vite build passed. The existing large flyover-chunk advisory remains; this is not a zero-warning Vite build. [Tests](hollow-web-delivery.log), [build](reward-popup-web-build-final.log)
- Reward layout: six viewports, including 320×568 and landscape, with no overlaps, overflow or off-screen controls. [Measurements](phone-reward-layout.json)
- Lore validation passed; Python tooling: **17 lore and 7 audio tests passed**.
- Cave export was byte-identical twice. Prop-atlas remapping preserved Ashen Ford's exported JSON byte for byte. [Export proof](cave-final-export-check.json)
- A real six-phone run completed Chapter I, accepted the bounty, entered the cave, won all four chambers, rested, collected proof, returned and verified 60 gold plus private class trinkets. [Log](bounty-e2e-six-bots.log)
- A four-phone parley regression reached the chapter card, recovering from an initial lost River Crossing. [Log](chapter-parley-final.log)
- Travel tests cover the real cave entrance/restart/return, plus saved interrupted votes, resume and end-session retention. Rendering demos write no campaign and do not substitute for the live flow test.

The final run uses two test modules at once to avoid contention in strict rendering microbenchmarks. A previous six-module run narrowly missed a 0.5 ms/frame limit; the unchanged benchmark passed independently, and the final complete run passed without relaxing the limit.

The container has no working audio device, so Godot falls back to its dummy driver and reports Ogg playback objects at shutdown. The same warning exists in the baseline preview; an attempted explicit cleanup did not resolve it and was reverted. No clean native-audio shutdown claim is made. Recorded clips are available for listening independently of that driver limitation.

## Balance findings and owner decisions

The campaign currently reaches this optional bounty at **level 1** after the ford (300 XP; level 2 starts at 1,000). The six-seat live run succeeded at that earned level. Four-seat attempts struggled; no XP, gold or flags were injected to turn a failed run into a success.

The [5,520-battle Hollow sweep](hollow-balance-normal-final.md) covers levels 1–3, sizes 1–4 and 20 seeds. The initial draft had level-2 four-member wins of Throat **99.2%**, Diggings **96.7%**, Nest **78.3%**, Ashmaw **100%**. This exposed the boss challenge gap. Its AI now actually uses its attacks; an earlier dodge-only defect was fixed before this sweep. The [solo level-2 Story sweep](hollow-balance-solo-story-final.md) won against Ashmaw in **85/100** runs, covering five healer/defender builds, not all twelve classes.

The paired trinket comparison uses identical 24 four-member parties, 20 seeds and levels 2/3 across River Crossing, Ford Assault and Chapter II's Forest Ruins. All twelve classes, including monk, appear. Overall wins rose **84.3% → 86.3%**; Forest Ruins at level 2 rose **37.7% → 45.6%**. Mean rounds rose 9.0 → 9.1 and capped battles 11 → 20. See the [comparison](trinket-comparison.md), [2,880-battle baseline](hollow-balance-trinket-before-final.md) and [2,880-battle equipped sweep](hollow-balance-trinket-after-final.md).

The issue reserves balance decisions to the owner. Keep 60 gold, 300 XP, twelve draft effects, draft monster numbers, no cubs, the surviving boy and dormant class-change behavior explicitly marked as defaults. The owner confirmed **“Keep the level-2 target”**, then requested roughly 60% wins for four players and clarified **“a little more difficult for 4+ players.”** The final profile yields **59.4% at level 2, 82.7% at level 3 and 97.5% at level 4** for four players. All nine smaller-party level/size cells match the original replay exactly. See the [final balance and scope report](boss-balance-final.md). No global XP thresholds changed.
