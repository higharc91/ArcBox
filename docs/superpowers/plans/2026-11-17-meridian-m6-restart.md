> Status: draft for review (2026-11-17). Not started. Live task state will belong on GitHub issues once approved (see the [live tracker index](../README.md)).

# Derelict: ISV Meridian — M6 Restart Implementation Plan

Steps use checkbox (`- [ ]`) syntax for tracking. Each task is test-first and ends in one commit.

**Goal:** A Meridian game survives a server restart. When the server comes back, the room comes back with its game where it was (at most one turn earlier), waits for the phones to reconnect, and plays on to the Debrief. Today a restart sends the room back to the lobby with "Game interrupted" (spec §5.6, success criterion 7).

**Architecture:**
- **One platform addition** (spec §5.6). `ISnapshotGame : IResumableGame` in `ArcBox.Sdk` lets a game hand the room an opaque JSON snapshot and take it back before `Resume`. The room stores the newest snapshot in a new `room_game_state` table, through the existing background `StorageWriter`, keyed by room code so the newest write wins. It deletes the row when the game ends. The platform knows nothing about what a snapshot holds.
- **Meridian's snapshot is its session.** `MeridianSession` already holds everything: the whole `GameState` (with its random generator, so a resumed game draws what it would have drawn), seats, phase, the presentation queue. It has round-tripped through JSON since M2, and M2's `MeridianGame.Load` was built as the restore half. Meridian takes a snapshot at the start of each turn and at each phase change, so a restart replays at most one turn.
- **Resuming** is a short state of its own. Every seat comes back disconnected. The game holds its clock and waits up to `ResumeWait` (60 s) for phones, with no bots taking over, then plays on with the usual disconnect rules.

**Spec:** [2026-11-13-meridian-design.md](../specs/2026-11-13-meridian-design.md), §3.3, §5.6, §7, §8. **Earlier stages:** [M1](2026-11-13-meridian-m1-rules-core.md), [M2](2026-11-13-meridian-m2-playable-explore.md), [M3](2026-11-13-meridian-m3-breach-engine.md) and [M4](2026-11-17-meridian-m4-tv-scene.md), including their *Implementation notes*. **Platform:** `src/ArcBox.Server/Rooms/Room.Durable.cs` (durable rooms), `GameSession.cs` (guarded calls), `Storage/` (the writer and migrations).

**Not in this plan:**
- Art and audio (M5), and scenarios 6–12 (M7).
- Snapshots for REDLINE or Tactics. Tactics already resumes from its campaign document. REDLINE keeps "Game interrupted". The interface is opt-in, so neither changes.
- Surviving a crash mid-write. The writer's newest-wins queue and SQLite's transactions are trusted as they are for rooms and campaigns.

## Global constraints

