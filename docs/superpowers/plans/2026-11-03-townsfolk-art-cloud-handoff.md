# Issue #80 — cloud handoff after Bree

Work in `higharc91/ArcBox`, branch **`townsfolk-art`**. Fetch and check out that
branch; inspect a dirty tree before changing it. Pull with rebase before pushing.
Do not merge, open a PR, close #80 or reset unrelated work. The owner requested a
checkpoint after Bree so a cloud AI can continue. Production art is unchanged.

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
- The **new complete directional set and TV shots still need owner review** before
  production installation. The earlier GIF approval alone does not approve all new frames.
- Other **25 sprites** (11 named + 14 generic variants), portraits and production
  integration remain. Legacy goblin presets remain out of scope; named Mother
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
uses them; there is no production install script yet.

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

1. Review the committed full-set GIFs/TV shots with the owner. Until full-set/TV
   approval arrives, continue independent **OW-05 loading/perf work** rather than
   installing or expanding unapproved pixel art. Do not redo approved concepts.
2. After approval, finish the named/generic sprite pipeline from the existing
   designs, maintaining one art style and complete real directions. Keep prompts,
   source frames, hashes, curation, exact receipts and costs. Resolve incomplete
   generic concept angles during directional QA; don't invent anatomy from legacy kits.
3. Produce named portraits from approved concepts in the existing portrait frame.
   Add an optional placed-person variant that round-trips through the Godot editor.
   Keep old sheets available until the approved swap lands.
4. Integrate approved assets. Named `talk` clips are currently authored but **not
   wired to ordinary world barks**: `WorldPeople` still selects idle/walk. Add that
   presentation wiring with idle fallback and meaningful stop/resume/contact tests.
   Do not change game rules, balance or server-to-phone contracts.
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


## Cloud performance continuation after the Bree checkpoint

OW-05 now has used-only, budgeted world-unit loading, explicit release on roster changes, shared fauna textures, and background fauna navigation preparation. Actual party sheets stay resident across hidden/formation/battle frames. The production assets and all approval/credit records remain unchanged.

The new four-party `--world-demo=<region> --perf` check covers region open/walk, an all-four race change and restoration, with a **512 MiB** cap and a strict log verifier (`tools/tactics-playtest/world-memory.cjs`). Ashen Ford opens at **264.91 MiB**, Cinder Hollow at **154.87 MiB**; swap peaks are **302.58 / 192.37 MiB** on Linux software Vulkan. The separate Bree review pack opens at **267.56 MiB** with four heroes and passes too. The unchanged cold production pack is **200,832,616 bytes**. Detailed logs, native tour screenshots, limits and final checks are in [the loading report](../reports/2026-11-03-townsfolk-art/loading/report.md).

Continue from the current branch rather than rebuilding these changes. Full-set Bree/TV owner approval remains pending. No further sprites/portraits were generated and nothing was installed in production. PixelLab MCP tools were not exposed in this cloud session, so no fresh balance or quote was obtained and **no credits were spent**. Check live schemas/costs/balance when that connection becomes available; 30.75 remains historical.

On cloud, the offline review pack was rebuilt at `/workspace/artifacts/issue80-bree-review` using an explicit output argument to `build-review-pack.cjs`, and the existing pixel/atlas verifiers pass. Do not edit linked content files. Future optional variant integration must update the region preload selector and WorldPeople's sprite selection together, and rerun all memory gates after the final approved assets land. Keep the existing art approval gates; current memory results do not approve the remaining cast.
