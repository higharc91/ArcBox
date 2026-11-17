# Meridian card art / #334

Three original nine-slice frame kinds, six matching kind glyphs (16px/24px), and all twenty first-wave item icons (8 Salvage / 12 Anomaly). Deterministic arcpix/Pillow source; 0 CU generation spend. Gameplay, balance, recordings and existing art are unchanged.

[Review every real card](index.html), [TV examples](cards-tv.png), [long choice at the same font size](choice-tv.png), [all twenty objects at TV 3×](icons-tv.png). Card PNGs contain no titles/rules; the review renders actual current #316 content as live text. Thirty-two exact content IDs map to three frame kinds. Source snapshots are byte-identical to #316 head `00d4a52bb9c7e73a5ee72b117b91891e6c0cdd11`.

Frames are 64×64 with twelve-pixel nine-slice bands and sixteen-pixel content insets. Only corners carry kind-specific motifs; edges/centres are uniform stretch regions. Warning triangle / handled case / hollow diamond symbols match the #332 floor glyphs. Items have individually authored silhouettes and accompany live names, so colour is supplementary. The future M7 wave extends the same keyed manifest and must add deliberately authored recipes.

## Checks

Eleven focused card tests pass, including coverage, distinct greyscale objects, kind shapes, stretch-safe regions, palette, exact content text, unknown effect rejection, missing/corrupt files, new-item and duplicate-ID rejection, and deterministic rebuild. `audit_cards.py` reports 32 cards, 20 icons, 3 frames, 6 symbols, 29 referenced PNGs.

Actual Chromium at 320×844, 390×844 and 1920×1080 loads all 136 images and 32 cards without page overflow, card text clipping or page/request errors. Images use only 2×/3×. Every TV card retains 30px body text; titles are 38px. Phone body/title sizes are 18px/25px. All three kind filters work. `check-card-corners.py` compares all 36 actual browser corners with exact nearest 2× source pixels, accounting only for screenshot rounding by at most one background pixel. TV captures detach the fixed-size panel from the phone scrolling wrapper to avoid ancestor clipping.

Native Godot displays all three complete 1920×1080 review screens with nearest filtering; pixel identity is checked by `check-card-native.py`. It produces actual Machado full-severity deuteranopia/protanopia transformations in linear sRGB and JPEG quality-55 / 4:2:0 stream proxies. These are screenshot checks, not a human recognition study, physical-TV test or Discord playtest. Existing unsupported V-Sync driver warnings occurred; no native script/runtime errors. Reduced motion is respected: these reading assets contain no animation.

Full repository validation remains a merge requirement and will be recorded separately from these focused checks. Manually disabled CI remains disabled. This is an asset/review set; the M5 runtime card renderer and controller integration are not claimed.

## Reproduce

```sh
python tools/meridian-assets/cards.py
python tools/meridian-assets/audit_cards.py --rules <current rules/cards directory>
python -m unittest discover -s tools/meridian-assets -p 'test_cards.py' -v
python -m http.server 4186 --bind 0.0.0.0
# another terminal
node tools/meridian-assets/check-card-browser.cjs
python tools/meridian-assets/check-card-corners.py
```

Use the style review's native-project.godot / native-display.gd on a real X display, with MERIDIAN_MOCK and MERIDIAN_SCREENSHOT paths, to capture cards-tv.png, choice-tv.png and icons-tv.png as native-cards.png, native-choice.png and native-icons.png. Then run `python tools/meridian-assets/check-card-native.py`.

The M5 contract published in #331's later comment is also followed: card frames declare the canonical `insets` field, and `icons.incident`, `icons.salvage`, `icons.anomaly` each expose their 16px/24px entries. The `symbols` index aliases exactly those entries for convenient gallery access; the audit rejects alias drift. This metadata correction changes no PNG or review pixels. The exact #316 checker at 00d4a52b was built with zero warnings/errors and run against this asset directory: no errors/warnings; coverage 48/48 tiles, 4/4 hatch states, 8/8 crew, 8/8 portraits, 3/3 frames and 23/23 icon IDs. Tokens/UI/FX/audio remain separate future sets. Complete checker/build logs are retained.
