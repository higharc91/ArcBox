#!/usr/bin/env bash
# Run the repository's build and test groups locally or in CI.
set -u

usage() {
    echo "Usage: tools/check-all.sh --fast|--full" >&2
    exit 2
}

[[ $# -eq 1 ]] || usage
case "$1" in
    --fast|--full) mode=$1 ;;
    *) usage ;;
esac

cd "$(dirname "$0")/.." || exit 2

# Content text is UTF-8; Windows otherwise defaults to a locale code page.
export PYTHONUTF8=1

dotnet_cmd=${DOTNET:-dotnet}
node_cmd=${NODE:-node}
npm_cmd=${NPM:-npm}
python_cmd=${PYTHON:-python3}

groups=()
results=()
failed=0

run_group() {
    local name=$1
    shift
    groups+=("$name")
    echo
    echo "== $name =="
    if "$@"; then
        results+=(PASS)
        echo "PASS $name"
    else
        results+=(FAIL)
        failed=1
        echo "FAIL $name"
    fi
}

dotnet_build() {
    "$dotnet_cmd" build ArcBox.slnx -c Release
}

dotnet_tests() {
    if [[ $mode == --full ]]; then
        "$dotnet_cmd" test ArcBox.slnx -c Release
    else
        local status=0
        "$dotnet_cmd" test games/Tactics/Tactics.Logic.Tests/Tactics.Logic.Tests.csproj -c Release || status=1
        "$dotnet_cmd" test games/Tactics/Tactics.Game.Tests/Tactics.Game.Tests.csproj -c Release || status=1
        return "$status"
    fi
}

phone() {
    (cd web/play && "$npm_cmd" ci && "$npm_cmd" test && "$npm_cmd" run build)
}

node_tools() {
    "$node_cmd" --test tools/tactics-playtest/*.test.cjs
}

python_tools() {
    local status=0
    "$python_cmd" -m unittest discover -s tools/tactics-assets -p 'test_*.py' || status=1
    "$python_cmd" -m unittest discover -s tools/tactics-audio -p 'test_*.py' || status=1
    "$python_cmd" tools/tactics-lore/check.py || status=1
    "$python_cmd" -m unittest discover -s tools/tactics-lore -p 'test_*.py' || status=1
    "$python_cmd" tools/docs-drift.py || status=1
    return "$status"
}

if [[ $mode == --full ]]; then
    run_group '.NET build' dotnet_build
fi
run_group '.NET tests' dotnet_tests
run_group 'Phone tests and build' phone
run_group 'Node tool tests' node_tools
run_group 'Python tools and lore' python_tools

echo
echo "== Summary ($mode) =="
for i in "${!groups[@]}"; do
    printf '%-5s %s\n' "${results[$i]}" "${groups[$i]}"
done
exit "$failed"
