# Wardenflame (Tactics): the overworld, 2026-10-31 to 2026-11-02

The party now walks Ashen Ford on the TV, steered from the leader's phone. Story beats play as scenes with votes and skill checks. Fights start where the party stands and are drawn in the world. All of Chapter 1 plays end to end, by either branch, through server restarts and a killed TV.

This covers branch `ccr-18ad4510-q68nad` from `99ea2dd` (main) to `48030c5`. No PR is open. The handoff that ordered the work is [2026-10-31-tactics-overworld-cloud-handoff.md](../plans/2026-10-31-tactics-overworld-cloud-handoff.md). Its §8 list is restated below, brought up to date with what's built.

Almost everything was seen only in a cloud container, with software rendering. The one exception is the user's Windows check at `213ede0`. Nothing has been played on a real phone, on a sofa, or with sound on.

---

## The user's list

### Decisions the user made, and what was built

| # | The user's answer (2026-11-01) | State | Commits |
|---|---|---|---|
| 3, 18 | **Losing:** a lost fight pays no XP. Its own injuries heal, earlier ones stay. Consumables used in it stay spent. Results say "You wake at the recovery point. Wounds tended — no XP. Potions used are gone." | Built | `de243a8`, `5aa588a` |
| 8 | **Spawn clash:** a halted walker whose last free tile was taken steps to the nearest free walkable tile from the tile it left (Manhattan distance, ties in tile order, no path or climb check) | Built, in the engine | `b91b43a` |
| 27 | **Boss music:** Rime Tower gets the boss theme and the boss fanfare. Ford Assault stays ordinary | Built | `9011bdb`, `e502f69`, `8620e2e` |
| 14 | **Player colours:** each player picks a colour at character creation. It shows on their TV ring (walking and in battle), vote pips, check card and phone. This replaces "stable seat colours" | Built | `866ca1b`, `2ccc1e5`, `9c5803f`, `ac7a937` |
| — | **Character screens:** every race, class, background, ability score, skill, subclass, feature and spell opens its details, during creation and from the sheet. Plus a live look preview, a colour picker, and the sheet's picks collapsed behind "Change" | Built | `866ca1b`, `f7b870d`, `9c5803f`, `2ccc1e5`, `f7a7b7d` |
| 7 | **TV content cache per file:** an export re-downloads only what changed | Built | `69e993c` |
| 21 | **Scene cast:** every scene's non-narrator speakers stand at the scene's spot while it plays | Built | `a8fd529`, `8a5afce`, `3573ea4` |
| 9, 29 | **"Back to walking"** is hidden when there's no world, using a new `canWalk` flag (a small contract addition) | Built | `d026f2f` |
| 20 | **Bree Tallow's portraits:** fix the angry one to match her other moods | Built, **but not through Scenario.** Her preset was built as a bald man with a moustache, so all six moods were wrong. The recipe now builds a stout woman with a chestnut bun, rebuilt with `build.py`. Please look at it | `3162074` |
| — | **The VIP can end the session from inside a scene** (a review default the user accepted) | Built, server and phone | `1f11d7e`, `44e96b2`, `4f4f78e` |
| 30 | **A second question in a row alerts again** | Built | `9afcc84`, `30057c1` |
| 6 | **Environmental tiles:** a draft design, no code | Written, awaiting answers | `9514024` |
| 13 | **The battle list is hidden from normal play** (`Tactics:BattleList`, Development only, off by default; a pack with no region keeps its list) | Built | `f681da2` |
| 1 | No-clip lines: captions only (`StoryVoice.Fallback = Captions`) | Settled, the default | — |
| 2 | Unanswered votes keep the first option at 45 s | Settled | — |
| 25 | The end-of-battle walk halt (an engine behaviour change) | Confirmed | `96ea82e` |
| 26 | After a server restart the saved line is spoken again | Confirmed | `410c6fe` |
| 28 | The vote countdown stays as is; the server owns the deadline | Settled | — |

### Decisions still open

