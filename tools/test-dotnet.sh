#!/usr/bin/env bash
# Daily functional checks; explicit full and isolated wall-clock lanes retain every assertion.
set -euo pipefail
cd "$(dirname "$0")/.."
mode=${1:---fast}
[[ $# -le 1 && $mode =~ ^--(fast|full|perf)$ ]] || { echo 'Usage: tools/test-dotnet.sh [--fast|--full|--perf]' >&2; exit 2; }
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
runner=${DOTNET:-dotnet}
if [[ $mode == --fast ]]; then
    # Read the actual solution so new .Tests projects are included automatically.
    project_list=$("$runner" sln ArcBox.slnx list)
    projects=()
    while IFS= read -r project; do
        project=${project%$'\r'}
        [[ $project != *.Tests.csproj ]] || projects+=("${project//\\//}")
    done <<< "$project_list"
    [[ ${#projects[@]} -gt 0 ]] || { echo 'No test projects found in ArcBox.slnx' >&2; exit 1; }
    # Build every test dependency, without rebuilding unrelated native host/editor and tool executables.
    temporary=$(mktemp -d "$PWD/.arcbox-tests-XXXXXX")
    trap 'rm -rf "$temporary"' EXIT
    {
        printf '{"solution":{"path":"../ArcBox.slnx","projects":['
        separator=''
        for project in "${projects[@]}"; do
            escaped=${project//\"/\\\"}
            printf '%s"%s"' "$separator" "$escaped"
            separator=,
        done
        echo ']}}'
    } > "$temporary/Tests.slnf"
    "$runner" build "$temporary/Tests.slnf" -c Release -m:1 -nr:false
    echo 'Functional lane: Perf, Sim and exhaustive Integration checks run in --full.'
    "$runner" test ArcBox.slnx -c Release --no-build --max-parallel-test-modules 10 --max-threads 1 \
        --filter-not-trait 'Category=Perf' --filter-not-trait 'Category=Sim' --filter-not-trait 'Category=Integration'
    exit "$?"
fi
"$runner" build ArcBox.slnx -c Release -m:1 -nr:false
if [[ $mode == --full ]]; then
    "$runner" test ArcBox.slnx -c Release --no-build
else
    # Isolate both assemblies and collections so unrelated load cannot distort budgets.
    for project in games/Tactics/Tactics.Logic.Tests games/Tactics/Tactics.Presentation.Tests src/ArcBox.Host.Core.Tests src/ArcBox.Server.Tests; do
        "$runner" test --project "$project" -c Release --no-build --max-threads 1 --filter-trait 'Category=Perf' --show-live-output on --output Detailed
    done
fi
