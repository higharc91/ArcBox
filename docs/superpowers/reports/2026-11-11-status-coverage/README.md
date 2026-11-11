# #184: every condition has overhead art

All 46 current rules conditions have an explicit icon decision in `status/manifest.json` (`conditionIcons`, with reasons). Dedicated static silhouettes cover grappling, dodging, disengaging, help, taunting, oil, reckless attacks, ensnaring and sleep. Other conditions share documented existing silhouettes; full names remain on phone and unit cards. Unknown conditions receive an exclamation diamond. The existing four-icon overhead limit remains, with identical icons deduplicated; all conditions remain available in the unit card.

The cross-check requires exact equality between rules IDs and manifest decisions, a valid status-atlas glyph for every decision, a reason, matching generator policy, and distinct nonempty new glyphs. Generic fallback is reserved for unknown future conditions; a missing policy entry still fails the gate.

All existing atlas cells are byte-identical. Existing overlays, sprites, ability artwork, voices, and animation/reduced-motion behavior are preserved. `python3 tools/tactics-assets/build.py status-icons` refreshes only this atlas; the normal status generator retains the policy.

Focused validation: 61 UnitArt tests, nine manifest cross-checks, zero build warnings. Native Godot 1920×1080 review uses `--tactics-demo=forest_ruins:statuses`; [native](native.png), actual Chromium [deuteranopia](deuteranopia.png) and [protanopia](protanopia.png) screenshots were visually inspected. This is automated desktop review, not a human usability or physical-TV test.

The complete gate remains blocked by main's ten missing story recordings, as recorded in PR #301. This feature must remain unmerged until the full check passes.
