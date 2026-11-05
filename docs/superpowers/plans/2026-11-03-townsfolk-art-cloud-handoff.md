# Issue #80 — cloud handoff after Bree

Work in `higharc91/ArcBox`, branch **`townsfolk-art`**. Fetch and check out that
branch; inspect a dirty tree before changing it. Pull with rebase before pushing.
Do not merge, open a PR, close #80 or reset unrelated work. The owner requested a
checkpoint after Bree so a cloud AI can continue. **Current continuation:** Bree’s
full-set/TV approval arrived and her exact compact atlas is now installed. Halden’s
complete eight-direction idle/walk/talk set is also now approved and installed.
Mother Aubrine’s complete image-tool set and native TV review are now also approved
and installed. Tobin’s approved body and six portraits are also installed. Lark’s
finished set is now approved and installed. Corvin’s first pixel appearance
candidate awaits approval. Read the latest
[Aubrine installation report](../reports/2026-11-03-townsfolk-art/aubrine-installed/report.md).
Last observed PixelLab balance was **0.75 generations**; do not submit more paid work or purchase credits.
Read the [approved-integration report](../reports/2026-11-03-townsfolk-art/approved-integration/report.md) first.
The original checkpoint details below are retained as provenance; current status
and next steps at the end supersede its pending-approval statements.

## Read first

1. Issue #80 and its memory-target comment: **512 MiB TV textures**, four party members.
2. `docs/superpowers/reports/2026-11-03-townsfolk-art/report.md` and its screenshots.
3. `tools/tactics-assets/pixellab/townsfolk/README.md`, `sprite-approval.json`,
   `credit-ledger.json`, `directional/atlas-plan.json`, `directional/atlas-validation.json`.
4. `tools/tactics-assets/concepts/townsfolk/README.md`, `briefs.json`,
   `approvals.json`, `verified-inventory.json`, `review-index.md`.
5. `docs/superpowers/plans/overworld-issue-board.md` (OW-05/OW-10),
   `docs/superpowers/specs/2026-11-03-tactics-ashen-ford-npc-brief.md`,
   `tools/tactics-assets/pixellab/wardenflame-style.json`, and `docs/tv-host.md`.

## Done and approval state

- **All concepts approved:** 12 named sheets plus seven paired generic sheets
  (14 generic designs), and the child-B body supplement. Halden/Maud were recovered;
  **do not regenerate concepts**. The separate Scenario spend is 221 CU.
- **Bree candidate 1 and its original southeast idle are owner-approved.**
  Candidate 2 is superseded; keep its historical receipts, do not switch back.
- Bree has eight real directions, idle/walk/talk, retained native source frames,
  loop curation, compact atlas tooling, and real Windows TV inn/square/market shots.
  Exact approved southeast pixels are preserved. No frame is independently recentered.
- The **complete Bree directional set and committed TV shots are now approved**
  by the later chat reply “I approve or bree.” Her exact atlas is installed.
- Other **25 sprites** (11 named + 14 generic variants), portraits and production
  integration for those assets remain. Halden has a complete review-only set; the
  other 24 have not been generated. Legacy goblin presets remain out of scope; named Mother
  Skarrow is part of the approved named queue. Respect species/child proportions.

## Reproduce Bree offline, without spending credits

From the repository root with Node/npm:

```sh
npm ci --prefix tools/tactics-assets/pixellab/townsfolk
npm run build-review --prefix tools/tactics-assets/pixellab/townsfolk
npm run verify --prefix tools/tactics-assets/pixellab/townsfolk
```

The default separate pack is `$HOME/.codex/tmp/bree-tv-review` (Windows uses the
user home). `build-review-pack.cjs /absolute/other/output` accepts another empty
destination; `build-motion-review.cjs` and `verify-review-pack.cjs` also accept it.
The builder links unchanged assets, with a file-copy fallback across filesystems.
Never edit a linked rule/content file in this review pack: it points at production.
Generated manifests and Bree's image are separate files. Only the demo override
uses them. The later approved `install-approved-bree.cjs` now installs only the
verified Bree sprite, not linked content or the review hero.

```sh
ARCBOX_TACTICS_ART_ROOT="$HOME/.codex/tmp/bree-tv-review" \
  "$GODOT" --path src/ArcBox.Host -- \
  --world-demo=ashen_ford \
  '--world-looks=inn@25,55@27,55;square@29,62@31,63;market@28,64@30,65' \
  --shots=/absolute/shot-directory --perf --quit-after=45
```

Use Godot **4.7.2 .NET** and build the host first. Linux software-rendering flags
are in `docs/tv-host.md`; report them, and do not call cloud timing a GPU benchmark.
On the owner's PC use the **left monitor**, no focus stealing: temporary
`src/ArcBox.Host/override.cfg` with `[display]` / `window/size/no_focus=true`, Godot
`--position 160,100`. Never overwrite an existing override. Restore/remove yours
afterwards. The PC's working SDK is
`C:/Users/fatma/AppData/Local/Microsoft/dotnet/dotnet.exe`.

