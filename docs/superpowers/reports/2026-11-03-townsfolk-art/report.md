> Latest: Halden’s chat approval has arrived and his exact reviewed atlas is now
> installed. See [Halden’s production checks](halden-installed/report.md).

> Continuation on 2026-11-04: Bree’s complete set and TV appearance are now owner-approved
> and installed unchanged. Halden’s complete directional review is ready; 0.75
> PixelLab generations remain. Current integration, memory gates and checks are in
> [the integration report](approved-integration/report.md). The checkpoint below
> preserves the earlier approval and validation history.

# Bree Tallow sprite review checkpoint

Issue #80, branch `townsfolk-art`, 2026-11-03. **Review art is complete for Bree;
production integration and the rest of the town are pending.**

The owner selected candidate 1, briefly tried candidate 2, then returned to 1 and
approved its displayed idle: “nice, I'm good with that. Proceed”. Exact approval
and source hashes are in `tools/tactics-assets/pixellab/townsfolk/sprite-approval.json`.
The new full directional set and TV shots have not received a separate owner
approval. No live NPC sheet, portrait, rules value or phone contract was changed.

## What was made and fixed

- Eight real facings. The approved southeast sprite was imported pixel-for-pixel;
  seven other facings come from the matching Pro Flash study, including a true
  front and genuine rear apron ties. No directions were manufactured by mirroring.
- Idle in all directions: original five-frame southeast clip, four-frame template
  clips elsewhere with the opening frame repeated for the common five-slot layout.
- Six-frame walk in all eight directions.
- Five source talking frames in each direction. The generated clip ended with
  the hand raised, so playback uses `0,1,2,3,4,3,2,1` to return to rest smoothly.
  This reuses genuine frames without a paid reroll or per-frame recentering.
- Rejected the one-generation Pixen front edit: it kept the diagonal pose and
  introduced an unwanted white patch. Its image and receipts remain for audit.
- Reduced transparent padding from the hero-sized 160×160 staging canvas to
  96×96, anchor `[48,84]`, still at 0.5 pixel scale. The approved visible pixels
  remain exact. A compact 1536×768 atlas costs **4.5 MiB RGBA**, versus 12.5 MiB
  for the first padded eight-direction pack. This is a canvas crop, not a body resize.
- Added dev-only `ARCBOX_TACTICS_ART_ROOT` for world-review packs. Live-room packs
  are unaffected, and the old Cinder Hollow review variable remains supported.

## Actual Windows TV check

Godot 4.7.2 .NET, Debug, Vulkan Forward+, RTX 5080, ultra, 1920×1080.
Windows were launched with the established left-monitor position and temporary
`window/size/no_focus=true`; that override was removed afterwards. No server is
running. Both demo processes exited normally.

![Bree outside the Last Coal beside the shipped hero](bree/world-inn.png)

[Square](bree/world-square.png), [market](bree/world-market.png),
[baseline inn](before/world-inn.png), [before log](before-run.txt),
[Bree log](bree-run.txt). All screenshots from both runs are retained.

I inspected the inn, square and market images: Bree stands on the paving without
clipping into the doorway; the new figure reads beside the current hero. The
remaining townsfolk still use old art. The first review placed the hero's HP bar
over Bree; final shots put the stand-in to her side so her full sprite is visible.

| Short demo measurement | Baseline | Compact Bree review |
|---|---:|---:|
| Texture memory after initial route | 190.5 MiB | 195.0 MiB |
| World build total | 801 ms | 490 ms |
| Texture loading | 561 ms | 404 ms |
| Sampled route frames over 16 ms | 0 / 20 | 0 / 23 |

The route is only two tiles and about 0.2 s, with one stand-in. Warm caches and
JIT make these timings unsuitable as a performance comparison. These numbers
**do not verify the four-party 512 MiB gate**, Cinder Hollow, cold download size
or used-only loading. Screenshots also stall rendering; they are not a frame-time
benchmark. The engine labels its binary-megabyte values “MB”; the table uses MiB.

## Validation and remaining work

- Host Debug build: **0 warnings, 0 errors**.
- All **2,450 presentation tests passed**; the earlier focused UnitArt run passed 54.
- Offline pilot verifier: approval hashes, exact crop/import and 0.973 hero-height ratio pass.
- Review atlas verifier: dimensions, all action/facing frames and visible bounds pass;
  idle contact varies by at most one world pixel, talking contact stays fixed.
  Walk contact varies by up to two world pixels during the gait.
- All frame contact sheets inspected; contact sheets and animated GIFs are in
  `tools/tactics-assets/pixellab/townsfolk/`. The compact approved southeast idle
  is verified pixel-for-pixel after its transparent-padding crop.
- Full solution tests, Release build, web checks and reconnect/bot e2e were not
  repeated for this preview-only checkpoint; they remain required before issue completion.

PixelLab: earlier pilot/alternative **19 generations**, this extension **30.25**,
**49.25 total**, **30.75 remaining** at the final account check. The extension is
1 rejected edit + 6 character study + 0.25 import + 8 walk + 7 idle + 8 talk.
Slot-rejected submissions created no jobs. All Bree jobs have settled. Scenario's
separate concept total remains **221 CU**; no concept was regenerated.

