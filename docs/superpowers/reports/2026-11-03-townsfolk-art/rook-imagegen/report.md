# Rook — completed directional art, portraits and voiced TV review

Historical review checkpoint: the owner subsequently approved the full set and
exact assets are now installed. Current production results: ../rook-installed/report.md.

Owner appearance/voice approval: “sounds and looks good”. The approved concept
and appearance were reused to finish eight real idle/walk/talk facings and six
40×40 dialogue portraits. The complete set is ready for separate finished-set/TV
approval before installation. Nothing was installed into production.

Review assets: tools/tactics-assets/imagegen/townsfolk/rook/.
Watch rook-finished-review.mp4; it contains actual sprite loops, portrait expressions,
the existing Umbriel town recording during talk, and a labelled native dock TV
still with four heroes.19.6 seconds; video/audio decoding passes. It is a voice
preview, not lip synchronization or a recording of live TV motion.

## Asset and provenance checks

Atlas1728×768 /5.0625MiB,96×96 frames, anchor[48,84], scale0.5, adult72/36 native/
world body height.144 selected source cells and frame slots, no held replacements.
Idle/talk playback10 entries, walk6,20/3fps. Shared96-colour sprite palette, binary
alpha, one fixed grid transform per facing, no mirroring or per-frame recentering.
Six64-colour portraits are40×40. All bounds, alpha, palette, adult size, distinct
motion and horizontal/vertical foot checks pass; maximum vertical drift1 world pixel.
Eight actual rear/front/profile facings preserve the coat, low ponytail and ledger
strap/hip arrangement. No Rook gender/species/age was added to canonical lore.

Nine completion image-tool calls plus three prior appearance calls = twelve total.
Exact requests/archived inputs/raw outputs/hashes retained. Opaque magenta exports
are colour-keyed deterministically, preserving non-key RGB, before native import;
this avoids the prior appearance alpha defect. Earlier appearance outputs remain.
No previously completed concepts or other characters were regenerated. Zero
PixelLab calls/spend/purchases and zero new speech. No image-tool price inferred.

## Runtime and loading verification

- Typed importer, bandit_captain/rook body and portrait aliases, all moods and
  atlas source rectangles pass. Original Rook quest visibility before contact and
  after completion is preserved and verified. Bark stop/hold/resume passes.
- 154 focused WorldPeople/CreatureAtlas/UnitArt/HudArt tests pass, no failures/skips.
  Host Debug and presentation-test builds have zero warnings/errors.
- Four actual heroes pass open/crossing/all-four race replacement/restoration
  and real texture uploads/releases in both regions. Strict512MiB budget passes:
  **332.20MiB Ashen Ford /192.37MiB Cinder Hollow** peak.
- Godot4.7.2 .NET, Linux Mesa llvmpipe software Vulkan/Mobile, low1280×720, dummy
  audio, LP_NUM_THREADS=2. Memory measurements, not GPU timing claims.
- Offline atlas/portrait/GIF rebuild preserves all19 bound review asset hashes
  plus native TV still. All12 generation source/request/input sets verify.
- Pending installer rejects before production writes. Production inventory remains
  **210288327bytes /4953files**, SHA256
  87870feadf6bf4355fc9187ff04997e7fa505215927bc29f938ef0a5424f96c4.

For the native shot only, prepare-native-review.cjs creates a private copy of the
region file and a preview clone at Rook’s actual104,104 dock. The original gated
Rook remains untouched; the clone permits a serverless art shot without quest
progress. Other rules remain linked/read-only. This fixture is external to
production and never passed to the installer. Native TV view/party stand102,104.
The dock screenshot is under ashen_ford-shots/world-rook.png. Clean native/build/
test/runtime/rebuild logs and strict memory results accompany this report.

## Reproduction and approval gate

Use the Rook README’s offline build/verify commands. Build an explicit external
review pack, then prepare its optional native presence fixture. Run verify-runtime.cs
against the review pack with the configured .NET SDK. Follow docs/tv-host.md for
Godot/software rendering; activate the SDK environment before launching Godot.
Never edit linked review files. No full-solution/web/phone test rerun is claimed
for this uninstalled art set.

Owner finished-set/TV approval is still pending before exact asset installation
and production checks. Maud’s appearance approval remains outstanding. Board and
handoff updated. Rebase before pushing townsfolk-art; no PR, merge, issue closure
or environment publication. Keep rules, balance and phone contracts unchanged.
