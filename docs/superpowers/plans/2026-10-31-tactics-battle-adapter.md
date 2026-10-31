# ArcBox Tactics battle adapter: implementation plan (milestone 3a)

- **Spec:** [`../specs/2026-10-31-tactics-battle-adapter-design.md`](../specs/2026-10-31-tactics-battle-adapter-design.md)
- **Branch:** `ccr-18ad4510-q68nad`, stacked on the rules-engine branch (`ccr-472d8660-sxpjlj`), so the adapter merges after the engine.

**Global constraints** (from the rules-engine plan; they still apply):
- `Tactics.Game` does no I/O and never reads the clock: it uses `IGameContext` and `ICampaignLease`.
- Every engine change ships with tests.
- `dotnet build ArcBox.slnx` has 0 warnings; `dotnet test ArcBox.slnx` is green after every task.
- Follow the content and serialisation rulings in the rules-engine progress tracker ("Binding additions").

## Tasks

| # | Task | Files | Done when |
|---|---|---|---|
| 1 | **`Battle.Hand`** | `Tactics.Logic/Rules/Battle.Control.cs`, tests | A unit's controller can be swapped. Reactions follow the new controller. Non-party units are refused |
| 2 | **`Choices.For`**: what a unit can do this turn | `Tactics.Logic/Rules/Choices.cs`, tests | Reachable tiles and costs, usable abilities and items with legal targets, odds and tooltips. Every option passes `Battle.Check` |
| 3 | **`Tactics.Game` skeleton**: manifest, campaign document v0, `TacticsCampaignRules` | `games/Tactics/Tactics.Game*`, `ArcBox.slnx` | The document round-trips, `CardFor` covers both cases, a new campaign is valid |
| 4 | **Muster and Encounter pick** | `TacticsGame.cs`, `Phases/*`, views | Class picks are made, absent members get role-filling defaults, the VIP picks an encounter and the battle starts |
| 5 | **The battle loop**: AI pacing, player turns, refusals | `Phases/BattlePhase.cs` | An all-AI battle finishes. A player's commands apply. A refusal toasts. `shown` shortens the pacing, and the fallback timer prevents stalls |
| 6 | **Timers, reactions and seats** | same | The 60 s timeout is finished by the AI with `AiFinished`. The 10 s reaction prompt falls back to `Ai.React`. Seat drop and return use `Battle.Hand` |
| 7 | **Persistence and resume** | `Campaign/*`, `TacticsGame.Resume` | The document saves after every command. Resuming mid-battle and mid-reaction matches an uninterrupted run. A different hash restarts the battle |
| 8 | **Results** | `Phases/ResultsPhase.cs` | XP and injuries are applied, party level-up works, there's a checkpoint, and Next battle / End session both work |
| 9 | **Server registration** | `src/ArcBox.Server/Games/Tactics/*` | The server starts with the game listed, and the packs load once at startup |
| 10 | **Phone module** | `web/play/src/games/tactics/*`, contract fixtures | Every screen renders from its C# fixture. The local cursor and target picker work. Vitest is green |

Milestone 3b (the Godot TV scene) gets its own plan once 3a is merged and has been playtested with the phones.

## Progress

| # | Status | Commits |
|---|---|---|
| 1 | done | `1135261` |
| 2 | done | `1135261` |
| 3 | done | `fc0c762` |
| 4 | done | `fc0c762` |
| 5 | done | `fc0c762` |
| 6 | done | `fc0c762` |
| 7 | done | `fc0c762` |
| 8 | done | `fc0c762` |
| 9 | done | `eaa55b4` |
| 10 | done | `bb80664` |

**Not covered yet:** a playtest on real phones.

**Known limits:**
- **Turn view size.** Tile targets that affect no one are sent as bare `[x, y]` tiles (`PhoneChoices`). The largest level-10 kit then comes to 23 KB (sorcerer; the druid went from 45 to 15 KB). `ViewSizeTests` keeps every class under 32 KB, half the platform's 64 KB warning.
- **The phone map** draws the battlefield's terrain from the content pack (`web/play/src/games/tactics/map.ts`), as the dev board does.
