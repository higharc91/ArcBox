# ArcBox Tactics

ArcBox Tactics is a turn-based tactical battle game for the ArcBox party platform. `Tactics.Logic` is its rules engine: a deterministic, pure C# library (no clock, no `System.Random`, sorted collections only) that takes a battle state and a command and returns the new state and the events that happened, so every battle can be replayed from its seed. `Tactics.Logic.Tests` covers it, and `Tactics.Sim` balance-tests the content: it plays every encounter with sampled AI parties, AI on both sides, and writes a Markdown report (the latest is in `docs/superpowers/reports/`):

```
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 --seeds 20 --out <report.md>
```

The design is in [docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md](../../docs/superpowers/specs/2026-10-30-tactics-rules-engine-design.md). Third-party notices are in [THIRD_PARTY.md](THIRD_PARTY.md).

## Running the simulator

```
dotnet run -c Release --project games/Tactics/Tactics.Sim -- --levels 1,3,5,7,10 --sizes 4,5,6 --parties 6 --seeds 20 --difficulty Normal --out <report.md>
```

That full sweep is 9,000 battles and takes about 5 minutes. While iterating, use `--seeds 6` (about 1.5 minutes), or narrow it with `--encounters a,b` and `--levels`. `--help` lists every option. The rules are read at run time, so a content edit needs no rebuild.

The report also groups environmental reactions, same-side/self conduction damage, burning tile-rounds and terminal surface deaths by encounter and level. Downs are reported separately from deaths. Environment-off encounters produce zero environmental tallies.

Six seeds are noisy, so judge the final numbers at 20. The report flags:
- win rates outside 60–95%, and mean battle lengths outside 8–12 rounds, at each encounter's own level;
- role-band values more than ±15% from the band mean;
- abilities used in under 2% of the turns where they're legal.

Simulated parties have no subclass. The latest numbers and every tuning decision are in [the final balance report](../../docs/superpowers/reports/2026-10-30-tactics-balance.md).

If a content change alters a golden replay, re-record with `UPDATE_GOLDENS=1 dotnet test games/Tactics/Tactics.Logic.Tests`, review each diff, then run again without the flag. The recording run fails `Warlord_bridge_plays_the_showcase_triggers` if it runs before the replay is written; the second run must pass. To re-record `warlord-bridge` from the AI, empty its `commands` in `Golden/warlord-bridge.replay.json`.

## Environmental encounter balance

Environmental reactions are opt-in per encounter. River Crossing, Ford Assault, Forest Ruins, Warlord Bridge and Crystal Cave enable them. The [completion report](../../docs/superpowers/reports/2026-11-02-environmental-tiles/completion.md) records final Ford/cave tuning, on/off comparisons, party-size results, visual review and Chapter 1 validation. The [earlier Phase G checkpoint](../../docs/superpowers/reports/2026-11-02-environmental-tiles/milestone-g.md) preserves the original measurements. Showcase opt-ins require their intended-level simulation to meet the win-rate and battle-length targets. A pooled pass does not guarantee equal difficulty at every party size.

## Bot phones and the TV, end to end

The fighting bots read the phone's optional damage forecasts when choosing a route, and leave burning ground on a safe path even after spending their action. Safe exits stay directed toward the shared target, and explicit defeat objectives trigger pursuit immediately. The bots retain the dry-ground preference among equally safe paths. This uses public phone data, not hidden battle state. Run its regression checks with `node --test tools/tactics-playtest/hazards.test.cjs`; the completion report records the older hazard-unaware attempts separately.

`tools/tactics-playtest/bots.cjs` joins a room as N phones over the real hub, with no browser, and plays a session: the first bot is the VIP, makes a campaign and starts the game, and each bot picks a class at Muster. Its header has every option and how to run the server and TV by hand; it needs `npm ci` in `web/play` first.

Muster lands on the walk screen. By default the VIP leaves it with `battles`, picks `--encounter` (default `river_crossing`) and the run is a battle to its Results:

| Option | What the bots do |
|---|---|
| `--fight` | play to win from what the phone shows (below); without it they only end their turns |
| `--lose[=<n>]` | lose the first n fights (default every one): walk up to the foes and do nothing there |
| `--classes=a,b,…` | the class each bot picks, in order (default fighter, cleric, rogue, wizard, paladin, ranger) |
| `--area`, `--sit-out=turn,react`, `--leave` | prefer area spells; let the turn and reaction timers run out; leave the battle to the AI |
| `--then=next` | play the next battle after the Results, for ever, instead of ending the session |
| `--people=<n>` | wait for n real phones to take a seat first, to play alongside the bots |
| `--host` | open the room and watch the host view themselves, with no TV |
| `--host --shown` | and answer every beat with `shown` at once, so a battle with no TV takes seconds: for trying the bots' play over many battles |
| `--walk-to-stamp[=<n>]` | fight n times (default 1) where the party walks into a fight, not from the list (below) |

