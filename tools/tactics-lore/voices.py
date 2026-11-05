"""Pre-renders every spoken line in the lore pack: plans the TTS jobs, then imports the clips.

The lore spec names voice files `audio/<lineId>.<sha8>.ogg`, where the hash covers the voice and the text. Rewording or
recasting a line makes a new file, so the content hash stays honest and stale clips are easy to find.

Who says a line decides its voice (voice-casting.json):
- player voice sets (`voices/<set>.json`) have two casts, `low` and `high`, so a line has two files;
- barks (`barks/<speaker>.json`) and scene lines (`scenes/*.json`, each with a `speaker`) use that speaker's one cast,
  `main`, with an optional pitch shift and filter that keep speakers who share a preset apart.

`audio/manifest.json` maps line id → cast → file, and lists the casting:

    { "lines": { "<lineId>": { "main": "<file>" } }, "casts": {...}, "characters": {...} }

The TTS call runs outside this script (Scenario's MCP tools), so the flow is:

    python3 tools/tactics-lore/voices.py plan > jobs.json     # one job per line and cast not yet rendered
    ...generate each job's `text` with `preset`, save the WAV as <wav-dir>/<job id>.wav...
    python3 tools/tactics-lore/voices.py import <wav-dir>     # filter, trim, normalise, encode, write the manifest
    python3 tools/tactics-lore/voices.py check                # coarse screen for broken or mis-cast renders

Clips are trimmed of silence and normalised to -16 LUFS (EBU R128), so every speaker sits at one loudness. They're
encoded as mono Ogg Vorbis at the TTS's 24 kHz, byte-identically for the same input. Needs ffmpeg.
"""
from __future__ import annotations

import hashlib
import json
import math
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LORE = ROOT / "content" / "tactics" / "base" / "lore"
AUDIO = LORE / "audio"
CASTING = Path(__file__).resolve().parent / "voice-casting.json"
RATE = 24000

# Filters keep two speakers on one preset apart, and give a few their colour. ffmpeg -af fragments, applied before the
# trim and loudness pass.
FILTERS = {
    "helm": "aecho=0.8:0.55:9|17|26:0.35|0.25|0.15,lowpass=f=4200,highpass=f=110",
    "echo": "aecho=0.8:0.5:70|140:0.22|0.12",
    "fey": "chorus=0.6:0.85:28|39:0.3|0.22:0.25|0.4:1.6|2.2",
}


def casting() -> dict:
    return json.loads(CASTING.read_text(encoding="utf-8"))


def sha8(*parts: object) -> str:
    return hashlib.sha256("\n".join(map(str, parts)).encode()).hexdigest()[:8]


def lines() -> list[tuple[str, str, str]]:
    """(source, line id, text) for every spoken line, in a stable order. Source is 'set:<id>' for a player voice
    set and 'char:<speaker>' for barks and scene lines."""
    pack = json.loads((LORE / "pack.json").read_text(encoding="utf-8"))
    out = []
    for file in pack["files"]:
        doc = json.loads((LORE / file).read_text(encoding="utf-8"))
        kind = file.split("/")[0].removesuffix(".json")
        if kind == "voices":
            out += [(f"set:{doc['id']}", b["id"], b["text"]) for bs in doc["events"].values() for b in bs]
        elif file == "barks/townsfolk.json":
            out += [(f"char:{data['speaker']}", b["id"], b["text"])
                    for data in doc["presets"].values() for b in data["lines"]]
        elif kind == "barks":
            out += [(f"char:{doc['speaker']}", b["id"], b["text"]) for bs in doc["events"].values() for b in bs]
        elif kind == "conversations":
            out += [(f"char:{ln['speaker']}", ln["id"], ln["text"])
                    for n in doc["nodes"] for ln in n["lines"]]
        elif kind == "scenes":
            out += [(f"char:{ln['speaker']}", ln["id"], ln["text"])
                    for s in doc["scenes"] for n in s["nodes"] for ln in n["lines"]]
    return out + say_lines() + ashen_ford_thing_lines()


