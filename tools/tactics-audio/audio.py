"""Imports the tactics sound library: generated WAVs in, trimmed/normalised Ogg Vorbis and art-set manifests out.

The sound and music lists live next to this script:

    sfx-list.json     {"voices": {...}, "<id>": {"prompt", "negative", "duration", "variants", "group", "loop", "use"}}
    music-list.json   {"<id>": {"prompt", "duration", "variants", "group", "loop", "use", "seed", "instrumental"}}
    sfx-assets.json   {"model", "team", "project", "clips": {"<clip id>": {"asset", "job", ...}}}
    music-assets.json (same shape)

A sound with `variants: 1` has one clip, `<id>`; with n > 1 it has clips `<id>.1` .. `<id>.n`. Generation runs outside
this script (Scenario's MCP tools), so the flow is:

    python3 tools/tactics-audio/audio.py plan              # clips listed but not generated yet (JSON, one per job)
    ...generate, record asset ids in *-assets.json, download each clip as <wav-dir>/<clip id>.wav (or .mp3/.flac)...
    python3 tools/tactics-audio/audio.py check  <wav-dir>  # duration / loudness / peak report, flags bad clips
    python3 tools/tactics-audio/audio.py import <wav-dir>  # trim, normalise, encode, write both manifests
    python3 tools/tactics-audio/audio.py status [wav-dir]  # counts per stage

Import: one-shots get leading/trailing silence trimmed (-50 dB) and a 5 ms fade at each end; loops are not trimmed, they
are made seamless by crossfading their last second(s) into their first. Loudness is measured (EBU R128 integrated, on
the clip repeated to at least 3 s so sub-400 ms clicks still measure), then a fixed gain brings it to its group's
target, capped so the sample peak stays at or under -2 dBFS before encoding; the shipped-file gate checks
-1 dBTP true peak and each group's target within 3 dB. Spiky clips get at most 6 dB of peak limiting and
stay under target if they need more. SFX are mono, music
stereo, both 48 kHz Ogg Vorbis, encoded bit-exactly so re-imports are byte-identical. Output:

    content/tactics/base/sfx/<clip id>.ogg   + manifest.json  {"sounds": {"<id>": {"files": [...], "loop", "group", "use"}}}
    content/tactics/base/music/<clip id>.ogg + manifest.json  {"tracks": {"<id>": {...}}}

The art loader turns each top-level object's entries into `<set>.<id>` keys, so `sfx.swing_sword` and `music.battle`
are valid presentation keys. The manifests keep every other top-level value a string, so no stray keys appear.
Needs ffmpeg and ffprobe.
"""
from __future__ import annotations

import json
import re
import subprocess
from concurrent.futures import ThreadPoolExecutor
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
BASE = ROOT / "content" / "tactics" / "base"
EXTS = (".wav", ".flac", ".mp3", ".ogg", ".opus")

# Integrated LUFS targets (RMS dBFS for clips too short for EBU R128 gating).
TARGET = {
    "ambience": -24.0, "battle": -18.0, "creature": -18.0, "footstep": -18.0,
    "impact": -18.0, "music": -20.0, "spell": -18.0, "sting": -19.0,
    "ui": -21.0, "weapon": -18.0, "world": -18.0,
}
LOUDNESS_TOLERANCE = 3.0
TRUE_PEAK_CEILING = -1.0  # dBTP on the shipped encoded file
PEAK_CEILING = -2.0       # dBFS before Vorbis encoding; leaves true-peak headroom
MAX_LIMIT = 6.0           # dB of peak limiting allowed when gain alone can't reach the target
LOOP_XFADE = {"ambience": 1.0, "music": 2.0}

KINDS = {
    "sfx": {"list": HERE / "sfx-list.json", "assets": HERE / "sfx-assets.json", "out": BASE / "sfx", "group_key": "sounds",
            "channels": 1, "quality": 4, "external": HERE / "external-sfx.json"},
    "music": {"list": HERE / "music-list.json", "assets": HERE / "music-assets.json", "out": BASE / "music",
              "group_key": "tracks", "channels": 2, "quality": 5},
}


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}


def entries(k: dict) -> dict[str, dict]:
    """A list's sound entries (the sfx list's top-level `voices` table describes creature families, not a sound)."""
    return {s: v for s, v in load(k["list"]).items() if s != "voices" and isinstance(v, dict) and "prompt" in v}


def sounds(kind: str) -> dict[str, dict]:
    return entries(KINDS[kind])


def clip_ids(sid: str, spec: dict) -> list[str]:
    n = int(spec.get("variants", 1))
    return [sid] if n == 1 else [f"{sid}.{i}" for i in range(1, n + 1)]


