# Issue #69 milestone reports

- [A — AI hazard avoidance](milestone-a.md): code, 32,400 before/after simulation battles, performance, golden checks and the 16,200-case crash sweep.
- [B — environmental engine](milestone-b.md): default-off surfaces, reactions, tooltips, model and mutation checks.
- [C — AI uses reactions](milestone-c.md): conduction/fire/ice value, empty-water aims, Story ally protection and performance.
- [D — oil flask](milestone-d.md): the item, one-use fire coating, two approved loot placements and persistent rewards.
- [E — TV surfaces and animations](milestone-e.md): layered reactions, readable ignition, surfaced ground, and an actual TV motion reel. [Steam refinement](steam-refinement.md) slows its dissipation after user review.

- [F — phone forecasts and terrain effects](milestone-f.md): movement risk, secondary spread, conducted-unit previews and current-tile animation. [Enhanced glare](phone-glare-refinement.md) makes the terrain effects clearer after review. [Web strands](phone-web-refinement.md) replace the floating square shapes.

- [G — initial integration checkpoint](milestone-g.md): original comparisons and the earlier held cave/four-player concerns.
- [Completion review](completion.md): final Ford/cave tuning, five enabled encounters, 5,760 final comparison battles, visual audit, phone-bot forecast support and final validation. Includes the rejected experiments and their parameters.

## Current status — 2026-11-03 (UTC)

After user review and authorization, A–C landed on the overworld branch and are now in `main`. D–G are implemented on `environmental-tiles`, including the final balance pass for River Crossing, Ford Assault, Warlord Bridge, Forest Ruins and Crystal Cave. The shops/gold changes from PR #70 are merged into this branch, and oil has its agreed price of 10.

The user authorized the remaining tuning, polish and PR on 2026-11-03; the [plan addendum](../../plans/2026-11-02-tactics-environmental-tiles.md) records that authorization. The completion report is the current source for validation and remaining-scope status. The PR targets `main`.
