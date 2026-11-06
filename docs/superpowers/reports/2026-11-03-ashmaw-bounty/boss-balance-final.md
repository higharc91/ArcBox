# Final Ashmaw balance: harder for four or more

The owner requested roughly 60% four-player wins at level 2, with XP grinding making the fight easier, then clarified: **“we just want it a little more difficult for 4+ players.”** The earlier 74.6% all-size proposal was never installed.

Only parties of four to six receive the stronger profile. One-to-three-member fights retain their original stat block, abilities and HP.

| Fighting members | HP | AC | STR | Maul on-hit damage |
|---:|---:|---:|---:|---|
| 1 | 32 | 12 | 17 | Two attacks, each 1d6 |
| 2 | 60 | 12 | 17 | Two attacks, each 1d6 |
| 3 | 90 | 12 | 17 | Two attacks, each 1d6 |
| 4 | 240 | 14 | 21 | Two attacks, each 1d6+7 |
| 5 | 320 | 14 | 21 | Two attacks, each 1d6+7 |
| 6 | 400 | 14 | 21 | Two attacks, each 1d6+7 |

The damage expressions are complete; Strength is not added again to the custom on-hit dice. Both profiles keep the same identity, approved art, sounds, roar, half-health phase and fire resistance. No cubs or new combat mechanics were added. `unitByParty` selects the authored profile before placement and phase resolution, preserving the named unit and original trigger tag. Omitted mappings leave older encounters unchanged; incomplete, empty and unknown references are rejected.

## Confirmed production simulations

Four-member parties: 24 compositions, all twelve classes represented, 20 seeds per level, Normal difficulty. The boss remains **level 2** throughout.

| Party level | Battles | Player wins | Win rate |
|---:|---:|---:|---:|
| 1 | 480 | 84 | 17.5% |
| 2 | 480 | 285 | **59.4%** |
| 3 | 480 | 397 | **82.7%** |
| 4 | 480 | 468 | **97.5%** |

Level-2 fights average 12.8 rounds, median 12. Five of 480 reached the 30-round cap and count as losses. This is slightly longer than the initial 8–12-round guideline; it prioritizes the owner's later request for a tough fight near 60% wins. The journal recommends level 2 without changing post-ford availability. Existing repeatable eligible battles still award XP; the boss does not scale up with the party's level.

The [1,920-battle four-player report](ashmaw-four-player-final.md) records the exact parties, seeds and reproduction arguments. These are AI-controlled results; equipment, rest benefits, racial traits and human tactics can change outcomes.

Five/six-member confirmation used six compositions and 20 seeds per level. At level 2, wins were 78.3% for five and 85.0% for six; at level 3, 97.5% and 93.3%. These larger parties receive the harder profile but retain an advantage from their additional members. See the [480-battle report](ashmaw-large-party-final.md).

The [1,020-battle smaller-party replay](ashmaw-small-party-final.md) matches **all nine original level/size cells exactly**. [Comparison proof](small-party-preservation.json). Regression tests also preserve the original smaller-party HP, AC, Strength and Maul dice, retain the full attack animation for both profiles, and verify that gaining levels strengthens heroes without increasing Ashmaw's level or stats.

The earlier six-phone full quest run proves the travel, chambers, proof and rewards flow using the draft combat numbers. The revised combat is verified by the production simulations above; that earlier live run is not presented as a full live playthrough of this stronger profile.
