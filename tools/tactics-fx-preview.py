"""Capture the ordinary Tactics TV scene on a frozen main worktree and the current branch."""
import argparse
import html
import json
import os
from pathlib import Path
import re
import shutil
import statistics
import subprocess
import tempfile
import time

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
HARNESS = [
    "games/Tactics/Tactics.Presentation/Beats/FxShowcase.cs",
    "src/ArcBox.Host/games/tactics/FxDemoFeeder.cs",
    "src/ArcBox.Host/games/tactics/FxDemoFeeder.cs.uid",
    "src/ArcBox.Host/Main.cs", "src/ArcBox.Host/Shell/AppArgs.cs",
    "src/ArcBox.Host/Shell/Screenshotter.cs",
]


def run(args, *, cwd=ROOT, log=None, env=None):
    if log:
        with open(log, "w") as stream:
            subprocess.run(args, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT, check=True)
    else:
        return subprocess.check_output(args, cwd=cwd, text=True).strip()


def prepare(before, baseline, cache):
    if not before.exists():
        run(["git", "worktree", "add", "--detach", str(before), baseline])
    if run(["git", "rev-parse", "HEAD"], cwd=before) != baseline:
        raise RuntimeError("Before worktree is not the frozen baseline.")
    if run(["git", "branch", "--show-current"], cwd=before):
        raise RuntimeError("Use a detached before worktree, never a user branch checkout.")
    changed = set(run(["git", "diff", "--name-only", "HEAD"], cwd=before).splitlines())
    changed.update(run(["git", "ls-files", "--others", "--exclude-standard"], cwd=before).splitlines())
    if changed - set(HARNESS):
        raise RuntimeError(f"Before worktree contains non-harness edits: {changed - set(HARNESS)}")
    # Only the neutral entry/fixture/capture harness is backported. No effects, renderer, timing or settings code.
    for name in HARNESS:
        target = before / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / name, target)
    for name, tree in [("before", before), ("after", ROOT)]:
        print(f"Build/import {name}", flush=True)
        run(["dotnet", "build", "src/ArcBox.Host/ArcBox.Host.csproj", "-c", "Debug"], cwd=tree, log=cache / f"{name}-build.log")
        run(["godot", "--headless", "--editor", "--path", str(tree / "src/ArcBox.Host"), "--import"], cwd=tree, log=cache / f"{name}-import.log")
        if name == 'before':
            # The editor can create a missing C# UID beside a tracked main file. It is import output, not a backport.
            extras = set(run(['git', 'ls-files', '--others', '--exclude-standard'], cwd=before).splitlines()) - set(HARNESS)
            tracked = set(run(['git', 'ls-files'], cwd=before).splitlines())
            for extra in extras:
                if extra.endswith('.cs.uid') and extra[:-4] in tracked:
                    (before / extra).unlink()
                else:
                    raise RuntimeError(f'Unexpected before import output: {extra}')


def capture(tree, selector, directory, mode, reduced=False, performance=False):
    directory.mkdir(parents=True, exist_ok=True)
    # A failed/shorter retake must not inherit a completion marker or extra frames from an older run.
    old_meta = directory / "frames/showcase.json"
    old_meta.unlink(missing_ok=True)
    for old in (directory / "frames").glob("*-frame*.png"):
        old.unlink()
    display = directory / "display"
    with display.open("w") as number, (directory / "xvfb.log").open("w") as log:
        server = subprocess.Popen(["Xvfb", "-displayfd", str(number.fileno()), "-screen", "0", "1920x1080x24", "-nolisten", "tcp"],
                                  stdout=log, stderr=log, pass_fds=(number.fileno(),))
        try:
            for _ in range(100):
                if display.stat().st_size: break
                if server.poll() is not None: raise RuntimeError("Xvfb exited before it supplied a display.")
                time.sleep(.1)
            env = dict(os.environ, DISPLAY=":" + display.read_text().strip(), LIBGL_ALWAYS_SOFTWARE="1",
                       XDG_DATA_HOME=str(directory / "userdata"), ARCBOX_FX_CINEMATICS=mode,
                       ARCBOX_FX_REDUCED="1" if reduced else "0", ARCBOX_FX_CAPTURE="0" if performance else "1",
                       ARCBOX_FX_FAST_PNG="1")
            args = ["godot", "--path", str(tree / "src/ArcBox.Host"), "--rendering-method", "gl_compatibility",
                    "--audio-driver", "Dummy", "--disable-vsync", "--fixed-fps", "60", "--resolution", "1920x1080", "--",
                    f"--fx-demo={selector}", "--preset=low", "--res=1920x1080", f"--shots={directory / 'frames'}", "--quit-after=300"]
            if performance: args.append("--perf")
            run(args, cwd=tree, log=directory / "godot.log", env=env)
        finally:
            server.terminate()
            server.wait(timeout=10)
    log = (directory / "godot.log").read_text()
    if "FX DEMO complete" not in log or "ERROR:" in log:
        raise RuntimeError(f"Incomplete/failed showcase: {directory / 'godot.log'}")
    return json.loads((directory / "frames/showcase.json").read_text())