def clips() -> dict[str, dict]:
    """clip id -> {kind, sound, spec} across both lists, in a stable order."""
    out: dict[str, dict] = {}
    for kind in KINDS:
        for sid, spec in sorted(sounds(kind).items()):
            for cid in clip_ids(sid, spec):
                if cid in out:
                    raise SystemExit(f"clip id {cid} is in both lists")
                out[cid] = {"kind": kind, "sound": sid, "spec": spec}
    return out


def target_of(spec: dict) -> float:
    return TARGET[spec["group"]]


def find_source(src: Path, cid: str) -> Path | None:
    for ext in EXTS:
        p = src / f"{cid}{ext}"
        if p.exists():
            return p
    return None


def probe_duration(path: Path) -> float:
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
                       capture_output=True, text=True, check=True)
    return float(r.stdout.strip() or 0)


def measure(path: Path, pre: str = "") -> dict:
    """Integrated loudness (LUFS) and sample peak (dBFS) of `path` after the optional filter chain `pre`.
    The clip is repeated to >= 3 s so even a 150 ms click gets past the 400 ms gating blocks."""
    dur = max(probe_duration(path), 0.05)
    loops = max(0, int(3.0 / dur))
    chain = (pre + "," if pre else "") + f"aloop=loop={loops}:size=2147483647,ebur128=peak=sample,astats=metadata=0"
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-af", chain, "-f", "null", "-"],
                       capture_output=True, text=True, check=True)
    err = r.stderr
    summary = err[err.rfind("Summary:"):] if "Summary:" in err else ""
    lufs = re.search(r"I:\s*(-?[\d.]+|-inf) LUFS", summary)
    peak = re.search(r"Peak:\s*(-?[\d.]+|-inf) dBFS", summary)
    as_peak = re.findall(r"Peak level dB:\s*(-?[\d.]+|-inf)", err)
    def num(m: str | None) -> float:
        return -120.0 if m in (None, "-inf") else float(m)
    return {"lufs": num(lufs.group(1) if lufs else None),
            "peak": num(peak.group(1) if peak else (as_peak[-1] if as_peak else None)),
            "duration": probe_duration(path)}


def shipped_measure(path: Path) -> dict:
    """Measure the encoded file: LUFS for gated clips, RMS for clips under 400 ms, and true peak."""
    duration = probe_duration(path)
    result = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(path),
                             "-af", "ebur128=peak=true,astats=metadata=0:reset=0",
                             "-f", "null", "-"], capture_output=True, text=True, check=True)
    report = result.stderr
    summary = report[report.rfind("Summary:"):]
    integrated = re.search(r"Integrated loudness:\s+I:\s*(-?[\d.]+|-inf) LUFS", summary)
    true_peak = re.search(r"True peak:\s+Peak:\s*(-?[\d.]+|-inf) dBFS", summary)
    rms = re.findall(r"RMS level dB:\s*(-?[\d.]+|-inf)", report)
    if not true_peak or not rms or (duration >= 0.4 and not integrated):
        raise ValueError(f"ffmpeg did not report loudness and true peak for {path}")
    def number(value: str) -> float:
        return -120.0 if value == "-inf" else float(value)
    short = duration < 0.4 or integrated is None or number(integrated.group(1)) <= -69.0
    return {"duration": duration, "level": number(rms[-1] if short else integrated.group(1)),
            "metric": "RMS dBFS" if short else "LUFS", "true_peak": number(true_peak.group(1))}


def audit_shipped() -> list[dict]:
    """Every file registered in the shipped music and sfx manifests, including external WAVs."""
    jobs = []
    for kind, table in KINDS.items():
        manifest = load(table["out"] / "manifest.json")
        for sid, spec in manifest.get(table["group_key"], {}).items():
            for filename in spec["files"]:
                jobs.append((table["out"] / filename, kind, sid, spec["group"]))
    def inspect(job: tuple) -> dict:
        path, kind, sid, group = job
        if not path.is_file():
            raise FileNotFoundError(path)
        measured = shipped_measure(path)
        return {"file": path.relative_to(BASE).as_posix(), "id": sid, "kind": kind,
                "group": group, "target": TARGET[group], **measured}
    with ThreadPoolExecutor(max_workers=4) as pool:
        return list(pool.map(inspect, jobs))


TRIM = "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.03"
EDGE = "afade=t=in:d=0.005"


