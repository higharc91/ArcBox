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
- **Design approval pending.** No PixelLab jobs have been submitted.

The anchor's front, three-quarter and rear views preserve her braided grey hair,
practical apron, boots and closed personal notebook. The rear has real apron
ties rather than a front buckle. These age, hair and clothing choices need the
owner's approval before becoming the reference for the remaining sheets.

`make-review.ps1` crops the first idle frame from the current production atlases
and builds the comparison. It reads frame sizes, foot anchors and tactical pixel
scale from the manifests. Both shipped sprites share a foot baseline and the
same 6x world enlargement. The concept is a painting; it is not evidence of a
finished pixel sprite's height or animation.

## Next steps and measurements

1. Owner reviews Bree's proposed design and the series rendering style.
2. Reuse the approved anchor as the style reference for the remaining named
   sheets and the seven paired townsfolk sheets; price the actual requests first.
3. Owner approves concepts per character/preset, then PixelLab trials begin.
4. Review idle, walk and named gesture motion beside the party on the real TV.
5. Integrate only approved sheets/portraits and optional placement variants.

The owner set **512 MB maximum TV texture memory at region open** in issue #80's
follow-up comment. Full-party Ashen Ford must fail the work if it exceeds that
limit. Measure Ashen Ford and Cinder Hollow, cold pack download size, and used-only
loading before and after integration. The old board's 350 MB and 179 MB figures
are historical, not a fresh measurement of this main revision. A `--world-demo`
`--perf` check remains to be implemented with the loading work. No new memory,
download-size or game test results are claimed by this concept-only preparation.
