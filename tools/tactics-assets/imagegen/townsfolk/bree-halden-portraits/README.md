# Bree and Halden — matching dialogue portraits

Created and installed on `townsfolk-art` in response to the owner's request:
“Fix Bree and Halden's portraits”. Their approved concepts and installed sprite
references were reused. This records implementation authorization, not a separate
owner visual approval of these new portraits.

Each character has six 40×40 moods: idle, hurt, happy, angry, sad and surprised.
The innkeeper/Bree and mayor/Halden manifest aliases now select the matching
portraits. The old portrait files remain available. Bodies, animation, voices,
lore and gameplay are unchanged.

`bree-source.png` and `halden-source.png` retain the two original image-tool
outputs. Provenance files record references, source hashes and request summaries.
Normalization follows the existing townsfolk portrait workflow: one fixed scale
and offset per character across all six moods, nearest-neighbour sampling,
64 shared colours and binary alpha. Generated source RGB outside the alpha mask
is discarded. No mood is independently recentered.

Reproduce from the repository root:

```sh
npm ci --prefix tools/tactics-assets/imagegen/townsfolk
node tools/tactics-assets/imagegen/townsfolk/bree-halden-portraits/build.cjs bree
node tools/tactics-assets/imagegen/townsfolk/bree-halden-portraits/build.cjs halden
node tools/tactics-assets/imagegen/townsfolk/bree-halden-portraits/install.cjs
```

The installer validates both complete sets before writing: hashes, dimensions,
binary alpha, occupied pixels and clear outer borders. Review contacts are
`bree-portraits-review.png` and `halden-portraits-review.png`; row order matches the
six moods above. Each production set uses 38,400 bytes of uncompressed texture
storage, the same dimensions as the legacy set. No PixelLab or Scenario jobs,
credit purchases or upgrades were made.

Validation: 81 HUD art tests and 14 host view contract tests pass. The TV host
build succeeds with zero warnings/errors. A native software OpenGL story demo
shows the new Halden portrait in the actual dialogue frame (exit 0).
The complete presentation suite reports 2,536 passed / 3 failed. All three
failures reproduce with the original portrait manifest: the all-speakers test
expects Dagna's old filename, a cast-facing assertion expects the legacy row,
and a story-marker assertion expects the old figure geometry. These pre-existing
checks were left outside this portrait fix. The native run logs an existing
ObjectDB cleanup warning; it is not a GPU performance measurement.
