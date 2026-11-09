# Representative build audit — #119

Content hash: `6e10255acd90ed4c72a66331da717288e50925f7056cc57489ec642c35d0f0d8`. 4720 deterministic battles; seeds 1–20, four heroes, Normal, cap 30. Stat increases are spent by class priority at levels 4/8, capped at 20. Spawn seats rotate over paired seeds. No consumables are used.

These are paired benchmark scenarios, not a certification of every party composition. The subclass control changes only the focus class's subclass; its three teammates retain their fixed subclasses. Race cells change all four heroes to the measured race, with identical classes, subclasses and seeds. Mixed-race parties are supported separately by --races class:race pairs.

| Class | Subclass | Level / encounter | Win % | Base-kit win % | Role measure | Base role | Change | Review |
|---|---|---|---:|---:|---:|---:|---:|---|
| barbarian | barbarian.berserker | 3 / forest_ruins | 25.0 | 35.0 | 7.69 | 8.52 | -9.8% | within ±25% |
| barbarian | barbarian.earthshaker | 3 / forest_ruins | 40.0 | 35.0 | 8.90 | 8.52 | +4.4% | within ±25% |
| barbarian | barbarian.spiritwarden | 3 / forest_ruins | 40.0 | 35.0 | 8.71 | 8.52 | +2.2% | within ±25% |
| bard | bard.valor | 3 / forest_ruins | 70.0 | 75.0 | 1.69 | 1.46 | +15.6% | within ±25% |
| bard | bard.lore | 3 / forest_ruins | 75.0 | 75.0 | 1.68 | 1.46 | +14.9% | within ±25% |
| bard | bard.siren | 3 / forest_ruins | 80.0 | 75.0 | 1.44 | 1.46 | -1.6% | within ±25% |
| cleric | cleric.life | 3 / forest_ruins | 45.0 | 50.0 | 3.18 | 2.27 | +39.9% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| cleric | cleric.war | 3 / forest_ruins | 50.0 | 50.0 | 1.98 | 2.27 | -12.8% | within ±25% |
| cleric | cleric.light | 3 / forest_ruins | 50.0 | 50.0 | 2.43 | 2.27 | +7.1% | within ±25% |
| druid | druid.shapeshifter | 3 / forest_ruins | 70.0 | 55.0 | 0.80 | 0.85 | -5.9% | within ±25% |
| druid | druid.stormcaller | 3 / forest_ruins | 55.0 | 55.0 | 0.80 | 0.85 | -5.9% | within ±25% |
| druid | druid.grovekeeper | 3 / forest_ruins | 65.0 | 55.0 | 0.75 | 0.85 | -11.8% | within ±25% |
| fighter | fighter.vanguard | 3 / forest_ruins | 45.0 | 40.0 | 7.80 | 7.76 | +0.5% | within ±25% |
| fighter | fighter.weaponmaster | 3 / forest_ruins | 80.0 | 40.0 | 6.03 | 7.76 | -22.4% | within ±25% |
| fighter | fighter.warlord | 3 / forest_ruins | 45.0 | 40.0 | 6.61 | 7.76 | -14.9% | within ±25% |
| monk | monk.open_hand | 3 / forest_ruins | 75.0 | 65.0 | 6.62 | 6.89 | -3.8% | within ±25% |
| monk | monk.windstep | 3 / forest_ruins | 70.0 | 65.0 | 6.55 | 6.89 | -4.8% | within ±25% |
| monk | monk.iron_body | 3 / forest_ruins | 65.0 | 65.0 | 7.33 | 6.89 | +6.5% | within ±25% |
| paladin | paladin.guardian | 3 / forest_ruins | 45.0 | 30.0 | 11.11 | 9.80 | +13.4% | within ±25% |
| paladin | paladin.crusader | 3 / forest_ruins | 70.0 | 30.0 | 8.82 | 9.80 | -10.0% | within ±25% |
| paladin | paladin.avenger | 3 / forest_ruins | 45.0 | 30.0 | 8.98 | 9.80 | -8.4% | within ±25% |
| ranger | ranger.sharpshooter | 3 / forest_ruins | 55.0 | 70.0 | 5.47 | 4.59 | +19.1% | within ±25% |
| ranger | ranger.beastmaster | 3 / forest_ruins | 75.0 | 70.0 | 5.30 | 4.59 | +15.5% | within ±25% |
| ranger | ranger.warden | 3 / forest_ruins | 65.0 | 70.0 | 4.48 | 4.59 | -2.5% | within ±25% |
| rogue | rogue.shadow | 3 / forest_ruins | 55.0 | 80.0 | 7.14 | 9.15 | -22.0% | within ±25% |
| rogue | rogue.trickster | 3 / forest_ruins | 60.0 | 80.0 | 4.65 | 9.15 | -49.1% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| rogue | rogue.duelist | 3 / forest_ruins | 85.0 | 80.0 | 9.45 | 9.15 | +3.3% | within ±25% |
| sorcerer | sorcerer.draconic | 3 / forest_ruins | 90.0 | 75.0 | 7.49 | 6.11 | +22.7% | within ±25% |
| sorcerer | sorcerer.chaos | 3 / forest_ruins | 80.0 | 75.0 | 6.90 | 6.11 | +13.0% | within ±25% |
| sorcerer | sorcerer.runeborn | 3 / forest_ruins | 85.0 | 75.0 | 6.99 | 6.11 | +14.5% | within ±25% |
| warlock | warlock.hexblade | 3 / forest_ruins | 85.0 | 55.0 | 5.54 | 5.40 | +2.7% | within ±25% |
| warlock | warlock.fiendbound | 3 / forest_ruins | 85.0 | 55.0 | 6.05 | 5.40 | +12.1% | within ±25% |
| warlock | warlock.void | 3 / forest_ruins | 90.0 | 55.0 | 5.66 | 5.40 | +4.9% | within ±25% |
| wizard | wizard.evoker | 3 / forest_ruins | 45.0 | 40.0 | 2.30 | 2.50 | -8.0% | within ±25% |
| wizard | wizard.chronomancer | 3 / forest_ruins | 55.0 | 40.0 | 4.05 | 2.50 | +62.0% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| wizard | wizard.geomancer | 3 / forest_ruins | 40.0 | 40.0 | 2.50 | 2.50 | 0.0% | within ±25% |
| barbarian | barbarian.berserker | 5 / crypt_hall | 90.0 | 85.0 | 6.08 | 7.62 | -20.2% | within ±25% |
| barbarian | barbarian.earthshaker | 5 / crypt_hall | 90.0 | 85.0 | 6.65 | 7.62 | -12.7% | within ±25% |
| barbarian | barbarian.spiritwarden | 5 / crypt_hall | 95.0 | 85.0 | 6.28 | 7.62 | -17.6% | within ±25% |
| bard | bard.valor | 5 / crypt_hall | 100.0 | 95.0 | 2.89 | 2.95 | -1.9% | within ±25% |
| bard | bard.lore | 5 / crypt_hall | 90.0 | 95.0 | 2.69 | 2.95 | -9.0% | within ±25% |
| bard | bard.siren | 5 / crypt_hall | 85.0 | 95.0 | 2.86 | 2.95 | -2.9% | within ±25% |
| cleric | cleric.life | 5 / crypt_hall | 80.0 | 70.0 | 5.27 | 3.84 | +37.1% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| cleric | cleric.war | 5 / crypt_hall | 75.0 | 70.0 | 3.17 | 3.84 | -17.5% | within ±25% |
| cleric | cleric.light | 5 / crypt_hall | 90.0 | 70.0 | 3.89 | 3.84 | +1.2% | within ±25% |
| druid | druid.shapeshifter | 5 / crypt_hall | 90.0 | 80.0 | 0.00 | 0.00 | 0.0% | within ±25% |
| druid | druid.stormcaller | 5 / crypt_hall | 90.0 | 80.0 | 0.00 | 0.00 | 0.0% | within ±25% |
| druid | druid.grovekeeper | 5 / crypt_hall | 95.0 | 80.0 | 0.00 | 0.00 | 0.0% | within ±25% |
| fighter | fighter.vanguard | 5 / crypt_hall | 70.0 | 90.0 | 10.22 | 8.72 | +17.2% | within ±25% |
| fighter | fighter.weaponmaster | 5 / crypt_hall | 90.0 | 90.0 | 8.27 | 8.72 | -5.2% | within ±25% |
| fighter | fighter.warlord | 5 / crypt_hall | 85.0 | 90.0 | 9.80 | 8.72 | +12.4% | within ±25% |
| monk | monk.open_hand | 5 / crypt_hall | 100.0 | 100.0 | 12.65 | 12.35 | +2.4% | within ±25% |
| monk | monk.windstep | 5 / crypt_hall | 100.0 | 100.0 | 13.80 | 12.35 | +11.8% | within ±25% |
| monk | monk.iron_body | 5 / crypt_hall | 100.0 | 100.0 | 12.12 | 12.35 | -1.8% | within ±25% |
| paladin | paladin.guardian | 5 / crypt_hall | 100.0 | 95.0 | 9.56 | 11.89 | -19.6% | within ±25% |
| paladin | paladin.crusader | 5 / crypt_hall | 100.0 | 95.0 | 9.52 | 11.89 | -19.9% | within ±25% |
| paladin | paladin.avenger | 5 / crypt_hall | 85.0 | 95.0 | 8.49 | 11.89 | -28.6% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| ranger | ranger.sharpshooter | 5 / crypt_hall | 100.0 | 95.0 | 8.04 | 9.78 | -17.8% | within ±25% |
| ranger | ranger.beastmaster | 5 / crypt_hall | 100.0 | 95.0 | 10.22 | 9.78 | +4.5% | within ±25% |
| ranger | ranger.warden | 5 / crypt_hall | 95.0 | 95.0 | 10.12 | 9.78 | +3.5% | within ±25% |
| rogue | rogue.shadow | 5 / crypt_hall | 70.0 | 75.0 | 9.68 | 10.54 | -8.2% | within ±25% |
| rogue | rogue.trickster | 5 / crypt_hall | 75.0 | 75.0 | 10.05 | 10.54 | -4.6% | within ±25% |
| rogue | rogue.duelist | 5 / crypt_hall | 80.0 | 75.0 | 10.71 | 10.54 | +1.6% | within ±25% |
| sorcerer | sorcerer.draconic | 5 / crypt_hall | 95.0 | 90.0 | 6.03 | 5.17 | +16.8% | within ±25% |
| sorcerer | sorcerer.chaos | 5 / crypt_hall | 90.0 | 90.0 | 6.30 | 5.17 | +22.0% | within ±25% |
| sorcerer | sorcerer.runeborn | 5 / crypt_hall | 95.0 | 90.0 | 7.40 | 5.17 | +43.3% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| warlock | warlock.hexblade | 5 / crypt_hall | 95.0 | 100.0 | 7.50 | 7.51 | -0.1% | within ±25% |
| warlock | warlock.fiendbound | 5 / crypt_hall | 100.0 | 100.0 | 7.91 | 7.51 | +5.3% | within ±25% |
| warlock | warlock.void | 5 / crypt_hall | 100.0 | 100.0 | 7.40 | 7.51 | -1.5% | within ±25% |
| wizard | wizard.evoker | 5 / crypt_hall | 75.0 | 75.0 | 2.45 | 2.40 | +2.1% | within ±25% |
| wizard | wizard.chronomancer | 5 / crypt_hall | 85.0 | 75.0 | 8.40 | 2.40 | +250.0% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| wizard | wizard.geomancer | 5 / crypt_hall | 75.0 | 75.0 | 2.40 | 2.40 | 0.0% | within ±25% |
| barbarian | barbarian.berserker | 7 / crystal_cave | 75.0 | 80.0 | 10.58 | 9.51 | +11.3% | within ±25% |
| barbarian | barbarian.earthshaker | 7 / crystal_cave | 90.0 | 80.0 | 11.63 | 9.51 | +22.3% | within ±25% |
| barbarian | barbarian.spiritwarden | 7 / crystal_cave | 80.0 | 80.0 | 9.71 | 9.51 | +2.2% | within ±25% |
| bard | bard.valor | 7 / crystal_cave | 90.0 | 70.0 | 3.38 | 3.38 | -0.1% | within ±25% |
| bard | bard.lore | 7 / crystal_cave | 90.0 | 70.0 | 2.71 | 3.38 | -19.8% | within ±25% |
| bard | bard.siren | 7 / crystal_cave | 85.0 | 70.0 | 3.48 | 3.38 | +3.0% | within ±25% |
| cleric | cleric.life | 7 / crystal_cave | 60.0 | 40.0 | 5.45 | 3.84 | +41.8% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| cleric | cleric.war | 7 / crystal_cave | 45.0 | 40.0 | 3.46 | 3.84 | -10.1% | within ±25% |
| cleric | cleric.light | 7 / crystal_cave | 45.0 | 40.0 | 3.00 | 3.84 | -22.0% | within ±25% |
| druid | druid.shapeshifter | 7 / crystal_cave | 100.0 | 90.0 | 0.00 | 0.00 | 0.0% | within ±25% |
| druid | druid.stormcaller | 7 / crystal_cave | 95.0 | 90.0 | 0.05 | 0.00 | new capability | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| druid | druid.grovekeeper | 7 / crystal_cave | 100.0 | 90.0 | 0.00 | 0.00 | 0.0% | within ±25% |
| fighter | fighter.vanguard | 7 / crystal_cave | 65.0 | 65.0 | 12.29 | 11.83 | +3.8% | within ±25% |
| fighter | fighter.weaponmaster | 7 / crystal_cave | 70.0 | 65.0 | 11.09 | 11.83 | -6.2% | within ±25% |
| fighter | fighter.warlord | 7 / crystal_cave | 75.0 | 65.0 | 10.50 | 11.83 | -11.2% | within ±25% |
| monk | monk.open_hand | 7 / crystal_cave | 65.0 | 65.0 | 5.83 | 5.32 | +9.6% | within ±25% |
| monk | monk.windstep | 7 / crystal_cave | 65.0 | 65.0 | 6.58 | 5.32 | +23.7% | within ±25% |
| monk | monk.iron_body | 7 / crystal_cave | 80.0 | 65.0 | 6.53 | 5.32 | +22.7% | within ±25% |
| paladin | paladin.guardian | 7 / crystal_cave | 85.0 | 55.0 | 14.97 | 11.31 | +32.4% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| paladin | paladin.crusader | 7 / crystal_cave | 70.0 | 55.0 | 12.77 | 11.31 | +13.0% | within ±25% |
| paladin | paladin.avenger | 7 / crystal_cave | 60.0 | 55.0 | 11.95 | 11.31 | +5.7% | within ±25% |
| ranger | ranger.sharpshooter | 7 / crystal_cave | 100.0 | 100.0 | 8.13 | 10.22 | -20.5% | within ±25% |
| ranger | ranger.beastmaster | 7 / crystal_cave | 100.0 | 100.0 | 10.84 | 10.22 | +6.0% | within ±25% |
| ranger | ranger.warden | 7 / crystal_cave | 95.0 | 100.0 | 9.54 | 10.22 | -6.7% | within ±25% |
| rogue | rogue.shadow | 7 / crystal_cave | 70.0 | 90.0 | 6.56 | 7.15 | -8.3% | within ±25% |
| rogue | rogue.trickster | 7 / crystal_cave | 75.0 | 90.0 | 6.93 | 7.15 | -3.1% | within ±25% |
| rogue | rogue.duelist | 7 / crystal_cave | 90.0 | 90.0 | 8.21 | 7.15 | +14.8% | within ±25% |
| sorcerer | sorcerer.draconic | 7 / crystal_cave | 85.0 | 65.0 | 8.49 | 7.06 | +20.3% | within ±25% |
| sorcerer | sorcerer.chaos | 7 / crystal_cave | 85.0 | 65.0 | 7.25 | 7.06 | +2.8% | within ±25% |
| sorcerer | sorcerer.runeborn | 7 / crystal_cave | 70.0 | 65.0 | 6.83 | 7.06 | -3.1% | within ±25% |
| warlock | warlock.hexblade | 7 / crystal_cave | 100.0 | 100.0 | 10.05 | 10.88 | -7.6% | within ±25% |
| warlock | warlock.fiendbound | 7 / crystal_cave | 100.0 | 100.0 | 9.50 | 10.88 | -12.7% | within ±25% |
| warlock | warlock.void | 7 / crystal_cave | 100.0 | 100.0 | 10.31 | 10.88 | -5.2% | within ±25% |
| wizard | wizard.evoker | 7 / crystal_cave | 60.0 | 75.0 | 4.05 | 3.60 | +12.5% | within ±25% |
| wizard | wizard.chronomancer | 7 / crystal_cave | 90.0 | 75.0 | 7.55 | 3.60 | +109.7% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| wizard | wizard.geomancer | 7 / crystal_cave | 70.0 | 75.0 | 4.30 | 3.60 | +19.4% | within ±25% |
| barbarian | barbarian.berserker | 10 / hall_of_the_flame | 100.0 | 100.0 | 11.60 | 12.77 | -9.2% | within ±25% |
| barbarian | barbarian.earthshaker | 10 / hall_of_the_flame | 100.0 | 100.0 | 11.40 | 12.77 | -10.7% | within ±25% |
| barbarian | barbarian.spiritwarden | 10 / hall_of_the_flame | 100.0 | 100.0 | 13.63 | 12.77 | +6.8% | within ±25% |
| bard | bard.valor | 10 / hall_of_the_flame | 100.0 | 100.0 | 2.76 | 2.30 | +19.7% | within ±25% |
| bard | bard.lore | 10 / hall_of_the_flame | 100.0 | 100.0 | 1.71 | 2.30 | -25.6% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| bard | bard.siren | 10 / hall_of_the_flame | 100.0 | 100.0 | 2.55 | 2.30 | +10.7% | within ±25% |
| cleric | cleric.life | 10 / hall_of_the_flame | 100.0 | 95.0 | 10.21 | 9.30 | +9.8% | within ±25% |
| cleric | cleric.war | 10 / hall_of_the_flame | 100.0 | 95.0 | 8.14 | 9.30 | -12.5% | within ±25% |
| cleric | cleric.light | 10 / hall_of_the_flame | 100.0 | 95.0 | 8.06 | 9.30 | -13.4% | within ±25% |
| druid | druid.shapeshifter | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.00 | 0.05 | -100.0% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| druid | druid.stormcaller | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.00 | 0.05 | -100.0% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| druid | druid.grovekeeper | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.00 | 0.05 | -100.0% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| fighter | fighter.vanguard | 10 / hall_of_the_flame | 100.0 | 95.0 | 10.32 | 12.32 | -16.2% | within ±25% |
| fighter | fighter.weaponmaster | 10 / hall_of_the_flame | 100.0 | 95.0 | 9.57 | 12.32 | -22.3% | within ±25% |
| fighter | fighter.warlord | 10 / hall_of_the_flame | 100.0 | 95.0 | 6.61 | 12.32 | -46.4% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| monk | monk.open_hand | 10 / hall_of_the_flame | 100.0 | 100.0 | 27.79 | 23.27 | +19.4% | within ±25% |
| monk | monk.windstep | 10 / hall_of_the_flame | 100.0 | 100.0 | 19.22 | 23.27 | -17.4% | within ±25% |
| monk | monk.iron_body | 10 / hall_of_the_flame | 100.0 | 100.0 | 22.37 | 23.27 | -3.9% | within ±25% |
| paladin | paladin.guardian | 10 / hall_of_the_flame | 100.0 | 100.0 | 8.28 | 9.97 | -17.0% | within ±25% |
| paladin | paladin.crusader | 10 / hall_of_the_flame | 100.0 | 100.0 | 8.42 | 9.97 | -15.6% | within ±25% |
| paladin | paladin.avenger | 10 / hall_of_the_flame | 100.0 | 100.0 | 9.11 | 9.97 | -8.7% | within ±25% |
| ranger | ranger.sharpshooter | 10 / hall_of_the_flame | 100.0 | 100.0 | 9.71 | 10.24 | -5.2% | within ±25% |
| ranger | ranger.beastmaster | 10 / hall_of_the_flame | 100.0 | 100.0 | 10.19 | 10.24 | -0.5% | within ±25% |
| ranger | ranger.warden | 10 / hall_of_the_flame | 100.0 | 100.0 | 10.86 | 10.24 | +6.1% | within ±25% |
| rogue | rogue.shadow | 10 / hall_of_the_flame | 100.0 | 100.0 | 28.72 | 28.21 | +1.8% | within ±25% |
| rogue | rogue.trickster | 10 / hall_of_the_flame | 75.0 | 100.0 | 18.04 | 28.21 | -36.1% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| rogue | rogue.duelist | 10 / hall_of_the_flame | 100.0 | 100.0 | 37.00 | 28.21 | +31.1% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| sorcerer | sorcerer.draconic | 10 / hall_of_the_flame | 100.0 | 100.0 | 7.80 | 10.13 | -23.1% | within ±25% |
| sorcerer | sorcerer.chaos | 10 / hall_of_the_flame | 100.0 | 100.0 | 10.81 | 10.13 | +6.7% | within ±25% |
| sorcerer | sorcerer.runeborn | 10 / hall_of_the_flame | 100.0 | 100.0 | 9.09 | 10.13 | -10.3% | within ±25% |
| warlock | warlock.hexblade | 10 / hall_of_the_flame | 100.0 | 100.0 | 10.03 | 10.28 | -2.4% | within ±25% |
| warlock | warlock.fiendbound | 10 / hall_of_the_flame | 100.0 | 100.0 | 11.05 | 10.28 | +7.5% | within ±25% |
| warlock | warlock.void | 10 / hall_of_the_flame | 100.0 | 100.0 | 9.45 | 10.28 | -8.1% | within ±25% |
| wizard | wizard.evoker | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.10 | 0.10 | 0.0% | within ±25% |
| wizard | wizard.chronomancer | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.50 | 0.10 | +400.0% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |
| wizard | wizard.geomancer | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.05 | 0.10 | -50.0% | OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator |

