#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SOLUTION_DIR/out"
SOLUTION_FILE="${BUILD_SOLUTION_FILE:-Solution.slnx}"

VERSION_ARGS=()
if [ -n "${BUILD_VERSION:-}" ]; then
  VERSION_ARGS=(-p:Version="$BUILD_VERSION")
fi

COMMON_ARGS=()
TEST_CONSOLE_LOGGER=()
QUIET=0
if [ "${BUILD_VERBOSITY:-}" = "quiet" ]; then
  COMMON_ARGS+=(-v quiet --tl:on)
  TEST_CONSOLE_LOGGER=(--logger "console;verbosity=quiet")
  QUIET=1
else
  COMMON_ARGS+=(--tl:auto)
fi

build() {
  dotnet build "$SOLUTION_DIR/$SOLUTION_FILE" -c Release --no-incremental \
    "${COMMON_ARGS[@]}" "${VERSION_ARGS[@]}"
  [ "$QUIET" = 1 ] && echo "Build Succeeded"
  return 0
}

test() {
  rm -rf "$OUT_DIR/test-results"

  local test_plugins_dir="$OUT_DIR/test/plugins"
  rm -rf "$test_plugins_dir"
  mkdir -p "$test_plugins_dir"
  for proj in "$SOLUTION_DIR"/src/plugins/*/*/*.csproj; do
    local name
    name="$(basename "$(dirname "$proj")")"
    dotnet publish "$proj" -c Release --no-build \
      "${COMMON_ARGS[@]}" -o "$test_plugins_dir/$name"
  done

  dotnet test "$SOLUTION_DIR/$SOLUTION_FILE" -c Release --no-build \
    "${COMMON_ARGS[@]}" "${TEST_CONSOLE_LOGGER[@]}" \
    --collect:"XPlat Code Coverage" \
    --settings "$SOLUTION_DIR/coverage.runsettings" \
    --logger "trx;LogFileName=results.trx" \
    --results-directory "$OUT_DIR/test-results"

  local reports
  reports=$(find "$OUT_DIR/test-results" -name "coverage.cobertura.xml" -printf "%p;" 2>/dev/null)
  if [ -n "$reports" ]; then
    dotnet tool restore > /dev/null
    dotnet tool run reportgenerator \
      -reports:"${reports%;}" \
      -targetdir:"$OUT_DIR/test-results/coverage" \
      -reporttypes:Cobertura\;TextSummary \
      > /dev/null

    [ "$QUIET" = 1 ] && echo "  $OUT_DIR/test-results/coverage/Summary.txt"
    [ "$QUIET" = 1 ] || cat "$OUT_DIR/test-results/coverage/Summary.txt"
  fi

  [ "$QUIET" = 1 ] && echo "Test Succeeded"
  return 0
}

if [ $# -eq 0 ]; then
  echo "Usage: $0 {build|test} [...]"
  echo ""
  echo "Environment:"
  echo "  BUILD_VERSION        Version string (default: from Directory.Build.props)"
  echo "  BUILD_VERBOSITY      'quiet' suppresses output except warnings and errors"
  echo "  BUILD_SOLUTION_FILE  Solution file to build (default: Solution.slnx)"
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
