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
