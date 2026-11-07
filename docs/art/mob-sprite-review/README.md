# Wardenflame mob sprite baseline

Branch: `mob-sprite-refresh`, based on main `50f4a0d7` (after PR #249). #186 was verified closed.

- [Single contact sheet](all-mobs.png): 98 numbered front/rear idle looks.
- [Standalone searchable review](index.html): all art embedded, no server needed; filters for 41 combat looks, 57 additional creature-library variants, and 28 older combat looks. Click a card to enlarge it or search stat-block names/IDs.
- [Inventory](inventory.json): 72 stat blocks plus every variant in the beasts, bosses and bestiary manifests. Sprite resolution uses the production `UnitArt` loader, including the newer dedicated-atlas overrides; obsolete fallback sheets are not substituted.
- [Card metadata](cards.json): stable numbers, sprite keys, file paths and the stat blocks sharing each appearance. Ordinary non-combat townsfolk/fauna and player outfits are outside this mob inventory; combat companions and wildshape forms are included.

The sprites are enlarged individually to make their details readable. This is **not a world-scale comparison**. "Older atlas" describes the existing two-direction asset pipeline, not an automatic rejection of the design. The same art used by multiple stat blocks is shown once with its aliases. Named characters currently sharing a generic enemy's art are identified together.

Validation: all 98 sprite keys resolve; all 72 stat-block IDs appear in the catalog; both facings of every sprite visibly render; all frame rectangles fit their source atlas. Browser review verified 98/41/28 filters, search, enlarged-card dialog, whole-sheet mode and a 390 px phone viewport without horizontal overflow.

Regenerate with .NET 10, Pillow, Node, and Playwright/Chromium installed:

```sh
python tools/tactics-assets/dev/review_mob_sprites.py
```

The Python tool calls `tools/tactics-mob-review` to resolve the production sprite metadata, inspects alpha bounds, then renders the unchanged source atlases with Chromium. It does not redraw, recolour or resample source artwork. No game files are changed by this baseline review.

## First batch integrated

The original 98-card sheet and inventory above are the pre-refresh baseline. [Current first-batch animation review](production/index.html) shows the integrated goblin cutthroat, orc brute and red drake, including all eight facings, world-scale loops and real renderer screenshots. These replace two of the original 28 older combat looks plus the dedicated red drake; 26 older combat looks remain. Other goblin roles and drake color variants retain their own designs until their pass. [Tracker](production/refresh-status.json).

## Current refresh progress

[Second batch: goblin archer, shaman and Vorga](goblin-roles/index.html) includes before/after art, eight-direction animation controls and actual game captures. Six looks are refreshed across both batches; 23 older combat looks remain. The 98-look baseline above remains a historical reference.
