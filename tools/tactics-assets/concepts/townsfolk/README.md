# Ashen Ford people — issue #80

Started on 2026-11-03 from main `2e64b9df`, on `townsfolk-art`.

The current region places **12 named NPCs** (including Rook) and **seven generic
townsfolk presets**: barmaid, child, farmer, farmhand, fisher, guard and merchant.
`briefs.json` reuses the existing named-character briefs and adds one brief with
two proposed variants for each generic preset. Mother Skarrow is included as a
named character; ordinary goblin presets retain their current creature art.

`node tools/tactics-assets/concepts/townsfolk/prepare.cjs` rebuilds this queue from
the placed people, refusing an unknown named brief or unauthored generic preset.
The source brief pack and the lore remain authoritative. Appearance choices are
proposals. No old NPC sheet has been replaced.

## First anchor: Bree Tallow

![Concept beside the current sprite and a shipped hero](bree-review.jpg)

- Full-resolution output: [bree-concept.png](bree-concept.png).
- Exact input: [bree-request.json](bree-request.json).
- Job, asset and cost receipt: [bree-result.json](bree-result.json).
- **Scenario cost: 12 CU quoted and 12 CU charged**, authorized by the owner.
- Model: Nano Banana 2.1, 2K, one output. The live schema has no seed parameter.
- **Concept approved on 2026-11-03:** “yes, her design looks correct.” See
  [approvals.json](approvals.json). No PixelLab jobs have been submitted.

The anchor's front, three-quarter and rear views preserve her braided grey hair,
practical apron, boots and closed personal notebook. The rear has real apron
ties rather than a front buckle. The owner approved these design choices and
the sheet can now serve as the rendering-style reference for the remaining concepts.

`make-review.ps1` crops the first idle frame from the current production atlases
and builds the comparison. It reads frame sizes, foot anchors and tactical pixel
scale from the manifests. Both shipped sprites share a foot baseline and the
same 6x world enlargement. The concept is a painting; it is not evidence of a
finished pixel sprite's height or animation.

## Next steps and measurements

1. Bree's proposed design and series rendering style are approved.
2. The remaining 11 named sheets and seven paired townsfolk sheets are prepared
   in [batch-requests.json](batch-requests.json), using Bree's actual Scenario asset
   as the style reference. Every exact request was priced in a dry run:
   **18 sheets × 13 CU = 234 additional CU**, or **246 CU including Bree**.
   [batch-prices.json](batch-prices.json) records all 18 quotes. The dry runs spent
   no credits and submitted no generation jobs. The owner approved the full
   234 CU batch: “Approve the full 234 CU batch.” See `approvals.json`.
3. Owner approves concepts per character/preset, then PixelLab trials begin.
4. Review idle, walk and named gesture motion beside the party on the real TV.
5. Integrate only approved sheets/portraits and optional placement variants.

`prepare-batch.cjs` refuses to build requests until the anchor approval is
recorded. `prepare.cjs` preserves approvals when rebuilding the queue and leaves
the submitted anchor's original request intact. The reference instructions copy
Bree's rendering language, while preserving each brief's own identity, species,
proportions and clothing. Approval of Bree does not approve the other designs or
their pixel animation.

## Partial batch: Scenario cooldown

![Mother Skarrow beside the current goblin shaman and a shipped hero](mother-skarrow-review.jpg)

Mother Skarrow completed successfully for **13 CU** using Bree's approved sheet
as its reference. Her front, three-quarter and rear views retain the staff, repaired
hide/wool, food pouches and grey hair. Her design is **pending owner approval**;
the eventual goblin sprite must retain its own species proportions and world scale.
The concept alone does not establish either.

The concurrent Mother Aubrine submission was rejected with HTTP 429:
“Too Many Requests. Please, wait 942 seconds before trying again.” It returned
no job ID and incurred no charge. Generation stopped without retries; there are
no outstanding jobs. The remaining **17 sheets** are not generated.

- This batch: **13 CU spent of 234 CU authorized**, leaving **221 CU authorized**.
- Including Bree: **25 CU spent**.
- `generated/` holds Mother Skarrow's PNG, job receipt and result receipt.
- [batch-progress.json](batch-progress.json) records every pending ID, the provider
  cooldown refusal and the remaining budget. Resume only those IDs after the
  cooldown; do not regenerate completed sheets or resubmit while throttled.

The completed cards were visually inspected, the queue generators ran successfully,
and `git diff --check` passed. Game tests and runtime memory checks have not run:
this checkpoint contains concepts, comparison tooling and documentation only.

The owner set **512 MB maximum TV texture memory at region open** in issue #80's
follow-up comment. Full-party Ashen Ford must fail the work if it exceeds that
limit. Measure Ashen Ford and Cinder Hollow, cold pack download size, and used-only
loading before and after integration. The old board's 350 MB and 179 MB figures
are historical, not a fresh measurement of this main revision. A `--world-demo`
`--perf` check remains to be implemented with the loading work. No new memory,
download-size or game test results are claimed by this concept-only preparation.
