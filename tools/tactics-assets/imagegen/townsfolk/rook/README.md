# Rook — completed art and voiced review

Appearance and existing voice approved: “sounds and looks good”. Eight real
idle/walk/talk directions and six40×40 dialogue portraits are complete.
The owner approved the full review (“looks good. What's neext”); the exact bound
atlas and six portraits are now installed.

[Watch the voiced review](rook-finished-review.mp4) · [Recorded voice](voice-sample.mp3)
· [Portraits](rook-portraits-review.png) · [Walk](rook-eight-direction-walk.gif)
· [Talk](rook-eight-direction-talk.gif) · [Close-up](rook-closeup-review.gif)

The19.6-second reel shows actual sprite loops and portraits, followed by a labelled
native TV still at the docks with four party members. Existing Umbriel town audio
plays during talk; this is a voice preview, not lip synchronization or live TV
footage. Source/caption/hashes are in voice-sample.json; no new speech generated.

Atlas1728×768 /5.0625MiB;96×96 frames, anchor[48,84], scale0.5, adult72 native /36
world pixels.144 selected source cells/slots, no held replacements. Shared96-colour
sprite palette, binary alpha, one fixed transform per direction, no mirroring or
per-frame recentering. Idle/talk playback10 entries, walk6,20/3fps. Maximum vertical
foot drift1 world pixel. Six64-colour portraits are40×40 pixels. Native bounds,
alpha, palette, scale, unique-motion, foot checks and typed aliases pass.

Rook’s approved concept and first appearance are reused. All three prior
appearance outputs remain, including the two defective transparent exports.
Nine completion image-tool calls plus three prior appearance calls. Opaque
magenta exports avoid the damaged-alpha issue; key-sources.cjs removes only the
background key before normal import. Raw exports/requests/input/output hashes,
keyed sources and normalized native frames are preserved. No PixelLab calls,
credits, purchases or new speech. No image-tool monetary price inferred.

Rook’s real quest-gated presence is preserved and tested. prepare-native-review.cjs
adds a private preview clone at the same dock in an isolated review pack, allowing
the TV art shot without changing production rules or the original Rook flags.
Never edit linked files. The exact approved Rook art is installed. Rules, lore/captions,
balance and phone contracts remain unchanged.

From repo root, after npm ci --prefix tools/tactics-assets/imagegen/townsfolk:

    node tools/tactics-assets/imagegen/townsfolk/rook/prepare-guides.cjs
    npm run build-rook --prefix tools/tactics-assets/imagegen/townsfolk
    npm run verify-rook --prefix tools/tactics-assets/imagegen/townsfolk
    node tools/tactics-assets/imagegen/townsfolk/rook/build-review-pack.cjs /external/review-pack
    node tools/tactics-assets/imagegen/townsfolk/rook/prepare-native-review.cjs /external/review-pack
    dotnet run tools/tactics-assets/imagegen/townsfolk/rook/verify-runtime.cs -- /external/review-pack

To recreate keyed sources, pass retained -export.png files to key-sources.cjs.
The guarded installer rejects appearance approval alone and verifies all bound
finished-review assets/TV hashes before any production write.

154 focused presentation tests pass. Native four-party texture peaks332.20MiB
Ashen Ford /192.37MiB Cinder Hollow are below512MiB, including real roster
replacement/restoration and upload/release checks. Linux llvmpipe is a memory
check, not a GPU timing claim. Production cold pack/hash is unchanged.
See docs/superpowers/reports/2026-11-03-townsfolk-art/rook-imagegen/report.md.

Production installation:161 focused tests pass; Debug/Release builds0warnings/errors;
exact hashes/aliases/quest gates verified. See the rook-installed report.