def shape_chain(spec: dict, dur: float) -> tuple[str, str | None]:
    """(filter chain before gain, loop crossfade length or None). Loops become `filter_complex` graphs."""
    if not spec.get("loop"):
        return f"{TRIM},areverse,{TRIM},{EDGE},areverse,{EDGE}", None
    x = min(LOOP_XFADE.get(spec.get("group", ""), 1.0), dur / 4)
    return "", f"{x:.3f}"


def loop_graph(dur: float, x: str, tail: str) -> str:
    """Seamless loop: body [x, dur-x] followed by the last x seconds crossfaded into the first x seconds. Played
    back to back, the end of that crossfade runs straight into the body, which starts where the head left off."""
    xf = float(x)
    return (f"[0:a]asplit=3[a][b][c];"
            f"[a]atrim=start={xf}:end={dur - xf},asetpts=PTS-STARTPTS[body];"
            f"[b]atrim=start={dur - xf},asetpts=PTS-STARTPTS[tail];"
            f"[c]atrim=end={xf},asetpts=PTS-STARTPTS[head];"
            f"[tail]afade=t=out:d={xf}:curve=qsin[t];[head]afade=t=in:d={xf}:curve=qsin[h];"
            f"[t][h]amix=inputs=2:normalize=0:duration=longest[seam];"
            f"[body][seam]concat=n=2:v=0:a=1{tail}[out]")


def encode(src: Path, ogg: Path, spec: dict, kind: str) -> dict:
    k = KINDS[kind]
    dur = probe_duration(src)
    pre, xfade = shape_chain(spec, dur)
    common = ["-ac", str(k["channels"]), "-ar", "48000", "-c:a", "libvorbis", "-q:a", str(k["quality"]),
              "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:a", "+bitexact"]
    m = measure(src, pre) if not xfade else measure(src)
    want = target_of(spec) - m["lufs"] if m["lufs"] > -70 else 0.0
    headroom = PEAK_CEILING - m["peak"]
    # Spiky clips (crackle, dice, drips) can't reach the target on gain alone: allow up to MAX_LIMIT dB of peak
    # limiting past the ceiling, and no more, so transients are tamed rather than flattened.
    extra = max(0.0, min(want - headroom, MAX_LIMIT))
    gain = min(want, headroom) + extra
    vol = f"volume={gain:.2f}dB"
    if extra > 0.5:
        vol += f",alimiter=limit={10 ** (PEAK_CEILING / 20):.4f}:level=0:attack=2:release=40:latency=1"
    if xfade:
        graph = loop_graph(dur, xfade, f",{vol},aresample=48000")
        cmd = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(src), "-filter_complex", graph,
               "-map", "[out]", *common, str(ogg)]
    else:
        cmd = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(src), "-af", f"{pre},{vol},aresample=48000",
               *common, str(ogg)]
    subprocess.run(cmd, check=True)
    return {"gain": round(gain, 2), "measured": m["lufs"], "target": target_of(spec), "result": measure(ogg)["lufs"]}


def out_name(cid: str) -> str:
    return f"{cid}.ogg"


def write_manifests() -> dict[str, int]:
    counts = {}
    for kind, k in KINDS.items():
        meta = load(k["assets"])
        table: dict[str, dict] = {}
        for sid, spec in sorted(sounds(kind).items()):
            files = [out_name(c) for c in clip_ids(sid, spec) if (k["out"] / out_name(c)).exists()]
            if not files:
                continue
            entry = {"files": files, "loop": bool(spec.get("loop")), "group": spec.get("group", ""),
                     "use": spec.get("use", "")}
            if "source" in spec:
                entry["source"] = spec["source"]
            if "voice" in spec:
                entry["voice"] = spec["voice"]
            table[sid] = entry
        external = load(k["external"]).get(k["group_key"], {}) if "external" in k else {}
        for sid, entry in external.items():
            if sid in table:
                raise ValueError(f"{sid} is registered as both generated and external audio")
            if not entry.get("files") or any(not (k["out"] / f).is_file() for f in entry["files"]):
                raise ValueError(f"external audio {sid} has a missing file")
            table[sid] = entry
        table = dict(sorted(table.items()))
        about = ("Generated sfx: id -> audio files in this folder (gameplay effects are Ogg Vorbis; nearby world-animal calls may be WAV). "
                 "Pick one at random when there are several. See tools/tactics-audio/audio.py.") if external else (
                 f"Generated {kind}: id -> Ogg Vorbis files in this folder (pick one at random when there are several). See tools/tactics-audio/audio.py.")
        manifest = {"about": about,
                    "model": meta.get("model", ""),
                    k["group_key"]: table}
        k["out"].mkdir(parents=True, exist_ok=True)
        path = k["out"] / "manifest.json"
        if load(path) != manifest:
            path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        counts[kind] = len(table)
    return counts


