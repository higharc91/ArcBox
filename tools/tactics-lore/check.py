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
               "backgrounds": "backgrounds", "arcs": "arcs", "scenes": "scenes", "townsfolk": "townsfolk", "conversations": "conversations", "quests": "quests"}
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
    if isinstance(value, int) and not isinstance(value, bool) and value > schema.get("maximum", value):
        errors.add(where, ptr, f"must be at most {schema['maximum']}")
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
    ids: dict[str, set[str]] = {"monsters": set(), "encounters": set(), "backgrounds": set(), "races": set(),
                                "classes": set(), "flags": set()}
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
    if file == "barks/townsfolk.json":
        return "townsfolk"
    return file.split("/")[0].removesuffix(".json")


def check(lore_dirs: list[Path], content: Path = CONTENT) -> Errors:
    errors = Errors()
    schemas = load_schemas()
    art = art_keys(content)
    moods = portrait_moods(content)

    defs: dict[str, dict[str, tuple[dict, str]]] = {k: {} for k in
                                                    ("character", "faction", "region", "place", "codex", "era", "event",
                                                     "voice", "background", "arc", "beat", "ending", "scene")}
    barks: list[tuple[dict, str]] = []
    scenes: list[tuple[dict, str]] = []
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
            elif kind in ("scenes", "conversations"):
                for n, scene in enumerate(doc["scenes"] if kind == "scenes" else [doc]):
                    _define(defs["scene"], scene, where, f"/scenes/{n}", "scene", errors)
                    scenes.append((scene, f"{where}#/scenes/{n}"))
                    for k, node in enumerate(scene["nodes"]):
                        for i, line in enumerate(node["lines"]):
                            at = f"/scenes/{n}/nodes/{k}/lines/{i}"
                            if line["id"] in line_ids:
                                errors.add(where, f"{at}/id", f"line id '{line['id']}' is already used in {line_ids[line['id']]}")
                            line_ids[line["id"]] = where
                            if "mood" in line and line["mood"] not in moods:
                                errors.add(where, f"{at}/id", f"mood '{line['mood']}' has no portrait")
            if kind == "townsfolk":
                for preset, data in doc["presets"].items():
                    if sum(not ln.get("requires") and not ln.get("unless") for ln in data["lines"]) < 3:
                        errors.add(where, f"/presets/{preset}", "a placed preset needs at least 3 unconditional barks")
                    for n, line in enumerate(data["lines"]):
                        at = f"/presets/{preset}/lines/{n}"
                        if line["id"] in line_ids:
                            errors.add(where, at, f"line id '{line['id']}' is already used")
                        line_ids[line["id"]] = where
                        for field in ("mood", "direction", "speaker"):
                            if not line.get(field):
                                errors.add(where, at, f"a townsfolk line needs '{field}'")
                        if line.get("speaker") != data["speaker"]:
                            errors.add(where, at, "line speaker differs from preset cast")
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

    townsfolk = next((d["presets"] for d, w in docs if w.endswith("/barks/townsfolk.json")), None)
    if townsfolk is not None:
        manifest = content / "rules" / "manifest.json"
        if manifest.exists():
            for file in json.loads(manifest.read_text())["files"]:
                if file.startswith("world/"):
                    for person in json.loads((content / "rules" / file).read_text()).get("people", []):
                        if not person.get("character") and person["preset"] not in townsfolk:
                            errors.add("townsfolk", "/presets", f"placed preset '{person['preset']}' has no bark set")
    rules = rules_ids(content)
    _references(defs, barks, art, rules, errors)
    _side_content(defs, docs, scenes, content, errors)
    _scenes(defs, scenes, rules, errors)
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
        for key, value, _ in _walk(doc):
            if key == "sets" and isinstance(value, list):
                set_by.update(value)
    for doc, where in docs:
        for key, value, _ in _walk(doc):
            if key == "when" and isinstance(value, list):
                set_by.update(value)
    for doc, where in docs:
        for key, value, ptr in _walk(doc):
            waits = ([value["flag"]] if key == "unlock" and "flag" in value
                     else value if key in ("requires", "unless") and isinstance(value, list) else [])
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


