# Issue #69 milestone reports

- [A — AI hazard avoidance](milestone-a.md): code, 32,400 before/after simulation battles, performance, golden checks and the 16,200-case crash sweep.
- [B — environmental engine](milestone-b.md): default-off surfaces, reactions, tooltips, model and mutation checks. No encounters opted in.

## Required A-only review and merge

Merge **through `dccfab9f6491011464dfbdb66be2c7bab76f4c70`** into `ccr-18ad4510-q68nad` after reading A's report. That commit includes only A and its validation. B follows it in separate commits on `environmental-tiles`.

The [agreed plan](../../plans/2026-11-02-tactics-environmental-tiles.md#milestones) explicitly says: **“A must land, and its sim report must be read, before C.”** C–G remain to follow that review point; no reaction AI, oil-flask placement, TV/phone additions or encounter opt-ins are included in these two milestones.
