# Authoring a game

A game is an `IGame` (`src/ArcBox.Sdk`) registered on the server with `services.AddArcGame<MyGame>()`. The room calls
it one message at a time, so a game needs no locks, but every call must return quickly and do no I/O.

## Campaign games

A campaign game keeps a document from session to session (characters, inventory, story progress), and the same group
of people plays it week after week. The platform stores the document, its members and its snapshots; the game decides
what is in it.

**1. Set `UsesCampaigns` in the manifest.** The lobby then gets a campaign step: once your game is selected, the TV
lists its campaigns, the host or VIP picks one (or the VIP creates one and becomes its owner), and every phone picks
who it is. Start stays disabled until every connected phone is a member.

```csharp
public GameManifest Manifest { get; } = new("tactics", "Tactics", "Weekly co-op", MinPlayers: 2, MaxPlayers: 6,
    UsesCampaigns: true);
```

`MinPlayers` and `MaxPlayers` count **every member**, including the ones who aren't here tonight. A campaign holds at
most 8 members.

**2. Implement `ICampaignGame`.** The room calls its `Start` instead of `IGame.Start` and hands you the campaign's
lease:

```csharp
public void Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign)
{
    _campaign = campaign;
    _state = Load(campaign.Document);
}
```

- `campaign.Document` is your JSON; `campaign.Info` has the name, the members and the version.
- `campaign.Save(doc)` replaces the document (version + 1) and the write happens in the background, so it is cheap.
  `campaign.Checkpoint(doc, "End of session 7")` also records a snapshot the owner can roll back to. Documents are
  limited to 2 MB.
- Keep the lease for the whole game, but never release it yourself: the room checks the campaign back in when the
  game ends, when it crashes and when the room closes. After that, `Save` does nothing.
- Every `Player` has `Profile` set: that is the person, stable across weeks, so key your characters by it, not by
  `PlayerId` (which is per room).

**3. Register `ICampaignRules`** with `services.AddCampaignRules<MyRules>()`. The rules are the game's code for
documents outside a session:

- `NewDocument(info)`: the document of a brand-new campaign.
- `CardFor(info, document, member)`: how a member looks in the phone's "Who are you?" grid and on the TV roster
  (`new SeatCard("Aldric", "Dwarf Cleric · Lv 3")`).
- `PortalView` and `Handle`: what a member sees, and the commands they can run, in the between-sessions portal. The
  portal is locked while a session runs, so `Handle` and your game never edit the document at the same time.

Rules run inside the platform's locks: keep them quick and pure.

**4. Handle absent members as disconnected players.** Every member gets a seat when the game starts. The members who
aren't in the room are in `players` like everyone else, and straight after `Start` you get
`OnPlayerConnectionChanged(id, false)` for each of them. Give those seats to your AI. A late arrival who takes a seat
back arrives as `OnPlayerConnectionChanged(id, true)`; it's the same call a phone gets after a dropped connection.

**5. Never hand every seat to the AI on `Resume`.** If your game also sets `ResumesAfterRestart`, `IResumableGame.Resume`
gets every player disconnected, because nobody has reconnected yet after the restart. Wait for them, for example with a
"Resuming…" phase, and only give the AI the seats of people who were absent before the restart.

## Host signals

A game paces its presentation with timers that guess how long the TV takes: a question read-out, an animation chain.
A game can instead let the TV say when it is done. Implement `IHostAware` (in `ArcBox.Sdk`):

```csharp
public sealed class MyGame : IGame, IHostAware
{
    public void OnHostSignal(string kind, PlayerInput payload)
    {
        if (kind != "AnimationDone") return;
        var ack = payload.Read<Ack>();            // record Ack(int Seq)
        if (ack.Seq != _animationSeq) return;     // stale: ignore it
        ctx.CancelTimer("animation-fallback");
        NextStep();
    }
}
```

The TV sends a signal with `HostClient.SignalAsync(kind, payload)`. The server runs `OnHostSignal` like `OnInput`: on the room's own thread, guarded (a
throw ends the game with "Game crashed"), and followed by a view publish. A game that isn't `IHostAware` never sees
signals; the server ignores them without an error.

The convention:

- **A signal only ever shortens a wait.** The TV reports and the game stays authoritative. Never make progress depend on
  a signal arriving.
- **Always keep a fallback timer.** Start the wait as a timer exactly as before; a signal only makes it end sooner. A
  crashed, slow or disconnected TV then can't stall the game.
- **Include `seq` and ignore stale signals.** Number the events you ask the TV to present, and have the TV send back the
  number it is acknowledging (`{ "seq": 41 }`). Ignore a signal whose `seq` isn't the one you are waiting for: it
  belongs to a wait that already ended, and acting on it would skip the current one.

Limits: `kind` is 1-32 characters from `A-Z a-z 0-9 _ . -`, and the payload is at most 4096 bytes of JSON. Only the
host connection can signal; anything else is rejected with "Invalid signal." (or "Only the host can do that.").

## Content packs

Maps, sprite sheets and dialogue don't have to be compiled into the TV app. Put them in a **content pack** and the
server delivers them, so a weekly expansion is a server deploy rather than a new TV install.

**Layout.** Packs live in the repo under `content/<gameId>/<packId>/…`; any files and subfolders are fine:

```
content/
  tactics/
    base/
      maps/arena.json
      sprites/hero.png
    expansion-1/
      maps/ruins.json
```

The build copies `content/` into the server's output and publish folders, and the server scans `Content:Root` (default
`content`; Development points it at `../../content`) when it starts. A game with no folder, or no packs (REDLINE,
Counter), simply has none. A root that doesn't exist is an empty registry, not an error.

**The hash is immutable.** Each pack has a hash: the SHA-256 of its files' sorted `(path, file hash)` list, computed at
startup. Files are served at `/content/<gameId>/<packId>/<hash>/<path>` with `Cache-Control: public, max-age=31536000,
immutable`, so a given URL's bytes must never change. **New content means a new hash**: edit or add a file, deploy, and
the pack gets a new hash and so a new URL. Don't try to patch a file under an old hash; an unknown hash is a 404, and
nothing cached against the old one is ever reused for new content. The same URLs work from phones (for example a
`SeatCard.Portrait`), and `GET /content/<gameId>/index.json` lists a game's packs with each file's path, SHA-256 and size.

**How the TV gets them.** The server sends `ContentRequired(gameId, [{packId, hash}])` to the host when a game is
selected, when it starts, and when a host connects to a room that has a game selected (including after a server restart).
The list is empty for a game with no packs. The TV's content cache (landing with the TV changes) downloads any pack it
doesn't have into a temporary folder, verifies every file's SHA-256, then renames it into place, so a partial download is
never read; it keeps the newest 3 hashes per pack. While a pack is missing the TV shows "Loading content… 42%" before it
shows the game, and a scene finds its files through `IContentScene.ContentRoot(packId)`. Your game doesn't have to do
anything to make this happen.
