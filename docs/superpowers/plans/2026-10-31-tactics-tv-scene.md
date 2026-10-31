# ArcBox Tactics TV scene: plan (milestone 3b)

- **Spec:** [adapter design](../specs/2026-10-31-tactics-battle-adapter-design.md), milestone 3b, and §4.3 / §8.
- **Needs:** a machine with Godot 4.7.2 .NET, for anything that has to be looked at ([`docs/tv-host.md`](../../tv-host.md)). The code compiles in CI (`dotnet build ArcBox.slnx` builds `src/ArcBox.Host`), but nobody has seen it draw. That's why this milestone wasn't started in the overnight session.

## What the scene gets

- **`TacticsHostView`** (`games/Tactics/Tactics.Game/TacticsGame.Views.cs`):
  - the phase, banner and notice;
  - in Battle: the map id, the units (sprite key, tile, HP, life, conditions), the turn order and the active unit, and the last 20 **beats** with their sequence numbers and events;
  - in Muster, Encounter pick and Results: those screens' data.
- **Content:** the `base` pack through `IContentScene`:
  - maps at `rules/maps/<id>.json` (tiles, heights, props, zones);
  - sprites and effects from the art sets' manifests (`characters`, `fx`, `projectiles`, …), whose keys the events already carry.
- **Signals:** `ISignalingScene.Signal("shown", new { seq })` once a beat's animation is done. The game falls back after 400 ms + 250 ms per event (up to 6 s), so a slow or missing TV never stalls it.

## Tasks

| # | Task | Done when |
|---|---|---|
| 1 | `TacticsScene` registered in `GameSceneMap`, rendering the banner and phase screens (Muster roster, encounter list, results) from the host view | Screenshot run shows each phase |
| 2 | **The map:** tiles from the map file (heights as shading or isometric steps), props, zones from the state | The five sample maps draw recognisably |
| 3 | **Units:** sprites from the art manifest by `sprite` key, HP bars, Downed countdown, condition icons, the active-unit ring | The river crossing start matches the encounter file |
| 4 | **Beat playback:** a queue of beats, newest `seq` first-seen; each event animated (move along path, attack lunge + projectile + number pop, save roll badge, conditions, zones, push/fall, downed/out, spawn, `say` lines through the narrator); `shown` sent after each beat | A full AI battle plays at watchable speed with no overlaps and no stalls when signals are dropped |
| 5 | **HUD:** turn order strip, round, objectives, "AI finished" badge on `AiFinished` events, reaction prompt banner | Playtest checklist entry passes |
| 6 | **Catch-up:** a host view whose oldest beat is newer than the last shown (reconnect, slow TV) snaps the board to the state and plays only the newest beat | Reconnect mid-battle in `tools/tv-e2e.sh` |

## Notes for whoever picks this up

- **Draw positions from the beats, not the state.** The state in the host view is already past the beat being animated, so start each beat from its own events; the units list is the truth only once the queue is empty.
- **Keep animation per event short.** The fallback timer is the game's patience, not the TV's target; aim for under 1 s per attack.
- **A playtest pass first:** run the server, two phones and the TV (`tools/tv-e2e.sh` has the pattern), play river_crossing, and fix what feels wrong on the phones before polishing the TV.
