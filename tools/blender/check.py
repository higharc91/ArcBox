"""Validates every committed .glb against its asset module's contracts (budgets, node and material names,
dimensions, file size) and prints triangle counts and sizes.

  blender -b --factory-startup -P tools/blender/check.py -- [--only cars,...]

Exit code 1 when anything fails. build.py runs the same checks right after exporting.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import build  # noqa: E402
from lib import asset, glb, validate  # noqa: E402


def main():
    opts = build.parse(sys.argv)
    failures = []
    total = 0
    for m in build.discover():
        if opts["only"] and m.GROUP not in opts["only"]:
            continue
        print(f"[check] {m.GROUP}: {m.__name__}.py")
        failures += validate.check_all(m.contracts(), asset.MODELS_DIR, log=lambda s: print("[check]" + s))
    for dirpath, _, files in os.walk(asset.MODELS_DIR):
        total += sum(os.path.getsize(os.path.join(dirpath, f)) for f in files if f.endswith(".glb"))
    print(f"[check] all .glb: {total / 1024 / 1024:.2f} MiB (budget 40 MiB)")
    if total > 40 * 1024 * 1024:
        failures.append("models over 40 MiB")
    print("[check] OK" if not failures else f"[check] FAILED: {len(failures)} error(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    code = main()
    sys.stdout.flush()
    if code:
        sys.exit(code)
