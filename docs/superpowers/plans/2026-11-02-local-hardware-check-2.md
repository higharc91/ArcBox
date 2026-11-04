# Prompt for the local Claude: second hardware check (Wardenflame only)

Paste everything below the line into the local Claude session on the Windows PC (RTX 5080).

---

Run the second Wardenflame hardware check on this PC, and change no code, tests or content. Only Wardenflame (Tactics) is in scope: skip
REDLINE and anything that needs Playwright.

**Setup**

1. `git fetch origin` then check out `ccr-18ad4510-q68nad` at its latest commit, in a clean worktree. Note the sha you test.
2. Read `docs/tv-host.md` (the run modes, "the world", "things to interact with", the performance notes) and the header of
   `tools/tv-e2e.sh`. Run every e2e from Git Bash, one at a time, at `--res=1920x1080`, with `GODOT=` set to the Godot 4.7.2 .NET
   editor.
3. Builds: `dotnet build ArcBox.slnx -c Debug --no-incremental` and `-c Release --no-incremental`; both must have 0 warnings.
4. Tests: `dotnet test --solution ArcBox.slnx` three times (note any failure, and whether it repeats alone);
   `npm --prefix web/play ci`, `npm --prefix web/play test` and `npm --prefix web/play run build`.

**Runs** (record pass or fail, the key log lines and any `ERROR` / `late block` / shader-cache lines for each)

| # | Run | What to check |
|---|---|---|
| 1 | `ARCBOX_GAME=tactics ARCBOX_RECONNECT=1` | The late-block fix: after the TV is killed mid-fight and relaunched, count the `WORLD late block` lines (last time: 3) and the texture memory at world open (last time 55.6 MB against 38.2 MB normally). No hole in the world while it comes back. |
| 2 | `ARCBOX_GAME=tactics ARCBOX_WALK=1` | Press to `WALK` median and p90. The first press after the scripted walk ("press 0: no step" last time): does it step now? |
| 3 | `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force` | All of Chapter 1 reaches the card. |
| 4 | `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=parley` | The same, on the other branch. |
| 5 | `ARCBOX_CHAPTER=1 ARCBOX_BRANCH=skip-assault` | The ford choice that skips Ford Assault reaches the card. |
| 6 | `ARCBOX_CHAPTER=1 ARCBOX_RESTART=1 ARCBOX_RECONNECT=1` | The saved line after the server restart is the one shown before; the relaunched TV shows its line without saying it again. |
| 7 | Story performance: `ARCBOX_CHAPTER=1 ARCBOX_SHOTS=0 TV_ARGS=--perf` | Frame times during scenes, votes, rolls and the scene cast walking in and out. Report the worst frames and where they happen. |
| 8 | Real audio: run 3 again **without** `--audio-driver Dummy`, with speakers or headphones on | Music changes (explore, battle, victory, the boss track at Rime Tower if reached), voiced lines, captions only where a line has no clip, footsteps, and that nothing is too loud or clips. Note anything that sounds wrong. |
| 9 | `--world-demo=things --res=1920x1080 --shots=<dir>` | Every interactable stand-in is readable on the TV. The new sounds play: door, gate, drawbridge, secret door, chest/crate/barrel/sack/cache, lever, lock open/fail, secret found, trap spotted/sprung/disarmed, pickup, page, shrine, well, altar (run once with real audio). |
| 10 | `--world-demo --res=1920x1080 --shots=<dir>` and `--perf` | Current main’s integrated Ashen Ford (197 buildings): frame times on the full walk, world load time, texture memory. Check the animals (fox, mule, goat, duck, crow) and the fences look right at 2× from the sofa. |
| 11 | `ARCBOX_GAME=tactics ARCBOX_WALK=1 ARCBOX_SHOTS=0 TV_ARGS=--perf` in Ashen Ford | Walking past the things placed in Ashen Ford: a chest opened, a note read, a hidden cache found. Any hitch when a thing animates. |

If a run fails, run it once more to see whether it repeats; don't fix anything.

**Report**

Write `docs/superpowers/reports/2026-10-31-tactics-overworld/local-windows-checks-2.md` in the same style as `local-windows-checks.md`
(on the `local-hw-checks` branch): the machine, the sha, a summary, one table per section, the numbers, and a numbered list of problems
with file and line where you can find them. Add your best shots next to it. Commit it on a new branch `local-hw-checks-2` from the tested
sha, with the trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`, and push that branch. Don't open a PR.


## Chapter automation after issue 76

Run in Git Bash from the repository root. Both branches must reach the Chapter 1 card:

```bash
export PATH="/c/Users/fatma/AppData/Local/Microsoft/dotnet:$PATH"
export GODOT="/c/Users/fatma/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
ARCBOX_CHAPTER=1 ARCBOX_BRANCH=force ARCBOX_DIFFICULTY=Story ARCBOX_SHOTS=0 tools/tv-e2e.sh
ARCBOX_CHAPTER=1 ARCBOX_BRANCH=parley ARCBOX_DIFFICULTY=Story ARCBOX_SHOTS=0 tools/tv-e2e.sh
```

Chapter battles use seed 76 by default. Screenshots are disabled to measure walking without capture stalls.
