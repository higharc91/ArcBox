# ArcBox Tactics: phone-view playtest (2026-10-31)

This run followed the [local playtest handoff](../plans/2026-10-31-tactics-local-playtest-handoff.md) on the user's Windows PC, in two parts. First, Claude played through the desktop app's in-app browser, with one tab emulating a phone at 375×812 with an Android Pixel 8 user agent, and three players on the dev page. Most taps on the phone tab were real clicks in that viewport; some, and all iframe players, were scripted. Most of this report comes from that part, so it wasn't a real phone or a person.

Then the user played on a real Samsung S23: see [A real phone, afterwards](#a-real-phone-afterwards). Some of the handoff's questions about how the game feels in the hand are still open: see [Still needs a person](#still-needs-a-person).

## Setup

- **Branch:** `ccr-18ad4510-q68nad`, with `origin/main` merged in (the merge was clean). The handoff is on this branch, not on `main`.
- **Tests before play:** the build had 0 warnings, and the web client's 241 tests and 1,908 of 1,909 .NET tests passed. The failure was a Windows-only finding, fixed below. After the fixes, all 1,909 .NET tests and all 247 web tests pass.
- **Android emulator:** not installed. The in-app browser's phone mode covered the layout questions, and an emulator would have added a multi-gigabyte install without answering the questions that need a person.

## What worked

| Handoff step | Result |
|---|---|
| Lobby to campaign | New campaign, "I'm new here" (two taps) and Everybody's in all work |
| Muster | The class grid fits one screen, and each player got their class |
| Battle pick | River Crossing started, and the board and phones agreed |
| Turn screen | Move, Delay and End turn are visible without scrolling, and river and rock are easy to read |
| Walking | Tap a tile, then Move here. Sam moved 4 tiles |
| Attack | Attack, then **Use on Goblin**: hit for 4, with HP and the log updated |
| Area spell at a tile | **Sleep**, aimed from the suggested list, put 2 goblins to sleep. This is the first play of this flow outside tests |
| Cone spell | **Burning Hands**: four orange tiles mark the directions. Aimed by tapping one, it hit 2 goblins for 5 and 13 |
| D-pad | Moves the cursor on the map |
| Reaction | The Shield prompt stayed up for about 10.7 s. Left unanswered, the AI used Shield for Sam, and the goblin missed |
| Downed and results | Defeat in round 6 or later: 125 XP each (half of 250), and Black Eye for the heroes who were Downed |
| Portal: new character | Point-buy caps at 15 and uses exactly 27 points. Race, class and background all save |
| Portal: existing character | Shows XP, injury, scores and spell picks, with no sideways scrolling |
| Character in a new session | Sam skipped the class pick and played the Elf Cleric: 11 HP, cleric spells |
| Phone reload mid-battle | Rejoined in the same state, in about 2 s |
| Server restart mid-battle | "Room PTWV rebuilt from storage": same round, HP, positions and countdowns. The phone reconnected by itself and carried on to the results |

## What was fixed

| Problem | Fix | Commit |
|---|---|---|
| The Tactics end-to-end test failed on a normal Windows PC, because creating a symlink needs Developer Mode or admin rights. CI can't show this | The test setup copies the content when a symlink is refused | `7bf9f2c` |
| After picking a spell from low on the list, **Use and Back sat above the top of the screen**, with the clock running | Picking an ability scrolls to the top | `c8b9edd` |
| After moving, **End turn jumped into Delay's spot**, so a habitual tap could end the turn by accident | Delay stays in place, greyed out | `c8b9edd` |
| **Spells were buried** below Dash, Disengage, Dodge and Help for classes sorted after `common`, such as wizard. Burning Hands started at about 968 px on an 812 px screen | Order is now Attack, class abilities, common actions, items | `c8b9edd` |
| Area targets were labelled with **raw coordinates**, such as "(8, 6)" | They now read "2 targets", with the names beneath | `c8b9edd` |
| Players saw a **raw internal id**: `statMod.wizard.shield` on the phone, and "Sam is statMod.wizard.shield" in the log | The phone shows "shield", and the log reads "Sam gains shield" | `c8b9edd` |

The five UI fixes are client-only, so the server contract and fixtures are unchanged. All five were written test-first, and the scroll, Delay and ordering fixes were checked again on the live phone view. The target labels and condition names are covered by tests only, because neither came up again in play.

## What's left, ranked by how much it hurt play

1. **Map tiles are about 24 px wide**, well under a comfortable tap size. The d-pad helps, but tapping a chosen tile will probably be fiddly. This one needs a real thumb to judge.
2. **Reaction prompts say very little.** "You're about to be hit" doesn't say who's attacking, what the roll was or what Shield does, and there's only a 10-second window to decide.
3. **Goblins waste turns.** Goblins and archers used Help on each other with no foe near, and Dodged or Dashed a lot. The earlier playtest explained the Dodge turns as holding the bank, but Help with nobody near looks like an AI weakness. It's a game-rules matter, so it's unchanged.
4. **Finding the portal is hard.** The campaign code came from `GET /api/me`, and nothing in the phone UI links to `/c/<code>`.
5. **A character from Muster can't be remade in the portal.** The creator only shows for someone with no character, so to choose race or point-buy you have to go to the portal before Muster.
6. **Tooltips read like the rules engine:** "SELF: TARGET GAINS ITS MOVE AGAIN" and "Weapon attack, weapon range: weapon damage". Bless's tooltip is also very long.
7. **The Tactics lobby card says "1–6 players"**, but every encounter needs at least 4.
8. **The A, B and 4 chips are unexplained**, and a cone gives no "pick a direction" hint: the button only says "Pick a target".
9. **Seen once, not reproduced:** a spell selection dropped back to the move screen right after a page reload at the start of a turn. The screen clears your selection whenever a new view arrives, so a re-sent view is a possible cause, but that's unconfirmed. Three later tries held for 1.8 s each.

**Not bugs:** the d-pad cursor on a goblin 7 tiles away showed "Pick a target" because Sleep's range is 6. The earlier suspicion that the Shield prompt vanished early was a stale screenshot, and the timing above is from a recorder on the page.

## A real phone, afterwards

The user then joined on a Samsung S23. **The LAN address didn't connect.** The server and Windows firewall looked right and no request reached the server, so a router blocking Wi-Fi devices from wired ones is the likely cause, though that's unconfirmed. **Tailscale worked:** `http://<pc-tailscale-ip>:5080/?room=<CODE>`.

The user's verdict:
- **Tile tapping is fine.** Visual polish can come later.
- **Locking the phone mid-turn rejoined the game, but control came back a turn late.** The AI kept the rest of that turn, with the player sitting on the status screen. A player who dropped before their turn got control back at once, so the two paths disagreed. A player who returns now takes over what's left of the turn at once, on the remaining timer. The spec's §4.4 was updated to match, and **the user confirmed the fix on the phone**.

## Still needs a person

- Is the d-pad comfortable? Tapping tiles is confirmed fine, but the d-pad has no verdict yet.
- Do 60 s for a turn and 10 s for a reaction feel right? Claude's tool delays make its timings meaningless, and the user hasn't given a verdict.
- Phones on the Wi-Fi LAN: the S23 couldn't connect, so check the router's isolation settings, or keep using Tailscale.
- **Wake Lock:** it needs HTTPS or `localhost`, so a phone on plain HTTP may still dim mid-battle.
- "Resuming…" wasn't seen, because the restarted room was already back when the dev page reopened.
- After one restart, a room came back on a new battle. That battle may have ended before the restart, but this wasn't confirmed.
- Whether the reaction prompt shows its countdown: no screenshot caught the prompt on screen.

Balance is unchanged. The decisions waiting on the user are listed in the rules-engine tracker.