Priority next: full-set/TV owner review; remaining 25 people and portraits;
production swap plus talk/bark wiring; optional variants and export round-trip;
OW-05 used-only loading and four-party/cold-download measurements; final solution,
phone and real-walk regression checks. The cloud handoff has exact commands and paths.


## Cloud continuation: loading and memory

The approved Bree source pixels, directional frames, receipts and Windows TV shots were reused unchanged. Offline rebuilding and verification pass; no concept or animation was generated, and no credits were spent. Full-set/TV approval is still requested separately. PixelLab is not exposed by this cloud session's connected tools, so current balance and cost quotes cannot be checked; **30.75 is historical, not a live balance**. No subscription purchase or upgrade was attempted.

OW-05 implementation and four-party measurements are in [the loading report](loading/report.md), with before/after native logs, gate output and an unchanged cold-pack hash. This independent performance work does not approve or install art, complete the remaining 25 sprites/portraits, or close issue #80.

## Latest continuation: Aubrine integration and Tobin appearance review

Aubrine's complete set and TV review are now owner-approved and installed.
Fresh production four-party peaks:310.83MiB Ashen /192.37MiB Cinder, below512.
111 focused tests pass; Debug/Release test-project builds zero warnings/errors.
Tobin's first image-tool appearance sheet awaits owner review before animation.
No PixelLab spend or rule/balance/phone changes. See
[the installation report](aubrine-installed/report.md) for receipts and current next steps.

## Latest continuation: Tobin completed review

Tobin's approved pixel appearance now has eight-direction idle/walk/talk animations
(144 native frames), six portraits and a native forge TV review. Full-set approval
is pending; production remains unchanged. Review memory peaks313.87 /192.37 MiB
with four heroes, below512. Frame/import/bark/portrait/exact-rebuild checks pass.
No PixelLab spend or gameplay/phone changes. [Current report](tobin-imagegen/report.md).

## Latest continuation: Tobin installed, Lark finished review

Tobin's exact approved body/six portraits are installed;118 focused tests pass,
Debug/Release test-project builds have zero warnings/errors. Production peaks
314.04 /192.37 MiB with four heroes, cold202,843,253 bytes. [Installation report](tobin-installed/report.md).

Lark's appearance is approved and the complete eight-direction motion/portrait/TV
review is ready. Finished-set approval remains pending, with guarded installation
and no Lark production changes.144 native slots include one deliberately held
peak; raw sources/curation/receipts are retained, with119 unrelated frames exact.
Frame/foot/gait/portrait/import/bark/rebuild checks pass. Review memory peaks
317.09 /192.37 MiB, below512 with four heroes. No PixelLab spend or gameplay/phone
changes. [Current Lark report and previews](lark-imagegen/report.md).

## Latest continuation: Lark approved/installed, Corvin appearance review

Lark's exact owner-approved body and six portraits are installed.125 focused tests
pass, Debug/Release test-project builds have zero warnings/errors. Four-party
production peaks317.26 /192.37 MiB, below512; cold203,477,572 bytes /4,835 files.
[Installation report](lark-installed/report.md). Corvin's first image-tool pixel
appearance candidate reuses his approved concept and awaits appearance approval.
No Corvin production change, no PixelLab spend, rules/balance/phone unchanged.
[Corvin appearance report](corvin-imagegen-trial.md).

## Latest continuation: Corvin approved and installed

Corvin’s exact approved atlas and six portraits are installed. Four-party production
peaks 320.48 /192.37 MiB, below512. Cold pack 204,126,234 bytes. Rules, balance and
phone contracts are unchanged. See [the installation report](corvin-installed/report.md).

## Current Pip completion checkpoint

Appearance approved: “Yep, she looks good”. The completed eight-direction motion
and six portraits, recorded voice MP3 and voiced review reel are in
`tools/tactics-assets/imagegen/townsfolk/pip-ashdown/`. Full-set/TV approval is pending;
do not request appearance approval again or regenerate completed assets. The compact
child atlas is1152×512 /2.25 MiB, frames64×64, anchor[32,56], body52 native/26 world,
scale0.5. Southwest is corrected; southeast talk holds a prior pose instead of the
drifting last raw cell. Use final rotations/, not the old wrong southwest guide.
Review uses Pip’s named child_pip variant; generic children remain unchanged.
Frame/import/portrait/bark/variant isolation and exact-rebuild checks pass. Review
peaks322.55 /192.37 MiB with four heroes, below512. Pending installer guard preserves
production. All15 review hashes/TV screenshot are bound in the approval record.
Nine completion image-tool calls, zero PixelLab spend/new voice generation. Continue
with finished-set approval then exact installation and production checks; other19
sprite sets and remaining portraits are ahead. Include existing recorded voice
samples in future reviews. Rebase before push, no PR/merge/issue closure.
Evidence: `docs/superpowers/reports/2026-11-03-townsfolk-art/pip-imagegen/report.md`.

## Latest continuation: Pip installed; Dagna first appearance and recorded voice