Review GIFs: `bree-eight-direction-idle.gif`, `-walk.gif`, `-talk.gif`. Rows are
south, southwest, west, northwest, then north, northeast, east, southeast.
Frame contact sheets show each direction on one row. The atlas has 16 unique
frame slots per direction, 96×96 frames, anchor `[48,84]`, scale 0.5; the talk
playback repeats source indices for the return to rest. **Do not rescale the body
or restore the wasteful 160×160 transparent padding.** `review-output.json` stores
the atlas hash. The normalizer's older hero-canvas output is a staging source.

## PixelLab provenance, budget and queue safety

- Final remaining balance: **30.75 generations**; Bree total **49.25**. This is
  not enough for the entire remaining cast at the same animation scope. Quote
  live costs and respect remaining credits; never purchase/upgrade automatically.
- Full curated character: `808551e2-cb8d-471b-a5be-6ed59b28e7a7`.
- Raw Pro Flash study: `ba372eea-b19c-4299-ac4b-96655efdc38b`.
- Walk group: `6b51ee92-573e-48b9-89c8-759ea916bd1e`.
- Idle group: `3ef17b07-2db8-4403-9958-af10f1c4ca11` (seven new directions;
  southeast uses the original approved loose-image clip).
- Talk group: `c27ce5fe-da26-43c5-872e-dd505c17fd31`.
- `directional/motion-result.json` gives actual provider frame URLs; sources are
  saved locally. `curated-rotations/` and `atlas-plan.json` are authoritative.
  `rejected-front.png` and its rejection record are **not** valid south art.
- All Bree jobs finished. `queue-state.json` has no remaining directions. **Do not
  resubmit them.** Slot-rejection receipts are failures to queue, not missing assets.
- PixelLab has eight active job slots. Queue only available slots; append missing
  directions to the **same animation group and same name**. Use the provider's
  wait tool; do not repeatedly reroll or recreate a character.
- API credentials live only in local Codex configuration, **never in this repo or
  handoff**. A cloud worker must use its own connected MCP/secret configuration.
  Local `.codex/tmp/pixellab/call.cjs`/queue helpers are not required for offline
  rebuilding and are intentionally not in git. Query live tool schemas before
  further paid calls. Never paste tokens into commits, reports or tool logs.

## Continue in this order

1. Bree’s and Halden’s full-set/TV approvals and integrations are complete.
   Future NPC sets retain their own owner pixel/TV approval gate.
   Continue independent free **OW-05 loading/perf/tooling work** while awaiting
   approval or an available allowance. Do not redo approved concepts.
2. After approval, finish the named/generic sprite pipeline from the existing
   designs, maintaining one art style and complete real directions. Keep prompts,
   source frames, hashes, curation, exact receipts and costs. Resolve incomplete
   generic concept angles during directional QA; don't invent anatomy from legacy kits.
3. Produce named portraits from approved concepts in the existing portrait frame.
   Add an optional placed-person variant that round-trips through the Godot editor.
   Keep old sheets available until the approved swap lands.
4. Integrate later assets only after their owner approval. Bree’s ordinary bark
   talk/idle fallback and stop/resume/contact wiring is complete and tested; reuse
   it. Do not change game rules, balance or server-to-phone contracts.
5. Complete OW-05: load only the party's heroes and the current region's used
   people/creatures, lazily within the existing per-frame budget. Add the required
   `--world-demo --perf` gate for a full party of four, measure Ashen Ford and
   Cinder Hollow plus cold download size before/after. **Fail above 512 MiB.**
   The 190.5 → 195.0 MiB sample is one stand-in, not this gate.
6. Run full .NET Debug/Release builds with zero warnings, solution tests, web tests
   and build, atlas/anchor/frame/height tests, variant export round-trip and real
   TV walks/e2e. Report Windows-only checks still needed if cloud hardware cannot
   reproduce them. Update the board/report, pull with rebase, push `townsfolk-art`.

Checkpoint checks: host Debug build 0 warnings/errors; all **2,450 presentation
tests** pass; offline crop/import/height/contact/frame validation passes; real GPU
TV renders pass. Full solution/web checks and the final memory gate are not done.


## Historical loading checkpoint before full-set approval

OW-05 now has used-only, budgeted world-unit loading, explicit release on roster changes, shared fauna textures, and background fauna navigation preparation. Actual party sheets stay resident across hidden/formation/battle frames. The production assets and all approval/credit records remain unchanged.

