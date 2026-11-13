# Project trackers and evidence

Use [GitHub issues](https://github.com/higharc91/ArcBox/issues) for live task state and merged PRs for completion evidence. A dated plan or report records what was known at that checkpoint; it does not reopen a closed issue or authorize another merge.

| Need | Current entry point |
|---|---|
| Clone, host, test and play Wardenflame | [Developer guide](../../games/Tactics/DEVELOPING.md) |
| Overworld backlog and dated review evidence | [Overworld board](plans/overworld-issue-board.md); confirm status on GitHub |
| Current game behavior | [Overworld spec](specs/2026-10-31-tactics-overworld-design.md), [shops/gold](specs/2026-11-02-tactics-shops-and-gold-design.md), [rest/recovery](specs/2026-11-03-tactics-rest-and-recovery-design.md) |
| Repository validation | [Full checker](../../tools/check-all.sh); GitHub Actions is intentionally disabled for the current queue |
| Asset rebuild blocker | [#203](https://github.com/higharc91/ArcBox/issues/203), [draft PR #230](https://github.com/higharc91/ArcBox/pull/230) |
| Remaining voiced camp talks | [#151](https://github.com/higharc91/ArcBox/issues/151); Keep watch merged in #236 |
| In-session leveling and Results wording | [#146](https://github.com/higharc91/ArcBox/issues/146); the Results label lives in the currently protected `screens.tsx` |
| REDLINE hardware/audio acceptance | [#24](https://github.com/higharc91/ArcBox/issues/24); interruption fix merged in #229 |
| Deferred engine findings and owner questions | [Historical progress audit](plans/2026-10-30-tactics-rules-engine.progress.md); its “Decisions for the user” section is retained unchanged |
| Derelict: ISV Meridian (new game, not started) | [Design spec](specs/2026-11-13-meridian-design.md), [M1 rules-core plan](plans/2026-11-13-meridian-m1-rules-core.md) |
| Platform architecture | [Platform spec](specs/2026-10-18-arcbox-platform-design.md); implementation status belongs on GitHub |
| Screenshots and measurements | [Reports](reports/); evidence is specific to the stated build, machine and date |

Every Markdown file under `plans/` carries a first-line status. Older plans, handoffs and reports retain their historical task lists and decisions; use this index and GitHub to choose current work. Follow the current user’s task authorization and file restrictions, rather than old session instructions embedded in a handoff.

## Check drift and generate current counts

```sh
python3 tools/docs-drift.py
bash tools/check-all.sh --full > /tmp/arcbox-checks.log 2>&1
python3 tools/repo-metrics.py --checks-log /tmp/arcbox-checks.log
```

The drift check also runs in the full/fast checker’s Python group. It guards known stale statements, plan status lines, the tracker index, closed OW-36 and the dead shop flag. It deliberately does not claim that the protected Results wording is fixed; that remaining work is tracked in #146/#204.

Metrics are generated from tracked base content, current build outputs and the supplied successful full-check log. Without `--checks-log`, the script reports source-file and byte counts only. Built phone text includes every HTML/JS/CSS entry, so it is not a first-load transfer measurement. Historical budget/benchmark tables remain labeled as snapshots; rerun their stated commands for new measurements.