**How `--fight` plays.** Each bot reads its turn screen's previews, as a player would. A friend who is down or below half health is healed first, and a healer keeps its spells for that, attacking with what costs nothing. Otherwise it takes the use with the most expected damage, a kill worth more, so the party finishes the hurt foe before spreading its blows, and never one that would hurt a friend. The party holds the ground it started on and takes on whatever comes within 7 tiles of it. Once the foes have kept away for two of the party's turns each, or two or fewer are left, it goes after them, the weakest first. It walks by the map's own ground: round the river to the ford, and never standing in water when dry ground will do. With no TV (`--host --shown`), 4 bots at level 1 won River Crossing 40 times out of 40, and the Ford Assault 4 times out of 20. The old play, which walked straight at the nearest foe and used the first ability in reach, lost River Crossing every time on the TV.

**`--walk-to-stamp`** needs to know where the party stands: the TV's `--walk-log` lines through `--tv-log`, or `--host`. From there `route.cs` (`dotnet run tools/tactics-playtest/route.cs -- <rules> <region> <map> <x> <y>`) gives the way into the stamp of `--encounter`'s map, by the server's own walking rule. The leader taps it out, a step at a time, waiting for each, and the step into the stamp starts the fight. A beat's lead-in scene plays before its fight, and its aftermath after a win: the leader's bot skips each line after 1.5 s, and the bots answer votes (the first option) and checks (the best bonus) at once. After each results the VIP goes on, so the next walk starts where the last fight left the party: after a loss, at the recovery spawn, about 100 steps away. After the last fight the party walks on a few steps, and the VIP ends the session.

Every bot logs each move, ability and end of turn, and a timeout says which screen each bot was on. With no TV nothing sends `shown`, so each AI beat waits its fallback (400 ms plus 250 ms an event, at most 6 s), and a battle takes minutes: a `--host` run with `--timeout=40` times out mid-battle with the bots still playing, which is the "no turn logged" the handoff saw.

**`--chapter`** plays all of Chapter 1 from wherever the party is, from a fresh campaign with no seed: it walks to the beat on offer, which it reads from the TV's `--story-log` (`STORY marker <beat> <x> <y> <emote>`) or the host view, by `route.cs … beat <id> <x> <y>` (round every stamp, so a battle beat's fight starts from its "!"), checking each step lands where the route said and routing again if not, and taps Interact once it's offered. In the scenes the leader skips each line after `--line` seconds (default 1.5), everyone votes for `--branch=force|parley` at the ford (and for `--story-pick`'s ids elsewhere, else the first option), and the best bonus rolls. A battle beat is fought with `--fight`; a loss puts its "!" back and the bots walk to it again. Once the TV shows `STORY card` the party walks a few steps and the VIP ends the session. `--silent` answers nothing and skips no line, so everything runs on the game's timers. With no TV (`--host --shown`) parley and force each played through in about 5 minutes. **A lost fight** (the user's rule, 2026-11-01) pays no XP, and the party wakes with that fight's injuries tended, its earlier ones as they were, and the potions it drank gone. When a loss kept its injuries, the bots lost nearly every Ford Assault retry they were seen to play after a real loss (14 of 15); with `--lose=1` (a loss that spends little) they won 6 of 12 retries under the old rule and 8 of 12 with the injuries healed. A force run is still about a coin toss on its first fight (5 of 10 won from the "!" side with no TV).

With any walking option the party walks instead, and then the VIP ends the session. They run in this order:

| Option | What it does |
|---|---|
| `--walk="e:4,ne:2,stop:1"` | holds each screen direction for so many seconds, pinging every 250 ms as the pad does; `stop` rests; `pause:<name>` logs `PAUSE <name>` and waits for the file `<name>` in `--go=<dir>` |
| `--walk-latency[=<n>]` | n presses (default 20), each timed from the press to the TV's `WALK` line in `--tv-log` (or the host view with `--host`) |
| `--walk-flood[=<n>]` | n seconds of a press and a stop at 60 messages a second, three times what the server's limiter lets through |
| `--walk-drop[=kill\|stall]` | holds, then cuts the leader's connection with no stop, and times the party stopping, the lead passing and the disconnect event |

While walking, whoever leads sends the pad's idle ping (a stop every 3 s), so a quiet leader keeps the lead as on a real phone.

The TV's dev flag `--walk-log` prints what the bots and the e2e run read:

