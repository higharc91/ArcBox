# Ten-issue batch review

Selected scope: #25 (REDLINE narration pacing), #95 (companion names), #150 (recovery matrix), #127 (monster/form roster), #131 and #144 (campaign experience), #191 (asset aliases/deduplication), #126 (equipment progression), #208 (private backgrounds) and #124 (build choices).

PRs 305–312 retain feature scopes. The final build-choice branch includes integration corrections: canonical creature keys retain existing sound/pose behaviour; +1 weapons reuse their base weapon sounds; cumulative technique defaults cover every level; forge/loot/XP expectations match authored content. An inline monster phase change no longer delays the remaining area victims, satisfying the unchanged real-battle damage timing limit. This is an event-timing correction; no owner-reviewed VFX or sprite-generation workflow was run and no art/audio pixels or recordings were generated.

Warg uses the existing wolf sound family. The six other newly exposed bestiary species have no authored voice/call and receive explicit silent decisions under the repository's existing sound policy. Their existing art is retained; no unrelated voices are substituted. Missing story recordings remain failures and are not waived.

Reviewed fixture changes are retained in fixture-review.json. Walking/checking phones gain their own unlocked private background panel; personal choices gain credit/permissions; portal fixtures gain build options/public hooks and revised equipment wording; over-level roaming rewards become zero. The already-stale trinket journal fixture is refreshed to the actual authored quest result. No test limit is raised and no test is skipped.

Native screenshots are under ../2026-11-12-native-review/. Phone report directories retain Chromium at 320×844 and 390×844. REDLINE's separate report includes a full native 15-round race with real phone bots. Main was re-fetched at cf156c468557266db94ede7ee388f2f5cff41469. The primary workspace's longstanding untracked WorldRain.cs.uid remains unchanged (SHA256 c40ea11e95bbea69cb1c7f9dfe4208fd4369aab568ad0c07751405b9215881b1).

The first integration gate found regressions that are corrected in this branch. A second mandatory full gate is running; no merges or issue closures are claimed. Main already has missing story recordings and other content failures, so draft PRs must remain unmerged until the complete gate passes.
