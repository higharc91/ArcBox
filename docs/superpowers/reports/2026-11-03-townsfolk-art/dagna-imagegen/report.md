# Dagna Holt — finished art and voiced review

The owner authorized completing the existing appearance: “ok, finish those please”.
Eight real idle/walk/talk facings and six40×40 dialogue portraits are complete.
The19.6-second voiced review shows the actual atlas loops, portraits and a labelled
native TV still. It reuses her owner-approved Gacrux town bark; no new speech was
generated. This is a voice preview, not lip synchronization or live TV footage.

**Finished-set/TV approval is pending before art installation.** The original
appearance/concept was reused. Ten completion image-tool calls: eight directional
sheets, six expressions in one sheet, and a straight-back pauldron-side correction.
Together with the two prior appearance calls, twelve image-tool calls are recorded.
No PixelLab calls, credits spent or purchases; no image-tool monetary cost inferred.
Raw images, exact requests and input/output hashes are retained. Do not regenerate
completed art or the approved66-line voice recast.

## Native assets

Atlas1728×768 /5.0625 MiB;96×96 frames, anchor[48,84], scale0.5, headTop12,
footprint16×36. Adult body72 native /36 world pixels.144 slots from144 selected
source cells. Idle/talk playback10 entries, walk6,20/3 fps. Each direction uses
one fixed transform; no per-frame recentering or mechanical mirroring. Binary
alpha, shared96-colour sprite palette and64-colour portrait palette. All144
normalized frames and final native rotations are retained.

Her sword stays sheathed on the left hip, horn/pouch on the right, cloak over the
left shoulder, prominent pauldron on the right. The original north source/guide
put the large plate on image left in back view; all18 north cells were corrected
to image right. Use final rotations and north-corrected-source.png. The old guide
and source remain as provenance, not as motion inputs.

## Validation

- Native bounds, adult height, binary alpha, shared palette and distinct-motion
  checks pass for all144 slots; max vertical foot drift2.5 world pixels (walk),
  horizontal foot drift2.0. No dropped/held source cells.
- Typed importer, all six portrait selections, eight authored direction rows and
  bark stop/hold/resume checks pass against the isolated review pack.
- 140 focused WorldPeople, CreatureAtlas, UnitArt and HudArt tests pass.
- Native TV host Debug build:0 warnings,0 errors.
- Four actual heroes pass open/crossing/race-change/restoration texture gates in
  both regions, including uploads and release of unused human/elf class atlases.
  Peaks **325.77 MiB Ashen /192.37 MiB Cinder**, below512 MiB.
- Linux Godot4.7.2 .NET, Mesa llvmpipe software Vulkan/Mobile, low1280×720, dummy
  audio, LP_NUM_THREADS=2. These are texture measurements, not GPU timing claims.
- Rebuild hashes, source receipts and pending-installer preservation are verified.
  Approval record binds19 review assets plus the native TV shot.

The production inventory remains **207,000,224 bytes /4,915 files**, hash
`544cebc50f4f92407ee35be5e2e7fb12fec42f45dbde3addfb7202fafd5addb8`
(method in production-inventory.json). This includes the prior installed voice
recast. No production content, rules, balance or phone contracts changed in this
art checkpoint. The installer refuses pending approval before any production write.
No broad solution/web test rerun is claimed for uninstalled artwork.

## Reproduce

```sh
npm ci --prefix tools/tactics-assets/imagegen/townsfolk
npm run build-dagna --prefix tools/tactics-assets/imagegen/townsfolk
npm run verify-dagna --prefix tools/tactics-assets/imagegen/townsfolk
node tools/tactics-assets/imagegen/townsfolk/dagna-holt/build-review-pack.cjs /external/review-pack
source /workspace/.arcbox/activate.sh
dotnet run tools/tactics-assets/imagegen/townsfolk/dagna-holt/verify-runtime.cs -- /external/review-pack
```

Never edit linked review-pack files. For native capture, use the handoff's
ARCBOX_TACTICS_ART_ROOT/world-demo commands with this isolated overlay.
The screenshot is at45,64, four-party stand47,65; no production facing/location
was edited. Native logs and strict verifier JSON are committed beside this report.

Review: tools/tactics-assets/imagegen/townsfolk/dagna-holt/dagna-finished-review.mp4.
Next: owner finished-set/TV approval, then install these exact bound assets and
rerun production checks. Rebase before pushing townsfolk-art; no PR, merge or
issue closure.
