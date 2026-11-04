# Issue #80 — cloud handoff after Bree

Work in `higharc91/ArcBox`, branch **`townsfolk-art`**. Fetch and check out that
branch; inspect a dirty tree before changing it. Pull with rebase before pushing.
Do not merge, open a PR, close #80 or reset unrelated work. The owner requested a
checkpoint after Bree so a cloud AI can continue. **Current continuation:** Bree’s
full-set/TV approval arrived and her exact compact atlas is now installed. Halden’s
complete eight-direction idle/walk/talk set is also now approved and installed.
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
