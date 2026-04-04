#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

build() {
  dotnet build "$SOLUTION_DIR/Solution.slnx" -c Release --no-incremental
}

test() {
  dotnet test "$SOLUTION_DIR/Solution.slnx" -c Release --no-build
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
