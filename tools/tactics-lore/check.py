"""Checks Tactics lore packs: each file against its JSON Schema, then the references between files and into the art.

The schemas in content/tactics/base/lore/schema are the authority for shape; this script applies them with a small
validator (the subset of draft 2020-12 they use) so the tool needs nothing beyond the standard library. It then checks
what a schema can't: ids unique per kind across packs (a later pack changes one only with "replaces", as the rules packs
do), references that resolve, presets and art keys that exist, and line ids unique everywhere so translations and
pre-rendered voice files can key on them. When a rules pack sits beside the lore (content/tactics/base/rules), bestiary
subjects and region encounters are checked against its monster and encounter ids too.

    python3 tools/tactics-lore/check.py                      # the base lore pack
    python3 tools/tactics-lore/check.py <lore-dir> [...]     # packs in load order (base first)

Errors print as '<pack>/<file>#<json-pointer>: message', like the rules loader's, and the exit code is 1.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTENT = ROOT / "content" / "tactics" / "base"
SCHEMAS = CONTENT / "lore" / "schema"
FILE_SCHEMA = {"world": "world", "timeline": "timeline", "factions": "factions", "regions": "regions",
               "characters": "characters", "codex": "codex", "barks": "barks", "voices": "voices",
               "backgrounds": "backgrounds", "arcs": "arcs"}
NARRATOR = "narrator"


class Errors(list):
    def add(self, where: str, pointer: str, message: str) -> None:
        self.append(f"{where}#{pointer}: {message}")


# ---- Schema subset ---------------------------------------------------------------------------------------------------

def _resolve(ref: str, base: str, schemas: dict) -> tuple[dict, str]:
    file, _, frag = ref.partition("#")
    file = file or base
    node = schemas[file]
    for part in filter(None, frag.split("/")):
        node = node[part]
    return node, file


_TYPES = {"object": dict, "array": list, "string": str, "boolean": bool}


def _is_type(value, name: str) -> bool:
    if name == "integer":
        return isinstance(value, int) and not isinstance(value, bool)
    return isinstance(value, _TYPES[name]) and not (name != "boolean" and isinstance(value, bool))


def validate(value, schema: dict, base: str, schemas: dict, where: str, ptr: str, errors: Errors) -> None:
    if "$ref" in schema:
        target, file = _resolve(schema["$ref"], base, schemas)
        validate(value, target, file, schemas, where, ptr, errors)
    if "type" in schema and not _is_type(value, schema["type"]):
        errors.add(where, ptr, f"expected {schema['type']}, got {type(value).__name__}")
        return
    if "const" in schema and value != schema["const"]:
        errors.add(where, ptr, f"must be {json.dumps(schema['const'])}")
    if "enum" in schema and value not in schema["enum"]:
        errors.add(where, ptr, f"{json.dumps(value)} isn't one of {', '.join(map(str, schema['enum']))}")
    if isinstance(value, str):
        if len(value) < schema.get("minLength", 0):
            errors.add(where, ptr, "is empty")
        if "pattern" in schema and not re.search(schema["pattern"], value):
            errors.add(where, ptr, f"'{value}' doesn't match {schema['pattern']}")
    if isinstance(value, int) and not isinstance(value, bool) and value < schema.get("minimum", value):
        errors.add(where, ptr, f"must be at least {schema['minimum']}")
    if isinstance(value, list):
        if len(value) < schema.get("minItems", 0):
            errors.add(where, ptr, f"needs at least {schema['minItems']} item(s)")
        if schema.get("uniqueItems") and len({json.dumps(v, sort_keys=True) for v in value}) != len(value):
            errors.add(where, ptr, "has duplicate items")
        if "items" in schema:
            for i, item in enumerate(value):
                validate(item, schema["items"], base, schemas, where, f"{ptr}/{i}", errors)
    if isinstance(value, dict):
        props = schema.get("properties", {})
        for key in schema.get("required", []):
            if key not in value:
                errors.add(where, ptr, f"'{key}' is required")
        for key, item in value.items():
            if "propertyNames" in schema:
                validate(key, schema["propertyNames"], base, schemas, where, f"{ptr}/{key}", errors)
            if key in props:
                validate(item, props[key], base, schemas, where, f"{ptr}/{key}", errors)
            elif schema.get("additionalProperties") is False:
                errors.add(where, ptr, f"'{key}' isn't a known key")
            elif isinstance(schema.get("additionalProperties"), dict):
                validate(item, schema["additionalProperties"], base, schemas, where, f"{ptr}/{key}", errors)


def load_schemas() -> dict:
    return {p.name: json.loads(p.read_text(encoding="utf-8")) for p in sorted(SCHEMAS.glob("*.schema.json"))}


# ---- Art -------------------------------------------------------------------------------------------------------------

def art_keys(content: Path) -> set[str]:
    """The keys Tactics.Logic's ArtManifests.Load harvests, by the same rule, so lore and rules agree on what exists."""
    keys: set[str] = set()
    for d in sorted(p for p in content.iterdir() if p.is_dir() and p.name != "rules"):
        manifest = d / "manifest.json"
        if not manifest.is_file():
            continue
        for group in json.loads(manifest.read_text(encoding="utf-8")).values():
            if isinstance(group, dict):
                for asset, body in group.items():
                    keys.add(f"{d.name}.{asset}")
                    if isinstance(body, dict) and isinstance(body.get("variants"), dict):
                        keys.update(f"{d.name}.{asset}.{v}" for v in body["variants"])
            elif isinstance(group, list):
                keys.update(f"{d.name}.{n}" for n in group if isinstance(n, str) and re.fullmatch(r"[A-Za-z0-9_-]+", n))
    return keys


