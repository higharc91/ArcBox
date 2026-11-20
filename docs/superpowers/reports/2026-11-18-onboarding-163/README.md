# First-session onboarding — issue #163

[Embedded screenshot review](index.html) · [Browser assertions](browser-validation.json) · [Six-image gallery check](gallery-validation.json)

A fresh profile now gets a three-card, inline quick start on its first player-controlled battle turn. It explains turn resources, selecting a map tile and committing movement/abilities, and Downed recovery. It can be hidden immediately and never blocks battle controls or pauses/changes the turn deadline. “How to play” reopens the complete guide from both the active and waiting battle screens, with explanations of dice, saves, AC/DC, advantage, concentration, reactions, Disengage and Delay.

The four economy chips open touch/keyboard-accessible explanations even after a resource is spent. The hero HP and aiming views explain the actual Downed countdown; noninteractive unit-selection labels remain safe inside their existing buttons. Existing sheet focus trapping, Escape and focus restoration are retained. The Downed sheet resets inherited HP typography so it remains left aligned and readable.

All twelve class cards in both quick class selection and the character creator have a playstyle sentence and an Easy / Moderate / Difficult rating; Fighter, Barbarian and Ranger carry a new-player recommendation. Accessible class names remain concise, with guidance attached as a description. Twenty common/early abilities have plain-language introductions, while exact server rules remain expandable in the aiming view; costs, targets, previews and sent commands are unchanged. Unknown classes/abilities keep their existing fallback.

Primer history is local to this phone and scoped by the public profile ID supplied by the existing lobby, not a profile credential. New characters on the same profile do not retrigger it; another profile does. Legacy/dev views fall back to unit identity. Blocked/full storage falls back to page memory. Waiting/summon turns do not consume the primer.

## Verification

Base: main `a3c69c4d60da572197f7d0509c0fbad1d1c269fd`. This is a phone-only change; no server protocol, C#, rules, balance, art pixels or audio were changed.

- Full phone suite: **610/610 tests passed across 41 files**, including nine new onboarding regressions and existing trade/reconnect/portal/battle tests. [Log](checks/phone-tests.txt). HappyDOM prints missing-content-fetch diagnostics in the existing fixture environment; the runner exits successfully.
- TypeScript and production Vite build pass without build warnings. [Log](checks/production-build.txt).
- Touch Chromium at **320, 360, 390 and 430 × 844**: no horizontal overflow. Action and Downed sheets fit after their entrance animation; the Downed body retains readable 16px left-aligned text. Keyboard focus stays in help, and Escape restores its opener.
- Real production phone components, with server-recorded fixtures: read/hide tips, move to a legal tile, aim/use Dash, return and end the turn. Exactly the original three game commands are emitted. Reload suppresses the automatic primer; explicit help still opens, and another profile gets its own introduction. All twelve class guides render. [Receipt](browser-validation.json).
- Every guided ability ID was checked against the current shipped ability definitions. The browser review uses the checked-in crests, title and portrait assets. It does not run a live server; its countdown is rebased for review.

The issue's final acceptance criterion—an actual person new to D&D completing a turn unaided—remains **manual and unclaimed**. Keep #163 open until that usability pass is recorded. No full .NET suite, physical phone or human usability test is claimed by this phone-only verification.

## Repeat the visual check

Install the repository phone dependencies, start `npm --prefix web/play run dev -- --host 0.0.0.0 --port 5173`, and run `review.cjs` with Playwright available to Node. It loads `web/play/test/review/onboarding.html`, a non-production fixture harness. `CHROMIUM_PATH` optionally selects a browser; `ARCBOX_PHONE_REVIEW_URL` optionally selects the Vite origin. The harness and test driver send no live server commands. Production builds retain only the existing index/dev/title entry points.

## Difficulty wording follow-up

Class cards now say **Difficulty: Easy**, **Difficulty: Moderate** or **Difficulty: Difficult** in both selectors. The mapping preserves the original relative ratings. After this copy change, all **9 onboarding tests** and the production build pass; Chromium verifies all twelve displayed ratings at 320/390 px and all six images in the refreshed gallery. [Rating/browser receipt](difficulty-validation.json) · [Focused tests](checks/difficulty-tests.txt) · [Build](checks/difficulty-build.txt). The 610-test full phone run above is the preceding implementation checkpoint.

## Cursor-label spacing follow-up

“Cursor” now has its own full-width row above the direction pad with 8px bottom padding, rather than overflowing the 44px centre cell. The four arrow targets remain 44×44px. [Normal controls](cursor-controls-390.png) · [Enlarged map with 2× caption text](cursor-large-text-320.png).

All **37 battle-map and turn tests** and the production build pass. A real Chromium geometry check verifies that the caption text clears every arrow by at least 7px at 320/360/390/430px, in both map modes at regular and doubled caption size. Left/right browsing still selects the expected tiles without sending game commands. [Receipt](cursor-validation.json) · [Repeatable check](cursor-review.cjs) · [Tests](checks/cursor-tests.txt) · [Build](checks/cursor-build.txt).
