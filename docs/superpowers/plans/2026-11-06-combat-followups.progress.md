> Status: in progress; combat follow-ups #239, #240 and #241 on `wardenflame-followups-239-241`, based on merged main `057eecf3`. CI stays disabled.

# Combat follow-ups

Implement #241 first because resource-spending reactions invalidate plans and affect the later measurements. Then measure/tune the ranged and rapier rogue for #239 and spell usage for #240. Keep each issue's changes in separate commits on this isolated branch. Other workers may own progression, camp content and the asset pipeline; do not edit their worktrees or main checkout.

- #241: reproduce a real Hellish Rebuke during a planned walk in an adapter test, allow a bounded fresh plan from remaining economy, and retain the exception watchdog. Document simulator refusal causes and recovery rather than hide refusals.
- #239: measure AI/scripted shortbows at levels 1–10 using the target-normalized measure; keep rapier within ±15%, including its inherited +18.12% level-4 exception. Preserve established Sneak Attack caps, monotonic primary dice and deterministic rules.
- #240: evaluate whole-turn denial for Command and emergency touch healing for ranger Cure Wounds; meaningful scenarios before tuning. Measure both against the 2% legal-turn threshold, or document an actual situational limitation.
- Final verification: twenty-seed results with formation rotation, no performance tests alongside simulation; full local check-all before delivery. Keep GitHub CI disabled. No main merge or issue closure without the owner's instruction for this new work.

The previous audit is retained as baseline evidence: rules hash `a52dd6d512ec1c1b148e6888d2e2e072c4788b0cc1a9855716df7105156a47a4`, 18,000 default benchmark battles at seeds 1–20, default rogue L4 +18.12%, Command 51/9,270 (0.55%), ranger Cure Wounds 520/26,881 (1.93%). The earlier shortbow figures used the old raw measure and must be remeasured.

- #241 RED: both new adapter regressions fail against main; a real walk triggers OA/Hellish Rebuke, consumes the last Pact, then the stale Hex forfeits the Action. GREEN: 37 adapter/reaction/watchdog tests pass after one bounded replan per turn, plus 13 simulator/report tests. Engine exceptions retain their existing watchdog; simulator refusals remain real counts and now include ability/rejection reason for the final report.
