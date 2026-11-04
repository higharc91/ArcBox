# Issue #80 — Lark finished review; Tobin installed

The owner approved Lark's appearance: **“Yes, looks great. Continue”**.
Lark now has eight real directional idle/walk/talk clips, six native40×40 portrait
moods and a native TV square preview. The existing approved concept and pixel
appearance were reused; no completed concept or other character art was regenerated.

**Finished-set/portrait/TV approval remains pending.** Lark is in a separate review
overlay. The independently approved Tobin body and portraits are now installed;
see [his production report](../tobin-installed/report.md). Rules, balance and phone
contracts are unchanged. No merge, PR or issue closure.

## Review material

- [16.6-second finished reel](../../../../../tools/tactics-assets/imagegen/townsfolk/lark/lark-finished-review.mp4):
  1024×640 H.264, silent, actual sprite loops and portraits, ending with a labelled
  native TV still. The closing screenshot is not presented as live video footage.
- [Phone-friendly close-up](../../../../../tools/tactics-assets/imagegen/townsfolk/lark/lark-closeup-review.gif):
  actual southeast atlas frames at4× nearest-neighbour, idle/walk/talk,4.8-second loop.
- [Native TV](world-songkeeper.png), [2× square crop](square-detail.png):
  Lark at the authored29,58; four actual heroes at32,61. Linux software render,
  not a Windows capture. All directional GIFs/contact sheets and portrait moods
  are linked in the art README.

Atlas **1728×768 /5.0625 MiB RGBA**, frame96×96, anchor[48,84], scale0.5,
adult body target72 native /36 world pixels. There are **144 native slots** from
143 selected source cells; the southwest talk deliberately holds one valid peak
pose. Each direction has six slots per action, with10/6/10 idle/walk/talk playback.
No attack clip or mirrored direction. Do not use adult human height for children
or goblins. Body keys `characters.minstrel` / `characters.lark`; portrait presets
`minstrel` / `lark` cover all six moods.

## Curation and verification

The source layout has outer margins. One regular column origin/pitch is fitted
from six planted idle-foot centres per direction, then one scale/offset is used
across all18 poses. No per-frame recentering. A locked96-colour palette with exact
RGB-keyed nearest lookup prevents unrelated colours changing during corrections;
GIF previews reuse that palette. Runtime alpha is binary. Portraits use a shared
64-colour palette and one common scale/offset, without per-mood recentering.

The northwest image incorrectly placed a scroll tube on the near left hip. An
image-tool correction restores the paper satchel throughout that facing. A second
image edit repairs the south walk, which initially kept one boot planted through
the cycle. Only its middle six-frame row is imported; south idle/talk are exact.
The new boot-mask regression rejects the initial frozen-boot clip and passes the
corrected one (vertical mask ranges6/2 native pixels). All119 unrelated native
frames remain pixel-identical: `directional/preserved-frames.json`.

The raw southwest talk's last pose failed the horizontal-foot gate at5.25 world
pixels. Curation holds the preceding valid peak, then returns through the authored
poses. This uses five distinct authored poses in six slots, rather than recentering
the rejected cell or weakening a limit. Raw images and selection notes are retained.
Instrument geometry follows the rear approved concept: right-shoulder neck and
left lower-back pear body. It stays strapped to the back; hands remain empty.

All144 slots pass bounds, adult height, anchors, binary alpha, palette, unique
motion and horizontal/vertical foot checks; all six portraits are distinct.
Maximum vertical foot drift **1.5 world pixels**, horizontal **2 world pixels**.
Existing limits remain2 pixels for planted poses,4 vertical/6 horizontal for walks.
The typed game importer checks aliases/playback/bounds, six portrait moods and
held talk/contact/resumed route: [receipt](runtime-validation.json). The read-only
terrain harness keeps its0.01-art-pixel float tolerance and2-pixel resume limit;
no game logic was changed for these checks.

An offline build/verify rerun leaves all15 bound review hashes unchanged:
[receipt](rebuild-validation.json), [log](rebuild.txt). The pending installer guard
refuses installation and preserves every production byte:
[guard/cold-pack receipt](production-guard.json). No broad solution/web rerun is
claimed for a review-only art addition; Tobin's separate production change has
118 fresh focused tests and zero-warning Debug/Release test-project builds.

## Four-party memory and spend

| Review overlay on approved production art | Open MiB | Roster peak MiB | Cap MiB |
|---|---:|---:|---:|
| Ashen Ford with Lark | 279.59 | 317.09 | 512 |
| Cinder Hollow | 154.87 | 192.37 | 512 |

Strict gates cover open, crossing, all-four race replacement and restoration,
with actual texture uploads/releases verified. Final Ashen peak332,488,960 bytes;
Cinder201,718,016. [Ashen](ashen-memory.json), [Cinder](hollow-memory.json), native
logs beside them. The final Ashen capture uses the corrected final atlas. Cinder
loads no Lark texture. Godot4.7.2 .NET, Linux Mesa software Vulkan/Mobile, low1280×720,
dummy audio, `LP_NUM_THREADS=2`. These are texture-memory measurements, not hardware
frame-time benchmarks; software frame timings and compilation contention are retained.

Production after Tobin remains **202,843,253 bytes /4,828 files**, inventory SHA256
`2e6da18bde5ec8564430f04b4992bacdc49c5bd82d57d603d6ac618a7871d5f4`.
Lark adds no production files before the finished-set approval gate.

**11 completion image-tool calls**, plus the earlier appearance call. All requests,
raw sources, input/output hashes and original paths are committed under
`tools/tactics-assets/imagegen/townsfolk/lark/`. **Zero PixelLab calls/credits or
purchases.** No image-tool monetary price was quoted or inferred. The historical
PixelLab balance0.75 is not a current quote; check live costs/balance before future
paid work. Never purchase credits automatically.

Next: owner finished-set/portrait/TV review, then exact installation with strict
Lark action-count/portrait/stop-resume cases and fresh production memory/cold-pack
checks. Other21 sprite sets and remaining portraits are ahead. Rebase before
pushing townsfolk-art; no PR, merge or issue closure.
