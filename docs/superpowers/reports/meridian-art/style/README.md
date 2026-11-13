# Meridian style samples — issue #331

Original code-built art for the first Meridian review. No paid generation, new game rules or changes to existing Wardenflame art/audio. `index.html` is the interactive review page; serve the repository root with `python3 -m http.server 4182` and open `/docs/superpowers/reports/meridian-art/style/index.html`.

## Delivered

- Six-shade arcpix ship ramps, three deck accents/marks, exact six platform seat colours, asset sizes and immutable file rules in `tools/meridian-assets/STYLE.md`.
- Four 64×64 samples: Command bridge, Habitat medical bay, Engineering reactor core, and Airlock. Four separate north-edge hatch overlays: open jambs, closed split leaves, welded sealed X, continuous wall.
- One original Ines Okafor crew sample: 24×24 standing plus a four-frame 6fps walk, feet anchor (12,22). The gallery shows source frames and 2× crew beside the 2× room tile.
- A 1920×1080 partly explored-deck concept mock with two crew markers. Sample rooms repeat to demonstrate spacing; this is not a legal tile draw, implemented Meridian gameplay, or the full UI set.
- Versioned manifests in `content/meridian/base/{tiles,hatches,crew}` with SHA256-named immutable PNGs. IDs match the rules on unmerged PR #316 (`af068bb1`); that dependency is explicit, rather than claiming those rules are already on main.

Full art sets #332–#338 are deliberately awaiting the owner's style approval required by #331. No approval, physical-TV playtest or compressed-stream usability test is claimed.

## Validation

Eight contract checks pass: deterministic second build; rejection of changed bytes under an existing hash name; 16px door gaps under quarter turns; identical wall lighting on all edges; distinct hatch shapes and transparent open passage; walking frame/anchor bounds; no visible pure black/white; manifest SHA/bounding-box checks and 1920×1080 mock size. No random noise, resynthesis or interpolation is used.

Real Chromium: 67 images in the initial review, 70 after adding the explicit crew source-frame row. The retained final `browser.json` and screenshots cover 320×844, 390×844 and 1920×1080. All images load, no page overflow or JS errors occur, and displayed tile ratios are exactly 1× or 2×. Reduced motion disables animation; a separate normal-motion check verifies that walking starts and Pause stops it. The large TV mock pans in an inner labelled panel instead of being scaled fractionally.

Godot 4.7.2 OpenGL displays the mock at native 1920×1080 with nearest-neighbour sampling (`native-mock.png`, `native-log.txt`, retained GDScript/project). This is a native image-display smoke check, not integration with Meridian's future TV scene. The driver emits its existing unsupported V-Sync warning; no native script/runtime error occurred.

Actual full-severity Machado 2009 deuteranopia/protanopia transformations of the native screenshot are retained with `simulate-cvd.py`. Both were visually inspected: deck names/count marks, room geometry, open versus closed hatch structure, and numbered crew markers remain present. Some accent hues converge, so colour alone is not treated as sufficient. These are limited screenshot simulations, not human recognition-rate measurements.

The full repository command is `bash tools/check-all.sh --full`. It also runs the new Meridian contract suite through the existing Python group. The command began at `f7fbba7d`; the gallery source-frame row and hatch bounding-box metadata were tightened before its Python phase, at asset code head `558abe07`. C# and phone production sources are unchanged from main. Final group results and complete compressed output are recorded in `validation.json` and `full-check.txt.gz`. CI remains manually disabled. A passing full gate and owner style approval are still required before merge/closure and full-set production.

Final full-check result: .NET 7,727/7,739 passed, 12 failures exactly matching the recorded main baseline, zero skipped and zero build warnings. Phone 601 tests plus production build pass (two existing SignalR annotation warnings); Node 24 and Python 104 tests pass (61 Tactics assets, 9 audio, 8 Meridian assets, 26 lore). Lore and documentation drift pass. Overall exit 1 blocks merge. The native mock and both colour-vision simulations were visually reviewed; owner style/readability approval remains pending.
