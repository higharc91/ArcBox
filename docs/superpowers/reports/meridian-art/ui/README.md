# Meridian UI art / #335

Original ship-interface kit continuing the approved #406 card style: metal housings, brass corners, linen signal dice, cool display details and hazard rails for the Breach. Deterministic arcpix/Pillow source; no generated purchases or generation spend.

[Live review](index.html): five native 1920×1080 phases, a responsive 390px phone lobby and all panel recipes. Static native captures: [Boarding](native-boarding.png), [Explore](native-explore.png), [Breach](native-breach.png), [Endgame](native-endgame.png), [Debrief](native-debrief.png). Phone: [320px](phone-320.png), [390px](phone-390.png).

## Delivery

- Thirteen canonical UI keys; ten 64px panel recipes with `insets: [12,12,12,12]` and 16px content insets. Display at whole 2×, keeping source corners at 24px. The seat slot composes the turn-order strip.
- A graphic ship/orbit logo and wide title backdrop. DERELICT / ISV MERIDIAN are live text, as are names, counters, rules, scenario announcements, results and the auto label.
- Signal die values `0,0,1,1,1,2`: three settled faces, four original tumble frames at 12fps. The reduced-motion review uses the settled zero PNG; Breach is static and never flashes or shakes.
- Mini-map housing and six pip-coded crew markers. Small maps use purpose-made topology glyphs rather than reduced room bitmaps. Main review rooms remain 128px and crew 48px (whole 2×).
- Full-screen static Breach rails and public scenario plate, plus Debrief space for winners, all six roles and three canonical award labels.
- CSS-friendly PNG lobby backdrop, graphic banner and six seat badges. A single neutral robot glyph accompanies live `auto` text; the identical glyph is used on both sides in Debrief. Pip counts identify seats without colour.

All 31 distinct runtime PNGs are hashed in `content/meridian/base/ui/manifest.json`. The 13 engine-consumed IDs are under `ui`. Supplementary phone theme data is under `lobby`, and neutral auto tags, mini markers and the full-screen Breach backdrop under `extras`; these require explicit selection by consumers. The strict local audit covers these supplemental entries too.

## Validation and limits

Eight focused tests cover complete IDs, hashes, corruption/missing assets, stretch safety, deterministic rebuild, greyscale differentiation, pip counts and live public scenario content. Real Chromium checks at 320×844, 390×844 and 1920×1080 cover loaded images, overflow, whole image scales, phase secrecy, neutral auto art, Ready demo behaviour, animated dice, pause and reduced motion. All **164 actual rendered panel corners** match exact nearest 2× source pixels.

Five Godot 4.7.2/Xvfb native 1920×1080 review captures match their Chromium images pixel for pixel. Actual full-screen deuteranopia/protanopia simulations are retained for every phase, as are JPEG quality-55/4:2:0 streaming proxies. These are **illustrative art/layout mocks**, not gameplay sessions, physical-TV tests, human usability tests or actual Discord streaming measurements. The Ready control only demonstrates local review state.

Scenario reference is copied byte-for-byte from open game PR #316 head `3b272cb6c0f3b7cb3a53efa35a0d34e9f4dd330d`; its current checker builds with zero warnings/errors and accepts **13/13 UI pieces with no checker warnings/errors**. Missing token/FX/audio sets remain explicit placeholders and separate issues. This main-branch art PR does not import the unmerged game code. The finished layouts still need adoption by the game UI; phone `LobbyTheme` selectors need the supplemental backdrop/badge entries and live wordmark. The platform TV-lobby image hook remains #405.

Issue #335 requests an owner visual OK. The review link is posted there; keep the issue open until that review is recorded.

## Reproduce

Source the managed toolchain (`/workspace/.arcbox/activate.sh`), then run `python tools/meridian-assets/ui.py`, `python tools/meridian-assets/audit_ui.py` and `python -m unittest discover -s tools/meridian-assets -p 'test_ui.py'`. Serve the repository root on port 4187 and run `node tools/meridian-assets/check-ui-browser.cjs`, followed by `python tools/meridian-assets/check-ui-corners.py`.

For native review, copy `../style/native-project.godot` to a temporary directory as `project.godot` with `../style/native-display.gd`; run Godot there on a 1920×1080 Xvfb display with `MERIDIAN_MOCK` pointing at each `{phase}-tv.png` and `MERIDIAN_SCREENSHOT` at `native-{phase}.png`. Then run `python tools/meridian-assets/check-ui-native.py`. This shows the completed review composition in a native Godot viewport; it does not exercise game consumers.
