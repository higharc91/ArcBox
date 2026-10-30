# ArcBox Platform v2: Design Spec

- **Date:** 2026-10-29
- **Status:** Draft, awaiting review (rev 2: identity reworked around a "Who are you?" seat picker)
- **Author:** higharc91 with Claude
- **Amends:** [ArcBox platform design](2026-10-18-arcbox-platform-design.md). It moves persistence out of §10 (out of scope) into the platform, and changes the join flow in §5.2.
- **Driven by:** the campaign tactics RPG (working name *ArcBox Tactics*). Its mockups are in [`docs/superpowers/mockups/tactics/`](../mockups/tactics/), and the join screens are in [`06-phone-join.png`](../mockups/tactics/06-phone-join.png). The game's own spec comes separately.

---

## 1. Purpose

ArcBox was built for party games that last one evening: rooms live in memory, players are per-room seats, and phones are generic widget renderers. The next game is a **persistent co-op campaign**. It's a speed-based tactics RPG that a group of 4–6 plays for about 2 hours a week. Each player keeps a character from week to week, the AI plays the character of anyone who's away, and new story content arrives weekly.

That needs seven platform features. Each one is generic, so the next persistent game gets it for free, and several of them make REDLINE better straight away.

### Decisions already made (from the brainstorming session)
- The campaign lives in a **platform campaign service**, with a **between-sessions portal** on the phone (option B).
- Loot is split by **need/greed rolls**, and absent players pass automatically. (That's game rules, recorded here for context.)
- **Rejoining each week must take one tap or one pick.** Phones never type a code-and-name to get their character back. A phone picks *who you are* from the room's roster, and mid-game a phone that lost its seat picks from the **offline** seats.
- All seven changes ship as **sub-project 0, "Platform v2"**, before any tactics code.

### Success criteria
1. In a campaign room, a phone that has played before sees **"Continue as Dave · Aldric"** and is seated with one tap. A new or wiped phone picks Dave from a list (§3.1).
2. Mid-game, in **any** game, a phone with no seat (a new phone, cleared storage, a late arrival) sees the offline seats and takes one. In a campaign, absent members' seats are offline seats, so a late arrival takes their character back from the AI (§3.2).
3. A game can drive its own phone screens through `CustomView`. REDLINE's phone looks and behaves exactly as before (§3.3).
4. A campaign document survives a server restart. While a room has it checked out, the portal can read it but not change it (§3.4).
5. Killing the server mid-game and restarting it brings back **the same room code and seats**, and phones reconnect without doing anything. A resumable game picks up again; REDLINE returns to the lobby with a "Game interrupted" note (§3.5).
6. A content-only change (new map or dialogue) reaches the TV without re-exporting the Godot app (§3.6).
7. A game can advance when the TV reports that an animation finished, and still advances on its fallback timer if the TV never reports (§3.7).
8. `dotnet test ArcBox.slnx`, `npm test` in `web/play` and `tools/tv-e2e.sh` stay green.

---

## 2. Summary of changes

| # | Change | Layers touched | REDLINE impact |
|---|---|---|---|
| 1 | **Profiles and the "Who are you?" picker** | Sdk, Server, phone, Host shell (lobby) | None: REDLINE rooms have no campaign, so the lobby still asks for a name |
| 2 | **Taking an offline seat mid-game** (any game) and **campaign rooms** (the lobby picks a campaign; every member gets a seat) | Sdk, Server, phone, Host shell | A phone that lost its seat mid-race can take it back ([REDLINE issue](#7-redline-work)) |
| 3 | **`CustomView` and game modules on the phone** | Sdk, phone | Its skin moves to `games/redline/` with no change in behavior ([REDLINE issue](#7-redline-work)) |
| 4 | **Campaign service and portal API** | Sdk (contracts), Server (new `Campaigns/`), phone | None |
| 5 | **Durable rooms:** room records persist, are rebuilt after a restart, and idle rooms are parked | Sdk, Server | Interrupted games return to the lobby with the same code ([REDLINE issue](#7-redline-work)) |
| 6 | **Content delivery** from server to TV | Server, Host.Core, Host shell | None: REDLINE has no packs |
| 7 | **Host signals:** TV → game messages such as "animation done" | Sdk, Server, Host.Core, Host shell | Optional adoption for pacing ([REDLINE issue](#7-redline-work)) |

Changes 1, 4 and 5 share one storage layer (§4).

---

## 3. Design

### 3.1 Profiles and the "Who are you?" picker

**Idea.** Identity comes from **picking yourself in the room**, not from typing anything. The trust anchor is the room itself: you have to be there, with the code that's on the TV. Picking yourself also leaves a **profile key** on the phone, so next week it's one tap, and the same key unlocks the between-sessions portal (§3.4). This is the same trust model as today's room secrets, which suits a group of friends.

**Profiles.**
- A **profile** is a person across rooms and campaigns: `id` (opaque, `pr_` + 16 random base64url characters) and `display_name`.
- A **profile key** is a 128-bit random base64url credential issued to *one device* when that device picks the profile. The server stores only its SHA-256 hash (table `profile_keys`). Each profile keeps its 10 newest keys, and older ones are pruned.
- The phone keeps its keys in localStorage `arcbox:profiles` as `[{profileId, key, name}]`. That's a list, so a shared family tablet can hold more than one person.
- Games see the profile on `Player`:

```csharp
public readonly record struct ProfileId(string Value);          // opaque; never a credential
public sealed record Player(PlayerId Id, string Name, bool IsVip, ProfileId? Profile = null);
```

**The phone's join flow** (replaces typing a code and name in campaign rooms; REDLINE rooms keep the name box):

1. The phone opens `/?room=CODE` from the QR code and calls **`Peek`**. This creates nothing and returns what the room can offer this phone:

```csharp
public sealed record PeekRequest(string Code, IReadOnlyList<string>? ProfileKeys = null);

public sealed record RoomPeek(
    string Code,
    string? GameId,
    string? CampaignName,                      // set in campaign rooms
    bool InGame,
    IReadOnlyList<SeatOffer> Offers,           // what this phone may take, in display order
    SeatOffer? Suggested,                      // the offer matching one of this phone's profile keys, if any
    bool CanJoinNew);                          // the "I'm new" / name box is allowed

public sealed record SeatOffer(SeatRef Ref, SeatCard Card, bool Absent);
/// <summary>Exactly one is set: an existing (offline) seat, or a campaign member who has no seat yet.</summary>
public sealed record SeatRef(PlayerId? Slot = null, ProfileId? Member = null);
/// <summary>How a seat is shown. The game supplies it in campaign rooms ("Aldric", "Dwarf Cleric · Lv 3").</summary>
public sealed record SeatCard(string Title, string? Subtitle = null, string? Color = null, string? Portrait = null);
```

2. The phone shows one of the screens in `06-phone-join.png`:
   - **Remembered:** `Suggested` is set → "Continue as Dave · Aldric" takes one tap, with "Not you?" below it to open the list.
   - **Who are you?** → a grid of `Offers` (portrait, character, player name), plus "I'm new here" when `CanJoinNew` is true.
   - **No campaign, lobby** (REDLINE today) → the name box, as now.
   - **Mid-game** → "Pick your seat": the offline seats. If there are none, the phone shows "Game in progress, join after this round".
3. The phone calls **`TakeSeat`**:

```csharp
public sealed record TakeSeatRequest(string Code, SeatRef? Seat = null, string? NewName = null,
                                     IReadOnlyList<string>? ProfileKeys = null);
public sealed record JoinResult(string Code, PlayerId PlayerId, string Secret, ProfileKey? Profile = null);
public sealed record ProfileKey(ProfileId Id, string Key, string Name);   // only on a profile-backed seat
```

**Rules.**
- **What can be taken.** Exactly one of `Seat` or `NewName` is set. A seat that is **connected** can never be taken ("Someone's already playing as Aldric."). An offline seat or an unseated member can be taken by anyone in the room.
- **Taking an offline seat rotates its secret.** The old phone's stored secret stops working, and the new phone gets a new one (secrets are stored only as hashes, §3.5). If the old phone comes back, its `Rejoin` fails and it lands on the picker.
- **Profile keys.** Taking a profile-backed seat issues a new key to *this* phone and returns it in `JoinResult.Profile`. If a key in `ProfileKeys` already matches that profile, no new key is issued.
- **Remembered seats.** A `Peek` whose keys match an offline member or seat puts it in `Suggested`. If two match (a shared tablet), the most recently used one is suggested and both stay listed.
- **"I'm new here"** in a campaign lobby creates a profile and a campaign membership (role `Player`) under the typed name. New members can join **only while the game isn't running**. Mid-game, `CanJoinNew` is false in campaign rooms.
- **Rejoin is unchanged.** A phone that still has its seat secret (`arcbox:<CODE>`) calls `Rejoin` as today, and the picker only appears when that fails or there's no secret. Any game can take offline seats; §3.2 covers the campaign specifics.
- **Legacy calls:** `Join(code, name)` maps to `TakeSeat(NewName: name)`. It stays for one release so a stale phone tab still works.
- **HTTP authentication** (portal): header `Authorization: Profile <key>`, matched by hash and rate limited per IP. There are no cookies, so there's nothing for CSRF to exploit.

### 3.2 Offline seats and campaign rooms

**Offline seats (any game).** Today `Room.JoinAsync` rejects anyone who joins mid-game. Now, when a game is running, `Peek` offers every seat that's **not connected**, and `TakeSeat` rebinds it. That's the existing rejoin path, so the game just gets `OnPlayerConnectionChanged(id, true)`. **No new game interface is needed**, and REDLINE gets the benefit straight away: a phone whose battery died mid-race can be swapped for another phone.

**Campaign rooms.** A game whose manifest sets `UsesCampaigns` gets a campaign step in the lobby:

```csharp
public sealed record GameManifest(string Id, string Name, string Description, int MinPlayers, int MaxPlayers,
                                  bool UsesCampaigns = false, bool ResumesAfterRestart = false);
```

- **Choosing the campaign.**
  - After the game is selected, the TV lobby lists the game's campaigns plus "New campaign".
  - The host or the VIP calls `SelectCampaign(campaignId)`, or `CreateCampaign(name)`; the VIP's phone types the name. The VIP becomes the owner.
  - Selecting a campaign that's already leased to another live room is refused ("Embers of the Vale is being played in room KQZT.").
- **The roster.** Once a campaign is selected, `LobbyState` carries its roster, so the TV shows who's here and who's away:

```csharp
public sealed record LobbyCampaign(string Id, string Code, string Name, IReadOnlyList<RosterEntry> Roster);
public sealed record RosterEntry(ProfileId Profile, SeatCard Card, PlayerId? SeatedAs, bool Connected);
// LobbyState gains: LobbyCampaign? Campaign, IReadOnlyList<CampaignSummary>? Campaigns (to pick from)
// LobbyPlayer gains: ProfileId? Profile
```

- **Phones already in the room** whose seat has no profile in this campaign (say they joined before it was picked) see **"Who are you in Embers of the Vale?"**, and answer with `ClaimMember(ClaimMemberRequest{Member, ProfileKeys, NewName})`: a member, or "I'm new here" (a null `Member`, under `NewName` if given). The VIP's Start button stays disabled until every *connected* seat is matched to a member.
- **Starting.**
  - The room checks out the campaign (§3.4) and creates a **disconnected seat for every member not present**, so every member is a `Player` from the start.
  - It passes the lease to the game, then reports each absent seat through the existing `OnPlayerConnectionChanged(id, false)`.
  - The game hands those seats to its AI. A late arrival taking one back is just `OnPlayerConnectionChanged(id, true)`.

```csharp
namespace ArcBox.Sdk.Campaigns;

/// <summary>Implemented by games whose manifest sets UsesCampaigns. The room calls this instead of IGame.Start.</summary>
public interface ICampaignGame
{
    void Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign);
}
```

- **The game owns the lease** until the game ends. The room then disposes it, which checks the campaign back in, so a crashed game can't leak it.
- **Player count:** the manifest's min/max counts **all** seats, absent ones included, and `RoomOptions.MaxPlayers` (8) still caps the roster. A campaign with more than 8 members is refused at selection ("This campaign has 9 members; rooms hold 8.").

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
  portal?: ComponentType<PortalProps>;                      // the game's between-sessions screens (§3.4)
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
- The module chunk starts loading on `LobbyChanged` once a game is selected, so the first custom screen doesn't wait on the network. Until it has loaded, the phone uses the generic look, the same as a game without a skin.
- **Contract test:** C# writes a `custom` fixture, and Vitest checks that `parseControllerView` accepts it and that the registry resolves `redline`.
- **REDLINE:** `skins/redline.ts` and its tests move to `games/redline/` and are exported as `module.skin`. `skinFor(gameId)` becomes `moduleFor(gameId)?.skin`.

### 3.4 Campaign service and portal API

**Responsibilities.** The platform owns campaigns, members, invites, versions, check-out, snapshots and storage. The **game owns the document**, an opaque JSON value whose schema and rules belong to the game.

**Contracts in `ArcBox.Sdk.Campaigns`.** Game projects reference only the Sdk, so the types games touch live there. The server implements them.

```csharp
public sealed record CampaignInfo(
    string Id, string Code, string GameId, string Name,
    ProfileId Owner, IReadOnlyList<CampaignMember> Members, long Version);
public sealed record CampaignMember(ProfileId Profile, string DisplayName, CampaignRole Role, DateTimeOffset JoinedUtc);
public enum CampaignRole { Owner, Player }
public sealed record CampaignSummary(string Id, string Code, string Name, int Members, DateTimeOffset UpdatedUtc);

/// <summary>A room's hold on a campaign while its game runs. Every member is synchronous and does no I/O.</summary>
public interface ICampaignLease
{
    CampaignInfo Info { get; }
    JsonElement Document { get; }
    /// <summary>Replaces the document (Version + 1) and queues a background write.</summary>
    void Save(JsonElement document);
    /// <summary>Like Save, and also records a named snapshot (e.g. "End of session 7") for rollback.</summary>
    void Checkpoint(JsonElement document, string label);
}

/// <summary>The game's rules for its documents, used by the lobby picker and the portal.</summary>
public interface ICampaignRules
{
    string GameId { get; }
    JsonElement NewDocument(CampaignInfo info);
    /// <summary>How a member's seat looks in the picker and on the TV roster ("Aldric", "Dwarf Cleric · Lv 3").</summary>
    SeatCard CardFor(CampaignInfo info, JsonElement document, ProfileId member);
    /// <summary>The member's view of the campaign for the portal (the game decides what each member may see).</summary>
    JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member);
    /// <summary>Applies a portal command: a new document, or an error message for the member.</summary>
    CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data);
}
public sealed record CommandResult(JsonElement? Document, string? Error);
```

The server registers rules the same way it registers games: `services.AddCampaignRules<TacticsCampaignRules>()`. **A level-up or trade in the portal is therefore checked by exactly the rules code the session uses.**

**The server's `CampaignService`.**
- It keeps every campaign **in memory**: a few campaigns of up to a few hundred KB each. Reads are synchronous, so the room actor can use them without risking the 2-second watchdog. Writes update memory first, then queue a background write (§4).
- **Leases** belong to a room code and live only in memory. Checking out again from the same room code returns the same lease (idempotent, for durable rooms §3.5). After a restart there are no leases, and the rebuilt room checks out again.
- While a campaign is leased, portal **commands** return `409 Conflict` with "A session is in progress. Use the in-game menu." Portal **reads** keep working.

**HTTP API** (authenticated with `Authorization: Profile <key>` from §3.1, JSON both ways):

| Method and path | Who | Does |
|---|---|---|
| `GET /api/me` | any key | The caller's profile and campaigns |
| `GET /api/campaigns/{code}` | member | `CampaignInfo` plus `PortalView(...)` |
| `POST /api/campaigns/{code}/commands` `{type, data, version}` | member | Runs `Handle`. Returns `409` on a version mismatch or an active lease |
| `POST /api/campaigns/{code}/members/{profileId}/forget-devices` | owner | Revokes all of that member's profile keys (a lost phone). The member picks themselves again next session |
| `DELETE /api/campaigns/{code}/members/{profileId}` | owner | Removes a member |
| `GET /api/campaigns/{code}/snapshots`, `POST …/rollback {snapshotId}` | owner | Lists snapshots and restores one (not while leased) |

Campaigns are **created and joined in the room** (§3.2), never through invite links, so the portal has no create or join endpoints.

**Rules.**
- **Optimistic concurrency:** each command carries the `version` the phone last saw. A mismatch returns `409` and the current view, so the phone can redraw and retry.
- **Snapshots:** every `Checkpoint` records one, and a `Save` records one at most every 10 minutes. The last 50 per campaign are kept.
- **Limits:** a document can be at most 2 MB (rejected with a logged error), each profile can own at most 20 campaigns, and each campaign can have at most 8 members (the room size).
- **Portal UI:** the phone route `/c/<code>` in `web/play` renders the game module's `portal` component. This spec covers its shell: authentication from `arcbox:profiles`, loading, `409` handling, and a "Pick yourself in a session first" card when the phone has no key for this campaign. The screens themselves belong to each game's spec.

### 3.5 Durable rooms

**Problem.** A restart or redeploy wipes every room. A campaign checkpoint alone would still leave everyone re-joining with a new code. Separately, `IdleTimeout` (10 minutes) closes a room during a long break.

**Design.** Persist the room's **membership**, but never its game state. Rebuild the room on demand.

- The `rooms` table stores: `code` (primary key), `host_token_hash`, `slots_json` (`[{id, name, secretHash, joinOrder, profile, forAbsentMember?}]`; `forAbsentMember` marks a seat the room made at a campaign Start for a member who has never been in the room, and such seats go when the selected campaign changes), `next_slot_id`, `selected_game`, `campaign_id`, `game_running`, `created_utc` and `updated_utc`.
- **Secrets and host tokens are stored only as SHA-256 hashes**, and are compared hash-to-hash with `CryptographicOperations.FixedTimeEquals`. In memory, `PlayerSlot.Secret` becomes `SecretHash`, and `Secrets.Matches(string expectedHash, string provided)` hashes `provided` before comparing. `Secrets.Hash(string)` is new. A leaked database then gives an attacker no working credentials.
- **When rows are written:** on create, take-seat, kick, game or campaign selection, game start and game end. These are small writes through the background writer (§4).
- **Rebuilding:** when `RoomManager.Find(code)` misses memory, it loads the row (if `updated_utc` is within `RoomOptions.RetainFor`, default 24 h), recreates the `Room` with every slot disconnected, and starts its actor. Two simultaneous misses for the same code produce one room (`GetOrAdd` over a lazy entry).
- **A game that was running when the room was lost:**

```csharp
/// <summary>Implemented by games whose manifest sets ResumesAfterRestart. Called instead of Start.</summary>
public interface IResumableGame
{
    /// <summary>
    /// Rebuild from the game's own durable state. campaign is set for campaign games (re-checked-out by the room).
    /// Every player starts disconnected and reconnects through OnPlayerConnectionChanged. Wait for them (e.g. a
    /// "Resuming…" phase) rather than handing every seat to an AI.
    /// </summary>
    void Resume(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease? campaign);
}
```

  - If `ResumesAfterRestart` is set, the rebuilt room calls `Resume` and goes back to `InGame`.
  - Otherwise (REDLINE) the room comes back in the **lobby**. `LastResults = new GameResults([], "Game interrupted: the server restarted.")` is sent as a `GameEnded` to each connection the first time it binds after the rebuild, so the TV and phones show the existing results screen with that note, then the lobby. No client changes are needed.
- **Parking idle rooms:** reaching `IdleTimeout` now **removes the room from memory without deleting its row**. The next `ResumeHost`, `Rejoin`, `Peek` or `TakeSeat` rebuilds it, so a long break is harmless. Rows older than `RetainFor` are deleted by `RoomSweeper`.
  - A room parks **only when no connection is bound**: no host connection and no connected seat. While any phone is connected the room stays live in memory however long the TV is away, so a TV that sleeps through a break never sends the phones `RoomClosed`.
  - A parked room's row says so in a `slots_json` envelope, `{"slots": [...], "parked": true}`, rather than a new column (no migration). The rebuilt room uses the flag to say "Game interrupted: the room was idle." instead of "…the server restarted.", and saves the plain array again.
  - A lookup that finds a room already closed (parked, but not yet swept out of memory) treats it as a miss and rebuilds from the row.
- A **watchdog close** (hung game) still deletes the row, because a room whose game hung shouldn't come back. A campaign survives because its last save is already stored.
- `RoomCodes.Generate` also skips codes that exist in storage.
- **Clients:** no protocol change. `HostClient.ResumeAfterReconnectAsync` and the phone's `Rejoin` already retry against the same code; now the server answers instead of saying "Room not found."
- **Shutdown:** `HostOptions.ShutdownTimeout` stays at 5 s. The background writer flushes within it (§4).

### 3.6 Content delivery to the TV

**Problem.** The TV is an exported Godot build. If a game's maps, sprite sheets and dialogue are compiled into it, every weekly expansion means re-exporting and reinstalling the TV app, and a TV whose content doesn't match the server draws the wrong map.

**Design.**
- **On disk:** `content/<gameId>/<packId>/…` in the repo, copied into the server's publish output. The pack's **hash** is a SHA-256 over its sorted `(relativePath, sha256(file))` list, computed at startup.
- **Index:** `GET /content/<gameId>/index.json` returns `[{packId, hash, files:[{path, sha256, bytes}]}]`.
- **Files:** `GET /content/<gameId>/<packId>/<hash>/<path>` responds with `Cache-Control: public, max-age=31536000, immutable`. A request for an unknown hash returns 404, and the path must resolve inside the pack directory (traversal guard). Phones use the same URLs for portraits (`SeatCard.Portrait`).
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

- **Engine:** SQLite through `Microsoft.Data.Sqlite`, with no ORM. The schema is a handful of small tables plus a JSON document per campaign, and EF Core's change tracking and migrations would be more machinery than the data needs.
- **Location:** `Storage:Path`, defaulting to `arcbox.db` in the content root; on Fly it's `/data/arcbox.db` on a volume. Tests use one in-memory database per factory (`Data Source=file:<guid>?mode=memory&cache=shared`), so a test can "restart" the server by building a second factory on the same database.
- **Schema** (migrations are numbered SQL scripts applied at startup and tracked with `PRAGMA user_version`; `journal_mode=WAL`):

| Table | Columns |
|---|---|
| `profiles` | `id`, `display_name`, `created_utc`, `last_seen_utc` |
| `profile_keys` | `key_hash` (PK), `profile_id`, `issued_utc`, `last_used_utc` |
| `rooms` | as in §3.5 |
| `campaigns` | `id`, `code` (unique), `game_id`, `name`, `owner`, `members_json`, `document`, `version`, `updated_utc` |
| `campaign_snapshots` | `id`, `campaign_id`, `version`, `label`, `document`, `created_utc` |

- **Background writer:** `StorageWriter : BackgroundService` owns a single `Channel<WriteOp>` and one connection. Pending writes are **combined per key**: ten campaign saves queued during one slow disk write become a single write of the newest document. It flushes every 250 ms or when 64 operations are pending, and on `StopAsync` it drains what is queued, even when the token it is given is already cancelled (Kestrel can spend the whole shutdown timeout closing connections first), for at most 4 s, inside the 5 s shutdown timeout. If the cap is reached it logs how many writes may be lost. Its health check (in `/healthz`) reports `degraded` if the queue is older than 10 s.
- **Reads:** campaigns and profiles load into memory at startup; rooms load lazily (§3.5).
- **Backups:** a daily `VACUUM INTO '/data/backups/arcbox-<date>.db'`, keeping 14 days. Off-site backup is out of scope.

---

## 5. Protocol changes

| Direction | Added | Kept for one release |
|---|---|---|
| Phone → server | `Peek(PeekRequest)`, `TakeSeat(TakeSeatRequest)`, `ClaimMember(ClaimMemberRequest{Member, ProfileKeys, NewName})` | `Join(code, name)` |
| Host or VIP → server | `SelectCampaign(campaignId)`, `CreateCampaign(name)` | |
| Host → server | `HostSignal(kind, payload)` | |
| Server → host | `ContentRequired(gameId, packs)` | |
| `LobbyState` | `Campaign`, `Campaigns`; `LobbyPlayer.Profile` | |
| Widgets | `custom` (`CustomView` / `CustomInput`) | |
| HTTP | `/api/me`, `/api/campaigns/*`, `/content/*` | |

`Rejoin(code, playerId, secret)` is unchanged. `protocol.ts` and the C# contract fixtures are updated together, and the contract tests catch any drift.

---

## 6. Testing

| Change | Tests |
|---|---|
| Picker and profiles | Hub integration: `Peek` creates nothing; `Suggested` matches a stored key; a connected seat can't be taken; taking an offline seat rotates its secret and the old secret's `Rejoin` fails; a profile key is issued once per device; "I'm new" creates a member only in the lobby; legacy `Join` still works |
| Offline seats and campaign rooms | Integration: a mid-race phone with no seat takes an offline REDLINE seat and the game sees `OnPlayerConnectionChanged(true)`; with no offline seat the existing message is shown; campaign selection refused when leased elsewhere or over 8 members; Start disabled until every connected seat is matched; absent members get disconnected seats and the game receives the lease; the lease is released when the game ends or crashes |
| CustomView | Sdk serialization and contract fixture; Vitest: registry lazy-load, fallback card, `send` produces `CustomInput`; every existing REDLINE skin test passes from its new path; picker screens render each `RoomPeek` shape |
| Campaigns | Service unit tests (leases, idempotent re-checkout, version conflicts, snapshot pruning, limits); HTTP integration tests per endpoint, including `409` while leased and `forget-devices` revoking keys |
| Durable rooms | Integration: create a room, take seats, **dispose the factory, start a new one on the same database**, then `ResumeHost`/`Rejoin` land in the same seats; REDLINE comes back in the lobby with the "interrupted" `GameEnded`; a resumable campaign test game gets `Resume` with its lease; parking and rebuild after `IdleTimeout`; a watchdog close deletes the row; secrets are stored only as hashes |
| Content | Pack hashing is deterministic; traversal is rejected; `ContentCache` resumes after an interrupted download, rejects a bad SHA, renames atomically and prunes |
| Host signals | Host-only check, kind and size validation, dropped for games that aren't `IHostAware`, guarded the same as input |
| Storage | Migrations run on an empty database and are idempotent; the writer combines writes per key and flushes on stop |
| End to end | `tools/tv-e2e.sh` gains `ARCBOX_RESTART=1` (restart the server mid-race; the TV and bot phones return to the same room's lobby) and `ARCBOX_SWAP=1` (a bot phone clears its storage mid-race and takes its offline seat back through the picker) |

---

## 7. REDLINE work

Tracked as GitHub issues labelled `redline` and `platform-v2`:

1. **#23: Move the phone skin into the `games/redline` module** (required, part of §3.3). No visible change; every skin test passes from the new path.
2. **#24: Handle a race interrupted by a server restart, and a phone swapped mid-race** (required, verifies §3.5 and §3.2). TV and phones show "Game interrupted: the server restarted." then the lobby with the same code; a phone that lost its seat mid-race takes it back from "Pick your seat". Covered by `tv-e2e.sh` with `ARCBOX_RESTART=1` and `ARCBOX_SWAP=1`. **Pending:** the end-to-end runs (`tv-e2e.sh` plain, `ARCBOX_RESTART=1` and `ARCBOX_SWAP=1`) still have to be run on a machine with Godot.
3. **#25: Pace narration and race animations with host signals** (optional, uses §3.7). The TV sends `NarrationDone` and `AnimationDone`, with the current timers kept as fallbacks.

---

## 8. Out of scope
- Accounts, email, passwords, OAuth or PINs. Picking yourself in the room, plus the profile key it leaves on the phone, is the whole identity model. A per-member PIN is the obvious next step if the group ever needs one.
- New campaign members joining mid-game (they join in the lobby, between games).
- Storing or resuming game state on the platform. Games own their durable state, through campaigns or otherwise.
- Hot-reloading content packs without a deploy. Durable rooms make a deploy between or during sessions cheap.
- Multiple server instances or off-site backups.
- The tactics game, its portal screens and its content schema (separate specs).

## 9. Risks

| Risk | Mitigation |
|---|---|
| Anyone in the room can take an offline seat and, with it, that member's profile key | The same trust model as today (room code plus a name). Connected seats can't be taken, and the TV roster always shows who's seated where. The owner can `forget-devices`, and a PIN is the next step if ever needed |
| A shared phone holds several people's keys | Keys are listed per person, and "Not you?" is always one tap away |
| SQLite on a single Fly volume is a single point of failure | Daily `VACUUM INTO` backups. Campaigns are small, so restoring is copying a file |
| A slow volume write stalls the game | Nothing writes on the actor: the background writer combines writes, and the health check reports a backlog |
| A stale phone or TV build after a deploy | Legacy `Join` is kept for one release; unknown `CustomView` screens show a Reload card |
| A rebuilt room resumes with everyone disconnected and the AI takes every seat | `IResumableGame.Resume` documents that games must wait for reconnects. The tactics spec will define a "Resuming…" phase |
| Any room can list and select every campaign of a game, so anyone who reaches the server can take any member's seat, including the owner's, and get that profile's key | MUST be resolved before any game with UsesCampaigns is registered in production: scope campaign listing and selection to profiles proven in the room (e.g. keys presented on Peek/TakeSeat), or tie campaigns to the TV's credential. Tracked in a GitHub issue. |
