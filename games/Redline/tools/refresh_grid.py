#!/usr/bin/env python3
"""Refresh Redline.Logic/Data/grid2026.json from an F1DB checkout.

Usage:  python3 -I games/Redline/tools/refresh_grid.py PATH_TO_F1DB [--out FILE] [--dry-run]

Reads (relative to the F1DB checkout):
  src/data/seasons/2026/races/*/race-results.yml   latest race: driverNumber, driverId, constructorId
  src/data/seasons/2026/driver-standings.yml       position, driverId, points
  src/data/drivers/<id>.yml                        name, abbreviation, nationalityCountryId

The team set and order, the livery blocks and every other top-level key of the
existing grid file are kept, as is the display name of any driver id already in
it (F1DB's name is used only for new ids, so e.g. "Alex Albon" stays as written),
and likewise its nationality (ISO 3166-1 alpha-2): only a new id maps F1DB's
nationalityCountryId through NATIONALITY, and an unmapped country is an error.
Drivers, codes and numbers come from the latest race result; points from the
standings; aiSkill = 0.85 + 0.30*sqrt(points/leader), rounded to 3 places.
Standard library only (a tiny regex reader stands in for YAML).
F1DB data is CC BY 4.0, see Redline.Logic/THIRD_PARTY.md.
"""
import argparse
import json
import math
import re
import sys
from pathlib import Path

SEASON = 2026
DEFAULT_GRID = Path(__file__).resolve().parent.parent / "Redline.Logic" / "Data" / "grid2026.json"

# F1DB country id -> ISO 3166-1 alpha-2, for the drivers' nationalities. Every code also needs an entry in
# Redline.Logic's Countries table (display name and anthem id); add both together when a new nationality arrives.
NATIONALITY = {
    "argentina": "AR",
    "australia": "AU",
    "brazil": "BR",
    "canada": "CA",
    "finland": "FI",
    "france": "FR",
    "germany": "DE",
    "italy": "IT",
    "mexico": "MX",
    "monaco": "MC",
    "netherlands": "NL",
    "new-zealand": "NZ",
    "spain": "ES",
    "thailand": "TH",
    "united-kingdom": "GB",
}


def scalar(text, key):
    """First top-level `key: value` scalar in a YAML document, unquoted."""
    m = re.search(r"^" + re.escape(key) + r":[ \t]*(.+?)[ \t]*$", text, re.M)
    return unquote(m.group(1)) if m else None


def unquote(v):
    v = v.strip()
    if len(v) >= 2 and v[0] == v[-1] and v[0] in "\"'":
        v = v[1:-1]
    return v


def list_blocks(text):
    """Split a YAML list of mappings into dicts of scalar fields (`- key: v` then `  key: v`)."""
    blocks, cur = [], None
    for line in text.splitlines():
        m = re.match(r"^-[ \t]+([A-Za-z0-9_]+):[ \t]*(.*?)[ \t]*$", line)
        if m:
            cur = {m.group(1): unquote(m.group(2))}
            blocks.append(cur)
            continue
        m = re.match(r"^[ \t]+([A-Za-z0-9_]+):[ \t]*(.*?)[ \t]*$", line)
        if m and cur is not None and m.group(1) not in cur:
            cur[m.group(1)] = unquote(m.group(2))
    return blocks


def latest_race_results(root):
    races = root / "src" / "data" / "seasons" / str(SEASON) / "races"
    if not races.is_dir():
        sys.exit(f"error: {races} not found; is this an F1DB checkout?")

    def round_no(p):
        m = re.match(r"(\d+)", p.name)
        return int(m.group(1)) if m else -1

    for d in sorted(races.iterdir(), key=round_no, reverse=True):
        f = d / "race-results.yml"
        if f.is_file():
            rows = [b for b in list_blocks(f.read_text(encoding="utf-8"))
                    if "driverId" in b and "constructorId" in b and "driverNumber" in b]
            if rows:
                return d.name, rows
    sys.exit(f"error: no race-results.yml with results under {races}")


def read_standings(root):
    f = root / "src" / "data" / "seasons" / str(SEASON) / "driver-standings.yml"
    if not f.is_file():
        sys.exit(f"error: {f} not found")
    return {b["driverId"]: float(b["points"])
            for b in list_blocks(f.read_text(encoding="utf-8")) if "driverId" in b and "points" in b}


def read_driver(root, driver_id):
    f = root / "src" / "data" / "drivers" / f"{driver_id}.yml"
    if not f.is_file():
        sys.exit(f"error: {f} not found")
    text = f.read_text(encoding="utf-8")
    name, code = scalar(text, "name"), scalar(text, "abbreviation")
    if not name or not code:
        sys.exit(f"error: {f} lacks name/abbreviation")
    return name, code, scalar(text, "nationalityCountryId")


def nationality(driver_id, country_id, prev):
    """The existing grid value when there is one, else F1DB's nationalityCountryId as ISO alpha-2."""
    if prev and prev.get("nationality"):
        return prev["nationality"]
    iso = NATIONALITY.get(country_id or "")
    if not iso:
        sys.exit(f"error: driver '{driver_id}' has nationalityCountryId {country_id!r}, which NATIONALITY does not map")
    return iso


def ai_skill(points, leader):
    ratio = points / leader if leader > 0 else 0.0
    return round(0.85 + 0.30 * math.sqrt(ratio), 3)


def build(root, grid):
    race, results = latest_race_results(root)
    standings = read_standings(root)
    leader = max(standings.values(), default=0.0)
    old = {d["id"]: (ti, di, d) for ti, t in enumerate(grid["teams"]) for di, d in enumerate(t["drivers"])}

    for team in grid["teams"]:
        rows = [r for r in results if r["constructorId"] == team["id"]]
        rows.sort(key=lambda r: (old.get(r["driverId"], (0, 99, None))[1], int(r["driverNumber"])))
        if len(rows) != 2:
            sys.exit(f"error: team '{team['id']}' has {len(rows)} drivers in {race}, expected 2")
        drivers = []
        for r in rows:
            did = r["driverId"]
            name, code, country = read_driver(root, did)
            pts = standings.get(did, 0.0)
            prev = old.get(did)
            drivers.append({
                "id": did, "name": prev[2]["name"] if prev else name, "code": code,
                "nationality": nationality(did, country, prev[2] if prev else None),
                "number": int(r["driverNumber"]),
                "tcam": prev[2]["tcam"] if prev else "black",
                "points": pts, "aiSkill": ai_skill(pts, leader),
            })
        team["drivers"] = drivers
    return race


def main():
    ap = argparse.ArgumentParser(description="Rewrite grid2026.json from an F1DB checkout (keeps livery blocks).")
    ap.add_argument("f1db", type=Path, help="path to an F1DB checkout")
    ap.add_argument("--out", type=Path, default=DEFAULT_GRID, help="grid file to read and rewrite")
    ap.add_argument("--dry-run", action="store_true", help="print the result instead of writing it")
    args = ap.parse_args()

    grid = json.loads(args.out.read_text(encoding="utf-8"))
    race = build(args.f1db, grid)
    text = json.dumps(grid, indent=1, ensure_ascii=False) + "\n"
    if args.dry_run:
        sys.stdout.write(text)
    else:
        args.out.write_text(text, encoding="utf-8")
        print(f"wrote {args.out} from {race}")


if __name__ == "__main__":
    main()