Pip’s exact approved set is installed as a named child variant.140 focused tests
pass and Debug/Release test-project builds have zero warnings/errors. Four-party
production peaks322.73 /192.37 MiB, below512; cold204,485,341 bytes.
[Installation report](pip-installed/report.md). Dagna’s appearance draft and existing
voice sample await approval, with southwest corrected. Two image-tool calls and
zero PixelLab/new speech spend. [Draft report](dagna-imagegen-trial.md). No Dagna
production or game-rule/balance/phone-contract changes.

## Dagna replacement voice audition — 2026-11-04

Scenario OAuth reconnected in the current environment. A Gacrux audition combines
the existing town line and battle command, directed as a mature, low, flat Northern
guard captain. Source WAV, playable normalized MP3, exact request, live schema,
cost estimate, cap check, paid job receipt and hashes are retained in
`tools/tactics-assets/imagegen/townsfolk/dagna-holt/voice-recast/`.
One paid job: **4 CU quoted /4 CU billed**. No credits purchased. Member/project
caps are unlimited; this does not establish the remaining team plan balance.

**Owner voice approval pending.** Production cast, all66 Dagna clips and captions
remain unchanged. Gacrux also exists in the player grim.high cast; assess identity
distinction before finalizing. The MP3 has normalization only, no pitch shift;
this is a fresh synthesis, not processing of the rejected recording. Source is
mono24 kHz /7.48 seconds; mechanical decode/hash checks pass. Human listening
review must assess maturity, accent, exact words and command delivery.

Retain appearance and full-set/TV gates. Keep authored-recordings-only playback;
the handoff's old live-fallback instructions are historical. No live TTS is to be
enabled. No rules, balance, phone contracts or production textures changed.

## Current Dagna checkpoint — approved veteran voice installed

The owner approved the Gacrux audition and authorized the remaining voice lines.
All66 Dagna lines are now installed in that cast, with the approved veteran
direction, no pitch/filter and unchanged captions.63 distinct renders cost232 CU
quoted/billed (236 CU including the prior audition). No credits purchased.
Original clips and paid provenance remain intact; the normal draft voice sample
now plays the new production town bark. Future lines inherit the cast direction.
Full voice inventory1443/1443; checker0 flags;20 Python,71 voice/scene and52
story/bark playback tests pass. Exact re-import hashes match. Rules, balance,
phone contracts, authored-recordings-only playback and textures unchanged.
Appearance/full-set/TV artwork gates remain pending. Report:
`docs/superpowers/reports/2026-11-03-townsfolk-art/dagna-voice-recast/report.md`.
Reuse these recordings; do not regenerate the approved voice. No PR or merge;
rebase before pushing townsfolk-art. Earlier blocked voice notes are historical.

## Current checkpoint — Dagna completed review with approved voice

The owner authorized finishing her existing appearance (“ok, finish those please”).
Eight real idle/walk/talk directions,144 native cells, six40×40 portraits and a
19.6-second voiced review are ready for **finished-set/TV approval before art
installation**. Her66-line veteran voice recast remains approved and installed;
the reel reuses its town bark, with no new speech generation. No appearance
reapproval or concept regeneration is needed.

Atlas1728×768 /5.0625 MiB,96×96 frames, anchor[48,84], scale0.5, adult72/36.
Binary alpha/shared96-colour palette, fixed transform per direction, no mirroring
or per-frame recentering. Straight-back pauldron corrected to anatomical right;
use final rotations and north-corrected-source, not the superseded north guide.
All144 source cells selected; native bounds/motion/foot checks and six portraits
pass. Ten completion image calls plus two prior appearance calls; zero PixelLab
calls/spend/purchases. Requests/raw outputs/input hashes/normalized frames retained.

Typed import/portrait/bark stop-resume and140 focused presentation tests pass;
TV host Debug build0 warnings/errors. Final review four-party texture peaks
**325.77 MiB Ashen /192.37 MiB Cinder**, below512; native uploads/releases and
all-four race replacement/restoration pass. Production unchanged:207,000,224 bytes /
4,915 files; inventory hash544cebc50f4f92407ee35be5e2e7fb12fec42f45dbde3addfb7202fafd5addb8.
No rules, balance or phone changes. Guarded installer refuses pending full-set
approval before writing production.19 review hashes and native TV shot are bound
in sprite-approval.json; offline exact rebuild verified.

Review/video/voice: tools/tactics-assets/imagegen/townsfolk/dagna-holt/.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/dagna-imagegen/report.md.
Next: owner finished-set/TV approval, exact installation, production checks.
Reuse all completed assets/voice. Rebase before pushing townsfolk-art; no PR, merge
or issue closure. Earlier pending-appearance and blocked-voice notes are historical.

## Latest checkpoint — Dagna approved/installed; Celandine appearance draft