The new four-party `--world-demo=<region> --perf` check covers region open/walk, an all-four race change and restoration, with a **512 MiB** cap and a strict log verifier (`tools/tactics-playtest/world-memory.cjs`). Ashen Ford opens at **264.91 MiB**, Cinder Hollow at **154.87 MiB**; swap peaks are **302.58 / 192.37 MiB** on Linux software Vulkan. The separate Bree review pack opens at **267.56 MiB** with four heroes and passes too. The unchanged cold production pack is **200,832,616 bytes**. Detailed logs, native tour screenshots, limits and final checks are in [the loading report](../reports/2026-11-03-townsfolk-art/loading/report.md).

Continue from the current branch rather than rebuilding these changes. Full-set Bree/TV owner approval remains pending. No further sprites/portraits were generated and nothing was installed in production. PixelLab MCP tools were not exposed in this cloud session, so no fresh balance or quote was obtained and **no credits were spent**. Check live schemas/costs/balance when that connection becomes available; 30.75 remains historical.

On cloud, the offline review pack was rebuilt at `/workspace/artifacts/issue80-bree-review` using an explicit output argument to `build-review-pack.cjs`, and the existing pixel/atlas verifiers pass. Do not edit linked content files. Future optional variant integration must update the region preload selector and WorldPeople's sprite selection together, and rerun all memory gates after the final approved assets land. Keep the existing art approval gates; current memory results do not approve the remaining cast.


## Continuation after owner’s Bree approval

- The chat approval “I approve or bree.” authorizes Bree’s complete committed
  directional set and TV appearance. `sprite-approval.json` binds that approval
  to the existing GIF/plan/TV hashes. `install-approved-bree.cjs <review-pack>`
  installs the exact atlas; no completed art was regenerated. Production keeps
  legacy files. Portrait replacement remains a later phase.
- Authored talk clips now play during ordinary world barks, with explicit idle
  fallback for missing clips. Stops preserve contact and resume their route.
- Optional `RegionPerson.Variant`/PersonMarker variant is cosmetic. Preset + suffix
  selects `characters.<preset>_<variant>` if available, otherwise the preset.
  World drawing and texture loading share `WorldPeople.SpriteKey`. Native Godot
  save/load/export check: `--check-person-variants` in the world editor.
- Four-party production gates pass with Bree: Ashen Ford open **267.56 MiB**,
  roster peak **305.24 MiB**; Cinder Hollow open **154.87 MiB**, peak **192.37 MiB**.
  Cold pack now **201,146,592 bytes**, **313,976 bytes** above the prior pack.
- Halden character `a25c93f8-7dcd-45fa-8db2-4b1539c19e6e`: 6 generations for
  eight directions, 8 idle, 8 walk, 8 talk = **30**. Retained sources/receipts in
  `tools/tactics-assets/pixellab/townsfolk/halden/`; no new concept or reroll.
  The 1440×768 compact atlas is **4.21875 MiB**, scale 0.5. Talk playback returns
  through existing frames. No direction is mirrored or independently recentered.
- API public Backblaze URLs were forbidden. The documented authenticated
  `api.pixellab.ai/mcp/characters/<id>/download` ZIP worked after jobs completed
  (423 while active); redirects strip credentials on host changes. Do not
  regenerate art to work around a download failure. No credential is committed.
- Next: owner review of Halden’s directional GIFs and TV shots, then integrate
  only if approved. Static north touches its source canvas’s top edge; inspect
  this during review. Other 24 sprites and portraits still need generation.
  **0.75 generations remain**, so continue free loading/tooling/review work
  until an allowance is available. Never purchase or upgrade automatically.
- Rules, balance and phone contracts remain unchanged. Keep this branch only;
  pull with rebase before pushing. Do not merge, open a PR or close #80.

Current continuation checks: Debug/Release solution builds have zero warnings/errors;
2,460 presentation, 676 game, 508 phone and 16 Node cases pass. The prior 397
WorldTools cases and final 44 writer cases (including the new variant case) pass;
native editor round-trip passes. Four-phone live walk/restart/cold reconnect/drop
passes with `LP_NUM_THREADS=2`, 20/20 presses, 95 ms median against the unchanged
100 ms budget. Default Mesa first missed at 136 ms; retain both receipts and do
not claim GPU timing. The environment has a four-CPU quota. Whole-cast final
regression and new-art Windows review remain ahead; prior full-solution/isolated
logic and cave-tour evidence is in the loading report, not a fresh full rerun.

## Current checkpoint after Halden approval

The owner reviewed Halden directly in chat (directions, native TV square, walk/talk
GIFs) and approved: **“OK, he looks good”**. `halden/sprite-approval.json` records
this gate, exact preview/source hashes and production installation. Do not ask
for his approval again or regenerate his completed art.

`node tools/tactics-assets/pixellab/townsfolk/halden/install-approved.cjs` installs
the exact 1440×768, 4.21875 MiB atlas at `creatures/halden_varrow.png`, bound to
`characters.mayor` and `characters.halden_varrow`. The existing talk/stop/resume
wiring is reused. No new generation or credit spend, no portrait/rule/phone change.
Production four-party gate peaks: Ashen Ford **307.61 MiB**, Cinder Hollow
**192.37 MiB**, with the same 512 MiB cap. The production cold pack is now
**201,517,448 bytes**, +370,856 from the Bree-only pack.

