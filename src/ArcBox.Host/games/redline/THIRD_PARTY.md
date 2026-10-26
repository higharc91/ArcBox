# Third-party material in the REDLINE TV scene

Everything in `games/redline/` is generated at runtime from code: there are no image, model or sound assets.

| What | Where it comes from | Licence |
|---|---|---|
| Circuit of the Americas centre line | `games/Redline/Redline.Presentation/Data/cota.json`, derived from [bacinger/f1-circuits](https://github.com/bacinger/f1-circuits) `circuits/us-2012.geojson`; the elevation profile is our own approximation | MIT, see `games/Redline/Redline.Presentation/THIRD_PARTY.md` |
| Car numbers, ad-board lettering | Titillium Web Black (`src/ArcBox.Host/fonts/`) | SIL Open Font License 1.1, see `src/ArcBox.Host/fonts/OFL.txt` |
| 2026 grid: teams, drivers, numbers, livery colours, chassis names, finish | `games/Redline/Redline.Logic/Data/grid2026.json` | Facts; see `games/Redline/Redline.Logic/THIRD_PARTY.md` |

No team, sponsor or series logos are reproduced: liveries are flat colour zones, helmets are plain with a team band, and
the trackside boards carry only this game's own REDLINE and ARCBOX marks. Landmarks (grandstands, pit building, start
gantry, Observation Tower) are simple primitives, not models of the real buildings.
