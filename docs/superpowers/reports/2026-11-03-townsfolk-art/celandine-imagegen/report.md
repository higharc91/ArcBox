# Lady Celandine Marrowby — finished art and recorded-voice review

The owner approved appearance and voice: “Perfect, continue”. Eight real facings
with idle, walk and talk loops, six40×40 dialogue portraits and a19.6-second voiced
review are complete. The review ends with a labelled native TV still and reuses
her existing town bark. No new speech or lip synchronization is claimed.

**Finished-set/TV approval is pending before production installation.** The
approved concept and appearance were reused. Twelve completion image-tool calls:
one northwest guide occlusion correction, eight motion sheets, one portrait sheet
and two rear-facing corrections. Two prior appearance calls make fourteen total.
No PixelLab calls/spend/purchases; no monetary image-tool price inferred. Every
request, raw source/correction, archived input and input/output hash is retained.

The initial north/northeast motion exports faced too far toward the camera; the
corrected sources show true back/back-right silhouettes without front face/chest
features. The northwest guide removes the keyring from the incorrect near hip.
Final northeast talk slot5 holds the valid preceding return pose rather than the
source's drifting last pose. Do not restore superseded facings or recenter frames.

## Native assets and validation

Atlas1728×768 /5.0625 MiB,96×96 frames, anchor[48,84], headTop12, scale0.5,
footprint16×36. Adult body72 native /36 world pixels.144 slots from143 selected
source cells, one held slot. Idle/talk10 playback entries; walk6;20/3 fps.
Shared96-colour sprite palette,64-colour portraits, binary alpha, one fixed
transform per direction and no mechanical mirroring/per-frame recentering.

- All144 frame bounds, alpha, palette, body height, distinct-motion and foot
  checks pass; max vertical foot drift1 world pixel.
- Typed review importer, both body/portrait aliases (noble/celandine_marrowby),
  all six moods and bark stop/hold/resume pass.
- 147 focused WorldPeople/CreatureAtlas/UnitArt/HudArt tests pass,0 failures/skips.
- TV host Debug build0 warnings/errors.
- Four actual heroes pass open/crossing/all-four race replacement/restoration
  and texture uploads/releases in both regions. Peaks **328.99 MiB Ashen /
  192.37 MiB Cinder**, below512 MiB.
- Godot4.7.2 .NET, Linux Mesa llvmpipe software Vulkan/Mobile, low1280×720, dummy
  audio, LP_NUM_THREADS=2. These are memory measurements, not GPU timing claims.
- Exact offline rebuild preserves19 bound review hashes plus native TV shot.
  Receipts verify all14 source/request/input sets.
- Pending installer rejects before production writes; production inventory stays
  **207,649,084 bytes /4,922 files**, SHA256
  6bf5e5410895a9f5d7355d97b721d1c9ab83d19fbebf7f6eb7f6d8ea0edd948f (method in production-inventory.json).

No production art, game rules, lore/captions, balance or phone contracts changed.
No broad solution/web test rerun is claimed for an uninstalled art review.

## Reproduce and next gate

```sh
npm ci --prefix tools/tactics-assets/imagegen/townsfolk
node tools/tactics-assets/imagegen/townsfolk/celandine-marrowby/prepare-guides.cjs
npm run build-celandine --prefix tools/tactics-assets/imagegen/townsfolk
npm run verify-celandine --prefix tools/tactics-assets/imagegen/townsfolk
node tools/tactics-assets/imagegen/townsfolk/celandine-marrowby/build-review-pack.cjs /external/review-pack
source /workspace/.arcbox/activate.sh
dotnet run tools/tactics-assets/imagegen/townsfolk/celandine-marrowby/verify-runtime.cs -- /external/review-pack
```

Use the handoff's ARCBOX_TACTICS_ART_ROOT/world-demo commands with the review
overlay. Native screenshot uses41,68 with four-party stand43,69. Never edit
linked review files. Native logs/screenshots and strict memory verifier JSON are
committed here. Owner full-set/TV approval precedes exact installation and
production checks. Rebase before pushing townsfolk-art; no PR, merge or issue closure.