| # | Question | Notes |
|---|---|---|
| 4 | `hall_of_the_flame` is 16 px too tall to draw at 2×, so the finale draws at half size | Use the top strip, or trim the map |
| 5 | The balance decisions in the rules-engine tracker | Untouched |
| 6 | Environmental tiles: the draft's [11 open questions](../specs/2026-11-01-tactics-environmental-tiles-draft.md#8-open-questions-for-the-user) | Nothing is built until they're answered |
| 10 | A battle the engine can't continue is called off with nothing won or lost, and the party goes back to the world. The alternative is to count it as a loss | `CallOffBattle` is the one place to change (`0eec931`) |
| 11 | Rules invariants run only in Debug builds. On a Release server a broken state is kept and saved, not caught | The checks are cheap. The TV and `dotnet run` are Debug today |
| 19, 31 | Ford Assault is hard for the bots: about 5 of 10 won from the "!" side, 4 of 20 from elsewhere. So a force-branch chapter run is about a coin toss | A balance question, if it matters. `ARCBOX_BRANCH=skip-assault` now runs the chapter without that fight. Keep chapter runs manual, or gate them? |
| — | Install Playwright so REDLINE's e2e can run on Windows | From the hardware check. `web/play/package.json` doesn't list it |

### Defaults the user can overrule

**Walking**
- The 8-way pad, with unequal sectors: n/s 60°, road directions 48°, e/w 24°. A thumb already holding e or w keeps it out to 72°, so a slide onto a road switches 8.6° off the road, not 10° (decision 12).
- Quick taps latch one step. A one-tile jag with both sides open doesn't slide; one with exactly one side open does.
- The wall bump: the leader leans 0.12 tile over 0.15 s, with the tile's step sound at -14 dB.
- The edge note "The road goes on, but not today.", once a session.
- The leader's arrival pulse on opening the walk.
- Followers rejoin the trail at its nearest reachable point after a rest.
- The session leader takes the lead back only after the party has been stopped 3 s, and a stand-in always keeps the lead at least 3 s (decision 17).
- The title card stays up until the world is revealed, and gives up after 10 s.

**Fights**
- A called-off battle wins and loses nothing (decision 10).
- The adapter catches any engine exception except out-of-memory.
- Fleeing lands outside the stamp, usually where the fight began (decision 15).
- A player whose turn comes first gets their 60 s only after the walk-in, at most 3 s (decision 16).
- A resumed battle keeps the stage it was saved with. If a repaint no longer matches, it plays on the plain board. A fight begun on a plain board stays plain.
- Hazards are harmless.
- The loader refuses a recovery spawn below a one-way drop.

**Story** (decision 22)
- Lines advance by themselves. One beat at a time.
- A question already asked in a scene isn't offered again, so a hub can't loop.
- An all-check choice nobody answers spends a roll.
- The question line can't be skipped. A skip is ignored for 0.6 s after a new line.
- Votes and roller picks send on the first tap and can change until the close.
- A leader's choice made before a lead change stands; the new leader can change it.
- Away players can be picked to roll. The chosen roller's card says "Rolls for the party".
- Skill rolls have no natural 20 or 1. The skill-to-stat table is the SRD's 18 lines.
- The chapter card isn't saved, so a restart during it skips it.
- Checkpoints are named after the beat's first scene.
- Panel words: "Party vote", "Leader's choice", "Tie-break", "Waiting for a phone to come back".
- `ch1_kindler` has no `place`.

**Colours**
- A player who never picks a colour gets one once, in join order, skipping colours others have, and keeps it.
- A picked colour is lightened until it reads on grass, stone, ash and the navy panels.
- On a hero's own turn their ring keeps their colour, drawn wider, with the turn arrow. Companions and enemies keep the gold turn ring (decision 23).

**Server**
- The server loads the Tactics content before it listens: about 1.5 s more at every start, Redline-only use included (decision 24, `7e2144b`). Before this, rooms were closed as hung while the pack loaded.

