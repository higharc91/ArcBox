# Meridian compartment set / #332

Current combined merge status: [integration report](../integration/README.md). The per-set gate results below are retained historical evidence.

The owner approved the refined #331 visual direction in conversation on 2026-11-17 and requested continued quality improvement and appropriate animation. A subsequent instruction emphasized larger room features for TV. This set keeps the 64px source / 128px TV-grid contract and enlarges the equipment silhouettes and connected highlights inside it.

## Delivered

- Every one of the 48 current room IDs (50 physical tiles), with 57 deck variants. Rooms use explicit authored layouts: bridge stations, navigation/chart tables, signal dishes, observation glass, medical beds, cryopods, plant trays, lockers, laundry drums, gym equipment, reactor containment, engine cylinders, coolant/fuel tanks, drone cradles and industrial shafts. No unknown ID gets a placeholder.
- Every room at source and TV scale in all four rotations. Legal hatch edges compose from the rule data; unconnected edges receive the wall overlay. All bases retain the four 16px transparent doorway gaps and overhead wall lighting.
- Larger 11px triangle/case/diamond floor symbols, deck accents plus I/II/III bars. Shape cues survive greyscale/rotation; hue is supplementary.
- Five north-edge overlays: open, closed, sealed, continuous wall and one-way shaft cue. Separate original opening (6 frames, 0.6s) and sealing (8 frames, 0.8s) strips at 10fps, one-shot metadata and settled-state reduced-motion guidance. Reduced-effects sealing omits sparks. Preview animations demonstrate these assets; runtime integration awaits M4/M5. This does not claim completion of board effects #337.
- Unexplored-space fill and subtle per-deck backgrounds. One 1920×1080 half-explored concept board per deck, with two crew on revealed cells and a proposed 256px focused-room detail panel. The grid stays 128px; the larger panel is a review concept, not shipped TV behavior or a legal random deal.
- Four 1080p readability sheets cover all 48 room IDs at exact 128px alongside greyscale/crew-occluded counterparts. `featured-tv.png` is a compact six-room sample; `room-tv-comparison.png` compares the previous four style samples with the TV-focused pass. `all-compartments-tv.png` contains every deck variant.

## Reproduce

Source `/workspace/.arcbox/activate.sh` on the managed machine when available. Run:

```sh
python tools/meridian-assets/compartments.py
python tools/meridian-assets/audit_compartments.py --rules <current-rules-tiles.json>
python -m unittest discover -s tools/meridian-assets -v
python -m http.server 4184 --bind 0.0.0.0
# In another terminal:
node tools/meridian-assets/check-browser.cjs
```

The builder defaults to the exact pinned rules snapshot in `tools/meridian-assets/reference/`. The actual rules dependency is unmerged #316 at ff3175af, not a claim that #329 has landed on main. Manifest version 2 uses `tiles[id].variants[deck]`; clients must choose the current deck and verify hashes. Previously committed hash-named files are retained. The style-only sample builder refuses to replace the full-set manifest.

Native capture uses the retained style review's `native-project.godot` and `native-display.gd`, with `MERIDIAN_MOCK`, `MERIDIAN_SCREENSHOT` and a real X display. `check-native.py` verifies pixel identity and produces full-severity Machado 2009 deuteranopia/protanopia captures in linear sRGB, plus JPEG quality-55/4:2:0 stream proxies. A JPEG proxy is not a Discord-stream or human/physical-TV playtest.

## Validation and limits

21 focused checks cover deterministic complete rebuilds, exact content/deck coverage and metadata, corruption/missing variants, unique greyscale room compositions, rotation/door gaps/wall light, legal overlay composition, distinct symbol shapes, hatch animation bounds/endpoints/rest states, source dimensions/palette and protection against sample-builder overwrite.

Real Chromium at 320×844, 390×844 and 1920×1080 loads all 464 images with no overflow/page/request errors. All 456 room views are exactly 1×/2×; deck filtering preserves all variants. Reduced-motion preferences show settled hatch states; normal motion advances, Pause stops it, and Still shows the rest frame. Retained screenshots show the viewport rather than a fractionally scaled huge page.

Native Godot OpenGL captures all three 1080p concept boards. The existing unsupported V-Sync warning is retained; no script/runtime errors occurred. Capture, simulation and compressed-preview review results are recorded in `native-check.json`. These are native image-display checks; Meridian's actual Godot TV scene is still outside this art branch. No physical-TV, measured human recognition or gameplay integration test is claimed.

Full `bash tools/check-all.sh --full`: .NET 7,727/7,739 passed, 12 failures exactly matching the recorded main baseline, zero skipped and zero build warnings. Phone 601 tests and production build pass (two existing upstream SignalR annotation warnings); Node 24, Python 117 (61 assets + 9 audio + 21 Meridian + 26 lore), lore and documentation drift pass. Overall exit 1 blocks merge. Source/run provenance and the complete compressed log are retained in `validation.json` / `full-check.txt.gz`. CI remains manually disabled. No gameplay, balance, existing Tactics art/voice, paid generation or separate combat-VFX workflow changes. Generation spend: 0 CU.

Room names remain live UI text from the content data. The asset manifest retains the exact rule name, and the audit rejects name drift. No names are painted into the room PNGs; the focus-panel mock demonstrates a separate label. The existing M2 phone map on #316 already renders `room.name` and accessible move labels. Final Godot HUD labels belong to M4/UI integration.

## Review links

[Six featured rooms](featured-tv.png) · [All variants](all-compartments-tv.png) · [Previous samples / larger TV features](room-tv-comparison.png) · [All hatch frames](motion-frames-tv.png) · [Opening](opening-preview.gif) · [Sealing](sealing-preview.gif)

Actual-size 1080p room sheets, colour beside greyscale + crew: [1](tv-readability-1.png), [2](tv-readability-2.png), [3](tv-readability-3.png), [4](tv-readability-4.png).

Native deck concept captures: [Command](native-command.png), [Habitat](native-habitat.png), [Engineering](native-engineering.png). Every native image was pixel-compared with its source. Both actual CVD simulations and all three JPEG stream proxies were visually inspected; room silhouettes, shape-coded symbols, labelled focus panel and numbered crew markers remain present. Accent hues converge, so these limited screenshot checks are not human recognition measurements.

Final coverage was rechecked against current #316 head `16dc31eced20a4331393e2991304c596f6a946a0`; its tile JSON is byte-identical to the pinned reference. All 48 IDs / 57 deck variants pass the live-data audit.

M5 contract compatibility check (current #316 at 00d4a52b): the four game hatch states remain under `states`; the separate north-edge shaft overlay is under `oneWay`, rather than inventing a fifth legal hatch state. Its immutable file and pixels are unchanged. The local audit still requires and hashes that overlay; a negative regression rejects its absence. The real game checker reports 48/48 tiles and 4/4 hatch states with no errors. Selecting the one-way shaft overlay/animation remains M5 consumer work. Checker/build logs are retained in the sibling cards report.
