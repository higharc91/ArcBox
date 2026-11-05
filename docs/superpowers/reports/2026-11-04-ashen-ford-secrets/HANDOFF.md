# Issue #81 handoff

Branch: `ashen-ford-secrets`; cloud worktree `/workspace/arcbox-secrets`. The original Windows worktree is `C:\Users\fatma\Programming\dotnet\ArcBox\.claude\worktrees\ashen-ford-secrets`; fetch the branch there to receive cloud changes. **The owner now requests push and merge.** The earlier no-PR instruction is superseded. See [current-main integration](merge/README.md) for merge resolution and release verification.

## Part 1 · complete

Ashen Ford has a DC 12 Survival reed-bank path at [118,86], a DC 13 Investigation cellar hatch revealed by a nearby draft, a ledger and one Smoke Bomb behind the open flag, and three Chapter 1 gated notices. `StoryOrder.cs` treats undiscovered paths/doors as shut. Narrator discovery speech, twelve Ogg clips, tests and TV stand-ins are committed. No early ember disclosure was added. OW-33 is done.

The [part 1 verification](verification/README.md) records 6,084 passing .NET tests, 504 phone tests, a clean solution build and an actual force-route Chapter 1 end-card pass. The optional parley run lost River Crossing twice and was stopped; no live parley end-card pass is claimed. Companion tests cover both routes. Current main includes the low-wall plug rendering fix from PR #91; broader interaction decisions are recorded in interactables design §7.8.

## Part 2 · approved and integrated

The owner approved Scenario concepts with “Looks good”, replaced PixelLab with “We don't have access to pixal lab, you make it”, then approved the native art and TV previews with **“I approve”** on 2026-11-05. No further art approval, PixelLab connection or generation credits are needed. The Scenario concepts cost 39 CU; local art cost zero generation credits.

`tools/tactics-assets/parts/lore_places.py` authors the locked granary, Maud's dressed cottage, cold-ash herb garden and stool/pots using production town geometry/materials. All approved PNG bytes are installed in live manifests. The saved `tools/tactics-world/regions/ashen_ford.tscn` places the 3×2 granary at [39,71], Maud's 2×2 cottage at [130,88], garden [132,88], stool/pots [129,90]. Existing sacks/cart dress the storage yard; four skull pikes near Maud are removed. Maud's original 188×155 frame, origin and anchor are preserved. Hanging herbs/charms are geometry attached to the cottage.

The quest sack [40,70], story markers [42,70]/[132,93], reed-bank shortcut [118,86], interactables, terrain, flags, rewards and voiced text are unchanged. All 988 original prop cells were remapped by identity after adding palette swatches; final count is 990. Two headless scene exports are byte-identical and semantically match the approved preview.

Rebuild only these assets safely with `python tools/tactics-assets/build.py lore_places`. Two successive builds preserved all 4,758 live pack files byte-for-byte. The legacy full prop generator is not the source for curated imported extras; see [integration details and current verification](integration/README.md). All pre-existing shipped PNGs and prop definitions are retained unchanged.

The integrated source `d7583d80` passed all 6,092 .NET tests and 504 phone tests, and built with zero warnings/errors. The Python art audit now passes all five checks and accounts for all 647 environment PNGs. Fresh live-pack TV captures show the granary, normal marker fade and Maud's home without TV errors. The final fresh Chapter 1 force run passed: Storming the Ford won, all 94 selected lines said once in order, end card displayed, four walking steps afterward. The gated narrator clip played after Maud; TV reported no errors. OW-34 is done. Nothing remains for issue #81 on this branch; no PR is open.

## Current release continuation

Current main is integrated and verified for the owner-authorized merge. Preserve the newer bounty/cave, roaming mobs and bridge/plug fixes. The claw-mark notice moves to [118,80] because the newer kobold arena covers [121,80]. A native dark medium rock at [117,84] restores the shortcut advantage required by its existing test; the path and its standing/check tile remain unchanged. All other issue #81 positions and the approved PNG bytes are preserved. Both editor prop layers have been remapped and both regions export deterministically. This section supersedes earlier branch-only statements about no PR, unchanged notice coordinates and no main integration. [Merge evidence and current checks](merge/README.md).

Final combined-source verification (`183d16a9`): all 6,376 .NET tests, 517 phone tests and five art checks (651 environment images) pass. Both builds pass. Both regions export twice byte-identically; Cinder Hollow remains byte-identical to main. Fresh native-art TV review is complete. The live force run won, said all 94 lines once, showed its end card and walked four steps afterward with no TV errors. OW-33/OW-34 are done, and the branch is ready for its authorized publication. No further implementation remains.
