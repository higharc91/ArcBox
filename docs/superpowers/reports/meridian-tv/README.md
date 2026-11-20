# Meridian TV: screenshots and readability review (M4, Task 10)

**Date:** 2026-11-17. **Build:** branch `ccr-de425e76-4vxzw2`, the commit that adds this page.

**How these were made.** Each shot comes from the TV app in demo mode on Xvfb with software Vulkan (Mesa lavapipe), rendering at 1920×1080 or 1280×720. The command was:

```
godot --path src/ArcBox.Host --rendering-driver vulkan -- --meridian-demo=<scenario> --shots=<dir> --res=<w>x<h> --preset=low --audio-log
```

The demo mode plays a recorded four-player bot game and waits for the scene's own `presented` signals, as a real server does. The art packs had not landed, so every tile, crew member and token is a placeholder. M5 swaps them for art at the same metrics.

## The rules checked

These are the M4 plan's Global constraints.

| Rule | Result |
|---|---|
| Body text at least 28 px at 1080p | Pass: panel text 30 px, titles 40 px, the HUD banner 34 px |
| Labels at least 22 px at 1080p | Pass with one exception (below) |
| Every seat by colour **and** mark | Pass: the strip, the board and the Debrief show each seat's mark on its colour. Marks stay unique when names share an initial (P1 to P4 here) |
| Nothing secret on screen | Pass: the TV draws only the host view, built from what everyone may see. Spoken lines are covered by the secrecy sweep in `TillerLinesTests` |
| Whole-step scaling for pixel art | Pass: cells are 32/64/128/256 px only (logical 1080p; the engine scales the window) |
| No frame over 100 ms (software Vulkan; guards against redraw storms) | Pass without screenshots; see *Frame times* |

**The exception.** A compartment name whose longest word is wider than its 128 px tile drops below 22 px, to 18 px at the smallest. "Containment" and "Reclamation" are the cases. The alternative was clipping letters (an earlier run read "Containme Lab").

## Fixed during the review

- **A symbol covered the end of a compartment's name** ("Gymnasiu"). The symbol moved to the bottom-right corner.
- **Crew discs covered two-line names** ("Engine Room"). Crew now stand in the tile's lower third, and shrink when four or more share a compartment.
- **Every seat showed "B"** when names shared an initial (Bot1 to Bot4). Seat marks now extend until they're unique: B1, Sa/Su.
- **A fight didn't say who fought** ("4 against 2: hit."). It now reads "Brood attacks Player1". A seat is named by its player, a monster by its kind; the whole table sees a fight.
- **A dropped item showed as a grey "I".** It now gets its own glyph.
- **The card panel was a fixed 230 px tall,** even for one line. It now sizes to its text.
- **Room names were about 9 px on screen at 64 px cells.** Names now go inside tiles only at 128 px cells. Smaller than that, the presented compartment and the active seat's compartment get a 22 px callout.
- **A silent voice overran the server's fallback.** This was found before this review, in Task 8: each spoken beat waited its estimate plus 2 s. The grace is now 1 s, and a test checks the silent worst case for every presented event.

## Frame times

The median, 95th-percentile and worst frame over each demo, after a 3-second warm-up. Each figure comes from one TV running alone on a 4-core container with software Vulkan:

| Run | Median | p95 | Worst |
|---|---|---|---|
| Salvage Rights, 1080p, no screenshots | 31 ms | 37 ms | 72 ms |
| Salvage Rights, 720p, no screenshots | 18 ms | 22 ms | 59 ms |
| Salvage Rights, 1080p, with screenshots | 30 ms | 37 ms | 106 ms |

The worst frames with screenshots on are the screenshot readbacks. Two TVs sharing the four cores (the other capture runs) roughly doubled the medians. The scene redraws only when something changes: a new view, a camera ease, a beat animating, or a new HUD model. That cut nothing measurable here, where the renderer's per-frame cost dominates, but it will matter on a slow TV box. The plan's real target, 60 fps on the reference TV hardware, needs that hardware.

## 1080p

| | |
|---|---|
| ![Explore begins](1080-01-explore-start.png) *Explore begins: the HUD, the seat strip, and the side decks framed on what's explored* | ![A reveal](1080-02-reveal.png) *A reveal at 64 px cells: the new compartment's name as a callout* |
| ![A card](1080-03-card.png) *An Incident card, read aloud by Tiller* | ![A roll](1080-04-roll.png) *The dice tray* |
| ![BREACH](1080-05-breach.png) *The Breach: lights to red, and where it fired* | ![The announcement](1080-06-announcement.png) *The scenario and its public announcement* |
| ![Read your phone](1080-07-read-your-phone.png) *Briefing: a ready count, never which seats* | ![A fight](1080-08-fight.png) *A fight, naming both sides; brood tokens on the board* |
| ![A death](1080-09-death.png) *A death: the dropped gear left behind* | ![A notice](1080-10-notice.png) *A scenario notice (Tiller Says No)* |
| ![A round warning](1080-11-round-warning.png) *A round warning (Decaying Orbit): pods, orbit and engine counters on the HUD* | ![The Debrief](1080-12-debrief.png) *The Debrief: the mimic revealed, the winners first, the awards and the whole ship* |

