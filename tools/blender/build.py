"""Builds every REDLINE model from scripts + data and exports .glb into src/ArcBox.Host/games/redline/models/.

  blender -b --factory-startup -P tools/blender/build.py -- [--only cars,drivers,...] [--preview] [--preview-dir DIR]
                                                            [--list] [key=value ...]

Asset modules are discovered, not registered: every tools/blender/<name>.py other than build.py, check.py and
files starting with "_" that defines GROUP, build(ctx) and contracts() (see lib/asset.py). Modules run in name order;
each starts from an empty scene. After a module builds, its contracts are checked; any failure exits with code 1.
key=value arguments land in ctx.args for the modules (e.g. team=mclaren for the car preview).
"""
import importlib
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from lib import asset, validate  # noqa: E402

SKIP = {"build", "check"}


def discover():
    mods = []
    for fn in sorted(os.listdir(HERE)):
        name, ext = os.path.splitext(fn)
        if ext != ".py" or name in SKIP or name.startswith("_"):
            continue
        mod = importlib.import_module(name)
        if all(hasattr(mod, a) for a in ("GROUP", "build", "contracts")):
            mods.append(mod)
    return mods


def parse(argv):
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    opts = dict(only=None, preview=False, preview_dir="", list=False, args={})
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--only":
            opts["only"] = set(argv[i + 1].split(","))
            i += 1
        elif a.startswith("--only="):
            opts["only"] = set(a.split("=", 1)[1].split(","))
        elif a == "--preview":
            opts["preview"] = True
        elif a == "--preview-dir":
            opts["preview_dir"] = argv[i + 1]
            i += 1
        elif a == "--list":
            opts["list"] = True
        elif "=" in a:
            k, v = a.split("=", 1)
            opts["args"][k] = v
        else:
            raise SystemExit(f"unknown argument {a}")
        i += 1
    return opts


def main():
    opts = parse(sys.argv)
    mods = discover()
    if opts["list"]:
        for m in mods:
            print(f"{m.GROUP:<10} {m.__name__}.py  -> {', '.join(c.path for c in m.contracts())}")
        return 0
    ctx = asset.Context(preview=opts["preview"], args=opts["args"],
                        preview_dir=opts["preview_dir"] or os.path.join(asset.REPO_ROOT, "tools", "blender", "out"))
    failures = []
    for m in mods:
        if opts["only"] and m.GROUP not in opts["only"]:
            continue
        t0 = time.time()
        print(f"[build] {m.GROUP}: {m.__name__}.py")
        m.build(ctx)
        print(f"[build] {m.__name__}.py exported in {time.time() - t0:.1f} s")
        failures += validate.check_all(m.contracts(), ctx.models_dir, log=lambda s: print("[check]" + s))
        if ctx.preview and hasattr(m, "preview"):
            os.makedirs(ctx.preview_dir, exist_ok=True)
            m.preview(ctx)
    if failures:
        print(f"[build] FAILED: {len(failures)} contract error(s)")
        return 1
    print("[build] OK")
    return 0


if __name__ == "__main__":
    code = main()
    sys.stdout.flush()
    if code:
        sys.exit(code)
