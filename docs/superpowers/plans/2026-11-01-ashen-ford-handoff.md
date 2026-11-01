# Handoff: finish rebuilding Ashen Ford

You're taking over from a Claude session on the user's Windows PC. The user is the owner of ArcBox. Wardenflame is the game's display name, and its ids stay `tactics`.

## Where the work is

All of it is on the **local** branch `world-dressing-design` in `C:\Users\fatma\Programming\dotnet\ArcBox`. **Most of it isn't pushed.** The remote branch of that name holds only the two early planning commits, and the local one was rebased on `ccr-18ad4510-q68nad`, so pushing needs `--force-with-lease`. Ask the user before pushing. Don't open or merge PRs unless asked. Don't touch `ccr-18ad4510-q68nad`: a cloud session pushes there.

Confirm the state first:

```sh
git checkout world-dressing-design
export PATH="/c/Users/fatma/AppData/Local/Microsoft/dotnet:$PATH"   # the first dotnet on PATH has no SDKs
dotnet build ArcBox.slnx
dotnet test --solution ArcBox.slnx
```

Expect 0 warnings and no failures, with the exception of `A_rebuild_on_the_largest_region_is_quick`, a known timing flake (`StagePlanTests.cs:378`), which also fails in Release.

## Read these

- `docs/superpowers/specs/2026-11-01-ashen-ford-rebuild-design.md`: the design, per place. **This is the brief.**
- `docs/superpowers/mockups/tactics/02-tv-explore-vote.png`: **the bar.** Look at it. The user judged the region "awful" before, so judge your work by eye against this.
- `content/tactics/base/lore/regions.json`: what Ashen Ford is. "Golden-hour autumn turning cold. Woodsmoke, wet ash, a town that is pretending to be fine."
- `docs/superpowers/reports/2026-10-31-tactics-overworld/place-*.png`: the current look.
- `docs/tv-host.md`, "The world" and "Fights in the world".
- `docs/superpowers/plans/2026-10-31-tactics-overworld-cloud-handoff.md` §2: the user's rules, and §8: the user's list.

## What's done

- Real town-kit buildings in the world, with shadows, smoke, signs, lit windows and a whole-building fade.
- Painter's order fixed, with no separate tall layer: faded items paint in sorted place. Blocking props and buildings now block on the TV as on the server.
- Dusk light for the region and fights at the ford, and a region-level ground grade.
- Terrain repainted by place, and Emberwick, fields, road, burned ground, ford, docks, hills and Maud's hut built by hand in `AshenFordDraft`.
- Story walk times hold (start to ford about 42 s), and both story branches walk.

<!-- STATUS -->

## What's left, in order

1. **Finish anything marked unfinished above.** It's the list A to J: ground edges, rock, road, ford, camp, hills, woods, people, workspace markers, the whole walk and e2e runs.
2. **Put the new props in.** The chosen sprites are in `docs/superpowers/reports/2026-10-31-tactics-overworld/new-props/`, with `tent_hide_source_1024.png` as the tent's full-size source. The user approved them. The comparison sheet is `art-props-compare.png` beside that folder. They're opaque-or-clear sprites, generated with Scenario.
   - Move them into `content/tactics/base/props/`, and update `props/manifest.json`: `size`, `anchor` (the ground contact pixel) and `height`. The old sizes are much smaller.
   - `tent_hide.png` (96×89) is a new `hide` variant of `tent`. Keep `canvas` and `red`. Use it at the goblin camp.
   - `haystack`, `sack`, `stump`, `log`, `signpost` replace their old art. Check each prop's variants in the manifest first.
   - `rubble`, `gravestone`, `grave_cross` have several colour variants in the manifest, and only one new look. Add the new one as a variant, and use it in Ashen Ford.
   - Shrink `gravestone`, `grave_cross` and `signpost` to about 70%: they're nearly door height.
   - `lantern_iron.png` is a **new prop**, not a replacement: the old `lantern` is an 8-frame animated stone lantern. Add it with light data, and use it at the docks and market. Its base is off-centre, so set the anchor under the base.
   - Rebuild the palette (`tools/tactics-world/palette/props.*`) with the workspace's tools, then re-export, and check the export is byte-identical on a second run.
   - The prop's size may shift footprints and sorting. Sweep `OverlayPictureTests`, and look on the TV.
