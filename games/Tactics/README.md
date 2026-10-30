# ArcBox Tactics

ArcBox Tactics is a turn-based tactical battle game for the ArcBox party platform. `Tactics.Logic` is its rules engine: a deterministic, pure C# library (no clock, no `System.Random`, sorted collections only) that takes a battle state and a command and returns the new state and the events that happened, so every battle can be replayed from its seed. `Tactics.Logic.Tests` covers it, and `Tactics.Sim` will balance-test the content.

The design is in [docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md](../../docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md). Third-party notices are in [THIRD_PARTY.md](THIRD_PARTY.md).