def ashen_ford_thing_lines() -> list[tuple[str, str, str]]:
    """The new Ashen Ford notices and cellar narration use the existing narrator cast.

    A notice has two delivery moments: the passive/Search find and a later Look. They
    share one TTS render, though their stable line IDs differ.
    """
    path = ROOT / "content/tactics/base/rules/interactables/ashen_ford.json"
    things = {thing["id"]: thing for thing in json.loads(path.read_text(encoding="utf-8"))["interactables"]}
    out = []
    for id in ("af_market_cellar_draft", "af_bank_claw_marks", "af_bridge_carving", "af_southfield_goat_tracks"):
        spoken = " ".join(things[id]["text"])
        out.extend([("char:narrator", id, spoken), ("char:narrator", id + "_use", spoken)])
    for index, spoken in enumerate(things["af_market_cellar_ledger"]["text"]):
        out.append(("char:narrator", f"thing.af_market_cellar_ledger.words.{index}", spoken))
    out.extend([
        ("char:narrator", "af_market_cellar_hatch_use", "The cellar hatch opens."),
        ("char:narrator", "af_market_cellar_store_use", "Inside: Smoke Bomb."),
    ])
    return out


def say_lines() -> list[tuple[str, str, str]]:
    """Battle lines from the rules encounters' `say` actions. They have no lore line id yet (lore spec follow-up 1),
    so each gets `say_<encounter>_<n>` in file order, and its encounter speaker (a unit id or tag) is mapped to a
    character through the casting's `aliases`, falling back to the id itself."""
    aliases = casting().get("aliases", {})
    rules = ROOT / "content" / "tactics" / "base" / "rules"
    out = []
    for file in json.loads((rules / "manifest.json").read_text(encoding="utf-8"))["files"]:
        if not file.startswith("encounters/"):
            continue
        enc = json.loads((rules / file).read_text(encoding="utf-8"))
        n = 0

        def walk(node):
            nonlocal n
            if isinstance(node, dict):
                if isinstance(node.get("say"), dict):
                    n += 1
                    who = node["say"]["speaker"]
                    out.append((f"char:{aliases.get(who, who)}", f"say_{enc['id']}_{n}", node["say"]["line"]))
                for v in node.values():
                    walk(v)
            elif isinstance(node, list):
                for v in node:
                    walk(v)
        walk(enc)
    return out


def delivery() -> dict[str, dict]:
    """Version delivery only for the new directed lines; the existing 1,262 clip hashes stay stable."""
    out = {}
    def walk(node):
        if isinstance(node, dict):
            if (isinstance(node.get("id"), str) and "text" in node and
                    node["id"].startswith(("sq_", "town_", "bark_pip_whistle_"))):
                out[node["id"]] = {"direction": node.get("direction", ""), "mood": node.get("mood", "idle")}
            for value in node.values(): walk(value)
        elif isinstance(node, list):
            for value in node: walk(value)
    for file in json.loads((LORE / "pack.json").read_text())["files"]:
        if file.startswith(("conversations/", "scenes/", "barks/")):
            walk(json.loads((LORE / file).read_text()))
    return out


def jobs() -> list[dict]:
    cast = casting()
    out = []
    directed = delivery()
    for source, line_id, text in lines():
        kind, who = source.split(":", 1)
        if kind == "set":
            voices = [(c, {"preset": p}) for c, p in sorted(cast["sets"].get(who, {}).items())]
        else:
            voices = [("main", cast["characters"][who])] if who in cast["characters"] else []
            if not voices:
                print(f"  no casting for speaker '{who}' (line {line_id})", file=sys.stderr)
        for name, v in voices:
            preset, pitch, filt = v["preset"], v.get("pitch", 0), v.get("filter", "")
            # The raw TTS clip depends only on preset and text: speakers sharing a preset can share a render.
            style = directed.get(line_id)
            render_text = f"<{style['direction']} Mood: {style['mood']}.> {text}" if style else text
            raw = f"{preset}.{sha8(preset, render_text)}"
            file = f"{line_id}.{sha8(preset, pitch, filt, text, render_text) if style else sha8(preset, pitch, filt, text)}.ogg"
            out.append({"id": f"{line_id}.{name}", "line": line_id, "cast": name, "speaker": who, "preset": preset,
                        "pitch": pitch, "filter": filt, "text": text, "renderText": render_text, "delivery": style, "raw": raw, "file": file,
                        "done": (AUDIO / file).exists()})
    return out


