#!/usr/bin/env bash
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="$SOLUTION_DIR/out"

VERSION_ARGS=()
if [ -n "${BUILD_VERSION:-}" ]; then
  VERSION_ARGS=(-p:Version="$BUILD_VERSION")
fi

RID_ARGS=()
if [ -n "${BUILD_RID:-}" ]; then
  RID_ARGS=(--self-contained -r "$BUILD_RID")
fi

rm -rf "$OUT_DIR/server"
dotnet publish "$SOLUTION_DIR/src/Server/Server.csproj" -c Release -o "$OUT_DIR/server" "${RID_ARGS[@]}" "${VERSION_ARGS[@]}"

for proj in "$SOLUTION_DIR"/src/plugins/*/*/*.csproj; do
  name="$(basename "$(dirname "$proj")")"
  dotnet publish "$proj" -c Release -o "$OUT_DIR/server/plugins/$name" "${RID_ARGS[@]}" "${VERSION_ARGS[@]}"
done

rid="${BUILD_RID:-local}"
echo "Creating tarball"
tar czf "$OUT_DIR/simple-vms-${rid}.tar.gz" -C "$OUT_DIR" server
