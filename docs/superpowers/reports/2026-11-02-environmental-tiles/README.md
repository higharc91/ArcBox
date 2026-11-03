# Issue #69 milestone reports

- [A — AI hazard avoidance](milestone-a.md): code, 32,400 before/after simulation battles, performance, golden checks and the 16,200-case crash sweep.
- [B — environmental engine](milestone-b.md): default-off surfaces, reactions, tooltips, model and mutation checks.
- [C — AI uses reactions](milestone-c.md): conduction/fire/ice value, empty-water aims, Story ally protection and performance.
- [D — oil flask](milestone-d.md): the item, one-use fire coating, two approved loot placements and persistent rewards.
- [E — TV surfaces and animations](milestone-e.md): layered reactions, readable ignition, surfaced ground, and an actual TV motion reel. [Steam refinement](steam-refinement.md) slows its dissipation after user review.

- [F — phone forecasts and terrain effects](milestone-f.md): movement risk, secondary spread, conducted-unit previews and current-tile animation. [Enhanced glare](phone-glare-refinement.md) makes the terrain effects clearer after review. [Web strands](phone-web-refinement.md) replace the floating square shapes.

## Current status — 2026-11-03 (UTC)

After user review and authorization, A was merged through `dccfab9f`, then **B/C were merged and pushed through `ea90c2e2`** into `ccr-18ad4510-q68nad`. D, E and F are implemented in separate commits on `environmental-tiles`. No encounter has opted in.

The [agreed plan's](../../plans/2026-11-02-tactics-environmental-tiles.md#milestones) A-before-C review/landing gate is complete. G remains: encounter opt-ins, Ford Assault’s lightning shaman and balance simulations. The oil price of 10 awaits the separate shops/economy branch. No PR has been opened.
