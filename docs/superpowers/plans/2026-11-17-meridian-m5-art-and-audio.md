> Status: draft for review (2026-11-17). Not started. Live task state will belong on GitHub issues once approved (see the [live tracker index](../README.md)).

# Derelict: ISV Meridian — M5 Art and Audio Implementation Plan

Steps use checkbox (`- [ ]`) syntax for tracking. Each task is test-first and ends in one commit.

**Goal:** The placeholders go (spec §8, M5). Every tile, hatch, crew member, monster, token, card, icon and HUD piece the art sets deliver is drawn on the TV and the phones, the board animates with effects, and the game has its music and sound. The art and audio themselves are produced in the `art` issues (#332–#338) by another agent, to the contract the owner approved in #331. **This plan is the engineering half:** every consumer is ready for every set before it lands, and every set is checked when it does.

**Architecture:**
- **Art is data, read through manifests.** Each set is `content/meridian/base/<set>/manifest.json`, keyed by content ids, naming hash-named files (#331). M4's `MeridianArt` already reads `tiles`, `hatches` and `crew`. This plan extends it to every set, with one rule: anything missing or bad is drawn as its placeholder, at the same metrics. So an art PR can land a set at a time, in any order, and nothing breaks between them.
- **One validator, three users.** `ArtCheck` (in `Meridian.Presentation`) checks a pack's manifests against the rules content and the contract. It catches unknown ids, missing files, wrong hashes, wrong frame sizes and missing states, and it reports coverage: which ids still draw a placeholder. A unit test runs it on the shipped pack; `tools/meridian-assets/check` runs it on an art branch for the art agent; and the TV logs its findings once.
- **Audio follows Tactics.** `Meridian.Presentation.Audio` decides what plays (music per phase, a sound per beat step). `MeridianAudio` in the Godot scene only plays it, on the mixer's Music and Sfx buses, ducked under Tiller's voice by the platform's `Ducking`. Tiller stays on text-to-speech (M4).
- **Phones read the same pack.** They load the Meridian pack (`/content/meridian/index.json`), as the Tactics module does, for portraits, card frames, item icons and the lobby theme. Art never carries text: card text and names stay live.

**Spec:** [2026-11-13-meridian-design.md](../specs/2026-11-13-meridian-design.md), §3.4, §5.4, §5.5, §8. **The contract:** #331 and its approved samples (draft PR #357). **The sets:** #332 tiles and hatches, #333 crew, #334 cards and icons, #335 TV UI kit and lobby theme, #336 monsters and tokens, #337 board effects, #338 music and sound. **Earlier stages:** [M4](2026-11-17-meridian-m4-tv-scene.md) and its *Implementation notes*.

**Not in this plan:**
- **Producing art or audio.** That's the `art` issues. Where this plan needs an asset to test with, it uses #357's approved samples, or generates a throwaway one in the test.
- **Scenarios 6–12's monsters and tokens** (M7). Their sets land with them; the token layer already falls back to labelled glyphs.

## Global constraints

- **Everything in M1–M4's Global constraints still holds**, above all the art contract's metrics and whole-step scaling (M4).
- **Placeholders never go away in code.** Every consumer keeps its placeholder path, so a missing or rejected file draws the M4 placeholder, never a hole or a crash.
- **Art carries no text.** Names, card text, numbers and labels are rendered live. The validator rejects a manifest entry that claims text.
- **Every frame is checked:** sha256 against the manifest (M4's rule), frame size against the contract, and frame count against the manifest's animation data.
- **Reduced effects.** The TV's existing `ReducedBattleEffects` setting (Tactics) also selects Meridian's reduced variants (#337): no flashing, no screen shake. Meridian adds no second switch.
- **Audio budget:** loudness matches Tactics (−16 LUFS, #338). The validator checks each file's length and channel count, not its loudness, which the art agent measures. Music crossfades; nothing plays over Tiller at full level.
- **Secrets stay secret.** No sound or effect may reveal a hidden role. A sabotage, for example, has no sound, because its event is hidden. Sounds come from the TV's public beats only, the same rule as Tiller's lines.

## Review focus

1. **A set lands half-done.** Some ids have art and some don't: each draws its own way, side by side, at the same size. [Tasks 1, 2]
2. **A bad file in a good set.** It's rejected by hash or size, logged once, and drawn as its placeholder; the rest of the set draws. [Task 1]
3. **Deck variants.** A tile with art for one deck but not another uses the art where it has it and the placeholder elsewhere, never the wrong deck's art. [Task 2]
4. **Facing and walking.** A crew member's facing follows their last move, through all four facings, and walking plays only while they move. [Task 3]
5. **A secret through sound.** For every S1 scenario at 3–6 players, no sound or effect is played for an event outside the TV's audience. [Task 7]
6. **Reduced effects** draw no flash and no shake, for every effect kind. [Task 6]

---

## Tasks

### Task 1: `ArtCheck`, and every set in `MeridianArt`

**Files:**
- Create `Art/ArtCheck.cs` (Presentation), with tests, and `tools/meridian-assets/check` (a thin runner).
- Extend `Art/MeridianArt.cs`.

- **The contract as code:** for each set (`tiles`, `hatches`, `crew`, `portraits`, `cards`, `icons`, `tokens`, `ui`, `fx`, `audio`), its id source and its frame metrics:

  | Set | Id source | Metrics |
  |---|---|---|
  | tiles | `rules/tiles.json` | 64 × 64 |
  | crew | `rules/crew.json` | 24 × 24 |
  | icons | `rules/cards` | 16 or 24 |
  | tokens | `BoardStyle`'s token kinds | per the contract |
  | the rest | the contract's lists | per the contract |
- **`ArtCheck.Run(packRoot, rulesPack)`** returns errors and a coverage report (ids with art, ids still placeholders).
  - Errors: an unknown id, a missing file, a hash mismatch, a wrong frame size, a missing hatch state, an animation whose strip doesn't divide into its frame count, a set's manifest that won't parse.
  - A unit test runs it on the shipped pack (no errors), on #357's samples (no errors, partial coverage), and on fixtures with each kind of fault.
- **`MeridianArt`** reads every set: frames, strips with their frame count and fps, 9-slice insets for UI pieces and card frames, and deck variants for tiles. Anything `ArtCheck` rejects is left out, so it draws as a placeholder (review focus 2).

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): ArtCheck, and every art set read`.

### Task 2: Tiles, hatches and deck variants on the TV

**Files:** modify `Board/TileDraw.cs` (Presentation) and `games/meridian/BoardCanvas.cs`; add tests.

- **The art for a compartment:** the tile's deck variant for the compartment's deck, else its single art, else the placeholder (review focus 3). The floor symbol is in the art (#332), so the scene draws its symbol glyph only over a placeholder.
- **Hatch overlays** use the art's four states, turned per side (M4).
- **Names stay live** at 128 px cells, and as callouts below that (M4), drawn over art the same as over placeholders.

Tests:
- The variant choice per deck.
- A mixed board (review focus 1).
- A TV run with #357's samples, with screenshots of a board where some rooms have art and some don't.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): tile and hatch art on the TV`.

### Task 3: Crew that walk, face, fall and echo

**Files:**
- Create `Board/CrewMotion.cs` (Presentation), with tests.
- Modify `BoardCanvas.cs`.
- Modify the phone module's crew pick and sheet.

- **Motion.** Today crew jump between compartments. `CrewMotion` tweens each marker along the compartments it passed through (`Reach.Paths` from the content pack), at a walking pace, with the facing of each step (review focus 4). The `walk` strip plays while moving; `idle` plays at rest.
- **States:** `hurt` for a beat after damage; `down` and then the `echo` variant after a death.
- **The player's colour ring** and the mark (M4) stay on top of the art.
- **Portraits** on the TV's seat strip and the Debrief, and on the phones' crew pick and sheet. Each falls back to the colour disc and mark.

Tests:
- Paths and facings over a recorded game.
- Facing per step for all four sides.
- Hurt and down timing from beats.
- Phone screens render with and without portraits.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): crew art that walks, faces and falls`.

### Task 4: Monsters and tokens

**Files:** modify `Board/BoardStyle.cs` (Presentation) and `BoardCanvas.cs`; add tests.

- **Tokens:** art per kind and state (a repair point broken or fixed, a pod used or not), else the M4 glyph.
- **Monsters** (brood in The Passenger): the crew treatment, with facings, walking along their moves, and a stunned loop.

Tests: the state choice per token kind, and the fallback to glyphs for an unknown kind.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): monster and token art`.

### Task 5: The TV UI kit, card frames, dice and the lobby

**Files:**
- Modify `HudCanvas.cs`, `BreachOverlay.cs` and `DebriefCanvas.cs`.
- Create `Hud/NineSlice.cs` (Presentation), with tests.
- Modify the phone module's `index.ts` (`lobbyTheme`) and its screens.

- **9-slice panels** (#335) for the seat strip, the figures, the log, the card panel and the dice tray. Each falls back to M4's flat panels.
- **Card frames** per kind (#334) on the TV's card panel and on the phone's card screen. The text stays live.
- **Item icons** in the phone's gear list and trade screen.
- **Signal dice:** tumbling frames and a settled frame per face (0, 0, 1, 1, 1, 2), in place of the drawn squares.
- **Breach and Debrief plates.** The Breach's title plate and the Debrief's results plate, with the scenario's name and the outcome set live.
- **Logo and lobby theme:** the title card in the TV's lobby banner, and the phone lobby's `lobbyTheme` and skin.

Tests: the 9-slice geometry at every panel size; each consumer falls back without its piece; the phone screens with and without art.

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): the TV UI kit, card frames, dice and lobby theme`.

### Task 6: Board effects

**Files:** create `Fx/EffectPlan.cs` (Presentation), with tests, and `games/meridian/FxLayer.cs`.

- **`EffectPlan.For(beat, step)`** chooses an effect for each beat step:
  - a reveal's lights-on;
  - a hatch opening and a weld;
  - a hit, a miss, a stagger, and a stun loop;
  - a trait's loss or gain;
  - the Breach's alarm wash and distortion;
  - venting, sparks, rot, and pressure loss;
  - a death's echo.
- **`FxLayer`** plays the strips at the beat's pace, between the tokens and the crew (spec §5.5's layers).
- **Reduced effects** (the TV's `ReducedBattleEffects`) picks each effect's reduced variant: no flash, no shake (review focus 6).

Tests: the effect per beat step, reduced variants for every kind, and none outlasting its beat (so the beat timing tests still hold).

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): board effects`.

### Task 7: Music and sound

**Files:**
- Create `Audio/SoundPlan.cs` (Presentation), with tests, and `games/meridian/MeridianAudio.cs`.
- Add a turn deadline to the host view, for the last-ten-seconds tick.

- **Music per phase:**
  - Boarding and Explore: an ambience loop.
  - The Breach: a sting, crossfading into the endgame's tension loop.
  - The Debrief: a "crew won" or "crew lost" cue. A free-for-all plays "won" if anyone won.
- **Effects per beat step** (#338): footsteps, a hatch, a reveal, a card per deck, dice and their settle, a hit, a miss, a stagger, a death, the echo, venting, alarms. Plus a soft tick in the last ten seconds of a turn: the host view gains `turnEndsIn`, seconds at view time, as `Wait` has.
- **Under Tiller:** music and effects duck under Tiller's voice through the platform's `Ducking`.
- **Secrecy:** sounds come only from the TV's public beats. A test sweeps every S1 scenario at 3–6 players and checks that no sound is chosen for an event outside the TV's audience (review focus 5).

Tests: the music for each phase and ending, the sound per beat step, the tick's timing, the secrecy sweep, and every chosen file present in the `audio` manifest (or skipped silently if it isn't).

- [ ] **Step 1: Write the failing tests.**
- [ ] **Steps 2–4:** confirm they fail, implement, confirm they pass.
- [ ] **Step 5: Commit** `feat(meridian): music and sound`.

### Task 8: Each art set as it lands

**Files:** `docs/superpowers/reports/meridian-tv/` (screenshots), and the issue for each set as it merges.

- **When an art PR is ready,** run `tools/meridian-assets/check` on its branch and post the coverage and any errors on its issue. Then run the TV demo (`--meridian-demo`, M4) at 1080p and 720p with the set, and add before-and-after screenshots to the TV review.
- **The readability rules apply again** (M4 Global constraints). Art that fails them goes back to its issue with the screenshot.
- **The placeholders are gone** when `ArtCheck`'s coverage is complete for every set and the last screenshot review passes. This task stays open until then. Unlike the others, it ends when the art does, not with a commit of its own.
- **Audio by ear:** a game night with the sound on (the playtest checklist gains a section: the music's levels, the duck under Tiller, the tick).

- [ ] **Step 1: Per set, as each lands: check, screenshots, review.**
- [ ] **Step 2: The full gate, and the checklist's sound section.**

---

## Done when

- Every consumer reads its set, and draws its placeholder for anything missing or rejected. This is shown by unit tests and by TV runs with #357's samples and partial sets.
- `ArtCheck` passes on the shipped pack, runs for the art agent on any branch, and reports coverage.
- Music, effects and the turn tick play from the TV's public beats only, ducked under Tiller, and the secrecy sweep covers them.
- Reduced effects remove every flash and shake.
- **Placeholders gone:** full coverage for every set, after the last art PR (#332–#338) merges, with the screenshot review passed. This depends on the art agent's schedule, so the engineering tasks can close before it.
- `bash tools/check-all.sh --full` passes, apart from any Tactics failures already on `main`.
