#!/usr/bin/env bash
# Imports the TV app's Godot assets (fonts, the Blender .glb models) when they need it, headless.
#
#   GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 tools/godot-import.sh [--check] [--force]
#
# Godot only imports in the editor: a game run uses the artefacts in src/ArcBox.Host/.godot/imported (not in git;
# the committed *.import files name them). This script runs `godot --headless --import` when any artefact is missing
# or older than its source (its .md5 sidecar's source_md5 differs from the file's md5), e.g. in a fresh clone or after
# tools/blender/build.py re-exported a model. Without the import the TV still works: ModelLibrary parses the .glb at
# runtime (slower) and falls back to the procedural meshes if that fails too.
#   --check   only report (exit 1 when an import is needed)
#   --force   import even when everything looks current
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
PROJ="$ROOT/src/ArcBox.Host"
GODOT=${GODOT:-$(command -v godot || true)}
CHECK=0
FORCE=0
for arg in "$@"; do
  case "$arg" in
    --check) CHECK=1 ;;
    --force) FORCE=1 ;;
    -h|--help) sed -n '/^#/!q;p' "$0" | tail -n +2; exit 0 ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

# Prints the first asset that needs importing, if any.
stale() {
  local imp src dest md5file want have
  while IFS= read -r -d '' imp; do
    src=${imp%.import}
    [[ -f "$src" ]] || continue
    dest=$(sed -n 's/^path[^=]*="res:\/\/\(.*\)"$/\1/p' "$imp" | head -n 1)
    [[ -n "$dest" ]] || continue
    if [[ ! -f "$PROJ/$dest" ]]; then echo "${src#"$PROJ"/} (not imported)"; return; fi
    md5file="$PROJ/${dest%.*}.md5"
    want=$(sed -n 's/^source_md5="\(.*\)"$/\1/p' "$md5file" 2>/dev/null || true)
    have=$(md5sum "$src" | cut -d' ' -f1)
    if [[ "$want" != "$have" ]]; then echo "${src#"$PROJ"/} (changed since its import)"; return; fi
  done < <(find "$PROJ" -name '*.import' -not -path '*/.godot/*' -print0)
}

WHY=$(stale)
if (( ! FORCE )) && [[ -z "$WHY" ]]; then
  echo "godot-import: everything is imported"
  exit 0
fi
if (( CHECK )); then
  echo "godot-import: needs an import: ${WHY:-forced}"
  exit 1
fi
if [[ -z "$GODOT" || ! -x "$GODOT" ]]; then
  echo "Set GODOT to the Godot 4.7.2 .NET binary (or put godot on PATH)." >&2
  exit 2
fi
echo "godot-import: importing (${WHY:-forced})"
timeout -k 5 600 "$GODOT" --headless --import --path "$PROJ" > /dev/null 2>&1 || true
WHY=$(stale)
if [[ -n "$WHY" ]]; then
  echo "godot-import: still not imported: $WHY" >&2
  exit 1
fi
echo "godot-import: done"
