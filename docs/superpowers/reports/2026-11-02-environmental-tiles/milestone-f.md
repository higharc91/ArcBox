# Milestone F — phone forecasts and immersive terrain

Date: **2026-11-03 (UTC)**. The user authorized F and asked for phone animation while a character stands in an environmental area.
Work remains on `environmental-tiles`; no production encounter is opted in by this milestone.

API/forecasts and fixtures: **`32a0d3fe`**. Phone rendering and preview harness: **`c6211f9f`**.

## What players see

- Movement options carry optional `risk` (expected damage, rounded to a tenth) and `warn`. The estimate uses the AI's existing
  path-entry, end-of-turn and next-turn hazard calculation, including saves, defenses and shared passive limits. A burning
  destination reads **“Move here · 2 · burns ~7”**; the map marks warned destinations with amber hatching. Ice, flammable oil
  and water that conducts lightning also receive short warnings. A preview is advisory; the existing authoritative command
  validation and exact movement path remain unchanged.
- Selected spell targets carry optional `spread`, drawn with a **dashed blue border**, separately from the primary aim and
  gold cursor. The legend describes reaction spread, including allies. Empty-tile targets with spread survive phone compaction.
  Conducted allies, the caster and enemies enter `affected` with expected secondary-save damage. Direct targets are not counted
  again; downed units are excluded; ice insulates. Probability branches, conduction geometry and damage/save maths reuse the
  AI's scorer and environment predicates without drawing RNG or changing battle state. Spread marks possible immediate
  reaction tiles, not speculative future rounds of fire spreading.
- The phone gains an optional **`data.surfaces: string[]`** on battle turn, waiting/status and reaction screens. This describes
  the player's **current server-side tile**, not the aim cursor. Burning ground sheds embers, ice has drifting crystals,
  water has ripples, oil has a slow shimmer, webs have drifting strands, and scorched ground has ash. A quiet edge tint and
  a readable terrain label accompany the effects. Ice suppresses the underlying water effect. Empty terrain, disabled
  encounters and dead/out units omit the field, clearing the animation normally.

Ambient art is behind the controls, with `pointer-events: none`; it uses only transform/opacity animation, at most two
visual layers and **24 particles**. **Reduced motion** stops the particle animation and retains a static terrain cue and label.
No audio, generated art, haptic permission or new gameplay effect is introduced. The phone map now recognizes the Ford's
existing local material families and timber boards, so water and autumn banks match the map instead of falling back to grey.

[Animated phone preview](f-phone-surfaces.mp4) · [Burning ground](f-phone-fire.png) · [Water](f-phone-water.png) · [Selected spread](f-phone-spread.png)

## Compatibility and reproducibility

All three forecast fields are optional and omitted when unused. All **existing phone fixtures remain byte-for-byte identical**
after regenerating `PhoneFixtureTests` with `UPDATE_FIXTURES=1`. The two new `environment-turn` / `environment-status` fixtures
come from an actual resumed `TacticsGame` session with test-only environmental state. Existing encounters and campaign-save
schemas are untouched. No encounter JSON or balance value changed.

The added `environment.html` is a development preview, not a production build entry. Run `npm --prefix web/play run dev`,
then open `/environment.html`; `?screen=status` uses the real waiting fixture. `?surface=burning`, `ice_sheet`, `water`, `oil`,
`web` or `scorched` previews that cosmetic treatment on the same controller. These explicit development overrides do not change
server state. The short movie demonstrates those six looks; warnings and spread screenshots use the actual generated turn
fixture, without changing its forecast fields. Supply the ordinary content-pack URLs for portraits/map art (the browser check
served the repository's real pack files).

## Validation

- Full Release solution: **5,864 passed, 0 failed, 0 skipped**, **0 .NET build warnings**.
- Web: **483 tests passed** across **21 files**; TypeScript checking and production build pass.
- Focused checks: **5 logic**, **14 game/fixture/size**, **5 web environment** cases passed.
- **11 new .NET cases** and **5 new web cases** were added. All original fixture hashes and all six golden hashes are unchanged.

[Validation and fingerprints](f-validation.json) · [Solution log](f-tests.txt) · [Web tests](f-web.txt) · [Build](f-dotnet-build.txt) · [Web build](f-web-build.txt) · [Browser results](f-browser-check.json).

Tests cover deterministic conduction previews and ally damage, direct-hit deduplication, downed exclusions, ice insulation,
fuel/water/scorch matching, path plus next-turn fire damage, optional-field omission, compaction preserving empty aims with
spread, current-tile updates and clearing, and real resumed phone views. The first logic cases failed before their forecast
fields existed; all five new web cases failed before their UI was implemented. New enabled level-10 wizard, druid and cleric
payload cases guard the **64 KB** warning threshold. The representative real environmental turn is **8,963 compact JSON bytes**.

Browser checks use real Chromium at **390×844**, **320×568** and **844×390**. They check each of the six treatments, successful
risk selection/commit controls, selected spread without hiding the gold cursor, clearing on ordinary terrain, disabled motion,
non-intercepting decorative layers, and absence of horizontal overflow. The animation was reviewed in motion and at successive
frames; no application page errors occurred.

The estimates use the current board and the existing AI hazard model. They refresh with state, rather than promising what
other units will do before the next turn. The web production build retains the existing large Three.js flyover-chunk warning;
it is unrelated to these bounded CSS effects. Existing happy-dom local-fetch/abort/error-boundary stderr remains documented
in the test log, with passing tests.

G remains: opt in the approved encounters, add Ford Assault's cantrip-strength lightning shaman, then run the agreed balance
comparisons and playtests. The oil shop price still awaits the separate economy branch. No PR or main-branch merge is part of F.