The owner approved Dagna's finished set: “Perfect, do the next character”. Exact
bound body and six portraits are installed; do not regenerate or seek approval
again. Body/portrait aliases guard_captain/dagna_holt resolve correctly.147 focused
presentation tests pass; Debug/Release test-project builds0 warnings/errors. Typed
production importer/bark/portrait and exact approved hashes pass. Production
four-party peaks325.77 /192.37 MiB below512 with full replacement/restoration and
upload/release gates. Cold pack207649084 bytes /4922 files, inventory
SHA256 6bf5e5410895a9f5d7355d97b721d1c9ab83d19fbebf7f6eb7f6d8ea0edd948f.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/dagna-installed/report.md.

Lady Celandine Marrowby follows (noble, celandine_marrowby). First pixel appearance
and existing recorded voice sample are under tools/tactics-assets/imagegen/townsfolk/
celandine-marrowby/. Approved concept reused; background cleaned; original and
exact requests/input/output hashes retained. Appearance approval pending before
motion/portraits, then separate full-set/TV gate. Native grid/alpha/palette/size/
anchor and equipment QA ahead; northwest near-side ledger/keyring should be
occluded before motion guides. Two image-tool calls, zero PixelLab/new speech
spend. No Celandine production content.

Rules, lore, balance and phone contracts unchanged. Her voice sample must be
included with future appearance and finished reviews. Rebase before pushing
townsfolk-art; no PR, merge or issue closure. Earlier pending-Dagna notes are
historical; continue with Celandine appearance approval.

## Current checkpoint — Celandine completed review with recorded voice

Appearance/voice approved: “Perfect, continue”. Eight real idle/walk/talk facings,
six40×40 portraits and19.6-second voiced review are ready for **finished-set/TV
approval before installation**. Reuse her existing town recording; no new speech.
Do not seek appearance reapproval or regenerate completed concepts/art.

Atlas1728×768 /5.0625 MiB,96×96 frame, anchor[48,84], scale0.5, adult72/36.
144 slots from143 selected source cells; northeast talk slot5 holds valid slot4
to avoid foot drift. Fixed transform per direction, binary alpha/shared96-colour
palette, no mirroring/recentering. Northwest guide occlusion corrected; north/
northeast motion sources corrected from wrong front views. Use final rotations
and corrected sources, not superseded guides/exports. Twelve completion image
calls plus two prior appearance calls, zero PixelLab spend or new speech.

All frame/foot/motion/portrait checks and typed body/portrait aliases/bark hold-
resume pass.147 focused presentation tests pass; TV host Debug build0 warnings/
errors. Four-party review texture peaks328.99 /192.37 MiB below512 with real
replacement/restoration/upload/release checks. Pending installer refuses before
writing production. Exact offline rebuild retains19 review hashes and native TV
shot;14 source receipts verify. Production unchanged:207649084 bytes /
4922 files, inventory SHA256 6bf5e5410895a9f5d7355d97b721d1c9ab83d19fbebf7f6eb7f6d8ea0edd948f.

Review and voice: tools/tactics-assets/imagegen/townsfolk/celandine-marrowby/.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/celandine-imagegen/report.md.
Next: owner finished-set/TV approval, install exact bound assets, production checks.
Rules/lore/balance/phone contracts unchanged. Include existing recorded voices in
future reviews. Rebase before pushing townsfolk-art; no PR, merge or issue closure.
Earlier pending-appearance notes are historical.

## Latest checkpoint — Celandine approved/installed; Old Maud appearance draft

The owner approved the completed set: “Looks good. Do the next character”. Exact
bound Celandine body and six portraits are installed; no regeneration or further
approval needed for that set. Body/portrait aliases noble/celandine_marrowby resolve
correctly.154 focused tests pass, Debug/Release test-project builds0 warnings/
errors. Typed production importer/aliases/portrait/bark and exact approved hashes
pass. Four-party production peaks328.99 /192.37 MiB below512; strict replacement/
restoration/upload/release gates pass. Cold pack208260666 bytes /4929
files, inventory SHA256 82f45b0cec3a2a190ef1e6211706aff7dd185bd30dc69d605e46fe457a12c1d4.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/celandine-installed/report.md.

Old Maud is next (hermit, old_maud). First pixel appearance and existing recorded
voice are under tools/tactics-assets/imagegen/townsfolk/old-maud/. Approved concept
reused. Both raw outputs/requests/hashes retained; candidate-1-preview.png applies
normal native alpha threshold and clears transparent RGB without redrawing.
Eight standing-cell bounds checks pass. Appearance approval pending before
motion/portraits; full-set/TV gate remains separate. Native grid/palette/body size/
anchors, staff handedness/grip and staff/boot contact QA ahead. Two image-tool
calls, zero PixelLab/new speech spend. No Old Maud production content.

Rules, lore/captions, balance and phone contracts unchanged. Include existing
recorded voice in future appearance and completed reviews. Rebase before pushing
townsfolk-art; no PR, merge or issue closure. Earlier pending-Celandine notes are
historical. Continue with Old Maud appearance approval.

## Latest Maud direction — older voice audition pending