## 720p

The same layout, rendered at 1920×1080 and scaled by the engine.

| | |
|---|---|
| ![Reveal at 720p](720-01-reveal-callout.png) | ![Card at 720p](720-02-card.png) |
| ![Read your phone at 720p](720-03-read-your-phone.png) | ![Debrief at 720p](720-04-debrief.png) |

## With the approved art samples (M5, Task 2)

These are the samples from draft PR #357, laid over the repository's pack: four tiles (Bridge, Med Bay, Reactor Core and Airlock), all four hatch states, and Ines Okafor's sprite. Rooms with art and rooms without sit side by side at the same size. The hatch art is turned to every side, and the placeholders' symbol glyphs give way to the art's floor markings. A room's name keeps the 22 px minimum, on a dark band over art.

![The start layout with sample art](art-01-samples-start.png)

To make a shot like this, run `ARCBOX_MERIDIAN_PACK=<a copy of content/meridian/base with an art branch's sets over it>` with `--meridian-demo`.

## With a stand-in UI kit (M5, Task 5)

The UI kit hadn't landed, so this run uses a stand-in kit made only to test the plumbing: deck-coloured card frames, a dice tray with die faces and a roll strip, seat slots, a log panel, and the Breach and Debrief plates. Every piece is a 9-slice, drawn at 2× so its 1 px edges stay crisp. The shots show the card panel (an Incident's frame), a roll, the Breach announcement on its plate, and the Debrief column. Pieces the kit doesn't have fall back to `panel`, then to the M4 look.

![The stand-in UI kit](art-02-ui-kit.png)

## The seven M7 scenarios (M7, Task 11)

Each sheet is one demo-mode game, rendered from its recording at 1080p. Its four panels are the scenario's announcement, two endgame moments, and the Debrief.

Every token and counter here is drawn from its scenario's content file (M7, Task 1): the label, the colour and the HUD's wording. No TV code names a scenario. The art sets for these tokens are tracked on #336; until they land, each draws its labelled placeholder.

Every run had no errors. Frame times were 25–29 ms median at 1080p (p95 29–35 ms) and 14 ms median at 720p.

| | |
|---|---|
| ![Company Asset](m7-company_asset.png) *Company Asset: security drones, logs transmitted* | ![Captain on Deck](m7-captain_on_deck.png) *Captain on Deck: a scenario's seal ("It won't open from this side"), Authority and cores pulled* |
| ![Cold Sleep](m7-cold_sleep.png) *Cold Sleep: sleepers marching on the Airlock, and a round warning* | ![Scuttle Charge](m7-scuttle_charge.png) *Scuttle Charge: a charge found, with the sparks notice. The planter is never shown* |
| ![Hull Rot](m7-hull_rot.png) *Hull Rot: the rot (~) and its source (@), with the spread notice and the rot effect* | ![Vent Cycle](m7-vent_cycle.png) *Vent Cycle: a deck decompresses, and the suits lie scattered* |
| ![The Last Clean Sample](m7-last_clean_sample.png) *The Last Clean Sample: infection per seat, and the scramble for the dose* | |

**At 720p** (the same layout, scaled by the engine):

| | |
|---|---|
| ![Hull Rot at 720p](m7-720-hull_rot.png) | ![Captain on Deck at 720p](m7-720-captain_on_deck.png) |

**Readability.** The M4 rules hold:
- labels are at least 22 px at 1080p, except the long compartment names M4 already noted;
- each seat shows its colour and mark;
- the TV view holds nothing secret.

**Fixed during this review:**
- **Per-seat counters crowded the figures.** In The Last Clean Sample, four "Infection: PlayerN" figures pushed "Rounds left" off the screen, and the objective line ran into a number. A per-seat counter is now a badge on its seat's chip ("Infection 4"). One of Us Isn't's suspicion moves there too. The objective line also stops short of the figures.
- **Six seats overflowed the strip,** with or without badges: M4 reviewed four. The strip now fits the screen by dropping crew names first, then shortening badges to their numbers. The seat's name, mark and colour always stay.

Below, a six-phone game on the real TV at 720p, after the fix:

![Six seats with their badges](m7-six-seats.png)

## For the first game night

These are things a person must judge that a screenshot can't:
- **Panel and voice timing on a real TV,** with the server's speech service.
- **The 3 m sofa test, and a Discord stream.** Check that the 64 px cell callouts and the side decks still read.
- **Whether the starfield's drift distracts.** It's slow, but it's the only motion on an idle board.
