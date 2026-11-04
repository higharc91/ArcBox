# OW-05: used-only world textures and four-party memory

Continued `townsfolk-art` from `0eacebec`. No production art, rules, balance or phone contracts changed. The complete Bree review remains gated by the owner's full-set/TV approval; no PixelLab or Scenario credits were spent. PixelLab tools are not connected in this cloud session, so 30.75 generations remains a historical balance, not a fresh account check.

## Loading changes

World preload now resolves each placed person's actual `UnitArt` sprite path. Previously it preloaded the legacy filename even when rendering used a different creature/review atlas. World units request only the current region's people and the figures actually drawn by the exploration/battle boards. Their uploads use the existing 8 ms texture budget (always allowing one image), including on roster changes. Drawing a unit no longer performs a synchronous image decode. An individual image can itself exceed the frame budget; this change does not claim to make PNG decoding preemptible.

The actual party stays resident across hidden/form-up/battle frames, so presentation ownership changes do not repeatedly decode the same hero. A shared request queue deduplicates sheets, cancels stale queued work on changes, and releases sheets no longer requested by any board or the region. World-stage battle requests are cleared when hidden; region travel frees the old world's cache. Static terrain/UI dependencies remain pinned so dropping an actor cannot dispose a texture still used by world geometry. Plain battle boards retain their existing loader.

Fauna now share one texture per source image: Ashen Ford's 11 animals use seven textures. Each Sprite2D still chooses its own animation frame and facing. Fauna navigation/domain preparation runs in the background world build, and native creation/upload happens incrementally inside the texture budget. Decoded images and native textures are explicitly disposed. Standalone ambient fauna also drain their own budget rather than waiting for a WorldCanvas that does not own them. Movement profiles, scales and source pixels are unchanged.

## Reproducible memory gate

`--world-demo=<region> --perf` draws fighter/cleric/rogue/wizard human heroes, walks the actual region, replaces all four with elf equivalents, then restores the original party. It prints exact current/peak bytes at `open`, `crossing`, `party-change` and `party-restored`. Completed failures exit nonzero. The agreed cap is **512 MiB = 536,870,912 bytes**.

`node tools/tactics-playtest/world-memory.cjs <log>` additionally rejects incomplete/time-limited runs, absent measurements, a smaller party, changed limits, missing hero uploads, missing releases, and Godot errors. Boundary tests include a claimed PASS one byte above the cap. The native monitor can include old resources retiring a frame later, so roster-swap peaks are reported rather than hidden.

Cinder Hollow with `--perf` selects this whole-region benchmark; omit `--perf` for the existing scripted bounty/creature/battle tour. The region-open benchmark has four heroes and the region's placed dressing, not an active encounter. The scripted creature/travel check and four-phone room check are recorded separately below.

## Before and after

Godot **4.7.2 .NET**, Debug, Linux Xvfb, Mesa llvmpipe Vulkan **Mobile**, low preset, 1280×720, Dummy audio, no screenshots, identical production pack. The baseline contains only the four-party measurement instrumentation; texture loading was still the checkpoint implementation. Both routes completed. These are software-rendered memory measurements, **not owner-GPU frame-time results**.

| Measurement | Ashen Ford before | Ashen Ford after | Cinder Hollow before | Cinder Hollow after |
|---|---:|---:|---:|---:|
| Region open, four heroes | 269.62 MiB | 264.91 MiB | 154.87 MiB | 154.87 MiB |
| End of region walk | 269.79 MiB | 265.08 MiB | 154.87 MiB | 154.87 MiB |
| Peak including new roster/reload | Not exercised | 302.58 MiB | Not exercised | 192.37 MiB |
| Gate | PASS | PASS | PASS | PASS |

Ashen Ford saves **4.71 MiB** at open; no quality, density or animation frames were reduced. Cinder Hollow has no placed people/fauna to deduplicate at open, so it is unchanged. The separate roster tests verify eight actual uploads and releases in both regions. Software-renderer/JIT/cache variation and the relocation of fauna planning into the background build prevent a clean opening-speed comparison; no speedup or 16 ms frame claim is made. The raw logs retain all frame measurements.

The **separate Bree review pack** was also measured with all four heroes: **267.56 MiB** at open, **267.74 MiB** at walk end and **305.24 MiB** at the roster-swap peak. All stages and uploads/releases pass. Its log loads `creatures/review_bree.png` (1536×768) and never the old innkeeper sheet, confirming that preload follows the actual approved-source review mapping. This is a preview-only memory check, not owner approval or a production install. [Review-pack log](bree-review-ashen.txt), [gate receipt](bree-review-ashen.json).

Receipts: [before Ashen Ford](before-ashen.txt), [after](after-ashen.txt), [before Cinder Hollow](before-hollow.txt), [after](after-hollow.txt), and verifier outputs [Ashen Ford](after-ashen.json) / [Cinder Hollow](after-hollow.json).