Latest evidence: [Halden installation report](../reports/2026-11-03-townsfolk-art/halden-installed/report.md).
Other 24 sprites and portraits remain; obtain a fresh balance/quote before any
future paid work and never buy credits automatically. Keep all existing approved
source frames, receipts and approval gates. Pull with rebase before pushing this
branch; no merge, PR or issue closure. Earlier review-only statements are historical.

## Alternative art trial requested by the owner

The owner requested trying the assistant’s image tool rather than PixelLab for
the next character. Mother Aubrine has one new appearance candidate under
`tools/tactics-assets/imagegen/townsfolk/mother-aubrine/`, from her existing
approved concept and Bree/Halden pixel-style references. No PixelLab call/spend.
The sheet’s compass order has repeats, transparency is imperfect, and the logical
pixel grid/native scale are unvalidated. Await appearance review; do not install
it as a finished eight-direction atlas. Keep the existing approval/TV/frame/memory
gates. Production still uses approved Bree/Halden; no completed art was regenerated.
See `reports/2026-11-03-townsfolk-art/imagegen-trial.md`.

## Current Aubrine completion checkpoint

The owner approved her appearance: “Looks good, continue so we can she her finished”.
Aubrine now has a complete **eight-direction idle/walk/talk set (144 native frames)**
and **six native 40×40 portrait moods**, built with the image tool and no PixelLab
API/credit use. All requests, raw images, normalized frames, shared palettes,
curation and receipts are committed under
`tools/tactics-assets/imagegen/townsfolk/mother-aubrine/`. Do not regenerate them.

Read [the completed review report](../reports/2026-11-03-townsfolk-art/aubrine-imagegen/report.md).
`aubrine-finished-review.mp4` shows loops/portraits and a labelled native TV still.
The left profile now correctly carries the cloth pouch; the other 126 native
frames remain exact. Use `rotations/` for final native facings, not the older
`directional/rotations/` input guides. The body atlas is 1728×768 / 5.0625 MiB,
frame96×96, anchor[48,84], scale0.5; idle/talk play ten indices across six sources,
walk six. Per-direction fixed import transforms, binary alpha, locked96-colour
palette. Do not apply this adult human height blindly to children/goblins.

Full-set/portrait/TV approval **is still pending**; appearance approval is already
granted and must not be requested again. `sprite-approval.json` binds the complete
review to exact hashes. A prepared `install-approved.cjs` refuses the pending gate;
its refusal was verified against the unchanged production inventory. After full
approval, update the per-character frame-count tests for Aubrine's10/6/10 clips,
integrate the body/portraits, and rerun production gates. Keep Bree/Halden checks strict.

Actual native review and typed game import/bark/portrait checks pass; all144 frames
validate with <=1world-pixel foot variation. Review peaks with four heroes and
roster replacement: **310.65 MiB Ashen**, **192.37 MiB Cinder**, below512. Offline
rebuild hashes reproduce. Production stays at Bree/Halden, cold201,517,448bytes;
no rules/balance/phone changes. Other23 sprites and remaining portraits are ahead.
Use the new image-tool package (`npm ci --prefix tools/tactics-assets/imagegen/townsfolk`)
for offline builders; no PixelLab helper/credential is needed. Pull with rebase
before pushing; do not merge, open a PR, close #80 or purchase credits.

## Current continuation — Aubrine approved, Tobin appearance candidate

The owner approved Aubrine's complete set/TV review: **“She looks great! Who is next?”**.
Approval is recorded and her exact existing atlas plus six portraits are installed.
Do not ask for this approval again or regenerate any completed assets. Latest
production gates: Ashen **310.83MiB**, Cinder **192.37MiB**, four actual heroes,
512MiB cap; cold pack **202,137,516bytes /4,821files**.111 focused tests pass,
Debug/Release test-project builds zero warnings/errors. Production typed import,
portraits and bark stop/resume pass. Rules/balance/phone contracts unchanged.
See the installation report linked at the top for logs, exact hashes and commands.

The owner selected Tobin Fell next and authorized the image-tool approach.
`tools/tactics-assets/imagegen/townsfolk/tobin-fell/` contains one first eight-view
standing appearance sheet, exact request, input/output hashes and review notes.
**Appearance approval is pending**; do not expand to full motions before it.
No Tobin production content changed. Native grid/alpha/palette/anchors/height and
full-set/portrait/TV approval remain separate gates. Other23 sprites, including
Tobin, and remaining portraits are ahead. No PixelLab calls/spend; check fresh
costs and balance before any future paid work. Never purchase credits. Pull with
rebase before pushing townsfolk-art. Do not merge, open a PR or close issue80.

