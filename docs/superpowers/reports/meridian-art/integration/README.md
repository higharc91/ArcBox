# Meridian art integration / #331–#334

This integrates the owner-approved style, TV-refined compartments, complete crew and first-wave cards. Per-set review records: #357, #384, #391; integration PR #402. All art is original deterministic arcpix/Pillow work, 0 CU. Existing Tactics art, recordings, gameplay and balance are preserved.

**Final gate: PASS.** Frozen source `6e8a518e10a3282ff9452e3dbb76a0e34663ffbd`, based on merged #403 / main `8863818a`: .NET **7,743/7,743**, phone **601** plus production build, Node **24**, Python **142** (including all **46 Meridian** checks), lore and docs all pass. **Zero build warnings/errors and zero skipped tests.** [Result/provenance](validation.json) · [Complete log](full-check.txt.gz). Final evidence changes docs only. Earlier per-set and disk-exhausted runs are retained as historical evidence, not counted as passing gates.

| Set | Delivered | Review |
|---|---|---|
| Style #331 | Six-shade ship ramps, exact player colours, immutable file contract, approved samples | [Style](../style/README.md) |
| Compartments #332 | 48 IDs / 50 physical tiles / 57 deck variants, all rotations, symbols, hatch states, shaft overlay, backgrounds and safe one-shot hatch motion | [Rooms](../tiles/README.md) |
| Crew #333 | Eight crew, four facings, 768 living/echo board frames, 32 independent portraits/cards | [Crew](../crew/README.md) |
| Cards #334 | Three nine-slice frames, six symbols, twenty item icons, all 32 real cards reviewed as live text | [Cards](../cards/README.md) |

The exact latest #316 checker at `93d5b3353d6f4f4c1cb23eda5fda0bd06003efd3` builds without warnings/errors and accepts every delivered set: 48/48 tiles, 4/4 hatch states, 8/8 crew, 8/8 portrait sets, 3/3 frames and 23/23 item/kind icon IDs. It reports no errors/warnings. The local compartment audit also requires and hashes the supplemental `oneWay` shaft overlay. Canonical frame `insets` and kind-icon keys match the published M5 contract. [Current checker evidence](current-contract-check.txt.gz) / [current build](current-contract-build.txt.gz).

Real Chromium phone checks at 320×844 / 390×844 and 1920×1080, native Godot 1080p image-display captures, actual deuteranopia/protanopia screenshot transformations and JPEG stream proxies are retained in each set's report. Integer scale, live names/rules, motion controls and reduced motion are checked. No human recognition study, physical-TV test, actual Discord playtest or runtime gameplay integration is claimed.

## Full gate

Run `bash docs/superpowers/reports/art-merge-validation/run-full-serial.sh`. It invokes the unchanged `bash tools/check-all.sh --full`, adding only `--max-parallel-test-modules 1` through DOTNET to avoid module contention in timing tests. Every test, timing limit and assertion remains enabled; CI stays manually disabled. Record the frozen source, complete exit status, totals, zero build warnings and full log in this directory before merging.

## Runtime boundary

This lands assets and review tooling. #316 is still a separate game-code PR. Current #316 already selects tile deck variants. The full-set crew, portrait and supplemental hatch selections still need the explicit mappings below before the game PR is called integrated. The flat 24px idle compatibility image remains usable by M4's whole-image draw. Runtime UI pieces/tokens/FX/audio are later art sets; none is replaced with a false completion claim here.

## Reproduce assets

Use the established activation script when present. Run `compartments.py`, `crew.py`, then `cards.py` under `tools/meridian-assets/`; audit each against current #316/main rule files. The sample-only builder intentionally refuses to overwrite completed tiles/crew. Hash-named PNGs are immutable; these metadata corrections alter no image bytes.

Current-content audits also pass against the actual rule files extracted from #316 at the head above: 48 tile IDs / 57 deck variants / 70 referenced room-hatch assets; eight crew / 768 canonical board frames / 32 portraits-cards / 296 assets; 32 cards / twenty icons / six glyphs / three frames / 29 assets. See `live-*-audit.json` and `live-rules.json` for exact content hashes and reference comparisons.

The first combined run exhausted the 32GB workspace while duplicating content into a second set of Release outputs. It was stopped; its log is retained as `superseded-disk-full.txt.gz` and is not counted as passing. Only ignored Release build outputs were removed from the primary checkout. The identical frozen commit is then validated in the existing validation worktree, reusing its already populated content cache. No source or runtime asset is changed by this recovery.

## Verified current consumer gaps in open #316

The generic checker accepts the complete manifests, but acceptance of entries is distinct from selecting them in gameplay. At `93d5b3353d6f4f4c1cb23eda5fda0bd06003efd3`:

- Crew assets expose `crew/id/facings/{south,east,north,west}/{idle,walk,hurt,down}` and `crew/id/echo/direction/pose`. `MeridianArt.CrewFrames` currently searches `crew/id/animation/{n,e,s,w}` and then flat compatibility entries, so it falls back instead of using the full facings/poses.
- Portraits expose `portraits/id/{neutral,hurt,echo,crewCard}`. The TV `Portrait` helper expects `portraits/id`; the phone expects `portraits[id].file`, so explicit neutral/expression/card selection is required (the current phone shape would produce an undefined image URL).
- `hatches.oneWay` and `hatches.animations` are supplemental assets, requiring explicit selection/loading rather than an extra legal hatch state.

These are disclosed engineering follow-ups in the still-open game PR, not missing asset files. No game-code branch was imported into this asset PR. All five current rule JSON files remain byte-identical; the current checker was freshly built and run against this art directory with no errors/warnings.