The production cold pack remains **200,832,616 bytes (191.53 MiB), 4,812 files**, with identical before/after inventory hash `e959665d9de30325c2a703a81c8e797935c8686cce3bce3b31cd97a822838636`. This is the sum of raw file bodies the cold content cache downloads, excluding index/HTTP overhead; it is not a compressed archive estimate. [Inventory receipt](cold-pack.json) follows `ContentPackRegistry`'s sorted path/file-hash algorithm. Loading fewer textures does not reduce the current whole-pack download protocol. Review sources, frames and tooling remain outside production content.

Example Linux command (the local Mesa ICD was extracted into this workspace, not installed or published globally):

```bash
source /workspace/.arcbox/activate.sh
# Start a private Xvfb display first; use its number below.
DISPLAY=:188 VK_ICD_FILENAMES=/workspace/.arcbox/vulkan/lvp-local.json \
  godot --path src/ArcBox.Host --rendering-driver vulkan --rendering-method mobile \
  --audio-driver Dummy --position 160,100 -- \
  --world-demo=ashen_ford --perf --res=1280x720 --preset=low --quit-after=150 > ashen-memory.log 2>&1
node tools/tactics-playtest/world-memory.cjs ashen-memory.log
```

Repeat with `cinder_hollow`. Stop the display after the process exits. Windows can use its normal renderer and 1920×1080; preserve the handoff's left-monitor/no-focus procedure and report fresh numbers rather than treating cloud results as hardware certification.

## Validation

- Initial focused C# queue/budget checks: **5 passed** (deduplication, cancellation/re-addition, full-party real atlases/bars, exact binary cap and invalid measurements).
- Memory-log verifier: **4 passed**, including CRLF, excessive memory, incomplete logs, wrong party size, missing uploads/releases and engine errors.
- Offline Bree approval/crop/import/height and complete atlas/contact/frame verification: **passed**, rebuilt in `/workspace/artifacts/issue80-bree-review` from committed frames. Approved southeast pixels remain exact; no art or motion source was regenerated.
- Four-phone live walk: **OK**. Default authored routing, saved-position server restart, cold TV reconnect and dropped-leader checks pass. 20/20 latency presses: median **81 ms**, p90 145 ms, worst 165 ms, against unchanged budgets. Actual live-room open measured **308.6 MiB**; the cold TV reopening measured **304.6 MiB**. Renderer was software Vulkan Mobile, 640×360, no shots. Frame timing is not a hardware benchmark.
- Final Debug and Release solution builds: **0 warnings, 0 errors** in both configurations.
- Cinder Hollow scripted tour: **completed without Godot errors**. Four heroes, Grisk and Ashmaw, formation/battle transitions, and return travel were exercised. Each party hero uploaded **once** within the cave across both fights; unused Grisk/worker/slinger sheets were released. The cave opened at **197.3 MiB** through the normal UI, and the return to Ashen Ford at **345.4 MiB**. [Tour log](hollow-tour.txt), [Ashmaw screenshot](hollow-ashmaw.png), [return screenshot](hollow-return.png). Screenshots were separate from the timed memory runs.
- Phone suite: **508 passed**; production phone build succeeded. Unchanged web files emitted localhost:3000 asset-fetch/Happy DOM teardown diagnostics during tests, and the build retains the existing **537.65 kB REDLINE flyover chunk** warning. No phone code, test, or warning threshold was changed.
- All bot-tool tests: **16 passed**.
- The first full solution attempt exposed a pre-existing export-test ordering assumption: an unsorted `Directory.GetFiles` result was compared with a sorted actual list. The assertion now sorts both expected and actual using ordinal ordering, preserving the exact same file-count/name and no-temporary-file requirements. Export behavior and authored data were not changed. The focused regression passes, and both full Debug/Release builds remain at zero warnings/errors.
- Full .NET solution run: **6,253 tests**, **6,252 passed / 1 failed / 0 skipped** in 19m 16s. The sole failure was the already-recorded AI stress wall-clock fluctuation: p95 **384 ms** against its unchanged **250 ms** limit. All other modules passed, including the complete **2,455 presentation tests** and the corrected export test. [Full-run log](solution-tests.txt).
- Subsequent isolated **entire logic suite: 1,258 passed / 0 failed / 0 skipped**, including every unchanged AI timing case, in 2m 09s. No AI/rules code, test budget, case or parallelization setting was changed. Every module has a passing final result across these runs; the full-run timing failure is retained here rather than described as an uninterrupted green run. [Logic rerun](logic-isolated-tests.txt), [export regression](export-ordering-test.txt).

Re-run these gates after the final approved cast/portraits are integrated: the current pass certifies the shipped pack and separately tested review assets, not the ungenerated cast.

The rest of issue #80 remains open: full Bree directional/TV approval, the other 25 sprites, named portraits, optional variant editing/export, production installation and talk/bark presentation wiring. None is silently treated as approved or complete by this performance work. Query live PixelLab schemas/costs/balance before any paid continuation; never buy credits automatically.