## Current checkpoint — Tobin finished, full-set approval pending

The owner approved his appearance: **“yeah that looks great. Proceed with finishing him”**.
Do not ask for appearance approval again. The complete eight-direction idle/walk/
talk set (144 native frames) and six 40×40 portraits are now committed under
`tools/tactics-assets/imagegen/townsfolk/tobin-fell/`, with all raw requests/sources,
receipts, native frames, atlas, review GIFs and a 16.6-second silent reel ending
in a labelled native TV still. Reuse them; do not regenerate completed art.

Read [the Tobin review report](../reports/2026-11-03-townsfolk-art/tobin-imagegen/report.md).
Final atlas SHA256:`d0b6a196f881238c2a5ea9928abe1a68abc5dc083a5c3a7491a3e9265a0bf11e`.
1728×768 /5.0625 MiB, frame96×96, anchor[48,84], scale0.5; playback10/6/10.
One regular source-column grid and shared transform per direction, never individual
frame recentring or mirroring. Planted/stride foot checks pass, max1.5 world pixels.
A corrected south walk replaces only six frames; all138 other native frames are
exact. Use `rotations/` for final native guides, not appearance input guides.

**Full-set/portrait/TV approval is pending.** The finished approval record binds
13 preview/import hashes and the native forge TV shot. The guarded installer
refuses the pending gate and was tested against the unchanged production inventory.
After approval, install these exact assets, add Tobin's strict10/6/10 counts and
portrait/stop-resume cases, and rerun production gates. Body/portrait aliases are
blacksmith and tobin_fell. No production content or rules/balance/phone changes.

Typed importer/portrait/bark checks, all144 frame checks and exact rebuild pass.
Four actual heroes review peaks: **313.87 MiB Ashen /192.37 MiB Cinder**, cap512.
Production remains202,137,516 bytes /4,821files, inventory hash unchanged.
Ten completion image-tool calls, zero PixelLab calls/credits; no purchase. Other22
sprite sets and remaining portraits are ahead. Check fresh PixelLab costs/balance
before future paid work. Pull with rebase before pushing townsfolk-art; no merge,
PR or issue closure. Earlier appearance-pending notes are historical.

## Latest owner approval — Tobin

The owner approved the finished set: **“Looks good. Who is next”**.
`sprite-approval.json` records full-set/TV approval with all existing hashes intact.
Production installation is still pending; do not ask for Tobin approval again.
Next integration should install the exact approved set and rerun the recorded
strict action/portrait/stop-resume and production memory/cold-pack checks.
Lark, the travelling minstrel, is next in the approved named concept queue after
Tobin. Do not regenerate Lark's existing approved concept. No new generation
has been authorized by this status question.

## Latest appearance candidate — Lark

The owner authorized starting Lark with the image tool. One eight-view standing
candidate is saved under `tools/tactics-assets/imagegen/townsfolk/lark/`, with exact
request/input/output hashes. The approved concept was reused; no concepts or
completed character assets were regenerated. The rear concept defines instrument
carry geometry to resolve its conflicting front/rear shoulder position. Preserve
anatomical equipment sides; no mechanical mirroring.

**Lark appearance approval is pending.** Do not expand to full motions before it.
Native grid/alpha/palette/scale/anchor import and full-set/portrait/TV review remain
ahead. No production content changed. Tobin's finished-set approval is complete;
his installation and strict production checks remain the independent next work.
No PixelLab API calls/spend/purchases. Keep existing credit and512 MiB/four-member
limits. Pull with rebase before pushing townsfolk-art; no PR, merge or issue closure.

## Tobin integration completed after owner approval

The exact approved Tobin atlas and six portraits are installed. Approval metadata
records installation; no Tobin art was regenerated. Production body/portrait aliases
are blacksmith and tobin_fell.118 focused tests pass, Debug/Release test-project
builds zero warnings/errors, typed production importer/bark/portrait check passes.
Fresh four-party production peaks **314.04 MiB Ashen /192.37 MiB Cinder**, cap512.
Cold pack **202,843,253 bytes /4,828 files**, +705,737; inventory hash
`2e6da18bde5ec8564430f04b4992bacdc49c5bd82d57d603d6ac618a7871d5f4`.
See [the Tobin installation report](../reports/2026-11-03-townsfolk-art/tobin-installed/report.md).
Rules/balance/phone contracts unchanged. Prior installation-pending notes are historical.
Lark's appearance was approved: “Yes, looks great. Continue”; finish that separate
review set, preserving the full-set/TV approval gate. No PixelLab spend or purchase.

## Current checkpoint — Lark complete; final approval pending

