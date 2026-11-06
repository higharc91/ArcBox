# Dagna Holt — completed motion and portrait review

The owner authorized finishing the existing appearance (“ok, finish those please”).
Eight real facings,144 native idle/walk/talk cells and six40×40 dialogue portraits
were approved by the owner (“Perfect, do the next character”). The exact reviewed
body and portraits are installed in production, alongside her approved66-line
veteran voice. The retained review pack is available for reproduction.

[Watch the voiced review](dagna-finished-review.mp4) · [Voice sample](voice-sample.mp3)
· [Portraits](dagna-portraits-review.png) · [Walk](dagna-eight-direction-walk.gif)
· [Talk](dagna-eight-direction-talk.gif) · [Close-up](dagna-closeup-review.gif)

The19.6-second reel shows actual atlas loops and portraits, then a labelled native
TV still with four party members. Her approved recorded town bark plays during
the talk segment. This is a voice preview, not lip synchronization or live TV footage.
Voice source/caption/hashes remain in voice-sample.json; no new speech was generated.

Atlas1728×768 /5.0625 MiB;96×96 frames, anchor[48,84], adult body72 native /36 world
pixels at scale0.5. Shared96-colour palette, binary alpha, fixed transform per
direction, no per-frame recentering or mirrored facings. Idle/talk use10 playback
entries from six source cells; walk uses six entries. All directions use20/3 fps.
Maximum vertical foot drift2.5 world pixels during walking.

Dagna keeps her braid/lowbun, worn scale-mail, muted green surcoat, dark cloak,
sheathed left-hip sword, right-side pouch/horn and right pauldron. The original
straight-back guide/source had the large shoulder plate on the wrong side; the
corrected source fixes all18 north-facing cells. Use final rotations/ and the
corrected source, not the superseded north guide. Raw sources and corrections are
retained with exact requests/input/output hashes. Ten completion image-tool calls
plus two prior appearance calls; zero PixelLab calls/spend/purchases.

Rebuild offline: npm run build-dagna and npm run verify-dagna with
--prefix tools/tactics-assets/imagegen/townsfolk. Build an isolated overlay using
node tools/tactics-assets/imagegen/townsfolk/dagna-holt/build-review-pack.cjs /external/path.
Never edit linked files. The guarded installer requires the owner's full-set/TV
approval and exact reviewed hashes before production integration. See the
[delivery report](../../../../../docs/superpowers/reports/2026-11-03-townsfolk-art/dagna-imagegen/report.md).

Production installation passed exact-asset hash checks and typed importer/portrait/bark stop-resume checks. Four-party production texture peaks325.77 /192.37 MiB, below512. See the installed report.