The owner rejected the existing voice: “her voice is too young sounding”. A new
Gacrux audition requests an ancient105-year-old, papery, lightly quavering,
unhurried but lucid woman. Hear tools/tactics-assets/imagegen/townsfolk/old-maud/
voice-recast/audition-gacrux.mp3. Same caption, fresh synthesis, no post pitch or
filter. Quoted5 CU /job billing5 CU, no credits purchased. Live schema/cap check
and paid source/receipt/hashes retained. Mono24 kHz /9.36 seconds;0 source clipping
and MP3 decode passes. Owner listening approval pending before replacing all24
Maud lines. Full inventory/plan: old-maud/voice-recast-plan.json.

Production cast/clips, normal old sample and art remain unchanged. Do not reuse
the rejected young-sounding sample for a finished review. After voice approval,
apply the approved cast/direction to all24 lines consistently and replace the
normal draft voice sample with the approved production town clip. Gacrux is
shared with Dagna/player grim.high; listening review must judge identity separation.
Artwork appearance/full-set/TV gates remain separate. No art regeneration,
PixelLab spend, rules/balance/phone changes.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/maud-voice-recast/report.md.
Rebase before pushing townsfolk-art; no PR, merge or issue closure. Earlier
existing-voice draft notes are historical.


## Latest checkpoint — Maud approved ancient voice installed

The owner approved the Gacrux audition: “perfect, let's go with that”. All24
Maud lines are now installed at pitch0 without a filter; exact approved town
source reused and normal voice-sample.mp3 updated. Three-line listening reel:
tools/tactics-assets/imagegen/townsfolk/old-maud/voice-recast/approved-voice-reel.mp3.
Captions, other speakers, rules, balance, phone contracts and authored-recordings-
only playback unchanged. Old24 clips and six rejected new paid takes preserved.

Long direction narration and three lexical substitutions were found by ASR and
repaired; all24 final captions checked, accepting only documented leading ah/oh,
punctuation and neighbour spelling differences. Exact request overrides guarded
against caption/cast/delivery edits; sources/receipts/quotes/provenance and offline
reimport/validation tools retained in old-maud/voice-recast/batch/.24 source/clip
checks, full1443/1443 coverage,0 pitch/brightness flags,23 tooling +71 logic +52
story/bark presentation tests pass. Both Debug test-project builds0warnings/errors.
Old11-second chapter maximum snapshot replaced with existing30-second budget;
independent exact decoder and per-line bounds retained.

Cost274CU including previous5CU audition; continuation269CU.150 speechCU and124
transcriptionCU across61 unique jobs; no purchases/upgrades, no PixelLab calls.
Cold pack210288327bytes /4953files, inventory SHA256
87870feadf6bf4355fc9187ff04997e7fa505215927bc29f938ef0a5424f96c4.
No textures changed; last native4-party peaks328.99 /192.37MiB below512, not rerun.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/maud-voice-recast/report.md.

**Maud artwork appearance remains pending approval** before motions/portraits;
full-set/TV approval is separate. Voice approval does not approve her artwork.
Reuse the existing appearance/concept; no regeneration of completed assets.
Rebase before pushing townsfolk-art; no PR, merge, issue closure or publication.
Earlier pending-voice notes are historical.


## Latest checkpoint — Rook appearance draft with recorded voice

Owner accepted Maud’s voice and requested the next character. Rook (Ash Market
broker, bandit_captain/rook) has an eight-view first pixel appearance proposal
under tools/tactics-assets/imagegen/townsfolk/rook/. Existing approved concept
reused; existing Umbriel town clip included as voice-sample.mp3, no new speech.
Appearance does not create new gender/species/age canon.

Three image-tool calls, zero PixelLab spend/purchases. Sources/requests/hashes
retained. Southwest facing and rear ledger-satchel side corrected. Two defective
transparent exports retained; selected opaque magenta export is deterministically
colour-keyed by build-appearance.cjs. Eight standing-cell bounds/binary-alpha
checks and voice decode pass. Native grid/palette/body-size/anchors, gear occlusion
and residual key-fringe cleanup remain before motions; not a native atlas yet.
**Rook appearance approval pending** before animations/portraits, then separate
finished-set/TV gate before installation. Maud’s appearance approval remains
outstanding; do not infer it from the request to move to Rook.

Production unchanged:210288327bytes /4953files, inventory SHA256
87870feadf6bf4355fc9187ff04997e7fa505215927bc29f938ef0a5424f96c4.
No rules/balance/phone changes. Last native four-party peaks328.99 /192.37MiB,
not rerun for review-only files. Include existing recorded voice in future reviews.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/rook-imagegen/report.md.
Rebase before pushing townsfolk-art; no PR, merge, issue closure or publication.


## Latest checkpoint — Rook completed set awaiting finished-set/TV approval

Appearance and existing voice approved: “sounds and looks good”. Eight real
idle/walk/talk facings, six40×40 portraits, phone-friendly close-up and19.6-second
voiced native-TV review are complete under tools/tactics-assets/imagegen/townsfolk/
rook/. Reuse approved concept/appearance and existing Umbriel recording; no new
speech. Do not regenerate completed art or request appearance approval again.

