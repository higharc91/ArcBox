# ArcBox Platform — Design Spec

- **Date:** 2026-10-18
- **Status:** Draft, awaiting review
- **Author:** higharc91 with Claude

---

## 1. Purpose

ArcBox is a Jackbox-style party game platform for a group of friends who also write the games. One machine runs the **TV host app**. Everyone else joins from a **phone browser** with a room code, and their phone becomes their controller. The platform takes care of rooms, joining, reconnects, timers and moving between games, so each new game is only its own logic and visuals.

### What we were told
- Mostly played in the same room on a TV. Sometimes remote friends join: one person screen-shares the TV view on Discord, and remote players use their own phones as controllers.
- Game logic is written in **C#**. Phone controllers are **web pages**. The TV runs a **game engine app** (Godot 4 .NET chosen).
- All games live in **one shared repo** and ship together.
- Sessions have **up to ~8 players** and no audience mode.
- It must be **robust**, and multiple games must layer onto one platform.

### What we're assuming
- Hobby scale: no accounts or logins (nickname plus room code), hosting around $5/month or less.
- "Robust" means:
  - phones survive screen-locks and network blips;
  - the server is the single source of truth;
  - a crashing game ends only its own game, never the server or other rooms;
  - the TV app can crash or restart without losing the room.
- The game authors are trusted friends, so there's no hostile-code sandboxing.

### Success criteria for v1
1. A group can create a room on the TV, join from iPhone and Android browsers, play **Prompt Duel** start to finish, and return to the lobby to play again.
2. Locking a phone for 60 s mid-round and unlocking it puts that player back in the game with the correct screen.
3. Force-quitting the Godot host mid-round and relaunching it resumes the same room.
4. A deliberately throwing test game returns the room to the lobby without affecting other rooms.
5. A friend can add a second game by following `docs/authoring-a-game.md` and copying the template. The only platform changes are registration (see §3.2 rule 4).

---

## 2. Research summary

Key sources:
- johnbox, the Ecast re-implementation (github.com/InvoxiPlayGames/johnbox)
- boardgame.io secret-state docs
- Colyseus `StateView` docs
- Microsoft Learn: SignalR configuration and scale-out (aspnetcore-10.0), assembly unloadability
- .NET support policy
- WebKit bugs 247943 and 207256
- caniuse: Wake Lock
- Fly.io and Railway pricing pages

These are the points that shaped the design:

- **Jackbox's own design** (pieced together from the open-source johnbox and jonahbox re-implementations):
  - Room codes, and phones running a web app at jackbox.tv.
  - The server is mostly a relay that syncs per-player "entities" (text, choice, number, doodle) protected by ACLs.
  - Joining returns a reconnect secret plus a snapshot of everything that player can see.
  - **What we borrowed:** thin phone renderers driven by typed widget descriptions, and reconnect-by-secret with a full snapshot.
- **No mature .NET Jackbox framework exists.** The ideas borrowed instead are boardgame.io's `playerView` and Colyseus's `StateView`: per-recipient projections, so secrets never leave the server. boardgame.io is in maintenance mode, and Hathora shut down in May 2026.
- **.NET 10 is the current LTS**, supported until Nov 2028. .NET 8 and 9 reach end of support on 2026-11-10. **SignalR** gives us groups, typed hubs, automatic reconnect and stateful reconnect (.NET 8+), which saves weeks compared with raw WebSockets.
- **iOS Safari drops WebSockets** when the phone locks or the tab goes to the background, and sometimes doesn't fire `onclose`. Mitigation: a token in localStorage, rejoin on `visibilitychange`/`pageshow`, a full snapshot on rejoin, and the Wake Lock API (iOS 16.4+, HTTPS only).
- **Loading game code into the server process isn't a security boundary.** Real isolation would mean WASM (Extism) or a process per room. For trusted friends, the chosen protection is a per-room actor, exception boundaries and a watchdog.
- **Hosting:** Fly.io is about $2–4/month for a small machine and Railway Hobby is about $5/month. Azure's free tier is unusable for WebSockets, and Azure SignalR Service / Redis aren't needed for a single instance.

### Approaches considered

| | Approach | Verdict |
|---|---|---|
| **A** | **Server-authoritative:** C# game logic on the ASP.NET Core server, a Godot TV app that renders views, and phones built from a shared controller kit | **Chosen.** It's the only option that meets "robust", and game logic is plain, testable C#. |
| B | Host-authoritative (the Jackbox model): logic runs in the Godot app, and the server is a relay | Rejected. The TV laptop becomes a single point of failure, and logic tests need Godot. |
| C | Colyseus (TypeScript server) with its Godot SDK | Rejected. Game logic would be TypeScript, not C#. |

