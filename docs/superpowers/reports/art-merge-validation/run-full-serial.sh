#!/usr/bin/env bash
# Same complete gate and timing assertions, one MTP test module at a time.
set -euo pipefail
arcbox_validation_root=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
if [[ -f /workspace/.arcbox/activate.sh ]]; then
  source /workspace/.arcbox/activate.sh
fi
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export ARCBOX_VALIDATION_DOTNET
ARCBOX_VALIDATION_DOTNET=$(command -v dotnet)
arcbox_validation_tmp=$(mktemp -d)
trap 'rm -rf "$arcbox_validation_tmp"' EXIT
cat > "$arcbox_validation_tmp/dotnet-serial" <<'WRAPPER'
#!/usr/bin/env bash
set -e
if [[ ${1:-} == test ]]; then
  shift
  exec "$ARCBOX_VALIDATION_DOTNET" test "$@" --max-parallel-test-modules 1
fi
exec "$ARCBOX_VALIDATION_DOTNET" "$@"
WRAPPER
chmod +x "$arcbox_validation_tmp/dotnet-serial"
DOTNET="$arcbox_validation_tmp/dotnet-serial" bash "$arcbox_validation_root/tools/check-all.sh" --full