The owner approved Lark's appearance: **“Yes, looks great. Continue”**. Do not ask
for that appearance approval again. All eight idle/walk/talk facings and six native
40×40 portraits are finished under `tools/tactics-assets/imagegen/townsfolk/lark/`.
Retained: exact requests, raw images, source receipts,144 native frame slots,
143 selected source cells, shared palette, offline builders/verifiers, all-direction
GIFs/contact sheets and a16.6-second silent reel ending with a labelled native TV
still. Reuse them; do not regenerate completed concepts, motions or portraits.

Read [the completed Lark report](../reports/2026-11-03-townsfolk-art/lark-imagegen/report.md).
Final atlas SHA256:`c532af63e88448bba8b0433b515ab0a95036b4915cb48a196c580fb1ebc6ff3b`.
1728×768 /5.0625 MiB, frame96×96, anchor[48,84], scale0.5, playback10/6/10.
One regular grid and shared scale/offset per direction; exact RGB-keyed locked96
palette, binary alpha. No mirroring or per-frame recentering. Northwest near hip
is corrected to paper satchel; south walk uses corrected opposite-leg poses.
Southwest talk holds its valid peak instead of a rejected drifting raw frame.
All119 unrelated native frames remain exact. Final native facings are `rotations/`,
not the older `directional/guides/` appearance inputs. Adult human height must not
be copied to children/goblins.

**Lark finished-set/portrait/TV approval is pending.** Approval metadata binds15
exact review/validation hashes and the TV screenshot. The guarded installer
refuses the pending gate, with production inventory unchanged by Lark. After full
approval, install the exact set, add strict Lark action-count/portrait/stop-resume
cases and rerun production memory/cold-pack checks. Aliases are minstrel and lark.

Frame/foot/gait/portrait checks, typed game importer/bark and exact offline rebuild
pass. Four actual heroes review peaks: **317.09 MiB Ashen /192.37 MiB Cinder**, below512.
Current production contains approved Bree, Halden, Aubrine and Tobin, cold pack
202,843,253 bytes /4,828 files; inventory hash remains the Tobin-installed hash.
Tobin's fresh118 focused tests and zero-warning Debug/Release test-project builds
are in his installation report. Rules, balance and phone contracts unchanged.
11 Lark completion image-tool calls, zero PixelLab calls/credits/purchases. Other21
sprite sets and remaining portraits are ahead. Check live PixelLab costs/balance
before future paid work; never purchase credits. Pull with rebase before pushing
this branch; no PR, merge or issue closure. Earlier pending-state notes are historical.

## Latest checkpoint — Lark installed, Corvin first appearance

The owner approved Lark's completed set: **“Looks good, next”**. Approval is recorded
against all existing hashes, and the exact atlas/six portraits are installed.
Do not ask for Lark approval again or regenerate the completed art. Production
aliases: minstrel and lark.125 focused tests pass; Debug/Release test-project builds
zero warnings/errors; typed production importer/bark/portrait check passes.
Four-party production peaks **317.26 MiB Ashen /192.37 MiB Cinder**, cap512.
Cold pack **203,477,572 bytes /4,835 files**, +634,319; inventory SHA256
`ddc6d04e5211f70321235e691f88d540d43b7044fc37d4a69ac99a13702d0d1f`.
Read [the Lark installation report](../reports/2026-11-03-townsfolk-art/lark-installed/report.md).
No rules, balance or phone-contract changes. Earlier pending-Lark notes are historical.

Corvin Ashby follows Lark in the approved named queue. One first pixel appearance
sheet is under `tools/tactics-assets/imagegen/townsfolk/corvin-ashby/`, with exact
request, input/output hashes and original path. Existing concept reused, no redesign
or other character regeneration. Corvin's preset is sage, character id corvin_ashby.
Right-hip satchel/keys, left-hip notebooks/ink bottle; round spectacles and worried
humane face. **Corvin appearance approval is pending**; do not expand to motions
before it. Native import/alpha/palette/scale/anchor QA and final full-set/TV gate
remain ahead. No Corvin production content. Other21 sprite sets and remaining
portraits are ahead. One appearance image-tool call, zero PixelLab calls/credits.
Check live costs/balance before future paid work; never purchase credits. Rebase
before pushing townsfolk-art; no merge, PR or issue closure.

## Current checkpoint — Corvin finished review

The owner approved Corvin’s appearance: “OK, he looks good. Proceed”. The complete eight-direction idle/walk/talk review and six portraits are under `tools/tactics-assets/imagegen/townsfolk/corvin-ashby/`. Do not request appearance approval again. North was corrected to a true rear view; one drifting west talk pose is held. All 144 native slots pass frame/alpha/palette/foot checks and the typed game importer/bark check. The 13 bound review hashes survive an offline rebuild. Four-party review peaks 320.30 MiB Ashen / 192.37 MiB Cinder, below 512 MiB. Full-set/portrait/TV approval remains pending; guarded production integration is not yet authorized. Rules/balance/phone unchanged, zero PixelLab spend. Reuse the retained images and receipts; pull with rebase before pushing, no PR or merge. Report: `../reports/2026-11-03-townsfolk-art/corvin-imagegen/report.md`.