---

## 3. Architecture

```
                ┌──────────── Cloud (Fly.io) or LAN ──────────────────────┐
 Phones (web)   │  ArcBox.Server  (ASP.NET Core, .NET 10)                 │   TV (Godot 4 .NET)
 web/play  ◄────┼──► ArcHub (SignalR, /hub)                               ├────► ArcBox.Host
 controller kit │      │                                                  │      scene per game
                │   RoomManager ── Room (actor, 1 per room)               │      renders HostView
                │                    └── IGame instance (from Games.*)    │
                │   static files: built web/play served at /              │
                └─────────────────────────────────────────────────────────┘
```

### 3.1 Repo layout

| Path | Purpose | Depends on |
|---|---|---|
| `src/ArcBox.Sdk` | `IGame`, `IGameContext`, `Player`, `PlayerInput`, `ControllerView` widget types, `GameManifest`, `GameResults`. No SignalR or Godot code. | — |
| `src/ArcBox.Sdk.Testing` | `GameHarness`: a fake `IGameContext` with a controllable clock and timers, for unit-testing games | Sdk |
| `src/ArcBox.Server` | SignalR hub, `RoomManager`, `Room` actor, room codes, lobby/VIP, tokens, timers, watchdog, `GameRegistry`, static file hosting | Sdk, all `*.Logic` |
| `src/ArcBox.Server.Tests` | Integration tests: in-memory server plus real SignalR clients | Server |
| `src/ArcBox.Host` | Godot 4 .NET TV app: connect screen, lobby with room code and QR, game scene switching, resume-after-crash | Sdk, all `*.Logic` (view types only), `Microsoft.AspNetCore.SignalR.Client` |
| `web/play` | Vite + TypeScript phone app: join, lobby, controller-kit renderer, reconnect, Wake Lock, dev multi-phone page | `@microsoft/signalr` |
| `games/<Name>/<Name>.Logic` | One game's C# logic and its `HostView` record types | Sdk |
| `games/<Name>/<Name>.Logic.Tests` | That game's xUnit tests | Logic, Sdk.Testing |
| `src/ArcBox.Host/games/<name>/` | That game's Godot scene(s) and assets | (inside Host) |
| `games/_Template/` | Copyable starter game: Logic, Tests and a stub scene | — |
| `docs/authoring-a-game.md` | The guide for friends | — |

### 3.2 Core rules
1. **The server is the only source of truth.** Clients send *intents* and receive *views*. Neither the phones nor the TV ever hold authoritative state.
2. **Every room is an actor.** All events for a room (hub calls, timer expiry, disconnects) are posted to that room's `Channel<RoomCommand>` and handled one at a time by a single loop. Game code is never called concurrently and needs no locks.
3. **Every call into game code is guarded.** Each one is wrapped in try/catch and timed by a watchdog (see §5.4).
4. **Adding a game** means one `.Logic` project and one Godot scene, plus registration:
   - a project reference from `ArcBox.Server` and from `ArcBox.Host`;
   - one line in `GameRegistry` (server);
   - one line in `GameSceneMap` (host).

   Phones need nothing new unless the game needs a new controller widget.

### 3.3 Target frameworks
- `ArcBox.Server` and the test projects target `net10.0`.
- `ArcBox.Sdk`, `ArcBox.Sdk.Testing` and every `*.Logic` project target whatever framework the installed Godot 4 .NET release can load: `net10.0` if supported, otherwise multi-target, e.g. `net8.0;net10.0`. **This is verified in Milestone 0** before any other code is written.

---

## 4. Game contract

```csharp
namespace ArcBox.Sdk;

public interface IGame
{
    GameManifest Manifest { get; }                      // Id, Name, MinPlayers, MaxPlayers, Description
    void Start(IGameContext ctx, IReadOnlyList<Player> players);
    void OnInput(PlayerId from, PlayerInput input);    // payload already matched to the current ViewId
    void OnTimer(string key);
    void OnPlayerConnectionChanged(PlayerId id, bool connected);
    object HostView();                                  // a game-specific record, e.g. PromptDuelHostView
    ControllerView PlayerView(PlayerId id);
}

public interface IGameContext
{
    DateTimeOffset Now { get; }
    Random Random { get; }                              // seeded per game; deterministic in tests
    void SetTimer(string key, TimeSpan after);          // replaces an existing timer with the same key
    void CancelTimer(string key);
    DateTimeOffset? TimerDeadline(string key);          // so views can include countdowns
    void EndGame(GameResults results);                  // ordered scores + optional message
}

public sealed record Player(PlayerId Id, string Name, bool IsVip);
```