Role measures match the simulator: defender damage mitigated/round, striker mean target damage/round, healer HP restored/round, controller turns denied. Outliers are documented rather than silently tuned; class identity gaps and racial balance remain tracked in #118/#120/#121/#125. Win rates from twenty seeds are descriptive, not precise estimates.

| Race | Level / encounter | Win % | Human win % | Delta (points) | Review |
|---|---|---:|---:|---:|---|
| aasimar | 3 / forest_ruins | 65.0 | 45.0 | +20.0 | OUTLIER — #125 |
| dragonborn | 3 / forest_ruins | 50.0 | 45.0 | +5.0 | within ±5 points |
| dwarf | 3 / forest_ruins | 45.0 | 45.0 | 0.0 | within ±5 points |
| elf | 3 / forest_ruins | 60.0 | 45.0 | +15.0 | OUTLIER — #125 |
| gnome | 3 / forest_ruins | 45.0 | 45.0 | 0.0 | within ±5 points |
| goliath | 3 / forest_ruins | 70.0 | 45.0 | +25.0 | OUTLIER — #125 |
| half_elf | 3 / forest_ruins | 50.0 | 45.0 | +5.0 | within ±5 points |
| half_orc | 3 / forest_ruins | 80.0 | 45.0 | +35.0 | OUTLIER — #125 |
| halfling | 3 / forest_ruins | 50.0 | 45.0 | +5.0 | within ±5 points |
| human | 3 / forest_ruins | 45.0 | 45.0 | 0.0 | within ±5 points |
| tiefling | 3 / forest_ruins | 60.0 | 45.0 | +15.0 | OUTLIER — #125 |
| aasimar | 5 / crypt_hall | 100.0 | 70.0 | +30.0 | OUTLIER — #125 |
| dragonborn | 5 / crypt_hall | 70.0 | 70.0 | 0.0 | within ±5 points |
| dwarf | 5 / crypt_hall | 70.0 | 70.0 | 0.0 | within ±5 points |
| elf | 5 / crypt_hall | 50.0 | 70.0 | -20.0 | OUTLIER — #125 |
| gnome | 5 / crypt_hall | 70.0 | 70.0 | 0.0 | within ±5 points |
| goliath | 5 / crypt_hall | 50.0 | 70.0 | -20.0 | OUTLIER — #125 |
| half_elf | 5 / crypt_hall | 55.0 | 70.0 | -15.0 | OUTLIER — #125 |
| half_orc | 5 / crypt_hall | 90.0 | 70.0 | +20.0 | OUTLIER — #125 |
| halfling | 5 / crypt_hall | 80.0 | 70.0 | +10.0 | OUTLIER — #125 |
| human | 5 / crypt_hall | 70.0 | 70.0 | 0.0 | within ±5 points |
| tiefling | 5 / crypt_hall | 60.0 | 70.0 | -10.0 | OUTLIER — #125 |
| aasimar | 7 / crystal_cave | 65.0 | 65.0 | 0.0 | within ±5 points |
| dragonborn | 7 / crystal_cave | 95.0 | 65.0 | +30.0 | OUTLIER — #125 |
| dwarf | 7 / crystal_cave | 65.0 | 65.0 | 0.0 | within ±5 points |
| elf | 7 / crystal_cave | 75.0 | 65.0 | +10.0 | OUTLIER — #125 |
| gnome | 7 / crystal_cave | 40.0 | 65.0 | -25.0 | OUTLIER — #125 |
| goliath | 7 / crystal_cave | 75.0 | 65.0 | +10.0 | OUTLIER — #125 |
| half_elf | 7 / crystal_cave | 75.0 | 65.0 | +10.0 | OUTLIER — #125 |
| half_orc | 7 / crystal_cave | 70.0 | 65.0 | +5.0 | within ±5 points |
| halfling | 7 / crystal_cave | 65.0 | 65.0 | 0.0 | within ±5 points |
| human | 7 / crystal_cave | 65.0 | 65.0 | 0.0 | within ±5 points |
| tiefling | 7 / crystal_cave | 85.0 | 65.0 | +20.0 | OUTLIER — #125 |
| aasimar | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| dragonborn | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| dwarf | 10 / hall_of_the_flame | 95.0 | 100.0 | -5.0 | within ±5 points |
| elf | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| gnome | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| goliath | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| half_elf | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| half_orc | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| halfling | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| human | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
| tiefling | 10 / hall_of_the_flame | 100.0 | 100.0 | 0.0 | within ±5 points |
