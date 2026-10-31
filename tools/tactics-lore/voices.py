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
        elif kind == "barks":
            out += [(f"char:{doc['speaker']}", b["id"], b["text"]) for bs in doc["events"].values() for b in bs]
        elif kind == "scenes":
            out += [(f"char:{ln['speaker']}", ln["id"], ln["text"])
                    for s in doc["scenes"] for n in s["nodes"] for ln in n["lines"]]
    return out + say_lines()


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


def jobs() -> list[dict]:
    cast = casting()
    out = []
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
            raw = f"{preset}.{sha8(preset, text)}"
            file = f"{line_id}.{sha8(preset, pitch, filt, text)}.ogg"
            out.append({"id": f"{line_id}.{name}", "line": line_id, "cast": name, "speaker": who, "preset": preset,
                        "pitch": pitch, "filter": filt, "text": text, "raw": raw, "file": file,
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
    chain += [trim, "areverse", trim, "areverse", "loudnorm=I=-16:TP=-1.5:LRA=11", f"aresample={RATE}"]
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(wav), "-af", ",".join(chain),
                    "-ac", "1", "-c:a", "libvorbis", "-q:a", "5", "-map_metadata", "-1", "-fflags", "+bitexact",
                    "-flags:a", "+bitexact", str(ogg)], check=True)


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
    """A coarse screen for gross render failures: a clip whose pitch is far (over 10 semitones) from the rest of its
    speaker's, or that is almost silent. Pitch and brightness can't tell emotion from identity (a shout sits several
    semitones up), so ordinary expressive range is expected and isn't flagged, and clips under 1.2 s, where pitch
    tracking is unreliable, are skipped. Voice identity itself comes from the fixed preset each speaker is rendered
    with; this only catches a wrong preset or a broken render."""
    by: dict[str, list[tuple[str, float, float]]] = {}
    for j in jobs():
        if j["done"]:
            p, c = _features(AUDIO / j["file"])
            dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                                        str(AUDIO / j["file"])], capture_output=True, text=True).stdout or 0)
            by.setdefault(f"{j['speaker']}.{j['cast']}", []).append((j["id"], p, c, dur))
    flagged = 0
    for who, rows in sorted(by.items()):
        ps = sorted(p for _, p, _, _ in rows if not math.isnan(p))
        if len(ps) < 4:
            continue
        med = ps[len(ps) // 2]
        for id_, p, c, dur in rows:
            off = abs(12 * math.log2(p / med)) if not math.isnan(p) else 0
            if dur < 1.2:
                continue
            if off > 10 or (math.isnan(p) and dur > 2):
                flagged += 1
                print(f"  {id_}: pitch {p:.0f} Hz vs {med:.0f} ({off:.1f} st), {dur:.1f} s")
        print(f"{who}: {len(rows)} clips, median pitch {med:.0f} Hz")
    print(f"{flagged} clip(s) flagged")
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
                             "speaker": j["speaker"], "scene": j["line"].startswith("ch")})
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
