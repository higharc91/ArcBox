# Issue #80 — Lark approved and installed

The owner approved the finished motion/portrait/native TV review:
**“Looks good, next”**. The exact existing body and six portraits are installed;
no Lark concept, sprite frame or portrait was regenerated. Approval metadata
retains all15 bound review hashes and the native TV screenshot hash.

`install-approved.cjs` verifies the approved data before copying the body,
portraits and two manifests. Production keys `characters.minstrel` /
`characters.lark`, portrait presets `minstrel` / `lark` cover all six moods.
Atlas1728×768 /5.0625 MiB RGBA, frame96×96, anchor[48,84], scale0.5,
eight real directions,10/6/10 idle/walk/talk playback. The deliberate held southwest
talk peak remains unchanged. No civilian attack clip; legacy files retained.
Atlas SHA256: `c532af63e88448bba8b0433b515ab0a95036b4915cb48a196c580fb1ebc6ff3b`.
Rules, balance and phone contracts are unchanged.

| Production with five approved NPC sets, four actual heroes | Open MiB | Roster peak MiB | Cap MiB |
|---|---:|---:|---:|
| Ashen Ford | 279.59 | 317.26 | 512 |
| Cinder Hollow | 154.87 | 192.37 | 512 |

Both strict native gates pass open, crossing, all-four race replacement and
restoration; actual uploads/releases are verified. Ashen peak332,670,208 bytes
includes its crossing allocation; Cinder201,718,016. [Ashen](ashen-memory.json),
[Cinder](hollow-memory.json), neighbouring native logs. Godot4.7.2 .NET, Linux Mesa
software Vulkan/Mobile, low1280×720, dummy audio, `LP_NUM_THREADS=2`.
These are texture-memory measurements, not GPU frame-time benchmarks.

[Cold pack](cold-pack.json): **203,477,572 bytes, 4,835 files**, +634,319 bytes
from the Tobin-installed pack. Inventory SHA256:
`ddc6d04e5211f70321235e691f88d540d43b7044fc37d4a69ac99a13702d0d1f`.
Only Lark's body, six portraits and their two manifests change in production.

**125 focused tests pass**, no failures/skips:33 WorldPeople,5 CreatureAtlas,
54 UnitArt,33 HudArt. The new stop/talk/resume row covers Lark, and six mood cases
verify both portrait aliases. Existing named civilian counts remain strict;
Lark's playback is explicitly10/6/10. Debug/Release test-project builds have
zero warnings/errors. The typed production importer/bark/portrait verifier passes:
[receipt](runtime-validation.json), aggregate [validation](validation.json).
Logs are beside this report; copied text trims trailing whitespace only.
No broad solution/web rerun is claimed for this asset-only addition; previous
full-suite evidence remains in earlier loading/integration reports.

Zero additional Lark image generations or PixelLab calls/credits/purchases for
installation. Corvin Ashby's first appearance pass is separate and pending owner
approval; no Corvin game assets are installed. Keep existing art gates and512 MiB
with four members. Pull with rebase before pushing townsfolk-art; no PR, merge
or issue closure.
