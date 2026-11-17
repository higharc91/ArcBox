# Meridian art integration / #331–#334

This integrates the owner-approved style, TV-refined compartments, complete crew and first-wave cards. Per-set review records: #357, #384, #391; integration PR #402. All art is original deterministic arcpix/Pillow work, 0 CU. Existing Tactics art, recordings, gameplay and balance are preserved.

**Merge status: full combined validation pending.** Baseline repairs and warning-free phone builds landed in #403, main `8863818a7c30c8b27d09ffe84b8fa26fe8c9f864`; this branch includes that main. Earlier per-set reports retain their historical failed gates; those are not represented as current passing checks. The integration gate below must finish before merge.

| Set | Delivered | Review |
|---|---|---|
| Style #331 | Six-shade ship ramps, exact player colours, immutable file contract, approved samples | [Style](../style/README.md) |
| Compartments #332 | 48 IDs / 50 physical tiles / 57 deck variants, all rotations, symbols, hatch states, shaft overlay, backgrounds and safe one-shot hatch motion | [Rooms](../tiles/README.md) |
| Crew #333 | Eight crew, four facings, 768 living/echo board frames, 32 independent portraits/cards | [Crew](../crew/README.md) |
| Cards #334 | Three nine-slice frames, six symbols, twenty item icons, all 32 real cards reviewed as live text | [Cards](../cards/README.md) |

The exact #316 checker at `00d4a52bb9c7e73a5ee72b117b91891e6c0cdd11` builds without warnings/errors and accepts every delivered set: 48/48 tiles, 4/4 hatch states, 8/8 crew, 8/8 portrait sets, 3/3 frames and 23/23 item/kind icon IDs. It reports no errors/warnings. The local compartment audit also requires and hashes the supplemental `oneWay` shaft overlay. Canonical frame `insets` and kind-icon keys match the published M5 contract. [Checker/build evidence](../cards/game-contract-check.txt.gz).

Real Chromium phone checks at 320×844 / 390×844 and 1920×1080, native Godot 1080p image-display captures, actual deuteranopia/protanopia screenshot transformations and JPEG stream proxies are retained in each set's report. Integer scale, live names/rules, motion controls and reduced motion are checked. No human recognition study, physical-TV test, actual Discord playtest or runtime gameplay integration is claimed.

## Full gate

Run `bash docs/superpowers/reports/art-merge-validation/run-full-serial.sh`. It invokes the unchanged `bash tools/check-all.sh --full`, adding only `--max-parallel-test-modules 1` through DOTNET to avoid module contention in timing tests. Every test, timing limit and assertion remains enabled; CI stays manually disabled. Record the frozen source, complete exit status, totals, zero build warnings and full log in this directory before merging.

## Runtime boundary

This lands assets and review tooling. #316 is still a separate game-code PR. M5 consumers must select deck variants, directional/echo clips and portraits, the supplemental shaft overlay and hatch motion, and card frames/icons. The flat 24px idle compatibility image remains usable by M4's whole-image draw. Runtime UI pieces/tokens/FX/audio are later art sets; none is replaced with a false completion claim here.

## Reproduce assets

Use the established activation script when present. Run `compartments.py`, `crew.py`, then `cards.py` under `tools/meridian-assets/`; audit each against current #316/main rule files. The sample-only builder intentionally refuses to overwrite completed tiles/crew. Hash-named PNGs are immutable; these metadata corrections alter no image bytes.

Current-content audits also pass against the actual rule files extracted from #316 at the head above: 48 tile IDs / 57 deck variants / 70 referenced room-hatch assets; eight crew / 768 canonical board frames / 32 portraits-cards / 296 assets; 32 cards / twenty icons / six glyphs / three frames / 29 assets. See `live-*-audit.json` and `live-rules.json` for exact content hashes and reference comparisons.