- `WALK <seq> <x> <y> <unix ms>`: each step the TV is told of, when it reached the TV's main thread. Steps that came in the TV's first view after a snap aren't printed.
- `WALKAT <unix ms> <seq> <held 0|1> <leader x> <leader y> <widest gap> <name> <x> <y> …`: where the TV drew each walker, leader first, at most every 0.25 s while anything moves and once when it comes to rest. Places are in tiles, with a tile's middle at .50. The widest gap is the furthest any walker is from the one ahead: a follower falling behind shows here.
- `BUMP <unix ms> <leader x> <leader y>`: the leader bumped a wall, because `Bumps` went up in a view. A first view, a new epoch and a reconnecting TV don't bump. With `--shots` the first few bumps are saved as `tactics-world-bump-<x>-<y>`.
- `TACTICS walk snapped to <x> <y>`: the party was put somewhere rather than walked there: a first view, a new epoch, a reconnect.
- `WALKSTATS …`: frame times and how far things moved on screen each frame, every 288 frames of walking and once more when the party stops, so a short walk has one line and a long one several.

`tools/tv-e2e.sh` runs the server, a TV on Xvfb (on Windows, in a window) and the bots together, and checks the TV's log. Set `GODOT` to the Godot 4.7.2 .NET binary (on Windows the console one):