def encode(wav: Path, ogg: Path, pitch: float = 0, filt: str = "") -> None:
    chain = []
    if pitch:
        r = 2 ** (pitch / 12)   # shift pitch, keep tempo: resample up or down, then stretch back
        chain += [f"asetrate={RATE * r:.3f}", f"aresample={RATE}", f"atempo={1 / r:.6f}"]
    if filt:
        chain.append(FILTERS[filt])
    trim = "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.05"
    level = ["loudnorm=I=-16:TP=-1.5:LRA=11", f"aresample={RATE}"]
    out = ["-ac", "1", "-c:a", "libvorbis", "-q:a", "5", "-map_metadata", "-1", "-fflags", "+bitexact",
           "-flags:a", "+bitexact", str(ogg)]
    ff = ["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y"]
    try:
        subprocess.run(ff + ["-i", str(wav), "-af", ",".join(chain + [trim, "areverse", trim, "areverse"] + level)]
                       + out, check=True, timeout=60)
    except subprocess.TimeoutExpired:
        # On rare clips ffmpeg's graph deadlocks when the reverse-trim feeds loudnorm (idle CPU, no output). Split it:
        # shape and trim to a lossless float WAV, then level and encode. Still deterministic: a clip that hangs always
        # hangs, so it always takes this path.
        mid = ogg.with_suffix(".trim.wav")
        try:
            subprocess.run(ff + ["-i", str(wav), "-af", ",".join(chain + [trim, "areverse", trim, "areverse"]),
                                 "-c:a", "pcm_f32le", str(mid)], check=True, timeout=60)
            subprocess.run(ff + ["-i", str(mid), "-af", ",".join(level)] + out, check=True, timeout=60)
        finally:
            mid.unlink(missing_ok=True)


def write_manifest() -> dict:
    cast = casting()
    table: dict[str, dict[str, str]] = {}
    for job in jobs():
        if job["done"]:
            table.setdefault(job["line"], {})[job["cast"]] = job["file"]
    manifest = {"about": "Pre-rendered lines: line id -> cast -> file in this folder. 'main' is a speaker's own voice; "
                         "player voice sets have 'low' and 'high'. See tools/tactics-lore/voices.py.",
                "model": cast["model"], "casts": cast["sets"],
                "characters": {k: {x: v[x] for x in ("preset", "pitch", "filter") if x in v}
                               for k, v in sorted(cast["characters"].items())},
                "lines": dict(sorted(table.items()))}
    (AUDIO / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def _features(ogg: Path) -> tuple[float, float]:
    """(median pitch in Hz, spectral centroid in Hz) of a clip: enough to spot a render whose voice drifted."""
    import numpy as np
    pcm = subprocess.run(["ffmpeg", "-loglevel", "error", "-i", str(ogg), "-f", "f32le", "-ac", "1", "-ar", "16000",
                          "-"], capture_output=True, check=True).stdout
    x = np.frombuffer(pcm, dtype=np.float32)
    frame, hop, pitches = 640, 320, []
    for i in range(0, len(x) - frame, hop):
        f = x[i:i + frame] * np.hanning(frame)
        if np.sqrt(np.mean(f ** 2)) < 0.02:
            continue
        ac = np.correlate(f, f, "full")[frame - 1:]
        lo, hi = 16000 // 400, 16000 // 60          # 60–400 Hz: the range of a speaking voice
        k = lo + int(np.argmax(ac[lo:hi]))
        if ac[k] > 0.3 * ac[0]:
            pitches.append(16000 / k)
    spec = np.abs(np.fft.rfft(x[: 16000 * 4]))
    freqs = np.fft.rfftfreq(min(len(x), 16000 * 4), 1 / 16000)
    centroid = float((spec * freqs).sum() / max(spec.sum(), 1e-9))
    return (float(np.median(pitches)) if pitches else math.nan), centroid


def check() -> int:
    """Measure pitch AND brightness, including new-versus-existing identity drift; cache unchanged clips."""
    import statistics
    cache_path = ROOT / ".cache" / "tactics-voice-features.json"
    cache_path.parent.mkdir(exist_ok=True)
    cache = json.loads(cache_path.read_text()) if cache_path.exists() else {}
    assets = json.loads((ROOT / "tools/tactics-lore/voice-assets.json").read_text())
    records = assets.get("ashenFordSecrets", [])
    new_raws = {r["raw"] for r in records}
    by = {}
    for j in jobs():
        if not j["done"]: continue
        path = AUDIO / j["file"]
        key = f"{j['file']}:{path.stat().st_size}"
        if key not in cache:
            p, c = _features(path)
            dur = float(subprocess.check_output(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)], text=True))
            cache[key] = [p if math.isfinite(p) else None, c, dur]
            if len(cache) % 25 == 0: cache_path.write_text(json.dumps(cache))
        p, c, dur = cache[key]
        by.setdefault(f"{j['speaker']}.{j['cast']}", []).append(dict(id=j["id"], pitch=p, centroid=c, duration=dur, new=j["raw"] in new_raws))
    cache_path.write_text(json.dumps(cache))
    flagged, comparisons = [], []
    for who, rows in sorted(by.items()):
        voiced = [r["pitch"] for r in rows if r["pitch"] and r["duration"] >= 1.2]
        if not voiced: continue
        med = statistics.median(voiced)
        for r in rows:
            if r["duration"] < 1.2: continue
            off = abs(12*math.log2(r["pitch"]/med)) if r["pitch"] else 0
            if off > 10 or (not r["pitch"] and r["duration"] > 2):
                flagged.append(dict(speaker=who, line=r["id"], kind="clip", semitones=round(off,2)))
        old = [r for r in rows if not r["new"] and r["pitch"] and r["duration"] >= 1.2]
        new = [r for r in rows if r["new"] and r["pitch"] and r["duration"] >= 1.2]
        comparison = dict(speaker=who, oldCount=len(old), newCount=len(new), medianPitch=round(med,2))
        if old and new:
            op, np = statistics.median(r["pitch"] for r in old), statistics.median(r["pitch"] for r in new)
            oc, nc = statistics.median(r["centroid"] for r in old), statistics.median(r["centroid"] for r in new)
            shift, ratio = 12*math.log2(np/op), nc/oc
            comparison.update(oldPitch=round(op,2),newPitch=round(np,2),pitchShift=round(shift,2),oldCentroid=round(oc,2),newCentroid=round(nc,2),centroidRatio=round(ratio,3))
            if abs(shift) > 6 or not .6 <= ratio <= 1.65:
                flagged.append(dict(speaker=who,kind="drift",pitchShift=round(shift,2),centroidRatio=round(ratio,3)))
        comparisons.append(comparison)
        print(json.dumps(comparison), flush=True)
    report = dict(comparisons=comparisons,flagged=flagged,thresholds=dict(clipSemitones=10,speakerSemitones=6,centroidRatio=[.6,1.65]))
    report_path = ROOT / "docs/superpowers/reports/2026-11-04-ashen-ford-secrets/voice-check.json"
    report_path.parent.mkdir(parents=True,exist_ok=True)
    report_path.write_text(json.dumps(report,indent=2)+"\n")
    print(f"{len(flagged)} clip(s) or speaker group(s) flagged")
    for row in flagged: print(json.dumps(row))
    return 1 if flagged else 0