def frames(directory, ability):
    key = re.sub(r"[^a-z0-9-]", "-", ability.lower()).strip("-")
    found = list((directory / "frames").glob(f"*-{key}-frame*.png"))
    return sorted(found, key=lambda p: int(re.search(r"frame(\d+)\.png$", p.name)[1]))


def media(before, after, old, new, output, key, mode, cache, reuse=False):
    left, right = frames(before, old["id"]), frames(after, new["id"])
    if not left or not right: raise RuntimeError(f"Missing frames for {key}")
    film = output / f"{key}-{mode}.webm"
    poster_path = output / f"{key}-{mode}.png"
    if reuse and film.exists() and poster_path.exists() and min(film.stat().st_mtime, poster_path.stat().st_mtime) > max(p.stat().st_mtime for p in left + right):
        return Image.open(poster_path).convert("RGB")
    # Keep full-resolution posters; compact nearest-neighbour videos fit a browser at normal zoom.
    # Hold the shorter side's final frame through the longer cinematic.
    seq = cache / f"pair-{mode}-{key}"
    seq.mkdir(exist_ok=True)
    for side, images in [("left", left), ("right", right)]:
        target = seq / side
        target.mkdir(exist_ok=True)
        for i in range(max(len(left), len(right))):
            path = target / f"{i:06}.png"
            if not path.exists(): path.symlink_to(images[min(i, len(images)-1)])
    run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", "30", "-i", str(seq / "left/%06d.png"),
         "-framerate", "30", "-i", str(seq / "right/%06d.png"), "-filter_complex",
         "[0:v]scale=960:540:flags=neighbor[l];[1:v]scale=960:540:flags=neighbor[r];[l][r]hstack=inputs=2[v]",
         "-map", "[v]", "-an", "-c:v", "libvpx-vp9", "-row-mt", "1", "-threads", "4", "-cpu-used", "4", "-crf", "36", "-b:v", "0", str(film)], log=seq / "ffmpeg.log")
    # Align the impact in posters, while the video retains the complete real timing from cast to settle.
    def peak(row):
        # Pick each real effect's bright release, including its own cinematic lead; never guess from total duration.
        lead = max(0, row['seconds'] - row.get('baseSeconds', row['seconds']) - .3)
        return row.get('posterAt', row['seconds'] * .55) + lead + 1/30
    a = Image.open(left[min(len(left)-1, round(peak(old) * 30))]).convert("RGB")
    b = Image.open(right[min(len(right)-1, round(peak(new) * 30))]).convert("RGB")
    poster = Image.new("RGB", (3840, 1130), "#101222")
    poster.paste(a, (0, 50)); poster.paste(b, (1920, 50))
    pen = ImageDraw.Draw(poster)
    font = ImageFont.truetype("DejaVuSans.ttf", 28)
    pen.text((25, 9), f"BEFORE · {old['name']} · {old['seconds']:.2f}s", font=font, fill="#f5e6bf")
    pen.text((1945, 9), f"AFTER · {mode.upper()} · {new['seconds']:.2f}s", font=font, fill="#f5e6bf")
    poster.save(output / f"{key}-{mode}.png", optimize=True)
    if old["id"] == "wizard.fireball" and mode == "off":
        detail = Image.new("RGB", (1220, 440), "#101222")
        for side, label in [(0, "Before"), (1, "With spell light")]:
            detail.paste(poster.crop((800 + side * 1920, 430, 1410 + side * 1920, 820)), (side * 610, 50))
            ImageDraw.Draw(detail).text((side * 610 + 16, 9), label, font=font, fill="#f5e6bf")
        detail.save(output / "fire-lighting-detail.png", optimize=True)
    return poster


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("selector", help="step0 or class[:abilityId]; a missing real trigger fixture fails the class capture")
    parser.add_argument("--before-ref", help="frozen main when this step/class started; required for new classes")
    parser.add_argument("--before-worktree", type=Path)
    parser.add_argument("--cache", type=Path)
    parser.add_argument("--reuse", action="store_true", help="reuse completed raw captures in the given cache")
    parser.add_argument("--perf-runs", type=int, default=3)
    args = parser.parse_args()
    for tool in ["git", "dotnet", "godot", "Xvfb", "ffmpeg"]:
        if not shutil.which(tool): parser.error(f"{tool} must be on PATH")
    slug = args.selector.replace(":", "-")
    output = ROOT / "docs/superpowers/reports/combat-vfx" / slug
    output.mkdir(parents=True, exist_ok=True)
    prior = json.loads((output / "baseline.json").read_text()) if (output / "baseline.json").exists() else {}
    reference = args.before_ref or prior.get("commit") or ("9a7ffb54" if args.selector == "step0" else None)
    if not reference: parser.error("Freeze main with --before-ref for a class's first capture.")
    baseline = run(["git", "rev-parse", reference])
    cache = (args.cache or Path(tempfile.mkdtemp(prefix="arcbox-fx-"))).resolve()
    cache.mkdir(parents=True, exist_ok=True)
    before = (args.before_worktree or cache / "before-worktree").resolve()
    if not args.reuse: prepare(before, baseline, cache)
    report = {"commit": baseline, "harnessOnlyBackport": HARNESS, "resolution": [1920, 1080], "simulationFps": 60,
              "captureFps": 30, "seed": 7, "map": "crypt_hall", "preset": "low", "renderer": "gl_compatibility", "audio": "Dummy"}
    (output / "baseline.json").write_text(json.dumps(report, indent=2) + "\n")
    captures = {}
    variants = [("before", before, "Full", False), ("full", ROOT, "Full", False)]
    if args.selector == "step0":
        variants += [("short", ROOT, "Short", False), ("off", ROOT, "Off", False), ("reduced", ROOT, "Full", True)]
    performance = {}
    for name, tree, mode, reduced in variants:
        directory = cache / name
        print(f"Capture {name}", flush=True)
        meta = directory / "frames/showcase.json"
        captures[name] = json.loads(meta.read_text()) if args.reuse and meta.exists() else capture(tree, args.selector, directory, mode, reduced)
        runs = []
        for repeat in range(args.perf_runs):
            perf = cache / f"perf-{name}-{repeat}"
            meta = perf / "frames/showcase.json"
            runs.append(json.loads(meta.read_text()) if args.reuse and meta.exists() else capture(tree, args.selector, perf, mode, reduced, True))
        if runs:
            # Fresh neutral metadata selects the reviewed ability/proc, including reactions, in reused native frames.
            for row, measured in zip(captures[name], runs[0]):
                if row['id'] != measured['id'] or abs(row['seconds'] - measured['seconds']) > 2/60:
                    raise RuntimeError(f"Native/performance fixture mismatch: {row['id']}")
                for field in ('baseSeconds', 'posterAt', 'textureBytes'):
                    if field in measured: row[field] = measured[field]
        performance[name] = {row["id"]: statistics.median(r[i]["frameMedianMs"] for r in runs)
                             for i, row in enumerate(captures[name])} if runs else {}
    (output / "timings.json").write_text(json.dumps(captures, indent=2) + "\n")
    if args.perf_runs or not (output / "performance.json").exists():
        (output / "performance.json").write_text(json.dumps(performance, indent=2) + "\n")
    cards, posters = [], []
    for i, old in enumerate(captures["before"]):
        key = old["id"].replace(".", "-")
        panels = []
        for mode in [v[0] for v in variants if v[0] != "before"]:
            new = captures[mode][i]
            if old["id"] != new["id"]: raise RuntimeError("Before/after fixture ordering differs.")
            print(f"Media {old['id']} {mode}", flush=True)
            poster = media(cache / "before", cache / mode, old, new, output, key, mode, cache, args.reuse)
            if mode == "full": posters.append(poster)
            panels.append(f'<details {"open" if mode == "full" else ""}><summary>{mode.title()}: {old["seconds"]:.2f}s → {new["seconds"]:.2f}s</summary>'
                          f'<video controls loop muted preload="none" poster="{key}-{mode}.png" src="{key}-{mode}.webm"></video></details>')
        cards.append(f'<article><h2>{html.escape(old["name"])} · Level {old["level"]}</h2><p>{html.escape(old["description"])}</p>{"".join(panels)}</article>')
    sheet = Image.new("RGB", (1920, 565 * len(posters)), "#101222")
    for i, poster in enumerate(posters): sheet.paste(poster.resize((1920, 565), Image.Resampling.NEAREST), (0, i * 565))
    sheet.save(output / "contact-sheet.png", optimize=True)
    inventory_html = ""
    inventories = {}
    for name in ["before", "full"]:
        source = cache / name / "frames/inventory.json"
        if source.exists():
            inventories[name] = json.loads(source.read_text())
            tree = before if name == 'before' else ROOT
            procs = json.loads((tree / 'content/tactics/base/fx/manifest.json').read_text()).get('passives', {})
            for row in inventories[name]:
                if row['kind'] == 'proc' and row['id'] in procs: row['presentation'] = {'fx': procs[row['id']]}
    if inventories:
        (output / "inventory.json").write_text(json.dumps(inventories, indent=2) + "\n")
        old_rows = {(r["id"], r.get("subclass")): r for r in inventories.get("before", [])}
        def keys(row):
            return " · ".join(str(v) for k, v in (row.get("presentation") or {}).items() if k != "cutIn" and v) or "No art key"
        rows = []
        for row in inventories.get("full", []):
            old = old_rows.get((row["id"], row.get("subclass")), {})
            cells = [row["name"], row.get("subclass") or "Base class", str(row["level"]), row["kind"], row["cutIn"], keys(old), keys(row)]
            rows.append("<tr>" + "".join(f"<td>{html.escape(c)}</td>" for c in cells) + "</tr>")
        inventory_html = '<h2>Complete feature inventory</h2><p>Static modifiers have no standalone proc. Every ability and visible proc must have a real fixture for this capture to succeed.</p><table><tr><th>Name</th><th>Subclass</th><th>Level</th><th>Kind</th><th>Cut-in</th><th>Before keys</th><th>After keys</th></tr>' + ''.join(rows) + '</table>'
    (output / "index.html").write_text('<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">'
        '<title>Wardenflame combat VFX preview</title><style>body{background:#101222;color:#eee6d0;font:18px system-ui;margin:2rem auto;max-width:1600px;padding:0 1rem}'
        'video,img{width:100%;image-rendering:pixelated}article{margin:3rem 0}summary{padding:.8rem;cursor:pointer}a{color:#f0d27a}p{max-width:85ch}table{border-collapse:collapse;width:100%;font-size:14px}td,th{border:1px solid #555;padding:.4rem;overflow-wrap:anywhere}</style>'
        f'<h1>Combat VFX · {html.escape(args.selector)}</h1><p>Native TV capture: before on the left, after on the right. Fixed map and seed; real engine events and BeatPlayer. '
        'Videos preserve the complete cast, impact, effect tails and camera settle. Posters align the impact for comparison.</p>'
        f'<p>Frozen before: <code>{baseline}</code>. <a href="review.md">Review notes and limitations</a> · '
        '<a href="performance.json">Frame measurements</a> · <a href="timings.json">Actual beat timings</a></p><img src="contact-sheet.png" alt="Before and after contact sheet">'
        + inventory_html + ''.join(cards) + '</html>')
    print(f"Preview: {output / 'index.html'}\nRaw captures: {cache}", flush=True)


if __name__ == "__main__":
    main()