| Run | What must happen |
|---|---|
| `ARCBOX_GAME=tactics` | a battle to its Results, with music, effects, the ending sting and a voice clip on the TV |
| `… ARCBOX_RECONNECT=1` | the TV is killed at `ARCBOX_AT_BEAT` (default 30) and relaunched; it catches up to a newer beat, never an old one |
| `… ARCBOX_RESTART=1` | the server restarts at that beat; the TV shows Resuming and plays on |
| `ARCBOX_STAGE=1` | a Development seed puts the party four steps from River Crossing's stamp (`ARCBOX_ENCOUNTER=ford_assault`: the Ford Assault's) with its fight on offer. The bots walk in and the TV draws the fight in place. They lose it on purpose, and the party wakes at the recovery spawn. They walk all the way back in, the same encounter starts in place again, and they fight it again. They fight to win, but a retry after a loss is often lost too, so either ending passes: at a win, victory music, the fall-in, then a step; at a loss, the defeat music. Shots of the walk-in, the fight and the results over the world |
| `… ARCBOX_RECONNECT=1`, `… ARCBOX_RESTART=1` | with `ARCBOX_STAGE=1`: the TV is killed, or the server restarted, at `ARCBOX_AT_BEAT` of the first fight (its own beats: a first fight that ends sooner fails the run). The TV catches up with no old beat, or plays on from a newer one |
| `… ARCBOX_RESTART=kill` | in place of `ARCBOX_RESTART=1`, in a battle, stage or chapter run: the server is killed outright, as a crash would, rather than stopped, so it saves nothing on the way down. The checks are the same |
| `… ARCBOX_DROP_SIGNALS=1` | with `ARCBOX_STAGE=1`: the TV runs with `--drop-signals`, so the game paces every beat on its fallbacks. Everything above must still happen |
| `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force\|parley\|skip-assault` | (the 5d plan's `ARCBOX_STORY`) all of Chapter 1 from a fresh campaign with `--chapter` (above); `skip-assault` is force with a Development seed that has the Ford Assault done, so the ford choice goes straight on to Old Maud and there's no fight to lose. `ARCBOX_SILENT=1` (force only) runs the bots `--silent`, so the game's timers resolve every line, vote and check, and fails if they answered anything; `ARCBOX_DROP_SIGNALS=1` runs the TV with `--drop-signals`. `tools/tactics-playtest/chapter.cjs check` fails it unless the TV's `STORY` lines are the branch's scenes in order, each a walk of its graph to an end, every line said once; the fight is between its lead-in and aftermath and the last was won; the card comes up and the party steps after it; and the TV log has no errors. `ARCBOX_RESTART=1` restarts the server at line 3 of Old Maud's scene (the TV must come back on the saved line) and `ARCBOX_RECONNECT=1` kills the TV at line 3 of the docks (the new TV shows that line silently and goes on); both can be in one run. Shots of every scene, question, roll, fight, results, the card and the walk after it land in `<out>/chapter` |
| `… ARCBOX_WALK=1` | the party walks Ashen Ford. The TV's step numbers climb and the leader really moves on it. The server restarts and the party is on its saved tile. The TV is killed and the new one snaps to the party's tile. The leader's phone is cut and no step starts after the 750 ms lease. The median press-to-`WALK` time is at most `ARCBOX_WALK_LATENCY` (default 100 ms), and at least `ARCBOX_WALK_LANDED` percent (default 90) of the timed presses reached the TV at all. Screenshots of the party walking and stopped |

`ARCBOX_WALK_SCRIPT` changes the route; it needs `pause:restart` and then `pause:tv`. Shots and logs land in `ARCBOX_E2E_OUT` (default `/tmp/arcbox-tv-e2e-<time>`). Software rendering on Linux draws a frame every 30 to 50 ms, so judge its screenshots for what's on them, not for smoothness.

**Don't judge hitches from a run with shots.** Every screenshot stalls the TV's frames: on the RTX 5080 the same fight start logged a 216 ms `WORLD back` frame with them, and 2 to 5 ms without. `ARCBOX_SHOTS=0` turns the TV's shots off, and the checks for them with it (the default, 1, takes them). For frame times, run with `ARCBOX_SHOTS=0 TV_ARGS=--perf` and read the `PERF` lines. The numbers from the first Linux runs are in [the Task 11 report](../../docs/superpowers/reports/2026-10-31-tactics-overworld/task11-bots-e2e.md).

## Adding a chapter pack

A pack is a folder with a `manifest.json`, and each weekly chapter is its own pack (`chapter-03`, and so on). `base` is always loaded.

1. Create the folder and its `manifest.json`: `id`, `rulesVersion`, `requires` (the pack ids it needs) and `files`, the list of its content files.
2. Add content in the same formats as `content/tactics/base/rules/`, with the schemas in `schema/`. Bring maps, monsters and encounters for the chapter.
3. **A chapter may add ids, but never redefine one by accident.** An id defined twice without `"replaces": "<id>"` is a load error that names both files and the JSON pointer, so balance changes stay deliberate.
4. Set an optional intended `level` on each encounter, so the simulator judges it at that level, and aim for no more than 10 enemies: that's the AI's tested worst case.
5. Run the simulator over the new encounters at their levels.

A pack with errors doesn't load: errors carry the file and a JSON pointer. The content hash covers every loaded pack and is stored in each saved battle, so changing a pack mid-campaign is detected.

## Content conventions

These turn SRD 5.2 into this game, and every content file starts from them. **The balance pass moved some numbers away from them on purpose**; the [balance report](../../docs/superpowers/reports/2026-10-30-tactics-balance.md) lists each change and its reason, and the differences are noted below.

- **Spell tier** = SRD spell level. **Mana cost = tier**; cantrips cost 0.
- **Max tier by level:** full casters (wizard, cleric, bard, druid) can cast tier ⌈level / 2⌉, capped at 5. Half casters (paladin, ranger) can cast tier ⌈level / 4⌉.
- **Pools:**
  - Full-caster Mana by level: 4, 6, 8, 10, 11, 12, 13, 14, 15, 16. Bard and druid get 3, 4, 6, 7, 8, 9, 10, 10, 11, 12 because they have a second resource.
  - Every other resource interpolates linearly between the spec §4.5 endpoints (level 1 → level 10), rounding down.
  - *After balancing:* the wizard keeps this Mana table. The bard, cleric, druid and sorcerer pools and the ranger's Focus differ, and warlock spells can cost more than 1 pact.
- **Affordable on arrival:** every ability a class or subclass grants, offers in a `choose` list or puts in its `defaultPicks` can be paid for from the class's pools at the level it arrives, and pools never shrink as the level rises. Tune a cost and a pool together, or move the pick to the level its cost fits.
- **The area ladder:** within one class's Mana spells, a higher-tier area spell covering at least as many tiles never deals less direct damage per target than a lower-tier one (equal is fine: the bigger footprint is the upgrade). Two area spells of the same tier whose footprints are within a third of each other differ by at most 1.5× per target. Single-target spells, damage dealt only by a zone, secondary environmental conduction and subclass kits aren't on this ladder. Conduction depends on the terrain and nearby units, so it is measured in encounter simulations rather than counted as a spell's direct area damage. Balance tuning has to cut the whole ladder, not only the spells in `defaultPicks`.
  - `ContentLadderTests` checks both rules at every level 1–10, so they hold beyond the levels and picks the simulator samples.
- **Spell ranges:** touch → 1. SRD range ≤ 30 ft → 4. Any longer ranged spell → 6.
- **Weapon ranges:** thrown → 4. Shortbow and light crossbow → 6. Longbow and heavy crossbow → 8.
- **Areas:** radius r ft → `burst` ⌈r / 10⌉. Cone r ft → `cone` ⌈r / 10⌉ + 1. Line r ft → `line` ⌈r / 10⌉.
- **Durations:** 1 minute → 10 rounds. 1 hour or longer → the whole battle. "Save ends" stays `until save`.
  - *After balancing:* Moonbeam lasts 1 round.
- **Per-rest features:** short-rest and long-rest uses both become `usesPerBattle`. Pools such as Lay on Hands refill each battle.
- **Left out:** spells with no battle use (utility, ritual, travel, divination).
- **`defaultPicks`:** every class lists a preset loadout per level, used by the simulator and by AI-built characters. Real players choose their own in the portal (sub-project 4).
- **Content may add, never override:** an id defined twice without `"replaces"` is a load error.