def _side_content(defs, docs, scenes, content, errors):
    listed = [q for d, w in docs if w.endswith("/quests.json") for q in d["quests"]]
    quests = {q["id"]: q for q in listed}
    if len(quests) != len(listed): errors.add("quests.json", "/quests", "duplicate quest ID")
    convos = {d["character"]: (d, w) for d, w in docs if "/conversations/" in w}
    main_flags = {f for beat, _ in defs["beat"].values() for f in beat.get("sets", [])}
    main_flags.update(f for b, _ in defs["beat"].values() for c in b.get("choices", []) for f in c.get("sets", []))
    setters = set(main_flags)
    for scene, _ in scenes:
        for key, value, _ in _walk(scene):
            if key == "sets" and isinstance(value, list):
                setters.update(value)
                if scene.get("beat") and not scene.get("camp"):
                    main_flags.update(value)
    people, things, regions, items, services = {}, {}, {}, set(), set()
    manifest = content / "rules" / "manifest.json"
    if manifest.exists():
        for file in json.loads(manifest.read_text())["files"]:
            data = json.loads((content / "rules" / file).read_text())
            for key, value, _ in _walk(data):
                if key == "sets" and isinstance(value, list): setters.update(value)
                if key == "setFlag" and isinstance(value, str): setters.add(value)
            if file.startswith("world/"):
                regions[data["id"]] = data
                people[data["id"]] = {p.get("id"):p for p in data.get("people", []) if p.get("id")}
            if file.startswith("interactables/"):
                things[data["region"]] = {t["id"]:t for t in data["interactables"]}
            if file == "items.json" or file.startswith("items/"):
                items.update(x["id"] for x in (data if isinstance(data,list) else [data]))
            if file.startswith("shops/"):
                services.update(s["id"] for s in data["shops"])
    for scene, loc in scenes:
        for node in scene["nodes"]:
            for option in node.get("options", []):
                action = option.get("quest")
                if action is None: continue
                kinds = [k for k in ("start", "advance", "complete") if k in action]
                if len(kinds) != 1:
                    errors.add(loc, "/quest", "quest action needs exactly one of start, advance or complete")
                    continue
                kind = kinds[0]; quest = quests.get(action[kind])
                if quest is None:
                    errors.add(loc, "/quest", f"unknown quest '{action[kind]}'")
                    continue
                stage = next((s for s in quest["stages"] if s["id"] == action.get("stage")), None)
                if kind == "advance" and stage is None:
                    errors.add(loc, "/quest", f"quest '{quest['id']}' has no stage '{action.get('stage')}'")
                    continue
                assigned = (quest["stages"][0] if kind == "start" else quest["done"] if kind == "complete" else stage)["when"]
                setters.update(assigned)
                if not scene.get("beat") and set(assigned) & main_flags:
                    errors.add(loc, "/quest", "a hook cannot set main-story flags through a quest action")
            for option in node.get("options", []):
                if option.get("give") and option["give"] not in items: errors.add(loc, "/give", f"unknown reward item '{option['give']}'")
                if option.get("service") and option["service"] not in services: errors.add(loc, "/service", f"unknown service '{option['service']}'")
    for region_id, region in things.items():
        if region_id not in defs["region"]: continue
        for thing in region.values():
            if "scene" in thing and not any(s["id"]==thing["scene"] and not s.get("beat") for s,_ in scenes):
                errors.add("things", "/scene", f"unknown standalone scene '{thing['scene']}'")
    for who, (scene, where) in convos.items():
        if who not in defs["character"]: errors.add(where, "/character", f"unknown character '{who}'")
        if scene.get("beat") or scene.get("after") or scene.get("camp"): errors.add(where, "", "a conversation must be a standalone scene")
        nodes = {n["id"] for n in scene["nodes"]}
        for e in scene["entries"]:
            if e["node"] not in nodes: errors.add(where, "/entries", f"entry '{e['node']}' is not a node")
        for node in scene["nodes"]:
            for line in node["lines"]:
                if not line.get("mood") or not line.get("direction"): errors.add(where, "/lines", f"line '{line['id']}' needs mood and direction")
    for q in quests.values():
        where = "quests.json/"+q["id"]
        if q["giver"] not in defs["character"]: errors.add(where,"/giver","unknown giver")
        if q["region"] not in regions: errors.add(where,"/region","unknown quest region")
        if len({s["id"] for s in q["stages"]}) != len(q["stages"]): errors.add(where,"/stages","duplicate stage ID")
        if not q["done"]["when"]: errors.add(where,"/done","a quest needs completing flags")
        for stage in q["stages"]:
            if not stage["when"] or set(stage["when"]) - setters:
                errors.add(where, "/stages", f"stage '{stage['id']}' has flags nothing in the pack can set")
            target = stage.get("target", {})
            if target and len(target) != 1: errors.add(where,"/target","target must name one person, thing or tile")
            for kind, value in target.items():
                if kind == "person" and value not in people.get(q["region"], {}): errors.add(where,"/target",f"unknown person '{value}'")
                if kind == "thing" and value not in things.get(q["region"], {}): errors.add(where,"/target",f"unknown thing '{value}'")
                if kind == "tile" and q["region"] in regions:
                    width,height = regions[q["region"]]["size"]
                    if len(value)!=2 or not (0<=value[0]<width and 0<=value[1]<height): errors.add(where,"/target","target is outside the quest region")
        for item in q["rewards"].get("items", []):
            if item["item"] not in items: errors.add(where,"/rewards",f"unknown item '{item['item']}'")
        if set(q["rewards"].get("sets", [])) & main_flags: errors.add(where,"/rewards","side rewards cannot set main-story flags")
    # Only complete game packs opt into people coverage; small validator fixtures have no townsfolk file.
    if any(w.endswith("/barks/townsfolk.json") for _, w in docs):
        bark_speakers = {d["speaker"] for d,w in docs if "/barks/" in w and "speaker" in d and d.get("events", {}).get("town")}
        for region in people.values():
            for p in region.values():
                if p.get("character") and p["character"] not in convos and p["character"] not in bark_speakers:
                    errors.add("people", "/character", f"'{p['character']}' has neither conversation nor barks")