**Game style.** A game is a plain class that holds its own state, typically an enum `Phase` plus fields. It never touches the network, real clocks, threads or static mutable state. Everything comes in through `IGame` methods and goes out through `ctx` or the view methods.

**View publishing.** After every call into the game, the room calls `HostView()` and `PlayerView(id)` for each player. Each view is serialized and compared with the last one sent to that recipient, and only changed views are sent. There's no diffing beyond that in v1, because the payloads are tiny at 8 players.

**Timers.** These are server-side only. The room runs them on its own scheduler and posts `OnTimer` into the actor inbox. Views carry `DeadlineUtc` so clients can draw countdowns locally.

### 4.1 Controller kit (phone widgets, v1)

`ControllerView` is a polymorphic record that the server serializes with a `kind` discriminator using System.Text.Json:

| `kind` | Fields | Input payload sent back |
|---|---|---|
| `wait` | `message` | — |
| `textEntry` | `prompt`, `maxLength`, `placeholder?` | `{ text }` |
| `choice` | `prompt`, `options[] {id, label}`, `allowMultiple`, `maxSelections?` | `{ selected: [id] }` |
| `buttons` | `prompt?`, `buttons[] {id, label}` | `{ pressed: id }` |
| `draw` | `prompt`, `colors[]?` | `{ strokes: [...] }` (size-capped) |

Every `ControllerView` also carries `viewId` (a string the room assigns and changes on every new view), `deadlineUtc?` and `header?` (e.g. score or round). Input sent with an old `viewId` is dropped before it reaches the game, so a lagging phone can't act on a screen that's already gone.

**Adding a widget** is a platform change: a C# record in Sdk, a TS renderer in `web/play`, and a contract test. Games then use it freely.

### 4.2 HostView
`HostView()` returns a game-specific record defined in the game's `.Logic` project. The server sends it as JSON. The Godot host deserializes it into the same C# type, because Host references the Logic project, and hands it to the active scene's `Render(view)` method.

### 4.3 First game: Prompt Duel
A small Quiplash-style game that exercises the whole contract:
- **Answer** (60 s, `textEntry`): each player answers two prompts. Each prompt is shared by two players.
- **Vote** (20 s per matchup, `choice`): everyone except the two authors votes on that pair of answers.
- **Reveal:** the TV shows the votes, and points are awarded.
- **Scores** after 3 rounds, then `EndGame`.
- Missing answers become "(no answer)", so disconnected players never stall a round.

---

## 5. Networking, rooms and resilience

### 5.1 Transport
- One SignalR hub at `/hub`, using the JSON protocol for debuggability.
- `AllowStatefulReconnects = true`.
- `MaximumReceiveMessageSize` raised to 256 KB for drawings.
- `ArcBox.Server` also serves the built `web/play` as static files at `/`, so the whole thing deploys as **one container**.
- HTTPS comes from the hosting platform, or from a dev certificate or tunnel on the LAN. Wake Lock requires it.

### 5.2 Room lifecycle
1. **Godot host:** `CreateRoom()` returns `{ code, hostToken }`.
   - Codes are 4 letters from `BCDFGHJKLMNPQRSTVWXZ`, avoiding confusables and accidental words.
   - The host shows the code plus a QR code for `https://<server>/?room=CODE`.
2. **Phone:** `Join(code, name)` returns `{ playerId, secret }`, which is stored in localStorage under `arcbox:<code>`.
   - Names are 1–12 characters, trimmed and unique within the room.
   - The first player to join is **VIP**. If the VIP leaves, the next player to have joined becomes VIP.
3. **Lobby:**
   - The VIP's phone or the host app can pick a game and press "Everybody's in".
   - Start is enabled only when the player count fits the manifest's min/max.
4. **Game runs**, and `EndGame` sends everyone back to the lobby with the results shown. The room and players persist for the next game.
5. **Late joiners during a game:** v1 rejects new joins mid-game with "Game in progress, join after this round". Rejoining with an existing secret always works.

### 5.3 Hub surface

| Direction | Methods |
|---|---|
| Host → server | `CreateRoom()`, `ResumeHost(code, hostToken)`, `SelectGame(gameId)`, `StartGame()`, `ReturnToLobby()`, `Kick(playerId)` |
| Phone → server | `Join(code, name)`, `Rejoin(code, playerId, secret)`, `SelectGame(gameId)` (VIP only), `StartGame()` (VIP only), `SendInput(viewId, payload)` |
| Server → host | `LobbyChanged(lobby)`, `HostView(gameId, view)`, `GameEnded(results)`, `RoomClosed(reason)` |
| Server → phone | `LobbyChanged(lobby)`, `ControllerView(view)`, `GameEnded(results)`, `RoomClosed(reason)`, `Kicked` |

