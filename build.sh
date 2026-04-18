#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

VERSION_ARGS=()
if [ -n "${BUILD_VERSION:-}" ]; then
  VERSION_ARGS=(-p:Version="$BUILD_VERSION")
fi

build() {
  dotnet build "$SOLUTION_DIR/Solution.slnx" -c Release --no-incremental "${VERSION_ARGS[@]}"
}

test() {
  rm -rf "$OUT_DIR/test-results"

  local test_plugins_dir="$OUT_DIR/test/plugins"
  rm -rf "$test_plugins_dir"
  mkdir -p "$test_plugins_dir"
  for proj in "$SOLUTION_DIR"/src/plugins/*/*/*.csproj; do
    local name
    name="$(basename "$(dirname "$proj")")"
    dotnet publish "$proj" -c Release --no-build -o "$test_plugins_dir/$name"
  done

  dotnet test "$SOLUTION_DIR/Solution.slnx" -c Release --no-build \
    --collect:"XPlat Code Coverage" \
    --settings "$SOLUTION_DIR/coverage.runsettings" \
    --logger "trx;LogFileName=results.trx" \
    --results-directory "$OUT_DIR/test-results"

  local reports
  reports=$(find "$OUT_DIR/test-results" -name "coverage.cobertura.xml" -printf "%p;" 2>/dev/null)
  if [ -n "$reports" ]; then
    dotnet tool restore
    dotnet tool run reportgenerator \
      -reports:"${reports%;}" \
      -targetdir:"$OUT_DIR/test-results/coverage" \
      -reporttypes:Cobertura\;TextSummary \
      > /dev/null

    cat "$OUT_DIR/test-results/coverage/Summary.txt"
  fi
}

if [ $# -eq 0 ]; then
  echo "Usage: $0 {build|test} [...]"
  echo ""
  echo "Environment:"
  echo "  BUILD_VERSION    Version string (default: from Directory.Build.props)"
  echo ""
  echo "For publishing and packaging, see scripts/publish/*.sh"
  exit 1
fi

declare -A requested=()
for cmd in "$@"; do
  case "$cmd" in
    build|test) requested[$cmd]=1 ;;
    *)
      echo "Error: unknown command '$cmd'"
      echo "Usage: $0 {build|test} [...]"
      exit 1
      ;;
  esac
done

for cmd in build test; do
  if [ -n "${requested[$cmd]:-}" ]; then
    case "$cmd" in
      build) build ;;
      test)  test ;;
    esac
  fi
done
