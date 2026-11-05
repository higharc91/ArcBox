# Issue #80 — Aubrine approved and installed; Tobin appearance review

The owner approved Aubrine's complete motion/portrait/native TV review in chat:
**“She looks great! Who is next?”**. Her existing 144-frame atlas and six portraits
are now installed without regeneration. The exact approval/preview hashes remain
in `tools/tactics-assets/imagegen/townsfolk/mother-aubrine/sprite-approval.json`.
Earlier pending-approval reports are historical and superseded by this approval.

`install-approved.cjs` verifies the approved images, metadata, review hashes and
TV still before copying. Production selects `characters.priest` and
`characters.mother_aubrine`; both portrait aliases cover all six moods. Atlas
1728×768, 96×96 frames, anchor[48,84], scale0.5, eight real directions,
10/6/10 idle/walk/talk playback. SHA256:
`a9f5fe5ae077f432194f189c5f5c08bf532b58b3a17c978eabf461835d8dc85f`.
Legacy files remain. Rules, balance and phone contracts are unchanged.

| Production, four actual heroes | Open MiB | Roster peak MiB | Cap MiB |
|---|---:|---:|---:|
| Ashen Ford, Bree + Halden + Aubrine | 273.15 | 310.83 | 512 |
| Cinder Hollow | 154.87 | 192.37 | 512 |

Strict gates pass open, crossing, all-four race replacement and restoration;
actual texture uploads/releases are verified. Ashen peak is 325,924,096 bytes,
including the crossing allocation. See [Ashen](ashen-memory.json) and
[Cinder](hollow-memory.json) receipts and neighbouring native logs.
Godot4.7.2 .NET, Linux Mesa software Vulkan/Mobile, low1280×720, dummy audio,
`LP_NUM_THREADS=2`. These are memory measurements, not GPU timing benchmarks.

[Cold pack](cold-pack.json): **202,137,516 bytes, 4,821 files**, +620,068 bytes
from Bree/Halden. Inventory SHA256:
`5caa9ac9075f37f45f3087c8ba2f681618cd6c85ce021300b545851bc7bd6514`.
Only the body atlas, six portraits and their two manifests change in content.

**111 focused tests pass**, no failures/skips:31 WorldPeople,5 CreatureAtlas,
54 UnitArt,21 HudArt. Each class was run separately with the real production pack.
The new case verifies Aubrine's held talk/contact/resumed route; six mood cases
verify both portrait aliases. Exact per-character playback expectations preserve
Bree/Halden checks. Debug/Release test-project builds have zero warnings/errors.
The read-only C# verifier passes production import/bounds/aliases, six portraits
and bark stop/resume. Offline validation passes all144 frames,96 colours and
<=1world-pixel foot drift. Logs and receipts are beside this report; copied text
logs trim trailing whitespace only. No broad solution/web rerun is claimed for
this asset addition; prior full-suite evidence remains in earlier reports.

Reproduce the new typed production check after SDK activation:

```sh
dotnet run tools/tactics-assets/imagegen/townsfolk/mother-aubrine/verify-runtime.cs -- content/tactics/base --production
```

The owner selected **Tobin Fell** next and authorized making his pixel appearance.
One image-tool standing eight-view candidate reuses his already approved concept,
under `tools/tactics-assets/imagegen/townsfolk/tobin-fell/`, with exact request and
input/output hashes. **Appearance approval remains pending**: no Tobin animation,
portrait or production installation yet. Native-grid/alpha/palette/anchor/height
validation and complete-set/TV approval remain ahead. Do not regenerate approved
concepts or completed Bree/Halden/Aubrine art.

Zero PixelLab calls/credits/purchases. The historical0.75 balance is not a fresh
quote; check live costs/balance before any future paid work. Other23 sprites and
remaining portraits are ahead, including Tobin. Pull with rebase before pushing
`townsfolk-art`; no merge, PR or issue closure.
