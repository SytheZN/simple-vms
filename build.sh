#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

build() {
  dotnet build "$SOLUTION_DIR/Solution.slnx" -c Release --no-incremental
}

test() {
  rm -rf "$OUT_DIR/coverage"

  dotnet test "$SOLUTION_DIR/Solution.slnx" -c Release --no-build \
    --collect:"XPlat Code Coverage" \
    --results-directory "$OUT_DIR/coverage"

  local reports
  reports=$(find "$OUT_DIR/coverage" -name "coverage.cobertura.xml" -printf "%p;" 2>/dev/null)
  if [ -n "$reports" ]; then
    dotnet tool restore
    dotnet tool run reportgenerator \
      -reports:"${reports%;}" \
      -targetdir:"$OUT_DIR/coverage/merged" \
      -reporttypes:Cobertura\;TextSummary \
      > /dev/null

    cat "$OUT_DIR/coverage/merged/Summary.txt"
  fi
}

publish() {
  rm -rf "$OUT_DIR"
  dotnet publish "$SOLUTION_DIR/src/Server/Server.csproj" -c Release -o "$OUT_DIR/server"

  for proj in "$SOLUTION_DIR"/src/plugins/*/*/*.csproj; do
    local name
    name="$(basename "$(dirname "$proj")")"
    dotnet publish "$proj" -c Release -o "$OUT_DIR/server/plugins/$name"
  done
}

case "${1:-help}" in
  build)   build ;;
  test)    test ;;
  publish) publish ;;
  *)
    echo "Usage: $0 {build|test|publish}"
    exit 1
    ;;
esac
