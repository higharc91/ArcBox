# Environmental encounter audit

Real-engine battles: 3600; gate: PASS.

| Encounter | Fuel | Shallows | Enabled | Battles | Reactions/battle | Reason |
|---|---:|---:|---|---:|---:|---|
| crypt_hall | 0 | 0 | False | 0 | — | No substantial native fuel or shallow water |
| crystal_cave | 18 | 0 | True | 360 | 6.7750 | Measured reactive combat terrain |
| ford_assault | 84 | 4 | True | 360 | 9.0944 | Measured reactive combat terrain |
| forest_ruins | 9 | 0 | False | 0 | — | The nine-tile decorative dry patch is outside the fighting lanes: the exploratory sweep produced 0.375 reactions per battle. Keep this sparse map off rather than silently presenting an unproductive opt-in. |
| hall_of_the_flame | 0 | 0 | False | 0 | — | No substantial native fuel or shallow water |
| hollow_diggings | 40 | 0 | True | 360 | 9.4583 | Measured reactive combat terrain |
| hollow_lair | 4 | 0 | False | 0 | — | Four decorative boards do not form a useful reaction area in this stone boss chamber (0.431 reactions per exploratory battle). The boss's fire attacks and existing phase hazards remain unchanged. |
| hollow_nest | 0 | 0 | False | 0 | — | The stone chamber has no native fuel or water. Its existing web hazard still restrains; incidental web burning produced only 0.097 reactions per exploratory battle, so terrain reactions are not enabled here. |
| hollow_throat | 0 | 0 | False | 0 | — | No substantial native fuel or shallow water |
| keep_gate | 0 | 0 | False | 0 | — | No substantial native fuel or shallow water |
| mob_hollow_bats | 0 | 0 | False | 0 | — | No substantial native fuel or shallow water |
| mob_hollow_kobolds | 0 | 0 | False | 0 | — | No substantial native fuel or shallow water |
| mob_kobold_scouts | 0 | 0 | False | 0 | — | No substantial native fuel or shallow water |
| mob_pine_wolves | 105 | 0 | True | 360 | 2.3000 | Measured reactive combat terrain |
| mob_river_slimes | 19 | 30 | True | 360 | 1.8306 | Measured reactive combat terrain |
| mob_road_bandits | 120 | 0 | True | 360 | 3.5556 | Measured reactive combat terrain |
| mob_rooting_boars | 51 | 0 | True | 360 | 2.5500 | Measured reactive combat terrain |
| rime_tower | 18 | 0 | True | 360 | 1.6722 | Measured reactive combat terrain |
| river_crossing | 84 | 4 | True | 360 | 2.0972 | Measured reactive combat terrain |
| warlord_bridge | 8 | 0 | True | 360 | 1.5056 | Measured reactive combat terrain |

## Lava decision

Lava is deep terrain, not an ambient fire surface: it blocks walking, and forced entry uses the existing deep-fall rule (Downed for party members, death for enemies). It neither adds periodic fire damage nor ignites neighbouring fuel.

Keep a single deterministic fall/ledge contract rather than silently adding a second damage model to the finale. Hazard remains an authoring tag; Deep is the mechanical flag. Any later change to the finale in #141 must explicitly replace this recorded contract.