### Only the user can check

- **On a sofa, on a real TV:** whether the walker rings, vote pips (the 11-option panel's smallest rows are about 14 px at 720p), the wall bump (about 5 px at 720p), the faint grid on grass and the check cards read. Whether the world's bright ground overpowers the small figures in a fight.
- **On real phones:** the pad (vibration, iOS Safari, the address bar, locking mid-walk, the back gesture), whether the 24° e/w sectors feel too thin, the 0.6 s skip guard, instant-send votes, the d20 spin, and the colour picker. Latency over Wi-Fi.
- **Sound:** footsteps at -18 dB, `town_emberwick`, the board's levels, the new victory, defeat and boss fanfares, and the voiced lines. The Windows check used a dummy audio driver, so no real audio device has been exercised.
- **A real TV box or modest GPU:** performance is measured only on an RTX 5080 (Debug, and an editor-run "Release"). No true Release export exists.
- **Feel:** the 3 s takeback, latched taps, jag slides, the rejoin after a rest, followers overlapping on quick U-turns, the walk-in on a GPU (a hero who waits a step to stay apart), and story pacing. Every playthrough so far skipped lines after 1.5 to 2.5 s, so no one has heard a scene at its own pace.
- **Art:** Bree Tallow's rebuilt portraits (`3162074`). The Ash pattern repeating every 4 tiles: a low-frequency vertex tint is the cheap fix.
- **Painting** in the real Godot editor against `tools/tactics-world/README.md`, whose editor steps are from memory.
- **On Windows, not yet run there:** `ARCBOX_STAGE=1` and `ARCBOX_CHAPTER=1` (both added after `213ede0`), a fight started by walking into a stamp, and a raised or sunk tile repainting the world.

### Ideas noted for later (not built)

- **Mobs in the overworld:** wild enemies away from the story's stamps. Open: how often, whether they pay XP, which maps they use (battles need a matching map), and whether the party can avoid them. A cheap visual step that changes no rules: the armed stamp's enemies idle or pace before the party arrives.
- **Fog of war:** the region hidden until explored. Open: whether it stays revealed across sessions (it'd live in the campaign save), how far the party sees and whether heights block sight, and how it reads from the sofa.
- **Environmental tiles:** [the draft](../specs/2026-11-01-tactics-environmental-tiles-draft.md) proposes lightning through water, fire on fuel, freezing shallow water, an oil flask, fire burning webs, and water putting out burning, on the engine's existing zones, opt-in per encounter. 11 open questions, milestones M1 to M6.
- **The BG3 gap list:** the user's world-dressing spec (branch `world-dressing-design`, `816798f`, plan `7be3f75`) names four steps towards Baldur's Gate: world dressing (props, ground detail, ambient life), inventory with looting and Perception, NPCs and side quests, and reactive surfaces. Not on this branch. I didn't find a separate gap list beyond that spec's opening.

---

## What was built

Commits are listed in order. Each milestone was built by a fresh implementer, then checked by an independent reviewer told not to trust the implementer's report, then fixed and re-reviewed.

| Milestone | What it does | Key commits |
|---|---|---|
| **5b: walking** (Tasks 1 to 10, from the local session) | The world as one `BattleMap`, the 8-way walk, saved place, the phone's pad, the world drawn in 16-tile blocks, the party and camera | `93c0555`, `986d35d`, `3a4a1a4`, `ab319a8`, `0aab0b4`, `52d5e04`, `b1e3222`, `69dbace`, `69852bf` |
| **5b Task 11: bots and e2e** | `bots.cjs` walks (`--walk`, `--walk-latency`, `--walk-flood`, `--walk-drop`). `ARCBOX_WALK=1`. `WALKAT` lines. The followers lag confirmed and fixed. The Tactics modes passed on Linux for the first time | `229d110`, `80a9aae`, `7533924`, `fe54492`, `d1fe4ae`. [Report](2026-10-31-tactics-overworld/task11-bots-e2e.md) |
| **The engine-failure catch** | An engine exception no longer ends the room. The prior state stands, the TV says "Something went wrong with that move.", an AI that fails ends its turn, and after repeated failure the battle is called off. Each fault is logged with its state | `0eec931`, `2ad6a63`, `1a7784e` |
| **5b interaction fixes** | Every **Do** item of the [interaction review](2026-10-31-tactics-overworld/interaction-review-5b.md): road-shaped pad sectors, the wall bump, the 3 s takeback, the first-lead alert and hint, the TV's "Bo leads now.", "Carry on", jag slides, latched taps, the edge note, a battle confirm, and the title held while the world loads | Phone `51505fc`, `202e894`, `c804f89`. Server `ebc9c4f`, `4ff1036`. TV `a26f87a`, `eccdedb`, `92cc416`, `706ca99`, `daa0740`, `7de8659` |
| **5c: fights on the spot** | Stepping into the armed stamp starts its fight one tile short. A win marks the beat done. A loss wakes the party at the recovery spawn and re-arms the stamp. On the TV, `StagePlan` builds the board from the world's own tiles, and `StageDirector` runs the walk-in, the fight, Results over the world, and the fall-in or waking | `d2cb7a3`, `f681da2`, `387d97f`, `fff56b9`, `2174338`, `36b9c4e`, `ecbe1ce`, `1ebcc0b`, `58eb02d`, `72dc6be` |
| **5d: the story runtime** | Scenes loaded and checked, walking a scene, votes and rolls, beats that start and lines that run on the server's clock, votes and skill checks, battle beats with lead-in and aftermath, travel, the chapter card, restarts. Phone story screens. The story on the TV: caption with portrait, vote and check panels, d20, the chapter card | `24672aa`, `9178079`, `0692cbc`, `cc3980e`, `4a7c4e6`, `a2a4b47`, `f603536`, `18887da`, `cc6e228`, `4f2fce2`, `3977633`, `884742e`, `ef733c3`, `213ede0`, `c967b4e`, `4995632`, `047c81a`, `c252c57` |
| **Chapter 1, end to end** | Game tests walk a fresh campaign to each "!" by either branch, with a restart at every moment of every scene. The bots' `--chapter` and `ARCBOX_CHAPTER=1` play it on the real TV | `3ff4e90`, `ff76e6e`, `3bbd3d5`, `b729090`, `410c6fe`, `b6234fa`, `1893d50`, `2c681d5` |
| **Review waves** | The hardware check's problems, then the whole-branch review (one reviewer per area) and its fix wave | Hardware: `767e0b2`, `31d793d`, `61f07e7`, `354d4cf`, `c35bef7`, `584d9bd`, `60225ba`. Branch review: `6891e06`, `b1c8d28`, `3b01bc0`, `fd8f216`, `51814f5`, `85026aa`, `03308a6`, `01e175c`, `c78c784`, `9afcc84`, `e28e6b5`, `d84e699`, `9e3aee3`, `8620e2e`, `4dde20d`, `2d464a5`, `9dee9c9`, `be8ed1d`, `44e96b2`, `ab0424a` |
| **The user's decision batch** | Loss rule, spawn clash, Rime Tower boss music, `canWalk`, the VIP ending a scene, a second question alerting again | `de243a8`, `5aa588a`, `b91b43a`, `9011bdb`, `d026f2f`, `1f11d7e`, `4f4f78e`, `9afcc84` |
| **Character screens and player colours** | Details for every pick from the rules pack, each skill's roll and bonus, a live paper-doll look, a colour picker, collapsed picks. Colours on the TV's rings, vote pips and check cards. The campaign API's JSON is compressed (Brotli, gzip), as the portal views grew to tens of KB | `866ca1b`, `f7b870d`, `9c5803f`, `2ccc1e5`, `0f6d875`, `f7a7b7d`, `ac7a937` |
| **The TV content cache** | A file the TV already has, with the SHA-256 the index lists, is copied and verified instead of fetched. Cold: 4,309 files, 122 MB. Warm restart: 0 requests. One region file changed: 1 file (76 KB) plus the 588 KB index | `69e993c` |
| **Storage robustness** | A failing disk keeps its writes and retries them with back-off (0.5 s doubling to 30 s), never dropping or stalling a room. Found when a chapter e2e hung on a full disk | `d0d6c21`, `e0f0a12` |
| **The scene cast** | A scene's speakers stand near its spot, appearing only once a line could reveal them, on tiles that survive a restart | `a8fd529`, `8a5afce`, `3573ea4` |
| **Bree's portrait** | The innkeeper preset rebuilt as a woman | `3162074` |
| **Other fixes** | The share-the-tile crash (`96ea82e`). The Tactics pack loads before the server listens (`370cb5a`, `7e2144b`). Two flaky tests fixed (`51f264b`, `3849fb1`, `9597005`). Orchestral victory and defeat stings and a boss fanfare (`e502f69`) | |
| **Docs** | The environmental tiles draft (`9514024`). The handoff's list, kept current (`3ec3451`, `6f41fd6`, `b70c7b4`, `3ac83dd`, `a4f5774`, `2a0e007`, `2283182`, `48030c5`) | |

---

## How it was verified

### Tests now

Measured at `48030c5` in this container (4 cores, Linux), Debug:

| Suite | Result |
|---|---|
| `dotnet test --solution ArcBox.slnx` | 4,887 passed, 0 failed, 0 skipped |
| `npm --prefix web/play test` | 461 passed, 19 files |

The build had 0 warnings. The web tests used the main checkout's installed `node_modules`. At the handoff it was 3,603 and 335. The Windows check at `213ede0` had 4,420 and 394.

Besides unit tests, the work leans on:

- **Model tests:** `WalkModelTests` (random walking, taps, blips, armed stamps) and the story model (restarts mid-roll, mid-line, on a tie-break, between scenes, sessions ended mid-scene).
- **Mutation checks:** most fixes record a break that made their test fail, in the commit message.
- **Every way through the campaign:** each scene of all five chapters is checked, and walked on all 144 branches (`0692cbc`, `cc3980e`).

### e2e runs on the TV

All on Linux in the cloud container: Godot 4.7.2 .NET under Xvfb with lavapipe (software Vulkan), 4 cores, server, TV and bots on one machine over loopback. The latest run of each mode passed:

| Mode | Result |
|---|---|
| `ARCBOX_GAME=tactics` | Passed. Latest on 2026-11-02 at 00:02, with player colours: a River Crossing win, victory music |
| `… ARCBOX_RECONNECT=1` | Passed: killed at beat 30, caught up to beat 36 |
| `… ARCBOX_RESTART=1` | Passed twice (Task 11) |
| `ARCBOX_WALK=1` | Passed 3 times in Task 11, and again on 2026-11-01 23:34 |
| `ARCBOX_STAGE=1` with `ARCBOX_RECONNECT=1`, at 1920×1080, and with `ARCBOX_SHOTS=0` | 4 runs, all passed: lost, woke at the recovery spawn, walked back in, won |
| `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force` with `ARCBOX_RESTART=1 ARCBOX_RECONNECT=1` | Passed twice |
| `… ARCBOX_BRANCH=parley` with both | Failed once, then passed. The failure was a real bug: the saved line after a restart wasn't voiced. Fixed in `410c6fe` |
| `… force ARCBOX_SILENT=1 ARCBOX_DROP_SIGNALS=1` | Passed: every line, vote and check on the game's timers |
| `… force ARCBOX_DROP_SIGNALS=1` | Reached its card with 291 signals dropped (a loss, then a win). Its log has no final verdict line, so **I can't confirm it passed** |
| Chapter, force and parley, with the scene cast | Parley passed. Force failed once: a bot skipped a line before the slow TV showed it. Fixed in the bots (`2c681d5`), then passed |
| `… ARCBOX_BRANCH=skip-assault` | Passed |
| `ARCBOX_CHAPTER=1` force, after player colours | Passed, 2026-11-01 23:56 |

**Not run since it changed:** `ARCBOX_RESTART=kill` (added in `2d464a5`): I found no log of a run. REDLINE's modes haven't been rerun on Linux or Windows.

### Screenshots from the TV

These are real Godot TV frames, not composed pictures, but drawn by software Vulkan. Judge them for content, not smoothness. They're in the session's scratchpad, `/tmp/claude-0/-home-user-ArcBox/96209855-791c-56f7-b997-7ec0e92b7063/scratchpad/`, which is temporary. A few walking shots are committed in [2026-10-31-tactics-overworld/](2026-10-31-tactics-overworld/).

| Folder (under the scratchpad) | What's in it |
|---|---|
| `e2e-r1`, `e2e-r5`, `e2e-r6`, `t11`, `e2e-walk` | Walking runs: the party walking, stopped, after a restart and a TV kill |
| `tv-fixes` | The 5b TV fixes: the title held while the world loads, the bump, the first ring colours (later reverted) |
| `5c-tv`, `5c-e2e` | Fights in place: walk-in, enemies stepping in, the grid, fighting, Results over the world, waking, the retry, a TV back mid-fight. `5c-e2e/bug-before-fix-walkers-stacked.png` is the bug `3d8d211` fixed |
| `e2e-win`, `e2e-next`, `e2e-reconnect`, `e2e-plain`, `e2e-ford`, `e2e-r2` | Seeded fights in place and on the plain board |
| `fixwave` | Four 1080p stage runs after the hardware fixes |
| `story-tv`, `demo1`, `demo2`, `run-ford`, `run-ford2`, `run-mayor` | The story on the TV: captions, votes, checks, from `--story-demo` and real runs |
| `chapter1`, `e2e-parley-1`, `e2e-parley-2`, `e2e-force-1`, `e2e-force-2`, `cast-parley`, `cast-force`, `cast-force2`, `e2e-chapter` | Chapter 1 runs: every scene, question, roll, fight, results, the card, the walk after it |
| `scene-cast` | The cast standing in the world (Mother Skarrow at the parley, nobody before a line reveals them) |
| `tv-colours` | Player colours on walker rings (grass, dirt, stone), battle rings on a hero's turn and an enemy's, vote pips, check cards |
| `e2e-battle` | A plain battle, the latest run |

Phone screens, from a browser against the real server, not a phone: `phone-fixes`, `story-phone`, `a11y-shots`, `charscreens`, `charscreens2`. Bree's portraits before and after: `bree/bree-before-after.png`.

### The hardware check

The user's Windows run at `213ede0` (`git show origin/local-hw-checks:docs/superpowers/reports/2026-10-31-tactics-overworld/local-windows-checks.md`): Windows 11, RTX 5080, 1920×1080 `ultra`, capped at 144 fps.

- **Every Tactics mode passed:** plain, `WALK`, `RECONNECT`, `RESTART`, and a seeded fight in place. `ARCBOX_STAGE` didn't exist yet. REDLINE didn't run (no Playwright).
- **Performance:** walking and fights held 144 fps. Walking median 6.94 ms a frame, 0 of 4,608 frames over 16 ms in Debug. No hitch at a fight's start or end. First world load 194 to 235 ms off the main thread. Press to the TV's `WALK` line: 4 to 5 ms median. Texture memory 38.2 MB.
- **Problems found, and what became of them:**

| Problem | Fix |
|---|---|
| 1. `A_rebuild_on_the_largest_region_is_quick` failed in Release (a timing ratio) | Checked by tiles read instead: `767e0b2` |
| 2. REDLINE needs Playwright | Open, the user's call |
| 3. Screenshots stall frames | `ARCBOX_SHOTS=0`: `c35bef7` |
| 4. A TV back mid-fight logged late blocks and used 55.6 MB | The world waits until the cut's blocks are recorded, and the plain board lets its art go: `584d9bd`, `60225ba`. On Linux at 1080p: 41.5 MB before, 28.8 MB after. The late blocks never happened on Linux, so the fix is **unseen on the GPU** |
| 5. "-102": a hit's "-10" beside a Downed "2" | `31d793d` |
| 6. Heroes sharing a tile in the walk-in, and bars during it | `61f07e7`, `60225ba` |
| 7. Marks under the Results table, and an over-tall table | `354d4cf` |
| 8, 9. Faint grid on grass; bright ground against small figures | Sofa checks, unchanged |

---

## Known issues and follow-ups

Ranked by how much they matter to a first real session.

1. **Nothing has been played by people.** Sofa, real phones, sound and feel are all on the user's list above.
2. **Ford Assault is close to a coin toss for the bots** (decisions 19 and 31). With the new loss rule, retries after a cheap loss were won 8 of 12. A real group may do better or worse.
3. **Rules invariants are off in Release** (decision 11). A broken state would be saved, not caught.
4. **The battle sweep wasn't rerun after the engine changes** (`96ea82e`, `b91b43a`, the loss rule). The handoff's 16,200-battle comparison was suggestive, not proof. The harness is `2026-10-31-tactics-overworld/battle-sweep-harness.cs.txt` (about 85 minutes on 14 threads).
5. **About 1.4% of sweep battles hit the 1,500-iteration cap,** mostly a passive hero against archers that Dodge at range. Not a hang. Worth a line in the AI's notes.
6. **Followers overlap on quick U-turns,** for a moment, while moving. At rest they fan out.
7. **The Windows-only fixes are unseen on a GPU:** the mid-fight takeover hold (`584d9bd`), the walk-in that keeps heroes apart (`61f07e7`), the results' marks (`354d4cf`).
8. **`ARCBOX_RESTART=kill` hasn't been seen to pass.** Nor has the force `ARCBOX_DROP_SIGNALS=1` chapter run reached a verdict line.
9. **Chapter II isn't there.** After Chapter 1's card the party walks freely, and the TV says "Chapter II isn't here yet".
10. **The TV still draws a generic class sprite,** not each character's look. The phone composes the real look, but the TV doesn't.
11. **REDLINE e2e:** not rerun since the TV start moved into a function; needs Playwright on Windows.
12. **One press of 40 made no step** on Windows (`LATENCY press 0: no step`), probably the party against a wall. Unconfirmed.
13. **`Unable to create shader cache directory`** appeared in some filled-cache runs here. Not seen on Windows. Uninvestigated.
14. **Interactables** are being built in another session on this branch, in their own files. Not covered here.

---

## How to run things

Set up once: `export PATH=/root/.dotnet:$PATH`, `npm --prefix web/play ci`, and `GODOT` pointing at the Godot 4.7.2 .NET binary (on Windows, the `_console.exe`).

**Tests**

```sh
dotnet build ArcBox.slnx            # 0 warnings
dotnet test --solution ArcBox.slnx
npm --prefix web/play test
```

**The TV end to end** (`tools/tv-e2e.sh`; [the Tactics README](../../../games/Tactics/README.md#bot-phones-and-the-tv-end-to-end) has every check):

| Run | What it does |
|---|---|
| `ARCBOX_GAME=tactics` | A battle from the list to its Results |
| `… ARCBOX_RECONNECT=1` | The TV is killed at `ARCBOX_AT_BEAT` (default 30) and relaunched |
| `… ARCBOX_RESTART=1` | The server is stopped cleanly at that beat and started again |
| `… ARCBOX_RESTART=kill` | The same, but the server is killed outright, as a crash would. Battle, stage or chapter runs only |
| `ARCBOX_GAME=tactics ARCBOX_WALK=1` | The party walks Ashen Ford: a restart, a TV kill, 20 timed presses, a cut leader's phone |
| `ARCBOX_STAGE=1` | A seed puts the party four steps from River Crossing's stamp. They walk in, lose, wake, walk back and fight again. `ARCBOX_ENCOUNTER=ford_assault` for the other branch. Combines with `ARCBOX_RECONNECT=1`, `ARCBOX_RESTART=1` or `ARCBOX_DROP_SIGNALS=1` |
| `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force\|parley\|skip-assault` | All of Chapter 1 from a fresh campaign. `ARCBOX_RESTART=1` restarts the server at line 3 of `ch1_maud`; `ARCBOX_RECONNECT=1` kills the TV at line 3 of `ch1_docks`. `ARCBOX_SILENT=1` (force) answers nothing. `ARCBOX_LINE` sets how long a line stays before the bot skips it |
| `ARCBOX_SHOTS=0` | Any mode with no screenshots. Use it with `TV_ARGS=--perf` for frame times: shots stall frames |

Other knobs: `ARCBOX_E2E_OUT` (output folder), `TV_RES`, `TV_PRESET`, `ARCBOX_SERVER_ENV` (Development settings such as a seed), `ARCBOX_BOT_ARGS`.

**The bots** (`tools/tactics-playtest/bots.cjs`, after `npm ci` in `web/play`): `--fight` to play to win, `--lose[=n]`, `--walk="e:4,ne:2,stop:1"`, `--walk-latency`, `--walk-flood`, `--walk-drop=kill|stall`, `--walk-to-stamp[=n]`, `--chapter --branch=force|parley`, `--silent`, `--tv-log=<TV log>` to follow the TV, and `--host` (with `--shown`) to run with no TV. Its header lists every option.

**TV dev flags:** `--walk-log`, `--story-log`, `--beat-log`, `--shots`, `--perf`, `--drop-signals`, `--world-demo`, `--story-demo[=<step>]`.

---

## Environment notes

- **The cloud container had no Godot or software Vulkan.** Godot 4.7.2 .NET was unpacked into `/root/godot/`, and Xvfb and `mesa-vulkan-drivers` (lavapipe) were installed with apt, by hand. The handoff (§7) expected Godot might be missing; installing it is what made every TV run in this report possible. The .NET 10 SDK is in `/root/.dotnet`.
- **The setup script the user was given** (`setup-environment.sh` in the scratchpad, not committed) installs all three and is safe to re-run. Its fix: the old script failed at line 5, where the PATH lines ran together. Each `.bashrc` line is now its own `echo`, behind an `ARCBOX_ENV` marker so it's added once. It also exports `GODOT`. I haven't seen it run in a fresh environment.
- **4 cores:** don't run two `dotnet build` or `dotnet test` in one checkout at once. Software rendering draws a frame every 30 to 50 ms, so cloud timings are not TV timings.

## Added after this report: world interactables

Built after the report was first written, at the user's request (engine only, no art).
- **Design, catalogue and art brief:** `docs/superpowers/specs/2026-11-02-tactics-interactables-design.md` and `…-interactables-art.json`.
  It ends with a note on how this fits the world-dressing plan.
- **Engine** (`0f08ea7`, `8c8e5d1`, `9bee2bd`):
  - 21 kinds, saved per thing in `WorldSave.Things`;
  - doors and chests block walking through `WorldGround.With`;
  - checks reuse the 5d story flow, restart-safe;
  - validation proves every teleport destination can walk back under the worst lever states.
- **TV and phone** (`937745c`, `3a2b290`, `b00b6fd`, `c32236a`, `7875b11`, `51d8b03`):
  - stand-ins or markers, animated from events;
  - `--world-demo=things` shows every kind in every state for the art AI;
  - secret doors draw as unbroken wall until found;
  - the leader's Interact names the nearest thing, with a picker when several are in reach.
- **Verified:** 5,069 .NET and 474 web tests; independent reviews of both stages; real-TV shots in the scratchpad `things/` folder.
- **Not verified:** sounds (none exist yet but `ui_loot`), a live session with things (no pack region has any), and the user's art.