def rules_ids(content: Path) -> dict[str, set[str]] | None:
    """Monster, encounter and background ids from the rules pack, and the story flags its encounter triggers set; None
    when there is no rules pack beside the lore."""
    manifest = content / "rules" / "manifest.json"
    if not manifest.is_file():
        return None
    ids: dict[str, set[str]] = {"monsters": set(), "encounters": set(), "backgrounds": set(), "flags": set()}
    for file in json.loads(manifest.read_text(encoding="utf-8"))["files"]:
        kind = file.split("/")[0].removesuffix(".json")
        if kind in ids:
            doc = json.loads((content / "rules" / file).read_text(encoding="utf-8"))
            ids[kind].update(d["id"] for d in (doc if isinstance(doc, list) else [doc]))
            if kind == "encounters":
                ids["flags"].update(value for key, value, _ in _walk(doc) if key == "setFlag")
    return ids


def _walk(node, ptr: str = ""):
    """Every (key, value, pointer) in a JSON document, depth first."""
    items = node.items() if isinstance(node, dict) else enumerate(node) if isinstance(node, list) else ()
    for key, value in items:
        yield key, value, f"{ptr}/{key}"
        yield from _walk(value, f"{ptr}/{key}")


def portrait_moods(content: Path) -> set[str]:
    return set(json.loads((content / "portraits" / "manifest.json").read_text(encoding="utf-8"))["moods"])


# ---- Packs -----------------------------------------------------------------------------------------------------------

def _kind(file: str) -> str:
    return file.split("/")[0].removesuffix(".json")


