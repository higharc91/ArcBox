# Card style refinement / November 18

The owner's requested stronger card styling is implemented in three new immutable nine-slice plates and the live-text review composition. Incident uses hazard plating and red/gold corner grips; Salvage uses pale equipment-case rails, square fasteners and brass gaskets; Anomaly uses faceted chartreuse corners and a cold inset rail. Old hash-named frame files are retained unchanged.

[Revised TV cards](cards-tv.png) · [Previous version](restyle-before.png) · [Long choice, unchanged 30px body](choice-tv.png) · [All real cards](index.html). Category mastheads, framed identity icons, italic flavour copy and boxed dice-result bands provide hierarchy while titles/rules remain live text. TV body/title stay 30px/38px, phone 18px/25px. Objects remain exact 2×/3×; nine-slice corners remain exact nearest 2×. The long-choice panel uses slightly tighter spacing while preserving font size and every word.

Eleven focused card regressions pass. Actual Chromium at 320×844, 390×844 and 1920×1080 loads all 162 images and 32 cards with no page overflow, text clipping or page/request errors. All filters work. All 36 browser corners match their exact source pixels. Three native Godot 1080p image-display captures match RGB pixels; actual Machado deuteranopia/protanopia transforms and JPEG stream proxies are regenerated. Both kind-comparison simulations were visually inspected: shapes and live names supplement colour. Existing unsupported V-Sync driver warnings are retained; no errors occur in the successful native captures. No physical-TV/human-recognition or gameplay test is claimed.

The current 93d5b335 game checker accepts the same complete card/icon coverage with no errors/warnings. Manifests remain keyed identically with 12px insets, 16px content insets, three frames, six symbols and twenty item icons. Frame bands/centres remain uniform stretch regions; all detail stays in fixed corners. No paid generation, gameplay, balance, rules text, recordings or existing item/crew/room art changes.

Full `bash tools/check-all.sh --full` validation is pending before merge. CI remains manually disabled; no tests are skipped or weakened. The earlier integration report describes its frozen source, not this new styling.

## Runtime boundary

This updates actual frame PNGs plus the asset review layout. The masthead/icon-well/result-band composition is a live HTML review design, not a claim that the same layout has been implemented in open game PR #316. Its previously documented consumer mappings remain separate engineering work.

## Reproduce

Run `python tools/meridian-assets/cards.py`, `python -m unittest discover -s tools/meridian-assets -p test_cards.py -v`, the existing card browser/corner/native checks, and the full serial-module gate. Serve this checkout on 4187 and set MERIDIAN_REVIEW_ORIGIN=http://127.0.0.1:4187 for browser checks. Native captures use style/native-display.gd and a real X display.
