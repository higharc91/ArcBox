# Issue #80 — Halden approved and installed

The owner reviewed Halden in chat (directional sheet, native TV square, walk/talk
GIFs), then approved: **“OK, he looks good”**. The approval record retains the
exact review/source/TV hashes and authorizes this production integration.

Installed the existing 1440×768 atlas unchanged as `creatures/halden_varrow.png`:
`784a9ffc2ce4504bf23021e45df8409f0b3f5995c2b75a6dc06c42e49fea9081`.
Frames remain 96×96, anchor `[48,84]`, scale 0.5: 4 idle, 6 walk and 5 talk source
frames in every real direction. Talk playback returns across the original frames.
Keys are `characters.mayor` and `characters.halden_varrow`. Existing talk/pause/
resume wiring is reused; legacy sheets remain. No frame regeneration, rescaling,
mirroring or per-frame recentering. **Zero additional generations/credits spent.**
No portrait, rules, balance, placed-person or phone contract changes.

Reproduce offline with
`node tools/tactics-assets/pixellab/townsfolk/halden/install-approved.cjs`.
The installer requires recorded owner approval, verifies source/TV hashes and
authored metadata, and copies only the reviewed atlas plus its manifest entry.
The prior [native TV image](../approved-integration/halden-square.png) and
[animations/source receipts](../../../../../tools/tactics-assets/pixellab/townsfolk/halden/README.md)
show these exact pixels. The earlier review-only status is superseded by this approval.

| Production pack, four heroes | Open MiB | Roster peak MiB | Limit |
|---|---:|---:|---:|
| Ashen Ford, approved Bree + Halden | 269.94 | 307.61 | 512 |
| Cinder Hollow | 154.87 | 192.37 | 512 |

Both strict native gates pass region open, walk, all-four race replacement and
restoration, with actual uploads/releases verified. Godot 4.7.2 .NET, Linux Mesa
software Vulkan/Mobile, low, 1280×720, dummy audio, `LP_NUM_THREADS=2`. These are
texture memory measurements, not hardware frame-time benchmarks. Commands follow
the previous loading/integration report. [Ashen receipt](ashen-memory.json),
[log](ashen-run.txt), [Cinder receipt](hollow-memory.json), [log](hollow-run.txt).

[Cold-pack receipt](cold-pack.json): **201,517,448 bytes (192.18 MiB), 4,814 files**,
up **370,856 bytes** from the Bree-only pack. The ordered path/file-SHA256 inventory
hash is `4768b6dd60fccb5d03b23637dc35c861105edea899f7ec125498007e25e2d674`.
Only the Halden PNG and creatures manifest changed in production content.

Focused checks: **30 WorldPeople**, **5 creature-atlas**, **54 UnitArt** cases pass,
zero failures/skips. The stop/talk/resume test now checks both Bree and Halden;
atlas tests retain every action/facing bounds check and each civilian's authored
idle count. Debug/Release test-project builds have **zero warnings/errors**.
[Validation receipt](validation.json); corresponding logs are beside this report.
Text log copies normalize trailing whitespace only. No broad game/web suite rerun
was needed for this asset-only addition; the preceding full checks and live walk
remain in the earlier report and are not represented as fresh runs.

Other 24 sprites and portraits remain. Last observed PixelLab balance was 0.75;
no new paid API call or purchase was made. Check live costs/balance before future
spending. Keep the branch, rebase before push, and do not merge, open a PR or close #80.
