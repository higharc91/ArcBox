# Art merge-gate repairs

This separate fix keeps shipped gameplay, balance, recordings and authored copy intact while addressing the five failures blocking Meridian's art PRs.

- `LoreBook` now retains the existing `sceneFlags` declarations from arcs.json, matching the Python lore checker. The strict scene-state test accepts only beat/choice flags or these explicit declarations. A negative regression injects an undeclared flag and must reject it; malformed declarations are rejected by the reader.
- Narration has no portrait, so it can use 72 conservative columns in the existing dialogue panel instead of the 64 reserved for speakers. The 30px font, three-row limit, panel geometry and recorded wording remain unchanged. The Ashmaw narration fits three rows without losing text; speakers retain their 64-column budget.
- Two Grisk expectations match the unchanged authored straight apostrophes. Assertions still require one exact cosmetic line, unchanged original events and deterministic output.
- The bounty phone golden updates only narrative text to current main's resolved story and apostrophes. Gold, XP, class-specific trinket data and all other fields remain unchanged.

Focused tests pass: BaseScene 12, StoryScreen 58 and Grisk; fixture verification, full gate and native font/layout proof are being completed before merge. Tests are neither skipped nor weakened; CI remains manually disabled.

The first full run exposed a pre-existing storage-test race at the final recovery assertion: the write counter increments before the recovery flag is cleared. That superseded run was stopped after diagnosis; its output is retained. The test now waits for both recovery signals before asserting, with the same deadline, retry bounds, exact write count and recovery assertion. Production storage code is unchanged. A complete full gate is restarted after focused storage verification; no final test is skipped.
