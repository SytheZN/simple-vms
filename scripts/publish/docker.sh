#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$SOLUTION_DIR/out"
BUILD_HASH="${BUILD_HASH:-local}"

if [ ! -d "$OUT_DIR/server" ]; then
  echo "Error: $OUT_DIR/server missing — run ./scripts/publish/server.sh first"
  exit 1
fi

version="${BUILD_VERSION:-0.0.0}-${BUILD_HASH}"
tag="simple-vms:${version}"
cp "$SOLUTION_DIR/src/docker/Dockerfile" "$OUT_DIR/Dockerfile"
docker build -t "$tag" "$OUT_DIR"
