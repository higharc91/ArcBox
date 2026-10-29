# ArcBox Platform v2: Design Spec

- **Date:** 2026-10-29
- **Status:** Draft, awaiting review
- **Author:** higharc91 with Claude
- **Amends:** [ArcBox platform design](2026-10-18-arcbox-platform-design.md). It moves persistence out of §10 (out of scope) and into the platform.
- **Driven by:** the campaign tactics RPG (working name *ArcBox Tactics*). Its mockups are in [`docs/superpowers/mockups/tactics/`](../mockups/tactics/). The game's own spec comes separately.

---

## 1. Purpose

ArcBox was built for party games that last one evening: rooms live in memory, players are per-room seats, and phones are generic widget renderers. The next game is a **persistent co-op campaign**. It's a speed-based tactics RPG that a group of 4–6 plays for about 2 hours a week. Each player keeps a character from week to week, the AI plays the character of anyone who's away, and new story content arrives weekly.

That needs seven platform features. Each one is generic, so the next persistent game gets it for free, and several of them make REDLINE better straight away.

### Decisions already made (from the brainstorming session)
- The campaign lives in a **platform campaign service**, with a **between-sessions portal** on the phone (option B).
- Loot is split by **need/greed rolls**; absent players pass automatically. (That's game rules, recorded here for context.)
- All seven changes below ship as **sub-project 0, "Platform v2"**, before any tactics code.

### Success criteria
1. A phone that joins next week's room as the same person is recognized without being asked (§3.1).
2. A player who arrives mid-game can be seated in a game that allows it; REDLINE still rejects late joins (§3.2).
3. A game can drive its own phone screens through `CustomView`. REDLINE's phone looks and behaves exactly as before (§3.3).
4. A campaign document survives a server restart. While a room has it checked out, the portal can read it but not change it (§3.4).
5. Killing the server mid-game and restarting it brings back **the same room code, the same seats and secrets**. A resumable game picks up again; REDLINE returns to the lobby with a "Game interrupted" note (§3.5).
6. A content-only change (new map or dialogue) reaches the TV without re-exporting the Godot app (§3.6).
7. A game can advance when the TV reports that an animation finished, and still advances on its fallback timer if the TV never reports (§3.7).
8. `dotnet test ArcBox.slnx`, `npm test` in `web/play` and `tools/tv-e2e.sh` stay green.

---

## 2. Summary of changes

| # | Change | Layers touched | REDLINE impact |
|---|---|---|---|
| 1 | **Profiles:** a device identity that persists across rooms | Sdk, Server, phone | Recompile only |
| 2 | **Opt-in late join** via `IAcceptsLateJoin` | Sdk, Server | None: it doesn't opt in |
| 3 | **`CustomView` + game modules on the phone** | Sdk, phone | Its skin moves to `games/redline/` with no change in behavior ([REDLINE issue](#7-redline-work)) |
| 4 | **Campaign service and portal API** | Server (new `Campaigns/`), phone | None |
| 5 | **Durable rooms:** room records persist, are rebuilt after a restart, and idle rooms are parked | Sdk, Server | Interrupted games return to the lobby with the same code ([REDLINE issue](#7-redline-work)) |
| 6 | **Content delivery** from server to TV | Server, Host.Core, Host shell | None: REDLINE has no packs |
| 7 | **Host signals:** TV → game messages such as "animation done" | Sdk, Server, Host.Core, Host shell | Optional adoption for pacing ([REDLINE issue](#7-redline-work)) |

Changes 1, 4 and 5 share one storage layer (§4).

---

## 3. Design

### 3.1 Profiles

**Problem.** A phone's identity is `arcbox:<CODE>` in localStorage: a player ID and secret for **one room**. Next week's room is unrelated, so a game can't tell that the phone joining as "Dave" owns Aldric.

**Design.**
- On first launch the phone creates `arcbox:device`, a 128-bit random base64url value, and sends it on every join and rejoin. It's the phone's **credential** and never leaves the device except in those calls.
- The server keeps a `profiles` row (`id`: an opaque `pr_` + 16 random base64url characters) and one `profile_devices` row per device holding `token_hash`, the SHA-256 of the device token. The token itself is never stored. Lookup is by hash.
- A profile is created the first time an unknown device token is seen. A missing token (an old phone build, or storage blocked) means **no profile**, and the session still works exactly as today.
- Games see the profile through `Player`:

```csharp
public readonly record struct ProfileId(string Value);          // opaque; never the credential
public sealed record Player(PlayerId Id, string Name, bool IsVip, ProfileId? Profile = null);
```

**Hub.** SignalR doesn't support method overloads, so the join calls become request records that can be extended later. The old methods stay for one release so a stale phone tab still works:

```csharp
public sealed record JoinRequest(string Code, string Name, string? DeviceToken = null);
public sealed record RejoinRequest(string Code, string PlayerId, string Secret, string? DeviceToken = null);

public Task<JoinResult> JoinRoom(JoinRequest request);
public Task RejoinRoom(RejoinRequest request);
// Join(code, name) and Rejoin(code, playerId, secret) forward with DeviceToken = null.
```

**Rules.**
- **One seat per profile per room.** A `JoinRoom` whose profile already has a seat in the room **rebinds that seat** (as `Rejoin` does), whatever name was typed. It **issues a new secret** for the seat and returns it, because only the old secret's hash is stored (§3.5), so the old secret stops working. A phone that lost its `arcbox:<CODE>` entry but kept its device token therefore lands back in its own seat.
- A rejoin with a device token whose profile differs from the seat's is rejected with "That seat belongs to another device." A rejoin that sends no token is allowed, as it is today.
- **Linking a new device.** `POST /api/profiles/link-codes` (authenticated as the profile) returns a 6-character one-time code (alphabet `BCDFGHJKLMNPQRSTVWXZ23456789`, 15-minute lifetime). `POST /api/profiles/link {code}` from the new phone attaches its device token to that profile; a profile may have several device-token hashes. The campaign owner can also issue a link code for any member (§3.4).
- **HTTP authentication** (portal and profile endpoints): header `Authorization: Device <token>`. The token is matched by hash and the endpoints are rate limited per IP. There are no cookies, so there's nothing for CSRF to exploit.

### 3.2 Opt-in late join

**Problem.** `Room.JoinAsync` throws "Game in progress, join after this round." That's right for REDLINE, but in a campaign the late arrival is the whole point: Dave turns up and takes Aldric back from the AI.

**Design.** An optional interface. The room checks `game is IAcceptsLateJoin`:

```csharp
namespace ArcBox.Sdk;

/// <summary>Opt-in: lets players join a running game. Called on the room's actor, like every IGame method.</summary>
public interface IAcceptsLateJoin
{
    /// <summary>Whether this player may join now. Profile is set when the phone has one.</summary>
    bool CanAcceptLateJoin(Player candidate);

    /// <summary>The player is seated; their first PlayerView is requested right after this returns.</summary>
    void OnPlayerJoined(Player player);
}
```

**Rules.**
- The room's player cap (`RoomOptions.MaxPlayers` and the manifest maximum) still applies.
- If `CanAcceptLateJoin` returns false, or the game doesn't implement the interface, the join is rejected with the existing message.
- `GameSession` gains `AddPlayer(Player)`. Both game calls go through `Guard`, so an exception ends the game as usual.
- The joining player is never VIP if a VIP is already connected. VIP rules are otherwise unchanged.

### 3.3 `CustomView` and game modules on the phone

**Problem.** The tactics phone needs an action list with action-economy pips, a target picker with a d-pad, a character sheet, a point-buy form and a two-sided trade. None of these fit the generic widgets, and adding generic widgets shaped around one game would bloat the controller kit.

**Design.** One new widget kind gives any game a way to define its own screens:

```csharp
[JsonDerivedType(typeof(CustomView), "custom")]
/// <param name="Module">A game module in web/play/src/games/ (normally the game id).</param>
/// <param name="Screen">A screen that module exports.</param>
/// <param name="Data">Screen-specific JSON. Kept under 64 KB (logged at Warning above that).</param>
public sealed record CustomView(string Module, string Screen, JsonElement Data) : ControllerView;

/// <summary>The CustomView reply: a screen-defined action and its data.</summary>
public sealed record CustomInput(string Action, JsonElement? Data = null);
```

On the phone, `web/play/src/skins/` becomes `web/play/src/games/`:

```ts
// web/play/src/games/module.ts
export interface GameModule {
  id: string;
  skin?: Skin;                                              // the existing Skin interface, unchanged
  screens?: Record<string, ComponentType<CustomScreenProps>>;
}
export interface CustomScreenProps { data: unknown; send(action: string, data?: unknown): void; }

// web/play/src/games/registry.ts: each module is a lazy chunk, loaded the first time its game is selected
const modules: Record<string, () => Promise<GameModule>> = {
  redline: () => import("./redline").then(m => m.module),
};
```

**Rules.**
- `viewId` semantics are unchanged. A custom screen that has to accept repeated input (a d-pad cursor) relies on the game keeping that view unchanged. The game sends all the per-target preview data once, and the phone renders the cursor locally.
- An unknown module or screen renders a fallback card ("This screen needs a newer app") with a **Reload** button. It never shows a blank page.
- The module chunk starts loading on `LobbyChanged` once a game is selected, so the first custom screen doesn't wait on the network.
- **Contract test:** C# writes a `custom` fixture, and Vitest checks that `parseControllerView` accepts it and that the registry resolves `redline`.
- **REDLINE:** `skins/redline.ts` and its tests move to `games/redline/` and are exported as `module.skin`. `skinFor(gameId)` becomes `moduleFor(gameId)?.skin`, which **resolves synchronously once loaded** and falls back to the generic look while loading, the same as a missing skin today.

### 3.4 Campaign service and portal API

**Problem.** Campaign state has to outlive rooms and server restarts, be editable between sessions from a phone, and never be edited from two places at once.

**Responsibilities.** The platform owns campaigns, members, invites, versions, check-out, snapshots and storage. The **game owns the document**, an opaque JSON value whose schema and rules belong to the game.

**Model.**

```csharp
public sealed record CampaignInfo(
    string Id, string Code, string GameId, string Name,
    ProfileId Owner, IReadOnlyList<CampaignMember> Members,
    long Version, string? CheckedOutBy);                      // room code, or null

public sealed record CampaignMember(ProfileId Profile, string DisplayName, CampaignRole Role, DateTimeOffset JoinedUtc);
public enum CampaignRole { Owner, Player }
```

- `Code` is 4 letters from the room-code alphabet. It's for typing into a URL, not a secret. An invite needs `?invite=<token>`, a 128-bit token the owner can rotate.
- **In memory, backed by storage.** `CampaignService` keeps every campaign in memory (a few campaigns of up to a few hundred KB each). Reads are synchronous, so a game can use them inside its actor without breaking the watchdog. Writes update memory first, then queue a background write (§4).

**Game-facing API** (synchronous, safe from a room's actor):

```csharp
public interface ICampaigns
{
    IReadOnlyList<CampaignInfo> ListForGame(string gameId);

    /// <summary>
    /// Checks the campaign out to a room. Returns null when another live room holds it.
    /// Re-checking out from the same room code returns the existing lease (idempotent, for durable rooms §3.5).
    /// </summary>
    CampaignLease? CheckOut(string campaignId, string roomCode);
}

public sealed class CampaignLease : IDisposable
{
    public CampaignInfo Info { get; }
    public JsonElement Document { get; }

    /// <summary>Replaces the document (Version + 1) and queues a write. Cheap: no I/O on the caller's thread.</summary>
    public void Save(JsonElement document);

    /// <summary>Like Save, and also records a named snapshot (e.g. "End of session 7") for rollback.</summary>
    public void Checkpoint(JsonElement document, string label);

    public void Dispose();                                    // check-in: releases the lease
}
```

Games receive `ICampaigns` through their factory, the same way `RedlineGame` receives `IQuestionSource` (`AddArcGame(sp => new TacticsGame(sp.GetRequiredService<ICampaigns>(), …))`).

**Leases.**
- A lease belongs to a room code, lives only in memory, and is released when the game disposes it or the room is removed.
- After a restart there are no leases. The rebuilt room's game checks out again (§3.5).
- While a campaign is leased, portal **commands** return `409 Conflict` with "A session is in progress. Use the in-game menu." Portal **reads** keep working.

**Portal commands go through the game's own rules.** Each game registers a handler, so a level-up or trade is validated by exactly the code the session uses:

```csharp
public interface ICampaignCommandHandler
{
    string GameId { get; }

    /// <summary>The member's view of the campaign for the portal (the game decides what each member may see).</summary>
    JsonElement ViewFor(CampaignInfo info, JsonElement document, ProfileId member);

    /// <summary>Applies a portal command. Returns the new document, or an error message for the member.</summary>
    CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data);

    /// <summary>The empty document for a new campaign.</summary>
    JsonElement NewDocument(CampaignInfo info);
}
public sealed record CommandResult(JsonElement? Document, string? Error);
```

**HTTP API** (authenticated with `Authorization: Device <token>` as in §3.1, JSON both ways):

| Method and path | Who | Does |
|---|---|---|
| `POST /api/campaigns` `{gameId, name, displayName}` | any profile | Creates a campaign; the caller becomes owner |
| `GET /api/campaigns/mine` | any profile | Lists the caller's campaigns |
| `POST /api/campaigns/{code}/join` `{invite, displayName}` | any profile | Becomes a member |
| `GET /api/campaigns/{code}` | member | `CampaignInfo` plus `ViewFor(...)` |
| `POST /api/campaigns/{code}/commands` `{type, data, version}` | member | Runs `Handle`. Returns `409` on a version mismatch or an active lease |
| `POST /api/campaigns/{code}/invite/rotate` | owner | Issues a new invite token |
| `POST /api/campaigns/{code}/members/{profileId}/link-code` | owner | Issues a device link code for that member (§3.1) |
| `GET /api/campaigns/{code}/snapshots` and `POST …/rollback {snapshotId}` | owner | Lists snapshots and restores one (not while leased) |

**Rules.**
- **Optimistic concurrency:** each command carries the `version` the phone last saw. A mismatch returns `409` and the current view, so the phone can redraw and retry.
- **Snapshots:** every `Checkpoint` records one, and a `Save` records one at most every 10 minutes. The last 50 per campaign are kept.
- **Limits:** a document can be at most 2 MB (larger ones are rejected with an error the game logs), each profile can own at most 20 campaigns, and each campaign can have at most 12 members.
- The phone portal is the route `/c/<code>` in `web/play`, built from the game's module (`games/<id>/portal`). This spec covers only its shell (authentication, loading, `409` handling); the screens belong to each game's spec.

### 3.5 Durable rooms

**Problem.** A restart or redeploy wipes every room. A campaign checkpoint alone still leaves five people re-typing a new code. Separately, `IdleTimeout` (10 minutes) closes a room during a long break.

**Design.** Persist the room's **membership**, but never its game state. Rebuild the room on demand.

- The `rooms` table stores: `code` (primary key), `host_token_hash`, `slots_json` (`[{id, name, secretHash, joinOrder, profile}]`), `next_slot_id`, `selected_game`, `game_running`, `created_utc` and `updated_utc`.
- **Secrets and host tokens are stored only as SHA-256 hashes**, and are compared hash-to-hash with `CryptographicOperations.FixedTimeEquals`. In memory, `PlayerSlot.Secret` becomes `SecretHash`, and `Secrets.Matches(string expectedHash, string provided)` hashes `provided` before the constant-time compare. `Secrets.Hash(string)` is new.
- **When rows are written:** on create, join, kick, game select, game start and game end. These are small writes through the background writer (§4).
- **Rebuilding:** when `RoomManager.Find(code)` misses memory, it loads the row (if `updated_utc` is within `RoomOptions.RetainFor`, default 24 h), recreates the `Room` with every slot disconnected, and starts its actor. Two simultaneous misses for the same code produce one room (`GetOrAdd` over a lazy entry).
- **A game that was running when the room was lost:**

```csharp
public sealed record GameManifest(string Id, string Name, string Description, int MinPlayers, int MaxPlayers,
                                  bool ResumesAfterRestart = false);

/// <summary>Implemented by games whose manifest sets ResumesAfterRestart. Called instead of Start.</summary>
public interface IResumableGame
{
    /// <summary>
    /// Rebuild from the game's own durable state (e.g. its campaign lease). Every player starts disconnected and
    /// reconnects through OnPlayerConnectionChanged; a game should wait for them rather than hand seats to an AI.
    /// </summary>
    void Resume(IGameContext ctx, IReadOnlyList<Player> players);
}
```

  - If `ResumesAfterRestart` is set, the rebuilt room calls `Resume` and goes back to `InGame`.
  - Otherwise (REDLINE) the room comes back in the **lobby**. `LastResults = new GameResults([], "Game interrupted: the server restarted.")` is sent as a `GameEnded` to each connection the first time it binds after the rebuild, so the TV and phones show the existing results screen with that note, then the lobby. No client changes are needed.
- **Parking idle rooms:** reaching `IdleTimeout` now **removes the room from memory without deleting its row**. The next `ResumeHost` or `Rejoin` rebuilds it, so a long break is harmless. Rows older than `RetainFor` are deleted by `RoomSweeper`.
- A **watchdog close** (hung game) still deletes the row, because a room whose game hung shouldn't come back. A campaign survives because its last save is already stored.
- `RoomCodes.Generate` also skips codes that exist in storage.
- **Clients:** no protocol change. `HostClient.ResumeAfterReconnectAsync` and the phone's `Rejoin` already retry against the same code. The only difference is that the server now answers instead of saying "Room not found."
- **Shutdown:** `HostOptions.ShutdownTimeout` stays at 5 s. The background writer flushes within it (§4).

### 3.6 Content delivery to the TV

**Problem.** The TV is an exported Godot build. If a game's maps, sprite sheets and dialogue are compiled into it, every weekly expansion means re-exporting and reinstalling the TV app, and a TV whose content doesn't match the server draws the wrong map.

**Design.**
- **On disk:** `content/<gameId>/<packId>/…` in the repo, copied into the server's publish output. The pack's **hash** is a SHA-256 over its sorted `(relativePath, sha256(file))` list, computed at startup.
- **Index:** `GET /content/<gameId>/index.json` returns `[{packId, hash, files:[{path, sha256, bytes}]}]`.
- **Files:** `GET /content/<gameId>/<packId>/<hash>/<path>` responds with `Cache-Control: public, max-age=31536000, immutable`. A request for an unknown hash returns 404, and the path must resolve inside the pack directory (traversal guard).
- **Telling the TV:** new client method `ContentRequired(gameId, packs[{packId, hash}])`, sent to the host when a game is selected, when it starts, and on `ResumeHost`.
- **Host.Core** adds `ContentCache(string root, HttpClient http)` with `Task<string> EnsureAsync(string gameId, string packId, string hash, IProgress<double>?, CancellationToken)`. It returns a local directory. It downloads into `<root>/.tmp-<guid>`, verifies every file's SHA-256, then renames the directory atomically to `<root>/<gameId>/<packId>/<hash>`, so a partial download can never be read. Old hashes are pruned, keeping the newest 3 per pack.
- **Host shell:** while a required pack isn't cached, it shows a "Loading content… 42%" card before `ShowGame`. Scenes find their files through `IContentScene.ContentRoot(packId)`.
- A game with no packs (REDLINE, Counter) gets an empty list and nothing changes for it.

### 3.7 Host signals

**Problem.** The host connection can manage the room but can't talk to the game. A game paces its presentation with timers that guess how long the TV takes. REDLINE does this for the question read-out (`RedlineRules.cs:69`, `600 + words × 360 ms`). A tactics enemy phase has animation chains of varying length, where a wrong guess either overlaps events or stalls the game.

**Design.**

```csharp
namespace ArcBox.Sdk;

/// <summary>Opt-in: receives messages from the TV. The TV reports; the game stays authoritative.</summary>
public interface IHostAware
{
    void OnHostSignal(string kind, PlayerInput payload);       // payload.Read<T>() as for player input
}
```

- **Hub:** `HostSignal(string kind, JsonElement payload)` is host-only (others get "Only the host can do that."), shares the connection's rate limiter, takes a `kind` of 1–32 characters `[A-Za-z0-9_.-]`, and accepts a payload of at most 4 KB.
- **Room:** if the game is `IHostAware`, the signal goes through `GameSession.HostSignal` (guarded and published like input). Otherwise it's dropped at Debug level.
- **Host.Core:** `HostClient.SignalAsync(string kind, object payload)`. **Host shell:** scenes implementing `ISignalingScene` get an `Action<string, object> Signal` from the shell.
- **Convention (documented in `docs/authoring-a-game.md`):** a signal only ever **shortens** a wait. The game always keeps a fallback timer, so a crashed, slow or disconnected TV can't stall it. Signals carry the sequence number of the event they acknowledge (`{ "seq": 41 }`), and the game ignores stale ones.

---

## 4. Storage

- **Engine:** SQLite through `Microsoft.Data.Sqlite`, with no ORM. The schema is six small tables (below), and the campaign document is a JSON blob. EF Core's change tracking and migrations would be more machinery than the data needs.
- **Location:** `Storage:Path`, defaulting to `arcbox.db` in the content root; on Fly it's `/data/arcbox.db` on a volume. Tests use one in-memory database per factory (`Data Source=file:<guid>?mode=memory&cache=shared`).
- **Schema** (migrations are numbered SQL scripts applied at startup and tracked with `PRAGMA user_version`; `journal_mode=WAL`):

| Table | Columns |
|---|---|
| `profiles` | `id`, `created_utc`, `last_seen_utc` |
| `profile_devices` | `token_hash` (PK), `profile_id`, `added_utc` |
| `link_codes` | `code` (PK), `profile_id`, `expires_utc` |
| `rooms` | as in §3.5 |
| `campaigns` | `id`, `code` (unique), `game_id`, `name`, `owner`, `members_json`, `invite_hash`, `document`, `version`, `updated_utc` |
| `campaign_snapshots` | `id`, `campaign_id`, `version`, `label`, `document`, `created_utc` |

- **Background writer:** `StorageWriter : BackgroundService` owns a single `Channel<WriteOp>` and one connection. Pending writes are **combined per key**: ten campaign saves queued during one slow disk write become a single write of the newest document. It flushes every 250 ms or when 64 operations are pending, and on `StopAsync` it drains the channel within the shutdown timeout. Its health check (in `/healthz`) reports `degraded` if the queue is older than 10 s.
- **Reads** at startup: campaigns and profiles load into memory, and rooms load lazily (§3.5).
- **Backups:** a daily `VACUUM INTO '/data/backups/arcbox-<date>.db'`, keeping 14 days. Off-site backup is out of scope.

---

## 5. Protocol changes

| Direction | Added | Kept for one release |
|---|---|---|
| Phone → server | `JoinRoom(JoinRequest)`, `RejoinRoom(RejoinRequest)` | `Join`, `Rejoin` |
| Host → server | `HostSignal(kind, payload)` | |
| Server → host | `ContentRequired(gameId, packs)` | |
| Widgets | `custom` (`CustomView` / `CustomInput`) | |
| HTTP | `/api/profiles/*`, `/api/campaigns/*`, `/content/*` | |

`protocol.ts` and the C# contract fixtures are updated together. The contract tests catch any drift.

---

## 6. Testing

| Change | Tests |
|---|---|
| Profiles | Hub integration: a join creates a profile; a second join with the same token rebinds the seat; a mismatched token is rejected on rejoin; link codes expire and are single-use; a phone with no token behaves as today |
| Late join | `GameHarness` and integration: an opted-in game seats a mid-game joiner; REDLINE and Counter still reject; a crash in `OnPlayerJoined` ends the game |
| CustomView | Sdk serialization and contract fixture; Vitest: registry lazy-load, fallback card, `send` produces `CustomInput`; every existing REDLINE skin test passes from its new path |
| Campaigns | Service unit tests (leases, idempotent re-checkout, version conflicts, snapshot pruning, limits); HTTP integration tests per endpoint, including `409` while leased |
| Durable rooms | Integration: create, join, **dispose the factory, start a new one on the same database**, then `ResumeHost`/`Rejoin` land in the same seats; REDLINE comes back in the lobby with the "interrupted" `GameEnded`; a resumable test game gets `Resume`; parking and rebuild after `IdleTimeout`; a watchdog close deletes the row; secrets are stored only as hashes |
| Content | Pack hashing is deterministic; traversal is rejected; `ContentCache` resumes after an interrupted download, rejects a bad SHA, renames atomically and prunes |
| Host signals | Host-only check, kind and size validation, dropped for games that aren't `IHostAware`, guarded the same as input |
| Storage | Migrations run on an empty database and are idempotent; the writer combines writes per key and flushes on stop |
| End to end | `tools/tv-e2e.sh` gains `ARCBOX_RESTART=1`: restart the server mid-race and check that the TV and bot phones return to the same room's lobby |

---

## 7. REDLINE work

Tracked as GitHub issues labelled `redline` and `platform-v2`:

1. **Move the phone skin into the `games/redline` module** (required, part of §3.3). No visible change; every skin test passes from the new path.
2. **Handle a race interrupted by a server restart** (required, verifies §3.5). TV and phones show "Game interrupted: the server restarted." then the lobby with the same code, covered by `tv-e2e.sh` with `ARCBOX_RESTART=1`.
3. **Pace narration and race animations with host signals** (optional, uses §3.7). The TV sends `NarrationDone` and `AnimationDone`, with the current timers kept as fallbacks.

---

## 8. Out of scope
- Accounts, email, passwords or OAuth. Device tokens and link codes are the whole identity model.
- Storing or resuming game state on the platform. Games own their durable state, through campaigns or otherwise.
- Hot-reloading content packs without a deploy. Durable rooms make a deploy between or during sessions cheap.
- Multiple server instances or off-site backups.
- The tactics game, its portal screens and its content schema (separate specs).

## 9. Risks

| Risk | Mitigation |
|---|---|
| A device token copied from a phone gives full access to that profile | The same trust model as today's room secrets, which are equally bearer tokens. Link codes are one-time and expire in 15 minutes. The owner can revoke a member's devices (later, if needed) |
| SQLite on a single Fly volume is a single point of failure | Daily `VACUUM INTO` backups. Campaigns are small, so restoring is copying a file |
| A slow volume write stalls the game | Nothing writes on the actor: the background writer combines writes, and the health check reports a backlog |
| A stale phone or TV build after a deploy | The old hub methods are kept for one release; unknown `CustomView` screens show a Reload card |
| A rebuilt room resumes with everyone disconnected and the AI takes every seat | `IResumableGame.Resume` documents that games must wait for reconnects. The tactics spec will define a "Resuming…" phase |
