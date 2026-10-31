"""Pre-renders the player voice sets' barks: plans the TTS jobs, then imports the clips into the lore pack.

The lore spec names pre-rendered voice files `audio/<lineId>.<sha8 of voice+text>.ogg`, so changing a line's text (or
recasting it) makes a new file and the content hash stays honest. Each voice set has two casts (`low`, `high`, see
voice-casting.json), so one line has up to two files; `audio/manifest.json` says which is which:

    { "lines": { "<lineId>": { "low": "<file>", "high": "<file>" } }, "casts": { "<set>": { "low": "<preset>", ... } } }

The TTS call itself runs outside this script (Scenario's MCP tools), so the flow is:

    python3 tools/tactics-lore/voices.py plan  > jobs.json     # one job per line and cast, skipping ones already done
    ...generate each job's `text` with `preset`, save the WAV as <wav-dir>/<job id>.wav...
    python3 tools/tactics-lore/voices.py import <wav-dir>      # trim, normalise, encode, write the manifest

Clips are trimmed of leading and trailing silence and normalised to -16 LUFS (EBU R128), so barks from different
voices sit at the same loudness, then encoded as mono Ogg Vorbis. Needs ffmpeg.
"""
from __future__ import annotations

import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LORE = ROOT / "content" / "tactics" / "base" / "lore"
AUDIO = LORE / "audio"
CASTING = Path(__file__).resolve().parent / "voice-casting.json"


def sha8(preset: str, text: str) -> str:
    return hashlib.sha256(f"{preset}\n{text}".encode()).hexdigest()[:8]


def lines() -> list[tuple[str, str, str]]:
    """(voice set, line id, text) for every bark in every voice set, in a stable order."""
    out = []
    for f in sorted((LORE / "voices").glob("*.json")):
        doc = json.loads(f.read_text(encoding="utf-8"))
        for event, barks in doc["events"].items():
            for bark in barks:
                out.append((doc["id"], bark["id"], bark["text"]))
    return out


def jobs() -> list[dict]:
    casting = json.loads(CASTING.read_text(encoding="utf-8"))["sets"]
    out = []
    for voice_set, line_id, text in lines():
        for cast, preset in sorted(casting.get(voice_set, {}).items()):
            name = f"{line_id}.{sha8(preset, text)}.ogg"
            out.append({"id": f"{line_id}.{cast}", "line": line_id, "cast": cast, "preset": preset, "text": text,
                        "file": name, "done": (AUDIO / name).exists()})
    return out


def encode(wav: Path, ogg: Path) -> None:
    trim = "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.05"
    chain = f"{trim},areverse,{trim},areverse,loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000"
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(wav), "-af", chain, "-ac", "1",
                    "-c:a", "libvorbis", "-q:a", "5", "-map_metadata", "-1", "-fflags", "+bitexact",
                    "-flags:a", "+bitexact", str(ogg)], check=True)


def write_manifest() -> dict:
    casting = json.loads(CASTING.read_text(encoding="utf-8"))
    table: dict[str, dict[str, str]] = {}
    for job in jobs():
        if job["done"]:
            table.setdefault(job["line"], {})[job["cast"]] = job["file"]
    manifest = {"about": "Pre-rendered barks: line id -> cast -> file in this folder. See tools/tactics-lore/voices.py.",
                "model": casting["model"], "casts": casting["sets"], "lines": dict(sorted(table.items()))}
    (AUDIO / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def main(argv: list[str]) -> int:
    cmd = argv[1] if len(argv) > 1 else "status"
    if cmd == "plan":
        print(json.dumps([j for j in jobs() if not j["done"]], indent=1))
        return 0
    if cmd == "import":
        src = Path(argv[2])
        AUDIO.mkdir(parents=True, exist_ok=True)
        wanted = {j["id"]: j for j in jobs()}
        done = 0
        for wav in sorted(src.glob("*.wav")):
            job = wanted.get(wav.stem)
            if job is None:
                print("  skipped (no such job):", wav.name)
                continue
            encode(wav, AUDIO / job["file"])
            done += 1
        # Clips for lines that were reworded or recast are stale: drop them so the pack carries only live files.
        live = {j["file"] for j in jobs()}
        for ogg in AUDIO.glob("*.ogg"):
            if ogg.name not in live:
                ogg.unlink()
                print("  removed stale", ogg.name)
        m = write_manifest()
        print(f"imported {done}; manifest covers {len(m['lines'])} lines")
        return 0
    if cmd == "status":
        js = jobs()
        print(f"{sum(j['done'] for j in js)}/{len(js)} clips present")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
