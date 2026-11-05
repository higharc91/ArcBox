# OW-08 · bridge clipping

When the ford is shown during walking or combat, the wooden span stops at a straight vertical cut, and an actor behind the cropped planks can be hidden by that false edge. The same defect reproduces on the current integrated Ashen Ford pack.

The timber bridge image is **192×128**, while its manifest deliberately draws it at **96×64** with anchor **[94,62]**. `BoardPainter.PaintSprite` used the destination dimensions as the source rectangle, reading only the upper-left quarter of the texture. The three bridge variants use the same higher-density layout. This is a source-sampling error rather than a battle-stamp or block-edge clip.

The renderer now samples a complete frame using the loaded texture dimensions. Destination size, ground anchor, lighting, depth order, original PNGs, map, collision and game rules are unchanged. Animated strips use their actual frame width; cached world-animation textures are already one frame. The glow pass shares the same source rectangle, and texture dimensions are cached when binding rather than queried per draw.

## Visual review

[Interactive before/after comparison](compare.html). These are actual Godot world-renderer captures from the same saved pack, camera, standing positions and 1280×720 configuration. The full span and both stone endcaps now render without the false cut. Normal characters and environmental animation remain present; unrelated smoke/animation phases can differ between captures.

| View | Before | After |
| --- | --- | --- |
| West lane / figure behind span | [Before](before-west.png) | [After](after-west.png) |
| Deck / far-bank contact | [Before](before-deck.png) | [After](after-deck.png) |
| East approach | [Before](before-east.png) | [After](after-east.png) |

[Before TV log](before-tv.txt) and [after TV log](after-tv.txt) contain no TV errors. OpenGL compatibility under Xvfb is a visual check, not a hardware frame-pacing measurement.

The [live round-3 combat capture](combat-after.png) also shows the complete span with party members beside its banks and endcaps, without the previous false cut.

## Regression checks

`OverlayPictureTests` now samples alpha masks from a full physical frame at destination resolution, matching the renderer. It previously read the texture's upper-left pixels directly, so the bridge's higher-density art was also incorrectly represented in that test harness. A new shipped-bridge check asserts that the entire 192×128 source is sampled while the destination stays 96×64. Two sweeps walk three party members through both ford lanes in both directions and compare the resulting pixels with global painter's order; both pass with zero incorrect pixels. All route cells are walkable.

Five source-rectangle cases cover native-size stills, higher-density stills, animated strips, animation wrapping and cached single-frame textures. [All 333 targeted art-index/overlay checks pass](targeted-tests.txt). [The full solution builds](solution-build.txt) with zero warnings/errors.

[All 2,446 presentation tests pass](presentation-tests.txt), with zero failures/skips. The fresh four-phone Chapter 1 force run **passed**: Storming the Ford won, all 93 selected story lines said once in graph order, end card displayed, and four walking steps afterward. One route was recalculated after battle fall-in and progression completed normally. No TV errors. [Harness output](chapter-force.txt), [TV log](chapter-tv.txt), [phone log](chapter-phones.txt), [end card](chapter-card.png), [walking afterward](chapter-walking-after.png). No PR is to be opened.

Source under test: `38d57465` on `ow-08-bridge-clipping`, based on issue #81’s completed integration.

OW-08 is complete on this branch. No other board item was implemented. Gameplay code, content PNGs/manifests, collision, saved scene and region JSON are unchanged from `42699a2a`. The shared rendering change was verified with the full presentation suite and live host; unrelated .NET modules and unchanged phone code were not retested for this fix.

Reproduce from the repository root with .NET 10 and Godot 4.7.2 .NET:

```sh
dotnet build ArcBox.slnx --no-restore
dotnet test --project games/Tactics/Tactics.Presentation.Tests/Tactics.Presentation.Tests.csproj --no-build --no-restore
```

For TV captures, use `--world-demo=ashen_ford` and `--world-looks=bridge-west@108,60@106,59;bridge-deck@108,60@108,60;bridge-east@108,60@109,61`, with an absolute `--shots` directory. The live check uses `tools/tv-e2e.sh --skip-build` with `ARCBOX_GAME=tactics`, `ARCBOX_CHAPTER=1`, `ARCBOX_BRANCH=force`, `ARCBOX_LINE=0.1`, the default four bot phones, and no campaign seed. This cloud run used OpenGL compatibility, Xvfb, a 1280×720 screen, 30 fps cap and Dummy audio. All processes started by these checks were stopped.
