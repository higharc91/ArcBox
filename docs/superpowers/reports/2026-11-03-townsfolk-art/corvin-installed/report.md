# Issue #80 — Corvin approved and installed

The owner approved installation: “OK, finish him”. The exact previously reviewed
atlas and six portraits are installed; no completed art was regenerated. The
installer verifies all bound review and TV hashes before copying. Production keys:
`characters.sage` / `characters.corvin_ashby`, portrait presets `sage` /
`corvin_ashby`. Legacy files remain. Atlas SHA256:
`331b749b46377f66c7065164efb573d46cf5b3bccf53161ba47fa08a6b527818`.

132 focused tests pass, no failures/skips: 34 WorldPeople, 5 CreatureAtlas,
54 UnitArt, 39 HudArt. The new cases verify talk/hold/resume and both portrait
aliases across all moods; strict named-person frame counts remain. Debug and Release
test-project builds have zero warnings/errors. The typed production importer,
portrait and bark verifier passes. Logs and validation receipts are beside this file.
No broad solution/web rerun is claimed for this asset-only change.

Four-party production texture peaks: **320.48 MiB Ashen Ford**, **192.37 MiB
Cinder Hollow**, below the 512 MiB cap. Strict native gates verify open, crossing,
all-four race replacement/restoration, and texture uploads/releases. Ashen peak:
336,043,264 bytes; Cinder: 201,718,016 bytes. Godot 4.7.2 .NET, Linux Mesa software
Vulkan/Mobile, low 1280×720, dummy audio, LP_NUM_THREADS=2. These are texture-memory
measurements, not GPU frame-time benchmarks.

Cold pack: **204,126,234 bytes, 4,842 files**, +648,662 bytes. Inventory SHA256:
`4298fd0489664ab01f7a4e3e23e8f117e5f32dd15bae3273b13b7986409d6ec0`.
Only the body, six portraits and their two manifests change in production.
Rules, balance and phone contracts remain unchanged. Zero new art generations,
PixelLab calls, credits or purchases for this installation. Include existing recorded
voice samples with future character drafts as requested. Pull with rebase before
pushing townsfolk-art; no PR, merge or issue closure.
