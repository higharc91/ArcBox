# Handoff: finish the Wardenflame overworld overnight

You're a cloud Claude Code session taking over from a local session on the user's Windows PC. The user is asleep or away, and won't answer. Work without them: decide, record the decision, and carry on. Anything only they can decide goes on the list in §8.

The game's display name is Wardenflame. Its ids stay `tactics`.

## 1. Get the code

```sh
git fetch origin ccr-18ad4510-q68nad
git checkout ccr-18ad4510-q68nad
git pull --rebase origin ccr-18ad4510-q68nad
```

Work and push on `ccr-18ad4510-q68nad`. Run `git pull --rebase` before every push, because other sessions push here too.

PR #67 was merged by the user, so everything up to `3894cad` is on `main`. Later commits on this branch aren't, and **no PR is open. Don't open or merge one unless the user asks.**

Before anything else, confirm the build is green:

```sh
dotnet build ArcBox.slnx            # expect 0 warnings
dotnet test --solution ArcBox.slnx  # see §3 for the count
npm --prefix web/play ci
npm --prefix web/play test
npm --prefix web/play run build
```

On some runners `dotnet test ArcBox.slnx` needs the `--solution` form, and a filter is `--filter-class "*PhoneFixtureTests"`.

## 2. Binding rules from the user

- Don't change game rules, balance numbers or the server-to-phone contract without asking. The overworld's new phone screens and TV view data are approved in spec §4.5. Existing battle screens don't change.
- Don't merge or open PRs unless asked.
- If the host view or a phone view changes, regenerate the fixtures and check the diff: `UPDATE_FIXTURES=1 dotnet test --project games/Tactics/Tactics.Game.Tests --filter-class "*PhoneFixtureTests"`.
- TV: animate from each beat's own events, not from state. Send `Signal("shown", {seq})` after each beat. Keep each attack under about 1 s. A slow or missing TV must never stall the game.
- Read frame indices from manifests, never hard-code them. `down` holds its last frame, and `death` ends flat.
- Anything worded, ordered or computed is pure C# with tests. Godot classes only draw.
- Don't generate art through Scenario without asking.
- Don't make the balance decisions listed under "Decisions for the user" in the rules-engine tracker.
- Commit trailer, last line: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Stage files by name. Keep `.superpowers/` and `.claude/` out of commits.

The user's standard for this work, in their words: "Spend a ton of time on making sure the quality and interactions are perfect", and "After the overworld is finished, continue to work."

## 3. Where things stand

Expect 3,603 .NET tests and 335 web tests, all passing, with 0 warnings. If they don't, stop and find out why before building on it.

**What works now:** after Muster the party stands in Ashen Ford. The leader steers from an 8-way pad on their phone, and the party walks the real TV world with the camera following. Leader rotation, handover, saved position, a stop that survives a lost release, and TV drops are all done. The battle list is still reached with `battles`, and a list fight returns to the world. **The world has no story and no fights on the spot yet.**

| Milestone | State | Where |
|---|---|---|
| 5a: world format, painting workspace, export, checks, Ashen Ford draft | Done | [plan](2026-10-31-tactics-overworld.md), `tools/tactics-world/README.md` |
| 5b: walking | Tasks 1 to 10 done, Task 11 partial, interaction fixes to do | [plan](2026-10-31-tactics-overworld-5b.md) |
| 5c: fights on the spot | Planned, not started | [plan](2026-10-31-tactics-overworld-5c.md) |
| 5d: the story runtime | Planned, not started | [plan](2026-10-31-tactics-overworld-5d.md) |
| 5e: tuning and a real session | Needs the user | [spec](../specs/2026-10-31-tactics-overworld-design.md) §6 |

**Also done on this branch:** board lighting (baked vertex colours, see `docs/tv-host.md`), and the fresh-install crash fix: `audio/general/text_to_speech` is off in `src/ArcBox.Host/project.godot`, because loading speech at startup collided with the cold shader compile. Crashes went from 12 of 43 to 0 of 30 launches. `ProjectSettingsTests` fails if it comes back.

