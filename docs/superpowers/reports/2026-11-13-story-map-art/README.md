# Later-chapter map art (#136), 2026-11-13

Based on main cf156c468557266db94ede7ee388f2f5cff41469. No generated or purchased assets; existing terrain, props and creature sheets are reused without changing their pixels.

## Result and scope

- Warlord Bridge: sand, dunes and sandstone replace meadow materials.
- Forest Ruins: bog and mud surround the existing ruins; a nine-tile corner pool is marked Water. The existing nine dry-grass fuel tiles are preserved, as required by the environmental regression. No Difficult flag was introduced. Freeze/conduct reactions can now use the pool.
- Crystal Cave: ice and snow replace warm cave ground, with the existing cold phase spider variant.
- Rime Tower: dedicated map/manifest entry and encounter binding, flagstone watch floor, snowy landing, ice, castle pillars/statue/brazier. Its tuned heights, gaps, cover and spawn coordinates intentionally retain the prior cave layout. This is separate architecture/dressing, not a balance redesign.
- Crypt Hall: Bone Warden uses the accepted lich sprite and original necromancer mechanics. Its explicit battle ID and tag preserve encounter identity. No voices, combat values or ability lists changed.
- Phone minimap: adds material-family styles for bog, mud, dunes, sandstone, flagstone and ice, which previously fell through to generic grey. Text, height numbers and unit shapes remain available independently of colour.

## Native review

`native/` contains real Godot 4.7.2 OpenGL 1920×1080 captures from all nine normal `--tactics-demo=encounter:<id>` paths. All nine were visually reviewed: ford assault and river crossing retain river/ford art; marsh ruins now read wet; bridge reads desert; crypt retains stone/undead cast; cave reads frozen crystal; tower reads built stone architecture; keep gate and Hall retain castle/ritual art. `before/` retains the four original map views.

Example command (under Xvfb :98):

```sh
source /workspace/.arcbox/activate.sh
ARCBOX_TACTICS_ART_ROOT=/workspace/ArcBox-art-maps/content/tactics/base \
DISPLAY=:98 godot --path /workspace/ArcBox-art/src/ArcBox.Host \
  --rendering-method gl_compatibility --rendering-driver opengl3 --audio-driver Dummy \
  -- --tactics-demo=encounter:rime_tower --res=1920x1080 --shots=/tmp/review --quit-after=5
```

The cached native host binary also includes an unused temporary portrait-review entry point from #182; this command uses the unchanged regular encounter renderer. The art-root override selects this branch's actual pack. No native runtime changes are proposed here. Capture logs contain no runtime errors. Existing main HUD clipping is outside this art issue and is not claimed fixed.

`cvd/` contains actual full-severity deuteranopia/protanopia transformations of five affected native screenshots. `simulate-cvd.py` records the Machado 2009 linear-sRGB matrices and conversion. All ten native simulations were visually inspected: terrain textures, height edges, bridge gaps, props and round/diamond unit bases remain visible. Marsh hues converge, so this review relies on water texture/geometry and existing textual phone tile details, not hue alone. Eight phone simulations and a visually reviewed contact sheet also retain the four changed map views at 390px. These simulations are limited screenshot checks, not a claim of physical-TV or human accessibility testing.

## Gameplay regression evidence

`StoryMapArtTests`: seven cases pass, including all nine encounters at intended levels for 4, 5 and 6 players. Baseline height/flag hashes and cover coordinates are fixed independently from the new dressing; only the declared nine Water flags are normalized. Bone Warden's mechanics are compared with the original caster.

Four simulation reports and `paired-summary.json` retain 400 completed battles: 100 seeds before and after for Forest Ruins (level 3) and Warlord Bridge (level 5), Normal, four default fighter/cleric/rogue/wizard heroes, round cap 30. Win rates remain 30% and 100%, respectively; mean durations remain 12.6 and 12.0. These aggregates cover only the stated party and seeds. These baseline rates were already outside the general balance target; no balance acceptance or all-party claim is made, and no retuning was performed for this art issue.

Phone evidence uses real Chromium against a Vite production build (`target: esnext` for the review harness), production BattleMap/decodeMap components and actual branch map JSON in a small review harness. It does not simulate a live multiplayer session. Harness source and browser checks are retained here; temporary app entry points are removed before commit.

Full repository check results and merge blockers are recorded in validation.json and full-check.txt.gz. No tests are skipped or weakened; CI remains manually disabled.

The initial full run exposed the dry-fuel regression and missing new-map lighting/ambience declarations. They were corrected; that superseded run was stopped and the entire full gate rerun against frozen source, rather than relying on partial or concurrently edited results.

Final focused regression totals: 7 story-map cases + 4 environmental encounter cases + 71 music policy cases + 11 board-map loading cases = 93 passed, zero skipped. The phone review harness verifies the actual tactics theme and dark number-label plates, including their visibility in the eight phone colour-vision simulations.

The final full .NET run completed: 7,743 passed / 7,755 total, 12 failures exactly matching the main baseline, zero skipped. Build: zero warnings. The same command passed all 602 phone tests and its production build, all 24 Node tests, all 96 Python tests (61 assets, 9 audio, 26 lore), lore validation and documentation drift. The phone build retains two upstream SignalR annotation warnings. Overall exit 1: the twelve baseline .NET failures block merge; no issue closure is claimed.