## Owner preference — include existing voice samples in character drafts

When presenting each character's appearance draft and completed review, include a
playable sample of that character's existing recorded voice when available. Match
clips to the character's actual bark/story IDs and text under `content/tactics/base/lore/`;
reuse the recorded audio rather than regenerating it. If no recording is found,
state that clearly. Corvin example: `lore/audio/bark_corvin_ashby_town_1.43f67d33.ogg`.

## Latest checkpoint — Corvin approved and installed

The owner requested installation: **“OK, finish him”**. The exact reviewed atlas
and six portraits are installed; do not ask for approval again or regenerate them.
Production body keys are characters.sage and characters.corvin_ashby; portrait
presets sage and corvin_ashby. Four-party production peaks **320.48 MiB Ashen /
192.37 MiB Cinder**, below512. Cold pack **204,126,234 bytes /4,842 files**,
inventory SHA256 `4298fd0489664ab01f7a4e3e23e8f117e5f32dd15bae3273b13b7986409d6ec0`.
See the Corvin installation report for test/build receipts. Rules, balance and phone
contracts remain unchanged. Earlier pending-installation notes are historical.
Continue remaining named and generic cast with their own appearance/full-set gates.
Include a playable existing recorded voice sample with each future character draft
and completed review. Never purchase PixelLab credits. Pull with rebase before
pushing townsfolk-art; no PR, merge or issue closure.

## Latest draft — Pip Ashdown

The owner authorized continuing with Pip. Her first pixel appearance and existing
recorded voice sample are in `tools/tactics-assets/imagegen/townsfolk/pip-ashdown/`.
Exact request, input/output hashes and voice provenance are retained. Appearance
approval remains pending; no animations/portraits or production installation.
The top-row second view incorrectly faces right; correct it to southwest before
motion guides. Target child52 native /26 world pixels, not adult72/36. Pip is nine;
preserve child proportions and the approved ordinary patched autumn clothing.
Future integration must use the named cosmetic child variant mechanism, preserving
other generic children. Include the existing voice sample with future reviews.
No PixelLab calls/spend/purchases, no gameplay or phone-contract changes. Reuse
completed assets. Rebase before pushing townsfolk-art; no PR, merge or issue closure.

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

## Latest checkpoint — Pip approved/installed, Dagna first draft