Hub methods only validate the input, look up the room and post a command to that room's inbox. They never touch room state directly.

### 5.4 Reconnects and failure handling

**Phones**
- SignalR automatic reconnect with backoff `[0, 1, 2, 5, 10, 10, …]` seconds, plus stateful reconnect for short blips.
- On `visibilitychange` → visible or `pageshow`: if the connection isn't `Connected`, start it and call `Rejoin`.
- After a successful `Rejoin`, the server binds the new connection ID to the player slot and **immediately sends the full current lobby or controller view**. Correctness never depends on replaying buffered messages.
- Request a Wake Lock while an input widget is shown, and release it on `wait`.
- **A disconnected player keeps their slot** for the room's lifetime. The game gets `OnPlayerConnectionChanged` and decides what to do.

**Host app**
- Saves `{serverUrl, code, hostToken}` to `user://session.json`.
- On launch, if the file exists, it tries `ResumeHost`. On success it shows the current state. On failure (room gone) it deletes the file and creates a new room.

**Failure table**

| Failure | Behavior |
|---|---|
| Game throws an exception | Log it with the room code and game ID, then end the game with "Game crashed" and return to the lobby. Players stay connected, and other rooms are unaffected. |
| Game call takes longer than 2 s | The watchdog logs it and closes the room (`RoomClosed("game stopped responding")`). **Known limitation:** .NET can't abort the stuck thread, so it stays busy until the server restarts. This is acceptable for trusted authors. |
| `HostView` / `PlayerView` throws | Handled the same as a game exception. |
| Stale `viewId`, oversized payload, or bad JSON | Dropped and logged at debug level. |
| Message flood | Rate limit of about 20 messages per second per connection. Excess messages are dropped. |
| Host gone for 10 min, or no connections for 10 min | Room disposed and clients get `RoomClosed`. |
| Server restart or redeploy | Rooms are in memory only, so they're lost. Clients show "Room ended, ask the host to start a new one". No persistence in v1. |

### 5.5 Security, proportionate to a hobby project
- Player secrets and host tokens are 128-bit random values, compared in constant time.
- Names are length-limited, and clients render them as text, never HTML.
- At most **20 concurrent rooms** and 8 players per room (the manifest can lower this), so a public URL can't be used to exhaust the server.
- No accounts and no personal data stored. Room codes aren't secret.

---

## 6. Clients

### 6.1 Phone app (`web/play`)
- **Stack:** Vite, TypeScript and Preact, chosen for being tiny with no heavy framework. It has two screens: Join and Room. Room renders the lobby or the current `ControllerView` widget.
- **URL:** `/?room=CODE` pre-fills the code. If a stored secret exists for that code, it rejoins automatically.
- **Dev page** `/dev.html?room=CODE&phones=6` shows N phone iframes side by side, so one person can test a full game on a single monitor.
- **Mobile basics:**
  - large tap targets;
  - `viewport-fit=cover`;
  - no zoom on input focus (16px fonts);
  - works in portrait;
  - tested on iOS Safari and Android Chrome.

### 6.2 TV host (`ArcBox.Host`, Godot 4 .NET)
- **Screens:** Connect (server URL from settings, defaulting to the production URL, with LAN override), Lobby (room code, QR code, player list, game picker), then the active game scene, then Results.
- **Network and threading:** uses the `Microsoft.AspNetCore.SignalR.Client` NuGet package. Hub callbacks arrive on thread-pool threads and are marshalled to the main thread with `CallDeferred` / a thread-safe queue drained in `_Process`.
- **Game scenes:**
  - each game's scene implements `IGameScene { void Render(object hostView); }`;
  - `GameSceneMap` maps game ID to scene path;
  - scenes only animate between views, and never decide game outcomes.
- **Export:** Windows desktop. A Linux/macOS export is possible later.

---

## 7. Testing strategy

