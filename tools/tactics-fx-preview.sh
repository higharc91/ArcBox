#!/usr/bin/env bash
# Real-engine, real-TV before/after showcase. CI is never invoked.
set -euo pipefail
exec python3 "$(dirname "$0")/tactics-fx-preview.py" "$@"