- **Everything in M1–M4's Global constraints still holds.**
- **The platform stays game-agnostic.** The room stores and returns `JsonElement`s; it never parses one. Meridian never touches the database.
- **Secrets at rest stay at rest.** A snapshot holds hidden roles and pile order. It is stored server-side only, never sent to any client, and never logged. A test checks no hub message carries it.
- **Bounded writes.** At most one snapshot per turn and per phase change, about one small write a minute. A snapshot over 256 KB is not stored: the game would resume from the previous one, and a warning is logged.
- **Restore is all-or-nothing.** A snapshot that fails to read (an older build's format, corruption) leaves the room in the lobby with "Game interrupted", exactly as a game with no snapshot does. It never resumes from half a state.
- **Determinism carries across a restart.** A game restored from its turn-start snapshot and given the same commands reaches the same state as the game that was never interrupted.

## Review focus

Situations a main-path test would not reach. Each gets a pinned test in the task in brackets.

1. **A restart before the first snapshot** (between Start and the first turn). The room returns to the lobby with "Game interrupted". [Task 2]
2. **The game ends while a snapshot write is queued.** The delete must win; no game resumes after it ended. [Task 2]
3. **A snapshot from an older build.** Restore fails cleanly: "Game interrupted", a warning, and no exception escapes. [Tasks 2, 3]
4. **A restart mid-vote, mid-briefing, or mid-presentation.** The game resumes from that turn's start or that phase's start, and nobody is asked for a ballot or a Ready that the restored state doesn't expect. [Task 4]
5. **Secrets after a restore.** The first views after a restore (TV, every phone, every bot) hold nothing outside their audience. [Task 4]
6. **Nobody comes back.** After `ResumeWait`, bots play every seat as the usual disconnect rules say, and the game can still end. [Task 4]
7. **Bot seats.** A seat a bot was playing before the restart is a disconnected seat after it, and a phone that rejoins takes it back. [Task 4]

---

## Tasks

### Task 1: `ISnapshotGame` and the `room_game_state` table

**Files:**
- Create `src/ArcBox.Sdk/ISnapshotGame.cs` and `src/ArcBox.Server/Rooms/GameStateStore.cs`.
- Modify `Storage/Migrations.cs` (script 2) and `RoomServices` registration.

- **The interface** is the spec's, with one addition: `Restore` may throw, and the room treats a throw as "no snapshot".

  ```csharp
  public interface ISnapshotGame : IResumableGame
  {
      /// <summary>Called after each guarded call. Return null when nothing worth saving changed.</summary>
      JsonElement? TakeSnapshot();

      /// <summary>Called before Resume with the last stored snapshot. Throws if it can't be read.</summary>
      void Restore(JsonElement snapshot);
  }
  ```
- **The table:**

  ```sql
  CREATE TABLE room_game_state (
      code TEXT PRIMARY KEY NOT NULL,
      game_id TEXT NOT NULL,
      snapshot TEXT NOT NULL,
      saved_utc TEXT NOT NULL
  )
  ```

  It's added as migration script 2; script 1 is never edited.
- **`GameStateStore`** follows `RoomStore`'s pattern:
  - writes queue on the `StorageWriter` under `game:{code}`, so the newest save or delete wins;
  - a remembered newest entry answers reads before the database;
  - `Save(code, gameId, snapshot)` refuses snapshots over 256 KB with a warning;
  - also `Load(code, gameId)`, `Delete(code)`, and deletion alongside `RoomStore.DeleteOlderThan`.

Tests: save/load/delete; the newest wins; an oversized snapshot isn't stored; a migration test from a version-1 database.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(platform): ISnapshotGame and the room_game_state table`.

### Task 2: The room snapshots, restores and forgets

**Files:**
- Modify `Rooms/GameSession.cs`, `Rooms/Room.Durable.cs` and `Rooms/Room.cs`.
- Add a test-only snapshot game in `ArcBox.Server.Tests/Support` (a counter that remembers its count) and `Rooms/SnapshotRoomTests.cs`.

- **Snapshots:** after each guarded call (input, host signal, connection change, timer, start, resume), a session whose game is an `ISnapshotGame` calls `TakeSnapshot()` and saves any non-null result. `TakeSnapshot` is guarded like everything else; a throw ends the game as a crash does today.
- **Resume:** in `TryResume`, a snapshot game first loads its stored snapshot.
  - None (review focus 1), or `Restore` throws (review focus 3): the room returns "Game interrupted" with a warning in the log.
  - Otherwise `Restore`, then `Resume` as today.
- **Forgetting:** the row is deleted when the game ends (`EndGame`), when the host ends it, and when the room is deleted or parked for good. A queued save followed by a delete leaves nothing (review focus 2).

Tests:
- With the counter game: count to 5, restart the server on the same database, and the room resumes at 5.
- No snapshot means "Game interrupted".
- A corrupt snapshot means "Game interrupted".
- After the game ends, a restart doesn't resume.
- No hub message to the TV or a phone ever contains the snapshot's bytes.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(platform): rooms resume games from their snapshots`.

### Task 3: Meridian's snapshot

**Files:** modify `MeridianGame.cs` (`ISnapshotGame`, the manifest's `ResumesAfterRestart`) and `MeridianSession.cs`; create `Snapshot.cs`; test `SnapshotTests.cs`.

- **The envelope:** `{ "version": 1, "session": <MeridianSession> }`. A different version, or a session that fails to read, throws, which means "Game interrupted" (review focus 3).
- **When:** `TakeSnapshot` returns one when the session's *moment* differs from the last saved one, and null otherwise. A moment is `(phase, round, active seat)`. That gives one snapshot per turn start and per phase change: Boarding, Explore, Briefing, Endgame, Debrief.
- **What a snapshot drops:** clocks and connections, as the spec says. On restore, every seat is disconnected and not auto, and the clock is rebuilt by `Resume`. The presentation queue is kept, so a resumed TV shows the turn's events again from its start.

Tests:
- Determinism: take a snapshot at a turn start in each S1 scenario, restore it into a fresh game, give both games the same commands to the end, and get the same states and the same outcome.
- Size: under 64 KB at six players with 45 compartments and the largest scenario board.
- One snapshot per turn over a whole bot game, and none between.
- Old and corrupt envelopes throw.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): snapshots at each turn and phase`.

### Task 4: Resuming

**Files:**
- Modify `Bots.cs`, `Timing.cs` and `MeridianSession.cs` (a `Resuming` flag with its deadline).
- Modify `Views/HostView.cs` and `Projection.cs`.
- Add a `ResumeWait` (60 s) to `MeridianRules`.
- Test `ResumeTests.cs`.

- **`Resume`:**
  - every seat disconnected, none auto;
  - the clock's purpose becomes `Resume`, lasting `ResumeWait`;
  - no bot plays while it runs, and no other deadline counts down.
- **Leaving Resuming:** when every seat has reconnected, or the wait runs out. Then the game re-arms as usual: a fresh decision gets its full time, and missing seats get the usual grace or pause, then a bot (review focus 6). A seat a bot played before the restart is just a disconnected seat; its phone takes it back on rejoining (review focus 7).
- **The TV** shows `Wait { kind: "resume", seconds }` with a count of phones back, never which ones. **The HUD** shows "Reconnecting… 2 of 4 phones back". Tiller says nothing.

Tests:
- Restore mid-vote, mid-briefing and mid-presentation (review focus 4).
- The M3 secrecy sweep over the first views after a restore in every S1 scenario at 3–6 players (review focus 5).
- Nobody comes back.
- A bot seat is taken back.
- The dev board shows the banner.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): resuming after a restart`.

### Task 5: End to end

**Files:**
- Modify `MeridianHubTests.cs`, `tools/meridian-playtest/bots.cjs` and `tools/tv-e2e.sh`.
- Modify `docs/playtest-checklist.md` and `docs/tv-host.md`.

- **Hub test:**
  1. Three phones and a TV play to the endgame.
  2. The server restarts on the same database.
  3. The TV resumes its room, and the phones rejoin their seats.
  4. The game plays on to the Debrief.

  Run it for each S1 scenario, forced with `Meridian:Scenario`, restarting at a random endgame turn from a fixed seed.
- **Bot phones** keep their seat's secret from `Join` and `Rejoin` when the connection drops, as the Tactics bots do.
- **`ARCBOX_GAME=meridian ARCBOX_RESTART=1 tools/tv-e2e.sh`:**
  1. Restart the server once the endgame begins.
  2. Wait for the TV and the phones to come back.
  3. Fail unless the game reaches the Debrief, the TV shows the Reconnecting banner, the TV abandons no beat after it's back, and no error is logged.

  Run it once per S1 scenario.
- **Checklist:** restart the server mid-game with real phones; restart it during a briefing.
- Run `bash tools/check-all.sh --full`. Expect only the Tactics failures already on `main`.

- [ ] **Step 1: Write the hub test; make it pass.**
- [ ] **Step 2: Bot phones and the TV run, every scenario.**
- [ ] **Step 3: Docs, checklist and the full gate.**
- [ ] **Step 4: Commit** `test(meridian): a game survives a server restart`.

---

## Done when

- A server restart mid-game, in any S1 scenario, resumes the room with the same game at most one turn back, waits for phones, and plays on to the Debrief (spec success criterion 7). Shown by the hub tests and by `ARCBOX_RESTART=1` on the real TV.
- No snapshot reaches a client, and the first views after a restore pass the secrecy sweep.
- A restart before the first snapshot, or from a snapshot that can't be read, returns the room to the lobby with "Game interrupted".
- `bash tools/check-all.sh --full` passes, apart from any Tactics failures already on `main`.