The owner approved Pip’s finished set (“Looks great, what's next”) and authorized
installation/next draft (“OK proceed”). Exact approved Pip atlas and six portraits
are installed; do not request approval again or regenerate completed art/voice.
Only Pip’s child preset uses variant pip; generic children retain their art.
Body keys child_pip/pip_ashdown; portraits likewise.140 focused tests pass;
Debug/Release test-project builds zero warnings/errors. Typed production import,
bark, portraits and generic-child isolation pass. Region parsed equality holds
once Pip’s cosmetic variant field is removed. Production four-party peaks322.73 /
192.37 MiB, below512. Cold204,485,341 bytes /4,849 files; inventory SHA256
`aa9191b3c40b055c36b83ba5b7f5c5601f4fcacd99fde2bbc758db6cbf0bc256`.
Report: `../reports/2026-11-03-townsfolk-art/pip-installed/report.md`.

Dagna Holt is next (preset guard_captain, character dagna_holt). Her first pixel
appearance and existing town-bark MP3 are in `tools/tactics-assets/imagegen/townsfolk/dagna-holt/`.
Existing approved concept reused. First wrong southwest angle corrected before
presentation; both raw sources, requests/input/output hashes and voice provenance
are retained. **Appearance approval pending** before motions/portraits; full-set/TV
approval remains separate. No Dagna production content. Native alpha/grid/palette/
body scale/anchor/equipment QA still ahead. Two image-tool calls, zero PixelLab
spend or speech generation; no prices inferred or credits purchased. Rules,
balance and phone contracts unchanged except Pip’s cosmetic placement variant.
Include existing recorded voice samples with future drafts. Rebase before pushing
townsfolk-art; no PR, merge or issue closure. Earlier pending-Pip notes are historical.

## Latest Dagna direction — voice recast requested

The owner rejected Dagna’s existing voice as too young for a seasoned veteran and
requested changing the voice, keeping the older visual direction. The proposal,
exact sample lines and complete current Dagna line inventory are saved in
`tools/tactics-assets/imagegen/townsfolk/dagna-holt/voice-recast-plan.json`.
Speech generation is blocked: Scenario/TTS tools are not connected in this session.
No new voice/audio was generated, no credits spent, and production casting/clips
remain intact. Audition a mature female voice with low, flat Northern command;
Gacrux is a candidate, not an approved cast. Check live schemas/cost/balance before
spending, never purchase credits. Render a concrete preview, then update all Dagna
recordings and live fallback consistently after owner voice approval. Do not pitch
shift the rejected sample and present it as a newly cast performance.

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

After Dagna's art approval and installation, the next approved named concept in the queue is Lady Celandine Marrowby (character_celandine_marrowby). Reuse her existing approved concept and include her existing recorded voice with the appearance draft. The owner asked who follows Dagna; no Celandine artwork has been generated in this checkpoint.

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


## Latest checkpoint — Old Maud completed animation/portrait/voiced TV review

Owner completion authorization: “ok, do those”. Appearance was already approved
(“I approve Old Mauds, start Mother Skarrow”); Gacrux voice already approved and
installed across all24 lines. Reused both plus the approved concept. **Separate
finished-set/TV approval remains required before Old Maud artwork installation.**
Mother Skarrow is already approved/installed; do not ask her approval again.

Maud has eight real facings,144 native frames: six source poses each idle/walk/
talk,10-frame ping-pong idle/talk playback, slower5fps six-frame walk. Small blink/
breath, careful alternating steps, restrained emptyLEFT-hand gestures; ordinary
walking stick remains anatomicalRIGHT. Six40×40 mood portraits. Human century-old
stoop normalized to64native/32world pixels versus upright human72/36.96×96 frames,
anchor48/84, headTop20, pixelScale0.5,1728×768 atlas5.0625MiB,96 shared colours,
binary alpha. One fixed registration per direction, no per-frame recentering or
mirroring; maximum vertical foot drift1.5world pixels. Component-based cell
extraction preserves source pixels and excludes neighbouring sprites. No redraw
or synthetic posed frames. Automated geometry checks complement owner visual QA.

Review video: tools/tactics-assets/imagegen/townsfolk/old-maud/
old_maud-finished-review.mp4 (19.6s). Includes approved installed Gacrux town1:
“Sit down, dearie. I've waited a hundred years, I can wait while you sit.”
Close-up GIF, three eight-facing action GIFs, portrait/contact sheets, every
normalized frame, source exports, guides and native TV still retained.21 assets
and the TV still are hash-bound in sprite-approval.json. Complete-set status is
pending_owner_full_set_tv_approval; guarded installer refuses before writes.

Private /workspace/artifacts/issue80-old-maud-review uses a hermit_old_maud
placed variant plus named old_maud sprite/portrait and art aliases. Generic
hermit art untouched. No battle role or new gameplay behavior. Rules/balance/
phone contracts/voices/captions and production content unchanged. Isolation
checks compare all5034 files, allowing only five named review cosmetic documents.
Current production pack216586388bytes, inventory SHA256
728704ed27106c3fe7818874f4d3537882c8ba9bb9c1aead254cf0435a4f8147.

Checks:170 focused presentation tests pass. Typed review runtime validates real
facings/atlas rectangles, named body and six portraits,32world-pixel head lift,
slow walk, generic-hermit separation and bark stop/resume. Frame/palette/source/
receipt/bound-review hash and offline rebuild checks pass. Native software Vulkan
four-party peaks **343.70MiB Ashen Ford /192.37MiB Cinder Hollow**, below512MiB;
strict memory checker covers open/crossing/party-change/restored and all8human/
elf class texture uploads/releases. These are memory results, not GPU timings.
Host/game code unchanged; prior installed checkpoint builds passed. New standalone
runtime verifier compiles and passes. Existing native host reused for these runs.

Nine new image-tool calls: eight directional6×3 sheets and one portrait sheet.
11 total including two original appearance calls. Raw exports/exact requests/
input-output hashes retained; no completed art regenerated. No PixelLab or new
speech calls, credit purchases or upgrades. Image-tool pricing unavailable; no
monetary estimate inferred. Next: owner finished-set/TV approval, then install
exact reviewed art and verify production; do not infer approval from appearance
or previous voice approval. Rebase before pushing townsfolk-art. No PR, merge,
issue closure or environment publication.


## Latest checkpoint — Old Maud approved and installed

Owner approved the finished set: “I approve”. Installed the hash-verified eight-direction animations and six portraits with named-only hermit art aliases; approved voice remains in place. Previous pending approval notes are historical. Rebase before pushing townsfolk-art; no PR, merge or publication.


## Latest checkpoint — Bree and Halden portraits installed

Owner request: “Fix Bree and Halden's portraits”. Added six matching 40×40 moods
for each from their approved concepts and installed sprites; preset and named
portrait aliases now use the new sets. No separate visual approval is claimed.
All twelve named NPCs now have replacement bodies and portrait sets installed.
The seven generic presets (two variants each) remain to finish.
See `tools/tactics-assets/imagegen/townsfolk/bree-halden-portraits/README.md` for
sources, reproducible normalization, native TV review and validation limitations.
81 HUD art and 14 host contract tests pass; host build has zero warnings/errors.
Full presentation suite: 2,536 pass, three failures reproduced against the original
portrait manifest (Dagna filename, cast facing, story marker geometry).
