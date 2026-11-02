# Overworld 5b: interaction review

Read from the code at `69852bf` and the shots. Nothing was played, and only a person can settle how these feel. Moments 7 (edges), 8 (stopping play), 9 (sound) and 11 (reconnects) were only partly covered, and road widths are judged from shots.

Each item ends with the local session's ruling. **Do** items are for the next session, before 5c starts.

## Must fix before 5c

1. **The road's direction sits on a pad sector's edge.** `walkModel.ts` (`dirAt`, `stickyDirAt`), `walk.tsx` (`wedge`, chevrons). A road along a map axis is bearing 63.4° on the pad, and the `ne` sector ends at 67.5°, so a thumb aimed along the road has 4.1° of margin. A slight miss sends `e` (1,-1), and the party drifts off the road. The chevron at 45° also points 18° from where the party goes. Use unequal sectors with boundaries at about 30° (n/ne) and 78° (ne/e), mirrored in all quadrants: n/s 60° wide, road directions 48°, e/w 24°. Draw chevrons and wedges at 63.4° and 116.6°. `stickyDirAt` hard-codes 22.5, so it needs per-sector widths. Keep `STICK_DEGREES` at 6. Phone only, and the protocol is unchanged. **Do.**

2. **A blocked press feels dead.** `TacticsGame.Explore.cs` `TakeStep`, `WalkTrack.AxisOf`. The pad stays gold, and the party stops and fans out, which looks like letting go. Nobody can tell a wall from a dropped connection. A blocked screen-diagonal also keeps the old facing, because the 0.707 dot passes `AxisOf`'s 0.7 test. Add `Blocked` to `ExploreView`, which is TV data under spec §4.5. The TV nudges the leader about 0.12 tiles at the wall and back over 0.15 s, and plays the tile's step sound at about -14 dB. Stay silent on the phone, because telling it would mint a new view. **Do.**

3. **The takeback lands mid-thumb.** `ReconsiderLeader`, `StopWalking`, `HeardFrom`. If Bo blips and Cy takes over, the first time Cy lifts a thumb the lead snaps back, and Cy's pad unmounts. Add `WalkTiming.TakebackAfter = 3 s`: the session leader takes the lead back only after the party has been stopped that long, and any press by the current leader restarts it. **Do. It amends a default the local session chose, so add it to the user's list.**

4. **Nobody is told the first time.** `walk.tsx:256`. `led` starts at `d.leader`, so the first leader gets no pulse, sound or buzz, and "hold a direction to walk" exists only in the `aria-label`. Use the existing `useAlertOnMount("yourTurn")`, and add one static line under the banner: leader, "Hold a direction to walk. Watch the TV."; others, "{name} steers. Watch the TV." **Do.**

5. **A handover isn't announced on the TV.** `ReconsiderLeader`, `WorldHud`. Only a name in the top-right panel changes. A locked leader's phone freezes everyone for 8 s with no explanation. When `_lead` changes, `Note("Bo leads now.")`: host-only, a step clears it, and no phone view changes. Keep the 8 s. **Do.**

6. **"Next battle" is now wrong.** `screens.tsx:112`. The button returns the party to the world. Use "Carry on", also true for a pack with no region, with no new field and the action still `next`. **Do. It's a label on an existing screen, so tell the user.**

## Should fix

7. **Road directions catch on walls.** `WorldWalk.Next`. Only n/e/s/w slide, so ne/se/sw/nw, the ones used most on roads, stop dead at a one-tile jag. On a blocked straight step, if exactly one side is open and the tile beyond the jag is also open, take that side step. Reachability doesn't change, but it overturns `A_blocked_straight_step_doesnt_slide`, and it needs checking for jitter along straight walls. **Do, because finding 1 and this are the two halves of following a road. Add it to the user's list.**

8. **Quick taps are lost.** `WalkInput`, `Halt`. A press during a step that's released before the step ends does nothing, though the phone buzzes: above about 3.3 taps a second, or 2.4 on diagonals. Latch one press and take a single step at `_stepDue`. **Do.**

9. **Turns over-run by a tile.** The next tile commits about 80 ms before the TV shows it, so a thumb has to move about one tile early. Harmless on roads 4 to 6 wide. Keep `WalkTiming.Step` at 300 ms and paint junctions at least 3 wide. Try 260 ms at 5e if it bothers people. **No change now. Add the junction width to the painting guide.**

10. **Locked follower phones may show as away.** `WalkerView.Here`, `UnitPlan.Walkers`. Wake lock needs HTTPS, so on LAN HTTP followers may lock within a minute and turn into dim ghosts. **Measure the disconnect delay first, in `tv-e2e` or on a real phone. If it happens, don't dim until someone has been gone 60 s.**

11. **"Which is me?" has no answer.** `walk.tsx:279`. The TV shows no names, and the phone's card shows a letter because `sprite: null` is passed. All rings are the same green and fade into grass, while mockup 02 gives each player a colour, and 5d's vote dots will need those colours. **Do the sprite if the pack has it. Do the ring colours from the seat if it's a small change, otherwise with 5d.**

12. **The road just ends.** `TakeStep`. At x=159 the party meets the blocked state, and void can show. When blocked beside a region edge, `Note("The road goes on, but not today.")`, once only. **Do. The wording is the user's to change.**

13. **"Choose a battle" is one tap with no confirm.** `HostMenu`. A VIP who isn't leading can pull the table out mid-walk, and the leader's pad vanishes under the thumb. Reuse the `sure` confirm. **Do.**

14. **Nothing says the world is loading.** `TacticsScene.ShowWorld`. The backdrop hides as soon as the world opens, and the HUD waits for `Open`, while phones are already live. Keep the region title up until `Revealed`. **Do.**

## Polish for 5e

15. The arrival banner's gold text on the gold pulse is hard to read at its peak (`arrival.png`), and the pad's glow clips the bottom edge at 360 wide.
16. Walking has no ambience. `AmbienceOf` covers battles only. Consider `amb_river` or `amb_plains_wind`.
17. Footsteps are 3 a second at -8 dB (`FootstepDb`) for long stretches. Try -12 dB if it nags. The counter doesn't reset at rest, so resetting it would make a press sound immediate.
18. Look-ahead of 1.5 tiles is only 68 to 136 px. `LookAhead` = 3 is worth a try.
19. The leader has no sheet link. That matches the spec. Leave it unless the user asks.
20. Followers' screens are static and about 60% empty. Fine with eyes on the TV.

## Already right

- Screen up really is up on the TV.
- The press steps in the same input call.
- The pad's double tick on a turn is a good touch.
- The new leader walks to the front rather than jumping.
- The confirm isn't under the finger that asked for it.
- Sprites are about 90 px, close to mockup 02, and the crown bubble is about 35 px.

## Only a person can judge

- Whether the unequal sectors feel better, and whether a 24° e/w sector feels too thin.
- Whether going up feels slow: 151 px/s against 239 along a road and 302 going right.
- Whether a 300 ms tile feels right, and how tapping feels.
- How the bump reads, and how the lead shuffle looks.
- Sprite legibility on the real TV.
- Footstep fatigue, and the music against the walk.
- A rules question: an awake but absent leader keeps the lead all session, because there's no pass-the-lead. That would be a new rule, so it's the user's call.
