# Meridian tile reference

`tiles.json` is copied byte-for-byte from unmerged PR #316, head `ff3175af859733bf8b02c0ff2afa43d0113e1c66`, path `content/meridian/base/rules/tiles.json`. It supplies 48 unique IDs representing 50 physical tiles and 57 deck variants for art #332 while the rules are off main. It is not installed as a runtime rules pack.

Before integration, fetch the rules PR/main and audit against the actual current file using `python tools/meridian-assets/audit_compartments.py --rules <path>`. The audit defaults to live checked-out rules if available. It fails on missing/extra IDs, deck variants, changed hatches/symbols/rules, missing files or incorrect hashes/frame dimensions. A new rules ID requires a deliberately authored composition; there is no generic fallback.

## Crew reference

`crew.json` is copied byte-for-byte from unmerged PR #316, head `1d770a7312e28d99c62fcfb7ebcd5bf398a758e9`, path `content/meridian/base/rules/crew.json`. It supplies all eight crew IDs, exact names, roles and trait tracks for art #333. It is not installed as a runtime rules pack. Recheck current content with `python tools/meridian-assets/audit_crew.py --rules <live crew.json>` before integration. Missing/extra IDs, missing facings/poses/portraits, name/role drift, dimensions or hash corruption fail the audit.