def main(argv: list[str]) -> int:
    cmd = argv[1] if len(argv) > 1 else "status"
    if cmd == "plan":
        # One TTS render per distinct (preset, text): speakers that share a preset share the raw clip.
        todo, seen = [], set()
        for j in jobs():
            if not j["done"] and j["raw"] not in seen:
                seen.add(j["raw"])
                todo.append({"raw": j["raw"], "preset": j["preset"], "text": j["text"], "line": j["line"],
                             "speaker": j["speaker"], "renderText": j["renderText"], "delivery": j["delivery"], "scene": j["line"].startswith("ch")})
        print(json.dumps(todo, indent=1))
        return 0
    if cmd == "import":
        src = Path(argv[2])
        AUDIO.mkdir(parents=True, exist_ok=True)
        done = missing = 0
        for j in jobs():
            wav = src / f"{j['raw']}.wav"
            if (AUDIO / j["file"]).exists():
                continue
            if not wav.exists():
                missing += 1
                continue
            encode(wav, AUDIO / j["file"], j["pitch"], j["filter"])
            done += 1
        live = {j["file"] for j in jobs()}
        for ogg in AUDIO.glob("*.ogg"):
            if ogg.name not in live:
                ogg.unlink()
                print("  removed stale", ogg.name)
        m = write_manifest()
        print(f"imported {done}, {missing} still without a render; manifest covers {len(m['lines'])} lines")
        return 0
    if cmd == "check":
        return check()
    if cmd == "status":
        js = jobs()
        raws = {j["raw"] for j in js if not j["done"]}
        print(f"{sum(j['done'] for j in js)}/{len(js)} clips present; {len(raws)} renders to make")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
