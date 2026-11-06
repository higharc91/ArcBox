# Generic townsfolk — approved production installation

The owner approved the finished fourteen-variant animation/portrait/TV batch:
**“looks good, do whatever is next”**. The installer verified all bound review
hashes before writing and installed the exact reviewed assets, with no new art,
voice generation, purchases or upgrades.

All twelve named NPC sets and all fourteen generic variants now have replacement
bodies and matching portraits installed on `townsfolk-art`. The issue #80 cast
has no remaining approval or generation work. No PR, merge or issue closure was
requested or performed.

## Installed content

- Seven generic presets × two variants: barmaid, child, farmer, farmhand, fisher,
  guard and merchant. Fourteen eight-facing atlases, 672 native idle/walk source
  frames, 84 40×40 mood portraits.
- The same 28 unnamed people select A/B variants; locations, walking routes,
  barks, interactions and presence gates are unchanged. Named Pip and the other
  named NPCs retain their approved art.
- 98 new art files and three cosmetic documents. Every retained baseline asset
  is present, all approved hashes match, and no other production content changed.
  Total: **5,151 files / 220,219,831 bytes**, versus 217,274,314 bytes before.
- The painting scene now retains all 31 currently placed suffixes: 28 generic
  variants plus the previously installed named Pip, Mother Skarrow and Old Maud
  variants. Exact scene-to-region export and native round-trip checks pass.

`tools/tactics-assets/imagegen/townsfolk/generic/verify-installed.cjs` verifies
current production against the approved hashes and the pre-install baseline.
The original `verify.cjs` and `verification.json` remain frozen pre-install
isolation evidence; they intentionally do not describe an installed pack.
The typed runtime checker passes for every direction/frame rectangle, scale,
alias, portrait and placement, with all named bodies preserved.

## Production memory

Fresh installed-pack four-party software OpenGL runs pass open, crossing,
party-change and party-restored stages, with eight actual human/elf hero uploads
and releases verified by `tools/tactics-playtest/world-memory.cjs`.

| Region | Open MiB | Peak MiB | Limit MiB |
| --- | ---: | ---: | ---: |
| Ashen Ford | 423.33 | 423.56 | 512 |
| Cinder Hollow | 208.41 | 208.41 | 512 |

Original Ashen Ford peaked at 409.28 MiB using the same renderer; original Cinder
Hollow peaked at 208.41 MiB. The increase in Ashen Ford is 14.28 MiB. Software
rendering establishes texture memory here, not GPU frame-time performance.
Exact logs and four-stage receipts are under
`tools/tactics-assets/imagegen/townsfolk/generic/native/installed-ashen-ford/`
and `installed-cinder-hollow/`.

## Regression checks

**6,338 .NET tests pass across the complete solution run and full affected-project
reruns.** The first complete solution run passed seven projects and found eight
failures in the three affected projects. Final complete reruns pass all 676 Game,
2,539 Presentation and 398 WorldTools cases; their retained logs are in this
report directory. The seven unaffected projects' complete runs passed in the
initial solution run. The test-case total is unchanged.

The eight failures were resolved as follows:

- Updated the farmer portrait expectation and atlas checks for the new generic
  idle/walk counts. All frame-bound checks remain in place.
- Updated older named-art tests for portrait aliases, eight real unmirrored
  facings and scaled head height. The all-speaker coverage check still requires
  each line's authored mood rather than idle fallback.
- Synced the scene's 31 variant fields with the installed exported region; the
  entire WorldTools suite now verifies exact byte-identical scene export.
- Regenerated five existing story fixture deadline timestamps from the current
  game harness; no other fixture fields changed.
- Expanded the default story-model run from 32 to 64 seeds. The old range reached
  tie-breaks but missed a restart during that rare state with the current voiced
  timings. The expanded run covers it without relaxing any invariant or coverage
  assertion; both the isolated model and full Game suite pass.

**508/508 phone tests pass** with one worker. The initial parallel run timed out
in the 20,000-event thumb-model test under concurrent regression/rendering load;
no phone implementation or timeout was changed. After refreshing the five fixture
timestamps, all 80 affected story/tactics phone tests also pass.

The TV host build has **0 warnings / 0 errors**. Production hash/runtime checks,
editor variant save/load, painted JSON round-trip and exact region export pass.
Approval and source/motion/portrait/TV hashes remain retained in the generic
source directory. Earlier pending approval notes are superseded by this report.
