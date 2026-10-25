# Third-party data in these mockups

## Circuit of the Americas layout (`cota.json`)

The `points` array is derived from `circuits/us-2012.geojson` in
[bacinger/f1-circuits](https://github.com/bacinger/f1-circuits), projected from
longitude/latitude to local metres. The `elevation` profile is our own
approximation and is not from that source.

```
MIT License

Copyright (c) 2019-2025 Tomislav Bacinger

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 2026 grid (`grid2026.json`)

Teams, chassis names, drivers, three-letter codes and car numbers come from
[F1DB](https://github.com/f1db/f1db) (`src/data/seasons/2026/entrants.yml`,
`src/data/drivers/*.yml` and the 2026 Singapore race result), licensed under
[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).
We selected the current lineup and added our own livery colours.

The livery colours are **our approximations** of each team's colour scheme, not
official values. No team, sponsor or series logos are reproduced. Driver and
team names are used descriptively; this is a private, non-commercial hobby game.
