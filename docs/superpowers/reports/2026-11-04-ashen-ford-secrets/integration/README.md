# Issue #81 · final integration

> Historical review checkpoint. The owner subsequently authorized push and merge; [current-main integration and release checks](../merge/README.md) supersede earlier no-PR instructions below.

The owner approved both Scenario concepts, replaced PixelLab with local authoring, then approved the native art and TV previews with **“I approve”** on 2026-11-05. Integration source commit: `d7583d80` on `ashen-ford-secrets`. No additional approval is needed for these assets or their reviewed layout. No PR is to be opened.

The locked granary, Maud's dressed cottage, cold-ash herb bed and stool/pots are now in the live town/props manifests. Every installed sprite matches its approved PNG bytes. The authoring scene places the 3×2 granary at [39,71] and retains Maud's 2×2 cottage at [130,88]. Existing sacks/cart furnish the storage yard; four skull pikes are removed near Maud. Quest interactables, both story markers, terrain, stamps, spawns, people, flags, rewards and voiced lines remain unchanged.

The editor prop palette gains the two new prop swatches. All 988 existing painted prop cells were remapped by identity before the approved prop changes; the final scene has 990 cells. Re-exporting the saved scene reproduces the approved preview's semantic layout, and two headless exports produce byte-identical world JSON. All existing shipped PNGs and pre-existing prop definitions are preserved.

## Rebuilding the new art

```sh
python tools/tactics-assets/build.py lore_places
```

This additive command rebuilds only the four authored families and their metadata. The town/props generators also append their respective new families after generation. The legacy full prop generator is not the authoring source for the game's curated/imported extras; the integration audit restored those existing files and definitions rather than shipping a legacy regeneration over them. Use the additive command for this art. No new generation credits or external art service is required.

## Verification

The integrated game/content source `d7583d80` passed the full solution build with **zero warnings/errors** and all **6,092 .NET tests** (zero failed/skipped). All **504 phone tests** passed. The Python audit was refreshed in `a2fc91f3` to handle existing per-variant dimensions, uniform higher-density bridge textures, aliased files and retained unreferenced crow sheets; it now passes all **five art checks**, accounting for **647 environment PNGs**. These audit/doc changes do not alter game content, so the .NET suite was not repeated afterward.

- [Solution build](solution-build.txt), [full .NET suite](dotnet-suite.txt), [phone suite](web-tests.txt), [art checks](overworld-art.txt).
- [Export/layout/approved-PNG proof](integration-proof.json), [first export](export-1.txt), [second export](export-2.txt).
- [Palette identity remap](props-remap.txt), [authored prop changes](scene-props.txt).
- [Live route checks](live-routes.json): all 12 story legs and both recovery routes pass with shortcuts closed; review standing tiles and both story markers remain walkable.
- [Two additive rebuilds](additive-rebuild.json): all 4,758 live pack files remain byte-identical.
- Fresh live-pack TV captures: [granary](granary-after.png), [marker clearance and normal fade](granary-marker.png), [Maud's home](maud-after.png). [TV output](live-art-tv.txt) contains no errors. These show the integrated pack rather than the earlier disposable preview. Character/building scale, lighting, dressing attachment and clear approaches were inspected visually.

The final fresh Chapter 1 TV/server run **passed**: four connected phone clients, one won Storming the Ford fight, all 94 selected story lines said exactly once in graph order, the Chapter I end card, then four walking steps. The gated claw-mark narrator clip played after Maud. One route was recalculated after battle fall-in; progression completed normally. [Harness output](chapter-force.txt), [TV log](chapter-force-tv.txt), [phone log](chapter-force-phones.txt), [live Maud scene](chapter-maud.png), [end card](chapter-card.png), [walking afterward](chapter-walking-after.png). The headless OpenGL software renderer establishes visual correctness and progression, not hardware frame pacing. Part 1's optional parley run did not reach its card after two defeats; no new live parley pass is claimed.

Reproduce the automated checks from the repository root:

```sh
dotnet build ArcBox.slnx --no-restore
dotnet test ArcBox.slnx --no-build --no-restore --verbosity normal --max-parallel-test-modules 1
python tools/tactics-assets/test_overworld.py
(cd web/play && npm test -- --run)
dotnet run tools/tactics-assets/concepts/ashen-ford-secrets/verify-local.cs -- content/tactics/base
```

The final live run uses `tools/tv-e2e.sh --skip-build` with `ARCBOX_GAME=tactics`, `ARCBOX_CHAPTER=1`, `ARCBOX_BRANCH=force`, `ARCBOX_LINE=0.1`, four default bot phones, Godot 4.7.2 .NET and `TV_RENDERING_DRIVER=opengl3`. The local Godot wrapper selects `gl_compatibility` and the Dummy audio driver; Xvfb supplies the 1280×720 screen. No campaign/beat seed is supplied.

OW-34 is complete. OW-33 remains complete; this integrated pass rechecks its content alongside the new art. No PR has been opened.
