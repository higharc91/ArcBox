# Cutting the wait between turns (#113)

Task 8 of the combat-area fixes (#98). Implements suggestion (b), per-player reaction preferences that answer without a prompt, and (d), a shorter clock once Move and Action are spent. (a) and (c) are follow-ups (below).

## What changed

- **Reaction preferences** are saved on the member record (`MemberRecord.Reactions`, `ReactionPrefs`; null is Ask, is not written, so older documents read unchanged).
  - "Opportunity attacks": Ask (default), Always, Never.
  - "Shield and other defensive reactions": Ask (default), "Only when it turns a hit into a miss", Never.
  - A reaction a preference answers is applied inside the game loop as the same `React` command a tap would send, before any prompt or clock exists. Nothing is shown to the player and nothing pauses. A summon's reactions follow the preferences of the player who controls it (its owner). Offers with an option no setting covers (a rebuke, a counterspell) are always asked.
  - "Only when it turns a hit into a miss" is `Ai.ChangesOutcome(state, offer, option, rules)` in `Tactics.Logic`: true when a defensive ability's AC makes the offered attack's actual roll miss (never against a natural 20). It is judged on each offer's own cause, so each attack of a multi-attack is its own question. It is a per-reaction predicate, so the Uncanny Dodge change (#112, "does halving stop this hit from Downing the rogue") is one more case in that method.
- **The phone** has a "Reactions" button on the waiting screen and on a turn, which opens a sheet of the two settings (`reactions.tsx`; input `reactionPrefs`, one setting per tap). The screens carry the current choices as `reactions`. Changing a setting while a prompt is up answers the prompt at once if the new setting covers it. The portal character page is not changed (not simple: it has no battle context).
- **The clock**: `TacticsTiming.SpentTurn` (15 s). Once the acting player's Action is spent and their Move is spent, the turn clock becomes `min(remaining, 15 s)`, at the moment of the command (not once the TV has caught up).
  - "Move spent" means none left, nowhere to go, or the unit has walked this turn (less Move than when its clock started). A player who stepped up and struck has made their move. Only an unspent Action, or a hero who hasn't moved yet, keeps the full minute.

## Measurement

`Tactics.Game.Tests/TurnWaitTests.cs` (the bot-phone harness in `tools/tactics-playtest` needs a browser and a running server, so it can't run headless here). Six bot phones play Crypt Hall (10 foes against 6 players) through the real `TacticsGame` on the platform's fake clock for 4 rounds, on seeds 1 to 4 (16 rounds per cell). The foes are the real AI, paced by the game's own fallback beat timer.

- **Before**: every reaction asked, and the clock set back to a flat 60 s (`SpentTurn = 60 s`).
- **After**: every bot has "Always" and "Only when it turns a hit into a miss" set, and the 15 s clock.
- A **brisk** bot starts a turn after 12 s, taps once a second, and ends its turn when done. A **dawdler** does the same but never taps End turn, so the clock runs out: the worst case the short clock is for. Prompts, when asked, are answered after 6 s.
- "Wait" is the time from a hero's turn ending to that player's next turn starting.

| Bot | Seconds per round (before to after) | Longest wait between one player's turns | Mean wait | Prompts, and seconds the table stood still for them |
|---|---|---|---|---|
| brisk | 60.4 to 59.4 | 102.0 s to 99.5 s | 86.3 s to 88.4 s | 10 to 0, 60 s to 0 s |
| dawdler | 218.6 to 176.3 (-19%) | 337.5 s to 309.5 s (-8%) | 243.6 s to 218.1 s (-10%) | 13 to 0, 78 s to 0 s |

Reading the numbers:

- **No prompt pauses the game for a player who opted in**: 23 prompts across the runs become 0 (asserted by the test).
- **The 337 s longest wait** reproduces the issue's "about 5.5 min between one player's turns" worst case with idle phones.
- **The short clock helps only turns that have both Move and Action spent.** A dawdler whose Action is still unspent (no foe in reach in rounds 1 and 2, where the bot can only walk) keeps its full minute, so the clock saves 19% per round, not more. Turning the rest of a fight into a quicker one needs (a) or (c).
- **Brisk players gain mostly from prompts**: a prompt costs about 6 s, one in about 1.6 rounds. The per-round times differ by noise, because changed timings change the battle's course (the 41 and 43 turns).
- The test asserts that the longest wait and the dawdler's seconds per round don't get worse. The numbers vary with the bots' model, so the "agreed amount" the issue asks for is the controller's to set. The test reports, it doesn't gate on a size.

To regenerate: `TURN_WAIT_REPORT=<file> dotnet test --project games/Tactics/Tactics.Game.Tests -c Release -- --filter-class "*TurnWaitTests*"`.

## Follow-ups (out of scope by the ruling)

- **(a) Simultaneous turns.** Let party members who are consecutive in the turn order act at once (they already win tie-breaks, `Rules/TurnOrder.cs:266`). It is the only suggestion that removes the 5 x 60 s of waiting for a player whose Action is genuinely unspent.
- **(c) Pre-picked actions.** A waiting phone picks an ability and target that runs when its turn starts, if still legal.
- Uncanny Dodge ("defensive") joins "Only when it turns a hit into a miss" with #112, by adding a case to `Ai.ChangesOutcome`.
- A "Reactions" toggle on the portal character page.
