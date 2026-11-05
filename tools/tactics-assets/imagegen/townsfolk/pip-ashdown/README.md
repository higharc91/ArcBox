# Pip Ashdown — completed review

The owner approved the appearance: “Yep, she looks good”. The owner then approved the finished review (“Looks great, what's next”) and
authorized installation (“OK proceed”). The exact body and portraits are installed;
the review pack remains available. See the installation report.

[Watch the voiced review](pip-finished-review.mp4) · [Voice sample](voice-sample.mp3)
· [Portraits](pip-portraits-review.png) · [Walk](pip-eight-direction-walk.gif)
· [Talk](pip-eight-direction-talk.gif)

The 19.6-second reel uses the actual sprite loops/portraits, followed by a labelled
native TV still. Her existing “Two coppers” recording plays during the talk segment;
this is a voice preview, not lip synchronization. No new speech was generated.
Voice source/text/hash provenance remains in voice-sample.json.

Atlas1152×512 /2.25 MiB, 64×64 frames, anchor[32,56], scale0.5, child body52 native /
26 world pixels. Footprint12×26, headTop4. There are144 slots from143 selected source
cells: southeast talk holds its preceding pose instead of using a drifting cell.
No per-frame recentering or mirroring. Binary alpha/shared64-colour palette;
idle/walk/talk playback10/6/10. The wrong draft southwest angle is corrected in the
motion set. Use rotations/ as final native facings; the older southwest guide is
superseded. Do not switch back to adult72-pixel sizing.

Review uses the existing child_pip variant only for Pip. Generic child art and
portraits stay intact. The review rules copy differs only by Pip’s cosmetic variant;
its other data matches production. Prepared installation sets that same variant
only after full-set approval, keeping child_pip/pip_ashdown aliases separate from child.

Rebuild offline with npm run build-pip and npm run verify-pip using
--prefix tools/tactics-assets/imagegen/townsfolk. Build the overlay with
node tools/tactics-assets/imagegen/townsfolk/pip-ashdown/build-review-pack.cjs /external/path.
Never edit linked overlay files. Typed import/variant isolation/portrait/bark checks
and four-party memory gates pass. All raw requests/images and source-frame outputs
are retained. Nine completion image-tool calls, zero PixelLab spend or purchases.
Rules, balance and phone contracts unchanged in production. See the
[report](../../../../../docs/superpowers/reports/2026-11-03-townsfolk-art/pip-imagegen/report.md).