Atlas1728×768 /5.0625MiB,96×96 frame, anchor[48,84], scale0.5, adult72/36.144
selected source cells/slots, no held replacements. Shared96-colour sprite palette,
binary alpha, fixed per-direction grid transform, no mirroring or per-frame shifts.
All native bounds/scale/foot/unique-motion/portrait checks pass, max vertical drift
1 world pixel. Nine completion image calls plus three prior appearance calls;
zero PixelLab spend/purchases. Raw exports/requests/inputs/hashes retained;
opaque colour-key exports avoid the prior alpha defect.

Typed body/portrait aliases bandit_captain/rook, six moods, original quest-gated
presence and bark stop/hold/resume pass.154 focused presentation tests pass,
host/test-project Debug builds0warnings/errors. Four actual heroes pass strict
open/crossing/roster replacement/restoration/upload/release budget checks in both
regions:332.20 /192.37MiB below512. Native TV at104,104 dock, party102,104; a private
review-only presence clone shows gated Rook without changing original flags or
production rules. Never edit linked review files.

Offline atlas/portrait/GIF rebuild preserves19 bound review hashes +native TV
still; all12 generation receipts verify. Pending installer rejects before writes.
Production unchanged:210288327bytes /4953files, inventory SHA256
87870feadf6bf4355fc9187ff04997e7fa505215927bc29f938ef0a5424f96c4.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/rook-imagegen/report.md.

**Next: owner finished-set/TV approval before exact Rook installation**, then
production checks. Maud appearance remains pending independently. Rules/lore/
balance/phone contracts unchanged. Include recorded voices in future reviews.
Rebase before pushing townsfolk-art; no PR, merge, issue closure or publication.
Earlier Rook pending-appearance notes are historical.


## Latest checkpoint — Rook approved and installed

Owner full-set/TV approval: “looks good. What's neext”. Exact approved Rook atlas
and six portraits installed; no regeneration. Both body/portrait aliases
bandit_captain/rook pass.161 focused presentation tests pass, Debug/Release
builds0warnings/errors. Typed production importer/source rects, original Rook
contact/completion quest gates and bark stop/hold/resume pass. All19 review hashes
and12 source receipts remain verified. Body SHA256
74bf2987f7a5da9563c9834c022f4de1a6a2d15126a7f402cd5d407adea6ba53.

Four actual party members pass strict production open/crossing/roster replacement/
restoration/upload/release gates:332.20 /192.37MiB below512. Production uses no
preview-presence clone; default demo correctly hides quest-gated Rook. Typed
checks explicitly exercise real contact/completion flags. No rule changes.
Cold pack210914068bytes /4960files, inventory SHA256
0990d66b04817b51942e119dd35ecbb7273dcedcfa06144c53a2bc7e902f547a.
All unrelated manifest data, rules/lore/balance/phone contracts unchanged.
No new image/speech/PixelLab calls, credits or purchases.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/rook-installed/report.md.

Next named appearance: Mother Skarrow (elderly goblin chief); reuse approved
concept and existing recorded voice, respecting species proportions. Legacy
generic goblin refresh remains outside scope. Maud appearance still awaits
approval independently before motions/portraits. Do not regenerate completed art.
Rebase before pushing townsfolk-art; no PR, merge, issue closure or publication.
Earlier pending-Rook approval notes are historical.


## Latest checkpoint — Maud appearance approved; Mother Skarrow first draft

Owner: “I approve Old Mauds, start Mother Skarrow”. **Old Maud appearance is now
approved**; exact existing appearance and approved voice retained. Her native QA,
motions and portraits are authorized and queued while Mother Skarrow starts.
Do not request Maud appearance/voice approval again. Her finished-set/TV gate
remains separate before art installation. Earlier pending-Maud appearance notes
are historical.

Mother Skarrow’s first eight-view pixel appearance and existing recorded town2
voice are under tools/tactics-assets/imagegen/townsfolk/mother-skarrow/. Approved
concept reused, not regenerated. Elderly female goblin chief, short adult goblin
proportions (proposed50 native body /25 world vs humans72/36), weathered green face,
long ears/greying braids, patched hide/wool, food pouches and plain LEFT-hand staff.
Appearance/voice review pending before motions/portraits, then separate finished-
set/TV gate before integration. Native grid/palette/body scale/anchors, ear/staff/
boot contact and handedness QA ahead; enlarged review sheet is not a native atlas.
Audit named-only aliases and required battle playback before later integration;
generic goblin refresh/contracts remain outside scope.

One image-tool call, zero PixelLab/new speech/purchases. Original opaque magenta
export/request/input/output hashes retained; deterministic key removal preserves
non-key RGB/layout, no redraw/rescale/mirror. Eight standing-cell bounds/binary-
alpha checks and existing voice decoding pass. No production art changes or game
test rerun. Production210914068bytes /4960files, inventory SHA256
0990d66b04817b51942e119dd35ecbb7273dcedcfa06144c53a2bc7e902f547a unchanged.
Last native4-party peaks332.20 /192.37MiB below512, not rerun for review-only files.
Rules/lore/balance/phone contracts unchanged. Include existing recorded voices in
future reviews. Report: docs/superpowers/reports/2026-11-03-townsfolk-art/mother-skarrow-imagegen/report.md.
Rebase before pushing townsfolk-art; no PR, merge, issue closure or publication.


