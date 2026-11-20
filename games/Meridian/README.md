# Derelict: ISV Meridian

A 3–6 player tile-exploration game with a mid-game twist. A salvage crew boards a silent starship and reveals it one
compartment at a time; partway through, the **Breach** picks a scenario, often turns one of the crew, and the second
half is an asymmetric race to each side's objective. The board is on the TV and every input is on a phone.

- Design: [docs/superpowers/specs/2026-11-13-meridian-design.md](../../docs/superpowers/specs/2026-11-13-meridian-design.md)
- Current plan: [docs/superpowers/plans/2026-11-13-meridian-m1-rules-core.md](../../docs/superpowers/plans/2026-11-13-meridian-m1-rules-core.md)
- Tracking issue: [#319](https://github.com/higharc91/ArcBox/issues/319)

| Project | What it is |
|---|---|
| `Meridian.Logic` | The rules: immutable state and one pure `Rules.Step`. No clocks, timers or views. |
| `Meridian.Logic.Tests` | xUnit tests for the rules. |
| `Meridian.Sim` | Bots play whole games and report balance. |

Run the tests with `dotnet test games/Meridian/Meridian.Logic.Tests`, and the simulator with
`dotnet run --project games/Meridian/Meridian.Sim -- explore --games 2000`.
