# Townsfolk integration with current main

Owner merge authorization: **“ok, merge this branch”**. Integrated main
`00490381` (Ashen Ford secrets, roaming mobs and TV drawing polish) into the
approved townsfolk branch at `8656e414`, resolving all conflicts before release.

The resulting content retains all twelve named NPCs and fourteen generic
variants, matching portraits and approved voice recasts. No new art or voice was
generated during integration.

## Conflict resolution

- Keep main's off-thread node construction, cancellation cleanup, depth-sorted
  `WorldHerd` and ambient rendering. Resolve actual person variants and herd
  texture paths during the off-thread plan, then upload them through the existing
  budgeted unit queue. Party residency, roster release and the 512 MiB gate remain.
  The superseded `WorldFauna` node remains deleted as on main.
- Preserve the roaming-mob demo entry point and the separate Cinder Hollow memory
  gate. Keep both painting-workspace commands: variant round-trip validation and
  authored roaming-arena edits.
- Retain both voice-recast and Ashen Ford secrets receipt collections and include
  both in voice checks. Keep both documentation histories and the current board.
- Preserve main's complete Ashen Ford gameplay/layout; its only region difference
  is the approved cosmetic person variants. All 4,902 binary art/audio files from
  the appropriate parent match exactly, all 256 approved review hashes match, and
  all 98 generic production art files match their approved source hashes.

## Validation

**A complete solution run passes all 6,466 .NET tests with no failures.** All
**517 phone tests** pass with one worker. The TV host builds with **0 warnings /
0 errors**. Native editor variant save/load and painted export checks pass.
The installed-art runtime check passes all aliases, eight directions, 672 source
frames, 84 portraits, 28 generic placements and preservation of named bodies.
All **1,455 voice clips** are present, with zero renders to make.

Fresh four-party software OpenGL memory runs pass every required stage (open,
crossing, race change and restoration) and verify real human/elf uploads/releases:

| Region | Peak MiB | Limit MiB |
| --- | ---: | ---: |
| Ashen Ford | 424.66 | 512 |
| Cinder Hollow | 210.18 | 512 |

The native roaming demo also renders moving kobolds, wolves, slimes, road bandits
and boars with the combined renderer and reports no Godot errors. The captured
120-second run is a sample of the demo; it does not claim to finish every demo
placement. The complete game/mob behavior is covered by the full regression suite.
Software rendering establishes memory and functional rendering, not GPU timing.

`checks.json` records the current deployable content file count/bytes. Test, build,
voice and integrity receipts are retained beside this report. Memory logs and
native captures are under `tools/tactics-assets/imagegen/townsfolk/generic/native/`
in `merged-ashen-ford`, `merged-cinder-hollow` and `merged-mobs`.

For this integrated checkpoint use
`python tools/tactics-assets/imagegen/townsfolk/generic/verify-integration.py`.
The older `verify-installed.cjs` deliberately checks the earlier pre-main
installation baseline and is historical evidence, not the integration checker.