## Latest Mother Skarrow direction — alternative voice audition pending

Owner rejected the existing voice: “let's draft another voice, it's not great”.
One-line Kore audition uses unchanged town2 caption with an elderly goblin
matriarch brief: low husky female contralto, dry age-creak, measured authority
and tired warmth. Fresh synthesis, no post pitch/filter. Hear mother-skarrow/
voice-recast/audition-kore.mp3 under tools/tactics-assets/imagegen/townsfolk/.

Live schema/cap check/quotes and paid sources/receipts/hashes retained.2CU speech
+4CU transcript check =6CU quoted/billed, no purchases/upgrades/PixelLab. Mono24kHz,
7.84s,0 clipping; WAV/MP3 decode pass. ASR detects no spoken direction notes and
matches remaining words but hears opening “Forty” as “For to you”; verify that
number before exact-take reuse. Owner acting/voice approval pending; do not claim
ASR proves maturity or perfectly matching words.

All67 Skarrow lines inventoried in mother-skarrow/voice-recast-plan.json. Production
casting/recordings/captions and normal original sample unchanged; rejected old
sample archived. After voice approval, replace all67 consistently with live quotes
and verification, retaining provenance/old files. Do not use the rejected normal
sample for the finished art review. Appearance/full-set/TV gates remain separate;
no Skarrow art approval inferred. Maud appearance already approved, motions queued.

No art/rules/balance/phone changes. Production inventory SHA256
0990d66b04817b51942e119dd35ecbb7273dcedcfa06144c53a2bc7e902f547a unchanged.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/mother-skarrow-voice-recast/report.md.
Rebase before pushing townsfolk-art; no PR, merge, closure or publication.
Earlier existing-voice draft notes are historical.


## Latest checkpoint — Mother Skarrow approved voice installed across all67 lines

Owner voice approval: “sounds good, use it”. All67 Skarrow lines installed as
Kore, pitch0, no filter,64 selected source takes. Canonical captions, other
speaker jobs/manifests and recordings-only playback unchanged. Normal town2
voice-sample.mp3 updated; three-line reel at mother-skarrow/voice-recast/
approved-voice-reel.mp3 under tools/tactics-assets/imagegen/townsfolk/.

Original67 clips and20 superseded paid speech takes preserved. Original audition
performance approved, but opening number unclear; verified numeric40 source
serves both identical “Forty” captions. Direction narration/word substitutions
were caught and repaired, never trimmed. Two stubborn chapter3 takes use only
documented pause tags with same Kore speaker/language/zero pitch/filter because
descriptive cues were narrated. Actual requests guarded in approvedRenderText;
all source/receipt/quote/hash/provenance and offline import/local-ASR tools retained.
Local unbiased small.en/medium.en checks are per-clip service-fee-free; model
weights stay outside the pack. Documented spelling/homophone/number/vocalization
normalizations and physically near-silent out-of-duration ASR artifacts only.

67 source/clip/word checks pass; mono24kHz16bit,4.4–15.16s,0 source clipping.
Full1443/1443 coverage,0 pitch/brightness flags.23 tooling +71 logic +52 story/
bark presentation tests pass; both Debug builds0warnings/errors. Original files
and all other speaker jobs/manifest data verified unchanged. Acting maturity is
owner listening judgment, not established by ASR/acoustic metrics.

Total224CU across85 unique successful jobs:220 speech +4 prior ASR. Continuation
218CU; selected speech169CU, superseded speech51CU incl original2CU audition.
Consumption5826→6044 matches; no purchases/upgrades/PixelLab/new images. Unused
quotes unbilled; cap status is not remaining plan-credit balance.
Cold pack215550416bytes /5027files, inventory SHA256
1373f33f5c9b56f62bd26015f3a87fe6bc23c8793fc587e5246d7d0f8fb99a3d.
Textures unchanged; last native four-party peaks332.20 /192.37MiB below512, not
rerun for audio-only work. No rules/balance/phone contract changes.
Report: docs/superpowers/reports/2026-11-03-townsfolk-art/mother-skarrow-voice-recast/report.md.

Skarrow **appearance approval remains pending** before motions/portraits; separate
finished-set/TV gate before artwork installation. No art approval inferred from
voice approval. Maud appearance already approved, completion art queued. Reuse
completed assets; include new approved production voices in future reviews.
Rebase before pushing townsfolk-art; no PR, merge, closure or publication.
Earlier pending-Skarrow voice notes are historical.


## Latest checkpoint — Mother Skarrow completed animation/portrait/voiced TV review

Owner appearance approval: “she looks good, do the animations”. Reused the existing
approved appearance/concept and all67 installed Kore voice lines. Artwork is now
ready for **separate complete-set/TV approval before production installation**.
Old Maud appearance/voice already approved; her completion artwork stays queued.

