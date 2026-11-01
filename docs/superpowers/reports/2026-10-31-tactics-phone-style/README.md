# Wardenflame phone restyle (2026-10-31)

The tactics phone screens and campaign portal (`web/play/src/games/tactics/`), restyled to the mockups in
`docs/superpowers/mockups/tactics/` (04 battle, 05 party, 07 creator). Screens are 390×844 at 2× (plus the turn
screen at 360 and 430 wide), from a real 3-phone session on `dotnet run` + `/dev.html?phones=3`. The portal shots use
the test fixtures' views (`portal-new`, `portal-character`) with the campaign API intercepted.

**Before:** `../2026-10-31-tactics-playtest/` (`phone-turn.png`, `phone-react.png`, `portal-*.png`).

| Screen | After |
|---|---|
| Class pick (muster) | [01-pick-class](01-pick-class.png): Wardenflame logo, class crests |
| Battle pick | [02-pick-encounter](02-pick-encounter.png) |
| Your turn | [03-turn](03-turn.png), [scrolled](03b-turn-scrolled.png), [tile picked](03c-turn-move-picked.png), [360 px](03d-turn-360.png), [430 px](03d-turn-430.png) |
| Aiming | [04-aim](04-aim.png): target card with portrait and HP, the ability's cost and effect, the expected damage |
| Reaction | [05-react](05-react.png) |
| Waiting | [06-status](06-status.png) (before your first turn), [later](06c-status-later.png), [Abilities tab](06b-status-abilities.png), [Bag tab](06d-status-bag.png) |
| Results | [07-results](07-results.png): the pack's DEFEAT/VICTORY banner |
| Portal | [08-portal-create](08-portal-create.png), [09-portal-sheet](09-portal-sheet.png) |

## What changed

- **Look.** Navy glass cards with hairline and gold edges, gold small-caps section heads, a gold CTA and ghost Back.
  The palette is CSS variables (`--wf-*`, from STYLE.md's UI palette), scoped to the tactics controller screen and the
  tactics portal. The shell's status bar, mute toggle and countdown are re-themed within that scope only.
- **Type.** Cinzel (titles), Alegreya Sans (body) and Pixelify Sans (the clock and XP), self-hosted via
  `@fontsource/*` 5.3.0 (SIL OFL), loaded with the tactics module only.
- **Art from the content pack** (`pack.ts` fetches `index.json` once and caches every manifest): ability icons by id
  (2× atlas on high-DPI screens, 1× atlas at 2× otherwise), enemy portraits by sprite key (hurt pose below ⅓ HP),
  class portraits for heroes, class crests, status icons on condition chips, the logo and emblem, and the VICTORY and
  DEFEAT banners. Pixel art is drawn at whole-number scales with `image-rendering: pixelated`.
- **Turn screen.** Your card (portrait, class, HP), the YOUR TURN banner with the turn clock, the action-economy chips
  (spent ones struck out), the map beside a 44 px d-pad, then abilities grouped as Actions / Bonus actions / Free with
  icon cards and subclass/resource tags. Move here / Delay / End turn sit in a sticky footer, so they are always on
  screen; aiming swaps them for Back / Use (gold) and shows the target card, effect, damage preview chips and a
  ◀ Target n of m ▶ stepper.
- **Waiting screen.** Your card, a Now acting card (portrait when this phone has seen the unit), "You're up in n",
  and Abilities / Sheet / Bag tabs.
- **Sound and haptics.** `sfx.ts` plays the pack's `ui_select`, `ui_confirm`, `ui_cancel`, `ui_your_turn`,
  `ui_reaction_prompt` and `ui_dice_roll` after the first gesture, follows the app's mute toggle, and vibrates for
  your turn and reactions.
- **Reduced motion.** The banner entrance, cursor pulse, urgent-clock pulse and HP-bar transitions are off under
  `prefers-reduced-motion`.

## Known gaps

What the views don't carry, so the phone can't show it yet:

- **Class, level, AC and ability scores** of your unit in battle (`UnitView`). The class comes from your abilities'
  classes in the icon manifest and is remembered for the waiting screen; before your first turn your card says
  "Hero" with an initial.
- **The turn order** (the mockup's queue) and the **acting unit's** id, sprite and player. The status view has only
  `active` (a name) and `upIn`, so the Now acting portrait is matched by name from the last turn view.
- **Your reaction availability** and **used/unavailable abilities** (the mockup dims spent ones). The turn view lists
  only what you can use now.
- **Hit chances and modifiers** for the "You roll d20 + 5 against AC 13" line, and advantage reasons.
- **Abilities and bag on the waiting screen.** They are this phone's memory of your last turn; there are none before
  your first.
- **Heroes and AI companions have no sprite**, and companions no class, so they show as initials.
- **The turn deadline** reaches the app shell, not the screen's data. The banner mirrors the shell's own countdown,
  which is hidden on that screen.
- **Portal:** no hero name, look or story steps (the view creates from race, class, background and scores only), no
  subclass descriptions, and no portraits for players' custom characters.
- **Join, "Who are you?" and seat picking (mockup 06)** are shared shell screens (`src/screens/*`), outside the tactics
  module, so they keep the generic look.
- **Map tiles** are about 16 px wide beside the d-pad. That's below 44 px, so the d-pad and the target buttons are the
  precise way to aim.
- The shell's generic tap sound also plays on each send, alongside the pack's UI cue.
