# Lady Celandine Marrowby — completed art and voiced review

Appearance and existing voice approved: “Perfect, continue”. Eight real idle/walk/
talk directions and six40×40 portraits are complete and ready for separate
finished-set/TV approval. Nothing is installed in production yet.

[Watch the voiced review](celandine-finished-review.mp4) · [Recorded voice](voice-sample.mp3)
· [Portraits](celandine-portraits-review.png) · [Walk](celandine-eight-direction-walk.gif)
· [Talk](celandine-eight-direction-talk.gif) · [Close-up](celandine-closeup-review.gif)

The19.6-second reel uses the actual sprite loops/portraits, followed by a labelled
native TV still with four party members. Her existing town recording plays during
the talk segment; this is a voice preview, not lip synchronization or live TV footage.
Voice source/text/hashes remain in voice-sample.json. No new speech was generated.

Atlas1728×768 /5.0625 MiB;96×96 frames, anchor[48,84], scale0.5, body72 native /36
world pixels. Shared96-colour sprite palette, binary alpha, fixed transform per
direction, no mirroring or per-frame recentering. Idle/talk playback10 entries,
walk6,20/3 fps.144 slots from143 selected source cells; northeast talk slot5
holds the preceding valid return pose instead of a drifting last cell. Maximum
vertical foot drift1 world pixel. Six64-colour portraits are40×40 pixels.

Northwest guide occlusion is corrected: no book/keys on the near left hip. Use
final rotations and corrected north/northeast motion sheets; the original north
and northeast exports incorrectly faced front and remain only as provenance.
No concept or previously completed character art was regenerated.

Twelve completion image-tool calls plus two prior appearance calls. Zero PixelLab
calls/spend/purchases and zero new speech. Exact requests, archived inputs, raw
outputs, receipts/hashes and normalized frames are retained.

Rebuild offline: npm run build-celandine and npm run verify-celandine with
--prefix tools/tactics-assets/imagegen/townsfolk. Build an isolated overlay with
node tools/tactics-assets/imagegen/townsfolk/celandine-marrowby/build-review-pack.cjs /external/path.
Never edit linked files. The guarded installer requires complete-set/TV owner
approval and verifies the bound review hashes before production writes.

[Validation report](../../../../../docs/superpowers/reports/2026-11-03-townsfolk-art/celandine-imagegen/report.md).