def check(lore_dirs: list[Path], content: Path = CONTENT) -> Errors:
    errors = Errors()
    schemas = load_schemas()
    art = art_keys(content)
    moods = portrait_moods(content)

    defs: dict[str, dict[str, tuple[dict, str]]] = {k: {} for k in
                                                    ("character", "faction", "region", "place", "codex", "era", "event",
                                                     "voice", "background", "arc", "beat", "ending")}
    barks: list[tuple[dict, str]] = []
    docs: list[tuple[dict, str]] = []
    world_seen: str | None = None
    line_ids: dict[str, str] = {}
    pack_ids: list[str] = []

    for d in lore_dirs:
        index_path = d / "pack.json"
        label = f"{d.name}/pack.json"
        if not index_path.is_file():
            errors.add(label, "", "the lore pack has no pack.json")
            continue
        index = json.loads(index_path.read_text(encoding="utf-8"))
        before = len(errors)
        validate(index, schemas["pack.schema.json"], "pack.schema.json", schemas, label, "", errors)
        if len(errors) > before:
            continue
        pack = index["id"]
        if pack in pack_ids:
            errors.add(label, "/id", f"two lore packs have the id '{pack}'")
            continue
        for i, req in enumerate(index["requires"]):
            if req not in pack_ids:
                errors.add(label, f"/requires/{i}", f"'{req}' must be loaded before this pack")
        pack_ids.append(pack)

        for i, file in enumerate(index["files"]):
            where = f"{pack}/{file}"
            path = d / file
            if not path.is_file():
                errors.add(label, f"/files/{i}", f"the file '{file}' doesn't exist")
                continue
            try:
                doc = json.loads(path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as ex:
                errors.add(where, "", f"isn't valid JSON: {ex}")
                continue
            schema = f"{FILE_SCHEMA[_kind(file)]}.schema.json"
            before = len(errors)
            validate(doc, schemas[schema], schema, schemas, where, "", errors)
            if len(errors) > before:
                continue
            kind = _kind(file)
            docs.append((doc, where))
            if kind == "world":
                if world_seen:
                    errors.add(where, "", f"only one world.json is allowed; {world_seen} already defines the world")
                world_seen = where
            elif kind == "timeline":
                for e, era in enumerate(doc["eras"]):
                    _define(defs["era"], era, where, f"/eras/{e}", "era", errors)
                    for v, event in enumerate(era["events"]):
                        _define(defs["event"], event, where, f"/eras/{e}/events/{v}", "event", errors)
            elif kind in ("factions", "regions", "characters"):
                key, singular = {"factions": ("factions", "faction"), "regions": ("regions", "region"),
                                 "characters": ("characters", "character")}[kind]
                for n, entry in enumerate(doc[key]):
                    _define(defs[singular], entry, where, f"/{key}/{n}", singular, errors)
                    for p, place in enumerate(entry.get("places", [])):
                        _define(defs["place"], place, where, f"/{key}/{n}/places/{p}", "place", errors)
            elif kind == "codex":
                for n, entry in enumerate(doc["entries"]):
                    _define(defs["codex"], entry, where, f"/entries/{n}", "codex entry", errors)
            elif kind == "backgrounds":
                for n, entry in enumerate(doc["backgrounds"]):
                    _define(defs["background"], entry, where, f"/backgrounds/{n}", "background", errors)
            elif kind == "arcs":
                for n, arc in enumerate(doc["arcs"]):
                    _define(defs["arc"], arc, where, f"/arcs/{n}", "arc", errors)
                    for b, beat in enumerate(arc["beats"]):
                        _define(defs["beat"], beat, where, f"/arcs/{n}/beats/{b}", "beat", errors)
                for n, ending in enumerate(doc["endings"]):
                    _define(defs["ending"], ending, where, f"/endings/{n}", "ending", errors)
            if kind == "voices":
                _define(defs["voice"], doc, where, "", "voice set", errors)
            if kind in ("barks", "voices"):
                if kind == "barks":
                    barks.append((doc, where))
                for event, lines in doc["events"].items():
                    for n, line in enumerate(lines):
                        at = f"/events/{event}/{n}/id"
                        if line["id"] in line_ids:
                            errors.add(where, at, f"line id '{line['id']}' is already used in {line_ids[line['id']]}")
                        line_ids[line["id"]] = where
                        if "mood" in line and line["mood"] not in moods:
                            errors.add(where, at, f"mood '{line['mood']}' has no portrait")

    rules = rules_ids(content)
    _references(defs, barks, art, rules, errors)
    if defs["arc"]:
        _flags(defs, docs, rules, errors)
    return errors


def _flags(defs: dict, docs: list, rules: dict | None, errors: Errors) -> None:
    """Once a pack set has arcs, every flag the lore waits on must be set somewhere: by a beat, a choice or an ending, or
    by a rules encounter trigger. A misspelt flag would otherwise leave a secret locked for ever."""
    set_by = set(rules["flags"]) if rules else set()
    for table in ("beat", "ending"):
        for entry, _ in defs[table].values():
            set_by.update(entry.get("sets", []))
            for choice in entry.get("choices", []):
                set_by.update(choice["sets"])
    for doc, where in docs:
        for key, value, ptr in _walk(doc):
            waits = [value["flag"]] if key == "unlock" and "flag" in value else value if key == "requires" else []
            for i, flag in enumerate(waits):
                if flag not in set_by:
                    at = f"{ptr}/flag" if key == "unlock" else f"{ptr}/{i}"
                    errors.add(where, at, f"nothing sets the story flag '{flag}' (no beat, choice, ending or encounter)")


def _define(table: dict, entry: dict, where: str, ptr: str, kind: str, errors: Errors) -> None:
    """Adds an entry to its kind's table; on an error the earlier definition stays, so its references are still checked."""
    id_ = entry["id"]
    if id_ in table and entry.get("replaces") != id_:
        errors.add(where, f"{ptr}/id", f"the {kind} '{id_}' is already defined in {table[id_][1]}; "
                                       f"use \"replaces\": \"{id_}\" to change it")
    elif "replaces" in entry and entry["replaces"] != id_:
        errors.add(where, f"{ptr}/replaces", f"\"replaces\" must be the entry's own id ('{id_}')")
    elif "replaces" in entry and id_ not in table:
        errors.add(where, f"{ptr}/replaces", f"no earlier pack defines the {kind} '{id_}'")
    else:
        table[id_] = (entry, f"{where}#{ptr}")


def _references(defs: dict, barks: list, art: set[str], rules: dict | None, errors: Errors) -> None:
    def at(location: str) -> tuple[str, str]:
        where, _, ptr = location.partition("#")
        return where, ptr

    def need(table: str, id_: str, location: str, key: str, what: str) -> None:
        if id_ not in defs[table]:
            errors.add(*at(location + key), f"no {what} has the id '{id_}'")

    def need_art(key: str, location: str, field: str) -> None:
        if key not in art:
            errors.add(*at(location + field), f"the art key '{key}' isn't in any art manifest")

    subjects = {k for t in ("character", "faction", "region", "place") for k in defs[t]}

    for id_, (c, loc) in defs["character"].items():
        need_art(f"characters.{c.get('preset', id_)}", loc, "/preset" if "preset" in c else "/id")
        if "faction" in c:
            need("faction", c["faction"], loc, "/faction", "faction")
        for i, r in enumerate(c.get("relationships", [])):
            need("character", r["with"], loc, f"/relationships/{i}/with", "character")
    for id_, (f, loc) in defs["faction"].items():
        if "crest" in f:
            need_art(f["crest"], loc, "/crest")
        if "leader" in f:
            need("character", f["leader"], loc, "/leader", "character")
        for other in f.get("stance", {}):
            need("faction", other, loc, f"/stance/{other}", "faction")
    for id_, (r, loc) in defs["region"].items():
        if "card" in r:
            need_art(r["card"], loc, "/card")
        for i, f in enumerate(r.get("factions", [])):
            need("faction", f, loc, f"/factions/{i}", "faction")
        for i, e in enumerate(r.get("encounters", [])):
            if rules is not None and e not in rules["encounters"]:
                errors.add(*at(loc + f"/encounters/{i}"), f"the rules pack has no encounter '{e}'")
    for id_, (e, loc) in defs["codex"].items():
        if "icon" in e:
            need_art(e["icon"], loc, "/icon")
        for i, rel in enumerate(e.get("related", [])):
            need("codex", rel, loc, f"/related/{i}", "codex entry")
        # Bestiary subjects are rules monster ids, checked only when a rules pack sits beside the lore.
        if "subject" in e and e["category"] == "bestiary":
            if rules is not None and e["subject"] not in rules["monsters"]:
                errors.add(*at(loc + "/subject"), f"the rules pack has no monster '{e['subject']}'")
        elif "subject" in e and e["subject"] not in subjects:
            errors.add(*at(loc + "/subject"), f"no character, faction, region or place has the id '{e['subject']}'")
    for id_, (b, loc) in defs["background"].items():
        if rules is not None and id_ not in rules["backgrounds"]:
            errors.add(*at(loc + "/id"), f"the rules pack has no background '{id_}'")
        for i, tie in enumerate(b["ties"]):
            if tie not in subjects:
                errors.add(*at(loc + f"/ties/{i}"), f"no character, faction, region or place has the id '{tie}'")
    for id_, (a, loc) in defs["arc"].items():
        if a["region"] not in defs["region"]:
            errors.add(*at(loc + "/region"), f"no region has the id '{a['region']}'")
        elif defs["region"][a["region"]][0]["chapter"] != a["chapter"]:
            errors.add(*at(loc + "/chapter"), f"the region '{a['region']}' is chapter {defs['region'][a['region']][0]['chapter']}")
    for id_, (b, loc) in defs["beat"].items():
        if "place" in b:
            need("place", b["place"], loc, "/place", "place")
        if "encounter" in b and rules is not None and b["encounter"] not in rules["encounters"]:
            errors.add(*at(loc + "/encounter"), f"the rules pack has no encounter '{b['encounter']}'")
    for doc, where in barks:
        if doc["speaker"] != NARRATOR and doc["speaker"] not in defs["character"]:
            errors.add(where, "/speaker", f"no character has the id '{doc['speaker']}' (or use '{NARRATOR}')")


def main(argv: list[str]) -> int:
    dirs = [Path(a) for a in argv] or [CONTENT / "lore"]
    errors = check(dirs)
    for e in errors:
        print(e)
    print(f"{len(errors)} error(s) in {', '.join(d.name for d in dirs)}" if errors else
          f"OK: {', '.join(d.name for d in dirs)}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
