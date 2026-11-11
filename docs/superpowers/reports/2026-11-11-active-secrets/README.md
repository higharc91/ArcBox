# #152: looking for secrets

Five of Ashen Ford's eight hidden things (62.5%) and Cinder Hollow's one (100%) now require active discovery. `searchOnly` excludes passive detection even with arbitrarily high scores; Search and Keep watch remain eligible. The active market clue and its cache are both protected, so an incidental passive clue cannot bypass the requirement. A regression checks that no passive notice chain reveals any active-only secret. Searches also work when standing directly on an active-only cache. Selected finds use range one and DCs 12–16.

[The executable audit](default-party-audit.json) uses the real rules, default human fighter/cleric/rogue/wizard sheets and detection code at levels 1, 2 and 3. At each level, passives can find the cellar draft/hatch and goat tracks when eligible and in range; the other five Ashen Ford finds and Hollow cache require action. Each row states its flags, skill, DC, score and range. This is an eligible/in-range opportunity audit, not a claim that a party walked the entire world.

Decision 4: only connected members contribute passive scores; connection changes invalidate the cache. A disconnect/reconnect integration regression covers an expert who would otherwise reveal a rune. Other scene-check rules remain intact.

Decision 6: a shrine mends one existing minor (`mends: rest`) injury on its invoking member, once per shrine per campaign. Lasting injuries remain, and persisted `used` state prevents repeats after restart. Using it uninjured spends it. Wells and altars retain their existing words/flags. The trapped pier crate now contains one healing potion and 15 gold. No voices or audio change.

Focused checks: 31 hidden-thing logic tests, 11 server discovery/search tests, 33 interaction tests, six shipped-campfire watch tests; zero skips and zero build warnings. Eight actual Chromium fixture/viewport checks (Search, walking/interactions and camp jobs at 320×844 and 390×844, reduced motion) report zero text-size, target-size, contrast or overflow failures. The strict full gate is also run; main's 13 missing recordings (ten lore lines plus [three encounter lines](missing-main-encounter-voices.json)) remain a merge blocker until resolved.

Reproduce:

```sh
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --secret-audit --out /tmp/secrets.json
```