def plan_jobs(kinds: dict) -> list[dict]:
    """One job per sound with clips not yet generated. A music job says whether to force an instrumental take, so a
    cue written for a choir (`instrumental: false`) is generated with its voices; every music cue must say."""
    jobs = []
    for kind, k in kinds.items():
        done = load(k["assets"]).get("clips", {})
        for sid, spec in sorted(entries(k).items()):
            if kind == "music" and not isinstance(spec.get("instrumental"), bool):
                raise SystemExit(f"music cue {sid} must say \"instrumental\": true or false")
            missing = [c for c in clip_ids(sid, spec) if c not in done]
            if missing:
                job = {"kind": kind, "id": sid, "clips": missing, "prompt": spec["prompt"],
                       "negative": spec.get("negative"), "duration": spec["duration"],
                       "numOutputs": len(missing), "seed": spec.get("seed"), "loop": spec.get("loop", False)}
                if kind == "music":
                    job["instrumental"] = spec["instrumental"]
                jobs.append(job)
    return jobs


def cmd_plan() -> int:
    print(json.dumps(plan_jobs(KINDS), indent=1))
    return 0


def cmd_status(src: Path | None) -> int:
    allc = clips()
    for kind, k in KINDS.items():
        mine = [c for c, v in allc.items() if v["kind"] == kind]
        gen = load(k["assets"]).get("clips", {})
        line = (f"{kind}: {len(sounds(kind))} sounds, {len(mine)} clips; generated {sum(c in gen for c in mine)}; "
                f"encoded {sum((k['out'] / out_name(c)).exists() for c in mine)}")
        if src:
            line += f"; downloaded {sum(find_source(src, c) is not None for c in mine)}"
        print(line)
    return 0


def cmd_check(src: Path) -> int:
    bad = 0
    for cid, info in clips().items():
        f = find_source(src, cid)
        if f is None:
            print(f"  {cid:28} MISSING")
            bad += 1
            continue
        spec = info["spec"]
        m = measure(f)
        trimmed = None
        if not spec.get("loop"):
            r = subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", str(f), "-af",
                                f"{TRIM},areverse,{TRIM},areverse", "-ac", "1", "-ar", "48000", "-f", "s16le", "-"],
                               capture_output=True, check=True)
            trimmed = len(r.stdout) / (2 * 48000)
        flags = []
        if m["peak"] < -45 or m["lufs"] < -60:
            flags.append("SILENT")
        if m["peak"] > -0.1:
            flags.append("CLIPPED?")
        want = float(spec["duration"])
        if abs(m["duration"] - want) > max(0.15, 0.3 * want):
            flags.append(f"LENGTH {m['duration']:.2f}s vs {want}s")
        if trimmed is not None and trimmed < 0.35 * want and want >= 0.4:
            flags.append(f"MOSTLY SILENCE ({trimmed:.2f}s of sound)")
        bad += bool(flags)
        print(f"  {cid:28} {m['duration']:6.2f}s {m['lufs']:6.1f} LUFS peak {m['peak']:6.1f}  {' '.join(flags)}")
    print(f"{bad} flagged")
    return 0


def cmd_import(src: Path) -> int:
    done = 0
    short: list[str] = []
    allc = clips()
    for cid, info in allc.items():
        f = find_source(src, cid)
        if f is None:
            continue
        k = KINDS[info["kind"]]
        k["out"].mkdir(parents=True, exist_ok=True)
        r = encode(f, k["out"] / out_name(cid), info["spec"], info["kind"])
        if r["result"] < r["target"] - 2:
            print(f"  {cid}: too spiky to reach its target: {r['result']:.1f} LUFS (target {r['target']})")
            short.append(cid)
        done += 1
    for kind, k in KINDS.items():
        live = {out_name(c) for c, v in allc.items() if v["kind"] == kind}
        for ogg in k["out"].glob("*.ogg"):
            if ogg.name not in live:
                ogg.unlink()
                print("  removed stale", ogg.name)
    counts = write_manifests()
    print(f"imported {done} clips ({len(short)} more than 2 LU under target); manifests: {counts}")
    return 0


def main(argv: list[str]) -> int:
    cmd = argv[1] if len(argv) > 1 else "status"
    if cmd == "plan":
        return cmd_plan()
    if cmd == "status":
        return cmd_status(Path(argv[2]) if len(argv) > 2 else None)
    if cmd in ("check", "import") and len(argv) > 2:
        return (cmd_check if cmd == "check" else cmd_import)(Path(argv[2]))
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
