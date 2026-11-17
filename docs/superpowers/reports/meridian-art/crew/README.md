# Meridian eight-crew assets / #333

Current combined merge status: [integration report](../integration/README.md). The per-set gate results below are retained historical evidence.

Original deterministic pixel art following the owner-approved Meridian style and the request for stronger TV-readable features and appropriate animation. The art branches are synchronized with main `22ce39b7` (new story/voice fixes); this crew change itself modifies no existing Tactics art, voices, gameplay or balance. No paid generation: 0 CU.

## Delivered

- All eight exact crew IDs/names/roles from the pinned rules snapshot on unmerged #316 at `1d770a73`.
- 24×24 board frames, feet anchor (12,22), four facings. South/north are authored front/back views; east is a distinct profile and west its matching mirror. Every pose has four distinct facing images. No whole-figure rotation or resizing is used for standing/moving facings.
- Idle: 3 frames / 3fps; walk: 6 distinct phases / 8fps; hurt: 2 / 6fps one-shot; down: 1 static. All living poses and their echo counterparts total 768 board frames. Echoes use stable translucency/scan bands rather than temporal flashing; reduced motion shows frame 0.
- 96×112 independently drawn busts in neutral/hurt/echo and one 96×160 full-body crew card per member: 32 portrait/card images. Headgear, facial features and equipment match the board identities; these are not enlarged board sprites.
- 296 immutable PNGs referenced by the two full manifests, including eight true single-frame idle images for the current M4 renderer. Earlier shipped hash-named images are preserved.
- Review page with every living facing/pose and source strip, a Living/Echo switch for every directional clip, individual hurt-pose replay, Pause/Still controls, and 1× phone / 2× TV portrait sizing. Names and roles remain live UI text.

## Cast and silhouettes

| Crew | Role | Shape cue |
| ---|---|--- |
| Ines Okafor | Salvage Chief | Compact helmet, broad command collar and square survey tablet |
| Tobe Raske | Rigger | Round hardhat, rope coil and projecting rigging hook |
| Sable Quist | Pilot | Swept hair, flight collar, headset and asymmetric thigh holster |
| Dr. Anselm Varga | Medic | Long split medical coat, spectacles and large side medical case |
| Pell Ostrander | Systems Tech | Square goggles, tall tool backpack and projecting probe |
| Juno Achterberg | Cargo Hauler | Wide padded shoulders, load frame, cropped hair and heavy gauntlets |
| Kit Sato | Scout | Pointed hood, long asymmetric scarf and narrow stance |
| Rhee Callender | Signals Officer | Twin radio aerials, broad earcups and chest receiver |

## Review and validation

[Actual 1080p crew line-up](lineup-tv.png): all eight at 48px TV size on 128px medical bays, each in colour, greyscale and echo. [Silhouette detail](crew-reference.png) is explicitly enlarged 3×; [four facings and walk phases](facings-review.png) shows facings at TV 2× and source walk frames at 1×. [Portrait contact](portraits-contact.png), [neutral/hurt at TV 2×](portraits-tv.png), [crew cards/echo at TV 2×](cards-tv.png).

[Idle](idle-preview.gif), [walking](walk-preview.gif) and [echo walking](echo-preview.gif) are enlarged 3× previews. Runtime metadata declares the actual fps/loop policy; hurt/down remain one-shot/static. Browser animation advances forward through the strip and respects reduced motion; no reversed walking loop is used.

35 focused Meridian tests pass (8 style, 13 compartment, 14 crew). Crew checks enforce exact content/facing/pose/echo/portrait coverage; names/roles and compatibility fields; bounds/anchor/transparent margins; distinct silhouettes without hue and four distinct facings per pose; six distinct walk phases; idle/hurt movement; echo coverage/opacity; independent portrait expressions/dimensions; palette; hash corruption rejection; unknown-ID rejection; immutable-file protection and byte-identical complete rebuilds. Pixelwise distinction is not a measured human recognition rate.

Real Chromium at 320×844, 390×844 and 1920×1080 loads all 161 images without overflow/page/request errors. Every board preview is exact 2×; source strips remain 1×; all 32 portraits switch between exact 1×/2× without phone overflow. Every directional echo loads, normal walking advances, Pause holds its frame, individual hurt Replay restarts/settles, and Still/reduced motion stop animation. Motion controls disable when system/Still preferences suppress animation.

Native Godot OpenGL captures the crew line-up, portraits and crew-card review screens at 1920×1080. `check-crew-native.py` verifies pixel identity and produces actual full-severity Machado deuteranopia/protanopia transformations in linear sRGB plus JPEG quality-55/4:2:0 stream proxies. Review these as limited screenshot evidence, not a physical-TV/human recognition test or an actual compressed Discord playtest. The existing unsupported V-Sync driver warning is retained; no native script/runtime error occurred.

Full `bash tools/check-all.sh --full` at frozen source `21ecbda2`: .NET 7,734/7,739 passed, five inherited failures, zero skipped and zero build warnings. Phone 601 tests and production build pass (two existing upstream SignalR annotation warnings); Node 24, Python 131 (61 assets + 9 audio + 35 Meridian + 26 lore), lore and docs drift pass. Overall exit 1 blocks merge. The seven former missing-recording failures cleared after syncing main 22ce39b7. The five remaining cases (flags, caption length, two bark punctuation expectations, one phone fixture) are retained by exact name in `validation.json` and the complete compressed log. No tests are skipped or weakened; manually disabled CI stays disabled.

## Reproduce

```sh
# Source /workspace/.arcbox/activate.sh on the managed machine when present.
python tools/meridian-assets/crew.py
python tools/meridian-assets/audit_crew.py --rules <current-crew.json>
python -m unittest discover -s tools/meridian-assets -v
python -m http.server 4185 --bind 0.0.0.0
# In another terminal:
node tools/meridian-assets/check-crew-browser.cjs
```

Native screenshots use the existing style review's `native-project.godot` / `native-display.gd`, a real X display, and `MERIDIAN_MOCK` / `MERIDIAN_SCREENSHOT` environment paths. Capture `lineup-tv.png`, `portraits-tv.png`, `cards-tv.png` to `native-lineup.png`, `native-portraits.png`, `native-cards.png`, then run `python tools/meridian-assets/check-crew-native.py`.

## Integration limits

This is the asset/review set, not an implemented crew-pick or animation state machine. The current #316 M4 reader draws the full idle image, so the flat `idle` field intentionally points to one 24×24 pose; `facings`/`echo` carry the complete animation strips for M5. Flat `walk` remains compatible with its existing fields. M5 also needs to select #384's `tiles[id].variants[deck]`; the current M4 tile reader predates that version-2 layout. No runtime TV integration or real gameplay playtest is claimed by the native image-display checks.

Final content check: #316 advanced to `809969e9`; its crew JSON is byte-identical to the pinned reference and all eight IDs / 768 frames / 32 portraits/cards / 296 referenced assets pass the live-data audit. The M4 art reader/renderer inspected for idle compatibility is unchanged from 1d770a73. All three native images, six actual CVD simulations and three JPEG proxies were visually reviewed; geometry/equipment cues remain present when hues converge. No human recognition rate is claimed.

Final source stayed unchanged during the full gate; the later commit records documentation/evidence only. `validation.json` includes source digests and run provenance. `screenshot-hashes.json` hashes the retained review images.
