#!/usr/bin/env bash
# Run build.sh inside a Fedora container that mirrors the CI image.
# Usage: ./build-ct.sh <build-sh-args>...
# Example: ./build-ct.sh build test
set -euo pipefail

SOLUTION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Bump tag when the inline Dockerfile below changes so contributors pick up the new base.
IMAGE="simple-vms-ci:fedora41-dotnet10"

if [ "$#" -eq 0 ]; then
  echo "Usage: $(basename "$0") <build-sh-args>..." >&2
  echo "Runs ./build.sh inside a Fedora container mirroring CI." >&2
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "docker not found on PATH (Rancher Desktop / Docker Desktop / colima required)" >&2
  exit 1
fi

if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
  echo "==> Building $IMAGE (one-time, cached for future runs)"
  docker build -t "$IMAGE" - <<'DOCKERFILE'
FROM fedora:41
RUN dnf install -y dotnet-sdk-10.0 nodejs git && dnf clean all
DOCKERFILE
fi

# Named Docker volumes below persist package caches across runs.
# To reset: docker volume rm simple-vms-nuget simple-vms-npm simple-vms-node-modules simple-vms-dotnet

TTY_FLAG=()
if [ -t 0 ] && [ -t 1 ]; then
  TTY_FLAG=(-it)
fi

NUGET_VOLUME="simple-vms-nuget"
NPM_VOLUME="simple-vms-npm"
NODE_MODULES_VOLUME="simple-vms-node-modules"
DOTNET_VOLUME="simple-vms-dotnet"

exec docker run --rm "${TTY_FLAG[@]}" \
  -v "$SOLUTION_DIR":/w \
  -v "$NUGET_VOLUME":/root/.nuget/packages \
  -v "$NPM_VOLUME":/root/.npm \
  -v "$NODE_MODULES_VOLUME":/w/src/Client.Web/node_modules \
  -v "$DOTNET_VOLUME":/root/.dotnet \
  -w /w \
  -e BUILD_VERSION="${BUILD_VERSION:-}" \
  -e BUILD_HASH="${BUILD_HASH:-}" \
  -e BUILD_RID="${BUILD_RID:-}" \
  "$IMAGE" \
  bash -c 'git config --global --add safe.directory /w && exec ./build.sh "$@"' _ "$@"
