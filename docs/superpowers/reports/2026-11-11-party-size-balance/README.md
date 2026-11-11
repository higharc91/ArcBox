# #140: larger-party encounter balance

Seven roaming mobs and all four Hollow chambers have authored size-five/six profiles. Ashmaw gets an intermediate three-player profile; the existing four-player profile is unchanged. The original solo/duo bear remains. Size-six boars and slimes use three smaller enemies to supply action pressure rather than relying entirely on extra HP. No classes, XP, rewards, geometry, art, existing voices or audio change.

The paired small-party guard replays 483 roaming fights and 207 non-boss Hollow fights at sizes one through four: **all 690 battles are identical**, including outcome, rounds and complete measurements. The requested three-player bear adjustment is outside that guard; separate content/AI tests cover its stable identity and attack/phase behavior. Four-player Ashmaw keeps its original 240 HP, AC 14 and Maul profile.

The original diagnostic sweep covers 5,280 battles (all eleven encounters, levels one/two, sizes five/six). Another 240 establish Ashmaw's three/four baseline. The acceptance audit rejects that baseline with 23 failures. These are complete measurements, not selected successful seeds.

Acceptance bands: level-one mobs 85–95% wins and 4–8 mean rounds; intended-level-two Hollow chambers at sizes five/six use the existing report's 60–95% and 8–12 bands. Ashmaw's level-two adjacent-size win-rate difference must be at most 20 percentage points from three through six. Its existing four-player challenge remains a control; sizes three/four are not retuned to the chamber length band.

**Final larger sweep is running. Acceptance is not yet claimed.** It fixes 100 seeds per sampled party, reports seeds 21–100 separately from the tuning seeds, and retains all raw measurements. Mobs use `--sizes 5,6`; Hollow uses `--sizes 3,4,5,6` so all four boss sizes share a published schedule. The simulator's party sampler advances across sizes, so compositions for a size can differ between those two configurations; comparisons use matched schedules only.

Focused verification: 15 Hollow/phase cases, nine mob content cases, 28 preview cases, eight art-manifest checks, ten Python simulation-audit cases, no skips, zero build warnings. The acceptance audit checks every required cell, complete seeds/parties, duplicates, exact unrounded win rates, mean rounds and the adjacent-size guard. Missing or failing cells fail the command.

The native review helper now accepts `--tactics-demo=encounter:<id>:<size>`, using real encounter setup. All existing sprite and animation paths are reused. Full gate and native evidence will be recorded when complete. Merges remain blocked by main's 13 missing recordings; strict voice tests remain unchanged.
