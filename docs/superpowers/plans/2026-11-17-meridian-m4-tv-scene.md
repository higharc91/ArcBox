> Status: approved 2026-11-17; in progress on branch `ccr-de425e76-4vxzw2`. Live task state belongs on GitHub issues (see the [live tracker index](../README.md)).

# Derelict: ISV Meridian — M4 TV Scene Implementation Plan

Steps use checkbox (`- [ ]`) syntax for tracking. Each task is test-first and ends in one commit.

**Goal:** Put the game on the TV. A Godot scene draws the ship top-down with placeholder tiles (or real art where the art packs have landed), presents every event in order and signals when each is done, plays the Breach as a full-screen sequence, has Tiller (the ship's computer) read the public text aloud, and ends on a Debrief that reveals everything. When this stage is done, a group with real phones and a TV can play a whole game: the first playable game night (spec §8).

**Architecture:**
- **The TV only animates.** Every rule, deadline and outcome is already decided by `Meridian.Game`. The scene reads `MeridianHostView` (already built from `Visibility.For(state, null)`), animates the change from the previous view, and signals `presented { seq }` when it has finished showing an event.
- **`games/Meridian/Meridian.Presentation`** is new (spec §5.1). It is the TV's view model with no Godot in it: deck layout and camera framing, which deck is on screen, the beat timeline for each presented event, when to signal, and what Tiller says. It references `Meridian.Game` for the host-view records, as `Tactics.Presentation` does, so all of it is unit-tested without Godot.
- **`src/ArcBox.Host/games/meridian/`** holds the Godot nodes. They are thin: each one draws a Presentation model. The scene implements `IGameScene`, `IContentScene`, `ISignalingScene`, `IAudioScene`, `IConnectionScene` and `IPreloadScene`.
- **Art is optional at runtime.** `MeridianArt` reads the art sets' manifests (`content/meridian/base/<set>/manifest.json`, the contract in #331, format as in draft PR #357). Any tile, hatch, crew member or token without art is drawn as a labelled placeholder at the same size, so M5 (art) only adds files.

**Spec:** [2026-11-13-meridian-design.md](../specs/2026-11-13-meridian-design.md), §3.1 (the TV column), §3.2, §3.3, §5.3, §5.5, §7, §8. **Earlier stages:** [M1](2026-11-13-meridian-m1-rules-core.md), [M2](2026-11-13-meridian-m2-playable-explore.md) and [M3](2026-11-13-meridian-m3-breach-engine.md), including their *Implementation notes*. **Platform:** `docs/tv-host.md`, `src/ArcBox.Host/Shell/IGameScene.cs` and the interfaces beside it.

**Not in this plan:**
- Final art and sound effects (M5). This plan draws placeholders, reads whatever art manifests exist, and plays no sound effects beyond Tiller's voice.
- Restart resume (M6). A TV that reconnects to a running game is in scope (Task 4, Task 11); a server restart is not.
- Scenarios 6–12 (M7). Their tokens will render through the same token layer: a token kind without art or a glyph is drawn as a labelled placeholder.
- Changing the round-limit finding from the M3 balance report. It is still the owner's decision.

## What M3 left that the TV needs

A read of the current host view against spec §3.1's TV column turned up six gaps. Task 2 closes them on the server side before any Godot work starts.

| Gap | Why it matters on a TV | Fix (Task 2) |
|---|---|---|
| **Fallback times are far shorter than reading time.** A card waits `1 + 0.06 × words` seconds (about 16 words a second). | Once Tiller reads cards aloud at about 2.7 words a second, a TV that signals late lets the game move on mid-sentence. The fallback is meant for a *missing* TV, so it must outlast a *present* one. | Every text presentation waits for its reading time at `MeridianRules.ReadingWordsPerSecond` (2.7, the rate REDLINE's reader assumes) plus a fixed lead. The dev board still signals early, so dev play isn't slowed. |
| **No "Waiting for…" on the TV.** The disconnect grace (Explore) and the endgame pause aren't in the host view. | Spec §3.3: the TV shows "Waiting for JO…". Without it a stalled room looks frozen. | `MeridianHostView.Wait`: who, why (`grace` or `pause`) and the deadline. The pause's text is the same for every seat, so it can't reveal a traitor (M3 Global constraints). |
| **The Breach has nothing public to announce.** The Breach and scenario events carry fixed one-liners. | Spec §3.1: "the ship's computer names the scenario and what everyone knows publicly". | Each scenario file gains an `announcement` (at most 60 words, public), validated by the loader. The `scenario` presentation carries it. Authors must not put a secret in it; review focus 3 adds a test. |
| **No round warnings.** | Spec §5.5: Tiller calls round warnings. | A `round` presentation at the start of the endgame's last three rounds and its last round. |
| **The Debrief's awards aren't on the TV.** Phones get them; the host view doesn't. | Spec §3.1: the TV's Debrief shows three awards. | `ScenarioPanel.Awards`, only once the game is over, like `Reveal`. |
| **No ready count during the briefing.** | The TV's "Read your phone" step should show how many are ready, so the room knows who it's waiting on in aggregate. | `MeridianHostView.Ready`, a count only during Briefing. It is a count, never which seats, so a slow reader isn't singled out. |

## Global constraints

- **Everything in M1–M3's Global constraints still holds**, above all: no view is built from the raw state, and hidden information never reaches the TV.
- **The scene decides nothing.** Godot code may not read `Meridian.Logic`'s rules (`Rules`, `Visibility`, scenarios) or run game logic. It reads only the host view, the content pack's tile and crew definitions (names, hatches) and the art manifests. A test (Task 1) checks that `ArcBox.Host`'s Meridian folder references nothing in `Meridian.Logic` but the content records and `PackLoader`.
- **All timing and layout logic lives in `Meridian.Presentation`**, and has unit tests. A Godot node that has an `if` about game state is a review comment.
- **Signals are idempotent and only ever acknowledge.** The TV sends `presented { seq }` once per `seq`, never for a `seq` it hasn't drawn, and never one higher than the view's current `Now`. The server already ignores stale values (M2).
- **Nothing secret is ever spoken.** Tiller's lines are a pure function of the host view, which is public. Task 5 runs the M3 secrecy harness over every spoken line too.
- **The art contract (#331) is the layout contract:**
  - 64 px tile sources shown at 2× (128 px) on a 1080p TV;
  - whole-multiple nearest-neighbour scaling only;
  - hatches as per-edge overlays rotated in 90° steps;
  - 24 × 24 characters anchored at the feet.

  Placeholders use exactly these metrics, so swapping in art moves nothing.
- **Readability from a sofa** (spec §5.5, #331): at 1080p, body text at least 28 px, labels at least 22 px. Every seat is shown by its colour *and* its initial, never colour alone.
- **Budgets:** the host view stays under 8 KB and an Explore step under one packet as a patch (spec §5.3) after Task 2's additions. The scene holds 60 fps at the `low` preset on the reference TV hardware with a 45-compartment ship. On this repo's Xvfb + software-Vulkan runs, the measure is no frame over 100 ms; it guards against pathological redraws, not speed.

## Review focus

Situations a main-path test would not reach. Each gets a pinned test in the task in brackets.

1. **A TV slower than the fallback.** The game moves on while the TV is still mid-beat. The TV drops the rest of that beat, catches up to the new view, and never signals the skipped `seq`. [Task 4]
2. **A TV that reconnects mid-presentation** (or joins a running game). The first view after it is a fresh look: no history is replayed. The current `Now` is shown in its short form and signalled once. [Tasks 4, 11]
3. **Secret text spoken or shown by the Breach sequence.** For each S1 scenario at 3–6 players, no announcement or spoken line contains a secret role's id or name or a secret objective's text. [Task 5]
4. **Reading time against the fallback.** For every card and every scenario announcement in the pack, the fallback is at least Tiller's estimated speaking time plus the lead. [Task 2]
5. **A deck with crew but nothing new on it.** Example: someone takes the lift to a deck holding only its landing. Framing still shows the landing and the crew on it, and never zooms in past 2×. [Task 3]
6. **Rotation.** For each of the four rotations of a tile, the hatch overlays the scene draws are exactly the sides `TileDef.HatchesAt(rotation)` returns, placeholders and art alike. [Tasks 3, 6]
7. **The endgame pause for an innocent.** The TV's waiting banner reads the same whichever living seat dropped. [Task 2]
8. **The TV killed during the Breach sequence** (spec §7.2). The game finishes the briefing on its fallback timers. When the TV comes back it shows the endgame, not the Breach again. [Task 11]

---

## File map

```
games/Meridian/Meridian.Presentation/            (new)
  Meridian.Presentation.csproj                   references Meridian.Game
  Art/MeridianArt.cs                             reads the art manifests; null for anything without art
  Board/DeckLayout.cs                            cell → pixels, deck bounds, the active deck, camera framing
  Board/TileDraw.cs                              what to draw for one compartment: art or placeholder, hatch overlays
  Beats/Beat.cs, Beats/Director.cs               a timeline per presented event; when to signal
  Hud/HudModel.cs                                seat strip, counters, round clock, scenario panel, waiting banner
  Breach/BreachSequence.cs                       the Breach's full-screen steps and timing
  Debrief/DebriefModel.cs                        outcome, roles, awards, full map
  Audio/TillerLines.cs                           what Tiller says, from the host view only
  Demo/DemoViews.cs                              reads a recorded game for the --meridian-demo dev mode
games/Meridian/Meridian.Presentation.Tests/      (new) one test file per folder above, plus HostViewContractTests
games/Meridian/Meridian.Game/                    Task 2: Wait, awards, announcement, round warnings, reading time
games/Meridian/Meridian.Logic/MeridianVoices.cs  (new) the meridian.tiller voice theme
content/meridian/base/rules/scenarios/*.json     Task 2: an "announcement" each (and the schema)
src/ArcBox.Host/games/meridian/                  (new) MeridianScene.tscn/.cs and the nodes in Tasks 6–9
src/ArcBox.Host/games/meridian/Demo/*.jsonl      recorded games (host views as patches), one per scenario
src/ArcBox.Host/Shell/GameSceneMap.cs            "meridian" → the scene
src/ArcBox.Host/Shell/AppArgs.cs, Main.cs        --meridian-demo[=<scenario>]
src/ArcBox.Server/Games/Meridian/…Extensions.cs  AddVoiceThemes(MeridianVoices.All)
tools/tv-e2e.sh                                  ARCBOX_GAME=meridian (+ ARCBOX_SCENARIO, ARCBOX_RECONNECT)
docs/tv-host.md, docs/playtest-checklist.md      the Meridian TV
docs/superpowers/reports/meridian-tv/            screenshots per phase, at 1280×720 and 1920×1080
```

---

## Tasks

### Task 1: The Presentation project, the scene's skeleton and the contract test

**Files:**
- Create `Meridian.Presentation` and its tests project, and add both to `ArcBox.slnx`.
- Create `src/ArcBox.Host/games/meridian/MeridianScene.{tscn,cs}`.
- Modify `GameSceneMap.cs` and `ArcBox.Host.csproj`.

- **Contract test** (as in `Tactics.Presentation.Tests/HostViewContractTests`):
  - Play a real session with bots through every phase: Boarding, Explore, Briefing, Endgame and Debrief, in each S1 scenario.
  - Serialize each host view the way the server does, and read it the way the scene will (`Deserialize<MeridianHostView>(ArcJson.Options)`).
  - Check that each view becomes a screen model.
  - Also check that the three host fixtures from M2 and M3 still deserialize.
- **Boundary test:** a reflection or Roslyn check over `ArcBox.Host`'s `Games.Meridian` namespace. It may use `Meridian.Game`'s view records, `Meridian.Presentation`, and from `Meridian.Logic` only the content records and `PackLoader`.
- **The skeleton scene** shows the banner and the seat strip as text. It is enough for the shell to pick the game and draw something, which proves the wiring.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): presentation project and the TV scene skeleton`.

### Task 2: What the TV needs from the server

**Files:**
- Modify `MeridianRules.cs`, `Presentation.cs` (Game), `Views/HostView.cs`, `Projection.cs` and `ScenarioDef`.
- Modify the scenario JSON files and `scenario.schema.json`.
- Modify `Validation.cs`, `Fixtures` and the dev board.

The six fixes in [What M3 left](#what-m3-left-that-the-tv-needs):

- **Reading time.**
  - `MeridianRules.ReadingWordsPerSecond = 2.7` and `ReadingLead = 1.5 s`.
  - A presentation that carries text waits `lead + words / rate`. That covers `card`, `scenario`, `round`, `notice` and the Debrief's outcome.
  - Non-text presentations (`reveal`, `roll`, `fight`) keep their current fixed times.
- **`Wait`** on the host view:
  - It is `{ seat, kind: "grace" | "pause", until }` while the clock's purpose is `Grace` or `Pause`.
  - The pause's banner text is built from one template for every seat.
- **`announcement`** in every scenario file:
  - Required, at most 60 words. The loader reports a missing or long one, as it does a long briefing.
  - The `scenario` presentation's text becomes the announcement.
- **Round warnings:** a `round` presentation when the endgame reaches three rounds left and one round left.
- **`ScenarioPanel.Awards`**, only when the game is over.
- **`Ready`**, the number of seats ready, only during Briefing.

Tests:
- Review focus 4: every card and announcement in the pack, checked against the reading-time rule.
- Review focus 7: the waiting banner is identical for each living seat dropping in the endgame.
- The host-view budgets with `Wait`, awards and a long announcement.
- Regenerated fixtures, and the dev board's tests for the waiting banner.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): reading-time fallbacks, waiting banner, announcements and round warnings`.

### Task 3: Deck layout and camera framing

**Files:** create `Board/DeckLayout.cs` and `Board/TileDraw.cs`, with tests.

- **Cell to pixels:** 128 px per cell at 1080p, scaled by the largest whole multiple of 64 px that fits the viewport's height.
- **The active deck,** in order of precedence:
  1. the deck of `Now.Compartment` while an event is being presented;
  2. else the active seat's deck;
  3. else the deck last shown.

  The active deck only changes between beats, never mid-beat.
- **Framing:**
  - The explored bounds of the active deck plus one cell of margin, fitted to the main area (the screen minus the HUD and the side strip).
  - Never more zoom than 2× the art's native size, and never less than what fits the deck's full radius.
  - The camera eases over 0.6 s when the bounds grow.
- **The side strip** shows the other two decks at a fixed small scale, with crew markers.
- **`TileDraw`** describes one compartment:
  - art or placeholder;
  - rotation;
  - one hatch overlay per side: `open` (a placed neighbour), `closed` (unexplored beyond), `sealed`, or `none` (a wall);
  - the symbol glyph (Incident, Salvage, Anomaly).

Tests: review focus 5 (framing a deck holding only its landing) and review focus 6 (four rotations of every tile in the pack against `HatchesAt`).

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): deck layout and framing`.

### Task 4: Beats and the presented signal

**Files:** create `Beats/Beat.cs` and `Beats/Director.cs`, with tests driven by a fake clock.

- **The `Director`** takes each new host view and its predecessor and produces a queue of **beats**. A beat is a named timeline of steps, each with a duration:

  | Presented kind | Timeline |
  |---|---|
  | `reveal` | camera eases to the new cell → tile drops in (0.4 s) → hatches open → crew walks in |
  | `card` | card panel slides in → Tiller reads → hold for 1 s → panel out |
  | `roll` | dice tumble (0.8 s) → faces land → total against target |
  | `fight` | both combatants flash → the dice → the loser's track ticks down |
  | `death` | the marker fades to its echo outline |
  | `breach`, `scenario` | hand over to the Breach sequence (Task 8) |
  | `round` | the round clock pulses → Tiller's warning |
  | `vote` | "The vote is in" and the result. Never who chose what (the host view doesn't carry it) |
  | `notice`, `sealed`, `over` | a banner while Tiller reads, if there is text |

- **The signal:** `presented { seq }` once the beat's timeline has ended and its narration has finished, or after the narration's estimated time plus 2 s if the narrator is silent or slow.
- **Changes that aren't presentations** tween without a signal: crew moving, a token moving, a counter changing.

Tests:
- Review focus 1: a newer `Now` arrives mid-beat. The director cuts the beat short, does not signal the old `seq`, and plays the new one.
- Review focus 2: after `ConnectionLost`, the first view is applied without replay, its `Now` shown in short form (0.5 s) and signalled once.
- A repeated view is a no-op.
- A view with `Now` null clears the queue.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): beats and the presented signal`.

### Task 5: Tiller

**Files:**
- Create `Meridian.Logic/MeridianVoices.cs` and `Audio/TillerLines.cs`, with tests.
- Modify `MeridianServiceCollectionExtensions.cs`.

- **The voice theme:**
  - `MeridianVoices.Tiller = "meridian.tiller"`, described as "Ship's computer: level, unhurried, faintly polite".
  - It uses a Chirp 3: HD preset at native pace (2.7 words a second, which Task 2's reading time assumes), with the built-in fallback voice.
  - It's registered with `AddVoiceThemes`, so an owner can retune it in `Speech:Voices` without a build.
- **`TillerLines.For(beat)`** is a pure function of the host view:
  - an Incident card: title, then text;
  - a Salvage or Anomaly card: its title only;
  - the Breach and its announcement;
  - round warnings;
  - the endgame pause ("Holding for a crew member");
  - the Debrief's outcome.

  Priority is `Question` (it cuts in) for card text and the Breach, and `Commentary` for everything else.

Tests:
- Review focus 3: the M3 secrecy harness, extended. For every S1 scenario at 3–6 players, every spoken line of whole bot games is searched for secret role ids and names and secret objective texts.
- The voice is registered once and merges with configuration (as `VoiceThemeTests` does for REDLINE).
- The speaking-time estimate matches Task 2's reading time.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): Tiller, the ship's computer`.

### Task 6: The board in Godot

**Files:**
- Create `games/meridian/{Starfield,DeckView,TileNode,TokenLayer,CrewLayer,SideDecks}.cs`.
- Create `Art/MeridianArt.cs` (Presentation), with tests.

- **Layers,** back to front (spec §5.5):
  1. starfield (slow parallax);
  2. deck plating in the deck's accent colour;
  3. compartment tiles;
  4. tokens;
  5. crew;
  6. effects, which are simple tweens until M5.
- **Placeholders:**
  - a 128 px rounded square in the deck's accent, with the compartment's name (from the content pack, by tile id);
  - hatch overlays drawn as gaps, bars or welds per state;
  - the symbol glyph in a corner.
- **Crew** are coloured discs with the seat's initial.
- **Tokens** are glyphs per kind: brood, repair point, core, pod, prize, and a labelled square for anything else.
- **Art:** `MeridianArt.Load(root)` reads the `tiles`, `hatches` and `crew` manifests where present. It checks each file's sha256 and falls back to the placeholder on any mismatch or missing file, with one log line.

Tests:
- `MeridianArt` against a temporary pack with one tile, one bad hash and no crew.
- Review focus 6 for art: the rotated hatch overlay's bounds match the side.
- A Godot run in Task 10 covers the drawing itself.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): the ship on the TV`.

### Task 7: HUD, card panel and dice tray

**Files:** create `Hud/HudModel.cs` (Presentation), with tests; create `games/meridian/{SeatStrip,Counters,CardPanel,DiceTray,LogTicker,WaitBanner}.cs`.

- **The seat strip:**
  - each seat's colour, initial, name and crew member;
  - the active seat highlighted;
  - "auto" and "offline" tags, and never a side or role.
- **Counters:** the Anomaly count and tiles left before the Breach; the round clock and the scenario panel's public counters after it.
- **The card panel and dice tray** follow the `card`, `roll` and `fight` beats.
- **The log ticker** shows the last three lines.
- **The waiting banner** shows Task 2's `Wait` as a countdown.

Tests: `HudModel` for each phase from the contract-test sessions, and that it carries nothing the host view doesn't (it is a projection, not a source).

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): the TV's HUD, card panel and dice tray`.

### Task 8: The Breach sequence and the Debrief

**Files:**
- Create `Breach/BreachSequence.cs` and `Debrief/DebriefModel.cs` (Presentation), with tests.
- Create `games/meridian/{BreachOverlay,DebriefScreen}.cs`.

- **The Breach sequence** is full screen, about 12 s:
  1. the lights fail to emergency red;
  2. "BREACH" with the compartment and the Anomaly;
  3. Tiller names the scenario;
  4. the announcement is read in full;
  5. "Read your phone" with the ready count (Task 2's `Ready`, out of the seats).

  It holds on step 5 until the phase leaves Briefing. The `breach` and `scenario` presentations are signalled at the end of steps 2 and 4.
- **The Debrief:**
  - the outcome line;
  - every seat's revealed role, with winners marked;
  - the three awards;
  - all three decks shown in full at once, because the map is now public.

  It parks the scene (`IPreloadScene.Park`) when the game ends.

Tests: the sequence's steps and signal points against fake time; the Debrief model from each scenario's last host view.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): the Breach sequence and the Debrief on the TV`.

### Task 9: Wiring, and a demo mode

**Files:**
- Modify `MeridianScene.cs`, `AppArgs.cs` and `Main.cs`.
- Create `Demo/DemoViews.cs` (Presentation), and `src/ArcBox.Host/games/meridian/Demo/<scenario>.jsonl`.
- Create a test that writes and checks the recorded games.

- **The scene's interfaces:**

  | Interface | What it does |
  |---|---|
  | `IContentScene` | `PackLoader` on the delivered pack, plus `MeridianArt` |
  | `ISignalingScene` | the director's signals |
  | `IAudioScene` | Tiller's lines go to the narrator |
  | `IConnectionScene` | tells the director the connection dropped |
  | `IPreloadScene` | builds the starfield and fonts behind the lobby |

- **`--meridian-demo[=<scenario>]`** plays a recorded bot game through the normal host-view path with no server, like `--redline-demo`.
  - Recordings are written by a test from a seeded `Harness` game, as host-view patches (`HostViewPatch`) to stay small.
  - The same test fails if a recording no longer matches what the game sends, as the phone fixtures do.
  - The demo feeder answers the scene's signals.
- **`--shots`** works with the demo, so Task 10 can capture every phase.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): TV wiring and the demo mode`.

### Task 10: Screenshots and a readability review

**Files:** create `docs/superpowers/reports/meridian-tv/` (an index page and screenshots).

- **Capture:** run Godot 4.7.2 (mono) on Xvfb with software Vulkan, `--meridian-demo=<scenario> --shots`, at 1280×720 and 1920×1080, for each S1 scenario. Capture Boarding, the first reveal, a card, a roll, the Breach (each step), an endgame turn with tokens, a fight, a vote, the waiting banner and the Debrief.
- **Measure:** frame times over the demo. No frame may exceed 100 ms on this software renderer (see Global constraints).
- **Review:** an index page with the shots side by side, and notes against the readability rules (text sizes, colour plus initial). Fix what fails before committing.

- [ ] **Step 1: Capture; review against the rules; fix.**
- [ ] **Step 2: Commit** `docs(meridian): TV screenshots and readability review`.

### Task 11: End to end with the real TV

**Files:** modify `tools/tv-e2e.sh`, `docs/tv-host.md` and `docs/playtest-checklist.md`.

- **`ARCBOX_GAME=meridian [ARCBOX_SCENARIO=<id>] GODOT=… tools/tv-e2e.sh`:**
  - starts the server in Development, forcing the scenario if one is given;
  - starts the TV with `--autostart=meridian:3`;
  - runs `tools/meridian-playtest/bots.cjs` as three phones without `--host`, so the TV does the signalling.
- **It checks:**
  - the game reaches the Debrief and the lobby;
  - the TV log has no errors;
  - every `presented` signal the server received came from the TV, not a fallback (a server log line counts each);
  - the audio log shows Tiller's lines in order: the first card, the Breach, the announcement, and the outcome.
- **`ARCBOX_RECONNECT=1`** kills the TV during the Breach sequence and restarts it. The game finishes the briefing on its fallback. The returning TV shows the endgame without replaying the Breach (review focus 8). Run once per scenario.
- **Checklist:** the first real game night (a full game on the TV with real phones), the TV killed during the Breach, a slow-TV check, and one remote player watching over Discord (spec §7.2).
- Run `bash tools/check-all.sh --full`. Expect only the Tactics failures already on `main`, unless they've been fixed by then.

- [ ] **Step 1: Extend the script; run every scenario, and the reconnect run.**
- [ ] **Step 2: Update the docs and checklist; run the full gate.**
- [ ] **Step 3: Commit** `test(meridian): the TV end to end`.

---

## Done when

- `ARCBOX_GAME=meridian tools/tv-e2e.sh` plays every S1 scenario from crew pick to the Debrief and back to the lobby, with the real TV doing every `presented` signal.
- The same run with `ARCBOX_RECONNECT=1` survives a TV killed during the Breach sequence.
- No secret is shown or spoken by the TV in any S1 scenario at 3–6 players (the secrecy harness covers views and spoken lines).
- The screenshot review passes the readability rules at 1280×720 and 1920×1080.
- Art from the art packs appears wherever its manifest has it, and placeholders appear everywhere else.
- `bash tools/check-all.sh --full` passes, apart from any Tactics failures already on `main`.
- The owner has played a game night on the TV with real phones (the checklist's first item), which this plan can't automate.