Eight real directions,288 native frame slots: six each idle/walk/talk/staff attack/
cast, three each hurt/down. Downed holds the authored lying frame35. Six40×40
portraits. Compact96×96 frames, anchor48/84, pixelScale0.5, two rows/direction,
1728×1536 atlas10.125MiB, shared96-colour palette/binary alpha. Idle body24.5–25.5
world pixels versus human36; no mirrored facings or per-frame recentering.
One fixed transform per direction; maximum vertical foot drift1world pixel.
Staff stays anatomicalLEFT. West duplicate staff repaired in one retained tool
edit. Connected-component frame extraction preserves source art extending into
magenta gutters without importing neighbouring staffs; no redraw or synthetic
pose construction. Native/source geometry checks complement visual owner review.

Review: tools/tactics-assets/imagegen/townsfolk/mother-skarrow/
mother_skarrow-finished-review.mp4 (31.6s; approved installed town2 voice),
mother_skarrow-closeup-review.gif and mother_skarrow-portraits-review.png.
Native TV still under this report’s ashen_ford-shots/world-mother-skarrow.png.
Private review pack /workspace/artifacts/issue80-mother-skarrow-review only.
Named overworld variant, named battle sprite, named lore portrait and registered
art aliases leave generic goblin_shaman and separate Skarrow Stormcaller intact.
Original slain presence gate retained. Gameplay fields and phone contracts unchanged.

Checks:161 focused presentation tests pass; Host and test Debug builds0warnings/
errors. Typed runtime verifies named body/portraits, eight facings, compact atlas
source rectangles, combat/down playback and bark stop/resume. Source/palette/
portrait/hash/unique-frame/production-isolation checks pass. Native software
Vulkan four-party peaks **340.48MiB Ashen Ford /192.37MiB Cinder Hollow**, below
512MiB; open/crossing/party-change/restored and all8human/elf class texture uploads/
releases pass the strict memory checker. These are memory figures, not GPU timings.

11 image-tool calls total: previous appearance1 +completion10 (eight direction
sheets, portraits, west repair). All raw exports/exact requests/input/output hashes
retained; offline rebuild verified. No PixelLab/speech generations, credit purchases
or upgrades. Image tool exposes no price; no monetary estimate inferred.
Production cold pack unchanged215550416bytes /5027files, inventory SHA256
1373f33f5c9b56f62bd26015f3a87fe6bc23c8793fc587e5246d7d0f8fb99a3d.
Installer rejects before any production write without exact finished-set/TV owner
approval;25 review assets and native TV still are hash-bound. Do not infer this
approval from the appearance or voice approvals. Do not regenerate completed art.

Rebase before pushing townsfolk-art; no PR, merge, issue closure or environment
publication. Earlier pending-appearance notes are historical and superseded.


## Latest checkpoint — Mother Skarrow approved and installed

Owner full-set/TV approval: “looks good, I approvee” (2026-11-05).
Installed the exact approved288-frame, eight-direction body atlas and six40×40
portraits after validating all25 review asset hashes and the approved TV still.
No new art or voice generation, credits, purchases or upgrades. All67 approved
Kore voice lines retained. Eleven named townsfolk artwork sets now installed.

Mother uses a placed goblin_shaman_mother_skarrow variant, named battle sprite
characters.mother_skarrow and named lore portrait preset. Generic goblin_shaman
and separate Skarrow Stormcaller remain unchanged. Staff attack/cast/hurt/down
playback included; lying down frame35 is held. Slain presence gate unchanged.
Added two registered cosmetic aliases, seven art files and six cosmetic document
edits only. Installer preserves compact existing JSON formatting. Every other
5021 baseline content file is byte-identical to9a208878. No gameplay, balance,
phone contract, caption or voice changes; all old assets preserved.

170 focused presentation tests pass, including named body/portrait/region preload
and generic shaman separation. All-atlas coverage now covers Mother’s authored
combat clips without treating other civilians as combatants. Typed production
checks verify eight facings/source rectangles/combat/down, six portraits,
bark stop/resume, named battle alias and original slain gate. Host/test Debug
builds have0warnings/errors. Native installed-pack software Vulkan four-party
peaks340.48MiB Ashen Ford /192.37MiB Cinder Hollow, below512MiB; strict memory
checks cover open/crossing/party-change/restored and all8human/elf class texture
uploads/releases. Software renderer figures establish memory, not GPU timing.

Production pack216586388bytes /5034files, inventory SHA256
728704ed27106c3fe7818874f4d3537882c8ba9bb9c1aead254cf0435a4f8147.
`mother-skarrow/verify-installed.cjs` verifies exact approved art, all retained
assets and cosmetic-only differences against the committed pre-install baseline.
It needs local Git subprocess access. Historical private-review isolation
artifacts remain frozen as pre-install evidence, including their old inventory.
Use verify-installed for the current installed checkpoint instead.

Native production screenshots/logs/memory/test/runtime evidence are retained at
docs/superpowers/reports/2026-11-03-townsfolk-art/mother-skarrow-imagegen/production/.
Do not regenerate completed assets or ask for Mother approval again. Old Maud’s
appearance and24-line voice are already approved; her completion animations and
portraits are next queued work, with a separate finished-set/TV gate.

Rebase before pushing townsfolk-art. No PR, merge, closure or environment publish.
Earlier pending-Mother approval/installation notes are historical and superseded.
