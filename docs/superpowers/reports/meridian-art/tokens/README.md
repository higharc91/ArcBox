# Meridian monsters and scenario tokens — #336

Original arcpix/Pillow art, no generation spend. All 18 canonical kinds from the twelve scenarios at game PR #316 head `6defbead1d71ae7abf8ad8b2a09fc9be97b0b4f0` are represented. This follows the final-ID correction in issue #336 comment 7043880902, rather than the superseded working list. Source snapshots and hashes live in `tools/meridian-assets/reference/tokens/`.

## Review

Open [the gallery](index.html). [Creature studies](creatures-detail.png) are enlarged 6× studies, explicitly labelled; [public pieces](tokens-tv.png), [monster states](monsters-tv.png) and [repair/relay pairs](states-tv.png) show actual 48px pieces on 128px rooms alongside labelled 96px detail studies. [Motion](monsters-motion.gif) is an optional manual link. The gallery supports four facings, pause, attack replay, still frames and system reduced motion. No runtime text is baked into sprites.

Three monsters (brood, drone, sleeper) each have four facings for idle, walk, attack, hurt and stun: 60 clips total. Walk has six distinct frames; attack/hurt are short one-shots. Stun uses a gentle pose change without flashing or shaking. Root default images are static. The full pack contains 81 unique 24×24-frame PNGs and SHA-256 manifest entries.

Repair has broken/fixed geometry; relay has powered/rerouted cable geometry. Initial broken/powered states are established in the actual scenario C# sources, not just JSON. Sabotage and the generic hidden marker are private references and excluded from public posters. Charges are shown only as found; disarming removes them. Rot/vent overlays are effects under #337, not additional token kinds.

## Verification and limits

- Twelve focused regression tests cover exact scenario/state/facing coverage, hashes, animation diversity, frame bounds, silhouettes, private visibility and byte-identical rebuilds.
- The exact current game art checker accepts tokens 18/18 without warnings/errors; build has zero warnings/errors. FX and audio coverage belong to separate issues.
- Real Chromium checks at 320×844, 390×844 and 1920×1080 cover loading, overflow, reduced motion, facings, pause and one-shot replay. Twelve isolated 48px browser sprites match nearest-neighbour source pixels exactly.
- Three native Godot art-review captures match the 1920×1080 source posters exactly. They are review layouts, not connected gameplay sessions. Actual linear-sRGB Machado deuteranopia/protanopia simulations and JPEG quality-55/4:2:0 proxies are retained. Shape differences remain visible in the state comparisons; this is no claim of human usability, physical-TV or Discord testing.

## Runtime handoff

Current #316 `MonsterFrames` paths resolve the delivered directional idle/walk/hurt/stun groups. Attack strips are delivered but the current consumer has no Attack mapping. The static monster `default` also needs a consumer fallback for the rare pose-less `Token(kind)` route, which currently ignores that entry. These integration checks belong to #399; this asset PR does not import or modify unmerged gameplay. Static token `.idle` and explicit state entries follow the existing consumer contract. Review names are live HTML/poster labels; complete in-game labelling remains a consumer responsibility.

Issue #336 remains open for the owner's visual approval. No gameplay/balance rules, existing voices or separate Tactics VFX workflows are changed.
