# Issue #80 — Tobin Fell completed review

The owner approved the first pixel appearance in chat:
**“yeah that looks great. Proceed with finishing him”**.
Tobin now has eight real directional idle/walk/talk animations (144 native source
frames), six native 40×40 portrait moods, and a native forge TV preview. All art,
requests, receipts, source frames and offline import/validation tools are retained
under `tools/tactics-assets/imagegen/townsfolk/tobin-fell/`.

**Full-set/portrait/TV approval remains pending.** This is a separate review overlay;
production still uses approved Bree/Halden/Aubrine and the old blacksmith. No rule,
balance, placed-person or phone-contract changes. No completed concept or other
character's art was regenerated.

## Review

- [Finished reel](../../../../../tools/tactics-assets/imagegen/townsfolk/tobin-fell/tobin-finished-review.mp4):
  16.6 seconds, 1024×640 H.264, silent. Actual idle/walk/talk loops and portraits,
  ending in a labelled native TV still; no claim of live video in that closing shot.
- [Phone-friendly close-up](../../../../../tools/tactics-assets/imagegen/townsfolk/tobin-fell/tobin-closeup-review.gif):
  exact southeast atlas frames, 4× nearest-neighbour, idle/walk/talk, 4.8-second loop.
- [Native TV](world-forge.png), [2× native forge crop](forge-detail.png).
  Four actual heroes at39,58; Tobin remains at his authored36,55 outside the forge.
- Eight-direction GIFs, contact sheets and portrait sheet are linked in the art README.

Atlas **1728×768 /5.0625 MiB**, frames96×96, anchor[48,84], scale0.5. Bodies
target72 native /36 world pixels, matching adult heroes; do not reuse this size for
children/goblins. Each direction supplies six idle, six walk and six talk poses.
Idle/talk play ten indices with a return across existing poses; walk plays six.
Keys: `characters.blacksmith` and `characters.tobin_fell`; portrait aliases cover
all six moods for both blacksmith and tobin_fell. No attack clip or mirrored facing.

The source sheets contain regular columns inside outer margins. Import fits one
column origin/pitch from all six idle-foot centres per direction, then shares one
scale/offset across all18 poses. It never recentres individual frames. A locked
96-colour palette and binary-128 alpha make runtime output consistent; coloured
low-alpha RGB artifacts in the raw northeast background do not enter the atlas.
Portraits use a common scale/offset and64-colour palette, without per-mood shifts.

The initial front walk repeated one lead leg. A single image-tool correction
supplied opposite-leg contacts; curation order is retained. gifenc's RGB-bucket
palette cache initially changed unrelated colour ties when that row changed.
The importer now reuses the preceding native atlas for all **138 unaffected
frames**, with an exact byte comparison; only the six south walk frames change.
Raw originals and the preceding atlas remain committed; nothing was silently
regenerated. Empty hands, anatomical right-hip hammer /left-hip pouch and tongs,
grey ponytail/beard and crossed back apron straps remain recognizable.

## Validation and memory

Offline checks pass all **144 frames**, eight distinct facings, six distinct
portraits, binary alpha, source bounds, unique motion, adult height and anchors.
Maximum measured vertical and horizontal foot drift: **1.5 world pixels**.
Horizontal limits are2 pixels for planted idle/talk and6 for a stride; existing
vertical limits remain2 for planted poses and4 for walking.

The actual C# importer passes aliases, playback, frame bounds, all six portrait
moods and bark hold/talk/resume: [typed receipt](runtime-validation.json).
The new harness originally compared held floating-point vectors for exact equality
and failed on Tobin's fractional terrain projection. It now checks both hold
transitions within0.01 art pixels; the resume2-pixel limit is unchanged. No game
code was changed to make the harness pass.

Offline rebuild reproduces all12 original bound review hashes; the additional
close-up GIF has its own repeat-build hash check. All13 are bound in the approval
record. [Rebuild receipt](rebuild-validation.json), [log](rebuild.txt).
The installer refuses the pending full-set gate, leaving all production bytes
unchanged: [guard/cold-pack receipt](production-guard.json).

| Review overlay, four actual heroes | Open MiB | Roster peak MiB | Cap MiB |
|---|---:|---:|---:|
| Ashen Ford with Tobin | 276.37 | 313.87 | 512 |
| Cinder Hollow | 154.87 | 192.37 | 512 |

Strict gates cover open, crossing, all-four race replacement and restoration;
actual uploads/releases are verified. Ashen peak329,115,904 bytes; Cinder201,718,016.
Receipts: [Ashen](ashen-memory.json), [Cinder](hollow-memory.json), neighbouring logs.
The final Ashen run uses the exact colour-locked atlas shown here. Cinder uses no
Tobin texture, so its used-only result is unchanged. Godot4.7.2 .NET, Linux Mesa
software Vulkan/Mobile, low1280×720, dummy audio, `LP_NUM_THREADS=2`. These are memory
measurements, not a hardware frame-time benchmark or a Windows capture.

Production cold pack stays **202,137,516 bytes /4,821 files**, inventory SHA256
`5caa9ac9075f37f45f3087c8ba2f681618cd6c85ce021300b545851bc7bd6514`.
No broad solution/web rerun is claimed for this review-only asset/tooling addition;
prior full-build/test evidence remains in the earlier integration/loading reports.

Ten completion image-tool calls plus the earlier appearance call; **zero PixelLab
calls/credits/purchases**. Exact requests, input/output hashes and generated paths
are in `generation-receipts.json`. No monetary image-tool price was quoted or inferred.
The last historical PixelLab balance was0.75; obtain a live quote/balance before
any future paid work. Never buy credits automatically.

Next: owner review of the finished set/portraits/TV. After approval, install the
exact verified body/portraits, update strict Tobin action-count/portrait/stop-resume
cases and rerun production memory/cold-pack checks. Other22 sprite sets and
remaining portraits are ahead. Pull with rebase before pushing `townsfolk-art`;
no merge, PR or issue closure.