| Layer | Tool | What it covers |
|---|---|---|
| Game logic | xUnit + `GameHarness` | Phase transitions, scoring, timers firing, disconnect behavior, deterministic `Random`. Every game ships with tests. |
| Server | xUnit + `WebApplicationFactory` + real SignalR clients | Create/join/rejoin, VIP rules, stale `viewId` dropping, rate limits, a crashing test game returning to the lobby, a hanging test game triggering the watchdog, room cleanup. Uses a fake clock and a test game. |
| Contract | xUnit + Vitest | C# serializes one sample of each `ControllerView` kind to JSON fixtures, and Vitest checks the TS renderer accepts each fixture. This catches C#/TS drift. |
| Phone app | Vitest | Widget rendering and reconnect state machine logic. |
| End to end | Manual playtest checklist (`docs/playtest-checklist.md`) | Real phones, lock/unlock, kill the host, network toggles. This maps to the success criteria in §1. |

Godot scenes aren't unit-tested in v1, which is acceptable because they hold no game logic.

---

## 8. Dev workflow and deployment

- **Local dev:**
  - `dotnet run` the server (it also serves `web/play` through the Vite dev proxy in development);
  - run the Godot host from the editor pointed at `https://localhost:<port>`;
  - use `/dev.html` for fake phones;
  - real phones on the LAN use `https://<pc-ip>:<port>` (a firewall rule plus a trusted dev cert), or a Cloudflare Tunnel for HTTPS without certificate hassle.
- **Container:** a multi-stage Dockerfile builds `web/play` with Node, publishes `ArcBox.Server` with .NET 10, and copies the static output into `wwwroot`.
- **Hosting:**
  - **Fly.io**, one shared-cpu-1x 512 MB machine, about $4/month, in the region closest to the group;
  - auto-stop when idle is fine because rooms are ephemeral;
  - **never deploy during a game night**, since a deploy restarts the server and drops rooms.
- **CI (later, optional):** a GitHub Actions workflow that runs `dotnet test` and `npm test` on push.

---

## 9. Roadmap

Each milestone produces something runnable. M2 and M3 can run in parallel once M1's contract is merged.

| Milestone | Deliverable | Done when |
|---|---|---|
| **M0 — Setup** | Install the .NET 10 SDK and the current Godot 4 .NET release. `git init`, solution skeleton, `.editorconfig`. **Spike:** a Godot project references a class library and a SignalR client and connects to a hello-world hub. | Godot calls a hub method and prints the reply. Sdk target framework decided (§3.3). |
| **M1 — Platform core** | `ArcBox.Sdk`, `Sdk.Testing`, `ArcBox.Server` (rooms, join/rejoin, VIP, lobby, actor loop, timers, view publishing, watchdog, limits) plus test games (`Counter`, `Thrower`, `Hanger`) | All server integration tests pass. |
| **M2 — Phone app** | `web/play` with join, lobby, all 5 widgets, reconnect, Wake Lock and the dev multi-phone page. Contract tests. | 6 dev phones play the `Counter` test game. Rejoin works after reloading a phone iframe. |
| **M3 — TV host shell** | Godot app with Connect, Lobby with QR, scene switching, Results, resume-after-crash, and a `Counter` scene | Full lobby → game → lobby loop on the TV with dev phones. Killing and relaunching the host resumes the room. |
| **M4 — Prompt Duel** | Logic, tests, Godot scene and a prompt list | Success criterion 1 works on the LAN with real phones. |
| **M5 — Deploy and playtest** | Dockerfile, Fly.io app, production URL in host settings, `docs/playtest-checklist.md` | All 5 success criteria pass in a real playtest, including one remote friend over Discord. |
| **M6 — Authoring kit** | `games/_Template`, `docs/authoring-a-game.md`, a script to copy and rename the template | A friend builds a trivial second game from the docs alone. |

---

## 10. Out of scope for v1 (possible later work)
- Audience mode, spectators, or more than 8 players.
- Drop-in game packs loaded at runtime. The repo-based model is enough for now.
- Persistence (room survival across restarts, player stats, saved drawings).
- Chromecast receiver, a browser-based TV host, or a separate host view for each remote player.
- Hard sandboxing of game code (WASM or a process per room).
- Multiple server instances, Redis backplane, Azure SignalR Service.
- Accounts, profanity filtering, moderation tools.
- Localization.

## 11. Risks and open items

| Risk | Mitigation |
|---|---|
| Godot .NET can't load `net10.0` assemblies | Verified in M0. Fallback is multi-targeting the Sdk and Logic projects. |
| iOS Safari socket behavior worse than expected | Rejoin-by-token plus full snapshot is designed for the worst case. Test on real iPhones in M2. |
| Hung game thread consumes a thread-pool thread | Watchdog closes the room. A server restart clears it. Revisit with process isolation if it ever happens in practice. |
| Fly.io pricing or free-tier changes | Railway or a small VPS are drop-in alternatives, since it's one container. |
| C# ↔ TS widget drift | Contract fixture tests (§7). |