Reports and proof shots are in `docs/superpowers/reports/2026-10-31-tactics-overworld/`.

**World dressing (merged from the user's local session, 2026-11-02):** props, buildings and people in the world, the woods, Ashen Ford
rebuilt and dressed, the props painting layer, tall-prop fading and dusk light. Its design is
[the world-dressing spec](../specs/2026-11-01-tactics-world-dressing-design.md) and [plan](2026-11-01-tactics-world-dressing.md); where
Ashen Ford stood at that handoff, and what's left there, is [the Ashen Ford handoff](2026-11-01-ashen-ford-handoff.md). Painted props and
things to interact with both block walking through `WorldGround`; the loader refuses a painted prop on a thing's tile, and the export
warns about a blocking prop beside a door or lever ([interactables design](../specs/2026-11-02-tactics-interactables-design.md), last section).

## 4. What to do, in order

Each milestone's plan is complete: read everything above its "### Task 1" first, because the flags and defaults there bind you.

1. **Finish Task 11 of 5b.** That commit exists but is partial: see §5.
2. **Catch engine failures,** the first item in §5.
3. **Do the interaction fixes.** [The interaction review](../reports/2026-10-31-tactics-overworld/interaction-review-5b.md) has a ruling on each item. Do every **Do** item before 5c, with tests.
4. Build 5c from its plan.
5. Build 5d from its plan.
6. Review the whole branch deeply, with one independent reviewer per area (rules engine, adapter, TV, phone, content, tools), then one fix wave.
7. Update `docs/superpowers/reports/2026-10-31-tactics-tv-scene.md` and add an overworld report, with open items ranked.

**How the work was run, and what paid off:** a fresh implementer per task or pair of tasks, then an independent reviewer told not to trust the implementer's report, then a fix round, then a scoped re-review. Reviewers probing real behaviour found far more than reading did. Keep these:

- **Model tests:** `WalkModelTests` runs random events against invariants. Raise it with `WALK_MODEL_EVENTS`. It found rotation electing an absent member and the crash in §5. Do the same for 5d's scene states.
- **Pictures:** compose or render and compare; frozen shots missed the overlay bug, which only showed in motion.
- **Mutations:** break a rule and check a test fails.
- **An interaction reviewer per milestone,** thinking as friends on a sofa, on top of code review.

## 5. Known bugs and open threads

- **Do this early: an engine failure can end a room and poison a save.** Nothing catches a `RulesInvariantException`, so `GameSession.Guard` reports "Game crashed" and the room returns to the lobby. Rules-engine spec §8 says the adapter should catch it, keep the prior state and show "Something went wrong with that move", and that was never built. The fatal command is lost, `EndBattle` never runs, and a save can stay poisoned: on seed 108, without the fix below, reopening with one player crashed 3 times out of 3, because the AI replayed the same move. Build what §8 says, with a test, and make sure an AI that hits it can't loop forever: skip or end its turn.
- **Fixed, the "share the tile" crash** (`Objectives.End` dropped queued `MoveStep`s without running `Halt`, leaving a walker on a Downed ally's tile). An engine fix with regression tests in `ReactionTests.cs`. Invariants run only in Debug, and the TV and `dotnet run` are Debug. The final beat gains one existing `Moved` event.
- **The sweep isn't finished.** Across 16,200 battles each, 5 crashes without the fix and 0 with it, but those runs used different battles, so that's suggestive and not proof. All 5 were this bug, on river_crossing (2), crypt_hall, ford_assault and forest_ruins. The deterministic harness is `docs/superpowers/reports/2026-10-31-tactics-overworld/battle-sweep-harness.cs.txt`. It needs two temporary patches, not committed: a `public static bool ZzNoFix` in Objectives with `while (!ZzNoFix && ...)` in `End`, and an `e.Data["outcome"]` catch around `Invariants.Check` in `Battle.Settled`. Run with `SWEEP_BOTH=1 SWEEP_SEEDS=100 SWEEP_OUT=<path>`: about 85 minutes on 14 threads. Run it after your own engine changes if you have the cores.
- **A second overlap remains:** `LastFree` can go stale if something spawns onto the tile a walker just left. Only `dragon_wakes` in `hall_of_the_flame`, with its `hpBelow` branch, might do this in the base pack. Choosing a fallback tile is a rules decision, so it's on the user's list.
- **Battles that don't end:** about 1.4% of sweep battles hit the 1,500-iteration cap, mostly a passive hero against archers that Dodge forever at range. Not a hang, because a player who moves ends it. Worth a line in the AI's notes.

- **Task 11 is partial.** `bots.cjs` has `battles` by default on the walk screen, plus `--walk`, `--walk-latency`, `--walk-flood`, `--walk-drop=kill|stall`, `--tv-log` and `--host` (no TV).
    - **`tools/tv-e2e.sh` hasn't run since Muster began landing on `walk`.** `ARCBOX_GAME=tactics`, `ARCBOX_RECONNECT=1` and `ARCBOX_RESTART=1` are unproven. In a no-TV run the VIP sent `battles` and picked river_crossing, but the run timed out at 40 s with no turn logged, so the bots weren't seen playing. Check this first. It may be a real bug.
    - **Still to do:** add `ARCBOX_WALK=1` (Seq climbs, `--walk-drop` stops within the lease, the party is on the saved tile after a restart, a killed TV snaps back), document the modes in `games/Tactics/README.md`, and measure thumb-to-TV latency (target ≤ 100 ms; needs a TV). `--walk` hasn't been checked to really move the party. Press to host view with no TV: median 1 ms, worst 109 ms, unexplained.
    - **Measured, on loopback:** a killed phone raises disconnect after about 15 s, and a silent one after about 30 s. The party stopped about 560 ms after the cut in all 5 runs, inside the lease, and the lead passed at about 7.8 s by the silence rule. **So a vanished phone looks connected for 15 to 30 s, and 5d must not treat `connected` as fast.** A real phone's network may differ.
- **A followers lag, unexplained:** once, with a rate limiter flooded and stops being lost, followers sat about 8 tiles behind the leader. `--walk-flood` hasn't been run with a TV, and the TV has no position log yet. Add a dev line under `--walk-log`. Hypothesis from reading `WalkTrack.cs`, untested: a flood flickers `Held`, each short rest runs `Fan` and pulls followers off the trail, `Trail` zeroes `Pace` while `Offset` is over `Rejoined` (0.2), so followers can't catch up before the next rest. A pure test of that sequence on `WalkTrack` needs no TV, so try that first.
- **A flaky test, maybe:** one full run failed one unidentified test after Task 9. Three later runs and 3 after Task 10 were clean. If you see it, name it and fix it.
- **One late crash:** in a baseline run the TV died about 7.3 s in, around quit, with speech on. Not seen since it went off, but the count is small.
- **`Unable to create shader cache directory ShadowFrustumShaderRD/...`** appears in some filled-cache runs. Harmless so far, uninvestigated.

## 6. Notes that bind 5c and 5d

These came from reviews after the plans were written. The plans don't all say them.

**5c:**
- A saved battle no longer pins a region file, because `rules/world/` left the battle-save hash. A repaint that moves a stamp mid-battle won't restart the fight, so place that battle sensibly.
- A teleport is `_epoch++` with the legs cleared, never the clock. `Epoch` starts from `ctx.Now`.
- `Held` is false when the party is blocked, so it stands, and `Facing` shows the way.
- "Affected blocks rebuilt" has no API yet: `WorldBlock`'s items and boxes are readonly, probes are dropped after baking (`LitBoard.Build` can re-probe from `Lighting`), a sink also changes the next block's sides, and the world's `Lighting` is fixed, so a sunk prop's light stays.
- The world is hidden through the RenderingServer, not `Visible`, because showing it again re-recorded all 80 blocks. Keep that when `BoardCanvas` joins the layer.
- `WorldCamera` has `PartyReach` = 12, which outranks `EdgeMargin`, because a tile is 128 layout px wide. Use `Hold` in a fight.
- `party_spawn` is on the west bank, so a Parley party coming from the hills fades in rather than running through the goblins. That's in the 5c plan's default 3.
- `StoryOrder` in `Tactics.WorldTools` models 5d's beat order and resumes at `party_spawn[0]` after a win. 5c uses the leader's battle tile. Keep them consistent.

**5d:**
- `ReconsiderLeader`'s mid-scene guard goes in `WantedLeader`'s first arm, not at the top of `ReconsiderLeader`, or a leader who leaves mid-scene won't be replaced. A comment marks the spot.
- `StoryOrder` follows the 5d plan. If the order changes, change both. It reads only choice and beat `sets`, not node `sets`.
- After a Parley loss the far-bank "!" sits beyond the armed stamp. A step in restarts the fight, so nothing soft-locks, but hide or move the marker on a retry if it reads badly.
- The story check warns at export, and `BasePackRegionTests` fails on any warning.

## 7. Your environment

The local session was on Windows with an RTX 5080. Check what you have.

- **Godot 4.7.2 .NET** may be missing. If so, you can't run the TV. Do all pure work with tests, keep Godot-only changes small, and say plainly what wasn't seen. Don't claim a picture works from tests alone. List it for the user to check.
- **`tools/tv-e2e.sh`** has run its Tactics modes only on Windows. Its Linux path uses Xvfb and software Vulkan, and needs checking.
- **Don't install new packages.** `npm ci` from the lockfile is fine.
- **If the user's PC is on:** Godot windows go on their left monitor, `--position 160,100`, and never take focus: an uncommitted `src/ArcBox.Host/override.cfg` with `[display]` then `window/size/no_focus=true`, deleted afterwards. This doesn't apply on a cloud machine.

## 8. The user's list

**Gold and shops (#68), USER DECISION defaults used:** A party purse; B loot + battle wins + story; B2 starting gold 0; C healing 50 / greater 150 / fire 50 / smoke 25 (future oil 10); D sell at 50%; E no haggling; F any present player; G purchases to the buyer; H sell only own bag; I no bag cap; J chapter restock per line; K one Ashen Ford herbalist; L no other gold sinks. These are the issue's defaults, not new owner balance choices. See the shops design's implementation section and shops art JSON. Old saves remain v1 and old phone fixtures remain unchanged. Rules hash changes restart a saved battle once. Balance follow-up: pre-fight character remakes still replace their starting kit under the existing rules; selling starting items is enabled, so the owner should consider their interaction before tuning the economy. No remake or starting-kit balance change was made here.


**The user's answers, 2026-11-01** (these settle the matching items below):
- **Boss music:** Rime Tower also gets the boss theme and the boss victory fanfare. Ford Assault stays ordinary. (27) *To build.*
- **Losing (refined after comparing with BG3, Divinity, FFT, Fire Emblem, XCOM and Darkest Dungeon):** a lost fight's injuries heal, so the retry is fair and there's no death spiral. The penalty is **no XP for a loss** (this also closes the farming hole) and **consumables used in the lost fight stay spent**. Earlier injuries stay. Results after a loss shows no injuries, just: "You wake at the recovery point. Wounds tended — no XP. Potions used are gone." This is the user's rules change. (3, 18) *Built.*
- **Spawn clash, as built:** the clash is in the battle engine (`MoveStep.Halt`'s `LastFree`). The walker goes to the nearest free walkable tile measured from the tile it left, Manhattan distance, ties in tile order. No path or climb check: literally the nearest free tile.
- **No-clip lines:** captions only (`StoryVoice.Fallback = Captions`, already the default). (1) *Settled.*
- **Restart:** the saved line is spoken again after a server restart. (26) *Settled.*
- **End-of-battle walk halt:** confirmed. (25) *Settled.*
- **Bree Tallow's angry portrait:** regenerate it in Scenario to match her other moods. (20) *To build.*
- **The VIP can end the session from inside a scene.** (Review default.) *Being built.*
- **Unanswered votes keep the first option** at 45 s. (2) *Settled.*
- **Environmental tiles:** start a draft design doc now (no code), for the user to review. (6) *To write.*
- **TVs cache region data per file,** so an export only re-downloads what changed. (7) *To build.*
- **The vote countdown:** leave it; the server owns the deadline. (28) *Settled.*
- **A second question in a row alerts again.** (30) *Being built.*
- **Player colours:** each player picks their colour with a colour picker at character creation. It's used for their TV ring, vote dots and phone. This replaces "stable seat colours". (14) *To build.*
- **Scene cast:** every scene places its non-narrator speakers at the scene's spot in the world while it plays. (21) *To build.*
- **"Back to walking":** hidden when there's no world, using a new canWalk flag (a small contract change). (9, 29) *To build.*
- **Spawn clash:** a walker on a spawn's tile steps to the nearest free walkable tile. (8) *Built.*
- **World interactables (the user's request, 2026-11-02): built, engine only, no art.** 21 kinds (containers, locks, doors and gates, levers, readables, traps, hidden finds, pickups, shrines and wells, ambient barks); the design is in `docs/superpowers/specs/2026-11-02-tactics-interactables-design.md`. The TV draws stand-ins or markers until the user's art AI drops `interactables.*` art in (see the art JSON and `docs/tv-host.md`; check it with `--world-demo=things`). *Built.* Open choices, each with the default in use:
  1. loot goes to the leader's bag;
  2. a failed attempt is spent, and a failed disarm springs the trap;
  3. traps outside battle are harmless (a message and flags only);
  4. passive Perception uses the party's best score within 3 tiles;
  5. keys are story flags;
  6. shrines set flags only;
  7. hidden chests and crates don't block until found, and secret doors draw as wall until found;
  8. quick-action results show on the TV only;
  9. 25 sounds are missing (`chest_open`, `door_open`, `lever_pull`, `trap_spring`, …); each plays once its sfx id exists;
  10. Ashen Ford has no things yet; the test region and the demo hold them.
- **Character screens need much better explanations** (the user's note after reviewing the phone screens). Explain:
  - racial benefits (traits, ability bonuses, speed, senses, and what each means in play);
  - class benefits (role, hit die, proficiencies, key features by level, spellcasting);
  - stat benefits (what each ability score does: which attacks, saves, skills and spells it drives, and the modifier);
  - other details such as backgrounds, skills and subclasses.

  These must be viewable at any time: during creation (before and after a pick), and from the character sheet afterwards. Tap any race, class, stat, background or feature for its details. *To design and build.*
- **Character sheet layout** (Claude's notes from the same review): the spell picks repeat the whole list, with full descriptions, for each "Level N: choose k" group, which makes the sheet over 10,000 px tall at 2×. Show the picks and a "Change" button that opens the list. Also add a live look preview (sprite and portrait) to creation, which is where the colour picker goes. *To build, with the item above.*

Keep this list, add to it, and put it at the top of your final report. Don't stop work for these.

**Decisions:**
1. Speech fallback for lines with no clip: a Google TTS key, or captions only. The local session leans towards captions only. 5d has one switch, `StoryVoice.Fallback`.
2. Unanswered votes take the first option, so nobody touching a phone kills Mother Skarrow. The rule stands with a 45 s timer.
3. ~~Losing pays half XP every time, so retrying can be farmed.~~ Settled by the user, 2026-11-01: a lost fight pays no XP, its injuries heal (earlier ones stay) and the consumables used in it stay spent.
4. `hall_of_the_flame` is 16 px short of drawing at 2×, on the darkest map.
5. The balance decisions in the rules-engine tracker.
6. **Environmental tiles:** the user wants tiles with effects that interact with spells, such as water and lightning. Hazards stay harmless until this is designed with them. Bring it up when 5d finishes.
7. A deployed region export makes each TV re-download all 125 MB. Per-file caching would fix it.
8. If a spawn lands on the tile a walker just left, where should the walker go: the nearest free tile, or stay and allow the overlap?
9. "Back to walking" is a dead button on a pack with no region. (Results' "Next battle" is now "Carry on", a label only, `51505fc`.)
10. **A battle the engine can't continue is called off with nothing won or lost**: no XP, injuries, Results or checkpoint, and the party goes back to the world. The alternative is counting it as a loss: `CallOffBattle` is the one place to change. (`0eec931`)
11. **Rules invariants run only in Debug builds.** On a Release server a broken state (the share-the-tile bug, say) won't throw: it's kept and saved. The checks are cheap; turning them on for production is the user's call.
12. **The pad from e/w:** a thumb already holding e or w keeps it out to 72°, so sliding round onto a road switches 8.6° off the road rather than 10°. Guaranteeing 10° would shrink e/w from 24° to about 21°. (`51505fc`)
13. **The battle list hidden from normal play**, as the user asked: `Tactics:BattleList`, Development only, off by default. A pack with no region keeps its list. The bots and `tv-e2e.sh` turn it on. (`f681da2`)
14. **Seat colours: superseded.** Each player now picks a colour at character creation, drawn on the TV (walker and battle rings, vote pips, check-card stripes; ac7a937) and phones. History: The room has no stable seat to share (`PlayerSlot.JoinOrder` repeats after a leave and never reaches the game), so the TV's walker rings stay the party green with a dark outline. Giving the TV and phone one seat number needs an SDK and lobby protocol change; 5d's vote dots need the same thing. The phone's palette is also weak for deuteranopia (sky against violet) and tritanopia (sky against green).
15. **Fleeing lands outside the stamp**, usually where the fight began, not at the stamp as the 5c plan said: standing in an armed stamp is what the landing rule forbids.
16. **A player whose turn comes first** gets their 60 s only after the walk-in (at most 3 s).
17. **Losing a lead to a blip:** a stand-in always keeps the lead at least 3 s after it's handed over, and the session leader takes it back after the party has been stopped for 3 s.
18. **A lost fight leaves the party weaker for the retry.** Injuries stack and potions stay spent. After one loss of Ford Assault, the bots lost most retries (14 of 15 seen; one drop-signals run won its retry), so "a loss re-arms the stamp" is a soft net and a real group could spiral. Settled with decision 3 (2026-11-01): a loss now pays no XP and heals its own injuries; consumables stay spent.
19. **Ford Assault is hard for bots:** they win River Crossing 40 of 40 fresh, but Ford Assault about 5 of 10 from the "!" side, 4 of 20 from elsewhere. A balance question, if it matters.
20. **Bree Tallow's `innkeeper_angry` portrait reads as a bald man.** It needs new Scenario art, which needs the user's OK.
21. **Scenes with no one standing in the world:** only the beat with the "!" puts its figure in the world (default 12), so Mother Skarrow isn't shown during the parley, and nobody is at the bridge in "Mother Skarrow lies where she fell".
22. **Story wording and choices the night made:**
    - a question already asked in a scene isn't offered again, which stops hubs looping when nobody answers;
    - an all-check choice nobody answers spends a roll;
    - the question line can't be skipped, and a skip is ignored for 0.6 s after a new line;
    - votes and roller picks send on the first tap and can change until the close;
    - a leader's choice made before a lead change stands, and the new leader can change it;
    - away players can be picked to roll;
    - the chosen roller's card says "Rolls for the party";
    - panel words such as "Party vote", "Leader's choice", "Tie-break" and "Waiting for a phone to come back";
    - the chapter card isn't saved, so a restart during it skips it;
    - checkpoints are named after the beat's first scene.
23. **Player colours are built** (see decision 14 and "The user's answers"). On a hero's own turn the ring keeps their colour, drawn wider, and the turn arrow marks whose turn it is; companions and enemies keep the gold turn ring.
24. **Server start loads the Tactics content first,** adding about 1.5 s to every server's startup, including Redline-only use. That stopped rooms being closed as hung while the pack loaded inside a room's first call.
25. **The end-of-battle walk halt is an engine behaviour change.** `Objectives.End` now halts walks still queued, which fixes the share-the-tile crash; a battle that ends mid-walk gains one `Moved` event before `BattleEnded`. No numbers changed. §2 says rule changes need the user's sign-off, so please confirm.
26. **After a server restart, the saved line is spoken again** (5d plan: "the saved line replays"). The "never replay a voice line" rule was written for a TV that rejoins; please confirm a server restart is an exception. When the phones reach a restarted server before the TV, that line is only shown, never spoken.
27. **Boss music:** only warlord_bridge, keep_gate and hall_of_the_flame get boss music and the boss victory fanfare. `rime_tower` (Ivo Rime, Chapter 4's climax, with a phase change and voiced lines) and `ford_assault` (Mother Skarrow) play the ordinary themes. Should either get them?
28. **The vote countdown can run up to the network delay behind the server.** The server still owns the deadline. Acceptable, or should the server also send its own "now"?
29. **"Back to walking"** shows on the battle list even in a pack with no region, where it does nothing. Hiding it needs a new field in the screen's data (a contract change).
30. **Two questions in a row:** after the fix wave, the second question plays the "your turn" sound and arrival again. Say if it should stay quiet.
31. **Chapter e2e runs on the force branch are about a coin toss** (the bots lose most Ford Assault retries). Keep them manual, or gate them somewhere?

**Defaults the review fix wave adds:** the VIP can end the session from inside a scene (the cursor is saved on every line); a resumed battle keeps the stage it was saved with, and a repainted region that no longer matches puts it on the plain board.



**Defaults added tonight, which the user can overrule:** the edge note ("The road goes on, but not today.") once a session; a one-tile jag with both sides open doesn't slide; quick taps latch one step; the pad's sectors (n/s 60°, roads 48°, e/w 24°); the leader's arrival pulse on opening the walk; a called-off battle wins and loses nothing; the adapter catches any engine exception but out-of-memory; followers rejoin the trail at its nearest point after a rest; the title card crossfades and gives up after 10 s; the wall bump is 0.12 tile over 0.15 s with the step sound at -14 dB.

**Ideas the user wants noted for later design** (not in this slice; don't build without them):
- **Mobs out in the overworld:** wild enemies roaming the regions, away from the stamped story battles. Open questions: how often fights happen, whether they pay XP (decision 3's farming problem), which encounters and maps they use given that battles need a matching map, and whether the party can avoid them. A cheap visual step that changes no rules: the armed stamp's enemies idle or pace inside their area before the party arrives.
- **Fog of war in the overworld:** the region hidden until the party explores it. Open questions: whether explored ground stays revealed across sessions (it'd live in the campaign save), how far the party sees (and whether heights block sight), and how it reads on the TV from the sofa.

**From merging world dressing (2026-11-02, 227e39b), for the user:**
- **A second crossing.** The new Ashen Ford has a walkable bridge deck at (105..108, 48), north of the ford stamp, so the ford is no longer the only way across. The Ashen Ford handoff says the ford must stay the only crossing, but that branch's own tests assert the deck is walkable. Kept walkable for now; say which you want.
- **Fixed in the merge:** the tip failed 12 tests and the lore check on its own branch. The causes were new art sizes stretching old prop variants (a variant can now have its own `looks`), a fence inside the reeve's cottage, and two bestiary subjects that aren't monsters.
- **Moved to fit the new map:** the start (30,65)→(26,58), recovery (30,55)→(31,57), `ch1_mayors_request` →(33,59), `ch1_granary` →(42,70), `ch1_skarrow_song` →(133,24).
- **Interactable sounds:** all 25 exist now (601755a, 116 CU).

**Hidden things and passive checks (2026-11-02, 62f4fc2..fc88986), for the user:**
- **Defaults to overrule:** USER DECISIONs 10–17 in the interactables design doc, §7.7:
  - all eight passive skills can find things;
  - a notice reveals its targets wherever they are;
  - range 1–6 tiles;
  - hidden-path rules;
  - Search is a check scene;
  - Search looks within 2 tiles, and a miss can't be retried;
  - Search costs nothing;
  - the DC is never shown.
- **Your call:** should Search also find traps nobody has spotted? Today it only looks for things marked `hidden`.
- **For a TV pass:** an unfound hidden path, and a secret door more faintly, leave a seam in the wall they hide in, because the plug's front face doesn't match its neighbours'.
- **Art:** nine new keys (`interactables.<notice>.noticed`, `interactables.hidden_path.found`) plus `fx.notice`, with prompts in `docs/superpowers/specs/2026-11-02-tactics-interactables-art.json`. No `notice` sound yet; it falls back to `secret_found`.

**Ashen Ford animals, fences and things (2026-11-02, abe55bb..aab5302), for the user:**
- **Animals:** the pack has a mule, goat, fox, duck and crow. There's no sheep or dog on `world-dressing-design` (a94d7e3); if the local session made them, they aren't pushed yet.
  - The strips came out about 3× too big, so a `scale` in the props manifest now shrinks them at load: mule 0.38, goat 0.33, fox 0.30, duck 0.25, crow 0.20. Remove it once redrawn art lands.
  - They now keep to one height level, stay off water (ducks stay on it) and stay in their pens.
  - The art AI prompt is in `docs/superpowers/specs/2026-11-02-world-animals-art-prompt.md`.
  - **For a TV pass:** animals still draw over every prop in their block. For now they're kept away from the tile behind a prop; the real fix is drawing them as units.
- **Fences:** ten spots fixed, with rails on their own field's level, no doubled rails, gates that open onto lanes, and no leaks. A test guards against fences on cliffs, lips, roads and doubled rails.
- **Things:** 17 in `rules/interactables/ashen_ford.json`.
  - There are no doors, because the building art's doorways open onto blocked footprints.
  - Loot: one Potion of Healing in the reeve's chest and one Smoke Bomb in the Ash Market cache. Your call on balance; it's decision 1 in the interactables design doc.
- **Protocol, your call:** each explore view now carries every visible thing, about 2.9 KB in Ashen Ford, past the 1,420-byte one-packet budget. The fix is to send things only when they change, or only those near the party.
- **Saved battles restart once:** listing the new file changes the battle hash.
- **Flaky test:** `ThingShowTests.Fifty_things_cost_a_frame_little_and_allocate_little`, a 1 ms per-frame timing check, now fails often under full-suite load and passes alone. Make it measure work, not wall time.

**Defaults the user can overrule:** the 8-way pad; the session leader taking the lead back once stopped; hazards harmless; the loader refusing a recovery spawn below a one-way drop; lines advancing by themselves; one beat at a time; skill rolls with no natural 20 or 1; a skill-to-stat table of 18 SRD lines; `ch1_kindler` having no `place`.

**Only the user can check:**
- Painting in the real Godot editor against `tools/tactics-world/README.md`, whose editor steps are from memory, plus a workspace screenshot and time taken (spec §7).
- The pad on a real phone: vibration, iOS Safari, address bar, locking mid-walk, the back gesture.
- How it sounds: footsteps at -18 dB, `town_emberwick`, the board's audio levels.
- Sofa distance on a real TV, and a modest TV box. Performance is only measured on an RTX 5080, Debug build.
- The Ash pattern repeats every 4 tiles. A low-frequency vertex tint is the cheap fix to try in polish.
- Whether the wall bump (about 5 px at 720p) reads from the sofa, and how the 3 s takeback, latched taps and jag slides feel.
- Followers briefly overlap on quick U-turns (seen in Task 11's shots).
- Responsiveness on a real phone over Wi-Fi with a GPU TV. Here, press to the TV's step was 24–35 ms median, mostly the software renderer.
- Whether the 24° e/w pad sectors feel too thin.
- Story pacing and voices: every playthrough tonight skipped lines after 1.5 to 2.5 s.
- Whether the 11-option vote panel's smallest rows (about 14 px at 720p) read from the sofa.
- The walk-in on a GPU: a hero who waits a step to stay apart, and the faint grid on grass.
- Whether the 0.6 s skip guard, instant-send votes and the d20 spin feel right on real phones.
