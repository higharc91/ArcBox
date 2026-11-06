# Ashen Ford ambience — #186

Branch `ashen-ford-ambience-186`, based on main `06a5fefd`. Implementation commit: `3b00ecbc`.

Main already includes `b507cd8f` (the initial #186 work): town/river/field beds for every Ashen Ford tile, swamp ambience in Mirewood, and an optional `ExploreView.Weather` check selecting `amb_rain`. This follow-up closes the missing connection: gameplay never supplied that field and no world renderer consumed it.

Regions can now author `"weather": "rain"`, `"storm"`, or `"drizzle"` in `rules/world/<region>.json`. The loader validates the value, gameplay forwards it to the host view (including after reconnect), and the TV tiles the existing animated rain strip below the HUD. The same ambience decision controls whether rain is visible: camp, battle, results and other non-exploration screens retain their established audio behavior. Missing weather art is safe.

Weather is optional. Existing regions remain dry; this does not add a random weather schedule, change combat, replace music, or change existing region files. Rain replaces the dry place bed while active, as in the initial implementation. Reused recordings: `amb_town`, `amb_river`, `amb_plains_wind`, `amb_rain`, `amb_swamp`; no Scenario generation or new music was needed. All five decode as 48 kHz mono Ogg/Vorbis (13–15 seconds) and are marked as loops in the manifest. They use the existing Sfx bus, mute/volume controls and -14 dB bed level.

The TV audio node also stops its timer/tweens/players and releases cached Ogg streams when leaving the tree. The screenshot feeder tears down its preview scene before quitting, allowing the audio server to release playback references. A verbose preview exposed eight Ogg/playback objects retained when quitting with active loops.

## Place decisions

| Area | Bed |
|---|---|
| Emberwick square, chapel, market, Last Coal, granary and town streets | `amb_town` |
| Riverbank, docks, ford bridge | `amb_river` |
| Farm fields, hills, roads, Maud's hut | `amb_plains_wind` |
| Cinder Hollow | `amb_cinder_hollow` |
| Mirewood | `amb_swamp` |
| Authored rain/storm/drizzle during exploration/story | `amb_rain` |
| Active camp | `amb_campfire` |
| Battles | Existing map-specific beds |
| Menus, results, recovery UI | Explicitly no exploration bed |

The baseline's tests cover the named Ford locations and all 19,200 cells in the 160×120 region. New tests cover region-weather validation/loading, host-view production and resume, weather art/frame looping, and clearing weather at screen changes.

## Preview

Run the TV with `--world-demo=ambience --audio-log --shots=<folder>`. It follows the normal host-view and audio path through town → river → fields → rain → storm → drizzle → clear. Storm/drizzle reuse the running rain loop without restarting it; clearing returns to `amb_town`. Screenshots cover each stop.

Cloud previews use Godot 4.7.2 .NET, 1280×720 and compatibility/OpenGL rendering. Audio play scheduling is verified in the log with dummy audio output; physical loudspeaker playback is not claimed.

## Verification

- Solution build and final host rebuild: **0 warnings/errors** ([solution](checks/build.txt), [host](checks/host-build.txt)).
- Game suite: **828 passed**, zero failures ([log](checks/game-tests.txt)). Includes four tests for authored weather reaching the host view, surviving resume, and existing dry regions.
- Region suite: **165 passed**, zero failures ([log](checks/region-tests.txt)). The three new authored-weather cases failed before the fix because the loader rejected the field ([before](checks/red.txt)).
- Presentation suite: **2,895 passed / 3 failed** ([log](checks/presentation-tests.txt)). New weather and audio-policy tests pass. All three failures are reproduced on unchanged main `06a5fefd`: hard-coded story counts of 921 vs 945 lines, 590 vs 614 speakers, and 8 vs 32 camp reflections ([screen baseline](checks/baseline-story-tests.txt), [track baseline](checks/baseline-story-track-tests.txt), [comparison](checks/baseline-comparison.json)). The temporary baseline worktree was removed after checking it.
- Existing recordings fully decode without errors ([check](checks/audio-decode.json)).

The full solution test suite was not rerun: the two affected game/presentation projects and the region tests were run as above. Physical audio output remains a manual listening check.

Final TV preview: **passed**, zero engine errors and zero leaked objects at exit ([log](checks/tv-preview.txt), [checks](checks/tv-acceptance.json)). V-Sync/AA capability warnings are expected with this software compatibility renderer. Screenshots: [town](preview/town.png), [river](preview/river.png), [fields](preview/fields.png), [rain](preview/rain.png), [storm](preview/storm.png), [drizzle](preview/drizzle.png), [clear](preview/clear.png).
