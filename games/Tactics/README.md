# ArcBox Tactics

ArcBox Tactics is a turn-based tactical battle game for the ArcBox party platform. `Tactics.Logic` is its rules engine: a deterministic, pure C# library (no clock, no `System.Random`, sorted collections only) that takes a battle state and a command and returns the new state and the events that happened, so every battle can be replayed from its seed. `Tactics.Logic.Tests` covers it, and `Tactics.Sim` balance-tests the content: it plays every encounter with sampled AI parties, AI on both sides, and writes a Markdown report (the latest is in `docs/superpowers/reports/`):

```
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 --seeds 20 --out <report.md>
```

The design is in [docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md](../../docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md). Third-party notices are in [THIRD_PARTY.md](THIRD_PARTY.md).
