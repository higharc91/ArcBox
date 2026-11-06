# Issue #81 · integration with current main

The owner approved the locally authored art and TV previews, confirmed “Approve both for integration”, then explicitly requested **“push and merge it”** on 2026-11-05. This supersedes the earlier no-PR/no-merge instruction. The native pixel art still uses the existing town renderer and material ramps; no new paid generation was needed.

The merge candidate combines `ashen-ford-secrets` (`42699a2a`) with current main (`b5a552ec`). It retains Ashmaw's bounty/cave, all roaming arenas, current bridge/plug rendering, host-view patches and portability fixes. The 30 merged interactables contain every one of main's 22 unchanged plus issue #81's eight additions. All 1,341 existing audio line mappings remain unchanged; twelve new mappings bring the total to 1,353. Existing main PNG and Ogg bytes are preserved. [Preservation proof](preservation-proof.json).

The notice placement adjustment is **`af_bank_claw_marks`: [121,80] → [118,80]**. Main's new `mob_kobold_scouts` arena covers its old position. The adjacent west-bank position is outside that arena, passes export validation and preserves the notice's name, text, story gate and audio. The reed track, cellar, granary quest sack and story markers retain their approved positions.

## Authoring and art

The main scene retains its terrain, all six stamps, return spawn, beats and people. Approved granary/Maud building replacements and yard dressing are applied to it. Main had moved two of Maud's four skull pikes; the approved removal is applied at those new locations as well. Both Ashen Ford and Cinder Hollow prop layers were remapped from main's old manifest by prop/variant identity before the new swatches were used: 927 painted cells in Ashen Ford and four in Cinder Hollow. The native yard dressing adds six and removes four; the bank rock adds one more, for 930 painted cells in Ashen Ford. Cinder Hollow's scene and exported world remain byte-identical to main.

Both regions exported twice with byte-identical output and no export problems. [Export receipts](exports.txt), [Ashen Ford remap](remap-ashen-ford.txt), [Cinder Hollow remap](remap-cinder-hollow.txt). All approved sprite PNG hashes still match. The art audit includes the existing candle/blood/bone families added on main and accounts for all **651 environment PNGs**.

The newer terrain reduced the reed shortcut's advantage below its existing minimum. A native dark medium rock at **[117,84]** closes an unintended bank bypass while leaving the ordinary route open. The hidden path stays at [118,86], its original standing/check tile [118,84] stays clear, and its DC/skill/facing are unchanged. The existing shortcut test now passes without changing its threshold. [Shortcut check](shortcut-test.txt), [bank edit](bank-rock.txt).

Fresh merged-pack TV captures were visually reviewed: [granary](granary-after.png), [marker clearance/fade](granary-marker.png), [Maud](maud-after.png), [bank dressing](reed-track.png). [TV output](live-art-tv.txt) has no errors. These world-demo views check scale, lighting, dressing and clearance; the bank demo is not a claim about a hidden passage's discovery state. Runtime discovery is covered by the game/logic tests.

## Validation

- [Full solution build](solution-build.txt): zero warnings/errors.
- [Phone tests](web-tests.txt): all **517** pass. [Phone production build](web-build.txt) passes.
- [Art checks](art-tests.txt): all **five** pass.
- [Final full .NET suite](dotnet-suite.txt): all **6,376** pass, zero failed/skipped.
- The first .NET run was stopped after a merge-resolution error in the core-walking test helper: it had lost the branch's exclusions for region speech and plugs when retaining main's newer patch-budget tests. The helper is restored; full-view network patch tests still include those fields and keep their original limits. [Rebuild](test-rebuild.txt). The subsequent full run passed 6,375 of 6,376 tests; the unchanged shortcut-utility test exposed the bank bypass described above. That placement is corrected and its targeted test passes. The complete final suite passed all 6,376 tests after both corrections, with no thresholds loosened.

Fresh TV review is complete. The fresh live four-phone Chapter 1 force run **passed** on the combined source `183d16a9`: one won Storming the Ford fight, all **94** selected story lines said once in graph order, end card displayed, and four walking steps afterward. One route was recalculated after battle fall-in and progression completed normally. No TV errors. [Harness output](chapter-force.txt), [TV log](chapter-tv.txt), [phone log](chapter-phones.txt), [Maud in the live story](chapter-maud.png), [end card](chapter-card.png), [walking afterward](chapter-walking-after.png). Earlier branch-only checks are historical evidence in the sibling `integration/` report; this page records the combined result against current main.

## Publication

The approved art, issue #81 content and current-main integration are complete and verified. The owner authorized pushing and merging. The final publication commit adds evidence/documentation only after source `183d16a9`; it does not alter the tested runtime, content or scenes. The prior no-PR restriction is superseded. OW-33 and OW-34 are complete. No live parley end-card pass is claimed for this merge; both branches' story/route and companion cases passed in the full suite. All processes started for these checks were stopped.

Reproduction uses .NET 10, Python/Pillow/numpy, Node and Godot 4.7.2 .NET:

```sh
dotnet build ArcBox.slnx --no-restore
dotnet test ArcBox.slnx --no-build --no-restore --verbosity normal --max-parallel-test-modules 1
python tools/tactics-assets/test_overworld.py
(cd web/play && npm test -- --run && npm run build)
```

The actual TV check uses `tools/tv-e2e.sh --skip-build`, `ARCBOX_GAME=tactics`, `ARCBOX_CHAPTER=1`, `ARCBOX_BRANCH=force`, `ARCBOX_LINE=0.1`, and the default four bot phones. There is no campaign/beat seed. This cloud run uses an explicit Godot binary, OpenGL compatibility, 30 fps cap, Dummy audio, Xvfb and absolute screenshot/output directories; it establishes visual correctness and progression rather than real-GPU frame pacing.