def _scenes(defs: dict, scenes: list, rules: dict | None, errors: Errors) -> None:
    """What a schema can't say about scenes: the graph holds together, the scene belongs to a real beat, its votes are
    that beat's choices, speakers and tags exist, and every flag it sets is one arcs.json (or an encounter) already
    sets, so dialogue can't invent a flag nothing else knows about."""
    arc_flags = set(rules["flags"]) if rules else set()
    for table in ("beat", "ending"):
        for entry, _ in defs[table].values():
            arc_flags.update(entry.get("sets", []))
            for choice in entry.get("choices", []):
                arc_flags.update(choice["sets"])
    # Camp reflections may declare their own outcome flags without adding a beat to the main story chain.
    for scene, _ in scenes:
        if scene.get("camp") or "beat" not in scene:
            for node in scene.get("nodes", []):
                arc_flags.update(node.get("sets", []))
                for option in node.get("options", []):
                    arc_flags.update(option.get("sets", []))
    main_flags = set()
    for entry, _ in defs["beat"].values():
        main_flags.update(entry.get("sets", []))
        for choice in entry.get("choices", []):
            main_flags.update(choice.get("sets", []))
    for scene, _ in scenes:
        if "beat" in scene and not scene.get("camp"):
            for node in scene.get("nodes", []):
                main_flags.update(node.get("sets", []))
                for option in node.get("options", []):
                    main_flags.update(option.get("sets", []))
    tagged = {"background": set(defs["background"]) | (rules["backgrounds"] if rules else set()),
              "race": rules["races"] if rules else None, "class": rules["classes"] if rules else None}
    covered: dict[str, set[str]] = {}
    aftermath: set[str] = set()

    for scene, loc in scenes:
        where, _, ptr = loc.partition("#")
        beat_id = scene.get("beat")
        beat = defs["beat"].get(beat_id)
        if beat_id is not None and beat is None:
            errors.add(where, f"{ptr}/beat", f"no beat has the id '{scene['beat']}'")
        # A battle beat plays its lead-in, then the fight, then its aftermath, so the two kinds can't interleave in file order.
        if scene.get("camp") and scene.get("after"):
            errors.add(where, f"{ptr}/camp", "a camp scene cannot also be a battle aftermath")
        if scene.get("after") and not scene.get("camp"):
            if beat is None or beat[0]["kind"] != "battle":
                errors.add(where, f"{ptr}/after", "only a battle beat has an aftermath" if beat is None else f"only a battle beat has an aftermath, and '{beat_id}' is a {beat[0]['kind']} beat")
            aftermath.add(beat_id)
        elif not scene.get("camp") and beat_id is not None and beat_id in aftermath:
            errors.add(where, ptr, f"a lead-in of '{scene['beat']}' comes after one of its aftermath scenes; move it before them")
        if "place" in scene and scene["place"] not in defs["place"]:
            errors.add(where, f"{ptr}/place", f"no place has the id '{scene['place']}'")
        for i, who in enumerate(scene["cast"]):
            if who != NARRATOR and who not in defs["character"]:
                errors.add(where, f"{ptr}/cast/{i}", f"no character has the id '{who}' (or use '{NARRATOR}')")
        choices = {c["id"] for c in beat[0].get("choices", [])} if beat else set()
        nodes: dict[str, int] = {}
        for k, node in enumerate(scene["nodes"]):
            if node["id"] in nodes:
                errors.add(where, f"{ptr}/nodes/{k}/id", f"the node '{node['id']}' is already in this scene")
            nodes[node["id"]] = k
        if scene["start"] not in nodes:
            errors.add(where, f"{ptr}/start", f"the scene has no node '{scene['start']}'")
        edges: dict[str, set[str]] = {}
        for k, node in enumerate(scene["nodes"]):
            at = f"{ptr}/nodes/{k}"
            ways = sum(key in node for key in ("options", "next", "end"))
            if ways != 1:
                errors.add(where, at, "a node needs exactly one of 'options', 'next' or 'end'")
            for i, line in enumerate(node["lines"]):
                if line["speaker"] not in scene["cast"]:
                    errors.add(where, f"{at}/lines/{i}/speaker", f"'{line['speaker']}' isn't in the scene's cast")
            for i, flag in enumerate(node.get("sets", [])):
                if beat_id is None and flag in main_flags:
                    errors.add(where, f"{at}/sets/{i}", f"a hook cannot set the main-story flag '{flag}'")
                if flag not in arc_flags:
                    errors.add(where, f"{at}/sets/{i}", f"no beat, choice, ending or encounter sets '{flag}'")
            out = edges.setdefault(node["id"], set())
            if "next" in node:
                out.add(node["next"])
            for o, option in enumerate(node.get("options", [])):
                oat = f"{at}/options/{o}"
                if "to" not in option and "check" not in option:
                    errors.add(where, oat, "an option needs 'to' or a 'check'")
                targets = [option[x] for x in ("to",) if x in option]
                if "check" in option:
                    targets += [option["check"]["pass"], option["check"]["fail"]]
                out.update(targets)
                if node["id"] in targets:
                    # The game never offers an option that leads to a node already played, and its own node is (SceneRun.Offered).
                    errors.add(where, oat, f"leads back to its own node '{node['id']}', so it's never offered")
                if "choice" in option:
                    if not node.get("vote"):
                        errors.add(where, f"{oat}/choice", "only a vote's options can be an arcs.json choice")
                    if option["choice"] not in choices:
                        errors.add(where, f"{oat}/choice", f"the beat '{scene.get('beat')}' has no choice '{option['choice']}'")
                    covered.setdefault(beat_id, set()).add(option["choice"])
                for i, flag in enumerate(option.get("sets", [])):
                    if beat_id is None and flag in main_flags:
                        errors.add(where, f"{oat}/sets/{i}", f"a hook cannot set the main-story flag '{flag}'")
                    if flag not in arc_flags:
                        errors.add(where, f"{oat}/sets/{i}", f"no beat, choice, ending or encounter sets '{flag}'")
                for kind, ids in option.get("tags", {}).items():
                    known = tagged[kind]
                    for i, id_ in enumerate(ids):
                        if known is not None and id_ not in known:
                            errors.add(where, f"{oat}/tags/{kind}/{i}", f"no {kind} has the id '{id_}'")
            opts = node.get("options", [])
            if opts and all(o.get("requires") for o in opts):
                # Every option is gated: some earlier vote must guarantee one of them shows, i.e. for one choice beat,
                # each of its choices sets a flag that one of these options waits on.
                waits = {f for o in opts for f in o["requires"]}
                if not any(beat_choices and all(set(c["sets"]) & waits for c in beat_choices)
                           for beat_choices in (b.get("choices", []) for b, _ in defs["beat"].values())):
                    errors.add(where, at, "every option is gated on flags, and no earlier vote guarantees one of them "
                                          "is set: the party could be left with nothing to pick")
            for target in out:
                if target not in nodes:
                    errors.add(where, at, f"leads to '{target}', which isn't a node in this scene")
        # Every node must be reachable from the start, and at least one path must end.
        seen, todo = set(), [n for n in [scene["start"], *[e["node"] for e in scene.get("entries", [])]] if n in nodes]
        while todo:
            n = todo.pop()
            if n not in seen:
                seen.add(n)
                todo.extend(t for t in edges.get(n, ()) if t in nodes)
        for node_id, k in nodes.items():
            if node_id not in seen:
                errors.add(where, f"{ptr}/nodes/{k}", f"the node '{node_id}' can't be reached from the start")
        if not any("end" in scene["nodes"][nodes[n]] for n in seen):
            errors.add(where, ptr, "no path through the scene ends")
    # A choice beat played as a scene must offer every one of its choices, or a branch of the story is unreachable.
    for beat_id, got in covered.items():
        want = {c["id"] for c in defs["beat"][beat_id][0].get("choices", [])} if beat_id in defs["beat"] else set()
        missing = sorted(want - got)
        if missing:
            where, _, ptr = defs["beat"][beat_id][1].partition("#")
            errors.add(where, ptr, f"its scenes never offer the choice(s) {', '.join(missing)}")


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