3. **Walk the whole region on the real TV** and compare with mockup 02. Fix what's wrong. Be harsh: for each place ask whether someone would name it without being told.

## If there's time

- **Ground edges:** judge the dither blend (`GroundBlend`, `9b8fe721`) on the TV. If the edges still look harsh, try Scenario autotile: `model_retrodiffusion-tile`, `style: tileset_advanced`, 32 px, about 5 CU, passing our own top textures as `image` and `extraImage`. A text-only test had soft edges but the wrong colours (`art-test-tileset.png`). Don't spend credits without asking.
- **Known looks left:** a lamp's halo shows through a wall in front of it; ambient life is narrower than the spec (birds and leaves are missing); the overlay query is main-thread and linear, so check it in the town with people in it.

## Rules that bind you

- Don't change game rules, balance numbers or the server-to-phone contract. If you think you must, stop and ask.
- No new generated art without asking: Scenario spends the user's credits. `docs` holds no key; the method is in the note below.
- Read frames, sizes, anchors and variants from manifests. Never hard-code them.
- Anything worded, ordered or computed is pure C# with tests, shown failing first. Godot classes only draw.
- Keep the `river_crossing` stamp's tiles, the ford as the only river crossing, both story branches, and start to ford at 35 to 50 s. Don't weaken a check to pass it, and say so if you change a threshold.
- Battles must not regress: `hall_of_the_flame:fight` is 521 draw calls, `river_crossing` 420.
- 144 fps on the RTX 5080, with no frame regularly over 16 ms. Measure with no screenshots: `ARCBOX_SHOTS=0` on `tools/tv-e2e.sh`, because captures stall frames.
- Stage files by name. Keep `.superpowers/` and `.claude/` out of commits. Commit trailer, last line: the attribution for your own model.

## Running the TV

The user games on their **right** monitor. Godot windows must open on the **left** and never take focus:

- `--position 160,100` before the `--`, and `--audio-driver Dummy`, `--quit-after`.
- An uncommitted `src/ArcBox.Host/override.cfg` with `[display]` then `window/size/no_focus=true`. Delete it afterwards, even on failure.
- Godot: `/c/Users/fatma/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`
- Python isn't installed. Use node or PowerShell. Stop everything you start.
- Seed the party near a place: `ARCBOX_SERVER_ENV="Tactics__Seed__Region=ashen_ford Tactics__Seed__At__0=<x> Tactics__Seed__At__1=<y>"` with `tools/tv-e2e.sh`. Dev flags: `--world-demo`, `--world-looks`, `--world-route`.
- `tv-e2e` modes: `ARCBOX_GAME=tactics`, plus `ARCBOX_WALK=1`, `ARCBOX_RECONNECT=1`, `ARCBOX_RESTART=1`, `ARCBOX_STAGE=1`. REDLINE modes need Playwright, which isn't installed: the user said not now.

## Scenario, if the user approves more art

Team `team_5nToMAwZ2bBo1gqSuy5933dS`, project `proj_DXXKthLZvfZMp4svC2fRJksp`, model `model_google-gemini-nano-banana-2-1`, 1K, 1:1, thinking HIGH, 2 outputs, 18 CU a prop, at most 7 jobs at once. Reference image: `asset_ntAQoCQzWzJvod4xRctfGcsm` (the town kit's cottage), in `referenceImages`. Prompt: "Using the reference image only for its art style, draw a single new game prop: ...", then same crisp hand-placed pixel art, dark 1-pixel outline, light from the top-left, 2:1 isometric angle, one object whole and uncropped, on flat solid magenta #FF00FF, no ground, shadow or text. Cut out with R>170, G<110, B>170, box-average opaque pixels only, alpha 0 or 255. Judge at game scale next to a real building, never at full size.

## For the user, later

- Speech fallback, environmental tiles and the other decisions: the cloud handoff's §8.
- Remaking character models. A 32×40 sprite sheet test copied the look without improving it, so better characters need bigger frames, which is an engine change, and the layered player characters are the hard case. See `art-test-innkeeper-sheet.png`.
- Regenerating the rest of the weak art by the same method.
- Painting in the real Godot editor hasn't been tried by a person yet.
