# Matching kobold crew

Both shipped kobold roles now use the Grisk-based Scenario concepts and matching PixelLab sprites. Every role has eight real facings for idle, walk, attack, hurt and death; the slinger also has its own throw. All physical combat statistics are preserved. The foreman stays larger and keeps his helmet and pick.

The turn order and story portrait moods now use matching head crops instead of the older presets. Portrait-resolution tests cover both roles. The slinger fires stone projectiles with a sling release; its fire-pot presentation selects the dedicated throw clip through the cast cue.

The actual TV demo ran through the cave and both battles to completion. `diggings.png` and `turn-order.png` show the refreshed models and portraits. Original pixel sources and paid receipts are retained under `tools/tactics-assets/pixellab/kobold-refresh`; the build rejects missing directions and clipping. The template's malformed west-facing hurt hood was replaced with the reviewed custom clip pictured in `slinger-hurt-review.png`.

The native preview retains the previously documented shutdown ObjectDB warning in this container.

Validation: presentation build and host build passed with zero warnings/errors. Final presentation suite: **2,450 passed, zero failed or skipped** (3m 01s). Atlas and portrait rebuilds were byte-identical. `git diff --check` passed.
