# Issue #80 — approved Tobin set installed

The owner approved the finished motion/portrait/native TV review:
**“Looks good. Who is next”**. The existing assets are installed unchanged;
no Tobin concept, animation or portrait was regenerated. His exact approval
hashes remain in `tools/tactics-assets/imagegen/townsfolk/tobin-fell/sprite-approval.json`.

`install-approved.cjs` verifies the approved set/TV hashes before copying the
body, six portraits and two manifest entries. Production uses
`characters.blacksmith` / `characters.tobin_fell`, with all six portrait moods
under both aliases. Legacy files remain. Atlas **1728×768 / 5.0625 MiB**, frame
96×96, anchor[48,84], scale0.5, eight real directions, idle/walk/talk10/6/10.
Atlas SHA256: `d0b6a196f881238c2a5ea9928abe1a68abc5dc083a5c3a7491a3e9265a0bf11e`.
Rules, balance and phone contracts are unchanged.

| Production, four actual heroes | Open MiB | Roster peak MiB | Cap MiB |
|---|---:|---:|---:|
| Ashen Ford with approved Bree/Halden/Aubrine/Tobin | 276.37 | 314.04 | 512 |
| Cinder Hollow | 154.87 | 192.37 | 512 |

Both strict native gates pass open, crossing, all-four race replacement and
restoration, with actual texture uploads/releases verified. Ashen peak is
329,297,152 bytes including its crossing allocation; Cinder201,718,016 bytes.
[Ashen receipt](ashen-memory.json), [Cinder receipt](hollow-memory.json), native
logs beside them. Godot4.7.2 .NET, Linux Mesa software Vulkan/Mobile, low1280×720,
dummy audio, `LP_NUM_THREADS=2`. These are memory measurements, not GPU timings.

[Cold production pack](cold-pack.json): **202,843,253 bytes, 4,828 files**,
up705,737 bytes from Bree/Halden/Aubrine. Ordered path/file-SHA256 inventory hash:
`2e6da18bde5ec8564430f04b4992bacdc49c5bd82d57d603d6ac618a7871d5f4`.
Only Tobin's body, six portraits and their two manifests change in content.

**118 focused tests pass**, zero failures/skips:32 WorldPeople,5 CreatureAtlas,
54 UnitArt,27 HudArt. The added stop/talk/resume row covers Tobin, and six new mood
cases cover both named/blacksmith portrait aliases. Exact civilian counts and
existing Bree/Halden/Aubrine assertions remain strict. Debug/Release test-project
builds have zero warnings/errors. The read-only typed production importer/bark/
portrait verifier passes: [receipt](runtime-validation.json). Logs and aggregate
[validation receipt](validation.json) are beside this report; text log copies trim
trailing whitespace only. No broad solution/web rerun is claimed for this asset
addition; earlier full-suite evidence remains in the loading/integration reports.

Zero additional art generations or PixelLab calls/credits/purchases for this
integration. Lark's appearance was separately approved and is being completed
in a review overlay; it has its own finished-set/TV gate. Keep the existing art
and512 MiB/four-member limits. Rebase before pushing townsfolk-art; no PR, merge
or issue closure.
